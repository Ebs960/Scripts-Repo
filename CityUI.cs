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
    [SerializeField] private Button makeCapitalButton;
    [SerializeField] private TextMeshProUGUI makeCapitalButtonText;

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
    [Header("Disease Display")]
    [SerializeField] private Transform diseaseContainer; // container to hold disease entries
    [SerializeField] private GameObject diseaseEntryPrefab; // prefab with children: Icon(Image), Name(TextMeshProUGUI), Mods(TextMeshProUGUI)

    [Header("Production Queue Display - Current Item")] // Placeholder for "Thing we are making"
    [SerializeField] private TextMeshProUGUI currentProductionItemNameText;
    [SerializeField] private TextMeshProUGUI currentProductionTurnsRemainingText;
    [SerializeField] private GameObject currentProductionPanel; // To hide if queue is empty

    [Header("Build Options")]
    [SerializeField] private Transform buildingsContainer; // Container for building options
    [SerializeField] private Transform unitsContainer; // Container for unit options
    [SerializeField] private Transform equipmentContainer; // Container for equipment options
    [SerializeField] private Transform projectilesContainer; // Container for projectile options
    [SerializeField] private Transform missilesContainer; // Container for missile production options
    [SerializeField] private GameObject buildOptionPrefab; // button + icon + cost

    [Header("Citizen Assignment UI")]
    [SerializeField] private Button openCitizenAssignmentButton;
    [SerializeField] private TextMeshProUGUI citizenJobsSummaryText;
    [SerializeField] private TextMeshProUGUI unemploymentWarningText;
    [SerializeField] private TextMeshProUGUI orderCrimeSummaryText;
    [SerializeField] private bool autoOpenCitizenAssignmentOverlayOnCityClick = false;

    [Header("City Feature Tabs")]
    [SerializeField] private CityUITabController tabController;

    [Header("Buildings & Specialists Tab")]
    [SerializeField] private TextMeshProUGUI buildingSlotSummaryText;
    [SerializeField] private TextMeshProUGUI specialistSlotSummaryText;

    [Header("Crime & Disease Tab")]
    [SerializeField] private TextMeshProUGUI citySecuritySummaryText;
    [SerializeField] private TextMeshProUGUI diseaseSummaryText;

    [Header("Unit Storage Tab")]
    [SerializeField] private TextMeshProUGUI unitStorageSummaryText;
    [SerializeField] private TextMeshProUGUI missileStorageSummaryText;

    [Header("Missile Launch")]
    [Tooltip("Button shown when the city has stored missiles ready to launch. Opens MissilePanelUI.")]
    [SerializeField] private Button launchMissileButton;
    [Tooltip("Additional button that can also open the missile panel. Allows designer-assigned placement.")]
    [SerializeField] private Button openMissilePanelButton;
    [Tooltip("Text label on the launch missile button showing stored count.")]
    [SerializeField] private TextMeshProUGUI launchMissileButtonText;

    // Performance caches
    private List<BuildingData> _cachedAvailableBuildings = new List<BuildingData>();
    private List<CombatUnitData> _cachedAvailableUnits = new List<CombatUnitData>();
    private List<WorkerUnitData> _cachedAvailableWorkers = new List<WorkerUnitData>();
    private List<EquipmentData> _cachedAvailableEquipment = new List<EquipmentData>();
    private List<GameCombat.ProjectileData> _cachedAvailableProjectiles = new List<GameCombat.ProjectileData>();
    private List<MissileData> _cachedAvailableMissiles = new List<MissileData>();
    private bool _buildOptionsCacheDirty = true;

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
    private List<GameCombat.ProjectileData> availableProjectiles = new List<GameCombat.ProjectileData>();
    private List<MissileData> availableMissiles = new List<MissileData>();

    // Removed tab buttons and panel references

    void Start()
    {
        if (launchMissileButton != null)
            launchMissileButton.onClick.AddListener(OnLaunchMissileClicked);
        if (openMissilePanelButton != null)
            openMissilePanelButton.onClick.AddListener(OnLaunchMissileClicked);
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
        if (makeCapitalButton != null)
        {
            makeCapitalButton.onClick.RemoveAllListeners();
            makeCapitalButton.onClick.AddListener(OnMakeCapitalClicked);
        }
        if (openCitizenAssignmentButton != null)
        {
            openCitizenAssignmentButton.onClick.RemoveAllListeners();
            openCitizenAssignmentButton.onClick.AddListener(OnOpenCitizenAssignmentClicked);
        }
        if (tabController != null)
            tabController.TabChanged += OnCityTabChanged;
    }

    private void OnMakeCapitalClicked()
    {
        if (currentCity == null || currentCity.owner == null)
            return;

        currentCity.owner.SetCapitalCity(currentCity);
        UIManager.Instance?.ShowNotification($"{currentCity.cityName} is now the capital.");
        RefreshUI();
    }

    public void ShowForCity(City city)
    {
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

        CityCameraFocus.Instance?.FocusCity(currentCity);
        tabController?.ResetToDefault();
        RefreshUI();
        gameObject.SetActive(true);
        if (autoOpenCitizenAssignmentOverlayOnCityClick)
            CityTileOverlayController.Instance?.EnterCityAssignmentMode(currentCity);
}

    public void ShowForCityTab(City city, CityUITab tab)
    {
        ShowForCity(city);
        tabController?.SelectTab(tab);
    }

    private void OnOpenCitizenAssignmentClicked()
    {
        if (currentCity == null) return;
        CityTileOverlayController.Instance?.EnterCityAssignmentMode(currentCity);
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
if (currentCity == null)
        {
            Hide();
            return;
        }

        if (cityNameText != null)
            cityNameText.text = currentCity.isCapital ? $"{currentCity.cityName} [Capital]" : currentCity.cityName;
        if (levelText != null)
        {
            string ownerName = currentCity.owner?.civData != null ? currentCity.owner.civData.civName : "Unowned";
            levelText.text = $"Population {currentCity.level} • Owner: {ownerName}";
        }

        UpdateCapitalControls();

        int netFood = currentCity.GetFoodPerTurn();
        if (foodStorageText != null)
            foodStorageText.text = $"Food: {currentCity.foodStorage}/{currentCity.foodGrowthRequirement} • Production: {currentCity.productionPerTurn}/turn";
        if (netFoodPerTurnText != null) netFoodPerTurnText.text = $"Net Food: {netFood:+#;-#;0}/turn";
        if (goldPerTurnText != null) goldPerTurnText.text = $"Gold: {currentCity.GetGoldPerTurn():+#;-#;0}/turn";
        if (sciencePerTurnText != null) sciencePerTurnText.text = $"Science: {currentCity.GetSciencePerTurn():+#;-#;0}/turn";
        if (culturePerTurnText != null) culturePerTurnText.text = $"Culture: {currentCity.GetCulturePerTurn():+#;-#;0}/turn";
        if (policyPointsPerTurnText != null) policyPointsPerTurnText.text = $"Policy: {currentCity.GetPolicyPointPerTurn():+#;-#;0}/turn";
        if (faithPerTurnText != null) faithPerTurnText.text = $"Faith: {currentCity.GetFaithPerTurn():+#;-#;0}/turn";
        RefreshCitizenAssignmentSummary();
        RefreshFeatureTabSummaries();

        // Update Governor Display
        UpdateGovernorDisplay();

        if (netFood > 0 && currentCity.foodStorage < currentCity.foodGrowthRequirement)
        {
            int turnsToGrow = Mathf.CeilToInt((float)(currentCity.foodGrowthRequirement - currentCity.foodStorage) / netFood);
            if (populationProgressText != null) populationProgressText.text = $"Pop. Growth in: {turnsToGrow} turns";
        }
        else if (currentCity.foodStorage >= currentCity.foodGrowthRequirement)
        {
            if (populationProgressText != null) populationProgressText.text = "Pop. Maxed for next level (Excess stored)";
        }
        else
        {
            if (populationProgressText != null) populationProgressText.text = "Pop. Stagnant or Shrinking";
        }
        
        // Update Current Production Display ("Thing we are making")
        UpdateCurrentProductionDisplay();
        
        // Load available production options
        LoadAvailableOptions();
        
        // Populate the unified build options list
        PopulateBuildOptionsList();

        // Populate disease list
        PopulateDiseaseList();
    }

    /// <summary>Refreshes every open view of a city after an authoritative ownership change.</summary>
    public static void NotifyOwnershipChanged(City city)
    {
        if (city == null) return;
        foreach (var view in FindObjectsByType<CityUI>(FindObjectsInactive.Include))
        {
            if (view.currentCity != city) continue;
            view.InvalidateBuildOptionsCache();
            view.RefreshUI();
        }
        CityTileOverlayController.Instance?.RefreshOverlay();
        CampaignMapModeController.Instance?.RefreshAll();
    }

    private void OnDestroy()
    {
        if (tabController != null)
            tabController.TabChanged -= OnCityTabChanged;
    }

    private void OnCityTabChanged(CityUITab tab)
    {
        if (tab == CityUITab.BuildingsAndSpecialists ||
            tab == CityUITab.CrimeAndDisease ||
            tab == CityUITab.UnitStorage)
        {
            RefreshFeatureTabSummaries();
        }
    }

    private void RefreshFeatureTabSummaries()
    {
        if (currentCity == null) return;
        RefreshBuildingAndSpecialistSummary();
        RefreshCrimeAndDiseaseSummary();
        RefreshUnitStorageSummary();
    }

    private void RefreshBuildingAndSpecialistSummary()
    {
        if (buildingSlotSummaryText != null)
        {
            var lines = new List<string>();
            foreach (CitySlotType slotType in System.Enum.GetValues(typeof(CitySlotType)))
            {
                int capacity = currentCity.GetBuildingSlotCapacity(slotType);
                int used = currentCity.GetUsedBuildingSlots(slotType);
                if (capacity > 0 || used > 0)
                    lines.Add($"{slotType}: {used}/{capacity}");
            }
            buildingSlotSummaryText.text = lines.Count > 0
                ? string.Join("\n", lines)
                : "No building slots";
        }

        if (specialistSlotSummaryText == null) return;

        int ruralSlots = 0;
        var tileSystem = TileSystem.GetForPlanet(currentCity.planetIndex) ?? TileSystem.Instance;
        if (tileSystem != null)
        {
            foreach (int tileIndex in currentCity.GetWorkableTileIndexes())
            {
                var tile = tileSystem.GetTileData(tileIndex);
                var improvement = tile?.improvementInstanceObject != null
                    ? tile.improvementInstanceObject.GetComponent<ImprovementInstance>()
                    : null;
                if (improvement == null) continue;
                foreach (var slot in improvement.GetActiveRuralSpecialistSlots())
                    if (slot != null) ruralSlots++;
            }
        }

        int urbanSlots = 0;
        foreach (var building in currentCity.GetBuildings())
            if (building?.urbanSpecialistSlots != null) urbanSlots += building.urbanSpecialistSlots.Length;
        foreach (var district in currentCity.GetDistricts())
            if (district?.urbanSpecialistSlots != null) urbanSlots += district.urbanSpecialistSlots.Length;

        int assignedRural = currentCity.GetAssignedCount(CityCitizenJobType.RuralSpecialist);
        int assignedUrban = currentCity.GetAssignedCount(CityCitizenJobType.UrbanSpecialist);
        specialistSlotSummaryText.text =
            $"Rural Specialists: {assignedRural}/{ruralSlots}\n" +
            $"Urban Specialists: {assignedUrban}/{urbanSlots}";
    }

    private void RefreshCrimeAndDiseaseSummary()
    {
        if (citySecuritySummaryText != null)
        {
            citySecuritySummaryText.text =
                $"Order: {currentCity.orderRating}/{currentCity.maxOrder}\n" +
                $"Morale: {currentCity.moraleRating}/{currentCity.maxMorale}\n" +
                $"Loyalty: {currentCity.loyalty:0}/{100}\n" +
                $"Defense: {currentCity.defenseRating}/{currentCity.maxDefense}\n" +
                $"Unemployment Order Penalty: -{currentCity.GetUnemploymentOrderPenaltyPerTurn()}/turn\n" +
                $"Bandit Risk from Unemployment: +{currentCity.CachedBanditRiskFromUnemployment}";
        }

        if (diseaseSummaryText != null)
        {
            int activeDiseaseCount = currentCity.activeDiseases?.Count(d => d != null && d.data != null) ?? 0;
            diseaseSummaryText.text = activeDiseaseCount == 0
                ? "No active diseases"
                : $"Active Diseases: {activeDiseaseCount}";
        }
    }

    private void RefreshUnitStorageSummary()
    {
        int garrisonedCombatUnits = 0;
        int basedAircraft = 0;
        int garrisonedWorkers = 0;

        if (currentCity.owner != null)
        {
            if (currentCity.owner.combatUnits != null)
            {
                foreach (var unit in currentCity.owner.combatUnits)
                {
                    if (unit == null || unit.planetIndex != currentCity.planetIndex ||
                        unit.currentTileIndex != currentCity.centerTileIndex) continue;
                    garrisonedCombatUnits++;
                    if (unit.data != null && CombatUnitData.IsAirCategory(unit.data.unitType))
                        basedAircraft++;
                }
            }

            if (currentCity.owner.workerUnits != null)
            {
                foreach (var worker in currentCity.owner.workerUnits)
                    if (worker != null && worker.planetIndex == currentCity.planetIndex &&
                        worker.currentTileIndex == currentCity.centerTileIndex) garrisonedWorkers++;
            }
        }

        if (unitStorageSummaryText != null)
        {
            unitStorageSummaryText.text =
                $"Garrisoned Units: {garrisonedCombatUnits}\n" +
                $"Based Aircraft: {basedAircraft}\n" +
                $"Workers in City: {garrisonedWorkers}\n" +
                $"Attached Settlements: {currentCity.attachedSettlements?.Count ?? 0}";
        }

        if (missileStorageSummaryText != null)
        {
            int storedMissiles = currentCity.storedMissiles?.Count ?? 0;
            missileStorageSummaryText.text =
                $"Missiles: {storedMissiles}/{currentCity.maxMissileStorage}";
        }
    }

    private void PopulateDiseaseList()
    {
        if (diseaseContainer == null || diseaseEntryPrefab == null) return;
        // Clear existing
        foreach (Transform t in diseaseContainer) Destroy(t.gameObject);

        if (currentCity == null) return;
        if (currentCity.activeDiseases == null || currentCity.activeDiseases.Count == 0)
        {
            var noneGO = new GameObject("NoDisease");
            noneGO.transform.SetParent(diseaseContainer, false);
            var txt = noneGO.AddComponent<TextMeshProUGUI>();
            txt.text = "No active diseases";
            txt.fontSize = 18f;
            return;
        }

        foreach (var di in currentCity.activeDiseases)
        {
            if (di == null || di.data == null) continue;
            var go = Instantiate(diseaseEntryPrefab, diseaseContainer);
            var iconImg = go.transform.Find("Icon")?.GetComponent<UnityEngine.UI.Image>();
            var nameTxt = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            var modsTxt = go.transform.Find("Mods")?.GetComponent<TextMeshProUGUI>();

            if (iconImg != null) iconImg.sprite = di.data.icon;
            if (nameTxt != null) nameTxt.text = di.data.diseaseName ?? "(Unknown)";

            // Compute modifiers from civilization
            string mods = "";
            if (currentCity.owner != null)
            {
                var totals = currentCity.owner.GetDiseaseModifierTotals(di.data, currentCity);
                if (totals.grantsImmunity) mods += "IMMUNE ";
                if (Mathf.Abs(totals.infectionChancePct) > 0.0001f) mods += $"Infect%:{totals.infectionChancePct * 100:+0;-0}% ";
                if (Mathf.Abs(totals.spreadChancePct) > 0.0001f) mods += $"Spread%:{totals.spreadChancePct * 100:+0;-0}% ";
                if (Mathf.Abs(totals.durationPct) > 0.0001f) mods += $"Dur%:{totals.durationPct * 100:+0;-0}% ";
                if (Mathf.Abs(totals.cityYieldPenaltyPct) > 0.0001f) mods += $"Yield%:{totals.cityYieldPenaltyPct * 100:+0;-0}% ";
            }
            if (modsTxt != null) modsTxt.text = string.IsNullOrEmpty(mods) ? "No modifiers" : mods.Trim();
        }
    }

    private void UpdateCapitalControls()
    {
        if (makeCapitalButton == null)
            return;

        bool isPlayerOwned = currentCity != null
            && currentCity.owner != null
            && CivilizationManager.Instance != null
            && currentCity.owner == CivilizationManager.Instance.playerCiv;

        makeCapitalButton.gameObject.SetActive(isPlayerOwned);
        if (!isPlayerOwned)
            return;

        bool alreadyCapital = currentCity != null && currentCity.isCapital;
        makeCapitalButton.interactable = !alreadyCapital;
        if (makeCapitalButtonText != null)
            makeCapitalButtonText.text = alreadyCapital ? "Current Capital" : "Make Capital";
    }

    /// <summary>
    /// Invalidate build options cache when techs/cultures change
    /// </summary>
    public void InvalidateBuildOptionsCache()
    {
        _buildOptionsCacheDirty = true;
        _cachedAvailableBuildings.Clear();
        _cachedAvailableUnits.Clear();
        _cachedAvailableWorkers.Clear();
        _cachedAvailableEquipment.Clear();
        _cachedAvailableProjectiles.Clear();
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
        else if (currentProd.data is GameCombat.ProjectileData pd) // NEW: Handle projectiles
        { 
            itemName = pd.projectileName; 
            totalCost = pd.productionCost; 
        }
        
        // Update UI with production info
        if (currentProductionItemNameText != null)
            currentProductionItemNameText.text =
                $"Producing: {itemName}\nProgress: {Mathf.Max(0, totalCost - currentProd.remainingPts)}/{totalCost}\nQueue: {currentCity.productionQueue.Count} item(s)";
        
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
        // Use cached data if available and not dirty
        if (!_buildOptionsCacheDirty && _cachedAvailableBuildings.Count > 0)
        {
            availableBuildings.Clear();
            availableBuildings.AddRange(_cachedAvailableBuildings);
            availableUnits.Clear();
            availableUnits.AddRange(_cachedAvailableUnits);
            availableWorkerUnits.Clear();
            availableWorkerUnits.AddRange(_cachedAvailableWorkers);
            availableEquipment.Clear();
            availableEquipment.AddRange(_cachedAvailableEquipment);
            availableProjectiles.Clear();
            availableProjectiles.AddRange(_cachedAvailableProjectiles);
            availableMissiles.Clear();
            availableMissiles.AddRange(_cachedAvailableMissiles);
            return;
        }

        availableBuildings.Clear();
        availableUnits.Clear();
        availableWorkerUnits.Clear();
        availableEquipment.Clear();
        availableProjectiles.Clear();
        availableMissiles.Clear();

        if (currentCity.owner == null) return;

        var ownerCiv = currentCity.owner;

        foreach (var building in currentCity.GetAvailableBuildingsForProduction())
        {
            if (building != null && !availableBuildings.Contains(building))
                availableBuildings.Add(building);
        }
        
        // Use the city's production rules so conquered cities can surface units
        // based on their original owner while still checking current-owner requirements.
        foreach (var unit in currentCity.GetAvailableCombatUnitsForProduction())
        {
            if (unit != null && !availableUnits.Contains(unit))
                availableUnits.Add(unit);
        }
        
        // Get all worker units that meet requirements (like equipment system)
        var allWorkerUnits = ResourceCache.GetAllWorkerUnits();
        foreach (var worker in allWorkerUnits)
        {
            if (worker == null || !worker.AreRequirementsMet(ownerCiv) || !currentCity.CanTrainWorkerInThisCity(worker)) continue;
            if (!availableWorkerUnits.Contains(worker))
            {
                availableWorkerUnits.Add(worker);
            }
        }
        
        // Equipment: producer buildings permanently unlock their declared equipment.
        availableEquipment.Clear();
        if (ownerCiv != null)
        {
            foreach (var equipment in ResourceCache.GetAllEquipment())
            {
                if (equipment != null && currentCity.CanProduceEquipment(equipment))
                    availableEquipment.Add(equipment);
            }
        }
        
        // NEW: Projectiles - Load all projectiles that can be produced by this civilization
        availableProjectiles.Clear();
        if (ownerCiv != null)
        {
            // Get all projectile assets in the game
            var allProjectiles = ResourceCache.GetAllProjectiles();
            foreach (var projectile in allProjectiles)
            {
                if (projectile != null && currentCity.CanProduceProjectile(projectile))
                {
                    availableProjectiles.Add(projectile);
                }
            }
        }

        // Missiles: show all missile types whose tech requirements the civ meets
        availableMissiles.Clear();
        if (ownerCiv != null)
        {
            var allMissiles = ResourceCache.GetAllMissiles();
            foreach (var missile in allMissiles)
            {
                if (missile == null) continue;
                bool techOk = true;
                if (missile.requiredTechs != null)
                    foreach (var tech in missile.requiredTechs)
                        if (tech != null && !ownerCiv.researchedTechs.Contains(tech)) { techOk = false; break; }
                if (techOk) availableMissiles.Add(missile);
            }
        }

        // Cache the results for next time
        _cachedAvailableBuildings.Clear();
        _cachedAvailableBuildings.AddRange(availableBuildings);
        _cachedAvailableUnits.Clear();
        _cachedAvailableUnits.AddRange(availableUnits);
        _cachedAvailableWorkers.Clear();
        _cachedAvailableWorkers.AddRange(availableWorkerUnits);
        _cachedAvailableEquipment.Clear();
        _cachedAvailableEquipment.AddRange(availableEquipment);
        _cachedAvailableProjectiles.Clear();
        _cachedAvailableProjectiles.AddRange(availableProjectiles);
        _cachedAvailableMissiles.Clear();
        _cachedAvailableMissiles.AddRange(availableMissiles);
        _buildOptionsCacheDirty = false;
    }
    
    private void PopulateBuildOptionsList()
    {
        // Clear all containers
        foreach (Transform t in buildingsContainer) Destroy(t.gameObject);
        foreach (Transform t in unitsContainer) Destroy(t.gameObject);
        foreach (Transform t in equipmentContainer) Destroy(t.gameObject);
        if (projectilesContainer != null) 
            foreach (Transform t in projectilesContainer) Destroy(t.gameObject);

        // Display Buildings in buildings container
        foreach (var building in availableBuildings.OrderBy(b => b.productionCost))
        {
            CreateBuildOptionButton(building, building.GetIcon(currentCity != null ? currentCity.owner : null), building.buildingName, building.productionCost, buildingsContainer);
        }
        
        // Display Combat Units in units container
        foreach (var unit in availableUnits.OrderBy(u => u.productionCost))
        {
            CreateBuildOptionButton(unit, unit.GetIcon(currentCity != null ? currentCity.owner : null), unit.unitName, unit.productionCost, unitsContainer);
        }
        
        // Display Worker Units also in units container
        foreach (var workerUnit in availableWorkerUnits.OrderBy(w => w.productionCost))
        {
            CreateBuildOptionButton(workerUnit, workerUnit.GetIcon(currentCity != null ? currentCity.owner : null), workerUnit.unitName, workerUnit.productionCost, unitsContainer);
        }
        
        // Equipment options
        foreach (var eq in availableEquipment.OrderBy(e => e.productionCost))
        {
            CreateBuildOptionButton(eq, eq.icon, eq.equipmentName, eq.productionCost, equipmentContainer);
        }
        
        // Projectile options
        if (projectilesContainer != null)
        {
            foreach (var projectile in availableProjectiles.OrderBy(p => p.productionCost))
            {
                CreateBuildOptionButton(projectile, projectile.icon, projectile.projectileName, projectile.productionCost, projectilesContainer);
            }
        }

        // Missile production options
        if (missilesContainer != null)
        {
            foreach (Transform t in missilesContainer) Destroy(t.gameObject);
            foreach (var missile in availableMissiles.OrderBy(m => m.productionCost))
            {
                CreateBuildOptionButton(missile, missile.icon, missile.missileName, missile.productionCost, missilesContainer);
            }
        }

        // Refresh launch missile button visibility
        RefreshLaunchMissileButton();
    }

    private void RefreshLaunchMissileButton()
    {
        int stored = currentCity?.storedMissiles?.Count ?? 0;
        if (launchMissileButton != null)
            launchMissileButton.gameObject.SetActive(stored > 0);
        if (openMissilePanelButton != null)
            openMissilePanelButton.gameObject.SetActive(stored > 0);
        if (launchMissileButtonText != null)
            launchMissileButtonText.text = stored > 0 ? $"Launch Missile ({stored})" : "Launch Missile";
    }

    private void OnLaunchMissileClicked()
    {
        if (currentCity == null) return;
        MissilePanelUI.Instance?.OpenForCity(currentCity);
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
        // NEW: If this is a projectile, show owned count if available
        else if (itemData is GameCombat.ProjectileData pd)
        {
            var ownedText = btnGO.transform.Find("OwnedCount")?.GetComponent<TextMeshProUGUI>();
            if (ownedText != null && currentCity != null && currentCity.owner != null)
            {
                ownedText.text = $"Owned: {currentCity.owner.GetProjectileCount(pd)}";
            }
            // Wire BuyButton if present (for instant gold purchase)
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

    private void RefreshCitizenAssignmentSummary()
    {
        if (currentCity == null) return;
        currentCity.RecalculateCitizenAssignmentCaches();
        int tileWorkers = currentCity.GetAssignedCount(CityCitizenJobType.TileWorker);
        int rural = currentCity.GetAssignedCount(CityCitizenJobType.RuralSpecialist);
        int urban = currentCity.GetAssignedCount(CityCitizenJobType.UrbanSpecialist);
        int unemployed = currentCity.GetUnemployedCount();
        if (citizenJobsSummaryText != null)
            citizenJobsSummaryText.text = $"Workers: {tileWorkers} | Rural: {rural} | Urban: {urban} | Unemployed: {unemployed}";
        if (unemploymentWarningText != null)
        {
            unemploymentWarningText.gameObject.SetActive(unemployed > 0);
            unemploymentWarningText.text = unemployed > 0 ? $"{unemployed} unemployed citizens are lowering order and raising bandit risk." : "";
        }
        if (orderCrimeSummaryText != null)
            orderCrimeSummaryText.text = $"Order: {currentCity.orderRating}/{currentCity.maxOrder} | Bandit Risk: +{currentCity.CachedBanditRiskFromUnemployment}";
    }

    public void Hide()
    {
        CityTileOverlayController.Instance?.ExitCityAssignmentMode();
        gameObject.SetActive(false);
        // Always restore the unit info panel when the city UI is closed.
        if (UIManager.Instance != null && UIManager.Instance.unitInfoPanel != null)
        {
            UIManager.Instance.unitInfoPanel.SetActive(true);
        }
    }
}
