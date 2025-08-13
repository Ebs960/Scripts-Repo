// Assets/Scripts/Cities/City.cs
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class City : MonoBehaviour
{
    // Production Queue Entry Definition
    public class ProdEntry {
        public enum Type { Unit, Worker, Building, District }
        public Type       type;
        public ScriptableObject data;      // CombatUnitData, WorkerUnitData, BuildingData, or DistrictData
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
    public int centerTileIndex;
    public Governor governor;

    [Header("Growth & Level")]
    public int level = 1;
    public int foodStorage = 0;
    public int foodGrowthRequirement = 20;

    [Header("Defense & Morale")]
    public int defenseRating = 100;
    public int maxDefense = 100;
    public int moraleRating = 100;
    public int maxMorale = 100;
    public int moraleDropPerTurn = 1;

    [Header("Loyalty")]
    [Tooltip("0 = total unrest, 100 = full loyalty")]
    [Range(0f, 100f)]
    public float loyalty = 100f;
    [Tooltip("If loyalty falls to or below this, the city revolts")]
    public float revoltThreshold = 30f;

    [Header("Territory")]
    public int baseRadius = 1;

    [Header("Production")]
    public int productionPerTurn = 10;
    public List<ProdEntry> productionQueue = new List<ProdEntry>();

    [Header("Built Content")]
    // Track (BuildingData, its spawned GameObject) so we can replace/destroy instances
    public List<(BuildingData data, GameObject instance)> builtBuildings = new List<(BuildingData, GameObject)>();
    // Track (DistrictData, its spawned GameObject, tile index) for districts
    public List<(DistrictData data, GameObject instance, int tileIndex)> builtDistricts = new List<(DistrictData, GameObject, int)>();
    public List<CombatUnitData> producedUnits = new List<CombatUnitData>();
    public List<EquipmentData> producedEquipment = new List<EquipmentData>();

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
    private int cachedFood;
    private int cachedScience;
    private int cachedCulture;
    private int cachedPolicyPoints;
    private int cachedFaith;

    // Dictionary to track which tile each district in queue will be placed on
    private Dictionary<DistrictData, int> districtTileTargets = new Dictionary<DistrictData, int>();

    void Start()
    {
        // If owner isn't set, this object is probably a template/prefab, do nothing.
        if (owner == null) return; 

        owner.AddCity(this);
        
        // Ensure city center and territory tiles are assigned to civ
        var territory = GetTerritoryTiles(baseRadius);
        foreach (var idx in territory)
        {
            if (!owner.ownedTileIndices.Contains(idx))
                owner.ownedTileIndices.Add(idx);
        }

        CreateLabelUI();
        
        // Cache reference
        // Use GameManager API for multi-planet support
        planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
    }

    /// <summary>
    /// Initialize the city with a name, owner and optional governor
    /// </summary>
    public void Initialize(string name, Civilization civ, Governor gov = null)
    {
        cityName = name;
        owner = civ;
        governor = gov;
        loyalty = 100f;
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
        RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3.5f, 1.5f);
        canvasRect.localScale = Vector3.one * (0.12f / 6f);
        canvasRect.localPosition = Vector3.zero;

        // Add a background panel (optional, for readability)
        GameObject bgGO = new GameObject("LabelBG");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0,0,0,0.4f);
        RectTransform bgRect = bgGO.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Add civ icon
        GameObject iconGO = new GameObject("CivIcon");
        iconGO.transform.SetParent(canvasGO.transform, false);
        civIconImg = iconGO.AddComponent<UnityEngine.UI.Image>();
        RectTransform iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.sizeDelta = new Vector2(200, 200);
        iconRect.anchoredPosition = new Vector2(-60, 0);
        civIconImg.preserveAspect = true;

        // Add city name
        GameObject nameGO = new GameObject("CityName");
        nameGO.transform.SetParent(canvasGO.transform, false);
        nameText = nameGO.AddComponent<TMPro.TextMeshProUGUI>();
        nameText.fontSize = 32;
        nameText.alignment = TMPro.TextAlignmentOptions.Left;
        RectTransform nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.sizeDelta = new Vector2(200, 20);
        nameRect.anchoredPosition = new Vector2(10, 30);

        // Add level
        GameObject levelGO = new GameObject("CityLevel");
        levelGO.transform.SetParent(canvasGO.transform, false);
        levelText = levelGO.AddComponent<TMPro.TextMeshProUGUI>();
        levelText.fontSize = 24;
        levelText.alignment = TMPro.TextAlignmentOptions.Left;
        RectTransform levelRect = levelGO.GetComponent<RectTransform>();
        levelRect.sizeDelta = new Vector2(200, 32);
        levelRect.anchoredPosition = new Vector2(10, 0);

        // Add loyalty
        GameObject loyaltyGO = new GameObject("CityLoyalty");
        loyaltyGO.transform.SetParent(canvasGO.transform, false);
        loyaltyText = loyaltyGO.AddComponent<TMPro.TextMeshProUGUI>();
        loyaltyText.fontSize = 24;
        loyaltyText.alignment = TMPro.TextAlignmentOptions.Left;
        RectTransform loyaltyRect = loyaltyGO.GetComponent<RectTransform>();
        loyaltyRect.sizeDelta = new Vector2(200, 32);
        loyaltyRect.anchoredPosition = new Vector2(10, -30);

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

    void LateUpdate()
    {
        // Position label above city and face camera
        if (labelCanvas != null)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 planetCenter = planetGenerator != null ? planetGenerator.transform.position : Vector3.zero;
                Vector3 normal = (transform.position - planetCenter).normalized;
                Vector3 labelPos = transform.position + normal * labelVerticalOffset;
                labelCanvas.transform.position = labelPos;
                labelCanvas.transform.rotation = cam.transform.rotation;
                float camDist = Vector3.Distance(cam.transform.position, labelPos);
                float scale = labelScaleAtReferenceDistance * (camDist / labelReferenceDistance);
                scale = Mathf.Clamp(scale, labelMinScale, labelMaxScale);
                labelCanvas.transform.localScale = Vector3.one * scale;
            }
        }
    }

    // Call this whenever city data changes
    public void UpdateLabel()
    {
        UpdateLabelUI();
    }

    private void OnLabelClicked()
    {
        Debug.Log($"[City] Label clicked for city: {cityName}");
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
        
        // 2) Process loyalty
        ProcessLoyalty();
        
        // 3) Produce
        ProcessProduction();
        // 4) Growth
        ProcessGrowth();
        // 5) Morale decay
        moraleRating = Mathf.Max(0, moraleRating - moraleDropPerTurn);
        // 6) Check surrender
        if (defenseRating <= 0 || moraleRating <= 0 || loyalty <= 0)
            HandleSurrender();
        // 7) Update label
        UpdateLabel();
    }

    /// <summary>
    /// Adjusts loyalty based on owner's war-weariness, famine and governor bonus.
    /// </summary>
    private void ProcessLoyalty()
    {
        // War-weariness penalty: convert owner's 0–1 warWeariness to percent
        float warPenaltyPercent = owner.warWeariness * 100f;

        // Famine penalty: a flat 5% loyalty loss if owner ran out of food
        float faminePenaltyPercent = owner.famineActive ? 5f : 0f;
        
        // Calculate governor bonus
        float governorBonus = 0f;
        if (governor != null)
        {
            switch (governor.specialization)
            {
                case Governor.Specialization.Military:
                    governorBonus = 10f;
                    break;
                case Governor.Specialization.Economic:
                    governorBonus = 8f;
                    break;
                case Governor.Specialization.Scientific:
                    governorBonus = 5f;
                    break;
                case Governor.Specialization.Cultural:
                    governorBonus = 12f;
                    break;
                case Governor.Specialization.Religious:
                    governorBonus = 15f;
                    break;
                case Governor.Specialization.Industrial:
                    governorBonus = 7f;
                    break;
            }
        }

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
    private void TriggerRevolt()
    {
        Debug.Log($"🔴 City '{cityName}' has revolted from {owner.civData.civName}!");

        // 1) Remove from old owner
        var oldOwner = owner;
        if (oldOwner.cities.Contains(this))
            oldOwner.cities.Remove(this);

        // 2) Create or fetch rebel faction
        var rebelCiv = CivilizationManager.Instance.CreateRebelFaction(this);

        // 3) Transfer city to rebel civ
        owner = rebelCiv;
        rebelCiv.cities.Add(this);

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
            w.Initialize(w.data, rebelCiv);   // reset its owner
        }

        // 5) Reassign map-ownership of the city's tiles
        // Use GameManager API for multi-planet support
        var planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planet != null)
        {
            // Get territory radius based on number of remaining cities
            int radius = oldOwner.cities.Count >= 1 ? oldOwner.cities.Count : 1;
            // Convert tiles in radius to rebel ownership
            List<int> territoryTiles = GetTerritoryTiles(radius);
            foreach (int idx in territoryTiles)
            {
                var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(idx);
                if (tileData != null)
                {
                    tileData.owner = rebelCiv;
                    tileData.controllingCity = this;
                    TileDataHelper.Instance.SetTileData(idx, tileData);
                }
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
        
        // Start with center and direct neighbors
        tiles.Add(centerTileIndex);
        foreach (int neighbor in TileDataHelper.Instance.GetTileNeighbors(centerTileIndex))
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
                foreach (int neighbor in TileDataHelper.Instance.GetTileNeighbors(tile))
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

    void ProcessProduction()
    {
        // If nothing in queue, just return
        if (productionQueue.Count == 0)
            return;

        // Get the current item in production (first in queue)
        var prodEntry = productionQueue[0];
        
        // Apply production points from this turn
        prodEntry.remainingPts -= productionPerTurn;
        
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
        => builtBuildings.Exists(tuple => tuple.data.providesHarbor);
        
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
        
        foreach (int idx in TileDataHelper.Instance.GetTileNeighbors(centerTileIndex))
        {
            var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(idx);
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
            bool requiresCoast = u.requiresCoastalCity;
            bool requiresHarbor = u.requiresHarbor;
            
            // Check naval requirements
            if (requiresCoast && !ControlsCoast()) return false;
            if (requiresHarbor && !HasHarbor()) return false;
            
            if (!CanProduce(u.requiredResources, u.requiredTerrains)) return false;
            productionQueue.Add(new ProdEntry(u, u.productionCost, u.goldCost,
                                            u.requiredResources, u.requiredTerrains,
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
            // Harbor buildings can only be built in coastal cities
            if (b.providesHarbor && !ControlsCoast()) {
                Debug.LogWarning($"Cannot build {b.buildingName} - city is not coastal!");
                return false;
            }
            
            if (!CanProduce(b.requiredResources, b.requiredTerrains)) return false;
            productionQueue.Add(new ProdEntry(b, b.productionCost, b.goldCost,
                                            b.requiredResources, b.requiredTerrains,
                                            false, false, // Buildings don't need coast/harbor
                                            ProdEntry.Type.Building));
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

    /// <summary>
    /// Check if a specific tile is valid for a district
    /// </summary>
    public bool IsValidDistrictTile(int tileIndex, DistrictData district)
    {
        if (planetGenerator == null) throw new System.Exception("City references not set!");
        
        // Get tile data
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return false;
        
        // Check if tile is owned by this city's civilization
        if (tileData.owner != owner) return false;
        
        // Check if tile is already occupied
        if (tileData.HasDistrict || tileData.HasImprovement || tileData.occupantId != 0)
            return false;
            
        // Check if tile is not land
        if (!tileData.isLand)
            return false;
            
        // Check if tile is within territory radius
        var cityPos = planetGenerator.Grid.tileCenters[centerTileIndex];
        var tilePos = planetGenerator.Grid.tileCenters[tileIndex];
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
            foreach (int neighborIdx in TileDataHelper.Instance.GetTileNeighbors(tileIndex))
            {
                var (neighborData, _) = TileDataHelper.Instance.GetTileData(neighborIdx);
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
            foreach (int neighborIdx in TileDataHelper.Instance.GetTileNeighbors(tileIndex))
            {
                var (neighborData, _) = TileDataHelper.Instance.GetTileData(neighborIdx);
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
            foreach (int neighborIdx in TileDataHelper.Instance.GetTileNeighbors(tileIndex))
            {
                var (neighborData, _) = TileDataHelper.Instance.GetTileData(neighborIdx);
                if (neighborData != null && neighborData.biome == Biome.Mountain)
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
            cost = u.goldCost;
            reqRes = u.requiredResources;
            reqTerr = u.requiredTerrains;
            requiresCoast = u.requiresCoastalCity;
            requiresHarbor = u.requiresHarbor;
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
        if (planetGenerator == null) planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
        Vector3 pos = planetGenerator.Grid.tileCenters[centerTileIndex];
        
        var unitGO = Instantiate(unitData.prefab, pos, Quaternion.identity);
        var unit = unitGO.GetComponent<CombatUnit>();
        unit.Initialize(unitData, owner);
        
        // Add to owner's units
        owner.combatUnits.Add(unit);
        
        Debug.Log($"Purchased {unitData.unitName} in {cityName} for {unitData.faithCost} faith.");
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
            if (planetGenerator == null) planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
            
            // gather city‐radius tiles (1 tile for simplicity)
            bool found = false;
            foreach (int n in TileDataHelper.Instance.GetTileNeighbors(centerTileIndex)) {
                if (planetGenerator == null) planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
                var (tdOpt, _) = TileDataHelper.Instance.GetTileData(n);
                
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
        if (planetGenerator == null) planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
        Vector3 pos = planetGenerator.Grid.tileCenters[centerTileIndex];

        switch (d) {
            case CombatUnitData u:
                var unitGO = Instantiate(u.prefab, pos, Quaternion.identity);
                var unit = unitGO.GetComponent<CombatUnit>();
                unit.Initialize(u, owner);
                owner.combatUnits.Add(unit);
                producedUnits.Add(u);
                
                // Award governor experience for unit production
                if (governor != null)
                {
                    governor.RecordStat(TraitTrigger.UnitsProduced);
                }
                break;

            case WorkerUnitData w:
                var wGO = Instantiate(w.prefab, pos, Quaternion.identity);
                var worker = wGO.GetComponent<WorkerUnit>();
                worker.Initialize(w, owner);
                owner.workerUnits.Add(worker);
                
                // Award governor experience for unit production
                if (governor != null)
                {
                    governor.RecordStat(TraitTrigger.UnitsProduced);
                }
                break;

            case BuildingData b:
                AddBuilding(b);
                
                // Award governor experience for building construction
                if (governor != null)
                {
                    governor.RecordStat(TraitTrigger.BuildingsConstructed);
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
        // Use civilization's method to get the appropriate building data
        // (will use unique building if available)
        if (owner != null)
        {
            b = owner.GetBuildingData(b);
        }
        
        // If this building upgrades an old one, destroy that instance
        if (b.replacesBuilding != null)
        {
            var oldTuple = builtBuildings.Find(tuple => tuple.data == b.replacesBuilding);
            
            if (oldTuple.instance != null)
            {
                Debug.Log($"Replacing {oldTuple.data.buildingName} with {b.buildingName}");
                Destroy(oldTuple.instance);
            }
            
            // Remove from list
            builtBuildings.RemoveAll(tuple => tuple.data == b.replacesBuilding);
        }
        
        // Instantiate the new building
        GameObject buildingInstance = null;
        if (b.buildingPrefab != null) 
        {
            buildingInstance = Instantiate(b.buildingPrefab, transform.position, Quaternion.identity);
            buildingInstance.transform.SetParent(transform); // Parent to city for organization
        }
        else if (b.prefab != null) // Fall back to prefab if buildingPrefab is null
        {
            buildingInstance = Instantiate(b.prefab, transform.position, Quaternion.identity);
            buildingInstance.transform.SetParent(transform);
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
                    owner.AddEquipment(production.equipment, production.quantity);
                    Debug.Log($"Building {b.buildingName} produced {production.quantity} {production.equipment.equipmentName} for {owner.civData.civName}");
                }
            }
        }
        
        Debug.Log($"Building {b.buildingName} completed in {cityName}");
    }
    
    /// <summary>
    /// Adds a district to the city on a specific tile
    /// </summary>
    void AddDistrict(DistrictData district, int tileIndex)
    {
        if (district == null || planetGenerator == null)
            return;
            
        // Get position for the district
        Vector3 pos = planetGenerator.Grid.tileCenters[tileIndex];
        
        // Instantiate the district
        GameObject districtInstance = null;
        if (district.prefab != null)
        {
            districtInstance = Instantiate(district.prefab, pos, Quaternion.identity);
        }
        
        // Update the tile data to include this district
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData != null)
        {
            // Mark the district on the tile
            tileData.district = district;
            
            // If it's a Holy Site, update the religion status
            if (district.isHolySite)
            {
                tileData.religionStatus.hasHolySite = true;
                tileData.religionStatus.holySiteDistrict = district;
                
                // If the civilization has a founded religion, apply pressure
                if (owner.hasFoundedReligion && owner.foundedReligion != null)
                {
                    tileData.religionStatus.AddPressure(owner.foundedReligion, 100f); // Initial pressure
                }
            }
            
            // Update the tile data
            TileDataHelper.Instance.SetTileData(tileIndex, tileData);
        }
        
        // Add to city's districts
        builtDistricts.Add((district, districtInstance, tileIndex));
        
        Debug.Log($"District {district.districtName} built in {cityName} at tile {tileIndex}");
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
                Debug.Log($"City {cityName} grew to level {level}! Governor {governor.Name} gained experience.");
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
        return baseFood;
    }

    enum BuildingYieldType { Food, Production, Gold, Science, Culture, Faith, PolicyPoints }

    int SumBuiltWithBonuses(BuildingYieldType kind)
    {
        int total = 0;
        foreach (var (data, _) in builtBuildings)
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
            }
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
        return baseGold;
    }
    
    public int GetSciencePerTurn()
    {
    int baseScience = SumYield(t => t.science) + SumBuiltWithBonuses(BuildingYieldType.Science);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseScience += bonuses.science;
        }
        return baseScience;
    }
    
    public int GetCulturePerTurn()
    {
    int baseCulture = SumYield(t => t.culture) + SumBuiltWithBonuses(BuildingYieldType.Culture);
        if (governor != null)
        {
            var bonuses = governor.GetTotalBonuses();
            baseCulture += bonuses.culture;
        }
        return baseCulture;
    }
    
    public int GetPolicyPointPerTurn()
    {
    int basePolicyPoints = SumYield(t => 0) + SumBuiltWithBonuses(BuildingYieldType.PolicyPoints); // Assuming tiles don't give policy points directly
        
        // Governors don't have base policy point bonuses, but traits might add them in the future
        
        return basePolicyPoints;
    }

    // Placeholder for summing yields from owned tiles within radius
    int SumYield(System.Func<HexTileData,int> selector)
    {
        int total = 0;
        if (planetGenerator == null) planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planetGenerator == null) return 0; // Safety check

        var owned = owner.ownedTileIndices; // Needs access to owner's tile list
        Vector3 cityCenterPos = planetGenerator.Grid.tileCenters[centerTileIndex];
        float maxDist = 1.0f * TerritoryRadius; // Default spacing value

        foreach (int idx in owned)
        {
            Vector3 tilePos = planetGenerator.Grid.tileCenters[idx];
            if (Vector3.Distance(cityCenterPos, tilePos) <= maxDist)
            {
                var (maybe, _) = TileDataHelper.Instance.GetTileData(idx);
                if (maybe != null)
                {
                    total += selector(maybe);
                    // TODO: Add yields from improvements on this tile (ImprovementManager?)
                }
            }
        }
        // Add yield from city center tile itself (if not covered by loop)
        var (centerMaybe, _) = TileDataHelper.Instance.GetTileData(centerTileIndex);
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

    void HandleSurrender()
    {
        Debug.Log($"{cityName} has surrendered!");
        // TODO: Implement surrender logic (transfer ownership, effects, UI notification)
        Destroy(gameObject); // Basic placeholder
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
                var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
                if (tileData == null) continue;
                var adjacentTiles = TileDataHelper.Instance.GetTileNeighbors(tileIndex);
                faith += Mathf.RoundToInt(adjacentTiles.Length * district.adjacencyBonusPerAdjacentTile);
                ReligionData dominantReligion = null;
                if (owner.hasFoundedReligion && owner.foundedReligion != null)
                {
                    dominantReligion = owner.foundedReligion;
                }
                else
                {
                    dominantReligion = tileData.religionStatus.GetDominantReligion();
                }
            }
        }
        return faith;
    }

    /// <summary>
    /// Finds a valid tile for placing a district
    /// </summary>
    private int FindValidDistrictTile(DistrictData district)
    {
        if (planetGenerator == null) throw new System.Exception("City references not set!");
        
        // Check city center and neighbors
        var tiles = new List<int> { centerTileIndex };
        tiles.AddRange(TileDataHelper.Instance.GetTileNeighbors(centerTileIndex));
        
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
        List<Civilization> allCivs = CivilizationManager.Instance != null
            ? CivilizationManager.Instance.GetAllCivs()
            : new List<Civilization>(FindObjectsByType<Civilization>(FindObjectsSortMode.None));
        
        foreach (var civ in allCivs)
        {
            foreach (var city in civ.cities)
            {
                if (city == this) continue; // Skip self
                
                int distance = Mathf.RoundToInt(TileDataHelper.Instance.GetTileDistance(centerTileIndex, city.centerTileIndex));
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
        
        Debug.Log($"Trade route established from {cityName} to {destinationCity.cityName}");
        
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
            Debug.Log($"Trade route cancelled between {cityName} and {otherCity.cityName}");
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

    // Add this method to allow clicking on a city to open the City UI
    void OnMouseDown()
    {
        Debug.Log($"[City] OnMouseDown called for city: {cityName} at position {transform.position}");
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log($"[City] Click on {cityName} ignored, was on UI.");
            return;
        }
        Debug.Log($"[City] Clicked on {cityName}.");
        var cityUI = FindAnyObjectByType<CityUI>();
        if (cityUI != null)
        {
            Debug.Log($"[City] Found CityUI: {cityUI.gameObject.name}, activeSelf: {cityUI.gameObject.activeSelf}");
            cityUI.ShowForCity(this);
            Debug.Log($"[City] Called ShowForCity on CityUI for {cityName}");
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
        Debug.Log($"[City] UpdateCityModelForAge called for {cityName}");
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
        newCity.centerTileIndex = this.centerTileIndex;
        // Copy label prefab if needed


        // 4. Replace in the owner's city list
        if (owner != null)
        {
            int idx = owner.cities.IndexOf(this);
            if (idx >= 0)
                owner.cities[idx] = newCity;
        }

        // 5. Destroy the old city object
        Debug.Log($"[City] Upgraded {cityName} to new age model: {newPrefab.name}");
        Destroy(gameObject);
                }

    // Add a method to set references
    public void SetReferences(PlanetGenerator planet)
    {
        planetGenerator = planet;
    }
}
