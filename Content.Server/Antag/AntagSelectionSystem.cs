using System.Linq;
using Content.Server.Antag.Components;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.Preferences.Managers;
using Content.Server.Roles.Jobs;
using Content.Server.Shuttles.Components;
using Content.Shared.Antag;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Players;
using Content.Shared.Whitelist;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Antag;

public sealed class AntagSelectionSystem : GameRuleSystem<GameRuleComponent>
{
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    [Dependency] private readonly JobSystem _jobs = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly RoleSystem _role = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    #region Eligible Player Selection
    /// <summary>
    /// Get all players that are eligible for an antag role
    /// </summary>
    /// <param name="playerSessions">All sessions from which to select eligible players</param>
    /// <param name="antagPrototype">The prototype to get eligible players for</param>
    /// <param name="includeAllJobs">Should jobs that prohibit antag roles (ie Heads, Sec, Interns) be included</param>
    /// <param name="acceptableAntags">Should players already selected as antags be eligible</param>
    /// <param name="ignorePreferences">Should we ignore if the player has enabled this specific role</param>
    /// <param name="customExcludeCondition">A custom condition that each player is tested against, if it returns true the player is excluded from eligibility</param>
    /// <returns>List of all player entities that match the requirements</returns>
    public List<EntityUid> GetEligiblePlayers(IEnumerable<ICommonSession> playerSessions,
        ProtoId<AntagPrototype> antagPrototype,
        bool includeAllJobs = false,
        AntagAcceptability acceptableAntags = AntagAcceptability.NotExclusive,
        bool ignorePreferences = false,
        bool allowNonHumanoids = false,
        Func<EntityUid?, bool>? customExcludeCondition = null)
    {
        var eligiblePlayers = new List<EntityUid>();

        SubscribeLocalEvent<GhostRoleAntagSpawnerComponent, TakeGhostRoleEvent>(OnTakeGhostRole);

        SubscribeLocalEvent<AntagSelectionComponent, ObjectivesTextGetInfoEvent>(OnObjectivesTextGetInfo);

        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnPlayerSpawning);
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(OnJobsAssigned);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    private void OnTakeGhostRole(Entity<GhostRoleAntagSpawnerComponent> ent, ref TakeGhostRoleEvent args)
    {
        if (args.TookRole)
            return;

        if (ent.Comp.Rule is not { } rule || ent.Comp.Definition is not { } def)
            return;

        if (!Exists(rule) || !TryComp<AntagSelectionComponent>(rule, out var select))
            return;

        MakeAntag((rule, select), args.Player, def, ignoreSpawner: true);
        args.TookRole = true;
        _ghostRole.UnregisterGhostRole((ent, Comp<GhostRoleComponent>(ent)));
    }

