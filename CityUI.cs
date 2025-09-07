// Assets/Scripts/UI/CityUI.cs
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class CityUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI cityNameText;
    [SerializeField] private TextMeshProUGUI levelText;

    [Header("Yield Display")]
    [SerializeField] private TextMeshProUGUI foodStorageText; // For "Food Storage X/Y"
    [SerializeField] private TextMeshProUGUI populationProgressText; // For "Pop. Progress: Growth in X turns"
    [SerializeField] private TextMeshProUGUI netFoodPerTurnText;
    [SerializeField] private TextMeshProUGUI goldPerTurnText;
    [SerializeField] private TextMeshProUGUI sciencePerTurnText;
    [SerializeField] private TextMeshProUGUI culturePerTurnText;
    [SerializeField] private TextMeshProUGUI policyPointsPerTurnText;
    [SerializeField] private TextMeshProUGUI faithPerTurnText;
    // Note: Production Points display from the image might need a separate Text field if it's different from city's direct productionPerTurn.

    [Header("Governor UI")]
    [SerializeField] private GovernorPanel governorPanel;

    [Header("Production Queue Display - Current Item")] // Placeholder for "Thing we are making"
    [SerializeField] private TextMeshProUGUI currentProductionItemNameText;
    [SerializeField] private TextMeshProUGUI currentProductionTurnsRemainingText;
    [SerializeField] private GameObject currentProductionPanel; // To hide if queue is empty

    [Header("Build Options")]
    [SerializeField] private Transform buildingsContainer; // Container for building options
    [SerializeField] private Transform unitsContainer; // Container for unit options
    [SerializeField] private Transform equipmentContainer; // Container for equipment options
    [SerializeField] private GameObject buildOptionPrefab; // button + icon + cost

    [Header("Governor Info")]
    [SerializeField] private TextMeshProUGUI governorNameText;
    [SerializeField] private TextMeshProUGUI governorLevelText;
    [SerializeField] private TextMeshProUGUI governorExperienceText;
    [SerializeField] private TMP_Dropdown governorDropdown;
    [SerializeField] private Button closeButton; // Assign this in the Inspector to your UI's X/close button

    private City currentCity;

    public City CurrentCity => currentCity;
    
    private List<BuildingData> availableBuildings = new List<BuildingData>();
    private List<CombatUnitData> availableUnits = new List<CombatUnitData>();
    private List<WorkerUnitData> availableWorkerUnits = new List<WorkerUnitData>();
    private List<EquipmentData> availableEquipment = new List<EquipmentData>();

    // Removed tab buttons and panel references

    void Start()
    {
        // Removed tab button listeners
    }

    // Mapping of dropdown entries (index-1 => governor in this list). Index 0 is "None".
    private System.Collections.Generic.List<Governor> dropdownGovernors = new System.Collections.Generic.List<Governor>();

    private void Awake()
    {
        if (governorDropdown != null)
        {
            governorDropdown.onValueChanged.RemoveAllListeners();
            governorDropdown.onValueChanged.AddListener(OnGovernorDropdownChanged);
        }
        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);
    }

    public void ShowForCity(City city)
    {
        Debug.Log($"[CityUI] ShowForCity called for city: {city?.cityName ?? "NULL"}, this={gameObject.name}, activeSelf={gameObject.activeSelf}");
        currentCity = city;
        if (currentCity == null)
        {
            Debug.LogError("CityUI: ShowForCity called with a null city.");
            gameObject.SetActive(false);
            return;
        }

        // Hide the unit info panel when the city UI is opened
        if (UIManager.Instance != null && UIManager.Instance.unitInfoPanel != null)
        {
            UIManager.Instance.unitInfoPanel.SetActive(false);
        }

        RefreshUI();
        gameObject.SetActive(true);
        Debug.Log($"[CityUI] ShowForCity finished. UI should now be active: {gameObject.activeSelf}");
    }

    private void OnGovernorDropdownChanged(int idx)
    {
        if (currentCity == null || currentCity.owner == null) return;
        var civ = currentCity.owner;
        // None selected
        if (idx == 0)
        {
            // Remove governor from this city if any
            if (currentCity.governor != null)
            {
                civ.RemoveGovernorFromCity(currentCity.governor, currentCity);
            }
        }
        else
        {
            int govIndex = idx - 1;
            if (govIndex >= 0 && govIndex < dropdownGovernors.Count)
            {
                var selected = dropdownGovernors[govIndex];
                if (selected != null)
                {
                    civ.AssignGovernorToCity(selected, currentCity);
                }
            }
        }
        // Refresh UI after change
        RefreshUI();
    }
    public void RefreshUI()
    {
        Debug.Log($"[CityUI] RefreshUI called. currentCity: {currentCity?.cityName ?? "NULL"}, UI: {gameObject.name}, activeSelf: {gameObject.activeSelf}");
        if (currentCity == null)
        {
            Hide();
            return;
        }

        int netFood = currentCity.GetFoodPerTurn();
        netFoodPerTurnText.text = $"Net Food: {netFood:+#;-#;0}/turn"; // Shows + for positive, - for negative
        goldPerTurnText.text = $"Gold: {currentCity.GetGoldPerTurn():+#;-#;0}/turn";
        sciencePerTurnText.text = $"Science: {currentCity.GetSciencePerTurn():+#;-#;0}/turn";
        culturePerTurnText.text = $"Culture: {currentCity.GetCulturePerTurn():+#;-#;0}/turn";
        policyPointsPerTurnText.text = $"Policy: {currentCity.GetPolicyPointPerTurn():+#;-#;0}/turn";
        faithPerTurnText.text = $"Faith: {currentCity.GetFaithPerTurn():+#;-#;0}/turn";

        // Update Governor Display
        UpdateGovernorDisplay();

        if (netFood > 0 && currentCity.foodStorage < currentCity.foodGrowthRequirement)
        {
            int turnsToGrow = Mathf.CeilToInt((float)(currentCity.foodGrowthRequirement - currentCity.foodStorage) / netFood);
            populationProgressText.text = $"Pop. Growth in: {turnsToGrow} turns";
        }
        else if (currentCity.foodStorage >= currentCity.foodGrowthRequirement)
        {
            populationProgressText.text = "Pop. Maxed for next level (Excess stored)";
        }
        else
        {
            populationProgressText.text = "Pop. Stagnant or Shrinking";
        }
        
        // Update Current Production Display ("Thing we are making")
        UpdateCurrentProductionDisplay();
        
        // Load available production options
        LoadAvailableOptions();
        
        // Populate the unified build options list
        PopulateBuildOptionsList();
    }
    
    private void UpdateCurrentProductionDisplay()
    {
        if (currentCity == null || currentCity.productionQueue == null || currentCity.productionQueue.Count == 0)
        {
            // No production in queue
            if (currentProductionPanel != null) 
                currentProductionPanel.SetActive(false);
            if (currentProductionItemNameText != null) 
                currentProductionItemNameText.text = "Nothing in production";
            if (currentProductionTurnsRemainingText != null) 
                currentProductionTurnsRemainingText.text = "";
            return;
        }

        // We have something in production
        if (currentProductionPanel != null)
            currentProductionPanel.SetActive(true);
            
        // Get data about what's being produced
        var currentProd = currentCity.productionQueue[0];
        string itemName = "Unknown Item";
        int totalCost = 1; // Default to prevent division by zero
        
        // Determine what type of item is being produced
        if (currentProd.data is CombatUnitData cud) 
        { 
            itemName = cud.unitName; 
            totalCost = cud.productionCost; 
        }
        else if (currentProd.data is WorkerUnitData wud) 
        { 
            itemName = wud.unitName; 
            totalCost = wud.productionCost; 
        }
        else if (currentProd.data is BuildingData bd) 
        { 
            itemName = bd.buildingName; 
            totalCost = bd.productionCost; 
        }
        else if (currentProd.data is DistrictData dd) 
        { 
            itemName = dd.districtName; 
            totalCost = dd.productionCost; 
        }
        
        // Update UI with production info
        if (currentProductionItemNameText != null)
            currentProductionItemNameText.text = $"Producing: {itemName}";
        
        // Calculate turns remaining
        if (currentProductionTurnsRemainingText != null)
        {
            if (currentCity.productionPerTurn > 0)
            {
                int turnsLeft = Mathf.CeilToInt((float)currentProd.remainingPts / currentCity.productionPerTurn);
                currentProductionTurnsRemainingText.text = $"{turnsLeft} turns left";
            }
            else
            {
                currentProductionTurnsRemainingText.text = "Stalled";
            }
        }
    }

    private void LoadAvailableOptions()
    {
        availableBuildings.Clear();
        availableUnits.Clear();
        availableWorkerUnits.Clear();
        // availableEquipment.Clear(); // Equipment not handled yet

        if (currentCity.owner == null) return;

        var ownerCiv = currentCity.owner;
        
        // Get buildings directly from already unlocked buildings
        foreach (var building in ownerCiv.unlockedBuildings)
        {
            // Skip if already built
            bool alreadyBuilt = false;
            foreach (var (builtData, _) in currentCity.builtBuildings)
            {
                if (builtData == building || 
                   (builtData.replacesBuilding != null && builtData.replacesBuilding == building) ||
                   (building.replacesBuilding != null && building.replacesBuilding == builtData))
                {
                    alreadyBuilt = true;
                    break;
                }
            }
            
            if (!alreadyBuilt)
            {
                // Check if the civilization has a unique version
                BuildingData actualBuildingToAdd = ownerCiv.GetBuildingData(building);
                if (!availableBuildings.Contains(actualBuildingToAdd))
                {
                    availableBuildings.Add(actualBuildingToAdd);
                }
            }
        }
        
        // Get units directly from already unlocked units
        foreach (var unit in ownerCiv.unlockedCombatUnits)
        {
            if (!availableUnits.Contains(unit))
            {
                availableUnits.Add(unit);
            }
        }
        
        // Get worker units directly from already unlocked worker units
        foreach (var worker in ownerCiv.unlockedWorkerUnits)
        {
            if (!availableWorkerUnits.Contains(worker))
            {
                availableWorkerUnits.Add(worker);
            }
        }
        
        // Add unique units from CivData if applicable
        if (ownerCiv.civData != null && ownerCiv.civData.uniqueUnits != null)
        {
            foreach (var unit in ownerCiv.civData.uniqueUnits)
            {
                if (!availableUnits.Contains(unit))
                {
                    availableUnits.Add(unit);
                }
            }
        }

        // Equipment: show equipment unlocked by civ/effects
        availableEquipment.Clear();
        // From civilization inventory and unlocked equipment via civ data
        if (ownerCiv != null)
        {
            // Equipment types that the civ already has in inventory
            if (ownerCiv.equipmentInventory != null)
            {
                foreach (var kv in ownerCiv.equipmentInventory)
                {
                    if (kv.Key != null && !availableEquipment.Contains(kv.Key))
                        availableEquipment.Add(kv.Key);
                }
            }

            // Also include equipment unlocked from CivData
            if (ownerCiv.civData != null && ownerCiv.civData.uniqueUnits != null)
            {
                // no-op: uniqueness handled elsewhere
            }
        }
    }
    
    private void PopulateBuildOptionsList()
    {
        // Clear all containers
        foreach (Transform t in buildingsContainer) Destroy(t.gameObject);
        foreach (Transform t in unitsContainer) Destroy(t.gameObject);
        foreach (Transform t in equipmentContainer) Destroy(t.gameObject);

        // Display Buildings in buildings container
        foreach (var building in availableBuildings.OrderBy(b => b.productionCost))
        {
            CreateBuildOptionButton(building, building.icon, building.buildingName, building.productionCost, buildingsContainer);
        }
        
        // Display Combat Units in units container
        foreach (var unit in availableUnits.OrderBy(u => u.productionCost))
        {
            CreateBuildOptionButton(unit, unit.icon, unit.unitName, unit.productionCost, unitsContainer);
        }
        
        // Display Worker Units also in units container
        foreach (var workerUnit in availableWorkerUnits.OrderBy(w => w.productionCost))
        {
            CreateBuildOptionButton(workerUnit, workerUnit.icon, workerUnit.unitName, workerUnit.productionCost, unitsContainer);
        }
        
        // Equipment options
        foreach (var eq in availableEquipment.OrderBy(e => e.productionCost))
        {
            CreateBuildOptionButton(eq, eq.icon, eq.equipmentName, eq.productionCost, equipmentContainer);
        }
    }

    private void CreateBuildOptionButton(ScriptableObject itemData, Sprite itemIcon, string itemName, int itemCost, Transform container)
    {
        if (buildOptionPrefab == null) return;
        var btnGO = Instantiate(buildOptionPrefab, container);
        
        var iconImg = btnGO.transform.Find("Icon")?.GetComponent<Image>(); // Assuming prefab has child "Icon" with Image
        var nameText = btnGO.transform.Find("Name")?.GetComponent<TextMeshProUGUI>(); // Assuming prefab has child "Name" with TMP
        var costText = btnGO.transform.Find("Cost")?.GetComponent<TextMeshProUGUI>(); // Assuming prefab has child "Cost" with TMP

        if (iconImg != null) iconImg.sprite = itemIcon; else Debug.LogWarning($"BuildOptionPrefab missing Icon Image for {itemName}");
        if (nameText != null) nameText.text = itemName; else Debug.LogWarning($"BuildOptionPrefab missing Name Text for {itemName}");
        if (costText != null) costText.text = itemCost.ToString(); else Debug.LogWarning($"BuildOptionPrefab missing Cost Text for {itemName}");

        // If this is equipment, show owned count if available
        if (itemData is EquipmentData ed)
        {
            var ownedText = btnGO.transform.Find("OwnedCount")?.GetComponent<TextMeshProUGUI>();
            if (ownedText != null && currentCity != null && currentCity.owner != null)
            {
                ownedText.text = $"Owned: {currentCity.owner.GetEquipmentCount(ed)}";
            }
            // Wire BuyButton if present
            var buyBtn = btnGO.transform.Find("BuyButton")?.GetComponent<Button>();
            if (buyBtn != null)
            {
                buyBtn.onClick.RemoveAllListeners();
                buyBtn.onClick.AddListener(() =>
                {
                    bool bought = currentCity.BuyProduction(itemData);
                    if (bought) RefreshUI();
                    else Debug.LogWarning($"Failed to buy {itemName} in {currentCity.cityName}");
                });
            }
        }

        var button = btnGO.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners(); // Clear existing listeners
            button.onClick.AddListener(() =>
            {
                bool success = currentCity.QueueProduction(itemData);
                if (success)
                {
                    RefreshUI(); // Refresh to show updated queue and potentially remove built item from list
                }
                else
                {
                    // Optionally, provide feedback to the player that queuing failed (e.g., not enough resources, missing requirements)
                    Debug.LogWarning($"Failed to queue {itemName} for {currentCity.cityName}");
                }
            });
        }
    }

    private void UpdateGovernorDisplay()
    {
        // Simple city-level governor info and dropdown (detailed management is in GovernorPanel)
        if (currentCity == null || currentCity.owner == null)
        {
            if (governorNameText != null) governorNameText.text = "(No City)";
            if (governorLevelText != null) governorLevelText.text = "";
            if (governorExperienceText != null) governorExperienceText.text = "";
            if (governorDropdown != null) governorDropdown.interactable = false;
            return;
        }

        var civ = currentCity.owner;
        // If governors are not enabled for this civ, show locked state
        if (!civ.governorsEnabled)
        {
            if (governorNameText != null) governorNameText.text = "(Governors Locked)";
            if (governorLevelText != null) governorLevelText.text = "";
            if (governorExperienceText != null) governorExperienceText.text = "";
            if (governorDropdown != null) governorDropdown.interactable = false;
            return;
        }

        var gov = currentCity.governor;
        if (gov == null)
        {
            if (governorNameText != null) governorNameText.text = "(No Governor)";
            if (governorLevelText != null) governorLevelText.text = "";
            if (governorExperienceText != null) governorExperienceText.text = "";
        }
        else
        {
            if (governorNameText != null) governorNameText.text = gov.Name;
            if (governorLevelText != null) governorLevelText.text = $"Level {gov.Level}";
            if (governorExperienceText != null) governorExperienceText.text = $"XP: {gov.Experience}";
        }

        // Populate dropdown
        PopulateGovernorDropdown();
    }

    private void PopulateGovernorDropdown()
    {
        dropdownGovernors.Clear();
        if (governorDropdown == null) return;
        governorDropdown.ClearOptions();
        var options = new System.Collections.Generic.List<string>();
        // None option
        options.Add("None");
        if (currentCity == null || currentCity.owner == null)
        {
            governorDropdown.AddOptions(options);
            governorDropdown.value = 0;
            governorDropdown.interactable = false;
            return;
        }

        var civ = currentCity.owner;
        if (!civ.governorsEnabled)
        {
            governorDropdown.AddOptions(options);
            governorDropdown.value = 0;
            governorDropdown.interactable = false;
            return;
        }

        // Add all civ governors
        if (civ.governors != null)
        {
            foreach (var g in civ.governors)
            {
                if (g == null) continue;
                dropdownGovernors.Add(g);
                options.Add($"{g.Name} ({g.specialization})");
            }
        }

        governorDropdown.AddOptions(options);
        // Set selected index to current city's governor
        if (currentCity.governor == null)
            governorDropdown.value = 0;
        else
        {
            int idx = dropdownGovernors.IndexOf(currentCity.governor);
            governorDropdown.value = (idx >= 0) ? idx + 1 : 0;
        }
        governorDropdown.interactable = true;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        // Always restore the unit info panel when the city UI is closed.
        if (UIManager.Instance != null && UIManager.Instance.unitInfoPanel != null)
        {
            UIManager.Instance.unitInfoPanel.SetActive(true);
        }
    }
}
