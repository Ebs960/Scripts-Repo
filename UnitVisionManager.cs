using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages unit vision and fog of war revelation.
/// Tracks all units and cities, computes visible tiles, and updates TileSystem fog.
/// 
/// Call UpdateVisionForCiv() when:
/// - A unit moves
/// - A unit is created/destroyed
/// - A city is founded/captured
/// - At the start of each turn
/// </summary>
public class UnitVisionManager : MonoBehaviour
{
    public static UnitVisionManager Instance { get; private set; }
    
    [Header("Settings")]
    [Tooltip("Default sight range for units without explicit sightRange")]
    [SerializeField] private int defaultSightRange = 2;
    
    [Tooltip("Sight range bonus for units on hills")]
    [SerializeField] private int hillSightBonus = 1;
    
    [Tooltip("Sight range for cities")]
    [SerializeField] private int citySightRange = 3;
    
    [Tooltip("Update vision automatically each frame (disable for manual control)")]
    [SerializeField] private bool autoUpdateVision = false;
    
    [Tooltip("Planet index this manager is for")]
    [SerializeField] private int planetIndex = 0;
    
    // Cached references
    private TileSystem tileSystem;
    private HexGrid grid;
    
    // Per-civilization visible tiles (computed each update)
    private Dictionary<int, HashSet<int>> civVisibleTiles = new Dictionary<int, HashSet<int>>();
    
    // Reusable collections to avoid GC
    private HashSet<int> tempVisibleSet = new HashSet<int>();
    private List<int> neighborsBuffer = new List<int>();

