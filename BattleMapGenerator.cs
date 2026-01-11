using UnityEngine;
using UnityEngine.AI; // For NavMesh runtime baking
using System.Collections.Generic;
using System.Linq;
#if VISTA
using Pinwheel.Vista.Graph;
using Pinwheel.Vista;
using Pinwheel.Vista.UnityTerrain;
#endif

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
    [Tooltip("Height variation for terrain")]
    public float heightVariation = 3f;
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
    
    [Tooltip("Biome settings for textures and materials")]
    public BiomeSettings[] biomeSettings = new BiomeSettings[0];
    
    // Legacy property for backward compatibility (uses primaryBattleBiome)
    public Biome primaryBiome => primaryBattleBiome;

    [Header("Obstacles & Decorations")]
    [Tooltip("Density of obstacles (trees, rocks, etc.)")]
    [Range(0f, 1f)]
    public float obstacleDensity = 0.15f;
    [Tooltip("Maximum number of decorations to spawn on the battle map (0 = unlimited, but may cause memory issues)")]
    [Range(0, 200)]
    public int maxDecorations = 50;
    
    // NOTE: Grass is rendered using GPUInstancedGrass component for high performance.
    // This replaces Unity's Terrain Detail system which was unreliable.
    // GPUInstancedGrass uses Graphics.DrawMeshInstanced for thousands of grass blades with minimal draw calls.
    
    [Header("Clouds")]
    [Tooltip("Optional cloud spawner for atmospheric clouds. Assign in inspector if you want clouds.")]
    public BattlefieldClouds cloudSpawner;
    
    [Header("Ambient Particles")]
    [Tooltip("Optional particle spawner for atmospheric particles (dust, ash, pollen, etc). Assign in inspector if you want ambient particles.")]
    public BattlefieldAmbientParticles ambientParticles;
    
    [Header("GPU Instanced Grass")]
    [Tooltip("Number of grass instances to spawn. Higher = denser grass but more GPU cost.")]
    [Range(5000, 50000)]
    public int grassInstanceCount = 20000;
    
    [Tooltip("Grass density multiplier. 1.0 = biome default, 2.0 = double density, 0.5 = half density")]
    [Range(0.1f, 3f)]
    public float grassDensityMultiplier = 1.5f;
    
    [Tooltip("Grass render distance in meters.")]
    [Range(50f, 500f)]
    public float grassRenderDistance = 150f;
    
    [Tooltip("Wind strength for grass animation")]
    [Range(0f, 2f)]
    public float grassWindStrength = 0.5f;
    
    // GPU grass system reference
    private GPUInstancedGrass gpuGrass;
    
    [Header("Atmosphere (HDRP-like Effects)")]
    [Tooltip("Optional atmosphere system for volumetric fog, light shafts, and atmospheric scattering. Assign in inspector for cinematic visuals.")]
    public BattlefieldAtmosphere atmosphere;

#if VISTA
    [Header("Vista Pro Terrain (Optional)")]
    [Tooltip("Optional Vista TerrainGraph used to generate battle terrains instead of the custom system.")]
    public TerrainGraph runtimeGraph;
