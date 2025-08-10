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
            // Pre-cache biome textures
            foreach (var tex in _textureLookup.Values)
            {
                if (tex != null && !_texturePixels.ContainsKey(tex))
                {
                    _texturePixels[tex] = (tex.GetPixels32(), tex.width, tex.height);
                }
            }
            // Optionally cache custom texture
            if (customMinimapTexture != null && !_texturePixels.ContainsKey(customMinimapTexture))
            {
                _texturePixels[customMinimapTexture] = (customMinimapTexture.GetPixels32(), customMinimapTexture.width, customMinimapTexture.height);
            }
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
                    return SampleTexture(customMinimapTexture, uv.Value);
                }
                break;

            case MinimapRenderMode.BiomeTextures:
                // Use individual biome textures
                if (_textureLookup != null && _textureLookup.TryGetValue(tile.biome, out var texture) && uv.HasValue)
                {
                    return SampleTexture(texture, uv.Value);
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

    private Color SampleTexture(Texture2D texture, Vector2 uv)
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
            int x = Mathf.Clamp((int)(uv.x * texture.width), 0, texture.width - 1);
            int y = Mathf.Clamp((int)(uv.y * texture.height), 0, texture.height - 1);
            return texture.GetPixel(x, y);
        }
    }
    
    /// <summary>
    /// Fallback colors for biomes not configured in the ColorProvider
    /// </summary>
    private Color GetDefaultBiomeColor(Biome biome)
    {
        return biome switch
        {
            Biome.Ocean => new Color(0.2f, 0.4f, 0.8f, 1f),      // Blue
            Biome.Forest => new Color(0.2f, 0.6f, 0.2f, 1f),     // Green
            Biome.Desert => new Color(0.8f, 0.7f, 0.3f, 1f),     // Sandy
            Biome.Mountain => new Color(0.6f, 0.5f, 0.4f, 1f),   // Brown
            Biome.Plains => new Color(0.4f, 0.7f, 0.3f, 1f),     // Light green
            Biome.Snow => new Color(0.9f, 0.9f, 0.9f, 1f),       // White
            Biome.Tundra => new Color(0.6f, 0.7f, 0.8f, 1f),     // Light blue-gray
            Biome.Jungle => new Color(0.1f, 0.5f, 0.1f, 1f),     // Dark green
            Biome.Grassland => new Color(0.5f, 0.8f, 0.3f, 1f),  // Bright green
            Biome.Marsh => new Color(0.3f, 0.5f, 0.4f, 1f),      // Muddy green
            Biome.Swamp => new Color(0.2f, 0.4f, 0.3f, 1f),      // Dark muddy
            Biome.Taiga => new Color(0.3f, 0.6f, 0.4f, 1f),      // Forest green
            Biome.Savannah => new Color(0.7f, 0.6f, 0.3f, 1f),   // Dry grass
            Biome.Coast => new Color(0.4f, 0.6f, 0.8f, 1f),      // Light blue
            Biome.Volcanic => new Color(0.8f, 0.3f, 0.2f, 1f),   // Red-orange
            Biome.Steam => new Color(0.7f, 0.7f, 0.8f, 1f),      // Light gray
            
            // Mars biomes
            Biome.MartianRegolith => new Color(0.6f, 0.3f, 0.2f, 1f),  // Rusty red
            Biome.MartianCanyon => new Color(0.5f, 0.2f, 0.1f, 1f),    // Dark red
            Biome.MartianPolarIce => new Color(0.8f, 0.8f, 0.9f, 1f),  // Ice white
            Biome.MartianDunes => new Color(0.7f, 0.4f, 0.2f, 1f),     // Sandy red
            
            // Venus biomes
            Biome.VenusianLava => new Color(1.0f, 0.4f, 0.1f, 1f),     // Bright orange
            Biome.VenusianPlains => new Color(0.7f, 0.5f, 0.3f, 1f),   // Rocky brown
            Biome.VenusianHighlands => new Color(0.6f, 0.4f, 0.3f, 1f), // Dark brown
            
            // Add more planet-specific biomes as needed...
            
            _ => new Color(0.5f, 0.5f, 0.5f, 1f)                 // Gray fallback
        };
    }
}
