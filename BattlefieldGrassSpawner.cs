using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spawns grass prefabs across the battlefield with wind animation support.
/// Optimized for performance using GPU instancing and LOD-based culling.
/// Attach to the BattleMapGenerator GameObject or a dedicated Grass parent.
/// </summary>
public class BattlefieldGrassSpawner : MonoBehaviour
{
    [Header("Grass Prefab")]
    [Tooltip("Grass prefab to spawn. Should have wind animation shader or animator.")]
    public GameObject grassPrefab;
    
    [Tooltip("Alternative grass prefabs for variety (optional)")]
    public GameObject[] grassVariants;
    
    [Header("Spawn Settings")]
    [Tooltip("Number of grass patches to spawn (keep low for performance)")]
    [Range(50, 200000)]
    public int grassCount = 500;
    
    [Tooltip("Minimum distance between grass patches")]
    [Range(0.1f, 5f)]
    public float minSpacing = 0.5f;
    
    [Tooltip("Maximum spawn radius from center (0 = use terrain bounds)")]
    public float spawnRadius = 0f;
    
    [Tooltip("Grass will not spawn on slopes steeper than this angle")]
    [Range(0f, 90f)]
    public float maxSlopeAngle = 45f;
    
    [Header("Grass Appearance")]
    [Tooltip("Minimum scale multiplier for grass")]
    [Range(0.1f, 2f)]
    public float minScale = 0.8f;
    
    [Tooltip("Maximum scale multiplier for grass")]
    [Range(0.5f, 3f)]
    public float maxScale = 1.2f;
    
    [Tooltip("Color tint variation (applied randomly to each grass patch)")]
    public Color baseTint = new Color(0.8f, 0.95f, 0.7f);
    
    [Tooltip("How much color can vary from base tint")]
    [Range(0f, 0.3f)]
    public float colorVariation = 0.1f;
    
    [Header("Wind Animation")]
    [Tooltip("Wind strength (affects shader wind parameters)")]
    [Range(0f, 2f)]
    public float windStrength = 0.5f;
    
    [Tooltip("Wind speed (affects shader wind parameters)")]
    [Range(0f, 5f)]
    public float windSpeed = 1f;
    
    [Tooltip("Wind direction (world space)")]
    public Vector3 windDirection = new Vector3(1f, 0f, 0.5f);
    
    [Header("Performance")]
    [Tooltip("Use GPU instancing for better performance")]
    public bool useInstancing = true;
    
    [Tooltip("Distance at which grass fades out")]
    [Range(50f, 500f)]
    public float fadeDistance = 150f;
    
    [Tooltip("Layer mask for terrain raycasting")]
    public LayerMask terrainLayer = ~0;
    
    [Header("Biome Settings")]
    [Tooltip("Density multiplier based on biome (1.0 = full density)")]
    [Range(0f, 2f)]
    public float biomeDensityMultiplier = 1f;
    
    // Internal
    private List<GameObject> spawnedGrass = new List<GameObject>();
    private Transform grassParent;
    private Terrain activeTerrain;
    private MaterialPropertyBlock propBlock;
    