#endif

    [Header("Spawn Points")]
    [Tooltip("Distance between attacker and defender spawn points")]
    public float spawnDistance = 30f;

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
    private Vector3 mapCenter;
    private List<Vector3> attackerSpawnPoints = new List<Vector3>();
    private List<Vector3> defenderSpawnPoints = new List<Vector3>();
    private List<GameObject> spawnedObjects = new List<GameObject>();
    private Dictionary<Biome, BiomeSettings> biomeSettingsLookup = new Dictionary<Biome, BiomeSettings>();

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
    /// Uses custom Unity Terrain API generation
    /// </summary>
    public void GenerateBattleMap(float mapSize, int attackerUnits, int defenderUnits)
    {
this.mapSize = mapSize;
        // Map center is at (0,0,0) - terrain is positioned to center at this point
        mapCenter = Vector3.zero;
        
        // Initialize biome settings lookup
        InitializeBiomeSettings();
        
        ClearExistingMap();
        
        // Generate terrain using custom system
GenerateTerrainWithCustomSystem();
        
        // Verify mapCenter is at terrain center
        if (terrainObjects.Count > 0)
        {
            Terrain terrain = terrainObjects[0].GetComponent<Terrain>();
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 terrainPos = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData.size;
                Vector3 calculatedCenter = terrainPos + terrainSize * 0.5f;
                mapCenter = new Vector3(calculatedCenter.x, 0f, calculatedCenter.z); // Use terrain Y=0 level for spawns
}
        }
        
        AddBiomeDecorations();
        CreateSpawnPoints(attackerUnits, defenderUnits);
        
        // IMPROVED: Bake NavMesh at runtime after map generation
        GenerateNavigationMesh();
        
        // Set up AAA-quality visuals for the battle scene
        SetupBattleVisuals();
    }
    
    /// <summary>
    /// Set up AAA-quality visuals for the battle scene
    /// Creates or configures BattleSceneVisuals component with biome-appropriate settings
    /// Also spawns battlefield grass using prefabs if available
    /// </summary>
    private void SetupBattleVisuals()
    {
        // Find or create BattleSceneVisuals
        BattleSceneVisuals visuals = FindFirstObjectByType<BattleSceneVisuals>();
        if (visuals == null)
        {
            GameObject visualsGO = new GameObject("BattleSceneVisuals");
            visuals = visualsGO.AddComponent<BattleSceneVisuals>();
}
        
        // Apply biome-specific visual settings
        visuals.ApplyBiomeVisuals(primaryBattleBiome);
        
        // Spawn battlefield grass if we have a grass spawner or prefab
        SpawnBattlefieldGrass();
        
        // Spawn atmospheric clouds if we have a cloud spawner
        SpawnBattlefieldClouds();
        
        // Spawn ambient particles (dust, ash, pollen, etc.)
        SpawnAmbientParticles();
        
        // Create atmosphere effects (volumetric fog, light shafts, etc.)
        SpawnAtmosphereEffects();
    }
    
    /// <summary>
    /// Spawn ambient particles (dust, ash, pollen, etc.) using BattlefieldAmbientParticles (if assigned or auto-create)
    /// </summary>
    private void SpawnAmbientParticles()
    {
        // Auto-create particle spawner if not assigned
        if (ambientParticles == null)
        {
            ambientParticles = GetComponent<BattlefieldAmbientParticles>();
            if (ambientParticles == null)
            {
                ambientParticles = gameObject.AddComponent<BattlefieldAmbientParticles>();
}
        }
        
        // Create particles with biome-appropriate settings
        ambientParticles.CreateParticles(mapSize, primaryBattleBiome);
        
        // Set consistent wind direction for particles
        ambientParticles.SetWindDirection(GetBattlefieldWindDirection());
    }
    
    /// <summary>
    /// Get consistent wind direction for all battlefield effects (clouds, particles, terrain grass)
    /// </summary>
    private Vector3 GetBattlefieldWindDirection()
    {
        // Default wind direction - slight angle for natural look
        return new Vector3(1f, 0f, 0.3f).normalized;
    }
    
    /// <summary>
    /// Spawn atmosphere effects (volumetric fog, light shafts, etc.) using BattlefieldAtmosphere (if assigned)
    /// </summary>
    private void SpawnAtmosphereEffects()
    {
        if (atmosphere == null)
        {
            return;
        }
        
        // Create atmosphere with biome-appropriate settings
        atmosphere.CreateAtmosphere(mapSize, primaryBattleBiome);
    }
    
    /// <summary>
    /// Spawn clouds above the battlefield using BattlefieldClouds (if assigned or auto-create)
    /// </summary>
    private void SpawnBattlefieldClouds()
    {
        // Auto-create cloud spawner if not assigned
        if (cloudSpawner == null)
        {
            // Create a cloud spawner component on this object
            cloudSpawner = GetComponent<BattlefieldClouds>();
            if (cloudSpawner == null)
            {
                cloudSpawner = gameObject.AddComponent<BattlefieldClouds>();
}
        }
        
        // Create clouds with biome-appropriate settings
        cloudSpawner.CreateClouds(mapSize, primaryBattleBiome);
        
        // Set consistent wind direction for clouds
        cloudSpawner.SetWindDirection(GetBattlefieldWindDirection());
    }
    
    /// <summary>
    /// Spawn GPU Instanced grass across the battlefield.
    /// Uses GPUInstancedGrass for high-performance grass rendering.
    /// </summary>
    private void SpawnBattlefieldGrass()
    {
        // Auto-create GPU grass system if not already present
        if (gpuGrass == null)
        {
            gpuGrass = GetComponent<GPUInstancedGrass>();
            if (gpuGrass == null)
            {
                gpuGrass = gameObject.AddComponent<GPUInstancedGrass>();
            }
        }
        
        // Configure grass settings
        gpuGrass.grassCount = grassInstanceCount;
        gpuGrass.densityMultiplier = grassDensityMultiplier;
        gpuGrass.renderDistance = grassRenderDistance;
        gpuGrass.windStrength = grassWindStrength;
        gpuGrass.windDirection = GetBattlefieldWindDirection();
        
        // Get terrain reference if available
        Terrain terrain = null;
        if (terrainObjects.Count > 0)
        {
            terrain = terrainObjects[0].GetComponent<Terrain>();
        }
        
        // Create the grass
        gpuGrass.CreateGrass(mapSize, primaryBattleBiome, terrain);
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
        attackerSpawnPoints.Clear();
        defenderSpawnPoints.Clear();
        spawnedObjects.Clear();
    }

    /// <summary>
    /// Generate terrain using custom Unity Terrain API with biome-specific generators
    /// or, if configured, using a Vista TerrainGraph.
    /// </summary>
    private void GenerateTerrainWithCustomSystem()
    {
#if VISTA
        // If a Vista TerrainGraph is assigned, use it instead of the custom generator.
        if (runtimeGraph != null)
        {
GenerateTerrainWithVista();
            return;
        }
#endif
        // Create terrain GameObject
        // Position terrain so its center is at (0,0,0)
        // TerrainData extends from (0,0,0) to (size.x, size.y, size.z) relative to terrain position
        // So we position at (-mapSize/2, 0, -mapSize/2) to center it at (0,0,0)
        GameObject terrainGO = new GameObject("BattleTerrain");
        terrainGO.transform.SetParent(transform);
        terrainGO.transform.position = new Vector3(-mapSize / 2f, 0f, -mapSize / 2f);
        
        // Ensure terrain uses the Battlefield layer if it exists
        int battlefieldLayer = LayerMask.NameToLayer("Battlefield");
        if (battlefieldLayer != -1) terrainGO.layer = battlefieldLayer;
        
        // Create TerrainData first (Unity's TerrainData, not our struct)
        UnityEngine.TerrainData terrainData = new UnityEngine.TerrainData();
        int resolution = CalculateOptimalResolution();
        terrainData.heightmapResolution = resolution;
        
        // CRITICAL: Set alphamapResolution BEFORE setting size
        // This determines the resolution of texture painting (splatmaps)
        // Default is tiny (16x16), causing texture to only appear in corner!
        terrainData.alphamapResolution = resolution;
        
        // Also set baseMapResolution for distance rendering
        terrainData.baseMapResolution = Mathf.Min(1024, resolution);
        
        terrainData.size = new Vector3(mapSize, heightVariation, mapSize);
        
        // Generate the actual terrain heights using our biome-aware height function
        // Without this call the terrain would remain completely flat.
        GenerateBasicTerrainHeightmap(terrainData, resolution);
        
        // Create Terrain component and assign data
        Terrain terrain = terrainGO.GetComponent<Terrain>();
        if (terrain == null)
        {
            terrain = terrainGO.AddComponent<Terrain>();
        }
        terrain.terrainData = terrainData;
        
        // Create TerrainCollider
        TerrainCollider terrainCollider = terrainGO.GetComponent<TerrainCollider>();
        if (terrainCollider == null)
        {
            terrainCollider = terrainGO.AddComponent<TerrainCollider>();
        }
        terrainCollider.terrainData = terrainData;
        
        // NOTE: Grass is now handled by GPUInstancedGrass in SpawnBattlefieldGrass()
        // Old Unity Terrain Detail system was unreliable
        
        // Apply biome material/texture to terrain (guaranteed to work with Unity Terrain)
        ApplyBiomeMaterialToTerrain(terrain);
        
        // Configure terrain rendering settings for AAA quality
        ConfigureTerrainQuality(terrain);
        
        // Add water planes for Naval/Coastal battles
        if (battleType == BattleType.Naval || battleType == BattleType.Coastal)
        {
            CreateWaterPlane(terrainGO, mapSize);
        }
        
        // Add lava planes for volcanic biomes with lava flows
        if (battleTileTemperature > 0.7f && 
            (primaryBattleBiome == Biome.Volcanic || primaryBattleBiome == Biome.Desert))
        {
            CreateLavaPlane(terrainGO, mapSize);
        }
        
        terrainObjects.Add(terrainGO);
}

