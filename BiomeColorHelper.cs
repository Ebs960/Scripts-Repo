using UnityEngine;

/// <summary>
/// Centralized biome color mapping for different contexts (battle maps, minimap, etc.)
/// Consolidates duplicate color mappings from BattleMapGenerator and MinimapColorProvider
/// </summary>
public static class BiomeColorHelper
{
    /// <summary>
    /// Get color for a biome in battle map context (uses same colors as minimap for consistency)
    /// </summary>
    public static Color GetBattleMapColor(Biome biome)
    {
        // Use same comprehensive color mapping as minimap for consistency
        return GetMinimapColor(biome);
    }
    
    /// <summary>
    /// Get color for a biome in minimap context (comprehensive, includes planet-specific biomes)
    /// </summary>
    public static Color GetMinimapColor(Biome biome)
    {
        return biome switch
        {
            // Standard Earth biomes
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
            Biome.Seas => new Color(0.3f, 0.5f, 0.7f, 1f),       // Medium blue
            Biome.Volcanic => new Color(0.8f, 0.3f, 0.2f, 1f),   // Red-orange
            Biome.Steam => new Color(0.7f, 0.7f, 0.8f, 1f),      // Light gray
            Biome.Rainforest => new Color(0.1f, 0.4f, 0.1f, 1f), // Very dark green
            Biome.Ashlands => new Color(0.4f, 0.3f, 0.2f, 1f),   // Dark brown-gray
            Biome.CharredForest => new Color(0.2f, 0.2f, 0.1f, 1f), // Very dark
            Biome.Scorched => new Color(0.6f, 0.4f, 0.2f, 1f),   // Burnt orange
            Biome.Floodlands => new Color(0.3f, 0.5f, 0.6f, 1f), // Blue-green
            Biome.Hellscape => new Color(0.7f, 0.2f, 0.1f, 1f),  // Dark red
            Biome.Brimstone => new Color(0.5f, 0.4f, 0.2f, 1f),   // Sulfur yellow-brown
            Biome.Frozen => new Color(0.8f, 0.9f, 1f, 1f),        // Light blue-white
            Biome.Arctic => new Color(0.9f, 0.95f, 1f, 1f),       // Very light blue-white
            Biome.Steppe => new Color(0.6f, 0.6f, 0.4f, 1f),      // Yellow-green
            Biome.PineForest => new Color(0.2f, 0.5f, 0.3f, 1f), // Dark green
            Biome.IcicleField => new Color(0.85f, 0.9f, 0.95f, 1f), // Light blue-white
            Biome.CryoForest => new Color(0.7f, 0.8f, 0.9f, 1f),  // Blue-gray
            
            // Moon biomes
            Biome.MoonDunes => new Color(0.6f, 0.6f, 0.5f, 1f),   // Gray-tan
            Biome.MoonCaves => new Color(0.3f, 0.3f, 0.3f, 1f),  // Dark gray
            
            // Mars biomes
            Biome.MartianRegolith => new Color(0.6f, 0.3f, 0.2f, 1f),  // Rusty red
            Biome.MartianCanyon => new Color(0.5f, 0.2f, 0.1f, 1f),    // Dark red
            Biome.MartianPolarIce => new Color(0.8f, 0.8f, 0.9f, 1f),  // Ice white
            Biome.MartianDunes => new Color(0.7f, 0.4f, 0.2f, 1f),     // Sandy red
            
            // Venus biomes
            Biome.VenusLava => new Color(1.0f, 0.4f, 0.1f, 1f),     // Bright orange
            Biome.VenusianPlains => new Color(0.7f, 0.5f, 0.3f, 1f),   // Rocky brown
            Biome.VenusHighlands => new Color(0.6f, 0.4f, 0.3f, 1f), // Dark brown
            
            // Mercury biomes
            Biome.MercuryCraters => new Color(0.5f, 0.5f, 0.4f, 1f),  // Gray-brown
            Biome.MercuryBasalt => new Color(0.4f, 0.4f, 0.3f, 1f),   // Dark gray
            Biome.MercuryScarp => new Color(0.45f, 0.45f, 0.35f, 1f), // Medium gray
            Biome.MercurianIce => new Color(0.7f, 0.8f, 0.9f, 1f),    // Light blue-gray
            
            // Jupiter biomes
            Biome.JovianClouds => new Color(0.8f, 0.6f, 0.4f, 1f),    // Orange-brown
            Biome.JovianStorm => new Color(0.9f, 0.5f, 0.3f, 1f),     // Red-orange
            
            // Saturn biomes
            Biome.SaturnRings => new Color(0.7f, 0.7f, 0.6f, 1f),     // Light tan
            Biome.SaturnSurface => new Color(0.8f, 0.7f, 0.5f, 1f),   // Yellow-tan
            
            // Uranus biomes
            Biome.UranusIce => new Color(0.7f, 0.85f, 0.9f, 1f),      // Light blue
            Biome.UranusSurface => new Color(0.6f, 0.8f, 0.85f, 1f),  // Blue-gray
            
            // Neptune biomes
            Biome.NeptuneWinds => new Color(0.3f, 0.5f, 0.8f, 1f),    // Deep blue
            Biome.NeptuneIce => new Color(0.4f, 0.6f, 0.85f, 1f),     // Medium blue
            Biome.NeptuneSurface => new Color(0.35f, 0.55f, 0.8f, 1f), // Blue
            
            // Pluto biomes
            Biome.PlutoCryo => new Color(0.8f, 0.85f, 0.9f, 1f),       // Light gray-blue
            Biome.PlutoTholins => new Color(0.6f, 0.5f, 0.4f, 1f),    // Brown-gray
            Biome.PlutoMountains => new Color(0.7f, 0.7f, 0.65f, 1f), // Light gray
            
            // Titan biomes
            Biome.TitanLakes => new Color(0.3f, 0.4f, 0.5f, 1f),      // Dark blue-gray
            Biome.TitanDunes => new Color(0.5f, 0.4f, 0.3f, 1f),      // Brown-tan
            Biome.TitanIce => new Color(0.7f, 0.75f, 0.8f, 1f),      // Light gray
            
            // Europa biomes
            Biome.EuropaIce => new Color(0.8f, 0.9f, 0.95f, 1f),     // Very light blue
            Biome.EuropaRidges => new Color(0.7f, 0.8f, 0.9f, 1f),   // Light blue-gray
            
            // Io biomes
            Biome.IoVolcanic => new Color(0.9f, 0.4f, 0.2f, 1f),      // Bright red-orange
            Biome.IoSulfur => new Color(0.9f, 0.8f, 0.3f, 1f),       // Yellow
            
            // River (special case)
            Biome.River => new Color(0.2f, 0.5f, 0.7f, 1f),          // Blue
            
            // Lake (inland freshwater)
            Biome.Lake => new Color(0.25f, 0.55f, 0.75f, 1f),        // Lighter blue than river
            
            // Glacier (special case)
            Biome.Glacier => new Color(0.85f, 0.9f, 0.95f, 1f),      // Light blue-white
            
            _ => new Color(0.5f, 0.5f, 0.5f, 1f)                     // Gray fallback
        };
    }
    
    /// <summary>
    /// Get default color for a biome (fallback when context-specific method doesn't exist)
    /// </summary>
    public static Color GetDefaultColor(Biome biome)
    {
        return GetMinimapColor(biome); // Use minimap colors as default (most comprehensive)
    }
}

