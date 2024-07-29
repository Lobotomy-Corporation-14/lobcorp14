using Content.Server.Administration.Commands;
using Content.Server.Administration.Managers;
using Content.Server.Antag;
using Content.Server.Communications;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Nuke;
using Content.Server.NukeOps;
using Content.Server.Popups;
using Content.Server.Roles;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Store.Components;
using Content.Server.Store.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Nuke;
using Content.Shared.NukeOps;
using Content.Shared.Store;
using Content.Shared.Tag;
using Content.Shared.Zombies;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using System.Linq;
using Content.Shared.Store.Components;

namespace Content.Server.GameTicking.Rules;

public sealed class NukeopsRuleSystem : GameRuleSystem<NukeopsRuleComponent>
{
    [Dependency] private readonly EmergencyShuttleSystem _emergency = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly AntagSelectionSystem _antagSelection = default!;

    private ISawmill _sawmill = default!;

    [ValidatePrototypeId<CurrencyPrototype>]
    private const string TelecrystalCurrencyPrototype = "Telecrystal";

    [ValidatePrototypeId<TagPrototype>]
    private const string NukeOpsUplinkTagPrototype = "NukeOpsUplink";

    [ValidatePrototypeId<AntagPrototype>]
    public const string NukeopsId = "Nukeops";

    [ValidatePrototypeId<DatasetPrototype>]
    private const string OperationPrefixDataset = "operationPrefix";

    [ValidatePrototypeId<DatasetPrototype>]
    private const string OperationSuffixDataset = "operationSuffix";

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("NukeOps");

