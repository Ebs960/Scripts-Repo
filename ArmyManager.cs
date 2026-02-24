using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages all armies on the campaign map (Total War style)
/// Handles army creation, movement, merging, splitting, and battle initiation
/// </summary>
public class ArmyManager : MonoBehaviour
{
    public static ArmyManager Instance { get; private set; }
    
    [Header("Army Settings")]
    [Tooltip("Default maximum units per army")]
    [Range(1, 40)]
    public int defaultMaxUnitsPerArmy = 20;
    [Tooltip("Minimum units required to form an army")]
    [Range(1, 5)]
    public int minUnitsForArmy = 1;
    
    [Header("Army Visuals")]
    [Tooltip("Prefab for army visual representation (optional)")]
    public GameObject armyVisualPrefab;
    
    // All armies on the campaign map
    private Dictionary<int, Army> allArmies = new Dictionary<int, Army>();
    private List<Army> armiesList = new List<Army>(); // For iteration
    
    // Selected armies (for player control)
    private List<Army> selectedArmies = new List<Army>();
    
    // Cached FindObjectsByType results to avoid expensive scene searches
    private static CombatUnit[] cachedAllCombatUnits;
    private static float lastCombatUnitCacheUpdate = 0f;
    private const float COMBAT_UNIT_CACHE_UPDATE_INTERVAL = 0.5f; // Update cache every 0.5 seconds
    
