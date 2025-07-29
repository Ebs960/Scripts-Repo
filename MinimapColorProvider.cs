using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MinimapColorProvider", menuName = "Minimap/Color Provider")]
public class MinimapColorProvider : ScriptableObject
{
    [Serializable]
    public struct BiomeColor
    {
        public string biomeId; // match your HexTileData biome identifier (enum name/string)
        public Color color;
    }

    [Header("Priority 1: Use HexTileData.MinimapColor if available")]
    public bool preferTileColorField = true;

    [Header("Priority 2: Biome → Color map (by string id)")]
    public List<BiomeColor> biomeMap = new List<BiomeColor>();

    private Dictionary<string, Color> _lookup;

    private void OnEnable()
    {
        _lookup = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        foreach (var bc in biomeMap)
        {
            if (!string.IsNullOrEmpty(bc.biomeId))
                _lookup[bc.biomeId] = bc.color;
        }
    }

    public Color ColorFor(HexTileData tile)
{
    // 1) If tile exposes a color, use it
    var exposed = TryGetTileColor(tile, out var c);
    if (preferTileColorField && exposed) return c;

    // 2) If tile exposes a biome id string, use the map
    var hasBiome = TryGetTileBiomeId(tile, out var id);
    if (hasBiome && _lookup != null && _lookup.TryGetValue(id, out var mapped))
        return mapped;

    // 3) Fallback: Use a biome color if you have it
    if (tile.biome != null) {
        // If Biome has a color field, use it (adjust as needed)
        // return tile.biome.color;
    }

    // 4) Fallback: Use a default color
    return Color.magenta;
}


    // ---- Reflection helpers (adjust to your actual field names if needed) ----

    private bool TryGetTileColor(HexTileData tile, out Color c)
    {
        // If your HexTileData has a 'Color MinimapColor' or similar, use it.
        c = default;
        var tp = tile.GetType();
        var f = tp.GetField("MinimapColor");
        if (f != null && f.FieldType == typeof(Color)) { c = (Color)f.GetValue(tile); return true; }

        var p = tp.GetProperty("MinimapColor");
        if (p != null && p.PropertyType == typeof(Color)) { c = (Color)p.GetValue(tile, null); return true; }

        return false;
    }

    private bool TryGetTileBiomeId(HexTileData tile, out string id)
    {
        id = null;
        var tp = tile.GetType();

        var f = tp.GetField("BiomeId");
        if (f != null) { var v = f.GetValue(tile); id = v?.ToString(); if (!string.IsNullOrEmpty(id)) return true; }

        var p = tp.GetProperty("BiomeId");
        if (p != null) { var v = p.GetValue(tile, null); id = v?.ToString(); if (!string.IsNullOrEmpty(id)) return true; }

        // Alternative common names
        f = tp.GetField("Biome");
        if (f != null) { var v = f.GetValue(tile); id = v?.ToString(); if (!string.IsNullOrEmpty(id)) return true; }

        p = tp.GetProperty("Biome");
        if (p != null) { var v = p.GetValue(tile, null); id = v?.ToString(); if (!string.IsNullOrEmpty(id)) return true; }

        return false;
    }
}
