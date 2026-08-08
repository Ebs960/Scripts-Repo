using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BattleManager : MonoBehaviour, ISaveGameParticipant
{
    [Serializable]
    private sealed class BattleSaveMarker
    {
        public int version = 4;
        public bool hasActiveBattle;
        public bool hasPreview;
        public bool hasResult;
        public int nextBattleId;
        public int interactionMode;
        public ActiveSessionSave active;
        public ResultSave result;
        public BattlePreviewSaveData preview;
    }

    [Serializable] private sealed class ActiveSessionSave
    {
        public int battleId, phase, activeSide, round, randomState;
        public int tacticalMode, selectedUnit, selectedCell, domainFilter;
        public bool attackerDeploymentConfirmed, defenderDeploymentConfirmed;
        public List<UnitSave> units = new();
        public List<string> actionLog = new();
        public List<BattleDetectionService.DetectionRecord> detection = new();
        public List<BattleCommandRecord> replay = new();
        public List<ReinforcementSave> reinforcements = new();
        public PlanSave aiPlan;
    }
    [Serializable] private sealed class UnitSave
    {
        public int id, runtimeId, side, health, cell, move, actions, reinforcementGroup, occupancyBand, depth, fuel, carrier, withdrawal, tacticalExit;
        public int startingTile, startingLayer, startingSlot;
        public string formation;
        public bool moved, acted, defending, waiting, waited, reserve, retreated, dead, attacked, entered, embarked, countered, revealed;
        public float commanderAttack, commanderDefense;
        public List<int> cargo = new(), ammo = new(), cooldowns = new();
        public List<int> retreatPath = new();
        public string retreatFailure;
        public List<StatusSave> statuses = new();
    }
    [Serializable] private sealed class StatusSave { public int type, rounds; public float magnitude; }
    [Serializable] private sealed class ReinforcementSave
    { public int id, availableRound, distance, lastAttempt; public bool eligible; public string eligibility, delay, entryDelay; }
    [Serializable] private sealed class PlanSave
    { public int side, theater, posture, objective, focus, round; public List<int> activationOrder = new(); }
    [Serializable] private sealed class ResultSave
    {
        public int battleId, winningSide, resolution, finalRound;
        public bool autoResolved, campaignApplied, playerInvolved;
        public List<ResultUnitSave> units = new();
        public List<PlacementFailureSave> placementFailures = new();
        public List<CommanderOutcomeSave> commanders = new();
    }
    [Serializable] private sealed class ResultUnitSave
    { public int runtimeId, side, health, withdrawal, tacticalExit, xp, carrier, tile, slot; public bool died, retreated, participated, embarked; public List<int> retreatPath = new(); public string retreatFailure; }
    [Serializable] private sealed class PlacementFailureSave
    { public int runtimeId, side, originalTile, requestedTile; public bool deepSpace; public string reason; }
    [Serializable] private sealed class CommanderOutcomeSave
    { public string assignment, formation; public int role, kind, character, xp, before, after; public bool participated, destroyed, retreated; }

    public static BattleManager Instance { get; private set; }

    [Header("Config")]
    [SerializeField] private BattleRuleset ruleset;

    public BattleState ActiveBattleState { get; private set; }
    public BattleSession ActiveBattle => ActiveBattleState?.Session;
    public bool IsBattleActive => ActiveBattleState != null;
    public EngagementPreview PendingPreview => pendingPreview;
    public BattleResult PendingResult => pendingResult;
    public BattleInputController TacticalInput => battleInput;
    public Camera TacticalCamera => battleCamera?.TacticalCamera;

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
    private BattleCameraController battleCamera;
    private BattleInputController battleInput;
    private BattleOverlayRenderer battleOverlays;

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
        var presenter = BattlePresenter.GetOrCreate(this);
        presenter.Bind(this);
        BattleResultUI.GetOrCreate(this).Bind(this);

        var presentationServices = new GameObject("Tactical Battle Presentation Services");
        presentationServices.transform.SetParent(transform, false);
        battleCamera = presentationServices.AddComponent<BattleCameraController>();
        battleInput = presentationServices.AddComponent<BattleInputController>();
        battleOverlays = presentationServices.AddComponent<BattleOverlayRenderer>();
        battleInput.Bind(this, battleCamera);
        presenter.CellClicked += battleInput.SelectCell;
        battleOverlays.Bind(this, presenter, battleInput);

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

        if (!IsHumanControlledSide(BattleSide.Attacker))
            state.Session.SetDeploymentConfirmed(BattleSide.Attacker, true);
        if (!IsHumanControlledSide(BattleSide.Defender))
            state.Session.SetDeploymentConfirmed(BattleSide.Defender, true);
        state.Session.SetDeploymentSide(IsHumanControlledSide(BattleSide.Attacker)
            ? BattleSide.Attacker : BattleSide.Defender);

        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.BattleDeployment);
        battleCamera?.FocusBattle(ActiveBattle.Map);
        BattlePreviewClosed?.Invoke();
        RaiseBattlePreviewClosed();
        BattleStarted?.Invoke(ActiveBattle);
        RaiseBattleStarted(ActiveBattle);

        battleInput?.SetActive(true);
        battleInput?.SetMode(BattleInteractionMode.Deployment);

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
            if (result != null) result.WasPlayerInvolved = IsPlayerInvolved(pendingPreview);
            resultApplier.Apply(result, pendingPreview);
            AwardCommanderExperience(result);
            BattleResolved?.Invoke(result);
            RaiseBattleResolved(result);
        }
        finally
        {
            commitments.ReleaseBattle(result != null ? result.BattleId : ActiveBattle.BattleId);
            ActiveBattleState = null;
            if (resolvingAiOnlyBattle) pendingPreview = null;
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
        pendingPreview = null;
        battleInput?.SetActive(false);
        battleOverlays?.Clear();
        battleCamera?.RestoreCampaignCamera();
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
        BattleSide side = ActiveBattle.ActiveSide;
        if (!ValidateDeployment(side, out reason)) return false;
        ActiveBattle.SetDeploymentConfirmed(side, true);

        BattleSide other = side == BattleSide.Attacker ? BattleSide.Defender : BattleSide.Attacker;
        if (!ActiveBattle.IsDeploymentConfirmed(other))
        {
            // Both sides are initially authority auto-deployed. AI confirmation is
            // immediate; a second human receives an independent deployment turn.
            if (IsHumanControlledSide(other))
            {
                ActiveBattle.SetDeploymentSide(other);
                battleInput?.SetMode(BattleInteractionMode.Deployment);
                NotifyBattleStateChanged();
                return true;
            }
            ActiveBattle.SetDeploymentConfirmed(other, true);
        }
        ActiveBattleState.TurnController.BeginRound(ActiveBattle);
        GameInteractionStateService.GetOrCreate().SetMode(GameInteractionMode.BattleActive);
        battleInput?.SetMode(BattleInteractionMode.Selection);
        ActiveBattleState.DetectionService.Update(ActiveBattle, BattleSide.Attacker);
        ActiveBattleState.DetectionService.Update(ActiveBattle, BattleSide.Defender);
        AdvanceManualFlow();
        NotifyBattleStateChanged();
        return true;
    }

    public bool ValidateDeployment(BattleSide side, out string reason)
    {
        reason = string.Empty;
        if (ActiveBattleState == null || ActiveBattle.Phase != BattlePhase.Deployment)
        { reason = "battle is not in deployment"; return false; }
        int available = 0, deployed = 0;
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
        {
            var unit = ActiveBattle.Units[i];
            if (unit == null || unit.Side != side || unit.IsDead || unit.IsEmbarked) continue;
            available++;
            if (unit.IsReserve) continue;
            var cell = ActiveBattle.Map.GetCell(unit.CellIndex);
            if (cell == null || cell.DeploymentOwner != side || !cell.Supports(unit.Domain))
            { reason = $"unit {unit.UnitId} is outside its legal deployment zone"; return false; }
            if (ActiveBattleState.Occupancy.GetOccupant(unit.CellIndex, unit.Domain, unit.OccupancyBand) != unit)
            { reason = $"unit {unit.UnitId} has invalid layered occupancy"; return false; }
            deployed++;
        }
        if (available > 0 && deployed == 0) { reason = "at least one available unit must be deployed"; return false; }
        if (deployed > ruleset.maxInitialUnitsPerSide) { reason = "deployment unit limit exceeded"; return false; }
        return true;
    }

    public bool ResetDeployment(BattleSide side, out string reason)
    {
        reason = string.Empty;
        if (ActiveBattleState == null || ActiveBattle.Phase != BattlePhase.Deployment || ActiveBattle.ActiveSide != side)
        { reason = "this side is not deploying"; return false; }
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
        {
            var unit = ActiveBattle.Units[i];
            if (unit != null && unit.Side == side && !unit.IsEmbarked) ActiveBattleState.Occupancy.Remove(unit);
        }
        int deployed = 0;
        for (int i = 0; i < ActiveBattle.Units.Count; i++)
        {
            var unit = ActiveBattle.Units[i];
            if (unit == null || unit.Side != side || unit.IsEmbarked) continue;
            int cell = deployed < ruleset.maxInitialUnitsPerSide ? FindFreeDeploymentCell(ActiveBattle.Map, ActiveBattleState.Occupancy, side, unit) : -1;
            unit.IsReserve = cell < 0; unit.HasEnteredBattle = cell >= 0;
            if (cell >= 0) { ActiveBattleState.Occupancy.TryMove(unit, cell, ActiveBattle.Map); deployed++; }
        }
        ActiveBattle.SetDeploymentConfirmed(side, false);
        NotifyBattleStateChanged();
        return true;
    }

    public int DeploymentUnitLimit => ruleset != null ? ruleset.maxInitialUnitsPerSide : 0;

    public BattleUnitState GetBattleUnit(int unitId) => FindUnit(unitId);
    public BattleDetectionLevel GetDetectionLevel(BattleSide observingSide, BattleUnitState target)
        => ActiveBattleState?.DetectionService?.GetLevel(observingSide,target)??BattleDetectionLevel.Undetected;
    public void AdjustTacticalCameraZoom(float direction) => battleCamera?.NudgeZoom(direction);

    public bool IsLegalCellForMode(int unitId, int cellIndex, BattleInteractionMode mode)
    {
        var unit = FindUnit(unitId);
        var cell = ActiveBattle?.Map?.GetCell(cellIndex);
        if (unit == null || cell == null) return false;
        if (mode == BattleInteractionMode.Deployment)
            return ActiveBattle.Phase == BattlePhase.Deployment && cell.DeploymentOwner == unit.Side
                && cell.Supports(unit.Domain) && ActiveBattleState.Occupancy.CanEnter(unit, cellIndex, ActiveBattle.Map);
        if (mode == BattleInteractionMode.Movement)
            return TryGetMovePath(unitId, cellIndex, out _);
        if (mode == BattleInteractionMode.Attack)
        {
            var target = GetUnitAtCell(cellIndex);
            return target != null && target.Side != unit.Side
                && ActiveBattleState.DetectionService.CanDirectlyTarget(unit.Side, target)
                && BattleTargetingService.FindWeaponIndex(unit, target, ActiveBattle.MapDistance(unit.CellIndex, cellIndex)) >= 0;
        }
        if (mode == BattleInteractionMode.Retreat)
            return cell.RetreatExitForSide == unit.Side && cell.Supports(unit.Domain);
        return cell.Supports(unit.Domain);
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

    public bool TryAttackUnitWithProfile(int unitId, int targetUnitId, BattleAttackProfile profile, out string reason)
    {
        var unit = FindUnit(unitId);
        var target = FindUnit(targetUnitId);
        if (unit == null || target == null || profile == null)
        { reason = "special attack profile or target not found"; return false; }

        return TrySubmitPlayerCommand(new BattleAttackCommand
        {
            UnitId = unitId,
            CommandType = profile.isRanged ? BattleCommandType.RangedAttack : BattleCommandType.MeleeAttack,
            TargetUnitId = targetUnitId,
            AttackFromCell = unit.CellIndex,
            IsRanged = profile.isRanged,
            IsSpecialAttack = true,
            AttackProfile = profile,
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

    public bool TryRetreatUnit(int unitId, int exitCell, out string reason)
    {
        reason = string.Empty;
        var unit = FindUnit(unitId);
        if (ActiveBattleState == null || unit == null
            || !ActiveBattleState.RetreatService.TryFindRoute(ActiveBattle, unit, ActiveBattleState.Occupancy, exitCell, out var route, out reason))
            return false;
        return TrySubmitPlayerCommand(new BattleRetreatCommand
        { UnitId = unitId, CommandType = BattleCommandType.Retreat, ExitCell = route[route.Count - 1], Route = route }, out reason);
    }

    public bool TryGetRetreatPath(int unitId, int requestedExit, out List<int> route, out string reason)
    {
        var unit = FindUnit(unitId);
        if (ActiveBattleState == null || unit == null) { route = null; reason = "unit not found"; return false; }
        return ActiveBattleState.RetreatService.TryFindRoute(ActiveBattle, unit, ActiveBattleState.Occupancy, requestedExit, out route, out reason);
    }

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
            var commanderAssignments = MilitaryCommanderAssignmentService.GetOrCreate().GetAssignments(units[i].Snapshot.FormationId);
            units[i].CommanderAttackMultiplier = MilitaryCommanderAssignmentService.GetOrCreate().GetAttackMultiplier(units[i].Snapshot.FormationId, units[i].Domain);
            units[i].CommanderDefenseMultiplier = MilitaryCommanderAssignmentService.GetOrCreate().GetDefenseMultiplier(units[i].Snapshot.FormationId, units[i].Domain);
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
                CommanderAssignmentId = commanderAssignments.Count > 0 ? commanderAssignments[0].AssignmentId : null,
                CampaignTurn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0,
            }))
            { commitments.ReleaseBattle(session.BattleId); return null; }
        }

        var movementService = new BattleMovementService(new BattlePathfinder(ruleset));
        var retreatService = new BattleRetreatService(new BattlePathfinder(ruleset));
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
            RetreatService = retreatService,
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
            int total=0, casualties=0; bool participated=false, retreated=false;
            for (int u=0;u<ActiveBattle.Units.Count;u++)
            {
                var member=ActiveBattle.Units[u]; if (member?.Snapshot?.FormationId!=formationId) continue;
                total++; if (member.IsDead || member.CurrentHealth<=0) casualties++; participated|=member.HasEnteredBattle; retreated|=member.HasRetreated;
            }
            var assignmentSnapshot=new List<(MilitaryCommanderAssignment assignment, BattleCommanderStatus before)>();
            foreach (var assignment in commanderService.GetAssignments(formationId)) assignmentSnapshot.Add((assignment,assignment.Status));
            commanderService.AwardBattleExperience(formationId, experience);
            int enemyCivId = -1;
            var enemy = unit.Side == BattleSide.Attacker ? pendingPreview?.Defender?.owner : pendingPreview?.Attacker?.owner;
            if (enemy != null && CivilizationManager.Instance != null)
                enemyCivId = CivilizationManager.Instance.GetCivIndex(enemy);
            int fateRoll=Mathf.FloorToInt(ActiveBattle.Random.NextUnitFloat()*int.MaxValue);
            commanderService.ResolveBattleFate(formationId, total>0?casualties/(float)total:0f, retreated,
                unit.Side!=result.WinningSide, participated, enemyCivId, fateRoll);
            foreach (var entry in assignmentSnapshot) result.CommanderOutcomes.Add(new BattleCommanderOutcome {
                AssignmentId=entry.assignment.AssignmentId, FormationId=formationId, Role=entry.assignment.Role,
                CharacterKind=entry.assignment.CharacterKind, CharacterId=entry.assignment.CharacterId, ExperienceGained=experience,
                Participated=participated, FormationDestroyed=total>0&&casualties==total, FormationRetreated=retreated,
                StatusBefore=entry.before, StatusAfter=entry.assignment.Status });
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
                WithdrawalTacticalExit = u.WithdrawalTacticalExit,
                RetreatPath = new List<int>(u.RetreatPath),
                RetreatFailureReason = u.RetreatFailureReason,
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
            {
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
        var save = new BattleSaveMarker
        {
            hasActiveBattle = IsBattleActive, hasPreview = pendingPreview != null, hasResult = pendingResult != null,
            nextBattleId = nextBattleId, interactionMode = (int)GameInteractionStateService.GetOrCreate().Mode,
        };
        if (pendingPreview != null) save.preview = BattlePreviewSaveCodec.Capture(pendingPreview);
        if (ActiveBattleState != null)
        {
            var s = ActiveBattle;
            save.active = new ActiveSessionSave
            {
                battleId = s.BattleId, phase = (int)s.Phase, activeSide = (int)s.ActiveSide, round = s.CurrentRound,
                randomState = unchecked((int)s.Random.CaptureState()),
                attackerDeploymentConfirmed = s.IsDeploymentConfirmed(BattleSide.Attacker),
                defenderDeploymentConfirmed = s.IsDeploymentConfirmed(BattleSide.Defender),
                actionLog = new List<string>(ActiveBattleState.ActionLog),
                detection = ActiveBattleState.DetectionService.CaptureState(),
                replay = new List<BattleCommandRecord>(ActiveBattleState.ReplayLog.Commands),
                tacticalMode=(int)(battleInput?.Mode??BattleInteractionMode.Selection), selectedUnit=battleInput?.SelectedUnitId??-1,
                selectedCell=battleInput?.SelectedCellIndex??-1, domainFilter=(int)(battleInput?.DomainFilter??BattleDomain.Land),
            };
            foreach (var u in s.Units)
            {
                var d = new UnitSave { id=u.UnitId, runtimeId=u.Snapshot.CampaignRuntimeId, side=(int)u.Side, health=u.CurrentHealth,
                    formation=u.Snapshot.FormationId, startingTile=u.Snapshot.StartingCampaignTile, startingLayer=(int)u.Snapshot.StartingLayer, startingSlot=u.Snapshot.StartingStackSlot,
                    cell=u.CellIndex, move=u.CurrentMovePoints, actions=u.CurrentActionPoints, reinforcementGroup=u.ReinforcementGroupId,
                    occupancyBand=u.OccupancyBand, depth=(int)u.DepthBand, fuel=u.FuelOrEndurance, carrier=u.CarrierOrTransportBattleUnitId,
                    withdrawal=u.WithdrawalCampaignTile, tacticalExit=u.WithdrawalTacticalExit, moved=u.HasMoved, acted=u.HasActed, defending=u.IsDefending, waiting=u.IsWaiting,
                    waited=u.HasWaitedThisTurn, reserve=u.IsReserve, retreated=u.HasRetreated, dead=u.IsDead, attacked=u.HasAttackedThisTurn,
                    entered=u.HasEnteredBattle, embarked=u.IsEmbarked, countered=u.CounterAttackedThisActivation, revealed=u.RevealedByAttack,
                    commanderAttack=u.CommanderAttackMultiplier, commanderDefense=u.CommanderDefenseMultiplier };
                d.cargo.AddRange(u.EmbarkedBattleUnitIds); d.ammo.AddRange(u.WeaponAmmo); d.cooldowns.AddRange(u.WeaponCooldowns);
                d.retreatPath.AddRange(u.RetreatPath); d.retreatFailure = u.RetreatFailureReason;
                foreach (var status in u.StatusEffects) d.statuses.Add(new StatusSave { type=(int)status.Type, rounds=status.RemainingRounds, magnitude=status.Magnitude });
                save.active.units.Add(d);
            }
            foreach (var g in s.Reinforcements) save.active.reinforcements.Add(new ReinforcementSave { id=g.ReinforcementGroupId,
                availableRound=g.AvailableFromRound, distance=g.StrategicDistance, lastAttempt=g.LastEntryAttemptRound, eligible=g.IsEligible,
                eligibility=g.EligibilityReason, delay=g.DelayReason, entryDelay=g.LastEntryDelayReason });
            if (ActiveBattleState.AiController.CurrentPlan != null)
            {
                var p=ActiveBattleState.AiController.CurrentPlan;
                save.active.aiPlan=new PlanSave { side=(int)p.Side, theater=(int)p.Theater, posture=(int)p.Posture,
                    objective=p.ObjectiveCell, focus=p.FocusTargetUnitId, round=p.BuiltRound, activationOrder=new List<int>(p.ActivationOrder) };
            }
        }
        if (pendingResult != null) save.result = CaptureResult(pendingResult);
        return JsonUtility.ToJson(save);
    }

    public void RestoreStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Battle save data is empty.", nameof(json));
        BattleSaveMarker marker;
        try { marker = JsonUtility.FromJson<BattleSaveMarker>(json); }
        catch (Exception ex) { throw new InvalidOperationException("Battle save data is corrupt.", ex); }
        if (marker == null || marker.version < 1 || marker.version > 4)
            throw new InvalidOperationException($"Unsupported tactical battle save version {marker?.version ?? 0}.");
        int restoredNextBattleId = Mathf.Max(1, marker.nextBattleId);
        pendingPreview = marker.hasPreview ? BattlePreviewSaveCodec.Restore(marker.preview) : null;
        if (marker.hasActiveBattle)
        {
            if (marker.active == null) throw new InvalidOperationException("Active battle payload is missing.");
            if (ActiveBattleState == null)
            {
                if (pendingPreview == null) throw new InvalidOperationException("Cannot restore active battle: campaign participant preview is unavailable.");
                nextBattleId = marker.active.battleId;
                ActiveBattleState = BuildBattleState(pendingPreview);
                if (ActiveBattleState == null || ActiveBattle.BattleId != marker.active.battleId)
                    throw new InvalidOperationException("Active battle services could not be reconstructed.");
            }
            RestoreActive(marker.active);
            battleCamera?.FocusBattle(ActiveBattle.Map); battleInput?.SetActive(true);
            battleInput?.RestorePresentationState((BattleInteractionMode)marker.active.tacticalMode, marker.active.selectedUnit,
                marker.active.selectedCell, (BattleDomain)marker.active.domainFilter);
            BattleStarted?.Invoke(ActiveBattle);
        }
        else ActiveBattleState = null;
        nextBattleId = restoredNextBattleId;
        pendingResult = marker.hasResult ? RestoreResult(marker.result) : null;
        GameInteractionStateService.GetOrCreate().SetMode((GameInteractionMode)marker.interactionMode);
        if (!marker.hasActiveBattle && pendingResult != null)
        {
            if (pendingPreview?.Map != null) battleCamera?.FocusBattle(pendingPreview.Map);
            battleInput?.SetActive(false);
            BattleResultUI.GetOrCreate(this).PresentRestored(pendingResult);
        }
        else if (!marker.hasActiveBattle && pendingPreview != null) BattlePreviewUI.GetOrCreate(this).PresentRestored(pendingPreview);
        NotifyBattleStateChanged();
    }

    private void RestoreActive(ActiveSessionSave saved)
    {
        if (ActiveBattle == null || ActiveBattle.Units.Count != saved.units.Count)
            throw new InvalidOperationException("Active battle participant set does not match the save.");
        var byRuntime = new Dictionary<int, BattleUnitState>();
        foreach (var u in ActiveBattle.Units) byRuntime[u.Snapshot.CampaignRuntimeId] = u;
        foreach (var d in saved.units)
        {
            if (!byRuntime.TryGetValue(d.runtimeId, out var u))
            {
                u=null;
                foreach (var candidate in ActiveBattle.Units)
                    if (candidate?.Snapshot?.FormationId==d.formation && candidate.Snapshot.StartingCampaignTile==d.startingTile
                        && (int)candidate.Snapshot.StartingLayer==d.startingLayer && candidate.Snapshot.StartingStackSlot==d.startingSlot)
                    { if (u!=null) throw new InvalidOperationException($"Stable tactical identity for formation {d.formation} is ambiguous."); u=candidate; }
                if (u==null) throw new InvalidOperationException($"Campaign unit {d.runtimeId} cannot be rebound.");
            }
            ActiveBattleState.Occupancy.Remove(u);
            u.CurrentHealth=d.health; u.CurrentMovePoints=d.move; u.CurrentActionPoints=d.actions; u.ReinforcementGroupId=d.reinforcementGroup;
            u.OccupancyBand=d.occupancyBand; u.DepthBand=(BattleDepthBand)d.depth; u.FuelOrEndurance=d.fuel; u.CarrierOrTransportBattleUnitId=d.carrier;
            u.WithdrawalCampaignTile=d.withdrawal; u.WithdrawalTacticalExit=d.tacticalExit; u.HasMoved=d.moved; u.HasActed=d.acted; u.IsDefending=d.defending; u.IsWaiting=d.waiting;
            u.HasWaitedThisTurn=d.waited; u.IsReserve=d.reserve; u.HasRetreated=d.retreated; u.IsDead=d.dead; u.HasAttackedThisTurn=d.attacked;
            u.HasEnteredBattle=d.entered; u.IsEmbarked=d.embarked; u.CounterAttackedThisActivation=d.countered; u.RevealedByAttack=d.revealed;
            u.CommanderAttackMultiplier=d.commanderAttack; u.CommanderDefenseMultiplier=d.commanderDefense;
            u.EmbarkedBattleUnitIds.Clear(); if (d.cargo != null) u.EmbarkedBattleUnitIds.AddRange(d.cargo);
            u.WeaponAmmo.Clear(); if (d.ammo != null) u.WeaponAmmo.AddRange(d.ammo);
            u.WeaponCooldowns.Clear(); if (d.cooldowns != null) u.WeaponCooldowns.AddRange(d.cooldowns); u.CellIndex=-1;
            u.RetreatPath.Clear(); if (d.retreatPath != null) u.RetreatPath.AddRange(d.retreatPath); u.RetreatFailureReason=d.retreatFailure;
            u.StatusEffects.Clear(); if (d.statuses != null) foreach (var status in d.statuses) u.StatusEffects.Add(new BattleStatusEffect { Type=(BattleStatusEffectType)status.type, RemainingRounds=status.rounds, Magnitude=status.magnitude });
            if (d.cell >= 0 && !u.IsReserve && !u.IsEmbarked && !u.IsDead && !ActiveBattleState.Occupancy.TryMove(u, d.cell, ActiveBattle.Map))
                throw new InvalidOperationException($"Saved occupancy for tactical unit {u.UnitId} is invalid.");
        }
        ActiveBattle.RestoreProgress((BattlePhase)saved.phase, (BattleSide)saved.activeSide, saved.round);
        ActiveBattle.SetDeploymentConfirmed(BattleSide.Attacker, saved.attackerDeploymentConfirmed);
        ActiveBattle.SetDeploymentConfirmed(BattleSide.Defender, saved.defenderDeploymentConfirmed);
        ActiveBattle.Random.RestoreState(unchecked((uint)saved.randomState));
        ActiveBattleState.ActionLog.Clear(); if (saved.actionLog != null) ActiveBattleState.ActionLog.AddRange(saved.actionLog);
        ActiveBattleState.DetectionService.RestoreState(saved.detection);
        ActiveBattleState.ReplayLog.Commands.Clear(); if (saved.replay != null) ActiveBattleState.ReplayLog.Commands.AddRange(saved.replay);
        if (saved.reinforcements != null) foreach (var state in saved.reinforcements)
            foreach (var group in ActiveBattle.Reinforcements)
                if (group.ReinforcementGroupId == state.id) { group.AvailableFromRound=state.availableRound; group.StrategicDistance=state.distance;
                    group.LastEntryAttemptRound=state.lastAttempt; group.IsEligible=state.eligible; group.EligibilityReason=state.eligibility;
                    group.DelayReason=state.delay; group.LastEntryDelayReason=state.entryDelay; break; }
        if (saved.aiPlan != null)
        {
            var p=new BattleTacticalPlan { Side=(BattleSide)saved.aiPlan.side, Theater=(BattleTheater)saved.aiPlan.theater,
                Posture=(BattlePlanPosture)saved.aiPlan.posture, ObjectiveCell=saved.aiPlan.objective,
                FocusTargetUnitId=saved.aiPlan.focus, BuiltRound=saved.aiPlan.round };
            if (saved.aiPlan.activationOrder != null) p.ActivationOrder.AddRange(saved.aiPlan.activationOrder);
            ActiveBattleState.AiController.RestorePlan(p);
        }
    }

    private static ResultSave CaptureResult(BattleResult result)
    {
        var s = new ResultSave { battleId=result.BattleId, winningSide=(int)result.WinningSide, resolution=(int)result.ResolutionType, finalRound=result.FinalRound, autoResolved=result.WasAutoResolved, campaignApplied=result.CampaignApplied, playerInvolved=result.WasPlayerInvolved };
        foreach (var u in result.UnitOutcomes) s.units.Add(new ResultUnitSave { runtimeId=u.CampaignRuntimeId, side=(int)u.Side, health=u.FinalHealth,
            withdrawal=u.WithdrawalCampaignTile, xp=u.ExperienceGained, carrier=u.CarrierOrTransportCampaignRuntimeId, tile=u.SuggestedCampaignTile,
            tacticalExit=u.WithdrawalTacticalExit, slot=u.SuggestedStackSlot, died=u.Died, retreated=u.Retreated, participated=u.Participated, embarked=u.IsEmbarked,
            retreatPath=new List<int>(u.RetreatPath), retreatFailure=u.RetreatFailureReason });
        foreach (var f in result.PlacementFailures) s.placementFailures.Add(new PlacementFailureSave { runtimeId=f.CampaignRuntimeId,
            side=(int)f.Side, originalTile=f.OriginalTile, requestedTile=f.RequestedTile, deepSpace=f.IsDeepSpace, reason=f.Reason });
        foreach (var c in result.CommanderOutcomes) s.commanders.Add(new CommanderOutcomeSave { assignment=c.AssignmentId,
            formation=c.FormationId, role=(int)c.Role, kind=(int)c.CharacterKind, character=c.CharacterId, xp=c.ExperienceGained,
            before=(int)c.StatusBefore, after=(int)c.StatusAfter, participated=c.Participated, destroyed=c.FormationDestroyed, retreated=c.FormationRetreated });
        return s;
    }
    private static BattleResult RestoreResult(ResultSave saved)
    {
        if (saved == null) throw new InvalidOperationException("Battle result payload is missing.");
        var r = new BattleResult { BattleId=saved.battleId, WinningSide=(BattleSide)saved.winningSide, ResolutionType=(BattleResolutionType)saved.resolution, FinalRound=saved.finalRound, WasAutoResolved=saved.autoResolved, CampaignApplied=saved.campaignApplied, WasPlayerInvolved=saved.playerInvolved };
        foreach (var u in saved.units) r.UnitOutcomes.Add(new BattleUnitOutcome { CampaignRuntimeId=u.runtimeId, Side=(BattleSide)u.side, FinalHealth=u.health,
            WithdrawalCampaignTile=u.withdrawal, ExperienceGained=u.xp, CarrierOrTransportCampaignRuntimeId=u.carrier, SuggestedCampaignTile=u.tile,
            SuggestedStackSlot=u.slot, WithdrawalTacticalExit=u.tacticalExit, Died=u.died, Retreated=u.retreated, Participated=u.participated, IsEmbarked=u.embarked,
            RetreatPath=u.retreatPath != null ? new List<int>(u.retreatPath) : new List<int>(), RetreatFailureReason=u.retreatFailure });
        if (saved.placementFailures != null) foreach (var f in saved.placementFailures) r.PlacementFailures.Add(new BattlePlacementFailure {
            CampaignRuntimeId=f.runtimeId, Side=(BattleSide)f.side, OriginalTile=f.originalTile, RequestedTile=f.requestedTile,
            IsDeepSpace=f.deepSpace, Reason=f.reason });
        if (saved.commanders != null) foreach (var c in saved.commanders) r.CommanderOutcomes.Add(new BattleCommanderOutcome {
            AssignmentId=c.assignment, FormationId=c.formation, Role=(CommandRole)c.role, CharacterKind=(CommanderCharacterKind)c.kind,
            CharacterId=c.character, ExperienceGained=c.xp, StatusBefore=(BattleCommanderStatus)c.before, StatusAfter=(BattleCommanderStatus)c.after,
            Participated=c.participated, FormationDestroyed=c.destroyed, FormationRetreated=c.retreated });
        return r;
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
