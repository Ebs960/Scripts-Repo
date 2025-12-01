using UnityEngine;

/// <summary>
/// Terrain generator for Martian canyons (Valles Marineris style)
/// Creates deep, dramatic canyon systems with layered walls, 
/// landslide debris, and canyon floor features
/// </summary>
public class MarsCanyonGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Canyon characteristics
    private float canyonDepth = 0.35f;          // How deep canyons are
    private float canyonWidth = 0.25f;          // Width of main canyon
    private float wallLayering = 0.6f;          // Layered cliff appearance
    private float landslideChance = 0.5f;       // Chance of landslide debris
    private float tributaryChance = 0.4f;       // Chance of side canyons
    
    public MarsCanyonGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.5f,
            noiseScale = 0.03f,
            roughness = 0.4f,
            hilliness = 0.5f,
            mountainSharpness = 0.4f,
            octaves = 5,
            lacunarity = 2.1f,
            persistence = 0.5f,
            hillThreshold = 0.3f,
            mountainThreshold = 0.55f,
            maxHeightVariation = 10f,
            useErosion = true,
            erosionStrength = 0.3f
        };
        
        terrainSettings = new BiomeTerrainSettings
        {
            baseElevation = 0.55f,
            heightScale = 0.85f,
            baseFrequency = 0.015f,
            detailFrequency = 0.05f,
            ridgeFrequency = 0.025f,
            valleyFrequency = 0.02f,
            baseWeight = 0.35f,
            detailWeight = 0.2f,
            ridgeWeight = 0.25f,
            valleyWeight = 0.2f,
            ridgeSharpness = 2.2f,
            useDomainWarping = true,
            domainWarpStrength = 30f
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
        
        // Start with elevated plateau
        float plateauHeight = 0.6f + elevation * 0.15f;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                heights[y, x] = plateauHeight;
            }
        }
        
        // Add plateau surface variation
        AddPlateauSurface(heights, resolution, mapSize, seed, plateauHeight);
        
        // Carve main canyon system
        float canyonAngle = Random.Range(0f, Mathf.PI);
        CarveCanyonSystem(heights, resolution, mapSize, seed, canyonAngle);
        
        // Add tributary canyons
        if (Random.value < tributaryChance)
        {
            AddTributaryCanyons(heights, resolution, mapSize, seed, canyonAngle);
        }
        
        // Add layered cliff walls
        AddLayeredCliffs(heights, resolution, mapSize, seed);
        
        // Add landslide debris
        if (Random.value < landslideChance)
        {
            AddLandslideDebris(heights, resolution, mapSize, seed);
        }
        
        // Add canyon floor features
        AddCanyonFloorFeatures(heights, resolution, mapSize, seed);
        
        // Add surface detail
        AddSurfaceDetail(heights, resolution, mapSize);
        
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
    /// Add variation to plateau surface
    /// </summary>
    private void AddPlateauSurface(float[,] heights, int resolution, float mapSize, int seed, float plateauHeight)
    {
        FastNoiseLite surfaceNoise = new FastNoiseLite(seed + 13000);
        surfaceNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        surfaceNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        surfaceNoise.SetFractalOctaves(4);
        surfaceNoise.SetFrequency(0.025f);
        
        float variation = 0.08f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float surface = surfaceNoise.GetNoise(worldX, worldZ);
                heights[z, x] = plateauHeight + surface * variation;
            }
        }
    }
    
    /// <summary>
    /// Carve the main canyon system
    /// </summary>
    private void CarveCanyonSystem(float[,] heights, int resolution, float mapSize, int seed, float canyonAngle)
    {
        // Canyon path with meanders
        FastNoiseLite pathWarp = new FastNoiseLite(seed + 13100);
        pathWarp.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        pathWarp.SetFrequency(0.02f);
        pathWarp.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2);
        pathWarp.SetDomainWarpAmp(25f);
        
        // Width variation
        FastNoiseLite widthNoise = new FastNoiseLite(seed + 13200);
        widthNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        widthNoise.SetFrequency(0.03f);
        
        float baseWidth = canyonWidth * mapSize;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Warp for meandering canyon
                float warpX = worldX;
                float warpZ = worldZ;
                pathWarp.DomainWarp(ref warpX, ref warpZ);
                
                // Rotate to canyon alignment
                float centerX = mapSize * 0.5f;
                float centerZ = mapSize * 0.5f;
                float relX = warpX - centerX;
                float relZ = warpZ - centerZ;
                
                float acrossCanyon = relX * Mathf.Cos(canyonAngle) + relZ * Mathf.Sin(canyonAngle);
                float alongCanyon = -relX * Mathf.Sin(canyonAngle) + relZ * Mathf.Cos(canyonAngle);
                
                // Width variation along canyon
                float widthMod = widthNoise.GetNoise(worldX, worldZ);
                widthMod = 0.7f + (widthMod + 1f) * 0.3f; // 0.7 to 1.3
                float localWidth = baseWidth * widthMod;
                
                // Distance from canyon center
                float distFromCenter = Mathf.Abs(acrossCanyon);
                
                if (distFromCenter < localWidth)
                {
                    // Inside canyon
                    float canyonT = distFromCenter / localWidth;
                    
                    // Canyon profile: steep walls with flat-ish floor
                    float canyonProfile;
                    if (canyonT < 0.6f)
                    {
                        // Canyon floor (relatively flat with some variation)
                        canyonProfile = canyonDepth;
                    }
                    else
                    {
                        // Canyon walls (steep)
                        float wallT = (canyonT - 0.6f) / 0.4f;
                        // Stepped wall profile for layered appearance
                        canyonProfile = canyonDepth * (1f - Mathf.Pow(wallT, 0.4f));
                    }
                    
                    heights[z, x] -= canyonProfile;
                }
                else if (distFromCenter < localWidth * 1.3f)
                {
                    // Canyon rim area (slightly eroded)
                    float rimT = (distFromCenter - localWidth) / (localWidth * 0.3f);
                    float rimErosion = 0.03f * (1f - rimT);
                    heights[z, x] -= rimErosion;
                }
            }
        }
    }
    
    /// <summary>
    /// Add tributary side canyons
    /// </summary>
    private void AddTributaryCanyons(float[,] heights, int resolution, float mapSize, int seed, float mainAngle)
    {
        int tributaryCount = Random.Range(2, 5);
        
        Random.InitState(seed + 13300);
        
        for (int t = 0; t < tributaryCount; t++)
        {
            // Tributaries branch off at angles
            float branchAngle = mainAngle + (Random.value > 0.5f ? 1 : -1) * Random.Range(0.4f, 0.8f);
            float branchX = Random.Range(mapSize * 0.2f, mapSize * 0.8f);
            float branchZ = Random.Range(mapSize * 0.2f, mapSize * 0.8f);
            float branchLength = Random.Range(mapSize * 0.15f, mapSize * 0.35f);
            float branchWidth = canyonWidth * mapSize * Random.Range(0.3f, 0.5f);
            float branchDepth = canyonDepth * Random.Range(0.5f, 0.8f);
            
            CarveTributary(heights, resolution, mapSize, branchX, branchZ, branchAngle, branchLength, branchWidth, branchDepth);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    private void CarveTributary(float[,] heights, int resolution, float mapSize,
                               float startX, float startZ, float angle, float length, float width, float depth)
    {
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float relX = worldX - startX;
                float relZ = worldZ - startZ;
                
                // Rotate to tributary alignment
                float along = relX * Mathf.Cos(angle) + relZ * Mathf.Sin(angle);
                float across = -relX * Mathf.Sin(angle) + relZ * Mathf.Cos(angle);
                
                if (along < 0 || along > length) continue;
                
                // Width tapers toward head
                float taperT = along / length;
                float localWidth = width * (1f - taperT * 0.6f);
                
                float distFromCenter = Mathf.Abs(across);
                
                if (distFromCenter < localWidth)
                {
                    float canyonT = distFromCenter / localWidth;
                    float localDepth = depth * (1f - taperT * 0.5f); // Shallower toward head
                    
                    float tributaryProfile;
                    if (canyonT < 0.5f)
                    {
                        tributaryProfile = localDepth;
                    }
                    else
                    {
                        float wallT = (canyonT - 0.5f) / 0.5f;
                        tributaryProfile = localDepth * (1f - Mathf.Pow(wallT, 0.5f));
                    }
                    
                    heights[z, x] = Mathf.Min(heights[z, x], heights[z, x] - tributaryProfile);
                }
            }
        }
    }
    
    /// <summary>
    /// Add layered cliff appearance (horizontal strata)
    /// </summary>
    private void AddLayeredCliffs(float[,] heights, int resolution, float mapSize, int seed)
    {
        if (wallLayering <= 0) return;
        
        FastNoiseLite layerNoise = new FastNoiseLite(seed + 13400);
        layerNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        layerNoise.SetFrequency(0.05f);
        
        int numLayers = 5;
        float layerStep = 0.06f;
        float layerStrength = 0.02f * wallLayering;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float currentHeight = heights[z, x];
                
                // Only apply to cliff areas (steep slopes)
                // Check gradient would be expensive, so use height bands instead
                for (int layer = 0; layer < numLayers; layer++)
                {
                    float layerHeight = 0.3f + layer * layerStep;
                    float distFromLayer = Mathf.Abs(currentHeight - layerHeight);
                    
                    if (distFromLayer < layerStep * 0.3f)
                    {
                        // Create ledge
                        float ledgeStrength = 1f - (distFromLayer / (layerStep * 0.3f));
                        float layerVariation = layerNoise.GetNoise(worldX + layer * 100f, worldZ);
                        
                        heights[z, x] += layerStrength * ledgeStrength * (0.5f + layerVariation * 0.5f);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Add landslide debris at canyon bases
    /// </summary>
    private void AddLandslideDebris(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite debrisNoise = new FastNoiseLite(seed + 13500);
        debrisNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        debrisNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.Euclidean);
        debrisNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance);
        debrisNoise.SetFrequency(0.08f);
        
        float debrisHeight = 0.04f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float currentHeight = heights[z, x];
                
                // Only add debris on canyon floor
                if (currentHeight < 0.35f)
                {
                    float debris = debrisNoise.GetNoise(worldX, worldZ);
                    debris = (debris + 1f) * 0.5f;
                    
                    // Create blocky debris piles
                    if (debris < 0.4f)
                    {
                        float debrisStrength = (0.4f - debris) / 0.4f;
                        heights[z, x] += debrisHeight * debrisStrength;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Add canyon floor features (ancient lake beds, sediment layers)
    /// </summary>
    private void AddCanyonFloorFeatures(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite floorNoise = new FastNoiseLite(seed + 13600);
        floorNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        floorNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        floorNoise.SetFractalOctaves(3);
        floorNoise.SetFrequency(0.04f);
        
        float floorVariation = 0.025f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float currentHeight = heights[z, x];
                
                // Only affect canyon floor
                if (currentHeight < 0.32f)
                {
                    float floorFeature = floorNoise.GetNoise(worldX, worldZ);
                    heights[z, x] += floorFeature * floorVariation;
                }
            }
        }
    }
    
    /// <summary>
    /// Add fine surface detail
    /// </summary>
    private void AddSurfaceDetail(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite detailNoise = new FastNoiseLite(Random.Range(1, 100000));
        detailNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        detailNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        detailNoise.SetFractalOctaves(3);
        detailNoise.SetFrequency(0.12f);
        
        float detailAmount = 0.01f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float detail = detailNoise.GetNoise(worldX, worldZ);
                heights[z, x] += detail * detailAmount;
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

