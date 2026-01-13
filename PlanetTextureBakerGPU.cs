using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GPU-accelerated planet texture baker using compute shaders.
/// Replaces CPU pixel loops with parallel GPU computation for dramatically faster texture generation.
/// 
/// IMPORTANT:
/// - Does NOT change gameplay logic or rules
/// - CPU remains authoritative for game state
/// - GPU is used only for texture generation (visual-only)
/// - Does NOT remove existing code - this is an alternative path
/// </summary>
public static class PlanetTextureBakerGPU
{
    // Cache for compute buffers to avoid per-frame reallocation
    private static readonly Dictionary<string, ComputeBuffer> _lutBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, ComputeBuffer> _colorBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, ComputeBuffer> _elevationBufferCache = new Dictionary<string, ComputeBuffer>();
    private static readonly Dictionary<string, RenderTexture> _biomeTextureCache = new Dictionary<string, RenderTexture>();
    private static readonly Dictionary<string, RenderTexture> _heightTextureCache = new Dictionary<string, RenderTexture>();

    /// <summary>
    /// GPU-accelerated texture baking result.
    /// Returns RenderTextures that can be used directly by materials (no CPU readback).
    /// </summary>
    public struct GPUBakeResult
    {
        public RenderTexture biomeTexture;   // RGBA32 biome texture
        public RenderTexture heightTexture;  // RFloat heightmap texture
        public int[] lut;                    // Pixel -> tile index LUT (for picking)
        public int width;
        public int height;
        public Color32[] tileColors;         // Tile color atlas (for reference)
    }

    /// <summary>
    /// Bake planet textures on GPU using compute shader.
    /// This replaces the CPU pixel loop in PlanetTextureBaker.Bake().
    /// </summary>
    /// <param name="planetGen">Planet generator with tile data</param>
    /// <param name="colorProvider">Optional color provider for biome colors</param>
    /// <param name="computeShader">Compute shader for texture baking (PlanetTextureBaker.compute)</param>
    /// <param name="width">Texture width (default 2048)</param>
    /// <param name="height">Texture height (default 1024)</param>
    /// <returns>GPUBakeResult with RenderTextures ready for material assignment</returns>
    public static GPUBakeResult Bake(
        PlanetGenerator planetGen,
        MinimapColorProvider colorProvider,
        ComputeShader computeShader,
        int width = 2048,
        int height = 1024)
    {
        var res = new GPUBakeResult { width = width, height = height };
        
        if (planetGen == null || planetGen.Grid == null || !planetGen.Grid.IsBuilt)
        {
            Debug.LogError("[PlanetTextureBakerGPU] FAILED: Invalid planet generator or grid not built");
            return res;
        }

        if (computeShader == null)
        {
            Debug.LogError("[PlanetTextureBakerGPU] Compute shader is NULL! This is the problem!");
            return res;
        }

        var grid = planetGen.Grid;
        int tileCount = grid.TileCount;
        if (tileCount <= 0)
        {
            Debug.LogError($"[PlanetTextureBakerGPU] FAILED: Grid has {tileCount} tiles");
            return res;
        }
// Build per-tile color atlas and elevation data (CPU - this is game state, not visual)
        var tileColors = new Color32[tileCount];
        var tileElevations = new float[tileCount];
        
        int colorProviderUsed = 0;
        int biomeHelperUsed = 0;
        
        for (int i = 0; i < tileCount; i++)
        {
            var td = planetGen.GetHexTileData(i);
            Biome biome = td != null ? td.biome : planetGen.GetBaseBiome(i);

            // Compute equirect UV from flat tile center (flat-only)
            Vector3 center = TileSystem.Instance != null ? TileSystem.Instance.GetTileCenterFlat(i) : Vector3.zero;
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
                    colorProviderUsed++;
                }
                else
                {
                    Debug.LogWarning($"[PlanetTextureBakerGPU] Tile {i} has no HexTileData, cannot sample from provider");
                    c = Color.magenta;
                }
            }
            else
            {
                Debug.LogError("[PlanetTextureBakerGPU] CRITICAL: MinimapColorProvider is NULL! GPU bake needs biome textures from the provider.");
                c = Color.magenta;
                biomeHelperUsed++;
            }

            tileColors[i] = (Color32)c;
            
            // Sample a few tiles
            if (i < 5 || i == tileCount - 1)
            {
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
            Debug.LogError("[PlanetTextureBakerGPU] Failed to build LUT");
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
            Debug.LogError("[PlanetTextureBakerGPU] Failed to create compute buffers");
            return res;
        }

        // Get or create output RenderTextures
        var biomeRT = GetOrCreateBiomeTexture(cacheKey, width, height);
        var heightRT = GetOrCreateHeightTexture(cacheKey, width, height);

        if (biomeRT == null || heightRT == null)
        {
            Debug.LogError("[PlanetTextureBakerGPU] Failed to create RenderTextures");
            return res;
        }

        // Find kernel
        int kernel = computeShader.FindKernel("BakeTextures");
        if (kernel < 0)
        {
            Debug.LogError("[PlanetTextureBakerGPU] CRITICAL: Kernel 'BakeTextures' not found in compute shader!");
            Debug.LogError("[PlanetTextureBakerGPU] The compute shader doesn't have the BakeTextures kernel defined!");
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

    /// <summary>
    /// Convert RenderTexture to Texture2D (only if CPU readback is absolutely required).
    /// WARNING: This is slow! Avoid if possible - use RenderTextures directly in materials.
    /// </summary>
    public static Texture2D RenderTextureToTexture2D(RenderTexture rt, TextureFormat format = TextureFormat.RGBA32)
    {
        if (rt == null) return null;

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(rt.width, rt.height, format, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();

        RenderTexture.active = previous;
        return tex;
    }

    /// <summary>
    /// Clean up cached resources for a specific planet.
    /// Call this when a planet is destroyed or regenerated.
    /// </summary>
    public static void ClearCache(string planetName)
    {
        ClearCacheForPrefix(planetName);
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

    // Private helper methods

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

    private static void ClearCacheForPrefix(string prefix)
    {
        var keysToRemove = new List<string>();
        
        foreach (var key in _lutBufferCache.Keys)
        {
            if (key.StartsWith(prefix))
            {
                _lutBufferCache[key]?.Release();
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove) _lutBufferCache.Remove(key);
        keysToRemove.Clear();

        foreach (var key in _colorBufferCache.Keys)
        {
            if (key.StartsWith(prefix))
            {
                _colorBufferCache[key]?.Release();
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove) _colorBufferCache.Remove(key);
        keysToRemove.Clear();

        foreach (var key in _elevationBufferCache.Keys)
        {
            if (key.StartsWith(prefix))
            {
                _elevationBufferCache[key]?.Release();
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove) _elevationBufferCache.Remove(key);
        keysToRemove.Clear();

        foreach (var key in _biomeTextureCache.Keys)
        {
            if (key.StartsWith(prefix))
            {
                _biomeTextureCache[key]?.Release();
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove) _biomeTextureCache.Remove(key);
        keysToRemove.Clear();

        foreach (var key in _heightTextureCache.Keys)
        {
            if (key.StartsWith(prefix))
            {
                _heightTextureCache[key]?.Release();
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove) _heightTextureCache.Remove(key);
    }
}

