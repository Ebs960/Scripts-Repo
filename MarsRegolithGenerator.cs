using UnityEngine;

/// <summary>
/// Terrain generator for Martian regolith (dusty red plains)
/// Creates the iconic red dusty Martian surface with scattered rocks, 
/// ancient riverbeds, and impact craters
/// </summary>
public class MarsRegolithGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Mars regolith characteristics
    private float rockFieldDensity = 0.4f;      // Scattered rock fields
    private float craterDensity = 0.25f;        // Some craters (less than Moon)
    private float ancientChannelChance = 0.35f; // Ancient water channels
    private float dustDevilTrackChance = 0.3f;  // Dust devil tracks
    
    public MarsRegolithGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.3f,
            noiseScale = 0.03f,
            roughness = 0.25f,
            hilliness = 0.3f,
            mountainSharpness = 0.15f,
            octaves = 4,
            lacunarity = 2.0f,
            persistence = 0.45f,
            hillThreshold = 0.4f,
            mountainThreshold = 0.7f,
            maxHeightVariation = 5f,
            useErosion = true, // Mars had ancient water erosion
            erosionStrength = 0.2f
        };
        
        terrainSettings = new BiomeTerrainSettings
        {
            baseElevation = 0.3f,
            heightScale = 0.5f,
            baseFrequency = 0.02f,
            detailFrequency = 0.06f,
            ridgeFrequency = 0.03f,
            valleyFrequency = 0.025f,
            baseWeight = 0.5f,
            detailWeight = 0.25f,
            ridgeWeight = 0.1f,
            valleyWeight = 0.15f,
            ridgeSharpness = 1.3f,
            useDomainWarping = true,
            domainWarpStrength = 15f
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
        
        // Adjust based on elevation
        terrainSettings.baseElevation = 0.25f + elevation * 0.15f;
        
        // Generate base Martian plains
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
        
        // Add scattered rock fields (small bumps)
        AddRockFields(heights, resolution, mapSize, seed);
        
        // Add impact craters (less dense than Mercury)
        GenerateCraters(heights, resolution, mapSize, seed);
        
        // Add ancient water channels (dried riverbeds)
        if (Random.value < ancientChannelChance)
        {
            AddAncientChannels(heights, resolution, mapSize, seed);
        }
        
        // Add dust devil tracks (subtle linear marks)
        if (Random.value < dustDevilTrackChance)
        {
            AddDustDevilTracks(heights, resolution, mapSize, seed);
        }
        
        // Add fine dust texture
        AddDustTexture(heights, resolution, mapSize);
        
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
    /// Add scattered rock fields - small rocks and boulders
    /// </summary>
    private void AddRockFields(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite rockNoise = new FastNoiseLite(seed + 11000);
        rockNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        rockNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.Euclidean);
        rockNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance);
        rockNoise.SetFrequency(0.15f);
        
        FastNoiseLite rockMask = new FastNoiseLite(seed + 11100);
        rockMask.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        rockMask.SetFrequency(0.02f);
        
        float rockHeight = 0.02f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Rock field mask
                float mask = rockMask.GetNoise(worldX, worldZ);
                mask = (mask + 1f) * 0.5f;
                
                if (mask > (1f - rockFieldDensity))
                {
                    float rockValue = rockNoise.GetNoise(worldX, worldZ);
                    rockValue = (rockValue + 1f) * 0.5f;
                    
                    // Create bumpy rock texture
                    if (rockValue < 0.3f)
                    {
                        float bumpStrength = (0.3f - rockValue) / 0.3f;
                        heights[z, x] += rockHeight * bumpStrength * mask;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Generate impact craters (moderate density for Mars)
    /// </summary>
    private void GenerateCraters(float[,] heights, int resolution, float mapSize, int seed)
    {
        int craterCount = Mathf.RoundToInt(craterDensity * mapSize * 0.3f);
        
        Random.InitState(seed + 11200);
        
        for (int c = 0; c < craterCount; c++)
        {
            float craterX = Random.Range(0f, mapSize);
            float craterZ = Random.Range(0f, mapSize);
            
            float sizeT = Mathf.Pow(Random.value, 1.8f);
            float radius = Mathf.Lerp(2f, 20f, sizeT);
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
                
                // Mars craters often have softer rims (some erosion)
                if (normalizedDist < 0.8f)
                {
                    float floorProfile = Mathf.Pow(normalizedDist / 0.8f, 0.7f);
                    craterEffect = -depth * (1f - floorProfile);
                }
                else if (normalizedDist < 1.05f)
                {
                    float rimProfile = 1f - Mathf.Abs(normalizedDist - 0.9f) / 0.15f;
                    rimProfile = Mathf.Pow(Mathf.Max(0, rimProfile), 0.8f); // Slightly softer rim
                    craterEffect = rimHeight * rimProfile;
                }
                
                heights[z, x] += craterEffect;
            }
        }
    }
    
    /// <summary>
    /// Add ancient water channels (dried riverbeds from Mars's wet past)
    /// </summary>
    private void AddAncientChannels(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite channelNoise = new FastNoiseLite(seed + 11300);
        channelNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        channelNoise.SetFrequency(0.015f);
        channelNoise.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2Reduced);
        channelNoise.SetDomainWarpAmp(40f);
        
        float channelDepth = 0.03f;
        float channelWidth = 0.06f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Warp for meandering channels
                float warpX = worldX;
                float warpZ = worldZ;
                channelNoise.DomainWarp(ref warpX, ref warpZ);
                
                float channelValue = channelNoise.GetNoise(warpX, warpZ);
                float channelT = Mathf.Abs(channelValue);
                
                // Create channel where noise is near zero
                if (channelT < channelWidth)
                {
                    float channelStrength = 1f - (channelT / channelWidth);
                    channelStrength = Mathf.SmoothStep(0f, 1f, channelStrength);
                    
                    // Carve channel with soft edges (eroded over time)
                    heights[z, x] -= channelDepth * channelStrength;
                }
            }
        }
    }
    
    /// <summary>
    /// Add dust devil tracks - dark streaks where dust devils have passed
    /// </summary>
    private void AddDustDevilTracks(float[,] heights, int resolution, float mapSize, int seed)
    {
        int trackCount = Random.Range(2, 6);
        
        Random.InitState(seed + 11400);
        
        for (int t = 0; t < trackCount; t++)
        {
            float startX = Random.Range(0f, mapSize);
            float startZ = Random.Range(0f, mapSize);
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float length = Random.Range(mapSize * 0.2f, mapSize * 0.5f);
            float width = Random.Range(2f, 5f);
            
            // Winding path
            float curvature = Random.Range(-0.01f, 0.01f);
            
            ApplyDustDevilTrack(heights, resolution, mapSize, startX, startZ, angle, length, width, curvature);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    private void ApplyDustDevilTrack(float[,] heights, int resolution, float mapSize,
                                     float startX, float startZ, float angle, float length, float width, float curvature)
    {
        float trackDepth = 0.005f; // Very subtle
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float relX = worldX - startX;
                float relZ = worldZ - startZ;
                
                // Rotate to track coordinate system
                float along = relX * Mathf.Cos(angle) + relZ * Mathf.Sin(angle);
                float across = -relX * Mathf.Sin(angle) + relZ * Mathf.Cos(angle);
                
                // Apply curvature
                across -= curvature * along * along;
                
                if (along < 0 || along > length) continue;
                
                // Distance from track center
                float trackDist = Mathf.Abs(across);
                
                if (trackDist < width)
                {
                    float trackStrength = 1f - (trackDist / width);
                    trackStrength = Mathf.SmoothStep(0f, 1f, trackStrength);
                    
                    // Taper at ends
                    float endTaper = 1f;
                    if (along < length * 0.1f)
                        endTaper = along / (length * 0.1f);
                    else if (along > length * 0.9f)
                        endTaper = (length - along) / (length * 0.1f);
                    
                    heights[z, x] -= trackDepth * trackStrength * endTaper;
                }
            }
        }
    }
    
    /// <summary>
    /// Add fine Martian dust texture
    /// </summary>
    private void AddDustTexture(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite dustNoise = new FastNoiseLite(Random.Range(1, 100000));
        dustNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        dustNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        dustNoise.SetFractalOctaves(3);
        dustNoise.SetFrequency(0.12f);
        
        float dustAmount = 0.008f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float dust = dustNoise.GetNoise(worldX, worldZ);
                heights[z, x] += dust * dustAmount;
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

