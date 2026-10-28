using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Authored tactical battle HUD (status bar, command dock, roster, deployment). It binds live battle state to
/// serialized prefab elements and forwards commands to BattleManager. BattleInputController owns selection.
/// </summary>
public sealed class BattleHUD : MonoBehaviour
{
    private enum AttackMode { Melee, Ranged, Special }

    [Flags]
    private enum AdvancedActions
    {
        None = 0, Embark = 1, Disembark = 2, Launch = 4, Recover = 8, Dive = 16, Shallow = 32, Scan = 64,
    }

    private const string CanvasResource = BattleUIUtil.ResourceFolder + "TacticalBattleCanvas";

    [Header("Status Bar")]
    [SerializeField] private BattleStatusBarView statusBar;
    [SerializeField] private GameObject objectiveIndicator;
    [SerializeField] private TextMeshProUGUI objectiveText;

    [Header("Camera Controls")]
    [SerializeField] private Button layerButton;
    [SerializeField] private TextMeshProUGUI layerLabel;
    [SerializeField] private Button zoomInButton;
    [SerializeField] private Button zoomOutButton;

    [Header("Dock")]
    [SerializeField] private RectTransform dock;
    [SerializeField] private Image unitIcon;
    [SerializeField] private TextMeshProUGUI unitName;
    [SerializeField] private TextMeshProUGUI unitStats;
    [SerializeField] private TextMeshProUGUI unitStatus;
    [SerializeField] private RectTransform unitHealthFill;
    [SerializeField] private Button previousUnitButton;
    [SerializeField] private Button nextUnitButton;

    [Header("Combat Controls")]
    [SerializeField] private GameObject combatControls;
    [SerializeField] private Button moveButton;
    [SerializeField] private Button attackButton;
    [SerializeField] private Button defendButton;
    [SerializeField] private Button waitButton;
    [SerializeField] private Button retreatButton;
    [SerializeField] private TextMeshProUGUI contextHint;
    [SerializeField] private GameObject attackModeSelector;
    [SerializeField] private Button meleeModeButton;
    [SerializeField] private Button rangedModeButton;
    [SerializeField] private Button specialModeButton;
    [SerializeField] private Button weaponButton;
    [SerializeField] private TextMeshProUGUI weaponLabel;
    [SerializeField] private Button moreActionsButton;

    [Header("Advanced Actions")]
    [SerializeField] private GameObject advancedPopover;
    [SerializeField] private Button embarkButton;
    [SerializeField] private Button disembarkButton;
    [SerializeField] private Button launchButton;
    [SerializeField] private Button recoverButton;
    [SerializeField] private Button diveButton;
    [SerializeField] private Button shallowButton;
    [SerializeField] private Button activeScanButton;

    [Header("Roster")]
    [SerializeField] private ScrollRect rosterScroll;
    [SerializeField] private RectTransform rosterContent;
    [SerializeField] private TacticalRosterCard rosterCardPrefab;

    [Header("Deployment Controls")]
    [SerializeField] private GameObject deploymentControls;
    [SerializeField] private Button deployedTab;
    [SerializeField] private TextMeshProUGUI deployedTabLabel;
    [SerializeField] private Button reservesTab;
    [SerializeField] private TextMeshProUGUI reservesTabLabel;
    [SerializeField] private TextMeshProUGUI deploymentCount;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button swapReserveButton;
    [SerializeField] private TextMeshProUGUI validationMessage;
    [SerializeField] private GameObject deploymentConfirmControls;
    [SerializeField] private TextMeshProUGUI deployingSideText;
    [SerializeField] private Button confirmDeploymentButton;

    [Header("Side Turn Controls")]
    [SerializeField] private GameObject sideTurnControls;
    [SerializeField] private TextMeshProUGUI readyUnitCount;
    [SerializeField] private Button endSideButton;

    [Header("Overlays")]
    [SerializeField] private BattleTooltipView tooltip;
    [SerializeField] private BattleCombatPreviewView combatPreview;
    [SerializeField] private BattleNotificationView notifications;

    private static readonly Color ReadyColor = new(0.4f, 0.85f, 0.45f);
    private static readonly Color WaitingColor = new(0.95f, 0.75f, 0.3f);
    private static readonly Color MutedColor = new(0.62f, 0.66f, 0.74f);
    private static readonly Color WarningColor = new(0.95f, 0.45f, 0.4f);

    private readonly List<Button> wiredButtons = new();
    private readonly Dictionary<int, TacticalRosterCard> cards = new();
    private readonly List<BattleUnitState> deployedUnits = new();
    private readonly List<BattleUnitState> reserveUnits = new();
    private readonly List<int> staleCards = new();
    private readonly HashSet<int> liveCards = new();

    private BattleManager manager;
    private BattlePresenter presenter;
    private BattleInputController input;
    private BattleDeploymentPanel deployment;
    private GameInteractionStateService interaction;
    private bool authored;
    private bool refreshing;
    private bool pendingRefresh;
    private bool showReserves;
    private bool advancedOpen;
    private int selectedReserveId = -1;
    private int lastRenderedUnitId = -1;
    private int lastNoticeRound = -1;
    private BattlePhase lastNoticePhase = BattlePhase.None;
    private string transientMessage;
    private List<BattleUnitState> activeRoster = new();

    public static BattleHUD GetOrCreate(BattleManager battleManager)
    {
        var existing = battleManager.GetComponentInChildren<BattleHUD>(true);
        if (existing != null) return existing;

        var prefab = Resources.Load<BattleHUD>(CanvasResource);
        if (prefab == null)
        {
            Debug.LogError($"[BattleHUD] Resources/{CanvasResource}.prefab is missing. Run Tools > Battle UI > Build Tactical Battle Prefabs.");
            return battleManager.gameObject.AddComponent<BattleHUD>();
        }

        var instance = Instantiate(prefab, battleManager.transform, false);
        instance.name = "Tactical Battle Canvas";
        return instance;
    }

