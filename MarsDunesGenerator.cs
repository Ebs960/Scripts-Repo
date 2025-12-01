using UnityEngine;

/// <summary>
/// Terrain generator for Martian sand dunes
/// Creates Martian dune fields similar to those found at the base of Olympus Mons
/// and in polar regions - barchan dunes, linear dunes, and dune fields
/// </summary>
public class MarsDunesGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Mars dune characteristics
    private float duneHeight = 0.12f;           // Height of major dunes
    private float duneFrequency = 0.04f;        // Spacing between dunes
    private float barchanChance = 0.4f;         // Chance of crescent-shaped barchans
    private float interdunePlainHeight = 0.25f; // Height of flat areas between dunes
    
    public MarsDunesGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.25f,
            noiseScale = 0.04f,
            roughness = 0.15f,
            hilliness = 0.2f,
            mountainSharpness = 0.1f,
            octaves = 3,
            lacunarity = 2.2f,
            persistence = 0.4f,
            hillThreshold = 0.5f,
            mountainThreshold = 0.8f,
            maxHeightVariation = 4f,
            useErosion = false,
            erosionStrength = 0f
        };
        
        terrainSettings = new BiomeTerrainSettings
        {
            baseElevation = 0.25f,
            heightScale = 0.4f,
            baseFrequency = 0.015f,
            detailFrequency = 0.04f,
            ridgeFrequency = 0.025f,
            valleyFrequency = 0.02f,
            baseWeight = 0.6f,
            detailWeight = 0.15f,
            ridgeWeight = 0.15f,
            valleyWeight = 0.1f,
            ridgeSharpness = 1.0f,
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
        
        // Start with flat interdune plains
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                heights[y, x] = interdunePlainHeight;
            }
        }
        
        // Determine dominant wind direction
        float windAngle = Random.Range(0f, Mathf.PI * 2f);
        
        // Generate main dune ridges (transverse/linear dunes)
        GenerateLinearDunes(heights, resolution, mapSize, seed, windAngle);
        
        // Add barchan dunes (crescent-shaped)
        if (Random.value < barchanChance)
        {
            GenerateBarchanDunes(heights, resolution, mapSize, seed, windAngle);
        }
        
        // Add dune ripples (small-scale features)
        AddDuneRipples(heights, resolution, mapSize, seed, windAngle);
        
        // Add subtle variation to interdune areas
        AddInterduneVariation(heights, resolution, mapSize, seed);
        
        // Add fine sand texture
        AddSandTexture(heights, resolution, mapSize);
        
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
    /// Generate linear/transverse dunes perpendicular to wind direction
    /// </summary>
    private void GenerateLinearDunes(float[,] heights, int resolution, float mapSize, int seed, float windAngle)
    {
        FastNoiseLite duneNoise = new FastNoiseLite(seed + 12000);
        duneNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        duneNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        duneNoise.SetFractalOctaves(3);
        duneNoise.SetFrequency(duneFrequency);
        
        // Warp for natural irregularity
        FastNoiseLite warpNoise = new FastNoiseLite(seed + 12100);
        warpNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        warpNoise.SetFrequency(0.02f);
        
        // Dunes form perpendicular to wind
        float duneAngle = windAngle + Mathf.PI * 0.5f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Rotate coordinates to dune alignment
                float rotX = worldX * Mathf.Cos(duneAngle) - worldZ * Mathf.Sin(duneAngle);
                float rotZ = worldX * Mathf.Sin(duneAngle) + worldZ * Mathf.Cos(duneAngle);
                
                // Add natural warp
                float warp = warpNoise.GetNoise(worldX, worldZ) * 10f;
                rotX += warp;
                
                // Get dune ridge pattern
                float duneValue = duneNoise.GetNoise(rotX, rotZ * 0.3f); // Stretch along dune crests
                duneValue = (duneValue + 1f) * 0.5f;
                
                // Create asymmetric dune profile (gentle windward, steep leeward)
                float duneProfile = CreateAsymmetricDuneProfile(duneValue);
                
                heights[z, x] += duneProfile * duneHeight;
            }
        }
    }
    
    /// <summary>
    /// Create asymmetric dune cross-section (gentle windward slope, steep slip face)
    /// </summary>
    private float CreateAsymmetricDuneProfile(float t)
    {
        // Transform ridged noise into asymmetric dune shape
        if (t < 0.3f)
        {
            // Interdune trough
            return 0f;
        }
        else if (t < 0.7f)
        {
            // Windward slope (gentle)
            float windwardT = (t - 0.3f) / 0.4f;
            return Mathf.Pow(windwardT, 0.7f);
        }
        else if (t < 0.85f)
        {
            // Crest
            return 1f;
        }
        else
        {
            // Leeward slip face (steep)
            float leewardT = (t - 0.85f) / 0.15f;
            return 1f - Mathf.Pow(leewardT, 0.4f);
        }
    }
    
    /// <summary>
    /// Generate barchan (crescent-shaped) dunes
    /// </summary>
    private void GenerateBarchanDunes(float[,] heights, int resolution, float mapSize, int seed, float windAngle)
    {
        int barchanCount = Mathf.RoundToInt(mapSize * 0.15f);
        
        Random.InitState(seed + 12200);
        
        for (int b = 0; b < barchanCount; b++)
        {
            float barchanX = Random.Range(mapSize * 0.1f, mapSize * 0.9f);
            float barchanZ = Random.Range(mapSize * 0.1f, mapSize * 0.9f);
            float barchanSize = Random.Range(8f, 20f);
            float barchanHeight = duneHeight * Random.Range(0.6f, 1.0f);
            
            ApplyBarchanDune(heights, resolution, mapSize, barchanX, barchanZ, barchanSize, barchanHeight, windAngle);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    /// <summary>
    /// Apply a single barchan dune
    /// </summary>
    private void ApplyBarchanDune(float[,] heights, int resolution, float mapSize,
                                  float centerX, float centerZ, float size, float height, float windAngle)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - size * 1.5f) / mapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + size * 1.5f) / mapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - size * 1.5f) / mapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + size * 1.5f) / mapSize * resolution));
        
        // Wind direction vectors
        float windX = Mathf.Cos(windAngle);
        float windZ = Mathf.Sin(windAngle);
        
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float relX = worldX - centerX;
                float relZ = worldZ - centerZ;
                
                // Rotate to wind-aligned coordinates
                float alongWind = relX * windX + relZ * windZ;
                float acrossWind = -relX * windZ + relZ * windX;
                
                // Barchan shape: crescent with horns pointing downwind
                float distFromCenter = Mathf.Sqrt(relX * relX + relZ * relZ);
                
                if (distFromCenter > size * 1.3f) continue;
                
                // Create crescent shape
                float normalizedDist = distFromCenter / size;
                
                // Horns extend downwind on the sides
                float hornEffect = 0f;
                if (Mathf.Abs(acrossWind) > size * 0.3f && alongWind > 0)
                {
                    // In horn region
                    float hornStrength = (Mathf.Abs(acrossWind) - size * 0.3f) / (size * 0.5f);
                    hornEffect = Mathf.Clamp01(hornStrength) * 0.5f;
                }
                
                // Main body (elliptical, elongated crosswind)
                float bodyX = alongWind / (size * (0.8f - hornEffect));
                float bodyZ = acrossWind / (size * 1.2f);
                float bodyDist = Mathf.Sqrt(bodyX * bodyX + bodyZ * bodyZ);
                
                if (bodyDist < 1f)
                {
                    // Inside dune body
                    float duneEffect;
                    
                    if (alongWind < 0)
                    {
                        // Windward side (gentle slope)
                        duneEffect = (1f - bodyDist) * (0.5f - alongWind / size);
                    }
                    else
                    {
                        // Leeward side (steep slip face)
                        duneEffect = (1f - bodyDist) * Mathf.Max(0, 1f - alongWind / (size * 0.5f));
                    }
                    
                    duneEffect = Mathf.Clamp01(duneEffect);
                    heights[z, x] = Mathf.Max(heights[z, x], interdunePlainHeight + height * duneEffect);
                }
            }
        }
    }
    
    /// <summary>
    /// Add small-scale dune ripples
    /// </summary>
    private void AddDuneRipples(float[,] heights, int resolution, float mapSize, int seed, float windAngle)
    {
        FastNoiseLite rippleNoise = new FastNoiseLite(seed + 12300);
        rippleNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        rippleNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        rippleNoise.SetFractalOctaves(2);
        rippleNoise.SetFrequency(0.2f);
        
        float rippleAngle = windAngle + Mathf.PI * 0.5f;
        float rippleHeight = 0.008f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Rotate to ripple alignment
                float rotX = worldX * Mathf.Cos(rippleAngle) - worldZ * Mathf.Sin(rippleAngle);
                
                float rippleValue = rippleNoise.GetNoise(rotX, worldZ * 0.5f);
                rippleValue = (rippleValue + 1f) * 0.5f;
                
                heights[z, x] += rippleValue * rippleHeight;
            }
        }
    }
    
    /// <summary>
    /// Add variation to interdune flat areas
    /// </summary>
    private void AddInterduneVariation(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite interduneNoise = new FastNoiseLite(seed + 12400);
        interduneNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        interduneNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        interduneNoise.SetFractalOctaves(2);
        interduneNoise.SetFrequency(0.03f);
        
        float variationAmount = 0.02f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Only add variation to lower areas (interdune plains)
                if (heights[z, x] < interdunePlainHeight + 0.03f)
                {
                    float variation = interduneNoise.GetNoise(worldX, worldZ);
                    heights[z, x] += variation * variationAmount;
                }
            }
        }
    }
    
    /// <summary>
    /// Add fine sand texture
    /// </summary>
    private void AddSandTexture(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite sandNoise = new FastNoiseLite(Random.Range(1, 100000));
        sandNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        sandNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        sandNoise.SetFractalOctaves(3);
        sandNoise.SetFrequency(0.15f);
        
        float sandAmount = 0.005f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float sand = sandNoise.GetNoise(worldX, worldZ);
                heights[z, x] += sand * sandAmount;
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

