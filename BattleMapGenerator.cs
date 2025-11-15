using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Generates biome-based battle maps with elevation and tactical terrain
/// </summary>
public class BattleMapGenerator : MonoBehaviour
{
    [Header("Map Generation")]
    [Tooltip("Resolution of terrain sampling (higher = more detailed)")]
    public float terrainResolution = 1f;
    [Tooltip("Height variation for terrain")]
    public float heightVariation = 3f;
    [Tooltip("Noise scale for terrain generation")]
    public float noiseScale = 0.1f;
    [Tooltip("Elevation noise scale")]
    public float elevationNoiseScale = 0.05f;

    [Header("Biome Settings")]
    [Tooltip("Primary biome for this battle map")]
    public Biome primaryBiome = Biome.Plains;
    [Tooltip("Secondary biomes that can appear")]
    public Biome[] secondaryBiomes = { Biome.Mountain, Biome.Forest };
    [Tooltip("Chance for secondary biomes to appear")]
    [Range(0f, 1f)]
    public float secondaryBiomeChance = 0.3f;
    [Tooltip("Biome settings for textures and materials")]
    public BiomeSettings[] biomeSettings = new BiomeSettings[0];

    [Header("Elevation Features")]
    [Tooltip("Threshold for hill elevation")]
    public float hillThreshold = 0.4f;
    [Tooltip("Threshold for mountain elevation")]
    public float mountainThreshold = 0.7f;
    [Tooltip("Maximum mountain height")]
    public float maxMountainHeight = 8f;

    [Header("Obstacles & Decorations")]
    [Tooltip("Density of obstacles (trees, rocks, etc.)")]
    [Range(0f, 1f)]
    public float obstacleDensity = 0.15f;
    [Tooltip("Density of cover objects")]
    [Range(0f, 1f)]
    public float coverDensity = 0.1f;

    [Header("Spawn Points")]
    [Tooltip("Distance between attacker and defender spawn points")]
    public float spawnDistance = 30f;
    [Tooltip("Prefab for spawn point markers")]
    public GameObject spawnPointPrefab;

    [Header("Victory Conditions")]
    [Tooltip("Morale threshold for routing (0-100)")]
    [Range(0, 100)]
    public int routingMoraleThreshold = 0;
    [Tooltip("Time units have to be routing before they're considered defeated")]
    public float routingTimeToDefeat = 10f;

    // Internal data
    private float mapSize; // Set via GenerateBattleMap() method parameter
    private List<GameObject> terrainObjects = new List<GameObject>();
    private Dictionary<Vector2, TerrainData> terrainData = new Dictionary<Vector2, TerrainData>();
    private Vector3 mapCenter;
    private List<Vector3> attackerSpawnPoints = new List<Vector3>();
    private List<Vector3> defenderSpawnPoints = new List<Vector3>();
    private List<GameObject> spawnedObjects = new List<GameObject>();
    private Dictionary<Biome, BiomeSettings> biomeSettingsLookup = new Dictionary<Biome, BiomeSettings>();
    
    // Cached materials to avoid creating new ones each time (memory optimization)
    private Material cachedTerrainMaterial;

    // Terrain data structure
    [System.Serializable]
    public struct TerrainData
    {
        public Biome biome;
        public float elevation;
        public TerrainType terrainType;
        public bool hasCover;
        public bool isImpassable;
        public int defenseBonus;
        public int movementCost;
    }

    /// <summary>
    /// Generate a biome-based battle map with elevation and tactical features
    /// </summary>
    public void GenerateBattleMap(float mapSize, int attackerUnits, int defenderUnits)
    {
        this.mapSize = mapSize;
        mapCenter = Vector3.zero;
        
        // Initialize biome settings lookup
        InitializeBiomeSettings();
        
        ClearExistingMap();
        GenerateTerrainMesh();
        AddBiomeDecorations();
        CreateSpawnPoints(attackerUnits, defenderUnits);
        
        Debug.Log($"[BattleMapGenerator] Generated {primaryBiome} battle map ({mapSize}x{mapSize}) with elevation and tactical features");
    }

