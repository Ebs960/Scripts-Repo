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


    [Header("Equipment")]
    [SerializeField] private Transform equipmentButtonContainer;
    [SerializeField] private GameObject equipmentButtonPrefab;
    [SerializeField] private Button openEquipmentButton;
    [SerializeField] private GameObject equipmentSelectionPanel;

    [Header("Actions")]
    [SerializeField] private Button settleCityButton;

    private CombatUnit currentCombatUnit;
    private WorkerUnit currentWorkerUnit;
    private List<EquipmentButton> equipmentButtons = new List<EquipmentButton>();

    private void Awake()
    {
        if (openEquipmentButton != null)
            openEquipmentButton.onClick.AddListener(ToggleEquipmentPanel);
        
        if (settleCityButton != null)
            settleCityButton.onClick.AddListener(OnSettleCityClicked);

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
            if (equipmentSelectionPanel != null) equipmentSelectionPanel.SetActive(false); // Close equipment by default
            if (openEquipmentButton != null) openEquipmentButton.gameObject.SetActive(true); // Show for combat units
            if (settleCityButton != null) settleCityButton.gameObject.SetActive(false); // Hide for combat units
        }
        else if (unitObject is WorkerUnit workerUnit)
        {
            currentWorkerUnit = workerUnit;
            currentCombatUnit = null; // Ensure combat unit is cleared
            unitNameForLog = currentWorkerUnit.data.unitName;
            Debug.Log($"UnitInfoPanel.ShowPanel: Processing WorkerUnit: {unitNameForLog}");
            PopulateForWorkerUnit(currentWorkerUnit);
            if (equipmentSelectionPanel != null) equipmentSelectionPanel.SetActive(false);
            if (openEquipmentButton != null) openEquipmentButton.gameObject.SetActive(false); // Hide for worker units

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
        if (openEquipmentButton != null) openEquipmentButton.gameObject.SetActive(false);
        if (equipmentSelectionPanel != null) equipmentSelectionPanel.SetActive(false);
        if (settleCityButton != null) settleCityButton.gameObject.SetActive(false);

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
        // else if (workPointsText != null) // If you add a dedicated field
        // {
        //     workPointsText.text = $"Work Points: {currentWorkerUnit.currentWorkPoints}/{currentWorkerUnit.data.baseWorkPoints}";
        // }
    }

    private void ToggleEquipmentPanel()
    {
        if (equipmentSelectionPanel == null || currentCombatUnit == null) // Check currentCombatUnit
        {
            if (equipmentSelectionPanel != null) equipmentSelectionPanel.SetActive(false);
            return;
        }

        bool isActive = !equipmentSelectionPanel.activeSelf;
        equipmentSelectionPanel.SetActive(isActive);

        if (isActive)
        {
            PopulateEquipmentOptions();
        }
    }

    private void PopulateEquipmentOptions()
    {
        if (currentCombatUnit == null) return; // Ensure we have a combat unit

        // Clear existing buttons
        foreach (var button in equipmentButtons)
        {
            if (button != null)
                Destroy(button.gameObject);
        }
        equipmentButtons.Clear();

        var civilization = currentCombatUnit.owner;
        if (civilization == null) return;

        var availableEquipment = civilization.GetAvailableEquipment();
        foreach (var equipment in availableEquipment)
        {
            if (equipment.IsValidForUnit(currentCombatUnit))
            {
                CreateEquipmentButton(equipment);
            }
        }
    }

    private void CreateEquipmentButton(EquipmentData equipment)
    {
        var buttonObj = Instantiate(equipmentButtonPrefab, equipmentButtonContainer);
        var equipmentButton = buttonObj.GetComponent<EquipmentButton>();
        
        if (equipmentButton != null)
        {
            equipmentButton.Setup(equipment, OnEquipmentSelected);
            equipmentButtons.Add(equipmentButton);
        }
    }

    private void OnEquipmentSelected(EquipmentData equipment)
    {
        if (currentCombatUnit != null) // Use currentCombatUnit
        {
            currentCombatUnit.EquipItem(equipment);
            UpdateUnitInfoForCombatUnit(); // Refresh combat unit info
            equipmentSelectionPanel.SetActive(false);
        }
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
        if (openEquipmentButton != null)
            openEquipmentButton.onClick.RemoveListener(ToggleEquipmentPanel);
        
        if (settleCityButton != null)
            settleCityButton.onClick.RemoveListener(OnSettleCityClicked);
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
}