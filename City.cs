// Assets/Scripts/Cities/City.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class City : MonoBehaviour
{
    // ─── Events ───
    public event Action<City, BuildingData> OnBuildingCompleted;
    public event Action<City, BuildingData, BuildingRemovalReason> OnBuildingRemoved;

    public enum BuildingRemovalReason
    {
        Dismantled,
        Replaced,
        Destroyed,
    }

    // Production Queue Entry Definition
    public class ProdEntry {
        public enum Type { Unit, Worker, Building, District, Equipment, Projectile, Missile }
        public Type       type;
        public ScriptableObject data;      // CombatUnitData, WorkerUnitData, BuildingData, DistrictData, EquipmentData, ProjectileData, or MissileData
        public int        remainingPts;    // turns left in production
        public int        goldCost;        // for instant buy
        public ResourceData[] requiredResources;
        public ResourceCost[] requiredResourceCosts;
        public Biome[]    requiredTerrains;
        public bool       reqCoast;        // Requires coastal city
        public bool       reqHarbor;       // Requires harbor building

        public ProdEntry(ScriptableObject d, int prodCost, int gCost,
                        ResourceData[] reqRes, Biome[] reqTerrains, 
                        bool coast, bool harbor, Type t, ResourceCost[] reqResourceCosts = null)
        {
            data = d;
            remainingPts = prodCost;
            goldCost     = gCost;
            requiredResources = reqRes;
            requiredResourceCosts = reqResourceCosts;
            requiredTerrains  = reqTerrains;
            reqCoast = coast;
            reqHarbor = harbor;
            type = t;
        }
    }
    
    [Header("Core Data")]
    public string cityName;
    public Civilization owner;
    public Civilization OriginalOwner;
    [Tooltip("Whether this city is the civilization's designated capital.")]
    public bool isCapital;
    public int centerTileIndex;
    [Tooltip("Which planet gameplay layer this city belongs to. Used by city bonus layer filters.")]
    public GameManager.PlanetLayerType cityLayer = GameManager.PlanetLayerType.Surface;
    [Tooltip("Which planet this city belongs to (multi-planet gameplay).")]
    public int planetIndex = -1;
    public Governor governor;

    // Convenience accessor: always use the correct planet's TileSystem.
    private TileSystem TileSys => TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;

    [Header("Growth & Level")]
    public int level = 1;
    public int foodStorage = 0;
    public int foodGrowthRequirement = 20;
    
    [Header("Population Consumption")]
    [Tooltip("Food consumed per population level per turn")]
    public int foodConsumptionPerPopulation = 1;
    public int Population => Mathf.Max(1, level);

    [Header("Citizen Assignment")]
    [SerializeField] private bool manualCitizenAssignment = false;
    [SerializeField] private List<CityCitizenAssignment> citizenAssignments = new List<CityCitizenAssignment>();

    [Header("Unemployment Effects")]
    [SerializeField] private int orderPenaltyPerUnemployedCitizen = 2;
    [SerializeField] private int banditRiskPerUnemployedCitizen = 5;
    [SerializeField] private int cachedUnemployedCitizens = 0;
    [SerializeField] private int cachedBanditRiskFromUnemployment = 0;

    public IReadOnlyList<CityCitizenAssignment> CitizenAssignments => citizenAssignments;
    public bool ManualCitizenAssignment => manualCitizenAssignment;
    public int CachedUnemployedCitizens => cachedUnemployedCitizens;
    public int CachedBanditRiskFromUnemployment => cachedBanditRiskFromUnemployment;

    [Header("Defense & Morale")]
    [Tooltip("Base maximum city defense before building, technology, culture, pantheon, and belief bonuses.")]
    public int baseMaxDefense = 100;
    public int defenseRating = 100;
    public int maxDefense = 100;
    [Tooltip("Base maximum city happiness/morale before building, technology, culture, pantheon, and belief bonuses.")]
    public int baseMaxHappiness = 100;
    public int moraleRating = 100;
    public int maxMorale = 100;
    public int moraleDropPerTurn = 1;
    [Tooltip("Base maximum city order before building bonuses. Order reduces rebellion/crime and trade route raid risk.")]
    public int baseMaxOrder = 100;
    public int orderRating = 100;
    public int maxOrder = 100;
    public int orderDropPerTurn = 1;
    [Tooltip("Last computed per-turn morale loss caused by citizens following non-state religions.")]
    public int cachedNonStateReligionUnhappiness = 0;

    // Track the last civilization that attacked this city (for surrender/capture)
    private Civilization lastAttackingCiv = null;

    [Header("Loyalty")]
    [Tooltip("0 = total unrest, 100 = full loyalty")]
    [Range(0f, 100f)]
    public float loyalty = 100f;
    [Tooltip("If loyalty falls to or below this, the city revolts")]
    public float revoltThreshold = 30f;
    [Tooltip("Flat loyalty support applied while this city is the capital.")]
    public float capitalLoyaltyBonus = 10f;

    [Header("Territory")]
    public int baseRadius = 1;

    [Header("Building Slots")]
    [Tooltip("Base building slots available in this city by category.")]
    public CitySlotModifier[] baseBuildingSlots = new CitySlotModifier[]
    {
        new CitySlotModifier { slotType = CitySlotType.Infrastructure, slotIncrease = 3 },
        new CitySlotModifier { slotType = CitySlotType.Food, slotIncrease = 1 },
        new CitySlotModifier { slotType = CitySlotType.Production, slotIncrease = 1 },
        new CitySlotModifier { slotType = CitySlotType.Commerce, slotIncrease = 1 },
        new CitySlotModifier { slotType = CitySlotType.Defense, slotIncrease = 1 }
    };
    [Tooltip("Attached hamlets, villages, towns, suburbs, and sectors extending this city.")]
    public List<CitySettlementExtension> attachedSettlements = new List<CitySettlementExtension>();

    [Header("Production")]
    public int productionPerTurn = 10;
    private int resourceSurplusProductionBonusThisTurn;
    public List<ProdEntry> productionQueue = new List<ProdEntry>();

    [Header("Built Content")]
    // Track (BuildingData, its spawned GameObject) so we can replace/destroy instances
    public List<(BuildingData data, GameObject instance)> builtBuildings = new List<(BuildingData, GameObject)>();
    [System.NonSerialized] private List<bool> buildingUpkeepSatisfied = new List<bool>();
    [System.NonSerialized] private List<ResourceUpkeepFailureBehavior> buildingUpkeepFailureBehavior = new List<ResourceUpkeepFailureBehavior>();
    [System.NonSerialized] private List<float> buildingUpkeepFailureMultiplier = new List<float>();
    // Track (DistrictData, its spawned GameObject, tile index) for districts
    public List<(DistrictData data, GameObject instance, int tileIndex)> builtDistricts = new List<(DistrictData, GameObject, int)>();
    public List<CombatUnitData> producedUnits = new List<CombatUnitData>();
    public List<EquipmentData> producedEquipment = new List<EquipmentData>();

    [Header("Missile Storage")]
    [Tooltip("Missiles produced by this city and ready to be launched.")]
    public List<MissileData> storedMissiles = new List<MissileData>();
    [Tooltip("Maximum number of missiles this city can store at once.")]
    public int maxMissileStorage = 10;

    [Header("Yields & Improvements")]
    public List<ImprovementData> nearbyImprovements = new List<ImprovementData>();
    [Tooltip("Base faith generated per turn by this city")]
    public int baseFaithPerTurn = 0;

    // --- Programmatic Label UI ---
    private Canvas labelCanvas;
    private UnityEngine.UI.Image civIconImg;
    private TMPro.TextMeshProUGUI nameText;
    private TMPro.TextMeshProUGUI levelText;
    private TMPro.TextMeshProUGUI loyaltyText;
    
    // Cached RectTransform references to avoid repeated GetComponent calls
    private RectTransform cachedCanvasRect;
    private RectTransform cachedBgRect;
    private RectTransform cachedIconRect;
    private RectTransform cachedNameRect;
    private RectTransform cachedLevelRect;
    private RectTransform cachedLoyaltyRect;
    // Offset above city center (in world units)
    private float labelVerticalOffset = 2.5f; // You can tweak this for your city model size
    // How large the label appears at a reference distance
    private float labelScaleAtReferenceDistance = 0.018f; // 1/10th original size
    // The distance from camera at which the label is at reference scale
    private float labelReferenceDistance = 20f;
    // Minimum and maximum scale for the label
    private float labelMinScale = 0.01f;
    private float labelMaxScale = 0.035f;
    
    // Cache references
    // Remove Hexasphere reference
    // private Hexasphere hexasphere;
    private PlanetGenerator planetGenerator;

    // Cached yields from last turn
    private int cachedGold;
    private int cachedProduction;
    private int cachedFood;
    private int cachedScience;
    private int cachedCulture;
    private int cachedPolicyPoints;
    private int cachedFaith;

    [Header("Disease")]
    [Tooltip("Active diseases currently afflicting this city.")]
    public List<DiseaseInstance> activeDiseases = new List<DiseaseInstance>();

    [Tooltip("Immunity cooldowns: disease → remaining immune turns after recovery.")]
    public Dictionary<DiseaseData, int> diseaseImmunities = new Dictionary<DiseaseData, int>();

    [HideInInspector]
    public float faminePopulationLossProgress = 0f;

    // Dictionary to track which tile each district in queue will be placed on
    private Dictionary<DistrictData, int> districtTileTargets = new Dictionary<DistrictData, int>();

    void Start()
    {
        // If owner isn't set, this object is probably a template/prefab, do nothing.
        if (owner == null) return; 

        if (OriginalOwner == null)
            OriginalOwner = owner;

        owner.AddCity(this);

        // Determine planet context early (city prefab may not be parented under a planet)
        if (planetIndex < 0)
        {
            // Prefer explicit parent planet generator if present
            var pg = GetComponentInParent<PlanetGenerator>();
            if (pg != null) planetIndex = pg.planetIndex;
            else if (planetGenerator != null) planetIndex = planetGenerator.planetIndex;
            else if (GameManager.Instance != null) planetIndex = GameManager.Instance.currentPlanetIndex;
            else planetIndex = 0;
        }
        var ts = TileSys;
        
        // Ensure city center and territory tiles are assigned to civ
        var territory = GetTerritoryTiles(baseRadius);
        foreach (var idx in territory)
        {
            if (ts != null)
            {
                ts.SetTileOwner(idx, owner, this);
            }
        }

        CreateLabelUI();
        
        // Cache reference: prefer owner's planet generator helper, then GameManager fallbacks
        planetGenerator = owner?.GetPlanetGeneratorForIndex(planetIndex)
                   ?? GameManager.Instance?.GetPlanetGenerator(planetIndex)
                   ?? GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planetGenerator != null) planetIndex = planetGenerator.planetIndex;
        RefreshCityDefenseAndHappinessBonuses();
    }

    /// <summary>
    /// Initialize the city with a name, owner and optional governor
    /// </summary>
    public void Initialize(string name, Civilization civ, Governor gov = null)
    {
        cityName = name;
        owner = civ;
        if (OriginalOwner == null)
            OriginalOwner = civ;
        governor = gov;
        loyalty = 100f;
    }

    public Civilization GetProductionHeritageOwner()
    {
        return OriginalOwner != null ? OriginalOwner : owner;
    }

    public CombatUnitData ResolveCombatUnitForProduction(CombatUnitData unitData)
    {
        if (unitData == null)
            return null;

        var heritageOwner = GetProductionHeritageOwner();
        if (heritageOwner == null)
            return unitData;

        var baseUnit = heritageOwner.GetBaseUnitData(unitData);
        var heritageUnit = heritageOwner.GetUnitData(baseUnit);
        return heritageUnit != null ? heritageUnit.GetLatestUnlockedUpgrade(owner) : null;
    }

    public BuildingData ResolveBuildingForProduction(BuildingData buildingData)
    {
        if (buildingData == null)
            return null;

        var heritageOwner = GetProductionHeritageOwner();
        if (heritageOwner == null)
            return buildingData;

        var baseBuilding = heritageOwner.GetBaseBuildingData(buildingData);
        return heritageOwner.GetBuildingData(baseBuilding);
    }

    public bool IsCombatUnitAvailableForProduction(CombatUnitData unitData)
    {
        if (owner == null || unitData == null)
            return false;

        var resolvedUnit = ResolveCombatUnitForProduction(unitData);
        return resolvedUnit != null && owner.IsCombatUnitAvailable(resolvedUnit);
    }

    public List<CombatUnitData> GetAvailableCombatUnitsForProduction()
    {
        var available = new List<CombatUnitData>();
        if (owner == null)
            return available;

        var seen = new HashSet<CombatUnitData>();
        foreach (var baseUnit in ResourceCache.GetAllCombatUnits())
        {
            if (baseUnit == null)
                continue;

            var resolvedUnit = ResolveCombatUnitForProduction(baseUnit);
            if (resolvedUnit == null || seen.Contains(resolvedUnit) || !owner.IsCombatUnitAvailable(resolvedUnit) || !CanTrainUnitInThisCity(resolvedUnit))
                continue;

            seen.Add(resolvedUnit);
            available.Add(resolvedUnit);
        }

        return available;
    }

    public List<BuildingData> GetAvailableBuildingsForProduction()
    {
        var available = new List<BuildingData>();
        if (owner == null)
            return available;

        var seen = new HashSet<BuildingData>();
        foreach (var baseBuilding in ResourceCache.GetAllBuildings())
        {
            if (baseBuilding == null)
                continue;

            var resolvedBuilding = ResolveBuildingForProduction(baseBuilding);
            if (resolvedBuilding == null || seen.Contains(resolvedBuilding))
                continue;

            if (!resolvedBuilding.AreRequirementsMet(owner))
                continue;
            if (!AreBuildingRequirementsMet(resolvedBuilding.requiredBuildings))
                continue;
            if (!CanFitBuildingInAnyAllowedSlot(resolvedBuilding))
                continue;

            bool alreadyBuilt = false;
            foreach (var (builtData, _) in builtBuildings)
            {
                if (builtData == null)
                    continue;

                if (builtData == resolvedBuilding ||
                    builtData == baseBuilding ||
                    (builtData.replacesBuilding != null && builtData.replacesBuilding == baseBuilding) ||
                    (resolvedBuilding.replacesBuilding != null && resolvedBuilding.replacesBuilding == builtData))
                {
                    alreadyBuilt = true;
                    break;
                }
            }

            if (alreadyBuilt)
                continue;

            seen.Add(resolvedBuilding);
            available.Add(resolvedBuilding);
        }

        return available;
    }

    public void RestoreBuiltBuildingsForSave(IEnumerable<BuildingData> savedBuildings)
    {
        foreach (var (_, instance) in builtBuildings)
        {
            if (instance != null)
                Destroy(instance);
        }

        builtBuildings.Clear();
        RefreshCityDefenseAndHappinessBonuses();
        defenseRating = maxDefense;
        moraleRating = maxMorale;
        orderRating = maxOrder;

        if (savedBuildings == null)
            return;

        foreach (var building in savedBuildings)
        {
            if (building != null)
                AddBuilding(building);
        }
    }

    public void RestoreProductionQueueForSave(List<ProdEntry> savedQueue, Dictionary<DistrictData, int> savedDistrictTargets)
    {
        productionQueue = savedQueue ?? new List<ProdEntry>();
        districtTileTargets = savedDistrictTargets ?? new Dictionary<DistrictData, int>();
    }

    private void CreateLabelUI()
    {
        // Create a new Canvas as a child
        GameObject canvasGO = new GameObject("CityLabelCanvas");
        canvasGO.transform.SetParent(transform);
        labelCanvas = canvasGO.AddComponent<Canvas>();
        labelCanvas.renderMode = RenderMode.WorldSpace;
        labelCanvas.worldCamera = Camera.main;
        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;
        scaler.referencePixelsPerUnit = 100;
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        cachedCanvasRect = canvasGO.GetComponent<RectTransform>(); // Cache RectTransform reference
        cachedCanvasRect.sizeDelta = new Vector2(3.5f, 1.5f);
        cachedCanvasRect.localScale = Vector3.one * (0.12f / 6f);
        cachedCanvasRect.localPosition = Vector3.zero;

        // Add a background panel (optional, for readability)
        GameObject bgGO = new GameObject("LabelBG");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0,0,0,0.4f);
        cachedBgRect = bgGO.GetComponent<RectTransform>(); // Cache RectTransform reference
        cachedBgRect.anchorMin = Vector2.zero;
        cachedBgRect.anchorMax = Vector2.one;
        cachedBgRect.offsetMin = Vector2.zero;
        cachedBgRect.offsetMax = Vector2.zero;

        // Add civ icon
        GameObject iconGO = new GameObject("CivIcon");
        iconGO.transform.SetParent(canvasGO.transform, false);
        civIconImg = iconGO.AddComponent<UnityEngine.UI.Image>();
        cachedIconRect = iconGO.GetComponent<RectTransform>(); // Cache RectTransform reference
        cachedIconRect.sizeDelta = new Vector2(200, 200);
        cachedIconRect.anchoredPosition = new Vector2(-60, 0);
        civIconImg.preserveAspect = true;

        // Add city name
        GameObject nameGO = new GameObject("CityName");
        nameGO.transform.SetParent(canvasGO.transform, false);
        nameText = nameGO.AddComponent<TMPro.TextMeshProUGUI>();
        nameText.fontSize = 32;
        nameText.alignment = TMPro.TextAlignmentOptions.Left;
        cachedNameRect = nameGO.GetComponent<RectTransform>(); // Cache RectTransform reference
        cachedNameRect.sizeDelta = new Vector2(200, 20);
        cachedNameRect.anchoredPosition = new Vector2(10, 30);

        // Add level
        GameObject levelGO = new GameObject("CityLevel");
        levelGO.transform.SetParent(canvasGO.transform, false);
        levelText = levelGO.AddComponent<TMPro.TextMeshProUGUI>();
        levelText.fontSize = 24;
        levelText.alignment = TMPro.TextAlignmentOptions.Left;
        cachedLevelRect = levelGO.GetComponent<RectTransform>(); // Cache RectTransform reference
        cachedLevelRect.sizeDelta = new Vector2(200, 32);
        cachedLevelRect.anchoredPosition = new Vector2(10, 0);

        // Add loyalty
        GameObject loyaltyGO = new GameObject("CityLoyalty");
        loyaltyGO.transform.SetParent(canvasGO.transform, false);
        loyaltyText = loyaltyGO.AddComponent<TMPro.TextMeshProUGUI>();
        loyaltyText.fontSize = 24;
        loyaltyText.alignment = TMPro.TextAlignmentOptions.Left;
        cachedLoyaltyRect = loyaltyGO.GetComponent<RectTransform>(); // Cache RectTransform reference
        cachedLoyaltyRect.sizeDelta = new Vector2(200, 32);
        cachedLoyaltyRect.anchoredPosition = new Vector2(10, -30);

        // Add a button for click events
        var btn = canvasGO.AddComponent<UnityEngine.UI.Button>();
        btn.onClick.AddListener(OnLabelClicked);

        UpdateLabelUI();
    }

    private void UpdateLabelUI()
    {
        if (nameText != null) nameText.text = cityName;
        if (levelText != null) levelText.text = $"Level {level}";
        if (loyaltyText != null) loyaltyText.text = $"Loyalty: {Mathf.RoundToInt(loyalty)}";
        if (civIconImg != null && owner != null && owner.civData != null)
        {
            civIconImg.sprite = owner.civData.icon;
            civIconImg.enabled = civIconImg.sprite != null;
        }
        else if (civIconImg != null)
        {
            civIconImg.enabled = false;
        }
    }

    private static Camera _cachedCam;
    private static int _cachedCamFrame = -1;

    void LateUpdate()
    {
        // Position label above city and face camera (every 3rd frame, staggered by instance)
        if (labelCanvas == null) return;
        if ((Time.frameCount + (this.GetRuntimeId() & 0x7FFFFFFF)) % 3 != 0) return;

        // Cache Camera.main across all City instances per frame
        if (_cachedCamFrame != Time.frameCount)
        {
            _cachedCam = Camera.main;
            _cachedCamFrame = Time.frameCount;
        }
        if (_cachedCam == null) return;

        Vector3 labelPos = transform.position + Vector3.up * labelVerticalOffset;
        labelCanvas.transform.position = labelPos;
        labelCanvas.transform.rotation = _cachedCam.transform.rotation;
        float camDist = Vector3.Distance(_cachedCam.transform.position, labelPos);
        float scale = labelScaleAtReferenceDistance * (camDist / labelReferenceDistance);
        scale = Mathf.Clamp(scale, labelMinScale, labelMaxScale);
        labelCanvas.transform.localScale = Vector3.one * scale;
    }

    // Call this whenever city data changes
    public void UpdateLabel()
    {
        UpdateLabelUI();
    }

    private void OnLabelClicked()
    {
if (UIManager.Instance != null)
        {
            var cityPanel = UIManager.Instance.GetPanel("CityPanel");
            if (cityPanel != null)
            {
                var cityUI = cityPanel.GetComponent<CityUI>();
                if (cityUI != null)
                {
                    cityUI.ShowForCity(this);
                    cityPanel.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("[City] CityUI component not found on cityPanel.");
                }
            }
            else
            {
                Debug.LogWarning("[City] CityPanel not found in UIManager.");
            }
        }
        else
        {
            Debug.LogWarning("[City] UIManager instance not found in scene to show city panel.");
        }
    }

    /// <summary>
    /// Called each turn at the beginning via Civilization.BeginTurn
    /// </summary>
    public void ProcessCityTurn()
    {
        RefreshCityDefenseAndHappinessBonuses();

        // 1) Collect yields (handled in Civilization)
        // Cache per-turn yields for collection by civilization
        cachedGold = GetGoldPerTurn();
        cachedFood = GetFoodPerTurn();
        cachedScience = GetSciencePerTurn();
        cachedCulture = GetCulturePerTurn();
        cachedPolicyPoints = GetPolicyPointPerTurn();
        cachedFaith = GetFaithPerTurn();
    // Add connected-city bonuses from roads/improvements
    var roadY = RoadConnectivityHelper.AggregateConnectedBonusesForCity(this);
    cachedGold += roadY.Gold;
    cachedProduction = Mathf.RoundToInt(GetProductionPerTurn() + roadY.Production); // ensure production cached too
    cachedFood += roadY.Food;
    cachedScience += roadY.Science;
    cachedCulture += roadY.Culture;
    cachedPolicyPoints += roadY.Policy;
    cachedFaith += roadY.Faith;

        // 1b) Apply disease yield penalties
        ApplyDiseaseYieldPenalties();
        
        // 2) Process loyalty
        ProcessLoyalty();
        
        // 3) Produce
        ProcessProduction();
        // 4) Growth
        ProcessGrowth();
        // 5) Morale decay
        cachedNonStateReligionUnhappiness = CalculateNonStateReligionUnhappinessPerTurn();
        moraleRating = Mathf.Max(0, moraleRating - moraleDropPerTurn - cachedNonStateReligionUnhappiness);
        orderRating = Mathf.Max(0, orderRating - orderDropPerTurn);
        RecalculateCitizenAssignmentCaches();
        int unemploymentOrderPenalty = GetUnemploymentOrderPenaltyPerTurn();
        orderRating = Mathf.Max(0, orderRating - unemploymentOrderPenalty);
        // 5b) Apply disease morale/loyalty/population effects
        ApplyDiseaseTurnEffects();
        // 6) Check surrender (only if defense was reduced by attacks, not just decay)
        // Surrender is handled in TakeDamage() when a unit attacks
        // If defense reaches 0 from other means, check for units on tile
        if (defenseRating <= 0 || moraleRating <= 0 || orderRating <= 0 || loyalty <= 0)
            HandleSurrender(lastAttackingCiv); // Use last attacking civ, or find from units on tile
        // 7) Update label
        UpdateLabel();
    }

    public float GetNonStateReligionFollowerCount()
    {
        if (owner == null || !owner.hasFoundedReligion || owner.foundedReligion == null)
            return 0f;

        var ts = TileSys;
        if (ts == null || !ts.IsReady())
            return 0f;

        Dictionary<ReligionData, float> pressureByReligion = new Dictionary<ReligionData, float>();
        Queue<(int tile, int distance)> queue = new Queue<(int tile, int distance)>();
        HashSet<int> visited = new HashSet<int>();

        queue.Enqueue((centerTileIndex, 0));
        visited.Add(centerTileIndex);

        while (queue.Count > 0)
        {
            var (tileIndex, distance) = queue.Dequeue();
            var pressures = ts.GetReligionPressures(tileIndex);
            if (pressures != null)
            {
                for (int i = 0; i < pressures.Count; i++)
                {
                    var entry = pressures[i];
                    if (entry.religion == null || entry.pressure <= 0f) continue;
                    if (!pressureByReligion.TryGetValue(entry.religion, out float current)) current = 0f;
                    pressureByReligion[entry.religion] = current + entry.pressure;
                }
            }

            if (distance >= TerritoryRadius) continue;
            var neighbors = ts.GetNeighbors(tileIndex);
            if (neighbors == null) continue;
            foreach (int neighbor in neighbors)
            {
                if (visited.Add(neighbor))
                    queue.Enqueue((neighbor, distance + 1));
            }
        }

        float totalPressure = 0f;
        float nonStatePressure = 0f;
        foreach (var kvp in pressureByReligion)
        {
            totalPressure += kvp.Value;
            if (kvp.Key != owner.foundedReligion)
                nonStatePressure += kvp.Value;
        }

        if (totalPressure <= 0f || nonStatePressure <= 0f)
            return 0f;

        return Mathf.Max(0f, level) * Mathf.Clamp01(nonStatePressure / totalPressure);
    }

    public int CalculateNonStateReligionUnhappinessPerTurn()
    {
        if (owner == null || !owner.hasFoundedReligion || owner.foundedReligion == null)
            return 0;

        float nonStateFollowers = GetNonStateReligionFollowerCount();
        if (nonStateFollowers <= 0f)
            return 0;

        float unhappinessPerFollower = owner.GetNonStateReligionUnhappinessPerFollower(planetIndex);
        return Mathf.Max(0, Mathf.RoundToInt(nonStateFollowers * unhappinessPerFollower));
    }

    /// <summary>
    /// Reduces cached yields based on active disease penalties.
    /// Called after yields are computed but before they are consumed.
    /// </summary>
    private void ApplyDiseaseYieldPenalties()
    {
        if (activeDiseases == null || activeDiseases.Count == 0) return;

        float foodMult = 1f;
        float prodMult = 1f;
        float goldMult = 1f;
        float sciMult = 1f;
        float culMult = 1f;
        float faithMult = 1f;

        foreach (var di in activeDiseases)
        {
            if (di == null || di.data == null) continue;
            var totals = owner != null ? owner.GetDiseaseModifierTotals(di.data, this) : default;
            float penaltyMultiplier = totals.CityYieldPenaltyMultiplier;
            foodMult  -= di.data.cityFoodPenaltyPct * penaltyMultiplier;
            prodMult  -= di.data.cityProductionPenaltyPct * penaltyMultiplier;
            goldMult  -= di.data.cityGoldPenaltyPct * penaltyMultiplier;
            sciMult   -= di.data.citySciencePenaltyPct * penaltyMultiplier;
            culMult   -= di.data.cityCulturePenaltyPct * penaltyMultiplier;
            faithMult -= di.data.cityFaithPenaltyPct * penaltyMultiplier;
        }

        // Clamp multipliers so yields can't go negative from disease alone
        foodMult  = Mathf.Max(0f, foodMult);
        prodMult  = Mathf.Max(0f, prodMult);
        goldMult  = Mathf.Max(0f, goldMult);
        sciMult   = Mathf.Max(0f, sciMult);
        culMult   = Mathf.Max(0f, culMult);
        faithMult = Mathf.Max(0f, faithMult);

        cachedFood       = Mathf.RoundToInt(cachedFood * foodMult);
        cachedProduction = Mathf.RoundToInt(cachedProduction * prodMult);
        cachedGold       = Mathf.RoundToInt(cachedGold * goldMult);
        cachedScience    = Mathf.RoundToInt(cachedScience * sciMult);
        cachedCulture    = Mathf.RoundToInt(cachedCulture * culMult);
        cachedFaith      = Mathf.RoundToInt(cachedFaith * faithMult);
    }

    /// <summary>
    /// Applies per-turn disease effects: population loss, morale drop, loyalty drop.
    /// Also ticks disease durations and handles recovery/immunity.
    /// </summary>
    private void ApplyDiseaseTurnEffects()
    {
        if (activeDiseases == null || activeDiseases.Count == 0) return;

        for (int i = activeDiseases.Count - 1; i >= 0; i--)
        {
            var di = activeDiseases[i];
            if (di == null || di.data == null) { activeDiseases.RemoveAt(i); continue; }
            var totals = owner != null ? owner.GetDiseaseModifierTotals(di.data, this) : default;

            // Population loss (fractional accumulation)
            di.accumulatedPopulationLoss += di.data.cityPopulationLossPerTurn * totals.CityPopulationLossMultiplier;
            while (di.accumulatedPopulationLoss >= 1f && level > 1)
            {
                level--;
                di.accumulatedPopulationLoss -= 1f;
            }

            // Morale penalty
            int moralePenalty = Mathf.Max(0, Mathf.RoundToInt(di.data.cityMoralePenaltyPerTurn * totals.CityMoralePenaltyMultiplier));
            moraleRating = Mathf.Max(0, moraleRating - moralePenalty);

            // Loyalty penalty
            loyalty = Mathf.Clamp(loyalty - (di.data.cityLoyaltyPenaltyPerTurn * totals.CityLoyaltyPenaltyMultiplier), 0f, 100f);

            // Tick duration
            if (!di.TickDuration())
            {
                // Disease expired — grant immunity
                if (di.data.immunityTurnsAfterRecovery > 0)
                    diseaseImmunities[di.data] = di.data.immunityTurnsAfterRecovery;
                activeDiseases.RemoveAt(i);
            }
        }

        // Tick immunity cooldowns
        var expiredKeys = new List<DiseaseData>();
        foreach (var kv in diseaseImmunities)
        {
            diseaseImmunities[kv.Key] = kv.Value - 1;
            if (diseaseImmunities[kv.Key] <= 0)
                expiredKeys.Add(kv.Key);
        }
        foreach (var key in expiredKeys)
            diseaseImmunities.Remove(key);
    }

    /// <summary>
    /// Returns true if this city currently has immunity to the given disease.
    /// </summary>
    public bool HasDiseaseImmunity(DiseaseData disease)
    {
        if (disease == null) return false;
        // Tech-granted immunity
        if (owner != null && disease.immunityTechs != null)
        {
            foreach (var tech in disease.immunityTechs)
            {
                if (tech != null && owner.researchedTechs.Contains(tech))
                    return true;
            }
        }
        if (owner != null && owner.GetDiseaseModifierTotals(disease, this).grantsImmunity)
            return true;
        // Recovery immunity cooldown
        if (diseaseImmunities != null && diseaseImmunities.ContainsKey(disease))
            return true;
        return false;
    }

    /// <summary>
    /// Returns the resistance multiplier (0-1) for this city against a specific disease.
    /// 0 = fully resistant, 1 = no resistance.
    /// </summary>
    public float GetDiseaseResistance(DiseaseData disease)
    {
        if (disease == null || disease.resistanceBuildings == null) return 1f;
        float resistance = 0f;
        foreach (var rb in disease.resistanceBuildings)
        {
            if (rb == null) continue;
            if (builtBuildings.Exists(b => b.data == rb))
                resistance += disease.resistancePctPerBuilding;
        }
        return Mathf.Clamp01(1f - resistance);
    }

    /// <summary>
    /// Returns true if this city is currently infected by the given disease.
    /// </summary>
    public bool HasDisease(DiseaseData disease)
    {
        if (disease == null || activeDiseases == null) return false;
        return activeDiseases.Exists(d => d.data == disease);
    }

    /// <summary>
    /// Infects this city with a disease. No-op if already infected or immune.
    /// </summary>
    public bool InfectWithDisease(DiseaseData disease)
    {
        if (disease == null) return false;
        if (HasDisease(disease)) return false;
        if (HasDiseaseImmunity(disease)) return false;
        int duration = disease.baseDuration > 0 ? disease.baseDuration : -1;
        if (owner != null && duration > 0)
        {
            var totals = owner.GetDiseaseModifierTotals(disease, this);
            duration = Mathf.Max(1, Mathf.RoundToInt(duration * totals.DurationMultiplier));
        }
        activeDiseases.Add(new DiseaseInstance(disease, duration));
        return true;
    }

    /// <summary>
    /// Cures a specific disease from this city. Optionally grants immunity.
    /// </summary>
    public bool CureDisease(DiseaseData disease, bool grantImmunity = true)
    {
        if (disease == null || activeDiseases == null) return false;
        int idx = activeDiseases.FindIndex(d => d.data == disease);
        if (idx < 0) return false;
        if (grantImmunity && disease.immunityTurnsAfterRecovery > 0)
            diseaseImmunities[disease] = disease.immunityTurnsAfterRecovery;
        activeDiseases.RemoveAt(idx);
        return true;
    }

    /// <summary>
    /// Adjusts loyalty based on owner's war-weariness, famine, governor specialization,
    /// and governor personality/opinion (CK-lite system).
    /// </summary>
    private void ProcessLoyalty()
    {
        // War-weariness penalty: convert owner's 0–1 warWeariness to percent
        float warPenaltyPercent = owner.warWeariness * 100f;

        // Famine penalty: a flat 5% loyalty loss if owner ran out of food
        float faminePenaltyPercent = owner.famineActive ? 5f : 0f;
        
        // Calculate governor specialization bonus (base)
        float governorBonus = 0f;
        if (governor != null)
        {
            switch (governor.specialization)
            {
                case Governor.Specialization.Military:   governorBonus = 10f; break;
                case Governor.Specialization.Economic:    governorBonus = 8f;  break;
                case Governor.Specialization.Scientific:  governorBonus = 5f;  break;
                case Governor.Specialization.Cultural:    governorBonus = 12f; break;
                case Governor.Specialization.Religious:   governorBonus = 15f; break;
                case Governor.Specialization.Industrial:  governorBonus = 7f;  break;
            }

            // CK-lite: governor opinion drives loyalty contribution
            // Tick opinion first (decays modifiers), then get loyalty effect
            governor.TickOpinion();
            governorBonus += governor.GetLoyaltyContribution();
        }

        if (isCapital)
            governorBonus += capitalLoyaltyBonus;

        float happinessRatio = maxMorale > 0 ? moraleRating / (float)maxMorale : 0f;
        float orderRatio = maxOrder > 0 ? orderRating / (float)maxOrder : 0f;
        float happinessLoyaltyModifier = (happinessRatio - 0.5f) * 20f;
        float orderLoyaltyModifier = (orderRatio - 0.5f) * 30f;

        loyalty = loyalty - warPenaltyPercent - faminePenaltyPercent + governorBonus + happinessLoyaltyModifier + orderLoyaltyModifier;

        // Clamp 0–100
        loyalty = Mathf.Clamp(loyalty, 0f, 100f);

        // Check for revolt. Low order and low happiness increase random rebellion risk before total loyalty collapse.
        float rebellionChance = GetRebellionChance();
        if (loyalty <= revoltThreshold || UnityEngine.Random.value < rebellionChance)
            TriggerRevolt();
    }

    /// <summary>
    /// What happens when loyalty collapses
    /// </summary>
    public void TriggerRevolt() => TriggerRevolt(null);

    /// <summary>
    /// TriggerRevolt with an optional name for the rebel faction.
    /// When rebelName is supplied the spawned rebel civ is renamed accordingly.
    /// </summary>
    public void TriggerRevolt(string rebelName)
    {
// 1) Remove from old owner
        var oldOwner = owner;
    oldOwner?.RemoveCity(this);

        // 2) Create or fetch rebel faction
        var rebelCiv = string.IsNullOrEmpty(rebelName)
            ? CivilizationManager.Instance.CreateRebelFaction(this)
            : CivilizationManager.Instance.CreateRebelFaction(this, rebelName);

        // 3) Transfer city to rebel civ
        owner = rebelCiv;
        rebelCiv?.AddCity(this);

        // 4) Reassign any garrisoned units (those on the city tile)
        //    Combat units:
        var combatToMove = oldOwner.combatUnits
            .Where(u => u.currentTileIndex == centerTileIndex)
            .ToList();
        foreach (var u in combatToMove)
        {
            oldOwner.combatUnits.Remove(u);
            rebelCiv.combatUnits.Add(u);
            u.Initialize(u.data, rebelCiv);  // reset its owner internally
        }
        //    Worker units:
        var workerToMove = oldOwner.workerUnits
            .Where(w => w.currentTileIndex == centerTileIndex)
            .ToList();
        foreach (var w in workerToMove)
        {
            oldOwner.workerUnits.Remove(w);
            rebelCiv.workerUnits.Add(w);
            w.Initialize(w.data, rebelCiv, w.currentTileIndex);   // reset its owner, keep position
        }

        // 5) Reassign map-ownership of the city's tiles
        // Use this city's planet context (multi-planet support)
        var planet = ResolvePlanetGenerator();
        if (planet != null)
        {
            // Get territory radius based on number of remaining cities
            int radius = oldOwner.cities.Count >= 1 ? oldOwner.cities.Count : 1;
            // Convert tiles in radius to rebel ownership
            List<int> territoryTiles = GetTerritoryTiles(radius);
            foreach (int idx in territoryTiles)
            {
                var ts = TileSys;
                if (ts != null) ts.SetTileOwner(idx, rebelCiv, this);
            }
        }

        // 6) Reset loyalty so rebels stabilize somewhat
        loyalty = 50f;
        orderRating = Mathf.Max(50, orderRating);

        // TODO: spawn rebel units, trigger UI popup, play SFX/VFX, etc.
    }
    
    // Helper method to get all tiles in this city's territory
    public List<int> GetTerritoryTiles(int radius)
    {
        List<int> tiles = new List<int>();
        var ts = TileSys;
        if (ts == null) return tiles;
        
        // Start with center and direct neighbors
        tiles.Add(centerTileIndex);
        foreach (int neighbor in ts.GetNeighbors(centerTileIndex))
        {
            tiles.Add(neighbor);
        }
        
        // Expand outward if radius > 1
        HashSet<int> processed = new HashSet<int>(tiles);
        for (int r = 1; r < radius; r++)
        {
            List<int> newTiles = new List<int>();
            foreach (int tile in tiles)
            {
                foreach (int neighbor in ts.GetNeighbors(tile))
                {
                    if (!processed.Contains(neighbor))
                    {
                        newTiles.Add(neighbor);
                        processed.Add(neighbor);
                    }
                }
            }
            tiles.AddRange(newTiles);
        }
        
        return tiles;
    }


    public int GetBuildingSlotCapacity(CitySlotType slotType)
    {
        int capacity = 0;
        AddSlotModifiers(ref capacity, baseBuildingSlots, slotType);
        AddSlotModifiers(ref capacity, owner?.civData?.citySlotModifiers, slotType);
        AddSlotModifiers(ref capacity, owner?.civData?.effects?.citySlotModifiers, slotType);

        if (owner?.researchedTechs != null)
        {
            foreach (var tech in owner.researchedTechs)
                AddSlotModifiers(ref capacity, tech?.citySlotModifiers, slotType);
        }

        if (owner?.researchedCultures != null)
        {
            foreach (var culture in owner.researchedCultures)
                AddSlotModifiers(ref capacity, culture?.citySlotModifiers, slotType);
        }

        AddSlotModifiers(ref capacity, owner?.currentGovernment?.citySlotModifiers, slotType);

        if (attachedSettlements != null)
        {
            foreach (var settlement in attachedSettlements)
                AddSlotModifiers(ref capacity, settlement?.slotModifiers, slotType);
        }

        return Mathf.Max(0, capacity);
    }

    public int GetUsedBuildingSlots(CitySlotType slotType)
    {
        return CalculateAssignedBuildingSlots().TryGetValue(slotType, out int used) ? used : 0;
    }

    public bool CanFitBuildingInSlot(BuildingData building, CitySlotType slotType)
    {
        if (!CanBuildingUseSlot(building, slotType)) return false;
        var usedBySlot = CalculateAssignedBuildingSlots();
        int used = usedBySlot.TryGetValue(slotType, out int count) ? count : 0;
        return used < GetBuildingSlotCapacity(slotType);
    }

    public bool CanFitBuildingInAnyAllowedSlot(BuildingData building)
    {
        if (building == null) return false;
        var usedBySlot = CalculateAssignedBuildingSlots();
        foreach (var slotType in GetAllowedSlotsForBuilding(building))
        {
            int used = usedBySlot.TryGetValue(slotType, out int count) ? count : 0;
            if (used < GetBuildingSlotCapacity(slotType))
                return true;
        }
        return false;
    }


    private Dictionary<CitySlotType, int> CalculateAssignedBuildingSlots()
    {
        var usedBySlot = new Dictionary<CitySlotType, int>();
        foreach (var (data, _) in builtBuildings)
        {
            if (data == null) continue;
            foreach (var slotType in GetAllowedSlotsForBuilding(data))
            {
                int used = usedBySlot.TryGetValue(slotType, out int count) ? count : 0;
                if (used < GetBuildingSlotCapacity(slotType))
                {
                    usedBySlot[slotType] = used + 1;
                    break;
                }
            }
        }
        return usedBySlot;
    }

    public void AddAttachedSettlement(CitySettlementExtension settlement)
    {
        if (settlement == null) return;
        if (attachedSettlements == null) attachedSettlements = new List<CitySettlementExtension>();
        attachedSettlements.Add(settlement);
        ClaimAttachedSettlementTerritory(settlement);
    }

    private static void AddSlotModifiers(ref int capacity, CitySlotModifier[] modifiers, CitySlotType slotType)
    {
        if (modifiers == null) return;
        foreach (var modifier in modifiers)
        {
            if (modifier != null && modifier.slotType == slotType)
                capacity += modifier.slotIncrease;
        }
    }

    private static IEnumerable<CitySlotType> GetAllowedSlotsForBuilding(BuildingData building)
    {
        if (building?.allowedCitySlotTypes != null && building.allowedCitySlotTypes.Length > 0)
            return building.allowedCitySlotTypes;
        return new[] { CitySlotType.Infrastructure };
    }

    private static bool CanBuildingUseSlot(BuildingData building, CitySlotType slotType)
    {
        if (building == null) return false;
        foreach (var allowed in GetAllowedSlotsForBuilding(building))
        {
            if (allowed == slotType) return true;
        }
        return false;
    }

    private int GetAttachedSettlementTerritoryRadiusBonus()
    {
        if (attachedSettlements == null) return 0;
        int bonus = 0;
        foreach (var settlement in attachedSettlements)
        {
            if (settlement != null)
                bonus += Mathf.Max(0, settlement.territoryRadiusBonus);
        }
        return bonus;
    }

    private void ClaimAttachedSettlementTerritory(CitySettlementExtension settlement)
    {
        if (settlement == null || owner == null) return;
        int originalCenter = centerTileIndex;
        if (settlement.centerTileIndex >= 0)
            centerTileIndex = settlement.centerTileIndex;

        var tiles = GetTerritoryTiles(Mathf.Max(1, settlement.territoryRadiusBonus));
        centerTileIndex = originalCenter;

        var ts = TileSys;
        if (ts == null) return;
        foreach (var idx in tiles)
            ts.SetTileOwner(idx, owner, this);
    }

    /// <summary>
    /// Resolve an appropriate PlanetGenerator for this city, preferring the owner's helper.
    /// Also updates `planetIndex` when a generator with a concrete index is found.
    /// </summary>
    private PlanetGenerator ResolvePlanetGenerator()
    {
        var gen = owner?.GetPlanetGeneratorForIndex(planetIndex)
                  ?? GameManager.Instance?.GetPlanetGenerator(planetIndex)
                  ?? GameManager.Instance?.GetCurrentPlanetGenerator();
        if (gen != null) planetIndex = gen.planetIndex;
        return gen;
    }

    void ProcessProduction()
    {
        // If nothing in queue, just return
        if (productionQueue.Count == 0)
            return;

        // Get the current item in production (first in queue)
        var prodEntry = productionQueue[0];
        
        // Apply production points from this turn. Unit entries can receive separate
        // training-speed modifiers from techs, cultures, and local buildings.
        prodEntry.remainingPts -= GetProductionPerTurn(prodEntry);
        resourceSurplusProductionBonusThisTurn = 0;
        
        // Check if completed
        if (prodEntry.remainingPts <= 0)
        {
            // Complete the item
            CompleteItem(prodEntry.data);
            
            // Remove from queue
            productionQueue.RemoveAt(0);
        }
    }

    /// <summary>
    /// Quick lookup of your city's harbor status
    /// </summary>
    private bool HasHarbor()
        => HasOperationalBuilding(data => data != null && data.providesHarbor);
        
    /// <summary>
    /// Quick lookup if city has a holy site
    /// </summary>
    public bool HasHolySite()
        => builtDistricts.Exists(tuple => tuple.data.isHolySite);
        
    /// <summary>
    /// Get the tile index of the city's holy site, if any
    /// </summary>
    public int GetHolySiteTileIndex()
    {
        var holySite = builtDistricts.Find(tuple => tuple.data.isHolySite);
        return holySite.tileIndex;
    }

    /// <summary>
    /// Quick lookup of coastal/accessible water tiles this city controls.
    /// Lakes and rivers count as valid coastal access for unit/building requirements.
    /// </summary>
    private bool ControlsCoast()
    {
        if (planetGenerator == null) throw new System.Exception("City references not set!");
        var ts = TileSys;
        if (ts == null) return false;
        
        foreach (int idx in ts.GetNeighbors(centerTileIndex))
        {
            var tileData = ts.GetTileData(idx);
            if (tileData == null) continue;
            var biome = tileData.biome;
            if (biome == Biome.Coast || biome == Biome.Seas || biome == Biome.Ocean || biome == Biome.Lake || biome == Biome.River)
                return true;
            if (tileData.waterType == TileWaterType.Lake || tileData.waterType == TileWaterType.River)
                return true;
        }
        return false;
    }


    public bool CanTrainUnitInThisCity(CombatUnitData unitData)
    {
        if (unitData == null) return false;
        if (unitData.requiresCoastalCity && !ControlsCoast()) return false;
        if (unitData.requiresHarbor && !HasHarbor()) return false;
        return HasRequiredUnitBuildings(unitData.requiredCityBuildings);
    }

    public bool CanTrainWorkerInThisCity(WorkerUnitData workerData)
    {
        if (workerData == null) return false;
        if (workerData.requiresCoastalCity && !ControlsCoast()) return false;
        if (workerData.requiresHarbor && !HasHarbor()) return false;
        return HasRequiredUnitBuildings(workerData.requiredCityBuildings);
    }

    private bool HasRequiredUnitBuildings(UnitBuildingRequirement[] requirements)
    {
        if (requirements == null || requirements.Length == 0) return true;

        foreach (var requirement in requirements)
        {
            if (requirement == null) continue;
            bool satisfied = HasOperationalBuilding(building =>
                building != null &&
                ((requirement.building != null && building == requirement.building) ||
                 (requirement.useBuildingCategoryFilter && MatchesBuildingCategory(building, requirement.buildingCategory))));
            if (!satisfied) return false;
        }

        return true;
    }

    private bool MeetsUnitPointRequirements(CombatUnitData unitData)
    {
        if (unitData == null || owner == null) return false;
        if (unitData.requiresPolicyPoints && owner.policyPoints < unitData.requiredPolicyPoints)
        {
            Debug.LogWarning($"Cannot produce {unitData.unitName} - requires {unitData.requiredPolicyPoints} policy points, have {owner.policyPoints}.");
            return false;
        }
        if (unitData.requiresFaith && owner.faith < unitData.requiredFaith)
        {
            Debug.LogWarning($"Cannot produce {unitData.unitName} - requires {unitData.requiredFaith} faith, have {owner.faith}.");
            return false;
        }
        return true;
    }

    private bool MeetsUnitPointRequirements(WorkerUnitData unitData)
    {
        if (unitData == null || owner == null) return false;
        if (unitData.requiresPolicyPoints && owner.policyPoints < unitData.requiredPolicyPoints)
        {
            Debug.LogWarning($"Cannot produce {unitData.unitName} - requires {unitData.requiredPolicyPoints} policy points, have {owner.policyPoints}.");
            return false;
        }
        if (unitData.requiresFaith && owner.faith < unitData.requiredFaith)
        {
            Debug.LogWarning($"Cannot produce {unitData.unitName} - requires {unitData.requiredFaith} faith, have {owner.faith}.");
            return false;
        }
        return true;
    }

    private bool CanPayUnitPurchaseCosts(CombatUnitData unitData)
    {
        if (!MeetsUnitPointRequirements(unitData)) return false;
        if (unitData.canPurchaseWithPolicyPoints && owner.policyPoints < unitData.policyPointPurchaseCost) return false;
        if (unitData.canPurchaseWithFaith && owner.faith < unitData.faithPurchaseCost) return false;
        if (!unitData.canPurchaseWithPolicyPoints && !unitData.canPurchaseWithFaith && owner.gold < unitData.goldCost) return false;
        return true;
    }

    private bool CanPayUnitPurchaseCosts(WorkerUnitData unitData)
    {
        if (!MeetsUnitPointRequirements(unitData)) return false;
        if (unitData.canPurchaseWithPolicyPoints && owner.policyPoints < unitData.policyPointPurchaseCost) return false;
        if (unitData.canPurchaseWithFaith && owner.faith < unitData.faithPurchaseCost) return false;
        if (!unitData.canPurchaseWithPolicyPoints && !unitData.canPurchaseWithFaith && owner.gold < unitData.goldCost) return false;
        return true;
    }

    private void SpendUnitPurchaseCosts(CombatUnitData unitData)
    {
        if (unitData.canPurchaseWithPolicyPoints) owner.policyPoints -= unitData.policyPointPurchaseCost;
        if (unitData.canPurchaseWithFaith) owner.faith -= unitData.faithPurchaseCost;
        if (!unitData.canPurchaseWithPolicyPoints && !unitData.canPurchaseWithFaith) owner.gold -= unitData.goldCost;
    }

    private void SpendUnitPurchaseCosts(WorkerUnitData unitData)
    {
        if (unitData.canPurchaseWithPolicyPoints) owner.policyPoints -= unitData.policyPointPurchaseCost;
        if (unitData.canPurchaseWithFaith) owner.faith -= unitData.faithPurchaseCost;
        if (!unitData.canPurchaseWithPolicyPoints && !unitData.canPurchaseWithFaith) owner.gold -= unitData.goldCost;
    }



    /// <summary>
    /// Queue production by production points.
    /// </summary>
    public bool QueueProduction(ScriptableObject d) {
        // Extract info based on type
        if (d is CombatUnitData u) {
            var resolvedUnit = ResolveCombatUnitForProduction(u);
            if (resolvedUnit == null || !IsCombatUnitAvailableForProduction(resolvedUnit)) return false;

            bool requiresCoast = resolvedUnit.requiresCoastalCity;
            bool requiresHarbor = resolvedUnit.requiresHarbor;
            
            // Check city-specific requirements
            if (requiresCoast && !ControlsCoast()) return false;
            if (requiresHarbor && !HasHarbor()) return false;
            if (!HasRequiredUnitBuildings(resolvedUnit.requiredCityBuildings)) return false;
            if (!MeetsUnitPointRequirements(resolvedUnit)) return false;
            
            if (!CanProduce(null, resolvedUnit.requiredTerrains, resolvedUnit.requiredResourceCosts, resolvedUnit.hasSubstituteResourceCosts)) return false;
            productionQueue.Add(new ProdEntry(resolvedUnit, resolvedUnit.productionCost, resolvedUnit.goldCost,
                                            null, resolvedUnit.requiredTerrains,
                                            requiresCoast, requiresHarbor,
                                            ProdEntry.Type.Unit, resolvedUnit.requiredResourceCosts));
            return true;
        }
        if (d is WorkerUnitData w) {
            bool requiresCoast = w.requiresCoastalCity;
            bool requiresHarbor = w.requiresHarbor;
            
            // Check city-specific requirements
            if (requiresCoast && !ControlsCoast()) return false;
            if (requiresHarbor && !HasHarbor()) return false;
            if (!HasRequiredUnitBuildings(w.requiredCityBuildings)) return false;
            if (!MeetsUnitPointRequirements(w)) return false;
            
            if (!CanProduce(null, w.requiredTerrains, w.requiredResourceCosts, w.hasSubstituteResourceCosts)) return false;
            productionQueue.Add(new ProdEntry(w, w.productionCost, w.goldCost,
                                            null, w.requiredTerrains,
                                            requiresCoast, requiresHarbor,
                                            ProdEntry.Type.Worker, w.requiredResourceCosts));
            return true;
        }
        if (d is BuildingData b) {
            b = ResolveBuildingForProduction(b);
            if (b == null) return false;

            // Harbor buildings can only be built in coastal cities
            if (b.providesHarbor && !ControlsCoast()) {
                Debug.LogWarning($"Cannot build {b.buildingName} - city is not coastal!");
                return false;
            }
            if (!b.AreRequirementsMet(owner)) return false;
            if (!AreBuildingRequirementsMet(b.requiredBuildings)) return false;
            if (!CanFitBuildingInAnyAllowedSlot(b)) return false;
            // Population requirement
            if (b.requiredPopulation > 0 && level < b.requiredPopulation) {
                Debug.LogWarning($"Cannot build {b.buildingName} - requires population level {b.requiredPopulation}, current {level}");
                return false;
            }
            if (!CanProduce(b.requiredResources, b.requiredTerrains, null, false, b.hasSubstituteRequiredResources)) return false;
            if (!b.CanPayBuildCosts(owner)) return false;
            if (!b.ConsumeBuildCosts(owner)) return false;
            productionQueue.Add(new ProdEntry(b, b.productionCost, b.goldCost,
                                            b.requiredResources, b.requiredTerrains,
                                            false, false, // Buildings don't need coast/harbor
                                            ProdEntry.Type.Building));
            return true;
        }
        if (d is EquipmentData eq)
        {
            // Equipment is produced like other items: consumes production points over time
            // Validate equipment-specific production prereqs via EquipmentData
            if (!eq.CanBeProducedBy(owner)) return false;
            productionQueue.Add(new ProdEntry(eq, eq.productionCost, 0, null, null, false, false, ProdEntry.Type.Equipment));
            return true;
        }
        if (d is GameCombat.ProjectileData projectile)
        {
            // Projectiles are produced like equipment: consumes production points over time
            if (!projectile.CanBeProducedBy(owner)) return false;
            productionQueue.Add(new ProdEntry(projectile, projectile.productionCost, projectile.goldCost, 
                                            projectile.requiredResources, null, false, false, 
                                            ProdEntry.Type.Projectile));
            return true;
        }
        if (d is MissileData missileData)
        {
            // Validate tech requirements
            if (missileData.requiredTechs != null)
            {
                foreach (var tech in missileData.requiredTechs)
                    if (tech != null && (owner == null || !owner.researchedTechs.Contains(tech))) return false;
            }
            if (storedMissiles.Count >= maxMissileStorage) return false;
            productionQueue.Add(new ProdEntry(missileData, missileData.productionCost, missileData.goldCost,
                                            null, null, false, false,
                                            ProdEntry.Type.Missile));
            return true;
        }
        if (d is DistrictData district) {
            // For districts, we need to select a tile instead of immediately queueing
            var districtPlacement = FindAnyObjectByType<DistrictPlacementController>();
            if (districtPlacement == null) {
                Debug.LogError("No DistrictPlacementController found in scene!");
                return false;
            }
            
            // Begin district placement mode
            districtPlacement.BeginDistrictPlacement(this, district);
            
            // Close any open UI to allow tile selection
            if (UIManager.Instance != null)
            {
                UIManager.Instance.HideAllPanels();
            }
            
            return true;
        }
        return false;
    }

    /// <summary>
    /// Add a district to the production queue after selecting a tile
    /// </summary>
    public bool AddDistrictToQueue(DistrictData district, int tileIndex)
    {
        if (district == null || !IsValidDistrictTile(tileIndex, district))
            return false;
        
        // Check requirements
        bool requiresCoast = district.requiresCoastal;
        if (requiresCoast && !ControlsCoast()) return false;
        
        if (!CanProduce(null, district.allowedBiomes)) return false;
        
        // Add to queue with the specific tile index
        var entry = new ProdEntry(district, district.productionCost, district.goldCost,
                                  null, district.allowedBiomes,
                                  requiresCoast, false,
                                  ProdEntry.Type.District);
        
        // Add to queue and store the target tile index for later
        productionQueue.Add(entry);
        districtTileTargets[district] = tileIndex;
        
        return true;
    }

    public bool TryGetQueuedDistrictTile(DistrictData district, out int tileIndex)
    {
        if (district != null && districtTileTargets.TryGetValue(district, out tileIndex))
            return true;

        tileIndex = -1;
        return false;
    }

    /// <summary>
    /// Check if a specific tile is valid for a district
    /// </summary>
    public bool IsValidDistrictTile(int tileIndex, DistrictData district)
    {
        if (planetGenerator == null) throw new System.Exception("City references not set!");
        var ts = TileSys;
        if (ts == null) return false;
        
        // Get tile data
        var tileData = ts.GetTileData(tileIndex);
        if (tileData == null) return false;
        
        // Check if tile is owned by this city's civilization
        if (tileData.owner != owner) return false;
        
        // Check if tile is already occupied (layer-aware)
        var occObj = TileOccupancyManager.GetOccupantObjectForTileWithFallback(tileIndex, TileLayer.Surface);
        if (tileData.HasDistrict || tileData.HasImprovement || occObj != null) return false;
            
        // Land / underwater check
        if (district.isUnderwaterDistrict)
        {
            // Underwater districts require a water tile with a valid underwaterBiome
            if (tileData.isLand) return false;
            if (district.allowedUnderwaterBiomes != null && district.allowedUnderwaterBiomes.Length > 0)
            {
                bool validUw = false;
                foreach (var ub in district.allowedUnderwaterBiomes)
                    if (tileData.underwaterBiome == ub) { validUw = true; break; }
                if (!validUw) return false;
            }
        }
        else
        {
            // Standard land districts
            if (!tileData.isLand)
                return false;
        }
            
        // Check if tile is within territory radius
        var cityPos = ts.GetTileCenterFlat(centerTileIndex);
        var tilePos = ts.GetTileCenterFlat(tileIndex);
        float distance = Vector3.Distance(cityPos, tilePos);
        if (distance > TerritoryRadius * 1.0f) // Scale factor based on your map scale
            return false;
            
        // Check biome requirements
        if (district.allowedBiomes != null && district.allowedBiomes.Length > 0)
        {
            bool validBiome = false;
            foreach (var allowedBiome in district.allowedBiomes)
            {
                if ((int)tileData.biome == (int)allowedBiome)
                {
                    validBiome = true;
                    break;
                }
            }
            
            if (!validBiome) return false;
        }
        
        // Check special requirements (river, coastal, mountain)
        if (district.requiresRiver)
        {
            bool hasRiver = false;
            foreach (int neighborIdx in ts.GetNeighbors(tileIndex))
            {
                var neighborData = ts.GetTileData(neighborIdx);
                if (neighborData != null && neighborData.biome == Biome.River)
                {
                    hasRiver = true;
                    break;
                }
            }
            
            if (!hasRiver) return false;
        }
        
        if (district.requiresCoastal)
        {
            bool hasWater = false;
            foreach (int neighborIdx in ts.GetNeighbors(tileIndex))
            {
                var neighborData = ts.GetTileData(neighborIdx);
                if (neighborData != null && 
                   (neighborData.biome == Biome.Ocean || 
                    neighborData.biome == Biome.Seas || 
                    neighborData.biome == Biome.Coast))
                {
                    hasWater = true;
                    break;
                }
            }
            
            if (!hasWater) return false;
        }
        
        if (district.requiresMountainAdjacent)
        {
            bool hasMountain = false;
            foreach (int neighborIdx in ts.GetNeighbors(tileIndex))
            {
                var neighborData = ts.GetTileData(neighborIdx);
                if (neighborData != null && neighborData.elevationTier == ElevationTier.Mountain)
                {
                    hasMountain = true;
                    break;
                }
            }
            
            if (!hasMountain) return false;
        }
        
        return true;
    }

    /// <summary>
    /// Instant purchase (spend gold, bypass production queue).
    /// </summary>
    public bool BuyProduction(ScriptableObject d) {
        int cost = 0;
        ResourceData[] reqRes = null;
        ResourceCost[] reqResourceCosts = null;
        bool hasSubstituteRequiredResources = false;
        bool hasSubstituteResourceCosts = false;
        Biome[] reqTerr = null;
        bool requiresCoast = false;
        bool requiresHarbor = false;
        bool isHarborBuilding = false;
        
        // Get cost and requirements based on type without using dynamic
        if (d is CombatUnitData u) {
            var resolvedUnit = ResolveCombatUnitForProduction(u);
            if (resolvedUnit == null || !IsCombatUnitAvailableForProduction(resolvedUnit)) return false;
            cost = resolvedUnit.goldCost;
            reqResourceCosts = resolvedUnit.requiredResourceCosts;
            hasSubstituteResourceCosts = resolvedUnit.hasSubstituteResourceCosts;
            reqTerr = resolvedUnit.requiredTerrains;
            requiresCoast = resolvedUnit.requiresCoastalCity;
            requiresHarbor = resolvedUnit.requiresHarbor;
            if (!HasRequiredUnitBuildings(resolvedUnit.requiredCityBuildings)) return false;
            if (!CanPayUnitPurchaseCosts(resolvedUnit)) return false;
            d = resolvedUnit;
        }
        else if (d is WorkerUnitData w) {
            cost = w.goldCost;
            reqResourceCosts = w.requiredResourceCosts;
            hasSubstituteResourceCosts = w.hasSubstituteResourceCosts;
            reqTerr = w.requiredTerrains;
            requiresCoast = w.requiresCoastalCity;
            requiresHarbor = w.requiresHarbor;
            if (!HasRequiredUnitBuildings(w.requiredCityBuildings)) return false;
            if (!CanPayUnitPurchaseCosts(w)) return false;
        }
        else if (d is BuildingData b) {
            cost = b.goldCost;
            reqRes = b.requiredResources;
            hasSubstituteRequiredResources = b.hasSubstituteRequiredResources;
            reqTerr = b.requiredTerrains;
            isHarborBuilding = b.providesHarbor;
            // Tech requirements
            if (b.requiredTechs != null && b.requiredTechs.Length > 0) {
                foreach (var tech in b.requiredTechs) {
                    if (tech == null || owner == null || owner.researchedTechs == null || !owner.researchedTechs.Contains(tech)) {
                        Debug.LogWarning($"Cannot buy {b.buildingName} - missing required tech: {tech?.techName ?? "(null)"}");
                        return false;
                    }
                }
            }
            // Culture requirements
            if (b.requiredCultures != null && b.requiredCultures.Length > 0) {
                foreach (var culture in b.requiredCultures) {
                    if (culture == null || owner == null || owner.researchedCultures == null || !owner.researchedCultures.Contains(culture)) {
                        Debug.LogWarning($"Cannot buy {b.buildingName} - missing required culture: {culture?.cultureName ?? "(null)"}");
                        return false;
                    }
                }
            }
            if (!AreBuildingRequirementsMet(b.requiredBuildings)) return false;
            if (!CanFitBuildingInAnyAllowedSlot(b)) return false;
            // Population
            if (b.requiredPopulation > 0 && level < b.requiredPopulation) {
                Debug.LogWarning($"Cannot buy {b.buildingName} - requires population level {b.requiredPopulation}, current {level}");
                return false;
            }
        }
        else if (d is DistrictData district) {
            cost = district.goldCost;
            reqTerr = district.allowedBiomes;
            requiresCoast = district.requiresCoastal;
            
            // Check if a valid tile exists
            int tileIndex = FindValidDistrictTile(district);
            if (tileIndex < 0) {
                Debug.LogWarning($"No valid tile found for {district.districtName}!");
                return false;
            }
        }
        else if (d is EquipmentData e)
        {
            cost = e.productionCost;
            // Equipment may have civ-level requirements
            if (!e.CanBeProducedBy(owner))
            {
                Debug.LogWarning($"Cannot buy {e.equipmentName} - requirements not met");
                return false;
            }
        }
        
        if (!(d is CombatUnitData) && !(d is WorkerUnitData) && owner.gold < cost) return false;
        
        // Check naval requirements
        if (requiresCoast && !ControlsCoast()) return false;
        if (requiresHarbor && !HasHarbor()) return false;
        
        // Special check for harbor buildings
        if (isHarborBuilding && !ControlsCoast()) {
            Debug.LogWarning("Cannot buy harbor - city is not coastal!");
            return false;
        }
        
        // Validate other requirements
        if (!CanProduce(reqRes, reqTerr, reqResourceCosts, hasSubstituteResourceCosts, hasSubstituteRequiredResources)) return false;

        if (d is BuildingData buildingToBuy)
        {
            if (!buildingToBuy.CanPayBuildCosts(owner)) return false;
            if (!buildingToBuy.ConsumeBuildCosts(owner)) return false;
        }
        
        if (d is CombatUnitData unitToBuy)
        {
            SpendUnitPurchaseCosts(unitToBuy);
        }
        else if (d is WorkerUnitData workerToBuy)
        {
            SpendUnitPurchaseCosts(workerToBuy);
        }
        else
        {
            owner.gold -= cost;
        }
        CompleteItem(d);
        return true;
    }
    
    /// <summary>
    /// Purchase a religious unit with faith
    /// </summary>
    public bool PurchaseReligiousUnit(ReligionUnitData unitData)
    {
        // Validate
        if (unitData == null || owner == null)
            return false;
            
        // Check if we have a founded religion
        if (!owner.hasFoundedReligion || owner.foundedReligion == null)
        {
            Debug.LogWarning("Cannot purchase religious unit - no founded religion!");
            return false;
        }
        
        // Check if we have a Holy Site
        if (!HasHolySite())
        {
            Debug.LogWarning("Cannot purchase religious unit - no Holy Site in city!");
            return false;
        }
        
        // Check if we have enough faith
        if (owner.faith < unitData.faithCost)
        {
            Debug.LogWarning($"Not enough faith to purchase {unitData.unitName}! Need {unitData.faithCost}, have {owner.faith}.");
            return false;
        }
        
        // Deduct faith
        owner.faith -= unitData.faithCost;
        
        // Spawn the unit
        if (planetGenerator == null) planetGenerator = ResolvePlanetGenerator();
        var ts = TileSys;
        Vector3 pos = ts != null ? ts.GetTileCenterFlat(centerTileIndex) : transform.position;
        
        var prefab = unitData.GetPrefab(owner);
        if (prefab == null)
        {
            Debug.LogError($"[City] Cannot spawn unit {unitData.unitName}: prefab not found in Addressables. Make sure prefab is marked as Addressable with address matching unitName.");
            return false;
        }
        
        var unitGO = Instantiate(prefab, pos, Quaternion.identity);
        // Keep hierarchy organized: parent spawned world objects under their planet generator.
        if (planetGenerator != null) unitGO.transform.SetParent(planetGenerator.transform, true);
        // Register unit with HexMapChunkManager so it moves with wrap teleport
        try
        {
            var mgr = FindObjectsByType<HexMapChunkManager>().FirstOrDefault(m => m.PlanetGenerator == planetGenerator);
            if (mgr != null) mgr.RegisterObjectForWrapAtTile(centerTileIndex, unitGO);
        }
        catch { }
        var unit = unitGO.GetComponent<CombatUnit>();
        if (unit == null)
        {
            Debug.LogError($"[City] Spawned prefab for {unitData.unitName} is missing CombatUnit component.");
            Destroy(unitGO);
            return false;
        }
        unit.Initialize(unitData, owner);
        unit.planetIndex = planetIndex;
        
        // Add to owner's units and fire training event
        owner.RegisterTrainedCombatUnit(unit);
        
        // Set tile index and register occupancy
        if (unit.currentTileIndex < 0)
        {
            unit.currentTileIndex = centerTileIndex;
        }
        var unitTileData = TileSystem.GetForPlanet(planetIndex)?.GetTileData(unit.currentTileIndex) ?? TileSystem.Instance?.GetTileData(unit.currentTileIndex);
        unit.currentLayer = UnitLayerRules.GetSpawnTileLayerForUnit(unit, unitTileData);
        // Register in the global unit registry, then register with occupancy manager so tile-based selection works
        try { unit.RegisterToRegistry(); } catch { }
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
        {
            occ.SetOccupant(unit.currentTileIndex, unitGO, unit.currentLayer);
        }

        // Fog of War: immediately refresh vision for this civ after spawning a unit.
        if (UnitVisionManager.Instance != null && owner != null)
        {
            UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(owner));
        }

    return true;
    }

    /// <summary>
    /// Ensure empire resources and city‐radius biomes satisfy requirements.
    /// </summary>
    private bool CanProduce(ResourceData[] reqRes, Biome[] reqTerrains, ResourceCost[] reqResourceCosts = null, bool hasSubstituteResourceCosts = false, bool hasSubstituteRequiredResources = false) {
        // Resources
        if (!ResourceCost.HasRequiredResources(owner, reqRes, hasSubstituteRequiredResources)) return false;
        
        // Resource amount requirements
        if (!ResourceCost.CanAfford(owner, reqResourceCosts, hasSubstituteResourceCosts)) return false;

        // Terrains
        if (reqTerrains != null && reqTerrains.Length > 0) {
            if (planetGenerator == null) planetGenerator = ResolvePlanetGenerator();
            var ts = TileSys;
            if (ts == null) return false;
            
            // gather city‐radius tiles (1 tile for simplicity)
            bool found = false;
            foreach (int n in ts.GetNeighbors(centerTileIndex)) {
                if (planetGenerator == null) planetGenerator = ResolvePlanetGenerator();
                var tdOpt = ts.GetTileData(n);
                
                if (tdOpt == null) continue;
                if (System.Array.IndexOf(reqTerrains, tdOpt.biome) >= 0) {
                    found = true; break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>
    /// Completes the item and adds it to the appropriate collection or instantiates it.
    /// </summary>
    private void CompleteItem(ScriptableObject d) {
        if (planetGenerator == null) planetGenerator = ResolvePlanetGenerator();
        var ts = TileSys;
        Vector3 pos = ts != null ? ts.GetTileCenterFlat(centerTileIndex) : transform.position;

        switch (d) {
            case CombatUnitData u:
                var resolvedUnit = ResolveCombatUnitForProduction(u);
                if (resolvedUnit == null)
                    break;
                var unitPrefab = resolvedUnit.GetPrefab(owner);
                if (unitPrefab == null)
                {
                    Debug.LogError($"[City] Cannot spawn unit {resolvedUnit.unitName}: prefab not found in Addressables. Make sure prefab is marked as Addressable with address matching unitName.");
                    break;
                }
                var unitGO = Instantiate(unitPrefab, pos, Quaternion.identity);
                if (planetGenerator != null) unitGO.transform.SetParent(planetGenerator.transform, true);
                try
                {
                    var mgr = FindObjectsByType<HexMapChunkManager>().FirstOrDefault(m => m.PlanetGenerator == planetGenerator);
                    if (mgr != null) mgr.RegisterObjectForWrapAtTile(centerTileIndex, unitGO);
                }
                catch { }
                var unit = unitGO.GetComponent<CombatUnit>();
                if (unit == null)
                {
                    Debug.LogError($"[City] Spawned prefab for {resolvedUnit.unitName} is missing CombatUnit component.");
                    Destroy(unitGO);
                    break;
                }
                unit.Initialize(resolvedUnit, owner);
                unit.planetIndex = planetIndex;
                // Set tile index and register occupancy
                if (unit.currentTileIndex < 0)
                {
                    unit.currentTileIndex = centerTileIndex;
                }
                var unitTileData = TileSystem.GetForPlanet(planetIndex)?.GetTileData(unit.currentTileIndex) ?? TileSystem.Instance?.GetTileData(unit.currentTileIndex);
                unit.currentLayer = UnitLayerRules.GetSpawnTileLayerForUnit(unit, unitTileData);
                var combatProgression = owner != null ? owner.GetNewCombatUnitProgressionBonuses(unit, this) : default;
                unit.ApplyStartingProgression(combatProgression.experienceAdd, combatProgression.levelsAdd);
                owner.RegisterTrainedCombatUnit(unit);
                producedUnits.Add(resolvedUnit);
                try { unit.RegisterToRegistry(); } catch { }
                var prodOcc = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
                if (prodOcc != null)
                {
                    prodOcc.SetOccupant(unit.currentTileIndex, unitGO, unit.currentLayer);
                }

                // Fog of War: immediately refresh vision for this civ after producing a unit.
                if (UnitVisionManager.Instance != null && owner != null)
                {
                    UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(owner));
                }
                
                // Award governor experience for unit production
                if (governor != null)
                {
                    governor.RecordStat(TraitTrigger.UnitsProduced);
                }
                break;

            case WorkerUnitData w:
                var workerPrefab = w.GetPrefab(owner);
                if (workerPrefab == null)
                {
                    Debug.LogError($"[City] Cannot spawn worker {w.unitName}: prefab not found.");
                    break;
                }
                var wGO = Instantiate(workerPrefab, pos, Quaternion.identity);
                if (planetGenerator != null) wGO.transform.SetParent(planetGenerator.transform, true);
                var worker = wGO.GetComponent<WorkerUnit>();
                worker.Initialize(w, owner, centerTileIndex);
                worker.planetIndex = planetIndex;
                var workerTileData = TileSystem.GetForPlanet(planetIndex)?.GetTileData(centerTileIndex) ?? TileSystem.Instance?.GetTileData(centerTileIndex);
                worker.currentLayer = UnitLayerRules.GetSpawnTileLayerForUnit(worker, workerTileData);
                var workerProgression = owner != null ? owner.GetNewWorkerUnitProgressionBonuses(worker, this) : default;
                worker.ApplyStartingProgression(workerProgression.experienceAdd, workerProgression.levelsAdd);
                owner.workerUnits.Add(worker);
                try { worker.RegisterToRegistry(); } catch { }

                // Fog of War: immediately refresh vision for this civ after producing a worker.
                if (UnitVisionManager.Instance != null && owner != null)
                {
                    UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(owner));
                }
                
                // Award governor experience for unit production
                if (governor != null)
                {
                    governor.RecordStat(TraitTrigger.UnitsProduced);
                }
                break;

            case BuildingData b:
                AddBuilding(b);
                OnBuildingCompleted?.Invoke(this, b);
                
                // Award governor experience for building construction
                if (governor != null)
                {
                    governor.RecordStat(TraitTrigger.BuildingsConstructed);
                }
                break;
            case EquipmentData eq:
                // Add produced equipment to city's producedEquipment list and give to owner inventory
                if (eq != null)
                {
                    producedEquipment.Add(eq);
                    if (owner != null)
                    {
                        owner.AddEquipment(eq);
                    }
                }
                break;
            
            case GameCombat.ProjectileData projectile:
                // Add produced projectiles to civilization's projectile inventory
                if (projectile != null && owner != null)
                {
                    owner.AddProjectile(projectile, 1); // Produce 1 unit of projectiles per completion
                }
                break;

            case MissileData missile:
                // Add finished missile to this city's stored missile inventory
                if (missile != null)
                {
                    if (storedMissiles.Count < maxMissileStorage)
                        storedMissiles.Add(missile);
                    else
                        Debug.LogWarning($"[City] {cityName}: missile storage full ({maxMissileStorage}). Missile '{missile.missileName}' was lost.");
                }
                break;
                
            case DistrictData district:
                // Use the stored target tile if available
                int targetTileIndex = centerTileIndex;
                if (districtTileTargets.ContainsKey(district))
                {
                    targetTileIndex = districtTileTargets[district];
                    // Remove from tracking dictionary
                    districtTileTargets.Remove(district);
                }
                
                AddDistrict(district, targetTileIndex);
                
                // Award governor experience for district construction (counts as building)
                if (governor != null)
                {
                    governor.RecordStat(TraitTrigger.BuildingsConstructed);
                }
                break;
        }
    }

    void AddBuilding(BuildingData b)
    {
        b = ResolveBuildingForProduction(b);
        if (b == null)
            return;
        
        // If this building upgrades an old one, destroy that instance
        if (b.replacesBuilding != null)
        {
            var oldTuple = builtBuildings.Find(tuple => tuple.data == b.replacesBuilding || tuple.data?.replacesBuilding == b.replacesBuilding);
            
            if (oldTuple.instance != null)
            {
Destroy(oldTuple.instance);
            }
            
            // Remove from list
            builtBuildings.RemoveAll(tuple => tuple.data == b.replacesBuilding || tuple.data?.replacesBuilding == b.replacesBuilding);
            OnBuildingRemoved?.Invoke(this, b.replacesBuilding, BuildingRemovalReason.Replaced);
        }

        if (!CanFitBuildingInAnyAllowedSlot(b))
        {
            Debug.LogWarning($"Cannot add {b.buildingName} to {cityName} - no compatible building slot is available.");
            return;
        }
        
        // Instantiate the new building
        GameObject buildingInstance = null;
        var buildingPrefab = b.GetBuildingPrefab(owner);
        if (buildingPrefab != null) 
        {
            buildingInstance = Instantiate(buildingPrefab, transform.position, Quaternion.identity);
            buildingInstance.transform.SetParent(transform); // Parent to city for organization
        }
        else
        {
            Debug.LogWarning($"Building {b.buildingName} has no prefab assigned!");
        }
        
        // Track the building and its instance
        builtBuildings.Add((b, buildingInstance));
        
        // Apply the building effects
        ApplyBuildingEffects(b);
        
        // NEW: Handle equipment production
        if (b.equipmentProduction != null && b.equipmentProduction.Length > 0 && owner != null)
        {
            foreach (var production in b.equipmentProduction)
            {
                if (production.equipment != null && production.quantity > 0)
                {
                    // Optionally produce immediately or enqueue
                    if (production.produceImmediately)
                    {
                        bool ok = owner.ProduceEquipment(production.equipment, production.quantity);
                        if (!ok)
                            Debug.LogWarning($"Building {b.buildingName} failed to immediately grant {production.quantity}x {production.equipment.equipmentName} to {owner.civData.civName}");
                    }
                    else
                    {
                        int prodCost = production.productionCostOverride > 0 ? production.productionCostOverride : production.equipment.productionCost;
                        int goldCost = production.goldCostOverride > 0 ? production.goldCostOverride : 0;
                        for (int i = 0; i < production.quantity; i++)
                        {
                            // Validate production prerequisites using EquipmentData
                            if (!production.equipment.CanBeProducedBy(owner))
                            {
                                Debug.LogWarning($"Building {b.buildingName} could not enqueue {production.equipment.equipmentName} production in {cityName} - requirements not met");
                                break;
                            }
                            productionQueue.Add(new ProdEntry(production.equipment, prodCost, goldCost, null, null, false, false, ProdEntry.Type.Equipment));
                        }
                    }
                }
            }
        }

        // Handle projectile production the same way as equipment production.
        if (b.projectileProduction != null && b.projectileProduction.Length > 0 && owner != null)
        {
            foreach (var production in b.projectileProduction)
            {
                if (production.projectile != null && production.quantity > 0)
                {
                    if (production.produceImmediately)
                    {
                        bool ok = owner.ProduceProjectile(production.projectile, production.quantity);
                        if (!ok)
                            Debug.LogWarning($"Building {b.buildingName} failed to immediately grant {production.quantity}x {production.projectile.projectileName} to {owner.civData.civName}");
                    }
                    else
                    {
                        int prodCost = production.productionCostOverride > 0 ? production.productionCostOverride : production.projectile.productionCost;
                        int goldCost = production.goldCostOverride > 0 ? production.goldCostOverride : 0;
                        for (int i = 0; i < production.quantity; i++)
                        {
                            if (!production.projectile.CanBeProducedBy(owner))
                            {
                                Debug.LogWarning($"Building {b.buildingName} could not enqueue {production.projectile.projectileName} production in {cityName} - requirements not met");
                                break;
                            }
                            productionQueue.Add(new ProdEntry(production.projectile, prodCost, goldCost, production.projectile.requiredResources, null, false, false, ProdEntry.Type.Projectile));
                        }
                    }
                }
            }
        }
    }

    public bool DismantleBuilding(BuildingData building)
    {
        if (building == null || owner == null) return false;

        var resolved = ResolveBuildingForProduction(building);
        int idx = builtBuildings.FindIndex(tuple => tuple.data == resolved || tuple.data == building);
        if (idx < 0) return false;

        var built = builtBuildings[idx];
        if (built.data == null || !built.data.canBeDismantled) return false;

        if (built.instance != null)
            Destroy(built.instance);

        builtBuildings.RemoveAt(idx);
        built.data.RefundDismantleCosts(owner);
        try { LimitManager.Instance?.RemoveBuilding(owner, built.data); } catch { }
        OnBuildingRemoved?.Invoke(this, built.data, BuildingRemovalReason.Dismantled);
        return true;
    }
    
    /// <summary>
    /// Adds a district to the city on a specific tile
    /// </summary>
    void AddDistrict(DistrictData district, int tileIndex)
    {
        if (district == null || planetGenerator == null)
            return;
        var ts = TileSys;
        if (ts == null) return;
            
        // Get position for the district
        Vector3 pos = ts.GetTileCenterFlat(tileIndex);
        
        // Instantiate the district
        GameObject districtInstance = null;
        if (district.prefab != null)
        {
            districtInstance = Instantiate(district.prefab, pos, Quaternion.identity);
            if (planetGenerator != null) districtInstance.transform.SetParent(planetGenerator.transform, true);
        }
        
        // Update the tile data to include this district
        var tileData2 = ts.GetTileData(tileIndex);
        if (tileData2 != null)
        {
            // Mark the district on the tile
            tileData2.district = district;
            
            // If it's a Holy Site, mark via TileSystem and seed pressure
            if (district.isHolySite)
            {
                ts.SetHolySite(tileIndex, true, district);
                if (owner.hasFoundedReligion && owner.foundedReligion != null)
                {
                    ts.AddReligionPressure(tileIndex, owner.foundedReligion, 100f);
                }
            }
            // Update the tile data (district placement)
            ts.SetTileData(tileIndex, tileData2);
        }
        
        // Add to city's districts
        builtDistricts.Add((district, districtInstance, tileIndex));
}

    /// <summary>
    /// Apply the effects of a newly constructed building
    /// </summary>
    private void ApplyBuildingEffects(BuildingData building)
    {
        RefreshCityDefenseAndHappinessBonuses();
    }

    void ProcessGrowth()
    {
        foodStorage += GetFoodPerTurn(); // Assuming GetFoodPerTurn is implemented correctly
        if (foodStorage >= foodGrowthRequirement)
        {
            int oldLevel = level;
            level = Mathf.Min(level + 1, 40);
            foodStorage -= foodGrowthRequirement;
            foodGrowthRequirement = level * 10;
            
            // Award governor experience for population growth
            if (level > oldLevel && governor != null)
            {
                governor.RecordStat(TraitTrigger.PopulationGrowth);
}
        }
    }

    public List<int> GetWorkableTileIndexes()
    {
        var tiles = GetTerritoryTiles(baseRadius);
        if (!tiles.Contains(centerTileIndex))
            tiles.Insert(0, centerTileIndex);
        return tiles;
    }

    public bool IsTileWorkableByThisCity(int tileIndex)
    {
        if (tileIndex < 0) return false;
        var ts = TileSys;
        if (ts == null) return false;
        var td = ts.GetTileData(tileIndex);
        if (td == null || td.owner != owner) return false;
        return GetWorkableTileIndexes().Contains(tileIndex);
    }

    public int GetAssignedCount(CityCitizenJobType jobType)
    {
        int count = 0;
        foreach (var assignment in citizenAssignments)
            if (assignment != null && assignment.jobType == jobType) count++;
        return count;
    }

    public int GetAvailablePopulationForNewAssignment() => Population - citizenAssignments.Count;
    public int GetUnemployedCount() => Mathf.Max(0, Population - citizenAssignments.Count);

    public CityCitizenAssignment GetTileAssignment(int tileIndex)
    {
        foreach (var assignment in citizenAssignments)
            if (assignment != null && assignment.tileIndex == tileIndex) return assignment;
        return null;
    }

    public bool IsTileLocked(int tileIndex)
    {
        var assignment = GetTileAssignment(tileIndex);
        return assignment != null && assignment.locked;
    }

    public bool AssignTileWorker(int tileIndex, out string reason)
    {
        reason = "";
        if (!IsTileWorkableByThisCity(tileIndex)) { reason = "Tile is not workable by this city."; return false; }
        if (GetTileAssignment(tileIndex) != null) { reason = "Tile already has an assignment."; return false; }
        if (GetAvailablePopulationForNewAssignment() <= 0) { reason = "No available population."; return false; }
        citizenAssignments.Add(new CityCitizenAssignment { jobType = CityCitizenJobType.TileWorker, tileIndex = tileIndex });
        manualCitizenAssignment = true;
        RecalculateCitizenAssignmentCaches();
        return true;
    }

    public bool AssignRuralSpecialist(int tileIndex, string slotId, out string reason)
    {
        reason = "";
        if (!IsTileWorkableByThisCity(tileIndex)) { reason = "Tile is not workable by this city."; return false; }
        if (GetAvailablePopulationForNewAssignment() <= 0) { reason = "No available population."; return false; }
        var ts = TileSys;
        var td = ts != null ? ts.GetTileData(tileIndex) : null;
        var instance = td?.improvementInstanceObject != null ? td.improvementInstanceObject.GetComponent<ImprovementInstance>() : null;
        if (instance == null) { reason = "This tile has no improvement specialist slots."; return false; }
        bool foundSlot = instance.GetActiveRuralSpecialistSlots().Any(slot => slot != null && slot.slotId == slotId);
        if (!foundSlot) { reason = "Rural specialist slot not found."; return false; }
        foreach (var assignment in citizenAssignments)
            if (assignment != null && assignment.jobType == CityCitizenJobType.RuralSpecialist && assignment.tileIndex == tileIndex && assignment.specialistSlotId == slotId)
            { reason = "That rural specialist slot is already assigned."; return false; }
        citizenAssignments.Add(new CityCitizenAssignment { jobType = CityCitizenJobType.RuralSpecialist, tileIndex = tileIndex, specialistSlotId = slotId, improvement = td.improvement });
        manualCitizenAssignment = true;
        RecalculateCitizenAssignmentCaches();
        return true;
    }

    public bool AssignUrbanSpecialist(BuildingData building, string slotId, out string reason)
    {
        reason = "";
        if (building == null) { reason = "No building selected."; return false; }
        if (GetAvailablePopulationForNewAssignment() <= 0) { reason = "No available population."; return false; }
        bool cityHasBuilding = builtBuildings.Any(entry => entry.data == building);
        if (!cityHasBuilding) { reason = "City does not have this building."; return false; }
        bool foundSlot = building.urbanSpecialistSlots != null && building.urbanSpecialistSlots.Any(slot => slot != null && slot.slotId == slotId);
        if (!foundSlot) { reason = "Urban specialist slot not found."; return false; }
        foreach (var assignment in citizenAssignments)
            if (assignment != null && assignment.jobType == CityCitizenJobType.UrbanSpecialist && assignment.building == building && assignment.specialistSlotId == slotId)
            { reason = "That urban specialist slot is already assigned."; return false; }
        citizenAssignments.Add(new CityCitizenAssignment { jobType = CityCitizenJobType.UrbanSpecialist, building = building, specialistSlotId = slotId });
        manualCitizenAssignment = true;
        RecalculateCitizenAssignmentCaches();
        return true;
    }

    public bool AssignUrbanSpecialist(DistrictData district, string slotId, out string reason)
    {
        reason = "";
        if (district == null) { reason = "No district selected."; return false; }
        if (GetAvailablePopulationForNewAssignment() <= 0) { reason = "No available population."; return false; }
        bool cityHasDistrict = builtDistricts.Any(entry => entry.data == district);
        if (!cityHasDistrict) { reason = "City does not have this district."; return false; }
        bool foundSlot = district.urbanSpecialistSlots != null && district.urbanSpecialistSlots.Any(slot => slot != null && slot.slotId == slotId);
        if (!foundSlot) { reason = "Urban specialist slot not found."; return false; }
        foreach (var assignment in citizenAssignments)
            if (assignment != null && assignment.jobType == CityCitizenJobType.UrbanSpecialist && assignment.district == district && assignment.specialistSlotId == slotId)
            { reason = "That urban specialist slot is already assigned."; return false; }
        citizenAssignments.Add(new CityCitizenAssignment { jobType = CityCitizenJobType.UrbanSpecialist, district = district, specialistSlotId = slotId });
        manualCitizenAssignment = true;
        RecalculateCitizenAssignmentCaches();
        return true;
    }

    public bool RemoveUrbanSpecialist(BuildingData building, string slotId)
    {
        return RemoveAssignment(citizenAssignments.FirstOrDefault(a => a != null && a.jobType == CityCitizenJobType.UrbanSpecialist && a.building == building && a.specialistSlotId == slotId));
    }

    public bool RemoveUrbanSpecialist(DistrictData district, string slotId)
    {
        return RemoveAssignment(citizenAssignments.FirstOrDefault(a => a != null && a.jobType == CityCitizenJobType.UrbanSpecialist && a.district == district && a.specialistSlotId == slotId));
    }

    public bool RemoveAssignment(CityCitizenAssignment assignment)
    {
        if (assignment == null) return false;
        bool removed = citizenAssignments.Remove(assignment);
        if (removed) RecalculateCitizenAssignmentCaches();
        return removed;
    }

    public bool RemoveAssignmentFromTile(int tileIndex) => RemoveAssignment(GetTileAssignment(tileIndex));

    public void SetTileAssignmentLocked(int tileIndex, bool locked)
    {
        var assignment = GetTileAssignment(tileIndex);
        if (assignment != null) assignment.locked = locked;
    }

    public void RecalculateCitizenAssignmentCaches()
    {
        cachedUnemployedCitizens = GetUnemployedCount();
        cachedBanditRiskFromUnemployment = cachedUnemployedCitizens * banditRiskPerUnemployedCitizen;
    }

    public int GetUnemploymentOrderPenaltyPerTurn() => cachedUnemployedCitizens * GetEffectiveOrderPenaltyPerUnemployedCitizen();

    public int GetEffectiveOrderPenaltyPerUnemployedCitizen()
    {
        int penalty = orderPenaltyPerUnemployedCitizen;
        return Mathf.Max(0, penalty);
    }

    public int TerritoryRadius => baseRadius
        + (level >= 20 ? 1 : 0) + (level >= 40 ? 1 : 0) + GetAttachedSettlementTerritoryRadiusBonus();

    public void AutoAssignCitizens()
    {
        citizenAssignments.RemoveAll(a => a == null || !a.locked);
        RecalculateCitizenAssignmentCaches();

        foreach (int tileIndex in GetWorkableTileIndexes()
            .OrderByDescending(GetBasicTileAssignmentScore))
        {
            if (GetAvailablePopulationForNewAssignment() <= 0) break;
            string reason;
            AssignTileWorker(tileIndex, out reason);
        }
    }

    private int GetBasicTileAssignmentScore(int tileIndex)
    {
        var ts = TileSys;
        var td = ts != null ? ts.GetTileData(tileIndex) : null;
        if (td == null) return 0;
        int score = td.food + td.production + td.gold;
        if (td.improvement != null)
            score += td.improvement.foodPerTurn + td.improvement.productionPerTurn + td.improvement.goldPerTurn;
        return score;
    }

    private TileYield GetYieldFromCitizenAssignments()
    {
        TileYield result = new TileYield();
        foreach (var assignment in citizenAssignments)
        {
            if (assignment == null) continue;
            if (assignment.jobType == CityCitizenJobType.TileWorker) AddWorkedTileYield(ref result, assignment.tileIndex);
            else if (assignment.jobType == CityCitizenJobType.RuralSpecialist) AddRuralSpecialistYield(ref result, assignment);
            else if (assignment.jobType == CityCitizenJobType.UrbanSpecialist) AddUrbanSpecialistYield(ref result, assignment);
        }
        return result;
    }

    private void AddWorkedTileYield(ref TileYield result, int tileIndex)
    {
        var ts = TileSys;
        var td = ts != null ? ts.GetTileData(tileIndex) : null;
        if (td == null) return;
        result.Food += td.food;
        result.Production += td.production;
        result.Gold += td.gold;
        result.Science += td.science;
        result.Culture += td.culture;
        result.Faith += td.faithYield;
        result.Policy += td.policyPointYield;
        if (td.improvement != null)
        {
            result.Food += td.improvement.foodPerTurn;
            result.Production += td.improvement.productionPerTurn;
            result.Gold += td.improvement.goldPerTurn;
            result.Science += td.improvement.sciencePerTurn;
            result.Culture += td.improvement.culturePerTurn;
            result.Faith += td.improvement.faithPerTurn;
            result.Policy += td.improvement.policyPointsPerTurn;
        }
    }

    private void AddRuralSpecialistYield(ref TileYield result, CityCitizenAssignment assignment)
    {
        var slot = FindRuralSpecialistSlot(assignment.tileIndex, assignment.specialistSlotId);
        AddSpecialistSlotYield(ref result, slot);
    }

    private void AddUrbanSpecialistYield(ref TileYield result, CityCitizenAssignment assignment)
    {
        SpecialistSlotDefinition slot = assignment.building != null
            ? FindUrbanSpecialistSlot(assignment.building, assignment.specialistSlotId)
            : FindUrbanSpecialistSlot(assignment.district, assignment.specialistSlotId);
        AddSpecialistSlotYield(ref result, slot);
    }

    private void AddSpecialistSlotYield(ref TileYield result, SpecialistSlotDefinition slot)
    {
        if (slot == null) return;
        result.Food += slot.food;
        result.Production += slot.production;
        result.Gold += slot.gold;
        result.Science += slot.science;
        result.Culture += slot.culture;
        result.Faith += slot.faith;
        result.Policy += slot.policyPoints;
    }

    private SpecialistSlotDefinition FindRuralSpecialistSlot(int tileIndex, string slotId)
    {
        var ts = TileSys;
        var td = ts != null ? ts.GetTileData(tileIndex) : null;
        var instance = td?.improvementInstanceObject != null ? td.improvementInstanceObject.GetComponent<ImprovementInstance>() : null;
        return instance == null ? null : instance.GetActiveRuralSpecialistSlots().FirstOrDefault(slot => slot != null && slot.slotId == slotId);
    }

    private SpecialistSlotDefinition FindUrbanSpecialistSlot(BuildingData building, string slotId)
    {
        if (building == null || building.urbanSpecialistSlots == null) return null;
        return building.urbanSpecialistSlots.FirstOrDefault(slot => slot != null && slot.slotId == slotId);
    }

    private SpecialistSlotDefinition FindUrbanSpecialistSlot(DistrictData district, string slotId)
    {
        if (district == null || district.urbanSpecialistSlots == null) return null;
        return district.urbanSpecialistSlots.FirstOrDefault(slot => slot != null && slot.slotId == slotId);
    }

    // --- Yield Calculation ---
    // NOTE: These need proper implementation based on your game logic
    // Currently referencing placeholder SumYield/SumBuilt methods

    public int GetFoodPerTurn()
    {
        int baseFood = SumYield(t => t.food) + SumBuiltWithBonuses(BuildingYieldType.Food);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseFood += bonuses.food;
        }
        return ApplyCityScopedYieldBonuses(baseFood, BuildingYieldType.Food);
    }

    enum BuildingYieldType { Food, Production, Gold, Science, Culture, Faith, PolicyPoints }
    struct CityYieldAgg
    {
        public int add;
        public float pct;
    }

    struct UnitCityAuraAgg
    {
        public int foodAdd, productionAdd, goldAdd, scienceAdd, cultureAdd, faithAdd, policyPointsAdd;
        public float foodPct, productionPct, goldPct, sciencePct, culturePct, faithPct, policyPointsPct;
        public int orderAdd, happinessAdd, defenseAdd;
        public float orderPct, happinessPct, defensePct;
    }

    struct CityStatAgg
    {
        public int defenseAdd;
        public float defensePct;
        public int happinessAdd;
        public float happinessPct;
        public int orderAdd;
        public float orderPct;
    }

    private void EnsureBuildingUpkeepState()
    {
        int count = builtBuildings != null ? builtBuildings.Count : 0;

        while (buildingUpkeepSatisfied.Count < count)
        {
            buildingUpkeepSatisfied.Add(true);
            buildingUpkeepFailureBehavior.Add(ResourceUpkeepFailureBehavior.Deactivate);
            buildingUpkeepFailureMultiplier.Add(1f);
        }

        if (buildingUpkeepSatisfied.Count > count)
        {
            buildingUpkeepSatisfied.RemoveRange(count, buildingUpkeepSatisfied.Count - count);
            buildingUpkeepFailureBehavior.RemoveRange(count, buildingUpkeepFailureBehavior.Count - count);
            buildingUpkeepFailureMultiplier.RemoveRange(count, buildingUpkeepFailureMultiplier.Count - count);
        }
    }

    public void ResetBuildingResourceUpkeepState()
    {
        EnsureBuildingUpkeepState();
        for (int i = 0; i < buildingUpkeepSatisfied.Count; i++)
        {
            buildingUpkeepSatisfied[i] = true;
            buildingUpkeepFailureBehavior[i] = ResourceUpkeepFailureBehavior.Deactivate;
            buildingUpkeepFailureMultiplier[i] = 1f;
        }
    }

    public void SetBuildingResourceUpkeepState(int buildingIndex, bool satisfied, ResourceUpkeepFailureBehavior failureBehavior, float debuffMultiplier)
    {
        EnsureBuildingUpkeepState();
        if (buildingIndex < 0 || buildingIndex >= buildingUpkeepSatisfied.Count)
            return;

        buildingUpkeepSatisfied[buildingIndex] = satisfied;
        buildingUpkeepFailureBehavior[buildingIndex] = failureBehavior;
        buildingUpkeepFailureMultiplier[buildingIndex] = Mathf.Clamp01(debuffMultiplier);
    }

    private bool IsBuildingDeactivated(int buildingIndex)
    {
        EnsureBuildingUpkeepState();
        if (buildingIndex < 0 || buildingIndex >= buildingUpkeepSatisfied.Count)
            return false;

        return !buildingUpkeepSatisfied[buildingIndex] && buildingUpkeepFailureBehavior[buildingIndex] == ResourceUpkeepFailureBehavior.Deactivate;
    }

    private float GetBuildingOperationalMultiplier(int buildingIndex)
    {
        EnsureBuildingUpkeepState();
        if (buildingIndex < 0 || buildingIndex >= buildingUpkeepSatisfied.Count)
            return 1f;

        if (buildingUpkeepSatisfied[buildingIndex])
            return 1f;

        if (buildingUpkeepFailureBehavior[buildingIndex] == ResourceUpkeepFailureBehavior.Deactivate)
            return 0f;

        return Mathf.Clamp01(buildingUpkeepFailureMultiplier[buildingIndex]);
    }

    public IEnumerable<(BuildingData data, GameObject instance, float upkeepMultiplier)> EnumerateOperationalBuildings()
    {
        EnsureBuildingUpkeepState();
        if (builtBuildings == null)
            yield break;

        for (int i = 0; i < builtBuildings.Count; i++)
        {
            if (IsBuildingDeactivated(i))
                continue;

            var (data, instance) = builtBuildings[i];
            if (data == null)
                continue;

            yield return (data, instance, GetBuildingOperationalMultiplier(i));
        }
    }

    public bool HasOperationalBuilding(System.Predicate<BuildingData> predicate)
    {
        if (predicate == null)
            return false;

        foreach (var (data, _, _) in EnumerateOperationalBuildings())
        {
            if (predicate(data))
                return true;
        }

        return false;
    }

    private bool AreBuildingRequirementsMet(CityBuildingRequirement[] requirements)
    {
        if (requirements == null || requirements.Length == 0)
            return true;

        foreach (var requirement in requirements)
        {
            if (requirement == null)
                continue;

            if (!IsCityBuildingRequirementMet(requirement))
                return false;
        }

        return true;
    }

    private bool IsCityBuildingRequirementMet(CityBuildingRequirement requirement)
    {
        if (requirement == null || owner == null)
            return false;

        bool MatchesRequiredBuildingData(BuildingData building)
        {
            return building != null &&
                ((requirement.building != null && (building == requirement.building || building.replacesBuilding == requirement.building)) ||
                (requirement.useBuildingCategoryFilter && MatchesBuildingCategory(building, requirement.buildingCategory)));
        }

        bool MatchesRequiredBuilding(City city)
        {
            return city != null && city.HasOperationalBuilding(MatchesRequiredBuildingData);
        }

        switch (requirement.scope)
        {
            case CityBuildingRequirementScope.SameCity:
                return MatchesRequiredBuilding(this);
            case CityBuildingRequirementScope.Capital:
                return MatchesRequiredBuilding(owner.CapitalCity);
            case CityBuildingRequirementScope.AnyOtherCity:
                return owner.cities != null && owner.cities.Any(city => city != null && city != this && MatchesRequiredBuilding(city));
            case CityBuildingRequirementScope.AnyCity:
                return owner.cities != null && owner.cities.Any(MatchesRequiredBuilding);
            case CityBuildingRequirementScope.EveryOtherCity:
                if (owner.cities == null) return false;
                var otherCities = owner.cities.Where(city => city != null && city != this).ToList();
                return otherCities.Count > 0 && otherCities.All(MatchesRequiredBuilding);
            default:
                return false;
        }
    }

    private bool HasCityResource(ResourceData resource)
    {
        if (resource == null)
            return true;

        var ts = TileSys;
        if (ts == null || !ts.IsReady())
            return false;

        int tileCount = ts.TileCount;
        for (int i = 0; i < tileCount; i++)
        {
            var tileData = ts.GetTileData(i);
            if (tileData != null && tileData.controllingCity == this && tileData.resource == resource)
                return true;
        }

        return false;
    }

    private static bool MatchesRequirement(BoolRequirement requirement, bool value)
    {
        return requirement switch
        {
            BoolRequirement.MustBeTrue => value,
            BoolRequirement.MustBeFalse => !value,
            _ => true,
        };
    }

    private bool MatchesTileYieldBonus(HexTileData tile, TileYieldBonus bonus)
    {
        if (tile == null || bonus == null) return false;
        if (bonus.useBiomeFilter && tile.biome != bonus.biome) return false;
        if (!MatchesRequirement(bonus.hillRequirement, tile.isHill)) return false;
        if (!MatchesRequirement(bonus.mountainRequirement, tile.isMountain)) return false;
        if (!MatchesRequirement(bonus.landRequirement, tile.isLand)) return false;
        if (!MatchesRequirement(bonus.waterRequirement, tile.IsWaterTile)) return false;
        if (!MatchesRequirement(bonus.improvementRequirement, tile.HasImprovement)) return false;
        if (!MatchesRequirement(bonus.districtRequirement, tile.HasDistrict)) return false;
        if (bonus.useImprovementFilter)
        {
            if (tile.improvement == null) return false;
            if (tile.improvement != bonus.improvement) return false;
        }
        if (bonus.useDistrictFilter)
        {
            if (tile.district == null) return false;
            if (tile.district != bonus.district) return false;
        }
        if (bonus.useResourceFilter)
        {
            if (tile.resource == null) return false;
            if (tile.resource != bonus.resource) return false;
        }
        if (bonus.useSeasonFilter)
        {
            if (bonus.seasons == null || bonus.seasons.Length == 0) return false;
            bool matched = false;
            foreach (var season in bonus.seasons) { if (season == tile.season) { matched = true; break; } }
            if (!matched) return false;
        }
        return true;
    }

    private static void AddBuildingBonus(ref CityYieldAgg agg, BuildingYieldBonus bonus, BuildingYieldType kind)
    {
        if (bonus == null) return;
        switch (kind)
        {
            case BuildingYieldType.Food: agg.add += bonus.foodAdd; agg.pct += bonus.foodPct; break;
            case BuildingYieldType.Production: agg.add += bonus.productionAdd; agg.pct += bonus.productionPct; break;
            case BuildingYieldType.Gold: agg.add += bonus.goldAdd; agg.pct += bonus.goldPct; break;
            case BuildingYieldType.Science: agg.add += bonus.scienceAdd; agg.pct += bonus.sciencePct; break;
            case BuildingYieldType.Culture: agg.add += bonus.cultureAdd; agg.pct += bonus.culturePct; break;
            case BuildingYieldType.Faith: agg.add += bonus.faithAdd; agg.pct += bonus.faithPct; break;
            case BuildingYieldType.PolicyPoints: agg.add += bonus.policyPointsAdd; agg.pct += bonus.policyPointsPct; break;
        }
    }

    private static void AddTileBonus(ref CityYieldAgg agg, TileYieldBonus bonus, BuildingYieldType kind)
    {
        if (bonus == null) return;
        switch (kind)
        {
            case BuildingYieldType.Food: agg.add += bonus.foodAdd; agg.pct += bonus.foodPct; break;
            case BuildingYieldType.Production: agg.add += bonus.productionAdd; agg.pct += bonus.productionPct; break;
            case BuildingYieldType.Gold: agg.add += bonus.goldAdd; agg.pct += bonus.goldPct; break;
            case BuildingYieldType.Science: agg.add += bonus.scienceAdd; agg.pct += bonus.sciencePct; break;
            case BuildingYieldType.Culture: agg.add += bonus.cultureAdd; agg.pct += bonus.culturePct; break;
            case BuildingYieldType.Faith: agg.add += bonus.faithAdd; agg.pct += bonus.faithPct; break;
            case BuildingYieldType.PolicyPoints: agg.add += bonus.policyPointsAdd; agg.pct += bonus.policyPointsPct; break;
        }
    }

    private static void AddUnitCityAuraYieldBonus(ref CityYieldAgg agg, UnitCityAuraAgg aura, BuildingYieldType kind)
    {
        switch (kind)
        {
            case BuildingYieldType.Food: agg.add += aura.foodAdd; agg.pct += aura.foodPct; break;
            case BuildingYieldType.Production: agg.add += aura.productionAdd; agg.pct += aura.productionPct; break;
            case BuildingYieldType.Gold: agg.add += aura.goldAdd; agg.pct += aura.goldPct; break;
            case BuildingYieldType.Science: agg.add += aura.scienceAdd; agg.pct += aura.sciencePct; break;
            case BuildingYieldType.Culture: agg.add += aura.cultureAdd; agg.pct += aura.culturePct; break;
            case BuildingYieldType.Faith: agg.add += aura.faithAdd; agg.pct += aura.faithPct; break;
            case BuildingYieldType.PolicyPoints: agg.add += aura.policyPointsAdd; agg.pct += aura.policyPointsPct; break;
        }
    }

    private static void AddCityBonus(ref CityYieldAgg agg, CityYieldBonus bonus, BuildingYieldType kind)
    {
        if (bonus == null) return;
        switch (kind)
        {
            case BuildingYieldType.Food: agg.add += bonus.foodAdd; agg.pct += bonus.foodPct; break;
            case BuildingYieldType.Production: agg.add += bonus.productionAdd; agg.pct += bonus.productionPct; break;
            case BuildingYieldType.Gold: agg.add += bonus.goldAdd; agg.pct += bonus.goldPct; break;
            case BuildingYieldType.Science: agg.add += bonus.scienceAdd; agg.pct += bonus.sciencePct; break;
            case BuildingYieldType.Culture: agg.add += bonus.cultureAdd; agg.pct += bonus.culturePct; break;
            case BuildingYieldType.Faith: agg.add += bonus.faithAdd; agg.pct += bonus.faithPct; break;
            case BuildingYieldType.PolicyPoints: agg.add += bonus.policyPointsAdd; agg.pct += bonus.policyPointsPct; break;
        }
    }

    private static void AddTileBonusScaled(ref CityYieldAgg agg, TileYieldBonus bonus, BuildingYieldType kind, float scale)
    {
        if (bonus == null || Mathf.Approximately(scale, 0f)) return;
        CityYieldAgg scaled = default;
        AddTileBonus(ref scaled, bonus, kind);
        agg.add += Mathf.RoundToInt(scaled.add * scale);
        agg.pct += scaled.pct * scale;
    }

    private static void AddBuildingBonusScaled(ref CityYieldAgg agg, BuildingYieldBonus bonus, BuildingYieldType kind, float scale)
    {
        if (bonus == null || Mathf.Approximately(scale, 0f)) return;
        CityYieldAgg scaled = default;
        AddBuildingBonus(ref scaled, bonus, kind);
        agg.add += Mathf.RoundToInt(scaled.add * scale);
        agg.pct += scaled.pct * scale;
    }

    private bool MatchesBuildingYieldBonus(BuildingData data, BuildingYieldBonus bonus)
    {
        if (data == null || bonus == null)
            return false;

        if (bonus.building != null && bonus.building != data)
            return false;

        if (bonus.useBuildingCategoryFilter && !MatchesBuildingCategory(data, bonus.buildingCategory))
            return false;

        if (!HasCityResource(bonus.requiredCityResource))
            return false;

        if (!AreBuildingRequirementsMet(bonus.requiredBuildings))
            return false;

        Season currentSeason = ClimateManager.Instance != null
            ? ClimateManager.Instance.GetSeasonForPlanet(planetIndex)
            : Season.Spring;
        return Civilization.MatchesSeasonFilter(currentSeason, bonus.useSeasonFilter, bonus.seasons);
    }

    private static bool MatchesBuildingCategory(BuildingData data, BuildingCategory category)
    {
        if (data == null) return false;

        return category switch
        {
            BuildingCategory.Food => data.isFoodBuilding,
            BuildingCategory.Production => data.isProductionBuilding,
            BuildingCategory.Gold => data.isGoldBuilding,
            BuildingCategory.Science => data.isScienceBuilding,
            BuildingCategory.Culture => data.isCultureBuilding,
            BuildingCategory.Faith => data.isFaithBuilding,
            BuildingCategory.Health => data.isHealthBuilding,
            BuildingCategory.Defense => data.isDefenseBuilding,
            BuildingCategory.Energy => data.isEnergyBuilding,
            BuildingCategory.Harbor => data.providesHarbor,
            BuildingCategory.Airport => data.providesAirport,
            BuildingCategory.Spaceport => data.providesSpaceport,
            BuildingCategory.PerimeterWall => data.isPerimeterWall,
            _ => false,
        };
    }

    private bool MatchesCityYieldBonus(CityYieldBonus bonus)
    {
        if (bonus == null)
            return false;

        Season currentSeason = ClimateManager.Instance != null
            ? ClimateManager.Instance.GetSeasonForPlanet(planetIndex)
            : Season.Spring;
        if (!Civilization.MatchesSeasonFilter(currentSeason, bonus.useSeasonFilter, bonus.seasons))
            return false;

        if (bonus.scope == CityYieldScope.CapitalOnly && !owner.IsCapitalCity(this))
            return false;

        if (bonus.useLayerFilter)
        {
            if (bonus.layers == null || bonus.layers.Length == 0) return false;
            bool layerMatched = false;
            foreach (var layer in bonus.layers)
            {
                if (layer == cityLayer)
                {
                    layerMatched = true;
                    break;
                }
            }
            if (!layerMatched) return false;
        }

        return true;
    }


    private static void AddBuildingStatBonus(ref CityStatAgg agg, BuildingYieldBonus bonus)
    {
        if (bonus == null) return;
        agg.defenseAdd += bonus.defenseAdd;
        agg.defensePct += bonus.defensePct;
        agg.happinessAdd += bonus.happinessAdd;
        agg.happinessPct += bonus.happinessPct;
    }

    private static void AddBuildingStatBonusScaled(ref CityStatAgg agg, BuildingYieldBonus bonus, float scale)
    {
        if (bonus == null || Mathf.Approximately(scale, 0f)) return;
        agg.defenseAdd += Mathf.RoundToInt(bonus.defenseAdd * scale);
        agg.defensePct += bonus.defensePct * scale;
        agg.happinessAdd += Mathf.RoundToInt(bonus.happinessAdd * scale);
        agg.happinessPct += bonus.happinessPct * scale;
    }

    private static void AddCityStatBonus(ref CityStatAgg agg, CityYieldBonus bonus)
    {
        if (bonus == null) return;
        agg.defenseAdd += bonus.defenseAdd;
        agg.defensePct += bonus.defensePct;
        agg.happinessAdd += bonus.happinessAdd;
        agg.happinessPct += bonus.happinessPct;
    }


    private UnitCityAuraAgg AggregateIncomingUnitCityAuras()
    {
        UnitCityAuraAgg agg = default;
        if (owner == null || centerTileIndex < 0) return agg;
        var ts = TileSys;
        if (ts == null) return agg;

        void AccumulateFrom(BaseUnit source)
        {
            if (source == null || source.planetIndex != planetIndex || source.currentTileIndex < 0) return;
            foreach (var aura in source.EnumerateOwnedAuraBonuses())
            {
                if (aura == null || aura.radius < 0) continue;
                if (aura.targetRelationship != UnitAuraTargetRelationship.SameCivilization && aura.targetRelationship != UnitAuraTargetRelationship.Friendly) continue;
                if (source.owner == null) continue;
                if (source.owner != owner)
                {
                    var state = DiplomacyManager.Instance != null
                        ? DiplomacyManager.Instance.GetRelationship(source.owner, owner)
                        : (source.owner.relations != null && source.owner.relations.TryGetValue(owner, out var rel) ? rel : DiplomaticState.Peace);
                    if (state == DiplomaticState.War) continue;
                }

                var tiles = MissileManager.GetTilesInRadius(ts, source.currentTileIndex, aura.radius);
                if (tiles == null || !tiles.Contains(centerTileIndex)) continue;

                agg.foodAdd += aura.cityFoodAdd; agg.productionAdd += aura.cityProductionAdd; agg.goldAdd += aura.cityGoldAdd;
                agg.scienceAdd += aura.cityScienceAdd; agg.cultureAdd += aura.cityCultureAdd; agg.faithAdd += aura.cityFaithAdd; agg.policyPointsAdd += aura.cityPolicyPointsAdd;
                agg.foodPct += aura.cityFoodPct; agg.productionPct += aura.cityProductionPct; agg.goldPct += aura.cityGoldPct;
                agg.sciencePct += aura.citySciencePct; agg.culturePct += aura.cityCulturePct; agg.faithPct += aura.cityFaithPct; agg.policyPointsPct += aura.cityPolicyPointsPct;
                agg.orderAdd += aura.cityOrderAdd; agg.happinessAdd += aura.cityHappinessAdd; agg.defenseAdd += aura.cityDefenseAdd;
                agg.orderPct += aura.cityOrderPct; agg.happinessPct += aura.cityHappinessPct; agg.defensePct += aura.cityDefensePct;
            }
        }

        foreach (var civ in FindObjectsByType<Civilization>())
        {
            if (civ == null) continue;
            if (civ.combatUnits != null) foreach (var unit in civ.combatUnits) AccumulateFrom(unit);
            if (civ.workerUnits != null) foreach (var unit in civ.workerUnits) AccumulateFrom(unit);
        }

        foreach (var improvement in FindObjectsByType<ImprovementInstance>())
        {
            if (improvement == null || improvement.PlanetIndex != planetIndex || improvement.tileIndex < 0 || improvement.owner == null || improvement.owner != owner) continue;
            foreach (var aura in improvement.EnumerateOwnedAuraBonuses())
            {
                if (aura == null || aura.radius < 0) continue;
                var tiles = MissileManager.GetTilesInRadius(ts, improvement.tileIndex, aura.radius);
                if (tiles == null || !tiles.Contains(centerTileIndex)) continue;
                agg.foodAdd += aura.cityFoodAdd; agg.productionAdd += aura.cityProductionAdd; agg.goldAdd += aura.cityGoldAdd;
                agg.scienceAdd += aura.cityScienceAdd; agg.cultureAdd += aura.cityCultureAdd; agg.faithAdd += aura.cityFaithAdd; agg.policyPointsAdd += aura.cityPolicyPointsAdd;
                agg.foodPct += aura.cityFoodPct; agg.productionPct += aura.cityProductionPct; agg.goldPct += aura.cityGoldPct;
                agg.sciencePct += aura.citySciencePct; agg.culturePct += aura.cityCulturePct; agg.faithPct += aura.cityFaithPct; agg.policyPointsPct += aura.cityPolicyPointsPct;
                agg.orderAdd += aura.cityOrderAdd; agg.happinessAdd += aura.cityHappinessAdd; agg.defenseAdd += aura.cityDefenseAdd;
                agg.orderPct += aura.cityOrderPct; agg.happinessPct += aura.cityHappinessPct; agg.defensePct += aura.cityDefensePct;
            }
        }

        return agg;
    }

    private CityStatAgg AggregateBuildingStatBonuses(BuildingData data)
    {
        CityStatAgg agg = default;
        if (data == null) return agg;

        agg.defenseAdd += Mathf.RoundToInt(data.defenseBonus);
        agg.happinessAdd += Mathf.RoundToInt(data.happinessBonus);
        agg.orderAdd += Mathf.RoundToInt(data.orderBonus);
        agg.happinessPct += data.cityHappinessModifier;
        agg.orderPct += data.cityOrderModifier;

        void Scan(BuildingYieldBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var bonus in bonuses)
                if (MatchesBuildingYieldBonus(data, bonus))
                    AddBuildingStatBonus(ref agg, bonus);
        }

        Scan(owner?.civData?.buildingBonuses);
        Scan(owner?.civData?.effects?.buildingBonuses);
        Scan(owner?.leader?.buildingBonuses);

        if (owner?.researchedTechs != null)
            foreach (var tech in owner.researchedTechs)
                Scan(tech?.buildingBonuses);

        if (owner?.researchedCultures != null)
            foreach (var culture in owner.researchedCultures)
                Scan(culture?.buildingBonuses);

        Scan(owner?.currentGovernment?.buildingBonuses);

        if (owner?.activePolicies != null)
            foreach (var policy in owner.activePolicies)
                Scan(policy?.buildingBonuses);

        if (owner?.activeLegacies != null)
            foreach (var legacy in owner.activeLegacies)
                Scan(legacy?.buildingBonuses);

        foreach (var pantheonBonuses in owner?.EnumeratePantheonBonuses() ?? System.Linq.Enumerable.Empty<PantheonBonuses>())
            Scan(pantheonBonuses?.buildingYieldBonuses);

        if (owner != null && owner.hasFoundedReligion)
            Scan(owner.foundedReligion?.buildingYieldBonuses);

        if (owner != null)
        {
            foreach (var belief in owner.EnumerateActiveBeliefs())
            {
                if (belief == null || !owner.IsBeliefSeasonActive(belief, planetIndex)) continue;
                Scan(belief.buildingYieldBonuses);
            }
        }

        foreach (var (sourceData, _, sourceUpkeepMultiplier) in EnumerateOperationalBuildings())
        {
            if (sourceData?.buildingBonuses == null) continue;
            foreach (var bonus in sourceData.buildingBonuses)
                if (MatchesBuildingYieldBonus(data, bonus))
                    AddBuildingStatBonusScaled(ref agg, bonus, sourceUpkeepMultiplier);
        }

        return agg;
    }

    private CityStatAgg AggregateCityStatBonuses()
    {
        CityStatAgg agg = default;
        if (owner == null) return agg;

        void Scan(CityYieldBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var bonus in bonuses)
                if (MatchesCityYieldBonus(bonus))
                    AddCityStatBonus(ref agg, bonus);
        }

        Scan(owner.civData?.cityBonuses);
        Scan(owner.leader?.cityBonuses);

        if (owner.researchedTechs != null)
            foreach (var tech in owner.researchedTechs)
                Scan(tech?.cityBonuses);

        if (owner.researchedCultures != null)
            foreach (var culture in owner.researchedCultures)
                Scan(culture?.cityBonuses);

        Scan(owner.currentGovernment?.cityBonuses);

        if (owner.activePolicies != null)
            foreach (var policy in owner.activePolicies)
                Scan(policy?.cityBonuses);

        if (owner.activeLegacies != null)
            foreach (var legacy in owner.activeLegacies)
                Scan(legacy?.cityBonuses);

        foreach (var pantheonBonuses in owner.EnumeratePantheonBonuses())
            Scan(pantheonBonuses?.cityYieldBonuses);

        if (owner.hasFoundedReligion)
            Scan(owner.foundedReligion?.cityBonuses);

        foreach (var belief in owner.EnumerateActiveBeliefs())
        {
            if (belief == null || !owner.IsBeliefSeasonActive(belief, planetIndex)) continue;
            Scan(belief.cityYieldBonuses);
        }

        return agg;
    }

    private CityStatAgg AggregateAllCityStatBonuses()
    {
        CityStatAgg agg = AggregateCityStatBonuses();
        UnitCityAuraAgg unitAuraAgg = AggregateIncomingUnitCityAuras();
        agg.defenseAdd += unitAuraAgg.defenseAdd;
        agg.defensePct += unitAuraAgg.defensePct;
        agg.happinessAdd += unitAuraAgg.happinessAdd;
        agg.happinessPct += unitAuraAgg.happinessPct;
        agg.orderAdd += unitAuraAgg.orderAdd;
        agg.orderPct += unitAuraAgg.orderPct;
        agg.happinessAdd += owner != null ? owner.GetResourceSurplusHappinessPerCity() : 0;
        foreach (var (data, _, upkeepMultiplier) in EnumerateOperationalBuildings())
        {
            if (data == null) continue;
            CityStatAgg buildingAgg = AggregateBuildingStatBonuses(data);
            agg.defenseAdd += Mathf.RoundToInt(buildingAgg.defenseAdd * upkeepMultiplier);
            agg.defensePct += buildingAgg.defensePct * upkeepMultiplier;
            agg.happinessAdd += Mathf.RoundToInt(buildingAgg.happinessAdd * upkeepMultiplier);
            agg.happinessPct += buildingAgg.happinessPct * upkeepMultiplier;
            agg.orderAdd += Mathf.RoundToInt(buildingAgg.orderAdd * upkeepMultiplier);
            agg.orderPct += buildingAgg.orderPct * upkeepMultiplier;
        }
        return agg;
    }

    public void RefreshCityDefenseAndHappinessBonuses()
    {
        int oldMaxDefense = Mathf.Max(1, maxDefense);
        int oldMaxMorale = Mathf.Max(1, maxMorale);
        int oldMaxOrder = Mathf.Max(1, maxOrder);
        CityStatAgg agg = AggregateAllCityStatBonuses();

        int newMaxDefense = Mathf.Max(1, Mathf.RoundToInt((baseMaxDefense + agg.defenseAdd) * (1f + agg.defensePct)));
        int newMaxMorale = Mathf.Max(1, Mathf.RoundToInt((baseMaxHappiness + agg.happinessAdd) * (1f + agg.happinessPct)));
        int newMaxOrder = Mathf.Max(1, Mathf.RoundToInt((baseMaxOrder + agg.orderAdd) * (1f + agg.orderPct)));

        bool defenseWasFull = defenseRating >= oldMaxDefense;
        bool moraleWasFull = moraleRating >= oldMaxMorale;
        bool orderWasFull = orderRating >= oldMaxOrder;

        maxDefense = newMaxDefense;
        maxMorale = newMaxMorale;
        maxOrder = newMaxOrder;

        defenseRating = defenseWasFull ? maxDefense : Mathf.Clamp(defenseRating, 0, maxDefense);
        moraleRating = moraleWasFull ? maxMorale : Mathf.Clamp(moraleRating, 0, maxMorale);
        orderRating = orderWasFull ? maxOrder : Mathf.Clamp(orderRating, 0, maxOrder);
    }

    private CityYieldAgg AggregateBuildingYieldBonuses(BuildingData data, BuildingYieldType kind)
    {
        CityYieldAgg agg = default;
        if (owner == null || data == null) return agg;

        void Scan(BuildingYieldBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var bonus in bonuses)
                if (MatchesBuildingYieldBonus(data, bonus))
                    AddBuildingBonus(ref agg, bonus, kind);
        }

        Scan(owner.civData?.buildingBonuses);
        Scan(owner.leader?.buildingBonuses);

        if (owner.researchedTechs != null)
            foreach (var tech in owner.researchedTechs)
                Scan(tech?.buildingBonuses);

        if (owner.researchedCultures != null)
            foreach (var culture in owner.researchedCultures)
                Scan(culture?.buildingBonuses);

        Scan(owner.currentGovernment?.buildingBonuses);

        if (owner.activePolicies != null)
            foreach (var policy in owner.activePolicies)
                Scan(policy?.buildingBonuses);

        if (owner.activeLegacies != null)
            foreach (var legacy in owner.activeLegacies)
                Scan(legacy?.buildingBonuses);

        foreach (var pantheonBonuses in owner.EnumeratePantheonBonuses())
            Scan(pantheonBonuses?.buildingYieldBonuses);

        if (owner.hasFoundedReligion)
            Scan(owner.foundedReligion?.buildingYieldBonuses);

        foreach (var belief in owner.EnumerateActiveBeliefs())
        {
            if (belief == null || !owner.IsBeliefSeasonActive(belief, planetIndex)) continue;
            Scan(belief.buildingYieldBonuses);
        }

        foreach (var (sourceData, _, sourceUpkeepMultiplier) in EnumerateOperationalBuildings())
        {
            if (sourceData?.buildingBonuses == null) continue;
            foreach (var bonus in sourceData.buildingBonuses)
                if (MatchesBuildingYieldBonus(data, bonus))
                    AddBuildingBonusScaled(ref agg, bonus, kind, sourceUpkeepMultiplier);
        }

        return agg;
    }

    private CityYieldAgg AggregateReligionTileBonuses(HexTileData tile, BuildingYieldType kind)
    {
        CityYieldAgg agg = default;
        if (owner == null || tile == null) return agg;

        foreach (var pantheonBonuses in owner.EnumeratePantheonBonuses())
        {
            if (pantheonBonuses?.tileYieldBonuses == null) continue;
            foreach (var bonus in pantheonBonuses.tileYieldBonuses)
                if (MatchesTileYieldBonus(tile, bonus))
                    AddTileBonus(ref agg, bonus, kind);
        }

        if (owner.hasFoundedReligion && owner.foundedReligion?.tileYieldBonuses != null)
        {
            foreach (var bonus in owner.foundedReligion.tileYieldBonuses)
                if (MatchesTileYieldBonus(tile, bonus))
                    AddTileBonus(ref agg, bonus, kind);
        }

        foreach (var belief in owner.EnumerateActiveBeliefs())
        {
            if (belief?.tileYieldBonuses == null || !owner.IsBeliefSeasonActive(belief, planetIndex)) continue;
            foreach (var bonus in belief.tileYieldBonuses)
                if (MatchesTileYieldBonus(tile, bonus))
                    AddTileBonus(ref agg, bonus, kind);
        }

        return agg;
    }

    private CityYieldAgg AggregateCivLeaderGovernorTileBonuses(HexTileData tile, BuildingYieldType kind)
    {
        CityYieldAgg agg = default;
        if (owner == null || tile == null) return agg;

        if (owner.civData?.tileYieldBonuses != null)
        {
            foreach (var bonus in owner.civData.tileYieldBonuses)
                if (MatchesTileYieldBonus(tile, bonus))
                    AddTileBonus(ref agg, bonus, kind);
        }

        if (owner.leader?.tileYieldBonuses != null)
        {
            foreach (var bonus in owner.leader.tileYieldBonuses)
                if (MatchesTileYieldBonus(tile, bonus))
                    AddTileBonus(ref agg, bonus, kind);
        }

        if (governor?.Traits != null)
        {
            foreach (var trait in governor.Traits)
            {
                if (trait?.tileYieldBonuses == null) continue;
                foreach (var bonus in trait.tileYieldBonuses)
                    if (MatchesTileYieldBonus(tile, bonus))
                        AddTileBonus(ref agg, bonus, kind);
            }
        }

        return agg;
    }

    private CityYieldAgg AggregateTechCulturePolicyTileBonuses(HexTileData tile, BuildingYieldType kind)
    {
        CityYieldAgg agg = default;
        if (owner == null || tile == null) return agg;

        if (owner.researchedTechs != null)
        {
            foreach (var tech in owner.researchedTechs)
            {
                if (tech?.tileYieldBonuses == null) continue;
                foreach (var bonus in tech.tileYieldBonuses)
                    if (MatchesTileYieldBonus(tile, bonus))
                        AddTileBonus(ref agg, bonus, kind);
            }
        }

        if (owner.researchedCultures != null)
        {
            foreach (var culture in owner.researchedCultures)
            {
                if (culture?.tileYieldBonuses == null) continue;
                foreach (var bonus in culture.tileYieldBonuses)
                    if (MatchesTileYieldBonus(tile, bonus))
                        AddTileBonus(ref agg, bonus, kind);
            }
        }

        if (owner.currentGovernment?.tileYieldBonuses != null)
        {
            foreach (var bonus in owner.currentGovernment.tileYieldBonuses)
                if (MatchesTileYieldBonus(tile, bonus))
                    AddTileBonus(ref agg, bonus, kind);
        }

        if (owner.activePolicies != null)
        {
            foreach (var policy in owner.activePolicies)
            {
                if (policy?.tileYieldBonuses == null) continue;
                foreach (var bonus in policy.tileYieldBonuses)
                    if (MatchesTileYieldBonus(tile, bonus))
                        AddTileBonus(ref agg, bonus, kind);
            }
        }

        return agg;
    }

    private CityYieldAgg AggregateBuildingTileYieldBonuses(HexTileData tile, BuildingYieldType kind)
    {
        CityYieldAgg agg = default;
        if (tile == null) return agg;

        foreach (var (data, _, upkeepMultiplier) in EnumerateOperationalBuildings())
        {
            if (data?.tileYieldBonuses == null) continue;
            foreach (var bonus in data.tileYieldBonuses)
                if (MatchesTileYieldBonus(tile, bonus))
                    AddTileBonusScaled(ref agg, bonus, kind, upkeepMultiplier);
        }

        return agg;
    }

    private CityYieldAgg AggregateCityScopedYieldBonuses(BuildingYieldType kind)
    {
        CityYieldAgg agg = default;
        if (owner == null) return agg;

        void Scan(CityYieldBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var bonus in bonuses)
            {
                if (!MatchesCityYieldBonus(bonus)) continue;
                AddCityBonus(ref agg, bonus, kind);
            }
        }

        Scan(owner.civData?.cityBonuses);
        Scan(owner.leader?.cityBonuses);

        if (owner.researchedTechs != null)
            foreach (var tech in owner.researchedTechs)
                Scan(tech?.cityBonuses);

        if (owner.researchedCultures != null)
            foreach (var culture in owner.researchedCultures)
                Scan(culture?.cityBonuses);

        Scan(owner.currentGovernment?.cityBonuses);

        if (owner.activePolicies != null)
            foreach (var policy in owner.activePolicies)
                Scan(policy?.cityBonuses);

        if (owner.activeLegacies != null)
            foreach (var legacy in owner.activeLegacies)
                Scan(legacy?.cityBonuses);

        foreach (var pantheonBonuses in owner.EnumeratePantheonBonuses())
            Scan(pantheonBonuses?.cityYieldBonuses);

        if (owner.hasFoundedReligion)
            Scan(owner.foundedReligion?.cityBonuses);

        foreach (var belief in owner.EnumerateActiveBeliefs())
        {
            if (belief == null || !owner.IsBeliefSeasonActive(belief, planetIndex)) continue;
            Scan(belief.cityYieldBonuses);
        }

        foreach (var (data, _, upkeepMultiplier) in EnumerateOperationalBuildings())
        {
            if (data == null) continue;
            agg.pct += GetDirectCityYieldModifier(data, kind) * upkeepMultiplier;
        }

        AddUnitCityAuraYieldBonus(ref agg, AggregateIncomingUnitCityAuras(), kind);

        return agg;
    }

    private float GetDirectCityYieldModifier(BuildingData data, BuildingYieldType kind)
    {
        if (data == null) return 0f;
        return kind switch
        {
            BuildingYieldType.Food => data.cityFoodModifier,
            BuildingYieldType.Production => data.cityProductionModifier,
            BuildingYieldType.Gold => data.cityGoldModifier,
            BuildingYieldType.Science => data.cityScienceModifier,
            BuildingYieldType.Culture => data.cityCultureModifier,
            BuildingYieldType.Faith => data.cityFaithModifier,
            BuildingYieldType.PolicyPoints => data.cityPolicyPointsModifier,
            _ => 0f,
        };
    }

    private float GetHighHappinessYieldModifier(BuildingYieldType kind)
    {
        if (kind != BuildingYieldType.Science && kind != BuildingYieldType.Culture) return 0f;
        if (maxMorale <= 0) return 0f;
        float ratio = moraleRating / (float)maxMorale;
        return ratio >= 0.90f ? 0.10f : ratio >= 0.75f ? 0.05f : 0f;
    }

    private float GetHighOrderYieldModifier(BuildingYieldType kind)
    {
        if (kind != BuildingYieldType.Production && kind != BuildingYieldType.Faith) return 0f;
        if (maxOrder <= 0) return 0f;
        float ratio = orderRating / (float)maxOrder;
        return ratio >= 0.90f ? 0.10f : ratio >= 0.75f ? 0.05f : 0f;
    }

    public float GetOrderRaidReduction()
    {
        if (maxOrder <= 0) return 0f;
        float ratio = orderRating / (float)maxOrder;
        if (ratio >= 0.90f) return 0.08f;
        if (ratio >= 0.75f) return 0.04f;
        if (ratio <= 0.25f) return -0.05f;
        return 0f;
    }

    public float GetRebellionChance()
    {
        float happinessRatio = maxMorale > 0 ? moraleRating / (float)maxMorale : 0f;
        float orderRatio = maxOrder > 0 ? orderRating / (float)maxOrder : 0f;
        float chance = 0f;
        if (happinessRatio < 0.50f) chance += (0.50f - happinessRatio) * 0.04f;
        if (orderRatio < 0.50f) chance += (0.50f - orderRatio) * 0.06f;
        if (loyalty < 50f) chance += (50f - loyalty) * 0.001f;
        return Mathf.Clamp(chance, 0f, 0.15f);
    }

    private int ApplyCityScopedYieldBonuses(int value, BuildingYieldType kind)
    {
        var agg = AggregateCityScopedYieldBonuses(kind);
        float statusModifier = GetHighHappinessYieldModifier(kind) + GetHighOrderYieldModifier(kind);
        float empireModifier = owner != null ? owner.GetEmpireBuildingYieldModifier(ToEmpireYieldType(kind)) : 0f;
        return Mathf.RoundToInt((value + agg.add) * (1f + agg.pct + statusModifier + empireModifier));
    }

    private static EmpireYieldType ToEmpireYieldType(BuildingYieldType kind)
    {
        return kind switch
        {
            BuildingYieldType.Food => EmpireYieldType.Food,
            BuildingYieldType.Production => EmpireYieldType.Production,
            BuildingYieldType.Gold => EmpireYieldType.Gold,
            BuildingYieldType.Science => EmpireYieldType.Science,
            BuildingYieldType.Culture => EmpireYieldType.Culture,
            BuildingYieldType.Faith => EmpireYieldType.Faith,
            BuildingYieldType.PolicyPoints => EmpireYieldType.PolicyPoints,
            _ => EmpireYieldType.Food,
        };
    }

    int SumBuiltWithBonuses(BuildingYieldType kind)
    {
        int total = 0;
        foreach (var (data, _, upkeepMultiplier) in EnumerateOperationalBuildings())
        {
            if (data == null) continue;

            int baseVal = 0;
            switch (kind)
            {
                case BuildingYieldType.Food: baseVal = data.foodPerTurn; break;
                case BuildingYieldType.Production: baseVal = data.productionPerTurn; break;
                case BuildingYieldType.Gold: baseVal = data.goldPerTurn; break;
                case BuildingYieldType.Science: baseVal = data.sciencePerTurn; break;
                case BuildingYieldType.Culture: baseVal = data.culturePerTurn; break;
                case BuildingYieldType.Faith: baseVal = data.faithPerTurn; break;
                case BuildingYieldType.PolicyPoints: baseVal = data.policyPointsPerTurn; break;
            }

            if (owner != null)
            {
                CityYieldAgg buildingAgg = AggregateBuildingYieldBonuses(data, kind);
                baseVal = Mathf.RoundToInt((baseVal + buildingAgg.add) * (1f + buildingAgg.pct));
            }

            baseVal = Mathf.RoundToInt(baseVal * upkeepMultiplier);
            total += baseVal;
        }

        return total;
    }
    
    public int GetGoldPerTurn()
    {
        int baseGold = SumYield(t => t.gold) + SumBuiltWithBonuses(BuildingYieldType.Gold);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseGold += bonuses.gold;
        }
        return ApplyCityScopedYieldBonuses(baseGold, BuildingYieldType.Gold);
    }

    public void AddResourceSurplusProduction(int amount)
    {
        resourceSurplusProductionBonusThisTurn += Mathf.Max(0, amount);
    }

    public int GetProductionPerTurn()
    {
        int baseProd = SumYield(t => t.production) + SumBuiltWithBonuses(BuildingYieldType.Production) + resourceSurplusProductionBonusThisTurn;
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseProd += bonuses.production;
        }
        return ApplyCityScopedYieldBonuses(baseProd, BuildingYieldType.Production);
    }

    public int GetProductionPerTurn(ProdEntry entry)
    {
        int baseProduction = GetProductionPerTurn();
        if (entry == null || (entry.type != ProdEntry.Type.Unit && entry.type != ProdEntry.Type.Worker))
            return baseProduction;

        var (flatAdd, percentAdd) = GetUnitProductionModifierTotals(entry.data);
        float modified = (baseProduction + flatAdd) * (1f + percentAdd);
        return Mathf.Max(0, Mathf.RoundToInt(modified));
    }

    private (int flatAdd, float percentAdd) GetUnitProductionModifierTotals(ScriptableObject unitData)
    {
        int flatAdd = 0;
        float percentAdd = 0f;

        void Scan(UnitProductionModifier[] modifiers, float sourceMultiplier = 1f)
        {
            if (modifiers == null) return;
            foreach (var modifier in modifiers)
            {
                if (modifier == null || !MatchesUnitProductionModifier(unitData, modifier)) continue;
                flatAdd += Mathf.RoundToInt(modifier.productionAdd * sourceMultiplier);
                percentAdd += modifier.productionPct * sourceMultiplier;
            }
        }

        if (owner != null)
        {
            Scan(owner.civData?.unitProductionModifiers);
            Scan(owner.civData?.effects?.unitProductionModifiers);
            if (owner.researchedTechs != null)
                foreach (var tech in owner.researchedTechs) Scan(tech?.unitProductionModifiers);
            if (owner.researchedCultures != null)
                foreach (var culture in owner.researchedCultures) Scan(culture?.unitProductionModifiers);
        }

        foreach (var (building, _, upkeepMultiplier) in EnumerateOperationalBuildings())
            Scan(building?.unitProductionModifiers, upkeepMultiplier);

        return (flatAdd, percentAdd);
    }

    private static bool MatchesUnitProductionModifier(ScriptableObject unitData, UnitProductionModifier modifier)
    {
        if (modifier == null) return false;
        if (unitData is CombatUnitData combat)
        {
            if (modifier.combatUnit != null && modifier.combatUnit != combat) return false;
            if (modifier.workerUnit != null) return false;
            if (modifier.useCombatCategoryFilter && modifier.combatCategory != combat.unitType) return false;
            return true;
        }
        if (unitData is WorkerUnitData worker)
        {
            if (modifier.workerUnit != null && modifier.workerUnit != worker) return false;
            if (modifier.combatUnit != null || modifier.useCombatCategoryFilter) return false;
            return true;
        }
        return false;
    }
    
    public int GetSciencePerTurn()
    {
        int baseScience = SumYield(t => t.science) + SumBuiltWithBonuses(BuildingYieldType.Science);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseScience += bonuses.science;
        }
        return ApplyCityScopedYieldBonuses(baseScience, BuildingYieldType.Science);
    }
    
    public int GetCulturePerTurn()
    {
        int baseCulture = SumYield(t => t.culture) + SumBuiltWithBonuses(BuildingYieldType.Culture);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseCulture += bonuses.culture;
        }
        return ApplyCityScopedYieldBonuses(baseCulture, BuildingYieldType.Culture);
    }
    
    public int GetPolicyPointPerTurn()
    {
        int basePolicyPoints = SumYield(t => 0) + SumBuiltWithBonuses(BuildingYieldType.PolicyPoints);
        
        // Governors don't have base policy point bonuses, but traits might add them in the future
        
        return ApplyCityScopedYieldBonuses(basePolicyPoints, BuildingYieldType.PolicyPoints);
    }

    public void AddBuildingResourceProductionTo(Dictionary<ResourceData, int> totals)
    {
        if (totals == null)
            return;

        foreach (var (data, _, upkeepMultiplier) in EnumerateOperationalBuildings())
        {
            if (data == null || data.resourceProductionPerTurn == null)
                continue;

            foreach (var resourceYield in data.resourceProductionPerTurn)
            {
                if (resourceYield == null || resourceYield.resource == null || resourceYield.amount <= 0)
                    continue;

                if (!totals.ContainsKey(resourceYield.resource))
                    totals[resourceYield.resource] = 0;

                totals[resourceYield.resource] += Mathf.RoundToInt(resourceYield.amount * upkeepMultiplier);
            }
        }
    }

    public int GetResourceProductionPerTurn(ResourceData resource)
    {
        if (resource == null)
            return 0;

        int total = 0;
        foreach (var (data, _, upkeepMultiplier) in EnumerateOperationalBuildings())
        {
            if (data == null || data.resourceProductionPerTurn == null)
                continue;

            foreach (var resourceYield in data.resourceProductionPerTurn)
            {
                if (resourceYield == null || resourceYield.resource != resource || resourceYield.amount <= 0)
                    continue;

                total += Mathf.RoundToInt(resourceYield.amount * upkeepMultiplier);
            }
        }

        return total;
    }

    // Placeholder for summing yields from owned tiles within radius
    int SumYield(System.Func<HexTileData,int> selector)
    {
        int total = 0;
        if (planetGenerator == null) planetGenerator = ResolvePlanetGenerator();
        if (planetGenerator == null) return 0; // Safety check
        var ts = TileSys;
        if (ts == null) return 0;

        // Planet-scoped owned tiles (tile indices repeat across planets)
        if (owner == null || owner.ownedTilesByPlanet == null || !owner.ownedTilesByPlanet.TryGetValue(planetIndex, out var ownedSet) || ownedSet == null)
            return 0;
        var owned = ownedSet;
        Vector3 cityCenterPos = ts.GetTileCenterFlat(centerTileIndex);
        float maxDist = 1.0f * TerritoryRadius; // Default spacing value
        
        // Create test tile once to determine which yield type the selector accesses
        HexTileData testTile = new HexTileData
        {
            food = 1000,
            production = 2000,
            gold = 3000,
            science = 4000,
            culture = 5000,
            faithYield = 6000,
            policyPointYield = 7000
        };
        int testResult = selector(testTile);
        
        // Determine which improvement yield property to use based on test result
        System.Func<ImprovementData, int> improvementSelector = null;
        if (testResult == 1000)
            improvementSelector = (imp) => imp.foodPerTurn;
        else if (testResult == 2000)
            improvementSelector = (imp) => imp.productionPerTurn;
        else if (testResult == 3000)
            improvementSelector = (imp) => imp.goldPerTurn;
        else if (testResult == 4000)
            improvementSelector = (imp) => imp.sciencePerTurn;
        else if (testResult == 5000)
            improvementSelector = (imp) => imp.culturePerTurn;
        else if (testResult == 6000)
            improvementSelector = (imp) => imp.faithPerTurn;
        else if (testResult == 7000)
            improvementSelector = (imp) => imp.policyPointsPerTurn;

        foreach (int idx in owned)
        {
            Vector3 tilePos = ts.GetTileCenterFlat(idx);
            float distanceSqr = (cityCenterPos - tilePos).sqrMagnitude;
            if (distanceSqr <= maxDist * maxDist)
            {
                var maybe = ts.GetTileData(idx);
                if (maybe != null)
                {
                    BuildingYieldType yieldKind = testResult == 1000 ? BuildingYieldType.Food :
                        testResult == 2000 ? BuildingYieldType.Production :
                        testResult == 3000 ? BuildingYieldType.Gold :
                        testResult == 4000 ? BuildingYieldType.Science :
                        testResult == 5000 ? BuildingYieldType.Culture :
                        testResult == 6000 ? BuildingYieldType.Faith : BuildingYieldType.PolicyPoints;

                    int baseTileYield = selector(maybe);
                    int effectiveTileYield = baseTileYield;

                    var civLeaderGovernorTileAgg = AggregateCivLeaderGovernorTileBonuses(maybe, yieldKind);
                    effectiveTileYield = Mathf.RoundToInt((effectiveTileYield + civLeaderGovernorTileAgg.add) * (1f + civLeaderGovernorTileAgg.pct));

                    var techCulturePolicyTileAgg = AggregateTechCulturePolicyTileBonuses(maybe, yieldKind);
                    effectiveTileYield = Mathf.RoundToInt((effectiveTileYield + techCulturePolicyTileAgg.add) * (1f + techCulturePolicyTileAgg.pct));

                    var buildingTileAgg = AggregateBuildingTileYieldBonuses(maybe, yieldKind);
                    effectiveTileYield = Mathf.RoundToInt((effectiveTileYield + buildingTileAgg.add) * (1f + buildingTileAgg.pct));

                    var tileBonusAgg = AggregateReligionTileBonuses(maybe, yieldKind);
                    effectiveTileYield = Mathf.RoundToInt((effectiveTileYield + tileBonusAgg.add) * (1f + tileBonusAgg.pct));

                    total += effectiveTileYield;

                    // Underwater biome bonus yields: when an ocean tile has a non-default
                    // underwaterBiome AND an underwater improvement or district, grant the
                    // difference between the underwater-floor yields and the surface Ocean yields.
                    if (maybe.IsUnderwaterTile && (maybe.HasUnderwaterImprovement || maybe.HasUnderwaterDistrict))
                    {
                        var uwYields = BiomeHelper.Yields(maybe.underwaterBiome);
                        var surfYields = BiomeHelper.Yields(maybe.biome);
                        var uwTile = new HexTileData
                        {
                            food = Mathf.Max(0, uwYields.food - surfYields.food),
                            production = Mathf.Max(0, uwYields.prod - surfYields.prod),
                            gold = Mathf.Max(0, uwYields.gold - surfYields.gold),
                            science = Mathf.Max(0, uwYields.sci - surfYields.sci),
                            culture = Mathf.Max(0, uwYields.cult - surfYields.cult)
                        };
                        total += selector(uwTile);
                    }
                    
                    // Add yields from improvements on this tile
                    // Only add yield if the improvement is owned by this city's owner
                    if (maybe.HasImprovement && maybe.improvement != null && 
                        (maybe.improvementOwner == owner || maybe.improvementOwner == null) &&
                        improvementSelector != null)
                    {
                        total += improvementSelector(maybe.improvement);
                    }
                }
            }
        }
        // Add yield from city center tile itself (if not covered by loop)
        var centerMaybe = ts.GetTileData(centerTileIndex);
        if(centerMaybe != null) {
             // Decide if center tile yield counts or if it's replaced by city flat yields
        }

        // Consider adding flat yields from the city center itself if applicable
        return total;
    }

    // Sums yields from buildings
    int SumBuilt(System.Func<BuildingData,int> selector)
    {
        int total = 0;
        foreach(var (data, _) in builtBuildings)
            total += selector(data);
        return total;
    }

    /// <summary>
    /// Called when a unit attacks this city - tracks the attacking civilization for capture
    /// </summary>
    public void OnAttackedBy(Civilization attackingCiv)
    {
        if (attackingCiv != null && attackingCiv != owner)
        {
            lastAttackingCiv = attackingCiv;
        }
    }
    
    /// <summary>
    /// Reduce city defense (called when attacked by a unit)
    /// </summary>
    public void TakeDamage(int damage, Civilization attackingCiv = null)
    {
        if (attackingCiv != null && attackingCiv != owner)
        {
            lastAttackingCiv = attackingCiv;
        }
        
        defenseRating = Mathf.Max(0, defenseRating - damage);
        
        // Check if city should surrender
        if (defenseRating <= 0 || moraleRating <= 0 || orderRating <= 0 || loyalty <= 0)
        {
            HandleSurrender(lastAttackingCiv);
        }
    }
    
    /// <summary>
    /// Handle city surrender - transfers ownership to the capturing civilization
    /// </summary>
    /// <param name="capturingCiv">The civilization that captured the city (from the unit that attacked). If null, will find from units on the city tile.</param>
    void HandleSurrender(Civilization capturingCiv = null)
    {
// Find the capturing civilization - prioritize the one that actually attacked
        Civilization attackerCiv = capturingCiv ?? lastAttackingCiv;
        
        // If still not found, find from units on the city tile (the unit that took it)
        if (attackerCiv == null && owner != null)
        {
            // Check for enemy combat units on the city tile
            var allCivs = CivilizationManager.Instance?.GetAllCivs();
            if (allCivs != null)
            {
                foreach (var civ in allCivs)
                {
                    if (civ != null && civ != owner)
                    {
                        // Check if this civ has units on the city tile
                        if (civ.combatUnits != null)
                        {
                            foreach (var unit in civ.combatUnits)
                            {
                                if (unit != null && unit.currentTileIndex == centerTileIndex)
                                {
                                    attackerCiv = civ;
                                    break;
                                }
                            }
                        }
                        
                        if (attackerCiv != null) break;
                    }
                }
            }
        }
        
        // If we found an attacker, transfer ownership
        if (attackerCiv != null && owner != null)
        {
            var oldOwner = owner;
            
            // 1) Remove from old owner
            oldOwner?.RemoveCity(this);
            
            // 2) Transfer city to attacker
            owner = attackerCiv;
            attackerCiv?.AddCity(this);
            
            // 3) Reassign any garrisoned units (those on the city tile)
            //    Combat units:
            var combatToMove = oldOwner.combatUnits
                .Where(u => u != null && u.currentTileIndex == centerTileIndex)
                .ToList();
            foreach (var u in combatToMove)
            {
                oldOwner.combatUnits.Remove(u);
                if (!attackerCiv.combatUnits.Contains(u))
                {
                    attackerCiv.combatUnits.Add(u);
                }
                u.Initialize(u.data, attackerCiv);  // reset its owner internally
            }
            //    Worker units:
            var workerToMove = oldOwner.workerUnits
                .Where(w => w != null && w.currentTileIndex == centerTileIndex)
                .ToList();
            foreach (var w in workerToMove)
            {
                oldOwner.workerUnits.Remove(w);
                if (!attackerCiv.workerUnits.Contains(w))
                {
                    attackerCiv.workerUnits.Add(w);
                }
                w.Initialize(w.data, attackerCiv, w.currentTileIndex);   // reset its owner, keep position
            }
            
            // 4) Reassign map-ownership of the city's tiles
            var planet = GameManager.Instance?.GetCurrentPlanetGenerator();
            if (planet != null)
            {
                List<int> territoryTiles = GetTerritoryTiles(TerritoryRadius);
                foreach (int idx in territoryTiles)
                {
                    var ts = TileSys;
                    if (ts != null) ts.SetTileOwner(idx, attackerCiv, this);
                }
            }
            
            // 5) Reset loyalty and morale
            loyalty = 50f;
        orderRating = Mathf.Max(50, orderRating); // Start with moderate loyalty to new owner
            moraleRating = Mathf.Max(50, moraleRating); // Boost morale slightly after surrender
            
            // 6) Update diplomatic relations (city capture doesn't automatically end war)
            // Wars continue unless a peace deal is made, just like in Civilization
            
            // 7) Show UI notification
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"{cityName} has been captured by {attackerCiv.civData.civName}!");
            }
}
        else
        {
            // No attacker found - city is destroyed/abandoned
            Debug.LogWarning($"⚠️ {cityName} surrendered but no attacker found. City will be destroyed.");
            owner?.RemoveCity(this);
            
            // Show notification
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"{cityName} has been abandoned!");
            }
            
            Destroy(gameObject);
        }
    }
    
    // Helper method to get all building data (for UI/inspection)
    public List<BuildingData> GetBuildings()
    {
        List<BuildingData> result = new List<BuildingData>();
        foreach (var (data, _) in builtBuildings) {
            result.Add(data);
        }
        return result;
    }
    
    // Helper method to get all district data (for UI/inspection)
    public List<DistrictData> GetDistricts()
    {
        List<DistrictData> result = new List<DistrictData>();
        foreach (var (data, _, _) in builtDistricts) {
            result.Add(data);
        }
        return result;
    }

    public int GetFaithPerTurn()
    {
        int faith = baseFaithPerTurn;
    faith += SumBuiltWithBonuses(BuildingYieldType.Faith);
        faith += SumYield(t => t.faithYield);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            faith += bonuses.faith;
        }
        // Add faith from districts (unchanged)
        foreach (var (district, _, tileIndex) in builtDistricts)
        {
            faith += district.baseFaith;
            if (district.isHolySite)
            {
                var ts = TileSys;
                if (ts == null) continue;
                var tileData = ts.GetTileData(tileIndex);
                if (tileData == null) continue;
                var adjacentTiles = ts.GetNeighbors(tileIndex);
                faith += Mathf.RoundToInt(adjacentTiles.Length * district.adjacencyBonusPerAdjacentTile);
                ReligionData dominantReligion = owner.hasFoundedReligion && owner.foundedReligion != null
                    ? owner.foundedReligion
                    : ts.GetDominantReligion(tileIndex);
            }
        }
        return ApplyCityScopedYieldBonuses(faith, BuildingYieldType.Faith);
    }

    /// <summary>
    /// Finds a valid tile for placing a district
    /// </summary>
    private int FindValidDistrictTile(DistrictData district)
    {
        if (planetGenerator == null) throw new System.Exception("City references not set!");
        var ts = TileSys;
        if (ts == null) return -1;
        
        // Check city center and neighbors
    var tiles = new List<int> { centerTileIndex };
    tiles.AddRange(ts.GetNeighbors(centerTileIndex));
        
        foreach (int tileIndex in tiles)
        {
            if (IsValidDistrictTile(tileIndex, district))
                return tileIndex;
        }
        
        return -1; // No valid tile found
    }

    /// <summary>
    /// Update the available buildings and units based on researched technologies
    /// Called when a new tech or culture is researched
    /// </summary>
    public void UpdateAvailableBuildings()
    {
        // This method is called by Civilization when a new tech or culture is researched
        // It will be used by the CityUI to refresh its available buildings and units
        
        // We don't need to implement anything here since the CityUI
        // determines available buildings and units when it's opened
        
        // Notify any open UI that it should refresh
        var cityUI = FindAnyObjectByType<CityUI>();
        if (cityUI != null && cityUI.gameObject.activeSelf && cityUI.CurrentCity == this)
        {
            cityUI.RefreshUI();
        }
    }
    
    // --- Trade Routes ---
    private List<TradeRoute> activeTradeRoutes = new List<TradeRoute>();
    private const int MAX_TRADE_ROUTES = 1; // Cities start with 1 trade route capacity
    
    /// <summary>
    /// Check if this city can initiate new trade routes
    /// </summary>
    public bool CanInitiateTradeRoute()
    {
        bool tradeEnabled = owner != null && (owner.tradeEnabled || (TradeManager.Instance != null && TradeManager.Instance.IsTradeEnabledForCivilization(owner)));
        return tradeEnabled && activeTradeRoutes.Count < MAX_TRADE_ROUTES;
    }
    
    /// <summary>
    /// Get all cities within trade range
    /// </summary>
    public List<City> GetCitiesInTradeRange()
    {
        List<City> citiesInRange = new List<City>();
        int tradeRange = TradeManager.CurrentMaxCityTradeRange;
        
        // Get civilizations without scanning the scene if possible
        IReadOnlyList<Civilization> allCivs = CivilizationManager.Instance != null
            ? CivilizationManager.Instance.GetAllCivs()
            : (IReadOnlyList<Civilization>)new List<Civilization>(FindObjectsByType<Civilization>());
        
        foreach (var civ in allCivs)
        {
            foreach (var city in civ.cities)
            {
                if (city == this) continue; // Skip self

                if (CanEstablishTradeRouteWith(city, tradeRange))
                {
                    citiesInRange.Add(city);
                }
            }
        }
        
        return citiesInRange;
    }
    
    /// <summary>
    /// Check if this city has an active trade route with another city
    /// </summary>
    public bool HasTradeRouteWith(City other)
    {
        return activeTradeRoutes.Exists(route => 
            route.destinationCity == other || route.sourceCity == other);
    }
    
    /// <summary>
    /// Get all active trade routes from this city
    /// </summary>
    public List<TradeRoute> GetActiveTradeRoutes()
    {
        return activeTradeRoutes;
    }
    
    /// <summary>
    /// Establish a new trade route with another city
    /// </summary>
    public bool EstablishTradeRoute(City destinationCity)
    {
        if (!CanInitiateTradeRoute())
            return false;
            
        if (HasTradeRouteWith(destinationCity))
            return false;

        if (!CanEstablishTradeRouteWith(destinationCity, TradeManager.CurrentMaxCityTradeRange))
            return false;
            
        var newRoute = new TradeRoute(this, destinationCity);
        activeTradeRoutes.Add(newRoute);
return true;
    }
    
    /// <summary>
    /// Process trade routes each turn - now just recalculates yields
    /// </summary>
    public void ProcessTradeRoutes()
    {
        foreach (var route in activeTradeRoutes)
        {
            route.CalculateYields();
        }
    }

    public bool CanEstablishTradeRouteWith(City destinationCity, int maxRange = -1)
    {
        if (maxRange <= 0)
            maxRange = TradeManager.CurrentMaxCityTradeRange;

        if (destinationCity == null || destinationCity == this) return false;

        bool samePlanet = planetIndex == destinationCity.planetIndex;
        int tileDistance = samePlanet ? GetTradeTileDistanceTo(destinationCity) : int.MaxValue;
        bool roadConnected = samePlanet && RoadConnectivityHelper.AreCitiesConnectedByRoad(this, destinationCity, maxRange);
        bool harborConnected = samePlanet && HasOperationalHarbor() && destinationCity.HasOperationalHarbor() && tileDistance <= maxRange;
        bool airportConnected = samePlanet && HasOperationalAirport() && destinationCity.HasOperationalAirport() && tileDistance <= TradeManager.CurrentMaxAirportTradeRange;
        bool spaceportConnected = TradeManager.CanSpacePortTradeBetween(this, destinationCity);

        return roadConnected || harborConnected || airportConnected || spaceportConnected;
    }

    public bool HasOperationalHarbor()
    {
        return HasOperationalBuilding(building => building != null && building.providesHarbor);
    }

    public bool HasOperationalAirport()
    {
        return HasOperationalBuilding(building => building != null && building.providesAirport);
    }

    public bool HasOperationalSpaceport()
    {
        return HasOperationalBuilding(building => building != null && building.providesSpaceport);
    }

    public int GetTradeTileDistanceTo(City destinationCity)
    {
        if (destinationCity == null || destinationCity.planetIndex != planetIndex)
            return int.MaxValue;

        var ts = TileSys;
        if (ts == null) return int.MaxValue;
        return Mathf.RoundToInt(ts.GetTileDistance(centerTileIndex, destinationCity.centerTileIndex));
    }

    public List<ResourceCost> GetTradeResourceExports()
    {
        var exports = new Dictionary<ResourceData, int>();

        AddBuildingResourceProductionTo(exports);

        var ts = TileSys;
        if (ts != null && ts.IsReady())
        {
            int tileCount = ts.TileCount;
            for (int i = 0; i < tileCount; i++)
            {
                var tileData = ts.GetTileData(i);
                if (tileData == null || tileData.resource == null) continue;
                if (tileData.controllingCity != this) continue;

                if (!exports.ContainsKey(tileData.resource))
                    exports[tileData.resource] = 0;
                exports[tileData.resource] = Mathf.Max(exports[tileData.resource], 1);
            }
        }

        var result = new List<ResourceCost>();
        foreach (var kvp in exports)
        {
            if (kvp.Key == null || kvp.Value <= 0) continue;
            result.Add(new ResourceCost { resource = kvp.Key, amount = 1 });
        }

        return result;
    }

    /// <summary>
    /// Cancel a specific trade route
    /// </summary>
    public bool CancelTradeRoute(City otherCity)
    {
        var route = activeTradeRoutes.Find(r => 
            r.destinationCity == otherCity || r.sourceCity == otherCity);
            
        if (route != null)
        {
            activeTradeRoutes.Remove(route);
return true;
        }
        
        return false;
    }

    public void RefreshGovernorBonuses()
    {
        // Recalculate all yields that might be affected by governor bonuses
        cachedGold = -1;      // Force recalculation
        cachedFood = -1;
        cachedScience = -1;
        cachedCulture = -1;
        cachedPolicyPoints = -1;
        cachedFaith = -1;
    }
    
    /// <summary>
    /// Calculate how much food this city's population consumes per turn
    /// </summary>
    public int GetFoodConsumptionPerTurn()
    {
        // Population consumes food based on city size (level)
        return level * foodConsumptionPerPopulation;
    }

    // Add this method to allow clicking on a city to open the City UI
    void OnMouseDown()
    {
if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
return;
        }
