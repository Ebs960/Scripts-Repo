using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Performance optimization utilities for terrain generation.
/// Implements AAA game techniques: caching, LOD, pooling, and async generation.
/// </summary>
public static class TerrainGenerationOptimizer
{
    // === NOISE GENERATOR POOLING ===
    // Reuse FastNoiseLite instances instead of creating new ones
    private static Dictionary<int, FastNoiseLite> noisePool = new Dictionary<int, FastNoiseLite>();
    private static Queue<FastNoiseLite> availableNoiseGenerators = new Queue<FastNoiseLite>();
    private const int POOL_SIZE = 20;
    
    static TerrainGenerationOptimizer()
    {
        // Pre-allocate noise generators
        for (int i = 0; i < POOL_SIZE; i++)
        {
            availableNoiseGenerators.Enqueue(new FastNoiseLite());
        }
    }
    
    /// <summary>
    /// Get a pooled noise generator (faster than creating new)
    /// </summary>
    public static FastNoiseLite GetNoiseGenerator(int seed)
    {
        // Check if we have one cached for this seed
        if (noisePool.TryGetValue(seed, out FastNoiseLite cached))
        {
            return cached;
        }
        
        // Get from pool or create new
        FastNoiseLite noise;
        if (availableNoiseGenerators.Count > 0)
        {
            noise = availableNoiseGenerators.Dequeue();
        }
        else
        {
            noise = new FastNoiseLite();
        }
        
        noise.SetSeed(seed);
        noisePool[seed] = noise;
        return noise;
    }
    
    /// <summary>
    /// Return a noise generator to the pool
    /// </summary>
    public static void ReturnNoiseGenerator(FastNoiseLite noise)
    {
        if (availableNoiseGenerators.Count < POOL_SIZE)
        {
            availableNoiseGenerators.Enqueue(noise);
        }
    }
    
    /// <summary>
    /// Clear the noise pool (call when changing scenes)
    /// </summary>
    public static void ClearNoisePool()
    {
        noisePool.Clear();
    }
    
    // === RESOLUTION SCALING ===
    
    /// <summary>
    /// Calculate optimal heightmap resolution based on map size and quality settings
    /// AAA games use adaptive resolution based on view distance
    /// </summary>
    public static int CalculateOptimalResolution(float mapSize, TerrainQuality quality)
    {
        // Base resolution on quality setting
        int baseRes = quality switch
        {
            TerrainQuality.Ultra => 513,
            TerrainQuality.High => 257,
            TerrainQuality.Medium => 129,
            TerrainQuality.Low => 65,
            TerrainQuality.VeryLow => 33,
            _ => 129
        };
        
        // Scale with map size (larger maps can use lower density)
        if (mapSize > 200f)
            baseRes = Mathf.Max(33, baseRes / 2);
        else if (mapSize > 100f)
            baseRes = Mathf.Max(65, (int)(baseRes * 0.75f));
        
        // Ensure power of 2 + 1
        return NearestPowerOfTwoPlusOne(baseRes);
    }
    
    private static int NearestPowerOfTwoPlusOne(int value)
    {
        int[] validSizes = { 33, 65, 129, 257, 513, 1025 };
        foreach (int size in validSizes)
        {
            if (value <= size) return size;
        }
        return 513;
    }
    
    // === LOD-BASED FEATURE SKIPPING ===
    
    /// <summary>
    /// Determine which features to generate based on quality setting
    /// Lower quality = skip expensive features
    /// </summary>
    public static TerrainFeatureFlags GetFeaturesForQuality(TerrainQuality quality)
    {
        return quality switch
        {
            TerrainQuality.Ultra => TerrainFeatureFlags.All,
            TerrainQuality.High => TerrainFeatureFlags.Base | TerrainFeatureFlags.Craters | 
                                   TerrainFeatureFlags.Ridges | TerrainFeatureFlags.Detail,
            TerrainQuality.Medium => TerrainFeatureFlags.Base | TerrainFeatureFlags.Craters | 
                                     TerrainFeatureFlags.Ridges,
            TerrainQuality.Low => TerrainFeatureFlags.Base | TerrainFeatureFlags.Craters,
            TerrainQuality.VeryLow => TerrainFeatureFlags.Base,
            _ => TerrainFeatureFlags.Base | TerrainFeatureFlags.Craters
        };
    }
    
    // === CRATER OPTIMIZATION ===
    
    /// <summary>
    /// Calculate reduced crater count based on quality
    /// </summary>
    public static int OptimizeCraterCount(int baseCraterCount, TerrainQuality quality)
    {
        float multiplier = quality switch
        {
            TerrainQuality.Ultra => 1.0f,
            TerrainQuality.High => 0.7f,
            TerrainQuality.Medium => 0.4f,
            TerrainQuality.Low => 0.2f,
            TerrainQuality.VeryLow => 0.1f,
            _ => 0.5f
        };
        
        return Mathf.Max(1, Mathf.RoundToInt(baseCraterCount * multiplier));
    }
    
    // === HEIGHTMAP CACHING ===
    
    private static Dictionary<string, float[,]> heightmapCache = new Dictionary<string, float[,]>();
    private const int MAX_CACHED_HEIGHTMAPS = 5;
    
