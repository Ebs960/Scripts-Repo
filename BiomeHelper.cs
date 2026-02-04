// BiomeHelper.cs  (single file holding everything)

using UnityEngine;
using System;

/// ---------- ENUM & DATA STRUCTS ----------
public enum Biome {
    Ocean, Coast, Desert, Savannah, Plains, Forest, Jungle, Glacier, Tundra, Grassland, Taiga, Swamp, Seas,
    River,
    Lake,       // Inland freshwater body
    MoonDunes,
    Volcanic,  // Added new volcanic terrain
    Steamlands,     // Added new steam terrain
    Ashlands,  // New biome for scorched map types
    Scorched,   // New biome for extremely hot and dry conditions in scorched maps
    Floodlands,  // New biome unique to monsoon map types
    Hellscape,   // New biome for demonic worlds - extremely hostile terrain
    Arctic,       // New biome - the coldest of all polar biomes
    IcicleField,  // Ice World exclusive biome

    // Real Solar System Planet Biomes
    MartianRegolith,    // Mars - dusty red soil
    MartianPolarIce,    // Mars - polar ice caps
    MartianDunes,       // Mars - sand dunes
    
    VenusLava,       // Venus - molten lava flows
    VenusianPlains,     // Venus - rocky plains
    
    MercuryPlains,   // Mercury - heavily cratered surface
    MercurianIce,       // Mercury - cold night side ice formations
    
    JovianClouds,       // Jupiter - gas giant cloud layers
    
    SaturnSurface,    // Saturn - cloud layers
    
    UranusSurface,     // Uranus - methane atmosphere
    
    NeptuneSurface,   // Neptune - standard surface terrain
    
    PlutoCryo,          // Pluto - frozen nitrogen plains
    
    TitanLakes,         // Titan - methane/ethane lakes
    TitanDunes,         // Titan - hydrocarbon sand dunes
    TitanIce,           // Titan - water ice bedrock
    
    EuropaIce,          // Europa - surface ice crust
    EuropaRidges,       // Europa - ice ridges and cracks
    
    IoVolcanic,         // Io - active volcanic surface
    IoSulfur,           // Io - sulfur deposits
    
    Any
}

[Serializable]                 // keep Serializable so it shows in Inspector
public class BiomeSettings {
    public Biome biome;                    // which biome
    public Texture2D albedoTexture;        // Albedo (color) map
    public Texture2D normalTexture;        // Normal (bump) map, optional
    public GameObject[] decorations;       // optional prefabs
    [Range(0f,1f)]
    public float spawnChance = 0.15f;

}

public struct YieldValues {
    public int food, prod, gold, sci, cult;
}

/// ---------- HELPER LOGIC ----------
public static class BiomeHelper {

    /// <summary>
    /// Determine whether a tile elevation qualifies as a mountain.
    /// This replaces the old `Biome.Mountain` enum member which has been removed.
    /// </summary>
    public static bool IsMountain(float elevation, float mountainThreshold)
    {
        return elevation >= mountainThreshold;
    }

    /// <summary>
    /// Determine whether a tile elevation qualifies as a hill (but not a mountain).
    /// </summary>
    public static bool IsHill(float elevation, float hillThreshold, float mountainThreshold)
    {
        return elevation >= hillThreshold && elevation < mountainThreshold;
    }

