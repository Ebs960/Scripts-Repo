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
    
    [System.Serializable]
    public class BiomeGraphEntry
    {
        public Biome biome;
        public ScriptableObject graph; // MapMagic 2 graph asset
    }
    
    [Header("MapMagic 2 Biome Graphs")]
    [Tooltip("Graph assets for each biome. Assign one graph per biome type. The system will automatically select the correct graph based on primaryBattleBiome.")]
    public BiomeGraphEntry[] biomeGraphs = new BiomeGraphEntry[0];
    
    private Dictionary<Biome, ScriptableObject> biomeGraphLookup = new Dictionary<Biome, ScriptableObject>();
    
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
    
    [Header("Grounding & Decoration Placement")]
    [Tooltip("Layers considered battlefield ground for raycast grounding and decoration placement")] 
    public LayerMask battlefieldLayers = ~0; // default: everything

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
    /// Initialize biome graph lookup dictionary from biomeGraphs array
    /// </summary>
    private void InitializeBiomeGraphLookup()
    {
        biomeGraphLookup.Clear();
        
        if (biomeGraphs != null)
        {
            foreach (var entry in biomeGraphs)
            {
                if (entry != null && entry.graph != null && entry.biome != Biome.Any)
                {
                    biomeGraphLookup[entry.biome] = entry.graph;
                }
            }
        }
        
        Debug.Log($"[BattleMapGenerator] Initialized biome graph lookup with {biomeGraphLookup.Count} graphs");
    }
    
    /// <summary>
    /// Try to initialize MapMagic 2 integration (uses reflection to avoid hard dependency)
    /// </summary>
    private bool TryInitializeMapMagic2()
    {
        if (mapMagic2Available && mapMagic2Instance != null)
            return true;
        
        // Initialize biome graph lookup
        InitializeBiomeGraphLookup();
        
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
    /// Automatically switches to the correct graph based on primaryBattleBiome
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
            
            // AUTOMATIC GRAPH SWITCHING: Select the correct graph based on biome
            if (biomeGraphLookup.TryGetValue(primaryBattleBiome, out ScriptableObject biomeGraph))
            {
                // Set the graph property on MapMagic component
                var graphProperty = mapMagicType.GetProperty("graph", BindingFlags.Public | BindingFlags.Instance);
                if (graphProperty != null)
                {
                    graphProperty.SetValue(mapMagic2Instance, biomeGraph);
                    Debug.Log($"[BattleMapGenerator] Switched MapMagic 2 graph to {primaryBattleBiome} graph");
                }
                else
                {
                    // Try field instead of property
                    var graphField = mapMagicType.GetField("graph", BindingFlags.Public | BindingFlags.Instance);
                    if (graphField != null)
                    {
                        graphField.SetValue(mapMagic2Instance, biomeGraph);
                        Debug.Log($"[BattleMapGenerator] Switched MapMagic 2 graph to {primaryBattleBiome} graph (via field)");
                    }
                    else
                    {
                        Debug.LogWarning($"[BattleMapGenerator] Could not find graph property/field on MapMagic 2. Graph may need to be set manually in Inspector.");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[BattleMapGenerator] No graph found for biome {primaryBattleBiome}! Add it to Biome Graphs array in Inspector. Using default MapMagic graph.");
            }
            
            // Set biome/elevation parameters if MapMagic 2 supports it
            SetMapMagic2BiomeParameters(mapMagicType, mapMagic2Instance);
            
            // Set terrain settings (vegetation, wind, detail density) based on biome
            SetMapMagic2TerrainSettings(mapMagicType, mapMagic2Instance);
            
            // Set height output settings based on elevation
            SetMapMagic2HeightOutput(mapMagicType, mapMagic2Instance);
            
            // Set tile settings (size, resolution) for battle map
            SetMapMagic2TileSettings(mapMagicType, mapMagic2Instance);
            
            // Set vegetation settings (trees, grass density) based on biome
            SetMapMagic2VegetationSettings(mapMagicType, mapMagic2Instance);
            
            // Set terrain material/texture based on biome
            SetMapMagic2TerrainMaterial(mapMagicType, mapMagic2Instance);
            
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
            // But we should track the terrain for cleanup and decoration placement
            // MapMagic 2 creates Terrain objects, so find all Terrain components
            Terrain[] mapMagicTerrains = mapMagic2Object.GetComponentsInChildren<Terrain>(true);
            foreach (Terrain terrain in mapMagicTerrains)
            {
                if (terrain != null && terrain.gameObject != null)
                {
                    terrainObjects.Add(terrain.gameObject);
                    Debug.Log($"[BattleMapGenerator] Found MapMagic terrain: {terrain.gameObject.name}");
                }
            }
            
            // Also try to find by name (MapMagic might use different naming)
            var terrainObj = mapMagic2Object.transform.Find("Terrain");
            if (terrainObj != null && !terrainObjects.Contains(terrainObj.gameObject))
            {
                terrainObjects.Add(terrainObj.gameObject);
            }
            
            // Find all terrain tiles (MapMagic uses a tile system)
            var tileParent = mapMagic2Object.transform.Find("Tile 0,0");
            if (tileParent != null)
            {
                var mainTerrain = tileParent.Find("Main Terrain");
                if (mainTerrain != null && !terrainObjects.Contains(mainTerrain.gameObject))
                {
                    terrainObjects.Add(mainTerrain.gameObject);
                }
            }
            
            // Apply biome material directly to terrain objects (backup in case ApplyTerrainSettings didn't work)
            // Use coroutine to apply after terrain generation completes
            StartCoroutine(ApplyBiomeMaterialToTerrainsDelayed());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BattleMapGenerator] Error generating terrain with MapMagic 2: {e.Message}");
            Debug.LogError($"[BattleMapGenerator] Stack trace: {e.StackTrace}");
            Debug.LogError($"[BattleMapGenerator] Falling back to simple mesh generation");
            GenerateTerrainMesh(); // Fallback
        }
    }
    
    /// <summary>
    /// Coroutine to apply biome material to terrains after generation completes
    /// </summary>
    private System.Collections.IEnumerator ApplyBiomeMaterialToTerrainsDelayed()
    {
        // Wait a frame for terrain generation to complete
        yield return null;
        
        // Try multiple times in case terrain generation takes longer
        for (int i = 0; i < 5; i++)
        {
            ApplyBiomeMaterialToTerrains();
            
            // Check if we found any terrains
            Terrain[] allTerrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            if (allTerrains != null && allTerrains.Length > 0)
            {
                break; // Terrains found, we're done
            }
            
            // Wait another frame before retrying
            yield return null;
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
    /// ENHANCED: Now sets terrain roughness, noise parameters, and exposed variables
    /// </summary>
    private void SetMapMagic2GraphParameters(object graph)
    {
        try
        {
            var graphType = graph.GetType();
            
            // Try to access exposed variables system
            var exposedProperty = graphType.GetProperty("exposed", BindingFlags.Public | BindingFlags.Instance);
            if (exposedProperty != null)
            {
                var exposed = exposedProperty.GetValue(graph);
                if (exposed != null)
                {
                    SetMapMagic2ExposedVariables(exposed, graph);
                }
            }
            
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
                        
                        // Calculate terrain roughness based on elevation (higher = rougher)
                        float terrainRoughness = CalculateTerrainRoughness();
                        
                        // Look for noise/height generators to set terrain roughness
                        if (typeName.Contains("noise") || typeName.Contains("height") || typeName.Contains("perlin") || typeName.Contains("fractal"))
                        {
                            // Set elevation/intensity parameters
                            SetGeneratorProperty(generator, genType, "intensity", battleTileElevation);
                            SetGeneratorProperty(generator, genType, "height", battleTileElevation * heightVariation);
                            
                            // Set terrain roughness via frequency/scale parameters
                            SetGeneratorProperty(generator, genType, "frequency", 0.1f / (1f + terrainRoughness)); // Lower frequency = rougher
                            SetGeneratorProperty(generator, genType, "scale", 0.1f * (1f + terrainRoughness)); // Higher scale = rougher
                            
                            // Set noise parameters for terrain variation
                            SetGeneratorProperty(generator, genType, "octaves", Mathf.RoundToInt(3 + terrainRoughness * 2)); // More octaves = rougher
                            SetGeneratorProperty(generator, genType, "lacunarity", 2f + terrainRoughness); // Higher lacunarity = rougher
                            SetGeneratorProperty(generator, genType, "persistence", 0.5f + terrainRoughness * 0.3f); // Higher persistence = rougher
                        }
                        
                        // Look for biome-related generators
                        if (typeName.Contains("biome"))
                        {
                            SetGeneratorProperty(generator, genType, "intensity", battleTileElevation);
                            SetGeneratorProperty(generator, genType, "height", battleTileElevation * heightVariation);
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
    /// Set exposed variables in MapMagic 2 graph (biome, elevation, moisture, temperature)
    /// </summary>
    private void SetMapMagic2ExposedVariables(object exposed, object graph)
    {
        try
        {
            var exposedType = exposed.GetType();
            
            // Try to find SetValue method or indexer
            var setValueMethod = exposedType.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Instance);
            if (setValueMethod == null)
            {
                // Try indexer setter
                var indexerProperty = exposedType.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
                if (indexerProperty != null && indexerProperty.CanWrite)
                {
                    // Exposed variables use id-based lookup, we'd need generator IDs
                    // For now, we'll try to set via graph generators
                    return;
                }
            }
            
            // If we have a graph, try to find generators and set their exposed values
            var graphType = graph.GetType();
            var generatorsProperty = graphType.GetProperty("generators", BindingFlags.Public | BindingFlags.Instance);
            if (generatorsProperty != null)
            {
                var generators = generatorsProperty.GetValue(graph);
                if (generators is System.Collections.IEnumerable genEnumerable)
                {
                    foreach (var generator in genEnumerable)
                    {
                        if (generator == null) continue;
                        
                        var genType = generator.GetType();
                        var idProperty = genType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                        if (idProperty == null)
                        {
                            idProperty = genType.GetProperty("id", BindingFlags.Public | BindingFlags.Instance);
                        }
                        
                        if (idProperty != null)
                        {
                            var genId = idProperty.GetValue(generator);
                            
                            // Try to set exposed variables for this generator
                            // Common exposed variable names: "biome", "elevation", "moisture", "temperature"
                            TrySetExposedVariable(exposed, exposedType, genId, "biome", primaryBattleBiome);
                            TrySetExposedVariable(exposed, exposedType, genId, "elevation", battleTileElevation);
                            TrySetExposedVariable(exposed, exposedType, genId, "moisture", battleTileMoisture);
                            TrySetExposedVariable(exposed, exposedType, genId, "temperature", battleTileTemperature);
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not set MapMagic 2 exposed variables: {e.Message}");
        }
    }
    
    /// <summary>
    /// Helper to try setting an exposed variable
    /// </summary>
    private void TrySetExposedVariable(object exposed, System.Type exposedType, object genId, string varName, object value)
    {
        try
        {
            // Try GetExpression/SetExpression pattern
            var getExpressionMethod = exposedType.GetMethod("GetExpression", BindingFlags.Public | BindingFlags.Instance);
            var setExpressionMethod = exposedType.GetMethod("SetExpression", BindingFlags.Public | BindingFlags.Instance);
            
            if (getExpressionMethod != null && setExpressionMethod != null)
            {
                // Check if variable exists
                var expression = getExpressionMethod.Invoke(exposed, new object[] { genId, varName });
                if (expression != null || true) // Try to set even if not found (might create it)
                {
                    // Convert value to string expression
                    string expressionValue = value.ToString();
                    if (value is float f) expressionValue = f.ToString("F3");
                    else if (value is Biome b) expressionValue = ((int)b).ToString();
                    
                    setExpressionMethod.Invoke(exposed, new object[] { genId, varName, expressionValue });
                }
            }
        }
        catch
        {
            // Silently fail - exposed variable might not exist or use different API
        }
    }
    
    /// <summary>
    /// Helper to set a property on a generator via reflection
    /// </summary>
    private void SetGeneratorProperty(object generator, System.Type genType, string propertyName, float value)
    {
        try
        {
            var property = genType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == typeof(float))
            {
                property.SetValue(generator, value);
            }
            else
            {
                // Try field instead
                var field = genType.GetField(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(float))
                {
                    field.SetValue(generator, value);
                }
            }
        }
        catch
        {
            // Property might not exist on this generator type - that's okay
        }
    }
    
    /// <summary>
    /// Calculate terrain roughness based on elevation (higher elevation = rougher terrain)
    /// </summary>
    private float CalculateTerrainRoughness()
    {
        // Base roughness from elevation (0-1 range)
        float baseRoughness = battleTileElevation;
        
        // Biome-specific roughness modifiers
        float biomeModifier = primaryBattleBiome switch
        {
            Biome.Mountain => 1.5f,      // Mountains are very rough
            Biome.Volcanic => 1.3f,       // Volcanic terrain is rough
            Biome.Hellscape => 1.2f,      // Hellscape is rough
            Biome.Brimstone => 1.1f,      // Brimstone is rough
            Biome.Forest => 0.3f,         // Forests are moderately rough
            Biome.Jungle => 0.4f,         // Jungles are moderately rough
            Biome.Swamp => 0.2f,          // Swamps are less rough (flat)
            Biome.Plains => 0.1f,         // Plains are smooth
            Biome.Desert => 0.15f,        // Deserts are mostly smooth
            Biome.Ocean => 0.05f,         // Ocean is very smooth
            _ => 0.2f                     // Default moderate roughness
        };
        
        return Mathf.Clamp01(baseRoughness * biomeModifier);
    }
    
    /// <summary>
    /// Set MapMagic 2 terrain settings (vegetation, wind, detail density) based on biome
    /// </summary>
    private void SetMapMagic2TerrainSettings(System.Type mapMagicType, object mapMagicInstance)
    {
        try
        {
            // Get terrainSettings property
            var terrainSettingsProperty = mapMagicType.GetProperty("terrainSettings", BindingFlags.Public | BindingFlags.Instance);
            if (terrainSettingsProperty == null)
            {
                terrainSettingsProperty = mapMagicType.GetProperty("TerrainSettings", BindingFlags.Public | BindingFlags.Instance);
            }
            
            if (terrainSettingsProperty == null)
            {
                Debug.LogWarning("[BattleMapGenerator] TerrainSettings property not found on MapMagic 2");
                return;
            }
            
            var terrainSettings = terrainSettingsProperty.GetValue(mapMagicInstance);
            if (terrainSettings == null)
            {
                Debug.LogWarning("[BattleMapGenerator] TerrainSettings is null");
                return;
            }
            
            var settingsType = terrainSettings.GetType();
            
            // Calculate biome-specific values
            float detailDensity = CalculateDetailDensity();
            float detailDistance = CalculateDetailDistance();
            float windSpeed = CalculateWindSpeed();
            float windBending = CalculateWindBending();
            float windSize = CalculateWindSize();
            Color grassTint = CalculateGrassTint();
            
            // Set detail/grass settings
            SetPropertyOrField(terrainSettings, settingsType, "detailDensity", detailDensity);
            SetPropertyOrField(terrainSettings, settingsType, "detailDistance", detailDistance);
            SetPropertyOrField(terrainSettings, settingsType, "detailDraw", detailDensity > 0.1f); // Only draw if density is significant
            
            // Set wind settings
            SetPropertyOrField(terrainSettings, settingsType, "windSpeed", windSpeed);
            SetPropertyOrField(terrainSettings, settingsType, "windBending", windBending);
            SetPropertyOrField(terrainSettings, settingsType, "windSize", windSize);
            SetPropertyOrField(terrainSettings, settingsType, "grassTint", grassTint);
            
            // Apply settings to existing terrains
            var applyMethod = mapMagicType.GetMethod("ApplyTerrainSettings", BindingFlags.Public | BindingFlags.Instance);
            if (applyMethod != null)
            {
                applyMethod.Invoke(mapMagicInstance, null);
            }
            
            Debug.Log($"[BattleMapGenerator] Set terrain settings: detailDensity={detailDensity:F2}, detailDistance={detailDistance:F1}, windSpeed={windSpeed:F2}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not set MapMagic 2 terrain settings: {e.Message}");
        }
    }
    
    /// <summary>
    /// Set MapMagic 2 height output settings based on elevation
    /// </summary>
    private void SetMapMagic2HeightOutput(System.Type mapMagicType, object mapMagicInstance)
    {
        try
        {
            // Get globals property (contains height output settings)
            var globalsProperty = mapMagicType.GetProperty("globals", BindingFlags.Public | BindingFlags.Instance);
            if (globalsProperty == null)
            {
                globalsProperty = mapMagicType.GetProperty("Globals", BindingFlags.Public | BindingFlags.Instance);
            }
            
            if (globalsProperty == null)
            {
                Debug.LogWarning("[BattleMapGenerator] Globals property not found on MapMagic 2");
                return;
            }
            
            var globals = globalsProperty.GetValue(mapMagicInstance);
            if (globals == null)
            {
                Debug.LogWarning("[BattleMapGenerator] Globals is null");
                return;
            }
            
            var globalsType = globals.GetType();
            
            // Set height output based on elevation
            float heightValue = battleTileElevation * heightVariation;
            SetPropertyOrField(globals, globalsType, "height", heightValue);
            
            // Set interpolation (None = no interpolation, Linear = smooth interpolation)
            // Use None for battle maps to preserve terrain features
            var interpolationType = System.Type.GetType("MapMagic.Nodes.MatrixGenerators.HeightOutput200+Interpolation, Assembly-CSharp");
            if (interpolationType != null)
            {
                var noneValue = System.Enum.Parse(interpolationType, "None");
                SetPropertyOrField(globals, globalsType, "heightInterpolation", noneValue);
            }
            
            Debug.Log($"[BattleMapGenerator] Set height output: height={heightValue:F2}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not set MapMagic 2 height output: {e.Message}");
        }
    }
    
    /// <summary>
    /// Set MapMagic 2 tile settings (size, resolution) for battle map
    /// </summary>
    private void SetMapMagic2TileSettings(System.Type mapMagicType, object mapMagicInstance)
    {
        try
        {
            // Set tile size to match battle map size
            // MapMagic uses Vector2D for tileSize, but we'll try Vector2 or Vector2D
            var tileSizeProperty = mapMagicType.GetProperty("tileSize", BindingFlags.Public | BindingFlags.Instance);
            if (tileSizeProperty != null)
            {
                var tileSizeValue = tileSizeProperty.GetValue(mapMagicInstance);
                if (tileSizeValue != null)
                {
                    var tileSizeType = tileSizeValue.GetType();
                    
                    // Try to set x and z components
                    var xProperty = tileSizeType.GetProperty("x", BindingFlags.Public | BindingFlags.Instance);
                    var zProperty = tileSizeType.GetProperty("z", BindingFlags.Public | BindingFlags.Instance);
                    
                    if (xProperty != null && zProperty != null)
                    {
                        xProperty.SetValue(tileSizeValue, mapSize);
                        zProperty.SetValue(tileSizeValue, mapSize);
                    }
                    else
                    {
                        // Try Vector2D structure (might need different approach)
                        var xField = tileSizeType.GetField("x", BindingFlags.Public | BindingFlags.Instance);
                        var zField = tileSizeType.GetField("z", BindingFlags.Public | BindingFlags.Instance);
                        if (xField != null && zField != null)
                        {
                            xField.SetValue(tileSizeValue, mapSize);
                            zField.SetValue(tileSizeValue, mapSize);
                        }
                    }
                }
            }
            
            // Set resolution based on map size (larger maps need higher resolution)
            // Resolution enum: _33, _65, _129, _257, _513, _1025, _2049
            int targetResolution = CalculateOptimalResolution();
            var resolutionType = System.Type.GetType("MapMagic.Core.MapMagicObject+Resolution, Assembly-CSharp");
            if (resolutionType != null)
            {
                // Find closest resolution enum value
                var resolutionValues = System.Enum.GetValues(resolutionType);
                int closestResolution = 513; // Default
                int minDiff = int.MaxValue;
                
                foreach (var resValue in resolutionValues)
                {
                    int resInt = (int)resValue;
                    int diff = Mathf.Abs(resInt - targetResolution);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        closestResolution = resInt;
                    }
                }
                
                var resolutionEnum = System.Enum.Parse(resolutionType, $"_{closestResolution}");
                SetPropertyOrField(mapMagicInstance, mapMagicType, "tileResolution", resolutionEnum);
                
                Debug.Log($"[BattleMapGenerator] Set tile resolution to {closestResolution} (target was {targetResolution})");
            }
            
            // Apply tile settings
            var applyMethod = mapMagicType.GetMethod("ApplyTileSettings", BindingFlags.Public | BindingFlags.Instance);
            if (applyMethod != null)
            {
                applyMethod.Invoke(mapMagicInstance, null);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not set MapMagic 2 tile settings: {e.Message}");
        }
    }
    
    /// <summary>
    /// Set MapMagic 2 vegetation settings (trees, grass density) based on biome
    /// </summary>
    private void SetMapMagic2VegetationSettings(System.Type mapMagicType, object mapMagicInstance)
    {
        try
        {
            // Get terrainSettings property
            var terrainSettingsProperty = mapMagicType.GetProperty("terrainSettings", BindingFlags.Public | BindingFlags.Instance);
            if (terrainSettingsProperty == null) return;
            
            var terrainSettings = terrainSettingsProperty.GetValue(mapMagicInstance);
            if (terrainSettings == null) return;
            
            var settingsType = terrainSettings.GetType();
            
            // Calculate biome-specific vegetation values
            float treeDistance = CalculateTreeDistance();
            float treeBillboardStart = CalculateTreeBillboardStart();
            int treeFullLod = CalculateTreeFullLod();
            
            // Set tree settings
            SetPropertyOrField(terrainSettings, settingsType, "treeDistance", treeDistance);
            SetPropertyOrField(terrainSettings, settingsType, "treeBillboardStart", treeBillboardStart);
            SetPropertyOrField(terrainSettings, settingsType, "treeFullLod", treeFullLod);
            SetPropertyOrField(terrainSettings, settingsType, "treeFadeLength", 5f); // Standard fade length
            
            // Apply settings
            var applyMethod = mapMagicType.GetMethod("ApplyTerrainSettings", BindingFlags.Public | BindingFlags.Instance);
            if (applyMethod != null)
            {
                applyMethod.Invoke(mapMagicInstance, null);
            }
            
            Debug.Log($"[BattleMapGenerator] Set vegetation settings: treeDistance={treeDistance:F1}, treeFullLod={treeFullLod}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not set MapMagic 2 vegetation settings: {e.Message}");
        }
    }
    
    /// <summary>
    /// Set MapMagic 2 terrain material/texture based on biome
    /// Creates a material with biome-specific textures and applies it to MapMagic terrain
    /// </summary>
    private void SetMapMagic2TerrainMaterial(System.Type mapMagicType, object mapMagicInstance)
    {
        try
        {
            // Get terrainSettings property
            var terrainSettingsProperty = mapMagicType.GetProperty("terrainSettings", BindingFlags.Public | BindingFlags.Instance);
            if (terrainSettingsProperty == null)
            {
                Debug.LogWarning("[BattleMapGenerator] TerrainSettings property not found for material setup");
                return;
            }
            
            var terrainSettings = terrainSettingsProperty.GetValue(mapMagicInstance);
            if (terrainSettings == null)
            {
                Debug.LogWarning("[BattleMapGenerator] TerrainSettings is null for material setup");
                return;
            }
            
            var settingsType = terrainSettings.GetType();
            
            // Create or get biome-specific material
            Material biomeMaterial = CreateBiomeTerrainMaterial();
            
            if (biomeMaterial != null)
            {
                // Set material on terrainSettings
                SetPropertyOrField(terrainSettings, settingsType, "material", biomeMaterial);
                
                // Apply terrain settings (this will apply the material to all terrains)
                var applyMethod = mapMagicType.GetMethod("ApplyTerrainSettings", BindingFlags.Public | BindingFlags.Instance);
                if (applyMethod != null)
                {
                    applyMethod.Invoke(mapMagicInstance, null);
                }
                
                bool hasBaseMap = biomeMaterial.GetTexture("_BaseMap") != null;
                bool hasMainTex = biomeMaterial.GetTexture("_MainTex") != null;
                Debug.Log($"[BattleMapGenerator] Set terrain material for biome {primaryBattleBiome} (has texture: {hasBaseMap || hasMainTex})");
            }
            else
            {
                Debug.LogWarning($"[BattleMapGenerator] Could not create terrain material for biome {primaryBattleBiome}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not set MapMagic 2 terrain material: {e.Message}");
            Debug.LogWarning($"[BattleMapGenerator] Stack trace: {e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Create a terrain material with biome-specific textures
    /// Uses textures from biomeSettings if available, otherwise uses biome color
    /// </summary>
    private Material CreateBiomeTerrainMaterial()
    {
        // Find appropriate shader (URP or Standard)
        Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (terrainShader == null)
        {
            terrainShader = Shader.Find("Nature/Terrain/Standard");
        }
        if (terrainShader == null)
        {
            terrainShader = Shader.Find("Standard");
        }
        
        if (terrainShader == null)
        {
            Debug.LogError("[BattleMapGenerator] Could not find terrain shader!");
            return null;
        }
        
        Material material = new Material(terrainShader);
        
        // Try to get biome settings for textures
        if (biomeSettingsLookup.TryGetValue(primaryBattleBiome, out BiomeSettings settings))
        {
            // Apply albedo texture if available
            if (settings.albedoTexture != null)
            {
                // Try URP property first
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", settings.albedoTexture);
                }
                // Fallback to Standard property
                else if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", settings.albedoTexture);
                }
                // Last resort: mainTexture
                else
                {
                    material.mainTexture = settings.albedoTexture;
                }
                
                Debug.Log($"[BattleMapGenerator] Applied albedo texture to terrain material: {settings.albedoTexture.name}");
            }
            else
            {
                // No texture - use biome color
                Color biomeColor = GetBiomeColor(primaryBattleBiome);
                
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", biomeColor);
                }
                else if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", biomeColor);
                }
                else
                {
                    material.color = biomeColor;
                }
                
                Debug.Log($"[BattleMapGenerator] Applied biome color to terrain material (no texture available): {biomeColor}");
            }
            
            // Apply normal texture if available
            if (settings.normalTexture != null)
            {
                if (material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", settings.normalTexture);
                    material.EnableKeyword("_NORMALMAP");
                }
                else if (material.HasProperty("_NormalMap"))
                {
                    material.SetTexture("_NormalMap", settings.normalTexture);
                    material.EnableKeyword("_NORMALMAP");
                }
                
                Debug.Log($"[BattleMapGenerator] Applied normal texture to terrain material: {settings.normalTexture.name}");
            }
        }
        else
        {
            // No biome settings - use fallback color
            Color biomeColor = GetBiomeColor(primaryBattleBiome);
            
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", biomeColor);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", biomeColor);
            }
            else
            {
                material.color = biomeColor;
            }
            
            Debug.LogWarning($"[BattleMapGenerator] No biome settings found for {primaryBattleBiome}! Using fallback color. Add biome settings or use BattleMapBiomeSetup to copy from PlanetGenerator.");
        }
        
        // Set material properties
        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0.0f);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.5f);
        }
        
        return material;
    }
    
    /// <summary>
    /// Apply biome material directly to all terrain objects (backup method)
    /// This ensures materials are applied even if MapMagic's ApplyTerrainSettings didn't work
    /// </summary>
    private void ApplyBiomeMaterialToTerrains()
    {
        try
        {
            Material biomeMaterial = CreateBiomeTerrainMaterial();
            if (biomeMaterial == null)
            {
                Debug.LogWarning("[BattleMapGenerator] Could not create biome material for direct application");
                return;
            }
            
            // Find all Terrain objects in the scene (MapMagic creates these)
            Terrain[] allTerrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            int appliedCount = 0;
            
            foreach (Terrain terrain in allTerrains)
            {
                if (terrain == null || terrain.materialTemplate != null) continue; // Skip if already has material
                
                // Check if this terrain is part of our battle map (within bounds)
                Vector3 terrainPos = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData != null ? terrain.terrainData.size : Vector3.zero;
                
                // Check if terrain is within our map bounds
                bool isInBounds = terrainPos.x >= -mapSize / 2f && terrainPos.x <= mapSize / 2f &&
                                 terrainPos.z >= -mapSize / 2f && terrainPos.z <= mapSize / 2f;
                
                if (isInBounds || terrainObjects.Contains(terrain.gameObject))
                {
                    terrain.materialTemplate = biomeMaterial;
                    appliedCount++;
                    Debug.Log($"[BattleMapGenerator] Applied biome material to terrain: {terrain.gameObject.name}");
                }
            }
            
            if (appliedCount > 0)
            {
                Debug.Log($"[BattleMapGenerator] Applied biome material to {appliedCount} terrain object(s)");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BattleMapGenerator] Could not apply biome material to terrains: {e.Message}");
        }
    }
    
    /// <summary>
    /// Helper to set property or field via reflection
    /// </summary>
    private void SetPropertyOrField(object target, System.Type targetType, string name, object value)
    {
        try
        {
            // Try property first
            var property = targetType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                // Check type compatibility
                if (value != null && property.PropertyType.IsAssignableFrom(value.GetType()))
                {
                    property.SetValue(target, value);
                    return;
                }
                else if (value != null && property.PropertyType == typeof(float) && value is float f)
                {
                    property.SetValue(target, f);
                    return;
                }
                else if (value != null && property.PropertyType == typeof(int) && value is int i)
                {
                    property.SetValue(target, i);
                    return;
                }
                else if (value != null && property.PropertyType == typeof(bool) && value is bool b)
                {
                    property.SetValue(target, b);
                    return;
                }
                else if (value != null && property.PropertyType == typeof(Color) && value is Color c)
                {
                    property.SetValue(target, c);
                    return;
                }
            }
            
            // Try field
            var field = targetType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                if (value != null && field.FieldType.IsAssignableFrom(value.GetType()))
                {
                    field.SetValue(target, value);
                    return;
                }
                else if (value != null && field.FieldType == typeof(float) && value is float f)
                {
                    field.SetValue(target, f);
                    return;
                }
                else if (value != null && field.FieldType == typeof(int) && value is int i)
                {
                    field.SetValue(target, i);
                    return;
                }
                else if (value != null && field.FieldType == typeof(bool) && value is bool b)
                {
                    field.SetValue(target, b);
                    return;
                }
                else if (value != null && field.FieldType == typeof(Color) && value is Color c)
                {
                    field.SetValue(target, c);
                    return;
                }
            }
        }
        catch
        {
            // Silently fail - property/field might not exist or be wrong type
        }
    }
    
    // ========== BIOME-SPECIFIC CALCULATION METHODS ==========
    
    /// <summary>
    /// Calculate detail density based on biome (forests = high, deserts = low)
    /// </summary>
    private float CalculateDetailDensity()
    {
        float baseDensity = primaryBattleBiome switch
        {
            Biome.Forest => 1.5f,
            Biome.Jungle => 1.8f,
            Biome.Rainforest => 2.0f,
            Biome.Grassland => 1.2f,
            Biome.Plains => 0.8f,
            Biome.Savannah => 0.6f,
            Biome.Taiga => 1.0f,
            Biome.Swamp => 1.3f,
            Biome.Marsh => 1.4f,
            Biome.Desert => 0.2f,
            Biome.Mountain => 0.3f,
            Biome.Volcanic => 0.1f,
            Biome.Scorched => 0.05f,
            Biome.Hellscape => 0.1f,
            Biome.Brimstone => 0.05f,
            Biome.Ocean => 0.0f,
            Biome.Snow => 0.4f,
            Biome.Tundra => 0.5f,
            Biome.Frozen => 0.3f,
            _ => 0.7f
        };
        
        // Modify by moisture (wetter = more vegetation)
        return Mathf.Clamp(baseDensity * (0.5f + battleTileMoisture), 0f, 2.5f);
    }
    
    /// <summary>
    /// Calculate detail distance based on biome
    /// </summary>
    private float CalculateDetailDistance()
    {
        return primaryBattleBiome switch
        {
            Biome.Forest => 100f,
            Biome.Jungle => 120f,
            Biome.Rainforest => 150f,
            Biome.Grassland => 90f,
            Biome.Plains => 80f,
            Biome.Desert => 60f,
            Biome.Mountain => 50f,
            _ => 80f
        };
    }
    
    /// <summary>
    /// Calculate wind speed based on biome
    /// </summary>
    private float CalculateWindSpeed()
    {
        return primaryBattleBiome switch
        {
            Biome.Desert => 0.8f,      // Windy deserts
            Biome.Savannah => 0.7f,    // Windy savannah
            Biome.Plains => 0.6f,      // Windy plains
            Biome.Mountain => 0.9f,    // Windy mountains
            Biome.Forest => 0.4f,      // Calmer forests
            Biome.Jungle => 0.3f,      // Calm jungles
            Biome.Swamp => 0.2f,       // Very calm swamps
            _ => 0.5f
        };
    }
    
    /// <summary>
    /// Calculate wind bending based on biome
    /// </summary>
    private float CalculateWindBending()
    {
        return primaryBattleBiome switch
        {
            Biome.Forest => 0.6f,      // Trees bend more
            Biome.Jungle => 0.7f,      // Dense vegetation bends
            Biome.Grassland => 0.5f,   // Grass bends
            Biome.Plains => 0.4f,      // Less bending on plains
            Biome.Desert => 0.3f,      // Minimal bending (sparse vegetation)
            _ => 0.5f
        };
    }
    
    /// <summary>
    /// Calculate wind size based on biome
    /// </summary>
    private float CalculateWindSize()
    {
        return primaryBattleBiome switch
        {
            Biome.Forest => 0.6f,
            Biome.Jungle => 0.7f,
            Biome.Grassland => 0.5f,
            _ => 0.5f
        };
    }
    
    /// <summary>
    /// Calculate grass tint color based on biome
    /// </summary>
    private Color CalculateGrassTint()
    {
        return primaryBattleBiome switch
        {
            Biome.Forest => new Color(0.7f, 0.8f, 0.6f),        // Green forest
            Biome.Jungle => new Color(0.6f, 0.9f, 0.5f),       // Vibrant green jungle
            Biome.Rainforest => new Color(0.5f, 0.8f, 0.4f),    // Deep green rainforest
            Biome.Grassland => new Color(0.8f, 0.9f, 0.7f),     // Bright green grassland
            Biome.Plains => new Color(0.85f, 0.85f, 0.7f),      // Yellow-green plains
            Biome.Savannah => new Color(0.9f, 0.8f, 0.6f),      // Yellow savannah
            Biome.Desert => new Color(0.95f, 0.9f, 0.7f),       // Beige desert
            Biome.Swamp => new Color(0.6f, 0.7f, 0.5f),         // Dark green swamp
            Biome.Marsh => new Color(0.65f, 0.75f, 0.55f),      // Dark green marsh
            Biome.Taiga => new Color(0.7f, 0.75f, 0.65f),       // Blue-green taiga
            Biome.Snow => new Color(0.9f, 0.9f, 0.95f),        // White-blue snow
            Biome.Tundra => new Color(0.8f, 0.85f, 0.75f),      // Pale tundra
            _ => Color.gray
        };
    }
    
    /// <summary>
    /// Calculate tree distance based on biome
    /// </summary>
    private float CalculateTreeDistance()
    {
        return primaryBattleBiome switch
        {
            Biome.Forest => 1500f,
            Biome.Jungle => 1800f,
            Biome.Rainforest => 2000f,
            Biome.Taiga => 1200f,
            Biome.Plains => 800f,
            Biome.Desert => 500f,
            Biome.Mountain => 1000f,
            _ => 1000f
        };
    }
    
    /// <summary>
    /// Calculate tree billboard start distance based on biome
    /// </summary>
    private float CalculateTreeBillboardStart()
    {
        return primaryBattleBiome switch
        {
            Biome.Forest => 250f,
            Biome.Jungle => 300f,
            Biome.Rainforest => 350f,
            Biome.Taiga => 200f,
            _ => 200f
        };
    }
    
    /// <summary>
    /// Calculate maximum full LOD trees based on biome
    /// </summary>
    private int CalculateTreeFullLod()
    {
        return primaryBattleBiome switch
        {
            Biome.Forest => 200,
            Biome.Jungle => 250,
            Biome.Rainforest => 300,
            Biome.Taiga => 150,
            Biome.Plains => 100,
            Biome.Desert => 50,
            _ => 150
        };
    }
    
    /// <summary>
    /// Calculate optimal terrain resolution based on map size
    /// </summary>
    private int CalculateOptimalResolution()
    {
        // Larger maps need higher resolution for detail
        // Resolution options: 33, 65, 129, 257, 513, 1025, 2049
        if (mapSize <= 50f) return 257;      // Small maps: 257
        if (mapSize <= 100f) return 513;     // Medium maps: 513
        if (mapSize <= 200f) return 1025;    // Large maps: 1025
        return 2049;                          // Very large maps: 2049
    }
    
    /// <summary>
    /// Helper method to find object by type using reflection
    /// </summary>
    private new Component FindFirstObjectByType(System.Type type)
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
    /// ENHANCED: Works with both MapMagic 2 terrain and fallback mesh generation
    /// Uses raycasting to place decorations on terrain surface
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
        
        Debug.Log($"[BattleMapGenerator] Placing {decorationCount} decorations for biome {primaryBattleBiome}");
        
        int successfulPlacements = 0;
        int maxAttempts = decorationCount * 3; // Try up to 3x to find valid positions
        int attempts = 0;
        
        while (successfulPlacements < decorationCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Random position on the map
            Vector3 position = new Vector3(
                Random.Range(-mapSize / 2f, mapSize / 2f),
                0f,
                Random.Range(-mapSize / 2f, mapSize / 2f)
            );
            
            // Get terrain height using raycasting (works with MapMagic terrain and fallback mesh)
            float terrainHeight = GetTerrainHeightAtPosition(position);
            
            // Skip if we couldn't find terrain (shouldn't happen, but safety check)
            if (terrainHeight == float.MinValue)
            {
                continue;
            }
            
            position.y = terrainHeight;
            
            // Use the primary biome from defender's tile (no variation)
            Biome biome = primaryBattleBiome;
            
            // Spawn decoration if appropriate
            if (ShouldSpawnObstacle(biome))
            {
                if (SpawnBiomeDecoration(position, biome))
                {
                    successfulPlacements++;
                }
            }
        }
        
        Debug.Log($"[BattleMapGenerator] Successfully placed {successfulPlacements} decorations (attempted {attempts} times)");
    }
    
    /// <summary>
    /// Get terrain height at a position using raycasting (works with MapMagic terrain and fallback mesh)
    /// Returns float.MinValue if terrain not found
    /// </summary>
    private float GetTerrainHeightAtPosition(Vector3 position)
    {
        // First, try to find Unity Terrain objects (MapMagic 2 uses these)
        Terrain[] terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        
        if (terrains != null && terrains.Length > 0)
        {
            // Try each terrain to see if position is within bounds
            foreach (Terrain terrain in terrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                
                Vector3 terrainPos = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData.size;
                
                // Check if position is within terrain bounds
                if (position.x >= terrainPos.x && position.x <= terrainPos.x + terrainSize.x &&
                    position.z >= terrainPos.z && position.z <= terrainPos.z + terrainSize.z)
                {
                    // Use Terrain.SampleHeight for accurate height
                    float height = terrain.SampleHeight(new Vector3(position.x, terrainPos.y + terrainSize.y, position.z));
                    return terrainPos.y + height;
                }
            }
        }
        
        // Fallback: Use raycasting to find any collider (works with fallback mesh)
        RaycastHit hit;
        Vector3 rayStart = new Vector3(position.x, position.y + 100f, position.z); // Start high above
        Vector3 rayDirection = Vector3.down;
        
        // Use battlefield layers if configured, otherwise check all layers
        int layerMask = battlefieldLayers != 0 ? battlefieldLayers : ~0;
        
        if (Physics.Raycast(rayStart, rayDirection, out hit, 200f, layerMask))
        {
            return hit.point.y;
        }
        
        // Last resort: Use CalculateHeightAtPosition for fallback mesh (if we're not using MapMagic)
        if (!useMapMagic2 || !mapMagic2Available)
        {
            return CalculateHeightAtPosition(position.x, position.z);
        }
        
        // Couldn't find terrain
        return float.MinValue;
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
    /// ENHANCED: Returns bool to indicate success, ensures decorations are placed on terrain surface
    /// </summary>
    private bool SpawnBiomeDecoration(Vector3 position, Biome biome)
    {
        // Try to get biome settings for decorations
        if (biomeSettingsLookup.TryGetValue(biome, out BiomeSettings settings) && 
            settings.decorations != null && settings.decorations.Length > 0)
        {
            // Use actual decoration prefabs from biome settings
            GameObject decorationPrefab = settings.decorations[Random.Range(0, settings.decorations.Length)];
            if (decorationPrefab != null)
            {
                // Ensure position is on terrain surface (raycast to be sure)
                float terrainHeight = GetTerrainHeightAtPosition(position);
                if (terrainHeight == float.MinValue)
                {
                    return false; // Couldn't find terrain
                }
                
                position.y = terrainHeight;
                
                // Random rotation for variety
                Quaternion rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                
                GameObject decoration = Instantiate(decorationPrefab, position, rotation);
                decoration.transform.SetParent(transform);
                decoration.name = $"Decoration_{biome}_{decorationPrefab.name}_{spawnedObjects.Count}";
                
                // Ensure decoration is on terrain surface (adjust if needed)
                AlignDecorationToTerrain(decoration);
                
                spawnedObjects.Add(decoration);
                return true;
            }
        }
        
        // Fallback: create simple colored cubes as placeholders (only if no biome settings)
        if (biomeSettingsLookup.Count == 0)
        {
            float terrainHeight = GetTerrainHeightAtPosition(position);
            if (terrainHeight == float.MinValue)
            {
                return false;
            }
            
            position.y = terrainHeight;
            
            GameObject fallbackDecoration = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallbackDecoration.transform.position = position + Vector3.up * 0.5f;
            fallbackDecoration.transform.localScale = Vector3.one * 0.5f;
            fallbackDecoration.transform.SetParent(transform);
            fallbackDecoration.name = $"Decoration_{biome}_{spawnedObjects.Count}";
            
            // Color based on biome
            Renderer fallbackRenderer = fallbackDecoration.GetComponent<Renderer>();
            if (fallbackRenderer != null)
            {
                fallbackRenderer.material.color = GetBiomeColor(biome) * 0.7f;
            }
            
            spawnedObjects.Add(fallbackDecoration);
            return true;
        }
        
        return false; // No decorations available for this biome
    }
    
    /// <summary>
    /// Align decoration to terrain surface (ensures it's sitting properly on terrain)
    /// </summary>
    private void AlignDecorationToTerrain(GameObject decoration)
    {
        if (decoration == null) return;
        
        // Get bounds of decoration to find bottom point
        Renderer renderer = decoration.GetComponent<Renderer>();
        if (renderer != null && renderer.bounds.size.y > 0.1f)
        {
            Vector3 bottomPoint = decoration.transform.position;
            bottomPoint.y = renderer.bounds.min.y;
            
            // Raycast down from decoration to find terrain
            RaycastHit hit;
            if (Physics.Raycast(bottomPoint + Vector3.up * 0.5f, Vector3.down, out hit, 2f, battlefieldLayers != 0 ? battlefieldLayers : ~0))
            {
                // Adjust position so bottom of decoration is on terrain
                float heightOffset = decoration.transform.position.y - bottomPoint.y;
                decoration.transform.position = new Vector3(
                    decoration.transform.position.x,
                    hit.point.y + heightOffset,
                    decoration.transform.position.z
                );
            }
        }
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

