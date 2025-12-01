using UnityEngine;

/// <summary>
/// Terrain generator for Venus - creates volcanic plains, shield volcanoes, tessera terrain,
/// lava channels, and corona formations unique to Venus's hellish surface
/// </summary>
public class VenusTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Venus-specific terrain features
    private float volcanoeDensity = 0.4f;        // Shield volcanoes (pancake domes)
    private float volcanoMinRadius = 8f;
    private float volcanoMaxRadius = 30f;
    private float volcanoHeight = 0.2f;
    private float lavaChannelDensity = 0.3f;     // Lava channels (canali)
    private float tesseraChance = 0.4f;          // Tessera (deformed highland terrain)
    private float coronaChance = 0.25f;          // Corona formations (unique circular features)
    
    public VenusTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.35f,
            noiseScale = 0.035f,
            roughness = 0.4f,
            hilliness = 0.5f,
            mountainSharpness = 0.3f,
            octaves = 5,
            lacunarity = 2.1f,
            persistence = 0.5f,
            hillThreshold = 0.4f,
            mountainThreshold = 0.7f,
            maxHeightVariation = 8f,
            useErosion = false, // No water erosion, but has some weathering
            erosionStrength = 0f
        };
        
        terrainSettings = BiomeTerrainSettings.CreateVenus();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system
        int seed = Random.Range(1, 100000);
        noiseSystem = new BattleTerrainNoiseSystem(seed);
        
        // Venus has two main terrain types: lowland plains and highland tessera
        // Elevation parameter determines highland vs lowland
        bool isHighland = elevation > 0.6f;
        
        // Adjust settings for highlands vs lowlands
        if (isHighland)
        {
            terrainSettings.baseElevation = 0.45f;
            terrainSettings.ridgeWeight = 0.25f;
            terrainSettings.heightScale = 0.9f;
        }
        else
        {
            terrainSettings.baseElevation = 0.25f;
            terrainSettings.ridgeWeight = 0.1f;
            terrainSettings.heightScale = 0.6f;
        }
        
        // Generate base volcanic plains
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
        
        // Add tessera terrain (deformed, folded highlands)
        if (isHighland || Random.value < tesseraChance)
        {
            AddTesseraTerrain(heights, resolution, mapSize, seed);
        }
        
        // Add shield volcanoes (pancake domes)
        GenerateShieldVolcanoes(heights, resolution, mapSize, seed);
        
        // Add lava channels (canali)
        if (Random.value < lavaChannelDensity)
        {
            GenerateLavaChannels(heights, resolution, mapSize, seed);
        }
        
        // Add corona formations
        if (Random.value < coronaChance)
        {
            GenerateCorona(heights, resolution, mapSize, seed);
        }
        
        // Add fine volcanic detail
        AddVolcanicDetail(heights, resolution, mapSize);
        
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
    /// Add tessera terrain - intensely deformed, folded terrain found in Venus highlands
    /// </summary>
    private void AddTesseraTerrain(float[,] heights, int resolution, float mapSize, int seed)
    {
        // Tessera uses multi-directional ridges creating a chaotic, folded appearance
        FastNoiseLite tesseraNoise1 = new FastNoiseLite(seed + 3000);
        tesseraNoise1.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        tesseraNoise1.SetFractalType(FastNoiseLite.FractalType.Ridged);
        tesseraNoise1.SetFractalOctaves(4);
        tesseraNoise1.SetFrequency(0.03f);
        
        FastNoiseLite tesseraNoise2 = new FastNoiseLite(seed + 3500);
        tesseraNoise2.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        tesseraNoise2.SetFractalType(FastNoiseLite.FractalType.Ridged);
        tesseraNoise2.SetFractalOctaves(3);
        tesseraNoise2.SetFrequency(0.05f);
        tesseraNoise2.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2);
        tesseraNoise2.SetDomainWarpAmp(30f);
        
        // Mask for where tessera appears
        FastNoiseLite tesseraMask = new FastNoiseLite(seed + 3100);
        tesseraMask.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        tesseraMask.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
        tesseraMask.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance);
        tesseraMask.SetFrequency(0.01f);
        
        float tesseraIntensity = 0.12f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Get tessera mask
                float mask = tesseraMask.GetNoise(worldX, worldZ);
                mask = (mask + 1f) * 0.5f;
                
                if (mask > 0.6f) // Only in certain regions
                {
                    float maskStrength = (mask - 0.6f) / 0.4f;
                    maskStrength = Mathf.SmoothStep(0f, 1f, maskStrength);
                    
                    // Combine two ridge patterns at different angles
                    float ridge1 = tesseraNoise1.GetNoise(worldX, worldZ);
                    float ridge2 = tesseraNoise2.GetNoise(worldX * 0.8f + worldZ * 0.6f, worldX * 0.6f - worldZ * 0.8f);
                    
                    float tessera = (ridge1 + ridge2) * 0.5f;
                    heights[z, x] += tessera * tesseraIntensity * maskStrength;
                }
            }
        }
    }
    
    /// <summary>
    /// Generate shield volcanoes (pancake domes) - flat-topped volcanic structures unique to Venus
    /// </summary>
    private void GenerateShieldVolcanoes(float[,] heights, int resolution, float mapSize, int seed)
    {
        int volcanoCount = Mathf.RoundToInt(volcanoeDensity * mapSize * 0.3f);
        
        Random.InitState(seed + 4000);
        
        for (int v = 0; v < volcanoCount; v++)
        {
            float volcanoX = Random.Range(0f, mapSize);
            float volcanoZ = Random.Range(0f, mapSize);
            
            // Size distribution - more small volcanoes
            float sizeT = Mathf.Pow(Random.value, 1.5f);
            float radius = Mathf.Lerp(volcanoMinRadius, volcanoMaxRadius, sizeT);
            float height = volcanoHeight * (0.4f + sizeT * 0.6f);
            
            // Venus shield volcanoes have flat tops (pancake domes)
            bool isPancakeDome = Random.value < 0.6f;
            
            ApplyVolcano(heights, resolution, mapSize, volcanoX, volcanoZ, radius, height, isPancakeDome);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    /// <summary>
    /// Apply a single volcano to the heightmap
    /// </summary>
    private void ApplyVolcano(float[,] heights, int resolution, float mapSize,
                             float centerX, float centerZ, float radius, float height, bool flatTop)
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
                float volcanoEffect = 0f;
                
                if (flatTop)
                {
                    // Pancake dome profile - steep sides, flat top
                    if (normalizedDist < 0.7f)
                    {
                        // Flat top
                        volcanoEffect = height;
                    }
                    else if (normalizedDist < 1.0f)
                    {
                        // Steep sides
                        float sideT = (normalizedDist - 0.7f) / 0.3f;
                        volcanoEffect = height * (1f - Mathf.Pow(sideT, 0.5f));
                    }
                    else if (normalizedDist < 1.2f)
                    {
                        // Gentle apron
                        float apronT = (normalizedDist - 1f) / 0.2f;
                        volcanoEffect = height * 0.1f * (1f - apronT);
                    }
                }
                else
                {
                    // Regular shield volcano profile
                    if (normalizedDist < 0.15f)
                    {
                        // Caldera depression at summit
                        float calderaT = normalizedDist / 0.15f;
                        volcanoEffect = height * (0.85f + calderaT * 0.15f);
                    }
                    else if (normalizedDist < 1.0f)
                    {
                        // Shield slope
                        float slopeT = (normalizedDist - 0.15f) / 0.85f;
                        volcanoEffect = height * (1f - Mathf.Pow(slopeT, 1.5f));
                    }
                    else if (normalizedDist < 1.2f)
                    {
                        // Lava apron
                        float apronT = (normalizedDist - 1f) / 0.2f;
                        volcanoEffect = height * 0.15f * (1f - apronT);
                    }
                }
                
                heights[z, x] += volcanoEffect;
            }
        }
    }
    
    /// <summary>
    /// Generate lava channels (canali) - long, winding channels carved by lava
    /// </summary>
    private void GenerateLavaChannels(float[,] heights, int resolution, float mapSize, int seed)
    {
        // Use domain-warped noise to create winding channels
        FastNoiseLite channelNoise = new FastNoiseLite(seed + 5000);
        channelNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        channelNoise.SetFrequency(0.02f);
        channelNoise.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2Reduced);
        channelNoise.SetDomainWarpAmp(50f);
        
        FastNoiseLite channelMask = new FastNoiseLite(seed + 5100);
        channelMask.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        channelMask.SetFrequency(0.008f);
        
        float channelDepth = 0.04f;
        float channelWidth = 0.08f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Warp coordinates for winding effect
                float warpX = worldX;
                float warpZ = worldZ;
                channelNoise.DomainWarp(ref warpX, ref warpZ);
                
                // Channel value
                float channelValue = channelNoise.GetNoise(warpX, warpZ);
                
                // Mask for where channels appear
                float mask = channelMask.GetNoise(worldX, worldZ);
                mask = (mask + 1f) * 0.5f;
                
                // Create channel where noise is near zero (creates thin lines)
                float channelT = Mathf.Abs(channelValue);
                if (channelT < channelWidth && mask > 0.4f)
                {
                    float channelStrength = 1f - (channelT / channelWidth);
                    channelStrength = Mathf.SmoothStep(0f, 1f, channelStrength);
                    
                    // Carve channel
                    heights[z, x] -= channelDepth * channelStrength * (mask - 0.4f) / 0.6f;
                    
                    // Slight raised banks
                    if (channelT > channelWidth * 0.7f)
                    {
                        float bankT = (channelT - channelWidth * 0.7f) / (channelWidth * 0.3f);
                        heights[z, x] += channelDepth * 0.2f * bankT;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Generate corona formations - unique circular tectonic features on Venus
    /// </summary>
    private void GenerateCorona(float[,] heights, int resolution, float mapSize, int seed)
    {
        int coronaCount = Random.Range(1, 3);
        
        Random.InitState(seed + 6000);
        
        for (int c = 0; c < coronaCount; c++)
        {
            float coronaX = Random.Range(mapSize * 0.2f, mapSize * 0.8f);
            float coronaZ = Random.Range(mapSize * 0.2f, mapSize * 0.8f);
            float coronaRadius = Random.Range(15f, 35f);
            
            ApplyCorona(heights, resolution, mapSize, coronaX, coronaZ, coronaRadius, seed + c * 100);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    /// <summary>
    /// Apply a single corona to the heightmap
    /// </summary>
    private void ApplyCorona(float[,] heights, int resolution, float mapSize,
                            float centerX, float centerZ, float radius, int seed)
    {
        // Corona features: raised rim, central depression, concentric fractures
        FastNoiseLite fractureNoise = new FastNoiseLite(seed);
        fractureNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        fractureNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        fractureNoise.SetFractalOctaves(3);
        fractureNoise.SetFrequency(0.1f);
        
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius * 1.5f) / mapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + radius * 1.5f) / mapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius * 1.5f) / mapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + radius * 1.5f) / mapSize * resolution));
        
        float rimHeight = 0.08f;
        float centerDepth = 0.05f;
        
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
                float coronaEffect = 0f;
                
                // Central depression (moat)
                if (normalizedDist < 0.5f)
                {
                    float depthT = normalizedDist / 0.5f;
                    coronaEffect = -centerDepth * (1f - depthT);
                }
                // Inner rim
                else if (normalizedDist < 0.8f)
                {
                    float rimT = (normalizedDist - 0.5f) / 0.3f;
                    coronaEffect = rimHeight * Mathf.Sin(rimT * Mathf.PI);
                }
                // Raised outer rim
                else if (normalizedDist < 1.1f)
                {
                    float outerRimT = (normalizedDist - 0.8f) / 0.3f;
                    coronaEffect = rimHeight * 0.7f * (1f - outerRimT);
                }
                // Concentric fracture zone
                else if (normalizedDist < 1.4f)
                {
                    float fractureZoneT = (normalizedDist - 1.1f) / 0.3f;
                    float fractures = fractureNoise.GetNoise(worldX, worldZ);
                    coronaEffect = fractures * 0.03f * (1f - fractureZoneT);
                }
                
                heights[z, x] += coronaEffect;
            }
        }
    }
    
    /// <summary>
    /// Add fine volcanic detail to the surface
    /// </summary>
    private void AddVolcanicDetail(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite detailNoise = new FastNoiseLite(Random.Range(1, 100000));
        detailNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        detailNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        detailNoise.SetFractalOctaves(4);
        detailNoise.SetFrequency(0.1f);
        
        float detailAmount = 0.025f;
        
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

