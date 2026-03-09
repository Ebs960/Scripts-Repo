using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UnitInfoPanel : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject unitInfoPanel;
    [SerializeField] private TextMeshProUGUI unitNameText;
    [SerializeField] private TextMeshProUGUI unitTypeText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI experienceText;
    
    [Header("Stats")]
    [SerializeField] private TextMeshProUGUI attackText;
    [SerializeField] private TextMeshProUGUI defenseText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI movePointsText;
    [SerializeField] private TextMeshProUGUI rangeText;
    [SerializeField] private TextMeshProUGUI moraleText;
    
    [Header("Worker Stats")]
    [SerializeField] private TextMeshProUGUI workPointsText;

    [Header("Actions")]
    [SerializeField] private Button settleCityButton;
    [SerializeField] private Button forageButton; // new forage action for workers
    [SerializeField] private Button startBuildButton; // starts selected build option (optional)
    [Header("Orbit Controls")]
    [SerializeField] private TextMeshProUGUI orbitStatusText;
    [SerializeField] private Button enterOrbitButton;
    [SerializeField] private Button exitOrbitButton;
    [Header("Worker Build Units UI")] 
    [SerializeField] private TMP_Dropdown buildOptionsDropdown; // TMP dropdown for build options
    [SerializeField] private Button contributeWorkButton; // applies work points this turn to current tile job (improvement or unit)

    private CombatUnit currentCombatUnit;
    private WorkerUnit currentWorkerUnit;
    // Dropdown option data
    private struct BuildOption
    {
        public enum OptionType { Improvement, CombatUnit, WorkerUnit }
        public OptionType Type;
        public ImprovementData Improvement;
        public CombatUnitData CombatUnit;
        public WorkerUnitData WorkerUnit;
        public string Display;
    }
    private List<BuildOption> buildOptions = new List<BuildOption>();
    private int pendingBuildIndex = -1;
    private bool suppressBuildOptionCallback;

    private void Awake()
    {
        if (settleCityButton != null)
            settleCityButton.onClick.AddListener(OnSettleCityClicked);

        if (contributeWorkButton != null)
            contributeWorkButton.onClick.AddListener(OnContributeWorkClicked);

        if (forageButton != null)
            forageButton.onClick.AddListener(OnForageClicked);

        if (enterOrbitButton != null)
            enterOrbitButton.onClick.AddListener(OnEnterOrbitClicked);
        if (exitOrbitButton != null)
            exitOrbitButton.onClick.AddListener(OnExitOrbitClicked);

        // On start, clear the panel to show a "no unit selected" state.
        ClearPanelInfo();
        if (buildOptionsDropdown != null)
        {
            buildOptionsDropdown.onValueChanged.RemoveAllListeners();
            buildOptionsDropdown.onValueChanged.AddListener(OnBuildOptionSelected);
            buildOptionsDropdown.gameObject.SetActive(false);
        }
        if (startBuildButton != null)
        {
            startBuildButton.onClick.RemoveAllListeners();
            startBuildButton.onClick.AddListener(OnStartBuildButtonClicked);
            startBuildButton.gameObject.SetActive(false);
            startBuildButton.interactable = false;
        }

        // Validate serialized fields at startup so missing inspector wiring is obvious in Console
        ValidateSerializedFields();

        // Subscribe to movement events so UI updates immediately when a selected unit moves
        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnUnitMoved += HandleUnitMovedEvent;
            GameEventManager.Instance.OnMovementCompleted += HandleUnitMovedEvent;
        }
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
        if (rangeText == null) Debug.LogWarning("[UnitInfoPanel] rangeText is not assigned in the Inspector.");
        if (moraleText == null) Debug.LogWarning("[UnitInfoPanel] moraleText is not assigned in the Inspector.");

        // Actions / Construction
        if (settleCityButton == null) Debug.LogWarning("[UnitInfoPanel] settleCityButton is not assigned in the Inspector.");
        if (forageButton == null) Debug.LogWarning("[UnitInfoPanel] forageButton is not assigned in the Inspector.");
        // (Removed obsolete buildUnitsContainer/buildUnitButtonPrefab warnings)
        if (contributeWorkButton == null) Debug.LogWarning("[UnitInfoPanel] contributeWorkButton is not assigned in the Inspector.");
    }

    public void ShowPanel(object unitObject)
    {
        if (unitInfoPanel != null) unitInfoPanel.SetActive(true); // Ensure the content view is active

        HideAllSections(); // Helper to hide all specific sections initially

        string unitNameForLog = "Unknown Unit";

    if (unitObject is CombatUnit combatUnit)
        {
            currentCombatUnit = combatUnit;
            currentWorkerUnit = null; // Ensure worker unit is cleared
            unitNameForLog = currentCombatUnit.data.unitName;
PopulateForCombatUnit(currentCombatUnit);
            if (settleCityButton != null) settleCityButton.gameObject.SetActive(false); // Hide for combat units
        }
        else if (unitObject is WorkerUnit workerUnit)
        {
            currentWorkerUnit = workerUnit;
            currentCombatUnit = null; // Ensure combat unit is cleared
            unitNameForLog = currentWorkerUnit.data.unitName;
PopulateForWorkerUnit(currentWorkerUnit);
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
unitInfoPanel.SetActive(true);
// Update common elements if any, or specific ones again if needed after activation
        // RefreshLayout(); // If you have dynamic content that needs a layout refresh
    }

    public void HidePanel()
    {
        // "Hiding" now means clearing the info to the default state.
        ClearPanelInfo();
    }

    private void ClearPanelInfo()
    {
        if (unitInfoPanel != null)
        {
            unitInfoPanel.SetActive(true); // Ensure the content container is always visible
        }

        // Set all text fields to their default "empty" state and ensure they are visible
        if (unitNameText != null) { unitNameText.text = "No Unit Selected"; unitNameText.gameObject.SetActive(true); }
        if (unitTypeText != null) { unitTypeText.text = "---"; unitTypeText.gameObject.SetActive(true); }
        if (levelText != null) { levelText.text = "Level: -"; levelText.gameObject.SetActive(true); }
        if (experienceText != null) { experienceText.text = "XP: -/-"; experienceText.gameObject.SetActive(true); }
        if (attackText != null) { attackText.text = "Attack: -"; attackText.gameObject.SetActive(true); }
        if (defenseText != null) { defenseText.text = "Defense: -"; defenseText.gameObject.SetActive(true); }
        if (healthText != null) { healthText.text = "Health: -/-"; healthText.gameObject.SetActive(true); }
        if (movePointsText != null) { movePointsText.text = "Move: -"; movePointsText.gameObject.SetActive(true); }
        if (rangeText != null) { rangeText.text = "Range: -"; rangeText.gameObject.SetActive(true); }
        if (moraleText != null) { moraleText.text = "Morale: -"; moraleText.gameObject.SetActive(true); }
        if (workPointsText != null) { workPointsText.text = "Work Points: -"; workPointsText.gameObject.SetActive(false); }

        // Hide buttons that require a unit
        if (settleCityButton != null) settleCityButton.gameObject.SetActive(false);
        if (buildOptionsDropdown != null)
        {
            buildOptionsDropdown.ClearOptions();
            buildOptionsDropdown.gameObject.SetActive(false);
        }
        if (contributeWorkButton != null) contributeWorkButton.gameObject.SetActive(false);

        // Hide orbit controls
        if (orbitStatusText != null) orbitStatusText.gameObject.SetActive(false);
        if (enterOrbitButton != null) enterOrbitButton.gameObject.SetActive(false);
        if (exitOrbitButton != null) exitOrbitButton.gameObject.SetActive(false);

        // Clear unit references
        currentCombatUnit = null;
        currentWorkerUnit = null;
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
        if (moraleText != null) moraleText.gameObject.SetActive(true);
        if (workPointsText != null) workPointsText.gameObject.SetActive(false);


        unitNameText.text = currentCombatUnit.data.unitName;
        unitTypeText.text = currentCombatUnit.data.unitType.ToString();
        levelText.text = $"Level: {currentCombatUnit.level}";
        experienceText.text = $"XP: {currentCombatUnit.experience}/{currentCombatUnit.data.xpToNextLevel[currentCombatUnit.level - 1]}";
        
        attackText.text = $"Attack: {currentCombatUnit.CurrentAttack}";
        defenseText.text = $"Defense: {currentCombatUnit.CurrentDefense}";
        healthText.text = $"Health: {currentCombatUnit.currentHealth}/{currentCombatUnit.MaxHealth}";
        if (movePointsText != null) movePointsText.text = $"Move Speed: {currentCombatUnit.moveSpeed:F1}";
        rangeText.text = $"Range: {currentCombatUnit.CurrentRange}";
        if (moraleText != null) moraleText.text = $"Ammo: {currentCombatUnit.currentAmmo}/{currentCombatUnit.data.maxAmmo}";

        // Orbit status & controls
        UpdateOrbitControls(currentCombatUnit);
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

        if (levelText != null) levelText.gameObject.SetActive(false);
        if (experienceText != null) experienceText.gameObject.SetActive(false);
        if (rangeText != null) rangeText.gameObject.SetActive(false);
        if (moraleText != null) moraleText.gameObject.SetActive(false);


        unitNameText.text = currentWorkerUnit.data.unitName;
        unitTypeText.text = "Worker Unit";
        
        healthText.text = $"Health: {currentWorkerUnit.currentHealth}/{currentWorkerUnit.data.baseHealth}";
        movePointsText.text = $"Move Points: {currentWorkerUnit.currentMovePoints}";
        attackText.text = $"Attack: {currentWorkerUnit.CurrentAttack}";
        defenseText.text = $"Defense: {currentWorkerUnit.CurrentDefense}";

        if (workPointsText != null)
        {
            workPointsText.text = $"Work Points: {currentWorkerUnit.currentWorkPoints}/{currentWorkerUnit.data.baseWorkPoints}";
        }
        PopulateWorkerBuildUnits(currentWorkerUnit);
        UpdateWorkerActionStates(currentWorkerUnit);
    }
    
    private void OnSettleCityClicked()
    {
        if (currentWorkerUnit != null)
        {
            currentWorkerUnit.FoundCity();
            HidePanel(); // Hide the panel, as the unit is consumed.
        }
    }

    private void OnDestroy()
    {
        if (settleCityButton != null)
            settleCityButton.onClick.RemoveListener(OnSettleCityClicked);
        if (contributeWorkButton != null)
            contributeWorkButton.onClick.RemoveListener(OnContributeWorkClicked);
        if (forageButton != null)
            forageButton.onClick.RemoveListener(OnForageClicked);
        if (enterOrbitButton != null)
            enterOrbitButton.onClick.RemoveListener(OnEnterOrbitClicked);
        if (exitOrbitButton != null)
            exitOrbitButton.onClick.RemoveListener(OnExitOrbitClicked);

        // Unsubscribe from movement events
        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnUnitMoved -= HandleUnitMovedEvent;
            GameEventManager.Instance.OnMovementCompleted -= HandleUnitMovedEvent;
        }
    }

    private void HandleUnitMovedEvent(GameEventManager.UnitMovementEventArgs args)
    {
        if (args == null || args.Unit == null) return;

        // If the moved unit is the one currently displayed, refresh its info
        if (currentWorkerUnit != null && args.Unit == currentWorkerUnit)
        {
            UpdateUnitInfoForWorkerUnit();
        }
        else if (currentCombatUnit != null && args.Unit == currentCombatUnit)
        {
            UpdateUnitInfoForCombatUnit();
        }
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
        if (rangeText != null) rangeText.gameObject.SetActive(false);
        if (moraleText != null) moraleText.gameObject.SetActive(false);

        // Hide action buttons
        if (settleCityButton != null) settleCityButton.gameObject.SetActive(false);
        if (forageButton != null) forageButton.gameObject.SetActive(false);
        if (contributeWorkButton != null) contributeWorkButton.gameObject.SetActive(false);

        // Hide orbit controls
        if (orbitStatusText != null) orbitStatusText.gameObject.SetActive(false);
        if (enterOrbitButton != null) enterOrbitButton.gameObject.SetActive(false);
        if (exitOrbitButton != null) exitOrbitButton.gameObject.SetActive(false);

        // (Removed obsolete buildUnitsContainer/buildUnitButtons cleanup)
    }

    private void PopulateForCombatUnit(CombatUnit combatUnit)
    {
        // Implement the logic to populate the panel for a CombatUnit
        // This is a placeholder and should be replaced with the actual implementation
UpdateUnitInfoForCombatUnit();
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
                    string resStr = resData != null ? resData.resourceName : "N/A";
                    Debug.Log($"[UnitInfoPanel] Forage disabled -> tile={tile} res={resStr} workerWP={workerUnit.currentWorkPoints} workerCanForage={workerUnit.data?.canForage} resCanBeForaged={(resData!=null?resData.canBeForaged.ToString():"N/A")} tileIsLand={(td!=null?td.isLand.ToString():"N/A")} workerTile={workerUnit.currentTileIndex} GamePlanet={GameManager.Instance?.currentPlanetIndex} resPlanet={workerUnit.planetIndex}");
                }
            }
            forageButton.interactable = canForageNow;
        }
    }

    private void UpdateWorkerActionStates(WorkerUnit workerUnit)
    {
        if (workerUnit == null) return;

        if (settleCityButton != null)
        {
            bool canEverSettle = workerUnit.data != null && workerUnit.data.canFoundCity;
            settleCityButton.gameObject.SetActive(canEverSettle);
            settleCityButton.interactable = canEverSettle && workerUnit.CanFoundCityOnCurrentTile();
        }

        if (buildOptionsDropdown != null)
        {
            buildOptionsDropdown.gameObject.SetActive(true);
        }

        if (contributeWorkButton != null)
        {
            bool hasJob = ImprovementManager.Instance != null &&
                          ImprovementManager.Instance.HasAnyJobAtTile(workerUnit.currentTileIndex, workerUnit.planetIndex);
            contributeWorkButton.gameObject.SetActive(true);
            contributeWorkButton.interactable = hasJob && workerUnit.currentWorkPoints > 0;
        }
    }


    private void OnForageClicked()
    {
        if (currentWorkerUnit == null) return;

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
                    Display = $"Build {imp.improvementName} ({imp.workCost} WP)"
                });
                options.Add($"Build {imp.improvementName} ({imp.workCost} WP)");
            }
        }

        // Combat Units
        var units = civ.unlockedCombatUnits;
        if (units != null)
        {
            foreach (var u in units)
            {
                if (u == null || !u.buildableByWorker || !worker.CanBuildUnit(u, worker.currentTileIndex)) continue;
                buildOptions.Add(new BuildOption {
                    Type = BuildOption.OptionType.CombatUnit,
                    CombatUnit = u,
                    Display = $"Build {u.unitName} ({u.workerWorkCost} WP)"
                });
                options.Add($"Build {u.unitName} ({u.workerWorkCost} WP)");
            }
        }

        // Worker Units
        var workerUnits = civ.unlockedWorkerUnits;
        if (workerUnits != null)
        {
            foreach (var wu in workerUnits)
            {
                if (wu == null || !wu.buildableByWorker || !worker.CanBuildWorker(wu, worker.currentTileIndex)) continue;
                buildOptions.Add(new BuildOption {
                    Type = BuildOption.OptionType.WorkerUnit,
                    WorkerUnit = wu,
                    Display = $"Build {wu.unitName} ({wu.workerWorkCost} WP)"
                });
                options.Add($"Build {wu.unitName} ({wu.workerWorkCost} WP)");
            }
        }

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
            var displayOptions = new List<string> { "Select build option..." };
            displayOptions.AddRange(options);
            suppressBuildOptionCallback = true;
            buildOptionsDropdown.ClearOptions();
            buildOptionsDropdown.AddOptions(displayOptions);
            buildOptionsDropdown.SetValueWithoutNotify(0);
            suppressBuildOptionCallback = false;
            buildOptionsDropdown.interactable = true;
            if (startBuildButton != null) { startBuildButton.gameObject.SetActive(true); startBuildButton.interactable = false; }
        }
        buildOptionsDropdown.gameObject.SetActive(true);
    }

    private void OnBuildOptionSelected(int idx)
    {
        if (suppressBuildOptionCallback || buildOptions == null || currentWorkerUnit == null) return;
        if (idx <= 0 || idx > buildOptions.Count)
        {
            // clear pending selection
            pendingBuildIndex = -1;
            if (startBuildButton != null) startBuildButton.interactable = false;
            return;
        }

        // Store pending build selection; do not start immediately. User must press the Start button.
        pendingBuildIndex = idx - 1;
        if (startBuildButton != null)
        {
            startBuildButton.interactable = true;
        }
    }

    private void OnStartBuildButtonClicked()
    {
        if (pendingBuildIndex < 0 || buildOptions == null || pendingBuildIndex >= buildOptions.Count || currentWorkerUnit == null) return;
        var opt = buildOptions[pendingBuildIndex];
        // Clear pending and disable button
        pendingBuildIndex = -1;
        if (startBuildButton != null) startBuildButton.interactable = false;

        switch (opt.Type)
        {
            case BuildOption.OptionType.Improvement:
                if (opt.Improvement != null) OnStartWorkerBuildImprovement(opt.Improvement);
                break;
            case BuildOption.OptionType.CombatUnit:
                if (opt.CombatUnit != null) OnStartWorkerBuildUnit(opt.CombatUnit);
                break;
            case BuildOption.OptionType.WorkerUnit:
                if (opt.WorkerUnit != null) OnStartWorkerBuildWorker(opt.WorkerUnit);
                break;
        }
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
        currentWorkerUnit.StartBuildingWorker(workerData, currentWorkerUnit.currentTileIndex);
        UpdateUnitInfoForWorkerUnit();
    }

    private void OnStartWorkerBuildUnit(CombatUnitData unitData)
    {
        if (currentWorkerUnit == null || unitData == null) return;
        currentWorkerUnit.StartBuildingUnit(unitData, currentWorkerUnit.currentTileIndex);
        UpdateUnitInfoForWorkerUnit();
    }

    private void OnStartWorkerBuildImprovement(ImprovementData imp)
    {
        if (currentWorkerUnit == null || imp == null) return;
        currentWorkerUnit.StartBuilding(imp, currentWorkerUnit.currentTileIndex);
        UpdateUnitInfoForWorkerUnit();
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

        currentCombatUnit.EnterOrbit(currentCombatUnit.currentTileIndex);
        currentCombatUnit.ConsumeAction();
        UpdateUnitInfoForCombatUnit();
    }

    private void OnExitOrbitClicked()
    {
        if (currentCombatUnit == null) return;
        if (currentCombatUnit.hasActedThisTurn) return;
        if (!currentCombatUnit.IsInOrbit) return;

        currentCombatUnit.ExitOrbit(currentCombatUnit.currentTileIndex);
        currentCombatUnit.ConsumeAction();
        UpdateUnitInfoForCombatUnit();
    }
}