    public void Bind(BattleManager battleManager)
    {
        if (manager == battleManager) return;
        if (statusBar == null)
        {
            Debug.LogError("[BattleHUD] Prefab references are not assigned; tactical HUD disabled.", this);
            return;
        }

        authored = true;
        manager = battleManager;
        WireControls();
        statusBar.Initialize(tooltip);

        presenter = BattlePresenter.GetOrCreate(manager);
        presenter.CellPointerEntered += OnCellPointerEntered;
        presenter.CellPointerExited += OnCellPointerExited;
        presenter.PresentationQueueDrained += OnPresentationQueueDrained;
        manager.BattleStarted += OnBattleStarted;
        manager.BattleStateChanged += OnBattleStateChanged;
        manager.BattlePreviewClosed += OnBattlePreviewClosed;

        deployment = GetComponent<BattleDeploymentPanel>();
        if (deployment == null) deployment = gameObject.AddComponent<BattleDeploymentPanel>();
        deployment.Changed += OnDeploymentChanged;

        interaction = GameInteractionStateService.GetOrCreate();
        interaction.OnInteractionModeChanged += OnInteractionModeChanged;
        ApplyCampaignSuppression(interaction.Mode);

        SetVisible(manager.ActiveBattle != null);
        if (manager.ActiveBattle != null) Refresh();
    }

    private void OnDestroy()
    {
        if (!authored) return;
        if (manager != null)
        {
            manager.BattleStarted -= OnBattleStarted;
            manager.BattleStateChanged -= OnBattleStateChanged;
            manager.BattlePreviewClosed -= OnBattlePreviewClosed;
        }
        if (presenter != null)
        {
            presenter.CellPointerEntered -= OnCellPointerEntered;
            presenter.CellPointerExited -= OnCellPointerExited;
            presenter.PresentationQueueDrained -= OnPresentationQueueDrained;
        }
        if (input != null) input.SelectionChanged -= OnSelectionChanged;
        if (input != null) input.RequestRejected -= OnRequestRejected;
        if (deployment != null) deployment.Changed -= OnDeploymentChanged;
        if (interaction != null) interaction.OnInteractionModeChanged -= OnInteractionModeChanged;
        foreach (var card in cards.Values)
            if (card != null) card.Clicked -= OnRosterCardClicked;
        for (int i = 0; i < wiredButtons.Count; i++)
            if (wiredButtons[i] != null) wiredButtons[i].onClick.RemoveAllListeners();
    }

    // The input controller is created after the HUD in BattleManager.Awake, so it is attached lazily and once.
    private void EnsureInput()
    {
        var current = manager != null ? manager.TacticalInput : null;
        if (current == null || current == input) return;
        if (input != null)
        {
            input.SelectionChanged -= OnSelectionChanged;
            input.RequestRejected -= OnRequestRejected;
        }
        input = current;
        input.SelectionChanged += OnSelectionChanged;
        input.RequestRejected += OnRequestRejected;
    }

    private void Wire(Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.AddListener(action);
        wiredButtons.Add(button);
    }

    private void WireControls()
    {
        Wire(layerButton, CycleLayer);
        Wire(zoomInButton, () => presenter?.AdjustZoom(0.1f));
        Wire(zoomOutButton, () => presenter?.AdjustZoom(-0.1f));
        Wire(previousUnitButton, () => CycleUnit(-1));
        Wire(nextUnitButton, () => CycleUnit(1));

        Wire(moveButton, () => SetMode(BattleInteractionMode.Movement));
        Wire(attackButton, () => SetMode(BattleInteractionMode.Attack));
        Wire(retreatButton, () => SetMode(BattleInteractionMode.Retreat));
        Wire(defendButton, () => Submit(manager.TryDefendUnit(input.SelectedUnitId, out string reason), reason));
        Wire(waitButton, () => Submit(manager.TryWaitUnit(input.SelectedUnitId, out string reason), reason));
        Wire(endSideButton, EndSide);

        Wire(meleeModeButton, () => SetAttackMode(AttackMode.Melee));
        Wire(rangedModeButton, () => SetAttackMode(AttackMode.Ranged));
        Wire(specialModeButton, () => SetAttackMode(AttackMode.Special));
        Wire(weaponButton, CycleWeapon);

        Wire(moreActionsButton, () => { advancedOpen = !advancedOpen; Refresh(); });
        Wire(embarkButton, () => BeginAdvanced(BattleInteractionMode.Embark));
        Wire(disembarkButton, () => BeginAdvanced(BattleInteractionMode.Disembark));
        Wire(launchButton, () => BeginAdvanced(BattleInteractionMode.Launch));
        Wire(recoverButton, () => BeginAdvanced(BattleInteractionMode.Recovery));
        Wire(diveButton, () => { advancedOpen = false; Submit(manager.TryChangeDepth(input.SelectedUnitId, BattleDepthBand.Deep, out string reason), reason); });
        Wire(shallowButton, () => { advancedOpen = false; Submit(manager.TryChangeDepth(input.SelectedUnitId, BattleDepthBand.Shallow, out string reason), reason); });
        Wire(activeScanButton, () => { advancedOpen = false; Submit(manager.TryActiveDetection(input.SelectedUnitId, out string reason), reason); });

        Wire(deployedTab, () => SetReservesTab(false));
        Wire(reservesTab, () => SetReservesTab(true));
        Wire(resetButton, ResetDeployment);
        Wire(swapReserveButton, SwapReserve);
        Wire(confirmDeploymentButton, ConfirmDeployment);

        SetTooltip(layerButton, "Layer: Cycle the visible battlefield domain.");
        SetTooltip(zoomInButton, "Zoom In: Move the tactical camera closer.");
        SetTooltip(zoomOutButton, "Zoom Out: Move the tactical camera farther away.");
        SetTooltip(previousUnitButton, "Previous Unit: Select the previous unit in the roster.");
        SetTooltip(nextUnitButton, "Next Unit: Select the next unit in the roster.");
        SetTooltip(deployedTab, "Deployed Units: Show deployed units.");
        SetTooltip(meleeModeButton, "Melee: Use a melee attack profile.");
        SetTooltip(rangedModeButton, "Ranged: Use a ranged attack profile.");
        SetTooltip(specialModeButton, "Special: Use this unit's special attack.");
        SetTooltip(weaponButton, "Weapon: Cycle this unit's available weapons.");
    }

    // ---------------------------------------------------------------- events

    private void OnBattleStarted(BattleSession session)
    {
        EnsureInput();
        showReserves = false;
        selectedReserveId = -1;
        advancedOpen = false;
        transientMessage = null;
        lastRenderedUnitId = -1;
        lastNoticeRound = -1;
        lastNoticePhase = BattlePhase.None;
        deployment.Bind(manager, session);
        if (interaction != null) ApplyCampaignSuppression(interaction.Mode);
        Refresh();
    }

    private void OnBattleStateChanged(BattleSession session)
    {
        transientMessage = null;
        combatPreview.Hide();
        Refresh();
    }