        SubscribeLocalEvent<RoundStartAttemptEvent>(OnStartAttempt);
        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnPlayersSpawning);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
        SubscribeLocalEvent<NukeExplodedEvent>(OnNukeExploded);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<NukeDisarmSuccessEvent>(OnNukeDisarm);

        SubscribeLocalEvent<NukeOperativeComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<NukeOperativeComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<NukeOperativeComponent, GhostRoleSpawnerUsedEvent>(OnPlayersGhostSpawning);
        SubscribeLocalEvent<NukeOperativeComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<NukeOperativeComponent, EntityZombifiedEvent>(OnOperativeZombified);

        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnShuttleFTLAttempt);
        SubscribeLocalEvent<WarDeclaredEvent>(OnWarDeclared);
        SubscribeLocalEvent<CommunicationConsoleCallShuttleAttemptEvent>(OnShuttleCallAttempt);

        SubscribeLocalEvent<NukeopsRuleComponent, AfterAntagEntitySelectedEvent>(OnAfterAntagEntSelected);
        SubscribeLocalEvent<NukeopsRuleComponent, RuleLoadedGridsEvent>(OnRuleLoadedGrids);
    }

    protected override void Started(EntityUid uid, NukeopsRuleComponent component, GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (GameTicker.RunLevel == GameRunLevel.InRound)
            SpawnOperativesForGhostRoles(uid, component);
    }

    #region Event Handlers

    private void OnStartAttempt(RoundStartAttemptEvent ev)
    {
        TryRoundStartAttempt(ev, Loc.GetString("nukeops-title"));
    }

    private void OnPlayersSpawning(RulePlayerSpawningEvent ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var nukeops, out _))
        {
            if (!SpawnMap((uid, nukeops)))
            {
                _sawmill.Info("Failed to load map for nukeops");
                continue;
            }

            //Handle there being nobody readied up
            if (ev.PlayerPool.Count == 0)
                continue;

            var commanderEligible = _antagSelection.GetEligibleSessions(ev.PlayerPool, nukeops.CommanderSpawnDetails.AntagRoleProto);
            var agentEligible = _antagSelection.GetEligibleSessions(ev.PlayerPool, nukeops.AgentSpawnDetails.AntagRoleProto);
            var operativeEligible = _antagSelection.GetEligibleSessions(ev.PlayerPool, nukeops.OperativeSpawnDetails.AntagRoleProto);
            //Calculate how large the nukeops team needs to be
            var nukiesToSelect = _antagSelection.CalculateAntagCount(_playerManager.PlayerCount, nukeops.PlayersPerOperative, nukeops.MaxOps);

            //Select Nukies
            //Select Commander, priority : commanderEligible, agentEligible, operativeEligible, all players
            var selectedCommander = _antagSelection.ChooseAntags(1, commanderEligible, agentEligible, operativeEligible, ev.PlayerPool).FirstOrDefault();
            //Select Agent, priority : agentEligible, operativeEligible, all players
            var selectedAgent = _antagSelection.ChooseAntags(1, agentEligible, operativeEligible, ev.PlayerPool).FirstOrDefault();
            //Select Operatives, priority : operativeEligible, all players
            var selectedOperatives = _antagSelection.ChooseAntags(nukiesToSelect - 2, operativeEligible, ev.PlayerPool);

            //Create the team!
            //If the session is null, they will be spawned as ghost roles (provided the cvar is set)
            var operatives = new List<NukieSpawn> { new NukieSpawn(selectedCommander, nukeops.CommanderSpawnDetails) };
            if (nukiesToSelect > 1)
                operatives.Add(new NukieSpawn(selectedAgent, nukeops.AgentSpawnDetails));

            for (var i = 0; i < nukiesToSelect - 2; i++)
            {
                //Use up all available sessions first, then spawn the rest as ghost roles (if enabled)
                if (selectedOperatives.Count > i)
                {
                    operatives.Add(new NukieSpawn(selectedOperatives[i], nukeops.OperativeSpawnDetails));
                }
                else
                {
                    operatives.Add(new NukieSpawn(null, nukeops.OperativeSpawnDetails));
                }
            }

            SpawnOperatives(operatives, _cfg.GetCVar(CCVars.NukeopsSpawnGhostRoles), nukeops);

            foreach (var nukieSpawn in operatives)
            {
                if (nukieSpawn.Session == null)
                    continue;

                GameTicker.PlayerJoinGame(nukieSpawn.Session);
            }
        }
    }

    private void OnRoundEndText(RoundEndTextAppendEvent ev)
    {
        var ruleQuery = QueryActiveRules();
        while (ruleQuery.MoveNext(out _, out _, out var nukeops, out _))
        {
            var winText = Loc.GetString($"nukeops-{nukeops.WinType.ToString().ToLower()}");
            ev.AddLine(winText);

            foreach (var cond in nukeops.WinConditions)
            {
                var text = Loc.GetString($"nukeops-cond-{cond.ToString().ToLower()}");
                ev.AddLine(text);
            }
        }

        ev.AddLine(Loc.GetString("nukeops-list-start"));

        var nukiesQuery = EntityQueryEnumerator<NukeopsRoleComponent, MindContainerComponent>();
        while (nukiesQuery.MoveNext(out var nukeopsUid, out _, out var mindContainer))
        {
            if (!_mind.TryGetMind(nukeopsUid, out _, out var mind, mindContainer))
                continue;

            ev.AddLine(mind.Session != null
                ? Loc.GetString("nukeops-list-name-user", ("name", Name(nukeopsUid)), ("user", mind.Session.Name))
                : Loc.GetString("nukeops-list-name", ("name", Name(nukeopsUid))));
        }
    }

    private void OnNukeExploded(NukeExplodedEvent ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var nukeops, out _))
        {
            if (ev.OwningStation != null)
            {
                if (ev.OwningStation == nukeops.NukieOutpost)
                {
                    nukeops.WinConditions.Add(WinCondition.NukeExplodedOnNukieOutpost);
                    SetWinType(uid, WinType.CrewMajor, nukeops);
                    continue;
                }

                if (TryComp(nukeops.TargetStation, out StationDataComponent? data))
                {
                    var correctStation = false;
                    foreach (var grid in data.Grids)
                    {
                        if (grid != ev.OwningStation)
                        {
                            continue;
                        }

                        nukeops.WinConditions.Add(WinCondition.NukeExplodedOnCorrectStation);
                        SetWinType(uid, WinType.OpsMajor, nukeops);
                        correctStation = true;
                    }

                    if (correctStation)
                        continue;
                }

                nukeops.WinConditions.Add(WinCondition.NukeExplodedOnIncorrectLocation);
            }
            else
            {
                nukeops.WinConditions.Add(WinCondition.NukeExplodedOnIncorrectLocation);
            }

            _roundEndSystem.EndRound();
        }
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var nukeops, out _))
        {
            switch (ev.New)
            {
                case GameRunLevel.InRound:
                    OnRoundStart(uid, nukeops);
                    break;
                case GameRunLevel.PostRound:
                    OnRoundEnd(uid, nukeops);
                    break;
            }
        }
    }

    private void OnNukeDisarm(NukeDisarmSuccessEvent ev)
    {
        CheckRoundShouldEnd();
    }

    private void OnComponentRemove(EntityUid uid, NukeOperativeComponent component, ComponentRemove args)
    {
        CheckRoundShouldEnd();
    }

    private void OnMobStateChanged(EntityUid uid, NukeOperativeComponent component, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead)
            CheckRoundShouldEnd();
    }

    private void OnPlayersGhostSpawning(EntityUid uid, NukeOperativeComponent component, GhostRoleSpawnerUsedEvent args)
    {
        var spawner = args.Spawner;

        if (!TryComp<NukeOperativeSpawnerComponent>(spawner, out var nukeOpSpawner))
            return;

        HumanoidCharacterProfile? profile = null;
        if (TryComp(args.Spawned, out ActorComponent? actor))
            profile = _prefs.GetPreferences(actor.PlayerSession.UserId).SelectedCharacter as HumanoidCharacterProfile;

        // TODO: this is kinda awful for multi-nukies
        foreach (var nukeops in EntityQuery<NukeopsRuleComponent>())
        {
            SetupOperativeEntity(uid, nukeOpSpawner.OperativeName, nukeOpSpawner.SpawnDetails, profile);

            nukeops.OperativeMindPendingData.Add(uid, nukeOpSpawner.SpawnDetails.AntagRoleProto);
        }
    }

    private void OnMindAdded(EntityUid uid, NukeOperativeComponent component, MindAddedMessage args)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out var mind))
            return;

        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var nukeops, out _))
        {
            if (nukeops.OperativeMindPendingData.TryGetValue(uid, out var role) || !nukeops.SpawnOutpost ||
                nukeops.RoundEndBehavior == RoundEndBehavior.Nothing)
            {
                role ??= nukeops.OperativeSpawnDetails.AntagRoleProto;
                _roles.MindAddRole(mindId, new NukeopsRoleComponent { PrototypeId = role });
                nukeops.OperativeMindPendingData.Remove(uid);
            }

            if (mind.Session is not { } playerSession)
                return;

            if (GameTicker.RunLevel != GameRunLevel.InRound)
                return;

            if (nukeops.TargetStation != null && !string.IsNullOrEmpty(Name(nukeops.TargetStation.Value)))
            {
                NotifyNukie(playerSession, component, nukeops);
            }
        }
    }

    private void OnOperativeZombified(EntityUid uid, NukeOperativeComponent component, ref EntityZombifiedEvent args)
    {
        RemCompDeferred(uid, component);
    }

    private void OnRuleLoadedGrids(Entity<NukeopsRuleComponent> ent, ref RuleLoadedGridsEvent args)
    {
        // Check each nukie shuttle
        var query = EntityQueryEnumerator<NukeOpsShuttleComponent>();
        while (query.MoveNext(out var uid, out var shuttle))
        {
            // Check if the shuttle's mapID is the one that just got loaded for this rule
            if (Transform(uid).MapID == args.Map)
            {
                shuttle.AssociatedRule = ent;
                break;
            }
        }
    }

    private void OnShuttleFTLAttempt(ref ConsoleFTLAttemptEvent ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var nukeops, out _))
        {
            if (ev.Uid != nukeops.NukieShuttle)
                continue;

            if (nukeops.WarDeclaredTime != null)
            {
                var timeAfterDeclaration = Timing.CurTime.Subtract(nukeops.WarDeclaredTime.Value);
                var timeRemain = nukeops.WarNukieArriveDelay.Subtract(timeAfterDeclaration);
                if (timeRemain > TimeSpan.Zero)
                {
                    ev.Cancelled = true;
                    ev.Reason = Loc.GetString("war-ops-infiltrator-unavailable",
                        ("time", timeRemain.ToString("mm\\:ss")));
                    continue;
                }
            }

            nukeops.LeftOutpost = true;
        }
    }

    private void OnShuttleCallAttempt(ref CommunicationConsoleCallShuttleAttemptEvent ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var nukeops, out _))
        {
            // Can't call while war nukies are preparing to arrive
            if (nukeops is { WarDeclaredTime: not null })
            {
                // Nukies must wait some time after declaration of war to get on the station
                var warTime = Timing.CurTime.Subtract(nukeops.WarDeclaredTime.Value);
                if (warTime < nukeops.WarNukieArriveDelay)
                {
                    ev.Cancelled = true;
                    ev.Reason = Loc.GetString("war-ops-shuttle-call-unavailable");
                    return;
                }
            }
        }
    }

    private void OnWarDeclared(ref WarDeclaredEvent ev)
    {
        // TODO: this is VERY awful for multi-nukies
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var nukeops, out _))
        {
            if (nukeops.WarDeclaredTime != null)
                continue;

            if (TryComp<RuleGridsComponent>(uid, out var grids) && Transform(ev.DeclaratorEntity).MapID != grids.Map)
                continue;

            var newStatus = GetWarCondition(nukeops, ev.Status);
            ev.Status = newStatus;
            if (newStatus == WarConditionStatus.WarReady)
            {
                nukeops.WarDeclaredTime = Timing.CurTime;
                var timeRemain = nukeops.WarNukieArriveDelay + Timing.CurTime;
                ev.DeclaratorEntity.Comp.ShuttleDisabledTime = timeRemain;

                DistributeExtraTc(nukeops);
            }
        }
    }

    #endregion Event Handlers

    /// <summary>
    ///     Returns conditions for war declaration
    /// </summary>
    public WarConditionStatus GetWarCondition(NukeopsRuleComponent nukieRule, WarConditionStatus? oldStatus)
    {
        if (!nukieRule.CanEnableWarOps)
            return WarConditionStatus.NoWarUnknown;

        if (EntityQuery<NukeopsRoleComponent>().Count() < nukieRule.WarDeclarationMinOps)
            return WarConditionStatus.NoWarSmallCrew;

        if (nukieRule.LeftOutpost)
            return WarConditionStatus.NoWarShuttleDeparted;

        if (oldStatus == WarConditionStatus.YesWar)
            return WarConditionStatus.WarReady;

        return WarConditionStatus.YesWar;
    }

    private void DistributeExtraTc(NukeopsRuleComponent nukieRule)
    {
        var enumerator = EntityQueryEnumerator<StoreComponent>();
        while (enumerator.MoveNext(out var uid, out var component))
        {
            if (!_tag.HasTag(uid, NukeOpsUplinkTagPrototype))
                continue;

            if (!nukieRule.NukieOutpost.HasValue)
                continue;

            if (Transform(uid).MapID != Transform(nukieRule.NukieOutpost.Value).MapID) // Will receive bonus TC only on their start outpost
                continue;

            _store.TryAddCurrency(new() { { TelecrystalCurrencyPrototype, nukieRule.Comp.WarTcAmountPerNukie } }, uid, component);

            var msg = Loc.GetString("store-currency-war-boost-given", ("target", uid));
            _popupSystem.PopupEntity(msg, uid);
        }
    }

    private void OnRoundStart(EntityUid uid, NukeopsRuleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        // TODO: This needs to try and target a Nanotrasen station. At the very least,
        // we can only currently guarantee that NT stations are the only station to
        // exist in the base game.

        var eligible = new List<Entity<StationEventEligibleComponent, NpcFactionMemberComponent>>();
        var eligibleQuery = EntityQueryEnumerator<StationEventEligibleComponent, NpcFactionMemberComponent>();
        while (eligibleQuery.MoveNext(out var eligibleUid, out var eligibleComp, out var member))
        {
            if (!_npcFaction.IsFactionHostile(component.Faction, (eligibleUid, member)))
                continue;

            eligible.Add((eligibleUid, eligibleComp, member));
        }

        if (eligible.Count == 0)
            return;

        component.TargetStation = RobustRandom.Pick(eligible);
        component.OperationName = _randomMetadata.GetRandomFromSegments([OperationPrefixDataset, OperationSuffixDataset], " ");

        var filter = Filter.Empty();
        var query = EntityQueryEnumerator<NukeOperativeComponent, ActorComponent>();
        while (query.MoveNext(out _, out var nukeops, out var actor))
        {
            NotifyNukie(actor.PlayerSession, nukeops, component);
            filter.AddPlayer(actor.PlayerSession);
        }
    }

    private void OnRoundEnd(EntityUid uid, NukeopsRuleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        // If the win condition was set to operative/crew major win, ignore.
        if (component.WinType == WinType.OpsMajor || component.WinType == WinType.CrewMajor)
            return;

        var nukeQuery = AllEntityQuery<NukeComponent, TransformComponent>();
        var centcomms = _emergency.GetCentcommMaps();

        while (nukeQuery.MoveNext(out var nuke, out var nukeTransform))
        {
            if (nuke.Status != NukeStatus.ARMED)
                continue;

            // UH OH
            if (nukeTransform.MapUid != null && centcomms.Contains(nukeTransform.MapUid.Value))
            {
                component.WinConditions.Add(WinCondition.NukeActiveAtCentCom);
                SetWinType(uid, WinType.OpsMajor, component);
                return;
            }

            if (nukeTransform.GridUid == null || component.TargetStation == null)
                continue;

            if (!TryComp(component.TargetStation.Value, out StationDataComponent? data))
                continue;

            foreach (var grid in data.Grids)
            {
                if (grid != nukeTransform.GridUid)
                    continue;

                component.WinConditions.Add(WinCondition.NukeActiveInStation);
                SetWinType(uid, WinType.OpsMajor, component);
                return;
            }
        }

        var allAlive = true;
        var query = EntityQueryEnumerator<NukeopsRoleComponent, MindContainerComponent, MobStateComponent>();
        while (query.MoveNext(out var nukeopsUid, out _, out var mindContainer, out var mobState))
        {
            // mind got deleted somehow so ignore it
            if (!_mind.TryGetMind(nukeopsUid, out _, out var mind, mindContainer))
                continue;

            // check if player got gibbed or ghosted or something - count as dead
            if (mind.OwnedEntity != null &&
                // if the player somehow isn't a mob anymore that also counts as dead
                // have to be alive, not crit or dead
                mobState.CurrentState is MobState.Alive)
            {
                continue;
            }

            allAlive = false;
            break;
        }

        // If all nuke ops were alive at the end of the round,
        // the nuke ops win. This is to prevent people from
        // running away the moment nuke ops appear.
        if (allAlive)
        {
            SetWinType(uid, WinType.OpsMinor, component);
            component.WinConditions.Add(WinCondition.AllNukiesAlive);
            return;
        }

        component.WinConditions.Add(WinCondition.SomeNukiesAlive);

        var diskAtCentCom = false;
        var diskQuery = AllEntityQuery<NukeDiskComponent, TransformComponent>();

        while (diskQuery.MoveNext(out _, out var transform))
        {
            diskAtCentCom = transform.MapUid != null && centcomms.Contains(transform.MapUid.Value);

            // TODO: The target station should be stored, and the nuke disk should store its original station.
            // This is fine for now, because we can assume a single station in base SS14.
            break;
        }

        // If the disk is currently at Central Command, the crew wins - just slightly.
        // This also implies that some nuclear operatives have died.
        if (diskAtCentCom)
        {
            SetWinType(uid, WinType.CrewMinor, component);
            component.WinConditions.Add(WinCondition.NukeDiskOnCentCom);
        }
        // Otherwise, the nuke ops win.
        else
        {
            SetWinType(uid, WinType.OpsMinor, component);
            component.WinConditions.Add(WinCondition.NukeDiskNotOnCentCom);
        }
    }

    private void SetWinType(EntityUid uid, WinType type, NukeopsRuleComponent? component = null, bool endRound = true)
    {
        if (!Resolve(uid, ref component))
            return;

        component.WinType = type;

        if (endRound && (type == WinType.CrewMajor || type == WinType.OpsMajor))
            _roundEndSystem.EndRound();
    }

    private void CheckRoundShouldEnd()
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var nukeops, out _))
        {
            if (nukeops.RoundEndBehavior == RoundEndBehavior.Nothing || nukeops.WinType == WinType.CrewMajor || nukeops.WinType == WinType.OpsMajor)
                continue;

            // If there are any nuclear bombs that are active, immediately return. We're not over yet.
            var armed = false;
            foreach (var nuke in EntityQuery<NukeComponent>())
            {
                if (nuke.Status == NukeStatus.ARMED)
                {
                    armed = true;
                    break;
                }
            }
            if (armed)
                continue;

            MapId? shuttleMapId = Exists(nukeops.NukieShuttle)
                ? Transform(nukeops.NukieShuttle.Value).MapID
                : null;

            MapId? targetStationMap = null;
            if (nukeops.TargetStation != null && TryComp(nukeops.TargetStation, out StationDataComponent? data))
            {
                var grid = data.Grids.FirstOrNull();
                targetStationMap = grid != null
                    ? Transform(grid.Value).MapID
                    : null;
            }

            // Check if there are nuke operatives still alive on the same map as the shuttle,
            // or on the same map as the station.
            // If there are, the round can continue.
            var operatives = EntityQuery<NukeOperativeComponent, MobStateComponent, TransformComponent>(true);
            var operativesAlive = operatives
                .Where(ent =>
                    ent.Item3.MapID == shuttleMapId
                    || ent.Item3.MapID == targetStationMap)
                .Any(ent => ent.Item2.CurrentState == MobState.Alive && ent.Item1.Running);

            if (operativesAlive)
                continue; // There are living operatives than can access the shuttle, or are still on the station's map.

            // Check that there are spawns available and that they can access the shuttle.
            var spawnsAvailable = EntityQuery<NukeOperativeSpawnerComponent>(true).Any();
            if (spawnsAvailable && shuttleMapId == nukeops.NukiePlanet)
                continue; // Ghost spawns can still access the shuttle. Continue the round.

            // The shuttle is inaccessible to both living nuke operatives and yet to spawn nuke operatives,
            // and there are no nuclear operatives on the target station's map.
            nukeops.WinConditions.Add(spawnsAvailable
                ? WinCondition.NukiesAbandoned
                : WinCondition.AllNukiesDead);

            SetWinType(uid, WinType.CrewMajor, nukeops, false);
            _roundEndSystem.DoRoundEndBehavior(
                nukeops.RoundEndBehavior, nukeops.EvacShuttleTime, nukeops.RoundEndTextSender, nukeops.RoundEndTextShuttleCall, nukeops.RoundEndTextAnnouncement);

            // prevent it called multiple times
            nukeops.RoundEndBehavior = RoundEndBehavior.Nothing;
        }

        // Check if there are nuke operatives still alive on the same map as the shuttle,
        // or on the same map as the station.
        // If there are, the round can continue.
        var operatives = EntityQuery<NukeOperativeComponent, MobStateComponent, TransformComponent>(true);
        var operativesAlive = operatives
            .Where(op =>
                op.Item3.MapID == shuttleMapId
                || op.Item3.MapID == targetStationMap)
            .Any(op => op.Item2.CurrentState == MobState.Alive && op.Item1.Running);

        if (operativesAlive)
            return; // There are living operatives than can access the shuttle, or are still on the station's map.

        // Check that there are spawns available and that they can access the shuttle.
        var spawnsAvailable = EntityQuery<NukeOperativeSpawnerComponent>(true).Any();
        if (spawnsAvailable && CompOrNull<RuleGridsComponent>(ent)?.Map == shuttleMapId)
            return; // Ghost spawns can still access the shuttle. Continue the round.

        // The shuttle is inaccessible to both living nuke operatives and yet to spawn nuke operatives,
        // and there are no nuclear operatives on the target station's map.
        nukeops.WinConditions.Add(spawnsAvailable
            ? WinCondition.NukiesAbandoned
            : WinCondition.AllNukiesDead);

        SetWinType(ent, WinType.CrewMajor, false);
        _roundEndSystem.DoRoundEndBehavior(
            nukeops.RoundEndBehavior, nukeops.EvacShuttleTime, nukeops.RoundEndTextSender, nukeops.RoundEndTextShuttleCall, nukeops.RoundEndTextAnnouncement);

        // prevent it called multiple times
        nukeops.RoundEndBehavior = RoundEndBehavior.Nothing;
    }

    private void OnAfterAntagEntSelected(Entity<NukeopsRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        if (nukeopsRule.TargetStation is not { } station)
            return;

        _antagSelection.SendBriefing(session, Loc.GetString("nukeops-welcome", ("station", station), ("name", nukeopsRule.OperationName)), Color.Red, nukeop.GreetSoundNotification);
    }

    /// <remarks>
    /// Is this method the shitty glue holding together the last of my sanity? yes.
    /// Do i have a better solution? not presently.
    /// </remarks>
    private EntityUid? GetOutpost(Entity<RuleGridsComponent?> ent)
    {
        if (!Resolve(uid, ref component))
            return;

        return ent.Comp.MapGrids.Where(e => !HasComp<NukeOpsShuttleComponent>(e)).FirstOrNull();
    }

    /// <remarks>
    /// Is this method the shitty glue holding together the last of my sanity? yes.
    /// Do i have a better solution? not presently.
    /// </remarks>
    private EntityUid? GetShuttle(Entity<NukeopsRuleComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return null;

        var query = EntityQueryEnumerator<NukeOpsShuttleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            _sawmill.Info("Failed to load map for nukeops");
            return;
        }

        var numNukies = _antagSelection.CalculateAntagCount(_playerManager.PlayerCount, component.PlayersPerOperative, component.MaxOps);

        //Dont continue if we have no nukies to spawn
        if (numNukies == 0)
            return;

        //Fill the ranks, commander first, then agent, then operatives
        //TODO: Possible alternative team compositions? Like multiple commanders or agents
        var operatives = new List<NukieSpawn>();
        if (numNukies >= 1)
            operatives.Add(new NukieSpawn(null, component.CommanderSpawnDetails));
        if (numNukies >= 2)
            operatives.Add(new NukieSpawn(null, component.AgentSpawnDetails));
        if (numNukies >= 3)
        {
            for (var i = 2; i < numNukies; i++)
            {
                operatives.Add(new NukieSpawn(null, component.OperativeSpawnDetails));
            }
        }

        SpawnOperatives(operatives, true, component);
    }

    //For admins forcing someone to nukeOps.
    public void MakeLoneNukie(EntityUid entity)
    {
        if (!_mind.TryGetMind(entity, out var mindId, out var mindComponent))
            return;

        //ok hardcoded value bad but so is everything else here
        _roles.MindAddRole(mindId, new NukeopsRoleComponent { PrototypeId = NukeopsId }, mindComponent);
        SetOutfitCommand.SetOutfit(entity, "SyndicateOperativeGearFull", EntityManager);
    }

    private sealed class NukieSpawn
    {
        public ICommonSession? Session { get; private set; }
        public NukeopSpawnPreset Type { get; private set; }

        public NukieSpawn(ICommonSession? session, NukeopSpawnPreset type)
        {
            Session = session;
            Type = type;
        }
    }
}
