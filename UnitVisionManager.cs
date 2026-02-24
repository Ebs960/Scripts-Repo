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
    /// Fog/ownership systems identify civilizations by their registration index in CivilizationManager.
    /// This helper converts a Civilization reference into that index.
    /// </summary>
    public static int GetCivIndex(Civilization civ)
    {
        if (civ == null || CivilizationManager.Instance == null) return -1;
        var all = CivilizationManager.Instance.GetAllCivs(); // returns a copy; acceptable for occasional lookups
        return all.IndexOf(civ);
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

        // Get civ (needed for armies + cities)
        var civ = GetCivilization(civId);

        // === Campaign-map vision source of truth ===
        // Combat units are typically hidden/managed by the Army system on the campaign map.
        // So we compute vision from armies (and also from any visible/unassigned units and workers).

        // Armies
        if (civ != null && ArmyManager.Instance != null)
        {
            var armies = ArmyManager.Instance.GetArmiesByOwner(civ);
            foreach (var army in armies)
            {
                if (army == null) continue;
                if (army.planetIndex != planetIndex) continue;
                if (army.currentTileIndex < 0) continue;

                int sightRange = GetArmySightRange(army);
                if (IsOnHill(army.currentTileIndex)) sightRange += hillSightBonus;
                AddVisibleTilesInRange(army.currentTileIndex, sightRange, tempVisibleSet);
            }
        }

        // Worker units (not part of armies)
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

        // Any combat units that are currently active on the campaign map (e.g., orphaned / not yet merged into an army)
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
    
    private int GetUnitSightRange(BaseUnit unit)
    {
        // Try CombatUnit
        var combatUnit = unit as CombatUnit;
        if (combatUnit != null && combatUnit.data != null)
        {
            int range = combatUnit.data.sightRange;

            // Orbit vision bonus: units in orbit see much further (planetary scanning)
            if (unit.IsInOrbit)
            {
                range += combatUnit.data.orbitVisionBonus;
            }

            return range;
        }
        
        // Try WorkerUnit
        var workerUnit = unit as WorkerUnit;
        if (workerUnit != null && workerUnit.data != null)
        {
            return workerUnit.data.sightRange;
        }
        
        return defaultSightRange;
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
    
    private void AddVisibleTilesInRange(int centerTile, int range, HashSet<int> result)
    {
        if (grid == null || centerTile < 0 || centerTile >= grid.TileCount) return;
        
        result.Add(centerTile);
        
        if (range <= 0) return;
        
        // BFS to find all tiles within range
        Queue<(int tile, int dist)> queue = new Queue<(int, int)>();
        HashSet<int> visited = new HashSet<int>();
        
        queue.Enqueue((centerTile, 0));
        visited.Add(centerTile);
        
        while (queue.Count > 0)
        {
            var (currentTile, currentDist) = queue.Dequeue();
            
            if (currentDist >= range) continue;
            
            // Get neighbors
            if (grid.neighbors != null && currentTile < grid.neighbors.Length && grid.neighbors[currentTile] != null)
            {
                foreach (int neighbor in grid.neighbors[currentTile])
                {
                    if (neighbor >= 0 && neighbor < grid.TileCount && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        result.Add(neighbor);
                        
                        // Check for blocking terrain (mountains block vision beyond them)
                        if (!BlocksVision(neighbor))
                        {
                            queue.Enqueue((neighbor, currentDist + 1));
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

    private int GetArmySightRange(Army army)
    {
        if (army == null) return defaultSightRange;
        int best = defaultSightRange;
        if (army.units != null)
        {
            foreach (var u in army.units)
            {
                if (u == null || u.data == null) continue;
                if (u.data.sightRange > best) best = u.data.sightRange;
            }
        }
        return best;
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
