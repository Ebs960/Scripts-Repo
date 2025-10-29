using UnityEngine;

/// <summary>
/// Generates battle maps for real-time combat
/// </summary>
public class BattleMapGenerator : MonoBehaviour
{
    [Header("Map Generation")]
    [Tooltip("Prefabs for different terrain types")]
    public GameObject[] terrainPrefabs;
    [Tooltip("Size of each terrain tile")]
    public float tileSize = 2f;
    [Tooltip("Height variation for terrain")]
    public float heightVariation = 1f;
    [Tooltip("Noise scale for terrain generation")]
    public float noiseScale = 0.1f;

    [Header("Obstacles")]
    [Tooltip("Prefabs for obstacles (trees, rocks, etc.)")]
    public GameObject[] obstaclePrefabs;
    [Tooltip("Density of obstacles (0-1)")]
    [Range(0f, 1f)]
    public float obstacleDensity = 0.1f;

    [Header("Spawn Points")]
    [Tooltip("Prefab for spawn point markers")]
    public GameObject spawnPointPrefab;

    private GameObject[,] terrainTiles;
    private int mapWidth;
    private int mapHeight;

    /// <summary>
    /// Generate a battle map
    /// </summary>
    public void GenerateBattleMap(float mapSize, int attackerUnits, int defenderUnits)
    {
        // Calculate map dimensions
        mapWidth = Mathf.CeilToInt(mapSize / tileSize);
        mapHeight = Mathf.CeilToInt(mapSize / tileSize);

        Debug.Log($"[BattleMapGenerator] Generating battle map: {mapWidth}x{mapHeight}");

        // Clear existing map
        ClearExistingMap();

        // Generate terrain
        GenerateTerrain();

        // Add obstacles
        AddObstacles();

        // Create spawn points
        CreateSpawnPoints(attackerUnits, defenderUnits);

        // Generate navigation mesh
        GenerateNavigationMesh();

        Debug.Log("[BattleMapGenerator] Battle map generated successfully!");
    }

