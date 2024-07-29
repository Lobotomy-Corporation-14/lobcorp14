using Content.Server.Antag;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.PDA.Ringer;
using Content.Server.Roles;
using Content.Server.Traitor.Uplink;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Objectives.Components;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Linq;
using System.Text;

namespace Content.Server.GameTicking.Rules;

public sealed class TraitorRuleSystem : GameRuleSystem<TraitorRuleComponent>
{
    [Dependency] private readonly AntagSelectionSystem _antagSelection = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly UplinkSystem _uplink = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly SharedRoleSystem _roleSystem = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartAttemptEvent>(OnStartAttempt);
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(OnPlayersSpawned);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(HandleLatejoin);

        SubscribeLocalEvent<TraitorRuleComponent, ObjectivesTextPrependEvent>(OnObjectivesTextPrepend);
    }

    //Set min players on game rule
    protected override void Added(EntityUid uid, TraitorRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        gameRule.MinPlayers = _cfg.GetCVar(CCVars.TraitorMinPlayers);
    }

    protected override void Started(EntityUid uid, TraitorRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        MakeCodewords(component);
    }

    protected override void ActiveTick(EntityUid uid, TraitorRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.SelectionStatus < TraitorRuleComponent.SelectionState.Started && component.AnnounceAt < _timing.CurTime)
        {
            DoTraitorStart(component);
            component.SelectionStatus = TraitorRuleComponent.SelectionState.Started;
        }
    }

    /// <summary>
    /// Check for enough players
    /// </summary>
    /// <param name="ev"></param>
    private void OnStartAttempt(RoundStartAttemptEvent ev)
    {
        TryRoundStartAttempt(ev, Loc.GetString("traitor-title"));
    }

    private void MakeCodewords(TraitorRuleComponent component)
    {
        var codewordCount = _cfg.GetCVar(CCVars.TraitorCodewordCount);
        var adjectives = _prototypeManager.Index<DatasetPrototype>(component.CodewordAdjectives).Values;
        var verbs = _prototypeManager.Index<DatasetPrototype>(component.CodewordVerbs).Values;
        var codewordPool = adjectives.Concat(verbs).ToList();
        var finalCodewordCount = Math.Min(codewordCount, codewordPool.Count);
        component.Codewords = new string[finalCodewordCount];
        for (var i = 0; i < finalCodewordCount; i++)
        {
            component.Codewords[i] = _random.PickAndTake(codewordPool);
        }
    }

    public bool MakeTraitor(EntityUid traitor, TraitorRuleComponent component, bool giveUplink = true)
    {
        //Grab the mind if it wasnt provided
        if (!_mindSystem.TryGetMind(traitor, out var mindId, out var mind))
            return false;

        if (HasComp<TraitorRoleComponent>(mindId))
        {
            Log.Error($"Player {mind.CharacterName} is already a traitor.");
            return false;
        }

        var briefing = Loc.GetString("traitor-role-codewords-short", ("codewords", string.Join(", ", component.Codewords)));
        var issuer = _random.Pick(_prototypeManager.Index(component.ObjectiveIssuers).Values);

        Note[]? code = null;
        if (giveUplink)
        {
            // Calculate the amount of currency on the uplink.
            var startingBalance = _cfg.GetCVar(CCVars.TraitorStartingBalance);
            if (_jobs.MindTryGetJob(mindId, out _, out var prototype))
                startingBalance = Math.Max(startingBalance - prototype.AntagAdvantage, 0);

            // creadth: we need to create uplink for the antag.
            // PDA should be in place already
            var pda = _uplink.FindUplinkTarget(traitor);
            if (pda == null || !_uplink.AddUplink(traitor, startingBalance))
                return false;

            // Give traitors their codewords and uplink code to keep in their character info menu
            code = EnsureComp<RingerUplinkComponent>(pda.Value).Code;

            // If giveUplink is false the uplink code part is omitted
            briefing = string.Format("{0}\n{1}", briefing,
                Loc.GetString("traitor-role-uplink-code-short", ("code", string.Join("-", code).Replace("sharp", "#"))));
        }

        _antag.SendBriefing(traitor, GenerateBriefing(component.Codewords, code, issuer), null, component.GreetSoundNotification);

        component.TraitorMinds.Add(mindId);

        // Assign traitor roles
        _roleSystem.MindAddRole(mindId, new TraitorRoleComponent
        {
            PrototypeId = component.TraitorPrototypeId
        }, mind, true);
        // Assign briefing
        _roleSystem.MindAddRole(mindId, new RoleBriefingComponent
        {
            Briefing = briefing.ToString()
        }, mind, true);

        // Change the faction
        _npcFaction.RemoveFaction(traitor, component.NanoTrasenFaction, false);
        _npcFaction.AddFaction(traitor, component.SyndicateFaction);

        return true;
    }

    // TODO: AntagCodewordsComponent
    private void OnObjectivesTextPrepend(EntityUid uid, TraitorRuleComponent comp, ref ObjectivesTextPrependEvent args)
    {
        args.Text += "\n" + Loc.GetString("traitor-round-end-codewords", ("codewords", string.Join(", ", comp.Codewords)));
    }

    // TODO: figure out how to handle this? add priority to briefing event?
    private string GenerateBriefing(string[] codewords, Note[]? uplinkCode, string? objectiveIssuer = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("traitor-role-greeting", ("corporation", objectiveIssuer ?? Loc.GetString("objective-issuer-unknown"))));
        sb.AppendLine(Loc.GetString("traitor-role-codewords-short", ("codewords", string.Join(", ", codewords))));
        if (uplinkCode != null)
            sb.AppendLine(Loc.GetString("traitor-role-uplink-code-short", ("code", string.Join("-", uplinkCode).Replace("sharp", "#"))));

        return sb.ToString();
    }

    public List<(EntityUid Id, MindComponent Mind)> GetOtherTraitorMindsAliveAndConnected(MindComponent ourMind)
    {
        List<(EntityUid Id, MindComponent Mind)> allTraitors = new();
        foreach (var traitor in EntityQuery<TraitorRuleComponent>())
        {
            foreach (var role in GetOtherTraitorMindsAliveAndConnected(ourMind, traitor))
            {
                if (!allTraitors.Contains(role))
                    allTraitors.Add(role);
            }
        }

        return allTraitors;
    }

    private List<(EntityUid Id, MindComponent Mind)> GetOtherTraitorMindsAliveAndConnected(MindComponent ourMind, TraitorRuleComponent component)
    {
        var traitors = new List<(EntityUid Id, MindComponent Mind)>();
        foreach (var traitor in component.TraitorMinds)
        {
            if (TryComp(traitor, out MindComponent? mind) &&
                mind.OwnedEntity != null &&
                mind.Session != null &&
                mind != ourMind &&
                _mobStateSystem.IsAlive(mind.OwnedEntity.Value) &&
                mind.CurrentEntity == mind.OwnedEntity)
            {
                traitors.Add((traitor, mind));
            }
        }

        return traitors;
    }
}
