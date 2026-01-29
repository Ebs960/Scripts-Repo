using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates a single equirectangular world texture for a planet, using:
/// - a pixel->tileIndex LUT (EquirectLUTBuilder)
/// - a per-tile color atlas derived from tile biomes (optionally via MinimapColorProvider)
/// 
/// IMPORTANT:
/// - Does NOT spawn per-tile prefabs
/// - Intended for flat map rendering and minimap sharing
/// - GPU-only: CPU baking has been removed
/// </summary>
public static class PlanetTextureBaker
{
    // Cache for compute buffers to avoid per-frame reallocation
    private static readonly Dictionary<string, ComputeBuffer> _lutBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, ComputeBuffer> _colorBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, ComputeBuffer> _elevationBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, RenderTexture> _biomeTextureCache = new Dictionary<string, RenderTexture>();
    private static readonly Dictionary<string, RenderTexture> _heightTextureCache = new Dictionary<string, RenderTexture>();

    public struct BakeResult
    {
        public RenderTexture texture;  // GPU RenderTexture - use directly in materials, no CPU readback
        public RenderTexture heightmap; // GPU RenderTexture - heightmap for elevation displacement
        public RenderTexture normalmap; // normal map generated from heightmap
        public int[] lut;
        public int width;
        public int height;
        public Color32[] tileColors;
    }

    /// <summary>
    /// CPU baking has been removed. Use BakeGPU().
    /// </summary>
    public static BakeResult Bake(PlanetGenerator planetGen, MinimapColorProvider colorProvider, int width = 2048, int height = 1024)
    {
        var res = new BakeResult { width = width, height = height };
        Debug.LogError("[PlanetTextureBaker] CPU Bake() has been removed. Use BakeGPU() with a valid compute shader.");
        return res;
    }

    /// <summary>
    /// GPU-accelerated texture baking using compute shaders.
    /// This replaces the CPU pixel loop with parallel GPU computation for dramatically faster generation.
    /// 
    /// IMPORTANT:
    /// - Does NOT change gameplay logic or rules
    /// - CPU remains authoritative for game state
    /// - GPU is used only for texture generation (visual-only)
    /// - Returns RenderTextures that can be used directly in materials (no CPU readback)
    /// 
    /// If computeShader is null, falls back to CPU path.
    /// </summary>
    /// <param name="planetGen">Planet generator with tile data</param>
    /// <param name="colorProvider">Optional color provider for biome colors</param>
    /// <param name="computeShader">Compute shader for GPU acceleration (PlanetTextureBaker.compute)</param>
    /// <param name="width">Texture width (default 2048)</param>
    /// <param name="height">Texture height (default 1024)</param>
    /// <param name="convertToTexture2D">If true, converts RenderTextures to Texture2D (slow, avoid if possible)</param>
    /// <returns>BakeResult with textures ready for material assignment</returns>
    public static BakeResult BakeGPU(
        PlanetGenerator planetGen,
        MinimapColorProvider colorProvider,
        ComputeShader computeShader,
        int width = 2048,
        int height = 1024,
        bool convertToTexture2D = false)
    {
        if (computeShader == null)
        {
            Debug.LogError("[PlanetTextureBaker] Compute shader is null. GPU-only baking requires a compute shader.");
            return new BakeResult { width = width, height = height };
        }

        // Use GPU path (implementation is inlined here; PlanetTextureBakerGPU.cs was removed)
        var gpuResult = BakeInternalGPU(planetGen, colorProvider, computeShader, width, height);
        
        var res = new BakeResult
        {
            width = gpuResult.width,
            height = gpuResult.height,
            lut = gpuResult.lut,
            tileColors = gpuResult.tileColors
        };

        // Use RenderTextures directly - no CPU readback conversion
        // Materials accept RenderTextures via SetTexture(), avoiding slow Texture2D conversion
        // This preserves all GPU-generated data without color loss from ReadPixels()
        if (gpuResult.biomeTexture != null)
        {
            res.texture = gpuResult.biomeTexture;
}
        
        if (gpuResult.heightTexture != null)
        {
            res.heightmap = gpuResult.heightTexture;
}

        return res;
    }