#if VISTA
    /// <summary>
    /// Generate the battle terrain using a Vista TerrainGraph.
    /// This creates a Unity Terrain and lets Vista compute the heightmap, while we still apply our own materials and water/lava planes.
    /// </summary>
    private void GenerateTerrainWithVista()
    {
        // Create terrain GameObject and position so its center is at (0,0,0)
        GameObject terrainGO = new GameObject("BattleTerrain_Vista");
        terrainGO.transform.SetParent(transform);
        terrainGO.transform.position = new Vector3(-mapSize / 2f, 0f, -mapSize / 2f);

        // Ensure terrain uses the Battlefield layer if it exists
        int battlefieldLayer = LayerMask.NameToLayer("Battlefield");
        if (battlefieldLayer != -1) terrainGO.layer = battlefieldLayer;

        // Create TerrainData
        UnityEngine.TerrainData terrainData = new UnityEngine.TerrainData();
        int resolution = CalculateOptimalResolution();
        terrainData.heightmapResolution = resolution;
        terrainData.alphamapResolution = resolution;
        terrainData.baseMapResolution = Mathf.Min(1024, resolution);
        terrainData.size = new Vector3(mapSize, heightVariation, mapSize);

        // Create Terrain and collider
        Terrain terrain = terrainGO.AddComponent<Terrain>();
        terrain.terrainData = terrainData;

        TerrainCollider terrainCollider = terrainGO.AddComponent<TerrainCollider>();
        terrainCollider.terrainData = terrainData;

        // Attach a TerrainTile so we can reuse Vista's height population helpers
        TerrainTile tile = terrainGO.GetComponent<TerrainTile>();
        if (tile == null)
        {
            tile = terrainGO.AddComponent<TerrainTile>();
        }

        // Get biome settings to drive Vista configs (height scale etc.)
        BiomeTerrainSettings settings = BiomeHelper.GetTerrainSettings(primaryBattleBiome);

        // Build configs for the Vista graph
        TerrainGenerationConfigs configs = TerrainGenerationConfigs.Create();
        configs.resolution = resolution;
        configs.terrainHeight = terrainData.size.y * settings.heightScale;

        Vector3 size = terrainData.size;
        Vector3 pos = terrainGO.transform.position;
        configs.worldBounds = new Rect(pos.x, pos.z, size.x, size.z);

        // Log key Vista terrain parameters for debugging
// Collect HeightOutput node
        var heightNode = runtimeGraph.GetNode(typeof(HeightOutputNode)) as HeightOutputNode;
        if (heightNode == null)
        {
            Debug.LogError("[BattleMapGenerator] Vista runtimeGraph has no HeightOutputNode; cannot generate heightmap.");
        }
        else
        {
            string[] nodeIds = new[] { heightNode.id };

            // Execute the graph immediately on this thread (simple integration for now)
            DataPool data = runtimeGraph.ExecuteImmediate(nodeIds, configs, null, null);

            // Extract the height RenderTexture from the pool
            GraphRenderTexture graphRT = data.RemoveRTFromPool(heightNode.mainOutputSlot);
            RenderTexture heightRT = graphRT; // implicit cast

            if (heightRT != null)
            {
                // Use Vista's TerrainTile helper to convert the Vista height RT into Unity's packed heightmap
                tile.PopulateHeightMap(heightRT);
                tile.UpdateGeometry();

                // Sample a few points from the generated heightmap for debugging
                UnityEngine.TerrainData td = terrain.terrainData;
                if (td != null)
                {
                    int hRes = td.heightmapResolution;
                    float centerH = td.GetHeight(hRes / 2, hRes / 2);
                    float corner00 = td.GetHeight(0, 0);
                    float corner10 = td.GetHeight(hRes - 1, 0);
                    float corner01 = td.GetHeight(0, hRes - 1);
                    float corner11 = td.GetHeight(hRes - 1, hRes - 1);
}
            }
            else
            {
                Debug.LogError("[BattleMapGenerator] Vista graph did not produce a height RenderTexture.");
            }

            data.Dispose();
        }

        // Apply our existing biome material & AAA quality settings
        ApplyBiomeMaterialToTerrain(terrain);
        ConfigureTerrainQuality(terrain);

        // Add water planes for Naval/Coastal battles
        if (battleType == BattleType.Naval || battleType == BattleType.Coastal)
        {
            CreateWaterPlane(terrainGO, mapSize);
        }

        // Add lava planes for volcanic biomes with lava flows
        if (battleTileTemperature > 0.7f &&
            (primaryBattleBiome == Biome.Volcanic || primaryBattleBiome == Biome.Desert))
        {
            CreateLavaPlane(terrainGO, mapSize);
        }

        terrainObjects.Add(terrainGO);
}
#endif
    
    // NOTE: Old C# ITerrainLayer system (siege/water/river/lava) removed - Vista graph now defines terrain height.
    
    /// <summary>
    /// Create a water plane for Naval/Coastal battles
    /// </summary>
    private void CreateWaterPlane(GameObject terrainParent, float mapSize)
    {
        GameObject waterGO = new GameObject("WaterPlane");
        waterGO.transform.SetParent(terrainParent.transform);
        waterGO.transform.localPosition = Vector3.zero;
        
        // Create a large plane for water
        MeshFilter meshFilter = waterGO.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = waterGO.AddComponent<MeshRenderer>();
        
        // Create water mesh (simple plane)
        Mesh waterMesh = new Mesh();
        float waterSize = mapSize * 1.2f; // Slightly larger than terrain
        float waterHeight = heightVariation * 0.15f; // Water level
        
        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-waterSize/2, waterHeight, -waterSize/2),
            new Vector3(waterSize/2, waterHeight, -waterSize/2),
            new Vector3(waterSize/2, waterHeight, waterSize/2),
            new Vector3(-waterSize/2, waterHeight, waterSize/2)
        };
        
        int[] triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        Vector2[] uvs = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };
        
        waterMesh.vertices = vertices;
        waterMesh.triangles = triangles;
        waterMesh.uv = uvs;
        waterMesh.RecalculateNormals();
        
        meshFilter.mesh = waterMesh;
        
        // Create water material using URP-compatible shader
        Material waterMaterial = CreateWaterMaterial();
        if (waterMaterial != null)
        {
            meshRenderer.material = waterMaterial;
        }
        else
        {
            // Fallback: create a simple blue material
            meshRenderer.material = new Material(Shader.Find("Unlit/Color"));
            meshRenderer.material.color = new Color(0.1f, 0.3f, 0.6f, 0.75f);
        }
        
        // Add collider for water (non-walkable for land units)
        MeshCollider waterCollider = waterGO.AddComponent<MeshCollider>();
        waterCollider.sharedMesh = waterMesh;
        
        // Set water layer (can be used for NavMesh exclusion)
        int waterLayer = LayerMask.NameToLayer("Water");
        if (waterLayer != -1)
        {
            waterGO.layer = waterLayer;
        }
}
    
    /// <summary>
    /// Create a lava plane for volcanic biomes with lava flows
    /// </summary>
    private void CreateLavaPlane(GameObject terrainParent, float mapSize)
    {
        GameObject lavaGO = new GameObject("LavaPlane");
        lavaGO.transform.SetParent(terrainParent.transform);
        lavaGO.transform.localPosition = Vector3.zero;
        
        // Create a plane for lava (similar to water but smaller, follows lava flow paths)
        MeshFilter meshFilter = lavaGO.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = lavaGO.AddComponent<MeshRenderer>();
        
        // Create lava mesh (simple plane, can be enhanced to follow lava flow paths)
        Mesh lavaMesh = new Mesh();
        float lavaSize = mapSize * 0.3f; // Smaller than water (lava flows are narrower)
        float lavaHeight = heightVariation * 0.2f; // Lava level (slightly above water)
        
        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-lavaSize/2, lavaHeight, -lavaSize/2),
            new Vector3(lavaSize/2, lavaHeight, -lavaSize/2),
            new Vector3(lavaSize/2, lavaHeight, lavaSize/2),
            new Vector3(-lavaSize/2, lavaHeight, lavaSize/2)
        };
        
        int[] triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        Vector2[] uvs = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };
        
        lavaMesh.vertices = vertices;
        lavaMesh.triangles = triangles;
        lavaMesh.uv = uvs;
        lavaMesh.RecalculateNormals();
        
        meshFilter.mesh = lavaMesh;
        
        // Create lava material using URP-compatible shader with emission
        Material lavaMaterial = CreateLavaMaterial();
        if (lavaMaterial != null)
        {
            meshRenderer.material = lavaMaterial;
        }
        else
        {
            // Fallback: create a simple red/orange material
            meshRenderer.material = new Material(Shader.Find("Unlit/Color"));
            meshRenderer.material.color = new Color(0.9f, 0.3f, 0.1f, 0.85f);
        }
        
        // Add collider for lava (non-walkable, causes damage)
        MeshCollider lavaCollider = lavaGO.AddComponent<MeshCollider>();
        lavaCollider.sharedMesh = lavaMesh;
        
        // Set lava layer
        int lavaLayer = LayerMask.NameToLayer("Water"); // Reuse Water layer for now, or create "Lava" layer
        if (lavaLayer != -1)
        {
            lavaGO.layer = lavaLayer;
        }
}
    
    /// <summary>
    /// Generate basic terrain heightmap (fallback when no biome generator available)
    /// </summary>
    private void GenerateBasicTerrainHeightmap(UnityEngine.TerrainData terrainData, int resolution)
    {
        float[,] heights = new float[resolution, resolution];
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;
        float sumHeight = 0f;
        int sampleCount = 0;
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                float height = CalculateHeightAtPosition(worldX, worldZ);
                float normalized = Mathf.Clamp01(height / heightVariation);
                heights[y, x] = normalized;
                
                // Track stats for debug
                if (normalized < minHeight) minHeight = normalized;
                if (normalized > maxHeight) maxHeight = normalized;
                sumHeight += normalized;
                sampleCount++;
            }
        }
        
        terrainData.SetHeights(0, 0, heights);
        
        if (sampleCount > 0)
        {
            float avgHeight = sumHeight / sampleCount;
}
    }
    
    /// <summary>
    /// Apply biome material/texture to terrain using TerrainData.terrainLayers (proper Unity Terrain texturing)
    /// </summary>
    private void ApplyBiomeMaterialToTerrain(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("[BattleMapGenerator] Cannot apply biome material: terrain or terrainData is null");
            return;
        }
