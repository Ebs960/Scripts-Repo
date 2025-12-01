using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Advanced terrain noise system for battle maps using FastNoiseLite.
/// Provides fractal Brownian motion (fBm), domain warping, biome masks, and smooth blending.
/// </summary>
public class BattleTerrainNoiseSystem
{
    // Noise generators using FastNoiseLite
    private FastNoiseLite baseTerrainNoise;      // Primary terrain shape
    private FastNoiseLite detailNoise;           // Fine detail layer
    private FastNoiseLite ridgeNoise;            // Mountains/ridges
    private FastNoiseLite valleyNoise;           // Valleys/erosion
    private FastNoiseLite domainWarpNoise;       // Domain warping for organic shapes
    private FastNoiseLite biomeMaskNoise;        // Biome transitions
    private FastNoiseLite featureNoise;          // Special features (rocks, pits, etc)
    
    private int seed;
    
    /// <summary>
    /// Initialize the noise system with a seed
    /// </summary>
    public BattleTerrainNoiseSystem(int seed = 0)
    {
        this.seed = seed == 0 ? Random.Range(1, 100000) : seed;
        InitializeNoiseGenerators();
    }
    
    /// <summary>
    /// Initialize all noise generators with different configurations
    /// </summary>
    private void InitializeNoiseGenerators()
    {
        // Base terrain - smooth, large features
        baseTerrainNoise = new FastNoiseLite(seed);
        baseTerrainNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        baseTerrainNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        baseTerrainNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        baseTerrainNoise.SetFractalOctaves(5);
        baseTerrainNoise.SetFractalLacunarity(2.0f);
        baseTerrainNoise.SetFractalGain(0.5f);
        baseTerrainNoise.SetFrequency(0.02f);
        
        // Detail noise - high frequency for micro-terrain
        detailNoise = new FastNoiseLite(seed + 100);
        detailNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        detailNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        detailNoise.SetFractalOctaves(4);
        detailNoise.SetFractalLacunarity(2.5f);
        detailNoise.SetFractalGain(0.45f);
        detailNoise.SetFrequency(0.1f);
        
        // Ridge noise - for mountains and ridges
        ridgeNoise = new FastNoiseLite(seed + 200);
        ridgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        ridgeNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        ridgeNoise.SetFractalOctaves(4);
        ridgeNoise.SetFractalLacunarity(2.2f);
        ridgeNoise.SetFractalGain(0.5f);
        ridgeNoise.SetFrequency(0.03f);
        
        // Valley noise - inverted ridges for valleys
        valleyNoise = new FastNoiseLite(seed + 300);
        valleyNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        valleyNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        valleyNoise.SetFractalOctaves(3);
        valleyNoise.SetFractalLacunarity(2.0f);
        valleyNoise.SetFractalGain(0.4f);
        valleyNoise.SetFrequency(0.025f);
        
        // Domain warp noise - for organic, non-uniform terrain
        domainWarpNoise = new FastNoiseLite(seed + 400);
        domainWarpNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        domainWarpNoise.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2);
        domainWarpNoise.SetDomainWarpAmp(30f);
        domainWarpNoise.SetFractalType(FastNoiseLite.FractalType.DomainWarpProgressive);
        domainWarpNoise.SetFractalOctaves(3);
        domainWarpNoise.SetFrequency(0.01f);
        
