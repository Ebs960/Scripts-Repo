using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages demon armies on the campaign map.
/// Demons spawn as armies and use the same movement system as civilization armies.
/// </summary>
public class DemonManager : MonoBehaviour
{
    public static DemonManager Instance { get; private set; }

    [Header("Demon Unit Settings")]
    [SerializeField] private DemonUnitData[] demonUnits;
    [SerializeField] private float spawnChancePerTurn = 0.15f;
    [SerializeField] private int maxDemonArmies = 5;
    [SerializeField] private int minTurnsBetweenSpawns = 3;

    [Header("Army Composition")]
    [Tooltip("Minimum units per demon army")]
    [SerializeField] private int minUnitsPerArmy = 2;
    [Tooltip("Maximum units per demon army")]
    [SerializeField] private int maxUnitsPerArmy = 6;
    [Tooltip("Movement points per turn for demon armies")]
    [SerializeField] private int demonArmyMovePoints = 2;

    [Header("Spawn Requirements")]
    [Tooltip("Biomes where demons can spawn")]
    [SerializeField] private Biome[] spawnableBiomes = { Biome.Hellscape };

    [Header("AI Behavior")]
    [Tooltip("Chance to move towards nearest civilization each turn")]
    [Range(0f, 1f)]
    [SerializeField] private float aggressionChance = 0.7f;
    [Tooltip("Maximum tiles to search for targets")]
    [SerializeField] private int targetSearchRange = 10;

    // Track demon armies
    private List<DemonArmy> activeDemonArmies = new List<DemonArmy>();
    private int turnsSinceLastSpawn;

    /// <summary>
    /// Wrapper class to track demon army data
    /// </summary>
    [System.Serializable]
    public class DemonArmy
    {
        public string armyName;
        public List<CombatUnit> units = new List<CombatUnit>();
        public int currentTileIndex;
        public int planetIndex;
        public int currentMovePoints;
        public int baseMovePoints;
        public GameObject armyVisual;
        
        public int TotalAttack => units.Sum(u => u != null ? u.CurrentAttack : 0);
        public int TotalDefense => units.Sum(u => u != null ? u.CurrentDefense : 0);
        public int TotalHealth => units.Sum(u => u != null ? u.currentHealth : 0);
        public int UnitCount => units.Count(u => u != null);
    }

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

    void Start()
    {
        turnsSinceLastSpawn = 0;
    }

    /// <summary>
    /// Process demon turn - spawn new armies and move existing ones
    /// </summary>
    public void ProcessDemonTurn()
    {
        // Clean up dead armies first
        CleanupDeadArmies();
        
        // Reset movement points for all demon armies
        foreach (var army in activeDemonArmies)
        {
            army.currentMovePoints = army.baseMovePoints;
        }
        
        // Move existing demon armies
        MoveAllDemonArmies();
        
        // Try to spawn new demon armies
        turnsSinceLastSpawn++;
        if (turnsSinceLastSpawn >= minTurnsBetweenSpawns && 
            activeDemonArmies.Count < maxDemonArmies &&
            Random.value < spawnChancePerTurn)
        {
            SpawnDemonArmy();
            turnsSinceLastSpawn = 0;
        }
    }

    /// <summary>
    /// Remove armies with no living units
    /// </summary>
    private void CleanupDeadArmies()
    {
        for (int i = activeDemonArmies.Count - 1; i >= 0; i--)
        {
            var army = activeDemonArmies[i];
            
            // Remove null/dead units from army
            army.units.RemoveAll(u => u == null || u.currentHealth <= 0);
            
            // If army is empty, destroy it
            if (army.units.Count == 0)
            {
                if (army.armyVisual != null)
                    Destroy(army.armyVisual);
                    
                activeDemonArmies.RemoveAt(i);
}
        }
    }

