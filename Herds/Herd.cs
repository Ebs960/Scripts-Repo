using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class Herd : MonoBehaviour
{
    [Header("Core")]
    public Civilization owner;
    public int planetIndex = 0;
    public int currentTileIndex = -1;

    public enum HerdSpecies { Chicken, Cow, Pig, Sheep, Other }

    [System.Serializable]
    public class HerdEntry { public HerdSpecies species; public int count = 0; }

    [Header("Stored Units")]
    // Units stored inside this herd (e.g., worker who founded the herd)
    public System.Collections.Generic.List<BaseUnit> storedUnits = new System.Collections.Generic.List<BaseUnit>();

    [Header("Animals (counts, abstract)")]
    public List<HerdEntry> animals = new List<HerdEntry>();

    [Header("Grazing")]
    // Food currently held by herd from grazing (not civilization food stockpile)
    public int foodReserve = 0;
    // Last amount grazed on most recent grazing tick
    public int lastGrazedAmount = 0;

    [Header("Instance Config (per-herd; city-like)")]
    [Tooltip("Base storage capacity for this herd instance (editable per-herd).")]
    public int baseStorage = 50;
    [Tooltip("Which building types this herd instance may construct (overrides HerdData if present)")]
    public BuildingData[] allowedBuildings;

    [System.Serializable]
    public class HerdStartingAnimal { public HerdSpecies species; public int count = 0; }
    [Tooltip("Starting animals for this herd instance")]
    public HerdStartingAnimal[] startingAnimals;

    [Header("Governor")]
    [Tooltip("Governor assigned to this herd (can be null)")]
    public Governor governor;

    // Default per-animal food consumption per turn by species (used for herd starvation calculations)
    public static int GetFoodConsumptionPerAnimal(HerdSpecies s)
    {
        switch (s)
        {
            case HerdSpecies.Chicken: return 1;
            case HerdSpecies.Cow: return 2;
            case HerdSpecies.Pig: return 1;
            case HerdSpecies.Sheep: return 1;
            default: return 1;
        }
    }

    // Note: Herds store abstract species counts only. CombatUnitData is not used here
    // except when converting captured units — see AddAnimals(CombatUnitData, int).

    [Header("Storage")]
    // How much food the herd can store from grazing / purchases
    public int storageCapacity = 50;

    // Simple health/defense placeholders
    public int defenseRating = 50;
    public int maxDefense = 50;
    public int health = 100;
    public int maxHealth = 100;

    void OnEnable()
    {
        if (HerdManager.Instance != null) HerdManager.Instance.RegisterHerd(this);
        if (owner != null)
        {
            try { owner.herds.Add(this); } catch { }
        }
        // initialize storage from per-herd baseStorage
        try { storageCapacity = Mathf.Max(1, baseStorage); } catch { }
        // create label UI for this herd
        try { CreateLabelUI(); UpdateLabelUI(); } catch { }
    }

    void OnDisable()
    {
        if (HerdManager.Instance != null) HerdManager.Instance.UnregisterHerd(this);
        if (owner != null)
        {
            try { owner.herds.Remove(this); } catch { }
        }
    }

    /// <summary>
    /// Called externally (e.g., from a turn manager) once per turn to process grazing.
    /// Sums forage shares from current tile + neighbors and adds to `foodReserve` according
    /// to biome-based consumption intervals.
    /// </summary>
    public void ProcessGrazingTick(int round)
    {
        lastGrazedAmount = 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return;

        // Build list of considered tiles: current tile + neighbors
        var tiles = new List<int>();
        if (currentTileIndex >= 0) tiles.Add(currentTileIndex);
        if (currentTileIndex >= 0)
        {
            var neigh = ts.GetNeighbors(currentTileIndex);
            foreach (var n in neigh) tiles.Add(n);
        }

        // For each tile, decide whether it triggers grazing this turn based on biome interval
        foreach (var t in tiles)
        {
            var td = ts.GetTileData(t);
            if (td == null) continue;
            int interval = HerdManager.Instance != null ? HerdManager.Instance.GetConsumptionIntervalForBiome(td.biome) : 1;

            // Simple deterministic schedule: use round modulo interval to decide when to graze
            if (interval <= 1 || (round % interval) == 0)
            {
                int baseFood = HerdManager.Instance != null ? HerdManager.Instance.ComputeHerdForageShare(planetIndex, t, this) : Mathf.Max(0, td.food);
                float multiplier = 1f;
                try
                {
                    if (ClimateManager.Instance != null)
                    {
                        var season = ClimateManager.Instance.GetSeasonForPlanet(planetIndex);
                        var resp = ClimateManager.Instance.GetSeasonResponseForTile(t, season);
                        multiplier = resp != null ? resp.yieldMultiplier : 1f;
                    }
                }
                catch { }

                int share = Mathf.FloorToInt(baseFood * multiplier);
                lastGrazedAmount += Mathf.Max(0, share);
            }
        }

        // Add to reserve (cap by storage capacity)
        if (lastGrazedAmount > 0)
        {
            try { foodReserve = Mathf.Min(storageCapacity, foodReserve + lastGrazedAmount); } catch { foodReserve += lastGrazedAmount; }
        }
    }

    /// <summary>
    /// Add animals to the herd (by species). Herd stores abstract species counts only.
    /// </summary>
    public void AddAnimals(HerdSpecies species, int count)
    {
        if (count <= 0) return;
        var e = animals.Find(x => x.species == species);
        if (e == null) animals.Add(new HerdEntry { species = species, count = count }); else e.count += count;
    }

    // Conversion overload: map a captured CombatUnitData into a HerdSpecies and add
    public void AddAnimals(CombatUnitData type, int count)
    {
        if (type == null || count <= 0) return;
        // Prefer explicit species set on unit data
        HerdSpecies s = HerdSpecies.Other;
        try { s = type.captureSpecies; } catch { s = HerdSpecies.Other; }
        if (s == HerdSpecies.Other)
        {
            var name = (type.unitName ?? "").ToLowerInvariant();
            if (name.Contains("chicken")) s = HerdSpecies.Chicken;
            else if (name.Contains("cow")) s = HerdSpecies.Cow;
            else if (name.Contains("pig")) s = HerdSpecies.Pig;
            else if (name.Contains("sheep") || name.Contains("ewe") || name.Contains("ram")) s = HerdSpecies.Sheep;
        }

        AddAnimals(s, count);
    }

    /// <summary>
    /// Store a unit inside this herd (e.g., the worker who founded it).
    /// Unit will be deactivated and marked as stored until unstored.
    /// </summary>
    public bool StoreUnit(BaseUnit unit)
    {
        if (unit == null) return false;
        if (owner != null && unit.owner != owner) return false;
        if (storedUnits == null) storedUnits = new System.Collections.Generic.List<BaseUnit>();
        if (storedUnits.Contains(unit)) return false;
        if (unit.currentTileIndex != currentTileIndex) return false;

        var occ = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
        {
            occ.ClearOccupant(currentTileIndex, TileLayer.Surface);
        }

        unit.isStored = true;
        unit.storedInHerd = this;
        unit.currentTileIndex = -1;
        unit.gameObject.SetActive(false);

        storedUnits.Add(unit);
        return true;
    }

    /// <summary>
    /// Try to unstore a unit back to the herd tile or an adjacent free tile.
    /// </summary>
    public bool TryUnstoreUnit(BaseUnit unit)
    {
        if (unit == null || storedUnits == null || !storedUnits.Contains(unit)) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;

        // First try herd tile
        if (occ != null && occ.GetOccupantObject(currentTileIndex, TileLayer.Surface) == null)
            return UnstoreToTile(unit, currentTileIndex);

        // Then try neighbors
        if (ts != null)
        {
            var neighbors = ts.GetNeighbors(currentTileIndex);
            if (neighbors != null)
            {
                foreach (var n in neighbors)
                {
                    if (n < 0) continue;
                    var td = ts.GetTileData(n);
                    if (td == null || !td.isPassable) continue;
                    if (occ != null && occ.GetOccupantObject(n, TileLayer.Surface) != null) continue;
                    return UnstoreToTile(unit, n);
                }
            }
        }

        return false;
    }

    private bool UnstoreToTile(BaseUnit unit, int tile)
    {
        if (unit == null) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;

        if (ts != null)
        {
            Vector3 pos = ts.GetTileCenterFlat(tile);
            unit.transform.position = pos;
        }

        unit.currentTileIndex = tile;
        try { unit.RegisterToRegistry(); } catch { }
        if (occ != null)
            occ.SetOccupant(tile, unit.gameObject, TileLayer.Surface);

        unit.gameObject.SetActive(true);
        unit.isStored = false;
        unit.storedInHerd = null;

        storedUnits.Remove(unit);
        return true;
    }

    /// <summary>
    /// Apply a herd-capable building's effects to this herd (e.g., increase storage capacity).
    /// </summary>
    public void ApplyBuilding(BuildingData building)
    {
        if (building == null) return;
        try { if (building.herdStorageBonus != 0) storageCapacity += building.herdStorageBonus; } catch { }
    }

    [Header("Structures")]
    // Buildings/structures currently attached to this herd
    public List<BuildingData> builtStructures = new List<BuildingData>();

    /// <summary>
    /// Construct or attach a building to this herd (mobile structure). Applies its effects.
    /// </summary>
    public void BuildStructure(BuildingData building)
    {
        if (building == null) return;
        // Only allow buildings that are marked buildableByHerd
        if (!building.buildableByHerd) return;
        builtStructures.Add(building);
        ApplyBuilding(building);
    }

    /// <summary>
    /// Refresh any governor-applied bonuses on this herd. Called when governor levels up or traits change.
    /// </summary>
    public void RefreshGovernorBonuses()
    {
        // Placeholder: apply governor bonuses to herd yields/buildings as needed.
        // For now this is a no-op hook so governors can be assigned to herds and notified on level-up.
    }

    // --- Simple label UI (similar to City labels) ---
    private Canvas labelCanvas;
    private TextMeshProUGUI nameTextLabel;
    private TextMeshProUGUI governorTextLabel;
    private TextMeshProUGUI ownerTextLabel;
    private UnityEngine.UI.Image ownerIconImage;
    private HerdLabel herdLabelInstance;
    [Header("Label Prefab")]
    [Tooltip("Optional prefab to use for herd world labels. Prefab should contain child objects named 'HerdName' (Text), 'HerdOwner' (Text) and optional 'HerdIcon' (Image). If not assigned, a default generated label is created.")]
    public GameObject herdLabelPrefab;

    private void CreateLabelUI()
    {
        if (labelCanvas != null) return;

        if (herdLabelPrefab != null)
        {
            var labelGO = Instantiate(herdLabelPrefab, transform);
            // Find Canvas on prefab (or its children)
            labelCanvas = labelGO.GetComponentInChildren<Canvas>();
            if (labelCanvas != null)
            {
                labelCanvas.renderMode = RenderMode.WorldSpace;
                if (labelCanvas.worldCamera == null) labelCanvas.worldCamera = Camera.main;
            }

            // Try to find HerdLabel component first (preferred)
            herdLabelInstance = labelGO.GetComponentInChildren<HerdLabel>(true);
            if (herdLabelInstance != null)
            {
                // Initialize now with available values
                var ownerName = owner != null && owner.civData != null ? owner.civData.civName : "(No Owner)";
                var ownerIcon = owner != null && owner.civData != null ? owner.civData.icon : null;
                herdLabelInstance.Initialize(transform, name, ownerName, ownerIcon, governor != null ? governor.Name : null);
            }

            // Named children lookup (fallbacks) for prefab without HerdLabel
            var nameTf = labelGO.transform.Find("HerdName");
            if (nameTf != null) nameTextLabel = nameTf.GetComponent<TextMeshProUGUI>();
            var ownerTf = labelGO.transform.Find("HerdOwner");
            if (ownerTf != null) ownerTextLabel = ownerTf.GetComponent<TextMeshProUGUI>();
            var iconTf = labelGO.transform.Find("HerdIcon");
            if (iconTf != null) ownerIconImage = iconTf.GetComponent<UnityEngine.UI.Image>();

            // Fallbacks: pick first/second TMP child if named ones not present
            var tmps = labelGO.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (nameTextLabel == null && tmps.Length > 0) nameTextLabel = tmps[0];
            if (ownerTextLabel == null && tmps.Length > 1) ownerTextLabel = tmps[1];

            // Wire click if prefab contains a Button
            var btn = labelGO.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if (btn != null) btn.onClick.AddListener(OnLabelClicked);
            else
            {
                // If no Button, add one to the root canvas for click handling
                var rootGO = labelCanvas != null ? labelCanvas.gameObject : labelGO;
                var added = rootGO.GetComponent<UnityEngine.UI.Button>();
                if (added == null) added = rootGO.AddComponent<UnityEngine.UI.Button>();
                added.onClick.AddListener(OnLabelClicked);
            }
        }
        else
        {
            GameObject canvasGO = new GameObject("HerdLabelCanvas");
            canvasGO.transform.SetParent(transform);
            labelCanvas = canvasGO.AddComponent<Canvas>();
            labelCanvas.renderMode = RenderMode.WorldSpace;
            labelCanvas.worldCamera = Camera.main;
            var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10;
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(2.5f, 0.9f);
            rect.localScale = Vector3.one * 0.08f;

            var bgGO = new GameObject("BG"); bgGO.transform.SetParent(canvasGO.transform, false);
            var bg = bgGO.AddComponent<UnityEngine.UI.Image>(); bg.color = new Color(0,0,0,0.4f);
            var bgRect = bgGO.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;

            var nameGO = new GameObject("HerdName"); nameGO.transform.SetParent(canvasGO.transform, false);
            nameTextLabel = nameGO.AddComponent<TextMeshProUGUI>(); nameTextLabel.fontSize = 22; nameTextLabel.alignment = TMPro.TextAlignmentOptions.Center;
            var nameRect = nameGO.GetComponent<RectTransform>(); nameRect.anchoredPosition = new Vector2(0, 8); nameRect.sizeDelta = new Vector2(220, 28);

            var govGO = new GameObject("HerdGovernor"); govGO.transform.SetParent(canvasGO.transform, false);
            governorTextLabel = govGO.AddComponent<TextMeshProUGUI>(); governorTextLabel.fontSize = 16; governorTextLabel.alignment = TMPro.TextAlignmentOptions.Center;
            var govRect = govGO.GetComponent<RectTransform>(); govRect.anchoredPosition = new Vector2(0, -8); govRect.sizeDelta = new Vector2(220, 20);

            // clickable area
            var btn = canvasGO.AddComponent<UnityEngine.UI.Button>(); btn.onClick.AddListener(OnLabelClicked);
        }
    }

    private void UpdateLabelUI()
    {
        // Prefer HerdLabel component when present
        if (herdLabelInstance != null)
        {
            var ownerName = owner != null && owner.civData != null ? owner.civData.civName : "(No Owner)";
            var ownerIcon = owner != null && owner.civData != null ? owner.civData.icon : null;
            herdLabelInstance.UpdateLabel(name, ownerName, ownerIcon, governor != null ? governor.Name : null);
            return;
        }

        if (nameTextLabel != null) nameTextLabel.text = name;
        // Owner: use owner.civData.civName if available
        if (ownerTextLabel != null)
            ownerTextLabel.text = owner != null && owner.civData != null ? owner.civData.civName : "(No Owner)";
        if (governorTextLabel != null) governorTextLabel.text = governor != null ? governor.Name : "(No Governor)";
        if (ownerIconImage != null && owner != null && owner.civData != null && owner.civData.icon != null)
            ownerIconImage.sprite = owner.civData.icon;
    }

    private void OnLabelClicked()
    {
        // Open HerdPanel if available
        UIManager.Instance?.ShowHerdPanelForHerd(this);
    }

    /// <summary>
    /// Move this herd to the specified tile index on the same planet if possible.
    /// Returns true if move succeeded.
    /// </summary>
    public bool MoveToTile(int tileIndex)
    {
        if (tileIndex < 0) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return false;

        var td = ts.GetTileData(tileIndex);
        if (td == null || !td.isPassable) return false;

        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
        {
            var existing = occ.GetOccupantObject(tileIndex, TileLayer.Surface);
            if (existing != null && existing != this.gameObject) return false; // occupied
        }

        // Clear old occupancy
        try { (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.ClearOccupant(currentTileIndex, TileLayer.Surface); } catch { }

        // Move transform to tile center
        try { transform.position = ts.GetTileCenterFlat(tileIndex); } catch { }

        currentTileIndex = tileIndex;

        try { (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(tileIndex, this.gameObject, TileLayer.Surface); } catch { }

        // Re-register with chunk manager for wrapping if present
        try
        {
            var planetGen = owner != null ? owner.GetPlanetGeneratorForIndex(planetIndex) ?? (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null)
                                       : (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null);
            var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGen);
            if (mgr != null) mgr.RegisterObjectForWrapAtTile(tileIndex, gameObject);
        }
        catch { }

        UpdateLabelUI();
        return true;
    }

    // ------------------------- Production (herd-local, city-like) -------------------------
    // Herd-local production queue entry
    public class ProdEntry {
        public ScriptableObject data;
        public int remainingPts;
        public int goldCost;
        public ResourceData[] requiredResources;
        public Biome[] requiredTerrains;

        public ProdEntry(ScriptableObject d, int prodCost, int gCost, ResourceData[] reqRes, Biome[] reqTerrains)
        {
            data = d;
            remainingPts = prodCost;
            goldCost = gCost;
            requiredResources = reqRes;
            requiredTerrains = reqTerrains;
        }

        public void Clamp() { if (remainingPts < 0) remainingPts = 0; }
    }

    // Herd-owned production queue (behaves like a city's production queue but is owned by the herd)
    public List<ProdEntry> productionQueue = new List<ProdEntry>();

    /// <summary>
    /// Compute this herd's production per turn. Sum of nearby tile production, attached building yields,
    /// and animal-contributed production (per-100 rules as configured by design).
    /// </summary>
    public int GetProductionPerTurn()
    {
        int total = 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null && currentTileIndex >= 0)
        {
            // current tile + neighbors
            var tiles = new List<int> { currentTileIndex };
            var neigh = ts.GetNeighbors(currentTileIndex);
            foreach (var n in neigh) tiles.Add(n);

            foreach (var t in tiles)
            {
                var td = ts.GetTileData(t);
                if (td == null) continue;
                var ty = td.GetTotalYield();
                total += ty.Production;
            }
        }

        // Add yields from attached herd buildings
        if (builtStructures != null)
        {
            foreach (var b in builtStructures) if (b != null) total += b.productionPerTurn;
        }

        // Animal contributions (per-100 rules); prefer explicit mapping from per-herd instance fields
        int chickenCount = 0, cowCount = 0, pigCount = 0, sheepCount = 0;
        foreach (var e in animals)
        {
            if (e == null) continue;
            switch (e.species)
            {
                case HerdSpecies.Chicken: chickenCount += e.count; break;
                case HerdSpecies.Cow: cowCount += e.count; break;
                case HerdSpecies.Pig: pigCount += e.count; break;
                case HerdSpecies.Sheep: sheepCount += e.count; break;
                default: break;
            }
        }

        // Per-design contributions per 100 animals
        // Cows: +2 production per 100
        total += (cowCount / 100) * 2;

        // Governor production bonus (flat added production per-turn)
        try
        {
            if (governor != null)
            {
                var gb = governor.GetTotalBonuses();
                total += gb.production;
            }
        }
        catch { }

        return Mathf.Max(0, total);
    }

    [System.Serializable]
    public struct AnimalYields { public int Food; public int Gold; public int Production; public int Science; public int Culture; public int Faith; public int Policy; }

    /// <summary>
    /// Returns per-turn yields contributed by the herd's animals (per-100 rules).
    /// Chickens: every 100 -> +2 Food, +1 Gold
    /// Cows:     every 100 -> +2 Production, +1 Gold
    /// Pigs:     every 100 -> +3 Food
    /// Sheep:    every 100 -> +1 Food, +2 Gold
    /// </summary>
    public AnimalYields GetAnimalYields()
    {
        int chickenCount = 0, cowCount = 0, pigCount = 0, sheepCount = 0;
        foreach (var e in animals)
        {
            if (e == null) continue;
            switch (e.species)
            {
                case HerdSpecies.Chicken: chickenCount += e.count; break;
                case HerdSpecies.Cow: cowCount += e.count; break;
                case HerdSpecies.Pig: pigCount += e.count; break;
                case HerdSpecies.Sheep: sheepCount += e.count; break;
                default: break;
            }
        }

        AnimalYields y = new AnimalYields();
        y.Food = (chickenCount / 100) * 2 + (pigCount / 100) * 3 + (sheepCount / 100) * 1;
        y.Gold = (chickenCount / 100) * 1 + (cowCount / 100) * 1 + (sheepCount / 100) * 2;
        y.Production = (cowCount / 100) * 2;
        y.Science = 0;
        y.Culture = 0;
        y.Faith = 0;
        y.Policy = 0;
        // Apply governor bonuses to herd yields (flat additions)
        try
        {
            if (governor != null)
            {
                var gb = governor.GetTotalBonuses();
                y.Food += gb.food;
                y.Gold += gb.gold;
                y.Production += gb.production; // stacked with per-100 cow production
                y.Science += gb.science;
                y.Culture += gb.culture;
                y.Faith += gb.faith;
                // Note: GovernorBonuses has no Policy field; if needed, add it to GovernorBonuses and persist traits
            }
        }
        catch { }
        return y;
    }

    /// <summary>
    /// Queue a production item (building, unit, etc.) into the herd's production queue.
    /// Validates requirements similar to `City.QueueProduction` but does not consume resources/gold immediately.
    /// </summary>
    public bool QueueProduction(ScriptableObject d)
    {
        if (d == null || owner == null) return false;

        if (d is BuildingData b)
        {
            if (!b.buildableByHerd) return false;
            if (!b.AreRequirementsMet(owner)) return false;
            // Check resource stockpile requirements
            if (b.requiredResources != null && b.requiredResources.Length > 0)
            {
                foreach (var r in b.requiredResources)
                {
                    if (owner.GetResourceCount(r) <= 0) return false;
                }
            }
            // Terrain requirements: ensure herd occupies or neighbors a tile matching required terrains
            if (b.requiredTerrains != null && b.requiredTerrains.Length > 0)
            {
                var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
                if (ts == null) return false;
                bool found = false;
                var tiles = new List<int>();
                if (currentTileIndex >= 0) { tiles.Add(currentTileIndex); foreach (var n in ts.GetNeighbors(currentTileIndex)) tiles.Add(n); }
                foreach (var t in tiles)
                {
                    var td = ts.GetTileData(t);
                    if (td == null) continue;
                    foreach (var req in b.requiredTerrains) { if (req == td.biome) { found = true; break; } }
                    if (found) break;
                }
                if (!found) return false;
            }

            var entry = new ProdEntry(b, b.productionCost, b.goldCost, b.requiredResources, b.requiredTerrains);
            productionQueue.Add(entry);
            return true;
        }

        // Other types (units/equipment) not supported for herds right now
        return false;
    }

    /// <summary>
    /// Process production for this herd once per turn: apply productionPerTurn to the front queue entry.
    /// </summary>
    public void ProcessProduction()
    {
        if (productionQueue == null || productionQueue.Count == 0) return;
        var entry = productionQueue[0];
        if (entry == null) return;

        int prod = GetProductionPerTurn();
        entry.remainingPts -= prod;
        entry.Clamp();
        if (entry.remainingPts <= 0)
        {
            // Complete the item
            if (entry.data is BuildingData bd)
            {
                // Instantiate prefab as child of herd for organization
                if (bd.buildingPrefab != null)
                {
                    var inst = Instantiate(bd.buildingPrefab, transform.position, Quaternion.identity);
                    inst.transform.SetParent(transform, true);
                }
                BuildStructure(bd);
            }
            productionQueue.RemoveAt(0);
        }
    }

    /// <summary>
    /// Cancel a queued production entry at the given index. Returns true if removed.
    /// Note: Queue entries do not consume resources at queue time in current design, so no refund is performed.
    /// </summary>
    public bool CancelProduction(int index)
    {
        if (productionQueue == null || index < 0 || index >= productionQueue.Count) return false;
        var entry = productionQueue[index];
        if (entry != null && owner != null)
        {
            // Refund gold if any
            try { if (entry.goldCost > 0) owner.AddGold(entry.goldCost); } catch { }
            // Refund one unit of each required resource (building requirements are single-unit checks in current design)
            try
            {
                if (entry.requiredResources != null)
                {
                    foreach (var r in entry.requiredResources)
                    {
                        if (r == null) continue;
                        try { owner.AddResource(r, 1); } catch { }
                    }
                }
            }
            catch { }
        }

        productionQueue.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Reorder production entries within the queue. Moves item at `fromIndex` to `toIndex`.
    /// </summary>
    public bool ReorderProduction(int fromIndex, int toIndex)
    {
        if (productionQueue == null) return false;
        if (fromIndex < 0 || fromIndex >= productionQueue.Count) return false;
        if (toIndex < 0 || toIndex >= productionQueue.Count) return false;
        if (fromIndex == toIndex) return true;
        var item = productionQueue[fromIndex];
        productionQueue.RemoveAt(fromIndex);
        productionQueue.Insert(toIndex, item);
        return true;
    }
}
