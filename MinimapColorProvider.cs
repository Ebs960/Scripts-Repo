using System;
using System.Collections.Generic;
using UnityEngine;

public enum MinimapRenderMode
{
    BiomeColors,      // Use solid colors for each biome
    BiomeTextures,    // Use individual textures for each biome
    CustomTexture     // Use single custom equirectangular texture
}

[CreateAssetMenu(fileName = "MinimapColorProvider", menuName = "Minimap/Color Provider")]
public class MinimapColorProvider : ScriptableObject
{
    [Serializable]
    public struct BiomeColor
    {
        public Biome biome;   // Uses your Biome enum!
        public Color color;
    }

    [Serializable]
    public struct BiomeTexture
    {
        public Biome biome;
        public Texture2D texture;
    }

    [Header("Rendering Mode")]
    [Tooltip("Choose how to render the minimap")]
    public MinimapRenderMode renderMode = MinimapRenderMode.BiomeColors;

    [Header("Minimap color per biome (used when renderMode = BiomeColors)")]
    public List<BiomeColor> biomeColors = new List<BiomeColor>();

    [Header("Biome textures (used when renderMode = BiomeTextures)")]
    [Tooltip("Assign textures for each biome - these will be sampled using UV coordinates")]
    public List<BiomeTexture> biomeTextures = new List<BiomeTexture>();

    [Header("Optional: Single custom minimap texture (used when renderMode = CustomTexture)")]
    [Tooltip("Single equirectangular projection texture that overrides everything")]
    public Texture2D customMinimapTexture;

    private Dictionary<Biome, Color> _colorLookup;
    private Dictionary<Biome, Texture2D> _textureLookup;
    
    [Header("Performance")]
    [Tooltip("Cache texture pixel arrays for fast CPU sampling (recommended for minimap generation)")]
    public bool cacheTexturePixels = true;

    // Cache raw pixel data for textures to avoid slow Texture2D.GetPixel in tight loops
    private Dictionary<Texture2D, (Color32[] pixels, int width, int height)> _texturePixels;
    
    // Warn once per texture when it is not readable.
    private HashSet<Texture2D> _warnedNonReadableTextures;

    private void OnEnable()
    {
        _colorLookup = new Dictionary<Biome, Color>();
        foreach (var bc in biomeColors)
        {
            _colorLookup[bc.biome] = bc.color;
        }

        _textureLookup = new Dictionary<Biome, Texture2D>();
        foreach (var bt in biomeTextures)
        {
            if (bt.texture != null)
            {
                _textureLookup[bt.biome] = bt.texture;
            }
        }

        if (cacheTexturePixels)
        {
            _texturePixels = new Dictionary<Texture2D, (Color32[] pixels, int width, int height)>();
            _warnedNonReadableTextures = new HashSet<Texture2D>();
            // Pre-cache biome textures
            foreach (var tex in _textureLookup.Values)
            {
                if (tex != null && !_texturePixels.ContainsKey(tex))
                {
                    TryCacheTexturePixels(tex);
                }
            }
            // Optionally cache custom texture
            if (customMinimapTexture != null && !_texturePixels.ContainsKey(customMinimapTexture))
            {
                TryCacheTexturePixels(customMinimapTexture);
            }
        }
        else
        {
            _warnedNonReadableTextures = new HashSet<Texture2D>();
        }
    }