    /// <summary>
    /// Clear all cached GPU resources. Call this on scene unload or when memory is tight.
    /// </summary>
    public static void ClearAllCaches()
    {
        foreach (var buf in _lutBufferCache.Values) buf?.Release();
        foreach (var buf in _colorBufferCache.Values) buf?.Release();
        foreach (var buf in _elevationBufferCache.Values) buf?.Release();
        foreach (var rt in _biomeTextureCache.Values) rt?.Release();
        foreach (var rt in _heightTextureCache.Values) rt?.Release();

        _lutBufferCache.Clear();
        _colorBufferCache.Clear();
        _elevationBufferCache.Clear();
        _biomeTextureCache.Clear();
        _heightTextureCache.Clear();
    }

    // -------------------- GPU bake implementation (previously PlanetTextureBakerGPU.cs) --------------------

    private struct GPUBakeResult
    {
        public RenderTexture biomeTexture;   // RGBA32 biome texture
        public RenderTexture heightTexture;  // RFloat heightmap texture
        public int[] lut;                    // Pixel -> tile index LUT (for picking)
        public int width;
        public int height;
        public Color32[] tileColors;         // Tile color atlas (for reference)
    }

    private static GPUBakeResult BakeInternalGPU(
        PlanetGenerator planetGen,
        MinimapColorProvider colorProvider,
        ComputeShader computeShader,
        int width,
        int height)
    {
        var res = new GPUBakeResult { width = width, height = height };

        if (planetGen == null || planetGen.Grid == null || !planetGen.Grid.IsBuilt)
        {
            Debug.LogError("[PlanetTextureBaker] FAILED: Invalid planet generator or grid not built");
            return res;
        }

        if (computeShader == null)
        {
            Debug.LogError("[PlanetTextureBaker] Compute shader is NULL!");
            return res;
        }

        var grid = planetGen.Grid;
        int tileCount = grid.TileCount;
        if (tileCount <= 0)
        {
            Debug.LogError($"[PlanetTextureBaker] FAILED: Grid has {tileCount} tiles");
            return res;
        }

        // Build per-tile color atlas and elevation data (CPU - this is game state, not visual)
        var tileColors = new Color32[tileCount];
        var tileElevations = new float[tileCount];

        for (int i = 0; i < tileCount; i++)
        {
            var td = planetGen.GetHexTileData(i);
            Biome biome = td != null ? td.biome : planetGen.GetBaseBiome(i);

            // Compute equirect UV from the planet's own grid so baking is independent of TileSystem timing
            // and works correctly for non-current planets in multi-planet mode.
            Vector3 center = (grid.tileCenters != null && i >= 0 && i < grid.tileCenters.Length) ? grid.tileCenters[i] : Vector3.zero;
            float u = (center.x + grid.MapWidth * 0.5f) / grid.MapWidth;
            float v = (center.z + grid.MapHeight * 0.5f) / grid.MapHeight;
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            var uv = new Vector2(u, v);

            Color c;
            // Use MinimapColorProvider to get actual biome texture colors (samples texture at UV)
            if (colorProvider != null)
            {
                if (td != null)
                {
                    c = colorProvider.ColorFor(td, uv);
                }
                else
                {
                    Debug.LogWarning($"[PlanetTextureBaker] Tile {i} has no HexTileData, cannot sample from provider");
                    c = Color.magenta;
                }
            }
            else
            {
                Debug.LogError("[PlanetTextureBaker] CRITICAL: MinimapColorProvider is NULL! GPU bake needs biome textures from the provider.");
                c = Color.magenta;
            }

            tileColors[i] = (Color32)c;

            // Store elevation (0-1 range)
            // Use renderElevation if available (normalized for full range), otherwise fall back to elevation
            float elevation = td != null ?
                (td.renderElevation > 0.001f ? td.renderElevation : td.elevation) :
                planetGen.GetTileElevation(i);
            tileElevations[i] = Mathf.Clamp01(elevation);
        }

        res.tileColors = tileColors;

        // Build LUT: pixel -> tileIndex (CPU - this is spatial mapping, not visual)
        var lut = EquirectLUTBuilder.BuildLUT(grid, width, height);
        res.lut = lut;
        if (lut == null || lut.Length != width * height)
        {
            Debug.LogError("[PlanetTextureBaker] Failed to build LUT");
            return res;
        }

        // Generate cache key for this planet/resolution combination
        string cacheKey = $"{planetGen.gameObject.name}_{width}x{height}";

        // Get or create compute buffers
        var lutBuffer = GetOrCreateLUTBuffer(cacheKey, lut);
        var colorBuffer = GetOrCreateColorBuffer(cacheKey, tileColors);
        var elevationBuffer = GetOrCreateElevationBuffer(cacheKey, tileElevations);

        if (lutBuffer == null || colorBuffer == null || elevationBuffer == null)
        {
            Debug.LogError("[PlanetTextureBaker] Failed to create compute buffers");
            return res;
        }

        // Get or create output RenderTextures
        var biomeRT = GetOrCreateBiomeTexture(cacheKey, width, height);
        var heightRT = GetOrCreateHeightTexture(cacheKey, width, height);

        if (biomeRT == null || heightRT == null)
        {
            Debug.LogError("[PlanetTextureBaker] Failed to create RenderTextures");
            return res;
        }

        // Find kernel
        int kernel = computeShader.FindKernel("BakeTextures");
        if (kernel < 0)
        {
            Debug.LogError("[PlanetTextureBaker] CRITICAL: Kernel 'BakeTextures' not found in compute shader!");
            return res;
        }

        // Set buffers
        computeShader.SetBuffer(kernel, "_PixelToTileLUT", lutBuffer);
        computeShader.SetBuffer(kernel, "_TileBiomeColors", colorBuffer);
        computeShader.SetBuffer(kernel, "_TileElevations", elevationBuffer);

        // Set output textures
        computeShader.SetTexture(kernel, "_BiomeTexture", biomeRT);
        computeShader.SetTexture(kernel, "_HeightTexture", heightRT);

        // Set parameters
        computeShader.SetInt("_Width", width);
        computeShader.SetInt("_Height", height);
        computeShader.SetInt("_TileCount", tileCount);

        // Dispatch compute shader (8x8 thread groups)
        int threadGroupsX = Mathf.CeilToInt(width / 8f);
        int threadGroupsY = Mathf.CeilToInt(height / 8f);
        computeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

        // Return results (RenderTextures are ready to use, no CPU readback)
        res.biomeTexture = biomeRT;
        res.heightTexture = heightRT;
        return res;
    }