    public static Biome GetBiome(bool isLand, float temperature, float moisture,
        bool isRainforestMapType = false, bool isScorchedMapType = false,
        bool isInfernalMapType = false, bool isDemonicMapType = false,
        bool isIceWorldMapType = false, bool isMonsoonMapType = false,
        bool isMarsWorldType = false, bool isVenusWorldType = false,
        bool isMercuryWorldType = false, bool isJupiterWorldType = false,
        bool isSaturnWorldType = false, bool isUranusWorldType = false,
        bool isNeptuneWorldType = false, bool isPlutoWorldType = false,
        bool isTitanWorldType = false, bool isEuropaWorldType = false,
        bool isIoWorldType = false,
        bool isLunaWorldType = false,
        float northSouth = 0f, float eastWest = 0f)
    {
        // Debug logging for planet-specific biome assignment (only for non-Earth planets)
        if (isMarsWorldType || isVenusWorldType || isMercuryWorldType || isJupiterWorldType ||
            isSaturnWorldType || isUranusWorldType || isNeptuneWorldType || isPlutoWorldType ||
            isTitanWorldType || isEuropaWorldType || isIoWorldType ||
            isLunaWorldType)
        {
            string planetType = "";
            if (isMarsWorldType) planetType = "Mars";
            else if (isVenusWorldType) planetType = "Venus";
            else if (isMercuryWorldType) planetType = "Mercury";
            else if (isJupiterWorldType) planetType = "Jupiter";
            else if (isSaturnWorldType) planetType = "Saturn";
            else if (isUranusWorldType) planetType = "Uranus";
            else if (isNeptuneWorldType) planetType = "Neptune";
            else if (isPlutoWorldType) planetType = "Pluto";
            else if (isTitanWorldType) planetType = "Titan";
            else if (isEuropaWorldType) planetType = "Europa";
            else if (isIoWorldType) planetType = "Io";
            else if (isLunaWorldType) planetType = "Luna";
            
            // Debug log for planet-specific biome processing (uncomment for detailed debugging)
            // UnityEngine.Debug.Log($"[BiomeHelper] Processing {planetType} biome: isLand={isLand}, temp={temperature:F2}, moisture={moisture:F2}");
            _ = planetType; // Suppress warning - planetType available for debugging when needed
        }

    
        if (!isLand) {
            // Water tiles: freeze into glaciers when sufficiently cold.
            // Glacier is now a water biome determined solely by low temperature.
            if (temperature <= 0.27f) return Biome.Glacier;
            return Biome.Ocean;
        }
        
        // === EXPLICIT PLANET-SPECIFIC BIOME RULES - NO FALLBACKS ===
        
        // MARS - Complete temperature/moisture coverage
        if (isMarsWorldType) {
            // Polar regions (coldest)
            if (temperature <= 0.15f) {
                if (moisture > 0.5f) return Biome.MartianPolarIce;
                return Biome.MartianDunes; // Cold, dry
            }
            // Cold regions
            if (temperature <= 0.25f) {
                if (moisture < 0.3f) return Biome.MartianDunes;
                return Biome.MartianRegolith;
            }
            // Warm regions
            if (temperature <= 0.5f) {
                if (moisture > 0.3f) return Biome.MartianRegolith;
                return Biome.MartianRegolith;
            }
            // Hot regions
            if (moisture > 0.4f) return Biome.MartianRegolith;
            return Biome.MartianRegolith; // Default Mars
        }
        
        // VENUS - Complete temperature/moisture coverage
        if (isVenusWorldType) {
            // Hottest regions
            if (temperature > 0.6f) {
                if (moisture < 0.2f) return Biome.VenusLava;
                return Biome.VenusianPlains;
            }
            // Warm regions
            if (temperature > 0.4f) {
                if (moisture < 0.5f) return Biome.VenusianPlains;
                return Biome.VenusianPlains;
            }
            // All other regions
            if (moisture > 0.3f) return Biome.VenusianPlains;
            return Biome.VenusianPlains; // Default Venus
        }
        
        // MERCURY - Day/Night hemispheres based on east-west split
        if (isMercuryWorldType) {
            // Determine if this is day side (east-west near center) or night side (edges)
            float normalizedLong = eastWest * 180f; // -180 to +180
            bool isDaySide = (normalizedLong >= -90f && normalizedLong <= 90f);
            
            if (isDaySide) {
                return Biome.MercuryPlains;
            }
            else {
                // Night side - ENTIRE hemisphere gets MercurianIce (realistic tidally locked planet)
                return Biome.MercurianIce;
            }
        }
        
        // JUPITER - North/south based: storms at poles, clouds elsewhere
        if (isJupiterWorldType) {
            // North/south is passed in normalized range [-1,1]. Use absolute value to detect polar caps.
            float absLat = Mathf.Abs(northSouth);
            // Consider 70+ degrees as polar region (0.78 in normalized [-1,1])
            if (absLat >= 0.78f) return Biome.JovianClouds; // Polar storms
            return Biome.JovianClouds; // Elsewhere
        }
        
        // SATURN - Complete temperature/moisture coverage
        if (isSaturnWorldType) {
            return Biome.SaturnSurface; // Default Saturn
        }
        
        // URANUS - Complete temperature/moisture coverage
        if (isUranusWorldType) {
            // Very cold regions
            if (temperature < -0.4f) {
                if (moisture > 0.7f) return Biome.UranusSurface;
                return Biome.UranusSurface;
            }
            // Cold regions
            if (temperature < -0.2f) {
                if (moisture > 0.5f) return Biome.UranusSurface;
                return Biome.UranusSurface;
            }
            // All other regions
            if (moisture > 0.6f) return Biome.UranusSurface;
            return Biome.UranusSurface; // Default Uranus
        }
        
        // NEPTUNE - Complete temperature/moisture coverage
        if (isNeptuneWorldType) {
            // Very cold regions
            if (temperature < -0.4f) {
                if (moisture > 0.7f) return Biome.NeptuneSurface;
                return Biome.NeptuneSurface;
            }
            // Cold regions
            if (temperature < -0.2f) {
                if (moisture > 0.5f) return Biome.NeptuneSurface;
                return Biome.NeptuneSurface;
            }
            // All other regions
            if (moisture > 0.6f) return Biome.NeptuneSurface;
            return Biome.NeptuneSurface; // Default Neptune
        }
        
        // PLUTO - Complete temperature/moisture coverage
        if (isPlutoWorldType) {
            return Biome.PlutoCryo; // Default Pluto
        }
        
        // TITAN - Complete temperature/moisture coverage
        if (isTitanWorldType) {
            // Very cold regions
            if (temperature < -0.4f) {
                if (moisture > 0.5f) return Biome.TitanLakes;
                return Biome.TitanIce;
            }
            // Cold regions
            if (temperature < -0.2f) {
                if (moisture < 0.3f) return Biome.TitanDunes;
                return Biome.TitanIce;
            }
            // All other regions
            if (moisture > 0.6f) return Biome.TitanLakes;
            if (moisture < 0.4f) return Biome.TitanDunes;
            return Biome.TitanIce; // Default Titan
        }
        
        // EUROPA - Complete temperature/moisture coverage
        if (isEuropaWorldType) {
            // Very cold regions
            if (temperature < -0.5f) {
                if (moisture > 0.5f) return Biome.EuropaIce;
                return Biome.EuropaRidges;
            }
            // Cold regions
            if (temperature < -0.3f) {
                if (moisture > 0.3f) return Biome.EuropaRidges;
                return Biome.EuropaIce;
            }
            // All other regions
            if (moisture > 0.4f) return Biome.EuropaIce;
            return Biome.EuropaRidges; // Default Europa
        }
        
        // IO - Complete temperature/moisture coverage
        if (isIoWorldType) {
            // Cold regions
            if (temperature < -0.3f) {
                if (moisture < 0.2f) return Biome.IoSulfur;
                return Biome.IoVolcanic;
            }
            // Moderate regions
            if (temperature < 0.0f) {
                if (moisture > 0.2f) return Biome.IoVolcanic;
                return Biome.IoSulfur;
            }
            // All other regions
            if (moisture > 0.3f) return Biome.IoVolcanic;
            return Biome.IoSulfur; // Default Io
        }
        
        // LUNA - Just Moon Dunes for now
        if (isLunaWorldType) {
            return Biome.MoonDunes; // Default moon
        }

        // === EARTH-ONLY SPECIAL MAP TYPES ===
        
        // ICE WORLD: Exclusive biomes
        if (isIceWorldMapType)
        {
            if (temperature < 0.25f && moisture > 0.7f)
                return Biome.IcicleField; // Wettest, coldest mapped to IcicleField
            if (temperature < 0.25f && moisture > 0.45f)
                return Biome.IcicleField; // Drier, cold = IcicleField
            // fallback to normal cold/frozen logic below
        }

        // DEMONIC WORLD: Coherent, characteristic-based logic
        if (isDemonicMapType && temperature > 0.7f) {
            if (temperature > 0.85f) {
                // Brimstone removed: fallback to Hellscape for extremely hot regions
                return Biome.Hellscape; // Extremely hot region
            }
            if (temperature > 0.7f) {
                if (moisture < 0.2f) return Biome.Scorched; // Very hot, very dry
                if (moisture < 0.4f) return Biome.Ashlands; // Very hot, dry
                if (moisture < 0.7f) return Biome.Ashlands; // Very hot, medium-wet
                return Biome.Steamlands; // Very hot, wet
            }
        }

        // For extremely high temperatures in infernal maps
        if (isInfernalMapType && temperature > 0.85f) {
            if (moisture > 0.75f) return Biome.Steamlands;
            if (moisture > 0.5f) return Biome.Ashlands; // Hot + Very Wet = Steamlands vents
            return Biome.Volcanic;                          // Very hot = Volcanic terrain
        }

        // For extremely high temperatures in scorched maps
        if (isScorchedMapType && temperature > 0.85f) {
            if (temperature > 0.90f && moisture < 0.2f) return Biome.Scorched;  // Extremely hot + Very Dry = Scorched wastes
            if (moisture > 0.75f) return Biome.Steamlands;                         // Extremely hot + Very Wet = Steamlands vents
            if (moisture > 0.5f) return Biome.Ashlands; // Hot + Medium Wet = Charred remains of forest
            return Biome.Ashlands;                          // Hot + Dry = Ashlands
        }

        // Very high moisture in hot/warm climates creates rainforests in rainforest map types
        if (isRainforestMapType && temperature > 0.7f && moisture > 0.6f) {
            return Biome.Jungle;
        }

        // MONSOON MAP TYPE: Unique biome
        if (isMonsoonMapType && temperature > 0.4f && temperature < 0.8f && moisture > 0.8f)
        {
            return Biome.Floodlands;
        }

        // === EARTH-ONLY STANDARD BIOME LOGIC ===
        // This section only executes for Earth (when no planet flags are set)
        
        // CRITICAL: Return early if ANY planet flag is set to prevent Earth biomes on other planets
        if (isMarsWorldType || isVenusWorldType || isMercuryWorldType || isJupiterWorldType ||
            isSaturnWorldType || isUranusWorldType || isNeptuneWorldType || isPlutoWorldType ||
            isTitanWorldType || isEuropaWorldType || isIoWorldType ||
            isLunaWorldType)
        {
            // If we reach here, the planet-specific logic above missed a temperature/moisture combination
            UnityEngine.Debug.LogError($"[BiomeHelper] CRITICAL ERROR: Planet-specific biome logic failed! " +
                $"Temp: {temperature:F2}, Moisture: {moisture:F2}. " +
                $"Planet: Mars={isMarsWorldType}, Venus={isVenusWorldType}, Mercury={isMercuryWorldType}, " +
                $"Jupiter={isJupiterWorldType}, Saturn={isSaturnWorldType}, Uranus={isUranusWorldType}, " +
                $"Neptune={isNeptuneWorldType}, Pluto={isPlutoWorldType}, Titan={isTitanWorldType}, " +
                $"Europa={isEuropaWorldType}, Io={isIoWorldType}, " +
                $"Luna={isLunaWorldType}");

            // Emergency fallback - return first planet-specific biome we can find
            
            Debug.LogWarning("[BiomeHelper] EMERGENCY FALLBACK: Assigning first available planet-specific biome.");
            if (isMarsWorldType) return Biome.MartianRegolith;
            if (isVenusWorldType) return Biome.VenusianPlains;
            if (isMercuryWorldType) return Biome.MercuryPlains;
            if (isJupiterWorldType) return Biome.JovianClouds;
            if (isSaturnWorldType) return Biome.SaturnSurface;
            if (isUranusWorldType) return Biome.UranusSurface;
            if (isNeptuneWorldType) return Biome.NeptuneSurface;
            if (isPlutoWorldType) return Biome.PlutoCryo;
            if (isTitanWorldType) return Biome.TitanIce;
            if (isEuropaWorldType) return Biome.EuropaIce;
            if (isIoWorldType) return Biome.IoSulfur;
            if (isLunaWorldType) return Biome.MoonDunes;
            
            // This should NEVER be reached
            UnityEngine.Debug.LogError("[BiomeHelper] EMERGENCY FALLBACK FAILED! Returning Plains as last resort.");
            return Biome.Plains;
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

        // Cold climates (Earth only)
        if (temperature > 0.15f) {
            if (moisture < 0.20f) return Biome.Tundra;
            if (moisture < 0.75f) return Biome.Taiga;
            return Biome.Swamp;
        }

        // EARTH POLAR BIOMES ONLY (temperature <= 0.20f) - Should never be reached by other planets
        if (temperature <= 0.15f) {
            return Biome.Arctic;
        }

        // Fallback for any missed cases (should rarely be reached)
        UnityEngine.Debug.LogWarning($"[BiomeHelper] Unexpected biome assignment fallback reached - Temp: {temperature:F2}, Moisture: {moisture:F2}, Planet flags set: {isMarsWorldType || isVenusWorldType || isMercuryWorldType || isJupiterWorldType || isSaturnWorldType || isUranusWorldType || isNeptuneWorldType || isPlutoWorldType || isTitanWorldType || isEuropaWorldType || isIoWorldType || isLunaWorldType}");
        return Biome.Plains;
    }

    /// <summary>
    /// Validate biome assignment - log if inappropriate biomes are assigned to specific planets
    /// </summary>
    public static Biome ValidateAndLogBiome(Biome assignedBiome, bool isMarsWorldType, bool isVenusWorldType, 
        bool isMercuryWorldType, bool isJupiterWorldType, bool isSaturnWorldType, bool isUranusWorldType,
        bool isNeptuneWorldType, bool isPlutoWorldType, bool isTitanWorldType, bool isEuropaWorldType,
        bool isIoWorldType, bool isLunaWorldType)
    {
        // Check for inappropriate polar biomes on planets that shouldn't have them
        if (assignedBiome == Biome.Glacier || assignedBiome == Biome.Tundra || assignedBiome == Biome.Arctic)
        {
            if (isVenusWorldType || isMercuryWorldType || isSaturnWorldType || isIoWorldType || 
                isLunaWorldType)
            {
                string planetType = isVenusWorldType ? "Venus" : isMercuryWorldType ? "Mercury" :
                                   isSaturnWorldType ? "Saturn" : isIoWorldType ? "Io" : "Luna";
                UnityEngine.Debug.LogWarning($"[BiomeHelper] WARNING: {planetType} incorrectly assigned {assignedBiome} biome!");
            }
        }
        
        return assignedBiome;
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
        Biome.Glacier => new YieldValues { food = 0, prod = 0, gold = 1, sci = 2, cult = 1 },
        Biome.Tundra => new YieldValues { food = 1, prod = 1, gold = 0, sci = 1, cult = 1 },
        Biome.Grassland => new YieldValues { food = 1, prod = 2, gold = 0, sci = 0, cult = 1 },
        Biome.Taiga => new YieldValues { food = 1, prod = 3, gold = 0, sci = 0, cult = 1 },
        Biome.Swamp => new YieldValues { food = 2, prod = 0, gold = 0, sci = 1, cult = 2 },
        Biome.River => new YieldValues { food = 1, prod = 0, gold = 1, sci = 1, cult = 1 },
        Biome.Lake => new YieldValues { food = 3, prod = 0, gold = 1, sci = 0, cult = 2 },
        Biome.MoonDunes => new YieldValues { food = 0, prod = 1, gold = 0, sci = 1, cult = 0 },
        Biome.Volcanic => new YieldValues { food = 0, prod = 3, gold = 2, sci = 0, cult = 0 },
        Biome.Steamlands => new YieldValues { food = 0, prod = 2, gold = 3, sci = 0, cult = 0 },
        Biome.Ashlands => new YieldValues { food = 1, prod = 2, gold = 0, sci = 1, cult = 1 },
        Biome.Scorched => new YieldValues { food = 0, prod = 1, gold = 2, sci = 2, cult = 0 },
        Biome.Floodlands => new YieldValues { food = 2, prod = 1, gold = 0, sci = 0, cult = 1 },
        Biome.Hellscape => new YieldValues { food = 1, prod = 5, gold = 2, sci = 3, cult = 0 },
        Biome.Arctic => new YieldValues { food = 1, prod = 1, gold = 0, sci = 1, cult = 1 },
        Biome.IcicleField => new YieldValues { food = 0, prod = 2, gold = 1, sci = 3, cult = 0 },

        // Mars Biomes
        Biome.MartianRegolith => new YieldValues { food = 0, prod = 3, gold = 2, sci = 2, cult = 1 },
        Biome.MartianPolarIce => new YieldValues { food = 1, prod = 1, gold = 0, sci = 2, cult = 0 },
        Biome.MartianDunes => new YieldValues { food = 0, prod = 1, gold = 0, sci = 1, cult = 0 },

        // Venus Biomes
        Biome.VenusLava => new YieldValues { food = 0, prod = 5, gold = 3, sci = 1, cult = 0 },
        Biome.VenusianPlains => new YieldValues { food = 0, prod = 3, gold = 2, sci = 1, cult = 0 },

        // Mercury Biomes
        Biome.MercuryPlains => new YieldValues { food = 0, prod = 1, gold = 3, sci = 2, cult = 0 },
        Biome.MercurianIce => new YieldValues { food = 1, prod = 1, gold = 1, sci = 4, cult = 0 },

        // Gas Giants
        Biome.JovianClouds => new YieldValues { food = 0, prod = 2, gold = 4, sci = 3, cult = 1 },
        Biome.SaturnSurface => new YieldValues { food = 0, prod = 2, gold = 3, sci = 3, cult = 0 },

        // Ice Giants
        Biome.UranusSurface => new YieldValues { food = 0, prod = 3, gold = 2, sci = 3, cult = 0 },
        Biome.NeptuneSurface => new YieldValues { food = 0, prod = 2, gold = 2, sci = 3, cult = 1 },

        // Pluto
        Biome.PlutoCryo => new YieldValues { food = 0, prod = 1, gold = 1, sci = 4, cult = 2 },

        // Moons and others
        Biome.TitanLakes => new YieldValues { food = 1, prod = 2, gold = 4, sci = 3, cult = 0 },
        Biome.TitanDunes => new YieldValues { food = 0, prod = 2, gold = 2, sci = 2, cult = 0 },
        Biome.TitanIce => new YieldValues { food = 1, prod = 1, gold = 1, sci = 2, cult = 0 },
        Biome.EuropaIce => new YieldValues { food = 2, prod = 1, gold = 1, sci = 3, cult = 0 },
        Biome.EuropaRidges => new YieldValues { food = 1, prod = 2, gold = 2, sci = 4, cult = 0 },
        Biome.IoVolcanic => new YieldValues { food = 0, prod = 6, gold = 3, sci = 2, cult = 0 },
        Biome.IoSulfur => new YieldValues { food = 0, prod = 3, gold = 4, sci = 1, cult = 0 },

        _ => new YieldValues { food = 1, prod = 1, gold = 1, sci = 1, cult = 1 }
    };

    // Returns only temperate-allowed biomes regardless of temperature extremes
    public static Biome GetTemperateBiome(float moisture)
    {
        if (moisture > 0.8f) return Biome.Swamp;
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
        Biome.Volcanic => 4,
        Biome.Steamlands => 2,
        Biome.Ashlands => 1,
        Biome.Scorched => 0,
        Biome.Floodlands => 1,
        Biome.Hellscape => 0,
        Biome.Arctic => 0,
        Biome.IcicleField => 1,

        // Planet-specific
        Biome.MartianRegolith => 0,
        Biome.MartianPolarIce => 1,
        Biome.MartianDunes => 0,

        Biome.VenusLava => 0,
        Biome.VenusianPlains => 0,

        Biome.MercuryPlains => 2,
        Biome.MercurianIce => 1,

        Biome.JovianClouds => 1,
        Biome.SaturnSurface => 1,

        Biome.UranusSurface => 0,
        Biome.NeptuneSurface => 1,

        Biome.PlutoCryo => 3,

        Biome.TitanLakes => 0,
        Biome.Lake => 0,
        Biome.TitanDunes => 1,
        Biome.TitanIce => 0,
        Biome.EuropaRidges => 2,
        Biome.EuropaIce => 0,
        Biome.IoVolcanic => 0,
        Biome.IoSulfur => 0,

        _ => 0
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
        Biome.Swamp => 3,
        Biome.Taiga => 2,

        Biome.Ocean => 1,
        Biome.Seas => 1,
        Biome.Lake => 2,

        Biome.Volcanic => 3,
        Biome.Steamlands => 2,
        Biome.Ashlands => 2,
        Biome.Scorched => 3,
        Biome.Floodlands => 2,
        Biome.Arctic => 2,
        Biome.IcicleField => 3,
        Biome.Glacier => 4,

        // Planet-specific
        Biome.MartianRegolith => 2,
        Biome.MartianPolarIce => 2,
        Biome.MartianDunes => 3,

        Biome.VenusLava => 4,
        Biome.VenusianPlains => 2,

        Biome.MercuryPlains => 3,
        Biome.MercurianIce => 2,

        Biome.JovianClouds => 2,
        Biome.SaturnSurface => 2,

        Biome.UranusSurface => 3,
        Biome.NeptuneSurface => 2,

        Biome.PlutoCryo => 3,

        Biome.TitanLakes => 2,
        Biome.TitanDunes => 3,
        Biome.TitanIce => 2,
        Biome.EuropaIce => 1,
        Biome.EuropaRidges => 3,
        Biome.IoVolcanic => 4,
        Biome.IoSulfur => 2,

        _ => 1
    };
    
    
    /// <summary>
    /// Returns the effective movement cost for a specific tile, taking into account
    /// improvements on the tile (e.g., roads that provide movement bonuses).
    /// Unit parameter is optional for future expansion (unit-specific effects).
    /// </summary>
    public static int GetMovementCost(HexTileData tile, UnityEngine.MonoBehaviour unit = null)
    {
        if (tile == null) return 99;
        int baseCost = GetMovementCost(tile.biome);

        // If there's an improvement that modifies movement, apply it as a flat reduction
        // NOTE: We interpret ImprovementData.movementSpeedBonus as a flat movement-cost reducer
        // (rounded), which has the same gameplay effect as "adds movement points when moving on this tile".
        if (tile.improvement != null)
        {
            float bonus = tile.improvement.movementSpeedBonus;
            int reduced = Mathf.RoundToInt(baseCost - bonus);
            // Keep impassable/high-cost sentinel values intact
            if (baseCost >= 99) return baseCost;
            return Mathf.Clamp(reduced, 1, 98);
        }

        return baseCost;
    }
    /// <summary>
    /// Checks if a biome causes damage to units
    /// </summary>
    public static bool IsDamagingBiome(Biome biome)
    {
        return biome switch {
            Biome.Volcanic => true,
            Biome.Steamlands => true,
            Biome.Ashlands => true,
            Biome.Scorched => true,
            Biome.Floodlands => true,
            Biome.Hellscape => true,
            Biome.Arctic => true,
            Biome.IcicleField => true,

            // Planet-specific damaging biomes
            Biome.VenusLava => true,
            Biome.MercuryPlains => true,
            Biome.MercurianIce => true,
            Biome.UranusSurface => true,
            Biome.NeptuneSurface => true,
            Biome.PlutoCryo => true,
            Biome.IoVolcanic => true,
            Biome.IoSulfur => true,

            _ => false
        };
    }
    
    /// <summary>
    /// Returns the damage percentage for a biome (0-1 value)
    /// </summary>
    public static float GetBiomeDamage(Biome biome)
    {
        return biome switch {
            Biome.Volcanic => 0.15f,
            Biome.Steamlands => 0.10f,
            Biome.Ashlands => 0.05f,
            Biome.Scorched => 0.20f,
            Biome.Floodlands => 0.10f,
            Biome.Hellscape => 0.30f,
            Biome.Arctic => 0.05f,
            Biome.IcicleField => 0.15f,

            // Planet-specific values
            Biome.VenusLava => 0.50f,
            Biome.MercuryPlains => 0.20f,
            Biome.MercurianIce => 0.10f,
            Biome.UranusSurface => 0.25f,
            Biome.NeptuneSurface => 0.15f,
            Biome.PlutoCryo => 0.20f,
            Biome.IoVolcanic => 0.60f,
            Biome.IoSulfur => 0.20f,

            _ => 0f
        };
    }

    /// <summary>
    /// Get biome-specific terrain settings used by the battle map generator and (optionally) Vista graphs.
    /// This reuses the existing BiomeTerrainSettings from BattleTerrainNoiseSystem so we don't duplicate config.
    /// </summary>
    public static BiomeTerrainSettings GetTerrainSettings(Biome biome)
    {
        switch (biome)
        {
            case Biome.Plains:
            case Biome.Grassland:
            case Biome.Savannah:
                return BiomeTerrainSettings.CreatePlains();

            case Biome.Desert:
            case Biome.Scorched:
            case Biome.Ashlands:
                return BiomeTerrainSettings.CreateDesert();

            // Mountain terrain is determined by elevation; PlutoCryo remains mountain-like
            case Biome.PlutoCryo:
                return BiomeTerrainSettings.CreateMountain();

            case Biome.Forest:
            case Biome.Taiga:
            case Biome.Jungle:
                return BiomeTerrainSettings.CreateForest();

            case Biome.Swamp:
            case Biome.Floodlands:
                return BiomeTerrainSettings.CreateSwamp();

            case Biome.Glacier:
            case Biome.Arctic:
            case Biome.IcicleField:
            case Biome.MartianPolarIce:
            case Biome.MercurianIce:
            case Biome.EuropaIce:
                return BiomeTerrainSettings.CreateIce();

            case Biome.Ocean:
            case Biome.Seas:
            case Biome.Coast:
            case Biome.Lake:
            case Biome.TitanLakes:
                return BiomeTerrainSettings.CreateOcean();

            case Biome.Volcanic:
            case Biome.VenusLava:
            case Biome.IoVolcanic:
                return BiomeTerrainSettings.CreateVolcanic();

            case Biome.MoonDunes:
            case Biome.MartianRegolith:
            case Biome.MartianDunes:
            case Biome.MercuryPlains:
                return BiomeTerrainSettings.CreateMoon();

            case Biome.VenusianPlains:
                return BiomeTerrainSettings.CreateVenus();

            default:
                return BiomeTerrainSettings.CreatePlains();
        }
    }
}