        // Biome mask noise - for smooth transitions
        biomeMaskNoise = new FastNoiseLite(seed + 500);
        biomeMaskNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        biomeMaskNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.Hybrid);
        biomeMaskNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance2Div);
        biomeMaskNoise.SetFractalType(FastNoiseLite.FractalType.None);
        biomeMaskNoise.SetFrequency(0.015f);
        
        // Feature noise - for special terrain features
        featureNoise = new FastNoiseLite(seed + 600);
        featureNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        featureNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.Euclidean);
        featureNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.CellValue);
        featureNoise.SetFrequency(0.05f);
    }
    
    /// <summary>
    /// Generate terrain height using multiple layered noise (fBm)
    /// </summary>
    public float GetTerrainHeight(float x, float z, BiomeTerrainSettings settings)
    {
        // Apply domain warping for organic shapes
        float warpX = x;
        float warpZ = z;
        if (settings.useDomainWarping)
        {
            domainWarpNoise.SetDomainWarpAmp(settings.domainWarpStrength);
            domainWarpNoise.DomainWarp(ref warpX, ref warpZ);
        }
        
        // Base terrain layer (large features)
        baseTerrainNoise.SetFrequency(settings.baseFrequency);
        float baseHeight = baseTerrainNoise.GetNoise(warpX, warpZ);
        baseHeight = (baseHeight + 1f) * 0.5f; // Normalize to 0-1
        
        // Detail layer (fine features)
        detailNoise.SetFrequency(settings.detailFrequency);
        float detail = detailNoise.GetNoise(warpX, warpZ);
        detail = (detail + 1f) * 0.5f;
        
        // Ridge layer (mountains)
        ridgeNoise.SetFrequency(settings.ridgeFrequency);
        float ridge = ridgeNoise.GetNoise(warpX, warpZ);
        ridge = (ridge + 1f) * 0.5f;
        ridge = Mathf.Pow(ridge, settings.ridgeSharpness);
        
        // Valley layer (erosion/valleys)
        valleyNoise.SetFrequency(settings.valleyFrequency);
        float valley = valleyNoise.GetNoise(warpX, warpZ);
        valley = (valley + 1f) * 0.5f;
        valley = 1f - Mathf.Pow(valley, 0.7f); // Invert and soften
        
        // Blend all layers based on biome settings
        float height = settings.baseElevation;
        height += baseHeight * settings.baseWeight;
        height += detail * settings.detailWeight;
        height += ridge * settings.ridgeWeight;
        height -= (1f - valley) * settings.valleyWeight * 0.5f; // Valleys subtract height
        
        // Apply overall height scaling
        height *= settings.heightScale;
        
        // Clamp to valid range
        return Mathf.Clamp01(height);
    }
    
    /// <summary>
    /// Generate a smooth biome transition mask
    /// Returns 0-1 where 0 is one biome and 1 is another
    /// </summary>
    public float GetBiomeMask(float x, float z, float maskScale = 1f)
    {
        biomeMaskNoise.SetFrequency(0.015f * maskScale);
        float mask = biomeMaskNoise.GetNoise(x, z);
        mask = (mask + 1f) * 0.5f;
        
        // Apply smoothstep for better transitions
        mask = SmoothStep(mask);
        
        return mask;
    }
    
    /// <summary>
    /// Generate multiple biome masks for smooth multi-biome blending
    /// Returns array of masks that sum to 1.0
    /// </summary>
    public float[] GetMultiBiomeMasks(float x, float z, int biomeCount, float scale = 1f)
    {
        float[] masks = new float[biomeCount];
        float total = 0f;
        
        for (int i = 0; i < biomeCount; i++)
        {
            // Use cellular noise with different seeds for each biome region
            FastNoiseLite biomeCellular = new FastNoiseLite(seed + 1000 + i * 100);
            biomeCellular.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
            biomeCellular.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
            biomeCellular.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance);
            biomeCellular.SetFrequency(0.02f * scale);
            
            float cellValue = biomeCellular.GetNoise(x + i * 100f, z + i * 100f);
            cellValue = 1f - (cellValue + 1f) * 0.5f; // Invert and normalize
            cellValue = Mathf.Pow(cellValue, 2f); // Sharpen
            
            masks[i] = cellValue;
            total += cellValue;
        }
        
        // Normalize so all masks sum to 1
        if (total > 0)
        {
            for (int i = 0; i < biomeCount; i++)
            {
                masks[i] /= total;
            }
        }
        
        return masks;
    }
    
    /// <summary>
    /// Get feature placement mask (for rocks, special terrain features)
    /// </summary>
    public float GetFeatureMask(float x, float z, float scale = 1f)
    {
        featureNoise.SetFrequency(0.05f * scale);
        float feature = featureNoise.GetNoise(x, z);
        feature = (feature + 1f) * 0.5f;
        return feature;
    }
    
    /// <summary>
    /// Generate erosion-influenced terrain
    /// Simulates simple hydraulic erosion effect
    /// </summary>
    public float ApplyErosionInfluence(float height, float slope, float moisture)
    {
        // Steeper slopes + more moisture = more erosion
        float erosionFactor = slope * moisture;
        float eroded = height - erosionFactor * 0.1f;
        
        // Deposit sediment in low, flat areas
        if (slope < 0.1f && height < 0.3f)
        {
            eroded += erosionFactor * 0.05f;
        }
        
        return Mathf.Clamp01(eroded);
    }
    
    /// <summary>
    /// Calculate slope at a position based on surrounding heights
    /// </summary>
    public float CalculateSlope(float[,] heights, int x, int z, int resolution)
    {
        if (x <= 0 || x >= resolution - 1 || z <= 0 || z >= resolution - 1)
            return 0f;
        
        float left = heights[z, x - 1];
        float right = heights[z, x + 1];
        float up = heights[z + 1, x];
        float down = heights[z - 1, x];
        
        float dx = (right - left) * 0.5f;
        float dz = (up - down) * 0.5f;
        
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
    
    /// <summary>
    /// Apply smoothstep function for nice transitions
    /// </summary>
    private float SmoothStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
    
    /// <summary>
    /// Apply smoother step function for even nicer transitions
    /// </summary>
    private float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}

