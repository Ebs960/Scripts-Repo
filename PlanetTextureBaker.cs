using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
    private static readonly Dictionary<string, ComputeBuffer> _biomeIndexBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, ComputeBuffer> _tileUVBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, RenderTexture> _biomeTextureCache = new Dictionary<string, RenderTexture>();
    private static readonly Dictionary<string, RenderTexture> _heightTextureCache = new Dictionary<string, RenderTexture>();
    
    // GPU color lookup cache (built from MinimapColorProvider)
    private static ComputeBuffer _biomeColorLookupBuffer;
    private static bool _clearingCaches = false;
    private static bool _isBaking = false;

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
    public static BakeResult Bake(PlanetGenerator planetGen, MinimapColorProvider colorProvider, int width = 2048, int height = 2048)
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
    /// <param name="height">Texture height (default 2048)</param>
    /// <param name="convertToTexture2D">If true, converts RenderTextures to Texture2D (slow, avoid if possible)</param>
    /// <returns>BakeResult with textures ready for material assignment</returns>
    public static BakeResult BakeGPU(
        PlanetGenerator planetGen,
        MinimapColorProvider colorProvider,
        ComputeShader computeShader,
        int width = 2048,
        int height = 2048,
        bool convertToTexture2D = false,
        int[] preBuiltLUT = null)
    {
        if (computeShader == null)
        {
            Debug.LogError("[PlanetTextureBaker] Compute shader is null. GPU-only baking requires a compute shader.");
            return new BakeResult { width = width, height = height };
        }

        // Use GPU path (implementation is inlined here; PlanetTextureBakerGPU.cs was removed)
        _isBaking = true;
        GPUBakeResult gpuResult;
        try
        {
            gpuResult = BakeInternalGPU(planetGen, colorProvider, computeShader, width, height, preBuiltLUT);
        }
        finally
        {
            _isBaking = false;
        }
        
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
        if (_clearingCaches) return;
        if (_isBaking)
        {
            Debug.LogWarning("[PlanetTextureBaker] ClearAllCaches deferred because a bake is in progress.");
            return;
        }
        _clearingCaches = true;
        try
        {
            try { foreach (var buf in _lutBufferCache.Values) { try { if (buf != null) buf.Release(); } catch { } } } catch { }
            try { foreach (var buf in _colorBufferCache.Values) { try { if (buf != null) buf.Release(); } catch { } } } catch { }
            try { foreach (var buf in _elevationBufferCache.Values) { try { if (buf != null) buf.Release(); } catch { } } } catch { }
            try { foreach (var buf in _biomeIndexBufferCache.Values) { try { if (buf != null) buf.Release(); } catch { } } } catch { }
            try { foreach (var buf in _tileUVBufferCache.Values) { try { if (buf != null) buf.Release(); } catch { } } } catch { }

            try { foreach (var rt in _biomeTextureCache.Values) { try { if (rt != null) { if (rt.IsCreated()) rt.Release(); Object.DestroyImmediate(rt); } } catch { } } } catch { }
            try { foreach (var rt in _heightTextureCache.Values) { try { if (rt != null) { if (rt.IsCreated()) rt.Release(); Object.DestroyImmediate(rt); } } catch { } } } catch { }

            _lutBufferCache.Clear();
            _colorBufferCache.Clear();
            _elevationBufferCache.Clear();
            _biomeIndexBufferCache.Clear();
            _tileUVBufferCache.Clear();
            _biomeTextureCache.Clear();
            _heightTextureCache.Clear();

            try { if (_biomeColorLookupBuffer != null) { _biomeColorLookupBuffer.Release(); _biomeColorLookupBuffer = null; } } catch { _biomeColorLookupBuffer = null; }
        }
        finally
        {
            _clearingCaches = false;
        }
    }

    // -------------------- GPU bake implementation (previously PlanetTextureBakerGPU.cs) --------------------
    
    /// <summary>
    /// Builds a StructuredBuffer with colors for all biomes.
    /// Index in buffer = Biome enum value.
    /// The effective color source is centralized inside MinimapColorProvider:
    /// - ProviderColors uses provider.biomeColors
    /// - DefaultColors uses BiomeColorHelper defaults
    /// </summary>
    private static void BuildBiomeColorLookup(MinimapColorProvider colorProvider)
    {
        // Always rebuild when asked; this avoids stale colors if the asset is edited at runtime or between loads.
        if (_biomeColorLookupBuffer != null)
        {
            _biomeColorLookupBuffer.Release();
            _biomeColorLookupBuffer = null;
        }

        // Get max biome enum value
        int maxBiomeValue = System.Enum.GetValues(typeof(Biome)).Length;
        var colors = new Vector4[maxBiomeValue];
        
        // Initialize all to magenta (error color)
        for (int i = 0; i < maxBiomeValue; i++)
        {
            colors[i] = new Vector4(1f, 0f, 1f, 1f); // Magenta
        }
        
        // Minimap colors are intentionally fixed to default biome colors (BiomeColorHelper) for minimap-only visuals.
        // The provider parameter is kept for call-site compatibility but is not used.
        for (int i = 0; i < maxBiomeValue; i++)
        {
            Biome biome = (Biome)i;
            Color c = BiomeColorHelper.GetMinimapColor(biome);
            colors[i] = new Vector4(c.r, c.g, c.b, c.a);
        }

        // Create compute buffer
        if (_biomeColorLookupBuffer != null)
        {
            _biomeColorLookupBuffer.Release();
        }
        _biomeColorLookupBuffer = new ComputeBuffer(maxBiomeValue, sizeof(float) * 4);
        _biomeColorLookupBuffer.SetData(colors);
    }

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
        int height,
        int[] preBuiltLUT = null)
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

        // Build GPU-ready biome color lookup (default biome colors).
        BuildBiomeColorLookup(colorProvider);
        if (_biomeColorLookupBuffer == null)
        {
            Debug.LogError("[PlanetTextureBaker] CRITICAL: Failed to build _BiomeColorLookup buffer.");
            return res;
        }

        // Build per-tile data: biome indices, UVs, and elevations (for GPU texture sampling)
        var tileBiomeIndices = ArrayPoolUtils.RentInt(tileCount);
        var tileUVs = new Vector2[tileCount]; // Unity Vector2 not supported by ArrayPool
        var tileElevations = ArrayPoolUtils.RentFloat(tileCount);
        var tileColors = new Color32[tileCount]; // Still computed for fallback/reference

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
            tileUVs[i] = new Vector2(u, v);

            // Store biome as integer index (for GPU lookup)
            tileBiomeIndices[i] = (int)biome;
            
            // Compute fallback color for reference (used if GPU sampling fails)
            if (colorProvider != null && td != null)
            {
                tileColors[i] = (Color32)colorProvider.ColorFor(td, tileUVs[i]);
            }
            else
            {
                tileColors[i] = (Color32)Color.magenta;
            }

            // Store elevation (world-space units, used directly by heightmap and shader)
            float elevation = td != null ? td.elevation : planetGen.GetTileElevation(i);
            tileElevations[i] = elevation;
        }

        res.tileColors = tileColors;

        // Build LUT: pixel -> tileIndex (CPU - this is spatial mapping, not visual)
        // Use pre-built LUT if provided (avoids redundant synchronous rebuild during batched chunk building)
        var lut = preBuiltLUT ?? EquirectLUTBuilder.BuildLUT(grid, width, height);
        res.lut = lut;
        if (lut == null || lut.Length != width * height)
        {
            Debug.LogError("[PlanetTextureBaker] Failed to build LUT");
            ArrayPoolUtils.ReturnInt(tileBiomeIndices);
            ArrayPoolUtils.ReturnFloat(tileElevations);
            return res;
        }

        // Generate cache key for this planet/resolution combination
        string cacheKey = $"{planetGen.gameObject.name}_{width}x{height}";

        // Get or create compute buffers
        var lutBuffer = GetOrCreateLUTBuffer(cacheKey, lut);
        var biomeIndexBuffer = GetOrCreateBiomeIndexBuffer(cacheKey, tileBiomeIndices);
        var tileUVBuffer = GetOrCreateTileUVBuffer(cacheKey, tileUVs);
        var elevationBuffer = GetOrCreateElevationBuffer(cacheKey, tileElevations);
        
        // Return pooled arrays after data is copied into compute buffers
        ArrayPoolUtils.ReturnInt(tileBiomeIndices);
        ArrayPoolUtils.ReturnFloat(tileElevations);
        
        // Color buffer kept for fallback/reference (may not be used in GPU path)
        // (Removed) Per-tile color buffer upload: shader uses biome lookup; tileColors are kept only for output/reference.

        if (lutBuffer == null || biomeIndexBuffer == null || tileUVBuffer == null || elevationBuffer == null)
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
        computeShader.SetBuffer(kernel, "_TileBiomeIndices", biomeIndexBuffer);
        computeShader.SetBuffer(kernel, "_TileUVs", tileUVBuffer);
        computeShader.SetBuffer(kernel, "_TileElevations", elevationBuffer);
        
        // Set biome color lookup (required)
        computeShader.SetBuffer(kernel, "_BiomeColorLookup", _biomeColorLookupBuffer);

        // Set output textures
        computeShader.SetTexture(kernel, "_BiomeTexture", biomeRT);
        computeShader.SetTexture(kernel, "_HeightTexture", heightRT);

        // Set parameters
        computeShader.SetInt("_Width", width);
        computeShader.SetInt("_Height", height);
        computeShader.SetInt("_TileCount", tileCount);
        computeShader.SetInt("_MaxBiomeValue", System.Enum.GetValues(typeof(Biome)).Length);

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
        try
        {
            if (_lutBufferCache.TryGetValue(key, out var buf))
            {
                if (buf != null)
                {
                    try
                    {
                        if (buf.count == data.Length)
                            return buf;
                    }
                    catch
                    {
                        // buffer appears invalid/destroyed
                        try { buf.Release(); } catch { }
                        _lutBufferCache.Remove(key);
                    }
                }
            }
        }
        catch { _lutBufferCache.Remove(key); }

        var newBuf = new ComputeBuffer(data.Length, sizeof(int));
        newBuf.SetData(data);
        _lutBufferCache[key] = newBuf;
        return newBuf;
    }

    private static ComputeBuffer GetOrCreateColorBuffer(string key, Color32[] data)
    {
        try
        {
            if (_colorBufferCache.TryGetValue(key, out var buf))
            {
                if (buf != null)
                {
                    try { if (buf.count == data.Length) return buf; }
                    catch { try { buf.Release(); } catch { } _colorBufferCache.Remove(key); }
                }
            }
        }
        catch { _colorBufferCache.Remove(key); }

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
        try
        {
            if (_elevationBufferCache.TryGetValue(key, out var buf))
            {
                if (buf != null)
                {
                    try { if (buf.count == data.Length) return buf; }
                    catch { try { buf.Release(); } catch { } _elevationBufferCache.Remove(key); }
                }
            }
        }
        catch { _elevationBufferCache.Remove(key); }

        var newBuf = new ComputeBuffer(data.Length, sizeof(float));
        newBuf.SetData(data);
        _elevationBufferCache[key] = newBuf;
        return newBuf;
    }
    
    private static ComputeBuffer GetOrCreateBiomeIndexBuffer(string key, int[] data)
    {
        try
        {
            if (_biomeIndexBufferCache.TryGetValue(key, out var buf))
            {
                if (buf != null)
                {
                    try { if (buf.count == data.Length) return buf; }
                    catch { try { buf.Release(); } catch { } _biomeIndexBufferCache.Remove(key); }
                }
            }
        }
        catch { _biomeIndexBufferCache.Remove(key); }

        var newBuf = new ComputeBuffer(data.Length, sizeof(int));
        newBuf.SetData(data);
        _biomeIndexBufferCache[key] = newBuf;
        return newBuf;
    }
    
    private static ComputeBuffer GetOrCreateTileUVBuffer(string key, Vector2[] data)
    {
        try
        {
            if (_tileUVBufferCache.TryGetValue(key, out var buf))
            {
                if (buf != null)
                {
                    try { if (buf.count == data.Length) return buf; }
                    catch { try { buf.Release(); } catch { } _tileUVBufferCache.Remove(key); }
                }
            }
        }
        catch { _tileUVBufferCache.Remove(key); }

        var newBuf = new ComputeBuffer(data.Length, sizeof(float) * 2);
        newBuf.SetData(data);
        _tileUVBufferCache[key] = newBuf;
        return newBuf;
    }

    private static RenderTexture GetOrCreateBiomeTexture(string key, int width, int height)
    {
        try
        {
            if (_biomeTextureCache.TryGetValue(key, out var rt) && rt != null && rt.width == width && rt.height == height)
                return rt;

            if (rt != null)
            {
                try { if (rt.IsCreated()) rt.Release(); Object.DestroyImmediate(rt); } catch { }
            }
        }
        catch { }

        var newRt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Repeat,
            name = $"PlanetBiomeTexture_{key}"
        };
        newRt.Create();
        _biomeTextureCache[key] = newRt;
        return newRt;
    }

    private static RenderTexture GetOrCreateHeightTexture(string key, int width, int height)
    {
        try
        {
            if (_heightTextureCache.TryGetValue(key, out var rt) && rt != null && rt.width == width && rt.height == height)
                return rt;

            if (rt != null)
            {
                try { if (rt.IsCreated()) rt.Release(); Object.DestroyImmediate(rt); } catch { }
            }
        }
        catch { }

        var newRt = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            name = $"PlanetHeightTexture_{key}"
        };
        newRt.Create();
        _heightTextureCache[key] = newRt;
        return newRt;
    }
}
