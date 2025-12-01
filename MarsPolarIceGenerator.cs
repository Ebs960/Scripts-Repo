using UnityEngine;

/// <summary>
/// Terrain generator for Martian polar ice caps
/// Creates layered ice deposits, spiral troughs, and sublimation features
/// unique to Mars's polar regions
/// </summary>
public class MarsPolarIceGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Polar ice characteristics
    private float iceCapHeight = 0.4f;          // Height of ice cap
    private float spiralTroughDepth = 0.1f;     // Depth of spiral troughs
    private float spiralTroughDensity = 0.5f;   // How many spiral troughs
    private float layeredTerrainStrength = 0.6f; // Layered terrain deposits
    private float sublimationPitChance = 0.4f;  // Swiss-cheese terrain
    
    public MarsPolarIceGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.35f,
            noiseScale = 0.03f,
            roughness = 0.2f,
            hilliness = 0.25f,
            mountainSharpness = 0.15f,
            octaves = 4,
            lacunarity = 2.0f,
            persistence = 0.45f,
            hillThreshold = 0.45f,
            mountainThreshold = 0.75f,
            maxHeightVariation = 5f,
            useErosion = false, // No liquid water erosion at poles
            erosionStrength = 0f
        };
        
        terrainSettings = new BiomeTerrainSettings
        {
            baseElevation = 0.35f,
            heightScale = 0.5f,
            baseFrequency = 0.02f,
            detailFrequency = 0.05f,
            ridgeFrequency = 0.03f,
            valleyFrequency = 0.025f,
            baseWeight = 0.5f,
            detailWeight = 0.2f,
            ridgeWeight = 0.15f,
            valleyWeight = 0.15f,
            ridgeSharpness = 1.2f,
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
        
        // Generate base ice cap dome
        GenerateIceCapBase(heights, resolution, mapSize, seed);
        
        // Add spiral troughs (characteristic of Mars polar caps)
        if (spiralTroughDensity > 0)
        {
            AddSpiralTroughs(heights, resolution, mapSize, seed);
        }
        
        // Add layered terrain deposits
        if (layeredTerrainStrength > 0)
        {
            AddLayeredDeposits(heights, resolution, mapSize, seed);
        }
        
        // Add sublimation pits ("Swiss cheese" terrain)
        if (Random.value < sublimationPitChance)
        {
            AddSublimationPits(heights, resolution, mapSize, seed);
        }
        
        // Add frost polygons
        AddFrostPolygons(heights, resolution, mapSize, seed);
        
        // Add fine ice texture
        AddIceTexture(heights, resolution, mapSize);
        
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
    /// Generate the base ice cap shape (dome with plateau)
    /// </summary>
    private void GenerateIceCapBase(float[,] heights, int resolution, float mapSize, int seed)
    {
        float centerX = mapSize * 0.5f;
        float centerZ = mapSize * 0.5f;
        float capRadius = mapSize * 0.45f;
        
        FastNoiseLite edgeNoise = new FastNoiseLite(seed + 14000);
        edgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        edgeNoise.SetFrequency(0.03f);
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float distFromCenter = Mathf.Sqrt((worldX - centerX) * (worldX - centerX) +
                                                  (worldZ - centerZ) * (worldZ - centerZ));
                
                // Edge variation
                float edgeVariation = edgeNoise.GetNoise(worldX, worldZ) * capRadius * 0.15f;
                float localRadius = capRadius + edgeVariation;
                
                float normalizedDist = distFromCenter / localRadius;
                
                if (normalizedDist < 1f)
                {
                    // Inside ice cap - create dome shape
                    float capProfile;
                    if (normalizedDist < 0.7f)
                    {
                        // Flat top plateau
                        capProfile = iceCapHeight;
                    }
                    else
                    {
                        // Sloping edges
                        float edgeT = (normalizedDist - 0.7f) / 0.3f;
                        capProfile = iceCapHeight * (1f - Mathf.Pow(edgeT, 0.6f));
                    }
                    
                    heights[z, x] = capProfile;
                }
                else
                {
                    // Outside ice cap - lower surrounding terrain
                    float outsideT = Mathf.Min(1f, (normalizedDist - 1f) / 0.3f);
                    heights[z, x] = iceCapHeight * 0.15f * (1f - outsideT) + 0.15f;
                }
            }
        }
    }
    
    /// <summary>
    /// Add spiral troughs - characteristic curving valleys in Mars polar ice
    /// </summary>
    private void AddSpiralTroughs(float[,] heights, int resolution, float mapSize, int seed)
    {
        float centerX = mapSize * 0.5f;
        float centerZ = mapSize * 0.5f;
        
        // Archimedean spiral parameters
        float spiralTightness = 0.015f;
        int numSpirals = Mathf.RoundToInt(spiralTroughDensity * 4f);
        
        Random.InitState(seed + 14100);
        
        for (int s = 0; s < numSpirals; s++)
        {
            float spiralPhase = (s / (float)numSpirals) * Mathf.PI * 2f;
            float spiralDirection = Random.value > 0.5f ? 1f : -1f; // Clockwise or counter
            
            ApplySpiralTrough(heights, resolution, mapSize, centerX, centerZ, 
                            spiralPhase, spiralDirection, spiralTightness);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    private void ApplySpiralTrough(float[,] heights, int resolution, float mapSize,
                                   float centerX, float centerZ, float phase, float direction, float tightness)
    {
        float troughWidth = mapSize * 0.03f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float relX = worldX - centerX;
                float relZ = worldZ - centerZ;
                
                float dist = Mathf.Sqrt(relX * relX + relZ * relZ);
                float angle = Mathf.Atan2(relZ, relX);
                
                // Calculate expected radius for this angle on the spiral
                float spiralAngle = angle * direction + phase;
                // Normalize angle to positive range
                while (spiralAngle < 0) spiralAngle += Mathf.PI * 2f;
                
                // Check multiple spiral arms
                for (int arm = 0; arm < 8; arm++)
                {
                    float armAngle = spiralAngle + arm * Mathf.PI * 2f;
                    float expectedRadius = armAngle / tightness;
                    
                    if (expectedRadius > mapSize * 0.5f) continue;
                    if (expectedRadius < mapSize * 0.1f) continue;
                    
                    float distFromSpiral = Mathf.Abs(dist - expectedRadius);
                    
                    if (distFromSpiral < troughWidth)
                    {
                        float troughStrength = 1f - (distFromSpiral / troughWidth);
                        troughStrength = Mathf.SmoothStep(0f, 1f, troughStrength);
                        
                        // Carve trough
                        heights[z, x] -= spiralTroughDepth * troughStrength;
                        break; // Only apply one spiral arm per point
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Add layered terrain deposits (alternating ice and dust layers)
    /// </summary>
    private void AddLayeredDeposits(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite layerNoise = new FastNoiseLite(seed + 14200);
        layerNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        layerNoise.SetFrequency(0.04f);
        
        int numLayers = 8;
        float layerSpacing = 0.04f;
        float layerDepth = 0.015f * layeredTerrainStrength;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float currentHeight = heights[z, x];
                
                // Apply layers where there's slope
                for (int layer = 0; layer < numLayers; layer++)
                {
                    float layerHeight = 0.15f + layer * layerSpacing;
                    float distFromLayer = currentHeight - layerHeight;
                    
                    if (distFromLayer > 0 && distFromLayer < layerSpacing * 0.4f)
                    {
                        // Create subtle ledge
                        float ledgeT = distFromLayer / (layerSpacing * 0.4f);
                        float layerVariation = layerNoise.GetNoise(worldX + layer * 50f, worldZ);
                        
                        float ledgeEffect = layerDepth * (1f - ledgeT) * (0.5f + layerVariation * 0.5f);
                        heights[z, x] += ledgeEffect;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Add sublimation pits ("Swiss cheese" terrain from CO2 sublimation)
    /// </summary>
    private void AddSublimationPits(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite pitNoise = new FastNoiseLite(seed + 14300);
        pitNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        pitNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
        pitNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance);
        pitNoise.SetFrequency(0.06f);
        
        float pitDepth = 0.05f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Only on ice cap surface
                if (heights[z, x] > iceCapHeight * 0.7f)
                {
                    float pitValue = pitNoise.GetNoise(worldX, worldZ);
                    pitValue = (pitValue + 1f) * 0.5f;
                    
                    // Create circular pits
                    if (pitValue < 0.25f)
                    {
                        float pitStrength = (0.25f - pitValue) / 0.25f;
                        pitStrength = Mathf.Pow(pitStrength, 0.7f);
                        
                        // Pit with steep walls
                        heights[z, x] -= pitDepth * pitStrength;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Add frost polygon patterns
    /// </summary>
    private void AddFrostPolygons(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite polygonNoise = new FastNoiseLite(seed + 14400);
        polygonNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        polygonNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
        polygonNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance2Add);
        polygonNoise.SetFrequency(0.08f);
        
        float polygonHeight = 0.01f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float polygon = polygonNoise.GetNoise(worldX, worldZ);
                
                // Create raised edges along polygon boundaries
                heights[z, x] += polygon * polygonHeight;
            }
        }
    }
    
    /// <summary>
    /// Add fine ice surface texture
    /// </summary>
    private void AddIceTexture(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite iceNoise = new FastNoiseLite(Random.Range(1, 100000));
        iceNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        iceNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        iceNoise.SetFractalOctaves(3);
        iceNoise.SetFrequency(0.15f);
        
        float iceAmount = 0.006f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float ice = iceNoise.GetNoise(worldX, worldZ);
                heights[z, x] += ice * iceAmount;
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

