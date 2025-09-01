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
    // Optional: Add a dedicated TextMeshProUGUI for Work Points if you modify the prefab
    // [SerializeField] private TextMeshProUGUI workPointsText; 

    [Header("Actions")]
    [SerializeField] private Button settleCityButton;
    [Header("Worker Build Units UI")] 
    [SerializeField] private Transform buildUnitsContainer; // vertical layout group
    [SerializeField] private GameObject buildUnitButtonPrefab; // simple button with icon/text
    [SerializeField] private Button contributeWorkButton; // applies work points this turn to current tile job (improvement or unit)

    private CombatUnit currentCombatUnit;
    private WorkerUnit currentWorkerUnit;
    private readonly List<GameObject> buildUnitButtons = new List<GameObject>();

    private void Awake()
    {
        if (settleCityButton != null)
            settleCityButton.onClick.AddListener(OnSettleCityClicked);

        if (contributeWorkButton != null)
            contributeWorkButton.onClick.AddListener(OnContributeWorkClicked);

        // On start, clear the panel to show a "no unit selected" state.
        ClearPanelInfo();
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
            Debug.Log($"UnitInfoPanel.ShowPanel: Processing CombatUnit: {unitNameForLog}");
            PopulateForCombatUnit(currentCombatUnit);
            if (settleCityButton != null) settleCityButton.gameObject.SetActive(false); // Hide for combat units
        }
        else if (unitObject is WorkerUnit workerUnit)
        {
            currentWorkerUnit = workerUnit;
            currentCombatUnit = null; // Ensure combat unit is cleared
            unitNameForLog = currentWorkerUnit.data.unitName;
            Debug.Log($"UnitInfoPanel.ShowPanel: Processing WorkerUnit: {unitNameForLog}");
            PopulateForWorkerUnit(currentWorkerUnit);

            // Show or hide the Settle City button
            if (settleCityButton != null)
            {
                bool canSettle = workerUnit.data.canFoundCity && workerUnit.CanFoundCityOnCurrentTile();
                settleCityButton.gameObject.SetActive(canSettle);
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

        Debug.Log($"UnitInfoPanel.ShowPanel: About to activate unitInfoPanel for {unitNameForLog}. Current state: {unitInfoPanel.activeSelf}");
        unitInfoPanel.SetActive(true);
        Debug.Log($"UnitInfoPanel.ShowPanel: unitInfoPanel for {unitNameForLog} should now be active. New state: {unitInfoPanel.activeSelf}");

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

        // Hide buttons that require a unit
        if (settleCityButton != null) settleCityButton.gameObject.SetActive(false);
        if (buildUnitsContainer != null)
        {
            foreach (var go in buildUnitButtons) if (go != null) Destroy(go);
            buildUnitButtons.Clear();
            buildUnitsContainer.gameObject.SetActive(false);
        }
        if (contributeWorkButton != null) contributeWorkButton.gameObject.SetActive(false);

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
        // if (workPointsText != null) workPointsText.gameObject.SetActive(false); // Hide worker specific if it exists


        unitNameText.text = currentCombatUnit.data.unitName;
        unitTypeText.text = currentCombatUnit.data.unitType.ToString();
        levelText.text = $"Level: {currentCombatUnit.level}";
        experienceText.text = $"XP: {currentCombatUnit.experience}/{currentCombatUnit.data.xpToNextLevel[currentCombatUnit.level - 1]}";
        
        attackText.text = $"Attack: {currentCombatUnit.CurrentAttack}";
        defenseText.text = $"Defense: {currentCombatUnit.CurrentDefense}";
        healthText.text = $"Health: {currentCombatUnit.currentHealth}/{currentCombatUnit.MaxHealth}";
        movePointsText.text = $"Move Points: {currentCombatUnit.currentMovePoints}";
        rangeText.text = $"Range: {currentCombatUnit.CurrentRange}";
        moraleText.text = $"Morale: {currentCombatUnit.currentMorale}";
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

        // if (workPointsText != null) workPointsText.gameObject.SetActive(true); // Show worker specific if it exists

        if (levelText != null) levelText.gameObject.SetActive(true); // Using levelText for Work Points
        if (experienceText != null) experienceText.gameObject.SetActive(false); // Hide XP
        if (rangeText != null) rangeText.gameObject.SetActive(false);
        if (moraleText != null) moraleText.gameObject.SetActive(false);


        unitNameText.text = currentWorkerUnit.data.unitName;
        unitTypeText.text = "Worker Unit"; // Explicitly set type
        
        healthText.text = $"Health: {currentWorkerUnit.currentHealth}/{currentWorkerUnit.data.baseHealth}";
        movePointsText.text = $"Move Points: {currentWorkerUnit.currentMovePoints}";
        attackText.text = $"Attack: {currentWorkerUnit.CurrentAttack}";
        defenseText.text = $"Defense: {currentWorkerUnit.CurrentDefense}";

        // Placeholder for Work Points - using levelText
        if (levelText != null)
        {
            levelText.text = $"Work Points: {currentWorkerUnit.currentWorkPoints}/{currentWorkerUnit.data.baseWorkPoints}";
            Debug.LogWarning("UnitInfoPanel: Using 'levelText' to display Worker Work Points. Consider adding a dedicated UI element.");
        }
        // Show worker build units section
        if (buildUnitsContainer != null)
        {
            PopulateWorkerBuildUnits(currentWorkerUnit);
            buildUnitsContainer.gameObject.SetActive(true);
        }
        if (contributeWorkButton != null) contributeWorkButton.gameObject.SetActive(true);
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
    }

    private void HideAllSections()
    {
        // Implement the logic to hide all sections of the panel
        // This is a placeholder and should be replaced with the actual implementation
        Debug.Log("UnitInfoPanel: Hiding all sections");
    }

    private void PopulateForCombatUnit(CombatUnit combatUnit)
    {
        // Implement the logic to populate the panel for a CombatUnit
        // This is a placeholder and should be replaced with the actual implementation
        Debug.Log("UnitInfoPanel: Populating for CombatUnit");
        UpdateUnitInfoForCombatUnit();
    }

    private void PopulateForWorkerUnit(WorkerUnit workerUnit)
    {
        // Implement the logic to populate the panel for a WorkerUnit
        // This is a placeholder and should be replaced with the actual implementation
        Debug.Log("UnitInfoPanel: Populating for WorkerUnit");
        UpdateUnitInfoForWorkerUnit();
    }

    private void PopulateWorkerBuildUnits(WorkerUnit worker)
    {
        if (buildUnitsContainer == null || worker == null) return;

        // Clear existing
        foreach (var go in buildUnitButtons) if (go != null) Destroy(go);
        buildUnitButtons.Clear();

        var civ = worker.owner;
        if (civ == null) return;

    // Gather units unlocked by civ (tech/culture/unique)
    var units = civ.unlockedCombatUnits;
    var workerUnits = civ.unlockedWorkerUnits;
    if (units == null && workerUnits == null) return;

        foreach (var u in units)
        {
            if (u == null) continue;
            if (!u.buildableByWorker) continue;
            if (!worker.CanBuildUnit(u, worker.currentTileIndex)) continue;

            var btnGO = Instantiate(buildUnitButtonPrefab, buildUnitsContainer);
            buildUnitButtons.Add(btnGO);

            // Try to populate basic visuals if it has Image/Text components
            var txt = btnGO.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.text = $"Build {u.unitName} ({u.workerWorkCost} WP)";
            var img = btnGO.GetComponentInChildren<Image>();
            if (img != null && u.icon != null) img.sprite = u.icon;

            var button = btnGO.GetComponent<Button>();
            if (button != null)
            {
                var unitLocal = u;
                button.onClick.AddListener(() => OnStartWorkerBuildUnit(unitLocal));
            }
        }

        // Also list buildable worker units
        if (workerUnits != null)
        {
            foreach (var wu in workerUnits)
            {
                if (wu == null) continue;
                if (!wu.buildableByWorker) continue;
                if (!worker.CanBuildWorker(wu, worker.currentTileIndex)) continue;

                var btnGO = Instantiate(buildUnitButtonPrefab, buildUnitsContainer);
                buildUnitButtons.Add(btnGO);

                var txt = btnGO.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) txt.text = $"Build {wu.unitName} ({wu.workerWorkCost} WP)";
                var img = btnGO.GetComponentInChildren<Image>();
                if (img != null && wu.icon != null) img.sprite = wu.icon;

                var button = btnGO.GetComponent<Button>();
                if (button != null)
                {
                    var localWu = wu;
                    button.onClick.AddListener(() => OnStartWorkerBuildWorker(localWu));
                }
            }
        }
    }

    private void OnStartWorkerBuildUnit(CombatUnitData unitData)
    {
        if (currentWorkerUnit == null || unitData == null) return;
        currentWorkerUnit.StartBuildingUnit(unitData, currentWorkerUnit.currentTileIndex);
        // After starting, allow immediate contribution if player wants
        UpdateUnitInfoForWorkerUnit();
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
}