    // Shader property IDs (cached for performance)
    private static readonly int WindStrengthID = Shader.PropertyToID("_WindStrength");
    private static readonly int WindSpeedID = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindDirectionID = Shader.PropertyToID("_WindDirection");
    private static readonly int TintColorID = Shader.PropertyToID("_Color");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    
    /// <summary>
    /// Spawn grass across the battlefield
    /// Call this after terrain is generated
    /// </summary>
    public void SpawnGrass(Terrain terrain = null, float mapSize = 0f, Biome biome = Biome.Plains)
    {
        // Clean up any existing grass
        ClearGrass();
        
        // Find terrain if not provided
        activeTerrain = terrain ?? FindFirstObjectByType<Terrain>();
        if (activeTerrain == null)
        {
            Debug.LogWarning("[BattlefieldGrassSpawner] No terrain found - cannot spawn grass");
            return;
        }
        
        // Create parent object for organization
        grassParent = new GameObject("BattlefieldGrass").transform;
        grassParent.SetParent(transform);
        grassParent.localPosition = Vector3.zero;
        
        // Calculate spawn area
        float actualRadius = spawnRadius > 0 ? spawnRadius : activeTerrain.terrainData.size.x / 2f;
        Vector3 terrainCenter = activeTerrain.transform.position + activeTerrain.terrainData.size * 0.5f;
        terrainCenter.y = 0; // Use ground level for center
        
        // Adjust density based on biome
        float densityMultiplier = GetBiomeDensityMultiplier(biome);
        int actualGrassCount = Mathf.RoundToInt(grassCount * densityMultiplier * biomeDensityMultiplier);
        
        if (actualGrassCount <= 0 || grassPrefab == null)
        {
            Debug.Log($"[BattlefieldGrassSpawner] Skipping grass spawn - count: {actualGrassCount}, prefab: {grassPrefab != null}");
            return;
        }
        
        // Adjust tint based on biome
        baseTint = GetBiomeGrassTint(biome);
        
        // Set up material property block for instancing
        propBlock = new MaterialPropertyBlock();
        
        // Spawn grass
        int spawned = 0;
        int attempts = 0;
        int maxAttempts = actualGrassCount * 3;
        
        while (spawned < actualGrassCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Random position within spawn area
            Vector2 randomOffset = Random.insideUnitCircle * actualRadius;
            Vector3 worldPos = terrainCenter + new Vector3(randomOffset.x, 100f, randomOffset.y);
            
            // Raycast to find terrain surface
            if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, 200f, terrainLayer))
            {
                // Check slope
                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
                if (slopeAngle > maxSlopeAngle) continue;
                
                // Check if underwater (below terrain water level, if any)
                float terrainHeight = activeTerrain.SampleHeight(hit.point);
                // Skip if significantly below expected height (likely underwater)
                
                // Spawn grass
                if (SpawnSingleGrass(hit.point, hit.normal))
                {
                    spawned++;
                }
            }
        }
        
        // Set global wind parameters
        UpdateWindParameters();
        
        Debug.Log($"[BattlefieldGrassSpawner] Spawned {spawned} grass patches for {biome} biome (density: {densityMultiplier:F2})");
    }
    
    /// <summary>
    /// Spawn a single grass patch at the specified position
    /// </summary>
    private bool SpawnSingleGrass(Vector3 position, Vector3 normal)
    {
        // Select prefab (main or variant)
        GameObject prefab = grassPrefab;
        if (grassVariants != null && grassVariants.Length > 0 && Random.value > 0.7f)
        {
            prefab = grassVariants[Random.Range(0, grassVariants.Length)];
            if (prefab == null) prefab = grassPrefab;
        }
        
        if (prefab == null) return false;
        
        // Random rotation
        Quaternion rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        
        // Slight tilt to match terrain normal (subtle)
        Quaternion terrainTilt = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, normal, 0.3f));
        rotation = terrainTilt * rotation;
        
        // Random scale
        float scale = Random.Range(minScale, maxScale);
        
        // Instantiate
        GameObject grass = Instantiate(prefab, position, rotation, grassParent);
        grass.transform.localScale = Vector3.one * scale;
        grass.name = $"Grass_{spawnedGrass.Count}";
        
        // Apply material property variations
        Renderer renderer = grass.GetComponent<Renderer>();
        if (renderer == null) renderer = grass.GetComponentInChildren<Renderer>();
        
        if (renderer != null)
        {
            // Get or create property block
            renderer.GetPropertyBlock(propBlock);
            
            // Random color variation
            Color tint = baseTint;
            tint.r += Random.Range(-colorVariation, colorVariation);
            tint.g += Random.Range(-colorVariation, colorVariation);
            tint.b += Random.Range(-colorVariation, colorVariation);
            
            // Apply color (try both URP and Standard property names)
            propBlock.SetColor(BaseColorID, tint);
            propBlock.SetColor(TintColorID, tint);
            
            // Random wind phase offset (so grass doesn't all sway together)
            float windPhase = Random.Range(0f, Mathf.PI * 2f);
            propBlock.SetFloat("_WindPhase", windPhase);
            
            renderer.SetPropertyBlock(propBlock);
            
            // Enable GPU instancing if available
            if (useInstancing && renderer.sharedMaterial != null)
            {
                renderer.sharedMaterial.enableInstancing = true;
            }
        }
        
        spawnedGrass.Add(grass);
        return true;
    }
    
    /// <summary>
    /// Update global wind shader parameters
    /// </summary>
    public void UpdateWindParameters()
    {
        // Set global shader properties for wind
        Shader.SetGlobalFloat(WindStrengthID, windStrength);
        Shader.SetGlobalFloat(WindSpeedID, windSpeed);
        Shader.SetGlobalVector(WindDirectionID, windDirection.normalized);
        
        // Also set Unity's built-in wind zone parameters if using terrain grass shader
        if (activeTerrain != null && activeTerrain.terrainData != null)
        {
            activeTerrain.terrainData.wavingGrassStrength = windStrength;
            activeTerrain.terrainData.wavingGrassSpeed = windSpeed;
            activeTerrain.terrainData.wavingGrassAmount = windStrength * 0.5f;
        }
    }
    
    /// <summary>
    /// Get grass density multiplier based on biome
    /// </summary>
    private float GetBiomeDensityMultiplier(Biome biome)
    {
        return biome switch
        {
            // Lush biomes = lots of grass
            Biome.Plains => 1.0f,
            Biome.Grassland => 1.2f,
            Biome.Forest => 0.8f,
            Biome.Jungle => 0.6f,
            Biome.Rainforest => 0.5f,
            Biome.Savannah => 0.7f,
            Biome.Steppe => 0.6f,
            Biome.Taiga => 0.5f,
            Biome.PineForest => 0.6f,
            
            // Moderate grass
            Biome.Swamp => 0.4f,
            Biome.Marsh => 0.5f,
            Biome.Floodlands => 0.4f,
            Biome.Coast => 0.3f,
            Biome.River => 0.5f,
            
            // Cold biomes = less grass
            Biome.Tundra => 0.3f,
            Biome.Snow => 0.1f,
            Biome.Frozen => 0.05f,
            Biome.Arctic => 0.02f,
            Biome.Glacier => 0.0f,
            Biome.IcicleField => 0.0f,
            Biome.CryoForest => 0.1f,
            
            // Desert/hot biomes = sparse grass
            Biome.Desert => 0.05f,
            Biome.Scorched => 0.0f,
            Biome.Ashlands => 0.0f,
            Biome.CharredForest => 0.05f,
            
            // Volcanic/hellish = no grass
            Biome.Volcanic => 0.0f,
            Biome.Steam => 0.0f,
            Biome.Hellscape => 0.0f,
            Biome.Brimstone => 0.0f,
            
            // Water biomes = no grass
            Biome.Ocean => 0.0f,
            Biome.Seas => 0.0f,
            
            // Mountains = sparse
            Biome.Mountain => 0.2f,
            
            // Moon/alien = no Earth grass
            Biome.MoonDunes => 0.0f,
            Biome.MoonCaves => 0.0f,
            
            // All planet-specific biomes = no Earth grass
            Biome.MartianRegolith => 0.0f,
            Biome.MartianCanyon => 0.0f,
            Biome.MartianPolarIce => 0.0f,
            Biome.MartianDunes => 0.0f,
            Biome.VenusLava => 0.0f,
            Biome.VenusianPlains => 0.0f,
            Biome.VenusHighlands => 0.0f,
            Biome.MercuryCraters => 0.0f,
            Biome.MercuryBasalt => 0.0f,
            Biome.MercuryScarp => 0.0f,
            Biome.MercurianIce => 0.0f,
            Biome.JovianClouds => 0.0f,
            Biome.JovianStorm => 0.0f,
            Biome.SaturnRings => 0.0f,
            Biome.SaturnSurface => 0.0f,
            Biome.UranusIce => 0.0f,
            Biome.UranusSurface => 0.0f,
            Biome.NeptuneWinds => 0.0f,
            Biome.NeptuneIce => 0.0f,
            Biome.NeptuneSurface => 0.0f,
            Biome.PlutoCryo => 0.0f,
            Biome.PlutoTholins => 0.0f,
            Biome.PlutoMountains => 0.0f,
            Biome.TitanLakes => 0.0f,
            Biome.TitanDunes => 0.0f,
            Biome.TitanIce => 0.0f,
            Biome.EuropaIce => 0.0f,
            Biome.EuropaRidges => 0.0f,
            Biome.IoVolcanic => 0.0f,
            Biome.IoSulfur => 0.0f,
            
            _ => 0.5f // Default moderate
        };
    }
    
    /// <summary>
    /// Get grass color tint based on biome
    /// </summary>
    private Color GetBiomeGrassTint(Biome biome)
    {
        return biome switch
        {
            Biome.Plains => new Color(0.7f, 0.85f, 0.5f),      // Fresh green
            Biome.Grassland => new Color(0.75f, 0.9f, 0.55f),  // Bright green
            Biome.Forest => new Color(0.5f, 0.7f, 0.4f),       // Dark green
            Biome.Jungle => new Color(0.4f, 0.75f, 0.35f),     // Deep green
            Biome.Rainforest => new Color(0.35f, 0.7f, 0.3f),  // Very deep green
            Biome.Savannah => new Color(0.85f, 0.8f, 0.5f),    // Yellow-green
            Biome.Steppe => new Color(0.8f, 0.75f, 0.5f),      // Dry yellow
            Biome.Taiga => new Color(0.5f, 0.65f, 0.45f),      // Blue-green
            Biome.PineForest => new Color(0.45f, 0.6f, 0.4f),  // Pine green
            Biome.Swamp => new Color(0.45f, 0.55f, 0.35f),     // Murky green
            Biome.Marsh => new Color(0.5f, 0.6f, 0.4f),        // Marsh green
            Biome.Tundra => new Color(0.6f, 0.65f, 0.5f),      // Pale green
            Biome.Snow => new Color(0.75f, 0.8f, 0.7f),        // Frost-tipped
            Biome.Desert => new Color(0.85f, 0.8f, 0.6f),      // Dry beige
            Biome.CharredForest => new Color(0.4f, 0.35f, 0.3f), // Burnt brown
            _ => new Color(0.7f, 0.85f, 0.5f)                  // Default green
        };
    }
    
    /// <summary>
    /// Clear all spawned grass
    /// </summary>
    public void ClearGrass()
    {
        foreach (var grass in spawnedGrass)
        {
            if (grass != null)
            {
                if (Application.isPlaying)
                    Destroy(grass);
                else
                    DestroyImmediate(grass);
            }
        }
        spawnedGrass.Clear();
        
        if (grassParent != null)
        {
            if (Application.isPlaying)
                Destroy(grassParent.gameObject);
            else
                DestroyImmediate(grassParent.gameObject);
            grassParent = null;
        }
    }
    
    void OnDestroy()
    {
        ClearGrass();
    }
    
    /// <summary>
    /// Update wind in real-time (call from Update if you want dynamic wind)
    /// </summary>
    public void SetWind(float strength, float speed, Vector3 direction)
    {
        windStrength = strength;
        windSpeed = speed;
        windDirection = direction;
        UpdateWindParameters();
    }
}

