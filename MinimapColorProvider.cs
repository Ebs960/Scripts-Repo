using System;
using UnityEngine;

/// <summary>
/// Legacy placeholder ScriptableObject.
///
/// The minimap now uses ONLY default biome colors (via BiomeColorHelper) and a GPU path.
/// This asset type is kept so existing scene/prefab references don't break, but it no longer
/// exposes per-biome configuration to avoid multiple competing minimap color systems.
/// </summary>
[CreateAssetMenu(fileName = "MinimapColorProvider", menuName = "Minimap/Color Provider")]
public class MinimapColorProvider : ScriptableObject
{
    /// <summary>
     /// Gets the minimap color for this tile.
     /// NOTE: This is intentionally the default palette only.
    /// </summary>
    public Color ColorFor(HexTileData tile, Vector2? uv = null)
    {
        if (tile == null) return Color.magenta;
        return BiomeColorHelper.GetMinimapColor(tile.biome);
    }

    /// <summary>
    /// Convenience: get a color directly from the provider for a Biome value.
    /// Returns a sensible fallback when the provider has no entry for the biome.
    /// </summary>
    public Color ColorForBiome(Biome biome)
    {
        return BiomeColorHelper.GetMinimapColor(biome);
    }
}