    private void ClearExistingMap()
    {
        // Destroy existing terrain tiles
        if (terrainTiles != null)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                for (int y = 0; y < mapHeight; y++)
                {
                    if (terrainTiles[x, y] != null)
                    {
                        DestroyImmediate(terrainTiles[x, y]);
                    }
                }
            }
        }

        // Destroy existing obstacles
        GameObject[] obstacles = GameObject.FindGameObjectsWithTag("BattleObstacle");
        foreach (var obstacle in obstacles)
        {
            DestroyImmediate(obstacle);
        }

        // Destroy existing spawn points
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");
        foreach (var spawnPoint in spawnPoints)
        {
            DestroyImmediate(spawnPoint);
        }
    }

    private void GenerateTerrain()
    {
        terrainTiles = new GameObject[mapWidth, mapHeight];

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                // Calculate world position
                Vector3 position = new Vector3(
                    x * tileSize - (mapWidth * tileSize) / 2f,
                    0,
                    y * tileSize - (mapHeight * tileSize) / 2f
                );

                // Add height variation using noise
                float height = Mathf.PerlinNoise(x * noiseScale, y * noiseScale) * heightVariation;
                position.y = height;

                // Choose terrain prefab
                GameObject terrainPrefab = GetTerrainPrefab(x, y);
                if (terrainPrefab != null)
                {
                    GameObject tile = Instantiate(terrainPrefab, position, Quaternion.identity);
                    tile.transform.parent = transform;
                    tile.name = $"Terrain_{x}_{y}";
                    terrainTiles[x, y] = tile;
                }
            }
        }
    }

    private GameObject GetTerrainPrefab(int x, int y)
    {
        if (terrainPrefabs == null || terrainPrefabs.Length == 0)
        {
            // Create a simple plane if no prefabs available
            return CreateSimplePlane();
        }

        // Use noise to determine terrain type
        float noise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f);
        int terrainIndex = Mathf.FloorToInt(noise * terrainPrefabs.Length);
        terrainIndex = Mathf.Clamp(terrainIndex, 0, terrainPrefabs.Length - 1);

        return terrainPrefabs[terrainIndex];
    }

    private GameObject CreateSimplePlane()
    {
        // Create a simple plane as fallback
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.transform.localScale = new Vector3(tileSize / 10f, 1, tileSize / 10f);
        plane.GetComponent<Renderer>().material.color = new Color(0.2f, 0.6f, 0.2f); // Green grass
        return plane;
    }

    private void AddObstacles()
    {
        if (obstaclePrefabs == null || obstaclePrefabs.Length == 0) return;

        int obstacleCount = Mathf.RoundToInt(mapWidth * mapHeight * obstacleDensity);

        for (int i = 0; i < obstacleCount; i++)
        {
            // Random position
            Vector3 position = new Vector3(
                Random.Range(-mapWidth * tileSize / 2f, mapWidth * tileSize / 2f),
                0,
                Random.Range(-mapHeight * tileSize / 2f, mapHeight * tileSize / 2f)
            );

            // Check if position is clear (simple check)
            if (IsPositionClear(position))
            {
                // Choose random obstacle
                GameObject obstaclePrefab = obstaclePrefabs[Random.Range(0, obstaclePrefabs.Length)];
                GameObject obstacle = Instantiate(obstaclePrefab, position, Quaternion.identity);
                obstacle.transform.parent = transform;
                obstacle.tag = "BattleObstacle";
                obstacle.name = $"Obstacle_{i}";
            }
        }
    }

    private bool IsPositionClear(Vector3 position)
    {
        // Simple check - can be improved with proper collision detection
        Collider[] colliders = Physics.OverlapSphere(position, 1f);
        return colliders.Length == 0;
    }

    private void CreateSpawnPoints(int attackerUnits, int defenderUnits)
    {
        if (spawnPointPrefab == null) return;

        // Attacker spawn point (left side)
        Vector3 attackerSpawn = new Vector3(-mapWidth * tileSize / 4f, 0, 0);
        GameObject attackerSpawnPoint = Instantiate(spawnPointPrefab, attackerSpawn, Quaternion.identity);
        attackerSpawnPoint.name = "AttackerSpawnPoint";
        attackerSpawnPoint.tag = "SpawnPoint";

        // Defender spawn point (right side)
        Vector3 defenderSpawn = new Vector3(mapWidth * tileSize / 4f, 0, 0);
        GameObject defenderSpawnPoint = Instantiate(spawnPointPrefab, defenderSpawn, Quaternion.identity);
        defenderSpawnPoint.name = "DefenderSpawnPoint";
        defenderSpawnPoint.tag = "SpawnPoint";
    }

    private void GenerateNavigationMesh()
    {
        // This is a placeholder - in a real implementation, you'd use Unity's NavMesh system
        // For now, we'll just ensure the terrain is walkable
        Debug.Log("[BattleMapGenerator] Navigation mesh generation placeholder - using default walkable terrain");
    }

    /// <summary>
    /// Get a random position on the map
    /// </summary>
    public Vector3 GetRandomPosition()
    {
        return new Vector3(
            Random.Range(-mapWidth * tileSize / 2f, mapWidth * tileSize / 2f),
            0,
            Random.Range(-mapHeight * tileSize / 2f, mapHeight * tileSize / 2f)
        );
    }

    /// <summary>
    /// Check if a position is within map bounds
    /// </summary>
    public bool IsPositionInBounds(Vector3 position)
    {
        float halfWidth = mapWidth * tileSize / 2f;
        float halfHeight = mapHeight * tileSize / 2f;

        return position.x >= -halfWidth && position.x <= halfWidth &&
               position.z >= -halfHeight && position.z <= halfHeight;
    }

    /// <summary>
    /// Get the terrain type at a specific position
    /// </summary>
    public TerrainType GetTerrainTypeAt(Vector3 position)
    {
        // Convert world position to grid coordinates
        int x = Mathf.FloorToInt((position.x + mapWidth * tileSize / 2f) / tileSize);
        int y = Mathf.FloorToInt((position.z + mapHeight * tileSize / 2f) / tileSize);

        // Check bounds
        if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight)
            return TerrainType.Impassable;

        // Return terrain type based on the tile
        if (terrainTiles[x, y] != null)
        {
            // This is a simplified version - in reality you'd check the actual terrain type
            return TerrainType.Plains;
        }

        return TerrainType.Impassable;
    }
}

/// <summary>
/// Types of terrain for battle maps
/// </summary>
public enum TerrainType
{
    Plains,         // Open field, no bonuses
    Hills,          // Defense bonus, movement penalty
    Forest,         // Cover bonus, movement penalty
    Water,          // Only certain units can cross
    Fortified,      // Major defense bonus
    Swamp,          // Movement penalty, health damage
    Impassable      // Cannot be traversed
}
