using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages individual demon units on the campaign map (Civ5-style).
/// Each demon occupies its own tile and moves independently.
/// </summary>
public class DemonManager : MonoBehaviour
{
    public static DemonManager Instance { get; private set; }

    [Header("Demon Unit Settings")]
    [SerializeField] private DemonUnitData[] demonUnits;
    [SerializeField] private float spawnChancePerTurn = 0.15f;
    [SerializeField] private int maxDemons = 10;
    [SerializeField] private int minTurnsBetweenSpawns = 3;

    [Header("Movement")]
    [Tooltip("Movement points per turn for each demon")]
    [SerializeField] private int demonMovePoints = 2;

    [Header("Spawn Requirements")]
    [Tooltip("Biomes where demons can spawn")]
    [SerializeField] private Biome[] spawnableBiomes = { Biome.Hellscape };

    [Header("AI Behavior")]
    [Tooltip("Chance to move towards nearest civilization each turn")]
    [Range(0f, 1f)]
    [SerializeField] private float aggressionChance = 0.7f;
    [Tooltip("Maximum tiles to search for targets")]
    [SerializeField] private int targetSearchRange = 10;

    // Track individual demon units
    private List<CombatUnit> activeDemonUnits = new List<CombatUnit>();
    private Dictionary<CombatUnit, int> remainingMovePoints = new Dictionary<CombatUnit, int>();
    private int turnsSinceLastSpawn;

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
    /// Process demon turn - spawn new demons and move existing ones
    /// </summary>
    public void ProcessDemonTurn()
    {
        CleanupDeadDemons();

        // Reset movement points for all demons
        remainingMovePoints.Clear();
        foreach (var demon in activeDemonUnits)
        {
            if (demon != null)
                remainingMovePoints[demon] = demonMovePoints;
        }

        MoveAllDemons();

        // Try to spawn a new demon
        turnsSinceLastSpawn++;
        if (turnsSinceLastSpawn >= minTurnsBetweenSpawns &&
            activeDemonUnits.Count < maxDemons &&
            Random.value < spawnChancePerTurn)
        {
            SpawnDemon();
            turnsSinceLastSpawn = 0;
        }
    }

