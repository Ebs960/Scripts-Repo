// BiomeHelper.cs  (single file holding everything)

using UnityEngine;
using System;

/// ---------- ENUM & DATA STRUCTS ----------
public enum Biome {
    Ocean, Coast, Desert, Savannah, Plains, Forest, Jungle, Snow, Glacier, Tundra, Grassland, Marsh, Taiga, Swamp, Seas, 
    Mountain,
    River,
    MoonDunes,
    MoonCaves,
    Volcanic,  // Added new volcanic terrain
    Steam,     // Added new steam terrain
    Rainforest, // Added new rainforest biome - even wetter than jungle
    Ashlands,  // New biome for scorched map types
    CharredForest, // New biome for scorched map types
    Scorched,   // New biome for extremely hot and dry conditions in scorched maps
    Floodlands,  // New biome unique to monsoon map types
    Hellscape,   // New biome for demonic worlds - extremely hostile terrain
    Brimstone,    // New biome for demonic worlds - sulfurous wastelands
    Frozen,       // New biome for polar land areas
    Arctic,       // New biome - the coldest of all polar biomes
    Steppe,
    PineForest
}

[Serializable]                 // keep Serializable so it shows in Inspector
public class BiomeSettings {
    public Biome biome;                    // which biome
    public Texture2D albedoTexture;        // Albedo (color) map
    public Texture2D normalTexture;        // Normal (bump) map, optional
    public GameObject[] decorations;       // optional prefabs
    [Range(0f,1f)]
    public float spawnChance = 0.15f;

    // --- SGT Feature/Detail Placement ---
    [Header("SGT Feature Placement")] 
    public GameObject[] featurePrefabs; // Prefabs to scatter as SGT features (e.g. cactus, pine tree)
    [Range(0f, 1f)]
    public float featureDensity = 0.05f; // How densely to scatter features (0 = none, 1 = max)
    public Vector2 featureScaleRange = new Vector2(1f, 1.2f); // Min/max scale for features
    public bool useDetails = false; // If true, use SGT LandscapeDetail for small/instanced objects
    public GameObject[] detailPrefabs; // Prefabs for SGT details (e.g. grass, pebbles)
    [Range(0f, 1f)]
    public float detailDensity = 0.1f;
    public Vector2 detailScaleRange = new Vector2(0.8f, 1.1f);
    // You can add more SGT-specific settings as needed (random rotation, alignment, etc.)
}

public struct YieldValues {
    public int food, prod, gold, sci, cult;
}

/// ---------- HELPER LOGIC ----------
public static class BiomeHelper {

    public static Biome GetBiome(bool isLand, float temperature, float moisture,
        bool isRainforestMapType = false, bool isScorchedMapType = false,
        bool isInfernalMapType = false, bool isDemonicMapType = false)
    {
        if (!isLand) {
            return Biome.Ocean;
        }

        // DEMONIC WORLD: Coherent, characteristic-based logic
        if (isDemonicMapType && temperature > 0.7f) {
            if (temperature > 0.85f) {
                if (moisture < 0.3f) return Biome.Hellscape; // Extremely hot and dry
                return Biome.Brimstone; // Extremely hot and wet
            }
            if (temperature > 0.7f) {
                if (moisture < 0.2f) return Biome.Scorched; // Very hot, very dry
                if (moisture < 0.4f) return Biome.Ashlands; // Very hot, dry
                if (moisture < 0.7f) return Biome.CharredForest; // Very hot, medium-wet
                return Biome.Steam; // Very hot, wet
            }
        }

        // For extremely high temperatures in infernal maps
        if (isInfernalMapType && temperature > 0.85f) {
            if (moisture > 0.75f) return Biome.Steam;
            if (moisture > 0.5f) return Biome.CharredForest; // Hot + Very Wet = Steam vents
            return Biome.Volcanic;                          // Very hot = Volcanic terrain
        }

        // For extremely high temperatures in scorched maps
        if (isScorchedMapType && temperature > 0.85f) {
            if (temperature > 0.90f && moisture < 0.2f) return Biome.Scorched;  // Extremely hot + Very Dry = Scorched wastes
            if (moisture > 0.75f) return Biome.Steam;                         // Extremely hot + Very Wet = Steam vents
            if (moisture > 0.5f) return Biome.CharredForest; // Hot + Medium Wet = Charred remains of forest
            return Biome.Ashlands;                          // Hot + Dry = Ashlands
        }

        // Very high moisture in hot/warm climates creates rainforests in rainforest map types
        if (isRainforestMapType && temperature > 0.7f && moisture > 0.6f) {
            return Biome.Rainforest;
        }

        // Hot climates
        if (temperature > 0.8f) {
            if (moisture < 0.4f) return Biome.Desert;
            if (moisture < 0.5f) return Biome.Savannah;
            if (moisture < 0.8f) return Biome.Jungle;
            return Biome.Swamp; // High moisture in hot climates creates swamp instead of rainforest in non-rainforest maps
        }

        // Warm climates
        if (temperature > 0.7f) {
            if (moisture < 0.3f) return Biome.Savannah;
            if (moisture < 0.6f) return Biome.Plains;
            return Biome.Jungle; // High moisture in warm climates creates jungle instead of rainforest in non-rainforest maps
        }

        // Temperate climates
        if (temperature > 0.4f) {
            if (moisture < 0.3f) return Biome.Plains;
            if (moisture < 0.6f) return Biome.Grassland;
            if (moisture < 0.8f) return Biome.Forest;
            return Biome.Swamp;
        }

        // The steppe/pines
        if (temperature > 0.30f) {
            if (moisture < 0.60f) return Biome.Steppe;
            return Biome.PineForest;
        }

        // Cold climates
        if (temperature > 0.20f) {
            if (moisture < 0.20f) return Biome.Tundra;
            if (moisture < 0.75f) return Biome.Taiga;
            return Biome.Marsh;
        }

        // Frozen climates
        if (moisture < 0.1f) return Biome.Tundra;
        if (moisture < 0.6f) return Biome.Snow;
        return Biome.Glacier;
    }


