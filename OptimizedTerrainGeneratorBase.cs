using UnityEngine;

/// <summary>
/// Base class for optimized terrain generators.
/// Implements AAA performance techniques:
/// - Pooled noise generators
/// - Quality-based feature skipping
/// - Single-pass generation where possible
/// - Cache-friendly memory access
/// </summary>
public abstract class OptimizedTerrainGeneratorBase : IBiomeTerrainGenerator
{
    // Cached noise generators (avoid creating new each frame)
    protected FastNoiseLite baseNoise;
    protected FastNoiseLite detailNoise;
    protected FastNoiseLite featureNoise;
    
    // Pre-computed parameters
    protected int currentSeed;
    protected bool noiseInitialized = false;
    
    // Performance tracking
    protected float lastGenerationTimeMs;
    
    /// <summary>
    /// Initialize noise generators once (call at start of Generate)
    /// </summary>
    protected virtual void InitializeNoise(int seed)
    {
        if (noiseInitialized && currentSeed == seed) return;
        
        currentSeed = seed;
        
        // Get pooled noise generators
        baseNoise = TerrainGenerationOptimizer.GetNoiseGenerator(seed);
        detailNoise = TerrainGenerationOptimizer.GetNoiseGenerator(seed + 1000);
        featureNoise = TerrainGenerationOptimizer.GetNoiseGenerator(seed + 2000);
        
        // Configure base noise
        baseNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        baseNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        baseNoise.SetFractalOctaves(GetNoiseOctaves());
        baseNoise.SetFrequency(GetBaseFrequency());
        
        // Configure detail noise
        detailNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        detailNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        detailNoise.SetFractalOctaves(2); // Always low for detail
        detailNoise.SetFrequency(0.1f);
        
        ConfigureFeatureNoise(featureNoise);
        
        noiseInitialized = true;
    }
    
    /// <summary>
    /// Override to configure biome-specific feature noise
    /// </summary>
    protected virtual void ConfigureFeatureNoise(FastNoiseLite noise)
    {
        noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        noise.SetFrequency(0.05f);
    }
    
    /// <summary>
    /// Get octave count based on quality setting
    /// </summary>
    protected int GetNoiseOctaves()
    {
        return TerrainGenerationSettings.GetQuality() switch
        {
            TerrainQuality.Ultra => 5,
            TerrainQuality.High => 4,
            TerrainQuality.Medium => 3,
            TerrainQuality.Low => 2,
            TerrainQuality.VeryLow => 1,
            _ => 3
        };
    }
    
    /// <summary>
    /// Override to specify base noise frequency
    /// </summary>
    protected virtual float GetBaseFrequency() => 0.02f;
    
    /// <summary>
    /// Main generation method - optimized single-pass where possible
    /// </summary>
    public virtual void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        float startTime = Time.realtimeSinceStartup;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        
        // Initialize noise generators
        int seed = Random.Range(1, 100000);
        InitializeNoise(seed);
        
        // Get feature flags for quality level
        TerrainFeatureFlags features = TerrainGenerationSettings.GetFeatures();
        
        // Try to use cached heightmap
        string cacheKey = TerrainGenerationOptimizer.GenerateCacheKey(GetBiomeType(), elevation, moisture, seed);
        if (TerrainGenerationOptimizer.TryGetCachedHeightmap(cacheKey, out float[,] cachedHeights))
        {
            if (cachedHeights.GetLength(0) == resolution)
            {
                terrainData.SetHeights(0, 0, cachedHeights);
                lastGenerationTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                Debug.Log($"[{GetType().Name}] Used cached heightmap ({lastGenerationTimeMs:F1}ms)");
                return;
            }
        }
        
        // Generate terrain in optimized single pass
        float[,] heights = new float[resolution, resolution];
        
        GenerateTerrain(heights, resolution, mapSize, elevation, moisture, temperature, features, seed);
        
        // Cache for future use
        TerrainGenerationOptimizer.CacheHeightmap(cacheKey, heights);
        
        // Apply to terrain
        terrainData.SetHeights(0, 0, heights);
        
        lastGenerationTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
        Debug.Log($"[{GetType().Name}] Generated terrain ({lastGenerationTimeMs:F1}ms, quality: {TerrainGenerationSettings.GetQuality()})");
    }
    
    /// <summary>
    /// Override to implement biome-specific terrain generation
    /// This should be a SINGLE PASS where possible for performance
    /// </summary>
    protected abstract void GenerateTerrain(float[,] heights, int resolution, float mapSize,
                                           float elevation, float moisture, float temperature,
                                           TerrainFeatureFlags features, int seed);
    
    /// <summary>
    /// Override to return the biome type for caching
    /// </summary>
    protected abstract Biome GetBiomeType();
    
    /// <summary>
    /// Optimized crater application - minimal bounds checking version
    /// </summary>
    protected void ApplyCraterOptimized(float[,] heights, int resolution, float mapSize,
                                        float centerX, float centerZ, float radius,
                                        float depth, float rimHeight)
    {
        // Pre-calculate bounds once
        float invMapSize = 1f / mapSize;
        float radiusSq = radius * radius;
        float rimRadiusSq = radius * 1.1f * radius * 1.1f;
        
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius * 1.2f) * invMapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + radius * 1.2f) * invMapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius * 1.2f) * invMapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + radius * 1.2f) * invMapSize * resolution));
        
        float invRadius = 1f / radius;
        float mapSizeOverRes = mapSize / resolution;
        
        for (int z = minZ; z <= maxZ; z++)
        {
            float worldZ = z * mapSizeOverRes;
            float dz = worldZ - centerZ;
            float dzSq = dz * dz;
            
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = x * mapSizeOverRes;
                float dx = worldX - centerX;
                float distSq = dx * dx + dzSq;
                
                if (distSq > rimRadiusSq) continue;
                
                float dist = Mathf.Sqrt(distSq);
                float normalizedDist = dist * invRadius;
                
                float craterEffect;
                if (normalizedDist < 0.8f)
                {
                    float t = normalizedDist * 1.25f; // normalizedDist / 0.8f
                    craterEffect = -depth * (1f - t * t * (3f - 2f * t)); // Smoothstep
                }
                else if (normalizedDist < 1.0f)
                {
                    float t = (normalizedDist - 0.8f) * 5f; // (normalizedDist - 0.8f) / 0.2f
                    craterEffect = rimHeight * (1f - t);
                }
                else
                {
                    float t = (normalizedDist - 1f) * 10f;
                    craterEffect = rimHeight * 0.3f * Mathf.Max(0f, 1f - t);
                }
                
                heights[z, x] += craterEffect;
            }
        }
    }
    
    /// <summary>
    /// Get noise profile for compatibility
    /// </summary>
    public virtual BiomeNoiseProfile GetNoiseProfile()
    {
        return new BiomeNoiseProfile
        {
            baseHeight = 0.3f,
            noiseScale = GetBaseFrequency(),
            octaves = GetNoiseOctaves()
        };
    }
}