/// <summary>
/// Settings for biome-specific terrain generation
/// </summary>
[System.Serializable]
public class BiomeTerrainSettings
{
    [Header("Height Settings")]
    [Tooltip("Base elevation (0-1)")]
    [Range(0f, 1f)]
    public float baseElevation = 0.3f;
    
    [Tooltip("Overall height scale")]
    [Range(0f, 2f)]
    public float heightScale = 1f;
    
    [Header("Noise Frequencies")]
    [Tooltip("Base terrain frequency (lower = larger features)")]
    public float baseFrequency = 0.02f;
    
    [Tooltip("Detail noise frequency")]
    public float detailFrequency = 0.1f;
    
    [Tooltip("Ridge/mountain frequency")]
    public float ridgeFrequency = 0.03f;
    
    [Tooltip("Valley frequency")]
    public float valleyFrequency = 0.025f;
    
    [Header("Layer Weights")]
    [Tooltip("Weight of base terrain layer")]
    [Range(0f, 1f)]
    public float baseWeight = 0.5f;
    
    [Tooltip("Weight of detail layer")]
    [Range(0f, 1f)]
    public float detailWeight = 0.2f;
    
    [Tooltip("Weight of ridge/mountain layer")]
    [Range(0f, 1f)]
    public float ridgeWeight = 0.0f;
    
    [Tooltip("Weight of valley/erosion layer")]
    [Range(0f, 1f)]
    public float valleyWeight = 0.1f;
    
    [Header("Shape Modifiers")]
    [Tooltip("Ridge sharpness (1 = normal, >1 = sharper peaks)")]
    [Range(0.5f, 3f)]
    public float ridgeSharpness = 1.5f;
    
    [Header("Domain Warping")]
    [Tooltip("Use domain warping for organic shapes")]
    public bool useDomainWarping = true;
    
    [Tooltip("Domain warp strength")]
    [Range(0f, 100f)]
    public float domainWarpStrength = 30f;
    