    /// <summary>
    /// Clear any existing map objects
    /// </summary>
    private void ClearExistingMap()
    {
        // Destroy all child objects
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(transform.GetChild(i).gameObject);
            else
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
        
        // Clear collections
        terrainObjects.Clear();
        terrainData.Clear();
        attackerSpawnPoints.Clear();
        defenderSpawnPoints.Clear();
        spawnedObjects.Clear();
        
        // Clear cached material to allow recreation with new biome settings
        if (cachedTerrainMaterial != null)
        {
            DestroyImmediate(cachedTerrainMaterial);
            cachedTerrainMaterial = null;
        }
    }

    /// <summary>
    /// Generate a single terrain mesh for the battle map
    /// </summary>
    private void GenerateTerrainMesh()
    {
        // Create a single large terrain mesh
        GameObject terrain = new GameObject("BattleTerrain");
        terrain.transform.SetParent(transform);
        // Ensure terrain uses the Battlefield layer if it exists
        int battlefieldLayer = LayerMask.NameToLayer("Battlefield");
        if (battlefieldLayer != -1) terrain.layer = battlefieldLayer;
        
        // Add mesh components
        MeshFilter meshFilter = terrain.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = terrain.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = terrain.AddComponent<MeshCollider>();
        
        // Generate the mesh
        Mesh terrainMesh = GenerateTerrainMeshData();
        meshFilter.mesh = terrainMesh;
        meshCollider.sharedMesh = terrainMesh;
        
        // Apply biome material
        ApplyTerrainMaterial(meshRenderer);
        
        terrainObjects.Add(terrain);
    }
    
    /// <summary>
    /// Generate mesh data for the terrain
    /// </summary>
    private Mesh GenerateTerrainMeshData()
    {
        int resolution = Mathf.RoundToInt(mapSize / terrainResolution);
        Vector3[] vertices = new Vector3[(resolution + 1) * (resolution + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[resolution * resolution * 6];
        
        // Generate vertices with height variation
        for (int y = 0; y <= resolution; y++)
        {
            for (int x = 0; x <= resolution; x++)
            {
                int index = y * (resolution + 1) + x;
                
                float worldX = (x / (float)resolution) * mapSize - mapSize / 2f;
                float worldZ = (y / (float)resolution) * mapSize - mapSize / 2f;
                
                // Calculate height using noise
                float height = CalculateHeightAtPosition(worldX, worldZ);
                
                vertices[index] = new Vector3(worldX, height, worldZ);
                uvs[index] = new Vector2(x / (float)resolution, y / (float)resolution);
            }
        }
        
        // Generate triangles
        int triIndex = 0;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int bottomLeft = y * (resolution + 1) + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = (y + 1) * (resolution + 1) + x;
                int topRight = topLeft + 1;
                
                // First triangle
                triangles[triIndex] = bottomLeft;
                triangles[triIndex + 1] = topLeft;
                triangles[triIndex + 2] = bottomRight;
                
                // Second triangle
                triangles[triIndex + 3] = bottomRight;
                triangles[triIndex + 4] = topLeft;
                triangles[triIndex + 5] = topRight;
                
                triIndex += 6;
            }
        }
        
        // Create mesh
        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        
        return mesh;
    }
    
    /// <summary>
    /// Calculate height at a specific world position
    /// </summary>
    private float CalculateHeightAtPosition(float worldX, float worldZ)
    {
        // Use noise to determine height
        float heightNoise = Mathf.PerlinNoise(worldX * elevationNoiseScale, worldZ * elevationNoiseScale);
        return heightNoise * heightVariation;
    }
    
