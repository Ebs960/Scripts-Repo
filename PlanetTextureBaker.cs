using UnityEngine;

/// <summary>
/// Generates a single equirectangular world texture for a planet, using:
/// - a pixel->tileIndex LUT (EquirectLUTBuilder)
/// - a per-tile color atlas derived from tile biomes (optionally via MinimapColorProvider)
/// 
/// IMPORTANT:
/// - Does NOT spawn per-tile prefabs
/// - Intended for flat map rendering and minimap sharing
/// - GPU path available via BakeGPU() for dramatically faster texture generation
/// </summary>
public static class PlanetTextureBaker
{
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
    /// CPU-based texture baking (original implementation).
    /// For GPU-accelerated version, use BakeGPU() instead.
    /// </summary>
    public static BakeResult Bake(PlanetGenerator planetGen, MinimapColorProvider colorProvider, int width = 2048, int height = 1024)
    {
        var res = new BakeResult { width = width, height = height };
        if (planetGen == null || planetGen.Grid == null || !planetGen.Grid.IsBuilt)
        {
            Debug.LogError($"[PlanetTextureBaker] Bake FAILED: planetGen null? {planetGen == null}, Grid null? {planetGen?.Grid == null}, IsBuilt? {planetGen?.Grid?.IsBuilt}");
            return res;
        }

        var grid = planetGen.Grid;
        int tileCount = grid.TileCount;
        if (tileCount <= 0)
        {
            Debug.LogError($"[PlanetTextureBaker] Bake FAILED: tileCount is {tileCount}");
            return res;
        }
        bool usePerPixelTextures = (colorProvider != null &&
            (colorProvider.renderMode == MinimapRenderMode.BiomeTextures ||
             colorProvider.renderMode == MinimapRenderMode.CustomTexture));

        // Build per-tile color atlas and elevation data
        var tileColors = new Color32[tileCount];
        var tileElevations = new float[tileCount]; // Store elevation for heightmap
        
        int colorProviderUsed = 0;
        int biomeHelperUsed = 0;
        
        for (int i = 0; i < tileCount; i++)
        {
            var td = planetGen.GetHexTileData(i);
            // Fallback biome if generator doesn't have data (should be rare)
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
            if (colorProvider != null)
            {
                // If td is null, we still need a dummy object for provider; fall back to biome color.
                if (td != null)
                {
                    c = colorProvider.ColorFor(td, uv);
                    colorProviderUsed++;
                }
                else
                {
                    c = BiomeColorHelper.GetMinimapColor(biome);
                    biomeHelperUsed++;
                }
            }
            else
            {
                c = BiomeColorHelper.GetMinimapColor(biome);
                biomeHelperUsed++;
            }

            tileColors[i] = (Color32)c;
            
            // Store elevation (0-1 range, will be encoded in heightmap)
            // Use renderElevation if available (normalized for full range), otherwise fall back to elevation
            float elevation = td != null ? 
                (td.renderElevation > 0.001f ? td.renderElevation : td.elevation) : 
                planetGen.GetTileElevation(i);
            tileElevations[i] = Mathf.Clamp01(elevation);
            
            // Sample a few tiles to show what colors are being generated
            if (i < 5 || i == tileCount - 1)
            {
}
        }
res.tileColors = tileColors;

        // LUT: pixel -> tileIndex
        var lut = EquirectLUTBuilder.BuildLUT(grid, width, height);
        res.lut = lut;
        if (lut == null || lut.Length != width * height)
        {
            Debug.LogError($"[PlanetTextureBaker] LUT build FAILED! lut is {(lut == null ? "NULL" : $"length {lut.Length}, expected {width * height}")}");
            return res;
        }
// Paint pixels by LUT indirection (no polygon rasterization)
        var pixels = new Color32[width * height];
        var heightmapPixels = new Color32[width * height]; // NEW: Heightmap pixels
        
        int magentaCount = 0;
        int validTileCount = 0;
        
        for (int p = 0; p < pixels.Length; p++)
        {
            int idx = lut[p];
            if (idx < 0 || idx >= tileColors.Length) idx = 0;

            if (usePerPixelTextures)
            {
                // Per-pixel sampling from provider textures using equirectangular UV
                int x = p % width;
                int y = p / width;
                Vector2 uv = new Vector2((float)x / (float)width, (float)y / (float)height);
                var td = planetGen.GetHexTileData(idx);
                Color c = (colorProvider != null && td != null)
                    ? colorProvider.ColorFor(td, uv)
                    : BiomeColorHelper.GetMinimapColor(td != null ? td.biome : Biome.Any);
                var c32 = (Color32)c;
                pixels[p] = c32;

                if (c32.r == 255 && c32.g == 0 && c32.b == 255)
                    magentaCount++;
                else
                    validTileCount++;
            }
            else
            {
                Color32 tileColor = tileColors[idx];
                pixels[p] = tileColor;

                if (tileColor.r == 255 && tileColor.g == 0 && tileColor.b == 255)
                    magentaCount++;
                else
                    validTileCount++;
            }

            // Encode elevation in heightmap (grayscale: black=low, white=high)
            float elevation = (idx >= 0 && idx < tileElevations.Length) ? tileElevations[idx] : 0f;
            byte heightValue = (byte)(Mathf.Clamp01(elevation) * 255);
            heightmapPixels[p] = new Color32(heightValue, heightValue, heightValue, 255);
        }
// Build CPU textures then upload to GPU RenderTextures
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            name = $"PlanetTexture_CPU_{planetGen.gameObject.name}_{width}x{height}"
        };
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);

        var heightmapTex = new Texture2D(width, height, TextureFormat.R8, mipChain: true, linear: true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = $"PlanetHeightmap_CPU_{planetGen.gameObject.name}_{width}x{height}"
        };
        heightmapTex.SetPixels32(heightmapPixels);
        heightmapTex.Apply(updateMipmaps: true, makeNoLongerReadable: false);

        // Create RenderTextures
        var biomeRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            name = $"PlanetTexture_{planetGen.gameObject.name}_{width}x{height}"
        };
        biomeRT.Create();

        // Heightmap RT: prefer single-channel if supported, else fallback to ARGB32
        RenderTexture heightRT;