    /// <summary>
    /// Move all demon armies using AI behavior
    /// </summary>
    private void MoveAllDemonArmies()
    {
        foreach (var army in activeDemonArmies)
        {
            if (army.units.Count == 0) continue;
            
            // Move while army has movement points
            while (army.currentMovePoints > 0)
            {
                bool moved = false;
                
                // Aggressive behavior - move towards nearest enemy
                if (Random.value < aggressionChance)
                {
                    moved = MoveTowardsTarget(army);
                }
                
                // If didn't move aggressively, try random movement
                if (!moved)
                {
                    moved = MoveRandomly(army);
                }
                
                // If couldn't move at all, stop trying
                if (!moved)
                    break;
            }
        }
    }

    /// <summary>
    /// Move army towards nearest civilization target
    /// </summary>
    private bool MoveTowardsTarget(DemonArmy army)
    {
        if (TileSystem.Instance == null) return false;
        
        // Find nearest enemy (any civilization unit, worker, or city)
        int targetTile = FindNearestTarget(army);
        if (targetTile < 0) return false;
        
        // Get neighbors and find best move towards target
        var neighbors = TileSystem.Instance.GetNeighbors(army.currentTileIndex);
        if (neighbors == null || neighbors.Length == 0) return false;
        
        int bestNeighbor = -1;
        float bestDistance = float.MaxValue;
        
        foreach (int neighbor in neighbors)
        {
            var tileData = TileSystem.Instance.GetTileData(neighbor);
            if (tileData == null || !CanDemonArmyEnterTile(tileData)) continue;
            
            float dist = TileSystem.Instance.GetTileDistance(neighbor, targetTile);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestNeighbor = neighbor;
            }
        }
        
        if (bestNeighbor >= 0)
        {
            MoveDemonArmy(army, bestNeighbor);
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Find nearest target (city, army, or unit)
    /// </summary>
    private int FindNearestTarget(DemonArmy army)
    {
        if (TileSystem.Instance == null) return -1;
        
        float nearestDist = float.MaxValue;
        int nearestTile = -1;
        
        // Check for civilization armies (all armies that have owners)
        if (ArmyManager.Instance != null)
        {
            foreach (var civ in CivilizationManager.Instance?.GetAllCivs() ?? new List<Civilization>())
            {
                if (civ == null) continue;
                
                var civArmies = ArmyManager.Instance.GetArmiesByOwner(civ);
                foreach (var civArmy in civArmies)
                {
                    if (civArmy == null) continue;
                    if (civArmy.currentTileIndex == army.currentTileIndex) continue;
                    
                    float dist = TileSystem.Instance.GetTileDistance(army.currentTileIndex, civArmy.currentTileIndex);
                    if (dist < nearestDist && dist <= targetSearchRange)
                    {
                        nearestDist = dist;
                        nearestTile = civArmy.currentTileIndex;
                    }
                }
            }
        }
        
        // Check for cities
        if (CivilizationManager.Instance != null)
        {
            foreach (var civ in CivilizationManager.Instance.GetAllCivs())
            {
                if (civ == null || civ.cities == null) continue;
                
                foreach (var city in civ.cities)
                {
                    if (city == null) continue;
                    
                    float dist = TileSystem.Instance.GetTileDistance(army.currentTileIndex, city.centerTileIndex);
                    if (dist < nearestDist && dist <= targetSearchRange)
                    {
                        nearestDist = dist;
                        nearestTile = city.centerTileIndex;
                    }
                }
            }
        }
        
        // Check for workers
        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (worker == null) continue;
            
            float dist = TileSystem.Instance.GetTileDistance(army.currentTileIndex, worker.currentTileIndex);
            if (dist < nearestDist && dist <= targetSearchRange)
            {
                nearestDist = dist;
                nearestTile = worker.currentTileIndex;
            }
        }
        
        return nearestTile;
    }

