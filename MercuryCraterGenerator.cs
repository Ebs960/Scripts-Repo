using UnityEngine;

/// <summary>
/// Terrain generator for Mercury's heavily cratered highlands
/// Creates dense impact crater coverage with sharp rims (no erosion on airless Mercury)
/// Mercury has some of the oldest, most cratered terrain in the solar system
/// </summary>
public class MercuryCraterGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Heavy crater characteristics
    private float craterDensity = 0.9f;        // Very heavily cratered
    private float craterSizeMin = 2f;
    private float craterSizeMax = 40f;
    private float craterDepth = 0.18f;
    private float craterRimHeight = 0.1f;
    private float intercraterPlainChance = 0.2f; // Some flat areas between craters
    
    public MercuryCraterGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.3f,
            noiseScale = 0.04f,
            roughness = 0.35f,
            hilliness = 0.45f,
            mountainSharpness = 0.25f,
            octaves = 4,
            lacunarity = 2.2f,
            persistence = 0.45f,
            hillThreshold = 0.35f,
            mountainThreshold = 0.65f,
            maxHeightVariation = 7f,
            useErosion = false, // No erosion on airless Mercury
            erosionStrength = 0f
        };
        
        terrainSettings = new BiomeTerrainSettings
        {
            baseElevation = 0.35f,
            heightScale = 0.75f,
            baseFrequency = 0.025f,
            detailFrequency = 0.07f,
            ridgeFrequency = 0.04f,
            valleyFrequency = 0.03f,
            baseWeight = 0.4f,
            detailWeight = 0.25f,
            ridgeWeight = 0.2f,
            valleyWeight = 0.15f,
            ridgeSharpness = 1.6f,
            useDomainWarping = true,
            domainWarpStrength = 20f
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
        
        // Adjust based on elevation (higher = more highland terrain)
        terrainSettings.baseElevation = 0.25f + elevation * 0.2f;
        
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
        
        // Generate HEAVY crater coverage
        GenerateHeavyCraters(heights, resolution, mapSize, seed);
        
        // Add secondary craters (from ejecta)
        GenerateSecondaryCraters(heights, resolution, mapSize, seed);
        
        // Add intercrater plains in some areas
        if (Random.value < intercraterPlainChance)
        {
            AddIntercraterPlains(heights, resolution, mapSize, seed);
        }
        
        // Add regolith detail
        AddRegolithDetail(heights, resolution, mapSize);
        
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
    /// Generate very dense crater coverage
    /// </summary>
    private void GenerateHeavyCraters(float[,] heights, int resolution, float mapSize, int seed)
    {
        // Multiple passes for crater saturation
        int largeCraterCount = Mathf.RoundToInt(craterDensity * mapSize * 0.15f);
        int mediumCraterCount = Mathf.RoundToInt(craterDensity * mapSize * 0.4f);
        int smallCraterCount = Mathf.RoundToInt(craterDensity * mapSize * 0.8f);
        
        Random.InitState(seed + 8000);
        
        // Large craters first (oldest)
        for (int c = 0; c < largeCraterCount; c++)
        {
            float craterX = Random.Range(0f, mapSize);
            float craterZ = Random.Range(0f, mapSize);
            float radius = Random.Range(craterSizeMax * 0.6f, craterSizeMax);
            float depth = craterDepth * 0.9f;
            float rimHeight = craterRimHeight * 0.8f;
            
            ApplyCrater(heights, resolution, mapSize, craterX, craterZ, radius, depth, rimHeight, true);
        }
        
        // Medium craters (overlap large ones)
        for (int c = 0; c < mediumCraterCount; c++)
        {
            float craterX = Random.Range(0f, mapSize);
            float craterZ = Random.Range(0f, mapSize);
            float radius = Random.Range(craterSizeMax * 0.2f, craterSizeMax * 0.5f);
            float depth = craterDepth * 0.7f;
            float rimHeight = craterRimHeight * 0.7f;
            
            ApplyCrater(heights, resolution, mapSize, craterX, craterZ, radius, depth, rimHeight, false);
        }
        
        // Small craters (most recent, on top of everything)
        for (int c = 0; c < smallCraterCount; c++)
        {
            float craterX = Random.Range(0f, mapSize);
            float craterZ = Random.Range(0f, mapSize);
            float radius = Random.Range(craterSizeMin, craterSizeMax * 0.2f);
            float depth = craterDepth * 0.5f;
            float rimHeight = craterRimHeight * 0.5f;
            
            ApplyCrater(heights, resolution, mapSize, craterX, craterZ, radius, depth, rimHeight, false);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    /// <summary>
    /// Apply a single crater with sharp, well-preserved rims (Mercury has no erosion)
    /// </summary>
    private void ApplyCrater(float[,] heights, int resolution, float mapSize,
                            float centerX, float centerZ, float radius,
                            float depth, float rimHeight, bool hasCentralPeak)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius * 1.5f) / mapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + radius * 1.5f) / mapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius * 1.5f) / mapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + radius * 1.5f) / mapSize * resolution));
        
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float dist = Mathf.Sqrt((worldX - centerX) * (worldX - centerX) +
                                       (worldZ - centerZ) * (worldZ - centerZ));
                
                if (dist > radius * 1.4f) continue;
                
                float normalizedDist = dist / radius;
                float craterEffect = 0f;
                
                if (normalizedDist < 0.75f)
                {
                    // Crater floor
                    float floorProfile = Mathf.Pow(normalizedDist / 0.75f, 0.6f);
                    craterEffect = -depth * (1f - floorProfile);
                    
                    // Central peak for large craters
                    if (hasCentralPeak && normalizedDist < 0.12f)
                    {
                        float peakProfile = 1f - (normalizedDist / 0.12f);
                        peakProfile = Mathf.Pow(peakProfile, 0.7f);
                        craterEffect += depth * 0.4f * peakProfile;
                    }
                }
                else if (normalizedDist < 1.0f)
                {
                    // Sharp crater rim (well-preserved on Mercury)
                    float rimProfile = 1f - Mathf.Abs(normalizedDist - 0.88f) / 0.12f;
                    rimProfile = Mathf.Pow(Mathf.Max(0, rimProfile), 0.6f); // Sharp peak
                    craterEffect = rimHeight * rimProfile;
                }
                else if (normalizedDist < 1.35f)
                {
                    // Ejecta blanket
                    float ejectaProfile = 1f - (normalizedDist - 1f) / 0.35f;
                    ejectaProfile = Mathf.Pow(ejectaProfile, 1.5f);
                    craterEffect = rimHeight * 0.4f * ejectaProfile;
                    
                    // Add some ejecta texture
                    float ejectaNoise = Mathf.PerlinNoise(worldX * 0.5f, worldZ * 0.5f);
                    craterEffect += ejectaNoise * 0.02f * ejectaProfile;
                }
                
                heights[z, x] += craterEffect;
            }
        }
    }
    
    /// <summary>
    /// Generate secondary craters (formed by ejecta from large impacts)
    /// These are smaller and often form in chains/clusters
    /// </summary>
    private void GenerateSecondaryCraters(float[,] heights, int resolution, float mapSize, int seed)
    {
        int clusterCount = Mathf.RoundToInt(mapSize * 0.1f);
        
        Random.InitState(seed + 8500);
        
        for (int cluster = 0; cluster < clusterCount; cluster++)
        {
            // Cluster center
            float clusterX = Random.Range(0f, mapSize);
            float clusterZ = Random.Range(0f, mapSize);
            float clusterRadius = Random.Range(10f, 25f);
            int cratersInCluster = Random.Range(3, 8);
            
            // Direction of cluster (secondary craters often radiate from source)
            float clusterAngle = Random.Range(0f, Mathf.PI * 2f);
            
            for (int c = 0; c < cratersInCluster; c++)
            {
                // Position along cluster direction
                float dist = Random.Range(0f, clusterRadius);
                float angle = clusterAngle + Random.Range(-0.3f, 0.3f);
                
                float craterX = clusterX + Mathf.Cos(angle) * dist;
                float craterZ = clusterZ + Mathf.Sin(angle) * dist;
                
                // Small secondary craters
                float radius = Random.Range(1f, 4f);
                float depth = 0.02f;
                float rimHeight = 0.01f;
                
                if (craterX >= 0 && craterX < mapSize && craterZ >= 0 && craterZ < mapSize)
                {
                    ApplyCrater(heights, resolution, mapSize, craterX, craterZ, radius, depth, rimHeight, false);
                }
            }
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    /// <summary>
    /// Add intercrater plains - smoother areas between heavily cratered regions
    /// </summary>
    private void AddIntercraterPlains(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite plainMask = new FastNoiseLite(seed + 8600);
        plainMask.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        plainMask.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
        plainMask.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance);
        plainMask.SetFrequency(0.012f);
        
        float plainLevel = 0.3f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float mask = plainMask.GetNoise(worldX, worldZ);
                mask = (mask + 1f) * 0.5f;
                
                // Plains where cellular value is low
                if (mask < 0.25f)
                {
                    float plainStrength = 1f - (mask / 0.25f);
                    plainStrength = Mathf.SmoothStep(0f, 1f, plainStrength) * 0.5f;
                    
                    heights[z, x] = Mathf.Lerp(heights[z, x], plainLevel, plainStrength);
                }
            }
        }
    }
    
    /// <summary>
    /// Add regolith surface detail
    /// </summary>
    private void AddRegolithDetail(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite regolithNoise = new FastNoiseLite(Random.Range(1, 100000));
        regolithNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        regolithNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        regolithNoise.SetFractalOctaves(3);
        regolithNoise.SetFrequency(0.15f);
        
        float regolithAmount = 0.015f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float dust = regolithNoise.GetNoise(worldX, worldZ);
                heights[z, x] += dust * regolithAmount;
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

