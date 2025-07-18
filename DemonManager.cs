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

        planet = FindAnyObjectByType<PlanetGenerator>();
        if (planet != null)
            grid = planet.Grid;
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
        if (grid == null)
        {
            Debug.LogError("DemonManager cannot spawn demons, SphericalHexGrid not found!");
            return;
        }
        // Find valid spawn locations
        List<int> validTiles = new List<int>();
        for (int i = 0; i < grid.TileCount; i++)
        {
            var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(i);
            if (tileData == null || isMoonTile) continue;

            // Check if tile is a valid demon spawn biome
            if (!spawnableBiomes.Contains(tileData.biome)) continue;


            // Check if tile is unoccupied
            if (tileData.occupantId != 0) continue;

            validTiles.Add(i);
        }

        if (validTiles.Count == 0) return;

        // Pick random spawn location
        int spawnTileIndex = validTiles[Random.Range(0, validTiles.Count)];
        Vector3 spawnPos = TileDataHelper.Instance.GetTileSurfacePosition(spawnTileIndex, 0.5f);

        // Pick random demon type
        DemonUnitData demonType = demonUnits[Random.Range(0, demonUnits.Length)];

        // Spawn the demon
        var demonGO = Instantiate(demonType.prefab, spawnPos, Quaternion.identity);
        var demonUnit = demonGO.GetComponent<CombatUnit>();
        // Demons have no owner civilization, so we pass null.
        // The Initialize method on CombatUnit should handle this gracefully.
        demonUnit.Initialize(demonType, null); 
        activeDemonUnits.Add(demonUnit);

        Debug.Log($"Spawned {demonType.unitName} at tile {spawnTileIndex}");
    }

    public void RemoveDemon(CombatUnit demonUnit)
    {
        if (activeDemonUnits.Contains(demonUnit))
        {
            activeDemonUnits.Remove(demonUnit);
        }
    }
} 