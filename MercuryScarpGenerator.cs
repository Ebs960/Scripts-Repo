using UnityEngine;

/// <summary>
/// Terrain generator for Mercury's lobate scarps (rupes)
/// Creates terrain dominated by massive cliff structures formed when Mercury cooled and shrank
/// Scarps can be hundreds of kilometers long and up to 3km high on the real Mercury
/// </summary>
public class MercuryScarpGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Scarp characteristics
    private float scarpDensity = 0.6f;         // How many scarps
    private float scarpHeight = 0.2f;          // Height of cliff faces
    private float scarpSharpness = 2.5f;       // How sharp the cliff edge is
    private float craterDensity = 0.4f;        // Moderate craters (scarps are newer features)
    
    public MercuryScarpGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.35f,
            noiseScale = 0.035f,
            roughness = 0.3f,
            hilliness = 0.4f,
            mountainSharpness = 0.35f,
            octaves = 4,
            lacunarity = 2.1f,
            persistence = 0.45f,
            hillThreshold = 0.35f,
            mountainThreshold = 0.6f,
            maxHeightVariation = 8f,
            useErosion = false,
            erosionStrength = 0f
        };
        
        terrainSettings = new BiomeTerrainSettings
        {
            baseElevation = 0.35f,
            heightScale = 0.7f,
            baseFrequency = 0.02f,
            detailFrequency = 0.06f,
            ridgeFrequency = 0.025f,
            valleyFrequency = 0.02f,
            baseWeight = 0.45f,
            detailWeight = 0.2f,
            ridgeWeight = 0.2f,
            valleyWeight = 0.15f,
            ridgeSharpness = 2.0f,
            useDomainWarping = true,
            domainWarpStrength = 25f
        };
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        int seed = Random.Range(1, 100000);
        noiseSystem = new BattleTerrainNoiseSystem(seed);
        
        // Elevation affects base terrain
        terrainSettings.baseElevation = 0.3f + elevation * 0.15f;
        
        // Generate base terrain
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                heights[y, x] = height;
            }
        }
        
        // Generate lobate scarps (the defining feature)
        GenerateLobateScarps(heights, resolution, mapSize, seed);
        
        // Add moderate crater coverage (scarps cut through older craters)
        GenerateCraters(heights, resolution, mapSize, seed);
        
        // Add wrinkle ridges (common on Mercury)
        AddWrinkleRidges(heights, resolution, mapSize, seed);
        
        // Add surface texture
        AddSurfaceTexture(heights, resolution, mapSize);
        
        // Clamp heights
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                heights[y, x] = Mathf.Clamp01(heights[y, x]);
            }
        }
        
        terrainData.SetHeights(0, 0, heights);
    }
    
    /// <summary>
    /// Generate lobate scarps - long, curved cliff structures
    /// These are thrust faults formed as Mercury's core cooled and the planet shrank
    /// </summary>
    private void GenerateLobateScarps(float[,] heights, int resolution, float mapSize, int seed)
    {
        int scarpCount = Mathf.RoundToInt(scarpDensity * 3f); // Usually 1-3 major scarps
        
        Random.InitState(seed + 9000);
        
        for (int s = 0; s < scarpCount; s++)
        {
            // Scarp parameters
            float startX = Random.Range(0f, mapSize);
            float startZ = Random.Range(0f, mapSize);
            float scarpAngle = Random.Range(0f, Mathf.PI * 2f);
            float scarpLength = Random.Range(mapSize * 0.4f, mapSize * 0.8f);
            float scarpCurvature = Random.Range(-0.02f, 0.02f); // Lobate = curved
            float thisScarpHeight = scarpHeight * Random.Range(0.7f, 1.3f);
            
            ApplyLobateScarp(heights, resolution, mapSize, startX, startZ, 
                           scarpAngle, scarpLength, scarpCurvature, thisScarpHeight);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    /// <summary>
    /// Apply a single lobate scarp to the terrain
    /// </summary>
    private void ApplyLobateScarp(float[,] heights, int resolution, float mapSize,
                                  float startX, float startZ, float angle, 
                                  float length, float curvature, float height)
    {
        // Create scarp using signed distance field approach
        FastNoiseLite scarpWarp = new FastNoiseLite(Random.Range(1, 100000));
        scarpWarp.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        scarpWarp.SetFrequency(0.03f);
        
        float scarpWidth = 15f; // Width of transition zone
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Calculate distance to scarp line (curved)
                float relX = worldX - startX;
                float relZ = worldZ - startZ;
                
                // Rotate to scarp's coordinate system
                float along = relX * Mathf.Cos(angle) + relZ * Mathf.Sin(angle);
                float across = -relX * Mathf.Sin(angle) + relZ * Mathf.Cos(angle);
                
                // Apply curvature
                across -= curvature * along * along;
                
                // Only affect area along the scarp length
                if (along < 0 || along > length) continue;
                
                // Taper at ends
                float endTaper = 1f;
                if (along < length * 0.15f)
                    endTaper = along / (length * 0.15f);
                else if (along > length * 0.85f)
                    endTaper = (length - along) / (length * 0.15f);
                
                // Add some natural waviness to the scarp
                float warp = scarpWarp.GetNoise(worldX, worldZ) * 5f;
                across += warp;
                
                // Calculate scarp effect based on distance from fault line
                float scarpEffect = 0f;
                
                if (Mathf.Abs(across) < scarpWidth)
                {
                    // Steep cliff transition
                    float t = across / scarpWidth; // -1 to 1
                    
                    // Sigmoid-like function for sharp cliff
                    float cliffProfile;
                    if (t > 0)
                    {
                        // High side
                        cliffProfile = 1f - Mathf.Pow(1f - t, scarpSharpness);
                    }
                    else
                    {
                        // Low side
                        cliffProfile = -Mathf.Pow(1f + t, scarpSharpness);
                    }
                    
                    scarpEffect = height * cliffProfile * 0.5f * endTaper;
                }
                else if (across > scarpWidth && across < scarpWidth * 3f)
                {
                    // Gradual rise on high side
                    float riseT = (across - scarpWidth) / (scarpWidth * 2f);
                    scarpEffect = height * 0.5f * (1f - riseT) * endTaper;
                }
                else if (across < -scarpWidth && across > -scarpWidth * 2f)
                {
                    // Gradual drop on low side
                    float dropT = (-across - scarpWidth) / scarpWidth;
                    scarpEffect = -height * 0.3f * (1f - dropT) * endTaper;
                }
                
                heights[z, x] += scarpEffect;
            }
        }
    }
    
    /// <summary>
    /// Generate moderate crater coverage
    /// </summary>
    private void GenerateCraters(float[,] heights, int resolution, float mapSize, int seed)
    {
        int craterCount = Mathf.RoundToInt(craterDensity * mapSize * 0.3f);
        
        Random.InitState(seed + 9100);
        
        for (int c = 0; c < craterCount; c++)
        {
            float craterX = Random.Range(0f, mapSize);
            float craterZ = Random.Range(0f, mapSize);
            
            float sizeT = Mathf.Pow(Random.value, 1.5f);
            float radius = Mathf.Lerp(2f, 25f, sizeT);
            float depth = 0.08f * (0.5f + sizeT * 0.5f);
            float rimHeight = 0.04f * (0.5f + sizeT * 0.5f);
            
            ApplyCrater(heights, resolution, mapSize, craterX, craterZ, radius, depth, rimHeight);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    private void ApplyCrater(float[,] heights, int resolution, float mapSize,
                            float centerX, float centerZ, float radius, float depth, float rimHeight)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius * 1.3f) / mapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + radius * 1.3f) / mapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius * 1.3f) / mapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + radius * 1.3f) / mapSize * resolution));
        
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float dist = Mathf.Sqrt((worldX - centerX) * (worldX - centerX) +
                                       (worldZ - centerZ) * (worldZ - centerZ));
                
                if (dist > radius * 1.2f) continue;
                
                float normalizedDist = dist / radius;
                float craterEffect = 0f;
                
                if (normalizedDist < 0.8f)
                {
                    float floorProfile = Mathf.Pow(normalizedDist / 0.8f, 0.7f);
                    craterEffect = -depth * (1f - floorProfile);
                }
                else if (normalizedDist < 1.05f)
                {
                    float rimProfile = 1f - Mathf.Abs(normalizedDist - 0.9f) / 0.15f;
                    craterEffect = rimHeight * Mathf.Max(0, rimProfile);
                }
                
                heights[z, x] += craterEffect;
            }
        }
    }
    
    /// <summary>
    /// Add wrinkle ridges that often parallel the scarps
    /// </summary>
    private void AddWrinkleRidges(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite ridgeNoise = new FastNoiseLite(seed + 9200);
        ridgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        ridgeNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        ridgeNoise.SetFractalOctaves(2);
        ridgeNoise.SetFrequency(0.02f);
        
        float ridgeHeight = 0.03f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float ridgeValue = ridgeNoise.GetNoise(worldX, worldZ);
                ridgeValue = (ridgeValue + 1f) * 0.5f;
                
                if (ridgeValue > 0.65f)
                {
                    float ridgeStrength = (ridgeValue - 0.65f) / 0.35f;
                    heights[z, x] += ridgeHeight * ridgeStrength;
                }
            }
        }
    }
    
    /// <summary>
    /// Add surface texture
    /// </summary>
    private void AddSurfaceTexture(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite textureNoise = new FastNoiseLite(Random.Range(1, 100000));
        textureNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        textureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        textureNoise.SetFractalOctaves(3);
        textureNoise.SetFrequency(0.12f);
        
        float textureAmount = 0.012f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float texture = textureNoise.GetNoise(worldX, worldZ);
                heights[z, x] += texture * textureAmount;
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

