using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Compatibility helper providing static access to biome colors.
/// Provides a stable built-in default palette.
///
/// IMPORTANT:
/// - This is intentionally NOT wired to MinimapColorProvider anymore.
/// - MinimapColorProvider is the single source of truth for minimap color mode
///   (ProviderColors vs DefaultColors). When that mode is DefaultColors it uses
///   this helper for the palette.
/// </summary>
public static class BiomeColorHelper
{
    private static Dictionary<Biome, Color> _colorMap;
    private static bool _initialized = false;

    // Default palette chosen for quick iteration in the minimap UI
    private static readonly Dictionary<Biome, Color> _defaultColors = new Dictionary<Biome, Color>() {
        { Biome.Ocean, new Color(0.02f, 0.30f, 0.55f) },
        { Biome.Seas, new Color(0.01f, 0.24f, 0.50f) },
        { Biome.River, new Color(0.12f, 0.54f, 0.90f) },
        { Biome.Lake, new Color(0.08f, 0.48f, 0.86f) },
        { Biome.Coast, new Color(0.87f, 0.76f, 0.55f) },
        { Biome.Desert, new Color(0.93f, 0.79f, 0.55f) },
        { Biome.Savannah, new Color(0.93f, 0.86f, 0.40f) },
        { Biome.Plains, new Color(0.64f, 0.82f, 0.42f) },
        { Biome.Grassland, new Color(0.55f, 0.78f, 0.36f) },
        { Biome.Forest, new Color(0.10f, 0.45f, 0.12f) },
        { Biome.Jungle, new Color(0.06f, 0.38f, 0.08f) },
        { Biome.Swamp, new Color(0.20f, 0.45f, 0.25f) },
        { Biome.Glacier, new Color(0.86f, 0.94f, 0.97f) },
        { Biome.Tundra, new Color(0.74f, 0.78f, 0.80f) },
        { Biome.Arctic, new Color(0.88f, 0.95f, 0.98f) },
        { Biome.IcicleField, new Color(0.90f, 0.96f, 0.99f) },
        { Biome.MoonDunes, new Color(0.70f, 0.70f, 0.74f) },
        { Biome.Volcanic, new Color(0.45f, 0.06f, 0.06f) },
        { Biome.Steamlands, new Color(0.50f, 0.28f, 0.28f) },
        { Biome.Ashlands, new Color(0.42f, 0.42f, 0.45f) },
        { Biome.Scorched, new Color(0.64f, 0.22f, 0.04f) },
        { Biome.Hellscape, new Color(0.22f, 0.05f, 0.12f) },
        { Biome.MartianRegolith, new Color(0.65f, 0.18f, 0.12f) },
        { Biome.MartianPolarIce, new Color(0.86f, 0.90f, 0.95f) },
        { Biome.MartianDunes, new Color(0.67f, 0.28f, 0.12f) },
        { Biome.VenusLava, new Color(0.95f, 0.45f, 0.06f) },
        { Biome.VenusianPlains, new Color(0.75f, 0.45f, 0.30f) },
        { Biome.MercuryPlains, new Color(0.55f, 0.50f, 0.45f) },
        { Biome.MercurianIce, new Color(0.78f, 0.85f, 0.90f) },
        { Biome.JovianClouds, new Color(0.92f, 0.86f, 0.72f) },
        { Biome.SaturnSurface, new Color(0.88f, 0.78f, 0.60f) },
        { Biome.UranusSurface, new Color(0.42f, 0.78f, 0.78f) },
        { Biome.NeptuneSurface, new Color(0.06f, 0.40f, 0.54f) },
        { Biome.PlutoCryo, new Color(0.80f, 0.86f, 0.92f) },
        { Biome.TitanLakes, new Color(0.06f, 0.38f, 0.44f) },
        { Biome.TitanDunes, new Color(0.68f, 0.58f, 0.42f) },
        { Biome.TitanIce, new Color(0.78f, 0.88f, 0.94f) },
        { Biome.EuropaIce, new Color(0.94f, 0.97f, 1.00f) },
        { Biome.EuropaRidges, new Color(0.78f, 0.86f, 0.92f) },
        { Biome.IoVolcanic, new Color(0.96f, 0.60f, 0.12f) },
        { Biome.IoSulfur, new Color(0.97f, 0.86f, 0.10f) },
        { Biome.Any, new Color(0.62f, 0.62f, 0.62f) }
    };

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        _colorMap = new Dictionary<Biome, Color>(_defaultColors);

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
