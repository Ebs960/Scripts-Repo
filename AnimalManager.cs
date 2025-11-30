using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AnimalManager : MonoBehaviour
{
    public static AnimalManager Instance { get; private set; }

    [System.Serializable]
    public class AnimalSpawnRule
    {
        public CombatUnitData unitData;
        public int initialCount;
        public int spawnRate;
        public int maxCount;
        public Biome[] allowedBiomes;
    }

    [Header("Configure each animal type here")]
    public AnimalSpawnRule[] spawnRules;
    
    // Track animals that were recently attacked (for prey behavior)
    private Dictionary<CombatUnit, int> recentlyAttackedAnimals = new Dictionary<CombatUnit, int>();
    private const int PREY_MEMORY_TURNS = 2;

    private readonly List<CombatUnit> activeAnimals = new List<CombatUnit>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    public void SpawnInitialAnimals()
    {
        Debug.Log("[AnimalManager] SpawnInitialAnimals called");
        int prevalence = GameManager.Instance != null ? GameManager.Instance.animalPrevalence : 3;
        float[] multipliers = { 0f, 0.25f, 0.5f, 1f, 2f, 3f };
        float mult = multipliers[Mathf.Clamp(prevalence, 0, multipliers.Length - 1)];
        Debug.Log($"[AnimalManager] Animal prevalence: {prevalence}, multiplier: {mult}");
        if (mult == 0f) return;

        foreach (var rule in spawnRules)
        {
            int count = Mathf.CeilToInt(rule.initialCount * mult);
            if (count < 1 && mult > 0f) count = 1;
            for (int i = 0; i < count; i++)
                TrySpawn(rule);
        }
    }

    /// <summary>
    /// Call this when an animal takes damage to mark it as recently attacked
    /// </summary>
    public void MarkAnimalAsAttacked(CombatUnit animal)
    {
        if (animal != null && animal.data.unitType == CombatCategory.Animal)
        {
            recentlyAttackedAnimals[animal] = GameManager.Instance.currentTurn;
            Debug.Log($"Animal {animal.data.unitName} marked as recently attacked on turn {GameManager.Instance.currentTurn}");
        }
    }
    
    /// <summary>
    /// Check if an animal was recently attacked (within PREY_MEMORY_TURNS)
    /// </summary>
    private bool WasRecentlyAttacked(CombatUnit animal)
    {
        if (recentlyAttackedAnimals.TryGetValue(animal, out int attackTurn))
        {
            int turnsSinceAttack = GameManager.Instance.currentTurn - attackTurn;
            return turnsSinceAttack <= PREY_MEMORY_TURNS;
        }
        return false;
    }
    
    /// <summary>
    /// Find the nearest civilization unit within movement range for predators to hunt
    /// </summary>
    private CombatUnit FindNearestCivilizationUnit(CombatUnit predator, int maxSearchRange = 3)
    {
        CombatUnit nearestTarget = null;
        float nearestDistance = float.MaxValue;
        var tileSystem = TileSystem.Instance;

        foreach (var civUnit in UnitRegistry.GetCombatUnits())
        {
            if (civUnit == null || civUnit == predator)
                continue;

            if (civUnit.data == null || civUnit.data.unitType == CombatCategory.Animal)
                continue;

            if (civUnit.owner == null || civUnit.currentTileIndex < 0)
                continue;

            float distance = tileSystem != null
                ? tileSystem.GetTileDistance(predator.currentTileIndex, civUnit.currentTileIndex)
                : float.MaxValue;

            if (distance <= maxSearchRange && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTarget = civUnit;
            }
        }

        return nearestTarget;
    }
    
    /// <summary>
    /// Get direction away from the nearest civilization unit for prey to flee
    /// </summary>
    private int? GetFleeDirection(CombatUnit prey)
    {
        var nearestCivUnit = FindNearestCivilizationUnit(prey, 4); // Slightly larger range for detection
        if (nearestCivUnit == null) return null;
        
    var neighborIndices = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(prey.currentTileIndex) : System.Array.Empty<int>();
        var validDestinations = neighborIndices
            .Where(index =>
            {
                var neighbor = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(index) : null;
                return neighbor != null && prey.CanMoveTo(index);
            })
            .ToList();
        
        if (validDestinations.Count == 0) return null;
        
        // Find the destination that is furthest from the civilization unit
        int bestDestination = validDestinations[0];
    float maxDistance = TileSystem.Instance != null ? TileSystem.Instance.GetTileDistance(bestDestination, nearestCivUnit.currentTileIndex) : 0f;
        
        foreach (var destination in validDestinations)
        {
            float distance = TileSystem.Instance != null ? TileSystem.Instance.GetTileDistance(destination, nearestCivUnit.currentTileIndex) : 0f;
            if (distance > maxDistance)
            {
                maxDistance = distance;
                bestDestination = destination;
            }
        }
        
        return bestDestination;
    }

    public void ProcessTurn()
    {
        SpawnNewAnimals();
        MoveAllAnimals();
    }

    void SpawnNewAnimals()
    {
        int prevalence = GameManager.Instance != null ? GameManager.Instance.animalPrevalence : 3;
        float[] multipliers = { 0f, 0.25f, 0.5f, 1f, 2f, 3f };
        float mult = multipliers[Mathf.Clamp(prevalence, 0, multipliers.Length - 1)];
        if (mult == 0f) return;

        foreach (var rule in spawnRules)
        {
            int already = activeAnimals.Count(u => u != null && u.data == rule.unitData);
            int maxCount = Mathf.CeilToInt(rule.maxCount * mult);
            if (maxCount < 1 && mult > 0f) maxCount = 1;
            int spawnRate = Mathf.CeilToInt(rule.spawnRate * mult);
            if (spawnRate < 1 && mult > 0f) spawnRate = 1;
            int toSpawn = Mathf.Min(spawnRate, maxCount - already);

            for (int i = 0; i < toSpawn; i++)
                TrySpawn(rule);
        }
    }

    void MoveAllAnimals()
    {
        // Clean up old attack records first
        CleanupOldAttackRecords();
        
        foreach (var unit in activeAnimals.ToList())
        {
            if (unit == null)
            {
                activeAnimals.Remove(unit);
                continue;
            }

            unit.ResetForNewTurn();

            var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(unit.currentTileIndex) : null;
            if (tileData == null) continue;

            // Determine movement behavior based on animal type
            bool moved = false;
            switch (unit.data.animalBehavior)
            {
                case AnimalBehaviorType.Predator:
                    moved = HandlePredatorMovement(unit);
                    break;
                    
                case AnimalBehaviorType.Prey:
                    moved = HandlePreyMovement(unit);
                    break;
                    
                case AnimalBehaviorType.Neutral:
                default:
                    moved = HandleNeutralMovement(unit);
                    break;
            }
            
            // If no special behavior movement occurred, fall back to random movement
            if (!moved)
            {
                HandleNeutralMovement(unit);
            }
        }
    }
    
    /// <summary>
    /// Clean up attack records older than PREY_MEMORY_TURNS
    /// </summary>
    private void CleanupOldAttackRecords()
    {
        var currentTurn = GameManager.Instance.currentTurn;
        var expiredRecords = recentlyAttackedAnimals
            .Where(kvp => currentTurn - kvp.Value > PREY_MEMORY_TURNS)
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var expiredAnimal in expiredRecords)
        {
            recentlyAttackedAnimals.Remove(expiredAnimal);
        }
    }
    
    /// <summary>
    /// Handle movement for predator animals - actively hunt civilization units
    /// </summary>
    private bool HandlePredatorMovement(CombatUnit predator)
    {
        var target = FindNearestCivilizationUnit(predator);
        if (target == null) return false;
        
        // Try to move closer to the target
    var neighborIndices = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(predator.currentTileIndex) : System.Array.Empty<int>();
        var validDestinations = neighborIndices
            .Where(index =>
            {
                var neighbor = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(index) : null;
                return neighbor != null && predator.CanMoveTo(index);
            })
            .ToList();
        
        if (validDestinations.Count == 0) return false;
        
        // Find the destination that gets us closest to the target
        int bestDestination = validDestinations[0];
    float minDistance = TileSystem.Instance != null ? TileSystem.Instance.GetTileDistance(bestDestination, target.currentTileIndex) : float.MaxValue;
        
        foreach (var destination in validDestinations)
        {
            float distance = TileSystem.Instance != null ? TileSystem.Instance.GetTileDistance(destination, target.currentTileIndex) : float.MaxValue;
            if (distance < minDistance)
            {
                minDistance = distance;
                bestDestination = destination;
            }
        }
        
        predator.MoveTo(bestDestination);
        Debug.Log($"Predator {predator.data.unitName} hunting towards {target.data.unitName}");
        return true;
    }
    
    /// <summary>
    /// Handle movement for prey animals - avoid civilization units unless recently attacked
    /// </summary>
    private bool HandlePreyMovement(CombatUnit prey)
    {
        bool wasAttacked = WasRecentlyAttacked(prey);
        
        if (wasAttacked)
        {
            // Prey was recently attacked, so it's aggressive and will hunt like a predator
            Debug.Log($"Prey {prey.data.unitName} is aggressively seeking revenge!");
            return HandlePredatorMovement(prey); // Use predator logic for aggressive behavior
        }
        else
        {
            // Normal prey behavior - try to flee from civilization units
            int? fleeDestination = GetFleeDirection(prey);
            if (fleeDestination.HasValue)
            {
                prey.MoveTo(fleeDestination.Value);
                Debug.Log($"Prey {prey.data.unitName} fleeing from civilization units");
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Handle movement for neutral animals - random movement (original behavior)
    /// </summary>
    private bool HandleNeutralMovement(CombatUnit unit)
    {
    var neighborIndices = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(unit.currentTileIndex) : System.Array.Empty<int>();
        var validDestinations = neighborIndices
            .Where(index =>
            {
                var neighbor = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(index) : null;
                return neighbor != null && unit.CanMoveTo(index);
            })
            .ToList();

        if (validDestinations.Count > 0)
        {
            int targetTile = validDestinations[Random.Range(0, validDestinations.Count)];
            unit.MoveTo(targetTile);
            return true;
        }
        
        return false;
    }

    void TrySpawn(AnimalSpawnRule rule)
    {
        var candidates = new List<int>();
        // FIXED: Always spawn animals on Earth (planet index 0) regardless of current planet
        var planet = GameManager.Instance?.GetPlanetGenerator(0); // Force Earth
        int tileCount = planet != null && planet.Grid != null ? planet.Grid.TileCount : 0;

        for (int i = 0; i < tileCount; i++)
        {
            var tile = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(i) : null;
            if (tile == null || !tile.isLand) continue;

            if (rule.allowedBiomes != null && rule.allowedBiomes.Length > 0)
            {
                if (!rule.allowedBiomes.Contains(tile.biome)) continue;
            }

            candidates.Add(i);
        }

        if (candidates.Count == 0) return;

        int chosenIndex = candidates[Random.Range(0, candidates.Count)];
        // FIXED: Use Earth-specific positioning for animal spawning
    Vector3 pos = TileSystem.Instance != null ? TileSystem.Instance.GetTileSurfacePosition(chosenIndex, 0.5f, 0) : planet.transform.TransformPoint(planet.Grid.tileCenters[chosenIndex]);

        var animalPrefab = rule.unitData.GetPrefab();
        if (animalPrefab == null)
        {
            Debug.LogError($"[AnimalManager] Cannot spawn animal {rule.unitData.unitName}: prefab not found in Addressables. Make sure prefab is marked as Addressable with address matching unitName.");
            return;
        }
        
        var go = Instantiate(animalPrefab, pos, Quaternion.identity);
        var unit = go.GetComponent<CombatUnit>();
        if (unit == null)
        {
            Debug.LogError($"[AnimalManager] Spawned prefab for {rule.unitData.unitName} is missing CombatUnit component.");
            Destroy(go);
            return;
        }
        unit.Initialize(rule.unitData, null);
        unit.currentTileIndex = chosenIndex;
        
        // Properly orient the animal on the planetary surface
        if (planet != null && planet.Grid != null)
        {
            unit.PositionUnitOnSurface(planet.Grid, chosenIndex);
        }

        activeAnimals.Add(unit);
        unit.OnDeath += () =>
        {
            activeAnimals.Remove(unit);
            recentlyAttackedAnimals.Remove(unit);
            UnitRegistry.Unregister(unit.gameObject);
        };
    }
}
