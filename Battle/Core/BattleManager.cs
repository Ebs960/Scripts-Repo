using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BattleManager : MonoBehaviour, ISaveGameParticipant
{
    [Serializable]
    private sealed class BattleSaveMarker
    {
        public bool hasActiveBattle;
    }

    public static BattleManager Instance { get; private set; }

    [Header("Config")]
    [SerializeField] private BattleRuleset ruleset;

    public BattleState ActiveBattleState { get; private set; }
    public BattleSession ActiveBattle => ActiveBattleState?.Session;
    public bool IsBattleActive => ActiveBattleState != null;
    public EngagementPreview PendingPreview => pendingPreview;
    public BattleResult PendingResult => pendingResult;

    public string SaveKey => "BattleManager";

    private BattleParticipantCollector participantCollector;
    private BattleMapBuilder mapBuilder;
    private BattleResultApplier resultApplier = new();
    private readonly PreBattleRetreatService preBattleRetreat = new();
    private EngagementPreview pendingPreview;
    private BattleResult pendingResult;
    private bool resolvingAiOnlyBattle;
    private int nextBattleId = 1;
    private readonly BattleCommitmentRegistry commitments = new();

    public event Action<EngagementPreview> BattlePreviewOpened;
    public event Action BattlePreviewClosed;
    public event Action<BattleSession> BattleStarted;
    public event Action<BattleSession> BattleStateChanged;
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
        BattlePreviewUI.GetOrCreate(this).Bind(this);
        BattleHUD.GetOrCreate(this).Bind(this);
        BattlePresenter.GetOrCreate(this).Bind(this);
        BattleResultUI.GetOrCreate(this).Bind(this);

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
        if (IsBattleActive || pendingPreview != null || pendingResult != null)
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
        AssignReinforcementEntries(preview);
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

        // Battles with no human participant are campaign simulation. Resolve
        // them here so every attack entry point gets identical behaviour and
        // no preview, deployment screen, or Continue prompt can block the AI.
        if (!IsPlayerInvolved(preview))
        {
            preview.AllowsManualBattle = false;
            preview.AllowsRetreat = false;
            preview.AllowsCancel = false;
            resolvingAiOnlyBattle = true;
            try
            {
                AutoResolve(preview);
            }
            finally
            {
                resolvingAiOnlyBattle = false;
            }
            return preview;
        }

        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.BattlePreview);
        BattlePreviewOpened?.Invoke(preview);
        RaiseBattlePreviewOpened(preview);
        return preview;
    }

    public void BeginManualBattle(EngagementPreview preview)
    {
        if (preview == null || !preview.IsValid || IsBattleActive || pendingResult != null)
            return;

        // A preview is a reservation for one engagement.  Do not allow a stale UI
        // (or another caller) to start a different engagement over that reservation.
        if (!ReferenceEquals(preview, pendingPreview))
            return;

        var state = BuildBattleState(preview);
        if (state == null) return;
        ActiveBattleState = state;
        pendingPreview = preview;

        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.BattleDeployment);
        BattlePreviewClosed?.Invoke();
        RaiseBattlePreviewClosed();
        BattleStarted?.Invoke(ActiveBattle);
        RaiseBattleStarted(ActiveBattle);

        state.TurnController.BeginBattle(state.Session);
        NotifyBattleStateChanged();
    }

    public bool BeginPendingManualBattle()
    {
        if (pendingPreview == null || !pendingPreview.IsValid || IsBattleActive)
            return false;

        BeginManualBattle(pendingPreview);
        return IsBattleActive;
    }

    public bool AutoResolvePendingPreview(out BattleResult result)
    {
        result = null;
        if (pendingPreview == null || !pendingPreview.IsValid || IsBattleActive)
            return false;

        result = AutoResolve(pendingPreview);
        return result.ResolutionType != BattleResolutionType.Invalid;
    }

    public bool RetreatPendingPreview(out string reason)
    {
        if (pendingPreview == null || !pendingPreview.AllowsRetreat || IsBattleActive)
        {
            reason = "pre-battle retreat is unavailable";
            return false;
        }

        if (!preBattleRetreat.TryRetreat(pendingPreview, out reason))
            return false;

        CancelPreview();
        return true;
    }

    public bool TryAssignGovernorCommander(BattleSide side, int governorId, out string reason)
        => TryAssignGovernorCommander(side, governorId, CommandRole.OverallCommander, out reason);

    public bool TryAssignGovernorCommander(BattleSide side, int governorId, CommandRole role, out string reason)
    {
        reason = string.Empty;
        if (pendingPreview == null || IsBattleActive)
        {
            reason = "commander assignment is unavailable";
            return false;
        }

        var snapshots = side == BattleSide.Attacker ? pendingPreview.AttackerUnits : pendingPreview.DefenderUnits;
        var owner = side == BattleSide.Attacker ? pendingPreview.Attacker?.owner : pendingPreview.Defender?.owner;
        if (snapshots.Count == 0 || owner == null || !owner.isPlayerControlled)
        {
            reason = "players may assign commanders only to their own formation";
            return false;
        }

        Governor governor = null;
        for (int i = 0; i < owner.governors.Count; i++)
            if (owner.governors[i] != null && owner.governors[i].Id == governorId)
            {
                governor = owner.governors[i];
                break;
            }
        if (governor == null)
        {
            reason = "governor not found";
            return false;
        }

        return MilitaryCommanderAssignmentService.GetOrCreate().TryAssignGovernor(owner, governor, snapshots[0].FormationId, role, out reason);
    }

    public bool TryAssignAdmiralCommander(BattleSide side, int admiralId, CommandRole role, out string reason)
    {
        reason = string.Empty;
        if (pendingPreview == null || IsBattleActive) { reason = "commander assignment is unavailable"; return false; }
        var snapshots = side == BattleSide.Attacker ? pendingPreview.AttackerUnits : pendingPreview.DefenderUnits;
        var owner = side == BattleSide.Attacker ? pendingPreview.Attacker?.owner : pendingPreview.Defender?.owner;
        if (snapshots.Count == 0 || owner == null || !owner.isPlayerControlled)
        { reason = "players may assign commanders only to their own formation"; return false; }
        int ownerId = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetCivIndex(owner) : -1;
        var admiral = AdmiralManager.Instance?.GetAdmiral(admiralId);
        if (admiral == null || admiral.ownerCivilizationId != ownerId)
        { reason = "admiral does not belong to this civilization"; return false; }
        return MilitaryCommanderAssignmentService.GetOrCreate().TryAssignAdmiral(owner, admiral, snapshots[0].FormationId, role, out reason);
    }

    public BattleResult AutoResolve(EngagementPreview preview)
    {
        if (preview == null || !preview.IsValid || IsBattleActive || pendingResult != null
            || !ReferenceEquals(preview, pendingPreview))
            return new BattleResult { ResolutionType = BattleResolutionType.Invalid, WasAutoResolved = true };

        var state = BuildBattleState(preview);
        if (state == null) return new BattleResult { ResolutionType = BattleResolutionType.Invalid, WasAutoResolved = true };
        ActiveBattleState = state;

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
            AwardCommanderExperience(result);
            BattleResolved?.Invoke(result);
            RaiseBattleResolved(result);
        }
        finally
        {
            commitments.ReleaseBattle(result != null ? result.BattleId : ActiveBattle.BattleId);
            ActiveBattleState = null;
            pendingPreview = null;
            pendingResult = resolvingAiOnlyBattle ? null : result;
            GameInteractionStateService.GetOrCreate().SetMode(
                resolvingAiOnlyBattle ? GameInteractionMode.Campaign : GameInteractionMode.BattleResult);
            BattlePreviewClosed?.Invoke();
            if (resolvingAiOnlyBattle)
                RaiseBattleClosed();
        }
    }

    public void ContinueAfterBattleResult()
    {
        if (pendingResult == null)
            return;

        pendingResult = null;
        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.Campaign);
        RaiseBattleClosed();
    }

    public void CancelPreview()
    {
        // The preview remains the campaign context used to apply an active
        // battle's result.  Clearing it mid-battle corrupts ownership and
        // placement decisions, so Cancel is strictly a pre-battle operation.
        if (IsBattleActive)
            return;

        pendingPreview = null;
        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.Campaign);

        BattlePreviewClosed?.Invoke();
        RaiseBattlePreviewClosed();
    }

    public bool ConfirmDeployment(out string reason)
    {
        reason = string.Empty;
        if (ActiveBattleState == null || ActiveBattle.Phase != BattlePhase.Deployment) { reason = "no battle awaiting deployment"; return false; }
        ActiveBattleState.TurnController.BeginRound(ActiveBattle);
        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.BattleActive);
        ActiveBattleState.DetectionService.Update(ActiveBattle, BattleSide.Attacker);
        ActiveBattleState.DetectionService.Update(ActiveBattle, BattleSide.Defender);
        AdvanceManualFlow();
        NotifyBattleStateChanged();
        return true;
    }

    public IReadOnlyList<BattleUnitState> GetUnitsForActiveSide()
    {
        if (ActiveBattle == null)
            return System.Array.Empty<BattleUnitState>();

        var result = new List<BattleUnitState>();
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
        {
            var unit = ActiveBattle.Units[i];
            if (unit != null && unit.Side == ActiveBattle.ActiveSide && unit.IsAliveAndActive)
                result.Add(unit);
        }
        return result;
    }

    public IReadOnlyList<BattleUnitState> GetVisibleEnemyUnits(int unitId)
    {
        if (ActiveBattleState == null)
            return System.Array.Empty<BattleUnitState>();

        var attacker = FindUnit(unitId);
        if (attacker == null)
            return System.Array.Empty<BattleUnitState>();

        var result = new List<BattleUnitState>();
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
        {
            var unit = ActiveBattle.Units[i];
            if (unit != null && unit.Side != attacker.Side && unit.IsAliveAndActive
                && ActiveBattleState.DetectionService.CanDirectlyTarget(attacker.Side, unit))
                result.Add(unit);
        }
        return result;
    }

    public BattleUnitState GetUnitAtCell(int cellIndex)
    {
        if (ActiveBattle == null)
            return null;
        // Prefer the active side so stacked air/surface/underwater layers remain
        // straightforward to select from a single tactical cell.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < ActiveBattle.Units.Count; i++)
            {
                var unit = ActiveBattle.Units[i];
                if (unit == null || !unit.IsAliveAndActive || unit.CellIndex != cellIndex)
                    continue;
                if (pass == 0 && unit.Side != ActiveBattle.ActiveSide)
                    continue;
                return unit;
            }
        return null;
    }

    public bool TryDeployUnit(int unitId, int destinationCell, out string reason)
    {
        reason = string.Empty;
        if (ActiveBattleState == null || ActiveBattle.Phase != BattlePhase.Deployment)
        { reason = "battle is not in deployment"; return false; }
        var unit = FindUnit(unitId);
        var cell = ActiveBattle.Map.GetCell(destinationCell);
        if (unit == null || unit.IsReserve || unit.IsEmbarked || unit.Side != BattleSide.Attacker && unit.Side != BattleSide.Defender)
        { reason = "unit is unavailable for deployment"; return false; }
        if (!IsHumanControlledSide(unit.Side))
        { reason = "cannot deploy an AI-controlled unit"; return false; }
        if (cell == null || cell.DeploymentOwner != unit.Side || !cell.Supports(unit.Domain))
        { reason = "cell is outside this side's deployment zone"; return false; }
        if (!ActiveBattleState.Occupancy.TryMove(unit, destinationCell, ActiveBattle.Map))
        { reason = "deployment cell is occupied"; return false; }
        NotifyBattleStateChanged();
        return true;
    }

    public IReadOnlyList<BattleUnitState> GetDeploymentReserves(BattleSide side)
    {
        var result = new List<BattleUnitState>();
        if (ActiveBattle == null) return result;
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
        {
            var unit = ActiveBattle.Units[i];
            if (unit != null && unit.Side == side && unit.IsReserve && !unit.IsDead) result.Add(unit);
        }
        return result;
    }

    public bool TrySwapDeploymentReserve(int deployedUnitId, int reserveUnitId, out string reason)
    {
        reason = string.Empty;
        if (ActiveBattleState == null || ActiveBattle.Phase != BattlePhase.Deployment)
        { reason = "battle is not in deployment"; return false; }
        var deployed = FindUnit(deployedUnitId); var reserve = FindUnit(reserveUnitId);
        if (deployed == null || reserve == null || deployed.Side != reserve.Side || deployed.IsReserve || !reserve.IsReserve
            || deployed.IsEmbarked || reserve.IsEmbarked || !IsHumanControlledSide(deployed.Side))
        { reason = "invalid reserve swap"; return false; }
        int cell = deployed.CellIndex;
        var destination = ActiveBattle.Map.GetCell(cell);
        if (destination == null || !destination.Supports(reserve.Domain))
        { reason = "reserve cannot deploy to this cell"; return false; }
        ActiveBattleState.Occupancy.Remove(deployed);
        deployed.IsReserve = true; deployed.HasEnteredBattle = false;
        reserve.IsReserve = false; reserve.HasEnteredBattle = true;
        if (!ActiveBattleState.Occupancy.TryMove(reserve, cell, ActiveBattle.Map))
        {
            reserve.IsReserve = true; reserve.HasEnteredBattle = false;
            deployed.IsReserve = false; deployed.HasEnteredBattle = true;
            ActiveBattleState.Occupancy.TryMove(deployed, cell, ActiveBattle.Map);
            reason = "reserve deployment failed"; return false;
        }
        NotifyBattleStateChanged(); return true;
    }

    public bool TryGetMovePath(int unitId, int destination, out List<int> path)
    {
        path = null;
        var unit = FindUnit(unitId);
        return ActiveBattleState != null && unit != null && unit.CanAct(ActiveBattle.ActiveSide)
            && ActiveBattleState.MovementService.TryGetPath(ActiveBattle, unit, destination, ActiveBattleState.Occupancy, out path);
    }

    public bool TryMoveUnit(int unitId, int destination, out string reason)
    {
        reason = string.Empty;
        if (!TryGetMovePath(unitId, destination, out var path))
        {
            reason = "invalid move";
            return false;
        }
        return TrySubmitPlayerCommand(new BattleMoveCommand { UnitId = unitId, CommandType = BattleCommandType.Move, Path = path }, out reason);
    }

    public bool TryAttackUnit(int unitId, int targetUnitId, bool ranged, out string reason)
    {
        var unit = FindUnit(unitId);
        var target = FindUnit(targetUnitId);
        if (unit == null || target == null)
        {
            reason = "unit not found";
            return false;
        }
        int distance = ActiveBattle.MapDistance(unit.CellIndex, target.CellIndex);
        int weaponIndex = BattleTargetingService.FindWeaponIndex(unit, target, distance);
        if (weaponIndex < 0)
        {
            reason = "no compatible weapon";
            return false;
        }
        return TryAttackUnitWithWeapon(unitId, targetUnitId, weaponIndex, out reason);
    }

    public bool TryAttackUnitWithWeapon(int unitId, int targetUnitId, int weaponIndex, out string reason)
    {
        var unit = FindUnit(unitId);
        var target = FindUnit(targetUnitId);
        var weapon = BattleTargetingService.GetWeapon(unit, weaponIndex);
        if (unit == null || target == null || weapon == null)
        { reason = "weapon or target not found"; return false; }
        return TrySubmitPlayerCommand(new BattleAttackCommand
        {
            UnitId = unitId,
            CommandType = weapon.usesRangedAttack ? BattleCommandType.RangedAttack : BattleCommandType.MeleeAttack,
            TargetUnitId = targetUnitId,
            AttackFromCell = unit.CellIndex,
            IsRanged = weapon.usesRangedAttack,
            WeaponIndex = weaponIndex,
        }, out reason);
    }

    public bool TryDefendUnit(int unitId, out string reason) => TrySubmitPlayerCommand(new BattleDefendCommand
    {
        UnitId = unitId,
        CommandType = BattleCommandType.Defend,
    }, out reason);

    public bool TryWaitUnit(int unitId, out string reason) => TrySubmitPlayerCommand(new BattleWaitCommand
    {
        UnitId = unitId,
        CommandType = BattleCommandType.Wait,
    }, out reason);

    public bool TryRetreatUnit(int unitId, int exitCell, out string reason) => TrySubmitPlayerCommand(new BattleRetreatCommand
    {
        UnitId = unitId,
        CommandType = BattleCommandType.Retreat,
        ExitCell = exitCell,
    }, out reason);

    public bool TryEmbarkUnit(int unitId, int transportUnitId, out string reason) => TrySubmitPlayerCommand(new BattleEmbarkCommand
    {
        UnitId = unitId,
        CommandType = BattleCommandType.Embark,
        TransportUnitId = transportUnitId,
    }, out reason);

    public bool TryDisembarkUnit(int unitId, int destinationCell, out string reason) => TrySubmitPlayerCommand(new BattleDisembarkCommand
    {
        UnitId = unitId,
        CommandType = BattleCommandType.Disembark,
        DestinationCell = destinationCell,
    }, out reason);

    public bool TryDisembarkFirstCargo(int carrierUnitId, int destinationCell, out string reason)
    {
        var carrier = FindUnit(carrierUnitId);
        if (carrier != null)
            for (int i = 0; i < carrier.EmbarkedBattleUnitIds.Count; i++)
            {
                var cargo = FindUnit(carrier.EmbarkedBattleUnitIds[i]);
                if (cargo != null && cargo.Domain != BattleDomain.Air && cargo.Domain != BattleDomain.Space)
                    return TryDisembarkUnit(cargo.UnitId, destinationCell, out reason);
            }
        reason = "transport has no cargo ready to disembark"; return false;
    }

    public bool TryLaunchAircraft(int carrierUnitId, int aircraftUnitId, int launchCell, out string reason) => TrySubmitPlayerCommand(new BattleLaunchAircraftCommand
    {
        UnitId = carrierUnitId,
        CommandType = BattleCommandType.LaunchAircraft,
        AircraftUnitId = aircraftUnitId,
        LaunchCell = launchCell,
    }, out reason);

    public bool TryLaunchFirstAircraft(int carrierUnitId, int launchCell, out string reason)
    {
        var carrier = FindUnit(carrierUnitId);
        if (carrier != null)
            for (int i = 0; i < carrier.EmbarkedBattleUnitIds.Count; i++)
            {
                var cargo = FindUnit(carrier.EmbarkedBattleUnitIds[i]);
                if (cargo != null && (cargo.Domain == BattleDomain.Air || cargo.Domain == BattleDomain.Space))
                    return TryLaunchAircraft(carrierUnitId, cargo.UnitId, launchCell, out reason);
            }
        reason = "carrier has no launch-ready aircraft"; return false;
    }

    public bool TryRecoverAircraft(int aircraftUnitId, int carrierUnitId, out string reason) => TrySubmitPlayerCommand(new BattleRecoverAircraftCommand
    {
        UnitId = aircraftUnitId,
        CommandType = BattleCommandType.RecoverAircraft,
        CarrierUnitId = carrierUnitId,
    }, out reason);

    public bool TryChangeDepth(int unitId, BattleDepthBand depth, out string reason) => TrySubmitPlayerCommand(new BattleChangeDepthCommand
    {
        UnitId = unitId,
        CommandType = BattleCommandType.ChangeDepth,
        Depth = depth,
    }, out reason);

    public bool TryActiveDetection(int unitId, out string reason) => TrySubmitPlayerCommand(new BattleActiveDetectionCommand
    { UnitId = unitId, CommandType = BattleCommandType.ActiveDetection }, out reason);

    public bool EndUnitActivation(int unitId, out string reason)
    {
        reason = string.Empty;
        var unit = FindUnit(unitId);
        if (unit == null || ActiveBattle == null || !unit.CanAct(ActiveBattle.ActiveSide))
        {
            reason = "unit cannot end activation";
            return false;
        }

        unit.HasActed = true;
        unit.CurrentActionPoints = 0;
        unit.CurrentMovePoints = 0;
        NotifyBattleStateChanged();
        return true;
    }

    public bool TrySubmitPlayerCommand(BattleCommand command, out string reason)
    {
        if (ActiveBattleState == null || (ActiveBattle.Phase != BattlePhase.AttackerTurn && ActiveBattle.Phase != BattlePhase.DefenderTurn))
        { reason = "battle is not accepting commands"; return false; }
        if (!IsHumanControlledSide(ActiveBattle.ActiveSide))
        { reason = "active side is AI-controlled"; return false; }
        var commandedUnit = command != null ? FindUnit(command.UnitId) : null;
        if (commandedUnit == null || commandedUnit.Side != ActiveBattle.ActiveSide)
        { reason = "unit is not controlled by the active side"; return false; }
        bool ok = ActiveBattleState.CommandExecutor.Execute(ActiveBattle, ActiveBattleState.Occupancy, command, out reason);
        if (ok)
        {
            ActiveBattleState.ActionLog.Add(BattleCommandLog.Format(ActiveBattle, command));
            ActiveBattleState.ReplayLog.Commands.Add(BattleCommandRecord.From(ActiveBattle, command));
            NotifyBattleStateChanged();
        }
        return ok;
    }

    public void EndPlayerSideTurn()
    {
        if (ActiveBattleState == null || !IsHumanControlledSide(ActiveBattle.ActiveSide))
            return;

        ActiveBattleState.TurnController.EndCurrentSide(ActiveBattle);
        AdvanceManualFlow();
        NotifyBattleStateChanged();
    }
    public void RunAISideTurn()
    {
        if (ActiveBattleState == null) return;
        ActiveBattleState.AiController.ExecuteSide(ActiveBattle, ActiveBattleState.CommandExecutor, ActiveBattleState.Occupancy, ruleset.maxAutoResolveCommandsPerRound, out _,
            command => ActiveBattleState.ReplayLog.Commands.Add(BattleCommandRecord.From(ActiveBattle, command)));
        NotifyBattleStateChanged();
    }

    private void AdvanceManualFlow()
    {
        while (ActiveBattleState != null)
        {
            if (ActiveBattle.Phase == BattlePhase.RoundEnd)
            {
                new BattleStatusService().ProcessRoundEnd(ActiveBattle);
                ActiveBattleState.DetectionService.Update(ActiveBattle, BattleSide.Attacker);
                ActiveBattleState.DetectionService.Update(ActiveBattle, BattleSide.Defender);
                var winner = TryResolveWinner(ActiveBattle, allowObjectiveVictory: true);
                if (winner.HasValue)
                {
                    FinishActiveBattle(BuildResult(ActiveBattle, winner.Value, false, BattleResolutionType.ObjectiveCaptured));
                    return;
                }

                new BattleReinforcementController().DeployRoundReinforcements(ActiveBattle, ActiveBattleState.Occupancy, ActiveBattle.CurrentRound + 1);
                if (!ActiveBattleState.TurnController.EndRoundAndAdvance(ActiveBattle))
                {
                    FinishActiveBattle(BuildResult(ActiveBattle, BattleSide.Defender, false, BattleResolutionType.DefenderHeld));
                    return;
                }
                ActiveBattleState.TurnController.BeginRound(ActiveBattle);
                continue;
            }

            if (IsHumanControlledSide(ActiveBattle.ActiveSide))
                return;

            RunAISideTurn();
            ActiveBattleState.TurnController.EndCurrentSide(ActiveBattle);
        }
    }

    private bool IsHumanControlledSide(BattleSide side)
    {
        if (pendingPreview == null)
            return false;
        var source = side == BattleSide.Attacker ? pendingPreview.Attacker : pendingPreview.Defender;
        return source != null && source.owner != null && source.owner.isPlayerControlled;
    }

    private static bool IsPlayerInvolved(EngagementPreview preview)
    {
        if (preview == null)
            return false;
        return (preview.Attacker != null && preview.Attacker.owner != null && preview.Attacker.owner.isPlayerControlled)
            || (preview.Defender != null && preview.Defender.owner != null && preview.Defender.owner.isPlayerControlled);
    }

    private BattleUnitState FindUnit(int unitId)
    {
        if (ActiveBattle == null)
            return null;
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
            if (ActiveBattle.Units[i]?.UnitId == unitId)
                return ActiveBattle.Units[i];
        return null;
    }

    private void NotifyBattleStateChanged()
    {
        if (ActiveBattle != null)
            BattleStateChanged?.Invoke(ActiveBattle);
    }

    private BattleState BuildBattleState(EngagementPreview preview)
    {
        var units = BattleUnitFactory.CreateStates(preview.AttackerUnits, preview.DefenderUnits);
        BattleUnitFactory.AppendReserves(units, preview.Reinforcements);
        var occupancy = new BattleOccupancy();

        InitializeTacticalCargo(units);

        AutoDeployUnits(preview.Map, units, occupancy, BattleSide.Attacker, ruleset.maxInitialUnitsPerSide);
        AutoDeployUnits(preview.Map, units, occupancy, BattleSide.Defender, ruleset.maxInitialUnitsPerSide);

        var session = new BattleSession(
            nextBattleId++,
            preview.Theater,
            preview.PlanetIndex,
            preview.SpaceRegionId,
            preview.AnchorTile,
            ruleset.maxRounds,
            preview.RandomSeed,
            preview.Map,
            units,
            preview.Objective,
            preview.Reinforcements);

        for (int i = 0; i < units.Count; i++)
        {
            int runtimeId = units[i].Snapshot.CampaignRuntimeId;
            var commanderAssignment = MilitaryCommanderAssignmentService.GetOrCreate().GetAssignment(units[i].Snapshot.FormationId);
            if (commanderAssignment != null)
            {
                units[i].CommanderAttackMultiplier = MilitaryCommanderAssignmentService.GetOrCreate().GetAttackMultiplier(units[i].Snapshot.FormationId, units[i].Domain);
                units[i].CommanderDefenseMultiplier = MilitaryCommanderAssignmentService.GetOrCreate().GetDefenseMultiplier(units[i].Snapshot.FormationId, units[i].Domain);
            }
            var sourceUnit = units[i].Snapshot.SourceUnit;
            int transportRuntimeId = sourceUnit != null
                && sourceUnit.IsTransported
                && sourceUnit.TransportingUnit != null
                && sourceUnit.TransportingUnit.gameObject != null
                ? sourceUnit.TransportingUnit.gameObject.GetRuntimeId()
                : -1;
            if (!commitments.TryCommit(new BattleCommitment
            {
                CampaignRuntimeId = runtimeId,
                FormationId = units[i].Snapshot.FormationId,
                BattleId = session.BattleId,
                Theater = preview.Theater,
                CarrierOrTransportRuntimeId = transportRuntimeId,
                CommanderAssignmentId = commanderAssignment?.AssignmentId,
                CampaignTurn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0,
            }))
            { commitments.ReleaseBattle(session.BattleId); return null; }
        }

        var movementService = new BattleMovementService(new BattlePathfinder(ruleset));
        var resolver = new BattleCombatResolver(ruleset);
        var detectionService = new BattleDetectionService();
        var commandExecutor = new BattleCommandExecutor(movementService, resolver, new BattleLineOfSight(), detectionService);
        var replayLog = new BattleReplayLog { BattleId = session.BattleId, Seed = session.RandomSeed, Theater = session.Theater };
        for (int i = 0; i < units.Count; i++)
            replayLog.InitialParticipantRuntimeIds.Add(units[i].Snapshot.CampaignRuntimeId);

        return new BattleState
        {
            Session = session,
            Occupancy = occupancy,
            MovementService = movementService,
            CombatResolver = resolver,
            CommandExecutor = commandExecutor,
            DetectionService = detectionService,
            ReplayLog = replayLog,
            TurnController = new BattleTurnController(ruleset),
            AiController = new BattleAIController(detectionService),
        };
    }

    private static void InitializeTacticalCargo(List<BattleUnitState> units)
    {
        var byCampaignUnit = new Dictionary<CombatUnit, BattleUnitState>();
        for (int i = 0; i < units.Count; i++)
            if (units[i]?.Snapshot?.SourceUnit != null)
                byCampaignUnit[units[i].Snapshot.SourceUnit] = units[i];

        for (int i = 0; i < units.Count; i++)
        {
            var cargo = units[i];
            var source = cargo?.Snapshot?.SourceUnit;
            if (source == null || !source.IsTransported || source.TransportingUnit == null
                || !byCampaignUnit.TryGetValue(source.TransportingUnit, out var host))
                continue;
            cargo.IsEmbarked = true;
            cargo.CarrierOrTransportBattleUnitId = host.UnitId;
            cargo.CellIndex = -1;
            if (!host.EmbarkedBattleUnitIds.Contains(cargo.UnitId))
                host.EmbarkedBattleUnitIds.Add(cargo.UnitId);
        }
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
                ai.ExecuteSide(session, state.CommandExecutor, state.Occupancy, ruleset.maxAutoResolveCommandsPerRound, out int executed,
                    command => state.ReplayLog.Commands.Add(BattleCommandRecord.From(session, command)));
                totalCommands += executed;
                if (totalCommands > ruleset.maxAutoResolveTotalCommands)
                    break;

                var winner = TryResolveWinner(session, allowObjectiveVictory: false);
                if (winner.HasValue)
                    return BuildResult(session, winner.Value, wasAutoResolved);

                turns.EndCurrentSide(session);
            }

            if (totalCommands > ruleset.maxAutoResolveTotalCommands)
                return BuildResult(session, BattleSide.Defender, true, BattleResolutionType.Invalid);

            new BattleStatusService().ProcessRoundEnd(session);
            state.DetectionService.Update(session, BattleSide.Attacker);
            state.DetectionService.Update(session, BattleSide.Defender);

            var roundEndWinner = TryResolveWinner(session, allowObjectiveVictory: true);
            if (roundEndWinner.HasValue)
                return BuildResult(session, roundEndWinner.Value, wasAutoResolved, BattleResolutionType.ObjectiveCaptured);

            reinforcements.DeployRoundReinforcements(session, state.Occupancy, session.CurrentRound + 1);

            if (!turns.EndRoundAndAdvance(session))
                return BuildResult(session, BattleSide.Defender, wasAutoResolved, BattleResolutionType.DefenderHeld);

            turns.BeginRound(session);
        }
    }

    private static BattleSide? TryResolveWinner(BattleSession session, bool allowObjectiveVictory)
    {
        bool attackerAlive = false;
        bool defenderAlive = false;

        for (int i = 0; i < session.Units.Count; i++)
        {
            var u = session.Units[i];
            if (u == null || u.IsDead || u.HasRetreated || u.CurrentHealth <= 0)
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
            if (u.IsAliveAndActive && u.Side == BattleSide.Attacker
                && u.CellIndex == session.Objective.CellIndex
                && CanCaptureObjective(u, session.Objective.Type, session.Theater))
            {
                attackerOnObjective = true;
                break;
            }
        }

        if (allowObjectiveVictory && attackerOnObjective && session.Phase == BattlePhase.RoundEnd)
            return BattleSide.Attacker;

        return null;
    }

    private static bool CanCaptureObjective(BattleUnitState unit, BattleObjectiveType objectiveType, BattleTheater theater)
    {
        if (unit == null)
            return false;
        return objectiveType switch
        {
            BattleObjectiveType.LandControl => unit.Domain == BattleDomain.Land,
            BattleObjectiveType.PortCapture => unit.Domain == BattleDomain.Land,
            BattleObjectiveType.Beachhead => unit.Domain == BattleDomain.Land,
            BattleObjectiveType.NavalControl => unit.Domain == BattleDomain.NavalSurface,
            BattleObjectiveType.RegionControl => theater == BattleTheater.DeepSpace
                ? unit.Domain == BattleDomain.Space
                : theater == BattleTheater.Underwater
                    ? unit.Domain == BattleDomain.Underwater
                    : unit.Domain == BattleDomain.Land || unit.Domain == BattleDomain.NavalSurface,
            BattleObjectiveType.Escape => false,
            BattleObjectiveType.Elimination => false,
            _ => false,
        };
    }

    private void AwardCommanderExperience(BattleResult result)
    {
        if (ActiveBattle == null || result == null)
            return;

        var awardedFormations = new HashSet<string>();
        var destroyedByFormation = new Dictionary<string, bool>();
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
        {
            var candidate = ActiveBattle.Units[i];
            string id = candidate?.Snapshot?.FormationId;
            if (string.IsNullOrEmpty(id)) continue;
            bool destroyed = candidate.IsDead || candidate.CurrentHealth <= 0;
            destroyedByFormation[id] = destroyedByFormation.TryGetValue(id, out bool prior) ? prior && destroyed : destroyed;
        }
        var commanderService = MilitaryCommanderAssignmentService.GetOrCreate();
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
        {
            var unit = ActiveBattle.Units[i];
            string formationId = unit?.Snapshot?.FormationId;
            if (string.IsNullOrEmpty(formationId) || !awardedFormations.Add(formationId))
                continue;

            int experience = unit.Side == result.WinningSide ? 20 : 8;
            if (unit.IsDead)
                experience = 4;
            commanderService.AwardBattleExperience(formationId, experience);
            int enemyCivId = -1;
            var enemy = unit.Side == BattleSide.Attacker ? pendingPreview?.Defender?.owner : pendingPreview?.Attacker?.owner;
            if (enemy != null && CivilizationManager.Instance != null)
                enemyCivId = CivilizationManager.Instance.GetCivIndex(enemy);
            commanderService.ResolveBattleFate(formationId,
                destroyedByFormation.TryGetValue(formationId, out bool destroyed) && destroyed,
                enemyCivId, ActiveBattle.RandomSeed + ActiveBattle.BattleId);
        }
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
                Side = u.Side,
                FinalHealth = Mathf.Max(0, u.CurrentHealth),
                Died = u.IsDead || u.CurrentHealth <= 0,
                Retreated = u.HasRetreated,
                Participated = u.HasEnteredBattle,
                WithdrawalCampaignTile = u.WithdrawalCampaignTile,
                ExperienceGained = !u.HasEnteredBattle || u.IsDead ? 0 : Mathf.Max(1, u.Snapshot.Level),
                IsEmbarked = u.IsEmbarked,
                CarrierOrTransportCampaignRuntimeId = GetCarrierCampaignRuntimeId(session, u),
                SuggestedCampaignTile = u.Snapshot.StartingCampaignTile,
                SuggestedStackSlot = u.Snapshot.StartingStackSlot,
            });
        }

        return result;
    }

    private static int GetCarrierCampaignRuntimeId(BattleSession session, BattleUnitState unit)
    {
        if (unit == null || !unit.IsEmbarked)
            return -1;
        for (int i = 0; i < session.Units.Count; i++)
            if (session.Units[i]?.UnitId == unit.CarrierOrTransportBattleUnitId)
                return session.Units[i].Snapshot.CampaignRuntimeId;
        return -1;
    }

    private static Vector2 ResolveApproachDirection(EngagementPreview preview)
    {
        if (preview.Theater == BattleTheater.DeepSpace)
        {
            var spaceGrid = SpaceWorldManager.Instance != null ? SpaceWorldManager.Instance.Grid
                : (SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : null);
            if (spaceGrid == null || preview.Attacker == null || preview.Defender == null)
                return Vector2.right;
            Vector3 atkSpace = spaceGrid.GetWorldPosition(preview.Attacker.currentSpaceTileIndex);
            Vector3 defSpace = spaceGrid.GetWorldPosition(preview.Defender.currentSpaceTileIndex);
            Vector2 spaceDirection = new Vector2(defSpace.x - atkSpace.x, defSpace.z - atkSpace.z);
            return spaceDirection.sqrMagnitude < 0.0001f ? Vector2.right : spaceDirection.normalized;
        }

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

    private static void AssignReinforcementEntries(EngagementPreview preview)
    {
        if (preview?.Map == null)
            return;

        var tileSystem = preview.Theater == BattleTheater.DeepSpace ? null : (TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance);
        for (int groupIndex = 0; groupIndex < preview.Reinforcements.Count; groupIndex++)
        {
            var group = preview.Reinforcements[groupIndex];
            group.EntryCellIndex = -1;
            group.EntryCellIndices.Clear();
            int bestDistance = int.MaxValue;
            for (int cellIndex = 0; cellIndex < preview.Map.Cells.Count; cellIndex++)
            {
                var cell = preview.Map.Cells[cellIndex];
                if (!cell.IsReinforcementEntry || cell.DeploymentOwner != group.Side || !cell.Supports(group.Domain))
                    continue;
                group.EntryCellIndices.Add(cellIndex);

                int distance = tileSystem != null
                    ? tileSystem.GetWrappedHexDistance(group.OriginCampaignTile, cell.CampaignTileIndex)
                    : Mathf.Abs(group.OriginSpaceRegion - cell.CampaignTileIndex);
                if (distance < bestDistance || (distance == bestDistance && cellIndex < group.EntryCellIndex))
                {
                    bestDistance = distance;
                    group.EntryCellIndex = cellIndex;
                }
            }
            group.EntryCellIndices.Sort((a, b) => a.CompareTo(b));
        }
    }

    private static void AutoDeployUnits(BattleMap map, List<BattleUnitState> units, BattleOccupancy occupancy, BattleSide side, int maxInitial)
    {
        int deployed = 0;
        for (int i = 0; i < units.Count && deployed < maxInitial; i++)
        {
            var u = units[i];
            if (u.Side != side || u.IsReserve || u.IsEmbarked)
                continue;

            int slot = FindFreeDeploymentCell(map, occupancy, side, u);
            if (slot < 0)
            {
                u.IsReserve = true;
                u.HasEnteredBattle = false;
                continue;
            }

            occupancy.TryMove(u, slot, map);
            deployed++;
        }

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u.Side == side && u.CellIndex < 0 && !u.IsEmbarked)
                u.IsReserve = true;
                u.HasEnteredBattle = false;
            }
        }
    }

    private static int FindFreeDeploymentCell(BattleMap map, BattleOccupancy occupancy, BattleSide side, BattleUnitState unit)
    {
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var c = map.Cells[i];
            if (c.DeploymentOwner != side)
                continue;

            if (!c.Supports(unit.Domain))
                continue;

            if (!occupancy.IsOccupied(i, unit.Domain, unit.OccupancyBand))
                return i;
        }

        return -1;
    }

    public string CaptureStateJson()
    {
        if (IsBattleActive || pendingPreview != null || pendingResult != null)
            throw new InvalidOperationException("Active tactical battles cannot be saved.");

        return JsonUtility.ToJson(new BattleSaveMarker { hasActiveBattle = false });
    }

    public void RestoreStateJson(string json)
    {
        var marker = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<BattleSaveMarker>(json);
        if (marker != null && marker.hasActiveBattle)
        {
            Debug.LogError("[BattleManager] Refusing to discard an active tactical battle from a save file.");
            return;
        }

        ActiveBattleState = null;
        pendingPreview = null;
        pendingResult = null;
        commitments.Clear();
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
