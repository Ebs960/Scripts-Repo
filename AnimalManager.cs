using System.Collections;
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
    [Header("Debug")]
    [Tooltip("Enable verbose logging for animal spawning decisions")]
    public bool debugSpawning = false;
    // Track which planets have had initial animals spawned
    private readonly HashSet<int> spawnedPlanetIndices = new HashSet<int>();
    
    // Track animals that were recently attacked (for prey behavior)
    private Dictionary<CombatUnit, int> recentlyAttackedAnimals = new Dictionary<CombatUnit, int>();
    private const int PREY_MEMORY_TURNS = 2;
    
    // Animals now use the unified BaseUnit movement API (`currentMovePoints`, `RestoreMovePointsForNewTurn`, `DeductMovePoints`).

    private readonly List<CombatUnit> activeAnimals = new List<CombatUnit>();

    // Diagnostics: store spawn-time component dumps by instance id so OnDestroy can report what was attached.
    private readonly Dictionary<int, string> _spawnComponentDumpById = new Dictionary<int, string>(256);
    // Track whether we've subscribed to TurnManager.OnNeutralTurn
    private bool _subscribedToTurnManager = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnEnable()
    {
        TrySubscribeToTurnManager();
        // Animals are spawned by GameManager during the dedicated spawning phase
        // (SpawnCivsAndAnimalsOnAllPlanets) to avoid early-generation spawns being wiped out
        // and then incorrectly blocked by the "already spawned" guard.
    }

    void OnDisable()
    {
        if (_subscribedToTurnManager && TurnManager.Instance != null)
        {
            TurnManager.Instance.OnNeutralTurn -= HandleNeutralTurn;
            _subscribedToTurnManager = false;
        }
    }

    void Update()
    {
        // If TurnManager wasn't available at OnEnable time, attempt to subscribe until successful.
        if (!_subscribedToTurnManager)
            TrySubscribeToTurnManager();
    }

    private void TrySubscribeToTurnManager()
    {
        if (_subscribedToTurnManager) return;
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnNeutralTurn += HandleNeutralTurn;
            _subscribedToTurnManager = true;
        }
    }

    private void HandleNeutralTurn(int round)
    {
        // Animals run during the neutral/world phase (once per round).
        ProcessTurn();
    }

    // Backwards-compatible entry: spawns on the current planet
    public void SpawnInitialAnimals()
    {
        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        SpawnInitialAnimalsOnPlanet(pIndex);
    }

    // New per-planet spawn entrypoint. Idempotent per planet.
    public void SpawnInitialAnimalsOnPlanet(int pIndex)
    {
        if (spawnedPlanetIndices.Contains(pIndex))
        {
            // IMPORTANT: the "spawned" flag must not block respawn if animals were later destroyed/never persisted.
            // We treat the planet as spawned only if at least one live animal can be found.
            if (!HasAnyLiveAnimalsOnPlanet(pIndex))
            {
                Debug.LogWarning($"[AnimalManager] Planet {pIndex} marked spawned but no live animals found; forcing respawn.");
                spawnedPlanetIndices.Remove(pIndex);
            }
            else
            {
                if (debugSpawning) Debug.Log($"[AnimalManager] Initial animals already spawned for planet {pIndex}");
                return;
            }
        }

        int prevalence = GameManager.Instance != null ? GameManager.Instance.animalPrevalence : 3;
        float[] multipliers = { 0f, 0.25f, 0.5f, 1f, 2f, 3f };
        float mult = multipliers[Mathf.Clamp(prevalence, 0, multipliers.Length - 1)];
        if (debugSpawning) Debug.Log($"[AnimalManager] SpawnInitialAnimalsOnPlanet p={pIndex} prevalence={prevalence} multiplier={mult}");
        if (mult == 0f) return;

        // Start coroutine-based batched spawning to avoid frame hitches
        StartCoroutine(SpawnInitialAnimalsOnPlanetCoroutine(pIndex, mult));
    }

    private bool HasAnyLiveAnimalsOnPlanet(int pIndex)
    {
        // Check manager-tracked list first
        for (int i = 0; i < activeAnimals.Count; i++)
        {
            var a = activeAnimals[i];
            if (a == null) continue;
            if (a.data == null) continue;
            if (a.data.unitType != CombatCategory.Animal) continue;
            if (a.planetIndex != pIndex) continue;
            if (a.gameObject == null) continue;
            return true;
        }

        // Fallback: check global registry in case activeAnimals desynced
        foreach (var u in UnitRegistry.GetCombatUnits())
        {
            if (u == null) continue;
            if (u.data == null) continue;
            if (u.data.unitType != CombatCategory.Animal) continue;
            if (u.planetIndex != pIndex) continue;
            if (u.gameObject == null) continue;
            return true;
        }

        return false;
    }

    [Header("Spawn Batching")]
    [Tooltip("How many animals to spawn per batch/frame")]
    public int animalSpawnBatchSize = 50;
    [Tooltip("Frames to wait between batches (0 = yield 1 frame)")]
    public int animalSpawnFramesBetweenBatches = 0;

    private IEnumerator SpawnInitialAnimalsOnPlanetCoroutine(int pIndex, float mult)
    {
        if (spawnedPlanetIndices.Contains(pIndex)) yield break;
        int processed = 0;

        foreach (var rule in spawnRules)
        {
            int count = Mathf.CeilToInt(rule.initialCount * mult);
            if (count < 1 && mult > 0f) count = 1;

            // Build candidate list once for this rule (avoid rescanning the map per spawn)
            var candidates = new List<int>();
            var planet = GameManager.Instance?.GetPlanetGenerator(pIndex);
            int tileCount = planet != null && planet.Grid != null ? planet.Grid.TileCount : 0;
            var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
            if (ts == null || !ts.IsReady())
            {
                Debug.LogWarning("[AnimalManager] TileSystem not ready; cannot spawn animals.");
                if (debugSpawning) Debug.LogWarning($"[AnimalManager] TileSystem null or not ready for planet {pIndex}");
                continue;
            }

            for (int i = 0; i < tileCount; i++)
            {
                var tile = ts.GetTileData(i);
                if (tile == null) continue;

                bool isWaterTile = !tile.isLand;
                if (isWaterTile)
                {
                    bool allowsWater = rule.allowedBiomes != null && (
                        System.Array.Exists(rule.allowedBiomes, b => b == Biome.Ocean || b == Biome.Seas || b == Biome.Lake || b == Biome.River)
                    );
                    if (!allowsWater) continue;
                }

                if (rule.allowedBiomes != null && rule.allowedBiomes.Length > 0)
                {
                    bool biomeAllowed = System.Array.Exists(rule.allowedBiomes, b => b == tile.biome);
                    if (!biomeAllowed) continue;
                }

                // One unit per tile: do not spawn on tiles already occupied by a unit or city
                if (IsTileOccupiedByUnitOrCity(pIndex, i)) continue;

                candidates.Add(i);
            }

            if (debugSpawning) Debug.Log($"[AnimalManager] Found {candidates.Count} candidate tiles for rule {rule?.unitData?.unitName}");
            if (candidates.Count == 0) continue;

            // Sample up to `count` unique tiles without replacement
            if (count >= candidates.Count)
            {
                foreach (var chosenIndex in candidates)
                {
                    SpawnAnimalAtTile(rule, pIndex, chosenIndex);
                    processed++;
                    if (processed >= animalSpawnBatchSize)
                    {
                        processed = 0;
                        if (animalSpawnFramesBetweenBatches > 0)
                            for (int f = 0; f < animalSpawnFramesBetweenBatches; f++)
                                yield return null;
                        else
                            yield return null;
                    }
                }
            }
            else
            {
                // Partial sample: do a Fisher-Yates style partial shuffle
                for (int s = 0; s < count; s++)
                {
                    int r = Random.Range(s, candidates.Count);
                    int tmp = candidates[s]; candidates[s] = candidates[r]; candidates[r] = tmp;
                }
                for (int s = 0; s < count; s++)
                {
                    int chosenIndex = candidates[s];
                    SpawnAnimalAtTile(rule, pIndex, chosenIndex);
                    processed++;
                    if (processed >= animalSpawnBatchSize)
                    {
                        processed = 0;
                        if (animalSpawnFramesBetweenBatches > 0)
                            for (int f = 0; f < animalSpawnFramesBetweenBatches; f++)
                                yield return null;
                        else
                            yield return null;
                    }
                }
            }
        }

        spawnedPlanetIndices.Add(pIndex);
        yield break;
    }

    /// <summary>
    /// Call this when an animal takes damage to mark it as recently attacked
    /// </summary>
    public void MarkAnimalAsAttacked(CombatUnit animal)
    {
        if (animal != null && animal.data.unitType == CombatCategory.Animal)
        {
            recentlyAttackedAnimals[animal] = GameManager.Instance.currentTurn;
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
        int pIndex = predator != null ? predator.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        var tileSystem = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;

        foreach (var civUnit in UnitRegistry.GetCombatUnits())
        {
            if (civUnit == null || civUnit == predator)
                continue;

            if (civUnit.data == null || civUnit.data.unitType == CombatCategory.Animal)
                continue;

            if (civUnit.owner == null || civUnit.currentTileIndex < 0)
                continue;

            // Multi-planet: predators only consider targets on the same planet
            if (civUnit.planetIndex != pIndex)
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

        int pIndex = prey != null ? prey.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        
        var neighborIndices = ts != null ? ts.GetNeighbors(prey.currentTileIndex) : System.Array.Empty<int>();
        var validDestinations = neighborIndices
            .Where(index =>
            {
                var neighbor = ts != null ? ts.GetTileData(index) : null;
                return neighbor != null && prey.CanMoveTo(index);
            })
            .ToList();
        
        if (validDestinations.Count == 0) return null;
        
        // Find the destination that is furthest from the civilization unit
        int bestDestination = validDestinations[0];
        float maxDistance = ts != null ? ts.GetTileDistance(bestDestination, nearestCivUnit.currentTileIndex) : 0f;
        
        foreach (var destination in validDestinations)
        {
            float distance = ts != null ? ts.GetTileDistance(destination, nearestCivUnit.currentTileIndex) : 0f;
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

    // Handle GameManager planet-ready event to spawn animals per-planet (idempotent)
    private void HandlePlanetFullyGenerated(PlanetGenerator generator)
    {
        if (generator == null) return;
        int planetIndex = generator.planetIndex;
        if (spawnedPlanetIndices.Contains(planetIndex))
        {
            // Same rule as SpawnInitialAnimalsOnPlanet: allow respawn if animals don't actually exist.
            if (!HasAnyLiveAnimalsOnPlanet(planetIndex))
            {
                Debug.LogWarning($"[AnimalManager] Planet {planetIndex} marked spawned on fully-generated event but no live animals found; forcing respawn.");
                spawnedPlanetIndices.Remove(planetIndex);
            }
            else
            {
                return;
            }
        }

        var ts = TileSystem.GetForPlanet(planetIndex);
        if (ts == null || !ts.IsReady())
        {
            if (debugSpawning) Debug.Log($"[AnimalManager] TileSystem not ready for planet {planetIndex}; deferring animal spawn.");
            StartCoroutine(WaitForTileSystemAndSpawn(generator));
            return;
        }

        SpawnInitialAnimalsOnPlanet(planetIndex);
    }

    private IEnumerator WaitForTileSystemAndSpawn(PlanetGenerator generator)
    {
        int planetIndex = generator != null ? generator.planetIndex : 0;
        var ts = TileSystem.GetForPlanet(planetIndex);
        while (ts == null || !ts.IsReady())
        {
            ts = TileSystem.GetForPlanet(planetIndex);
            yield return null;
        }

        if (generator == null || spawnedPlanetIndices.Contains(planetIndex)) yield break;

        SpawnInitialAnimalsOnPlanet(planetIndex);
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
            
            // Restore movement points for this animal using BaseUnit API (respects animalMovePoints fallback in GetStartingMovePoints)
            try { unit.RestoreMovePointsForNewTurn(); } catch { /* ignore if override not present */ }

            var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
            var tileData = ts != null ? ts.GetTileData(unit.currentTileIndex) : null;
            if (tileData == null) continue;

            // Animals can move multiple times per turn based on their movement points
            while (unit.currentMovePoints > 0)
            {
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
                
                // If behavior had nothing to do (no target, nowhere to flee), still move: wander randomly so animals are always active
                if (!moved)
                    moved = HandleNeutralMovement(unit);
                
                if (!moved)
                    break;
                // If movement started and the unit is now moving (coroutine started), stop issuing more orders this update
                try { if (unit.isMoving) break; } catch { }
            }
        }
    }
    
    // Movement now uses BaseUnit.currentMovePoints and BaseUnit.DeductMovePoints
    
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
        // Check if predator has movement points
    if (predator.currentMovePoints <= 0) return false;

        var target = FindNearestCivilizationUnit(predator);
        if (target == null) return false;

        var ts = TileSystem.GetForPlanet(predator.planetIndex) ?? TileSystem.Instance;

        // If we're already adjacent to the target, perform an attack instead of moving
        float distToTarget = ts != null ? ts.GetTileDistance(predator.currentTileIndex, target.currentTileIndex) : float.MaxValue;
        if (distToTarget <= 1f)
        {
            var dmg = predator is CombatUnit cu ? cu.CurrentAttack : predator.BaseAttack;
            var ctx = new BaseUnit.AttackContext
            {
                attacker = predator,
                defender = target,
                weapon = null,
                damage = dmg,
                isRanged = false,
                isMelee = true
            };

            // Use an attack point for melee attacks (consistent with all units)
            if (!predator.TryConsumeAttackPoint())
                return false; // no AP available, cannot attack

            predator.PerformAttack(ctx);
            return true;
        }

        // Not adjacent: try to move closer to the target
        var neighborIndices = ts != null ? ts.GetNeighbors(predator.currentTileIndex) : System.Array.Empty<int>();
        var validDestinations = neighborIndices
            .Where(index =>
            {
                var neighbor = ts != null ? ts.GetTileData(index) : null;
                return neighbor != null && predator.CanMoveTo(index);
            })
            .ToList();

        if (validDestinations.Count == 0) return false;

        // Find the destination that gets us closest to the target
        int bestDestination = validDestinations[0];
        float minDistance = ts != null ? ts.GetTileDistance(bestDestination, target.currentTileIndex) : float.MaxValue;

        foreach (var destination in validDestinations)
        {
            float distance = ts != null ? ts.GetTileDistance(destination, target.currentTileIndex) : float.MaxValue;
            if (distance < minDistance)
            {
                minDistance = distance;
                bestDestination = destination;
            }
        }

        // Compute movement cost for the chosen destination and deduct then move
        int moveCostPred = 1;
        var tdPred = ts != null ? ts.GetTileData(bestDestination) : null;
        moveCostPred = tdPred != null ? BiomeHelper.GetMovementCost(tdPred, predator) : 1;
        if (predator.currentMovePoints < moveCostPred) return false;
        predator.MoveTo(bestDestination);
        return true;
    }
    
    /// <summary>
    /// Handle movement for prey animals - avoid civilization units unless recently attacked
    /// </summary>
    private bool HandlePreyMovement(CombatUnit prey)
    {
        // Check if prey has movement points
        if (prey.currentMovePoints <= 0) return false;
        
        bool wasAttacked = WasRecentlyAttacked(prey);

        var ts = TileSystem.GetForPlanet(prey.planetIndex) ?? TileSystem.Instance;

        if (wasAttacked)
        {
            // Prey was recently attacked, so it's aggressive and will hunt like a predator
            return HandlePredatorMovement(prey); // Use predator logic for aggressive behavior
        }

        // If not aggressive, check for nearby traps that attract animals
        if (ImprovementManager.Instance != null)
        {
            int? trapTile = ImprovementManager.Instance.GetNearestTrapForAnimals(prey.planetIndex, prey.currentTileIndex, 6);
            if (trapTile.HasValue)
            {
                ts = TileSystem.GetForPlanet(prey.planetIndex) ?? TileSystem.Instance;
                float dist = ts != null ? ts.GetTileDistance(prey.currentTileIndex, trapTile.Value) : float.MaxValue;
                if (dist <= 1f)
                {
                    int moveCostTrap = 1;
                    var tdTrap = ts != null ? ts.GetTileData(trapTile.Value) : null;
                    moveCostTrap = tdTrap != null ? BiomeHelper.GetMovementCost(tdTrap, prey) : 1;
                    if (prey.currentMovePoints < moveCostTrap) return false;
                    prey.MoveTo(trapTile.Value);
                    return true;
                }

                var neighborIndices = ts != null ? ts.GetNeighbors(prey.currentTileIndex) : System.Array.Empty<int>();
                var validDestinations = neighborIndices
                    .Where(index =>
                    {
                        var neighbor = ts != null ? ts.GetTileData(index) : null;
                        return neighbor != null && prey.CanMoveTo(index);
                    })
                    .ToList();

                if (validDestinations.Count > 0)
                {
                    int bestDestination = validDestinations[0];
                    float minDistance = ts != null ? ts.GetTileDistance(bestDestination, trapTile.Value) : float.MaxValue;
                    foreach (var destination in validDestinations)
                    {
                        float distance = ts != null ? ts.GetTileDistance(destination, trapTile.Value) : float.MaxValue;
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            bestDestination = destination;
                        }
                    }

                    int moveCostNeighbor = 1;
                    var tdNeighbor = ts != null ? ts.GetTileData(bestDestination) : null;
                    moveCostNeighbor = tdNeighbor != null ? BiomeHelper.GetMovementCost(tdNeighbor, prey) : 1;
                    if (prey.currentMovePoints < moveCostNeighbor) return false;
                    prey.MoveTo(bestDestination);
                    return true;
                }
            }
        }

        // Normal prey behavior - try to flee from civilization units
        int? fleeDestination = GetFleeDirection(prey);
        if (fleeDestination.HasValue)
        {
            int moveCostFlee = 1;
            var tdFlee = ts != null ? ts.GetTileData(fleeDestination.Value) : null;
            moveCostFlee = tdFlee != null ? BiomeHelper.GetMovementCost(tdFlee, prey) : 1;
            if (prey.currentMovePoints < moveCostFlee) return false;
            prey.MoveTo(fleeDestination.Value);
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Handle movement for neutral animals - random movement (original behavior)
    /// </summary>
    private bool HandleNeutralMovement(CombatUnit unit)
    {
        // Check if unit has movement points
        if (unit.currentMovePoints <= 0) return false;
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        
        // First, check for nearby traps that attract animals
        if (ImprovementManager.Instance != null)
        {
            int? trapTile = ImprovementManager.Instance.GetNearestTrapForAnimals(unit.planetIndex, unit.currentTileIndex, 6);
            if (trapTile.HasValue)
            {
                float dist = ts != null ? ts.GetTileDistance(unit.currentTileIndex, trapTile.Value) : float.MaxValue;
                if (dist <= 1f)
                {
                    int moveCostTrap = 1;
                    var tdTrap = ts != null ? ts.GetTileData(trapTile.Value) : null;
                    moveCostTrap = tdTrap != null ? BiomeHelper.GetMovementCost(tdTrap, unit) : 1;
                    if (unit.currentMovePoints < moveCostTrap) return false;
                    unit.MoveTo(trapTile.Value);
                    return true;
                }

                var trapNeighborIndices = ts != null ? ts.GetNeighbors(unit.currentTileIndex) : System.Array.Empty<int>();
                var trapValidDestinations = trapNeighborIndices
                    .Where(index =>
                    {
                        var neighbor = ts != null ? ts.GetTileData(index) : null;
                        return neighbor != null && unit.CanMoveTo(index);
                    })
                    .ToList();

                if (trapValidDestinations.Count > 0)
                {
                    int bestDestination = trapValidDestinations[0];
                    float minDistance = ts != null ? ts.GetTileDistance(bestDestination, trapTile.Value) : float.MaxValue;
                    foreach (var destination in trapValidDestinations)
                    {
                        float distance = ts != null ? ts.GetTileDistance(destination, trapTile.Value) : float.MaxValue;
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            bestDestination = destination;
                        }
                    }

                    int moveCostNeighbor = 1;
                    var tdNeighbor = ts != null ? ts.GetTileData(bestDestination) : null;
                    moveCostNeighbor = tdNeighbor != null ? BiomeHelper.GetMovementCost(tdNeighbor, unit) : 1;
                    if (unit.currentMovePoints < moveCostNeighbor) return false;
                    unit.MoveTo(bestDestination);
                    return true;
                }
            }
        }

        var neighborIndices = ts != null ? ts.GetNeighbors(unit.currentTileIndex) : System.Array.Empty<int>();
        var validDestinations = neighborIndices
            .Where(index =>
            {
                var neighbor = ts != null ? ts.GetTileData(index) : null;
                return neighbor != null && unit.CanMoveTo(index);
            })
            .ToList();

        if (validDestinations.Count > 0)
        {
            int targetTile = validDestinations[Random.Range(0, validDestinations.Count)];
            int moveCostRand = 1;
            var tdRand = ts != null ? ts.GetTileData(targetTile) : null;
            moveCostRand = tdRand != null ? BiomeHelper.GetMovementCost(tdRand, unit) : 1;
            if (unit.currentMovePoints < moveCostRand) return false;
            unit.MoveTo(targetTile);
            return true;
        }
        
        return false;
    }

    void TrySpawn(AnimalSpawnRule rule, int pIndex = -1)
    {
        var candidates = new List<int>();
        if (debugSpawning) Debug.Log($"[AnimalManager] TrySpawn for rule={rule?.unitData?.unitName ?? "<null>"} planet={pIndex}");
        // Determine target planet (default to current)
        if (pIndex < 0) pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var planet = GameManager.Instance?.GetPlanetGenerator(pIndex);
        int tileCount = planet != null && planet.Grid != null ? planet.Grid.TileCount : 0;
        if (debugSpawning) Debug.Log($"[AnimalManager] PlanetIndex={pIndex} tileCount={tileCount}");
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady())
        {
            Debug.LogWarning("[AnimalManager] TileSystem not ready; cannot spawn animals.");
            if (debugSpawning) Debug.LogWarning($"[AnimalManager] TileSystem null or not ready for planet {pIndex}");
            return;
        }

        for (int i = 0; i < tileCount; i++)
        {
            var tile = ts.GetTileData(i);
            if (tile == null)
            {
                if (debugSpawning) Debug.Log($"[AnimalManager] Skipping tile {i}: tile data null");
                continue;
            }


            // Preserve previous behavior: skip tiles that are not land unless the allowedBiomes includes water biomes
            bool isWaterTile = !tile.isLand;
            if (isWaterTile)
            {
                // Only allow water tiles if rule explicitly permits water biomes
                bool allowsWater = rule.allowedBiomes != null && (
                    System.Array.Exists(rule.allowedBiomes, b => b == Biome.Ocean || b == Biome.Seas || b == Biome.Lake || b == Biome.River)
                );
                if (!allowsWater)
                {
                    continue;
                }
            }

            // One unit per tile: do not spawn on tiles already occupied by a unit or city
            if (IsTileOccupiedByUnitOrCity(pIndex, i)) continue;

            candidates.Add(i);
        }

        if (debugSpawning) Debug.Log($"[AnimalManager] Found {candidates.Count} candidate tiles for rule {rule?.unitData?.unitName}");

        if (candidates.Count == 0)
        {
            if (debugSpawning) Debug.LogWarning($"[AnimalManager] No candidate tiles to spawn for {rule?.unitData?.unitName}");
            return;
        }

        int chosenIndex = candidates[Random.Range(0, candidates.Count)];
        SpawnAnimalAtTile(rule, pIndex, chosenIndex);
    }

    // Instantiate and register an animal at the specified tile index (shared helper)
    private void SpawnAnimalAtTile(AnimalSpawnRule rule, int pIndex, int chosenIndex)
    {
        var planet = GameManager.Instance?.GetPlanetGenerator(pIndex);
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        // Match ResourceManager placement: surface-aware position (includes tile elevation)
        Vector3 surfacePos = ts.GetTileSurfacePosition(chosenIndex, 0f);
        var tileData = ts != null ? ts.GetTileDataFromPlanet(chosenIndex, pIndex) : null;

        var animalPrefab = rule.unitData.GetPrefab();
        if (animalPrefab == null)
        {
            Debug.LogError($"[AnimalManager] Cannot spawn animal {rule.unitData.unitName}: prefab not found in Addressables. Make sure prefab is marked as Addressable with address matching unitName.");
            return;
        }

        var go = Instantiate(animalPrefab, surfacePos, Quaternion.identity);

        // Match ResourceManager hierarchy parenting rules: prefer dedicated roots when present.
        try
        {
            if (planet != null)
            {
                Transform parent = null;
                if (planet.resourcesRoot != null)
                    parent = planet.resourcesRoot.transform;
                else if (tileData != null && tileData.isLand && planet.surfaceRoot != null)
                    parent = planet.surfaceRoot.transform;
                else if (tileData != null && !tileData.isLand && planet.underwaterRoot != null)
                    parent = planet.underwaterRoot.transform;
                else
                    parent = planet.transform;

                if (parent != null)
                    go.transform.SetParent(parent, true);
            }
        }
        catch { }

        var unit = go.GetComponent<CombatUnit>();
        if (unit == null)
        {
            Debug.LogError($"[AnimalManager] Spawned prefab for {rule.unitData.unitName} is missing CombatUnit component.");
            Destroy(go);
            return;
        }

        unit.Initialize(rule.unitData, null);
        unit.planetIndex = pIndex;
        unit.currentTileIndex = chosenIndex;

        var chosenTile = ts.GetTileData(chosenIndex);
        var spawnLayer = UnitLayerRules.GetSpawnLayerForUnit(unit, chosenTile);
        if (!LayerConversion.TryToTileLayer(spawnLayer, out var occLayer)) occLayer = TileLayer.Surface;
        unit.currentLayer = occLayer;
        unit.PositionUnitOnSurface(null, chosenIndex);

        // Ensure the unit is registered in the global registry before claiming occupancy.
        try { unit.RegisterToRegistry(); } catch { }

        try { (TileOccupancyManager.GetForPlanet(pIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(chosenIndex, unit.gameObject, unit.currentLayer); } catch { }

        // Register with HexMapChunkManager so this animal is teleported when its column wraps
        try
        {
            var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planet);
            if (mgr != null)
            {
                mgr.RegisterObjectForWrapAtTile(chosenIndex, unit.gameObject);
            }
        }
        catch { }

        // Match ResourceManager: ensure prefab pivot/bounds sit on the terrain surface.
        // This prevents "spawned but invisible" cases where the model is buried below the terrain.
        try
        {
            float surfaceY = surfacePos.y;
            float lowest = float.MaxValue;
            var rends = unit.gameObject.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (r == null) continue;
                lowest = Mathf.Min(lowest, r.bounds.min.y);
            }
            var cols = unit.gameObject.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
            {
                if (c == null) continue;
                lowest = Mathf.Min(lowest, c.bounds.min.y);
            }
            if (lowest != float.MaxValue)
            {
                float delta = surfaceY - lowest;
                if (Mathf.Abs(delta) > 0.0001f)
                    unit.transform.position = unit.transform.position + new Vector3(0f, delta, 0f);
            }
        }
        catch { }

        activeAnimals.Add(unit);
        // Ensure registry contains the instance and emit diagnostics to help track disappearing animals
        try { unit.RegisterToRegistry(); } catch { }
        unit.gameObject.SetActive(true);
        if (debugSpawning)
        {
            // Capture component list early (helps identify self-destruct scripts on the prefab).
            try
            {
                int id = unit.gameObject.GetInstanceID();
                var mbs = unit.gameObject.GetComponentsInChildren<MonoBehaviour>(true);
                var sb = new System.Text.StringBuilder(512);
                sb.AppendLine($"Components on '{unit.gameObject.name}' (id={id}):");
                if (mbs != null)
                {
                    for (int i = 0; i < mbs.Length; i++)
                    {
                        var mb = mbs[i];
                        if (mb == null) { sb.AppendLine($"  [{i}] <missing script>"); continue; }
                        sb.AppendLine($"  [{i}] {mb.GetType().FullName} enabled={mb.enabled}");
                    }
                }
                else
                {
                    sb.AppendLine("  <none>");
                }
                _spawnComponentDumpById[id] = sb.ToString();
            }
            catch { }

            string parentName = unit.transform.parent != null ? unit.transform.parent.name : "<none>";
            bool parentActiveSelf = unit.transform.parent != null ? unit.transform.parent.gameObject.activeSelf : false;
            string sceneName = unit.gameObject.scene.IsValid() ? unit.gameObject.scene.name : "<invalid>";
            string layerName = LayerMask.LayerToName(unit.gameObject.layer);
            if (string.IsNullOrEmpty(layerName)) layerName = unit.gameObject.layer.ToString();
            var rends = unit.gameObject.GetComponentsInChildren<Renderer>(true);
            int rendererCount = rends != null ? rends.Length : 0;
            string rend0 = "<none>";
            if (rends != null && rends.Length > 0 && rends[0] != null)
            {
                var b = rends[0].bounds;
                rend0 = $"{rends[0].GetType().Name} enabled={rends[0].enabled} min={b.min.ToString("F2")} max={b.max.ToString("F2")}";
            }
            Debug.Log(
                $"[AnimalManager] Spawn diagnostics: id={unit.gameObject.GetInstanceID()} " +
                $"activeSelf={unit.gameObject.activeSelf} activeInHierarchy={unit.gameObject.activeInHierarchy} " +
                $"scene={sceneName} layer={layerName} " +
                $"hp={unit.currentHealth}/{unit.MaxHealth} " +
                $"pos={unit.transform.position.ToString("F2")} scale={unit.transform.lossyScale.ToString("F3")} " +
                $"parent={parentName} parentActiveSelf={parentActiveSelf} " +
                $"registryHas={(UnitRegistry.GetObject(unit.gameObject.GetInstanceID()) != null)} " +
                $"renderers={rendererCount} rend0={rend0}"
            );

            // Verify the object still exists after a couple frames (catches immediate cleanup/disable).
            StartCoroutine(VerifySpawnedAnimal(unit, pIndex, chosenIndex));
        }
        unit.OnDeath += () =>
        {
            activeAnimals.Remove(unit);
            recentlyAttackedAnimals.Remove(unit);
            UnitRegistry.Unregister(unit.gameObject);
        };

        if (debugSpawning) Debug.Log($"[AnimalManager] Spawned {rule?.unitData?.unitName ?? "<unknown>"} at tile {chosenIndex} on planet {pIndex}");
    }

    private IEnumerator VerifySpawnedAnimal(CombatUnit unit, int pIndex, int tileIndex)
    {
        // 1 frame later
        yield return null;
        if (unit == null || unit.gameObject == null)
        {
            Debug.LogWarning($"[AnimalManager] VerifySpawn: animal destroyed after 1 frame (planet={pIndex} tile={tileIndex})");
            yield break;
        }
        Debug.Log(
            $"[AnimalManager] VerifySpawn(1f): id={unit.gameObject.GetInstanceID()} " +
            $"activeSelf={unit.gameObject.activeSelf} activeInHierarchy={unit.gameObject.activeInHierarchy} " +
            $"pos={unit.transform.position.ToString("F2")} parent={(unit.transform.parent != null ? unit.transform.parent.name : "<none>")}"
        );

        // 10 more frames later
        for (int i = 0; i < 10; i++) yield return null;
        if (unit == null || unit.gameObject == null)
        {
            Debug.LogWarning($"[AnimalManager] VerifySpawn: animal destroyed after ~11 frames (planet={pIndex} tile={tileIndex})");
            yield break;
        }
        Debug.Log(
            $"[AnimalManager] VerifySpawn(11f): id={unit.gameObject.GetInstanceID()} " +
            $"activeSelf={unit.gameObject.activeSelf} activeInHierarchy={unit.gameObject.activeInHierarchy} " +
            $"pos={unit.transform.position.ToString("F2")} parent={(unit.transform.parent != null ? unit.transform.parent.name : "<none>")}"
        );
    }

    internal bool TryGetSpawnComponentDump(int instanceId, out string dump) => _spawnComponentDumpById.TryGetValue(instanceId, out dump);

    internal void ClearSpawnComponentDump(int instanceId)
    {
        if (_spawnComponentDumpById.ContainsKey(instanceId))
            _spawnComponentDumpById.Remove(instanceId);
    }

    /// <summary>
    /// Returns true if the tile has a unit or city on the surface layer (one unit per tile — used to avoid spawning on occupied tiles).
    /// </summary>
    private static bool IsTileOccupiedByUnitOrCity(int planetIndex, int tileIndex)
    {
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return false;
        var obj = occ.GetOccupantObject(tileIndex, TileLayer.Surface);
        if (obj == null) return false;
        return obj.GetComponent<BaseUnit>() != null || obj.GetComponent<City>() != null;
    }
    
    /// <summary>
    /// Remove an animal from the manager (called when hunted or killed)
    /// </summary>
    public void RemoveAnimal(CombatUnit animal)
    {
        if (animal == null) return;
        
        activeAnimals.Remove(animal);
        recentlyAttackedAnimals.Remove(animal);
        UnitRegistry.Unregister(animal.gameObject);
}
    
    /// <summary>
    /// Get all active animals (for UI or other systems to query)
    /// </summary>
    public List<CombatUnit> GetActiveAnimals()
    {
        return new List<CombatUnit>(activeAnimals);
    }
    
    /// <summary>
    /// Get animals at a specific tile
    /// </summary>
    public List<CombatUnit> GetAnimalsAtTile(int tileIndex, int planetIndex = -1)
    {
        var result = new List<CombatUnit>();
        if (planetIndex < 0) planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        foreach (var animal in activeAnimals)
        {
            if (animal != null && animal.planetIndex == planetIndex && animal.currentTileIndex == tileIndex)
            {
                result.Add(animal);
            }
        }
        return result;
    }
    
    /// <summary>
    /// Get animals adjacent to a specific tile (for hunting range checks)
    /// </summary>
    public List<CombatUnit> GetAnimalsNearTile(int tileIndex, int planetIndex = -1)
    {
        var result = new List<CombatUnit>();
        if (planetIndex < 0) planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        
        // Animals on the tile
        result.AddRange(GetAnimalsAtTile(tileIndex, planetIndex));
        
        // Animals on adjacent tiles
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null)
        {
            var neighbors = ts.GetNeighbors(tileIndex);
            foreach (int neighbor in neighbors)
            {
                result.AddRange(GetAnimalsAtTile(neighbor, planetIndex));
            }
        }
        
        return result;
    }
}