    private void OnPlayerSpawning(RulePlayerSpawningEvent args)
    {
        var pool = args.PlayerPool;

        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var comp, out _))
        {
            if (comp.SelectionTime != AntagSelectionTime.PrePlayerSpawn)
                continue;

            if (comp.SelectionsComplete)
                continue;

            ChooseAntags((uid, comp), pool);

            foreach (var session in comp.SelectedSessions)
            {
                args.PlayerPool.Remove(session);
                GameTicker.PlayerJoinGame(session);
            }
        }

    private void OnJobsAssigned(RulePlayerJobsAssignedEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var comp, out _))
        {
            if (comp.SelectionTime != AntagSelectionTime.PostPlayerSpawn)
                continue;

            ChooseAntags((uid, comp), args.Players);
        }
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!args.LateJoin)
            return;

        // TODO: this really doesn't handle multiple latejoin definitions well
        // eventually this should probably store the players per definition with some kind of unique identifier.
        // something to figure out later.

        var query = QueryActiveRules();
        var rules = new List<(EntityUid, AntagSelectionComponent)>();
        while (query.MoveNext(out var uid, out _, out var antag, out _))
        {
            rules.Add((uid, antag));
        }
        RobustRandom.Shuffle(rules);

        foreach (var (uid, antag) in rules)
        {
            if (!RobustRandom.Prob(LateJoinRandomChance))
                continue;

            if (!antag.Definitions.Any(p => p.LateJoinAdditional))
                continue;

            DebugTools.AssertEqual(antag.SelectionTime, AntagSelectionTime.PostPlayerSpawn);

            if (!TryGetNextAvailableDefinition((uid, antag), out var def))
                continue;

            if (TryMakeAntag((uid, antag), args.Player, def.Value))
                break;
        }
    }

    protected override void Added(EntityUid uid, AntagSelectionComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        for (var i = 0; i < component.Definitions.Count; i++)
        {
            var def = component.Definitions[i];

            if (def.MinRange != null)
            {
                def.Min = def.MinRange.Value.Next(RobustRandom);
            }

            if (def.MaxRange != null)
            {
                def.Max = def.MaxRange.Value.Next(RobustRandom);
            }
        }
    }

    protected override void Started(EntityUid uid, AntagSelectionComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // If the round has not yet started, we defer antag selection until roundstart
        if (GameTicker.RunLevel != GameRunLevel.InRound)
            return;

        if (component.SelectionsComplete)
            return;

        var players = _playerManager.Sessions
            .Where(x => GameTicker.PlayerGameStatuses[x.UserId] == PlayerGameStatus.JoinedGame)
            .ToList();

        ChooseAntags((uid, component), players, midround: true);
    }

    /// <summary>
    /// Chooses antagonists from the given selection of players
    /// </summary>
    public void ChooseAntags(Entity<AntagSelectionComponent> ent, IList<ICommonSession> pool, bool midround = false)
    {
        if (ent.Comp.SelectionsComplete)
            return;

        foreach (var def in ent.Comp.Definitions)
        {
            ChooseAntags(ent, pool, def, midround: midround);
        }

        ent.Comp.SelectionsComplete = true;
    }

    /// <summary>
    /// Test eligibility of the player for a specific antag role
    /// </summary>
    /// <param name="midround">Disable picking players for pre-spawn antags in the middle of a round</param>
    public void ChooseAntags(Entity<AntagSelectionComponent> ent, IList<ICommonSession> pool, AntagSelectionDefinition def, bool midround = false)
    {
        var playerPool = GetPlayerPool(ent, pool, def);
        var count = GetTargetAntagCount(ent, GetTotalPlayerCount(pool), def);

        // if there is both a spawner and players getting picked, let it fall back to a spawner.
        var noSpawner = def.SpawnerPrototype == null;
        var picking = def.PickPlayer;
        if (midround && ent.Comp.SelectionTime == AntagSelectionTime.PrePlayerSpawn)
        {
            // prevent antag selection from happening if the round is on-going, requiring a spawner if used midround.
            // this is so rules like nukies, if added by an admin midround, dont make random living people nukies
            Log.Info($"Antags for rule {ent:?} get picked pre-spawn so only spawners will be made.");
            DebugTools.Assert(def.SpawnerPrototype != null, $"Rule {ent:?} had no spawner for pre-spawn rule added mid-round!");
            picking = false;
        }

        for (var i = 0; i < count; i++)
        {
            var session = (ICommonSession?) null;
            if (picking)
            {
                if (!playerPool.TryPickAndTake(RobustRandom, out session) && noSpawner)
                {
                    Log.Warning($"Couldn't pick a player for {ToPrettyString(ent):rule}, no longer choosing antags for this definition");
                    break;
                }

                if (session != null && ent.Comp.SelectedSessions.Contains(session))
                {
                    Log.Warning($"Somehow picked {session} for an antag when this rule already selected them previously");
                    continue;
                }
            }

            //If we have reached the desired number of players, skip
            if (chosenPlayers.Count >= count)
                continue;

            //Pick and choose a random number of players from this list
            chosenPlayers.AddRange(ChooseAntags(count - chosenPlayers.Count, playerList));
        }
        return chosenPlayers;
    }
    /// <summary>
    /// Helper method to choose sessions from a list
    /// </summary>
    /// <param name="eligiblePlayers">List of eligible sessions</param>
    /// <param name="count">How many to choose</param>
    /// <returns>Up to the specified count of elements from the provided list</returns>
    public List<ICommonSession> ChooseAntags(int count, List<ICommonSession> eligiblePlayers)
    {
        var chosenPlayers = new List<ICommonSession>();

        for (int i = 0; i < count; i++)
        {
            if (eligiblePlayers.Count == 0)
                break;

            chosenPlayers.Add(RobustRandom.PickAndTake(eligiblePlayers));
        }

        return chosenPlayers;
    }
    #endregion

    #region Briefings
    /// <summary>
    /// Helper method to send the briefing text and sound to a list of entities
    /// </summary>
    /// <param name="entities">The players chosen to be antags</param>
    /// <param name="briefing">The briefing text to send</param>
    /// <param name="briefingColor">The color the briefing should be, null for default</param>
    /// <param name="briefingSound">The sound to briefing/greeting sound to play</param>
    public void SendBriefing(List<EntityUid> entities, string briefing, Color? briefingColor, SoundSpecifier? briefingSound)
    {
        foreach (var entity in entities)
        {
            SendBriefing(entity, briefing, briefingColor, briefingSound);
        }
    }

    /// <summary>
    /// Helper method to send the briefing text and sound to a player entity
    /// </summary>
    public bool TryMakeAntag(Entity<AntagSelectionComponent> ent, ICommonSession? session, AntagSelectionDefinition def, bool ignoreSpawner = false, bool checkPref = true)
    {
        if (checkPref && !HasPrimaryAntagPreference(session, def))
            return false;

        if (!IsSessionValid(ent, session, def) || !IsEntityValid(session?.AttachedEntity, def))
            return false;

        MakeAntag(ent, session, def, ignoreSpawner);
        return true;
    }

    /// <summary>
    /// Makes a given player into the specified antagonist.
    /// </summary>
    public void MakeAntag(Entity<AntagSelectionComponent> ent, ICommonSession? session, AntagSelectionDefinition def, bool ignoreSpawner = false)
    {
        EntityUid? antagEnt = null;
        var isSpawner = false;

        if (session != null)
        {
            ent.Comp.SelectedSessions.Add(session);

            // we shouldn't be blocking the entity if they're just a ghost or smth.
            if (!HasComp<GhostComponent>(session.AttachedEntity))
                antagEnt = session.AttachedEntity;
        }
        else if (!ignoreSpawner && def.SpawnerPrototype != null) // don't add spawners if we have a player, dummy.
        {
            antagEnt = Spawn(def.SpawnerPrototype);
            isSpawner = true;
        }

        if (!antagEnt.HasValue)
        {
            var getEntEv = new AntagSelectEntityEvent(session, ent);
            RaiseLocalEvent(ent, ref getEntEv, true);
            antagEnt = getEntEv.Entity;
        }

        if (antagEnt is not { } player)
        {
            Log.Error($"Attempted to make {session} antagonist in gamerule {ToPrettyString(ent)} but there was no valid entity for player.");
            if (session != null)
                ent.Comp.SelectedSessions.Remove(session);
            return;
        }

        var getPosEv = new AntagSelectLocationEvent(session, ent);
        RaiseLocalEvent(ent, ref getPosEv, true);
        if (getPosEv.Handled)
        {
            var playerXform = Transform(player);
            var pos = RobustRandom.Pick(getPosEv.Coordinates);
            _transform.SetMapCoordinates((player, playerXform), pos);
        }

        // If we want to just do a ghost role spawner, set up data here and then return early.
        // This could probably be an event in the future if we want to be more refined about it.
        if (isSpawner)
        {
            if (!TryComp<GhostRoleAntagSpawnerComponent>(player, out var spawnerComp))
            {
                Log.Error($"Antag spawner {player} does not have a GhostRoleAntagSpawnerComponent.");
                if (session != null)
                    ent.Comp.SelectedSessions.Remove(session);
                return;
            }

            spawnerComp.Rule = ent;
            spawnerComp.Definition = def;
            return;

        // The following is where we apply components, equipment, and other changes to our antagonist entity.
        EntityManager.AddComponents(player, def.Components);
        _stationSpawning.EquipStartingGear(player, def.StartingGear);

        if (session != null)
        {
            var curMind = session.GetMind();
            if (curMind == null)
            {
                curMind = _mind.CreateMind(session.UserId, Name(antagEnt.Value));
                _mind.SetUserId(curMind.Value, session.UserId);
            }

            _mind.TransferTo(curMind.Value, antagEnt, ghostCheckOverride: true);
            _role.MindAddRoles(curMind.Value, def.MindComponents, null, true);
            ent.Comp.SelectedMinds.Add((curMind.Value, Name(player)));
            SendBriefing(session, def.Briefing);
        }

        var afterEv = new AfterAntagEntitySelectedEvent(session, player, ent, def);
        RaiseLocalEvent(ent, ref afterEv, true);
    }

    /// <summary>
    /// Helper method to send the briefing text and sound to a list of sessions
    /// </summary>
    public AntagSelectionPlayerPool GetPlayerPool(Entity<AntagSelectionComponent> ent, IList<ICommonSession> sessions, AntagSelectionDefinition def)
    {
        var preferredList = new List<ICommonSession>();
        var fallbackList = new List<ICommonSession>();
        foreach (var session in sessions)
        {
            if (!IsSessionValid(ent, session, def) || !IsEntityValid(session.AttachedEntity, def))
                continue;

            if (HasPrimaryAntagPreference(session, def))
            {
                preferredList.Add(session);
            }
            else if (HasFallbackAntagPreference(session, def))
            {
                fallbackList.Add(session);
            }
        }

        return new AntagSelectionPlayerPool(new() { preferredList, fallbackList });
    }
    /// <summary>
    /// Helper method to send the briefing text and sound to a session
    /// </summary>
    /// <param name="session">The player chosen to be an antag</param>
    /// <param name="briefing">The briefing text to send</param>
    /// <param name="briefingColor">The color the briefing should be, null for default</param>
    /// <param name="briefingSound">The sound to briefing/greeting sound to play</param>

    public void SendBriefing(ICommonSession session, string briefing, Color? briefingColor, SoundSpecifier? briefingSound)
    {
        // TODO ROLE TIMERS
        // Check if antag role requirements are met

        if (session == null)
            return true;

        if (session.Status is SessionStatus.Disconnected or SessionStatus.Zombie)
            return false;

        if (ent.Comp.SelectedSessions.Contains(session))
            return false;

        mind ??= session.GetMind();

        // If the player has not spawned in as any entity (e.g., in the lobby), they can be given an antag role/entity.
        if (mind == null)
            return true;

        //todo: we need some way to check that we're not getting the same role twice. (double picking thieves or zombies through midrounds)

        switch (def.MultiAntagSetting)
        {
            case AntagAcceptability.None:
            {
                if (_role.MindIsAntagonist(mind))
                    return false;
                break;
            }
            case AntagAcceptability.NotExclusive:
            {
                if (_role.MindIsExclusiveAntagonist(mind))
                    return false;
                break;
            }
        }

        // todo: expand this to allow for more fine antag-selection logic for game rules.
        if (!_jobs.CanBeAntag(session))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if a given entity (mind/session not included) is valid for a given antagonist.
    /// </summary>
    public bool IsEntityValid(EntityUid? entity, AntagSelectionDefinition def)
    {
        // If the player has not spawned in as any entity (e.g., in the lobby), they can be given an antag role/entity.
        if (entity == null)
            return true;

        if (HasComp<PendingClockInComponent>(entity))
            return false;

        if (!def.AllowNonHumans && !HasComp<HumanoidAppearanceComponent>(entity))
            return false;

        if (def.Whitelist != null)
        {
            if (!_whitelist.IsValid(def.Whitelist, entity.Value))
                return false;
        }

        if (def.Blacklist != null)
        {
            if (_whitelist.IsValid(def.Blacklist, entity.Value))
                return false;
        }

        return true;
    }

    private void OnObjectivesTextGetInfo(Entity<AntagSelectionComponent> ent, ref ObjectivesTextGetInfoEvent args)
    {
        if (ent.Comp.AgentName is not {} name)
            return;

        args.Minds = ent.Comp.SelectedMinds;
        args.AgentName = Loc.GetString(name);
    }
}