    public static YieldValues Yields(Biome biome) => biome switch {
        Biome.Ocean => new YieldValues { food = 1, prod = 0, gold = 1, sci = 0, cult = 0 },
        Biome.Coast => new YieldValues { food = 1, prod = 1, gold = 2, sci = 0, cult = 1 },
        Biome.Seas => new YieldValues { food = 2, prod = 0, gold = 1, sci = 0, cult = 0 },
        Biome.Desert => new YieldValues { food = 0, prod = 1, gold = 0, sci = 2, cult = 1 },
        Biome.Savannah => new YieldValues { food = 2, prod = 1, gold = 0, sci = 0, cult = 1 },
        Biome.Plains => new YieldValues { food = 3, prod = 1, gold = 0, sci = 0, cult = 0 },
        Biome.Forest => new YieldValues { food = 1, prod = 2, gold = 0, sci = 1, cult = 1 },
        Biome.Jungle => new YieldValues { food = 2, prod = 0, gold = 0, sci = 2, cult = 1 },
        Biome.Rainforest => new YieldValues { food = 3, prod = 0, gold = 0, sci = 2, cult = 2 }, // More food and culture than jungle
        Biome.Snow => new YieldValues { food = 0, prod = 1, gold = 0, sci = 2, cult = 0 },
        Biome.Glacier => new YieldValues { food = 0, prod = 0, gold = 1, sci = 2, cult = 1 },
        Biome.Tundra => new YieldValues { food = 1, prod = 1, gold = 0, sci = 1, cult = 1 },
        Biome.Grassland => new YieldValues { food = 1, prod = 2, gold = 0, sci = 0, cult = 1 },
        Biome.Marsh => new YieldValues { food = 2, prod = 0, gold = 0, sci = 1, cult = 2 },
        Biome.Taiga => new YieldValues { food = 1, prod = 3, gold = 0, sci = 0, cult = 1 },
        Biome.Swamp => new YieldValues { food = 2, prod = 0, gold = 0, sci = 1, cult = 2 },
        Biome.Mountain => new YieldValues { food = 0, prod = 2, gold = 1, sci = 1, cult = 0 },
        Biome.River => new YieldValues { food = 1, prod = 0, gold = 1, sci = 1, cult = 1 },
        Biome.MoonDunes => new YieldValues { food = 0, prod = 1, gold = 0, sci = 1, cult = 0 },
        Biome.MoonCaves => new YieldValues { food = 0, prod = 2, gold = 1, sci = 0, cult = 0 },
        Biome.Volcanic => new YieldValues { food = 0, prod = 3, gold = 2, sci = 0, cult = 0 }, // High production and gold, no food
        Biome.Steam => new YieldValues { food = 0, prod = 2, gold = 3, sci = 0, cult = 0 }, // High gold and good production, no food
        Biome.Ashlands => new YieldValues { food = 0, prod = 2, gold = 1, sci = 1, cult = 0 }, // Unique yields for Ashlands
        Biome.CharredForest => new YieldValues { food = 1, prod = 2, gold = 0, sci = 1, cult = 1 }, // Unique yields for Charred Forest
        Biome.Scorched => new YieldValues { food = 0, prod = 1, gold = 2, sci = 2, cult = 0 }, // Harsh but resource-rich
        Biome.Floodlands => new YieldValues { food = 2, prod = 1, gold = 0, sci = 0, cult = 1 }, // Unique yields for Floodlands
        Biome.Hellscape => new YieldValues { food = 1, prod = 5, gold = 2, sci = 3, cult = 0 }, // 
        Biome.Brimstone => new YieldValues { food = 0, prod = 7, gold = 2, sci = 4, cult = 0 }, // 
        Biome.Frozen => new YieldValues { food = 1, prod = 1, gold = 0, sci = 1, cult = 1 },
        Biome.Arctic => new YieldValues { food = 0, prod = 1, gold = 0, sci = 2, cult = 0 }, // Harsh, no food, but high science
        Biome.Steppe => new YieldValues { food = 1, prod = 1, gold = 0, sci = 0, cult = 0 }, // Unique yields for Steppe
        Biome.PineForest => new YieldValues { food = 1, prod = 1, gold = 0, sci = 0, cult = 0 }, // Unique yields for Pine Forest
        _ => new YieldValues { food = 1, prod = 1, gold = 1, sci = 1, cult = 1 }
    };

