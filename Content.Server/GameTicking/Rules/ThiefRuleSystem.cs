using Content.Server.Antag;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.Roles;
using Content.Shared.Antag;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.GameTicking.Rules;

public sealed class ThiefRuleSystem : GameRuleSystem<ThiefRuleComponent>
{
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly AntagSelectionSystem _antag = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(OnPlayersSpawned);

        SubscribeLocalEvent<ThiefRoleComponent, GetBriefingEvent>(OnGetBriefing);
    }

    private void OnPlayersSpawned(RulePlayerJobsAssignedEvent ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var comp, out var gameRule))
        {
            //Get all players eligible for this role, allow selecting existing antags
            //TO DO: When voxes specifies are added, increase their chance of becoming a thief by 4 times >:)
            var eligiblePlayers = _antagSelection.GetEligiblePlayers(ev.Players, comp.ThiefPrototypeId, acceptableAntags: AntagAcceptability.All, allowNonHumanoids: true);

            //Abort if there are none
            if (eligiblePlayers.Count == 0)
            {
                Log.Warning($"No eligible thieves found, ending game rule {ToPrettyString(uid):rule}");
                GameTicker.EndGameRule(uid, gameRule);
                continue;
            }

            //Calculate number of thieves to choose
            var thiefCount = _random.Next(1, comp.MaxAllowThief + 1);

            //Select our theives
            var thieves = _antagSelection.ChooseAntags(thiefCount, eligiblePlayers);

            MakeThief(thieves, comp, comp.PacifistThieves);
        }
    }

    public void MakeThief(List<EntityUid> players, ThiefRuleComponent thiefRule, bool addPacified)
    {
        foreach (var thief in players)
        {
            MakeThief(thief, thiefRule, addPacified);
        }
    }

    public void MakeThief(EntityUid thief, ThiefRuleComponent thiefRule, bool addPacified)
    {
        if (!_mindSystem.TryGetMind(thief, out var mindId, out var mind))
            return;

        if (HasComp<ThiefRoleComponent>(mindId))
            return;

        // Assign thief roles
        _roleSystem.MindAddRole(mindId, new ThiefRoleComponent
        {
            PrototypeId = thiefRule.ThiefPrototypeId,
        }, silent: true);

        //Add Pacified  
        //To Do: Long-term this should just be using the antag code to add components.
        if (addPacified) //This check is important because some servers may want to disable the thief's pacifism. Do not remove.
        {
            EnsureComp<PacifiedComponent>(thief);
        }

        //Generate objectives
        _antag.SendBriefing(args.EntityUid, MakeBriefing(args.EntityUid), null, null);
    }

    //Add mind briefing
    private void OnGetBriefing(Entity<ThiefRoleComponent> thief, ref GetBriefingEvent args)
    {
        if (!TryComp<MindComponent>(thief.Owner, out var mind) || mind.OwnedEntity == null)
            return;

        args.Append(MakeBriefing(mind.OwnedEntity.Value));
    }

    private string MakeBriefing(EntityUid thief)
    {
        var isHuman = HasComp<HumanoidAppearanceComponent>(thief);
        var briefing = "\n";
        briefing = isHuman
            ? Loc.GetString("thief-role-greeting-human")
            : Loc.GetString("thief-role-greeting-animal");

        briefing += "\n \n" + Loc.GetString("thief-role-greeting-equipment") + "\n";
        return briefing;
    }
}