// Get biome settings
        Texture2D albedoTexture = null;
        Texture2D normalTexture = null;
        
        if (biomeSettingsLookup.TryGetValue(primaryBattleBiome, out BiomeSettings settings))
        {
            albedoTexture = settings.albedoTexture;
            normalTexture = settings.normalTexture;
}
        else
        {
            Debug.LogWarning($"[BattleMapGenerator] No biome settings found for {primaryBattleBiome}! Terrain will use default material.");
        }
        
        // IMPORTANT: In URP, we need BOTH:
        // 1. materialTemplate = URP Terrain shader (provides the rendering)
        // 2. terrainLayers = textures (provides the appearance)
        // Setting materialTemplate to null causes purple terrain!
        
        // First, create and assign a proper URP terrain material
        Material urpTerrainMaterial = CreateURPTerrainMaterial();
        if (urpTerrainMaterial != null)
        {
            terrain.materialTemplate = urpTerrainMaterial;
}
        else
        {
            Debug.LogWarning("[BattleMapGenerator] Could not create URP terrain material - terrain may appear pink!");
        }
        
        // Now create terrain layer for textures
        if (albedoTexture != null)
        {
            TerrainLayer terrainLayer = new TerrainLayer();
            terrainLayer.diffuseTexture = albedoTexture;
            terrainLayer.tileSize = new Vector2(15f, 15f); // Texture tiling size
            terrainLayer.tileOffset = Vector2.zero;
            
            if (normalTexture != null)
            {
                terrainLayer.normalMapTexture = normalTexture;
            }
            
            // Set metallic and smoothness for URP
            terrainLayer.metallic = 0.0f;
            terrainLayer.smoothness = 0.3f;
            
            // Set the terrain layer on terrainData
            terrain.terrainData.terrainLayers = new TerrainLayer[] { terrainLayer };
// Paint the entire terrain with this layer (100% coverage)
            int alphamapWidth = terrain.terrainData.alphamapWidth;
            int alphamapHeight = terrain.terrainData.alphamapHeight;
            float[,,] alphamaps = new float[alphamapHeight, alphamapWidth, 1]; // 1 layer
            
            // Fill with 1.0 (100% coverage) for the single layer
            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    alphamaps[y, x, 0] = 1.0f;
                }
            }
            
            terrain.terrainData.SetAlphamaps(0, 0, alphamaps);
}
        else
        {
            // Fallback: Create a TerrainLayer with biome color texture
            Debug.LogWarning($"[BattleMapGenerator] No texture available for {primaryBattleBiome}, creating default TerrainLayer with biome color");
            
            // Create a simple TerrainLayer with a solid color texture
            TerrainLayer defaultLayer = new TerrainLayer();
            Color biomeColor = GetBiomeColor(primaryBattleBiome);
            
            // Create a small texture with the biome color (not 1x1, that can cause issues)
            Texture2D colorTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    colorTexture.SetPixel(x, y, biomeColor);
                }
            }
            colorTexture.Apply();
            
            defaultLayer.diffuseTexture = colorTexture;
            defaultLayer.tileSize = new Vector2(10f, 10f);
            defaultLayer.metallic = 0.0f;
            defaultLayer.smoothness = 0.3f;
            
            terrain.terrainData.terrainLayers = new TerrainLayer[] { defaultLayer };
            
            // Paint entire terrain with this layer
            int alphamapWidth = terrain.terrainData.alphamapWidth;
            int alphamapHeight = terrain.terrainData.alphamapHeight;
            float[,,] alphamaps = new float[alphamapHeight, alphamapWidth, 1];
            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    alphamaps[y, x, 0] = 1.0f;
                }
            }
            terrain.terrainData.SetAlphamaps(0, 0, alphamaps);
}
    }
    
    // NOTE: Grass is now handled by GPUInstancedGrass - old Terrain Detail system removed
    
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
    /// Configure terrain rendering settings for AAA quality visuals
    /// </summary>
    private void ConfigureTerrainQuality(Terrain terrain)
    {
        if (terrain == null) return;
        
        // === HEIGHTMAP SETTINGS (AAA Quality) ===
        // Pixel error controls LOD quality (lower = higher quality)
        terrain.heightmapPixelError = 1f; // Highest quality (was 3, AAA uses 1-2)
        
        // Base map distance - distance at which terrain switches to base map
        terrain.basemapDistance = 2000f; // Keep high-res textures visible at distance
        
        // NOTE: Grass is now handled by GPUInstancedGrass, not Terrain Details
        
        // === TREE SETTINGS ===
        terrain.treeDistance = CalculateTreeDistance();
        terrain.treeBillboardDistance = CalculateTreeBillboardStart();
        terrain.treeCrossFadeLength = 100f; // Smooth transition between 3D and billboard (AAA standard)
        terrain.treeMaximumFullLODCount = CalculateTreeFullLod();
        
        // === RENDERING SETTINGS ===
        // Draw instanced for better performance with many grass/details
        terrain.drawInstanced = true;
        
        // === SHADOW SETTINGS ===
        terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        
        // === REFLECTION PROBE USAGE ===
        terrain.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
        
        // === MATERIAL QUALITY ENHANCEMENTS ===
        if (terrain.materialTemplate != null)
        {
            // Set texture rendering quality on terrain material
            terrain.materialTemplate.SetFloat("_Smoothness", 0.3f); // Subtle specularity
            
            // If available, enable heightmap blending
            if (terrain.materialTemplate.HasProperty("_HeightTransition"))
            {
                terrain.materialTemplate.SetFloat("_HeightTransition", 0.5f);
            }
        }
}
    
    // ========== BIOME-SPECIFIC CALCULATION METHODS ==========
    
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
    /// Calculate the actual bounds of all terrain objects
    /// This ensures NavMesh bounds include the full terrain, not just the mapSize
    /// </summary>
    private Bounds CalculateTerrainBounds()
    {
        Bounds combinedBounds = new Bounds();
        bool hasBounds = false;
        
        foreach (var terrainObj in terrainObjects)
        {
            if (terrainObj == null) continue;
            
            Bounds objBounds = new Bounds();
            bool objHasBounds = false;
            
            // Check for Terrain component
            Terrain terrain = terrainObj.GetComponent<Terrain>();
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 terrainPos = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData.size;
                objBounds = new Bounds(
                    terrainPos + terrainSize * 0.5f,
                    terrainSize
                );
                objHasBounds = true;
            }
            // Fallback: use collider bounds
            else
            {
                Collider collider = terrainObj.GetComponent<Collider>();
                if (collider != null)
                {
                    objBounds = collider.bounds;
                    objHasBounds = true;
                }
            }
            
            if (objHasBounds)
            {
                if (!hasBounds)
                {
                    combinedBounds = objBounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(objBounds);
                }
            }
        }
        
        // If no terrain bounds found, return empty bounds (will use mapSize fallback)
        if (!hasBounds)
        {
            Debug.LogWarning("[BattleMapGenerator] Could not calculate terrain bounds, will use mapSize-based bounds");
            return new Bounds();
        }
        
        // Expand bounds slightly to ensure we capture everything
        combinedBounds.Expand(10f);
