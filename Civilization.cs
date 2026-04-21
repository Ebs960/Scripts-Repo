// Assets/Scripts/Civs/Civilization.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public enum DiplomaticState
{
    War,
    Peace,
    Alliance,
    Vassal,
    Protected,
    Trade
}

/// <summary>
/// Represents one civilization's full runtime state: data, units, cities, research, culture, policies, government, yields, and relations.
/// </summary>
public class Civilization : MonoBehaviour
{
    [Header("Static Data")]
    public CivData civData { get; private set; }
    public LeaderData leader { get; private set; } // Added to store the active leader
    
    // Cache for unique unit and building replacements
    private Dictionary<CombatUnitData, CombatUnitData> uniqueUnitReplacements = new Dictionary<CombatUnitData, CombatUnitData>();
    private Dictionary<BuildingData, BuildingData> uniqueBuildingReplacements = new Dictionary<BuildingData, BuildingData>();
    
    // Performance caches
    private Dictionary<CombatUnitData, bool> _unitAvailabilityCache = new Dictionary<CombatUnitData, bool>();
    private Dictionary<WorkerUnitData, bool> _workerAvailabilityCache = new Dictionary<WorkerUnitData, bool>();
    private Dictionary<BuildingData, bool> _buildingAvailabilityCache = new Dictionary<BuildingData, bool>();
    private Dictionary<EquipmentData, bool> _equipmentAvailabilityCache = new Dictionary<EquipmentData, bool>();
    private bool _availabilityCacheDirty = true;

    // Equipment "can equip" cache (by unit archetype + equipment). This is used by Equipment UI filtering.
    // Key: (equipment instance id, unitType enum) packed into a long.
    private readonly Dictionary<long, bool> _canEquipByUnitTypeCache = new Dictionary<long, bool>();
    
    // Runtime property for diplomatic state access
    public bool isPlayerControlled = false;

    [Header("Map & Military")]
    // NOTE: Multi-planet gameplay requires planet-scoped ownership because tile indices repeat per planet.
    // Key = planetIndex, Value = owned tile indices on that planet.
    public Dictionary<int, HashSet<int>> ownedTilesByPlanet = new Dictionary<int, HashSet<int>>();
    public List<int> ownedTileIndices       = new List<int>();
    public List<City> cities                = new List<City>();
    [SerializeField] private City capitalCity;
    public List<CombatUnit> combatUnits     = new List<CombatUnit>();
    public List<WorkerUnit> workerUnits     = new List<WorkerUnit>();
    public City CapitalCity => capitalCity;
    [Header("Attrition Settings")]
    [Tooltip("Base HP damage applied to units each turn while the civilization is in famine (food <= 0).")]
    public int famineAttritionDamage = 1;

    /// <summary>Register a newly trained combat unit and fire the OnUnitTrained event.</summary>
    public void RegisterTrainedCombatUnit(CombatUnit unit)
    {
        if (unit == null) return;
        combatUnits.Add(unit);
        OnUnitTrained?.Invoke(this, unit.data);
    }

    // -------------------- Owned biome aggregates (multi-planet) --------------------
    // Motivation: Tech/Culture requirements used to scan ownedTilesByPlanet and then query tile data for each tile.
    // That becomes O(tiles) per check on large worlds. We maintain incremental counts instead.
    private bool _biomeControlCacheBuilt = false;
    private int _biomeEnumCount = -1;
    private readonly Dictionary<int, int[]> _ownedBiomeCountsByPlanet = new Dictionary<int, int[]>();
    private int[] _ownedBiomeCountsAnyPlanet; // index = (int)Biome, value = total owned tiles of that biome across all planets

    private bool CanAffordResourceCosts(ResourceCost[] costs)
    {
        if (costs == null || costs.Length == 0)
            return true;

        foreach (var cost in costs)
        {
            if (cost == null || cost.resource == null || cost.amount <= 0)
                continue;

            if (GetResourceCount(cost.resource) < cost.amount)
                return false;
        }

        return true;
    }

    private bool TryConsumeResourceCosts(ResourceCost[] costs)
    {
        if (!CanAffordResourceCosts(costs))
            return false;

        if (costs == null)
            return true;

        foreach (var cost in costs)
        {
            if (cost == null || cost.resource == null || cost.amount <= 0)
                continue;

            ConsumeResource(cost.resource, cost.amount);
        }

        return true;
    }

    private void ApplyPerTurnResourceUpkeep()
    {
        if (cities != null)
        {
            foreach (var city in cities)
                city?.ResetBuildingResourceUpkeepState();

            foreach (var city in cities)
            {
                if (city == null || city.builtBuildings == null)
                    continue;

                for (int i = 0; i < city.builtBuildings.Count; i++)
                {
                    var building = city.builtBuildings[i].data;
                    if (building == null)
                        continue;

                    bool satisfied = TryConsumeResourceCosts(building.resourceUpkeepPerTurn);
                    city.SetBuildingResourceUpkeepState(i, satisfied, building.upkeepFailureBehavior, building.upkeepFailureDebuffMultiplier);
                }
            }
        }

        if (combatUnits != null)
        {
            foreach (var unit in combatUnits)
            {
                if (unit == null)
                    continue;

                var data = unit.data;
                bool satisfied = data == null || TryConsumeResourceCosts(data.resourceUpkeepPerTurn);
                unit.SetResourceUpkeepState(satisfied,
                    data != null ? data.upkeepFailureBehavior : ResourceUpkeepFailureBehavior.Deactivate,
                    data != null ? data.upkeepFailureDebuffMultiplier : 1f);
            }
        }

        if (workerUnits != null)
        {
            foreach (var unit in workerUnits)
            {
                if (unit == null)
                    continue;

                var data = unit.data;
                bool satisfied = data == null || TryConsumeResourceCosts(data.resourceUpkeepPerTurn);
                unit.SetResourceUpkeepState(satisfied,
                    data != null ? data.upkeepFailureBehavior : ResourceUpkeepFailureBehavior.Deactivate,
                    data != null ? data.upkeepFailureDebuffMultiplier : 1f);
            }
        }
    }

    private void EnsureBiomeAggregateArrays()
    {
        if (_biomeEnumCount <= 0)
            _biomeEnumCount = System.Enum.GetValues(typeof(Biome)).Length;
        if (_ownedBiomeCountsAnyPlanet == null || _ownedBiomeCountsAnyPlanet.Length != _biomeEnumCount)
            _ownedBiomeCountsAnyPlanet = new int[_biomeEnumCount];
    }

    private int[] GetOrCreatePlanetBiomeCounts(int planetIndex)
    {
        EnsureBiomeAggregateArrays();
        if (!_ownedBiomeCountsByPlanet.TryGetValue(planetIndex, out var arr) || arr == null || arr.Length != _biomeEnumCount)
        {
            arr = new int[_biomeEnumCount];
            _ownedBiomeCountsByPlanet[planetIndex] = arr;
        }
        return arr;
    }

    private void RebuildOwnedBiomeAggregates()
    {
        EnsureBiomeAggregateArrays();
        _ownedBiomeCountsByPlanet.Clear();
        System.Array.Clear(_ownedBiomeCountsAnyPlanet, 0, _ownedBiomeCountsAnyPlanet.Length);

        if (ownedTilesByPlanet == null || ownedTilesByPlanet.Count == 0)
        {
            _biomeControlCacheBuilt = true;
            return;
        }

        foreach (var kv in ownedTilesByPlanet)
        {
            int planetIndex = kv.Key;
            var set = kv.Value;
            if (set == null || set.Count == 0) continue;

            var perPlanet = GetOrCreatePlanetBiomeCounts(planetIndex);

            // Prefer TileSystem (fast array lookup); fall back to PlanetGenerator if needed.
            var ts = TileSystem.GetForPlanet(planetIndex);
            PlanetGenerator gen = null;
            if (ts == null && GameManager.Instance != null) gen = GameManager.Instance.GetPlanetGenerator(planetIndex);

            foreach (int tileIndex in set)
            {
                HexTileData td = ts != null ? ts.GetTileData(tileIndex) : (gen != null ? gen.GetHexTileData(tileIndex) : null);
                if (td == null) continue;
                Biome biome = td.biome;

                int b = (int)biome;
                if (b < 0 || b >= _biomeEnumCount) continue;
                perPlanet[b]++;
                _ownedBiomeCountsAnyPlanet[b]++;
            }
        }

        _biomeControlCacheBuilt = true;
    }

    /// <summary>
    /// Returns true if this civ currently controls at least one tile of the given biome on any planet.
    /// Used by Tech/Culture prerequisite checks (avoids scanning owned tiles).
    /// </summary>
    public bool HasControlledBiome(Biome biome)
    {
        if (!_biomeControlCacheBuilt) RebuildOwnedBiomeAggregates();
        EnsureBiomeAggregateArrays();
        int b = (int)biome;
        if (b < 0 || b >= _ownedBiomeCountsAnyPlanet.Length) return false;
        return _ownedBiomeCountsAnyPlanet[b] > 0;
    }

    /// <summary>
    /// Determine a new herd display name for this civilization.
    /// Prefers an unused entry from `civData.herdNames` if available; otherwise falls back to a generated name.
    /// </summary>
    public string GetNewHerdName()
    {
        string civBase = civData != null && !string.IsNullOrEmpty(civData.civName) ? civData.civName : (name ?? "HerdOwner");
        var existing = herds != null ? herds.Select(h => string.IsNullOrEmpty(h.herdName) ? h.gameObject.name : h.herdName).ToList() : new List<string>();
        string fromList = civData?.herdNames?.FirstOrDefault(n => !existing.Contains(n));
        if (!string.IsNullOrEmpty(fromList)) return fromList;
        if (herds == null || herds.Count == 0)
            return civBase + " Herd 1";
        return civBase + " Herd " + (herds.Count + 1);
    }

    /// <summary>
    /// Incremental update hook used by TileSystem.SetTileOwner().
    /// Keeps owned biome aggregates in sync without rescanning the entire map.
    /// </summary>
    internal void NotifyOwnedTileBiomeChanged(int planetIndex, Biome biome, bool nowOwned)
    {
        // Ensure cache exists before applying deltas (avoids negative counts if we haven't built baseline yet).
        if (!_biomeControlCacheBuilt) RebuildOwnedBiomeAggregates();

        int b = (int)biome;
        EnsureBiomeAggregateArrays();
        if (b < 0 || b >= _biomeEnumCount) return;

        var perPlanet = GetOrCreatePlanetBiomeCounts(planetIndex);

        if (nowOwned)
        {
            perPlanet[b]++;
            _ownedBiomeCountsAnyPlanet[b]++;
        }
        else
        {
            // Defensive clamping: ownership can be noisy during generation/debug operations.
            perPlanet[b] = Mathf.Max(0, perPlanet[b] - 1);
            _ownedBiomeCountsAnyPlanet[b] = Mathf.Max(0, _ownedBiomeCountsAnyPlanet[b] - 1);
        }
    }
    
    [Header("Interplanetary Trade")]
    public List<TradeRoute> interplanetaryTradeRoutes = new List<TradeRoute>();
    
    [Header("Trade System")]
    [Tooltip("When true this civilization may initiate trade routes (set when adopting certain cultures)")]
    public bool tradeEnabled = false;
    
    [Header("Resources")]
    public Dictionary<ResourceData, int> resourceStockpile = new Dictionary<ResourceData, int>();
    
    [Header("Equipment Inventory")]
    // Track equipment availability - each civ has stockpiles of equipment
    public Dictionary<EquipmentData, int> equipmentInventory = new Dictionary<EquipmentData, int>();
    // Starting equipment to spawn with
    [SerializeField] private List<EquipmentData> startingEquipment = new List<EquipmentData>();
    [Tooltip("The base prefab used to create a new city. The City script on this prefab will handle spawning the correct visual model based on tech age.")]
    [SerializeField] private GameObject cityPrefab;
    
    [Header("Projectile Inventory")]
    // Track projectile availability - each civ has stockpiles of different projectile types
    public Dictionary<GameCombat.ProjectileData, int> projectileInventory = new Dictionary<GameCombat.ProjectileData, int>();
    // Event for projectile changes
    public System.Action<GameCombat.ProjectileData, int> OnProjectileChanged;

    [Header("Science & Technology")]
    public List<TechData> researchedTechs    = new List<TechData>();
    public TechData      currentTech;
    public float         currentTechProgress;
    public float scienceModifier = 0f; // Civilization-wide percentage bonus, starts at 0%
    // When true the civ started this research during the current turn and should
    // not receive science progress until the next turn (ensures minimum 1-turn duration)
    private bool researchStartedThisTurn = false;

    /// <summary>
    /// Called when research is started this turn to defer progress until next turn.
    /// </summary>
    public void MarkResearchStartedThisTurn()
    {
        researchStartedThisTurn = true;
    }
    // When true the civ started culture adoption this turn and should defer progress
    // until the next turn (ensures minimum 1-turn duration for culture adoption)
    private bool cultureStartedThisTurn = false;

    public void MarkCultureStartedThisTurn()
    {
        cultureStartedThisTurn = true;
    }

    [Header("Culture")]
    public List<CultureData> researchedCultures    = new List<CultureData>();
    public CultureData       currentCulture;
    public float             currentCultureProgress;
    public float cultureModifier = 0f; // Civilization-wide percentage bonus, starts at 0%

    [Header("Policy & Government")]
    public List<PolicyData>      unlockedPolicies       = new List<PolicyData>();
    public List<PolicyData>      activePolicies         = new List<PolicyData>();
    public List<GovernmentData>  unlockedGovernments    = new List<GovernmentData>();
    public GovernmentData        currentGovernment;

    [Header("Legacies")]
    [Tooltip("All legacies this civilization has ever earned from missions")]
    public List<LegacyData> earnedLegacies = new List<LegacyData>();
    [Tooltip("Currently promoted legacies (max governed by maxActiveLegacies)")]
    public List<LegacyData> activeLegacies = new List<LegacyData>();
    [Tooltip("Maximum number of legacies that can be promoted at once")]
    public int maxActiveLegacies = 3;

    [Header("Unrest & Famine")]
    [Tooltip("0–1 scale. Increases when at war, reduces loyalty city-wide.")]
    [Range(0f, 1f)]
    public float warWeariness = 0f;
    [Tooltip("% warWeariness added per war partner, per turn")]
    public float warWearinessPerWarTurn = 0.02f;
    [Tooltip("% warWeariness recovered per peace turn")]
    public float warWearinessRecoveryPerTurn = 0.01f;
    [HideInInspector] public bool famineActive = false;

    [Header("Diplomacy")]
    public Dictionary<Civilization, DiplomaticState> relations = new Dictionary<Civilization, DiplomaticState>();

    [Header("Yields & Storage")]
    public int gold;
    public int food;
    public int science;
    public int culture;
    public int policyPoints;
    public int faith;

    /// <summary>Cached per-turn yield rates computed by BeginTurn. UI can read these instead of recomputing.</summary>
    [HideInInspector] public int cachedGoldPerTurn;
    [HideInInspector] public int cachedFoodPerTurn;
    [HideInInspector] public int cachedSciencePerTurn;
    [HideInInspector] public int cachedCulturePerTurn;
    [HideInInspector] public int cachedPolicyPerTurn;
    [HideInInspector] public int cachedFaithPerTurn;
    [HideInInspector] public int cachedFoodConsumption;
    
    [Header("Herds")]
    public bool herdsEnabled = false; // set by techs/cultures when herd mechanic becomes available
    public List<Herd> herds = new List<Herd>();

    /// <summary>
    /// Add captured/purchased animals to the nearest herd owned by this civilization on the same planet.
    /// If no nearby herd is found within `maxSearchDistance`, a new Herd GameObject is created at `tileIndex`.
    /// </summary>
    public void AddAnimalsToNearestHerd(CombatUnitData type, int count, int planetIndex, int tileIndex, int maxSearchDistance = 10)
    {
        if (type == null || count <= 0) return;

        Herd best = null;
        int bestDist = int.MaxValue;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;

        foreach (var h in herds)
        {
            if (h == null) continue;
            if (h.planetIndex != planetIndex) continue;
            int d = int.MaxValue;
            try
            {
                if (ts != null && h.currentTileIndex >= 0 && tileIndex >= 0)
                    d = ts.GetTileDistance(h.currentTileIndex, tileIndex);
            }
            catch { }

            if (d < bestDist)
            {
                bestDist = d;
                best = h;
            }
        }

        if (best != null && bestDist <= maxSearchDistance)
        {
            best.AddAnimals(type, count);
            return;
        }

        // Create a new herd at the tile (use prefab if assigned)
        try
        {
            GameObject go;
            var spawnPos = (ts != null && tileIndex >= 0) ? ts.GetTileSurfacePosition(tileIndex) : Vector3.zero;
            var prefabToUse = (civData != null) ? civData.herdPrefab : null;
            if (prefabToUse == null)
            {
                Debug.LogWarning($"[Civilization] Cannot spawn herd: civData.herdPrefab is not assigned for {(civData != null ? civData.civName : name)}. Aborting spawn.");
                return;
            }
            go = Instantiate(prefabToUse, spawnPos, Quaternion.identity);

            var herd = go.GetComponent<Herd>() ?? go.AddComponent<Herd>();
            herd.owner = this;
                try { herd.herdName = GetNewHerdName(); } catch { }
                herd.planetIndex = planetIndex;
                herd.currentTileIndex = tileIndex;
                herd.AddAnimals(type, count);
        }
        catch { }
    }
    
    [Header("Consumption Settings")]
    [Tooltip("Minimum food stockpile (prevents going below zero with buffer)")]
    public int minimumFoodStockpile = -1;
    [Tooltip("Fallback food consumption for units without foodConsumptionPerTurn set")]
    public int defaultFoodPerCombatUnit = 2;
    [Tooltip("Fallback food consumption for workers without foodConsumptionPerTurn set")]
    public int defaultFoodPerWorkerUnit = 1;

    [Header("Modifiers")]
    public float attackBonus;
    public float defenseBonus;
    public float movementBonus;
    // Specific yield modifiers
    public float foodModifier;
    public float productionModifier;
    public float goldModifier;
    // scienceModifier and cultureModifier already exist in the [Header("Science & Technology")] and [Header("Culture")] sections.
    // They will be repurposed to be the civilization-wide percentage modifiers.
    public float faithModifier;

    [Header("Religion")]
    // Support multiple pantheons (spirits/gods). Key: the pantheon asset.
    public List<PantheonData> foundedPantheons = new List<PantheonData>();
    public ReligionData foundedReligion;
    public bool hasFoundedReligion;
    // Pantheons/beliefs unlocked by adopted cultures (in addition to global available list)
    public List<PantheonData> cultureUnlockedPantheons = new List<PantheonData>();
    public List<BeliefData> cultureUnlockedBeliefs = new List<BeliefData>();
    // Custom beliefs assigned via UI (one per BeliefCategory). These are additional active beliefs
    // that are not tied to a specific pantheon or founded religion.
    public List<BeliefData> customAssignedBeliefs = new List<BeliefData>();

    [Header("Pantheon Limits")]
    [Tooltip("Base maximum number of pantheons this civilization may found (default 1).")]
    public int basePantheonCap = 1;
    [Tooltip("Additional pantheon capacity gained from techs/cultures/policies (computed at runtime)")]
    public int pantheonCapFromBonuses = 0;

    public int CurrentPantheonCap => Mathf.Max(0, basePantheonCap + pantheonCapFromBonuses);

    public bool CanFoundMorePantheons()
    {
    // Count actual founded pantheons
    int owned = (foundedPantheons != null) ? foundedPantheons.Count : 0;
        return owned < CurrentPantheonCap;
    }


    [Header("Unlocked Units")]
    public List<CombatUnitData> unlockedCombatUnits = new List<CombatUnitData>();
    public List<WorkerUnitData> unlockedWorkerUnits = new List<WorkerUnitData>();
    public List<BuildingData> unlockedBuildings = new List<BuildingData>();
    public List<AbilityData> unlockedAbilities = new List<AbilityData>();
    // Unlocked religions are not stored here; ReligionManager handles availability.

    // Events for UI or other systems
    public event Action<Civilization,int> OnTurnStarted;  // civ, round
    public event Action<CultureData>        OnCultureCompleted;
    public event System.Action<ResourceData, int> OnResourceChanged;
    // Yield change events for immediate UI updates
    public event System.Action<int,int> OnFoodChanged; // (newAmount, delta)
    public event System.Action<int,int> OnGoldChanged;
    public event System.Action<int,int> OnFaithChanged;
    public event System.Action<int,int> OnPolicyPointsChanged;
    // Add equipment event
    public event System.Action<EquipmentData, int> OnEquipmentChanged;
    public event Action<TechData> OnTechStarted;
    public event Action<CultureData> OnCultureStarted;
    public event Action<TechData> OnTechResearched;  // The event
    // Fired after research/culture changes that may affect availability (units/buildings/improvements)
    public event Action OnUnlocksChanged;
    public event Action OnBeliefsChanged;
    // Mission-system hooks
    public event Action<Civilization, PolicyData> OnPolicyAdopted;
    public event Action<Civilization, GovernmentData> OnGovernmentChanged;
    public event Action<Civilization, City> OnCityFounded;
    public event Action<Civilization, PantheonData> OnPantheonFounded;
    public event Action<Civilization, CombatUnitData> OnUnitTrained;

    private int turnCount;

    // --- Governor System ---
    public int governorCount = 1; // Number of governors this civ can create (modifiable by events, policies, etc.)
    public List<Governor> governors = new List<Governor>(); // All created governors
    // Whether this civilization has the governor feature unlocked (via cultures/policies/tech)
    [Tooltip("If true this civilization may create and assign governors.")]
    public bool governorsEnabled = false;

    // List of governor traits this civ has unlocked (for trait assignment UI)
    public List<GovernorTrait> unlockedGovernorTraits = new List<GovernorTrait>();

    // --- Royal Council ---
    /// <summary>Governors currently seated on the royal council.</summary>
    public List<Governor> royalCouncil = new List<Governor>();

    // --- Noble Factions ---
    /// <summary>Active noble factions that have formed within this civilization.</summary>
    public List<FactionBloc> nobleFactions = new List<FactionBloc>();
    
    [Header("City Cap")]
    [Tooltip("Base maximum number of cities this civilization may own. Set to 0 for Paleolithic nomads.")]
    [SerializeField] private int baseCityCap = 0;
    [Tooltip("Additional city capacity gained from technologies, policies, etc. Computed at runtime.")]
    [SerializeField] private int cityCapFromBonuses = 0;
    /// <summary>
    /// Current max cities allowed = base + bonuses. Default 0 so early ages are nomadic.
    /// </summary>
    public int CurrentCityCap => Mathf.Max(0, baseCityCap + cityCapFromBonuses);
    /// <summary>
    /// Returns true if this civ may found/own another city given the cap.
    /// </summary>
    public bool CanFoundMoreCities() => cities == null || cities.Count < CurrentCityCap;

    public int GetCityCapBonusForSave() => cityCapFromBonuses;