    /// <summary>
    /// Remove dead or destroyed demon units
    /// </summary>
    private void CleanupDeadDemons()
    {
        for (int i = activeDemonUnits.Count - 1; i >= 0; i--)
        {
            var demon = activeDemonUnits[i];
            if (demon == null || demon.currentHealth <= 0)
            {
                if (demon != null)
                {
                    // Clear occupancy before destroying
                    var occ = TileOccupancyManager.GetForPlanet(demon.planetIndex) ?? TileOccupancyManager.Instance;
                    if (occ != null) occ.ClearOccupant(demon.currentTileIndex, TileLayer.Surface);
                    Destroy(demon.gameObject);
                }
                activeDemonUnits.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Move all demon units using AI behavior
    /// </summary>
    private void MoveAllDemons()
    {
        foreach (var demon in activeDemonUnits)
        {
            if (demon == null) continue;

            while (remainingMovePoints.TryGetValue(demon, out int mp) && mp > 0)
            {
                bool moved = false;

                if (Random.value < aggressionChance)
                {
                    moved = MoveTowardsTarget(demon);
                }

                if (!moved)
                {
                    moved = MoveRandomly(demon);
                }

                if (!moved)
                    break;
            }
        }
    }

    /// <summary>
    /// Move demon towards nearest civilization target
    /// </summary>
    private bool MoveTowardsTarget(CombatUnit demon)
    {
        var ts = TileSystem.GetForPlanet(demon.planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return false;

        int targetTile = FindNearestTarget(demon);
        if (targetTile < 0) return false;

        var neighbors = ts.GetNeighbors(demon.currentTileIndex);
        if (neighbors == null || neighbors.Length == 0) return false;

        int bestNeighbor = -1;
        float bestDistance = float.MaxValue;

        foreach (int neighbor in neighbors)
        {
            var tileData = ts.GetTileData(neighbor);
            if (tileData == null || !CanDemonEnterTile(tileData, neighbor, demon.planetIndex)) continue;

            float dist = ts.GetTileDistance(neighbor, targetTile);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestNeighbor = neighbor;
            }
        }

        if (bestNeighbor >= 0)
        {
            MoveDemon(demon, bestNeighbor);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Find nearest target (city, combat unit, or worker)
    /// </summary>
    private int FindNearestTarget(CombatUnit demon)
    {
        var ts = TileSystem.GetForPlanet(demon.planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return -1;

        float nearestDist = float.MaxValue;
        int nearestTile = -1;

        // Check for combat units on the map (skip other demons)
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (unit == null || unit.owner == null) continue;
            if (!unit.gameObject.activeSelf) continue;
            if (unit.currentTileIndex == demon.currentTileIndex) continue;

            float dist = ts.GetTileDistance(demon.currentTileIndex, unit.currentTileIndex);
            if (dist < nearestDist && dist <= targetSearchRange)
            {
                nearestDist = dist;
                nearestTile = unit.currentTileIndex;
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

                    float dist = ts.GetTileDistance(demon.currentTileIndex, city.centerTileIndex);
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

            float dist = ts.GetTileDistance(demon.currentTileIndex, worker.currentTileIndex);
            if (dist < nearestDist && dist <= targetSearchRange)
            {
                nearestDist = dist;
                nearestTile = worker.currentTileIndex;
            }
        }

        return nearestTile;
    }

    /// <summary>
    /// Move demon randomly
    /// </summary>
    private bool MoveRandomly(CombatUnit demon)
    {
        var ts = TileSystem.GetForPlanet(demon.planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return false;

        var neighbors = ts.GetNeighbors(demon.currentTileIndex);
        if (neighbors == null || neighbors.Length == 0) return false;

        var validNeighbors = new List<int>();
        foreach (int neighbor in neighbors)
        {
            var tileData = ts.GetTileData(neighbor);
            if (tileData != null && CanDemonEnterTile(tileData, neighbor, demon.planetIndex))
            {
                validNeighbors.Add(neighbor);
            }
        }

        if (validNeighbors.Count > 0)
        {
            int targetTile = validNeighbors[Random.Range(0, validNeighbors.Count)];
            MoveDemon(demon, targetTile);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Check if a demon can enter a tile (passability + occupancy)
    /// </summary>
    private bool CanDemonEnterTile(HexTileData tileData, int tileIndex, int planetIndex)
    {
        if (tileData == null) return false;
        if (!tileData.isPassable) return false;

        // Must be land or hellscape
        if (!tileData.isLand && tileData.biome != Biome.Hellscape)
            return false;

        // Civ5-style: one unit per tile — check occupancy
        bool occupied = TileOccupancyManager.GetOccupantObjectForTileWithFallback(tileIndex, TileLayer.Surface, planetIndex) != null;
        if (occupied) return false;

        return true;
    }

    /// <summary>
    /// Move demon to a new tile
    /// </summary>
    private void MoveDemon(CombatUnit demon, int targetTile)
    {
        var ts = TileSystem.GetForPlanet(demon.planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return;

        // Deduct movement point
        if (remainingMovePoints.ContainsKey(demon))
            remainingMovePoints[demon]--;

        // Clear old occupancy
        var occ = TileOccupancyManager.GetForPlanet(demon.planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
            occ.ClearOccupant(demon.currentTileIndex, TileLayer.Surface);

        // Update position
        demon.currentTileIndex = targetTile;
        Vector3 worldPos = ts.GetTileSurfacePosition(targetTile);
        demon.transform.position = worldPos;

        // Set new occupancy
        if (occ != null)
            occ.SetOccupant(targetTile, demon.gameObject, TileLayer.Surface);

        // Check for encounters
        CheckForEncounters(demon);
    }

    /// <summary>
    /// Check if demon encounters enemies at current tile
    /// </summary>
    private void CheckForEncounters(CombatUnit demon)
    {
        // Check for combat units at this tile
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (unit != null && unit.owner != null && unit.gameObject.activeSelf
                && unit.currentTileIndex == demon.currentTileIndex && unit != demon)
            {
                // TODO: Implement Civ5-style tile combat with demon units
                return;
            }
        }

        // Check for workers at this tile
        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (worker != null && worker.currentTileIndex == demon.currentTileIndex)
            {
                AttackWorker(demon, worker);
            }
        }
    }

    /// <summary>
    /// Auto-resolve attack on a worker (instant kill)
    /// </summary>
    private void AttackWorker(CombatUnit demon, WorkerUnit worker)
    {
        if (worker == null) return;

        worker.ApplyDamage(worker.MaxHealth);

        if (UIManager.Instance != null && worker.owner != null && worker.owner.isPlayerControlled)
        {
            UIManager.Instance.ShowNotification($"A demon has killed your {worker.UnitName}!");
        }
    }

    /// <summary>
    /// Spawn a new individual demon unit
    /// </summary>
    private void SpawnDemon()
    {
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
            var tsPlanet = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            for (int i = 0; i < planetGrid.TileCount; i++)
            {
                var tileData = tsPlanet != null ? tsPlanet.GetTileDataFromPlanet(i, planetIndex) : null;
                if (tileData == null) continue;

                if (!spawnableBiomes.Contains(tileData.biome)) continue;

                bool occupied = TileOccupancyManager.GetOccupantObjectForTileWithFallback(i, TileLayer.Surface, planetIndex) != null;
                if (occupied) continue;

                validTiles.Add((i, planetIndex));
            }
        }

        if (validTiles.Count == 0) return;

        var (spawnTileIndex, spawnPlanetIndex) = validTiles[Random.Range(0, validTiles.Count)];

        var ts = TileSystem.GetForPlanet(spawnPlanetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady())
        {
            Debug.LogWarning("[DemonManager] TileSystem not ready for selected spawn planet; cannot spawn demon");
            return;
        }

        Vector3 spawnPos = ts.GetTileSurfacePosition(spawnTileIndex);

        // Pick random demon type
        DemonUnitData demonType = demonUnits[Random.Range(0, demonUnits.Length)];

        var demonPrefab = demonType.GetPrefab();
        if (demonPrefab == null)
        {
            Debug.LogError($"[DemonManager] Cannot spawn demon {demonType.unitName}: prefab not found");
            return;
        }

        var demonGO = Instantiate(demonPrefab, spawnPos, Quaternion.identity);
        // Register demon with wrap registry so it teleports with columns
        try
        {
            var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == GameManager.Instance?.GetPlanetGenerator(spawnPlanetIndex));
            if (mgr != null) mgr.RegisterObjectForWrapAtTile(spawnTileIndex, demonGO);
        }
        catch { }
        var demonUnit = demonGO.GetComponent<CombatUnit>();
        if (demonUnit == null)
        {
            Debug.LogError($"[DemonManager] Spawned prefab for {demonType.unitName} missing CombatUnit");
            Destroy(demonGO);
            return;
        }

        // Initialize with no owner (demons are ownerless)
        demonUnit.Initialize(demonType, null);
        demonUnit.currentTileIndex = spawnTileIndex;
        demonUnit.planetIndex = spawnPlanetIndex;

        // Register occupancy
        var occ = TileOccupancyManager.GetForPlanet(spawnPlanetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
            occ.SetOccupant(spawnTileIndex, demonGO, TileLayer.Surface);

        // Track in our list
        activeDemonUnits.Add(demonUnit);

        // Track death
        demonUnit.OnDeath += () => OnDemonUnitDeath(demonUnit);
    }

    /// <summary>
    /// Handle demon unit death
    /// </summary>
    private void OnDemonUnitDeath(CombatUnit unit)
    {
        if (unit != null)
        {
            var occ = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
            if (occ != null) occ.ClearOccupant(unit.currentTileIndex, TileLayer.Surface);
        }
        activeDemonUnits.Remove(unit);
    }

    /// <summary>
    /// Remove a specific demon unit
    /// </summary>
    public void RemoveDemon(CombatUnit demonUnit)
    {
        if (demonUnit == null) return;

        var occ = TileOccupancyManager.GetForPlanet(demonUnit.planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null) occ.ClearOccupant(demonUnit.currentTileIndex, TileLayer.Surface);

        activeDemonUnits.Remove(demonUnit);
        Destroy(demonUnit.gameObject);
    }

    /// <summary>
    /// Get all active demon units
    /// </summary>
    public List<CombatUnit> GetActiveDemonUnits()
    {
        return new List<CombatUnit>(activeDemonUnits);
    }

    /// <summary>
    /// Get demon unit at a specific tile (one unit per tile)
    /// </summary>
    public CombatUnit GetDemonAtTile(int tileIndex, int planetIndex = -1)
    {
        if (planetIndex < 0) planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        foreach (var demon in activeDemonUnits)
        {
            if (demon != null && demon.planetIndex == planetIndex && demon.currentTileIndex == tileIndex)
                return demon;
        }
        return null;
    }
}