    /// <summary>
    /// Create default plains settings
    /// </summary>
    public static BiomeTerrainSettings CreatePlains()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.25f,
            heightScale = 0.6f,
            baseFrequency = 0.015f,
            detailFrequency = 0.08f,
            ridgeFrequency = 0.03f,
            valleyFrequency = 0.02f,
            baseWeight = 0.6f,
            detailWeight = 0.25f,
            ridgeWeight = 0.0f,
            valleyWeight = 0.15f,
            ridgeSharpness = 1f,
            useDomainWarping = true,
            domainWarpStrength = 20f
        };
    }
    
    /// <summary>
    /// Create desert settings (dunes, flat areas)
    /// </summary>
    public static BiomeTerrainSettings CreateDesert()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.2f,
            heightScale = 0.5f,
            baseFrequency = 0.02f,
            detailFrequency = 0.15f, // Higher for sand dunes
            ridgeFrequency = 0.04f,
            valleyFrequency = 0.025f,
            baseWeight = 0.4f,
            detailWeight = 0.4f, // More detail for dunes
            ridgeWeight = 0.1f,
            valleyWeight = 0.1f,
            ridgeSharpness = 0.8f,
            useDomainWarping = true,
            domainWarpStrength = 40f // More warping for wind-shaped dunes
        };
    }
    
    /// <summary>
    /// Create mountain settings
    /// </summary>
    public static BiomeTerrainSettings CreateMountain()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.4f,
            heightScale = 1.5f,
            baseFrequency = 0.025f,
            detailFrequency = 0.12f,
            ridgeFrequency = 0.02f,
            valleyFrequency = 0.03f,
            baseWeight = 0.3f,
            detailWeight = 0.2f,
            ridgeWeight = 0.4f, // High ridge weight for peaks
            valleyWeight = 0.2f,
            ridgeSharpness = 2.5f, // Sharp peaks
            useDomainWarping = true,
            domainWarpStrength = 50f
        };
    }
    
    /// <summary>
    /// Create forest settings (rolling hills)
    /// </summary>
    public static BiomeTerrainSettings CreateForest()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.3f,
            heightScale = 0.8f,
            baseFrequency = 0.018f,
            detailFrequency = 0.1f,
            ridgeFrequency = 0.025f,
            valleyFrequency = 0.02f,
            baseWeight = 0.5f,
            detailWeight = 0.3f,
            ridgeWeight = 0.1f,
            valleyWeight = 0.2f,
            ridgeSharpness = 1.2f,
            useDomainWarping = true,
            domainWarpStrength = 35f
        };
    }
    
    /// <summary>
    /// Create swamp settings (flat with depressions)
    /// </summary>
    public static BiomeTerrainSettings CreateSwamp()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.15f,
            heightScale = 0.4f,
            baseFrequency = 0.02f,
            detailFrequency = 0.08f,
            ridgeFrequency = 0.04f,
            valleyFrequency = 0.015f,
            baseWeight = 0.3f,
            detailWeight = 0.2f,
            ridgeWeight = 0.0f,
            valleyWeight = 0.5f, // High valley weight for water pockets
            ridgeSharpness = 1f,
            useDomainWarping = true,
            domainWarpStrength = 25f
        };
    }
    
    /// <summary>
    /// Create ice/snow settings
    /// </summary>
    public static BiomeTerrainSettings CreateIce()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.25f,
            heightScale = 0.7f,
            baseFrequency = 0.02f,
            detailFrequency = 0.06f, // Less detail (smooth ice)
            ridgeFrequency = 0.03f,
            valleyFrequency = 0.025f,
            baseWeight = 0.5f,
            detailWeight = 0.15f,
            ridgeWeight = 0.2f,
            valleyWeight = 0.15f,
            ridgeSharpness = 1.8f,
            useDomainWarping = true,
            domainWarpStrength = 20f
        };
    }
    
    /// <summary>
    /// Create volcanic settings
    /// </summary>
    public static BiomeTerrainSettings CreateVolcanic()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.35f,
            heightScale = 1.2f,
            baseFrequency = 0.025f,
            detailFrequency = 0.15f, // High detail for rocky terrain
            ridgeFrequency = 0.02f,
            valleyFrequency = 0.03f,
            baseWeight = 0.35f,
            detailWeight = 0.3f,
            ridgeWeight = 0.3f,
            valleyWeight = 0.2f, // Lava channels
            ridgeSharpness = 2.0f,
            useDomainWarping = true,
            domainWarpStrength = 45f
        };
    }
    
    /// <summary>
    /// Create ocean/flat settings
    /// </summary>
    public static BiomeTerrainSettings CreateOcean()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.1f,
            heightScale = 0.3f,
            baseFrequency = 0.01f,
            detailFrequency = 0.05f,
            ridgeFrequency = 0.02f,
            valleyFrequency = 0.015f,
            baseWeight = 0.7f,
            detailWeight = 0.2f,
            ridgeWeight = 0.0f,
            valleyWeight = 0.1f,
            ridgeSharpness = 1f,
            useDomainWarping = false,
            domainWarpStrength = 10f
        };
    }
    
    /// <summary>
    /// Create moon/cratered body settings (Mercury, Pluto, Moons)
    /// </summary>
    public static BiomeTerrainSettings CreateMoon()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.3f,
            heightScale = 0.7f,
            baseFrequency = 0.025f,
            detailFrequency = 0.08f,
            ridgeFrequency = 0.04f,
            valleyFrequency = 0.03f,
            baseWeight = 0.4f,
            detailWeight = 0.25f,
            ridgeWeight = 0.15f,  // Some ridges for highland terrain
            valleyWeight = 0.1f,
            ridgeSharpness = 1.5f,
            useDomainWarping = true,
            domainWarpStrength = 25f
        };
    }
    
    /// <summary>
    /// Create Venus volcanic terrain settings
    /// </summary>
    public static BiomeTerrainSettings CreateVenus()
    {
        return new BiomeTerrainSettings
        {
            baseElevation = 0.3f,
            heightScale = 0.75f,
            baseFrequency = 0.02f,
            detailFrequency = 0.06f,
            ridgeFrequency = 0.035f,
            valleyFrequency = 0.025f,
            baseWeight = 0.45f,
            detailWeight = 0.2f,
            ridgeWeight = 0.2f,   // Tessera ridges
            valleyWeight = 0.15f, // Lava channels
            ridgeSharpness = 1.8f,
            useDomainWarping = true,
            domainWarpStrength = 35f // More warping for chaotic Venus terrain
        };
    }
}