    // Reusable collections for pathfinding (avoid allocations)
    private Queue<int> reusablePathfindingQueue = new Queue<int>();
    private HashSet<int> reusablePathfindingVisited = new HashSet<int>();
    private Dictionary<int, int> reusablePathfindingParent = new Dictionary<int, int>();
    private List<int> reusablePathfindingPath = new List<int>();
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
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
        // Reset army movement points for the civ whose turn it is
        var civArmies = GetArmiesByOwner(civ);
        foreach (var army in civArmies)
        {
            if (army != null)
            {
                army.ResetForNewTurn();
            }
        }
    }
    
    void Update()
    {
        // Enforce army-only system on campaign map (only check periodically for performance)
        if (Time.frameCount % 60 == 0) // Every 60 frames (~1 second)
        {
            EnforceArmyOnlySystem();
        }
    }
    
    /// <summary>
    /// Enforce that all units on campaign map are in armies (Total War style)
    /// Units not in armies are automatically added to armies
    /// </summary>
    private void EnforceArmyOnlySystem()
    {
        // Use cached combat units array to avoid expensive FindObjectsByType call
        if (Time.time - lastCombatUnitCacheUpdate > COMBAT_UNIT_CACHE_UPDATE_INTERVAL)
        {
            cachedAllCombatUnits = FindObjectsByType<CombatUnit>(FindObjectsSortMode.None);
            lastCombatUnitCacheUpdate = Time.time;
        }
        
        var unitsNotInArmies = new List<CombatUnit>();
        
        if (cachedAllCombatUnits != null)
        {
            foreach (var unit in cachedAllCombatUnits)
            {
                if (unit == null || unit.gameObject == null) continue;
                
                // Skip if unit is inactive (already hidden in army)
                if (!unit.gameObject.activeSelf) continue;
                
                // Check if unit is in any army (manual loop to avoid LINQ)
                bool isInArmy = false;
                foreach (var army in armiesList)
                {
                    if (army != null && army.units.Contains(unit))
                    {
                        isInArmy = true;
                        break;
                    }
                }
                
                if (!isInArmy)
                {
                    unitsNotInArmies.Add(unit);
                }
            }
        }
        
        // Add orphaned units to armies
        foreach (var unit in unitsNotInArmies)
        {
            if (unit == null || unit.owner == null) continue;
            
            // Try to add to existing army at same tile (manual loop to avoid LINQ)
            var armiesAtTile = GetArmiesAtTile(unit.currentTileIndex, unit.planetIndex);
            Army friendlyArmy = null;
            foreach (var army in armiesAtTile)
            {
                if (army != null && army.owner == unit.owner && army.units.Count < army.maxUnits)
                {
                    friendlyArmy = army;
                    break;
                }
            }
            
            if (friendlyArmy != null)
            {
                friendlyArmy.AddUnit(unit);
}
            else
            {
                // Create new army for this unit
                var newArmy = CreateArmy(new List<CombatUnit> { unit }, unit.owner);
                if (newArmy != null)
                {
                    newArmy.MoveToTile(unit.currentTileIndex);
}
            }
        }
    }
    
    /// <summary>
    /// Create a new army from a list of units
    /// </summary>
    public Army CreateArmy(List<CombatUnit> units, Civilization owner, string armyName = null)
    {
        if (units == null || units.Count < minUnitsForArmy)
        {
            Debug.LogWarning($"[ArmyManager] Cannot create army: need at least {minUnitsForArmy} units");
            return null;
        }
        
        if (owner == null)
        {
            Debug.LogWarning("[ArmyManager] Cannot create army: owner is null");
            return null;
        }
        
        // Create army GameObject
        GameObject armyGO = new GameObject(armyName ?? $"Army_{owner.civData.civName}");
        Army army = armyGO.AddComponent<Army>();
        army.owner = owner;
        army.maxUnits = defaultMaxUnitsPerArmy;
        army.armyName = armyName ?? $"Army_{owner.civData.civName}";
        // Multi-planet: army belongs to the same planet as its first unit.
        army.planetIndex = (units != null && units.Count > 0 && units[0] != null) ? units[0].planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        
        // Add all units to army
        foreach (var unit in units)
        {
            if (unit != null && unit.owner == owner)
            {
                army.AddUnit(unit);
            }
        }
        
        // Set position to first unit's position
        if (units.Count > 0 && units[0] != null)
        {
            army.currentTileIndex = units[0].currentTileIndex;
            var ts = TileSystem.GetForPlanet(army.planetIndex) ?? TileSystem.Instance;
            if (ts != null)
            {
                Vector3 worldPos = ts.GetTileCenterFlat(army.currentTileIndex);
                army.transform.position = worldPos;
            }
        }
        
        // Register army
        RegisterArmy(army);
return army;
    }
    
    /// <summary>
    /// Register an army with the manager
    /// </summary>
    public void RegisterArmy(Army army)
    {
        if (army == null) return;
        
        if (!allArmies.ContainsKey(army.armyId))
        {
            allArmies[army.armyId] = army;
            armiesList.Add(army);
}
    }
    
    /// <summary>
    /// Unregister an army
    /// </summary>
    public void UnregisterArmy(Army army)
    {
        if (army == null) return;
        
        if (allArmies.ContainsKey(army.armyId))
        {
            allArmies.Remove(army.armyId);
            armiesList.Remove(army);
            selectedArmies.Remove(army);
}
    }
    
    /// <summary>
    /// Destroy an army and release its units
    /// </summary>
    public void DestroyArmy(Army army)
    {
        if (army == null) return;
        
        UnregisterArmy(army);
        army.DestroyArmy();
        Destroy(army.gameObject);
    }
    
    /// <summary>
    /// Get all armies owned by a civilization (manual loop to avoid LINQ allocation)
    /// </summary>
    public List<Army> GetArmiesByOwner(Civilization owner)
    {
        var result = new List<Army>();
        foreach (var army in armiesList)
        {
            if (army != null && army.owner == owner)
            {
                result.Add(army);
            }
        }
        return result;
    }
    
    /// <summary>
    /// Get all armies at a specific tile (manual loop to avoid LINQ allocation)
    /// </summary>
    public List<Army> GetArmiesAtTile(int tileIndex, int planetIndex = -1)
    {
        var result = new List<Army>();
        if (planetIndex < 0) planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        foreach (var army in armiesList)
        {
            if (army != null && army.planetIndex == planetIndex && army.currentTileIndex == tileIndex)
            {
                result.Add(army);
            }
        }
        return result;
    }
    
    /// <summary>
    /// Select an army (for player control)
    /// </summary>
    public void SelectArmy(Army army)
    {
        if (army == null) return;
        if (!selectedArmies.Contains(army))
        {
            selectedArmies.Add(army);
        }
        
        // Show army info in UI
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowArmyPanelForArmy(army);
        }
    }
    
    /// <summary>
    /// Deselect an army
    /// </summary>
    public void DeselectArmy(Army army)
    {
        if (army == null) return;
        selectedArmies.Remove(army);
        // Hide army UI if nothing is selected
        if (UIManager.Instance != null && selectedArmies.Count == 0)
        {
            UIManager.Instance.HideArmyPanel();
        }
    }
    
    /// <summary>
    /// Clear all army selections
    /// </summary>
    public void ClearSelection()
    {
        selectedArmies.Clear();
        if (UIManager.Instance != null)
        {
            UIManager.Instance.HideArmyPanel();
        }
    }
    
    /// <summary>
    /// Get currently selected armies
    /// </summary>
    public List<Army> GetSelectedArmies()
    {
        return new List<Army>(selectedArmies);
    }
    
    /// <summary>
    /// Move selected armies to a tile (right-click movement)
    /// Uses pathfinding to move armies across the campaign map
    /// Checks movement points before moving
    /// </summary>
    public void MoveSelectedArmiesToTile(int tileIndex)
    {
        foreach (var army in selectedArmies)
        {
            if (army == null || army.currentTileIndex < 0) continue;
            var ts = TileSystem.GetForPlanet(army.planetIndex) ?? TileSystem.Instance;
            if (ts == null || !ts.IsReady()) continue;
            
            // Check if army has movement points
            if (army.currentMovePoints <= 0)
            {
continue;
            }
            
            // Find path to target tile
            var path = FindPath(army.currentTileIndex, tileIndex, ts);
            if (path != null && path.Count > 1)
            {
                // Limit path length based on available movement points
                // Calculate how many tiles the army can actually move
                int maxTilesToMove = 0;
                int remainingPoints = army.currentMovePoints;
                
                for (int i = 1; i < path.Count && remainingPoints > 0; i++)
                {
                    int tileIdx = path[i];
                    var tileData = ts.GetTileData(tileIdx);
                    if (tileData != null)
                    {
                        int cost = BiomeHelper.GetMovementCost(tileData.biome);
                        if (remainingPoints >= cost)
                        {
                            remainingPoints -= cost;
                            maxTilesToMove = i + 1; // +1 because we include start tile
                        }
                        else
                        {
                            break; // Can't afford this tile
                        }
                    }
                }
                
                // Trim path to only tiles we can reach (remove excess elements instead of creating new list)
                if (maxTilesToMove > 1 && maxTilesToMove < path.Count)
                {
                    // Remove elements from end instead of creating new list
                    for (int i = path.Count - 1; i >= maxTilesToMove; i--)
                    {
                        path.RemoveAt(i);
                    }
                }
                else
                {
continue;
                }
                
                // Start movement coroutine
                if (army.gameObject.activeInHierarchy)
                {
                    var mover = army.GetComponent<ArmyMover>();
                    if (mover == null)
                    {
                        mover = army.gameObject.AddComponent<ArmyMover>();
                    }
                    mover.MoveToTile(tileIndex, path, army.armyMoveSpeed);
                }
            }
            else
            {
                // Direct move if pathfinding fails (but still check movement points)
                if (army.CanMoveTo(tileIndex))
                {
                    army.MoveToTile(tileIndex);
                }
                else
                {
}
            }
        }
    }
    
    /// <summary>
    /// Simple pathfinding for armies (A* or simple neighbor-based)
    /// Uses reusable collections to avoid allocations
    /// </summary>
    private List<int> FindPath(int startTile, int targetTile, TileSystem ts)
    {
        if (ts == null || !ts.IsReady()) return null;
        if (startTile == targetTile)
        {
            reusablePathfindingPath.Clear();
            reusablePathfindingPath.Add(startTile);
            return new List<int>(reusablePathfindingPath);
        }
        
        // Clear and reuse collections (avoid allocations)
        reusablePathfindingQueue.Clear();
        reusablePathfindingVisited.Clear();
        reusablePathfindingParent.Clear();
        reusablePathfindingPath.Clear();
        
        reusablePathfindingQueue.Enqueue(startTile);
        reusablePathfindingVisited.Add(startTile);
        reusablePathfindingParent[startTile] = -1;
        
        while (reusablePathfindingQueue.Count > 0)
        {
            int current = reusablePathfindingQueue.Dequeue();
            
            if (current == targetTile)
            {
                // Reconstruct path
                reusablePathfindingPath.Clear();
                int node = targetTile;
                while (node != -1)
                {
                    reusablePathfindingPath.Add(node);
                    node = reusablePathfindingParent[node];
                }
                reusablePathfindingPath.Reverse();
                return new List<int>(reusablePathfindingPath);
            }
            
            // Check neighbors
            var neighbors = ts.GetNeighbors(current);
            foreach (int neighbor in neighbors)
            {
                if (reusablePathfindingVisited.Contains(neighbor)) continue;
                
                var tileData = ts.GetTileData(neighbor);
                if (tileData == null || !tileData.isLand) continue; // Only move on land
                
                reusablePathfindingVisited.Add(neighbor);
                reusablePathfindingParent[neighbor] = current;
                reusablePathfindingQueue.Enqueue(neighbor);
            }
        }
        
        return null; // No path found
    }
    
    /// <summary>
    /// Merge selected armies into the first selected army
    /// </summary>
    public void MergeSelectedArmies()
    {
        if (selectedArmies.Count < 2) return;
        
        Army targetArmy = selectedArmies[0];
        
        for (int i = 1; i < selectedArmies.Count; i++)
        {
            Army armyToMerge = selectedArmies[i];
            if (targetArmy.CanMergeWith(armyToMerge))
            {
                targetArmy.MergeArmy(armyToMerge);
            }
        }
        
        selectedArmies.Clear();
        selectedArmies.Add(targetArmy);
    }
    
    /// <summary>
    /// Get all armies (for UI display) - manual loop to avoid LINQ
    /// </summary>
    public List<Army> GetAllArmies()
    {
        var result = new List<Army>();
        foreach (var army in armiesList)
        {
            if (army != null)
            {
                result.Add(army);
            }
        }
        return result;
    }
    
    /// <summary>
    /// Find the army containing a specific unit
    /// </summary>
    public Army GetArmyContainingUnit(CombatUnit unit)
    {
        if (unit == null) return null;
        
        foreach (var army in armiesList)
        {
            if (army != null && army.units.Contains(unit))
            {
                return army;
            }
        }
        
        return null;
    }
}