    private void OnBattlePreviewClosed() => Refresh();
    private void OnPresentationQueueDrained() => Refresh();
    private void OnSelectionChanged() => Refresh();
    private void OnDeploymentChanged() => Refresh();

    private void OnRequestRejected(string reason)
    {
        transientMessage = reason;
        notifications.Post(reason);
    }

    private void OnInteractionModeChanged(GameInteractionMode mode)
    {
        ApplyCampaignSuppression(mode);
        Refresh();
    }

    private static void ApplyCampaignSuppression(GameInteractionMode mode)
    {
        bool tactical = mode == GameInteractionMode.BattleDeployment || mode == GameInteractionMode.BattleActive
            || mode == GameInteractionMode.BattleResult;
        if (UIManager.Instance != null) UIManager.Instance.SetTacticalBattleHudSuppressed(tactical);
    }

    private void SetVisible(bool visible)
    {
        if (gameObject.activeSelf == visible) return;
        gameObject.SetActive(visible);
        if (!visible)
        {
            advancedOpen = false;
            combatPreview.Hide();
            tooltip.Hide();
            notifications.Clear();
        }
    }

    public BattleTooltipView CreateTooltipOverlay(RectTransform canvasRoot)
        => tooltip != null ? tooltip.CreateOverlay(canvasRoot) : null;

    // --------------------------------------------------------------- refresh

    private void Refresh()
    {
        if (!authored) return;
        if (refreshing) { pendingRefresh = true; return; }
        refreshing = true;
        try
        {
            for (int pass = 0; pass < 4; pass++)
            {
                pendingRefresh = false;
                RefreshState();
                if (!pendingRefresh) break;
            }
        }
        finally { refreshing = false; }
    }

    private bool IsHuman(BattleSide side) => manager.IsHumanControlledSide(side);

    private void RefreshState()
    {
        EnsureInput();
        var session = manager.ActiveBattle;
        if (session == null || input == null || session.Phase == BattlePhase.Resolved || session.Phase == BattlePhase.None)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        bool deploying = session.Phase == BattlePhase.Deployment;
        BattleSide side = deploying || IsHuman(session.ActiveSide) ? session.ActiveSide : BattleUIUtil.Opposite(session.ActiveSide);
        if (!deploying) showReserves = false;
        // Escape cancels back to Selection, which would leave deployment hexes unusable.
        if (deploying && input.Mode != BattleInteractionMode.Deployment) input.SetMode(BattleInteractionMode.Deployment);

        BuildRosterLists(session, side, deploying);
        if (deploying && showReserves && reserveUnits.Count == 0) { showReserves = false; activeRoster = deployedUnits; }
        EnsureValidSelection(side);

        var selected = manager.GetBattleUnit(input.SelectedUnitId);
        bool unitChanged = input.SelectedUnitId != lastRenderedUnitId;
        if (unitChanged)
        {
            lastRenderedUnitId = input.SelectedUnitId;
            advancedOpen = false;
            transientMessage = null;
            combatPreview.Hide();
            if (deployment != null && !string.IsNullOrEmpty(deployment.ValidationMessage)) deployment.CancelAction();
        }
        NormalizeAttackSelection(selected, unitChanged);

        var displayed = selected;
        bool displayingReserve = false;
        if (deploying && showReserves)
        {
            var reserve = FindIn(reserveUnits, selectedReserveId);
            if (reserve == null && reserveUnits.Count > 0) { reserve = reserveUnits[0]; selectedReserveId = reserve.UnitId; }
            if (reserve != null) { displayed = reserve; displayingReserve = true; }
        }

        UpdateStatusBar(session);
        UpdateObjective(session);
        UpdateCameraControls();
        UpdateSelectedPanel(displayed, displayingReserve);
        combatControls.SetActive(!deploying);
        sideTurnControls.SetActive(!deploying);
        deploymentControls.SetActive(deploying);
        deploymentConfirmControls.SetActive(deploying);
        if (deploying) UpdateDeploymentControls(session, side, selected);
        else
        {
            UpdateCombatControls(session, selected);
            UpdateSideTurnControls(session);
            PostTurnNotices(session);
        }
        UpdateRoster(session, side, deploying, unitChanged);
    }

    private void BuildRosterLists(BattleSession session, BattleSide side, bool deploying)
    {
        deployedUnits.Clear();
        reserveUnits.Clear();
        for (int i = 0; i < session.Units.Count; i++)
        {
            var unit = session.Units[i];
            if (unit != null && unit.Side == side && unit.IsAliveAndActive) deployedUnits.Add(unit);
        }
        if (deploying) reserveUnits.AddRange(manager.GetDeploymentReserves(side));
        activeRoster = deploying && showReserves ? reserveUnits : deployedUnits;
    }

    private static BattleUnitState FindIn(List<BattleUnitState> units, int unitId)
    {
        for (int i = 0; i < units.Count; i++)
            if (units[i].UnitId == unitId) return units[i];
        return null;
    }

    // Selection only changes here when the current unit is gone or belongs to the other side.
    private void EnsureValidSelection(BattleSide side)
    {
        var current = manager.GetBattleUnit(input.SelectedUnitId);
        if (current != null && current.Side == side && current.IsAliveAndActive) return;

        int replacement = -1;
        for (int i = 0; i < deployedUnits.Count && replacement < 0; i++)
            if (deployedUnits[i].CanAct(side)) replacement = deployedUnits[i].UnitId;
        if (replacement < 0 && deployedUnits.Count > 0) replacement = deployedUnits[0].UnitId;
        if (input.SelectedUnitId != replacement) input.SelectUnit(replacement);
    }

    private void NormalizeAttackSelection(BattleUnitState unit, bool unitChanged)
    {
        if (unit == null) return;
        int weaponCount = unit.Snapshot?.Weapons?.Count ?? 0;
        bool specialOnly = weaponCount == 0 && unit.Snapshot?.SpecialAttackProfile != null;
        if (unitChanged && specialOnly)
        {
            if (!input.UseSpecialAttack || input.SelectedWeaponIndex != 0) input.SetAttackSelection(0, true);
            return;
        }
        bool invalid = (unitChanged && (input.SelectedWeaponIndex != 0 || input.UseSpecialAttack))
            || (input.UseSpecialAttack && unit.Snapshot?.SpecialAttackProfile == null)
            || input.SelectedWeaponIndex >= Mathf.Max(1, weaponCount);
        if (invalid) input.SetAttackSelection(0, false);
    }

    // ------------------------------------------------------------ status bar

