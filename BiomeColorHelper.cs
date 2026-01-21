using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Compatibility helper providing static access to biome colors.
/// Prefers colors from a MinimapColorProvider instance when available,
/// otherwise falls back to a stable generated color.
/// </summary>
public static class BiomeColorHelper
{
    private static Dictionary<Biome, Color> _colorMap;
    private static bool _initialized = false;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _colorMap = new Dictionary<Biome, Color>();

        // Try Resources first (optional asset named 'MinimapColorProvider' in Resources)
        MinimapColorProvider provider = Resources.Load<MinimapColorProvider>("MinimapColorProvider");
        if (provider == null)
        {
            // Fallback: any instance in scene (editor/runtime)
            provider = Object.FindObjectOfType<MinimapColorProvider>();
        }

        if (provider != null && provider.biomeColors != null)
        {
            foreach (var bc in provider.biomeColors)
            {
                if (!_colorMap.ContainsKey(bc.biome))
                    _colorMap[bc.biome] = bc.color;
            }
        }

        _initialized = true;
    }

    /// <summary>
    /// Get the color used for minimap rendering for this biome.
    /// </summary>
    public static Color GetMinimapColor(Biome biome)
    {
        EnsureInitialized();
        if (_colorMap != null && _colorMap.TryGetValue(biome, out var c))
            return c;

        // Stable fallback color based on biome hash
        int h = Mathf.Abs(biome.GetHashCode());
        float hue = (h % 360) / 360f;
        return Color.HSVToRGB(hue, 0.45f, 0.92f);
    }

    /// <summary>
    /// Get color optimized for battle map rendering. Defaults to a slightly darker tint.
    /// </summary>
    public static Color GetBattleMapColor(Biome biome)
    {
        Color baseCol = GetMinimapColor(biome);
        // Slightly desaturate / darken for battle map readability
        Color.RGBToHSV(baseCol, out float h, out float s, out float v);
        s = Mathf.Clamp01(s * 0.9f);
        v = Mathf.Clamp01(v * 0.85f);
        return Color.HSVToRGB(h, s, v);
    }

    /// <summary>
    /// Clear cached provider/colors (useful in editor when assets change).
    /// </summary>
    public static void ClearCache()
    {
        _initialized = false;
        _colorMap = null;
    }
}