    // Returns only temperate-allowed biomes regardless of temperature extremes
    public static Biome GetTemperateBiome(float moisture)
    {
        if (moisture > 0.8f) return Biome.Marsh;
        if (moisture > 0.6f) return Biome.Forest;
        if (moisture > 0.45f) return Biome.Grassland;
        return Biome.Plains;
    }
    
    /// <summary>
    /// Returns the defensive bonus for a given biome.
    /// </summary>
    public static int GetDefenseBonus(Biome biome) => biome switch {
        Biome.Forest => 1,
        Biome.Jungle => 2,
        Biome.Rainforest => 2, // Same defense bonus as jungle
        Biome.Mountain => 3,
        Biome.Volcanic => 4,     // Significant defense bonus due to difficult terrain
        Biome.Steam => 2,        // Some defense bonus due to obscured visibility
        Biome.Ashlands => 1,     // Minor defense bonus from ash dunes
        Biome.CharredForest => 3, // Good defense bonus from burned tree remains
        Biome.Scorched => 0,     // No defense bonus - too harsh for cover
        Biome.Floodlands => 1,   // Minor defense bonus from floodlands
        Biome.Hellscape => 0,    // No defense bonus - extremely hostile terrain
        Biome.Brimstone => 0,    // No defense bonus - sulfurous wastelands
        Biome.Frozen => 0,        // No defense bonus - polar land areas
        Biome.Arctic => 0,        // No defense bonus - extremely cold polar land areas
        _ => 0  // No bonus for other biome types
    };
    
    /// <summary>
    /// Returns the movement cost for a given biome.
    /// </summary>
    public static int GetMovementCost(Biome biome) => biome switch {
        Biome.Plains => 1,
        Biome.Grassland => 1,
        Biome.Desert => 1,
        Biome.Tundra => 1,
        Biome.Savannah => 1,
        Biome.Coast => 1,
        
        Biome.Forest => 2,
        Biome.Jungle => 2,
        Biome.Rainforest => 3, // Harder to move through than jungle
        Biome.Marsh => 2,
        Biome.Swamp => 3,
        Biome.Taiga => 2,
        
        Biome.Mountain => 3,
        
        Biome.Ocean => 1,  // For naval units
        Biome.Seas => 1,   // For naval units
        
        Biome.Volcanic => 3,     // Very difficult to traverse
        Biome.Steam => 2,        // Moderately difficult due to hot steam vents
        Biome.Ashlands => 2,     // Difficult due to ash drifts
        Biome.CharredForest => 2, // Difficult due to fallen burned trees
        Biome.Scorched => 3,     // Very difficult to traverse due to extreme heat
        Biome.Floodlands => 2,   // Difficult due to floodwaters
        Biome.Hellscape => 2,    // No movement cost - extremely hostile terrain
        Biome.Brimstone => 2,    // No movement cost - sulfurous wastelands
        Biome.Frozen => 2,        // No movement cost - polar land areas
        Biome.Arctic => 2,        // Higher movement cost - extremely harsh conditions
        _ => 1  // Default cost
    };
    
    /// <summary>
    /// Checks if a biome causes damage to units
    /// </summary>
    public static bool IsDamagingBiome(Biome biome)
    {
        return biome switch {
            Biome.Volcanic => true,
            Biome.Steam => true,
            Biome.Ashlands => true,
            Biome.Scorched => true,
            Biome.Floodlands => true,
            Biome.Hellscape => true,
            Biome.Brimstone => true,
            Biome.Frozen => true,
            Biome.Arctic => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Returns the damage percentage for a biome (0-1 value)
    /// </summary>
    public static float GetBiomeDamage(Biome biome)
    {
        return biome switch {
            Biome.Volcanic => 0.15f,  // Significant damage from lava
            Biome.Steam => 0.10f,     // Moderate damage from scalding steam
            Biome.Ashlands => 0.05f,  // Minor damage from toxic ash
            Biome.Scorched => 0.20f,  // Highest damage - extremely hostile environment
            Biome.Floodlands => 0.10f, // Minor damage from floodwaters
            Biome.Hellscape => 0.30f,   // extreme damage - extremely hostile terrain
            Biome.Brimstone => 0.45f,   // extreme damage - sulfurous wastelands
            Biome.Frozen => 0.05f,      // Minor damage from polar land areas
            Biome.Arctic => 0.10f,      // More damage from extremely harsh arctic conditions
            _ => 0f
        };
    }
}