    private struct SideCounts { public int Remaining, OnField, Reserve, Embarked, Lost, Retreated; }

    private static SideCounts Count(BattleSession session, BattleSide side)
    {
        var counts = new SideCounts();
        for (int i = 0; i < session.Units.Count; i++)
        {
            var unit = session.Units[i];
            if (unit == null || unit.Side != side) continue;
            if (unit.IsDead) { counts.Lost++; continue; }
            if (unit.HasRetreated) { counts.Retreated++; continue; }
            counts.Remaining++;
            if (unit.IsEmbarked) counts.Embarked++;
            else if (unit.IsReserve) counts.Reserve++;
            else counts.OnField++;
        }
        return counts;
    }

    private void UpdateStatusBar(BattleSession session)
    {
        var preview = manager.PendingPreview;
        foreach (BattleSide side in new[] { BattleSide.Attacker, BattleSide.Defender })
        {
            var owner = BattleUIUtil.SideOwner(preview, side);
            var counts = Count(session, side);
            string breakdown = $"<b>Units Remaining: {counts.Remaining}</b>\nOn battlefield: {counts.OnField}\nIn reserve: {counts.Reserve}\nEmbarked: {counts.Embarked}\nLost: {counts.Lost}\nRetreated: {counts.Retreated}";
            statusBar.SetSide(side, BattleUIUtil.CivName(owner), BattleUIUtil.CivEmblem(owner), $"{counts.Remaining} Units Remaining", breakdown);
        }

        string round = $"Round {session.CurrentRound} / {session.MaximumRounds}";
        var activeOwner = BattleUIUtil.SideOwner(preview, session.ActiveSide);
        string civ = BattleUIUtil.CivName(activeOwner);
        switch (session.Phase)
        {
            case BattlePhase.Deployment:
                statusBar.SetCenter(round, "Deployment", $"{civ} deploying");
                break;
            case BattlePhase.RoundEnd:
                statusBar.SetCenter(round, "Round Transition", "Resolving round end");
                break;
            default:
                string who = !IsHuman(session.ActiveSide) ? "Enemy Turn"
                    : IsHuman(BattleSide.Attacker) && IsHuman(BattleSide.Defender) ? "Turn" : "Your Turn";
                statusBar.SetCenter(round, session.Phase == BattlePhase.AttackerTurn ? "Attacker Phase" : "Defender Phase", $"{civ} - {who}");
                break;
        }
    }

    private void UpdateObjective(BattleSession session)
    {
        bool elimination = session.Objective.Type == BattleObjectiveType.Elimination;
        bool has = elimination || session.Objective.CellIndex >= 0;
        objectiveIndicator.SetActive(has);
        if (elimination)
            objectiveText.text = $"Eliminate / rout the enemy • Defender holds after Round {session.MaximumRounds}";
        else if (has)
            objectiveText.text = $"Objective: {BattleUIUtil.Humanize(session.Objective.Type.ToString())}  |  Held by {session.Objective.Owner}";
    }

    private void UpdateCameraControls()
    {
        if (presenter != null) layerLabel.text = $"Layer: {presenter.VisibleLayerName}";
    }

    private void PostTurnNotices(BattleSession session)
    {
        if (session.CurrentRound == lastNoticeRound && session.Phase == lastNoticePhase) return;
        bool turn = session.Phase == BattlePhase.AttackerTurn || session.Phase == BattlePhase.DefenderTurn;
        if (turn)
        {
            if (session.CurrentRound != lastNoticeRound) notifications.Post($"Round {session.CurrentRound}");
            else notifications.Post(IsHuman(session.ActiveSide) ? "Your turn" : "Enemy turn");
        }
        lastNoticeRound = session.CurrentRound;
        lastNoticePhase = session.Phase;
    }

    // -------------------------------------------------------- selected panel

    private void UpdateSelectedPanel(BattleUnitState unit, bool isReserve)
    {
        previousUnitButton.interactable = nextUnitButton.interactable = activeRoster.Count > 1;
        if (unit == null)
        {
            unitName.text = "No unit selected";
            unitIcon.enabled = false;
            unitStats.text = string.Empty;
            unitStatus.text = string.Empty;
            unitHealthFill.anchorMax = new Vector2(0f, 1f);
            return;
        }

        var snapshot = unit.Snapshot;
        var sprite = BattleUIUtil.UnitIcon(unit);
        unitIcon.sprite = sprite;
        unitIcon.enabled = sprite != null;
        unitName.text = BattleUIUtil.UnitName(unit);

        int max = Mathf.Max(1, snapshot != null ? snapshot.MaximumHealth : 1);
        unitHealthFill.anchorMax = new Vector2(Mathf.Clamp01(unit.CurrentHealth / (float)max), 1f);
        unitStats.text =
            $"HP <b>{unit.CurrentHealth}/{max}</b>   MOV <b>{unit.CurrentMovePoints}/{snapshot?.TacticalMovePoints ?? 0}</b>   AP <b>{unit.CurrentActionPoints}/{snapshot?.TacticalActionPoints ?? 0}</b>\n" +
            $"ATK <b>{snapshot?.MeleeAttack ?? 0}/{snapshot?.RangedAttack ?? 0}</b>   DEF <b>{snapshot?.Defense ?? 0}</b>   RNG <b>{snapshot?.Range ?? 0:0}</b>   LV <b>{snapshot?.Level ?? 1}</b>";
        unitStatus.text = BuildStatus(unit, isReserve);
    }

    private static string BuildStatus(BattleUnitState unit, bool isReserve)
    {
        if (isReserve || unit.IsReserve) return "<color=#66a6f2>In reserve</color>";
        var parts = new List<string>();
        if (unit.IsEmbarked) parts.Add("Embarked");
        else if (unit.IsWaiting) parts.Add("<color=#f2bf4d>Waiting</color>");
        else if (unit.HasActed || unit.CurrentActionPoints <= 0) parts.Add("<color=#9ea6b8>Acted</color>");
        else parts.Add("<color=#66d973>Ready</color>");
        if (unit.IsDefending) parts.Add("Defending");
        if (unit.HasMoved) parts.Add("Moved");
        if (unit.EmbarkedBattleUnitIds.Count > 0) parts.Add($"Cargo {unit.EmbarkedBattleUnitIds.Count}");
        for (int i = 0; i < unit.StatusEffects.Count; i++)
        {
            var effect = unit.StatusEffects[i];
            parts.Add($"<color=#f27369>{BattleUIUtil.Humanize(effect.Type.ToString())} ({effect.RemainingRounds})</color>");
        }
        return string.Join("  |  ", parts);
    }