    /// <summary>
    /// Cache a generated heightmap for reuse
    /// </summary>
    public static void CacheHeightmap(string key, float[,] heights)
    {
        // Limit cache size
        if (heightmapCache.Count >= MAX_CACHED_HEIGHTMAPS)
        {
            // Remove oldest entry
            var firstKey = new List<string>(heightmapCache.Keys)[0];
            heightmapCache.Remove(firstKey);
        }
        
        // Clone the array (don't store reference)
        int size = heights.GetLength(0);
        float[,] cached = new float[size, size];
        System.Array.Copy(heights, cached, heights.Length);
        heightmapCache[key] = cached;
    }
    
    /// <summary>
    /// Try to get a cached heightmap
    /// </summary>
    public static bool TryGetCachedHeightmap(string key, out float[,] heights)
    {
        return heightmapCache.TryGetValue(key, out heights);
    }
    
    /// <summary>
    /// Generate a cache key for terrain parameters
    /// </summary>
    public static string GenerateCacheKey(Biome biome, float elevation, float moisture, int seed)
    {
        return $"{biome}_{elevation:F2}_{moisture:F2}_{seed}";
    }
    
    /// <summary>
    /// Clear heightmap cache
    /// </summary>
    public static void ClearHeightmapCache()
    {
        heightmapCache.Clear();
    }
    
    // === BATCH NOISE SAMPLING ===
    
    /// <summary>
    /// Sample noise in batches for better cache coherency
    /// This is faster than random access patterns
    /// </summary>
    public static void BatchSampleNoise(FastNoiseLite noise, float[,] output, 
                                        int resolution, float mapSize, float scale = 1f)
    {
        // Process in cache-friendly row order
        for (int z = 0; z < resolution; z++)
        {
            float worldZ = (z / (float)resolution) * mapSize;
            
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                output[z, x] = noise.GetNoise(worldX * scale, worldZ * scale);
            }
        }
    }
    
    // === MEMORY OPTIMIZATION ===
    
    /// <summary>
    /// Reusable heightmap array to avoid allocations
    /// </summary>
    private static float[,] reusableHeightmap;
    private static int reusableHeightmapSize;
    
    /// <summary>
    /// Get a reusable heightmap array (avoids GC allocations)
    /// </summary>
    public static float[,] GetReusableHeightmap(int resolution)
    {
        if (reusableHeightmap == null || reusableHeightmapSize != resolution)
        {
            reusableHeightmap = new float[resolution, resolution];
            reusableHeightmapSize = resolution;
        }
        else
        {
            // Clear existing data
            System.Array.Clear(reusableHeightmap, 0, reusableHeightmap.Length);
        }
        
        return reusableHeightmap;
    }
}

/// <summary>
/// Terrain quality levels for LOD system
/// </summary>
public enum TerrainQuality
{
    VeryLow,    // Minimal features, fastest
    Low,        // Basic terrain only
    Medium,     // Standard quality (default)
    High,       // Full features
    Ultra       // Maximum detail
}

/// <summary>
/// Flags for which terrain features to generate
/// </summary>
[System.Flags]
public enum TerrainFeatureFlags
{
    None = 0,
    Base = 1 << 0,          // Base terrain shape
    Craters = 1 << 1,       // Impact craters
    Ridges = 1 << 2,        // Ridges/mountains
    Valleys = 1 << 3,       // Valleys/channels
    Detail = 1 << 4,        // Fine surface detail
    Special = 1 << 5,       // Biome-specific features (corona, spirals, etc.)
    
    All = Base | Craters | Ridges | Valleys | Detail | Special
}

/// <summary>
/// Global terrain generation settings
/// Attach to a GameObject in your scene to configure quality
/// </summary>
public class TerrainGenerationSettings : MonoBehaviour
{
    public static TerrainGenerationSettings Instance { get; private set; }
    
    [Header("Quality Settings")]
    [Tooltip("Overall terrain quality level")]
    public TerrainQuality quality = TerrainQuality.Medium;
    
    [Header("Performance Limits")]
    [Tooltip("Maximum crater count regardless of settings")]
    [Range(5, 200)]
    public int maxCraters = 50;
    
    [Tooltip("Maximum noise passes per terrain")]
    [Range(1, 8)]
    public int maxNoisePasses = 4;
    
    [Header("Generation Timing")]
    [Tooltip("Spread generation across multiple frames")]
    public bool useAsyncGeneration = true;
    
    [Tooltip("Maximum milliseconds per frame for terrain generation")]
    [Range(5, 50)]
    public int maxMsPerFrame = 16; // Target 60fps
    
    [Header("Caching")]
    [Tooltip("Cache generated heightmaps for reuse")]
    public bool enableHeightmapCaching = true;
    
    [Tooltip("Cache noise generator instances")]
    public bool enableNoisePooling = true;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            // Clear caches
            TerrainGenerationOptimizer.ClearNoisePool();
            TerrainGenerationOptimizer.ClearHeightmapCache();
        }
    }
    
    /// <summary>
    /// Get current quality setting (with fallback)
    /// </summary>
    public static TerrainQuality GetQuality()
    {
        return Instance != null ? Instance.quality : TerrainQuality.Medium;
    }
    
    /// <summary>
    /// Get feature flags for current quality
    /// </summary>
    public static TerrainFeatureFlags GetFeatures()
    {
        return TerrainGenerationOptimizer.GetFeaturesForQuality(GetQuality());
    }
    
    /// <summary>
    /// Check if a specific feature should be generated
    /// </summary>
    public static bool ShouldGenerateFeature(TerrainFeatureFlags feature)
    {
        return (GetFeatures() & feature) != 0;
    }
}

