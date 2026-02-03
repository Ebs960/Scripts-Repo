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
    private static readonly Dictionary<string, ComputeBuffer> _biomeIndexBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, ComputeBuffer> _tileUVBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, RenderTexture> _biomeTextureCache = new Dictionary<string, RenderTexture>();
    private static readonly Dictionary<string, RenderTexture> _heightTextureCache = new Dictionary<string, RenderTexture>();
    
    // Cache for texture arrays (built from MinimapColorProvider)
    private static Texture2DArray _cachedBiomeTextureArray;
    private static Texture2D _cachedCustomTexture;
    private static Dictionary<Biome, int> _biomeToTextureIndex;
    private static ComputeBuffer _biomeToTextureIndexBuffer; // Maps biome enum value -> texture array index (-1 = no texture)
    private static ComputeBuffer _biomeColorLookupBuffer;
    private static MinimapRenderMode _cachedRenderMode;

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
        foreach (var buf in _biomeIndexBufferCache.Values) buf?.Release();
        foreach (var buf in _tileUVBufferCache.Values) buf?.Release();
        foreach (var rt in _biomeTextureCache.Values) rt?.Release();
        foreach (var rt in _heightTextureCache.Values) rt?.Release();

        _lutBufferCache.Clear();
        _colorBufferCache.Clear();
        _elevationBufferCache.Clear();
        _biomeIndexBufferCache.Clear();
        _tileUVBufferCache.Clear();
        _biomeTextureCache.Clear();
        _heightTextureCache.Clear();
        
        // Clear texture array cache
        if (_cachedBiomeTextureArray != null)
        {
            Object.DestroyImmediate(_cachedBiomeTextureArray);
            _cachedBiomeTextureArray = null;
        }
        _cachedCustomTexture = null;
        _biomeToTextureIndex = null;
        if (_biomeColorLookupBuffer != null)
        {
            _biomeColorLookupBuffer.Release();
            _biomeColorLookupBuffer = null;
        }
        if (_biomeToTextureIndexBuffer != null)
        {
            _biomeToTextureIndexBuffer.Release();
            _biomeToTextureIndexBuffer = null;
        }
    }

    // -------------------- GPU bake implementation (previously PlanetTextureBakerGPU.cs) --------------------
    
    /// <summary>
    /// Builds a Texture2DArray from biome textures and creates biome-to-index mapping.
    /// Also builds color lookup table for BiomeColors mode.
    /// </summary>
    private static void BuildTextureArrayAndMappings(MinimapColorProvider colorProvider)
    {
        if (colorProvider == null)
        {
            Debug.LogError("[PlanetTextureBaker] Cannot build texture array: colorProvider is null");
            return;
        }
        
        // Check if we need to rebuild (render mode changed or not yet built)
        if (_cachedBiomeTextureArray != null && _cachedRenderMode == colorProvider.renderMode)
        {
            // Already built for this render mode
            return;
        }
        
        // Clear old cache
        if (_cachedBiomeTextureArray != null)
        {
            Object.DestroyImmediate(_cachedBiomeTextureArray);
            _cachedBiomeTextureArray = null;
        }
        if (_biomeColorLookupBuffer != null)
        {
            _biomeColorLookupBuffer.Release();
            _biomeColorLookupBuffer = null;
        }
        
        _cachedRenderMode = colorProvider.renderMode;
        _biomeToTextureIndex = new Dictionary<Biome, int>();
        
        switch (colorProvider.renderMode)
        {
            case MinimapRenderMode.BiomeColors:
                // Build color lookup table (one color per biome enum value)
                BuildBiomeColorLookup(colorProvider);
                break;
                
            case MinimapRenderMode.BiomeTextures:
                // Build texture array from biome textures
                BuildBiomeTextureArray(colorProvider);
                break;
                
            case MinimapRenderMode.CustomTexture:
                // Use single custom texture (no array needed)
                _cachedCustomTexture = colorProvider.customMinimapTexture;
                break;
        }
    }
    
    /// <summary>
    /// Builds a StructuredBuffer with colors for all biomes (for BiomeColors mode).
    /// Index in buffer = Biome enum value.
    /// </summary>
    private static void BuildBiomeColorLookup(MinimapColorProvider colorProvider)
    {
        // Get max biome enum value
        int maxBiomeValue = System.Enum.GetValues(typeof(Biome)).Length;
        var colors = new Vector4[maxBiomeValue];
        
        // Initialize all to magenta (error color)
        for (int i = 0; i < maxBiomeValue; i++)
        {
            colors[i] = new Vector4(1f, 0f, 1f, 1f); // Magenta
        }
        
        // Fill in configured colors
        foreach (var bc in colorProvider.biomeColors)
        {
            int biomeIndex = (int)bc.biome;
            if (biomeIndex >= 0 && biomeIndex < maxBiomeValue)
            {
                colors[biomeIndex] = new Vector4(bc.color.r, bc.color.g, bc.color.b, bc.color.a);
            }
        }
        
        // Fill in defaults for unconfigured biomes
        for (int i = 0; i < maxBiomeValue; i++)
        {
            if (colors[i].x == 1f && colors[i].y == 0f && colors[i].z == 1f) // Still magenta
            {
                Biome biome = (Biome)i;
                Color defaultColor = BiomeColorHelper.GetMinimapColor(biome);
                colors[i] = new Vector4(defaultColor.r, defaultColor.g, defaultColor.b, defaultColor.a);
            }
        }
        
        // Create compute buffer
        if (_biomeColorLookupBuffer != null)
        {
            _biomeColorLookupBuffer.Release();
        }
        _biomeColorLookupBuffer = new ComputeBuffer(maxBiomeValue, sizeof(float) * 4);
        _biomeColorLookupBuffer.SetData(colors);
    }
    
    /// <summary>
    /// Builds a Texture2DArray from all biome textures and creates biome-to-index mapping.
    /// </summary>
    private static void BuildBiomeTextureArray(MinimapColorProvider colorProvider)
    {
        var textures = new List<Texture2D>();
        var biomeList = new List<Biome>();
        
        // Collect all unique textures (deduplicate by texture reference)
        var textureToIndex = new Dictionary<Texture2D, int>();
        foreach (var bt in colorProvider.biomeTextures)
        {
            if (bt.texture != null)
            {
                if (!textureToIndex.ContainsKey(bt.texture))
                {
                    int index = textures.Count;
                    textures.Add(bt.texture);
                    textureToIndex[bt.texture] = index;
                    biomeList.Add(bt.biome);
                }
                _biomeToTextureIndex[bt.biome] = textureToIndex[bt.texture];
            }
        }
        
        if (textures.Count == 0)
        {
            Debug.LogWarning("[PlanetTextureBaker] No biome textures found in BiomeTextures mode. Falling back to colors.");
            BuildBiomeColorLookup(colorProvider);
            return;
        }
        
        // Find common dimensions (use first texture's size)
        int width = textures[0].width;
        int height = textures[0].height;
        
        // Validate all textures have same dimensions (required for Texture2DArray)
        bool dimensionsMatch = true;
        for (int i = 1; i < textures.Count; i++)
        {
            if (textures[i].width != width || textures[i].height != height)
            {
                Debug.LogError($"[PlanetTextureBaker] Texture '{textures[i].name}' has dimensions {textures[i].width}x{textures[i].height}, but expected {width}x{height}. Texture2DArray requires all textures to have the same dimensions. Skipping this texture.");
                dimensionsMatch = false;
            }
        }
        
        if (!dimensionsMatch)
        {
            Debug.LogWarning("[PlanetTextureBaker] Not all textures have matching dimensions. Texture array may be incomplete.");
        }
        
        // Create Texture2DArray
        _cachedBiomeTextureArray = new Texture2DArray(width, height, textures.Count, TextureFormat.RGBA32, false);
        _cachedBiomeTextureArray.filterMode = FilterMode.Bilinear;
        _cachedBiomeTextureArray.wrapMode = TextureWrapMode.Repeat;
        
        // Copy textures into array using Graphics.CopyTexture (GPU-to-GPU, works even for non-readable textures)
        for (int i = 0; i < textures.Count; i++)
        {
            var tex = textures[i];
            try
            {
                // Graphics.CopyTexture works GPU-to-GPU, so it should work even for non-readable textures
                Graphics.CopyTexture(tex, 0, _cachedBiomeTextureArray, i);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PlanetTextureBaker] Failed to copy texture '{tex.name}' to array: {ex.Message}. This texture will use fallback colors.");
            }
        }
        
        // Build biome-to-texture-index mapping buffer (for GPU lookup)
        int maxBiomeValue = System.Enum.GetValues(typeof(Biome)).Length;
        var biomeToIndexMap = new int[maxBiomeValue];
        
        // Initialize all to -1 (no texture)
        for (int i = 0; i < maxBiomeValue; i++)
        {
            biomeToIndexMap[i] = -1;
        }
        
        // Fill in mappings
        foreach (var kvp in _biomeToTextureIndex)
        {
            int biomeValue = (int)kvp.Key;
            if (biomeValue >= 0 && biomeValue < maxBiomeValue)
            {
                biomeToIndexMap[biomeValue] = kvp.Value;
            }
        }
        
        // Create compute buffer for mapping
        if (_biomeToTextureIndexBuffer != null)
        {
            _biomeToTextureIndexBuffer.Release();
        }
        _biomeToTextureIndexBuffer = new ComputeBuffer(maxBiomeValue, sizeof(int));
        _biomeToTextureIndexBuffer.SetData(biomeToIndexMap);
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

        // Build texture array and mappings from color provider (GPU-ready)
        if (colorProvider != null)
        {
            BuildTextureArrayAndMappings(colorProvider);
        }
        else
        {
            Debug.LogError("[PlanetTextureBaker] CRITICAL: MinimapColorProvider is NULL! GPU bake needs biome textures from the provider.");
        }

        // Build per-tile data: biome indices, UVs, and elevations (for GPU texture sampling)
        var tileBiomeIndices = new int[tileCount];
        var tileUVs = new Vector2[tileCount];
        var tileElevations = new float[tileCount];
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
        var biomeIndexBuffer = GetOrCreateBiomeIndexBuffer(cacheKey, tileBiomeIndices);
        var tileUVBuffer = GetOrCreateTileUVBuffer(cacheKey, tileUVs);
        var elevationBuffer = GetOrCreateElevationBuffer(cacheKey, tileElevations);
        
        // Color buffer kept for fallback/reference (may not be used in GPU path)
        var colorBuffer = GetOrCreateColorBuffer(cacheKey, tileColors);

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
        
        // Set texture resources based on render mode
        if (colorProvider != null)
        {
            switch (colorProvider.renderMode)
            {
                case MinimapRenderMode.BiomeColors:
                    // Use color lookup table
                    if (_biomeColorLookupBuffer != null)
                    {
                        computeShader.SetBuffer(kernel, "_BiomeColorLookup", _biomeColorLookupBuffer);
                    }
                    computeShader.SetInt("_RenderMode", 0); // BiomeColors = 0
                    break;
                    
                case MinimapRenderMode.BiomeTextures:
                    // Use texture array
                    if (_cachedBiomeTextureArray != null)
                    {
                        computeShader.SetTexture(kernel, "_BiomeTextureArray", _cachedBiomeTextureArray);
                    }
                    // Set biome-to-texture-index mapping
                    if (_biomeToTextureIndexBuffer != null)
                    {
                        computeShader.SetBuffer(kernel, "_BiomeToTextureIndex", _biomeToTextureIndexBuffer);
                    }
                    // Also set color buffer as fallback
                    computeShader.SetBuffer(kernel, "_TileBiomeColors", colorBuffer);
                    computeShader.SetInt("_RenderMode", 1); // BiomeTextures = 1
                    break;
                    
                case MinimapRenderMode.CustomTexture:
                    // Use single custom texture
                    if (_cachedCustomTexture != null)
                    {
                        computeShader.SetTexture(kernel, "_CustomTexture", _cachedCustomTexture);
                    }
                    computeShader.SetInt("_RenderMode", 2); // CustomTexture = 2
                    break;
            }
        }
        else
        {
            // Fallback: use color buffer
            computeShader.SetBuffer(kernel, "_TileBiomeColors", colorBuffer);
            computeShader.SetInt("_RenderMode", 0);
        }

        // Set output textures
        computeShader.SetTexture(kernel, "_BiomeTexture", biomeRT);
        computeShader.SetTexture(kernel, "_HeightTexture", heightRT);

        // Set parameters
        computeShader.SetInt("_Width", width);
        computeShader.SetInt("_Height", height);
        computeShader.SetInt("_TileCount", tileCount);
        computeShader.SetInt("_MaxBiomeValue", System.Enum.GetValues(typeof(Biome)).Length);
        
        // Set texture array size if using BiomeTextures mode
        if (colorProvider != null && colorProvider.renderMode == MinimapRenderMode.BiomeTextures && _cachedBiomeTextureArray != null)
        {
            computeShader.SetInt("_BiomeTextureArraySize", _cachedBiomeTextureArray.depth);
        }
        else
        {
            computeShader.SetInt("_BiomeTextureArraySize", 0);
        }

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
    
    private static ComputeBuffer GetOrCreateBiomeIndexBuffer(string key, int[] data)
    {
        if (_biomeIndexBufferCache.TryGetValue(key, out var buf) && buf != null && buf.count == data.Length)
            return buf;

        if (buf != null) buf.Release();

        var newBuf = new ComputeBuffer(data.Length, sizeof(int));
        newBuf.SetData(data);
        _biomeIndexBufferCache[key] = newBuf;
        return newBuf;
    }
    
    private static ComputeBuffer GetOrCreateTileUVBuffer(string key, Vector2[] data)
    {
        if (_tileUVBufferCache.TryGetValue(key, out var buf) && buf != null && buf.count == data.Length)
            return buf;

        if (buf != null) buf.Release();

        var newBuf = new ComputeBuffer(data.Length, sizeof(float) * 2);
        newBuf.SetData(data);
        _tileUVBufferCache[key] = newBuf;
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