    // -------------------------------------------------------- combat controls

    private void UpdateCombatControls(BattleSession session, BattleUnitState unit)
    {
        bool commandPhase = session.Phase == BattlePhase.AttackerTurn || session.Phase == BattlePhase.DefenderTurn;
        string blocked = null;
        if (!commandPhase) blocked = "Commands are unavailable right now.";
        else if (!IsHuman(session.ActiveSide)) blocked = "Enemy turn in progress.";
        else if (input.IsCommandInputLocked) blocked = "Wait for the current action to finish.";
        else if (unit == null) blocked = "No unit selected.";
        else if (unit.Side != session.ActiveSide) blocked = "Select a unit from the acting side.";
        else if (!unit.IsAliveAndActive) blocked = "This unit is not on the battlefield.";

        string actReason = blocked;
        if (actReason == null && !unit.CanAct(session.ActiveSide))
            actReason = unit.HasActed ? "This unit has already acted." : unit.IsWaiting ? "This unit is waiting." : "This unit has no action points left.";

        string moveReason = blocked;
        if (moveReason == null)
        {
            if (unit.CurrentMovePoints <= 0) moveReason = "No movement points left.";
            else if (actReason != null && !unit.HasAttackedThisTurn) moveReason = actReason;
        }

        string attackReason = actReason;
        if (attackReason == null)
        {
            bool hasAttackProfile = unit.Snapshot?.Weapons != null && unit.Snapshot.Weapons.Count > 0
                || unit.Snapshot?.SpecialAttackProfile != null;
            if (!hasAttackProfile) attackReason = "This unit has no available attack.";
            else if (unit.HasMoved && !(unit.Snapshot.TacticalProfile?.canAttackAfterMoving ?? true)) attackReason = "This unit cannot attack after moving.";
            else if (!HasLegalAttackTarget(unit, out string targetReason)) attackReason = targetReason;
        }

        if (moveReason == null && !HasLegalMoveDestination(unit)) moveReason = "No legal movement destinations.";
        string retreatReason = actReason;
        if (retreatReason == null && !manager.TryGetRetreatPath(unit.UnitId, -1, out _, out string routeReason))
            retreatReason = string.IsNullOrEmpty(routeReason) ? "No valid battlefield exit is reachable." : routeReason;

        var mode = input.Mode;
        SetButton(moveButton, moveReason == null, moveReason, "Move: Select a destination for this unit.");
        SetButton(attackButton, attackReason == null, attackReason, "Attack: Choose an attack and select a target.");
        SetButton(defendButton, actReason == null, actReason, "Defense Mode: Prepare this unit to defend.");
        SetButton(waitButton, actReason == null, actReason, "Wait: Delay this unit's activation.");
        SetButton(retreatButton, retreatReason == null, retreatReason, "Retreat: Withdraw through a valid battlefield exit.");
        SetFrame(moveButton, mode == BattleInteractionMode.Movement);
        SetFrame(attackButton, mode == BattleInteractionMode.Attack);
        SetFrame(retreatButton, mode == BattleInteractionMode.Retreat);
        SetFrame(defendButton, unit != null && unit.IsDefending);
        bool hasMoreActions = GetAdvancedActions(session, unit) != AdvancedActions.None;
        SetButton(moreActionsButton, hasMoreActions, "No additional actions are available for this unit.", "More Actions: Show additional unit abilities.");

        UpdateAttackSelector(unit, mode == BattleInteractionMode.Attack && unit != null);
        UpdateAdvancedActions(session, unit, actReason);
        contextHint.text = HintFor(mode);
    }

    private static string HintFor(BattleInteractionMode mode) => mode switch
    {
        BattleInteractionMode.Movement => "Select a highlighted destination hex.",
        BattleInteractionMode.Attack => "Select a highlighted detected target.",
        BattleInteractionMode.Retreat => "Select a highlighted friendly battlefield-edge exit.",
        BattleInteractionMode.Embark => "Select an adjacent friendly transport.",
        BattleInteractionMode.Disembark => "Select a beach, port, or valid adjacent destination.",
        BattleInteractionMode.Launch => "Select an adjacent aircraft launch cell.",
        BattleInteractionMode.Recovery => "Select an adjacent friendly carrier.",
        _ => "Choose a command, or select a unit.",
    };

    private bool HasLegalMoveDestination(BattleUnitState unit)
    {
        if (unit == null || manager.ActiveBattle == null) return false;
        for (int i = 0; i < manager.ActiveBattle.Map.CellCount; i++)
            if (i != unit.CellIndex && manager.IsLegalCellForMode(unit.UnitId, i, BattleInteractionMode.Movement)) return true;
        return false;
    }

    private bool HasLegalAttackTarget(BattleUnitState unit, out string reason)
    {
        reason = "No detected targets.";
        var targets = manager.GetVisibleEnemyUnits(unit.UnitId);
        if (targets.Count == 0) return false;
        reason = "No legal targets for the selected attack.";
        var profile = input.UseSpecialAttack ? unit.Snapshot?.SpecialAttackProfile : null;
        for (int i = 0; i < targets.Count; i++)
        {
            if (!manager.TryGetDamagePreview(unit.UnitId, targets[i].UnitId, input.SelectedWeaponIndex, profile, out var preview, out _))
                continue;
            if (preview.CanAttack) return true;
            if (!string.IsNullOrEmpty(preview.RejectionReason)) reason = preview.RejectionReason;
        }
        return false;
    }

    private void UpdateAttackSelector(BattleUnitState unit, bool attackMode)
    {
        bool melee = HasWeapon(unit, false), ranged = HasWeapon(unit, true), special = unit?.Snapshot?.SpecialAttackProfile != null;
        int modes = (melee ? 1 : 0) + (ranged ? 1 : 0) + (special ? 1 : 0);
        bool showSelector = attackMode && modes >= 2;

        var current = CurrentAttackMode(unit);
        var indices = WeaponIndicesFor(unit, current);
        bool showWeapon = attackMode && current != AttackMode.Special && indices.Count >= 2;

        SetButton(meleeModeButton, melee, "Melee attacks are unavailable for this unit.", "Melee: Use an available melee weapon.");
        SetButton(rangedModeButton, ranged, "Ranged attacks are unavailable for this unit.", "Ranged: Use an available ranged weapon.");
        SetButton(specialModeButton, special, "No special attack is available for this unit.", "Special: Use this unit's special attack.");
        SetButton(weaponButton, indices.Count > 1, "No alternate weapon is available.", "Weapon: Cycle the selected attack mode's weapons.");

        attackModeSelector.SetActive(showSelector);
        meleeModeButton.gameObject.SetActive(melee);
        rangedModeButton.gameObject.SetActive(ranged);
        specialModeButton.gameObject.SetActive(special);
        SetFrame(meleeModeButton, current == AttackMode.Melee);
        SetFrame(rangedModeButton, current == AttackMode.Ranged);
        SetFrame(specialModeButton, current == AttackMode.Special);

        weaponButton.gameObject.SetActive(showWeapon);
        if (attackMode && unit != null) weaponLabel.text = WeaponLabel(unit, current, indices);
        contextHint.gameObject.SetActive(!showSelector && !showWeapon);
    }