return combinedBounds;
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
    
    /// <summary>
    /// Create a URP terrain material for materialTemplate
    /// This provides the shader; TerrainLayers provide the textures
    /// </summary>
    private Material CreateURPTerrainMaterial()
    {
        // Try URP Terrain shader first (this is what Unity expects for URP terrains)
        Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        
        if (terrainShader == null)
        {
            Debug.LogWarning("[BattleMapGenerator] 'Universal Render Pipeline/Terrain/Lit' not found, trying alternatives...");
            
            // Try other URP terrain shader paths
            string[] shaderPaths = new string[]
            {
                "Universal Render Pipeline/Terrain/Standard",
                "Shader Graphs/Terrain Lit",
                "Nature/Terrain/Standard",  // Built-in fallback
                "Universal Render Pipeline/Lit",  // Basic URP lit
                "Standard"  // Absolute fallback
            };
            
            foreach (string path in shaderPaths)
            {
                terrainShader = Shader.Find(path);
                if (terrainShader != null)
                {
break;
                }
            }
        }
        
        if (terrainShader == null)
        {
            Debug.LogError("[BattleMapGenerator] No terrain shader found! Install URP package or check shader includes.");
            return null;
        }
        
        Material material = new Material(terrainShader);
return material;
    }
    
    /// <summary>
    /// Create a water material using URP-compatible shaders
    /// Provides semi-transparent, reflective water appearance
    /// </summary>
    private Material CreateWaterMaterial()
    {
        // Try to find URP-compatible transparent shader
        Shader waterShader = Shader.Find("Universal Render Pipeline/Lit");
        
        if (waterShader == null)
        {
            waterShader = Shader.Find("Standard");
        }
        
        if (waterShader == null)
        {
            Debug.LogError("[BattleMapGenerator] No shader found for water material!");
            return null;
        }
        
        Material waterMaterial = new Material(waterShader);
        
        // Configure for URP transparency
        if (waterShader.name.Contains("Universal"))
        {
            // URP Lit shader settings
            waterMaterial.SetFloat("_Surface", 1); // 0 = Opaque, 1 = Transparent
            waterMaterial.SetFloat("_Blend", 0); // 0 = Alpha, 1 = Premultiply, 2 = Additive, 3 = Multiply
            waterMaterial.SetFloat("_AlphaClip", 0);
            waterMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            waterMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            waterMaterial.SetFloat("_ZWrite", 0);
            waterMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            waterMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            
            // Set water color and properties
            waterMaterial.SetColor("_BaseColor", new Color(0.1f, 0.3f, 0.6f, 0.75f));
            waterMaterial.SetFloat("_Smoothness", 0.95f); // Very smooth/reflective
            waterMaterial.SetFloat("_Metallic", 0.0f);
        }
        else
        {
            // Standard shader fallback
            waterMaterial.SetFloat("_Mode", 3); // Transparent mode
            waterMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            waterMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            waterMaterial.SetInt("_ZWrite", 0);
            waterMaterial.DisableKeyword("_ALPHATEST_ON");
            waterMaterial.EnableKeyword("_ALPHABLEND_ON");
            waterMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            waterMaterial.renderQueue = 3000;
            waterMaterial.color = new Color(0.1f, 0.3f, 0.6f, 0.75f);
            waterMaterial.SetFloat("_Glossiness", 0.95f);
        }
return waterMaterial;
    }
    
    /// <summary>
    /// Create a lava material using URP-compatible shaders with emission
    /// </summary>
    private Material CreateLavaMaterial()
    {
        Shader lavaShader = Shader.Find("Universal Render Pipeline/Lit");
        
        if (lavaShader == null)
        {
            lavaShader = Shader.Find("Standard");
        }
        
        if (lavaShader == null)
        {
            Debug.LogError("[BattleMapGenerator] No shader found for lava material!");
            return null;
        }
        
        Material lavaMaterial = new Material(lavaShader);
        
        // Configure for URP with emission
        if (lavaShader.name.Contains("Universal"))
        {
            lavaMaterial.SetFloat("_Surface", 1); // Transparent
            lavaMaterial.SetFloat("_Blend", 0);
            lavaMaterial.SetFloat("_AlphaClip", 0);
            lavaMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lavaMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lavaMaterial.SetFloat("_ZWrite", 0);
            lavaMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            lavaMaterial.EnableKeyword("_EMISSION");
            lavaMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            
            // Lava color and emission
            lavaMaterial.SetColor("_BaseColor", new Color(0.9f, 0.3f, 0.1f, 0.85f));
            lavaMaterial.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.1f) * 2f); // Bright orange glow
            lavaMaterial.SetFloat("_Smoothness", 0.8f);
            lavaMaterial.SetFloat("_Metallic", 0.0f);
        }
        else
        {
            lavaMaterial.SetFloat("_Mode", 3);
            lavaMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lavaMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lavaMaterial.SetInt("_ZWrite", 0);
            lavaMaterial.DisableKeyword("_ALPHATEST_ON");
            lavaMaterial.EnableKeyword("_ALPHABLEND_ON");
            lavaMaterial.EnableKeyword("_EMISSION");
            lavaMaterial.renderQueue = 3000;
            lavaMaterial.color = new Color(0.9f, 0.3f, 0.1f, 0.85f);
            lavaMaterial.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.1f) * 2f);
        }
