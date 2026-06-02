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
        public Biome[]    requiredTerrains;
        public bool       reqCoast;        // Requires coastal city
        public bool       reqHarbor;       // Requires harbor building

        public ProdEntry(ScriptableObject d, int prodCost, int gCost,
                        ResourceData[] reqRes, Biome[] reqTerrains, 
                        bool coast, bool harbor, Type t)
        {
            data = d;
            remainingPts = prodCost;
            goldCost     = gCost;
            requiredResources = reqRes;
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

    [Header("Defense & Morale")]
    public int defenseRating = 100;
    public int maxDefense = 100;
    public int moraleRating = 100;
    public int maxMorale = 100;
    public int moraleDropPerTurn = 1;

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

    [Header("Production")]
    public int productionPerTurn = 10;
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
        return heritageOwner.GetUnitData(baseUnit);
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
            if (resolvedUnit == null || seen.Contains(resolvedUnit) || !owner.IsCombatUnitAvailable(resolvedUnit))
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
        defenseRating = maxDefense;
        moraleRating = maxMorale;

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
        moraleRating = Mathf.Max(0, moraleRating - moraleDropPerTurn);
        // 5b) Apply disease morale/loyalty/population effects
        ApplyDiseaseTurnEffects();
        // 6) Check surrender (only if defense was reduced by attacks, not just decay)
        // Surrender is handled in TakeDamage() when a unit attacks
        // If defense reaches 0 from other means, check for units on tile
        if (defenseRating <= 0 || moraleRating <= 0 || loyalty <= 0)
            HandleSurrender(lastAttackingCiv); // Use last attacking civ, or find from units on tile
        // 7) Update label
        UpdateLabel();
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

        loyalty = loyalty - warPenaltyPercent - faminePenaltyPercent + governorBonus;

        // Clamp 0–100
        loyalty = Mathf.Clamp(loyalty, 0f, 100f);

        // Check for revolt
        if (loyalty <= revoltThreshold)
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

        // TODO: spawn rebel units, trigger UI popup, play SFX/VFX, etc.
    }
    
    // Helper method to get all tiles in this city's territory
    private List<int> GetTerritoryTiles(int radius)
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
        
        // Apply production points from this turn
    prodEntry.remainingPts -= GetProductionPerTurn();
        
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
    /// Quick lookup of coastal tiles this city controls
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
            if (biome == Biome.Coast || biome == Biome.Seas || biome == Biome.Ocean)
                return true;
        }
        return false;
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
            
            // Check naval requirements
            if (requiresCoast && !ControlsCoast()) return false;
            if (requiresHarbor && !HasHarbor()) return false;
            
            if (!CanProduce(resolvedUnit.requiredResources, resolvedUnit.requiredTerrains)) return false;
            productionQueue.Add(new ProdEntry(resolvedUnit, resolvedUnit.productionCost, resolvedUnit.goldCost,
                                            resolvedUnit.requiredResources, resolvedUnit.requiredTerrains,
                                            requiresCoast, requiresHarbor,
                                            ProdEntry.Type.Unit));
            return true;
        }
        if (d is WorkerUnitData w) {
            bool requiresCoast = w.requiresCoastalCity;
            bool requiresHarbor = w.requiresHarbor;
            
            // Check naval requirements
            if (requiresCoast && !ControlsCoast()) return false;
            if (requiresHarbor && !HasHarbor()) return false;
            
            if (!CanProduce(w.requiredResources, w.requiredTerrains)) return false;
            productionQueue.Add(new ProdEntry(w, w.productionCost, w.goldCost,
                                            w.requiredResources, w.requiredTerrains,
                                            requiresCoast, requiresHarbor,
                                            ProdEntry.Type.Worker));
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
            // Population requirement
            if (b.requiredPopulation > 0 && level < b.requiredPopulation) {
                Debug.LogWarning($"Cannot build {b.buildingName} - requires population level {b.requiredPopulation}, current {level}");
                return false;
            }
            if (!CanProduce(b.requiredResources, b.requiredTerrains)) return false;
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
        Biome[] reqTerr = null;
        bool requiresCoast = false;
        bool requiresHarbor = false;
        bool isHarborBuilding = false;
        
        // Get cost and requirements based on type without using dynamic
        if (d is CombatUnitData u) {
            var resolvedUnit = ResolveCombatUnitForProduction(u);
            if (resolvedUnit == null || !IsCombatUnitAvailableForProduction(resolvedUnit)) return false;
            cost = resolvedUnit.goldCost;
            reqRes = resolvedUnit.requiredResources;
            reqTerr = resolvedUnit.requiredTerrains;
            requiresCoast = resolvedUnit.requiresCoastalCity;
            requiresHarbor = resolvedUnit.requiresHarbor;
            d = resolvedUnit;
        }
        else if (d is WorkerUnitData w) {
            cost = w.goldCost;
            reqRes = w.requiredResources;
            reqTerr = w.requiredTerrains;
            requiresCoast = w.requiresCoastalCity;
            requiresHarbor = w.requiresHarbor;
        }
        else if (d is BuildingData b) {
            cost = b.goldCost;
            reqRes = b.requiredResources;
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
        
        if (owner.gold < cost) return false;
        
        // Check naval requirements
        if (requiresCoast && !ControlsCoast()) return false;
        if (requiresHarbor && !HasHarbor()) return false;
        
        // Special check for harbor buildings
        if (isHarborBuilding && !ControlsCoast()) {
            Debug.LogWarning("Cannot buy harbor - city is not coastal!");
            return false;
        }
        
        // Validate other requirements
        if (!CanProduce(reqRes, reqTerr)) return false;

        if (d is BuildingData buildingToBuy)
        {
            if (!buildingToBuy.CanPayBuildCosts(owner)) return false;
            if (!buildingToBuy.ConsumeBuildCosts(owner)) return false;
        }
        
        owner.gold -= cost;
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
    private bool CanProduce(ResourceData[] reqRes, Biome[] reqTerrains) {
        // Resources
        if (reqRes != null && reqRes.Length > 0) {
            foreach (var r in reqRes) {
                if (owner.GetResourceCount(r) <= 0) return false;
            }
        }
        
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

        defenseRating = Mathf.Min(maxDefense, defenseRating + 10); // example defense bonus
        moraleRating = Mathf.Min(maxMorale, moraleRating + 5); // example morale bonus
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

    public int TerritoryRadius => baseRadius
        + (level >= 20 ? 1 : 0) + (level >= 40 ? 1 : 0);

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
        return ApplyCityScopedReligionBonuses(baseFood, BuildingYieldType.Food);
    }

    enum BuildingYieldType { Food, Production, Gold, Science, Culture, Faith, PolicyPoints }
    struct CityYieldAgg
    {
        public int add;
        public float pct;
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
        if (bonus.useResourceFilter)
        {
            if (tile.resource == null) return false;
            if (tile.resource != bonus.resource) return false;
        }
        if (bonus.useSeasonFilter)
        {
            if (bonus.seasons == null || bonus.seasons.Length == 0) return false;
            bool matched = false;
            foreach (var s in bonus.seasons) { if (s == tile.season) { matched = true; break; } }
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

    private bool MatchesBuildingYieldBonus(BuildingYieldBonus bonus)
    {
        if (bonus == null)
            return false;

        Season currentSeason = ClimateManager.Instance != null
            ? ClimateManager.Instance.GetSeasonForPlanet(planetIndex)
            : Season.Spring;
        return Civilization.MatchesSeasonFilter(currentSeason, bonus.useSeasonFilter, bonus.seasons);
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

        return true;
    }

    private CityYieldAgg AggregateReligionBuildingBonuses(BuildingData data, BuildingYieldType kind)
    {
        CityYieldAgg agg = default;
        if (owner == null || data == null) return agg;

        foreach (var pantheonBonuses in owner.EnumeratePantheonBonuses())
        {
            if (pantheonBonuses?.buildingYieldBonuses == null) continue;
            foreach (var bonus in pantheonBonuses.buildingYieldBonuses)
                if (bonus != null && bonus.building == data && MatchesBuildingYieldBonus(bonus))
                    AddBuildingBonus(ref agg, bonus, kind);
        }

        foreach (var belief in owner.EnumerateActiveBeliefs())
        {
            if (belief?.buildingYieldBonuses == null || !owner.IsBeliefSeasonActive(belief, planetIndex)) continue;
            foreach (var bonus in belief.buildingYieldBonuses)
                if (bonus != null && bonus.building == data && MatchesBuildingYieldBonus(bonus))
                    AddBuildingBonus(ref agg, bonus, kind);
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

        foreach (var belief in owner.EnumerateActiveBeliefs())
        {
            if (belief?.tileYieldBonuses == null || !owner.IsBeliefSeasonActive(belief, planetIndex)) continue;
            foreach (var bonus in belief.tileYieldBonuses)
                if (MatchesTileYieldBonus(tile, bonus))
                    AddTileBonus(ref agg, bonus, kind);
        }

        return agg;
    }

    private CityYieldAgg AggregateReligionCityBonuses(BuildingYieldType kind)
    {
        CityYieldAgg agg = default;
        if (owner == null) return agg;

        foreach (var pantheonBonuses in owner.EnumeratePantheonBonuses())
        {
            if (pantheonBonuses?.cityYieldBonuses == null) continue;
            foreach (var bonus in pantheonBonuses.cityYieldBonuses)
            {
                if (!MatchesCityYieldBonus(bonus)) continue;
                AddCityBonus(ref agg, bonus, kind);
            }
        }

        foreach (var belief in owner.EnumerateActiveBeliefs())
        {
            if (belief?.cityYieldBonuses == null || !owner.IsBeliefSeasonActive(belief, planetIndex)) continue;
            foreach (var bonus in belief.cityYieldBonuses)
            {
                if (!MatchesCityYieldBonus(bonus)) continue;
                AddCityBonus(ref agg, bonus, kind);
            }
        }

        return agg;
    }

    private int ApplyCityScopedReligionBonuses(int value, BuildingYieldType kind)
    {
        var agg = AggregateReligionCityBonuses(kind);
        return Mathf.RoundToInt((value + agg.add) * (1f + agg.pct));
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
                // Local aggregate through techs/cultures
                var agg = new { foodAdd = 0, prodAdd = 0, goldAdd = 0, scienceAdd = 0, cultureAdd = 0, faithAdd = 0, policyAdd = 0, foodPct = 0f, prodPct = 0f, goldPct = 0f, sciencePct = 0f, culturePct = 0f, faithPct = 0f, policyPct = 0f };
                if (owner.researchedTechs != null)
                    foreach (var t in owner.researchedTechs)
                    {
                        if (t?.buildingBonuses == null) continue;
                        foreach (var b in t.buildingBonuses)
                            if (b != null && b.building == data)
                            {
                                agg = new { foodAdd = agg.foodAdd + b.foodAdd, prodAdd = agg.prodAdd + b.productionAdd, goldAdd = agg.goldAdd + b.goldAdd, scienceAdd = agg.scienceAdd + b.scienceAdd, cultureAdd = agg.cultureAdd + b.cultureAdd, faithAdd = agg.faithAdd + b.faithAdd, policyAdd = agg.policyAdd + b.policyPointsAdd, foodPct = agg.foodPct + b.foodPct, prodPct = agg.prodPct + b.productionPct, goldPct = agg.goldPct + b.goldPct, sciencePct = agg.sciencePct + b.sciencePct, culturePct = agg.culturePct + b.culturePct, faithPct = agg.faithPct + b.faithPct, policyPct = agg.policyPct + b.policyPointsPct };
                            }
                    }
                if (owner.researchedCultures != null)
                    foreach (var c in owner.researchedCultures)
                    {
                        if (c?.buildingBonuses == null) continue;
                        foreach (var b in c.buildingBonuses)
                            if (b != null && b.building == data)
                            {
                                agg = new { foodAdd = agg.foodAdd + b.foodAdd, prodAdd = agg.prodAdd + b.productionAdd, goldAdd = agg.goldAdd + b.goldAdd, scienceAdd = agg.scienceAdd + b.scienceAdd, cultureAdd = agg.cultureAdd + b.cultureAdd, faithAdd = agg.faithAdd + b.faithAdd, policyAdd = agg.policyAdd + b.policyPointsAdd, foodPct = agg.foodPct + b.foodPct, prodPct = agg.prodPct + b.productionPct, goldPct = agg.goldPct + b.goldPct, sciencePct = agg.sciencePct + b.sciencePct, culturePct = agg.culturePct + b.culturePct, faithPct = agg.faithPct + b.faithPct, policyPct = agg.policyPct + b.policyPointsPct };
                            }
                    }
                int add = 0; float pct = 0f;
                switch (kind)
                {
                    case BuildingYieldType.Food: add = agg.foodAdd; pct = agg.foodPct; break;
                    case BuildingYieldType.Production: add = agg.prodAdd; pct = agg.prodPct; break;
                    case BuildingYieldType.Gold: add = agg.goldAdd; pct = agg.goldPct; break;
                    case BuildingYieldType.Science: add = agg.scienceAdd; pct = agg.sciencePct; break;
                    case BuildingYieldType.Culture: add = agg.cultureAdd; pct = agg.culturePct; break;
                    case BuildingYieldType.Faith: add = agg.faithAdd; pct = agg.faithPct; break;
                    case BuildingYieldType.PolicyPoints: add = agg.policyAdd; pct = agg.policyPct; break;
                }
                baseVal = Mathf.RoundToInt((baseVal + add) * (1f + pct));
                var religionAgg = AggregateReligionBuildingBonuses(data, kind);
                baseVal = Mathf.RoundToInt((baseVal + religionAgg.add) * (1f + religionAgg.pct));
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
        return ApplyCityScopedReligionBonuses(baseGold, BuildingYieldType.Gold);
    }

    public int GetProductionPerTurn()
    {
        int baseProd = SumYield(t => t.production) + SumBuiltWithBonuses(BuildingYieldType.Production);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseProd += bonuses.production;
        }
        return ApplyCityScopedReligionBonuses(baseProd, BuildingYieldType.Production);
    }
    
    public int GetSciencePerTurn()
    {
        int baseScience = SumYield(t => t.science) + SumBuiltWithBonuses(BuildingYieldType.Science);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseScience += bonuses.science;
        }
        return ApplyCityScopedReligionBonuses(baseScience, BuildingYieldType.Science);
    }
    
    public int GetCulturePerTurn()
    {
        int baseCulture = SumYield(t => t.culture) + SumBuiltWithBonuses(BuildingYieldType.Culture);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseCulture += bonuses.culture;
        }
        return ApplyCityScopedReligionBonuses(baseCulture, BuildingYieldType.Culture);
    }
    
    public int GetPolicyPointPerTurn()
    {
        int basePolicyPoints = SumYield(t => 0) + SumBuiltWithBonuses(BuildingYieldType.PolicyPoints);
        
        // Governors don't have base policy point bonuses, but traits might add them in the future
        
        return ApplyCityScopedReligionBonuses(basePolicyPoints, BuildingYieldType.PolicyPoints);
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
                    total += selector(maybe);

                    var tileBonusAgg = AggregateReligionTileBonuses(maybe, kind: testResult == 1000 ? BuildingYieldType.Food :
                        testResult == 2000 ? BuildingYieldType.Production :
                        testResult == 3000 ? BuildingYieldType.Gold :
                        testResult == 4000 ? BuildingYieldType.Science :
                        testResult == 5000 ? BuildingYieldType.Culture :
                        testResult == 6000 ? BuildingYieldType.Faith : BuildingYieldType.PolicyPoints);
                    int baseTileYield = selector(maybe);
                    total += Mathf.RoundToInt((baseTileYield + tileBonusAgg.add) * (1f + tileBonusAgg.pct)) - baseTileYield;

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
        if (defenseRating <= 0 || moraleRating <= 0 || loyalty <= 0)
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
            loyalty = 50f; // Start with moderate loyalty to new owner
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
        return ApplyCityScopedReligionBonuses(faith, BuildingYieldType.Faith);
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
        return activeTradeRoutes.Count < MAX_TRADE_ROUTES;
    }
    
    /// <summary>
    /// Get all cities within trade range
    /// </summary>
    public List<City> GetCitiesInTradeRange()
    {
        List<City> citiesInRange = new List<City>();
        int tradeRange = 10; // Default trade range, could be modified by technology/civics
        
        // Get civilizations without scanning the scene if possible
        IReadOnlyList<Civilization> allCivs = CivilizationManager.Instance != null
            ? CivilizationManager.Instance.GetAllCivs()
            : (IReadOnlyList<Civilization>)new List<Civilization>(FindObjectsByType<Civilization>());
        
        foreach (var civ in allCivs)
        {
            foreach (var city in civ.cities)
            {
                if (city == this) continue; // Skip self

                // Trade range is planet-local for now (no interplanetary trade distance here).
                if (city == null || city.planetIndex != planetIndex) continue;
                var ts = TileSys;
                if (ts == null) continue;
                int distance = Mathf.RoundToInt(ts.GetTileDistance(centerTileIndex, city.centerTileIndex));
                if (distance <= tradeRange)
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
