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
                    var uvVal = uv.Value;
                    int x = Mathf.Clamp(Mathf.FloorToInt(uvVal.x * customMinimapTexture.width), 0, customMinimapTexture.width - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt(uvVal.y * customMinimapTexture.height), 0, customMinimapTexture.height - 1);
                    return customMinimapTexture.GetPixel(x, y);
                }
                break;

            case MinimapRenderMode.BiomeTextures:
                // Use individual biome textures
                if (_textureLookup != null && _textureLookup.TryGetValue(tile.biome, out var texture) && uv.HasValue)
                {
                    var uvVal = uv.Value;
                    int x = Mathf.Clamp(Mathf.FloorToInt(uvVal.x * texture.width), 0, texture.width - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt(uvVal.y * texture.height), 0, texture.height - 1);
                    return texture.GetPixel(x, y);
                }
                // Fallback to color if no texture assigned for this biome
                goto case MinimapRenderMode.BiomeColors;

            case MinimapRenderMode.BiomeColors:
            default:
                // Use solid biome colors
                if (_colorLookup != null && _colorLookup.TryGetValue(tile.biome, out var col))
                    return col;
                break;
        }

        // Fallback for any unhandled cases
        return Color.magenta;
    }
}