    private string WeaponLabel(BattleUnitState unit, AttackMode mode, List<int> indices)
    {
        if (mode == AttackMode.Special) return unit.Snapshot.SpecialAttackProfile.attackName;
        var weapon = BattleTargetingService.GetWeapon(unit, input.SelectedWeaponIndex);
        string name = weapon?.equipment != null ? weapon.equipment.equipmentName : weapon != null ? "Built-in weapon" : "Unarmed";
        int position = Mathf.Max(0, indices.IndexOf(input.SelectedWeaponIndex)) + 1;
        return indices.Count > 1 ? $"{name} ({position}/{indices.Count})" : name;
    }

    private AttackMode CurrentAttackMode(BattleUnitState unit)
    {
        if (unit?.Snapshot == null) return AttackMode.Melee;
        if (input.UseSpecialAttack && unit.Snapshot.SpecialAttackProfile != null) return AttackMode.Special;
        var weapon = BattleTargetingService.GetWeapon(unit, input.SelectedWeaponIndex);
        return weapon != null && weapon.usesRangedAttack ? AttackMode.Ranged : AttackMode.Melee;
    }

    private static bool HasWeapon(BattleUnitState unit, bool ranged)
    {
        var weapons = unit?.Snapshot?.Weapons;
        if (weapons == null) return false;
        for (int i = 0; i < weapons.Count; i++)
            if (weapons[i] != null && weapons[i].usesRangedAttack == ranged) return true;
        return false;
    }

    private static List<int> WeaponIndicesFor(BattleUnitState unit, AttackMode mode)
    {
        var result = new List<int>();
        var weapons = unit?.Snapshot?.Weapons;
        if (weapons == null || mode == AttackMode.Special) return result;
        for (int i = 0; i < weapons.Count; i++)
            if (weapons[i] != null && weapons[i].usesRangedAttack == (mode == AttackMode.Ranged)) result.Add(i);
        return result;
    }

    private void SetAttackMode(AttackMode mode)
    {
        var unit = manager.GetBattleUnit(input.SelectedUnitId);
        if (unit == null) return;
        if (mode == AttackMode.Special)
        {
            if (unit.Snapshot?.SpecialAttackProfile != null) input.SetAttackSelection(input.SelectedWeaponIndex, true);
            return;
        }
        var indices = WeaponIndicesFor(unit, mode);
        if (indices.Count > 0) input.SetAttackSelection(indices[0], false);
    }

    private void CycleWeapon()
    {
        var unit = manager.GetBattleUnit(input.SelectedUnitId);
        var indices = WeaponIndicesFor(unit, CurrentAttackMode(unit));
        if (indices.Count < 2) return;
        int next = (Mathf.Max(0, indices.IndexOf(input.SelectedWeaponIndex)) + 1) % indices.Count;
        input.SetAttackSelection(indices[next], false);
    }

    // ------------------------------------------------------ advanced actions

    private void UpdateAdvancedActions(BattleSession session, BattleUnitState unit, string actReason)
    {
        var available = GetAdvancedActions(session, unit);
        moreActionsButton.gameObject.SetActive(available != AdvancedActions.None);
        if (available == AdvancedActions.None) advancedOpen = false;
        advancedPopover.SetActive(advancedOpen);
        if (!advancedOpen) return;

        SetAdvanced(embarkButton, available, AdvancedActions.Embark, actReason, "Embark: Board an adjacent friendly transport.");
        SetAdvanced(disembarkButton, available, AdvancedActions.Disembark, actReason, "Disembark: Unload cargo to a valid destination.");
        SetAdvanced(launchButton, available, AdvancedActions.Launch, actReason, "Launch Aircraft: Launch embarked aircraft.");
        SetAdvanced(recoverButton, available, AdvancedActions.Recover, actReason, "Recover Aircraft: Land on an adjacent friendly carrier.");
        SetAdvanced(diveButton, available, AdvancedActions.Dive, actReason, "Dive: Move this unit to deep water.");
        SetAdvanced(shallowButton, available, AdvancedActions.Shallow, actReason, "Shallow: Move this unit to shallow water.");
        SetAdvanced(activeScanButton, available, AdvancedActions.Scan, actReason, "Active Scan: Improve detection and end this activation.");
    }

    private void SetAdvanced(Button button, AdvancedActions available, AdvancedActions flag, string reason, string hint)
    {
        button.gameObject.SetActive((available & flag) != 0);
        SetButton(button, reason == null, reason, hint);
    }

    private AdvancedActions GetAdvancedActions(BattleSession session, BattleUnitState unit)
    {
        var result = AdvancedActions.None;
        if (unit?.Snapshot == null || !unit.IsAliveAndActive || unit.Side != session.ActiveSide) return result;
        var profile = unit.Snapshot.TacticalProfile;

        if (!unit.IsEmbarked && unit.CellIndex >= 0)
            for (int i = 0; i < session.Units.Count; i++)
            {
                var other = session.Units[i];
                var otherProfile = other?.Snapshot?.TacticalProfile;
                if (other == null || other.UnitId == unit.UnitId || other.Side != unit.Side || !other.IsAliveAndActive || other.CellIndex < 0) continue;
                if (otherProfile == null || !(otherProfile.isTransport || otherProfile.isCarrier)) continue;
                if (session.MapDistance(unit.CellIndex, other.CellIndex) > 1) continue;
                result |= AdvancedActions.Embark;
                if (unit.Domain == BattleDomain.Air || unit.Domain == BattleDomain.Space) result |= AdvancedActions.Recover;
            }

        if (profile != null && (profile.isTransport || profile.isCarrier))
            for (int i = 0; i < unit.EmbarkedBattleUnitIds.Count; i++)
            {
                var cargo = manager.GetBattleUnit(unit.EmbarkedBattleUnitIds[i]);
                if (cargo == null) continue;
                bool aircraft = cargo.Domain == BattleDomain.Air || cargo.Domain == BattleDomain.Space;
                result |= aircraft ? AdvancedActions.Launch : AdvancedActions.Disembark;
            }

        if (unit.Domain == BattleDomain.Underwater)
        {
            if (unit.DepthBand != BattleDepthBand.Deep) result |= AdvancedActions.Dive;
            if (unit.DepthBand != BattleDepthBand.Shallow) result |= AdvancedActions.Shallow;
        }
        if (profile != null && profile.sensorRange > 0) result |= AdvancedActions.Scan;
        return result;
    }

