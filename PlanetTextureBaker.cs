using UnityEngine;

/// <summary>
/// Generates a single equirectangular world texture for a planet, using:
/// - a pixel->tileIndex LUT (EquirectLUTBuilder)
/// - a per-tile color atlas derived from tile biomes (optionally via MinimapColorProvider)
/// 
/// IMPORTANT:
/// - Does NOT spawn per-tile prefabs
/// - Intended for both globe and flat map to share the same baked texture
/// </summary>
public static class PlanetTextureBaker
{
    public struct BakeResult
    {
        public Texture2D texture;
        public Texture2D heightmap; // NEW: Heightmap texture for elevation displacement
        public int[] lut;
        public int width;
        public int height;
        public Color32[] tileColors;
    }

    public static BakeResult Bake(PlanetGenerator planetGen, MinimapColorProvider colorProvider, int width = 2048, int height = 1024)
    {
        var res = new BakeResult { width = width, height = height };
        if (planetGen == null || planetGen.Grid == null || !planetGen.Grid.IsBuilt)
            return res;

        var grid = planetGen.Grid;
        int tileCount = grid.TileCount;
        if (tileCount <= 0) return res;

        // Build per-tile color atlas and elevation data
        var tileColors = new Color32[tileCount];
        var tileElevations = new float[tileCount]; // Store elevation for heightmap
        for (int i = 0; i < tileCount; i++)
        {
            var td = planetGen.GetHexTileData(i);
            // Fallback biome if generator doesn't have data (should be rare)
            Biome biome = td != null ? td.biome : planetGen.GetBaseBiome(i);

            // Compute equirect UV from tile center direction so BiomeTextures mode can sample consistently.
            Vector3 dir = grid.tileCenters[i].normalized;
            float lon = Mathf.Atan2(dir.z, dir.x);                 // -PI..PI
            float lat = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));   // -PI/2..PI/2
            float u = (lon / (2f * Mathf.PI)) + 0.5f;
            float v = (lat / Mathf.PI) + 0.5f;
            var uv = new Vector2(u, v);

            Color c;
            if (colorProvider != null)
            {
                // If td is null, we still need a dummy object for provider; fall back to biome color.
                if (td != null)
                    c = colorProvider.ColorFor(td, uv);
                else
                    c = BiomeColorHelper.GetMinimapColor(biome);
            }
            else
            {
                c = BiomeColorHelper.GetMinimapColor(biome);
            }

            tileColors[i] = (Color32)c;
            
            // Store elevation (0-1 range, will be encoded in heightmap)
            float elevation = td != null ? td.elevation : planetGen.GetTileElevation(i);
            tileElevations[i] = Mathf.Clamp01(elevation);
        }
        res.tileColors = tileColors;

        // LUT: pixel -> tileIndex
        var lut = EquirectLUTBuilder.BuildLUT(grid, width, height);
        res.lut = lut;
        if (lut == null || lut.Length != width * height) return res;

        // Paint pixels by LUT indirection (no polygon rasterization)
        var pixels = new Color32[width * height];
        var heightmapPixels = new Color32[width * height]; // NEW: Heightmap pixels
        for (int p = 0; p < pixels.Length; p++)
        {
            int idx = lut[p];
            if (idx < 0 || idx >= tileColors.Length) idx = 0;
            pixels[p] = tileColors[idx];
            
            // Encode elevation in heightmap (grayscale: black=low, white=high)
            float elevation = (idx >= 0 && idx < tileElevations.Length) ? tileElevations[idx] : 0f;
            byte heightValue = (byte)(Mathf.Clamp01(elevation) * 255);
            heightmapPixels[p] = new Color32(heightValue, heightValue, heightValue, 255);
        }

        // Build color texture
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            name = $"PlanetTexture_{planetGen.gameObject.name}_{width}x{height}"
        };
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        res.texture = tex;

        // Build heightmap texture
        var heightmapTex = new Texture2D(width, height, TextureFormat.R8, mipChain: true, linear: true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear, // Bilinear for smooth height transitions
            name = $"PlanetHeightmap_{planetGen.gameObject.name}_{width}x{height}"
        };
        heightmapTex.SetPixels32(heightmapPixels);
        heightmapTex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        res.heightmap = heightmapTex;

        return res;
    }
}