    /// <summary>
    /// Gets the minimap color for this tile based on the selected render mode.
    /// For BiomeTextures mode, pass the UV coordinates (0–1) for this tile in 'uv'.
    /// </summary>
    public Color ColorFor(HexTileData tile, Vector2? uv = null)
    {
        switch (renderMode)
        {
            case MinimapRenderMode.CustomTexture:
                // Use single custom equirectangular texture
                if (customMinimapTexture != null && uv.HasValue)
                {
                    // If texture cannot be sampled, fall back to biome default for the tile.
                    return SampleTexture(customMinimapTexture, uv.Value, GetDefaultBiomeColor(tile.biome));
                }
                break;

            case MinimapRenderMode.BiomeTextures:
                // Use individual biome textures
                if (_textureLookup != null && _textureLookup.TryGetValue(tile.biome, out var texture) && uv.HasValue)
                {
                    // If texture cannot be sampled, fall back to biome default for the tile.
                    return SampleTexture(texture, uv.Value, GetDefaultBiomeColor(tile.biome));
                }
                // Debug warning when texture is missing for this biome in BiomeTextures mode
                if (_textureLookup != null && !_textureLookup.ContainsKey(tile.biome))
                {
                    Debug.LogWarning($"[MinimapColorProvider] BiomeTextures mode: No texture assigned for biome '{tile.biome}'. Falling back to solid color. Please assign a texture for this biome in the MinimapColorProvider asset.");
                }
                // Fallback to color if no texture assigned for this biome
                goto case MinimapRenderMode.BiomeColors;

            case MinimapRenderMode.BiomeColors:
            default:
                // Use solid biome colors
                if (_colorLookup != null && _colorLookup.TryGetValue(tile.biome, out var col))
                    return col;
                
                // IMPROVED FALLBACK: Instead of magenta, use sensible defaults for common biomes
                return GetDefaultBiomeColor(tile.biome);
        }

        // Fallback for any unhandled cases
        return Color.magenta;
    }

    private Color SampleTexture(Texture2D texture, Vector2 uv, Color fallback)
    {
        uv.x = Mathf.Repeat(uv.x, 1f);
        uv.y = Mathf.Repeat(uv.y, 1f);

        if (cacheTexturePixels && _texturePixels != null && _texturePixels.TryGetValue(texture, out var entry))
        {
            int x = Mathf.Clamp((int)(uv.x * entry.width), 0, entry.width - 1);
            int y = Mathf.Clamp((int)(uv.y * entry.height), 0, entry.height - 1);
            int idx = y * entry.width + x;
            return (Color)entry.pixels[idx];
        }
        else
        {
            // Guard: if the texture is not readable, Unity will throw.
            if (texture == null || !texture.isReadable)
            {
                WarnNonReadable(texture);
                return fallback;
            }

            int x = Mathf.Clamp((int)(uv.x * texture.width), 0, texture.width - 1);
            int y = Mathf.Clamp((int)(uv.y * texture.height), 0, texture.height - 1);
            return texture.GetPixel(x, y);
        }
    }

    private void TryCacheTexturePixels(Texture2D tex)
    {
        if (tex == null) return;
        if (_texturePixels == null) return;

        // Some imported textures are not readable at runtime; attempting GetPixels32() will throw.
        if (!tex.isReadable)
        {
            WarnNonReadable(tex);
            return;
        }

        try
        {
            _texturePixels[tex] = (tex.GetPixels32(), tex.width, tex.height);
        }
        catch (Exception ex)
        {
            // Catch any unexpected import/format edge cases; don't kill scene load.
            Debug.LogWarning($"[MinimapColorProvider] Failed to cache pixels for texture '{tex.name}'. Falling back to biome colors. Exception: {ex.Message}");
        }
    }

    private void WarnNonReadable(Texture2D tex)
    {
        if (tex == null) return;
        if (_warnedNonReadableTextures == null) _warnedNonReadableTextures = new HashSet<Texture2D>();
        if (_warnedNonReadableTextures.Contains(tex)) return;
        _warnedNonReadableTextures.Add(tex);

        Debug.LogWarning($"[MinimapColorProvider] Texture '{tex.name}' is not readable. Minimap texture sampling requires Read/Write Enabled in the texture import settings. Falling back to biome colors for this texture.");
    }

    // (Removed generic fallback color; callers provide a biome-aware fallback.)
    
    /// <summary>
    /// Fallback colors for biomes not configured in the ColorProvider (uses BiomeColorHelper)
    /// </summary>
    private Color GetDefaultBiomeColor(Biome biome)
    {
        return BiomeColorHelper.GetMinimapColor(biome);
    }

    /// <summary>
    /// Convenience: get a color directly from the provider for a Biome value.
    /// Returns a sensible fallback when the provider has no entry for the biome.
    /// </summary>
    public Color ColorForBiome(Biome biome)
    {
        if (_colorLookup != null && _colorLookup.TryGetValue(biome, out var c))
            return c;
        return GetDefaultBiomeColor(biome);
    }
}