    /// <summary>
    /// Move army randomly
    /// </summary>
    private bool MoveRandomly(DemonArmy army)
    {
        if (TileSystem.Instance == null) return false;
        
        var neighbors = TileSystem.Instance.GetNeighbors(army.currentTileIndex);
        if (neighbors == null || neighbors.Length == 0) return false;
        
        // Shuffle neighbors
        var validNeighbors = new List<int>();
        foreach (int neighbor in neighbors)
        {
            var tileData = TileSystem.Instance.GetTileData(neighbor);
            if (tileData != null && CanDemonArmyEnterTile(tileData))
            {
                validNeighbors.Add(neighbor);
            }
        }
        
        if (validNeighbors.Count > 0)
        {
            int targetTile = validNeighbors[Random.Range(0, validNeighbors.Count)];
            MoveDemonArmy(army, targetTile);
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Check if demon army can enter a tile
    /// </summary>
    private bool CanDemonArmyEnterTile(HexTileData tileData)
    {
        if (tileData == null) return false;
        if (!tileData.isPassable) return false;
        
        // Demons can cross lava/hellscape
        if (tileData.biome == Biome.Hellscape)
            return true;
        
        // Can enter land tiles
        if (tileData.isLand)
            return true;
        
        return false;
    }

    /// <summary>
    /// Move demon army to a new tile
    /// </summary>
    private void MoveDemonArmy(DemonArmy army, int targetTile)
    {
        if (TileSystem.Instance == null) return;
        
        // Deduct movement point
        army.currentMovePoints--;
        
        // Update position
        army.currentTileIndex = targetTile;
        
        // Move visual
        if (army.armyVisual != null)
        {
            Vector3 worldPos = TileSystem.Instance.GetTileSurfacePosition(targetTile);
            army.armyVisual.transform.position = worldPos;
        }
        
        // Move all units
        foreach (var unit in army.units)
        {
            if (unit != null)
            {
                unit.currentTileIndex = targetTile;
                // Keep units hidden (they're in the army)
            }
        }
        
        // Check for encounters
        CheckForEncounters(army);
    }

    /// <summary>
    /// Check if demon army encounters enemies at current tile
    /// </summary>
    private void CheckForEncounters(DemonArmy army)
    {
        // Check for civilization armies at this tile
        if (ArmyManager.Instance != null)
        {
            var armiesAtTile = ArmyManager.Instance.GetArmiesAtTile(army.currentTileIndex);
            foreach (var civArmy in armiesAtTile)
            {
                if (civArmy != null && civArmy.owner != null)
                {
                    // Trigger battle!
InitiateBattleWithArmy(army, civArmy);
                    return;
                }
            }
        }
        
        // Check for workers at this tile
        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (worker != null && worker.currentTileIndex == army.currentTileIndex)
            {
                // Auto-resolve attack on worker
                AttackWorker(army, worker);
            }
        }
    }

    /// <summary>
    /// Initiate real-time battle between demon army and civilization army
    /// </summary>
    private void InitiateBattleWithArmy(DemonArmy demonArmy, Army civArmy)
    {
        if (BattleTestSimple.Instance == null)
        {
            Debug.LogError("[DemonManager] BattleTestSimple not available for demon battle");
            return;
        }
        
        // Prepare demon units for battle
        var demonUnitsForBattle = new List<CombatUnit>();
        foreach (var unit in demonArmy.units)
        {
            if (unit != null)
            {
                unit.gameObject.SetActive(true);
                unit.InitializeForBattle(true); // Demons are attackers
                demonUnitsForBattle.Add(unit);
            }
        }
        
        // Prepare civilization units for battle
        var civUnitsForBattle = civArmy.GetBattleUnits();
        foreach (var unit in civUnitsForBattle)
        {
            if (unit != null)
            {
                unit.gameObject.SetActive(true);
                unit.InitializeForBattle(false); // Civ units are defenders
            }
        }
        
        if (demonUnitsForBattle.Count == 0 || civUnitsForBattle.Count == 0)
        {
            Debug.LogWarning("[DemonManager] Cannot start battle - one side has no units");
            return;
        }
// Start battle - demons have no civilization, so pass null for attacker
        BattleTestSimple.Instance.StartBattle(null, civArmy.owner, demonUnitsForBattle, civUnitsForBattle);
    }

    /// <summary>
    /// Auto-resolve attack on a worker (instant kill)
    /// </summary>
    private void AttackWorker(DemonArmy army, WorkerUnit worker)
    {
        if (worker == null) return;
// Instant kill
        worker.ApplyDamage(worker.MaxHealth);
        
        // Notify player
        if (UIManager.Instance != null && worker.owner != null && worker.owner.isPlayerControlled)
        {
            UIManager.Instance.ShowNotification($"Demons have killed your {worker.UnitName}!");
        }
    }

    /// <summary>
    /// Spawn a new demon army
    /// </summary>
    private void SpawnDemonArmy()
    {
        if (TileSystem.Instance == null)
        {
            Debug.LogWarning("[DemonManager] TileSystem not ready, cannot spawn demons");
            return;
        }
        
        if (GameManager.Instance == null)
        {
            Debug.LogError("[DemonManager] GameManager not available");
            return;
        }
        
        // Find valid spawn locations across ALL planets
        List<(int tileIndex, int planetIndex)> validTiles = new List<(int, int)>();
        
        var planetData = GameManager.Instance.GetPlanetData();
        foreach (var kvp in planetData)
        {
            int planetIndex = kvp.Key;
            var planetGen = GameManager.Instance.GetPlanetGenerator(planetIndex);
            if (planetGen == null || planetGen.Grid == null) continue;
            
            var planetGrid = planetGen.Grid;
            for (int i = 0; i < planetGrid.TileCount; i++)
            {
                var tileData = TileSystem.Instance.GetTileDataFromPlanet(i, planetIndex);
                if (tileData == null) continue;

                // Check if tile is a valid demon spawn biome
                if (!spawnableBiomes.Contains(tileData.biome)) continue;

                // Check if tile is unoccupied
                if (tileData.occupantId != 0) continue;

                validTiles.Add((i, planetIndex));
            }
        }

        if (validTiles.Count == 0) 
        {
return;
        }

        // Pick random spawn location
        var (spawnTileIndex, spawnPlanetIndex) = validTiles[Random.Range(0, validTiles.Count)];
        
        Vector3 spawnPos = TileSystem.Instance.GetTileSurfacePosition(spawnTileIndex);

        // Create the demon army
        DemonArmy newArmy = new DemonArmy
        {
            armyName = GenerateDemonArmyName(),
            currentTileIndex = spawnTileIndex,
            planetIndex = spawnPlanetIndex,
            baseMovePoints = demonArmyMovePoints,
            currentMovePoints = demonArmyMovePoints
        };
        
        // Determine army size
        int armySize = Random.Range(minUnitsPerArmy, maxUnitsPerArmy + 1);
        
        // Spawn demon units
        for (int i = 0; i < armySize; i++)
        {
            // Pick random demon type
            DemonUnitData demonType = demonUnits[Random.Range(0, demonUnits.Length)];

            var demonPrefab = demonType.GetPrefab();
            if (demonPrefab == null)
            {
                Debug.LogError($"[DemonManager] Cannot spawn demon {demonType.unitName}: prefab not found");
                continue;
            }
            
            var demonGO = Instantiate(demonPrefab, spawnPos, Quaternion.identity);
            var demonUnit = demonGO.GetComponent<CombatUnit>();
            if (demonUnit == null)
            {
                Debug.LogError($"[DemonManager] Spawned prefab for {demonType.unitName} missing CombatUnit");
                Destroy(demonGO);
                continue;
            }
            
            // Initialize with no owner (demons are ownerless)
            demonUnit.Initialize(demonType, null);
            demonUnit.currentTileIndex = spawnTileIndex;
            
            // Hide unit (it's in an army)
            demonGO.SetActive(false);
            
            // Add to army
            newArmy.units.Add(demonUnit);
            
            // Track death
            demonUnit.OnDeath += () => OnDemonUnitDeath(newArmy, demonUnit);
        }
        
        if (newArmy.units.Count == 0)
        {
            Debug.LogWarning("[DemonManager] Failed to spawn any demon units for army");
            return;
        }
        
        // Create army visual
        CreateArmyVisual(newArmy, spawnPos);
        
        // Add to tracked armies
        activeDemonArmies.Add(newArmy);
}

    /// <summary>
    /// Create visual representation for demon army on campaign map
    /// </summary>
    private void CreateArmyVisual(DemonArmy army, Vector3 position)
    {
        GameObject visual = null;
        
        // Try to get prefab from the first demon unit's DemonUnitData
        if (army.units.Count > 0 && army.units[0] != null)
        {
            DemonUnitData demonData = army.units[0].data as DemonUnitData;
            if (demonData != null && demonData.demonArmyPrefab != null)
            {
                visual = Instantiate(demonData.demonArmyPrefab, position, Quaternion.identity);
                visual.name = $"DemonArmy_{army.armyName}";
            }
        }
        
        // Fallback to primitive if no prefab available
        if (visual == null)
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = $"DemonArmy_{army.armyName}";
            visual.transform.position = position;
            visual.transform.localScale = new Vector3(1.5f, 2f, 1.5f);
            
            // Remove collider (we don't want physics interactions)
            var collider = visual.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            
            // Set demon color (dark red)
            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.5f, 0f, 0f, 1f);
            }
        }
        
        // Add selection indicator beneath
        CreateSelectionIndicator(visual);
        
        army.armyVisual = visual;
    }
    
    /// <summary>
    /// Create a selection indicator beneath the army visual
    /// </summary>
    private void CreateSelectionIndicator(GameObject armyVisual)
    {
        // Create circular indicator
        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        indicator.name = "SelectionIndicator";
        indicator.transform.SetParent(armyVisual.transform);
        indicator.transform.localPosition = new Vector3(0, -0.5f, 0);
        indicator.transform.localScale = new Vector3(2f, 0.05f, 2f);
        
        // Remove collider
        var collider = indicator.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        
        // Set demon indicator color (dark red, semi-transparent)
        var renderer = indicator.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = new Color(0.5f, 0f, 0f, 0.5f);
        }
        
        // Initially hidden (shown when selected)
        indicator.SetActive(false);
    }

    /// <summary>
    /// Handle demon unit death
    /// </summary>
    private void OnDemonUnitDeath(DemonArmy army, CombatUnit unit)
    {
        if (army != null && army.units != null)
        {
            army.units.Remove(unit);
        }
    }

    /// <summary>
    /// Generate a random demon army name
    /// </summary>
    private string GenerateDemonArmyName()
    {
        string[] prefixes = { "Infernal", "Hellborn", "Abyssal", "Burning", "Damned", "Accursed", "Blighted", "Corrupted" };
        string[] suffixes = { "Legion", "Horde", "Warband", "Host", "Swarm", "Onslaught", "Ravagers", "Destroyers" };
        
        return $"{prefixes[Random.Range(0, prefixes.Length)]} {suffixes[Random.Range(0, suffixes.Length)]}";
    }

    /// <summary>
    /// Remove a demon army (called after battle if all units die)
    /// </summary>
    public void RemoveDemonArmy(DemonArmy army)
    {
        if (army == null) return;
        
        // Destroy remaining units
        foreach (var unit in army.units)
        {
            if (unit != null)
                Destroy(unit.gameObject);
        }
        army.units.Clear();
        
        // Destroy visual
        if (army.armyVisual != null)
            Destroy(army.armyVisual);
        
        activeDemonArmies.Remove(army);
    }

    /// <summary>
    /// Get all active demon armies
    /// </summary>
    public List<DemonArmy> GetActiveDemonArmies()
    {
        return new List<DemonArmy>(activeDemonArmies);
    }

    /// <summary>
    /// Get demon armies at a specific tile
    /// </summary>
    public List<DemonArmy> GetDemonArmiesAtTile(int tileIndex)
    {
        var result = new List<DemonArmy>();
        foreach (var army in activeDemonArmies)
        {
            if (army.currentTileIndex == tileIndex)
                result.Add(army);
        }
        return result;
    }

    /// <summary>
    /// Legacy method for backwards compatibility
    /// </summary>
    public void RemoveDemon(CombatUnit demonUnit)
    {
        foreach (var army in activeDemonArmies)
        {
            if (army.units.Contains(demonUnit))
            {
                army.units.Remove(demonUnit);
                return;
            }
        }
    }
} 