    /// <summary>
    /// Apply terrain material based on primary biome (reuses cached material to save memory)
    /// </summary>
    private void ApplyTerrainMaterial(MeshRenderer renderer)
    {
        // Reuse cached material if it exists and matches the current biome
        // Otherwise create a new one and cache it
        if (cachedTerrainMaterial == null)
        {
            // Use URP Lit as requested
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            cachedTerrainMaterial = new Material(urpLit != null ? urpLit : Shader.Find("Standard"));
            
            // Try to get biome settings for primary biome
            if (biomeSettingsLookup.TryGetValue(primaryBiome, out BiomeSettings settings))
            {
                if (settings.albedoTexture != null)
                {
                    // URP Lit uses _BaseMap for the albedo texture
                    cachedTerrainMaterial.SetTexture("_BaseMap", settings.albedoTexture);
                }
                else
                {
                    // URP Lit uses _BaseColor
                    cachedTerrainMaterial.SetColor("_BaseColor", GetBiomeColor(primaryBiome));
                }
                
                if (settings.normalTexture != null)
                {
                    cachedTerrainMaterial.SetTexture("_BumpMap", settings.normalTexture);
                    cachedTerrainMaterial.EnableKeyword("_NORMALMAP");
                }
            }
            else
            {
                cachedTerrainMaterial.SetColor("_BaseColor", GetBiomeColor(primaryBiome));
            }
            
            // Set material properties
            cachedTerrainMaterial.SetFloat("_Metallic", 0.0f);
            cachedTerrainMaterial.SetFloat("_Smoothness", 0.5f);
        }
        
        // Reuse the cached material (shared material instance)
        renderer.sharedMaterial = cachedTerrainMaterial;
    }

    /// <summary>
    /// Add biome-specific decorations and obstacles (limited to prevent memory issues)
    /// </summary>
    private void AddBiomeDecorations()
    {
        // Calculate decoration count but limit to maximum to prevent memory issues
        int decorationCount = Mathf.RoundToInt(mapSize * mapSize * obstacleDensity / 100f);
        // MEMORY OPTIMIZATION: Limit decorations to maximum of 20 to prevent memory spikes
        const int maxDecorations = 20;
        decorationCount = Mathf.Min(decorationCount, maxDecorations);
        
        for (int i = 0; i < decorationCount; i++)
        {
            // Random position on the map
            Vector3 position = new Vector3(
                Random.Range(-mapSize / 2f, mapSize / 2f),
                0f,
                Random.Range(-mapSize / 2f, mapSize / 2f)
            );
            
            // Calculate height at this position
            position.y = CalculateHeightAtPosition(position.x, position.z);
            
            // Determine biome for this position
            Biome biome = DetermineBiomeAtPosition(position.x, position.z);
            
            // Spawn decoration if appropriate
            if (ShouldSpawnObstacle(biome))
            {
                SpawnBiomeDecoration(position, biome);
            }
        }
    }
    
    /// <summary>
    /// Determine biome at a specific world position
    /// </summary>
    private Biome DetermineBiomeAtPosition(float worldX, float worldZ)
    {
        // Use noise to determine if this should be a secondary biome
        float biomeNoise = Mathf.PerlinNoise(worldX * noiseScale, worldZ * noiseScale);
        
        if (biomeNoise < secondaryBiomeChance && secondaryBiomes.Length > 0)
        {
            // Select a random secondary biome
            return secondaryBiomes[Random.Range(0, secondaryBiomes.Length)];
        }
        
        return primaryBiome;
    }


    /// <summary>
    /// Initialize biome settings lookup dictionary
    /// </summary>
    private void InitializeBiomeSettings()
    {
        biomeSettingsLookup.Clear();
        
        if (biomeSettings != null)
        {
            foreach (var setting in biomeSettings)
            {
                if (setting.biome != Biome.Any) // Skip the "Any" biome
                {
                    biomeSettingsLookup[setting.biome] = setting;
                }
            }
        }
        
        Debug.Log($"[BattleMapGenerator] Initialized biome settings for {biomeSettingsLookup.Count} biomes");
    }