    public void RestoreProgressionState(
        List<TechData> restoredTechs,
        TechData restoredCurrentTech,
        float restoredCurrentTechProgress,
        List<CultureData> restoredCultures,
        CultureData restoredCurrentCulture,
        float restoredCurrentCultureProgress,
        bool restoredTradeEnabled,
        bool restoredGovernorsEnabled,
        int restoredGovernorCount,
        int restoredCityCapFromBonuses,
        int restoredPantheonCapFromBonuses,
        float restoredAttackBonus,
        float restoredDefenseBonus,
        float restoredMovementBonus,
        float restoredFoodModifier,
        float restoredProductionModifier,
        float restoredGoldModifier,
        float restoredScienceModifier,
        float restoredCultureModifier,
        float restoredFaithModifier,
        List<GovernorTrait> restoredUnlockedGovernorTraits,
        List<PantheonData> restoredPantheons,
        List<BeliefData> restoredBeliefs,
        List<BeliefData> restoredCustomBeliefs)
    {
        researchedTechs = restoredTechs ?? new List<TechData>();
        currentTech = restoredCurrentTech;
        currentTechProgress = restoredCurrentTechProgress;

        researchedCultures = restoredCultures ?? new List<CultureData>();
        currentCulture = restoredCurrentCulture;
        currentCultureProgress = restoredCurrentCultureProgress;

        tradeEnabled = restoredTradeEnabled;
        governorsEnabled = restoredGovernorsEnabled;
        governorCount = restoredGovernorCount;
        cityCapFromBonuses = restoredCityCapFromBonuses;
        pantheonCapFromBonuses = restoredPantheonCapFromBonuses;

        attackBonus = restoredAttackBonus;
        defenseBonus = restoredDefenseBonus;
        movementBonus = restoredMovementBonus;
        foodModifier = restoredFoodModifier;
        productionModifier = restoredProductionModifier;
        goldModifier = restoredGoldModifier;
        scienceModifier = restoredScienceModifier;
        cultureModifier = restoredCultureModifier;
        faithModifier = restoredFaithModifier;

        unlockedGovernorTraits = restoredUnlockedGovernorTraits ?? new List<GovernorTrait>();
        cultureUnlockedPantheons = restoredPantheons ?? new List<PantheonData>();
        cultureUnlockedBeliefs = restoredBeliefs ?? new List<BeliefData>();
        customAssignedBeliefs = restoredCustomBeliefs ?? new List<BeliefData>();

        RecalculateCivilizationModifiers();
        RefreshUnlockedContentLists();

        InvalidateAvailabilityCache();
        UpdateCityModelsForNewAge();

        try
        {
            if (combatUnits != null)
                foreach (var u in combatUnits)
                    if (u != null) u.OnCivBonusesChanged();
            if (workerUnits != null)
                foreach (var w in workerUnits)
                    if (w != null) w.OnCivBonusesChanged();
            if (cities != null)
                foreach (var c in cities)
                    if (c != null)
                    {
                        c.RefreshGovernorBonuses();
                        c.UpdateAvailableBuildings();
                    }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Civilization] RestoreProgressionState refresh threw: {ex}");
        }