#if UNITY_2020_2_OR_NEWER
        heightRT = new RenderTexture(width, height, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = $"PlanetHeightmap_{planetGen.gameObject.name}_{width}x{height}"
        };
#else
        heightRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = $"PlanetHeightmap_{planetGen.gameObject.name}_{width}x{height}"
        };
#endif
        heightRT.Create();

        // Upload CPU textures to RTs
        Graphics.Blit(tex, biomeRT);
        Graphics.Blit(heightmapTex, heightRT);

        // Generate a normal map from the heightmap (simple central-difference method)
        try
        {
            var normalTex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = $"PlanetNormal_CPU_{planetGen.gameObject.name}_{width}x{height}"
            };

            Color32[] heightPixels = heightmapTex.GetPixels32();
            Color32[] normalPixels = new Color32[width * height];

            float strength = 1.0f; // normal strength; tweak later if desired
            for (int y = 0; y < height; y++)
            {
                int yUp = (y + 1) % height;
                int yDown = (y - 1 + height) % height;
                for (int x = 0; x < width; x++)
                {
                    int xL = (x - 1 + width) % width;
                    int xR = (x + 1) % width;

                    float hL = heightPixels[y * width + xL].r / 255.0f;
                    float hR = heightPixels[y * width + xR].r / 255.0f;
                    float hD = heightPixels[yDown * width + x].r / 255.0f;
                    float hU = heightPixels[yUp * width + x].r / 255.0f;

                    // derivatives
                    float dx = (hR - hL) * strength;
                    float dy = (hU - hD) * strength;

                    // normal: assume height is along Y; use [-dx, 1, -dy]
                    Vector3 n = new Vector3(-dx, 2.0f, -dy);
                    n.Normalize();

                    byte nx = (byte)(Mathf.Clamp01(n.x * 0.5f + 0.5f) * 255);
                    byte ny = (byte)(Mathf.Clamp01(n.y * 0.5f + 0.5f) * 255);
                    byte nz = (byte)(Mathf.Clamp01(n.z * 0.5f + 0.5f) * 255);
                    normalPixels[y * width + x] = new Color32(nx, ny, nz, 255);
                }
            }

            normalTex.SetPixels32(normalPixels);
            normalTex.Apply(updateMipmaps: true, makeNoLongerReadable: false);

            var normalRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = $"PlanetNormal_{planetGen.gameObject.name}_{width}x{height}"
            };
            normalRT.Create();
            Graphics.Blit(normalTex, normalRT);

            res.normalmap = normalRT;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlanetTextureBaker] Normal map generation failed: {ex.Message}");
            res.normalmap = null;
        }

        res.texture = biomeRT;
        res.heightmap = heightRT;
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
        // Fallback to CPU path if compute shader is not available
        if (computeShader == null)
        {
            Debug.LogWarning("[PlanetTextureBaker] Compute shader is null, falling back to CPU path");
            return Bake(planetGen, colorProvider, width, height);
        }

        // Use GPU path
        var gpuResult = PlanetTextureBakerGPU.Bake(planetGen, colorProvider, computeShader, width, height);
        
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
}