var cityUI = FindAnyObjectByType<CityUI>();
        if (cityUI != null)
        {
cityUI.ShowForCity(this);
}
        else
        {
            Debug.LogWarning("[City] No CityUI found in scene to show.");
        }
    }
    
    /// <summary>
    /// Upgrades the city to the correct visual prefab for the current tech age.
    /// </summary>
    public void UpdateCityModelForAge()
    {
// 1. Get the correct prefab for the new age
        GameObject newPrefab = null;
        if (owner?.civData?.cityPrefabsByAge != null)
        {
        TechAge currentAge = owner.GetCurrentAge();
        foreach (var agePrefab in owner.civData.cityPrefabsByAge)
        {
            if (agePrefab.techAge == currentAge && agePrefab.cityPrefab != null)
            {
                    newPrefab = agePrefab.cityPrefab;
                    break;
            }
        }
        }
        if (newPrefab == null)
        {
            Debug.LogWarning($"[City] No city prefab found for age {owner?.GetCurrentAge()} for {cityName}");
            return;
    }
    
        // 2. Instantiate the new prefab at the current position/rotation
        GameObject newCityGO = Instantiate(newPrefab, transform.position, transform.rotation);
        // Keep hierarchy stable: preserve parent so the city stays under its planet in the hierarchy.
        if (transform.parent != null) newCityGO.transform.SetParent(transform.parent, true);
        City newCity = newCityGO.GetComponent<City>();
        if (newCity == null)
        {
            Debug.LogError("[City] New city prefab is missing the City script!");
            Destroy(newCityGO);
            return;
            }
            
        // 3. Copy over relevant data
        newCity.cityName = this.cityName;
        newCity.owner = this.owner;
        newCity.OriginalOwner = this.OriginalOwner != null ? this.OriginalOwner : this.owner;
        newCity.centerTileIndex = this.centerTileIndex;
        newCity.cityLayer = this.cityLayer;
        newCity.planetIndex = this.planetIndex;
        // Copy label prefab if needed


        // 4. Replace in the owner's city list
        if (owner != null)
        {
            owner.ReplaceCityReference(this, newCity);
        }

        // 5. Destroy the old city object
Destroy(gameObject);
                }

    // Add a method to set references
    public void SetReferences(PlanetGenerator planet)
    {
        planetGenerator = planet;
    }
}