    /// <summary>
    /// Fog/ownership systems identify civilizations by their stable per-session map actor slot
    /// (Civilization.MapActorSlot), NOT by CivilizationManager's mutable registration-order index.
    /// The name is kept for source compatibility with the many existing call sites that use it to
    /// index fog/vision arrays; it no longer returns a value that shifts when another civ is eliminated.
    /// </summary>
    public static int GetCivIndex(Civilization civ)
    {
        if (civ == null || CivilizationManager.Instance == null) return -1;
        return civ.MapActorSlot;
    }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        FindReferences();
    }

    void OnEnable()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
        }
    }

    void OnDisable()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
        }
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
        // Update vision for the civ whose turn it is
        // Note: This runs after civ.BeginTurn() is called, so unit positions are updated
        UpdateVisionForCiv(GetCivIndex(civ));
    }
    
    void Update()
    {
        EnsureActivePlanetReferences();

        if (autoUpdateVision && tileSystem != null && tileSystem.isReady)
        {
            // Update vision for local player
            int localCiv = tileSystem.localPlayerCivId;
            if (localCiv >= 0)
            {
                UpdateVisionForCiv(localCiv);
            }
        }
    }
    
    private void FindReferences()
    {
        // Prefer the currently active planet when available
        if (GameManager.Instance != null)
        {
            planetIndex = GameManager.Instance.currentPlanetIndex;
        }

        tileSystem = TileSystem.GetForPlanet(planetIndex);
        if (tileSystem == null)
        {
            tileSystem = TileSystem.Instance;
        }
        
        if (GameManager.Instance != null)
        {
            var planetGen = GameManager.Instance.GetPlanetGenerator(planetIndex);
            if (planetGen != null)
            {
                grid = planetGen.Grid;
            }
        }
        
        if (grid == null)
        {
            var planetGen = FindAnyObjectByType<PlanetGenerator>();
            if (planetGen != null)
            {
                grid = planetGen.Grid;
            }
        }
    }
    
    /// <summary>
    /// Update fog of war visibility for a specific civilization.
    /// Computes all visible tiles from units + cities and updates TileSystem.
    /// </summary>
    public void UpdateVisionForCiv(int civId)
    {
        EnsureActivePlanetReferences();

        if (tileSystem == null || !tileSystem.isReady)
        {
            FindReferences();
            if (tileSystem == null || !tileSystem.isReady) return;
        }
        
        if (grid == null || !grid.IsBuilt)
        {
            FindReferences();
            if (grid == null || !grid.IsBuilt) return;
        }
        
        tempVisibleSet.Clear();

        // Get civ (needed for units + cities)
        var civ = GetCivilization(civId);

        // === Civ5-style vision: each individual unit provides vision ===

        // Combat units
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (unit == null || unit.owner == null) continue;
            if (civ != null && unit.owner != civ) continue;
            if (!unit.gameObject.activeSelf) continue;
            if (unit.planetIndex != planetIndex) continue;
            if (unit.currentTileIndex < 0) continue;

            int sightRange = GetUnitSightRange(unit);
            if (IsOnHill(unit.currentTileIndex)) sightRange += hillSightBonus;
            AddVisibleTilesInRange(unit.currentTileIndex, sightRange, tempVisibleSet);
        }

        // Worker units
        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (worker == null || worker.owner == null) continue;
            if (civ != null && worker.owner != civ) continue;
            if (worker.planetIndex != planetIndex) continue;
            if (worker.currentTileIndex < 0) continue;

            int sightRange = GetUnitSightRange(worker);
            if (IsOnHill(worker.currentTileIndex)) sightRange += hillSightBonus;
            AddVisibleTilesInRange(worker.currentTileIndex, sightRange, tempVisibleSet);
        }

        // Cities
        if (civ != null && civ.cities != null)
        {
            foreach (var city in civ.cities)
            {
                if (city == null) continue;

                if (city.planetIndex != planetIndex) continue;

                int tileIndex = city.centerTileIndex;
                if (tileIndex >= 0)
                {
                    AddVisibleTilesInRange(tileIndex, citySightRange, tempVisibleSet);
                }
            }
        }
        
        // Apply the computed vision to TileSystem
        tileSystem.ApplyVisionHashSet(civId, tempVisibleSet);
        
        // Cache for potential queries
        if (!civVisibleTiles.ContainsKey(civId))
        {
            civVisibleTiles[civId] = new HashSet<int>();
        }
        civVisibleTiles[civId].Clear();
        civVisibleTiles[civId].UnionWith(tempVisibleSet);
    }
    
    /// <summary>
    /// Update vision for all civilizations (call at turn start)
    /// </summary>
    public void UpdateVisionForAllCivs()
    {
        if (CivilizationManager.Instance == null) return;
        
        var allCivs = CivilizationManager.Instance.GetAllCivs();
        for (int i = 0; i < allCivs.Count; i++)
        {
            if (allCivs[i] != null) UpdateVisionForCiv(i);
        }
    }
    
    /// <summary>
    /// Reveal tiles around a specific position (for events, exploration, etc.)
    /// </summary>
    public void RevealTilesAroundPosition(int civId, Vector3 worldPos, int range)
    {
        if (grid == null || tileSystem == null) return;
        
        int centerTile = grid.GetTileAtPosition(worldPos);
        if (centerTile < 0) return;
        
        tempVisibleSet.Clear();
        AddVisibleTilesInRange(centerTile, range, tempVisibleSet);
        tileSystem.RevealTiles(civId, tempVisibleSet);
    }
    
    /// <summary>
    /// Check if a tile is currently visible to a civilization
    /// </summary>
    public bool IsTileVisibleToCiv(int tileIndex, int civId)
    {
        if (civVisibleTiles.TryGetValue(civId, out var visibleSet))
        {
            return visibleSet.Contains(tileIndex);
        }
        return false;
    }
    
    /// <summary>
    /// Get tiles visible to a civilization
    /// </summary>
    public HashSet<int> GetVisibleTiles(int civId)
    {
        if (civVisibleTiles.TryGetValue(civId, out var visibleSet))
        {
            return visibleSet;
        }
        return new HashSet<int>();
    }
    
    private struct SightRangeBonusAgg
    {
        public float add;
        public float pct;
    }

    private int GetUnitSightRange(BaseUnit unit)
    {
        if (unit == null)
            return defaultSightRange;

        float range = defaultSightRange;
        int orbitBonus = 0;

        // Try CombatUnit
        var combatUnit = unit as CombatUnit;
        if (combatUnit != null && combatUnit.data != null)
        {
            range = combatUnit.data.sightRange;

            // Orbit vision bonus: units in orbit see much further (planetary scanning)
            if (unit.IsInOrbit)
            {
                orbitBonus = combatUnit.data.orbitVisionBonus;
            }
        }
        else
        {
            // Try WorkerUnit
            var workerUnit = unit as WorkerUnit;
            if (workerUnit != null && workerUnit.data != null)
            {
                range = workerUnit.data.sightRange;
            }
        }

        var bonuses = AggregateSightRangeBonuses(unit);
        range = (range + bonuses.add + unit.GetAbilitySightRangeModifier()) * (1f + bonuses.pct);
        range += orbitBonus;

        return Mathf.Max(0, Mathf.RoundToInt(range));
    }

    private SightRangeBonusAgg AggregateSightRangeBonuses(BaseUnit unit)
    {
        SightRangeBonusAgg agg = new SightRangeBonusAgg();
        if (unit == null)
            return agg;

        var civ = unit.owner;
        if (civ == null)
            return agg;

        var combatUnit = unit as CombatUnit;
        var workerUnit = unit as WorkerUnit;
        var combatData = combatUnit != null ? combatUnit.data : null;
        var workerData = workerUnit != null ? workerUnit.data : null;

        void AddUnitBonuses(UnitStatBonus[] bonuses)
        {
            if (bonuses == null || combatData == null) return;
            foreach (var bonus in bonuses)
            {
                if (!MatchesSightBonusTarget(combatData, bonus) || !MatchesUnitSightBonusLocation(unit, bonus))
                    continue;

                agg.add += bonus.sightRangeAdd;
                agg.pct += bonus.sightRangePct;
            }
        }

        void AddWorkerBonuses(WorkerUnitStatBonus[] bonuses)
        {
            if (bonuses == null || workerData == null) return;
            foreach (var bonus in bonuses)
            {
                if (!MatchesWorkerSightBonusTarget(workerData, bonus) || !MatchesWorkerSightBonusLocation(unit, bonus))
                    continue;

                agg.add += bonus.sightRangeAdd;
                agg.pct += bonus.sightRangePct;
            }
        }

        void AddEquipmentBonuses(EquipmentStatBonus[] bonuses, EquipmentData equipped)
        {
            if (bonuses == null || equipped == null) return;
            foreach (var bonus in bonuses)
            {
                if (bonus == null || bonus.equipment != equipped)
                    continue;
                if (Civilization.HasCombatBonusOpponentFilter(bonus.targetUnit, bonus.targetWorker, bonus.useTargetUnitCategoryFilter))
                    continue;

                agg.add += bonus.sightRangeAdd;
                agg.pct += bonus.sightRangePct;
            }
        }

        AddUnitBonuses(civ.civData?.unitBonuses);
        AddWorkerBonuses(civ.civData?.workerBonuses);
        AddUnitBonuses(civ.leader?.unitBonuses);
        AddWorkerBonuses(civ.leader?.workerBonuses);

        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                AddUnitBonuses(tech?.unitBonuses);
                AddWorkerBonuses(tech?.workerBonuses);
            }
        }

        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                AddUnitBonuses(culture?.unitBonuses);
                AddWorkerBonuses(culture?.workerBonuses);
            }
        }

        AddUnitBonuses(civ.currentGovernment?.unitBonuses);
        AddWorkerBonuses(civ.currentGovernment?.workerBonuses);

        if (civ.activePolicies != null)
        {
            foreach (var policy in civ.activePolicies)
            {
                AddUnitBonuses(policy?.unitBonuses);
                AddWorkerBonuses(policy?.workerBonuses);
            }
        }

        foreach (var pantheonBonuses in civ.EnumeratePantheonBonuses())
        {
            AddUnitBonuses(pantheonBonuses?.unitBonuses);
            AddWorkerBonuses(pantheonBonuses?.workerBonuses);
        }

        foreach (var belief in civ.EnumerateActiveBeliefs())
        {
            if (!civ.IsBeliefSeasonActive(belief, planetIndex))
                continue;

            AddUnitBonuses(belief?.unitBonuses);
            AddWorkerBonuses(belief?.workerBonuses);
        }

        if (civ.foundedReligion != null)
        {
            AddUnitBonuses(civ.foundedReligion.unitBonuses);
            AddWorkerBonuses(civ.foundedReligion.workerBonuses);
        }

        var cityContext = GetUnitCityContext(unit);
        if (cityContext != null)
        {
            foreach (var (building, _, _) in cityContext.EnumerateOperationalBuildings())
            {
                AddUnitBonuses(building?.unitBonuses);
                AddWorkerBonuses(building?.workerBonuses);
            }
        }

        foreach (var equipment in unit.EnumerateEquippedItemsForVision())
        {
            if (equipment == null) continue;

            agg.add += equipment.sightRangeBonus;

            if (civ.researchedTechs != null)
            {
                foreach (var tech in civ.researchedTechs)
                    AddEquipmentBonuses(tech?.equipmentBonuses, equipment);
            }

            if (civ.researchedCultures != null)
            {
                foreach (var culture in civ.researchedCultures)
                    AddEquipmentBonuses(culture?.equipmentBonuses, equipment);
            }
        }

        return agg;
    }
    

    private bool MatchesSightBonusTarget(CombatUnitData combatData, UnitStatBonus bonus)
    {
        if (combatData == null || bonus == null)
            return false;

        if (bonus.targetUnit != null || bonus.targetWorker != null || bonus.useTargetUnitCategoryFilter)
            return false;

        bool hasSpecificUnitTarget = bonus.unit != null;
        if (hasSpecificUnitTarget && bonus.unit != combatData)
            return false;

        if (bonus.useUnitCategoryFilter && combatData.unitType != bonus.unitCategory)
            return false;

        // For vision only, an empty unit filter means "all combat units".
        return true;
    }

    private bool MatchesWorkerSightBonusTarget(WorkerUnitData workerData, WorkerUnitStatBonus bonus)
    {
        if (workerData == null || bonus == null)
            return false;

        if (bonus.targetUnit != null || bonus.targetWorker != null || bonus.useTargetUnitCategoryFilter)
            return false;

        // For vision only, an empty worker filter means "all worker units".
        return bonus.worker == null || bonus.worker == workerData;
    }

    private bool MatchesRequirement(BoolRequirement requirement, bool value)
    {
        return requirement switch
        {
            BoolRequirement.MustBeTrue => value,
            BoolRequirement.MustBeFalse => !value,
            _ => true,
        };
    }

    private bool MatchesTerritoryRequirement(HexTileData tile, Civilization civ, UnitTerritoryRequirement requirement)
    {
        if (requirement == UnitTerritoryRequirement.Any)
            return true;
        if (tile == null || civ == null)
            return false;

        var tileOwner = tile.owner;
        switch (requirement)
        {
            case UnitTerritoryRequirement.Owned:
                return tileOwner == civ;
            case UnitTerritoryRequirement.Unowned:
                return tileOwner == null;
            case UnitTerritoryRequirement.Enemy:
                return tileOwner != null && tileOwner != civ && DiplomacyManager.Instance != null
                    ? DiplomacyManager.Instance.GetRelationship(civ, tileOwner) == DiplomaticState.War
                    : tileOwner != null && tileOwner != civ && civ.relations.TryGetValue(tileOwner, out var enemyState) && enemyState == DiplomaticState.War;
            case UnitTerritoryRequirement.Friendly:
                if (tileOwner == null || tileOwner == civ) return false;
                if (DiplomacyManager.Instance != null)
                    return DiplomacyManager.Instance.GetRelationship(civ, tileOwner) != DiplomaticState.War;
                return !civ.relations.TryGetValue(tileOwner, out var friendlyState) || friendlyState != DiplomaticState.War;
            default:
                return true;
        }
    }

    private bool MatchesUnitSightBonusLocation(BaseUnit unit, UnitStatBonus bonus)
    {
        if (unit == null || bonus == null)
            return false;

        var civ = unit.owner;
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? tileSystem ?? TileSystem.Instance;
        var tile = ts != null && unit.currentTileIndex >= 0 ? ts.GetTileData(unit.currentTileIndex) : null;
        bool isCityTile = tile?.controllingCity != null;

        if (!MatchesRequirement(bonus.cityRequirement, isCityTile)) return false;
        if (bonus.useBiomeFilter && (tile == null || tile.biome != bonus.biome)) return false;
        if (!MatchesRequirement(bonus.hillRequirement, tile != null && tile.isHill)) return false;
        if (!MatchesRequirement(bonus.mountainRequirement, tile != null && tile.isMountain)) return false;
        if (bonus.useResourceFilter && (tile == null || tile.resource != bonus.resource)) return false;
        if (!MatchesTerritoryRequirement(tile, civ, bonus.territoryRequirement)) return false;
        if (civ != null && !civ.MatchesSeasonFilterForPlanet(bonus.useSeasonFilter, bonus.seasons, unit.planetIndex)) return false;

        return true;
    }

    private bool MatchesWorkerSightBonusLocation(BaseUnit unit, WorkerUnitStatBonus bonus)
    {
        if (unit == null || bonus == null)
            return false;

        var civ = unit.owner;
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? tileSystem ?? TileSystem.Instance;
        var tile = ts != null && unit.currentTileIndex >= 0 ? ts.GetTileData(unit.currentTileIndex) : null;
        bool isCityTile = tile?.controllingCity != null;

        if (!MatchesRequirement(bonus.cityRequirement, isCityTile)) return false;
        if (bonus.useBiomeFilter && (tile == null || tile.biome != bonus.biome)) return false;
        if (!MatchesRequirement(bonus.hillRequirement, tile != null && tile.isHill)) return false;
        if (!MatchesRequirement(bonus.mountainRequirement, tile != null && tile.isMountain)) return false;
        if (bonus.useResourceFilter && (tile == null || tile.resource != bonus.resource)) return false;
        if (!MatchesTerritoryRequirement(tile, civ, bonus.territoryRequirement)) return false;
        if (civ != null && !civ.MatchesSeasonFilterForPlanet(bonus.useSeasonFilter, bonus.seasons, unit.planetIndex)) return false;

        return true;
    }

    private City GetUnitCityContext(BaseUnit unit)
    {
        if (unit == null)
            return null;

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? tileSystem ?? TileSystem.Instance;
        var tile = ts != null && unit.currentTileIndex >= 0 ? ts.GetTileData(unit.currentTileIndex) : null;
        return tile?.controllingCity;
    }
    private int GetUnitTileIndex(BaseUnit unit)
    {
        if (unit == null) return -1;
        return unit.currentTileIndex;
    }
    
    private bool IsOnHill(int tileIndex)
    {
        if (tileSystem == null) return false;
        
        var tileData = tileSystem.GetTileData(tileIndex);
        if (tileData != null)
        {
            // Use real, existing fields: isHill + elevation tier.
            // Note: Biome enum uses Mountain (singular), not Mountains.
            return tileData.isHill || tileData.elevationTier == ElevationTier.Hill || tileData.elevationTier == ElevationTier.Mountain;
        }
        return false;
    }
    
    // Reusable BFS structures to avoid per-call allocations
    private readonly Queue<(int tile, int dist)> bfsQueue = new Queue<(int, int)>();
    private readonly HashSet<int> bfsVisited = new HashSet<int>();

    private void AddVisibleTilesInRange(int centerTile, int range, HashSet<int> result)
    {
        if (grid == null || centerTile < 0 || centerTile >= grid.TileCount) return;
        
        result.Add(centerTile);
        
        if (range <= 0) return;
        
        // BFS to find all tiles within range (reuse structures)
        bfsQueue.Clear();
        bfsVisited.Clear();
        
        bfsQueue.Enqueue((centerTile, 0));
        bfsVisited.Add(centerTile);
        
        while (bfsQueue.Count > 0)
        {
            var (currentTile, currentDist) = bfsQueue.Dequeue();
            
            if (currentDist >= range) continue;
            
            // Get neighbors
            if (grid.neighbors != null && currentTile < grid.neighbors.Length && grid.neighbors[currentTile] != null)
            {
                foreach (int neighbor in grid.neighbors[currentTile])
                {
                    if (neighbor >= 0 && neighbor < grid.TileCount && !bfsVisited.Contains(neighbor))
                    {
                        bfsVisited.Add(neighbor);
                        result.Add(neighbor);
                        
                        // Check for blocking terrain (mountains block vision beyond them)
                        if (!BlocksVision(neighbor))
                        {
                            bfsQueue.Enqueue((neighbor, currentDist + 1));
                        }
                    }
                }
            }
        }
    }
    
    private bool BlocksVision(int tileIndex)
    {
        if (tileSystem == null) return false;
        
        var tileData = tileSystem.GetTileData(tileIndex);
        if (tileData != null)
        {
            // Mountains block vision beyond them (use elevation tier since Mountain enum removed).
            return tileData.elevationTier == ElevationTier.Mountain;
        }
        return false;
    }

    /// <summary>
    /// If the player switches planets, update cached references to match the currently active planet.
    /// </summary>
    private void EnsureActivePlanetReferences()
    {
        if (GameManager.Instance == null) return;
        int active = GameManager.Instance.currentPlanetIndex;
        if (active == planetIndex && tileSystem != null && grid != null) return;

        // If planet changed or references are missing, re-acquire.
        planetIndex = active;
        FindReferences();
    }
    
    private Civilization GetCivilization(int civId)
    {
        if (CivilizationManager.Instance == null) return null;
        
        var allCivs = CivilizationManager.Instance.GetAllCivs();
        if (civId < 0 || civId >= allCivs.Count) return null;
        return allCivs[civId];
    }
    
    /// <summary>
    /// Call when a unit moves to update vision
    /// </summary>
    public void OnUnitMoved(BaseUnit unit, int ownerCivId)
    {
        UpdateVisionForCiv(ownerCivId);
    }
    
    /// <summary>
    /// Call when a unit is created
    /// </summary>
    public void OnUnitCreated(BaseUnit unit, int ownerCivId)
    {
        UpdateVisionForCiv(ownerCivId);
    }
    
    /// <summary>
    /// Call when a unit is destroyed
    /// </summary>
    public void OnUnitDestroyed(int ownerCivId)
    {
        UpdateVisionForCiv(ownerCivId);
    }
    
    /// <summary>
    /// Call when a city is founded or captured
    /// </summary>
    public void OnCityChanged(int ownerCivId)
    {
        UpdateVisionForCiv(ownerCivId);
    }
}
