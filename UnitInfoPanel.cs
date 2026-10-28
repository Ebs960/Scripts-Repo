using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class UnitInfoPanel : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject unitInfoPanel;
    [SerializeField] private Image unitIconImage; // Display the unit's icon
    [SerializeField] private TextMeshProUGUI unitNameText;
    [SerializeField] private TextMeshProUGUI unitTypeText;
    [SerializeField] private TextMeshProUGUI unitDescriptionText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI experienceText;
    
    [Header("Stats")]
    [SerializeField] private TextMeshProUGUI attackText;
    [SerializeField] private TextMeshProUGUI defenseText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI movePointsText;
    [SerializeField] private TextMeshProUGUI attackPointsText;
    [SerializeField] private TextMeshProUGUI rangeText;
    
    [Header("Worker Stats")]
    [SerializeField] private TextMeshProUGUI workPointsText;

    [Header("Actions")]
    [SerializeField] private Button settleCityButton;
    [SerializeField] private Button forageButton; // new forage action for workers
    [SerializeField] private Button fortifyButton;
    [SerializeField] private Button startBuildButton; // starts selected build option (optional)
    [SerializeField] private Button captureButton; // capture action for animals/workers
    [SerializeField] private Button upgradeButton; // upgrades selected combat unit to latest unlocked replacement
    [Header("Orbit Controls")]
    [SerializeField] private TextMeshProUGUI orbitStatusText;
    [SerializeField] private Button enterOrbitButton;
    [SerializeField] private Button exitOrbitButton;
    [Header("Worker Build Units UI")] 
    [SerializeField] private TMP_Dropdown buildOptionsDropdown; // TMP dropdown for build options
    [Header("Herd Build UI")]
    [SerializeField] private Button buildHerdButton; // Button to construct selected herd building using worker's work points
    [Header("Unit Build UI")]
    [SerializeField] private TMP_Dropdown unitBuildDropdown; // separate dropdown specifically for unit builds
    
    [Header("Stack Controls")]
    [SerializeField] private Button unstackButton; // Split this member into a new army (costs full turn)
    [SerializeField] private TextMeshProUGUI stackInfoText; // Shows army roster position and capacity
    [SerializeField] private StackOrderPanel stackOrderPanel; // Icon list for reordering the stack

    private CombatUnit currentCombatUnit;
    private WorkerUnit currentWorkerUnit;
    private CampaignArmyPanel campaignArmyPanel;
    private GameEventManager subscribedGameEvents;
    public bool IsVisible => unitInfoPanel != null && unitInfoPanel.activeSelf;
    public TMP_Text StackInfoText => stackInfoText;
    // Dropdown option data
    private struct BuildOption
    {
        public enum OptionType { Improvement, CombatUnit, WorkerUnit }
        public OptionType Type;
        public ImprovementData Improvement;
        public CombatUnitData CombatUnit;
        public WorkerUnitData WorkerUnit;
        public string Display;
        public bool IsAvailable;
    }
    private List<BuildOption> buildOptions = new List<BuildOption>();
    private int pendingBuildIndex = -1;
    private bool suppressBuildOptionCallback;
    // Unit-build specific list and pending index
    private struct UnitBuildEntry { public bool isCombat; public CombatUnitData combatData; public WorkerUnitData workerData; public string display; public bool isAvailable; }
    private List<UnitBuildEntry> unitBuildOptions = new List<UnitBuildEntry>();
    private int pendingUnitBuildIndex = -1;

    private void Awake()
    {
        campaignArmyPanel = CampaignArmyPanel.GetOrCreate(this);
        if (settleCityButton != null)
            settleCityButton.onClick.AddListener(OnSettleCityClicked);

        // Tooltips for unit UI buttons
        AddTooltipToButton(settleCityButton, "Found City", "Found a new city on this tile. Consumes the worker.");

        // Contribute work actions are routed through the primary Start/Contribute button now

        if (forageButton != null)
            forageButton.onClick.AddListener(OnForageClicked);
        AddTooltipToButton(forageButton, "Forage", "Forage resources from this tile if available.");
        EnsureUpgradeButton();
        EnsureOrbitButtons();
        if (fortifyButton != null)
            fortifyButton.onClick.AddListener(OnFortifyClicked);
        AddTooltipToButton(fortifyButton, "Fortify", "Fortify to gain defense and skip this unit's action.");
        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveAllListeners();
            upgradeButton.onClick.AddListener(OnUpgradeClicked);
            upgradeButton.gameObject.SetActive(false);
        }

        if (enterOrbitButton != null)
            enterOrbitButton.onClick.AddListener(OnEnterOrbitClicked);
        AddTooltipToButton(enterOrbitButton, "Enter Orbit", "Enter orbit from this tile if available.");
        if (exitOrbitButton != null)
            exitOrbitButton.onClick.AddListener(OnExitOrbitClicked);
        AddTooltipToButton(exitOrbitButton, "Exit Orbit", "Exit orbit and land on this tile.");

        // On start, clear the panel to show a "no unit selected" state.
        ClearPanelInfo();
        if (buildOptionsDropdown != null)
        {
            buildOptionsDropdown.onValueChanged.RemoveAllListeners();
            buildOptionsDropdown.onValueChanged.AddListener(OnBuildOptionSelected);
            buildOptionsDropdown.gameObject.SetActive(false);
            buildOptionsDropdown.interactable = false;
        }
        if (unitBuildDropdown != null)
        {
            unitBuildDropdown.onValueChanged.RemoveAllListeners();
            unitBuildDropdown.onValueChanged.AddListener(OnUnitBuildOptionSelected);
            unitBuildDropdown.gameObject.SetActive(false);
            unitBuildDropdown.interactable = false;
        }
        if (startBuildButton != null)
        {
            startBuildButton.onClick.RemoveAllListeners();
            startBuildButton.onClick.AddListener(OnStartBuildButtonClicked);
            // Keep the primary build button visible; disable until an action is available
            startBuildButton.gameObject.SetActive(true);
            startBuildButton.interactable = false;
            AddTooltipToButton(startBuildButton, "Work / Build", "Start or contribute to building improvements or training units on this tile.");
        }
        // Legacy unit-build button removed; unit builds go through startBuildButton

        // Split-army button (legacy serialized field name retained for prefab compatibility)
        if (unstackButton != null)
        {
            unstackButton.onClick.RemoveAllListeners();
            unstackButton.onClick.AddListener(OnUnstackClicked);
            unstackButton.gameObject.SetActive(false);
            AddTooltipToButton(unstackButton, "Split Army", "Move this member into a new army on an adjacent tile. Costs the full turn.");
        }

        // Validate serialized fields at startup so missing inspector wiring is obvious in Console
        ValidateSerializedFields();

        SubscribeToGameEvents();
    }

    private void SubscribeToGameEvents()
    {
        var current = GameEventManager.Instance;
        if (current == subscribedGameEvents) return;
        UnsubscribeFromGameEvents();
        subscribedGameEvents = current;
        if (subscribedGameEvents == null) return;
        subscribedGameEvents.OnUnitMoved += HandleUnitMovedEvent;
        subscribedGameEvents.OnMovementCompleted += HandleUnitMovedEvent;
        subscribedGameEvents.OnMovePointsChanged += HandleMovePointsChanged;
        subscribedGameEvents.OnAttackPointsChanged += HandleAttackPointsChanged;
        subscribedGameEvents.OnHealthChanged += HandleHealthChanged;
        subscribedGameEvents.OnDamageApplied += HandleCombatEvent;
        subscribedGameEvents.OnUnitKilled += HandleCombatEvent;
        subscribedGameEvents.OnUnitLost += HandleUnitLost;
        subscribedGameEvents.OnTurnStarted += HandleTurnChanged;
        subscribedGameEvents.OnTurnEnded += HandleTurnChanged;
    }

    private void UnsubscribeFromGameEvents()
    {
        if (subscribedGameEvents == null) return;
        subscribedGameEvents.OnUnitMoved -= HandleUnitMovedEvent;
        subscribedGameEvents.OnMovementCompleted -= HandleUnitMovedEvent;
        subscribedGameEvents.OnMovePointsChanged -= HandleMovePointsChanged;
        subscribedGameEvents.OnAttackPointsChanged -= HandleAttackPointsChanged;
        subscribedGameEvents.OnHealthChanged -= HandleHealthChanged;
        subscribedGameEvents.OnDamageApplied -= HandleCombatEvent;
        subscribedGameEvents.OnUnitKilled -= HandleCombatEvent;
        subscribedGameEvents.OnUnitLost -= HandleUnitLost;
        subscribedGameEvents.OnTurnStarted -= HandleTurnChanged;
        subscribedGameEvents.OnTurnEnded -= HandleTurnChanged;
        subscribedGameEvents = null;
    }


    private void EnsureUpgradeButton()
    {
        if (upgradeButton != null || fortifyButton == null) return;

        var clone = Instantiate(fortifyButton.gameObject, fortifyButton.transform.parent);
        clone.name = "Upgrade Button";
        var rect = clone.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = new Vector2(200f, -14f);
            rect.sizeDelta = new Vector2(86f, 28f);
        }
        upgradeButton = clone.GetComponent<Button>();
        var label = clone.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = "Upgrade";
        clone.SetActive(false);
    }

    private void EnsureOrbitButtons()
    {
        if (fortifyButton == null) return;
        if (enterOrbitButton == null)
            enterOrbitButton = CreateActionButton("Enter Orbit", 285f);
        if (exitOrbitButton == null)
            exitOrbitButton = CreateActionButton("Exit Orbit", 285f);
    }

    private Button CreateActionButton(string labelText, float x)
    {
        var clone = Instantiate(fortifyButton.gameObject, fortifyButton.transform.parent, false);
        clone.name = labelText + " Button";
        var rect = clone.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = new Vector2(x, -14f);
            rect.sizeDelta = new Vector2(82f, 28f);
        }
        var label = clone.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = labelText;
        var button = clone.GetComponent<Button>();
        button?.onClick.RemoveAllListeners();
        clone.SetActive(false);
        return button;
    }

    private void AddTooltipToButton(Button btn, string title, string description)
    {
        if (btn == null || TooltipSystem.Instance == null) return;
        // Ensure an EventTrigger exists
        var trig = btn.GetComponent<EventTrigger>();
        if (trig == null) trig = btn.gameObject.AddComponent<EventTrigger>();

        // Clear existing entries for safety (don't remove other unrelated triggers)
        // Remove only PointerEnter/PointerExit to avoid interfering with other handlers
        for (int i = trig.triggers.Count - 1; i >= 0; --i)
        {
            if (trig.triggers[i].eventID == EventTriggerType.PointerEnter || trig.triggers[i].eventID == EventTriggerType.PointerExit)
                trig.triggers.RemoveAt(i);
        }

        var entryEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entryEnter.callback.AddListener((data) => { TooltipSystem.Instance.ShowSimpleTooltip(title, description); });
        trig.triggers.Add(entryEnter);

        var entryExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        entryExit.callback.AddListener((data) => { TooltipSystem.Instance.RequestHideTooltip(); });
        trig.triggers.Add(entryExit);
    }

    private void ValidateSerializedFields()
    {
        // Common section
        if (unitInfoPanel == null) Debug.LogWarning("[UnitInfoPanel] unitInfoPanel is not assigned in the Inspector.");
        if (unitNameText == null) Debug.LogWarning("[UnitInfoPanel] unitNameText is not assigned in the Inspector.");
        if (unitTypeText == null) Debug.LogWarning("[UnitInfoPanel] unitTypeText is not assigned in the Inspector.");
        if (levelText == null) Debug.LogWarning("[UnitInfoPanel] levelText is not assigned in the Inspector.");
        if (experienceText == null) Debug.LogWarning("[UnitInfoPanel] experienceText is not assigned in the Inspector.");

        // Stats
        if (attackText == null) Debug.LogWarning("[UnitInfoPanel] attackText is not assigned in the Inspector.");
        if (defenseText == null) Debug.LogWarning("[UnitInfoPanel] defenseText is not assigned in the Inspector.");
        if (healthText == null) Debug.LogWarning("[UnitInfoPanel] healthText is not assigned in the Inspector.");
        if (movePointsText == null) Debug.LogWarning("[UnitInfoPanel] movePointsText is not assigned in the Inspector.");
        if (attackPointsText == null) Debug.LogWarning("[UnitInfoPanel] attackPointsText is not assigned in the Inspector.");
        if (rangeText == null) Debug.LogWarning("[UnitInfoPanel] rangeText is not assigned in the Inspector.");

        // Actions / Construction
        if (settleCityButton == null) Debug.LogWarning("[UnitInfoPanel] settleCityButton is not assigned in the Inspector.");
        if (forageButton == null) Debug.LogWarning("[UnitInfoPanel] forageButton is not assigned in the Inspector.");
        if (fortifyButton == null) Debug.LogWarning("[UnitInfoPanel] fortifyButton is not assigned in the Inspector.");
        if (captureButton == null) Debug.LogWarning("[UnitInfoPanel] captureButton is not assigned in the Inspector.");
        // (Removed obsolete buildUnitsContainer/buildUnitButtonPrefab warnings)
        if (unitBuildDropdown == null) Debug.LogWarning("[UnitInfoPanel] unitBuildDropdown is not assigned in the Inspector.");
        if (buildHerdButton == null) Debug.LogWarning("[UnitInfoPanel] buildHerdButton is not assigned in the Inspector.");
        // Legacy startUnitBuildButton removed; use startBuildButton instead
    }

    public void ShowPanel(object unitObject)
    {
        if (unitObject == null) { HidePanel(); return; }
        if (unitInfoPanel != null && !unitInfoPanel.activeSelf)
            unitInfoPanel.SetActive(true);

        HideAllSections(); // Helper to hide all specific sections initially

        string unitNameForLog = "Unknown Unit";

    if (unitObject is CombatUnit combatUnit)
        {
            currentCombatUnit = combatUnit;
            currentWorkerUnit = null; // Ensure worker unit is cleared
            unitNameForLog = currentCombatUnit.data.unitName;
            campaignArmyPanel?.SetInspector(InspectRosterMember);
            campaignArmyPanel?.Show(currentCombatUnit);
            PopulateForCombatUnit(currentCombatUnit);
            if (settleCityButton != null) settleCityButton.gameObject.SetActive(false); // Hide for combat units
        }
        else if (unitObject is WorkerUnit workerUnit)
        {
            currentWorkerUnit = workerUnit;
            currentCombatUnit = null; // Ensure combat unit is cleared
            unitNameForLog = currentWorkerUnit.data.unitName;
            CombatUnit attachedArmy = FindAttachedArmy(workerUnit);
            if (attachedArmy != null)
            {
                campaignArmyPanel?.SetInspector(InspectRosterMember);
                campaignArmyPanel?.Show(attachedArmy);
                InspectRosterMember(workerUnit);
            }
            else
            {
                campaignArmyPanel?.Hide();
                PopulateForWorkerUnit(currentWorkerUnit);
            }
        }
        else
        {
            Debug.LogError($"UnitInfoPanel.ShowPanel: Received an unknown unit type: {unitObject?.GetType().Name ?? "null"}");
            if (unitInfoPanel != null) unitInfoPanel.SetActive(false); // Hide if unknown type
            return;
        }

        // Critical Check:
        if (unitInfoPanel == null)
        {
            Debug.LogError($"UnitInfoPanel.ShowPanel: The internal 'unitInfoPanel' GameObject reference is NULL for {unitNameForLog}! Cannot activate panel. Check prefab assignment in UnitInfoPanel.cs Inspector.");
            return;
        }
        // Start slide-in animation
        // Update common elements if any, or specific ones again if needed after activation
        // RefreshLayout(); // If you have dynamic content that needs a layout refresh
    }

    public void HidePanel()
    {
        ClearPanelInfo();
        if (unitInfoPanel != null) unitInfoPanel.SetActive(false);
    }

    private void LateUpdate()
    {
        SubscribeToGameEvents();
        if (IsVisible && currentCombatUnit == null && currentWorkerUnit == null)
            HidePanel();
    }


    private CombatUnit FindAttachedArmy(WorkerUnit worker)
    {
        if (worker == null || string.IsNullOrEmpty(worker.AttachedArmyFormationId) || worker.owner?.combatUnits == null)
            return null;
        foreach (var member in worker.owner.combatUnits)
            if (member != null && member.MilitaryFormationId == worker.AttachedArmyFormationId)
                return CampaignArmyService.GetRepresentative(member);
        return null;
    }

    private void InspectRosterMember(BaseUnit unit)
    {
        if (unit == null || !IsVisible) return;
        if (unit is CombatUnit combat)
        {
            currentCombatUnit = combat;
            currentWorkerUnit = null;
            PopulateForCombatUnit(combat);
        }
        else if (unit is WorkerUnit worker)
        {
            currentWorkerUnit = worker;
            currentCombatUnit = null;
            PopulateForWorkerUnit(worker);
        }
    }

    private void ClearPanelInfo()
    {
        // Set all text fields to their default "empty" state and ensure they are visible
        if (unitNameText != null) { unitNameText.text = "No Unit Selected"; unitNameText.gameObject.SetActive(true); }
        if (unitTypeText != null) { unitTypeText.text = "---"; unitTypeText.gameObject.SetActive(true); }
        if (unitDescriptionText != null) { unitDescriptionText.text = string.Empty; unitDescriptionText.gameObject.SetActive(false); }
        if (levelText != null) { levelText.text = "Level: -"; levelText.gameObject.SetActive(true); }
        if (experienceText != null) { experienceText.text = "XP: -/-"; experienceText.gameObject.SetActive(true); }
        if (attackText != null) { attackText.text = "Attack: -"; attackText.gameObject.SetActive(true); }
        if (defenseText != null) { defenseText.text = "Defense: -"; defenseText.gameObject.SetActive(true); }
        if (healthText != null) { healthText.text = "Health: -/-"; healthText.gameObject.SetActive(true); }
        if (movePointsText != null) { movePointsText.text = "Move: -"; movePointsText.gameObject.SetActive(true); }
        if (attackPointsText != null) { attackPointsText.text = "AP: -"; attackPointsText.gameObject.SetActive(true); }
        if (rangeText != null) { rangeText.text = "Range: -"; rangeText.gameObject.SetActive(true); }
        if (workPointsText != null) { workPointsText.text = "Work Points: -"; workPointsText.gameObject.SetActive(false); }

        // Hide buttons that require a unit
        if (settleCityButton != null) settleCityButton.gameObject.SetActive(false);
        if (fortifyButton != null) fortifyButton.gameObject.SetActive(false);
        if (buildOptionsDropdown != null)
        {
            buildOptionsDropdown.ClearOptions();
            buildOptionsDropdown.gameObject.SetActive(false);
            buildOptionsDropdown.interactable = false;
        }
        if (unitBuildDropdown != null)
        {
            unitBuildDropdown.ClearOptions();
            unitBuildDropdown.gameObject.SetActive(false);
            unitBuildDropdown.interactable = false;
        }
        // Primary start/contribute button: keep visible but disabled when no action
        if (startBuildButton != null) { startBuildButton.gameObject.SetActive(true); startBuildButton.interactable = false; }

        // Hide orbit controls
        if (orbitStatusText != null) orbitStatusText.gameObject.SetActive(false);
        if (enterOrbitButton != null) enterOrbitButton.gameObject.SetActive(false);
        if (exitOrbitButton != null) exitOrbitButton.gameObject.SetActive(false);
        if (upgradeButton != null) upgradeButton.gameObject.SetActive(false);

        // Hide stack controls
        if (unstackButton != null) unstackButton.gameObject.SetActive(false);
        if (stackInfoText != null) stackInfoText.gameObject.SetActive(false);
        stackOrderPanel?.Refresh(null);
        campaignArmyPanel?.Hide();

        // Clear unit references
        currentCombatUnit = null;
        currentWorkerUnit = null;
        if (unitInfoPanel != null) unitInfoPanel.SetActive(false);
    }

    private void PopulateUnitBuildDropdown(WorkerUnit worker)
    {
        if (unitBuildDropdown == null || worker == null) return;
        unitBuildOptions.Clear();
        var civ = worker.owner;
        if (civ == null) return;

        var options = new List<string> { "Train Unit..." };

        var units = GetWorkerBuildableCombatUnits(worker);
        if (units.Count > 0)
        {
            foreach (var u in units)
            {
                bool isAvailable = worker.CanBuildUnit(u, worker.currentTileIndex);
                string label = $"Combat: {u.unitName} ({u.workerWorkCost} WP)";
                unitBuildOptions.Add(new UnitBuildEntry { isCombat = true, combatData = u, workerData = null, display = label, isAvailable = isAvailable });
                options.Add(FormatDisabledDropdownLabel(label, isAvailable));
            }
        }

        var workerUnits = GetWorkerBuildableWorkerUnits(worker);
        if (workerUnits.Count > 0)
        {
            foreach (var wu in workerUnits)
            {
                bool isAvailable = worker.CanBuildWorker(wu, worker.currentTileIndex);
                string label = $"Worker: {wu.unitName} ({wu.workerWorkCost} WP)";
                unitBuildOptions.Add(new UnitBuildEntry { isCombat = false, combatData = null, workerData = wu, display = label, isAvailable = isAvailable });
                options.Add(FormatDisabledDropdownLabel(label, isAvailable));
            }
        }

        if (options.Count <= 1)
        {
            unitBuildDropdown.ClearOptions();
            unitBuildDropdown.options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("No unit build options") };
            unitBuildDropdown.SetValueWithoutNotify(0);
            unitBuildDropdown.interactable = false;
            unitBuildDropdown.gameObject.SetActive(false);
            // Use primary Start/Contribute button for unit actions; keep it visible but disabled
            if (startBuildButton != null) { startBuildButton.gameObject.SetActive(true); startBuildButton.interactable = false; }
            return;
        }

        unitBuildDropdown.ClearOptions();
        unitBuildDropdown.AddOptions(options);
        unitBuildDropdown.SetValueWithoutNotify(0);
        unitBuildDropdown.interactable = true;
        unitBuildDropdown.gameObject.SetActive(true);
        pendingUnitBuildIndex = -1;
        RefreshStartBuildButtonState(worker);
    }

    private void OnUnitBuildOptionSelected(int idx)
    {
        if (idx <= 0 || unitBuildOptions == null)
        {
            pendingUnitBuildIndex = -1;
            RefreshStartBuildButtonState(currentWorkerUnit);
            return;
        }

        pendingUnitBuildIndex = idx - 1;
        pendingBuildIndex = -1;
        unitBuildDropdown.gameObject.SetActive(false);
        RefreshStartBuildButtonState(currentWorkerUnit);
    }


    private List<CombatUnitData> GetWorkerBuildableCombatUnits(WorkerUnit worker)
    {
        var result = new List<CombatUnitData>();
        if (worker == null || worker.owner == null) return result;

        var seen = new HashSet<CombatUnitData>();
        foreach (var unit in worker.owner.unlockedCombatUnits)
        {
            if (unit == null || !unit.buildableByWorker || seen.Contains(unit)) continue;
            seen.Add(unit);
            result.Add(unit);
        }

        return result;
    }

    private List<WorkerUnitData> GetWorkerBuildableWorkerUnits(WorkerUnit worker)
    {
        var result = new List<WorkerUnitData>();
        if (worker == null || worker.owner == null) return result;

        var seen = new HashSet<WorkerUnitData>();
        foreach (var unit in worker.owner.unlockedWorkerUnits)
        {
            if (unit == null || !unit.buildableByWorker || seen.Contains(unit)) continue;
            seen.Add(unit);
            result.Add(unit);
        }

        return result;
    }

    private static string FormatDisabledDropdownLabel(string label, bool isAvailable)
    {
        return isAvailable ? label : $"<color=#808080>{label}</color>";
    }

    private string GetCombatBuildabilityReason(WorkerUnit worker, CombatUnitData unitData)
    {
        if (worker == null) return "worker null";
        if (unitData == null) return "unit null";
        if (worker.owner == null) return "owner null";
        if (!unitData.buildableByWorker) return "buildableByWorker=false";
        if (worker.currentWorkPoints <= 0) return "no work points";
        if (worker.currentTileIndex < 0) return "invalid worker tile";
        if (!unitData.IsBuildableFor(worker.owner)) return unitData.AreObsoleteFor(worker.owner) ? "obsolete" : "requirements unmet";
        if (LimitManager.Instance != null && !LimitManager.Instance.CanCreateCombatUnit(worker.owner, unitData)) return "combat unit limit reached";
        return "buildable";
    }

    private string GetWorkerBuildabilityReason(WorkerUnit worker, WorkerUnitData workerData)
    {
        if (worker == null) return "worker null";
        if (workerData == null) return "unit null";
        if (worker.owner == null) return "owner null";
        if (!workerData.buildableByWorker) return "buildableByWorker=false";
        if (worker.currentWorkPoints <= 0) return "no work points";
        if (worker.currentTileIndex < 0) return "invalid worker tile";
        if (!workerData.IsBuildableFor(worker.owner)) return workerData.AreObsoleteFor(worker.owner) ? "obsolete" : "requirements unmet";
        if (LimitManager.Instance != null && !LimitManager.Instance.CanCreateWorkerUnit(worker.owner, workerData)) return "worker unit limit reached";
        return "buildable";
    }

    private void LogWorkerBuildDebug(WorkerUnit worker)
    {
        // Diagnostic logging removed.
    }


    private void SetUnitDescription(string description, string fallbackTitle)
    {
        bool hasDescription = !string.IsNullOrWhiteSpace(description);
        if (unitDescriptionText != null)
        {
            unitDescriptionText.text = hasDescription ? description.Trim() : string.Empty;
            unitDescriptionText.gameObject.SetActive(hasDescription);
        }
        if (unitNameText != null)
            AddTooltipToText(unitNameText, fallbackTitle, hasDescription ? description.Trim() : "No unit description set.");
    }

    private void AddTooltipToText(TextMeshProUGUI text, string title, string description)
    {
        if (text == null || TooltipSystem.Instance == null) return;
        var trig = text.GetComponent<EventTrigger>();
        if (trig == null) trig = text.gameObject.AddComponent<EventTrigger>();
        for (int i = trig.triggers.Count - 1; i >= 0; --i)
            if (trig.triggers[i].eventID == EventTriggerType.PointerEnter || trig.triggers[i].eventID == EventTriggerType.PointerExit)
                trig.triggers.RemoveAt(i);
        var entryEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entryEnter.callback.AddListener((data) => { TooltipSystem.Instance.ShowSimpleTooltip(title, description); });
        trig.triggers.Add(entryEnter);
        var entryExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        entryExit.callback.AddListener((data) => { TooltipSystem.Instance.RequestHideTooltip(); });
        trig.triggers.Add(entryExit);
    }

    private void UpdateUnitInfoForCombatUnit()
    {
        if (currentCombatUnit == null) return;

        // Ensure all relevant fields are visible
        if (unitNameText != null) unitNameText.gameObject.SetActive(true);
        if (unitTypeText != null) unitTypeText.gameObject.SetActive(true);
        if (levelText != null) levelText.gameObject.SetActive(true);
        if (experienceText != null) experienceText.gameObject.SetActive(true);
        if (attackText != null) attackText.gameObject.SetActive(true);
        if (defenseText != null) defenseText.gameObject.SetActive(true);
        if (healthText != null) healthText.gameObject.SetActive(true);
        if (movePointsText != null) movePointsText.gameObject.SetActive(true);
        if (rangeText != null) rangeText.gameObject.SetActive(true);
        if (workPointsText != null) workPointsText.gameObject.SetActive(false);


        unitNameText.text = currentCombatUnit.data.unitName;
        unitTypeText.text = currentCombatUnit.data.unitType.ToString();
        SetUnitDescription(currentCombatUnit.data.description, currentCombatUnit.data.unitName);

        // Display unit icon
        if (unitIconImage != null)
        {
            unitIconImage.sprite = currentCombatUnit.data != null
                ? currentCombatUnit.data.GetIcon(currentCombatUnit.owner) : null;
            unitIconImage.enabled = unitIconImage.sprite != null;
        }
        levelText.text = $"{currentCombatUnit.level}";
        experienceText.text = $"XP: {currentCombatUnit.experience}/{currentCombatUnit.data.xpToNextLevel[currentCombatUnit.level - 1]}";
        
        attackText.text = $"{currentCombatUnit.CurrentAttack}";
        defenseText.text = $"{currentCombatUnit.CurrentDefense}";
        healthText.text = $"{currentCombatUnit.currentHealth}/{currentCombatUnit.MaxHealth}";
        if (movePointsText != null) movePointsText.text = $"{currentCombatUnit.currentMovePoints}/{currentCombatUnit.GetTurnMovementBudget()}";
        rangeText.text = $"{currentCombatUnit.CurrentRange}";
        if (attackPointsText != null)
        {
            attackPointsText.gameObject.SetActive(true);
            attackPointsText.text = $"AP: {currentCombatUnit.CurrentAttackPoints}/{currentCombatUnit.MaxAttackPoints}";
        }

        // Orbit status & controls
        UpdateOrbitControls(currentCombatUnit);
        UpdateUpgradeActionState(currentCombatUnit);
        UpdateFortifyActionState(currentCombatUnit);
        UpdateStackInfo(currentCombatUnit);
        campaignArmyPanel?.Refresh();
    }

    private void UpdateUnitInfoForWorkerUnit()
    {
        if (currentWorkerUnit == null) return;

        // Ensure common and worker-specific fields are visible, hide others
        if (unitNameText != null) unitNameText.gameObject.SetActive(true);
        if (unitTypeText != null) unitTypeText.gameObject.SetActive(true);
        if (healthText != null) healthText.gameObject.SetActive(true);
        if (movePointsText != null) movePointsText.gameObject.SetActive(true);
        if (attackText != null) attackText.gameObject.SetActive(true); // Show attack for worker
        if (defenseText != null) defenseText.gameObject.SetActive(true); // Show defense for worker

        if (workPointsText != null) workPointsText.gameObject.SetActive(true);

        if (levelText != null) levelText.gameObject.SetActive(true);
        if (experienceText != null) experienceText.gameObject.SetActive(true);
        if (rangeText != null)
        {
            rangeText.gameObject.SetActive(true);
            bool hasJob = ImprovementManager.Instance != null
                && ImprovementManager.Instance.HasAnyJobAtTile(currentWorkerUnit.currentTileIndex, currentWorkerUnit.planetIndex);
            bool assigned = hasJob && ImprovementManager.Instance.JobAssignedToWorker(
                currentWorkerUnit.currentTileIndex, currentWorkerUnit, currentWorkerUnit.planetIndex);
            rangeText.text = assigned ? "Work: Assigned" : hasJob ? "Work: Available" : "Work: Idle";
        }


        unitNameText.text = currentWorkerUnit.data.unitName;
        unitTypeText.text = "Worker Unit";
        SetUnitDescription(currentWorkerUnit.data.description, currentWorkerUnit.data.unitName);

        // Display worker icon (was missing, causing stale/blank icon in worker panel)
        if (unitIconImage != null)
        {
            unitIconImage.sprite = currentWorkerUnit.data != null ? currentWorkerUnit.data.GetIcon(currentWorkerUnit.owner) : null;
            unitIconImage.enabled = unitIconImage.sprite != null;
        }
        
        healthText.text = $"{currentWorkerUnit.currentHealth}/{currentWorkerUnit.MaxHealth}";
        movePointsText.text = $"{currentWorkerUnit.currentMovePoints}/{currentWorkerUnit.GetTurnMovementBudget()}";
        attackText.text = $"{currentWorkerUnit.CurrentAttack}";
        defenseText.text = $"{currentWorkerUnit.CurrentDefense}";
        if (levelText != null) levelText.text = $"Level: {currentWorkerUnit.level}";
        if (experienceText != null)
        {
            int nextXp = currentWorkerUnit.ExperienceToNextLevel;
            experienceText.text = nextXp == int.MaxValue
                ? $"XP: {currentWorkerUnit.experience}"
                : $"XP: {currentWorkerUnit.experience}/{nextXp}";
        }
        if (attackPointsText != null)
        {
            attackPointsText.gameObject.SetActive(true);
            attackPointsText.text = $"{currentWorkerUnit.CurrentAttackPoints}/{currentWorkerUnit.MaxAttackPoints}";
        }

        if (workPointsText != null)
        {
            workPointsText.text = $"{currentWorkerUnit.currentWorkPoints}/{currentWorkerUnit.MaxWorkPoints}";
        }
        LogWorkerBuildDebug(currentWorkerUnit);
        PopulateWorkerBuildUnits(currentWorkerUnit);
        PopulateUnitBuildDropdown(currentWorkerUnit);
        UpdateWorkerActionStates(currentWorkerUnit);
        UpdateFortifyActionState(currentWorkerUnit);
        UpdateStackInfo(currentWorkerUnit);
        campaignArmyPanel?.Refresh();
    }
    

    private void UpdateUpgradeActionState(CombatUnit combatUnit)
    {
        if (upgradeButton == null) return;

        upgradeButton.onClick.RemoveAllListeners();
        upgradeButton.gameObject.SetActive(true);

        CombatUnitData target = null;
        int cost = 0;
        string reason = null;
        bool canUpgrade = combatUnit != null && combatUnit.CanUpgrade(out target, out cost, out reason);
        upgradeButton.interactable = canUpgrade;

        string title = target != null ? $"Upgrade to {target.unitName}" : "Upgrade";
        string description = canUpgrade
            ? $"Pay {cost} gold to upgrade this unit. Current HP is preserved."
            : (string.IsNullOrWhiteSpace(reason) ? "No unlocked upgrade is available." : $"Cannot upgrade: {reason}.");
        AddTooltipToButton(upgradeButton, title, description);

        if (canUpgrade)
            upgradeButton.onClick.AddListener(OnUpgradeClicked);
    }

    private void OnUpgradeClicked()
    {
        if (currentCombatUnit == null) return;
        if (currentCombatUnit.TryUpgradeToLatest())
            UpdateUnitInfoForCombatUnit();
    }

    private void OnSettleCityClicked()
    {
        if (currentWorkerUnit == null) return;
        var placementPreview = PlacementPreview.EnsureInstance();
        placementPreview.EnterCityMode(currentWorkerUnit, null,
            onConfirm: () => HidePanel(),
            onCancel: null);
    }

    private void OnBuildHerdClicked()
    {
        if (currentWorkerUnit == null) return;
        currentWorkerUnit.ClearFortify();
        // WorkerUnit.StartBuildingHerd will instantiate the herd prefab at the worker's tile
        currentWorkerUnit.StartBuildingHerd(null);
        UpdateUnitInfoForWorkerUnit();
    }

    private void OnFortifyClicked()
    {
        BaseUnit unit = currentCombatUnit != null ? (BaseUnit)currentCombatUnit : currentWorkerUnit;
        if (unit == null) return;

        if (unit is CombatUnit)
        {
            campaignArmyPanel?.ToggleDefenseFromUnitInfo();
            UpdateUnitInfoForCombatUnit();
            return;
        }

        unit.Fortify();

        if (currentCombatUnit != null) UpdateUnitInfoForCombatUnit();
        else if (currentWorkerUnit != null) UpdateUnitInfoForWorkerUnit();
    }

    private void OnUnstackClicked()
    {
        BaseUnit unit = currentCombatUnit != null ? (BaseUnit)currentCombatUnit : currentWorkerUnit;
        if (unit == null) return;

        if (unit.Unstack())
        {
            // Refresh panel after splitting the selected member into a new army.
            if (currentCombatUnit != null) UpdateUnitInfoForCombatUnit();
            else if (currentWorkerUnit != null) UpdateUnitInfoForWorkerUnit();
            UpdateStackInfo(unit);
            // Re-select the unit at its new location
            if (UnitSelectionManager.Instance != null)
                UnitSelectionManager.Instance.SelectUnit(unit);
        }
        else
        {
            if (UIManager.Instance != null)
                UIManager.Instance.ShowNotification("Cannot split army: select a non-representative member and ensure an adjacent tile is free.");
        }
    }

    /// <summary>
    /// Update the army roster indicator and split button visibility.
    /// </summary>
    private void UpdateStackInfo(BaseUnit unit)
    {
        if (unit == null)
        {
            if (stackInfoText != null) stackInfoText.gameObject.SetActive(false);
            if (unstackButton != null) unstackButton.gameObject.SetActive(false);
            return;
        }

        // Army roster info
        var companions = unit.GetStackedUnits();
        int totalInStack = companions.Count + 1;

        if (totalInStack <= 1)
        {
            if (stackInfoText != null) stackInfoText.gameObject.SetActive(false);
            if (unstackButton != null) unstackButton.gameObject.SetActive(false);
            return;
        }

        if (stackInfoText != null)
        {
            stackInfoText.gameObject.SetActive(true);
            int capacity = unit is CombatUnit combat && combat.owner != null
                ? combat.owner.GetMaxArmySize()
                : totalInStack;
            stackInfoText.text = $"Army: {totalInStack}/{capacity}  Member {unit.stackSlot + 1}  [Tab to cycle]";
        }

        if (unstackButton != null)
        {
            // Campaign armies split contextually by selecting HUD cards and moving them.
            // Keep the serialized legacy control for save/prefab compatibility, but do not
            // expose BaseUnit.Unstack as an army action.
            unstackButton.gameObject.SetActive(unit is not CombatUnit);
            unstackButton.interactable = false;
        }

        stackOrderPanel?.Refresh(null);
        if (unstackButton != null) unstackButton.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (settleCityButton != null)
            settleCityButton.onClick.RemoveListener(OnSettleCityClicked);
        if (forageButton != null)
            forageButton.onClick.RemoveListener(OnForageClicked);
        if (fortifyButton != null)
            fortifyButton.onClick.RemoveListener(OnFortifyClicked);
        if (enterOrbitButton != null)
            enterOrbitButton.onClick.RemoveListener(OnEnterOrbitClicked);
        if (exitOrbitButton != null)
            exitOrbitButton.onClick.RemoveListener(OnExitOrbitClicked);
        if (unstackButton != null)
            unstackButton.onClick.RemoveListener(OnUnstackClicked);
        if (unitBuildDropdown != null)
            unitBuildDropdown.onValueChanged.RemoveAllListeners();

        UnsubscribeFromGameEvents();
    }
    

    private void HandleUnitMovedEvent(GameEventManager.UnitMovementEventArgs args)
    {
        if (args == null || args.Unit == null) return;

        if (IsDisplayedArmyMember(args.Unit)) RefreshDisplayedUnit();
    }

    private void HandleMovePointsChanged(GameEventManager.MovePointsChangedEventArgs args)
    {
        if (args == null || args.Unit == null) return;

        if (IsDisplayedArmyMember(args.Unit)) RefreshDisplayedUnit();
    }

    private void HandleAttackPointsChanged(GameEventManager.UnitValueChangedEventArgs args)
    {
        if (args == null || args.Unit == null) return;

        if (IsDisplayedArmyMember(args.Unit)) RefreshDisplayedUnit();
    }

    private void HandleHealthChanged(GameEventManager.UnitValueChangedEventArgs args)
    {
        if (args == null || args.Unit == null) return;

        if (IsDisplayedArmyMember(args.Unit)) RefreshDisplayedUnit();
    }

    private void HandleCombatEvent(GameEventManager.CombatEventArgs args)
    {
        if (args == null) return;

        if (IsDisplayedArmyMember(args.Attacker) || IsDisplayedArmyMember(args.Defender)) RefreshDisplayedUnit();
    }

    private bool IsDisplayedArmyMember(MonoBehaviour value)
    {
        if (value is not BaseUnit unit) return false;
        if (unit == currentCombatUnit || unit == currentWorkerUnit) return true;
        if (currentCombatUnit != null && unit is CombatUnit combat)
            return combat.MilitaryFormationId == currentCombatUnit.MilitaryFormationId;
        if (currentCombatUnit != null && unit is WorkerUnit worker)
            return worker.AttachedArmyFormationId == currentCombatUnit.MilitaryFormationId;
        return currentWorkerUnit != null && unit is WorkerUnit displayedWorker
            && displayedWorker.AttachedArmyFormationId == currentWorkerUnit.AttachedArmyFormationId
            && !string.IsNullOrEmpty(currentWorkerUnit.AttachedArmyFormationId);
    }

    private void RefreshDisplayedUnit()
    {
        if (!IsVisible) return;
        if (currentCombatUnit != null) UpdateUnitInfoForCombatUnit();
        else if (currentWorkerUnit != null) UpdateUnitInfoForWorkerUnit();
    }

    private void HandleTurnChanged() => RefreshDisplayedUnit();

    private void HandleUnitLost(GameEventManager.UnitLostEventArgs args)
    {
        if (args?.Unit == null || !IsDisplayedArmyMember(args.Unit)) return;
        bool isMapSelection = UnitSelectionManager.Instance != null
            && UnitSelectionManager.Instance.GetSelectedUnit() == args.Unit;
        if (args.Unit == currentCombatUnit && currentCombatUnit != null)
        {
            var survivingMember = CampaignArmyService.GetMembers(currentCombatUnit)
                .FirstOrDefault(member => member != null && member != currentCombatUnit && member.currentHealth > 0);
            if (survivingMember != null && !isMapSelection)
            {
                currentCombatUnit = CampaignArmyService.GetRepresentative(survivingMember);
                UpdateUnitInfoForCombatUnit();
                return;
            }
            if (isMapSelection) UnitSelectionManager.Instance.DeselectUnit();
            else HidePanel();
            return;
        }
        if (args.Unit == currentWorkerUnit && currentWorkerUnit != null)
        {
            CombatUnit attachedArmy = FindAttachedArmy(currentWorkerUnit);
            if (attachedArmy != null && !isMapSelection)
            {
                currentWorkerUnit = null;
                currentCombatUnit = attachedArmy;
                PopulateForCombatUnit(attachedArmy);
                return;
            }
            if (isMapSelection) UnitSelectionManager.Instance.DeselectUnit();
            else HidePanel();
            return;
        }
        RefreshDisplayedUnit();
    }

    private void HideAllSections()
    {
        // Reset all stat text fields to hidden so the populate methods can selectively show what they need
        if (unitNameText != null) unitNameText.gameObject.SetActive(false);
        if (unitTypeText != null) unitTypeText.gameObject.SetActive(false);
        if (levelText != null) levelText.gameObject.SetActive(false);
        if (experienceText != null) experienceText.gameObject.SetActive(false);
        if (attackText != null) attackText.gameObject.SetActive(false);
        if (defenseText != null) defenseText.gameObject.SetActive(false);
        if (healthText != null) healthText.gameObject.SetActive(false);
        if (movePointsText != null) movePointsText.gameObject.SetActive(false);
        if (attackPointsText != null) attackPointsText.gameObject.SetActive(false);
        if (rangeText != null) rangeText.gameObject.SetActive(false);

        // Hide action buttons
        if (settleCityButton != null) settleCityButton.gameObject.SetActive(false);
        if (forageButton != null) forageButton.gameObject.SetActive(false);
        if (fortifyButton != null) fortifyButton.gameObject.SetActive(false);
        if (buildOptionsDropdown != null) buildOptionsDropdown.gameObject.SetActive(false);
        if (unitBuildDropdown != null) unitBuildDropdown.gameObject.SetActive(false);
        if (buildHerdButton != null) buildHerdButton.gameObject.SetActive(false);
        if (captureButton != null) captureButton.gameObject.SetActive(false);
        // Keep primary build button visible but disabled when hidden sections are active
        if (startBuildButton != null) { startBuildButton.gameObject.SetActive(true); startBuildButton.interactable = false; }

        // Hide orbit controls
        if (orbitStatusText != null) orbitStatusText.gameObject.SetActive(false);
        if (enterOrbitButton != null) enterOrbitButton.gameObject.SetActive(false);
        if (exitOrbitButton != null) exitOrbitButton.gameObject.SetActive(false);
        if (upgradeButton != null) upgradeButton.gameObject.SetActive(false);

        // (Removed obsolete buildUnitsContainer/buildUnitButtons cleanup)
    }

    private void PopulateForCombatUnit(CombatUnit combatUnit)
    {
        // Implement the logic to populate the panel for a CombatUnit
        // This is a placeholder and should be replaced with the actual implementation
UpdateUnitInfoForCombatUnit();
        // Capture button: if another selected unit (actor) exists, the displayed unit is captureable,
        // and the actor is adjacent to the target.
        if (captureButton != null)
        {
            captureButton.onClick.RemoveAllListeners();
            var player=CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
            bool canIntegrate=CrisisManager.Instance != null && CrisisManager.Instance.CanIntegrateCrisisActor(player,combatUnit);
            if (canIntegrate)
            {
                captureButton.gameObject.SetActive(true);
                captureButton.interactable=true;
                var label=captureButton.GetComponentInChildren<TextMeshProUGUI>(); if (label != null) label.text="Offer Integration";
                AddTooltipToButton(captureButton,"Offer Integration","Peacefully incorporate this Mutant crisis actor into your civilization.");
                captureButton.onClick.AddListener(()=>CrisisManager.Instance.TryIntegrateCrisisActor(player,combatUnit));
                return;
            }
            var actor = UnitSelectionManager.Instance != null ? UnitSelectionManager.Instance.GetSelectedUnit() : null;
            bool canCapture = actor != null && actor != combatUnit && combatUnit.data != null && combatUnit.data.captureable && AreAdjacent(actor, combatUnit);
            captureButton.gameObject.SetActive(canCapture);
            captureButton.interactable = canCapture;
            AddTooltipToButton(captureButton, "Capture", "Capture an adjacent animal or worker into a herd (requires adjacency).");
            if (canCapture)
            {
                captureButton.onClick.AddListener(() => OnCaptureClicked(actor, combatUnit));
            }
        }
    }

    private void PopulateForWorkerUnit(WorkerUnit workerUnit)
    {
UpdateUnitInfoForWorkerUnit();

        if (forageButton != null)
        {
            forageButton.gameObject.SetActive(true);
            bool canForageNow = false;
            if (workerUnit != null)
            {
                int tile = workerUnit.currentTileIndex;
                var ts = TileSystem.GetForPlanet(workerUnit.planetIndex) ?? TileSystem.Instance;
                var td = ts != null ? ts.GetTileData(tile) : null;
                var resData = td != null ? td.resource : null;
                canForageNow = resData != null && workerUnit.CanForage(resData, tile);
                if (!canForageNow)
                {
                    // Forage not available on this tile.
                }
            }
            forageButton.interactable = canForageNow;
            AddTooltipToButton(forageButton, "Forage", canForageNow
                ? "Harvest resources from this tile."
                : "No suitable forageable resource is available here.");
        }

        // Worker herd creation: use a single herd button (behaves like SettleCity)
        if (buildHerdButton != null)
        {
            buildHerdButton.onClick.RemoveAllListeners();
            buildHerdButton.gameObject.SetActive(true);
            bool canCreateHerd = false;
            if (workerUnit != null && workerUnit.owner != null)
            {
                canCreateHerd = workerUnit.owner.herdsEnabled
                    && workerUnit.owner.civData != null && workerUnit.owner.civData.herdPrefab != null
                    && CanCreateHerdAtCurrentTile(workerUnit);
            }
            buildHerdButton.interactable = canCreateHerd;
            var herdLabel = buildHerdButton.GetComponentInChildren<TextMeshProUGUI>();
            if (herdLabel != null) herdLabel.text = "Create Herd";
            AddTooltipToButton(buildHerdButton, "Create Herd", "Create a herd at this tile (requires a civ herd prefab and herding tech). Herds cannot share the same tile.");
            if (canCreateHerd)
                buildHerdButton.onClick.AddListener(OnBuildHerdClicked);
        }

        // Capture button: allow other selected unit to capture this worker if captureable and adjacent
        if (captureButton != null)
        {
            captureButton.onClick.RemoveAllListeners();
            var actor = UnitSelectionManager.Instance != null ? UnitSelectionManager.Instance.GetSelectedUnit() : null;
            bool canCapture = actor != null && actor != workerUnit && workerUnit.data != null && workerUnit.data.captureable && AreAdjacent(actor, workerUnit);
            captureButton.gameObject.SetActive(canCapture);
            captureButton.interactable = canCapture;
            if (canCapture)
            {
                captureButton.onClick.AddListener(() => OnCaptureClicked(actor, workerUnit));
            }
        }

    }

    private static bool CanCreateHerdAtCurrentTile(WorkerUnit worker)
    {
        if (worker == null || worker.currentTileIndex < 0) return false;
        var tileSystem = TileSystem.GetForPlanet(worker.planetIndex) ?? TileSystem.Instance;
        if (tileSystem == null || tileSystem.GetTileData(worker.currentTileIndex) == null) return false;
        var occupant = TileOccupancyManager.GetOccupantObjectForTileWithFallback(
            worker.currentTileIndex, TileLayer.Surface, worker.planetIndex);
        return occupant == null || occupant == worker.gameObject;
    }

    private bool AreAdjacent(BaseUnit a, BaseUnit b)
    {
        if (a == null || b == null) return false;
        if (a.planetIndex != b.planetIndex) return false;
        var ts = TileSystem.GetForPlanet(a.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return false;
        try { return ts.GetTileDistance(a.currentTileIndex, b.currentTileIndex) == 1; } catch { return false; }
    }

    private void UpdateWorkerActionStates(WorkerUnit workerUnit)
    {
        if (workerUnit == null) return;

        if (settleCityButton != null)
        {
            bool canEverSettle = workerUnit.data != null && workerUnit.data.canFoundCity;
            settleCityButton.gameObject.SetActive(canEverSettle);
            settleCityButton.interactable = canEverSettle && workerUnit.CanFoundCityOnCurrentTile();
            AddTooltipToButton(settleCityButton, "Found City", canEverSettle
                ? settleCityButton.interactable ? "Found a city here. Consumes this worker." : "This tile is not valid for founding a city."
                : "This worker cannot found cities.");
        }

        if (buildOptionsDropdown != null)
        {
            buildOptionsDropdown.gameObject.SetActive(false);
        }

        // Use the single Start/Contribute button for both starting builds and contributing work
        if (startBuildButton != null)
        {
            bool hasJob = ImprovementManager.Instance != null &&
                          ImprovementManager.Instance.HasAnyJobAtTile(workerUnit.currentTileIndex, workerUnit.planetIndex);
            // Improvement placement now starts immediately from the dropdown.
            // Keep this button for contributing work and unit build placement.
            startBuildButton.gameObject.SetActive(true);
            var txt = startBuildButton.GetComponentInChildren<TextMeshProUGUI>();
            string actionLabel = hasJob ? "Contribute Work"
                : pendingUnitBuildIndex >= 0 ? "Start Unit Build" : "Build";
            if (txt != null) txt.text = actionLabel;
            AddTooltipToButton(startBuildButton, actionLabel, hasJob
                ? workerUnit.currentWorkPoints > 0 ? "Contribute this worker's available work points to the assigned job." : "No work points remain this turn."
                : pendingUnitBuildIndex >= 0 ? "Place the selected unit build on a valid adjacent tile."
                : buildOptions.Count > 0 ? "Choose an improvement to begin placement." : "No improvement is available here.");
        }
        RefreshStartBuildButtonState(workerUnit);
        // Legacy contribute button removed; primary start button handles contribute/start behavior
    }

    private void RefreshStartBuildButtonState(WorkerUnit workerUnit)
    {
        if (startBuildButton == null) return;

        bool hasJob = workerUnit != null && ImprovementManager.Instance != null &&
                      ImprovementManager.Instance.HasAnyJobAtTile(workerUnit.currentTileIndex, workerUnit.planetIndex);
        if (hasJob)
        {
            startBuildButton.interactable = workerUnit.currentWorkPoints > 0;
            return;
        }

        bool canStart = false;
        if (workerUnit != null && workerUnit.currentWorkPoints > 0)
        {
            if (pendingUnitBuildIndex >= 0 && pendingUnitBuildIndex < unitBuildOptions.Count)
                canStart = unitBuildOptions[pendingUnitBuildIndex].isAvailable;
            else canStart = buildOptions.Count > 0;
        }

        startBuildButton.interactable = canStart;
    }

    private void UpdateFortifyActionState(BaseUnit unit)
    {
        if (fortifyButton == null) return;

        fortifyButton.gameObject.SetActive(unit != null);
        bool isArmy = unit is CombatUnit;
        bool defended = unit is CombatUnit combat
            ? CampaignArmyService.IsInDefenseMode(combat)
            : unit != null && unit.IsFortified;
        fortifyButton.interactable = unit != null && !unit.isStored && !unit.isMoving && (isArmy || !defended);

        var txt = fortifyButton.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
            txt.text = isArmy ? defended ? "Defense: On" : "Defense Mode"
                : defended ? "Fortified" : "Fortify";
        AddTooltipToButton(fortifyButton, isArmy ? "Defense Mode" : "Fortify",
            isArmy ? defended ? "Exit defense mode for this army." : "Put every combat member in defense mode."
                : "Fortify this worker to gain defense and skip its action.");
    }


    private void OnForageClicked()
    {
        if (currentWorkerUnit == null) return;
        currentWorkerUnit.ClearFortify();

        // Try current tile first then adjacent tiles
        var rm = ResourceManager.Instance;
        if (rm == null) return;

        // Only attempt to forage the tile the worker is standing on
        int tile = currentWorkerUnit.currentTileIndex;
        var inst = rm.GetResourceInstanceAtTile(tile);
        if (inst != null && currentWorkerUnit.CanForage(inst.data, tile))
        {
            currentWorkerUnit.Forage(inst.data, tile);
            rm.ForageResource(inst, currentWorkerUnit.owner);
            UpdateUnitInfoForWorkerUnit();
        }
    }

    private void OnCaptureClicked(BaseUnit actor, BaseUnit target)
    {
        if (actor == null || target == null) return;

        var attackerCiv = actor.owner;
        if (attackerCiv == null) return;

        CombatUnitData targetCombatData = target as CombatUnit != null ? (target as CombatUnit).data : null;
        WorkerUnitData targetWorkerData = target as WorkerUnit != null ? (target as WorkerUnit).data : null;
        var targetData = (CombatUnitData)targetCombatData ?? (CombatUnitData)(object)targetWorkerData; // prefer combat data shape; worker data may be null
        if ((targetCombatData == null && targetWorkerData == null) || !( (targetCombatData!=null && targetCombatData.captureable) || (targetWorkerData!=null && targetWorkerData.captureable) ))
        {
            Destroy(target.gameObject);
            return;
        }

        int herdCount = 0;
        if (targetCombatData != null) herdCount = targetCombatData.captureHerdCount;
        else if (targetWorkerData != null) herdCount = targetWorkerData.captureHerdCount;
        if (herdCount > 0)
        {
            if (targetCombatData != null)
                attackerCiv.AddAnimalsToNearestHerd(targetCombatData, herdCount, target.planetIndex, target.currentTileIndex);
            else if (targetWorkerData != null)
                attackerCiv.AddAnimalsToNearestHerd(targetWorkerData, herdCount, target.planetIndex, target.currentTileIndex);
        }

        // Destroy the captured unit GameObject without triggering normal kill flow
        Destroy(target.gameObject);

        // Consume action if actor is a CombatUnit
        if (actor is CombatUnit combatActor)
        {
            try { combatActor.ConsumeAction(); } catch { }
        }

        // Refresh displayed info
        if (currentCombatUnit != null) UpdateUnitInfoForCombatUnit();
        if (currentWorkerUnit != null) UpdateUnitInfoForWorkerUnit();
    }

    private void PopulateWorkerBuildUnits(WorkerUnit worker)
    {
        if (buildOptionsDropdown == null || worker == null) return;

        buildOptions.Clear();
        var civ = worker.owner;
        if (civ == null) return;

        var options = new List<string>();

        // Improvements
        var improvements = civ.GetAvailableImprovementsForWorker(worker.data, worker.currentTileIndex, worker.planetIndex);
        if (improvements != null && improvements.Count > 0)
        {
            foreach (var imp in improvements)
            {
                if (imp == null) continue;
                buildOptions.Add(new BuildOption {
                    Type = BuildOption.OptionType.Improvement,
                    Improvement = imp,
                    Display = $"Build {imp.improvementName} ({imp.workCost} WP)",
                    IsAvailable = true
                });
                options.Add($"Build {imp.improvementName} ({imp.workCost} WP)");
            }
        }

        // Note: unit builds are intentionally NOT included in the general "build options"
        // dropdown. Units have their own dedicated `unitBuildDropdown` populated by
        // `PopulateUnitBuildDropdown()` so remove units from here to avoid duplication.

        if (options.Count == 0)
        {
            suppressBuildOptionCallback = true;
            buildOptionsDropdown.ClearOptions();
            buildOptionsDropdown.options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("No build options available") };
            buildOptionsDropdown.SetValueWithoutNotify(0);
            suppressBuildOptionCallback = false;
            buildOptionsDropdown.interactable = false;
            if (startBuildButton != null) { startBuildButton.gameObject.SetActive(false); startBuildButton.interactable = false; }
        }
        else
        {
            var displayOptions = new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData("Select build option...")
            };
            for (int i = 0; i < buildOptions.Count; i++)
            {
                var buildOption = buildOptions[i];
                Sprite optionIcon = buildOption.Type == BuildOption.OptionType.Improvement
                    && buildOption.Improvement != null
                    ? buildOption.Improvement.GetIcon(civ)
                    : null;
                displayOptions.Add(new TMP_Dropdown.OptionData(buildOption.Display, optionIcon, Color.white));
            }
            suppressBuildOptionCallback = true;
            buildOptionsDropdown.ClearOptions();
            buildOptionsDropdown.AddOptions(displayOptions);

            // If this worker is already assigned to a build job on this tile, pre-select that option
            int preselect = 0;
            if (ImprovementManager.Instance != null && worker != null)
            {
                bool assigned = ImprovementManager.Instance.JobAssignedToWorker(worker.currentTileIndex, worker, worker.planetIndex);
                if (assigned)
                {
                    var jobImp = ImprovementManager.Instance.GetBuildJobDataAtTile(worker.currentTileIndex, worker.planetIndex);
                    if (jobImp != null)
                    {
                        int found = buildOptions.FindIndex(b => b.Type == BuildOption.OptionType.Improvement && b.Improvement == jobImp);
                        if (found >= 0) preselect = found + 1; // +1 because index 0 is the placeholder
                    }
                }
            }

            buildOptionsDropdown.SetValueWithoutNotify(preselect);
            suppressBuildOptionCallback = false;
            buildOptionsDropdown.interactable = true;
            if (startBuildButton != null) startBuildButton.gameObject.SetActive(true);
        }
        buildOptionsDropdown.gameObject.SetActive(false);
        RefreshStartBuildButtonState(worker);
    }

    private void OnBuildOptionSelected(int idx)
    {
        if (suppressBuildOptionCallback || buildOptions == null || currentWorkerUnit == null) return;
        if (idx <= 0 || idx > buildOptions.Count)
        {
            ClearImprovementPlacementSelection();
            RefreshStartBuildButtonState(currentWorkerUnit);
            return;
        }

        pendingBuildIndex = idx - 1;
        pendingUnitBuildIndex = -1;
        buildOptionsDropdown.gameObject.SetActive(false);
        var opt = buildOptions[pendingBuildIndex];
        if (opt.Type == BuildOption.OptionType.Improvement && opt.Improvement != null)
            BeginImprovementPlacement(opt.Improvement);
        RefreshStartBuildButtonState(currentWorkerUnit);
    }

    private void OnStartBuildButtonClicked()
    {
        if (currentWorkerUnit == null) return;
        currentWorkerUnit.ClearFortify();

        // If there's an existing job on this tile, treat the button as "Contribute Work"
        bool hasJob = ImprovementManager.Instance != null &&
                      ImprovementManager.Instance.HasAnyJobAtTile(currentWorkerUnit.currentTileIndex, currentWorkerUnit.planetIndex);
        if (hasJob)
        {
            OnContributeWorkClicked();
            return;
        }

        // Otherwise behave as Start Build (require a pending selection)
        // First, check if a unit build is pending (unitBuildDropdown)
        if (pendingUnitBuildIndex >= 0 && unitBuildOptions != null && pendingUnitBuildIndex < unitBuildOptions.Count)
        {
            var entry = unitBuildOptions[pendingUnitBuildIndex];
            pendingUnitBuildIndex = -1;
            RefreshStartBuildButtonState(currentWorkerUnit);
            if (!entry.isAvailable) return;
            if (entry.isCombat && entry.combatData != null) OnStartWorkerBuildUnit(entry.combatData);
            else if (!entry.isCombat && entry.workerData != null) OnStartWorkerBuildWorker(entry.workerData);
            return;
        }

        if (buildOptions.Count > 0 && buildOptionsDropdown != null)
        {
            buildOptionsDropdown.gameObject.SetActive(true);
            buildOptionsDropdown.Show();
        }

    }

    private void ClearImprovementPlacementSelection()
    {
        pendingBuildIndex = -1;
        if (PlacementPreview.Instance != null && PlacementPreview.Instance.IsActive)
            PlacementPreview.Instance.Cancel();
    }

    private void BeginImprovementPlacement(ImprovementData improvement)
    {
        if (currentWorkerUnit == null || improvement == null) return;

        PlacementPreview.EnsureInstance().EnterImprovementMode(
            currentWorkerUnit,
            improvement,
            onConfirm: () =>
            {
                pendingBuildIndex = -1;
                if (buildOptionsDropdown != null)
                    buildOptionsDropdown.SetValueWithoutNotify(0);
                UpdateUnitInfoForWorkerUnit();
            },
            onCancel: () =>
            {
                pendingBuildIndex = -1;
                if (buildOptionsDropdown != null)
                    buildOptionsDropdown.SetValueWithoutNotify(0);
                RefreshStartBuildButtonState(currentWorkerUnit);
            });
    }

    private void OnContributeWorkClicked()
    {
        if (currentWorkerUnit == null) return;
        // Contribute to either improvement or unit job on current tile
        currentWorkerUnit.ContributeWork();
        currentWorkerUnit.ContributeWorkToUnit();
        currentWorkerUnit.ContributeWorkToWorker();
        UpdateUnitInfoForWorkerUnit();
    }


    private void OnStartWorkerBuildWorker(WorkerUnitData workerData)
    {
        if (currentWorkerUnit == null || workerData == null) return;
        PlacementPreview.EnsureInstance().EnterWorkerUnitMode(currentWorkerUnit, workerData,
            onConfirm: () => UpdateUnitInfoForWorkerUnit());
    }

    private void OnStartWorkerBuildUnit(CombatUnitData unitData)
    {
        if (currentWorkerUnit == null || unitData == null) return;
        PlacementPreview.EnsureInstance().EnterCombatUnitMode(currentWorkerUnit, unitData,
            onConfirm: () => UpdateUnitInfoForWorkerUnit());
    }

    private void OnStartWorkerBuildImprovement(ImprovementData imp)
    {
        if (currentWorkerUnit == null || imp == null) return;
        PlacementPreview.EnsureInstance().EnterImprovementMode(currentWorkerUnit, imp,
            onConfirm: () => UpdateUnitInfoForWorkerUnit());
    }

    // ===== ORBIT CONTROLS =====

    /// <summary>
    /// Update orbit status text and Enter/Exit Orbit button visibility for a combat unit.
    /// </summary>
    private void UpdateOrbitControls(CombatUnit unit)
    {
        if (unit == null || unit.data == null)
        {
            if (orbitStatusText != null) orbitStatusText.gameObject.SetActive(false);
            if (enterOrbitButton != null) enterOrbitButton.gameObject.SetActive(false);
            if (exitOrbitButton != null) exitOrbitButton.gameObject.SetActive(false);
            return;
        }

        bool isInOrbit = unit.IsInOrbit;
        bool canOrbit = unit.CanEnterOrbit(); // Spaceship type
        bool hasActed = unit.hasActedThisTurn;

        // Orbit status label
        if (orbitStatusText != null)
        {
            if (isInOrbit)
            {
                orbitStatusText.text = "IN ORBIT";
                orbitStatusText.gameObject.SetActive(true);
            }
            else if (canOrbit)
            {
                orbitStatusText.text = "Surface";
                orbitStatusText.gameObject.SetActive(true);
            }
            else
            {
                orbitStatusText.gameObject.SetActive(false);
            }
        }

        // Enter Orbit button: visible when unit can orbit and is currently on surface
        if (enterOrbitButton != null)
        {
            bool showEnter = canOrbit && !isInOrbit && !hasActed;
            enterOrbitButton.gameObject.SetActive(showEnter);
            enterOrbitButton.interactable = showEnter;
        }

        // Exit Orbit button: visible when unit is in orbit
        if (exitOrbitButton != null)
        {
            bool showExit = isInOrbit && !hasActed;
            exitOrbitButton.gameObject.SetActive(showExit);
            exitOrbitButton.interactable = showExit;
        }
    }

    private void OnEnterOrbitClicked()
    {
        if (currentCombatUnit == null) return;
        if (currentCombatUnit.hasActedThisTurn) return;
        if (!currentCombatUnit.CanEnterOrbit()) return;
        if (currentCombatUnit.IsInOrbit) return;

        currentCombatUnit.ClearFortify();
        currentCombatUnit.EnterOrbit(currentCombatUnit.currentTileIndex);
        currentCombatUnit.ConsumeAction();
        UpdateUnitInfoForCombatUnit();
        if (startBuildButton != null) startBuildButton.gameObject.SetActive(false);
        if (buildOptionsDropdown != null) buildOptionsDropdown.gameObject.SetActive(false);
        if (unitBuildDropdown != null) unitBuildDropdown.gameObject.SetActive(false);
        if (buildHerdButton != null) buildHerdButton.gameObject.SetActive(false);
    }

    private void OnExitOrbitClicked()
    {
        if (currentCombatUnit == null) return;
        if (currentCombatUnit.hasActedThisTurn) return;
        if (!currentCombatUnit.IsInOrbit) return;

        currentCombatUnit.ClearFortify();
        currentCombatUnit.ExitOrbit(currentCombatUnit.currentTileIndex);
        currentCombatUnit.ConsumeAction();
        UpdateUnitInfoForCombatUnit();
    }
}