    private static ComputeBuffer GetOrCreateLUTBuffer(string key, int[] data)
    {
        if (_lutBufferCache.TryGetValue(key, out var buf) && buf != null && buf.count == data.Length)
            return buf;

        if (buf != null) buf.Release();

        var newBuf = new ComputeBuffer(data.Length, sizeof(int));
        newBuf.SetData(data);
        _lutBufferCache[key] = newBuf;
        return newBuf;
    }

    private static ComputeBuffer GetOrCreateColorBuffer(string key, Color32[] data)
    {
        if (_colorBufferCache.TryGetValue(key, out var buf) && buf != null && buf.count == data.Length)
            return buf;

        if (buf != null) buf.Release();

        // Convert Color32[] to float4[] for GPU
        var float4Colors = new Vector4[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            float4Colors[i] = new Vector4(
                data[i].r / 255f,
                data[i].g / 255f,
                data[i].b / 255f,
                data[i].a / 255f
            );
        }

        var newBuf = new ComputeBuffer(data.Length, sizeof(float) * 4);
        newBuf.SetData(float4Colors);
        _colorBufferCache[key] = newBuf;
        return newBuf;
    }

    private static ComputeBuffer GetOrCreateElevationBuffer(string key, float[] data)
    {
        if (_elevationBufferCache.TryGetValue(key, out var buf) && buf != null && buf.count == data.Length)
            return buf;

        if (buf != null) buf.Release();

        var newBuf = new ComputeBuffer(data.Length, sizeof(float));
        newBuf.SetData(data);
        _elevationBufferCache[key] = newBuf;
        return newBuf;
    }

    private static RenderTexture GetOrCreateBiomeTexture(string key, int width, int height)
    {
        if (_biomeTextureCache.TryGetValue(key, out var rt) && rt != null && rt.width == width && rt.height == height)
            return rt;

        if (rt != null) rt.Release();

        rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Repeat,
            name = $"PlanetBiomeTexture_{key}"
        };
        rt.Create();
        _biomeTextureCache[key] = rt;
        return rt;
    }

    private static RenderTexture GetOrCreateHeightTexture(string key, int width, int height)
    {
        if (_heightTextureCache.TryGetValue(key, out var rt) && rt != null && rt.width == width && rt.height == height)
            return rt;

        if (rt != null) rt.Release();

        rt = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            name = $"PlanetHeightTexture_{key}"
        };
        rt.Create();
        _heightTextureCache[key] = rt;
        return rt;
    }
}