        OnUnlocksChanged?.Invoke();
        OnBeliefsChanged?.Invoke();
    }

    /// <summary>
    /// Returns true if this civilization has researched at least one technology
    /// whose TechAge is at or after the given age.
    /// </summary>
    public bool HasReachedTechAge(TechAge age)
    {
        if (researchedTechs == null || researchedTechs.Count == 0) return false;
        foreach (var t in researchedTechs)
        {
            if (t == null) continue;
            if (t.techAge >= age) return true;
        }
        return false;
    }

    public bool HasMosquitoImmunityTechnology()
    {
        if (researchedTechs == null || researchedTechs.Count == 0) return false;
        foreach (var tech in researchedTechs)
        {
            if (tech != null && tech.preventsMosquitoDamage)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the maximum number of units that can share a single tile for this civ.
    /// Default is 1 (no stacking). Techs with unlocksUnitStacking raise this to 2 or 3.
    /// </summary>
    public int GetMaxStackSize()
    {
        if (researchedTechs == null || researchedTechs.Count == 0) return 1;
        int max = 1;
        foreach (var tech in researchedTechs)
        {
            if (tech != null && tech.unlocksUnitStacking)
                max = Mathf.Max(max, tech.unitStackSizeGranted);
        }
        return max;
    }
    

    // Increase the number of governors this civ can create
    public void IncreaseGovernorCount(int amount = 1)
    {
        governorCount += amount;
    }

    // Create a new governor if there is an available slot
    public Governor CreateGovernor(string name, Governor.Specialization specialization)
    {
    if (!governorsEnabled) return null;
    if (governors.Count >= governorCount)
            return null; // No available slots
        int newId = governors.Count > 0 ? governors[governors.Count - 1].Id + 1 : 1;
        var gov = new Governor(newId, name, specialization);
        gov.AssignRandomPersonality();
        governors.Add(gov);
        return gov;
    }

    // Assign a governor to a city (removes from previous city if needed)
    public bool AssignGovernorToCity(Governor governor, City city)
    {
    if (!governorsEnabled) return false;
        if (governor == null || city == null) return false;

        // If the city already has a governor, anger them for the reassignment
        if (city.governor != null && city.governor != governor)
        {
            city.governor.AddGrievance(GrievanceSource.CityReassigned);
            city.governor.Cities.Remove(city);
        }

        // Remove this city from any other governor who has it listed
        foreach (var c in governors.SelectMany(g => g.Cities).ToList())
        {
            if (c == city)
            {
                c.governor = null;
                governor.Cities.Remove(c);
            }
        }
        // Assign
        city.governor = governor;
        if (!governor.Cities.Contains(city))
            governor.Cities.Add(city);

        // Refresh eligibility now that domain size changed
        governor.RefreshCouncilEligibility();
        return true;
    }

    // Remove a governor from a city
    public bool RemoveGovernorFromCity(Governor governor, City city)
    {
        if (governor == null || city == null) return false;
        if (city.governor == governor)
        {
            city.governor = null;
            governor.Cities.Remove(city);
            return true;
        }
        return false;
    }

    // Assign a governor to a herd (removes from previous herd if needed)
    public bool AssignGovernorToHerd(Governor governor, Herd herd)
    {
        if (!governorsEnabled) return false;
        if (governor == null || herd == null) return false;
        // Remove from any previous herd assignments
        // Remove this herd from any governor that currently references it
        foreach (var g in governors)
        {
            if (g == null) continue;
            if (g.Herds.Contains(herd))
            {
                g.Herds.Remove(herd);
                herd.governor = null;
            }
        }
        // Assign
        herd.governor = governor;
        if (!governor.Herds.Contains(herd)) governor.Herds.Add(herd);
        // Notify the herd to apply governor bonuses
        try { herd.RefreshGovernorBonuses(); } catch { }
        return true;
    }

    // Remove a governor from a herd
    public bool RemoveGovernorFromHerd(Governor governor, Herd herd)
    {
        if (governor == null || herd == null) return false;
        if (herd.governor == governor)
        {
            herd.governor = null;
            governor.Herds.Remove(herd);
            try { herd.RefreshGovernorBonuses(); } catch { }
            return true;
        }
        return false;
    }

    // Get all cities in a province (all cities assigned to a governor)
    public List<City> GetProvinceCities(Governor governor)
    {
        return governor?.Cities ?? new List<City>();
    }

    // Get all herds assigned to a governor
    public List<Herd> GetGovernorHerds(Governor governor)
    {
        return governor?.Herds ?? new List<Herd>();
    }

    // ── Royal Council ─────────────────────────────────────────────────────────

    /// <summary>Maximum council seats allowed by the current government type.</summary>
    public int MaxCouncilSeats => currentGovernment != null ? currentGovernment.councilSeatCount : 0;

    /// <summary>
    /// Which domains can the council currently veto?
    /// Returns None if there are no seated councillors.
    /// </summary>
    public VetoDomain ActiveVetoDomains => (royalCouncil.Count > 0 && currentGovernment != null)
        ? currentGovernment.councilVetoDomains : VetoDomain.None;

    /// <summary>
    /// Returns true if the council currently holds a veto over the given domain.
    /// </summary>
    public bool HasCouncilVeto(VetoDomain domain)
        => (ActiveVetoDomains & domain) != VetoDomain.None;

    /// <summary>
    /// Seat a governor on the royal council, if a slot is available.
    /// Clears any CouncilSeatDenied grievance on success.
    /// </summary>
    public bool AddToCouncil(Governor gov)
    {
        if (gov == null || royalCouncil.Contains(gov)) return false;
        if (royalCouncil.Count >= MaxCouncilSeats) return false;

        royalCouncil.Add(gov);
        gov.IsOnCouncil = true;
        gov.ClearGrievance(GrievanceSource.CouncilSeatDenied);
        gov.AddOpinionModifier("Granted Council Seat", 20f, -1);

        // If this governor was in a faction, they may leave it now
        gov.Faction?.RemoveMember(gov);
        return true;
    }

    /// <summary>Remove a governor from the royal council.</summary>
    public bool RemoveFromCouncil(Governor gov)
    {
        if (gov == null || !royalCouncil.Contains(gov)) return false;
        royalCouncil.Remove(gov);
        gov.IsOnCouncil = false;
        gov.AddGrievance(GrievanceSource.TitleRevoked);
        return true;
    }

    /// <summary>
    /// Returns all governors who are eligible for a council seat but not currently seated.
    /// Useful for generating "powerful lord not on council" unrest penalties.
    /// </summary>
    public List<Governor> GetUnseatedPowerfulLords()
    {
        var result = new List<Governor>();
        foreach (var gov in governors)
        {
            if (gov == null || gov.IsOnCouncil) continue;
            gov.RefreshCouncilEligibility();
            if (gov.IsCouncilEligible) result.Add(gov);
        }
        return result;
    }

    /// <summary>
    /// Apply ongoing "powerful lord not on council" opinion penalties.
    /// Call once per turn from BeginTurn. Unseated eligible lords drift negative faster.
    /// </summary>
    public void ProcessCouncilPressure()
    {
        if (MaxCouncilSeats <= 0) return;
        foreach (var gov in GetUnseatedPowerfulLords())
        {
            // Powerful lords not on council add a CouncilSeatDenied grievance each time seat stays unfilled
            // (only re-add every 10 turns to avoid spam)
            if (!gov.Grievances.ContainsKey(GrievanceSource.CouncilSeatDenied))
                gov.AddGrievance(GrievanceSource.CouncilSeatDenied);
        }
    }

    // ── Noble Factions ────────────────────────────────────────────────────────

    /// <summary>
    /// Per-turn faction logic: form new factions, invite angry lords, generate demands.
    /// Call from BeginTurn after governor opinion ticks have run.
    /// </summary>
    public void ProcessFactionTick(int currentTurn)
    {
        // 1. Invite unaffiliated angry lords into existing or new factions
        foreach (var gov in governors)
        {
            if (gov == null || gov.IsOnCouncil || gov.Faction != null) continue;
            if (gov.Opinion > 10f) continue;  // Content lords don't join blocs

            // Try existing faction
            FactionBloc joined = null;
            foreach (var bloc in nobleFactions)
            {
                if (bloc.CanJoin(gov)) { bloc.AddMember(gov); joined = bloc; break; }
            }

            // Found a new faction if no existing one fits and this lord is angry enough
            if (joined == null && gov.AmbitionScore > 50 && gov.Opinion < -10f)
            {
                var alignment = DetermineAlignment(gov);
                string name = $"The {gov.Name} Faction";
                var newBloc = new FactionBloc(name, alignment, gov);
                nobleFactions.Add(newBloc);
            }
        }

        // 2. Dissolve factions that have too few members or are no longer angry
        for (int i = nobleFactions.Count - 1; i >= 0; i--)
        {
            var bloc = nobleFactions[i];
            if (bloc.Members.Count == 0 ||
                (bloc.Members.Count == 1 && bloc.Leader?.Opinion > 30f))
            {
                foreach (var m in bloc.Members) m.Faction = null;
                nobleFactions.RemoveAt(i);
            }
        }

        // 3. Factions with high power generate demands
        foreach (var bloc in nobleFactions)
        {
            if (bloc.ActiveDemands.Count == 0 && bloc.ComputePower() > 10f)
                bloc.GenerateDemand(currentTurn);
        }
    }

    /// <summary>
    /// Accept or refuse a faction demand. Refusal may trigger multi-city rebellion.
    /// </summary>
    public bool ResolveFactionDemand(FactionBloc bloc, FactionDemand demand, bool accepted, int currentTurn)
    {
        if (bloc == null || demand == null) return false;

        // Handle accepted demands mechanically
        if (accepted)
        {
            switch (demand.type)
            {
                case FactionDemandType.GrantCouncilSeat:
                    if (bloc.Leader != null) AddToCouncil(bloc.Leader);
                    break;
                case FactionDemandType.ReduceTaxation:
                    // Symbolic concession: give a temporary opinion boost
                    foreach (var m in bloc.Members)
                        m.AddOpinionModifier("Tax Concession Granted", 10f, 15);
                    break;
                case FactionDemandType.GrantReligiousFreedom:
                    foreach (var m in bloc.Members)
                    {
                        m.ClearGrievance(GrievanceSource.ReligionForced);
                        m.AddOpinionModifier("Religious Freedom Granted", 15f, 25);
                    }
                    break;
                case FactionDemandType.AdoptPolicy:
                    if (demand.targetPolicy != null && PolicyManager.Instance != null)
                        PolicyManager.Instance.AdoptPolicy(this, demand.targetPolicy);
                    break;
                case FactionDemandType.ChangeGovernment:
                    if (demand.targetGovernment != null && PolicyManager.Instance != null)
                        PolicyManager.Instance.ChangeGovernment(this, demand.targetGovernment);
                    break;
            }
        }

        return bloc.ResolveDemand(demand, accepted, this, currentTurn);
    }

    private static FactionAlignment DetermineAlignment(Governor gov)
    {
        if (gov.HasPersonality(PersonalityTrait.Zealous))    return FactionAlignment.Religious;
        if (gov.specialization == Governor.Specialization.Economic) return FactionAlignment.Mercantile;
        if (gov.AmbitionScore > 70 && gov.Opinion < -30f)   return FactionAlignment.Separatist;
        if (gov.HasPersonality(PersonalityTrait.Ambitious))  return FactionAlignment.Independent;
        return FactionAlignment.Conservative;
    }

    // ── Policy / Government reaction hooks ───────────────────────────────────

    /// <summary>
    /// Push governor opinion reactions for all active policy effects.
    /// Called by PolicyManager after adoption/government-change.
    /// </summary>
    public void ApplyGovernorPoliticalReactions(GovernorOpinionEffect[] effects)
    {
        PolicyManager.Instance?.ApplyGovernorPoliticalReactions(this, effects);
    }

    void Awake()
    {
        // Initialize the leader-specific unique units and buildings
        if (leader != null)
        {
            InitializeLeaderUniques();
        }
    }

    /// <summary>
    /// Attach a building to the nearest herd owned by this civ on the same planet.
    /// If none found within `maxSearchDistance`, create a new herd at the tile and attach the building.
    /// </summary>
    public void AddStructureToNearestHerd(BuildingData building, int planetIndex, int tileIndex, int maxSearchDistance = 10)
    {
        if (building == null) return;

        Herd best = null;
        int bestDist = int.MaxValue;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;

        foreach (var h in herds)
        {
            if (h == null) continue;
            if (h.planetIndex != planetIndex) continue;
            int d = int.MaxValue;
            try
            {
                if (ts != null && h.currentTileIndex >= 0 && tileIndex >= 0)
                    d = ts.GetTileDistance(h.currentTileIndex, tileIndex);
            }
            catch { }

            if (d < bestDist)
            {
                bestDist = d;
                best = h;
            }
        }

        if (best != null && bestDist <= maxSearchDistance)
        {
            best.BuildStructure(building);
            return;
        }

        try
        {
            GameObject go;
            var spawnPos = (ts != null && tileIndex >= 0) ? ts.GetTileSurfacePosition(tileIndex) : Vector3.zero;
            var prefabToUse = (civData != null) ? civData.herdPrefab : null;
            if (prefabToUse == null)
            {
                Debug.LogWarning($"[Civilization] Cannot spawn herd: civData.herdPrefab is not assigned for {(civData != null ? civData.civName : name)}. Aborting spawn.");
                return;
            }
            go = Instantiate(prefabToUse, spawnPos, Quaternion.identity);

            var herd = go.GetComponent<Herd>() ?? go.AddComponent<Herd>();
            herd.owner = this;
            try { herd.herdName = GetNewHerdName(); } catch { }
            herd.planetIndex = planetIndex;
            herd.currentTileIndex = tileIndex;
            herd.BuildStructure(building);
        }
        catch { }
    }

    // Backwards-compatible overload accepting WorkerUnitData (some code paths may pass worker data when converting captures).
    public void AddAnimalsToNearestHerd(WorkerUnitData type, int count, int planetIndex, int tileIndex, int maxSearchDistance = 10)
    {
        if (type == null || count <= 0) return;
        // If WorkerUnitData specifies a capture species, convert and add directly to nearest herd
        try
        {
            var s = type.captureSpecies;
            if (s != Herd.HerdSpecies.Other)
            {
                Herd best = null;
                int bestDist = int.MaxValue;
                var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;

                foreach (var h in herds)
                {
                    if (h == null) continue;
                    if (h.planetIndex != planetIndex) continue;
                    int d = int.MaxValue;
                    try
                    {
                        if (ts != null && h.currentTileIndex >= 0 && tileIndex >= 0)
                            d = ts.GetTileDistance(h.currentTileIndex, tileIndex);
                    }
                    catch { }

                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = h;
                    }
                }

                if (best != null && bestDist <= maxSearchDistance)
                {
                    best.AddAnimals(s, count);
                    return;
                }

                // Create new herd if none nearby
                try
                {
                    GameObject go;
                    var spawnPos = (ts != null && tileIndex >= 0) ? ts.GetTileSurfacePosition(tileIndex) : Vector3.zero;
                    var prefabToUse = (civData != null) ? civData.herdPrefab : null;
                    if (prefabToUse == null)
                    {
                        Debug.LogWarning($"[Civilization] Cannot spawn herd: civData.herdPrefab is not assigned for {(civData != null ? civData.civName : name)}. Aborting spawn.");
                        return;
                    }
                    go = Instantiate(prefabToUse, spawnPos, Quaternion.identity);

                    var herd = go.GetComponent<Herd>() ?? go.AddComponent<Herd>();
                    herd.owner = this;
                    try { herd.herdName = GetNewHerdName(); } catch { }
                    herd.planetIndex = planetIndex;
                    herd.currentTileIndex = tileIndex;
                    herd.AddAnimals(s, count);
                }
                catch { }
                return;
            }
        }
        catch { }

        Debug.LogWarning("[Civilization] AddAnimalsToNearestHerd called with WorkerUnitData — no animal conversion implemented.");
    }

    void Start()
    {
        // Seed starting techs
        if (civData.startingTechs != null)
            researchedTechs.AddRange(civData.startingTechs);

        // Seed starting cultures.
        if (civData.startingCultures != null)
            researchedCultures.AddRange(civData.startingCultures);
        else if (civData.cultureBonuses != null)
            researchedCultures.AddRange(civData.cultureBonuses);

        // Seed starting policies
        if (civData.startingPolicies != null)
        {
            unlockedPolicies.AddRange(civData.startingPolicies);
            activePolicies.AddRange(civData.startingPolicies);
        }
        
        // Initialize equipment inventory with starting equipment
        if (startingEquipment != null && startingEquipment.Count > 0)
        {
            foreach (var equipment in startingEquipment)
            {
                AddEquipment(equipment, 5); // Start with 5 of each equipment
            }
        }
        
        RecalculateCivilizationModifiers();

        // Register with the turn order (only if CivilizationManager exists)
        if (CivilizationManager.Instance != null)
        {
            CivilizationManager.Instance.RegisterCiv(this);
        }

        // If loading from a save or starting with pre-researched cultures, ensure governorsEnabled reflects those cultures
        if (!governorsEnabled && researchedCultures != null)
        {
            foreach (var cult in researchedCultures)
            {
                if (cult != null && cult.enablesGovernors)
                {
                    governorsEnabled = true;
                    break;
                }
            }
        }

        RefreshUnlockedContentLists();
    }
    
    /// <summary>
    /// Initialize dictionaries to map standard units and buildings to their unique replacements
    /// </summary>
    private void InitializeLeaderUniques()
    {
        // Clear existing dictionaries
        uniqueUnitReplacements.Clear();
        uniqueBuildingReplacements.Clear();

        if (leader == null || leader.uniqueUnits == null) return;

        // Process unique units from the leader
        foreach (var uniqueUnitDef in leader.uniqueUnits)
        {
            if (uniqueUnitDef != null && uniqueUnitDef.replacesUnit != null && uniqueUnitDef.uniqueUnit != null)
            {
                uniqueUnitReplacements[uniqueUnitDef.replacesUnit] = uniqueUnitDef.uniqueUnit;
            }
        }

        if (leader.uniqueBuildings == null) return;

        // Process unique buildings from the leader
        foreach (var uniqueBuildingDef in leader.uniqueBuildings)
        {
            if (uniqueBuildingDef != null && uniqueBuildingDef.replacesBuilding != null && uniqueBuildingDef.uniqueBuilding != null)
            {
                uniqueBuildingReplacements[uniqueBuildingDef.replacesBuilding] = uniqueBuildingDef.uniqueBuilding;
            }
        }
    }

    private void RefreshUnlockedContentLists()
    {
        // Preserve any inspector-serialized entries so they aren't lost at runtime
        var prefabCombatEntries = unlockedCombatUnits != null ? new List<CombatUnitData>(unlockedCombatUnits) : new List<CombatUnitData>();
        var prefabWorkerEntries = unlockedWorkerUnits != null ? new List<WorkerUnitData>(unlockedWorkerUnits) : new List<WorkerUnitData>();
        var prefabBuildingEntries = unlockedBuildings != null ? new List<BuildingData>(unlockedBuildings) : new List<BuildingData>();
        var pantheonCombatEntries = GetPantheonGrantedCombatUnits();
        var pantheonWorkerEntries = GetPantheonGrantedWorkerUnits();
        var pantheonBuildingEntries = GetPantheonGrantedBuildings();

        unlockedCombatUnits.Clear();
        unlockedWorkerUnits.Clear();
        unlockedBuildings.Clear();

        var seenCombat = new HashSet<CombatUnitData>();
        foreach (var baseUnit in ResourceCache.GetAllCombatUnits())
        {
            if (baseUnit == null || !baseUnit.AreRequirementsMet(this)) continue;
            var resolvedUnit = GetUnitData(baseUnit);
            if (resolvedUnit == null || seenCombat.Contains(resolvedUnit)) continue;
            seenCombat.Add(resolvedUnit);
            unlockedCombatUnits.Add(resolvedUnit);
        }

        // Merge any prefab-specified combat units that weren't included by the resource scan
        if (prefabCombatEntries != null)
        {
            foreach (var pref in prefabCombatEntries)
            {
                if (pref == null) continue;
                if (seenCombat.Contains(pref)) continue;
                seenCombat.Add(pref);
                unlockedCombatUnits.Add(pref);
            }
        }

        foreach (var bonusUnit in pantheonCombatEntries)
        {
            if (bonusUnit == null || seenCombat.Contains(bonusUnit)) continue;
            seenCombat.Add(bonusUnit);
            unlockedCombatUnits.Add(bonusUnit);
        }

        var seenWorkers = new HashSet<WorkerUnitData>();
        foreach (var workerUnit in ResourceCache.GetAllWorkerUnits())
        {
            if (workerUnit == null || !workerUnit.AreRequirementsMet(this) || seenWorkers.Contains(workerUnit)) continue;
            seenWorkers.Add(workerUnit);
            unlockedWorkerUnits.Add(workerUnit);
        }

        // Merge any prefab-specified worker units that weren't included by the resource scan
        if (prefabWorkerEntries != null)
        {
            foreach (var pref in prefabWorkerEntries)
            {
                if (pref == null) continue;
                if (seenWorkers.Contains(pref)) continue;
                seenWorkers.Add(pref);
                unlockedWorkerUnits.Add(pref);
            }
        }

        foreach (var bonusWorker in pantheonWorkerEntries)
        {
            if (bonusWorker == null || seenWorkers.Contains(bonusWorker)) continue;
            seenWorkers.Add(bonusWorker);
            unlockedWorkerUnits.Add(bonusWorker);
        }

        var seenBuildings = new HashSet<BuildingData>();
        foreach (var building in ResourceCache.GetAllBuildings())
        {
            if (building == null || !building.AreRequirementsMet(this) || seenBuildings.Contains(building)) continue;
            seenBuildings.Add(building);
            unlockedBuildings.Add(building);
        }

        // Merge any prefab-specified buildings that weren't included by the resource scan
        if (prefabBuildingEntries != null)
        {
            foreach (var pref in prefabBuildingEntries)
            {
                if (pref == null) continue;
                if (seenBuildings.Contains(pref)) continue;
                seenBuildings.Add(pref);
                unlockedBuildings.Add(pref);
            }
        }

        foreach (var bonusBuilding in pantheonBuildingEntries)
        {
            if (bonusBuilding == null || seenBuildings.Contains(bonusBuilding)) continue;
            seenBuildings.Add(bonusBuilding);
            unlockedBuildings.Add(bonusBuilding);
        }
    }
    
    /// <summary>
    /// Apply leader's bonus modifiers
    /// </summary>
    private void ApplyLeaderBonuses()
    {
        if (leader == null) return;

        // Apply leader-specific modifiers
        attackBonus += leader.militaryStrengthModifier;
        goldModifier += leader.goldModifier;
        scienceModifier += leader.scienceModifier;
        productionModifier += leader.productionModifier;
        foodModifier += leader.foodModifier;
        cultureModifier += leader.cultureModifier;
        faithModifier += leader.faithModifier;
    }

    public IEnumerable<PantheonBonuses> EnumeratePantheonBonuses()
    {
        if (foundedPantheons == null)
            yield break;

        foreach (var pantheon in foundedPantheons)
        {
            if (pantheon?.bonuses == null)
                continue;

            yield return pantheon.bonuses;
        }
    }

    public IEnumerable<BeliefData> EnumerateActiveBeliefs()
    {
        // Yield any custom assigned beliefs (UI-applied)
        if (customAssignedBeliefs != null)
        {
            foreach (var cb in customAssignedBeliefs)
                if (cb != null) yield return cb;
        }
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

    private bool MatchesTerritoryRequirement(HexTileData tile, UnitTerritoryRequirement requirement)
    {
        if (requirement == UnitTerritoryRequirement.Any)
            return true;
        if (tile == null)
            return false;

        var tileOwner = tile.owner;
        switch (requirement)
        {
            case UnitTerritoryRequirement.Owned:
                return tileOwner == this;
            case UnitTerritoryRequirement.Unowned:
                return tileOwner == null;
            case UnitTerritoryRequirement.Enemy:
                return tileOwner != null && tileOwner != this && DiplomacyManager.Instance != null
                    ? DiplomacyManager.Instance.GetRelationship(this, tileOwner) == DiplomaticState.War
                    : tileOwner != null && tileOwner != this && relations.TryGetValue(tileOwner, out var enemyState) && enemyState == DiplomaticState.War;
            case UnitTerritoryRequirement.Friendly:
                if (tileOwner == null || tileOwner == this) return false;
                if (DiplomacyManager.Instance != null)
                    return DiplomacyManager.Instance.GetRelationship(this, tileOwner) != DiplomaticState.War;
                return !relations.TryGetValue(tileOwner, out var friendlyState) || friendlyState != DiplomaticState.War;
            default:
                return true;
        }
    }

    private bool MatchesUnitStatBonusLocation(BaseUnit unit, BoolRequirement cityRequirement, bool useBiomeFilter, Biome biome,
        BoolRequirement hillRequirement, BoolRequirement mountainRequirement, bool useResourceFilter, ResourceData resource,
        UnitTerritoryRequirement territoryRequirement)
    {
        if (unit == null) return false;

        HexTileData tile = null;
        if (unit.currentTileIndex >= 0)
        {
            var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
            if (ts != null && ts.IsReady())
                tile = ts.GetTileData(unit.currentTileIndex);
        }

        bool inCity = tile != null && tile.HasCity;
        if (!MatchesRequirement(cityRequirement, inCity)) return false;
        if (tile == null) return cityRequirement == BoolRequirement.Any && territoryRequirement == UnitTerritoryRequirement.Any && !useBiomeFilter && !useResourceFilter && hillRequirement == BoolRequirement.Any && mountainRequirement == BoolRequirement.Any;
        if (useBiomeFilter && tile.biome != biome) return false;
        if (!MatchesRequirement(hillRequirement, tile.isHill)) return false;
        if (!MatchesRequirement(mountainRequirement, tile.isMountain)) return false;
        if (useResourceFilter)
        {
            if (tile.resource == null) return false;
            if (tile.resource != resource) return false;
        }
        if (!MatchesTerritoryRequirement(tile, territoryRequirement)) return false;
        return true;
    }

    /// <summary>
    /// Sum per-unit healing speed bonuses (as fractional percent, e.g. 0.10 = +10%) for the given combat unit.
    /// Includes bonuses from civ identity, researched techs/cultures, current government, active policies, pantheons, beliefs, and city buildings when provided.
    /// </summary>
    public float GetUnitHealingPct(CombatUnit unit, City cityContext = null)
    {
        if (unit == null || unit.data == null) return 0f;
        float total = 0f;
        int planetIndex = unit.planetIndex;

        // Civ identity
        if (civData?.unitBonuses != null)
            foreach (var b in civData.unitBonuses)
                if (b != null && MatchesCombatUnitBonusTarget(unit.data, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(unit, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        // Techs
        if (researchedTechs != null)
            foreach (var t in researchedTechs)
                if (t?.unitBonuses != null)
                    foreach (var b in t.unitBonuses) if (b != null && MatchesCombatUnitBonusTarget(unit.data, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(unit, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        // Cultures
        if (researchedCultures != null)
            foreach (var c in researchedCultures)
                if (c?.unitBonuses != null)
                    foreach (var b in c.unitBonuses) if (b != null && MatchesCombatUnitBonusTarget(unit.data, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(unit, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        // Government
        if (currentGovernment != null && currentGovernment.unitBonuses != null)
            foreach (var b in currentGovernment.unitBonuses) if (b != null && MatchesCombatUnitBonusTarget(unit.data, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(unit, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        // Policies
        if (activePolicies != null)
            foreach (var p in activePolicies)
                if (p?.unitBonuses != null)
                    foreach (var b in p.unitBonuses) if (b != null && MatchesCombatUnitBonusTarget(unit.data, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(unit, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        // Pantheons
        foreach (var pb in EnumeratePantheonBonuses())
            if (pb?.unitBonuses != null)
                foreach (var b in pb.unitBonuses) if (b != null && MatchesCombatUnitBonusTarget(unit.data, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(unit, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        // Active beliefs
        foreach (var belief in EnumerateActiveBeliefs())
            if (belief?.unitBonuses != null && IsBeliefSeasonActive(belief, planetIndex))
                foreach (var b in belief.unitBonuses) if (b != null && MatchesCombatUnitBonusTarget(unit.data, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(unit, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        // City buildings (if context provided)
        if (cityContext != null)
        {
            foreach (var (bd, _, _) in cityContext.EnumerateOperationalBuildings())
            {
                if (bd == null || bd.unitBonuses == null) continue;
                foreach (var b in bd.unitBonuses) if (b != null && MatchesCombatUnitBonusTarget(unit.data, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(unit, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;
            }
        }

        return total;
    }

    /// <summary>
    /// Same as GetUnitHealingPct but for worker unit archetypes.
    /// </summary>
    public float GetWorkerHealingPct(WorkerUnit worker, City cityContext = null)
    {
        if (worker == null || worker.data == null) return 0f;
        float total = 0f;
        int planetIndex = worker.planetIndex;

        if (civData?.workerBonuses != null)
            foreach (var b in civData.workerBonuses)
                if (b != null && b.worker == worker.data && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(worker, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        if (researchedTechs != null)
            foreach (var t in researchedTechs)
                if (t?.workerBonuses != null)
                    foreach (var b in t.workerBonuses) if (b != null && b.worker == worker.data && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(worker, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        if (researchedCultures != null)
            foreach (var c in researchedCultures)
                if (c?.workerBonuses != null)
                    foreach (var b in c.workerBonuses) if (b != null && b.worker == worker.data && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(worker, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        if (currentGovernment != null && currentGovernment.workerBonuses != null)
            foreach (var b in currentGovernment.workerBonuses) if (b != null && b.worker == worker.data && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(worker, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        if (activePolicies != null)
            foreach (var p in activePolicies)
                if (p?.workerBonuses != null)
                    foreach (var b in p.workerBonuses) if (b != null && b.worker == worker.data && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(worker, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        foreach (var pb in EnumeratePantheonBonuses())
            if (pb?.workerBonuses != null)
                foreach (var b in pb.workerBonuses) if (b != null && b.worker == worker.data && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(worker, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        foreach (var belief in EnumerateActiveBeliefs())
            if (belief?.workerBonuses != null && IsBeliefSeasonActive(belief, planetIndex))
                foreach (var b in belief.workerBonuses) if (b != null && b.worker == worker.data && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(worker, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;

        if (cityContext != null)
        {
            foreach (var (bd, _, _) in cityContext.EnumerateOperationalBuildings())
            {
                if (bd == null) continue;
                if (bd.workerBonuses != null)
                    foreach (var b in bd.workerBonuses) if (b != null && b.worker == worker.data && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex) && MatchesUnitStatBonusLocation(worker, b.cityRequirement, b.useBiomeFilter, b.biome, b.hillRequirement, b.mountainRequirement, b.useResourceFilter, b.resource, b.territoryRequirement)) total += b.healingRatePct;
            }
        }

        return total;
    }

    public struct UnitTrainingProgressionBonusTotals
    {
        public int experienceAdd;
        public int levelsAdd;
    }

    public UnitTrainingProgressionBonusTotals GetNewCombatUnitProgressionBonuses(CombatUnit unit, City cityContext = null)
    {
        UnitTrainingProgressionBonusTotals totals = default;
        if (unit == null || unit.data == null)
            return totals;

        int planetIndex = unit.planetIndex;

        void Accumulate(UnitStatBonus[] bonuses)
        {
            if (bonuses == null)
                return;

            foreach (var bonus in bonuses)
            {
                if (bonus == null)
                    continue;
                if (!MatchesCombatUnitBonusTarget(unit.data, bonus.unit, bonus.useUnitCategoryFilter, bonus.unitCategory))
                    continue;
                if (!MatchesSeasonFilterForPlanet(bonus.useSeasonFilter, bonus.seasons, planetIndex))
                    continue;
                if (!MatchesUnitStatBonusLocation(unit, bonus.cityRequirement, bonus.useBiomeFilter, bonus.biome, bonus.hillRequirement, bonus.mountainRequirement, bonus.useResourceFilter, bonus.resource, bonus.territoryRequirement))
                    continue;

                totals.experienceAdd += bonus.startingExperienceAdd;
                totals.levelsAdd += bonus.startingLevelsAdd;
            }
        }

        Accumulate(civData?.unitBonuses);

        if (researchedTechs != null)
            foreach (var tech in researchedTechs)
                Accumulate(tech?.unitBonuses);

        if (researchedCultures != null)
            foreach (var culture in researchedCultures)
                Accumulate(culture?.unitBonuses);

        Accumulate(currentGovernment?.unitBonuses);

        if (activePolicies != null)
            foreach (var policy in activePolicies)
                Accumulate(policy?.unitBonuses);

        foreach (var pantheonBonuses in EnumeratePantheonBonuses())
            Accumulate(pantheonBonuses?.unitBonuses);

        foreach (var belief in EnumerateActiveBeliefs())
            if (belief?.unitBonuses != null && IsBeliefSeasonActive(belief, planetIndex))
                Accumulate(belief.unitBonuses);

        if (cityContext != null)
        {
            foreach (var (building, _, _) in cityContext.EnumerateOperationalBuildings())
                Accumulate(building?.unitBonuses);
        }

        return totals;
    }

    public UnitTrainingProgressionBonusTotals GetNewWorkerUnitProgressionBonuses(WorkerUnit worker, City cityContext = null)
    {
        UnitTrainingProgressionBonusTotals totals = default;
        if (worker == null || worker.data == null)
            return totals;

        int planetIndex = worker.planetIndex;

        void Accumulate(WorkerUnitStatBonus[] bonuses)
        {
            if (bonuses == null)
                return;

            foreach (var bonus in bonuses)
            {
                if (bonus == null || bonus.worker != worker.data)
                    continue;
                if (!MatchesSeasonFilterForPlanet(bonus.useSeasonFilter, bonus.seasons, planetIndex))
                    continue;
                if (!MatchesUnitStatBonusLocation(worker, bonus.cityRequirement, bonus.useBiomeFilter, bonus.biome, bonus.hillRequirement, bonus.mountainRequirement, bonus.useResourceFilter, bonus.resource, bonus.territoryRequirement))
                    continue;

                totals.experienceAdd += bonus.startingExperienceAdd;
                totals.levelsAdd += bonus.startingLevelsAdd;
            }
        }

        Accumulate(civData?.workerBonuses);

        if (researchedTechs != null)
            foreach (var tech in researchedTechs)
                Accumulate(tech?.workerBonuses);

        if (researchedCultures != null)
            foreach (var culture in researchedCultures)
                Accumulate(culture?.workerBonuses);

        Accumulate(currentGovernment?.workerBonuses);

        if (activePolicies != null)
            foreach (var policy in activePolicies)
                Accumulate(policy?.workerBonuses);

        foreach (var pantheonBonuses in EnumeratePantheonBonuses())
            Accumulate(pantheonBonuses?.workerBonuses);

        foreach (var belief in EnumerateActiveBeliefs())
            if (belief?.workerBonuses != null && IsBeliefSeasonActive(belief, planetIndex))
                Accumulate(belief.workerBonuses);

        if (cityContext != null)
        {
            foreach (var (building, _, _) in cityContext.EnumerateOperationalBuildings())
                Accumulate(building?.workerBonuses);
        }

        return totals;
    }

    private static bool MatchesDiseaseModifier(DiseaseModifierBonus bonus, DiseaseData disease)
    {
        if (bonus == null || disease == null)
            return false;

        return bonus.affectsAllDiseases || bonus.disease == disease;
    }

    private static void AccumulateDiseaseModifier(ref DiseaseModifierTotals totals, DiseaseModifierBonus bonus)
    {
        if (bonus == null)
            return;

        totals.grantsImmunity |= bonus.grantsImmunity;
        totals.infectionChancePct += bonus.infectionChancePct;
        totals.spreadChancePct += bonus.spreadChancePct;
        totals.durationPct += bonus.durationPct;
        totals.cityPopulationLossPct += bonus.cityPopulationLossPct;
        totals.cityYieldPenaltyPct += bonus.cityYieldPenaltyPct;
        totals.cityMoralePenaltyPct += bonus.cityMoralePenaltyPct;
        totals.cityLoyaltyPenaltyPct += bonus.cityLoyaltyPenaltyPct;
        totals.herdMortalityPct += bonus.herdMortalityPct;
        totals.herdForagePenaltyPct += bonus.herdForagePenaltyPct;
    }

    public DiseaseModifierTotals GetDiseaseModifierTotals(DiseaseData disease, City cityContext = null, Herd herdContext = null)
    {
        DiseaseModifierTotals totals = default;
        if (disease == null)
            return totals;

        int planetIndex = cityContext != null ? cityContext.planetIndex : herdContext != null ? herdContext.planetIndex : -1;

        void Accumulate(DiseaseModifierBonus[] bonuses)
        {
            if (bonuses == null)
                return;

            foreach (var bonus in bonuses)
            {
                if (MatchesDiseaseModifier(bonus, disease) && MatchesSeasonFilterForPlanet(bonus.useSeasonFilter, bonus.seasons, planetIndex))
                    AccumulateDiseaseModifier(ref totals, bonus);
            }
        }

        Accumulate(civData?.diseaseBonuses);

        if (researchedTechs != null)
            foreach (var tech in researchedTechs)
                Accumulate(tech?.diseaseBonuses);

        if (researchedCultures != null)
            foreach (var culture in researchedCultures)
                Accumulate(culture?.diseaseBonuses);

        Accumulate(currentGovernment?.diseaseBonuses);

        if (activePolicies != null)
            foreach (var policy in activePolicies)
                Accumulate(policy?.diseaseBonuses);

        foreach (var pantheonBonuses in EnumeratePantheonBonuses())
            Accumulate(pantheonBonuses?.diseaseBonuses);

        foreach (var belief in EnumerateActiveBeliefs())
            if (IsBeliefSeasonActive(belief, planetIndex))
                Accumulate(belief?.diseaseBonuses);

        if (cityContext != null)
        {
            foreach (var (building, _, _) in cityContext.EnumerateOperationalBuildings())
                Accumulate(building?.diseaseBonuses);
        }

        if (herdContext != null && herdContext.builtStructures != null)
        {
            foreach (var building in herdContext.builtStructures)
                Accumulate(building?.diseaseBonuses);
        }

        return totals;
    }

    private static void AccumulateAttritionModifier(ref AttritionModifierTotals totals, AttritionModifierBonus bonus)
    {
        if (bonus == null) return;
        totals.winterDamageReductionPct += bonus.winterDamageReductionPct;
        totals.famineDamageReductionPct += bonus.famineDamageReductionPct;
        totals.biomeDamageReductionPct += bonus.biomeDamageReductionPct;
    }

    public AttritionModifierTotals GetAttritionModifierTotals(City cityContext = null, Herd herdContext = null)
    {
        AttritionModifierTotals totals = default;
        int planetIndex = cityContext != null ? cityContext.planetIndex : herdContext != null ? herdContext.planetIndex : -1;

        void Accumulate(AttritionModifierBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var b in bonuses)
                if (b != null && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                    AccumulateAttritionModifier(ref totals, b);
        }

        Accumulate(civData?.attritionBonuses);

        if (researchedTechs != null)
            foreach (var tech in researchedTechs)
                Accumulate(tech?.attritionBonuses);

        if (researchedCultures != null)
            foreach (var culture in researchedCultures)
                Accumulate(culture?.attritionBonuses);

        Accumulate(currentGovernment?.attritionBonuses);

        if (activePolicies != null)
            foreach (var policy in activePolicies)
                Accumulate(policy?.attritionBonuses);

        foreach (var pantheonBonuses in EnumeratePantheonBonuses())
            Accumulate(pantheonBonuses?.attritionBonuses);

        foreach (var belief in EnumerateActiveBeliefs())
            if (IsBeliefSeasonActive(belief, planetIndex))
                Accumulate(belief?.attritionBonuses);

        if (cityContext != null)
        {
            foreach (var (building, _, _) in cityContext.EnumerateOperationalBuildings())
                Accumulate(building?.attritionBonuses);
        }

        if (herdContext != null && herdContext.builtStructures != null)
        {
            foreach (var b in herdContext.builtStructures)
                Accumulate(b?.attritionBonuses);
        }

        return totals;
    }

    public float GetHerdStarvationPercentReduction(Herd herdContext = null)
    {
        float total = 0f;
        int planetIndex = herdContext != null ? herdContext.planetIndex : -1;

        total += civData != null ? civData.herdStarvationPercentReduction : 0f;

        if (researchedTechs != null)
            foreach (var tech in researchedTechs)
                if (tech != null) total += tech.herdStarvationPercentReduction;

        if (researchedCultures != null)
            foreach (var culture in researchedCultures)
                if (culture != null) total += culture.herdStarvationPercentReduction;

        if (currentGovernment != null)
            total += currentGovernment.herdStarvationPercentReduction;

        if (activePolicies != null)
            foreach (var policy in activePolicies)
                if (policy != null) total += policy.herdStarvationPercentReduction;

        foreach (var pantheonBonuses in EnumeratePantheonBonuses())
            if (pantheonBonuses != null) total += pantheonBonuses.herdStarvationPercentReduction;

        foreach (var belief in EnumerateActiveBeliefs())
            if (IsBeliefSeasonActive(belief, planetIndex)) total += belief.herdStarvationPercentReduction;

        if (herdContext != null && herdContext.builtStructures != null)
        {
            foreach (var building in herdContext.builtStructures)
                if (building != null) total += building.herdStarvationPercentReduction;
        }

        return total;
    }

    private void NotifyBeliefsChanged()
    {
        OnBeliefsChanged?.Invoke();
    }

    /// <summary>
    /// Returns true if any active belief (pantheon-chosen or founded religion) is of the given category.
    /// </summary>
    public bool HasActiveBeliefInCategory(BeliefCategory category)
    {
        foreach (var b in EnumerateActiveBeliefs())
        {
            if (b != null && b.category == category) return true;
        }
        return false;
    }

    public bool CanUseBelief(BeliefData belief)
    {
        if (belief == null)
            return false;

        if (belief.exclusiveToPantheons == null || belief.exclusiveToPantheons.Length == 0)
            return true;

        if (foundedPantheons == null || foundedPantheons.Count == 0)
            return false;

        foreach (var pantheon in foundedPantheons)
        {
            if (pantheon == null) continue;
            for (int i = 0; i < belief.exclusiveToPantheons.Length; i++)
            {
                if (belief.exclusiveToPantheons[i] == pantheon)
                    return true;
            }
        }

        return false;
    }

    public bool CanUseBeliefForPantheon(PantheonData pantheon, BeliefData belief)
    {
        if (pantheon == null || belief == null)
            return false;

        if (belief.exclusiveToPantheons == null || belief.exclusiveToPantheons.Length == 0)
            return true;

        for (int i = 0; i < belief.exclusiveToPantheons.Length; i++)
        {
            if (belief.exclusiveToPantheons[i] == pantheon)
                return true;
        }

        return false;
    }

    public static bool MatchesSeasonFilter(Season currentSeason, bool useSeasonFilter, Season[] seasons)
    {
        if (!useSeasonFilter)
            return true;

        if (seasons == null || seasons.Length == 0)
            return false;

        foreach (var season in seasons)
        {
            if (season == currentSeason)
                return true;
        }

        return false;
    }

    public static bool MatchesCombatUnitBonusTarget(CombatUnitData actualUnit, CombatUnitData targetUnit, bool useUnitCategoryFilter, CombatCategory unitCategory)
    {
        if (actualUnit == null)
            return false;

        bool hasSpecificUnitTarget = targetUnit != null;
        if (hasSpecificUnitTarget && targetUnit != actualUnit)
            return false;

        if (useUnitCategoryFilter && actualUnit.unitType != unitCategory)
            return false;

        return hasSpecificUnitTarget || useUnitCategoryFilter;
    }

    public static bool HasCombatBonusOpponentFilter(CombatUnitData targetUnit, WorkerUnitData targetWorker, bool useTargetUnitCategoryFilter)
    {
        return targetUnit != null || targetWorker != null || useTargetUnitCategoryFilter;
    }

    public static bool MatchesCombatBonusOpponent(BaseUnit opponent, CombatUnitData targetUnit, WorkerUnitData targetWorker, bool useTargetUnitCategoryFilter, CombatCategory targetUnitCategory)
    {
        if (!HasCombatBonusOpponentFilter(targetUnit, targetWorker, useTargetUnitCategoryFilter))
            return true;

        if (opponent == null)
            return false;

        if (targetUnit != null)
        {
            if (opponent is not CombatUnit combatOpponent || combatOpponent.data != targetUnit)
                return false;
        }

        if (targetWorker != null)
        {
            if (opponent is not WorkerUnit workerOpponent || workerOpponent.data != targetWorker)
                return false;
        }

        if (useTargetUnitCategoryFilter)
        {
            if (opponent is not CombatUnit categoryOpponent || categoryOpponent.data == null || categoryOpponent.data.unitType != targetUnitCategory)
                return false;
        }

        return true;
    }

    public bool MatchesSeasonFilterForPlanet(bool useSeasonFilter, Season[] seasons, int planetIndex = -1)
    {
        Season currentSeason = ClimateManager.Instance != null
            ? ClimateManager.Instance.GetSeasonForPlanet(planetIndex >= 0 ? planetIndex : 0)
            : Season.Spring;
        return MatchesSeasonFilter(currentSeason, useSeasonFilter, seasons);
    }

    public bool IsBeliefSeasonActive(BeliefData belief, int planetIndex = -1)
    {
        return belief != null && MatchesSeasonFilterForPlanet(belief.useSeasonFilter, belief.seasons, planetIndex);
    }

    public static bool IsBeliefSeasonActive(BeliefData belief, Season currentSeason)
    {
        return belief != null && MatchesSeasonFilter(currentSeason, belief.useSeasonFilter, belief.seasons);
    }

    /// <summary>
    /// Get the custom-assigned belief (UI) for a category, or null.
    /// </summary>
    public BeliefData GetCustomBeliefInCategory(BeliefCategory category)
    {
        if (customAssignedBeliefs == null) return null;
        foreach (var b in customAssignedBeliefs)
            if (b != null && b.category == category) return b;
        return null;
    }

    public int GetBeliefFaithCost(BeliefData belief)
    {
        return belief != null ? Mathf.Max(0, belief.faithCost) : 0;
    }

    /// <summary>
    /// Set/replace a custom-assigned belief for the given category.
    /// Replacing a belief costs the full faith cost of the newly selected belief.
    /// </summary>
    public bool SetCustomBelief(BeliefCategory category, BeliefData belief)
    {
        if (belief == null) return RemoveCustomBeliefInCategory(category);

        if (!CanUseBelief(belief))
            return false;

        var currentBelief = GetCustomBeliefInCategory(category);
        if (currentBelief == belief)
            return true;

        int faithCost = GetBeliefFaithCost(belief);
        if (faith < faithCost)
            return false;

        // Remove any existing custom belief in this category
        RemoveCustomBeliefInCategoryInternal(category, false);

        if (customAssignedBeliefs == null) customAssignedBeliefs = new List<BeliefData>();
        customAssignedBeliefs.Add(belief);

        if (faithCost > 0)
            AddFaith(-faithCost);

        UpdateFaithYieldModifier();
        return true;
    }

    /// <summary>
    /// Remove any custom-assigned belief in the category. Returns true if removed or nothing to remove.
    /// </summary>
    public bool RemoveCustomBeliefInCategory(BeliefCategory category)
    {
        return RemoveCustomBeliefInCategoryInternal(category, true);
    }

    private bool RemoveCustomBeliefInCategoryInternal(BeliefCategory category, bool updateModifiers)
    {
        if (customAssignedBeliefs == null) return true;
        for (int i = customAssignedBeliefs.Count - 1; i >= 0; i--)
        {
            var b = customAssignedBeliefs[i];
            if (b != null && b.category == category)
            {
                customAssignedBeliefs.RemoveAt(i);
                if (updateModifiers)
                    UpdateFaithYieldModifier();
                return true;
            }
        }
        return true;
    }

    public bool IsCapitalCity(City city)
    {
        if (city == null) return false;
        EnsureCapitalCity();
        return capitalCity == city;
    }

    private List<CombatUnitData> GetPantheonGrantedCombatUnits()
    {
        var result = new List<CombatUnitData>();

        foreach (var bonuses in EnumeratePantheonBonuses())
        {
            if (bonuses.unlockedCombatUnits == null)
                continue;

            foreach (var unit in bonuses.unlockedCombatUnits)
            {
                if (unit != null)
                    result.Add(unit);
            }
        }

        return result;
    }

    private List<WorkerUnitData> GetPantheonGrantedWorkerUnits()
    {
        var result = new List<WorkerUnitData>();

        foreach (var bonuses in EnumeratePantheonBonuses())
        {
            if (bonuses.unlockedWorkerUnits == null)
                continue;


            foreach (var unit in bonuses.unlockedWorkerUnits)
            {
                if (unit != null)
                    result.Add(unit);
            }
        }

        return result;
    }

    private List<BuildingData> GetPantheonGrantedBuildings()
    {
        var result = new List<BuildingData>();

        foreach (var bonuses in EnumeratePantheonBonuses())
        {
            if (bonuses.unlockedBuildings == null)
                continue;

            foreach (var building in bonuses.unlockedBuildings)
            {
                if (building != null)
                    result.Add(building);
            }
        }

        return result;
    }

    private void RecalculateCivilizationModifiers()
    {
        attackBonus = civData != null ? civData.attackBonus : 0f;
        defenseBonus = civData != null ? civData.defenseBonus : 0f;
        movementBonus = civData != null ? civData.movementBonus : 0f;
        foodModifier = civData != null ? civData.foodModifier : 0f;
        productionModifier = civData != null ? civData.productionModifier : 0f;
        goldModifier = civData != null ? civData.goldModifier : 0f;
        scienceModifier = civData != null ? civData.scienceModifier : 0f;
        cultureModifier = civData != null ? civData.cultureModifier : 0f;
        faithModifier = civData != null ? civData.faithModifier : 0f;

        ApplyLeaderBonuses();
        ApplyPantheonBonuses();
        ApplyBeliefBonuses();
    }

    private void ApplyPantheonBonuses()
    {
        foreach (var bonuses in EnumeratePantheonBonuses())
        {
            attackBonus += bonuses.attackBonus;
            defenseBonus += bonuses.defenseBonus;
            movementBonus += bonuses.movementBonus;
            foodModifier += bonuses.foodModifier;
            productionModifier += bonuses.productionModifier;
            goldModifier += bonuses.goldModifier;
            scienceModifier += bonuses.scienceModifier;
            cultureModifier += bonuses.cultureModifier;
            faithModifier += bonuses.faithModifier;
        }
    }

    private void ApplyBeliefBonuses()
    {
        // Apply modifiers from any active beliefs (custom-assigned beliefs)
        foreach (var b in EnumerateActiveBeliefs())
        {
            if (b == null) continue;
            if (!IsBeliefSeasonActive(b)) continue;
            foodModifier += b.foodModifier;
            productionModifier += b.productionModifier;
            goldModifier += b.goldModifier;
            scienceModifier += b.scienceModifier;
            cultureModifier += b.cultureModifier;
            faithModifier += b.faithModifier;
        }
    }

    /// <summary>
    /// Called by TurnManager at the start of this civ's turn.
    /// </summary>
    public void BeginTurn(int round)
    {
        try
        {
            turnCount = round;

            ApplyPerTurnResourceUpkeep();

            // 1) Reset units (iterate a snapshot to avoid collection-modified exceptions)
            if (combatUnits != null)
            {
                foreach (var u in combatUnits.ToArray())
                {
                    if (u != null) u.ResetForNewTurn();
                }
            }
            if (workerUnits != null)
            {
                foreach (var w in workerUnits.ToArray())
                {
                    if (w != null) w.ResetForNewTurn();
                }
            }

            // 2) Process each city (production, growth, morale, surrender, label)
            foreach (var city in cities)
            {
                if (city != null)
                {
                    try
                    {
                        city.ProcessCityTurn();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[Civilization] Error processing city {city.cityName}: {e.Message}");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Civilization] Error in BeginTurn for {civData?.civName ?? "Unknown"}: {e.Message}");
        }

        // 3) Collect city yields into storage
        // Compute science and culture as per-turn yields (do not accumulate them across turns).
        int totalScienceThisTurn = 0;
        int totalCultureThisTurn = 0;
        // Track per-turn income rates for UI caching
        int totalGoldThisTurn = 0;
        int totalFoodThisTurn = 0;
        int totalPolicyThisTurn = 0;
        int totalFaithThisTurn = 0;
        var buildingResourcesThisTurn = new Dictionary<ResourceData, int>();
        var globalBonuses = CalculateTotalBonuses(researchedTechs, researchedCultures);

        foreach (var city in cities)
        {
            if (city != null)
            {
                try
                {
                    int cityGold = Mathf.RoundToInt(city.GetGoldPerTurn() * (1 + goldModifier));
                    int cityFood = Mathf.RoundToInt(city.GetFoodPerTurn() * (1 + foodModifier));
                    int cityPolicy = city.GetPolicyPointPerTurn();
                    int cityFaith = Mathf.RoundToInt(city.GetFaithPerTurn() * (1 + faithModifier));
                    gold         += cityGold;
                    food         += cityFood;
                    totalScienceThisTurn += Mathf.RoundToInt(city.GetSciencePerTurn() * (1 + scienceModifier));
                    totalCultureThisTurn += Mathf.RoundToInt(city.GetCulturePerTurn() * (1 + cultureModifier));
                    policyPoints += cityPolicy;
                    faith        += cityFaith;
                    totalGoldThisTurn += cityGold;
                    totalFoodThisTurn += cityFood;
                    totalPolicyThisTurn += cityPolicy;
                    totalFaithThisTurn += cityFaith;
                    city.AddBuildingResourceProductionTo(buildingResourcesThisTurn);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Civilization] Error collecting yields from city {city.cityName}: {e.Message}");
                }
            }
        }

        if (buildingResourcesThisTurn.Count > 0)
        {
            foreach (var kvp in buildingResourcesThisTurn)
            {
                AddResource(kvp.Key, kvp.Value);
            }
        }

        // 3.5) Process interplanetary trade routes
        foreach (var tradeRoute in interplanetaryTradeRoutes)
        {
            if (tradeRoute != null && tradeRoute.isInterplanetaryRoute)
            {
                int tradeGold = Mathf.RoundToInt(tradeRoute.goldPerTurn * (1 + goldModifier));
                gold += tradeGold;
                totalGoldThisTurn += tradeGold;
}
        }

        // 3.6) Per-unit yields (combat units). Applies after city yields, before research/culture processing.
    if (combatUnits != null && combatUnits.Count > 0)
        {
            int addFood = 0, addGold = 0, addSci = 0, addCul = 0, addFai = 0, addPol = 0;
            foreach (var u in combatUnits)
            {
                if (u == null || u.data == null) continue;
        var yields = ComputeUnitPerTurnYield(u.data, u.planetIndex, u.Weapon, u.Shield, u.Armor, u.Miscellaneous);
                addFood += yields.food;
                addGold += yields.gold;
                addSci  += yields.science;
                addCul  += yields.culture;
                addFai  += yields.faith;
                addPol  += yields.policy;

                // Orbit yields: units in orbit collect yields from the tile they orbit over
                if (u.IsInOrbit)
                {
                    var ts = TileSystem.GetForPlanet(u.planetIndex) ?? TileSystem.Instance;
                    var tileData = ts != null ? ts.GetTileData(u.currentTileIndex) : null;
                    if (tileData != null)
                    {
                        var tileYield = tileData.GetTotalYield();
                        addFood += tileYield.Food;
                        addGold += tileYield.Gold;
                        addSci  += tileYield.Science;
                        addCul  += tileYield.Culture;
                        addFai  += tileYield.Faith;
                        addPol  += tileYield.Policy;
                    }
                }
            }

            // Apply global civ yield modifiers to these additions as well
            gold    += Mathf.RoundToInt(addGold * (1 + goldModifier));
            food    += Mathf.RoundToInt(addFood * (1 + foodModifier));
            totalScienceThisTurn += Mathf.RoundToInt(addSci  * (1 + scienceModifier));
            totalCultureThisTurn += Mathf.RoundToInt(addCul  * (1 + cultureModifier));
            faith   += Mathf.RoundToInt(addFai  * (1 + faithModifier));
            policyPoints += addPol; // no global modifier currently
            totalGoldThisTurn += Mathf.RoundToInt(addGold * (1 + goldModifier));
            totalFoodThisTurn += Mathf.RoundToInt(addFood * (1 + foodModifier));
            totalFaithThisTurn += Mathf.RoundToInt(addFai  * (1 + faithModifier));
            totalPolicyThisTurn += addPol;
        }

        // 3.7) Per-unit yields (workers)
        if (workerUnits != null && workerUnits.Count > 0)
        {
            int addFood = 0, addGold = 0, addSci = 0, addCul = 0, addFai = 0, addPol = 0;
            foreach (var w in workerUnits)
            {
                if (w == null || w.data == null) continue;
                var yields = ComputeWorkerPerTurnYield(w.data, w.planetIndex);
                addFood += yields.food;
                addGold += yields.gold;
                addSci  += yields.science;
                addCul  += yields.culture;
                addFai  += yields.faith;
                addPol  += yields.policy;
            }

            gold    += Mathf.RoundToInt(addGold * (1 + goldModifier));
            food    += Mathf.RoundToInt(addFood * (1 + foodModifier));
            totalScienceThisTurn += Mathf.RoundToInt(addSci  * (1 + scienceModifier));
            totalCultureThisTurn += Mathf.RoundToInt(addCul  * (1 + cultureModifier));
            faith   += Mathf.RoundToInt(addFai  * (1 + faithModifier));
            policyPoints += addPol;
            totalGoldThisTurn += Mathf.RoundToInt(addGold * (1 + goldModifier));
            totalFoodThisTurn += Mathf.RoundToInt(addFood * (1 + foodModifier));
            totalFaithThisTurn += Mathf.RoundToInt(addFai  * (1 + faithModifier));
            totalPolicyThisTurn += addPol;
        }

        // 3.75) Herd yields & grazing
        if (herds != null && herds.Count > 0)
        {
            foreach (var h in herds.ToArray())
            {
                if (h == null) continue;
                try
                {
                    // Process herd-local production (herd acts like a mobile city)
                    try { h.ProcessProduction(); } catch { }
                    h.ProcessGrazingTick(round);

                    // Sum animal per-turn yields using herd rules (per-100 species contributions)
                    int totalAnimalConsumption = 0;
                    int totalAnimals = 0;
                    foreach (var ae in h.animals)
                    {
                        if (ae == null) continue;
                        int cnt = Mathf.Max(0, ae.count);
                        totalAnimals += cnt;
                        // Apply per-100 consumption rules where defined, and fall back to per-animal for remainder
                        int per100 = Herd.GetFoodConsumptionPer100(ae.species);
                        int perAnimal = Herd.GetFoodConsumptionPerAnimal(ae.species);
                        totalAnimalConsumption += (cnt / 100) * per100;
                        totalAnimalConsumption += (cnt % 100) * perAnimal;
                    }

                    // Animal yields (food/gold/production) computed by herd per-100 rules
                    var ay = h.GetAnimalYields();
                    // Aggregate targeted herd yield bonuses from techs, cultures, beliefs, etc.
                    var hb = AggregateHerdYieldBonuses(h, h.planetIndex);
                    // Apply civilization modifiers + targeted bonuses to herd yields
                    int herdGold = Mathf.RoundToInt((ay.Gold + hb.goldAdd) * (1 + goldModifier + hb.goldPct));
                    int herdFood = Mathf.RoundToInt((ay.Food + hb.foodAdd) * (1 + foodModifier + hb.foodPct));
                    int herdFaith = Mathf.RoundToInt((ay.Faith + hb.faithAdd) * (1 + faithModifier + hb.faithPct));
                    int herdSci = Mathf.RoundToInt((ay.Science + hb.scienceAdd) * (1 + scienceModifier + hb.sciencePct));
                    int herdCul = Mathf.RoundToInt((ay.Culture + hb.cultureAdd) * (1 + cultureModifier + hb.culturePct));
                    int herdProd = Mathf.RoundToInt((ay.Production + hb.productionAdd) * (1 + hb.productionPct));
                    int herdPol = ay.Policy + hb.policyPointsAdd;
                    gold += herdGold;
                    food += herdFood;
                    totalScienceThisTurn += herdSci;
                    totalCultureThisTurn += herdCul;
                    faith += herdFaith;
                    policyPoints += herdPol;
                    // herdProd is consumed locally by herd builds; not added to civ production
                    totalGoldThisTurn += herdGold;
                    totalFoodThisTurn += herdFood;
                    totalFaithThisTurn += herdFaith;
                    totalPolicyThisTurn += herdPol;
                    // Herd production may be consumed locally for herd builds; add its production to civ stats as well
                    // (optional display/aggregation)
                    // production += h.GetProductionPerTurn();

                    // Consume from herd's foodReserve (herd grazing provides food, not civ stockpile)
                    if (totalAnimalConsumption > 0)
                    {
                        if (h.foodReserve >= totalAnimalConsumption)
                        {
                            h.foodReserve -= totalAnimalConsumption;
                        }
                        else
                        {
                            int deficit = totalAnimalConsumption - h.foodReserve;
                            h.foodReserve = 0;

                            // Default starvation percent loss, reduced by civ/tech/culture/government/policy/pantheon/belief/structure bonuses.
                            float baseStarvePercent = 0.25f;
                            float reduction = GetHerdStarvationPercentReduction(h);
                            float netPercent = Mathf.Max(0f, baseStarvePercent - reduction);

                            // If there are animals, remove netPercent of total animals (round up)
                            if (totalAnimals > 0 && netPercent > 0f)
                            {
                                int animalsToLose = Mathf.CeilToInt(totalAnimals * netPercent);
                                int remainingToRemove = animalsToLose;

                                // Remove proportionally from each animal entry
                                for (int i = h.animals.Count - 1; i >= 0 && remainingToRemove > 0; i--)
                                {
                                    var ae = h.animals[i];
                                    if (ae == null || ae.count <= 0) { h.animals.RemoveAt(i); continue; }
                                    int remove = Mathf.FloorToInt(((float)ae.count / (float)totalAnimals) * animalsToLose);
                                    // Ensure we remove at least 1 when needed
                                    if (remove <= 0) remove = 1;
                                    remove = Mathf.Min(remove, ae.count);
                                    ae.count -= remove;
                                    remainingToRemove -= remove;
                                    if (ae.count == 0) h.animals.RemoveAt(i);
                                }

                                // If rounding left some remaining, remove from largest stacks
                                if (remainingToRemove > 0 && h.animals.Count > 0)
                                {
                                    // Sort descending by count once, then trim from the top — O(m log m) instead of O(m*k)
                                    h.animals.Sort((a, b) => b.count.CompareTo(a.count));
                                    for (int i = 0; i < h.animals.Count && remainingToRemove > 0; i++)
                                    {
                                        var ae = h.animals[i];
                                        if (ae == null) continue;
                                        int take = Mathf.Min(remainingToRemove, ae.count);
                                        ae.count -= take;
                                        remainingToRemove -= take;
                                    }
                                    h.animals.RemoveAll(ae => ae == null || ae.count <= 0);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Civilization] Error processing herd grazing: {ex.Message}");
                }
            }
        }

        if (globalBonuses.flatGoldBonus != 0)
        {
            gold += globalBonuses.flatGoldBonus;
            totalGoldThisTurn += globalBonuses.flatGoldBonus;
        }
        if (globalBonuses.flatFoodBonus != 0)
        {
            food += globalBonuses.flatFoodBonus;
            totalFoodThisTurn += globalBonuses.flatFoodBonus;
        }
        if (globalBonuses.flatScienceBonus != 0)
        {
            totalScienceThisTurn += globalBonuses.flatScienceBonus;
        }
        if (globalBonuses.flatCultureBonus != 0)
        {
            totalCultureThisTurn += globalBonuses.flatCultureBonus;
        }
        if (globalBonuses.flatFaithBonus != 0)
        {
            faith += globalBonuses.flatFaithBonus;
            totalFaithThisTurn += globalBonuses.flatFaithBonus;
        }

        // Commit computed per-turn science & culture yields into their fields (do not accumulate across turns)
        science = totalScienceThisTurn;
        culture = totalCultureThisTurn;

        // 3.8) FOOD CONSUMPTION - Units and cities must eat!
        int totalFoodConsumption = 0;
        
        // Combat units consume food based on their data
        if (combatUnits != null)
        {
            foreach (var u in combatUnits)
            {
                if (u != null && u.data != null)
                    totalFoodConsumption += u.data.foodConsumptionPerTurn;
                else
                    totalFoodConsumption += defaultFoodPerCombatUnit; // Fallback
            }
        }
        
        // Worker units consume food based on their data
        if (workerUnits != null)
        {
            foreach (var w in workerUnits.ToArray())
            {
                if (w != null && w.data != null)
                    totalFoodConsumption += w.data.foodConsumptionPerTurn;
                else
                    totalFoodConsumption += defaultFoodPerWorkerUnit; // Fallback
            }
        }
        
        // Cities consume food based on population size
        if (cities != null)
        {
            foreach (var city in cities.ToArray())
            {
                if (city != null)
                    totalFoodConsumption += city.GetFoodConsumptionPerTurn();
            }
        }
        
        // Consume food from stockpile
        food -= totalFoodConsumption;
        
        // Clamp to minimum (allows small negative buffer before critical famine)
        if (food < minimumFoodStockpile)
            food = minimumFoodStockpile;

        // Cache per-turn rates so UI doesn't need to recompute them
        cachedGoldPerTurn = totalGoldThisTurn;
        cachedFoodPerTurn = totalFoodThisTurn;
        cachedSciencePerTurn = totalScienceThisTurn;
        cachedCulturePerTurn = totalCultureThisTurn;
        cachedPolicyPerTurn = totalPolicyThisTurn;
        cachedFaithPerTurn = totalFaithThisTurn;
        cachedFoodConsumption = totalFoodConsumption;

        Debug.Log($"[Civilization][BeginTurn] {civData?.civName}: turn={round} cities={cities?.Count} combatUnits={combatUnits?.Count} workers={workerUnits?.Count} | gold={gold} food={food} science={science} culture={culture} faith={faith}");

        // 4) Advance technology
        ProcessResearch();

        // 5) Advance culture
        ProcessCulture();

        // 6) Noble politics: council pressure and faction formation/demands
        if (governorsEnabled && governors.Count > 0)
        {
            ProcessCouncilPressure();
            ProcessFactionTick(round);
        }

        // --- NEW: Unrest & famine handling ---
        // Update war weariness
        int warCount = 0;
        foreach (var kv in relations.ToArray())
            if (kv.Value == DiplomaticState.War)
                warCount++;
        if (warCount > 0)
            warWeariness += warCount * warWearinessPerWarTurn;
        else
            warWeariness = Mathf.Max(0f, warWeariness - warWearinessRecoveryPerTurn);

        // Clamp 0–1
        warWeariness = Mathf.Clamp01(warWeariness);

        // Check famine: true if food stockpile <= 0 (AFTER consumption)
        // Famine applies loyalty penalties (via City.UpdateLoyalty) and unit attrition when active.
        famineActive = (food <= 0);
        if (famineActive)
        {
            if (isPlayerControlled && UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"FAMINE! {civData.civName} has no food. Cities are losing loyalty!");
            }

            if (cities != null)
            {
                foreach (var city in cities.ToArray())
                {
                    if (city == null || city.level <= 1) continue;

                    try
                    {
                        var cityAttritionTotals = GetAttritionModifierTotals(city, null);
                        city.faminePopulationLossProgress += cityAttritionTotals.FamineDamageMultiplier;
                        while (city.faminePopulationLossProgress >= 1f && city.level > 1)
                        {
                            city.level--;
                            city.faminePopulationLossProgress -= 1f;
                        }
                    }
                    catch { }
                }
            }

            // Apply per-turn famine attrition to owned units (combat + workers)
            try
            {
                var attrTotals = GetAttritionModifierTotals(null, null);
                int baseDamage = Mathf.Max(0, famineAttritionDamage);
                int damageToApply = Mathf.CeilToInt(baseDamage * attrTotals.FamineDamageMultiplier);
                if (damageToApply > 0)
                {
                    if (combatUnits != null)
                    {
                        foreach (var u in combatUnits.ToArray())
                        {
                            try { if (u != null) u.ApplyDamage(damageToApply); } catch { }
                        }
                    }

                    if (workerUnits != null)
                    {
                        foreach (var w in workerUnits.ToArray())
                        {
                            try { if (w != null) w.ApplyDamage(damageToApply); } catch { }
                        }
                    }

                    if (isPlayerControlled && UIManager.Instance != null)
                    {
                        UIManager.Instance.ShowNotification($"Famine: units took {damageToApply} attrition damage this turn.");
                    }
                }
            }
            catch { }
        }
        else if (food < totalFoodConsumption * 2 && isPlayerControlled && UIManager.Instance != null)
        {
            // Warning: food running low (less than 2 turns worth)
            UIManager.Instance.ShowNotification($"Warning: {civData.civName} food reserves are low ({food} remaining)");
        }

        // 6) Fire turn‐started event
        OnTurnStarted?.Invoke(this, round);

        // After all per-turn effects (which may have killed units), check if this civ is now empty.
        // NOTE: Do NOT self-destruct here — TurnManager's coroutine still holds a reference to us
        // and will fire turn-change events after BeginTurn returns.
        // Instead just flag ourselves; TurnManager's per-round prune will clean us up safely.
        CheckAndFlagIfEmpty();
    }

    /// <summary>
    /// Set to true when the civ has no cities or units left. TurnManager will prune it at end of round.
    /// </summary>
    [HideInInspector] public bool markedForRemoval = false;

    /// <summary>
    /// Prune null entries and flag this civ for removal if empty.
    /// Does NOT destroy the GameObject — TurnManager does that safely during its prune pass.
    /// </summary>
    public void CheckAndFlagIfEmpty()
    {
        try
        {
            cities?.RemoveAll(x => x == null);
            combatUnits?.RemoveAll(x => x == null);
            workerUnits?.RemoveAll(x => x == null);

            bool hasCities = cities != null && cities.Count > 0;
            bool hasCombat = combatUnits != null && combatUnits.Count > 0;
            bool hasWorkers = workerUnits != null && workerUnits.Count > 0;

            if (!hasCities && !hasCombat && !hasWorkers)
            {
                markedForRemoval = true;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Civilization] CheckAndFlagIfEmpty failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Prune null entries and, if there are no cities and no units left, unregister
    /// and destroy this Civilization GameObject. This is the authoritative self-terminate path.
    /// Safe to call multiple times.
    /// </summary>
    public void CheckAndDestroyIfEmpty()
    {
        try
        {
            cities?.RemoveAll(x => x == null);
            combatUnits?.RemoveAll(x => x == null);
            workerUnits?.RemoveAll(x => x == null);

            bool hasCities = cities != null && cities.Count > 0;
            bool hasCombat = combatUnits != null && combatUnits.Count > 0;
            bool hasWorkers = workerUnits != null && workerUnits.Count > 0;

            if (!hasCities && !hasCombat && !hasWorkers)
            {
                Debug.Log($"[Civilization] {civData?.civName ?? "(unknown)"} has no cities or units — self-terminating.");

                // Unregister from managers
                try { CivilizationManager.Instance?.UnregisterCiv(this); } catch { }
                try { TurnManager.Instance?.UnregisterCivilization(this); } catch { }

                // Clear diplomacy relations to avoid lingering references
                try { relations?.Clear(); } catch { }

                // Destroy the GameObject (Unity will finalize OnDestroy later)
                try { if (gameObject != null) Destroy(gameObject); } catch { }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Civilization] CheckAndDestroyIfEmpty failed: {ex.Message}");
        }
    }

    private void ProcessResearch()
    {
        if (currentTech == null)
        {
            Debug.Log($"[Civilization][Research] {civData?.civName}: No currentTech assigned — skipping ProcessResearch. science={science}");
            return;
        }
        // If research was started this turn, defer progress until the next turn
        if (researchStartedThisTurn)
        {
            researchStartedThisTurn = false;
            Debug.Log($"[Civilization][Research] {civData?.civName}: research started this turn - deferring first progress tick.");
            return;
        }
        float prevProgress = currentTechProgress;
        currentTechProgress += science;
        Debug.Log($"[Civilization][Research] {civData?.civName}: tech='{currentTech.techName}' scienceThisTurn={science} prevProgress={prevProgress} newProgress={currentTechProgress} cost={currentTech.scienceCost} remaining={currentTech.scienceCost - currentTechProgress}");
        if (currentTechProgress >= currentTech.scienceCost)
        {
            TechData completedTech = currentTech;
            currentTech = null; // Stop further progress on this tech immediately
            currentTechProgress = 0;

            // Call TechManager to handle completion
            if (TechManager.Instance != null)
            {
                TechManager.Instance.CompleteResearch(this, completedTech);
            }
            else
            {
                Debug.LogError($"Civilization {civData.civName}: TechManager.Instance is null. Cannot complete research for {completedTech.techName}.");
                // Fallback: Manually do critical parts if manager is missing (not ideal)
                if (!researchedTechs.Contains(completedTech)) researchedTechs.Add(completedTech);
                ApplyTechBonuses(completedTech); 
            }
        }
    }

    /// <summary>
    /// Updates all city models when advancing to a new tech age
    /// </summary>
    private void UpdateCityModelsForNewAge()
    {
        foreach (var city in cities)
        {
            if (city != null)
            {
                city.UpdateCityModelForAge();
            }
        }
    }

    private void ApplyTechBonuses(TechData tech)
    {
        // Store old age to check if we advanced
        TechAge oldAge = GetCurrentAge();
        
        // Apply civilization bonuses from tech
        attackBonus += tech.attackBonus;
        defenseBonus += tech.defenseBonus;
        movementBonus += tech.movementBonus;
        foodModifier += tech.foodModifier;
        productionModifier += tech.productionModifier;
        goldModifier += tech.goldModifier;
        scienceModifier += tech.scienceModifier;
        cultureModifier += tech.cultureModifier;
        faithModifier += tech.faithModifier;

        // Unlock items from tech

        // Apply governor-related bonuses
        if (tech.additionalGovernorSlots > 0)
        {
            IncreaseGovernorCount(tech.additionalGovernorSlots);
}

        if (tech.unlockedGovernorTraits != null)
        {
            foreach (var trait in tech.unlockedGovernorTraits)
            {
                if (!unlockedGovernorTraits.Contains(trait))
                {
                    unlockedGovernorTraits.Add(trait);
}
            }
        }
        
        // Check if we advanced to a new age and update city models
        TechAge newAge = GetCurrentAge();
        if (newAge != oldAge)
        {
            UpdateCityModelsForNewAge();
        }
    }

    private void ProcessCulture()
    {
        if (currentCulture == null)
        {
            Debug.Log($"[Civilization][Culture] {civData?.civName}: No currentCulture assigned — skipping ProcessCulture. culture={culture}");
            return;
        }
        // If culture was started this turn, defer progress until the next turn
        if (cultureStartedThisTurn)
        {
            cultureStartedThisTurn = false;
            Debug.Log($"[Civilization][Culture] {civData?.civName}: culture adoption started this turn - deferring first progress tick.");
            return;
        }
        float prevProgress = currentCultureProgress;
        currentCultureProgress += culture;
        Debug.Log($"[Civilization][Culture] {civData?.civName}: culture='{currentCulture.cultureName}' cultureThisTurn={culture} prevProgress={prevProgress} newProgress={currentCultureProgress} cost={currentCulture.cultureCost} remaining={currentCulture.cultureCost - currentCultureProgress}");
        if (currentCultureProgress >= currentCulture.cultureCost)
        {
            CultureData completedCulture = currentCulture;
            // Stop further progress on this culture immediately, GameManager will null it after calling OnCultureAdopted.
            // currentCulture = null; 
            // currentCultureProgress = 0;

            // Call CultureManager to handle completion
            if (CultureManager.Instance != null)
            {
                CultureManager.Instance.CompleteCultureAdoption(this, completedCulture);
            }
            else
            {
                Debug.LogError($"Civilization {civData.civName}: CultureManager.Instance is null. Cannot complete culture adoption for {completedCulture.cultureName}.");
                // Fallback: Manually do critical parts if manager is missing (not ideal)
                OnCultureAdopted(completedCulture); // This will add to researchedCultures and apply bonuses
                currentCulture = null; // Still need to clear it here for fallback
                currentCultureProgress = 0;
            }
        }
    }

    private void ApplyCultureBonuses(CultureData cult)
    {
        attackBonus   += cult.attackBonus;
        defenseBonus  += cult.defenseBonus;
        movementBonus += cult.movementBonus;
        foodModifier += cult.foodModifier;
        productionModifier += cult.productionModifier;
        goldModifier += cult.goldModifier;
        scienceModifier += cult.scienceModifier;
        cultureModifier += cult.cultureModifier;
        faithModifier += cult.faithModifier;

        // Unlock items from culture

        // Apply governor-related bonuses
        if (cult.additionalGovernorSlots > 0)
        {
            IncreaseGovernorCount(cult.additionalGovernorSlots);
}

        if (cult.unlockedGovernorTraits != null)
        {
            foreach (var trait in cult.unlockedGovernorTraits)
            {
                if (!unlockedGovernorTraits.Contains(trait))
                {
                    unlockedGovernorTraits.Add(trait);
}
            }
        }
    }

    // --- Tech & Culture API ---
    public bool CanResearch(TechData tech)
    {
        if (tech == null) return false;
        if (currentTech != null) return false;
        if (researchedTechs.Contains(tech)) return false;
        // if (science <= 0) { Debug.Log($"[Civilization] CanResearch ({tech.techName}): Science output is <= 0."); return false; } // Usually, we allow selection even with 0 science, it just won't progress.

        foreach (var req in tech.requiredTechnologies)
        {
            if (!researchedTechs.Contains(req)) return false;
        }
        foreach (var req in tech.requiredCultures)
        {
            if (!researchedCultures.Contains(req)) return false;
        }
        if (cities.Count < tech.requiredCityCount) return false;
        // Add biome check if needed
        return true;
    }

    public void StartResearch(TechData tech)
    {
        if (!CanResearch(tech)) return;
        currentTech = tech;
        currentTechProgress = 0;
OnTechStarted?.Invoke(tech); // Fire event for UI
    }

    public bool CanCultivate(CultureData cult)
    {
        if (cult == null) return false;
        if (currentCulture != null) return false;
        if (researchedCultures.Contains(cult)) return false;
        // Cannot adopt a culture that belongs to an age we haven't unlocked via tech research
        if (cult.cultureAge > GetCurrentAge()) return false;
        // if (culture <= 0) { Debug.Log($"[Civilization] CanCultivate ({cult.cultureName}): Culture output is <= 0."); return false; }
        if (cult.requiredTechnologies != null)
        {
            foreach (var req in cult.requiredTechnologies)
            {
                if (req != null && !researchedTechs.Contains(req)) return false;
            }
        }
        foreach (var req in cult.requiredCultures)
        {
            if (!researchedCultures.Contains(req)) return false;
        }
        if (cities.Count < cult.requiredCityCount) return false;
        if (cult.requiredControlledBiomes != null)
        {
            foreach (var biome in cult.requiredControlledBiomes)
            {
                if (!HasControlledBiome(biome)) return false;
            }
        }
        return true;
    }

    public void StartCulture(CultureData cult)
    {
        if (!CanCultivate(cult)) return;
        currentCulture = cult;
        currentCultureProgress = 0;
        // Ensure first culture progress tick is deferred until next turn
        MarkCultureStartedThisTurn();
OnCultureStarted?.Invoke(cult); // Fire event for UI
    }

    // --- Policy & Government API ---
    public bool CanAdoptPolicy(PolicyData p)
        => PolicyManager.Instance.GetAvailablePolicies(this).Contains(p);

    public void AdoptPolicy(PolicyData p)
    {
        // PolicyManager.Instance.AdoptPolicy(this, p); // This would typically handle adding to activePolicies
        if (p == null || !CanAdoptPolicy(p)) return;

        if (!activePolicies.Contains(p))
        {
            activePolicies.Add(p);
            ApplyPolicyBonuses(p); // Apply bonuses when adopted
            RecalculateCachedYieldRates();
            OnPolicyAdopted?.Invoke(this, p);
            // TODO: UI update, notifications
        }
    }

    // New method to apply bonuses from a single policy
    private void ApplyPolicyBonuses(PolicyData policy)
    {
        if (policy == null) return;
        attackBonus += policy.attackBonus;
        defenseBonus += policy.defenseBonus;
        movementBonus += policy.movementBonus;
        foodModifier += policy.foodModifier;
        productionModifier += policy.productionModifier;
        goldModifier += policy.goldModifier;
        scienceModifier += policy.scienceModifier;
        cultureModifier += policy.cultureModifier;
        faithModifier += policy.faithModifier;

        // Governor slot and trait unlocks
        if (policy.additionalGovernorSlots > 0)
        {
            IncreaseGovernorCount(policy.additionalGovernorSlots);
}
        if (policy.unlockedGovernorTraits != null)
        {
            foreach (var trait in policy.unlockedGovernorTraits)
            {
                if (!unlockedGovernorTraits.Contains(trait))
                {
                    unlockedGovernorTraits.Add(trait);
}
            }
        }
    }

    // New method to remove bonuses from a policy (if policies can be revoked)
    private void RemovePolicyBonuses(PolicyData policy)
    {
        if (policy == null) return;
        attackBonus -= policy.attackBonus;
        defenseBonus -= policy.defenseBonus;
        movementBonus -= policy.movementBonus;
        foodModifier -= policy.foodModifier;
        productionModifier -= policy.productionModifier;
        goldModifier -= policy.goldModifier;
        scienceModifier -= policy.scienceModifier;
        cultureModifier -= policy.cultureModifier;
        faithModifier -= policy.faithModifier;
    }

    public bool CanChangeGovernment(GovernmentData g)
        => PolicyManager.Instance.GetAvailableGovernments(this).Contains(g);

    public void ChangeGovernment(GovernmentData g)
    {
        // PolicyManager.Instance.ChangeGovernment(this, g); // This would handle setting currentGovernment
        if (g == null || !CanChangeGovernment(g) || currentGovernment == g) return;

        // Remove bonuses from old government if one was active
        if (currentGovernment != null)
        {
            RemoveGovernmentBonuses(currentGovernment);
        }
        currentGovernment = g;
        ApplyGovernmentBonuses(g); // Apply bonuses from new government

        // Notify cities to update their available buildings
        foreach (var city in cities)
        {
            city.UpdateAvailableBuildings();
        }

        RecalculateCachedYieldRates();
        OnGovernmentChanged?.Invoke(this, g);
        // TODO: UI update, notifications
    }

    // New method to apply bonuses from a government
    private void ApplyGovernmentBonuses(GovernmentData gov)
    {
        if (gov == null) return;
        attackBonus += gov.attackBonus;
        defenseBonus += gov.defenseBonus;
        movementBonus += gov.movementBonus;
        foodModifier += gov.foodModifier;
        productionModifier += gov.productionModifier;
        goldModifier += gov.goldModifier;
        scienceModifier += gov.scienceModifier;
        cultureModifier += gov.cultureModifier;
        faithModifier += gov.faithModifier;
    }

    // New method to remove bonuses from a government
    private void RemoveGovernmentBonuses(GovernmentData gov)
    {
        if (gov == null) return;
        attackBonus -= gov.attackBonus;
        defenseBonus -= gov.defenseBonus;
        movementBonus -= gov.movementBonus;
        foodModifier -= gov.foodModifier;
        productionModifier -= gov.productionModifier;
        goldModifier -= gov.goldModifier;
        scienceModifier -= gov.scienceModifier;
        cultureModifier -= gov.cultureModifier;
        faithModifier -= gov.faithModifier;
    }

    // --- Diplomacy ---
    public void SetRelation(Civilization other, DiplomaticState state)
        => relations[other] = state;

    public float GetDiplomaticWeight()
    {
        EnsureCapitalCity();

        float cityWeight = (cities != null ? cities.Count : 0) * 2f;
        float researchWeight = (researchedTechs != null ? researchedTechs.Count : 0) * 0.35f;
        float cultureWeight = (researchedCultures != null ? researchedCultures.Count : 0) * 0.35f;

        if (capitalCity == null)
            return cityWeight + researchWeight + cultureWeight;

        return cityWeight
            + researchWeight
            + cultureWeight
            + capitalCity.level * 5f
            + capitalCity.loyalty * 0.2f
            + capitalCity.defenseRating * 0.05f;
    }

    /// <summary>
    /// Gets the appropriate building data, using unique building if available
    /// </summary>
    public BuildingData GetBuildingData(BuildingData standardBuilding)
    {
        // Check if we should use a unique building replacement
        if (uniqueBuildingReplacements.TryGetValue(standardBuilding, out BuildingData uniqueReplacement))
        {
            return uniqueReplacement;
        }
        
        return standardBuilding;
    }

    /// <summary>
    /// Given a resolved/actual building asset, return the standard archetype it replaces.
    /// If this is not a unique replacement, returns the same building.
    /// </summary>
    public BuildingData GetBaseBuildingData(BuildingData actualBuilding)
    {
        if (actualBuilding == null) return null;

        foreach (var kvp in uniqueBuildingReplacements)
        {
            if (kvp.Value == actualBuilding)
                return kvp.Key;
        }

        return actualBuilding;
    }
    
    /// <summary>
    /// Gets the appropriate unit data, using unique unit if available
    /// </summary>
    public CombatUnitData GetUnitData(CombatUnitData standardUnit)
    {
        // Check if we should use a unique unit replacement
        if (uniqueUnitReplacements.TryGetValue(standardUnit, out CombatUnitData uniqueReplacement))
        {
            return uniqueReplacement;
        }
        
        return standardUnit;
    }

    /// <summary>
    /// Given a resolved/actual combat unit asset, return the standard archetype it replaces.
    /// If this is not a unique replacement, returns the same unit.
    /// </summary>
    public CombatUnitData GetBaseUnitData(CombatUnitData actualUnit)
    {
        if (actualUnit == null) return null;

        foreach (var kvp in uniqueUnitReplacements)
        {
            if (kvp.Value == actualUnit)
                return kvp.Key;
        }

        return actualUnit;
    }
    
    /// <summary>
    /// Adds a city to this civilization's control
    /// </summary>
    public void AddCity(City city)
    {
        if (city == null) return;

        if (!cities.Contains(city))
        {
            cities.Add(city);
            if (city.owner != this)
                city.owner = this;
            EnsureCapitalCity();
            OnCityFounded?.Invoke(this, city);
        }
        else
        {
            EnsureCapitalCity();
        }
    }

    public void RemoveCity(City city)
    {
        if (city == null || cities == null) return;
        if (cities.Remove(city))
        {
            if (capitalCity == city)
                capitalCity = null;
            city.isCapital = false;
            EnsureCapitalCity();
        }
    }

    public void ReplaceCityReference(City oldCity, City newCity)
    {
        if (oldCity == null || newCity == null || cities == null) return;

        int index = cities.IndexOf(oldCity);
        if (index >= 0)
            cities[index] = newCity;
        else if (!cities.Contains(newCity))
            cities.Add(newCity);

        bool shouldBeCapital = capitalCity == oldCity || oldCity.isCapital;
        oldCity.isCapital = false;
        if (shouldBeCapital)
        {
            capitalCity = newCity;
            newCity.isCapital = true;
        }

        EnsureCapitalCity();
    }

    public void SetCapitalCity(City city)
    {
        if (city == null || city.owner != this) return;
        if (cities != null && !cities.Contains(city))
            cities.Add(city);

        capitalCity = city;
        if (cities != null)
        {
            foreach (var existingCity in cities)
            {
                if (existingCity == null) continue;
                existingCity.isCapital = existingCity == city;
            }
        }
    }

    public void EnsureCapitalCity()
    {
        if (cities == null)
        {
            capitalCity = null;
            return;
        }

        cities.RemoveAll(city => city == null);

        City resolvedCapital = null;
        if (capitalCity != null && cities.Contains(capitalCity) && capitalCity.owner == this)
        {
            resolvedCapital = capitalCity;
        }
        else
        {
            foreach (var city in cities)
            {
                if (city != null && city.owner == this && city.isCapital)
                {
                    resolvedCapital = city;
                    break;
                }
            }

            if (resolvedCapital == null)
            {
                foreach (var city in cities)
                {
                    if (city != null && city.owner == this)
                    {
                        resolvedCapital = city;
                        break;
                    }
                }
            }
        }

        capitalCity = resolvedCapital;

        foreach (var city in cities)
        {
            if (city == null) continue;
            city.isCapital = city == capitalCity;
        }
    }
    
    /// <summary>
    /// Add resources to the civilization's stockpile
    /// </summary>
    public void AddResource(ResourceData resource, int amount)
    {
        if (resource == null || amount <= 0) return;
        
        if (!resourceStockpile.ContainsKey(resource))
            resourceStockpile[resource] = 0;
            
        resourceStockpile[resource] += amount;
        
        // Notify any UI or other systems
        OnResourceChanged?.Invoke(resource, resourceStockpile[resource]);
    }
    
    /// <summary>
    /// Add an interplanetary trade route
    /// </summary>
    public void AddTradeRoute(TradeRoute route)
    {
        if (route != null && route.isInterplanetaryRoute)
        {
            interplanetaryTradeRoutes.Add(route);
        }
    }
    
    /// <summary>
    /// Get all interplanetary trade routes for this civilization
    /// </summary>
    public List<TradeRoute> GetInterplanetaryTradeRoutes()
    {
        return interplanetaryTradeRoutes;
    }
    
    /// <summary>
    /// Get total gold income from all interplanetary trade routes
    /// </summary>
    public int GetInterplanetaryTradeIncome()
    {
        int totalGold = 0;
        foreach (var route in interplanetaryTradeRoutes)
        {
            if (route != null && route.isInterplanetaryRoute)
                totalGold += route.goldPerTurn;
        }
        return totalGold;
    }
    
    /// <summary>
    /// Remove resources from the civilization's stockpile
    /// </summary>
    public bool ConsumeResource(ResourceData resource, int amount)
    {
        if (resource == null || amount <= 0) return true; // Nothing to consume
        
        if (!resourceStockpile.ContainsKey(resource) || resourceStockpile[resource] < amount)
            return false; // Not enough resources
            
        resourceStockpile[resource] -= amount;
        
        // Notify any UI or other systems
        OnResourceChanged?.Invoke(resource, resourceStockpile[resource]);
        return true;
    }
    
    /// <summary>
    /// Get current amount of a resource in stockpile
    /// </summary>
    public int GetResourceCount(ResourceData resource)
    {
        if (resource == null || !resourceStockpile.ContainsKey(resource))
            return 0;
            
        return resourceStockpile[resource];
    }

    /// <summary>
    /// Attempt to found a Pantheon (requires enough faith and prerequisite tech).
    /// </summary>
    public bool FoundPantheon(PantheonData pantheon)
    {
        // Check if the civilization meets a pantheon founding prereq:
        // either a tech that unlocks religion or an adopted culture that unlocks pantheons.
        bool hasPantheonPrereq = false;
        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech != null && tech.unlocksReligion)
                {
                    hasPantheonPrereq = true;
                    break;
                }
            }
        }
        // Also allow cultures to enable pantheon founding
        if (!hasPantheonPrereq && researchedCultures != null)
        {
            foreach (var cult in researchedCultures)
            {
                if (cult != null && cult.unlocksPantheon)
                {
                    hasPantheonPrereq = true;
                    break;
                }
            }
        }

        if (!hasPantheonPrereq)
        {
return false;
        }
        
        // Check pantheon cap
        if (!CanFoundMorePantheons())
        {
return false;
        }
        
        // Check if has enough faith
        if (faith < pantheon.faithCost)
        {
return false;
        }
        
        // Found the pantheon: pay faith cost and add to list
        faith -= pantheon.faithCost;
        if (foundedPantheons == null) foundedPantheons = new List<PantheonData>();
        foundedPantheons.Add(pantheon);

        // Recompute belief/faith modifiers and notify
        UpdateFaithYieldModifier();
        OnPantheonFounded?.Invoke(this, pantheon);
        return true;
    }
    
    /// <summary>
    /// Attempt to found a Religion (requires pantheon, holy site, and enough faith).
    /// </summary>
    public bool FoundReligion(ReligionData religion, City holySiteCity)
    {
        // Check prerequisites: civ must have founded the required pantheon
        if (foundedPantheons == null || !foundedPantheons.Contains(religion.requiredPantheon))
        {
return false;
        }
        
        if (hasFoundedReligion || foundedReligion != null)
        {
return false;
        }
        
        if (faith < religion.faithCost)
        {
return false;
        }
        
        // Check if the city has a Holy Site
        bool hasHolySite = false;
        
        // Get the hex tile data for the city's center tile (planet-aware)
        var tsHS = (holySiteCity != null) ? (TileSystem.GetForPlanet(holySiteCity.planetIndex) ?? TileSystem.Instance) : TileSystem.Instance;
        var tileDataHS = tsHS != null ? tsHS.GetTileData(holySiteCity.centerTileIndex) : null;
        if (tileDataHS != null)
        {
            hasHolySite = tileDataHS.HasHolySite;
        }
        
        if (!hasHolySite)
        {
return false;
        }
        
        // Found the religion
        faith -= religion.faithCost;
        foundedReligion = religion;
        hasFoundedReligion = true;
        
        // Apply any additional faith yield modifiers
        UpdateFaithYieldModifier();

        // Notify cities to update their available buildings
        foreach (var city in cities)
        {
            city.UpdateAvailableBuildings();
        }
return true;
    }

    /// <summary>
    /// Upgrade an existing founded pantheon (spirit) into its upgraded pantheon (God), if available.
    /// </summary>
    public bool UpgradePantheon(PantheonData spiritPantheon)
    {
        if (spiritPantheon == null) return false;
        if (foundedPantheons == null || !foundedPantheons.Contains(spiritPantheon)) return false;
        if (!spiritPantheon.IsSpirit || !spiritPantheon.canUpgradeToGod || spiritPantheon.upgradedPantheon == null) return false;

        var god = spiritPantheon.upgradedPantheon;

        // Replace in the list, preserving order (replace first occurrence)
        int idx = foundedPantheons.IndexOf(spiritPantheon);
        if (idx < 0) return false;

        foundedPantheons[idx] = god;

        // Recompute belief-based modifiers
        UpdateFaithYieldModifier();
        return true;
    }
    
    /// <summary>
    /// Update faith yield modifier based on pantheon and religion beliefs
    /// </summary>
    private void UpdateFaithYieldModifier() // Renaming and repurposing for Beliefs
    {
        RecalculateCivilizationModifiers();
        RefreshUnlockedContentLists();
        InvalidateAvailabilityCache();
        NotifyBeliefsChanged();
    }
    
    /// <summary>
    /// Purchase a missionary unit with faith in the specified city
    /// </summary>
    public bool PurchaseMissionary(ReligionUnitData missionaryData, City city)
    {
        if (!hasFoundedReligion || foundedReligion == null)
        {
return false;
        }
        
        if (faith < missionaryData.faithCost)
        {
return false;
        }
        
        // Check if the city has a Holy Site
        bool hasHolySite = false;
        
        // Get the hex tile data for the city's center tile (planet-aware)
        var tsMS = (city != null) ? (TileSystem.GetForPlanet(city.planetIndex) ?? TileSystem.Instance) : TileSystem.Instance;
        var tileDataMS = tsMS != null ? tsMS.GetTileData(city.centerTileIndex) : null;
        if (tileDataMS != null)
        {
            hasHolySite = tileDataMS.HasHolySite;
        }
        
        if (!hasHolySite)
        {
return false;
        }
        
        // Deduct faith cost
        faith -= missionaryData.faithCost;
        
        // Instantiate the missionary unit
        var grid = planetGenerator != null ? planetGenerator.Grid : null;
        if (grid != null)
        {
            Vector3 pos = tsMS != null ? tsMS.GetTileCenterFlat(city.centerTileIndex) : Vector3.zero;
            var missionaryPrefab = missionaryData.GetPrefab(this);
            if (missionaryPrefab == null)
            {
                Debug.LogError($"[Civilization] Cannot spawn missionary {missionaryData.unitName}: prefab not found in Addressables. Make sure prefab is marked as Addressable with address matching unitName.");
                return false;
            }
            
            var missionaryGO = Instantiate(missionaryPrefab, pos, Quaternion.identity);
            var missionaryUnit = missionaryGO.GetComponent<CombatUnit>();
            if (missionaryUnit == null)
            {
                Debug.LogError($"[Civilization] Spawned prefab for {missionaryData.unitName} is missing CombatUnit component.");
                Destroy(missionaryGO);
                return false;
            }
            missionaryUnit.Initialize(missionaryData, this);
            missionaryUnit.planetIndex = (city != null) ? city.planetIndex : 0;
            
            // Set tile index and register occupancy
            if (missionaryUnit.currentTileIndex < 0)
            {
                missionaryUnit.currentTileIndex = city.centerTileIndex;
            }
            try { missionaryUnit.RegisterToRegistry(); } catch { }
            var occ = TileOccupancyManager.GetForPlanet(missionaryUnit.planetIndex) ?? TileOccupancyManager.Instance;
            if (occ != null)
            {
                occ.SetOccupant(missionaryUnit.currentTileIndex, missionaryGO, missionaryUnit.currentLayer);
            }
            combatUnits.Add(missionaryUnit);

            // Fog of War: immediately refresh vision for this civ after spawning a unit.
            if (UnitVisionManager.Instance != null)
            {
                UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(this));
            }
            
            // The missionary unit should have the civilization's religion associated with it
            // This would be handled by a specialized ReligionUnit component or by adding properties to CombatUnit
return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get the faith cost to found a pantheon, taking into account any modifiers
    /// </summary>
    public int GetPantheonCost(PantheonData pantheon)
    {
        if (pantheon == null) return 0;
        return pantheon.faithCost;
    }
    
    /// <summary>
    /// Get the faith cost to found a religion, taking into account any modifiers
    /// </summary>
    public int GetReligionCost(ReligionData religion)
    {
        if (religion == null) return 0;
        return religion.faithCost;
    }

    public void RecalculateCachedYieldRates()
    {
        int totalScienceThisTurn = 0;
        int totalCultureThisTurn = 0;
        int totalGoldThisTurn = 0;
        int totalFoodThisTurn = 0;
        int totalPolicyThisTurn = 0;
        int totalFaithThisTurn = 0;
        var globalBonuses = CalculateTotalBonuses(researchedTechs, researchedCultures);

        if (cities != null)
        {
            foreach (var city in cities)
            {
                if (city == null) continue;
                totalGoldThisTurn += Mathf.RoundToInt(city.GetGoldPerTurn() * (1 + goldModifier));
                totalFoodThisTurn += Mathf.RoundToInt(city.GetFoodPerTurn() * (1 + foodModifier));
                totalScienceThisTurn += Mathf.RoundToInt(city.GetSciencePerTurn() * (1 + scienceModifier));
                totalCultureThisTurn += Mathf.RoundToInt(city.GetCulturePerTurn() * (1 + cultureModifier));
                totalPolicyThisTurn += city.GetPolicyPointPerTurn();
                totalFaithThisTurn += Mathf.RoundToInt(city.GetFaithPerTurn() * (1 + faithModifier));
            }
        }

        if (interplanetaryTradeRoutes != null)
        {
            foreach (var tradeRoute in interplanetaryTradeRoutes)
            {
                if (tradeRoute != null && tradeRoute.isInterplanetaryRoute)
                    totalGoldThisTurn += Mathf.RoundToInt(tradeRoute.goldPerTurn * (1 + goldModifier));
            }
        }

        if (combatUnits != null)
        {
            int addFood = 0, addGold = 0, addSci = 0, addCul = 0, addFai = 0, addPol = 0;
            foreach (var u in combatUnits)
            {
                if (u == null || u.data == null) continue;
                var yields = ComputeUnitPerTurnYield(u.data, u.planetIndex, u.Weapon, u.Shield, u.Armor, u.Miscellaneous);
                addFood += yields.food;
                addGold += yields.gold;
                addSci += yields.science;
                addCul += yields.culture;
                addFai += yields.faith;
                addPol += yields.policy;

                if (u.IsInOrbit)
                {
                    var ts = TileSystem.GetForPlanet(u.planetIndex) ?? TileSystem.Instance;
                    var tileData = ts != null ? ts.GetTileData(u.currentTileIndex) : null;
                    if (tileData != null)
                    {
                        var tileYield = tileData.GetTotalYield();
                        addFood += tileYield.Food;
                        addGold += tileYield.Gold;
                        addSci += tileYield.Science;
                        addCul += tileYield.Culture;
                        addFai += tileYield.Faith;
                        addPol += tileYield.Policy;
                    }
                }
            }

            totalGoldThisTurn += Mathf.RoundToInt(addGold * (1 + goldModifier));
            totalFoodThisTurn += Mathf.RoundToInt(addFood * (1 + foodModifier));
            totalScienceThisTurn += Mathf.RoundToInt(addSci * (1 + scienceModifier));
            totalCultureThisTurn += Mathf.RoundToInt(addCul * (1 + cultureModifier));
            totalFaithThisTurn += Mathf.RoundToInt(addFai * (1 + faithModifier));
            totalPolicyThisTurn += addPol;
        }

        if (workerUnits != null)
        {
            int addFood = 0, addGold = 0, addSci = 0, addCul = 0, addFai = 0, addPol = 0;
            foreach (var w in workerUnits)
            {
                if (w == null || w.data == null) continue;
                var yields = ComputeWorkerPerTurnYield(w.data, w.planetIndex);
                addFood += yields.food;
                addGold += yields.gold;
                addSci += yields.science;
                addCul += yields.culture;
                addFai += yields.faith;
                addPol += yields.policy;
            }

            totalGoldThisTurn += Mathf.RoundToInt(addGold * (1 + goldModifier));
            totalFoodThisTurn += Mathf.RoundToInt(addFood * (1 + foodModifier));
            totalScienceThisTurn += Mathf.RoundToInt(addSci * (1 + scienceModifier));
            totalCultureThisTurn += Mathf.RoundToInt(addCul * (1 + cultureModifier));
            totalFaithThisTurn += Mathf.RoundToInt(addFai * (1 + faithModifier));
            totalPolicyThisTurn += addPol;
        }

        if (herds != null)
        {
            foreach (var h in herds)
            {
                if (h == null) continue;
                var ay = h.GetAnimalYields();
                totalGoldThisTurn += Mathf.RoundToInt(ay.Gold * (1 + goldModifier));
                totalFoodThisTurn += Mathf.RoundToInt(ay.Food * (1 + foodModifier));
                totalScienceThisTurn += Mathf.RoundToInt(ay.Science * (1 + scienceModifier));
                totalCultureThisTurn += Mathf.RoundToInt(ay.Culture * (1 + cultureModifier));
                totalFaithThisTurn += Mathf.RoundToInt(ay.Faith * (1 + faithModifier));
                totalPolicyThisTurn += ay.Policy;
            }
        }

        totalGoldThisTurn += globalBonuses.flatGoldBonus;
        totalFoodThisTurn += globalBonuses.flatFoodBonus;
        totalScienceThisTurn += globalBonuses.flatScienceBonus;
        totalCultureThisTurn += globalBonuses.flatCultureBonus;
        totalFaithThisTurn += globalBonuses.flatFaithBonus;

        int totalFoodConsumption = 0;
        if (combatUnits != null)
        {
            foreach (var u in combatUnits)
                totalFoodConsumption += (u != null && u.data != null) ? u.data.foodConsumptionPerTurn : defaultFoodPerCombatUnit;
        }
        if (workerUnits != null)
        {
            foreach (var w in workerUnits)
                totalFoodConsumption += (w != null && w.data != null) ? w.data.foodConsumptionPerTurn : defaultFoodPerWorkerUnit;
        }
        if (cities != null)
        {
            foreach (var city in cities)
                if (city != null) totalFoodConsumption += city.GetFoodConsumptionPerTurn();
        }

        cachedGoldPerTurn = totalGoldThisTurn;
        cachedFoodPerTurn = totalFoodThisTurn;
        cachedSciencePerTurn = totalScienceThisTurn;
        cachedCulturePerTurn = totalCultureThisTurn;
        cachedPolicyPerTurn = totalPolicyThisTurn;
        cachedFaithPerTurn = totalFaithThisTurn;
        cachedFoodConsumption = totalFoodConsumption;
    }

    public void HandleTechResearched(TechData tech)  // Renamed from OnTechResearched
    {
        if (tech == null) return;

        // Add tech to researched list if not already there
        if (!researchedTechs.Contains(tech))
            researchedTechs.Add(tech);

        // Apply tech bonuses
        ApplyTechBonuses(tech);
        
        // City-cap increase from this technology (enables settlement when first >0 is researched)
        if (tech.cityCapIncrease != 0)
        {
            cityCapFromBonuses = Mathf.Max(0, cityCapFromBonuses + tech.cityCapIncrease);
        }
        // Pantheon cap increase
        if (tech.pantheonCapIncrease != 0)
        {
            pantheonCapFromBonuses = Mathf.Max(0, pantheonCapFromBonuses + tech.pantheonCapIncrease);
        }

        // Update city models if this tech changes the age
        UpdateCityModelsForNewAge();

        // Invalidate availability cache
        InvalidateAvailabilityCache();

        // Refresh derived stats and caches across the civ after research completes
        try
        {
            // Units/workers: update only health cap safely (do not refill points mid-turn)
            if (combatUnits != null)
                foreach (var u in combatUnits)
                    if (u != null) u.OnCivBonusesChanged();
            if (workerUnits != null)
                foreach (var w in workerUnits)
                    if (w != null) w.OnCivBonusesChanged();

            // Cities: invalidate caches and update available buildings (for new unlocks)
            if (cities != null)
                foreach (var c in cities)
                    if (c != null)
                    {
                        c.RefreshGovernorBonuses();
                        c.UpdateAvailableBuildings();
                    }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Civilization] Refresh after tech research threw: {ex}");
        }

        // Ensure flat all-workers work point bonuses are applied to already-spawned workers
        ApplyAllWorkersWorkPointsToExisting();

        RecalculateCachedYieldRates();

        // Invoke the event after caches are refreshed so the HUD shows updated rates immediately.
        OnTechResearched?.Invoke(tech);

        // Notify listeners that unlock-driven availability may have changed
        OnUnlocksChanged?.Invoke();

        // If this tech enables herding, toggle the civ-level flag and notify player
        if (tech.enablesHerding)
        {
            herdsEnabled = true;
            UIManager.Instance?.ShowNotification($"{(civData!=null?civData.civName:"A civ")} has unlocked Herding!");
        }
        // Add any governments unlocked by this tech to the civ's unlocked governments list
        if (tech.unlockedGovernments != null && tech.unlockedGovernments.Length > 0)
        {
            if (unlockedGovernments == null) unlockedGovernments = new List<GovernmentData>();
            foreach (var g in tech.unlockedGovernments)
            {
                if (g != null && !unlockedGovernments.Contains(g)) unlockedGovernments.Add(g);
            }
        }
    }

    // Compute aggregated flat work points granted to ALL workers by techs/cultures/policies/government
    public int GetAggregatedAllWorkersWorkPoints()
    {
        int total = 0;
        if (researchedTechs != null)
        {
            foreach (var t in researchedTechs)
                if (t != null) total += t.allWorkersWorkPoints;
        }
        if (researchedCultures != null)
        {
            foreach (var c in researchedCultures)
                if (c != null) total += c.allWorkersWorkPoints;
        }
    // Note: policies and government currently do not expose allWorkersWorkPoints
    // If they gain that field in the future, include them here.
        return total;
    }

    // Apply current aggregated all-worker flat bonuses to all existing WorkerUnit instances
    private void ApplyAllWorkersWorkPointsToExisting()
    {
        int flat = GetAggregatedAllWorkersWorkPoints();
        if (flat == 0) return;
        if (workerUnits == null) return;

        foreach (var w in workerUnits)
        {
            if (w == null) continue;
            // Prefer an explicit API on WorkerUnit to receive civ-level updates
            try {
                w.OnCivBonusesChanged(); // allow worker to recompute its effective work points
                // Also ensure any persistent field is adjusted if WorkerUnit exposes one
                // e.g., if WorkerUnit has AddTemporaryWorkPoints(int), call it here. We'll rely on OnCivBonusesChanged for now.
            } catch (System.Exception ex) {
                Debug.LogWarning($"[Civilization] Failed to apply allWorkersWorkPoints to worker {w.name}: {ex}");
            }
        }
    }
    
    /// <summary>
    /// Called when a culture is fully adopted (e.g., by CultureManager)
    /// </summary>
    public void OnCultureAdopted(CultureData cult)
    {
        if (cult == null) return;

        // Add to researched cultures if not already there
        if (!researchedCultures.Contains(cult))
        {
            researchedCultures.Add(cult);
        }

        // Apply bonuses from the adopted culture
        ApplyCultureBonuses(cult);

        // Invalidate availability cache
        InvalidateAvailabilityCache();

        // Apply culture unlocks for religion/pantheons
        if (cult.unlocksPantheons != null)
        {
            if (cultureUnlockedPantheons == null) cultureUnlockedPantheons = new List<PantheonData>();
            foreach (var p in cult.unlocksPantheons)
            {
                if (p != null && !cultureUnlockedPantheons.Contains(p)) cultureUnlockedPantheons.Add(p);
            }
        }
        if (cult.unlocksBeliefs != null)
        {
            if (cultureUnlockedBeliefs == null) cultureUnlockedBeliefs = new List<BeliefData>();
            foreach (var b in cult.unlocksBeliefs)
            {
                if (b != null && !cultureUnlockedBeliefs.Contains(b)) cultureUnlockedBeliefs.Add(b);
            }
        }

        // Add any governments unlocked by this culture to the civ's unlocked governments list
        if (cult.unlockedGovernments != null && cult.unlockedGovernments.Length > 0)
        {
            if (unlockedGovernments == null) unlockedGovernments = new List<GovernmentData>();
            foreach (var g in cult.unlockedGovernments)
            {
                if (g != null && !unlockedGovernments.Contains(g)) unlockedGovernments.Add(g);
            }
        }

        // Cities might need to update their buildable units/buildings if culture unlocks them
        if (cities != null)
        {
            foreach (var city in cities)
            {
                if (city != null)
                {
                    city.UpdateAvailableBuildings(); // And potentially units
                }
            }
        }

        // Refresh derived stats and caches across the civ after culture adoption
        try
        {
            if (combatUnits != null)
                foreach (var u in combatUnits)
                    if (u != null) u.OnCivBonusesChanged();
            if (workerUnits != null)
                foreach (var w in workerUnits)
                    if (w != null) w.OnCivBonusesChanged();
            if (cities != null)
                foreach (var c in cities)
                    if (c != null) c.RefreshGovernorBonuses();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Civilization] Refresh after culture adoption threw: {ex}");
        }

        // Ensure flat all-workers work point bonuses are applied to already-spawned workers
        ApplyAllWorkersWorkPointsToExisting();

        RecalculateCachedYieldRates();

        // Trigger the event for other systems (like UI) after caches are refreshed.
        OnCultureCompleted?.Invoke(cult); 

        // Notify listeners that unlock-driven availability may have changed
        OnUnlocksChanged?.Invoke();

        // If this culture enables the trade system, enable it for this civ and notify player
        if (cult.enablesTradeSystem)
        {
            tradeEnabled = true;
            UIManager.Instance?.ShowNotification($"{civData.civName} has unlocked the Trade system!");
        }
        // If this culture enables the governor mechanic, enable it for this civ and notify player
        if (cult.enablesGovernors)
        {
            governorsEnabled = true;
            UIManager.Instance?.ShowNotification($"{civData.civName} has unlocked Governors!");
        }
        // Apply pantheon cap increase from culture
        if (cult.pantheonCapIncrease != 0)
        {
            pantheonCapFromBonuses = Mathf.Max(0, pantheonCapFromBonuses + cult.pantheonCapIncrease);
        }
    }

    // --- NEW: Equipment Inventory Methods ---
    
    /// <summary>
    /// Add equipment to the civilization's inventory
    /// </summary>
    public void AddEquipment(EquipmentData equipment, int count = 1)
    {
        if (equipment == null || count <= 0) return;
        
        if (!equipmentInventory.ContainsKey(equipment))
            equipmentInventory[equipment] = 0;
            
        equipmentInventory[equipment] += count;
        _canEquipByUnitTypeCache.Clear();
        
        // Notify listeners
        OnEquipmentChanged?.Invoke(equipment, equipmentInventory[equipment]);
}

    /// <summary>
    /// Produce equipment and add it to inventory (consumes production cost)
    /// </summary>
    public bool ProduceEquipment(EquipmentData equipment, int count = 1)
    {
        if (equipment == null || count <= 0) return false;
        
        // Check if we can produce this equipment
        if (!equipment.CanBeProducedBy(this))
        {
            Debug.LogWarning($"{civData.civName} cannot produce {equipment.equipmentName} - requirements not met");
            return false;
        }
        
        // Calculate total cost
        int totalCost = equipment.productionCost * count;
        
        // Check if we have enough gold
        if (totalCost > 0 && gold < totalCost)
        {
            Debug.LogWarning($"{civData.civName} cannot produce {equipment.equipmentName} - not enough gold ({gold}/{totalCost})");
            return false;
        }
        
        // Deduct production cost
        if (totalCost > 0)
        {
            gold -= totalCost;
}
        
        // Add equipment to inventory
        AddEquipment(equipment, count);
        
        return true;
    }
    
    /// <summary>
    /// Consume equipment from the civilization's inventory
    /// </summary>
    public bool ConsumeEquipment(EquipmentData equipment, int count = 1)
    {
        if (equipment == null || count <= 0) return true; // Nothing to consume
        
        if (!equipmentInventory.ContainsKey(equipment) || equipmentInventory[equipment] < count)
        {
            Debug.LogWarning($"{civData.civName} does not have enough {equipment.equipmentName} in inventory");
            return false; // Not enough equipment
        }
            
        equipmentInventory[equipment] -= count;
        _canEquipByUnitTypeCache.Clear();
        
        // Notify listeners
        OnEquipmentChanged?.Invoke(equipment, equipmentInventory[equipment]);
return true;
    }
    
    /// <summary>
    /// Get the count of a specific equipment in inventory
    /// </summary>
    public int GetEquipmentCount(EquipmentData equipment)
    {
        if (equipment == null || !equipmentInventory.ContainsKey(equipment))
            return 0;
            
        return equipmentInventory[equipment];
    }
    
    /// <summary>
    /// Check if the civilization has enough of the equipment in inventory
    /// </summary>
    public bool HasEquipment(EquipmentData equipment, int count = 1)
    {
        return GetEquipmentCount(equipment) >= count;
    }
    
    // ===================== PROJECTILE INVENTORY MANAGEMENT =====================
    
    /// <summary>
    /// Adds projectiles to the civilization's inventory
    /// </summary>
    public void AddProjectile(GameCombat.ProjectileData projectile, int count = 1)
    {
        if (projectile == null || count <= 0) return;
        
        if (!projectileInventory.ContainsKey(projectile))
            projectileInventory[projectile] = 0;
        
        projectileInventory[projectile] += count;
        OnProjectileChanged?.Invoke(projectile, projectileInventory[projectile]);
        
        if (isPlayerControlled)
        {
}
    }
    
    /// <summary>
    /// Produces projectiles (checks resources and adds to inventory)
    /// </summary>
    public bool ProduceProjectile(GameCombat.ProjectileData projectile, int count = 1)
    {
        if (projectile == null || count <= 0) return false;
        
        // Check if can be produced
        if (!projectile.CanBeProducedBy(this))
        {
            Debug.LogWarning($"{civData.civName} cannot produce {projectile.projectileName} - requirements not met!");
            return false;
        }
        
        // Consume required resources
        if (projectile.requiredResources != null)
        {
            foreach (var resource in projectile.requiredResources)
            {
                if (resource != null)
                {
                    if (!ConsumeResource(resource, count))
                    {
                        Debug.LogWarning($"{civData.civName} lacks {resource.resourceName} to produce {projectile.projectileName}!");
                        return false;
                    }
                }
            }
        }
        
        // Add to inventory
        AddProjectile(projectile, count);
        return true;
    }
    
    /// <summary>
    /// Consumes projectiles from inventory (not implemented yet - for future ammo consumption)
    /// </summary>
    public bool ConsumeProjectile(GameCombat.ProjectileData projectile, int count = 1)
    {
        if (projectile == null || count <= 0) return false;
        
        if (!projectileInventory.ContainsKey(projectile) || projectileInventory[projectile] < count)
        {
            Debug.LogWarning($"{civData.civName} doesn't have enough {projectile.projectileName}! Need {count}, have {GetProjectileCount(projectile)}");
            return false;
        }
        
        projectileInventory[projectile] -= count;
        OnProjectileChanged?.Invoke(projectile, projectileInventory[projectile]);
        
        // Remove from dictionary if depleted
        if (projectileInventory[projectile] <= 0)
            projectileInventory.Remove(projectile);
        
        return true;
    }
    
    /// <summary>
    /// Gets the count of a specific projectile type in inventory
    /// </summary>
    public int GetProjectileCount(GameCombat.ProjectileData projectile)
    {
        if (projectile == null) return 0;
        return projectileInventory.ContainsKey(projectile) ? projectileInventory[projectile] : 0;
    }
    
    /// <summary>
    /// Checks if the civilization has at least the specified count of this projectile
    /// </summary>
    public bool HasProjectile(GameCombat.ProjectileData projectile, int count = 1)
    {
        if (projectile == null) return false;
        return GetProjectileCount(projectile) >= count;
    }
    
    /// <summary>
    /// Gets all available projectiles for a specific category
    /// </summary>
    public List<GameCombat.ProjectileData> GetAvailableProjectiles(GameCombat.ProjectileCategory category)
    {
        var available = new List<GameCombat.ProjectileData>();
        
        foreach (var kvp in projectileInventory)
        {
            if (kvp.Key != null && kvp.Key.category == category && kvp.Value > 0)
            {
                available.Add(kvp.Key);
            }
        }
        
        return available;
    }
    
    /// <summary>
    /// Equip a unit with an item from the civilization's inventory
    /// </summary>
    public bool EquipUnit(CombatUnit unit, EquipmentData equipment)
    {
        if (unit == null || equipment == null)
            return false;
            
        // Check if the unit belongs to this civilization
        if (!combatUnits.Contains(unit))
        {
            Debug.LogWarning($"Cannot equip unit: {unit.name} does not belong to {civData.civName}");
            return false;
        }
        
        // Check if we have the equipment in stock
        if (!HasEquipment(equipment))
        {
            Debug.LogWarning($"Cannot equip unit: {civData.civName} does not have {equipment.equipmentName} in inventory");
            return false;
        }
        
        // Validate that the equipment is suitable for this unit
        if (!equipment.IsValidForUnit(unit, this))
        {
            Debug.LogWarning($"Cannot equip unit: {equipment.equipmentName} is not valid for {unit.data.unitName}");
            return false;
        }
        
        // Get the currently equipped item of this type (if any)
        EquipmentData currentEquipment = null;
        
        switch (equipment.equipmentType)
        {
            case EquipmentType.Weapon:
                currentEquipment = unit.Weapon;
                break;
            case EquipmentType.Shield:
                currentEquipment = unit.Shield;
                break;
            case EquipmentType.Armor:
                currentEquipment = unit.Armor;
                break;
            case EquipmentType.Miscellaneous:
                currentEquipment = unit.Miscellaneous;
                break;
        }
        
        // Consume the new equipment from inventory FIRST
        if (!ConsumeEquipment(equipment))
        {
            Debug.LogError($"Failed to consume {equipment.equipmentName} from inventory");
            return false;
        }
        
        // Return the existing equipment to inventory if any
        if (currentEquipment != null)
        {
            AddEquipment(currentEquipment);
        }
        
        // Equip the unit with the new item
        unit.EquipItem(equipment);
return true;
    }
    
    /// <summary>
    /// Get equipment from the unit and return it to inventory
    /// </summary>
    public void UnequipUnit(CombatUnit unit, EquipmentType equipmentType)
    {
        if (unit == null)
            return;
            
        // Check if the unit belongs to this civilization
        if (!combatUnits.Contains(unit))
        {
            Debug.LogWarning($"Cannot unequip unit: {unit.name} does not belong to {civData.civName}");
            return;
        }
        
        // Get the currently equipped item of this type (if any)
        EquipmentData currentEquipment = null;
        
        switch (equipmentType)
        {
            case EquipmentType.Weapon:
                currentEquipment = unit.Weapon;
                break;
            case EquipmentType.Shield:
                currentEquipment = unit.Shield;
                break;
            case EquipmentType.Armor:
                currentEquipment = unit.Armor;
                break;
            case EquipmentType.Miscellaneous:
                currentEquipment = unit.Miscellaneous;
                break;
        }
        
        // Return the existing equipment to inventory if any
        if (currentEquipment != null)
        {
            AddEquipment(currentEquipment);
            unit.UnequipItem(equipmentType);
}
    }

    /// <summary>
    /// Creates a new city for this civilization at the specified tile.
    /// This is now the primary method for founding cities.
    /// </summary>
    /// <param name="tileIndex">The tile where the city will be founded.</param>
    public void FoundNewCity(int tileIndex, HexGrid gridOverride = null, PlanetGenerator planetOverride = null)
    {
// City-cap gating
        if (!CanFoundMoreCities())
        {
            Debug.LogWarning($"[{civData?.civName ?? "Civ"}] cannot found a new city: at city cap ({cities?.Count ?? 0}/{CurrentCityCap}).");
            return;
        }
        if (cityPrefab == null)
        {
            Debug.LogError("[FoundNewCity] City prefab not assigned to civilization!");
            return;
        }

        // Create the city game object from prefab (model and logic are the same)
        GameObject cityGO = null;
        try {
            cityGO = Instantiate(cityPrefab);
} catch (System.Exception ex) {
            Debug.LogError($"[FoundNewCity] Exception during Instantiate: {ex}");
            return;
        }
        if (cityGO == null)
        {
            Debug.LogError("[FoundNewCity] Instantiated city GameObject is null!");
            return;
        }

        City newCity = cityGO.GetComponent<City>();
        if (newCity == null)
        {
            Debug.LogError("[FoundNewCity] City prefab is missing the City component!");
            Destroy(cityGO);
            return;
        }
// Set references for correct world context
        HexGrid gridToUse = gridOverride ?? planetGrid;
        PlanetGenerator planetToUse = planetOverride ?? planetGenerator;
        if (gridToUse == null) {
            var currentPlanet = GameManager.Instance?.GetCurrentPlanetGenerator();
            gridToUse = currentPlanet?.Grid;
        }
        if (planetToUse == null)
            planetToUse = GameManager.Instance?.GetCurrentPlanetGenerator();
        // City class sets its own references now

        // Keep hierarchy organized: parent the city under its planet generator so it doesn't "hang out" at scene root.
        if (planetToUse != null)
        {
            cityGO.transform.SetParent(planetToUse.transform, true);
        }
        // Register city GameObject with HexMapChunkManager so it follows world-wrap columns
        try {
            var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetToUse);
            if (mgr != null) mgr.RegisterObjectForWrapAtTile(tileIndex, cityGO);
        } catch { }
// --- Position and orient the city on the correct tile ---
        if (gridToUse != null)
        {
            Vector3 tileCenter = gridToUse.tileCenters[tileIndex];
            Vector3 planetCenter = planetToUse.transform.position;
            Vector3 surfaceNormal = (tileCenter - planetCenter).normalized;
            float planetRadius = planetToUse.transform.localScale.x * 0.5f;
            // Slightly above surface
            float baseOffset = 0.1f;
            Vector3 surfacePosition = planetCenter + surfaceNormal * (planetRadius + baseOffset);
            cityGO.transform.position = surfacePosition;

            // Orient city to stand upright on the surface
            Vector3 planetUp = planetToUse.transform.up;
            Vector3 right = Vector3.Cross(planetUp, surfaceNormal);
            if (right.sqrMagnitude < 0.01f)
            {
                right = Vector3.Cross(Vector3.forward, surfaceNormal);
                if (right.sqrMagnitude < 0.01f)
                {
                    right = Vector3.Cross(Vector3.right, surfaceNormal);
                }
            }
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, surfaceNormal).normalized;
            cityGO.transform.rotation = Quaternion.LookRotation(forward, surfaceNormal);
        }
        else
        {
            Debug.LogWarning("[FoundNewCity] gridToUse is null, city will not be positioned correctly!");
        }

        // --- Determine City Name ---
        string cityName;
        var existingCityNames = cities.Select(c => c.gameObject.name).ToList(); 
        string newNameFromList = civData.cityNames?.FirstOrDefault(name => !existingCityNames.Contains(name));
        if (!string.IsNullOrEmpty(newNameFromList))
        {
            cityName = newNameFromList;
        }
        else
        {
            if (cities.Count == 0 && !string.IsNullOrEmpty(civData.civName))
            {
                cityName = civData.civName;
            }
            else
            {
                cityName = $"{civData.civName} City {cities.Count + 1}";
            }
        }
        newCity.centerTileIndex = tileIndex;
        // Multi-planet: persist which planet this city belongs to so it doesn't read/write the wrong TileSystem later.
        newCity.planetIndex = (planetToUse != null) ? planetToUse.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        newCity.Initialize(cityName, this);
        AddCity(newCity);
    }

    public List<EquipmentData> GetAvailableEquipment()
    {
        return equipmentInventory.Keys.ToList();
    }

    // --- Improvements availability & obsolescence helpers ---
    /// <summary>
    /// Returns all improvements unlocked by researched technologies.
    /// Note: Cultures currently do not unlock improvements directly.
    /// </summary>
    public List<ImprovementData> GetUnlockedImprovements()
    {
        var result = new HashSet<ImprovementData>();
        foreach (var imp in ResourceCache.GetAllImprovements())
        {
            if (imp != null && imp.AreRequirementsMet(this))
                result.Add(imp);
        }
        return result.ToList();
    }

    /// <summary>
    /// For a given worker archetype, compute which improvements should be considered obsolete
    /// because the civ has unlocked a replacement that this worker can also build.
    /// </summary>
    public HashSet<ImprovementData> GetObsoleteImprovementsForWorker(WorkerUnitData worker)
    {
        var obsolete = new HashSet<ImprovementData>();
        var unlocked = GetUnlockedImprovements();

        foreach (var replacement in unlocked)
        {
            if (replacement == null) continue;

            if (replacement.replacesImprovements != null)
            {
                foreach (var old in replacement.replacesImprovements)
                {
                    if (old != null)
                        obsolete.Add(old);
                }
            }
        }

        return obsolete;
    }

    /// <summary>
    /// Get the list of improvements this worker can currently build, filtered to remove obsolete ones.
    /// If tileIndex is provided, also filters by tile land/biome requirements.
    /// </summary>
    public List<ImprovementData> GetAvailableImprovementsForWorker(WorkerUnitData worker, int tileIndex = -1, int planetIndex = -1)
    {
        var list = new List<ImprovementData>();
        var unlocked = GetUnlockedImprovements();
        if (unlocked == null || unlocked.Count == 0) return list;

        var obsolete = GetObsoleteImprovementsForWorker(worker);

        HexTileData tileData = null;
        if (tileIndex >= 0)
        {
            if (planetIndex < 0) planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        }

        foreach (var imp in unlocked)
        {
            if (imp == null) continue;

            if (tileData != null)
            {
                if (!tileData.isLand) continue;
                if (imp.allowedBiomes != null && imp.allowedBiomes.Length > 0)
                {
                    bool allowed = System.Array.IndexOf(imp.allowedBiomes, tileData.biome) >= 0;
                    if (!allowed) continue;
                }
            }

            bool obsoleteHere = false;
            foreach (var repl in unlocked)
            {
                if (repl == null || repl.replacesImprovements == null) continue;
                bool replacesThis = System.Array.IndexOf(repl.replacesImprovements, imp) >= 0;
                if (!replacesThis) continue;

                if (tileData != null && repl.allowedBiomes != null && repl.allowedBiomes.Length > 0)
                {
                    bool replAllowed = System.Array.IndexOf(repl.allowedBiomes, tileData.biome) >= 0;
                    if (!replAllowed) continue;
                }

                obsoleteHere = true;
                break;
            }

            if (obsoleteHere) continue;

            list.Add(imp);
        }

        return list;
    }

    public List<CombatUnitData> GetAvailableCombatUnitsForWorker(WorkerUnit worker, int tileIndex = -1)
    {
        var list = new List<CombatUnitData>();
        if (worker == null) return list;

        var seen = new HashSet<CombatUnitData>();
        foreach (var resolvedUnit in unlockedCombatUnits)
        {
            if (resolvedUnit == null || !resolvedUnit.buildableByWorker) continue;
            if (seen.Contains(resolvedUnit)) continue;

            int candidateTile = tileIndex >= 0 ? tileIndex : worker.currentTileIndex;
            if (!worker.CanBuildUnit(resolvedUnit, candidateTile)) continue;

            seen.Add(resolvedUnit);
            list.Add(resolvedUnit);
        }

        return list;
    }

    public List<WorkerUnitData> GetAvailableWorkerUnitsForWorker(WorkerUnit worker, int tileIndex = -1)
    {
        var list = new List<WorkerUnitData>();
        if (worker == null) return list;

        var seen = new HashSet<WorkerUnitData>();
        foreach (var workerUnit in unlockedWorkerUnits)
        {
            if (!workerUnit.buildableByWorker) continue;
            if (seen.Contains(workerUnit)) continue;

            int candidateTile = tileIndex >= 0 ? tileIndex : worker.currentTileIndex;
            if (!worker.CanBuildWorker(workerUnit, candidateTile)) continue;

            seen.Add(workerUnit);
            list.Add(workerUnit);
        }

        return list;
    }

    public TechAge GetCurrentAge()
    {
        // If no techs researched, default to Paleolithic (first defined age)
        if (researchedTechs == null || researchedTechs.Count == 0)
            return TechAge.PaleolithicAge;

        TechAge maxAge = TechAge.PaleolithicAge;
        foreach (var tech in researchedTechs)
        {
            if (tech != null && tech.techAge > maxAge)
                maxAge = tech.techAge;
        }
        return maxAge;
    }

    /// <summary>
    /// Calculate total food consumption per turn (for UI display)
    /// Includes units AND city populations!
    /// </summary>
    public int GetFoodConsumptionPerTurn()
    {
        int totalConsumption = 0;
        
        // Combat units
        if (combatUnits != null)
        {
            foreach (var u in combatUnits)
            {
                if (u != null && u.data != null)
                    totalConsumption += u.data.foodConsumptionPerTurn;
                else
                    totalConsumption += defaultFoodPerCombatUnit;
            }
        }
        
        // Worker units
        if (workerUnits != null)
        {
            foreach (var w in workerUnits)
            {
                if (w != null && w.data != null)
                    totalConsumption += w.data.foodConsumptionPerTurn;
                else
                    totalConsumption += defaultFoodPerWorkerUnit;
            }
        }
        
        // City populations
        if (cities != null)
        {
            foreach (var city in cities)
            {
                if (city != null)
                    totalConsumption += city.GetFoodConsumptionPerTurn();
            }
        }
        
        return totalConsumption;
    }
    
    /// <summary>
    /// Get net food per turn (production - consumption)
    /// </summary>
    public int GetNetFoodPerTurn()
    {
        int production = 0;
        if (cities != null)
        {
            foreach (var city in cities)
            {
                production += Mathf.RoundToInt(city.GetFoodPerTurn() * (1 + foodModifier));
            }
        }
        return production - GetFoodConsumptionPerTurn();
    }

    // Centralized yield modification helpers that raise events for UI to subscribe to.
    public void AddFood(int amount)
    {
        int old = food;
        food += amount;
        OnFoodChanged?.Invoke(food, amount);
    }

    public void AddGold(int amount)
    {
        int old = gold;
        gold += amount;
        OnGoldChanged?.Invoke(gold, amount);
    }

    public void AddFaith(int amount)
    {
        int old = faith;
        faith += amount;
        OnFaithChanged?.Invoke(faith, amount);
    }

    public void AddPolicyPoints(int amount)
    {
        int old = policyPoints;
        policyPoints += amount;
        OnPolicyPointsChanged?.Invoke(policyPoints, amount);
    }
    
    /// <summary>
    /// Get detailed food consumption breakdown (for UI tooltips)
    /// </summary>
    public (int units, int cities, int total) GetFoodConsumptionBreakdown()
    {
        int unitConsumption = 0;
        int cityConsumption = 0;
        
        // Units
        if (combatUnits != null)
        {
            foreach (var u in combatUnits)
            {
                if (u != null && u.data != null)
                    unitConsumption += u.data.foodConsumptionPerTurn;
                else
                    unitConsumption += defaultFoodPerCombatUnit;
            }
        }
        if (workerUnits != null)
        {
            foreach (var w in workerUnits)
            {
                if (w != null && w.data != null)
                    unitConsumption += w.data.foodConsumptionPerTurn;
                else
                    unitConsumption += defaultFoodPerWorkerUnit;
            }
        }
        
        // Cities
        if (cities != null)
        {
            foreach (var city in cities)
            {
                if (city != null)
                    cityConsumption += city.GetFoodConsumptionPerTurn();
            }
        }
        
        return (unitConsumption, cityConsumption, unitConsumption + cityConsumption);
    }

    public void Initialize(CivData data, LeaderData leaderData, bool isPlayer, HexGrid grid = null, PlanetGenerator planet = null)
    {
        civData = data;
        leader = leaderData; // Set the leader for this civilization instance
        isPlayerControlled = isPlayer;
        // For multi-planet support: only assign planet-specific references when explicitly provided.
        // Do NOT bind this Civilization to the current GameManager planet by default.
        if (planet != null) planetGenerator = planet;
        if (grid != null) planetGrid = grid;
        else if (planet != null) planetGrid = planet.Grid;
        
        // --- Ensure cityPrefab is set from CivData ---
        if (cityPrefab == null && civData != null && civData.cityPrefabsByAge != null && civData.cityPrefabsByAge.Length > 0)
        {
            // Choose the prefab that matches the starting tech age (assumed first entry)
            var prefabEntry = civData.cityPrefabsByAge[0];
            if (prefabEntry != null)
            {
                cityPrefab = prefabEntry.cityPrefab;
                if (cityPrefab == null)
                {
                    Debug.LogWarning($"[{civData.civName}] City prefab entry for starting age is null!");
                }
            }
        }
        
        // Use the new 'leader' field for initialization
        InitializeLeaderUniques();
        ApplyLeaderBonuses();

        // Initialize starting equipment
        if (startingEquipment != null)
        {
            foreach(var item in startingEquipment)
            {
                AddEquipment(item, 5); // Start with a default quantity
            }
        }

        // Starting food stockpile for all civilizations
        food = 7;
    }

    // --- Consolidated bonus aggregation & calculation (moved from BonusAggregator.cs / BonusCalculator.cs) ---

    public struct UnitBonusAgg
    {
        public int attackAdd, defenseAdd, healthAdd, rangeAdd;
        public float attackPct, defensePct, healthPct, rangePct;
    }

    public struct WorkerBonusAgg
    {
        public int workPointsAdd, movePointsAdd, healthAdd;
        public float workPointsPct, movePointsPct, healthPct;
    }
    
    public struct YieldBonusAgg
    {
        public int foodAdd, productionAdd, goldAdd, scienceAdd, cultureAdd, faithAdd, policyPointsAdd;
        public float foodPct, productionPct, goldPct, sciencePct, culturePct, faithPct, policyPointsPct;
    }

    private static void AddUnitYieldBonus(ref YieldBonusAgg agg, UnitYieldBonus bonus)
    {
        if (bonus == null) return;
        agg.foodAdd += bonus.foodAdd; agg.productionAdd += bonus.productionAdd; agg.goldAdd += bonus.goldAdd;
        agg.scienceAdd += bonus.scienceAdd; agg.cultureAdd += bonus.cultureAdd; agg.faithAdd += bonus.faithAdd; agg.policyPointsAdd += bonus.policyPointsAdd;
        agg.foodPct += bonus.foodPct; agg.productionPct += bonus.productionPct; agg.goldPct += bonus.goldPct;
        agg.sciencePct += bonus.sciencePct; agg.culturePct += bonus.culturePct; agg.faithPct += bonus.faithPct; agg.policyPointsPct += bonus.policyPointsPct;
    }

    private static void AddWorkerYieldBonus(ref YieldBonusAgg agg, WorkerUnitYieldBonus bonus)
    {
        if (bonus == null) return;
        agg.foodAdd += bonus.foodAdd; agg.goldAdd += bonus.goldAdd; agg.scienceAdd += bonus.scienceAdd;
        agg.cultureAdd += bonus.cultureAdd; agg.faithAdd += bonus.faithAdd; agg.policyPointsAdd += bonus.policyPointsAdd;
        agg.foodPct += bonus.foodPct; agg.goldPct += bonus.goldPct; agg.sciencePct += bonus.sciencePct;
        agg.culturePct += bonus.culturePct; agg.faithPct += bonus.faithPct; agg.policyPointsPct += bonus.policyPointsPct;
    }

    public struct EquipBonusAgg
    {
        public int attackAdd, defenseAdd, healthAdd, rangeAdd;
        public float attackPct, defensePct, healthPct, rangePct;
    }

    private YieldBonusAgg AggregateUnitYieldBonuses(CombatUnitData unit, int planetIndex = -1)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (unit == null) return agg;

        // Techs
        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech == null || tech.unitYieldBonuses == null) continue;
                foreach (var b in tech.unitYieldBonuses)
                {
                    if (b != null && MatchesCombatUnitBonusTarget(unit, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                        AddUnitYieldBonus(ref agg, b);
                }
            }
        }
        // Cultures
        if (researchedCultures != null)
        {
            foreach (var culture in researchedCultures)
            {
                if (culture == null || culture.unitYieldBonuses == null) continue;
                foreach (var b in culture.unitYieldBonuses)
                {
                    if (b != null && MatchesCombatUnitBonusTarget(unit, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                        AddUnitYieldBonus(ref agg, b);
                }
            }
        }
        // Policies
        if (activePolicies != null)
        {
            foreach (var policy in activePolicies)
            {
                if (policy == null || policy.unitYieldBonuses == null) continue;
                foreach (var b in policy.unitYieldBonuses)
                {
                    if (b != null && MatchesCombatUnitBonusTarget(unit, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                        AddUnitYieldBonus(ref agg, b);
                }
            }
        }
        // Government
        if (currentGovernment != null && currentGovernment.unitYieldBonuses != null)
        {
            foreach (var b in currentGovernment.unitYieldBonuses)
            {
                if (b != null && MatchesCombatUnitBonusTarget(unit, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                    AddUnitYieldBonus(ref agg, b);
            }
        }

        foreach (var pantheonBonuses in EnumeratePantheonBonuses())
        {
            if (pantheonBonuses?.unitYieldBonuses == null) continue;
            foreach (var b in pantheonBonuses.unitYieldBonuses)
                if (b != null && MatchesCombatUnitBonusTarget(unit, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                    AddUnitYieldBonus(ref agg, b);
        }

        foreach (var belief in EnumerateActiveBeliefs())
        {
            if (belief?.unitYieldBonuses == null || !IsBeliefSeasonActive(belief, planetIndex)) continue;
            foreach (var b in belief.unitYieldBonuses)
                if (b != null && MatchesCombatUnitBonusTarget(unit, b.unit, b.useUnitCategoryFilter, b.unitCategory) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                    AddUnitYieldBonus(ref agg, b);
        }

        return agg;
    }

    private YieldBonusAgg AggregateEquipmentYieldBonuses(EquipmentData equip)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (equip == null) return agg;

        // Techs
        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech == null || tech.equipmentYieldBonuses == null) continue;
                foreach (var b in tech.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Cultures
        if (researchedCultures != null)
        {
            foreach (var culture in researchedCultures)
            {
                if (culture == null || culture.equipmentYieldBonuses == null) continue;
                foreach (var b in culture.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Policies
        if (activePolicies != null)
        {
            foreach (var policy in activePolicies)
            {
                if (policy == null || policy.equipmentYieldBonuses == null) continue;
                foreach (var b in policy.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Government
        if (currentGovernment != null && currentGovernment.equipmentYieldBonuses != null)
        {
            foreach (var b in currentGovernment.equipmentYieldBonuses)
            {
                if (b != null && b.equipment == equip)
                {
                    agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                    agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                    agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                    agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                }
            }
        }

        return agg;
    }

    public (int food, int gold, int science, int culture, int faith, int policy) ComputeUnitPerTurnYield(CombatUnitData unit, params EquipmentData[] equippedItems)
    {
        return ComputeUnitPerTurnYield(unit, -1, equippedItems);
    }

    public (int food, int gold, int science, int culture, int faith, int policy) ComputeUnitPerTurnYield(CombatUnitData unit, int planetIndex, params EquipmentData[] equippedItems)
    {
        if (unit == null) return (0,0,0,0,0,0);
        int baseFood = unit.foodPerTurn;
        int baseGold = unit.goldPerTurn;
        int baseSci  = unit.sciencePerTurn;
        int baseCul  = unit.culturePerTurn;
        int baseFai  = unit.faithPerTurn;
        int basePol  = unit.policyPointsPerTurn;

        // Include base equipment yields from all equipped items
        if (equippedItems != null)
        {
            foreach (var eq in equippedItems)
            {
                if (eq == null) continue;
                baseFood += eq.foodPerTurn;
                baseGold += eq.goldPerTurn;
                baseSci  += eq.sciencePerTurn;
                baseCul  += eq.culturePerTurn;
                baseFai  += eq.faithPerTurn;
                basePol  += eq.policyPointsPerTurn;
            }
        }

        var u = AggregateUnitYieldBonuses(unit, planetIndex);
        // Sum equipment-based yield modifiers from bonuses too
        YieldBonusAgg eAgg = new YieldBonusAgg();
        if (equippedItems != null)
        {
            foreach (var eq in equippedItems)
            {
                var e = AggregateEquipmentYieldBonuses(eq);
                eAgg.foodAdd += e.foodAdd; eAgg.goldAdd += e.goldAdd; eAgg.scienceAdd += e.scienceAdd; eAgg.cultureAdd += e.cultureAdd; eAgg.faithAdd += e.faithAdd; eAgg.policyPointsAdd += e.policyPointsAdd;
                eAgg.foodPct += e.foodPct; eAgg.goldPct += e.goldPct; eAgg.sciencePct += e.sciencePct; eAgg.culturePct += e.culturePct; eAgg.faithPct += e.faithPct; eAgg.policyPointsPct += e.policyPointsPct;
            }
        }

        int food = Mathf.RoundToInt((baseFood + u.foodAdd + eAgg.foodAdd) * (1f + u.foodPct + eAgg.foodPct));
        int gold = Mathf.RoundToInt((baseGold + u.goldAdd + eAgg.goldAdd) * (1f + u.goldPct + eAgg.goldPct));
        int sci  = Mathf.RoundToInt((baseSci  + u.scienceAdd + eAgg.scienceAdd) * (1f + u.sciencePct + eAgg.sciencePct));
        int cul  = Mathf.RoundToInt((baseCul  + u.cultureAdd + eAgg.cultureAdd) * (1f + u.culturePct + eAgg.culturePct));
        int fai  = Mathf.RoundToInt((baseFai  + u.faithAdd + eAgg.faithAdd) * (1f + u.faithPct + eAgg.faithPct));
        int pol  = Mathf.RoundToInt((basePol  + u.policyPointsAdd + eAgg.policyPointsAdd) * (1f + u.policyPointsPct + eAgg.policyPointsPct));

        return (food, gold, sci, cul, fai, pol);
    }

    private YieldBonusAgg AggregateWorkerYieldBonuses(WorkerUnitData worker, int planetIndex = -1)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (worker == null) return agg;

        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech == null || tech.workerYieldBonuses == null) continue;
                foreach (var b in tech.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                        AddWorkerYieldBonus(ref agg, b);
                }
            }
        }
        if (researchedCultures != null)
        {
            foreach (var culture in researchedCultures)
            {
                if (culture == null || culture.workerYieldBonuses == null) continue;
                foreach (var b in culture.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                        AddWorkerYieldBonus(ref agg, b);
                }
            }
        }
        if (activePolicies != null)
        {
            foreach (var policy in activePolicies)
            {
                if (policy == null || policy.workerYieldBonuses == null) continue;
                foreach (var b in policy.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                        AddWorkerYieldBonus(ref agg, b);
                }
            }
        }
        if (currentGovernment != null && currentGovernment.workerYieldBonuses != null)
        {
            foreach (var b in currentGovernment.workerYieldBonuses)
            {
                if (b != null && b.worker == worker && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                    AddWorkerYieldBonus(ref agg, b);
            }
        }

        foreach (var pantheonBonuses in EnumeratePantheonBonuses())
        {
            if (pantheonBonuses?.workerYieldBonuses == null) continue;
            foreach (var b in pantheonBonuses.workerYieldBonuses)
                if (b != null && b.worker == worker && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                    AddWorkerYieldBonus(ref agg, b);
        }

        foreach (var belief in EnumerateActiveBeliefs())
        {
            if (belief?.workerYieldBonuses == null || !IsBeliefSeasonActive(belief, planetIndex)) continue;
            foreach (var b in belief.workerYieldBonuses)
                if (b != null && b.worker == worker && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                    AddWorkerYieldBonus(ref agg, b);
        }

        return agg;
    }

    public (int food, int gold, int science, int culture, int faith, int policy) ComputeWorkerPerTurnYield(WorkerUnitData worker)
    {
        return ComputeWorkerPerTurnYield(worker, -1);
    }

    public (int food, int gold, int science, int culture, int faith, int policy) ComputeWorkerPerTurnYield(WorkerUnitData worker, int planetIndex)
    {
        if (worker == null) return (0,0,0,0,0,0);
        int baseFood = worker.foodPerTurn;
        int baseGold = worker.goldPerTurn;
        int baseSci  = worker.sciencePerTurn;
        int baseCul  = worker.culturePerTurn;
        int baseFai  = worker.faithPerTurn;
        int basePol  = worker.policyPointsPerTurn;

        var w = AggregateWorkerYieldBonuses(worker, planetIndex);
        int food = Mathf.RoundToInt((baseFood + w.foodAdd) * (1f + w.foodPct));
        int gold = Mathf.RoundToInt((baseGold + w.goldAdd) * (1f + w.goldPct));
        int sci  = Mathf.RoundToInt((baseSci  + w.scienceAdd) * (1f + w.sciencePct));
        int cul  = Mathf.RoundToInt((baseCul  + w.cultureAdd) * (1f + w.culturePct));
        int fai  = Mathf.RoundToInt((baseFai  + w.faithAdd) * (1f + w.faithPct));
        int pol  = Mathf.RoundToInt((basePol  + w.policyPointsAdd) * (1f + w.policyPointsPct));
        return (food, gold, sci, cul, fai, pol);
    }

    private static void AddHerdYieldBonus(ref YieldBonusAgg agg, HerdYieldBonus bonus)
    {
        if (bonus == null) return;
        agg.foodAdd += bonus.foodAdd; agg.productionAdd += bonus.productionAdd; agg.goldAdd += bonus.goldAdd;
        agg.scienceAdd += bonus.scienceAdd; agg.cultureAdd += bonus.cultureAdd; agg.faithAdd += bonus.faithAdd; agg.policyPointsAdd += bonus.policyPointsAdd;
        agg.foodPct += bonus.foodPct; agg.productionPct += bonus.productionPct; agg.goldPct += bonus.goldPct;
        agg.sciencePct += bonus.sciencePct; agg.culturePct += bonus.culturePct; agg.faithPct += bonus.faithPct; agg.policyPointsPct += bonus.policyPointsPct;
    }

    private bool MatchesHerdSpeciesFilter(HerdYieldBonus bonus, Herd herd)
    {
        if (bonus == null || herd == null) return false;
        if (!bonus.useSpeciesFilter) return true;
        if (herd.animals == null) return false;
        foreach (var entry in herd.animals)
            if (entry != null && entry.count > 0 && entry.species == bonus.species)
                return true;
        return false;
    }

    public YieldBonusAgg AggregateHerdYieldBonuses(Herd herd, int planetIndex = -1)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (herd == null) return agg;

        void Scan(HerdYieldBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var b in bonuses)
                if (b != null && MatchesHerdSpeciesFilter(b, herd) && MatchesSeasonFilterForPlanet(b.useSeasonFilter, b.seasons, planetIndex))
                    AddHerdYieldBonus(ref agg, b);
        }

        // CivData
        Scan(civData?.herdYieldBonuses);

        // Leader
        if (leader != null) Scan(leader.herdYieldBonuses);

        // Techs
        if (researchedTechs != null)
            foreach (var tech in researchedTechs) Scan(tech?.herdYieldBonuses);

        // Cultures
        if (researchedCultures != null)
            foreach (var culture in researchedCultures) Scan(culture?.herdYieldBonuses);

        // Government
        Scan(currentGovernment?.herdYieldBonuses);

        // Policies
        if (activePolicies != null)
            foreach (var policy in activePolicies) Scan(policy?.herdYieldBonuses);

        // Pantheons
        foreach (var pantheonBonuses in EnumeratePantheonBonuses())
            Scan(pantheonBonuses?.herdYieldBonuses);

        // Beliefs
        foreach (var belief in EnumerateActiveBeliefs())
            if (belief != null && IsBeliefSeasonActive(belief, planetIndex))
                Scan(belief.herdYieldBonuses);

        // Herd structures (BuildingData)
        if (herd.builtStructures != null)
            foreach (var structure in herd.builtStructures)
                Scan(structure?.herdYieldBonuses);

        return agg;
    }

    // --- CombinedBonuses (from BonusCalculator) ---
    [System.Serializable]
    public struct CombinedBonuses
    {
        public float foodModifier;
        public float productionModifier;
        public float goldModifier;
        public float scienceModifier;
        public float cultureModifier;
        public float faithModifier;
        public float attackBonus;
        public float defenseBonus;
        public float movementBonus;

        public int flatFoodBonus;
        public int flatProductionBonus;
        public int flatGoldBonus;
        public int flatScienceBonus;
        public int flatCultureBonus;
        public int flatFaithBonus;

        public int additionalGovernorSlots;

        public List<UnitLimitModifier> unitLimitModifiers;
        public List<BuildingLimitModifier> buildingLimitModifiers;
    }

    public CombinedBonuses CalculateTechBonuses(List<TechData> technologies)
    {
        CombinedBonuses result = new CombinedBonuses();
        if (technologies == null || technologies.Count == 0)
            return result;
        foreach (var tech in technologies)
        {
            if (tech == null) continue;
            result.foodModifier += tech.foodModifier;
            result.productionModifier += tech.productionModifier;
            result.goldModifier += tech.goldModifier;
            result.scienceModifier += tech.scienceModifier;
            result.cultureModifier += tech.cultureModifier;
            result.faithModifier += tech.faithModifier;
            result.attackBonus += tech.attackBonus;
            result.defenseBonus += tech.defenseBonus;
            result.movementBonus += tech.movementBonus;

            result.flatFoodBonus += tech.flatFoodBonus;
            result.flatProductionBonus += tech.flatProductionBonus;
            result.flatGoldBonus += tech.flatGoldBonus;
            result.flatScienceBonus += tech.flatScienceBonus;
            result.flatCultureBonus += tech.flatCultureBonus;
            result.flatFaithBonus += tech.flatFaithBonus;

            result.additionalGovernorSlots += tech.additionalGovernorSlots;

            if (result.unitLimitModifiers == null)
                result.unitLimitModifiers = new List<UnitLimitModifier>();
            if (result.buildingLimitModifiers == null)
                result.buildingLimitModifiers = new List<BuildingLimitModifier>();

            if (tech.unitLimitModifiers != null)
                result.unitLimitModifiers.AddRange(tech.unitLimitModifiers);
            if (tech.buildingLimitModifiers != null)
                result.buildingLimitModifiers.AddRange(tech.buildingLimitModifiers);
        }
        return result;
    }

    public CombinedBonuses CalculateCultureBonuses(List<CultureData> cultures)
    {
        CombinedBonuses result = new CombinedBonuses();
        if (cultures == null || cultures.Count == 0)
            return result;
        foreach (var culture in cultures)
        {
            if (culture == null) continue;
            result.foodModifier += culture.foodModifier;
            result.productionModifier += culture.productionModifier;
            result.goldModifier += culture.goldModifier;
            result.scienceModifier += culture.scienceModifier;
            result.cultureModifier += culture.cultureModifier;
            result.faithModifier += culture.faithModifier;
            result.attackBonus += culture.attackBonus;
            result.defenseBonus += culture.defenseBonus;
            result.movementBonus += culture.movementBonus;

            result.flatFoodBonus += culture.flatFoodBonus;
            result.flatProductionBonus += culture.flatProductionBonus;
            result.flatGoldBonus += culture.flatGoldBonus;
            result.flatScienceBonus += culture.flatScienceBonus;
            result.flatCultureBonus += culture.flatCultureBonus;
            result.flatFaithBonus += culture.flatFaithBonus;

            result.additionalGovernorSlots += culture.additionalGovernorSlots;

            if (result.unitLimitModifiers == null)
                result.unitLimitModifiers = new List<UnitLimitModifier>();
            if (result.buildingLimitModifiers == null)
                result.buildingLimitModifiers = new List<BuildingLimitModifier>();

            if (culture.unitLimitModifiers != null)
                result.unitLimitModifiers.AddRange(culture.unitLimitModifiers);
            if (culture.buildingLimitModifiers != null)
                result.buildingLimitModifiers.AddRange(culture.buildingLimitModifiers);
        }
        return result;
    }

    public CombinedBonuses CalculateTotalBonuses(List<TechData> technologies, List<CultureData> cultures)
    {
        var techBonuses = CalculateTechBonuses(technologies);
        var cultureBonuses = CalculateCultureBonuses(cultures);
        return CombineBonuses(techBonuses, cultureBonuses);
    }

    public CombinedBonuses CombineBonuses(CombinedBonuses bonuses1, CombinedBonuses bonuses2)
    {
        CombinedBonuses result = new CombinedBonuses();
        result.foodModifier = bonuses1.foodModifier + bonuses2.foodModifier;
        result.productionModifier = bonuses1.productionModifier + bonuses2.productionModifier;
        result.goldModifier = bonuses1.goldModifier + bonuses2.goldModifier;
        result.scienceModifier = bonuses1.scienceModifier + bonuses2.scienceModifier;
        result.cultureModifier = bonuses1.cultureModifier + bonuses2.cultureModifier;
        result.faithModifier = bonuses1.faithModifier + bonuses2.faithModifier;
        result.attackBonus = bonuses1.attackBonus + bonuses2.attackBonus;
        result.defenseBonus = bonuses1.defenseBonus + bonuses2.defenseBonus;
        result.movementBonus = bonuses1.movementBonus + bonuses2.movementBonus;

        result.flatFoodBonus = bonuses1.flatFoodBonus + bonuses2.flatFoodBonus;
        result.flatProductionBonus = bonuses1.flatProductionBonus + bonuses2.flatProductionBonus;
        result.flatGoldBonus = bonuses1.flatGoldBonus + bonuses2.flatGoldBonus;
        result.flatScienceBonus = bonuses1.flatScienceBonus + bonuses2.flatScienceBonus;
        result.flatCultureBonus = bonuses1.flatCultureBonus + bonuses2.flatCultureBonus;
        result.flatFaithBonus = bonuses1.flatFaithBonus + bonuses2.flatFaithBonus;

        result.additionalGovernorSlots = bonuses1.additionalGovernorSlots + bonuses2.additionalGovernorSlots;

        result.unitLimitModifiers = new List<UnitLimitModifier>();
        result.buildingLimitModifiers = new List<BuildingLimitModifier>();
        if (bonuses1.unitLimitModifiers != null)
            result.unitLimitModifiers.AddRange(bonuses1.unitLimitModifiers);
        if (bonuses2.unitLimitModifiers != null)
            result.unitLimitModifiers.AddRange(bonuses2.unitLimitModifiers);
        if (bonuses1.buildingLimitModifiers != null)
            result.buildingLimitModifiers.AddRange(bonuses1.buildingLimitModifiers);
        if (bonuses2.buildingLimitModifiers != null)
            result.buildingLimitModifiers.AddRange(bonuses2.buildingLimitModifiers);

        return result;
    }

    /// <summary>
    /// Simple yield collection used by bonus calculations and application helpers.
    /// </summary>
    [System.Serializable]
    public struct YieldCollection
    {
        public int food;
        public int production;
        public int gold;
        public int science;
        public int culture;
        public int faith;

        public YieldCollection(int food = 0, int production = 0, int gold = 0, int science = 0, int culture = 0, int faith = 0)
        {
            this.food = food;
            this.production = production;
            this.gold = gold;
            this.science = science;
            this.culture = culture;
            this.faith = faith;
        }

        public static YieldCollection operator +(YieldCollection a, YieldCollection b)
        {
            return new YieldCollection(
                a.food + b.food,
                a.production + b.production,
                a.gold + b.gold,
                a.science + b.science,
                a.culture + b.culture,
                a.faith + b.faith
            );
        }
        public override string ToString()
        {
            return $"food:{food} prod:{production} gold:{gold} sci:{science} cul:{culture} faith:{faith}";
        }
    }

    public int ApplyBonuses(int baseYield, float percentageModifier, int flatBonus)
    {
        float modifiedYield = baseYield * (1f + percentageModifier);
        return Mathf.RoundToInt(modifiedYield) + flatBonus;
    }

    public YieldCollection ApplyYieldBonuses(YieldCollection baseYields, CombinedBonuses bonuses)
    {
        YieldCollection finalYields = new YieldCollection();
        finalYields.food = ApplyBonuses(baseYields.food, bonuses.foodModifier, bonuses.flatFoodBonus);
        finalYields.production = ApplyBonuses(baseYields.production, bonuses.productionModifier, bonuses.flatProductionBonus);
        finalYields.gold = ApplyBonuses(baseYields.gold, bonuses.goldModifier, bonuses.flatGoldBonus);
        finalYields.science = ApplyBonuses(baseYields.science, bonuses.scienceModifier, bonuses.flatScienceBonus);
        finalYields.culture = ApplyBonuses(baseYields.culture, bonuses.cultureModifier, bonuses.flatCultureBonus);
        finalYields.faith = ApplyBonuses(baseYields.faith, bonuses.faithModifier, bonuses.flatFaithBonus);
        return finalYields;
    }

    public HexGrid planetGrid; // Add this field to store the main planet's grid
    public PlanetGenerator planetGenerator; // Add this field to store the main planet's generator

    /// <summary>
    /// Return the authoritative PlanetGenerator for the given planet index, or a sensible fallback.
    /// This helper allows code to stop assuming a Civilization is tied to a single PlanetGenerator.
    /// </summary>
    public PlanetGenerator GetPlanetGeneratorForIndex(int planetIndex)
    {
        // Prefer GameManager's registered generator for the requested planet.
        var gm = GameManager.Instance;
        if (gm != null)
        {
            var gen = gm.GetPlanetGenerator(planetIndex);
            if (gen != null) return gen;
            Debug.LogWarning($"[Civilization] GetPlanetGeneratorForIndex: no generator for requested index {planetIndex} on GameManager; will try owned planets as fallback.");
        }
        else
        {
            Debug.LogWarning("[Civilization] GetPlanetGeneratorForIndex: GameManager.Instance is null; cannot resolve requested planet generator directly.");
        }

        // If this civ owns tiles on other planets, prefer one of those planet generators
        if (ownedTilesByPlanet != null && ownedTilesByPlanet.Count > 0 && gm != null)
        {
            foreach (var kv in ownedTilesByPlanet)
            {
                var gen = gm.GetPlanetGenerator(kv.Key);
                if (gen != null)
                {
                    Debug.LogWarning($"[Civilization] GetPlanetGeneratorForIndex: falling back to owned planet generator for planetIndex {kv.Key}.");
                    return gen;
                }
            }
        }

        // Fallback to the current active planet generator
        var current = gm?.GetCurrentPlanetGenerator();
        if (current != null)
        {
            Debug.LogWarning($"[Civilization] GetPlanetGeneratorForIndex: falling back to current planet generator (index {gm?.currentPlanetIndex}).");
            return current;
        }

        Debug.LogWarning("[Civilization] GetPlanetGeneratorForIndex: unable to resolve any PlanetGenerator (returning null).");
        return null;
    }

    /// <summary>
    /// Convenience: get the HexGrid for a specific planet index (may return null).
    /// </summary>
    public HexGrid GetGridForPlanetIndex(int planetIndex)
    {
        var gen = GetPlanetGeneratorForIndex(planetIndex);
        return gen != null ? gen.Grid : null;
    }

    /// <summary>
    /// Invalidate availability cache when techs/cultures change
    /// </summary>
    private void InvalidateAvailabilityCache()
    {
        _availabilityCacheDirty = true;
        _unitAvailabilityCache.Clear();
        _workerAvailabilityCache.Clear();
        _buildingAvailabilityCache.Clear();
        _equipmentAvailabilityCache.Clear();
        _canEquipByUnitTypeCache.Clear();
        RefreshUnlockedContentLists();
    }

    /// <summary>
    /// Cached check: can a *newly produced level-1 unit archetype* equip this item, given the civ's current unlocks/inventory?
    /// This is intentionally "unit type" based (not per-instance) for fast UI filtering.
    /// </summary>
    public bool CanEquipEquipmentForUnitTypeCached(CombatCategory unitType, EquipmentData equipment)
    {
        if (equipment == null) return false;

        // Must be usable by CombatUnits for this panel.
        if (!(equipment.targetUnit == EquipmentTarget.Both || equipment.targetUnit == EquipmentTarget.CombatUnit))
            return false;

        // Must have at least one in inventory.
        if (!HasEquipment(equipment))
            return false;

        // Unit type restriction.
        if (equipment.allowedUnitTypes != null && equipment.allowedUnitTypes.Length > 0)
        {
            bool typeAllowed = false;
            foreach (var t in equipment.allowedUnitTypes)
            {
                if (t == unitType) { typeAllowed = true; break; }
            }
            if (!typeAllowed) return false;
        }

        // Minimum level: EquipmentManagerPanel configures defaults for archetypes; treat level 1 as baseline.
        if (equipment.minimumLevel > 1) return false;

        long key = ((long)equipment.GetInstanceID() << 32) ^ (uint)unitType;
        if (_canEquipByUnitTypeCache.TryGetValue(key, out var cached))
            return cached;

        // Tech prerequisites
        if (equipment.requiredTechs != null && equipment.requiredTechs.Length > 0)
        {
            foreach (var tech in equipment.requiredTechs)
            {
                if (tech == null) continue;
                if (researchedTechs == null || !researchedTechs.Contains(tech))
                {
                    _canEquipByUnitTypeCache[key] = false;
                    return false;
                }
            }
        }

        // Culture prerequisites
        if (equipment.requiredCultures != null && equipment.requiredCultures.Length > 0)
        {
            foreach (var culture in equipment.requiredCultures)
            {
                if (culture == null) continue;
                if (researchedCultures == null || !researchedCultures.Contains(culture))
                {
                    _canEquipByUnitTypeCache[key] = false;
                    return false;
                }
            }
        }

        _canEquipByUnitTypeCache[key] = true;
        return true;
    }

    /// <summary>
    /// Check if a combat unit is available (cached)
    /// </summary>
    public bool IsCombatUnitAvailable(CombatUnitData unitData)
    {
        if (unitData == null) return false;
        
        if (_availabilityCacheDirty || !_unitAvailabilityCache.ContainsKey(unitData))
        {
            bool available = unitData.AreRequirementsMet(this);
            _unitAvailabilityCache[unitData] = available;
        }
        
        return _unitAvailabilityCache[unitData];
    }

    /// <summary>
    /// Check if a worker unit is available (cached)
    /// </summary>
    public bool IsWorkerUnitAvailable(WorkerUnitData unitData)
    {
        if (unitData == null) return false;
        
        if (_availabilityCacheDirty || !_workerAvailabilityCache.ContainsKey(unitData))
        {
            bool available = unitData.AreRequirementsMet(this);
            _workerAvailabilityCache[unitData] = available;
        }
        
        return _workerAvailabilityCache[unitData];
    }

    /// <summary>
    /// Check if a building is available (cached)
    /// </summary>
    public bool IsBuildingAvailable(BuildingData buildingData)
    {
        if (buildingData == null) return false;
        
        if (_availabilityCacheDirty || !_buildingAvailabilityCache.ContainsKey(buildingData))
        {
            bool available = buildingData.AreRequirementsMet(this);
            _buildingAvailabilityCache[buildingData] = available;
        }
        
        return _buildingAvailabilityCache[buildingData];
    }

    /// <summary>
    /// Check if equipment is available (cached)
    /// </summary>
    public bool IsEquipmentAvailable(EquipmentData equipmentData)
    {
        if (equipmentData == null) return false;
        
        if (_availabilityCacheDirty || !_equipmentAvailabilityCache.ContainsKey(equipmentData))
        {
            bool available = equipmentData.CanBeProducedBy(this);
            _equipmentAvailabilityCache[equipmentData] = available;
        }
        
        return _equipmentAvailabilityCache[equipmentData];
    }

    void OnDestroy()
    {
        try { CivilizationManager.Instance?.UnregisterCiv(this); } catch { }
        try { TurnManager.Instance?.UnregisterCivilization(this); } catch { }
        try { relations?.Clear(); } catch { }
    }
}