    private void BeginAdvanced(BattleInteractionMode mode)
    {
        advancedOpen = false;
        input.SetMode(mode);
    }

    // ------------------------------------------------------- side-turn dock

    private void UpdateSideTurnControls(BattleSession session)
    {
        bool humanTurn = IsHuman(session.ActiveSide)
            && (session.Phase == BattlePhase.AttackerTurn || session.Phase == BattlePhase.DefenderTurn);
        int ready = 0;
        for (int i = 0; i < deployedUnits.Count; i++)
            if (IsReady(deployedUnits[i])) ready++;

        readyUnitCount.text = humanTurn ? $"{ready} / {deployedUnits.Count} units ready" : "Enemy turn";
        string reason = !humanTurn ? "Enemy turn in progress." : input.IsCommandInputLocked ? "Wait for the current action to finish." : null;
        SetButton(endSideButton, reason == null, reason, "End Turn: finish your tactical turn.");
    }

    private static bool IsReady(BattleUnitState unit) => !unit.HasActed && !unit.IsWaiting && unit.CurrentActionPoints > 0;

    private void EndSide()
    {
        manager.EndPlayerSideTurn();
        Refresh();
    }

    // ------------------------------------------------------------ deployment

    private void UpdateDeploymentControls(BattleSession session, BattleSide side, BattleUnitState selected)
    {
        int limit = manager.DeploymentUnitLimit;
        deployedTabLabel.text = $"Deployed ({deployedUnits.Count})";
        reservesTabLabel.text = $"Reserves ({reserveUnits.Count})";
        SetButton(reservesTab, reserveUnits.Count > 0, "No units in reserve.", "Show units held in reserve");
        SetFrame(deployedTab, !showReserves);
        SetFrame(reservesTab, showReserves);
        deploymentCount.text = limit > 0 ? $"Deployed <b>{deployedUnits.Count} / {limit}</b>" : $"Deployed <b>{deployedUnits.Count}</b>";
        deployingSideText.text = $"Deploying: {BattleUIUtil.CivName(BattleUIUtil.SideOwner(manager.PendingPreview, side))}";

        bool locked = input.IsCommandInputLocked;
        SetButton(resetButton, !locked, "Wait for the current action to finish.", "Reset Deployment: Restore the default deployment.");

        string swapReason = null;
        var reserve = FindIn(reserveUnits, selectedReserveId);
        if (reserveUnits.Count == 0) swapReason = "No units in reserve.";
        else if (reserve == null) swapReason = "Select a reserve unit in the Reserves tab.";
        else if (selected == null) swapReason = "Select a deployed unit to replace.";
        else if (locked) swapReason = "Wait for the current action to finish.";
        SetButton(swapReserveButton, swapReason == null, swapReason, "Swap the selected deployed unit with the selected reserve");

        bool valid = manager.ValidateDeployment(side, out string reason);
        SetButton(confirmDeploymentButton, valid && !locked, valid ? "Wait for the current action to finish." : reason, "Confirm this deployment and continue");

        string message = !string.IsNullOrEmpty(deployment.ValidationMessage) ? deployment.ValidationMessage : transientMessage;
        if (!string.IsNullOrEmpty(message)) { validationMessage.color = WarningColor; validationMessage.text = message; }
        else if (!valid) { validationMessage.color = WarningColor; validationMessage.text = reason; }
        else { validationMessage.color = ReadyColor; validationMessage.text = "Deployment is valid. Select a unit, then a highlighted hex to reposition it."; }
    }

    private void SetReservesTab(bool reserves)
    {
        if (showReserves == reserves) return;
        showReserves = reserves;
        Refresh();
    }

    private void ResetDeployment()
    {
        selectedReserveId = -1;
        deployment.ResetSide();
    }

    private void SwapReserve()
    {
        int reserveId = selectedReserveId;
        deployment.SelectUnit(input.SelectedUnitId);
        if (!deployment.SwapWithReserve(reserveId)) return;
        selectedReserveId = -1;
        showReserves = false;
        input.SelectUnit(reserveId);
    }

    private void ConfirmDeployment() => deployment.Confirm();

    // ---------------------------------------------------------------- roster

    private void UpdateRoster(BattleSession session, BattleSide side, bool deploying, bool unitChanged)
    {
        liveCards.Clear();
        int selectedId = deploying && showReserves ? selectedReserveId : input.SelectedUnitId;
        for (int i = 0; i < activeRoster.Count; i++)
        {
            var unit = activeRoster[i];
            if (!cards.TryGetValue(unit.UnitId, out var card) || card == null)
            {
                card = Instantiate(rosterCardPrefab, rosterContent, false);
                card.SetTooltipView(tooltip);
                card.Clicked += OnRosterCardClicked;
                cards[unit.UnitId] = card;
            }

            var state = deploying ? (showReserves ? TacticalRosterCardState.Reserve : TacticalRosterCardState.Deployed)
                : unit.IsWaiting ? TacticalRosterCardState.Waiting
                : IsReady(unit) ? TacticalRosterCardState.Ready : TacticalRosterCardState.Acted;
            int max = Mathf.Max(1, unit.Snapshot != null ? unit.Snapshot.MaximumHealth : 1);
            string tip = $"{BattleUIUtil.UnitName(unit)}\nHP {unit.CurrentHealth}/{max}\n{StateLabel(state)}";
            card.transform.SetSiblingIndex(i);
            card.Bind(unit, unit.UnitId == selectedId, state, tip);
            liveCards.Add(unit.UnitId);
        }

        staleCards.Clear();
        foreach (var pair in cards)
            if (!liveCards.Contains(pair.Key)) staleCards.Add(pair.Key);
        for (int i = 0; i < staleCards.Count; i++)
        {
            var card = cards[staleCards[i]];
            cards.Remove(staleCards[i]);
            if (card == null) continue;
            card.Clicked -= OnRosterCardClicked;
            Destroy(card.gameObject);
        }

        if (unitChanged && cards.TryGetValue(selectedId, out var selectedCard)) ScrollTo(selectedCard);
    }