return lavaMaterial;
    }
    
    /// <summary>
    /// Create a terrain material with biome-specific textures
    /// Uses textures from biomeSettings if available, otherwise uses biome color
    /// </summary>
    private Material CreateBiomeTerrainMaterial()
    {
        // Find appropriate shader (URP or Standard)
        // URP Terrain shader path
        Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        
        if (terrainShader == null)
        {
            Debug.LogWarning("[BattleMapGenerator] URP Terrain shader not found at 'Universal Render Pipeline/Terrain/Lit', trying alternatives...");
            // Try alternative URP paths
            terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Standard");
        }
        
        if (terrainShader == null)
        {
            // Try built-in terrain shader (for non-URP projects)
            terrainShader = Shader.Find("Nature/Terrain/Standard");
        }
        
        if (terrainShader == null)
        {
            // Last resort: Use URP Lit shader (not ideal for terrain but will work)
            Debug.LogWarning("[BattleMapGenerator] No terrain-specific shader found, using URP Lit shader");
            terrainShader = Shader.Find("Universal Render Pipeline/Lit");
        }
        
        if (terrainShader == null)
        {
            // Absolute fallback
            terrainShader = Shader.Find("Standard");
        }
        
        if (terrainShader == null)
        {
            Debug.LogError("[BattleMapGenerator] Could not find ANY shader! Terrain will appear pink.");
            Debug.LogError("[BattleMapGenerator] Make sure URP is properly installed and configured.");
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
    /// Add biome-specific decorations and obstacles (limited to prevent memory issues)
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
int successfulPlacements = 0;
        int maxAttempts = decorationCount * 3; // Try up to 3x to find valid positions
        int attempts = 0;
        
        while (successfulPlacements < decorationCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Random position on the map
            Vector3 position = new Vector3(
                UnityEngine.Random.Range(-mapSize / 2f, mapSize / 2f),
                0f,
                UnityEngine.Random.Range(-mapSize / 2f, mapSize / 2f)
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
        
        // Last resort: Use CalculateHeightAtPosition for fallback mesh
        return CalculateHeightAtPosition(position.x, position.z);
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
            GameObject decorationPrefab = settings.decorations[UnityEngine.Random.Range(0, settings.decorations.Length)];
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
                Quaternion rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
                
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
    /// Armies spawn at the center of the terrain, facing each other with spawnDistance spacing
    /// Formations are arranged in ROWS (spread along Z axis) facing each other (along X axis)
    /// </summary>
    private void CreateSpawnPoints(int attackerUnits, int defenderUnits)
    {
        // Calculate spawn positions relative to map center
        // Attacker spawns on the left (negative X), defender on the right (positive X)
        // Both are at the center of the terrain (Z=0) with spacing along X axis
        float halfDistance = spawnDistance / 2f;
        
        // Attacker spawns at mapCenter + (-halfDistance, 0, 0)
        Vector3 attackerBaseSpawn = mapCenter + new Vector3(-halfDistance, 0f, 0f);
        // Defender spawns at mapCenter + (+halfDistance, 0, 0)
        Vector3 defenderBaseSpawn = mapCenter + new Vector3(halfDistance, 0f, 0f);
        
        // Find good spawn positions (avoid impassable terrain)
        Vector3 attackerSpawn = FindGoodSpawnPosition(attackerBaseSpawn);
        Vector3 defenderSpawn = FindGoodSpawnPosition(defenderBaseSpawn);
        
        // Create spawn points for attackers in ROWS facing defenders
        // Rows spread along Z (width), columns along X (depth toward enemy)
        float formationSpreadZ = 12f; // Wider spread along Z (row width)
        float formationSpreadX = 6f;  // Narrower spread along X (depth)
        
        // Calculate grid dimensions: more units per row (Z) than columns (X)
        // This creates wide, shallow formations facing each other
        int unitsPerRow = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(attackerUnits * 2f))); // Wider rows
        int numRows = Mathf.CeilToInt((float)attackerUnits / unitsPerRow);
        
        for (int i = 0; i < attackerUnits; i++)
        {
            int row = i / unitsPerRow;    // Which row (depth, along X toward enemy)
            int col = i % unitsPerRow;    // Which column (width, along Z)
            
            // Spread along Z for width (row), X for depth (column)
            // Center the formation on the base spawn
            float offsetZ = (col - (unitsPerRow - 1) / 2f) * formationSpreadZ;
            float offsetX = (row - (numRows - 1) / 2f) * formationSpreadX;
            
            Vector3 spawnPos = attackerSpawn + new Vector3(offsetX, 0f, offsetZ);
            spawnPos.y = GetTerrainHeightAtPosition(spawnPos);
            attackerSpawnPoints.Add(spawnPos);
        }
        
        // Create spawn points for defenders in ROWS facing attackers
        unitsPerRow = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(defenderUnits * 2f)));
        numRows = Mathf.CeilToInt((float)defenderUnits / unitsPerRow);
        
        for (int i = 0; i < defenderUnits; i++)
        {
            int row = i / unitsPerRow;
            int col = i % unitsPerRow;
            
            float offsetZ = (col - (unitsPerRow - 1) / 2f) * formationSpreadZ;
            float offsetX = (row - (numRows - 1) / 2f) * formationSpreadX;
            
            Vector3 spawnPos = defenderSpawn + new Vector3(offsetX, 0f, offsetZ);
            spawnPos.y = GetTerrainHeightAtPosition(spawnPos);
            defenderSpawnPoints.Add(spawnPos);
        }
    }
    
    /// <summary>
    /// Find a good spawn position near the target position (avoiding impassable terrain)
    /// Returns world position at terrain height
    /// </summary>
    private Vector3 FindGoodSpawnPosition(Vector3 targetPosition)
    {
        // Try the exact target position first
        if (IsPositionInBounds(targetPosition) && !IsPositionImpassable(targetPosition))
        {
            targetPosition.y = GetTerrainHeightAtPosition(targetPosition);
            return targetPosition;
        }
        
        // Search nearby positions in a spiral pattern
        float searchRadius = 5f;
        int maxAttempts = 20;
        
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float angle = (attempt / (float)maxAttempts) * 2f * Mathf.PI;
            float radius = searchRadius * (attempt / (float)maxAttempts);
            float offsetX = Mathf.Cos(angle) * radius;
            float offsetZ = Mathf.Sin(angle) * radius;
            
            Vector3 candidate = targetPosition + new Vector3(offsetX, 0f, offsetZ);
            
            if (IsPositionInBounds(candidate) && !IsPositionImpassable(candidate))
            {
                candidate.y = GetTerrainHeightAtPosition(candidate);
                return candidate;
            }
        }
        
        // Fallback: return target position at terrain height (even if not ideal)
        targetPosition.y = GetTerrainHeightAtPosition(targetPosition);
        Debug.LogWarning($"[BattleMapGenerator] Could not find ideal spawn position near {targetPosition}, using fallback");
        return targetPosition;
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
// Get NavMesh build settings (use default settings with ID 0)
        // You can create custom settings in the Navigation window and reference them by ID
        NavMeshBuildSettings buildSettings = NavMesh.GetSettingsByID(0);
        
        // Calculate actual terrain bounds from terrain objects (MapMagic terrains might be larger than mapSize)
        Bounds terrainBounds = CalculateTerrainBounds();
        
        // Use terrain bounds if available, otherwise use mapSize-based bounds
        Bounds buildBounds = terrainBounds.size.magnitude > 0.1f 
            ? terrainBounds 
            : new Bounds(mapCenter, new Vector3(mapSize, 20f, mapSize));
