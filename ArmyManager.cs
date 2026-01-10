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
    
    void Update()
    {
        // Check for army battles (when armies meet)
        CheckForArmyBattles();
        
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
        // Only enforce on campaign map, not in battle
        if (BattleTestSimple.Instance != null) return; // In battle, units should be visible
        
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
            var armiesAtTile = GetArmiesAtTile(unit.currentTileIndex);
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
                Debug.Log($"[ArmyManager] Auto-added orphaned unit {unit.data?.unitName ?? "Unknown"} to army {friendlyArmy.armyName}");
            }
            else
            {
                // Create new army for this unit
                var newArmy = CreateArmy(new List<CombatUnit> { unit }, unit.owner);
                if (newArmy != null)
                {
                    newArmy.MoveToTile(unit.currentTileIndex);
                    Debug.Log($"[ArmyManager] Created new army {newArmy.armyName} for orphaned unit {unit.data?.unitName ?? "Unknown"}");
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
            if (TileSystem.Instance != null)
            {
                Vector3 worldPos = TileSystem.Instance.GetTileCenterFlat(army.currentTileIndex);
                army.transform.position = worldPos;
            }
        }
        
        // Register army
        RegisterArmy(army);
        
        Debug.Log($"[ArmyManager] Created {army.armyName} with {army.units.Count} units");
        
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
            Debug.Log($"[ArmyManager] Registered {army.armyName} (ID: {army.armyId})");
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
            Debug.Log($"[ArmyManager] Unregistered {army.armyName}");
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
    public List<Army> GetArmiesAtTile(int tileIndex)
    {
        var result = new List<Army>();
        foreach (var army in armiesList)
        {
            if (army != null && army.currentTileIndex == tileIndex)
            {
                result.Add(army);
            }
        }
        return result;
    }
    
    /// <summary>
    /// Check if armies from different civilizations are at the same tile (initiate battle)
    /// Manual grouping to avoid LINQ allocations
    /// </summary>
    private void CheckForArmyBattles()
    {
        // Group armies by tile (manual grouping to avoid LINQ)
        Dictionary<int, List<Army>> armiesByTile = new Dictionary<int, List<Army>>();
        
        foreach (var army in armiesList)
        {
            if (army == null || army.currentTileIndex < 0) continue;
            
            if (!armiesByTile.ContainsKey(army.currentTileIndex))
            {
                armiesByTile[army.currentTileIndex] = new List<Army>();
            }
            armiesByTile[army.currentTileIndex].Add(army);
        }
        
        // Check each tile with multiple armies
        foreach (var kvp in armiesByTile)
        {
            var armiesAtTile = kvp.Value;
            if (armiesAtTile.Count < 2) continue; // Need at least 2 armies
            
            // Check if there are armies from different civilizations (manual grouping)
            HashSet<Civilization> civsAtTile = new HashSet<Civilization>();
            foreach (var army in armiesAtTile)
            {
                if (army != null && army.owner != null)
                {
                    civsAtTile.Add(army.owner);
                }
            }
            
            if (civsAtTile.Count > 1)
            {
                // Different civilizations at same tile - initiate battle!
                InitiateBattle(armiesAtTile);
            }
        }
    }
    
    /// <summary>
    /// Initiate a battle between armies at the same tile
    /// This will load the battle scene and create a battle map
    /// </summary>
    private void InitiateBattle(List<Army> armiesAtTile)
    {
        // Group by civilization
        var attackerArmies = new List<Army>();
        var defenderArmies = new List<Army>();
        
        // First civilization is attacker, others are defenders
        var firstCiv = armiesAtTile[0].owner;
        foreach (var army in armiesAtTile)
        {
            if (army.owner == firstCiv)
            {
                attackerArmies.Add(army);
            }
            else
            {
                defenderArmies.Add(army);
            }
        }
        
        if (defenderArmies.Count == 0) return; // No enemies
        
        // Collect all units from all armies and prepare them for battle
        var attackerUnits = new List<CombatUnit>();
        var defenderUnits = new List<CombatUnit>();
        
        foreach (var army in attackerArmies)
        {
            var battleUnits = army.GetBattleUnits();
            foreach (var unit in battleUnits)
            {
                if (unit != null)
                {
                    // Activate unit (it might be hidden in army)
                    unit.gameObject.SetActive(true);
                    // Initialize for battle
                    unit.InitializeForBattle(true);
                    attackerUnits.Add(unit);
                }
            }
        }
        
        foreach (var army in defenderArmies)
        {
            var battleUnits = army.GetBattleUnits();
            foreach (var unit in battleUnits)
            {
                if (unit != null)
                {
                    // Activate unit (it might be hidden in army)
                    unit.gameObject.SetActive(true);
                    // Initialize for battle
                    unit.InitializeForBattle(false);
                    defenderUnits.Add(unit);
                }
            }
        }
        
        if (attackerUnits.Count == 0 || defenderUnits.Count == 0) return;
        
        // Store army references for post-battle cleanup
        var allParticipatingArmies = new List<Army>();
        allParticipatingArmies.AddRange(attackerArmies);
        allParticipatingArmies.AddRange(defenderArmies);
        
        // Start battle using BattleTestSimple - this will load the battle scene
        if (BattleTestSimple.Instance != null)
        {
            var attackerCiv = attackerArmies[0].owner;
            var defenderCiv = defenderArmies[0].owner;
            
            Debug.Log($"[ArmyManager] Initiating battle: {attackerCiv.civData.civName} ({attackerUnits.Count} units) vs {defenderCiv.civData.civName} ({defenderUnits.Count} units)");
            Debug.Log($"[ArmyManager] Loading battle scene and generating battle map...");
            
            // Start battle - this will load the BattleScene and generate the battle map
            BattleTestSimple.Instance.StartBattle(attackerCiv, defenderCiv, attackerUnits, defenderUnits);
            
            // Store participating armies for post-battle cleanup
            StoreParticipatingArmies(allParticipatingArmies);
        }
        else
        {
            Debug.LogError("[ArmyManager] BattleTestSimple.Instance is null! Cannot start battle.");
        }
    }
    
    // Store armies participating in current battle for post-battle cleanup
    private List<Army> participatingArmies = new List<Army>();
    
    /// <summary>
    /// Store armies that are participating in the current battle
    /// </summary>
    private void StoreParticipatingArmies(List<Army> armies)
    {
        participatingArmies = new List<Army>(armies);
    }
    
    /// <summary>
    /// Called after battle ends to return units to armies or handle casualties
    /// </summary>
    public void OnBattleEnded(BattleResult result)
    {
        // Return surviving units to their armies
        foreach (var army in participatingArmies)
        {
            if (army == null) continue;
            
            // Collect all units from the army
            var allArmyUnits = new List<CombatUnit>(army.units);
            
            // Remove dead units from army (units with 0 health or 0 soldier count)
            var deadUnits = new List<CombatUnit>();
            foreach (var unit in allArmyUnits)
            {
                if (unit == null || unit.currentHealth <= 0 || unit.soldierCount <= 0)
                {
                    deadUnits.Add(unit);
                }
            }
            
            foreach (var deadUnit in deadUnits)
            {
                if (deadUnit != null)
                {
                    army.RemoveUnit(deadUnit);
                    // Destroy the dead unit
                    if (deadUnit.gameObject != null)
                    {
                        Destroy(deadUnit.gameObject);
                    }
                }
            }
            
            // Update army stats after casualties
            army.UpdateArmyStats();
            
            // If army is destroyed (no units left), remove it
            if (army.IsDestroyed())
            {
                DestroyArmy(army);
            }
            else
            {
                // Hide all surviving units (they're represented by the army on campaign map)
                foreach (var unit in army.units)
                {
                    if (unit != null)
                    {
                        // Ensure unit is hidden and marked as not in battle
                        unit.gameObject.SetActive(false);
                        unit.IsInBattle = false;
                        unit.battleState = BattleUnitState.Idle;
                        unit.SetRouted(false); // Clear routing state
                    }
                }
            }
        }
        
        participatingArmies.Clear();
        Debug.Log("[ArmyManager] Battle ended - units returned to armies, casualties applied");
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
            UIManager.Instance.ShowUnitInfoPanelForUnit(army);
        }
    }
    
    /// <summary>
    /// Deselect an army
    /// </summary>
    public void DeselectArmy(Army army)
    {
        if (army == null) return;
        selectedArmies.Remove(army);
    }
    
    /// <summary>
    /// Clear all army selections
    /// </summary>
    public void ClearSelection()
    {
        selectedArmies.Clear();
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
        if (TileSystem.Instance == null) return;
        
        foreach (var army in selectedArmies)
        {
            if (army == null || army.currentTileIndex < 0) continue;
            
            // Check if army has movement points
            if (army.currentMovePoints <= 0)
            {
                Debug.Log($"[ArmyManager] {army.armyName} has no movement points remaining");
                continue;
            }
            
            // Find path to target tile
            var path = FindPath(army.currentTileIndex, tileIndex);
            if (path != null && path.Count > 1)
            {
                // Limit path length based on available movement points
                // Calculate how many tiles the army can actually move
                int maxTilesToMove = 0;
                int remainingPoints = army.currentMovePoints;
                
                for (int i = 1; i < path.Count && remainingPoints > 0; i++)
                {
                    int tileIdx = path[i];
                    var tileData = TileSystem.Instance.GetTileData(tileIdx);
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
                    Debug.Log($"[ArmyManager] {army.armyName} cannot reach target (insufficient movement points)");
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
                    Debug.Log($"[ArmyManager] {army.armyName} cannot move to tile {tileIndex} (insufficient movement points or impassable)");
                }
            }
        }
    }
    
    /// <summary>
    /// Simple pathfinding for armies (A* or simple neighbor-based)
    /// Uses reusable collections to avoid allocations
    /// </summary>
    private List<int> FindPath(int startTile, int targetTile)
    {
        if (TileSystem.Instance == null) return null;
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
            var neighbors = TileSystem.Instance.GetNeighbors(current);
            foreach (int neighbor in neighbors)
            {
                if (reusablePathfindingVisited.Contains(neighbor)) continue;
                
                var tileData = TileSystem.Instance.GetTileData(neighbor);
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

