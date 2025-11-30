using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DemonManager : MonoBehaviour
{
    public static DemonManager Instance { get; private set; }

    [Header("Demon Unit Settings")]
    [SerializeField] private DemonUnitData[] demonUnits;
    [SerializeField] private float spawnChancePerTurn = 0.15f;
    [SerializeField] private int maxDemonsPerWorld = 10;
    [SerializeField] private int minTurnsBetweenSpawns = 3;

    [Header("Spawn Requirements")]
    [Tooltip("Biomes where demons can spawn")]
    [SerializeField] private Biome[] spawnableBiomes = { Biome.Hellscape, Biome.Brimstone };


    private List<CombatUnit> activeDemonUnits = new List<CombatUnit>();
    private int turnsSinceLastSpawn;

    private SphericalHexGrid grid;
    private PlanetGenerator planet;

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

        // DemonManager works across all planets - no need to store single planet reference
        // Individual spawn operations will iterate through all planets
    }

    void Start()
    {
        turnsSinceLastSpawn = 0;
    }

    public void ProcessDemonTurn()
    {
        if (turnsSinceLastSpawn < minTurnsBetweenSpawns)
        {
            turnsSinceLastSpawn++;
            return;
        }

        if (activeDemonUnits.Count >= maxDemonsPerWorld) return;

        // Try to spawn new demons
        if (Random.value < spawnChancePerTurn)
        {
            SpawnDemon();
            turnsSinceLastSpawn = 0;
        }
    }

    private void SpawnDemon()
    {
        // Safety check for tile system
        if (TileSystem.Instance == null)
        {
            Debug.LogWarning("[DemonManager] TileSystem not ready, cannot spawn demons");
            return;
        }
        
        if (GameManager.Instance == null || !GameManager.Instance.enableMultiPlanetSystem)
        {
            Debug.LogError("[DemonManager] GameManager or multi-planet system not available");
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

        if (validTiles.Count == 0) return;

        // Pick random spawn location from any planet
        var (spawnTileIndex, spawnPlanetIndex) = validTiles[Random.Range(0, validTiles.Count)];
        
        // Get the planet generator for the selected planet
        var spawnPlanetGen = GameManager.Instance.GetPlanetGenerator(spawnPlanetIndex);
        if (spawnPlanetGen == null)
        {
            Debug.LogError($"[DemonManager] Could not find planet generator for planet {spawnPlanetIndex}");
            return;
        }
        
        // Get spawn position on the selected planet
    // Get a surface position via TileSystem planet-aware API
    Vector3 spawnPos = TileSystem.Instance.GetTileSurfacePosition(spawnTileIndex, 0.5f, spawnPlanetIndex);

        // Pick random demon type
        DemonUnitData demonType = demonUnits[Random.Range(0, demonUnits.Length)];

        // Spawn the demon
        var demonPrefab = demonType.GetPrefab();
        if (demonPrefab == null)
        {
            Debug.LogError($"[DemonManager] Cannot spawn demon {demonType.unitName}: prefab not found in Addressables. Make sure prefab is marked as Addressable with address matching unitName.");
            return;
        }
        
        var demonGO = Instantiate(demonPrefab, spawnPos, Quaternion.identity);
        var demonUnit = demonGO.GetComponent<CombatUnit>();
        if (demonUnit == null)
        {
            Debug.LogError($"[DemonManager] Spawned prefab for {demonType.unitName} is missing CombatUnit component.");
            Destroy(demonGO);
            return;
        }
        // Demons have no owner civilization, so we pass null.
        // The Initialize method on CombatUnit should handle this gracefully.
        demonUnit.Initialize(demonType, null); 
        activeDemonUnits.Add(demonUnit);

    Debug.Log($"Spawned {demonType.unitName} at tile {spawnTileIndex} on planet {spawnPlanetIndex}");
    }

    public void RemoveDemon(CombatUnit demonUnit)
    {
        if (activeDemonUnits.Contains(demonUnit))
        {
            activeDemonUnits.Remove(demonUnit);
        }
    }
} 