// Collect all sources (terrain and obstacles) using NavMeshBuilder
        // This automatically finds all colliders and meshes in the bounds
        // FIXED: Use Battlefield layer to match terrain object layer
        int battlefieldLayer = LayerMask.NameToLayer("Battlefield");
        int layerMask = battlefieldLayer != -1 ? (1 << battlefieldLayer) : ~0; // Use Battlefield layer, or all layers if it doesn't exist
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
if (sources.Count > 0)
        {
            Vector3 firstSourcePos = sources[0].transform.GetColumn(3); // Position is in the 4th column of Matrix4x4
}
        
        // DEBUG: Log terrain objects with detailed info
foreach (var terrainObj in terrainObjects)
        {
            if (terrainObj != null)
            {
                var collider = terrainObj.GetComponent<Collider>();
                var terrainCollider = terrainObj.GetComponent<TerrainCollider>();
                var terrainComponent = terrainObj.GetComponent<Terrain>();
                var meshFilter = terrainObj.GetComponent<MeshFilter>();
                int layer = terrainObj.layer;
                string layerName = LayerMask.LayerToName(layer);
                Vector3 position = terrainObj.transform.position;
                Bounds? objBounds = null;
                
                if (terrainComponent != null && terrainComponent.terrainData != null)
                {
                    objBounds = new Bounds(
                        terrainObj.transform.position + terrainComponent.terrainData.size * 0.5f,
                        terrainComponent.terrainData.size
                    );
                }
                else if (collider != null)
                {
                    objBounds = collider.bounds;
                }
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
            // Log what types of sources we're using
            int meshSources = 0, colliderSources = 0;
            foreach (var source in filteredSources)
            {
                if (source.shape == NavMeshBuildSourceShape.Mesh) meshSources++;
                else if (source.shape == NavMeshBuildSourceShape.Box || 
                         source.shape == NavMeshBuildSourceShape.Sphere ||
                         source.shape == NavMeshBuildSourceShape.Capsule) colliderSources++;
            }
}
        else
        {
            Debug.LogError("[BattleMapGenerator] NavMesh data is null! Baking failed.");
        }
        
        // IMPROVED: Wait a frame to ensure NavMesh is fully processed by Unity
        // This helps prevent formations from failing to find NavMesh immediately after baking
        StartCoroutine(WaitForNavMeshReady());
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
        
        return UnityEngine.Random.value < chance;
    }

    // Public API methods
    public Vector3 GetRandomPosition()
    {
        // Return position relative to MapMagic center
        float x = UnityEngine.Random.Range(-mapSize / 2f, mapSize / 2f);
        float z = UnityEngine.Random.Range(-mapSize / 2f, mapSize / 2f);
        // Get height at world position
        Vector3 worldPos = mapCenter + new Vector3(x, 0, z);
        float y = GetTerrainHeightAtPosition(worldPos);
        if (y == float.MinValue)
        {
            y = CalculateHeightAtPosition(worldPos.x, worldPos.z);
        }
        return new Vector3(x, y, z); // Return relative to map center
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
    
#if VISTA
    /// <summary>
    /// Generate terrain using a Vista TerrainGraph asynchronously.
    /// NOTE: This runs the graph; applying its data back into the Terrain is handled by Vista's TerrainTile/VistaManager pipeline.
    /// </summary>
    public System.Collections.IEnumerator GenerateBattleTerrainAsync()
    {
        if (runtimeGraph == null)
        {
            Debug.LogWarning("[BattleMapGenerator] Vista runtime graph is not assigned; skipping Vista terrain generation.");
            yield break;
        }

        // Resolve biome-specific settings
        BiomeTerrainSettings settings = BiomeHelper.GetTerrainSettings(primaryBattleBiome);

        // Find the first generated Terrain as Vista's target area
        Terrain targetTerrain = null;
        if (terrainObjects != null && terrainObjects.Count > 0)
        {
            targetTerrain = terrainObjects[0].GetComponent<Terrain>();
        }

        if (targetTerrain == null || targetTerrain.terrainData == null)
        {
            Debug.LogWarning("[BattleMapGenerator] No Terrain found for Vista graph to operate on.");
            yield break;
        }

        Vector3 size = targetTerrain.terrainData.size;
        Vector3 pos = targetTerrain.transform.position;
        Bounds worldBounds = new Bounds(pos + size * 0.5f, size);

        int resolution = targetTerrain.terrainData.heightmapResolution;

        // Prepare configs (mirror what TerrainGraphUtilities does, but simplified for a single tile)
        TerrainGenerationConfigs configs = new TerrainGenerationConfigs
        {
            resolution = resolution,
            terrainHeight = size.y * settings.heightScale,
            worldBounds = new Rect(worldBounds.min.x, worldBounds.min.z, worldBounds.size.x, worldBounds.size.z),
            seed = 0
        };

        // Collect the HeightOutputNode (heightmap output); additional outputs can be added later if needed
        var nodeIds = new List<string>();
        var heightNode = runtimeGraph.GetNode(typeof(HeightOutputNode)) as HeightOutputNode;
        if (heightNode != null)
        {
            nodeIds.Add(heightNode.id);
        }

        if (nodeIds.Count == 0)
        {
            Debug.LogWarning("[BattleMapGenerator] Vista graph has no HeightOutputNode; nothing to execute.");
            yield break;
        }

        // Execute graph asynchronously (non-blocking over multiple frames)
        ExecutionHandle handle = runtimeGraph.Execute(nodeIds.ToArray(), configs, null, null);
        while (!handle.isCompleted)
        {
            yield return null;
        }

        // The Vista terrain system (VistaManager/TerrainTile) is responsible for reading DataPool
        // and applying it to the Unity Terrain. Here we just ensure the graph has finished.
        handle.Dispose();
    }
#endif
    
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
// Verify NavMesh is queryable by sampling a position at the map center
        int attempts = 0;
        const int maxAttempts = 10;
        
        while (attempts < maxAttempts)
        {
            // Try sampling at map center
            if (NavMesh.SamplePosition(mapCenter, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
// DEBUG: Test a few more positions to ensure NavMesh is fully ready
                bool allTestsPassed = true;
                for (int i = 0; i < 5; i++)
                {
                    Vector3 testPos = mapCenter + new Vector3(
                        UnityEngine.Random.Range(-mapSize * 0.3f, mapSize * 0.3f),
                        0,
                        UnityEngine.Random.Range(-mapSize * 0.3f, mapSize * 0.3f)
                    );
                    if (!NavMesh.SamplePosition(testPos, out NavMeshHit testHit, 10f, NavMesh.AllAreas))
                    {
                        allTestsPassed = false;
                        Debug.LogWarning($"[BattleMapGenerator] NavMesh test failed at {testPos}");
                    }
                }
                
                if (allTestsPassed)
                {
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