    /// <summary>
    /// Apply visual properties based on biome using textures from biome settings
    /// </summary>
    private void ApplyBiomeVisuals(GameObject tile, TerrainData data)
    {
        Renderer renderer = tile.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Try to get biome settings for this biome
            if (biomeSettingsLookup.TryGetValue(data.biome, out BiomeSettings settings))
            {
                // Create a new material instance for this tile
                Material material = new Material(Shader.Find("Standard"));
                
                // Apply albedo texture if available
                if (settings.albedoTexture != null)
                {
                    material.mainTexture = settings.albedoTexture;
                    material.SetTexture("_MainTex", settings.albedoTexture);
                }
                else
                {
                    // Fallback to biome color if no texture
                    material.color = GetBiomeColor(data.biome);
                }
                
                // Apply normal map if available
                if (settings.normalTexture != null)
                {
                    material.SetTexture("_BumpMap", settings.normalTexture);
                    material.EnableKeyword("_NORMALMAP");
                }
                
                // Set material properties based on terrain type
                if (data.terrainType == TerrainType.Mountain)
                {
                    material.SetFloat("_Metallic", 0.3f);
                    material.SetFloat("_Smoothness", 0.1f);
                }
                else if (data.terrainType == TerrainType.Forest)
                {
                    material.SetFloat("_Metallic", 0.0f);
                    material.SetFloat("_Smoothness", 0.2f);
                }
                else
                {
                    material.SetFloat("_Metallic", 0.0f);
                    material.SetFloat("_Smoothness", 0.5f);
                }
                
                renderer.material = material;
            }
            else
            {
                // Fallback to biome color if no settings found
                Color biomeColor = GetBiomeColor(data.biome);
                renderer.material.color = biomeColor;
            }
        }
    }

    /// <summary>
    /// Get color for a biome (uses BiomeColorHelper for battle map context)
    /// </summary>
    private Color GetBiomeColor(Biome biome)
    {
        return BiomeColorHelper.GetBattleMapColor(biome);
    }

    /// <summary>
    /// Spawn biome-specific decorations using biome settings
    /// </summary>
    private void SpawnBiomeDecoration(Vector3 position, Biome biome)
    {
        // Try to get biome settings for decorations
        if (biomeSettingsLookup.TryGetValue(biome, out BiomeSettings settings) && 
            settings.decorations != null && settings.decorations.Length > 0)
        {
            // Use actual decoration prefabs from biome settings
            GameObject decorationPrefab = settings.decorations[Random.Range(0, settings.decorations.Length)];
            if (decorationPrefab != null)
            {
                GameObject decoration = Instantiate(decorationPrefab, position, Quaternion.identity);
                decoration.transform.SetParent(transform);
                decoration.name = $"Decoration_{biome}_{decorationPrefab.name}";
                spawnedObjects.Add(decoration);
                return;
            }
        }
        
        // Fallback: create simple colored cubes as placeholders
        GameObject fallbackDecoration = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fallbackDecoration.transform.position = position + Vector3.up * 0.5f;
        fallbackDecoration.transform.localScale = Vector3.one * 0.5f;
        fallbackDecoration.transform.SetParent(transform);
        fallbackDecoration.name = $"Decoration_{biome}";
        
        // Color based on biome
        Renderer fallbackRenderer = fallbackDecoration.GetComponent<Renderer>();
        if (fallbackRenderer != null)
        {
            fallbackRenderer.material.color = GetBiomeColor(biome) * 0.7f;
        }
        
        spawnedObjects.Add(fallbackDecoration);
    }

    /// <summary>
    /// Create spawn points for both sides
    /// </summary>
    private void CreateSpawnPoints(int attackerUnits, int defenderUnits)
    {
        // Find good spawn positions (avoid impassable terrain)
        Vector3 attackerSpawn = FindGoodSpawnPosition(-spawnDistance / 2f);
        Vector3 defenderSpawn = FindGoodSpawnPosition(spawnDistance / 2f);
        
        // Create spawn points for attackers
        for (int i = 0; i < attackerUnits; i++)
        {
            Vector3 spawnPos = attackerSpawn + new Vector3(
                Random.Range(-5f, 5f),
                0f,
                Random.Range(-5f, 5f)
            );
            attackerSpawnPoints.Add(spawnPos);
        }
        
        // Create spawn points for defenders
        for (int i = 0; i < defenderUnits; i++)
        {
            Vector3 spawnPos = defenderSpawn + new Vector3(
                Random.Range(-5f, 5f),
                0f,
                Random.Range(-5f, 5f)
            );
            defenderSpawnPoints.Add(spawnPos);
        }
        
        Debug.Log($"[BattleMapGenerator] Created {attackerSpawnPoints.Count} attacker and {defenderSpawnPoints.Count} defender spawn points");
    }

    /// <summary>
    /// Find a good spawn position (avoiding impassable terrain)
    /// </summary>
    private Vector3 FindGoodSpawnPosition(float xOffset)
    {
        for (int attempts = 0; attempts < 50; attempts++)
        {
            float z = Random.Range(-mapSize / 4f, mapSize / 4f);
            Vector3 candidate = new Vector3(xOffset, 0f, z);
            
            if (IsPositionInBounds(candidate) && !IsPositionImpassable(candidate))
            {
                return candidate;
            }
        }
        
        // Fallback to center if no good position found
        return new Vector3(xOffset, 0f, 0f);
    }

    /// <summary>
    /// Check if a position is impassable
    /// </summary>
    private bool IsPositionImpassable(Vector3 position)
    {
        if (!IsPositionInBounds(position)) return true;
        
        Biome biome = DetermineBiomeAtPosition(position.x, position.z);
        return IsBiomeImpassable(biome);
    }

    /// <summary>
    /// Generate navigation mesh for pathfinding
    /// </summary>
    private void GenerateNavigationMesh()
    {
        // This would integrate with Unity's NavMesh system
        // For now, it's a placeholder
        Debug.Log("[BattleMapGenerator] Navigation mesh generation placeholder");
    }

    // Helper methods
    private TerrainType GetTerrainTypeFromBiome(Biome biome)
    {
        return biome switch
        {
            Biome.Mountain => TerrainType.Mountain,
            Biome.Forest or Biome.Jungle => TerrainType.Forest,
            Biome.Swamp or Biome.Marsh => TerrainType.Swamp,
            Biome.Ocean or Biome.Seas => TerrainType.Water,
            _ => TerrainType.Plains
        };
    }

    private bool ShouldHaveCover(Biome biome)
    {
        return biome == Biome.Forest || biome == Biome.Jungle || biome == Biome.Swamp;
    }

    private bool IsBiomeImpassable(Biome biome)
    {
        return biome == Biome.Ocean || biome == Biome.Seas || biome == Biome.Mountain;
    }

    private bool ShouldSpawnObstacle(Biome biome)
    {
        float chance = biome switch
        {
            Biome.Forest or Biome.Jungle => 0.3f,
            Biome.Mountain => 0.2f,
            Biome.Swamp or Biome.Marsh => 0.15f,
            Biome.Desert => 0.1f,
            _ => 0.05f
        };
        
        return Random.value < chance;
    }

    // Public API methods
    public Vector3 GetRandomPosition()
    {
        float x = Random.Range(-mapSize / 2f, mapSize / 2f);
        float z = Random.Range(-mapSize / 2f, mapSize / 2f);
        float y = CalculateHeightAtPosition(x, z);
        return new Vector3(x, y, z);
    }

    public bool IsPositionInBounds(Vector3 position)
    {
        return position.x >= -mapSize / 2f && position.x <= mapSize / 2f &&
               position.z >= -mapSize / 2f && position.z <= mapSize / 2f;
    }

    public TerrainType GetTerrainTypeAt(Vector3 position)
    {
        if (!IsPositionInBounds(position)) return TerrainType.Impassable;
        
        Biome biome = DetermineBiomeAtPosition(position.x, position.z);
        return GetTerrainTypeFromBiome(biome);
    }

    public TerrainData GetTerrainDataAt(Vector3 position)
    {
        if (!IsPositionInBounds(position))
        {
            return new TerrainData { isImpassable = true };
        }
        
        Biome biome = DetermineBiomeAtPosition(position.x, position.z);
        float elevation = CalculateHeightAtPosition(position.x, position.z);
        
        return new TerrainData
        {
            biome = biome,
            elevation = elevation,
            terrainType = GetTerrainTypeFromBiome(biome),
            hasCover = ShouldHaveCover(biome),
            isImpassable = IsBiomeImpassable(biome),
            defenseBonus = BiomeHelper.GetDefenseBonus(biome),
            movementCost = BiomeHelper.GetMovementCost(biome)
        };
    }

    public List<Vector3> GetAttackerSpawnPoints() => attackerSpawnPoints;
    public List<Vector3> GetDefenderSpawnPoints() => defenderSpawnPoints;
}

public enum TerrainType
{
    Plains,         // Open field, no bonuses
    Hills,          // Defense bonus, movement penalty
    Forest,         // Cover bonus, movement penalty
    Water,          // Only certain units can cross
    Mountain,       // Impassable, high defense
    Swamp,          // Movement penalty, health damage
    Impassable      // Cannot be traversed
}