    private static string StateLabel(TacticalRosterCardState state) => state switch
    {
        TacticalRosterCardState.Ready => "Ready",
        TacticalRosterCardState.Waiting => "Waiting",
        TacticalRosterCardState.Deployed => "Deployed",
        TacticalRosterCardState.Reserve => "In reserve",
        _ => "Acted",
    };

    private void ScrollTo(TacticalRosterCard card)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(rosterContent);
        float overflow = rosterContent.rect.width - rosterScroll.viewport.rect.width;
        if (overflow <= 0f) { rosterScroll.horizontalNormalizedPosition = 0f; return; }
        var rect = (RectTransform)card.transform;
        float center = rect.anchoredPosition.x + rect.rect.width * 0.5f;
        rosterScroll.horizontalNormalizedPosition = Mathf.Clamp01((center - rosterScroll.viewport.rect.width * 0.5f) / overflow);
    }

    private void OnRosterCardClicked(int unitId) => SelectRosterUnit(unitId);

    private void SelectRosterUnit(int unitId)
    {
        if (manager.ActiveBattle != null && manager.ActiveBattle.Phase == BattlePhase.Deployment && showReserves)
        {
            selectedReserveId = unitId;
            Refresh();
            return;
        }
        input.SelectUnit(unitId);
    }

    private void CycleUnit(int direction)
    {
        if (activeRoster.Count == 0) return;
        bool reserves = manager.ActiveBattle != null && manager.ActiveBattle.Phase == BattlePhase.Deployment && showReserves;
        int currentId = reserves ? selectedReserveId : input.SelectedUnitId;
        int index = activeRoster.FindIndex(unit => unit.UnitId == currentId);
        index = index < 0 ? 0 : (index + direction + activeRoster.Count) % activeRoster.Count;
        SelectRosterUnit(activeRoster[index].UnitId);
    }

    // -------------------------------------------------------------- commands

    private void SetMode(BattleInteractionMode mode)
    {
        advancedOpen = false;
        input.SetMode(mode);
    }

    private void CycleLayer()
    {
        presenter?.CycleLayer();
        Refresh();
    }

    private void Submit(bool success, string reason)
    {
        if (!success) notifications.Post(string.IsNullOrEmpty(reason) ? "Command rejected." : reason);
        Refresh();
    }

    private void SetButton(Button button, bool enabled, string disabledReason, string enabledHint)
    {
        button.interactable = enabled;
        var tip = button.GetComponent<BattleHoverTip>();
        if (tip != null) tip.SetText(enabled ? enabledHint : disabledReason);
    }

    private static void SetTooltip(Button button, string text)
    {
        var tip = button != null ? button.GetComponent<BattleHoverTip>() : null;
        if (tip != null) tip.SetText(text);
    }

    private static void SetFrame(Button button, bool active)
    {
        var frame = button != null ? button.transform.Find("ActiveFrame") : null;
        if (frame != null && frame.gameObject.activeSelf != active) frame.gameObject.SetActive(active);
    }

    // ------------------------------------------------------- combat preview

    private void OnCellPointerEntered(int cellIndex)
    {
        if (input == null || input.Mode != BattleInteractionMode.Attack || manager.ActiveBattle == null)
        {
            combatPreview.Hide();
            return;
        }

        var attacker = manager.GetBattleUnit(input.SelectedUnitId);
        var target = presenter.GetDisplayedUnitAtCell(cellIndex);
        if (attacker == null || target == null || target.Side == attacker.Side)
        {
            combatPreview.Hide();
            return;
        }

        var profile = input.UseSpecialAttack ? attacker.Snapshot?.SpecialAttackProfile : null;
        Vector2 screen = ScreenPointForCell(cellIndex, target);
        if (!manager.TryGetDamagePreview(attacker.UnitId, target.UnitId, input.SelectedWeaponIndex, profile, out var p, out var detection))
        {
            // Undetected contacts must not be revealed; suspected ones expose no exact data.
            if (detection == BattleDetectionLevel.Suspected)
                combatPreview.Show("Suspected contact", "Exact combat information is unavailable.", screen);
            else combatPreview.Hide();
            return;
        }

        string kind = p.IsSpecial ? "Special" : p.IsRanged ? "Ranged" : "Melee";
        string ammo = p.AmmoRemaining < 0 ? "Unlimited" : p.AmmoRemaining.ToString();
        string cover = p.HasHardCover ? "Hard" : p.HasSoftCover ? "Soft" : "None";
        string elevation = p.ElevationDelta > 0 ? "High ground" : p.ElevationDelta < 0 ? "Low ground" : "Level";

        string result;
        if (p.CanAttack)
        {
            result = $"Damage: <b>{p.PredictedDamage}</b>";
            if (p.CounterattackPossible) result += $"\nCounterattack: <b>{p.PredictedCounterDamage}</b>";
        }
        else
        {
            string why = !string.IsNullOrEmpty(p.RejectionReason) ? p.RejectionReason
                : p.LosReason != BattleLosBlockReason.None ? BattleUIUtil.Humanize(p.LosReason.ToString()) : "Not a legal target";
            result = $"<color=#f27369>Cannot attack</color>\n{why}";
        }

        string identity = detection == BattleDetectionLevel.Identified
            ? $"Target HP: {target.CurrentHealth}/{target.Snapshot?.MaximumHealth ?? 1}"
            : "Detected contact";
        combatPreview.Show($"{p.WeaponName} ({kind})",
            $"{result}\n{identity}\nRange: {p.Range} ({p.MinimumRange}-{p.MaximumRange})\nAmmo: {ammo}   Cooldown: {p.CooldownRemaining}\nCover: {cover}   Elevation: {elevation}",
            screen);
    }

    private void OnCellPointerExited(int cellIndex) => combatPreview.Hide();

    private Vector2 ScreenPointForCell(int cellIndex, BattleUnitState target)
    {
        var camera = manager.TacticalCamera;
        if (camera == null || presenter == null) return Input.mousePosition;
        Vector3 world = presenter.GetBattleCellWorldPosition(cellIndex, target.Domain, target.DepthBand);
        Vector3 screen = camera.WorldToScreenPoint(world);
        return screen.z > 0f ? new Vector2(screen.x, screen.y) : (Vector2)Input.mousePosition;
    }
}
