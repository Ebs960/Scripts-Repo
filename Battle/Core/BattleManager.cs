using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BattleManager : MonoBehaviour, ISaveGameParticipant
{
    public static BattleManager Instance { get; private set; }

    [Header("Config")]
    [SerializeField] private BattleRuleset ruleset;

    public BattleSession ActiveBattle { get; private set; }
    public bool IsBattleActive => ActiveBattle != null;

    public string SaveKey => "BattleManager";

    private BattleParticipantCollector participantCollector;
    private BattleMapBuilder mapBuilder;
    private BattleResultApplier resultApplier = new();
    private EngagementPreview pendingPreview;
    private int nextBattleId = 1;

    public event Action<EngagementPreview> BattlePreviewOpened;
    public event Action BattlePreviewClosed;
    public event Action<BattleSession> BattleStarted;
    public event Action<BattleResult> BattleResolved;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (ruleset == null)
            ruleset = ScriptableObject.CreateInstance<BattleRuleset>();

        participantCollector = new BattleParticipantCollector(ruleset);
        mapBuilder = new BattleMapBuilder(ruleset);

        SaveGameRegistry.Register(this);
        GameInteractionStateService.GetOrCreate();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        SaveGameRegistry.Unregister(this);
    }

    public EngagementPreview RequestEngagement(CombatUnit attacker, CombatUnit defender)
    {
        if (IsBattleActive)
        {
            return new EngagementPreview
            {
                IsValid = false,
                RejectionReason = "battle already active",
                Attacker = attacker,
                Defender = defender,
                Mode = EngagementMode.Unsupported,
            };
        }

        if (!participantCollector.TryBuildPreview(attacker, defender, out var preview))
            return preview;

        preview.Map = mapBuilder.Build(preview);
        if (preview.Map == null)
        {
            preview.IsValid = false;
            preview.RejectionReason = "map generation failed";
            return preview;
        }

        preview.ApproachDirectionXZ = ResolveApproachDirection(preview);
        BattleDeploymentBuilder.BuildDeploymentZones(preview.Map, preview, ruleset.deploymentDepthCells);
        preview.Objective = BattleObjectiveBuilder.BuildObjective(preview.Map);

        int deployA = Mathf.Min(ruleset.maxInitialUnitsPerSide, preview.AttackerUnits.Count);
        int deployD = Mathf.Min(ruleset.maxInitialUnitsPerSide, preview.DefenderUnits.Count);
        if (!BattleMapValidator.Validate(preview.Map, deployA, deployD, out string reason))
        {
            preview.IsValid = false;
            preview.RejectionReason = reason;
            return preview;
        }

        pendingPreview = preview;
        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.BattlePreview);
        BattlePreviewOpened?.Invoke(preview);
        RaiseBattlePreviewOpened(preview);
        return preview;
    }

    public void BeginManualBattle(EngagementPreview preview)
    {
        if (preview == null || !preview.IsValid)
            return;

        var state = BuildBattleState(preview);
        ActiveBattle = state.Session;

        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.BattleDeployment);
        BattleStarted?.Invoke(ActiveBattle);
        RaiseBattleStarted(ActiveBattle);

        // MVP: manual mode uses headless simulation until full tactical presentation is connected.
        var result = SimulateBattle(state, wasAutoResolved: false);
        FinishActiveBattle(result);
    }

    public BattleResult AutoResolve(EngagementPreview preview)
    {
        if (preview == null || !preview.IsValid)
            return new BattleResult { ResolutionType = BattleResolutionType.Invalid, WasAutoResolved = true };

        var state = BuildBattleState(preview);
        ActiveBattle = state.Session;

        var result = SimulateBattle(state, wasAutoResolved: true);
        FinishActiveBattle(result);
        return result;
    }

    public void FinishActiveBattle(BattleResult result)
    {
        if (ActiveBattle == null)
            return;

        try
        {
            resultApplier.Apply(result, pendingPreview);
            BattleResolved?.Invoke(result);
            RaiseBattleResolved(result);
        }
        finally
        {
            ActiveBattle = null;
            pendingPreview = null;
            GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.Campaign);
            BattlePreviewClosed?.Invoke();
            RaiseBattleClosed();
        }
    }

    public void CancelPreview()
    {
        pendingPreview = null;
        if (!IsBattleActive)
            GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.Campaign);

        BattlePreviewClosed?.Invoke();
        RaiseBattlePreviewClosed();
    }

    private BattleState BuildBattleState(EngagementPreview preview)
    {
        var units = BattleUnitFactory.CreateStates(preview.AttackerUnits, preview.DefenderUnits);
        var occupancy = new BattleOccupancy();

        AutoDeployUnits(preview.Map, units, occupancy, BattleSide.Attacker, ruleset.maxInitialUnitsPerSide);
        AutoDeployUnits(preview.Map, units, occupancy, BattleSide.Defender, ruleset.maxInitialUnitsPerSide);

        var session = new BattleSession(
            nextBattleId++,
            preview.PlanetIndex,
            preview.AnchorTile,
            ruleset.maxRounds,
            preview.RandomSeed,
            preview.Map,
            units,
            preview.Objective,
            preview.Reinforcements);

        var movementService = new BattleMovementService(new BattlePathfinder(ruleset));
        var resolver = new BattleCombatResolver(ruleset);
        var commandExecutor = new BattleCommandExecutor(movementService, resolver, new BattleLineOfSight());

        return new BattleState
        {
            Session = session,
            Occupancy = occupancy,
            MovementService = movementService,
            CombatResolver = resolver,
            CommandExecutor = commandExecutor,
            TurnController = new BattleTurnController(ruleset),
            AiController = new BattleAIController(),
        };
    }

    private BattleResult SimulateBattle(BattleState state, bool wasAutoResolved)
    {
        var session = state.Session;
        var turns = state.TurnController;
        var ai = state.AiController;

        turns.BeginBattle(session);
        turns.BeginRound(session);

        int totalCommands = 0;
        var reinforcements = new BattleReinforcementController();

        while (true)
        {
            RaiseBattleRoundStarted(session.CurrentRound);
            for (int sideStep = 0; sideStep < 2; sideStep++)
            {
                RaiseBattleSideTurnStarted(session.ActiveSide);
                ai.ExecuteSide(session, state.CommandExecutor, state.Occupancy, ruleset.maxAutoResolveCommandsPerRound, out int executed);
                totalCommands += executed;
                if (totalCommands > ruleset.maxAutoResolveTotalCommands)
                    break;

                var winner = TryResolveWinner(session);
                if (winner.HasValue)
                    return BuildResult(session, winner.Value, wasAutoResolved);

                turns.EndCurrentSide(session);
            }

            if (totalCommands > ruleset.maxAutoResolveTotalCommands)
                return BuildResult(session, BattleSide.Defender, true, BattleResolutionType.Invalid);

            reinforcements.DeployRoundReinforcements(session, session.CurrentRound + 1);

            if (!turns.EndRoundAndAdvance(session))
                return BuildResult(session, BattleSide.Defender, wasAutoResolved, BattleResolutionType.DefenderHeld);

            turns.BeginRound(session);
        }
    }

    private static BattleSide? TryResolveWinner(BattleSession session)
    {
        bool attackerAlive = false;
        bool defenderAlive = false;

        for (int i = 0; i < session.Units.Count; i++)
        {
            var u = session.Units[i];
            if (!u.IsAliveAndActive)
                continue;

            if (u.Side == BattleSide.Attacker)
                attackerAlive = true;
            else
                defenderAlive = true;
        }

        if (!attackerAlive && defenderAlive)
            return BattleSide.Defender;

        if (!defenderAlive && attackerAlive)
            return BattleSide.Attacker;

        if (!attackerAlive && !defenderAlive)
            return BattleSide.Defender;

        bool attackerOnObjective = false;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var u = session.Units[i];
            if (u.IsAliveAndActive && u.Side == BattleSide.Attacker && u.CellIndex == session.Objective.CellIndex)
            {
                attackerOnObjective = true;
                break;
            }
        }

        if (attackerOnObjective && session.Phase == BattlePhase.RoundEnd)
            return BattleSide.Attacker;

        return null;
    }

    private static BattleResult BuildResult(BattleSession session, BattleSide winner, bool wasAutoResolved, BattleResolutionType forcedType = BattleResolutionType.AutoResolved)
    {
        var result = new BattleResult
        {
            BattleId = session.BattleId,
            WinningSide = winner,
            ResolutionType = forcedType == BattleResolutionType.AutoResolved
                ? (winner == BattleSide.Attacker ? BattleResolutionType.Elimination : BattleResolutionType.DefenderHeld)
                : forcedType,
            FinalRound = session.CurrentRound,
            WasAutoResolved = wasAutoResolved,
        };

        for (int i = 0; i < session.Units.Count; i++)
        {
            var u = session.Units[i];
            result.UnitOutcomes.Add(new BattleUnitOutcome
            {
                CampaignRuntimeId = u.Snapshot.CampaignRuntimeId,
                FinalHealth = Mathf.Max(0, u.CurrentHealth),
                Died = u.IsDead || u.CurrentHealth <= 0,
                Retreated = u.HasRetreated,
                ExperienceGained = u.IsDead ? 0 : Mathf.Max(1, u.Snapshot.Level),
                SuggestedCampaignTile = u.Snapshot.StartingCampaignTile,
                SuggestedStackSlot = u.Snapshot.StartingStackSlot,
            });
        }

        return result;
    }

    private static Vector2 ResolveApproachDirection(EngagementPreview preview)
    {
        var ts = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        if (ts == null || preview.Attacker == null || preview.Defender == null)
            return Vector2.right;

        Vector3 atk = ts.GetTileCenterFlat(preview.Attacker.currentTileIndex);
        Vector3 def = ts.GetTileCenterFlat(preview.Defender.currentTileIndex);
        Vector2 d = new Vector2(def.x - atk.x, def.z - atk.z);
        if (d.sqrMagnitude < 0.0001f)
            return Vector2.right;

        return d.normalized;
    }

    private static void AutoDeployUnits(BattleMap map, List<BattleUnitState> units, BattleOccupancy occupancy, BattleSide side, int maxInitial)
    {
        int deployed = 0;
        for (int i = 0; i < units.Count && deployed < maxInitial; i++)
        {
            var u = units[i];
            if (u.Side != side)
                continue;

            int slot = FindFreeDeploymentCell(map, occupancy, side);
            if (slot < 0)
            {
                u.IsReserve = true;
                continue;
            }

            occupancy.TryMove(u, slot, map);
            deployed++;
        }

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u.Side == side && u.CellIndex < 0)
                u.IsReserve = true;
        }
    }

    private static int FindFreeDeploymentCell(BattleMap map, BattleOccupancy occupancy, BattleSide side)
    {
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var c = map.Cells[i];
            if (c.DeploymentOwner != side)
                continue;

            if (!c.IsPassable || c.IsWater)
                continue;

            if (!occupancy.IsOccupied(i))
                return i;
        }

        return -1;
    }

    public string CaptureStateJson()
    {
        return "{}";
    }

    public void RestoreStateJson(string json)
    {
        ActiveBattle = null;
        pendingPreview = null;
        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.Campaign);
    }

    public static BattleManager GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        var existing = FindAnyObjectByType<BattleManager>();
        if (existing != null)
            return existing;

        var go = new GameObject("BattleManager");
        return go.AddComponent<BattleManager>();
    }

    private static void RaiseBattlePreviewOpened(EngagementPreview preview)
    {
        try { GameEventManager.Instance?.RaiseBattlePreviewOpened(preview); } catch { }
    }

    private static void RaiseBattlePreviewClosed()
    {
        try { GameEventManager.Instance?.RaiseBattlePreviewClosed(); } catch { }
    }

    private static void RaiseBattleStarted(BattleSession session)
    {
        try { GameEventManager.Instance?.RaiseBattleStarted(session.BattleId); } catch { }
    }

    private static void RaiseBattleResolved(BattleResult result)
    {
        try { GameEventManager.Instance?.RaiseBattleResolved(result.BattleId, result.ResolutionType, result.WinningSide); } catch { }
    }

    private static void RaiseBattleClosed()
    {
        try { GameEventManager.Instance?.RaiseBattleClosed(); } catch { }
    }

    private static void RaiseBattleRoundStarted(int round)
    {
        try { GameEventManager.Instance?.RaiseBattleRoundStarted(round); } catch { }
    }

    private static void RaiseBattleSideTurnStarted(BattleSide side)
    {
        try { GameEventManager.Instance?.RaiseBattleSideTurnStarted(side); } catch { }
    }
}
