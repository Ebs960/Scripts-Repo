using UnityEngine;
using UnityEngine.AI; // For NavMesh runtime baking
using System.Collections.Generic;
using System.Linq;
using System.Reflection; // For MapMagic 2 integration via reflection

/// <summary>
/// Type of battle - affects terrain generation
/// </summary>
public enum BattleType
{
    Land,    // Standard land battle
    Naval,   // Naval battle (water-based)
    Coastal, // Coastal battle (land and water)
    Siege    // Siege battle (fortified positions)
}

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
    [Tooltip("Primary biome for this battle map. Set manually for editor testing, or automatically from defender's tile when starting battle from campaign map.")]
    public Biome primaryBattleBiome = Biome.Plains;
    [Tooltip("Elevation for battle map (0-1). Set manually for editor testing, or automatically from defender's tile when starting battle from campaign map.")]
    public float battleTileElevation = 0.5f;
    [Tooltip("Moisture for battle map. Set manually for editor testing, or automatically from defender's tile when starting battle from campaign map.")]
    public float battleTileMoisture = 0.5f;
    [Tooltip("Temperature for battle map. Set manually for editor testing, or automatically from defender's tile when starting battle from campaign map.")]
    public float battleTileTemperature = 0.5f;
    
    [Header("Battle Type")]
    [Tooltip("Type of battle: Land, Naval, Coastal, or Siege")]
    public BattleType battleType = BattleType.Land;
    
    [Header("MapMagic 2 Integration")]
    [Tooltip("Use MapMagic 2 for procedural terrain generation (if available)")]
    public bool useMapMagic2 = true;
    [Tooltip("Reference to MapMagic 2 GameObject/Component (auto-detected if null)")]
    public GameObject mapMagic2Object;
    [Tooltip("MapMagic 2 graph preset to use for terrain generation (optional)")]
    public ScriptableObject mapMagic2Graph;
    
    [Tooltip("Biome settings for textures and materials")]
    public BiomeSettings[] biomeSettings = new BiomeSettings[0];
    
    // Legacy property for backward compatibility (uses primaryBattleBiome)
    public Biome primaryBiome => primaryBattleBiome;
    
    // MapMagic 2 integration (via reflection to avoid hard dependency)
    private object mapMagic2Instance;
    private bool mapMagic2Available = false;

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
    [Tooltip("Maximum number of decorations to spawn on the battle map (0 = unlimited, but may cause memory issues)")]
    [Range(0, 200)]
    public int maxDecorations = 50;
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
    /// Uses MapMagic 2 if available, otherwise falls back to simple mesh generation
    /// </summary>
    public void GenerateBattleMap(float mapSize, int attackerUnits, int defenderUnits)
    {
        Debug.Log($"[BattleMapGenerator] GenerateBattleMap called: mapSize={mapSize}, biome={primaryBattleBiome}, elevation={battleTileElevation}");
        
        this.mapSize = mapSize;
        mapCenter = Vector3.zero;
        
        // Initialize biome settings lookup
        InitializeBiomeSettings();
        
        ClearExistingMap();
        
        // Try to use MapMagic 2 if enabled and available
        if (useMapMagic2 && TryInitializeMapMagic2())
        {
            Debug.Log("[BattleMapGenerator] Using MapMagic 2 for terrain generation");
            GenerateTerrainWithMapMagic2();
        }
        else
        {
            Debug.Log("[BattleMapGenerator] Using fallback terrain mesh generation");
            GenerateTerrainMesh();
        }
        
        AddBiomeDecorations();
        CreateSpawnPoints(attackerUnits, defenderUnits);
        
        // IMPROVED: Bake NavMesh at runtime after map generation
        GenerateNavigationMesh();
        
        Debug.Log($"[BattleMapGenerator] Generated {primaryBattleBiome} battle map ({mapSize}x{mapSize}) with elevation {battleTileElevation:F2}, battle type: {battleType}");
        Debug.Log($"[BattleMapGenerator] Terrain objects created: {terrainObjects.Count}, Decorations spawned: {spawnedObjects.Count}");
    }
    
    /// <summary>
    /// Try to initialize MapMagic 2 integration (uses reflection to avoid hard dependency)
    /// </summary>
    private bool TryInitializeMapMagic2()
    {
        if (mapMagic2Available && mapMagic2Instance != null)
            return true;
        
        // Try to find MapMagic 2 component
        if (mapMagic2Object == null)
        {
            // Search for MapMagic component in scene
            var mapMagicType = System.Type.GetType("MapMagic.MapMagic, Assembly-CSharp");
            if (mapMagicType == null)
            {
                // Try alternative namespace
                mapMagicType = System.Type.GetType("MapMagic.MapMagic");
            }
            
            if (mapMagicType != null)
            {
                var mapMagicComponent = FindFirstObjectByType(mapMagicType);
                if (mapMagicComponent != null)
                {
                    mapMagic2Object = ((Component)mapMagicComponent).gameObject;
                    mapMagic2Instance = mapMagicComponent;
                }
            }
        }
        else
        {
            // Use assigned reference
            var mapMagicComponent = mapMagic2Object.GetComponent("MapMagic");
            if (mapMagicComponent != null)
            {
                mapMagic2Instance = mapMagicComponent;
            }
        }
        
        mapMagic2Available = (mapMagic2Instance != null);
        
        if (mapMagic2Available)
        {
            Debug.Log("[BattleMapGenerator] MapMagic 2 detected and initialized");
        }
        else
        {
            Debug.LogWarning("[BattleMapGenerator] MapMagic 2 not found - using fallback terrain generation. Install MapMagic 2 from Asset Store to use procedural terrain.");
        }
        
        return mapMagic2Available;
    }
    
    /// <summary>
    /// Generate terrain using MapMagic 2
    /// Passes biome/elevation/moisture/temperature data to MapMagic 2
    /// </summary>
    private void GenerateTerrainWithMapMagic2()
    {
        if (mapMagic2Instance == null)
        {
            Debug.LogError("[BattleMapGenerator] MapMagic 2 instance is null!");
            GenerateTerrainMesh(); // Fallback
            return;
        }
        
        try
        {
            // Get MapMagic type for reflection
            var mapMagicType = mapMagic2Instance.GetType();
            
            // Set biome/elevation parameters if MapMagic 2 supports it
            SetMapMagic2BiomeParameters(mapMagicType, mapMagic2Instance);
            
            // Generate terrain using MapMagic 2
            // MapMagic 2 typically has a Generate() or GenerateMap() method
            var generateMethod = mapMagicType.GetMethod("Generate", BindingFlags.Public | BindingFlags.Instance);
            if (generateMethod == null)
            {
                generateMethod = mapMagicType.GetMethod("GenerateMap", BindingFlags.Public | BindingFlags.Instance);
            }
            
            if (generateMethod != null)
            {
                // Call Generate() or GenerateMap()
                generateMethod.Invoke(mapMagic2Instance, null);
                Debug.Log("[BattleMapGenerator] MapMagic 2 terrain generation completed");
            }
            else
            {
                Debug.LogWarning("[BattleMapGenerator] MapMagic 2 Generate method not found - terrain may need manual generation");
            }
            
            // MapMagic 2 creates its own terrain objects, so we don't need to create mesh manually
            // But we should track the terrain for cleanup
            var terrain = mapMagic2Object.transform.Find("Terrain");
            if (terrain != null)
            {
                terrainObjects.Add(terrain.gameObject);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BattleMapGenerator] Error generating terrain with MapMagic 2: {e.Message}");
            Debug.LogError($"[BattleMapGenerator] Falling back to simple mesh generation");
            GenerateTerrainMesh(); // Fallback
        }
    }
    
    /// <summary>
    /// Set biome/elevation/moisture/temperature parameters in MapMagic 2
    /// Uses reflection to set parameters if MapMagic 2 exposes them
    /// </summary>
    private void SetMapMagic2BiomeParameters(System.Type mapMagicType, object mapMagicInstance)
    {
        try
        {
            // Try to set biome parameter (MapMagic 2 may have a biome field or property)
            var biomeField = mapMagicType.GetField("biome", BindingFlags.Public | BindingFlags.Instance);
            if (biomeField != null && biomeField.FieldType == typeof(Biome))
            {
                biomeField.SetValue(mapMagicInstance, primaryBattleBiome);
            }
            
            var biomeProperty = mapMagicType.GetProperty("biome", BindingFlags.Public | BindingFlags.Instance);
            if (biomeProperty != null && biomeProperty.PropertyType == typeof(Biome))
            {
                biomeProperty.SetValue(mapMagicInstance, primaryBattleBiome);
            }
            
            // Try to set elevation parameter
            var elevationField = mapMagicType.GetField("elevation", BindingFlags.Public | BindingFlags.Instance);
            if (elevationField != null && elevationField.FieldType == typeof(float))
            {
                elevationField.SetValue(mapMagicInstance, battleTileElevation);
            }
            
            var elevationProperty = mapMagicType.GetProperty("elevation", BindingFlags.Public | BindingFlags.Instance);
            if (elevationProperty != null && elevationProperty.PropertyType == typeof(float))
            {
                elevationProperty.SetValue(mapMagicInstance, battleTileElevation);
            }
            
            // Try to set moisture parameter
            var moistureField = mapMagicType.GetField("moisture", BindingFlags.Public | BindingFlags.Instance);
            if (moistureField != null && moistureField.FieldType == typeof(float))
            {
                moistureField.SetValue(mapMagicInstance, battleTileMoisture);
            }
            
            var moistureProperty = mapMagicType.GetProperty("moisture", BindingFlags.Public | BindingFlags.Instance);
            if (moistureProperty != null && moistureProperty.PropertyType == typeof(float))
            {
                moistureProperty.SetValue(mapMagicInstance, battleTileMoisture);
            }
            
            // Try to set temperature parameter
            var temperatureField = mapMagicType.GetField("temperature", BindingFlags.Public | BindingFlags.Instance);
            if (temperatureField != null && temperatureField.FieldType == typeof(float))
            {
                temperatureField.SetValue(mapMagicInstance, battleTileTemperature);
            }
            
            var temperatureProperty = mapMagicType.GetProperty("temperature", BindingFlags.Public | BindingFlags.Instance);
            if (temperatureProperty != null && temperatureProperty.PropertyType == typeof(float))
            {
                temperatureProperty.SetValue(mapMagicInstance, battleTileTemperature);
            }
            
            // Try to access graph and set parameters there (MapMagic 2 uses graphs)
            var graphProperty = mapMagicType.GetProperty("graph", BindingFlags.Public | BindingFlags.Instance);
            if (graphProperty != null)
            {
                var graph = graphProperty.GetValue(mapMagicInstance);
                if (graph != null)
                {
                    SetMapMagic2GraphParameters(graph);
                }
            }
            
            Debug.Log($"[BattleMapGenerator] Set MapMagic 2 parameters: biome={primaryBattleBiome}, elevation={battleTileElevation}, moisture={battleTileMoisture}, temperature={battleTileTemperature}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not set all MapMagic 2 parameters: {e.Message}");
        }
    }
    
    /// <summary>
    /// Set parameters in MapMagic 2's graph system (biomes, nodes, etc.)
    /// </summary>
    private void SetMapMagic2GraphParameters(object graph)
    {
        try
        {
            var graphType = graph.GetType();
            
            // Try to find biome nodes or biome settings in the graph
            // MapMagic 2 graphs typically have collections of generators/nodes
            var generatorsProperty = graphType.GetProperty("generators", BindingFlags.Public | BindingFlags.Instance);
            if (generatorsProperty == null)
            {
                generatorsProperty = graphType.GetProperty("nodes", BindingFlags.Public | BindingFlags.Instance);
            }
            
            if (generatorsProperty != null)
            {
                var generators = generatorsProperty.GetValue(graph);
                if (generators is System.Collections.IEnumerable genEnumerable)
                {
                    foreach (var generator in genEnumerable)
                    {
                        if (generator == null) continue;
                        
                        var genType = generator.GetType();
                        var typeName = genType.Name.ToLower();
                        
                        // Look for biome-related generators
                        if (typeName.Contains("biome") || typeName.Contains("noise") || typeName.Contains("height"))
                        {
                            // Try to set elevation/intensity parameters
                            var intensityProperty = genType.GetProperty("intensity", BindingFlags.Public | BindingFlags.Instance);
                            if (intensityProperty != null && intensityProperty.PropertyType == typeof(float))
                            {
                                intensityProperty.SetValue(generator, battleTileElevation);
                            }
                            
                            var heightProperty = genType.GetProperty("height", BindingFlags.Public | BindingFlags.Instance);
                            if (heightProperty != null && heightProperty.PropertyType == typeof(float))
                            {
                                heightProperty.SetValue(generator, battleTileElevation * heightVariation);
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not set MapMagic 2 graph parameters: {e.Message}");
        }
    }
    
    /// <summary>
    /// Helper method to find object by type using reflection
    /// </summary>
    private Component FindFirstObjectByType(System.Type type)
    {
        // Use Unity's FindFirstObjectByType<T> via reflection
        var findMethod = typeof(Object).GetMethod("FindFirstObjectByType", BindingFlags.Public | BindingFlags.Static);
        if (findMethod != null)
        {
            // FindFirstObjectByType is generic, so we need to make a generic method
            var genericMethod = findMethod.MakeGenericMethod(type);
            var result = genericMethod.Invoke(null, null);
            return result as Component;
        }
        
        // Fallback to FindObjectOfType
        var findObjectMethod = typeof(Object).GetMethod("FindObjectOfType", BindingFlags.Public | BindingFlags.Static);
        if (findObjectMethod != null)
        {
            var genericMethod = findObjectMethod.MakeGenericMethod(type);
            var result = genericMethod.Invoke(null, null);
            return result as Component;
        }
        
        // Last resort: search all objects
        var allObjects = FindObjectsByType(type, FindObjectsSortMode.None);
        if (allObjects != null && allObjects.Length > 0)
        {
            return allObjects[0] as Component;
        }
        
        return null;
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
        // This ensures material updates when biome changes
        if (cachedTerrainMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(cachedTerrainMaterial);
            else
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
    /// Uses world tile elevation as base, with micro-variations for terrain roughness
    /// Higher elevation tiles have rougher terrain (more variation)
    /// </summary>
    private float CalculateHeightAtPosition(float worldX, float worldZ)
    {
        // Base height from world tile elevation (0-1 range, scale to world units)
        float baseHeight = battleTileElevation * heightVariation;
        
        // Micro-variations for terrain roughness (noise detail)
        // Higher elevation = rougher terrain (more variation)
        float roughnessMultiplier = 0.25f + (battleTileElevation * 0.5f); // 0.25 to 0.75 based on elevation
        float heightNoise = Mathf.PerlinNoise(worldX * elevationNoiseScale, worldZ * elevationNoiseScale);
        float noiseDetail = (heightNoise - 0.5f) * 2f; // -1 to 1
        
        // Apply roughness based on elevation
        float height = baseHeight + (noiseDetail * roughnessMultiplier * heightVariation);
        
        return height;
    }
    
    /// <summary>
    /// Apply terrain material based on primary biome (reuses cached material to save memory)
    /// </summary>
    private void ApplyTerrainMaterial(MeshRenderer renderer)
    {
        // Always recreate material to ensure it uses the current biome
        // This ensures biome changes are reflected immediately
        if (cachedTerrainMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(cachedTerrainMaterial);
            else
                DestroyImmediate(cachedTerrainMaterial);
            cachedTerrainMaterial = null;
        }
        
        // Create new material for current biome
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
            // No biome settings found - use fallback color
            cachedTerrainMaterial.SetColor("_BaseColor", GetBiomeColor(primaryBiome));
            Debug.LogWarning($"[BattleMapGenerator] No biome settings found for {primaryBiome}! Using fallback color. Add biome settings or use BattleMapBiomeSetup to copy from PlanetGenerator.");
        }
        
        // Set material properties
        cachedTerrainMaterial.SetFloat("_Metallic", 0.0f);
        cachedTerrainMaterial.SetFloat("_Smoothness", 0.5f);
        
        // Apply material to renderer
        renderer.sharedMaterial = cachedTerrainMaterial;
    }

    /// <summary>
    /// Add biome-specific decorations and obstacles (limited to prevent memory issues)
    /// </summary>
    private void AddBiomeDecorations()
    {
        // Calculate decoration count but limit to maximum to prevent memory issues
        int decorationCount = Mathf.RoundToInt(mapSize * mapSize * obstacleDensity / 100f);
        // Limit decorations to configured maximum (0 = unlimited, but not recommended)
        if (maxDecorations > 0)
        {
            decorationCount = Mathf.Min(decorationCount, maxDecorations);
        }
        
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
            
            // Use the primary biome from defender's tile (no variation)
            Biome biome = primaryBattleBiome;
            
            // Spawn decoration if appropriate
            if (ShouldSpawnObstacle(biome))
            {
                SpawnBiomeDecoration(position, biome);
            }
        }
    }
    
    /// <summary>
    /// Determine biome at a specific world position
    /// Battle maps now use ONLY the defender's tile biome (no secondary biomes)
    /// </summary>
    private Biome DetermineBiomeAtPosition(float worldX, float worldZ)
    {
        // Battle map uses ONLY the primary biome from the defender's tile
        // No secondary biomes or randomness - 100% faithful to the campaign map tile
        return primaryBattleBiome;
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
    /// Generate NavMesh at runtime for procedurally generated maps
    /// Uses Unity's NavMeshBuilder API to bake NavMesh after map generation
    /// This works at runtime without needing editor-only APIs
    /// </summary>
    private void GenerateNavigationMesh()
    {
        Debug.Log("[BattleMapGenerator] Starting runtime NavMesh baking...");
        
        // Get NavMesh build settings (use default settings with ID 0)
        // You can create custom settings in the Navigation window and reference them by ID
        NavMeshBuildSettings buildSettings = NavMesh.GetSettingsByID(0);
        
        // Define the bounds for NavMesh building (the entire map)
        Bounds buildBounds = new Bounds(
            mapCenter,
            new Vector3(mapSize, 20f, mapSize) // Height of 20 should cover terrain elevation
        );
        
        // Collect all sources (terrain and obstacles) using NavMeshBuilder
        // This automatically finds all colliders and meshes in the bounds
        // FIXED: Use Battlefield layer to match terrain object layer
        int battlefieldLayer = LayerMask.NameToLayer("Battlefield");
        int layerMask = battlefieldLayer != -1 ? (1 << battlefieldLayer) : ~0; // Use Battlefield layer, or all layers if it doesn't exist
        
        Debug.Log($"[BattleMapGenerator] Collecting NavMesh sources on layer mask: {layerMask} (Battlefield layer index: {battlefieldLayer})");
        
        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(
            buildBounds,
            layerMask, // Use Battlefield layer to match terrain
            NavMeshCollectGeometry.RenderMeshes | NavMeshCollectGeometry.PhysicsColliders,
            0, // Default area (walkable)
            new List<NavMeshBuildMarkup>(), // No special markups
            sources
        );
        
        // DEBUG: Log what sources were collected
        Debug.Log($"[BattleMapGenerator] Collected {sources.Count} NavMesh sources from bounds {buildBounds}");
        if (sources.Count > 0)
        {
            Vector3 firstSourcePos = sources[0].transform.GetColumn(3); // Position is in the 4th column of Matrix4x4
            Debug.Log($"[BattleMapGenerator] First source: shape={sources[0].shape}, component={sources[0].component?.name ?? "null"}, position={firstSourcePos}");
        }
        
        // DEBUG: Log terrain objects
        Debug.Log($"[BattleMapGenerator] Checking {terrainObjects.Count} terrain objects:");
        foreach (var terrainObj in terrainObjects)
        {
            if (terrainObj != null)
            {
                var collider = terrainObj.GetComponent<Collider>();
                var meshFilter = terrainObj.GetComponent<MeshFilter>();
                Debug.Log($"  - {terrainObj.name}: Collider={collider != null} (isTrigger={collider?.isTrigger ?? false}), MeshFilter={meshFilter != null}, Parent={terrainObj.transform.parent?.name ?? "none"}");
            }
        }
        
        // Filter sources to only include our terrain and obstacles
        // This ensures we only bake the procedurally generated map, not other scene objects
        // FIXED: Also check child objects and use a more robust matching method
        List<NavMeshBuildSource> filteredSources = new List<NavMeshBuildSource>();
        foreach (var source in sources)
        {
            // Check if this source belongs to our generated terrain or obstacles
            bool isOurObject = false;
            
            // Check terrain objects (including child objects)
            foreach (var terrainObj in terrainObjects)
            {
                if (terrainObj != null && IsSourceFromObject(source, terrainObj))
                {
                    isOurObject = true;
                    Debug.Log($"[BattleMapGenerator] Matched source to terrain object: {terrainObj.name}");
                    break;
                }
            }
            
            // Check spawned objects (obstacles, decorations)
            if (!isOurObject)
            {
                foreach (var spawnedObj in spawnedObjects)
                {
                    if (spawnedObj != null && IsSourceFromObject(source, spawnedObj))
                    {
                        // Only include if it has a non-trigger collider (should block navigation)
                        var collider = spawnedObj.GetComponent<Collider>();
                        if (collider != null && !collider.isTrigger)
                        {
                            isOurObject = true;
                            break;
                        }
                    }
                }
            }
            
            if (isOurObject)
            {
                filteredSources.Add(source);
            }
        }
        
        // DEBUG: If no sources matched, try a fallback: include ALL sources within bounds
        // This handles cases where child objects or component matching fails
        if (filteredSources.Count == 0 && sources.Count > 0)
        {
            Debug.LogWarning("[BattleMapGenerator] No sources matched terrain objects. Using fallback: including all sources within bounds.");
            Debug.LogWarning("[BattleMapGenerator] This might include scene objects. Consider checking IsSourceFromObject matching logic.");
            
            // Fallback: include all sources that are within our map bounds
            // This is safer than failing completely, but may include unwanted objects
            foreach (var source in sources)
            {
                // Extract position from Matrix4x4 transform
                Vector3 sourcePos = source.transform.GetColumn(3); // Position is in the 4th column
                
                // Only include if the source's position is within our bounds
                if (buildBounds.Contains(sourcePos))
                {
                    filteredSources.Add(source);
                    string sourceName = source.component != null ? source.component.name : "Unknown";
                    Debug.Log($"[BattleMapGenerator] Fallback: Added source from {sourceName} at {sourcePos}");
                }
            }
        }
        
        if (filteredSources.Count == 0)
        {
            Debug.LogError("[BattleMapGenerator] No NavMesh sources found! Make sure terrain has colliders.");
            Debug.LogError($"[BattleMapGenerator] Total sources collected: {sources.Count}, Terrain objects: {terrainObjects.Count}");
            return;
        }
        
        // Build the NavMesh data
        NavMeshData navMeshData = NavMeshBuilder.BuildNavMeshData(
            buildSettings,
            filteredSources,
            buildBounds,
            Vector3.zero,
            Quaternion.identity
        );
        
        // Remove any existing NavMesh and add the new one
        NavMesh.RemoveAllNavMeshData();
        NavMesh.AddNavMeshData(navMeshData);
        
        // DEBUG: Log NavMesh data details
        if (navMeshData != null)
        {
            Debug.Log($"[BattleMapGenerator] NavMesh Data Details:");
            Debug.Log($"  - Sources used: {filteredSources.Count}/{sources.Count}");
            Debug.Log($"  - Bounds: {buildBounds}");
            Debug.Log($"  - Source objects: {filteredSources.Count}");
            
            // Log what types of sources we're using
            int meshSources = 0, colliderSources = 0;
            foreach (var source in filteredSources)
            {
                if (source.shape == NavMeshBuildSourceShape.Mesh) meshSources++;
                else if (source.shape == NavMeshBuildSourceShape.Box || 
                         source.shape == NavMeshBuildSourceShape.Sphere ||
                         source.shape == NavMeshBuildSourceShape.Capsule) colliderSources++;
            }
            Debug.Log($"  - Mesh sources: {meshSources}, Collider sources: {colliderSources}");
        }
        else
        {
            Debug.LogError("[BattleMapGenerator] NavMesh data is null! Baking failed.");
        }
        
        // IMPROVED: Wait a frame to ensure NavMesh is fully processed by Unity
        // This helps prevent formations from failing to find NavMesh immediately after baking
        StartCoroutine(WaitForNavMeshReady());
        
        Debug.Log($"[BattleMapGenerator] NavMesh baked successfully! Sources: {filteredSources.Count}/{sources.Count}, Bounds: {buildBounds}");
    }
    
    /// <summary>
    /// Check if a NavMeshBuildSource comes from a specific GameObject
    /// IMPROVED: Also checks child objects and uses more robust matching
    /// </summary>
    private bool IsSourceFromObject(NavMeshBuildSource source, GameObject obj)
    {
        if (obj == null) return false;
        
        // Check if source's component belongs to this object or any of its children
        if (source.component != null)
        {
            GameObject sourceGameObject = source.component.gameObject;
            
            // Direct match
            if (sourceGameObject == obj)
            {
                return true;
            }
            
            // Check if it's a child of our object
            Transform sourceTransform = sourceGameObject.transform;
            while (sourceTransform != null)
            {
                if (sourceTransform.gameObject == obj)
                {
                    return true;
                }
                sourceTransform = sourceTransform.parent;
            }
        }
        
        // For mesh sources, check if it matches any mesh on the object or its children
        if (source.shape == NavMeshBuildSourceShape.Mesh && source.sourceObject != null)
        {
            // Check object and all children
            MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>(true);
            foreach (var meshFilter in meshFilters)
            {
                if (meshFilter != null && meshFilter.sharedMesh == source.sourceObject)
                {
                    return true;
                }
            }
        }
        
        // For collider sources, check if it matches any collider on the object or its children
        if (source.shape == NavMeshBuildSourceShape.Box || 
            source.shape == NavMeshBuildSourceShape.Sphere ||
            source.shape == NavMeshBuildSourceShape.Capsule ||
            source.shape == NavMeshBuildSourceShape.Mesh) // MeshCollider also counts
        {
            // Check object and all children
            Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider != null && collider == source.component)
                {
                    return true;
                }
            }
        }
        
        return false;
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
    
    /// <summary>
    /// Wait for NavMesh to be fully processed by Unity after baking
    /// This ensures formations can find the NavMesh when they try to set up their agents
    /// </summary>
    private System.Collections.IEnumerator WaitForNavMeshReady()
    {
        // Wait a frame to let Unity process the NavMesh data
        yield return null;
        
        // DEBUG: Check NavMesh data immediately
        var navMeshTri = NavMesh.CalculateTriangulation();
        bool hasData = navMeshTri.vertices != null && navMeshTri.vertices.Length > 0;
        Debug.Log($"[BattleMapGenerator] NavMesh data check: Has data = {hasData}, Vertices = {navMeshTri.vertices?.Length ?? 0}");
        
        // Verify NavMesh is queryable by sampling a position at the map center
        int attempts = 0;
        const int maxAttempts = 10;
        
        while (attempts < maxAttempts)
        {
            // Try sampling at map center
            if (NavMesh.SamplePosition(mapCenter, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                Debug.Log($"[BattleMapGenerator] NavMesh confirmed ready after {attempts + 1} frame(s) at center {mapCenter} (sampled to {hit.position})");
                
                // DEBUG: Test a few more positions to ensure NavMesh is fully ready
                bool allTestsPassed = true;
                for (int i = 0; i < 5; i++)
                {
                    Vector3 testPos = mapCenter + new Vector3(
                        Random.Range(-mapSize * 0.3f, mapSize * 0.3f),
                        0,
                        Random.Range(-mapSize * 0.3f, mapSize * 0.3f)
                    );
                    if (!NavMesh.SamplePosition(testPos, out NavMeshHit testHit, 10f, NavMesh.AllAreas))
                    {
                        allTestsPassed = false;
                        Debug.LogWarning($"[BattleMapGenerator] NavMesh test failed at {testPos}");
                    }
                }
                
                if (allTestsPassed)
                {
                    Debug.Log($"[BattleMapGenerator] All NavMesh tests passed! NavMesh is fully ready.");
                }
                
                yield break;
            }
            
            attempts++;
            yield return null; // Wait another frame
        }
        
        Debug.LogWarning($"[BattleMapGenerator] NavMesh may not be fully ready after {maxAttempts} frames. Formations will retry setup.");
        Debug.LogWarning($"[BattleMapGenerator] Final check - NavMesh data: Has data = {hasData}, Vertices = {navMeshTri.vertices?.Length ?? 0}");
        
        // DEBUG: Check if terrain has colliders
        bool hasTerrainColliders = false;
        foreach (var terrainObj in terrainObjects)
        {
            if (terrainObj != null && terrainObj.GetComponent<Collider>() != null)
            {
                hasTerrainColliders = true;
                break;
            }
        }
        Debug.LogWarning($"[BattleMapGenerator] Terrain has colliders: {hasTerrainColliders}");
    }
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
