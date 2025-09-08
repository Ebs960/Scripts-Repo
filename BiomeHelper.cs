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
    PineForest,
    IcicleField,  // Ice World exclusive biome
    CryoForest,   // Ice World exclusive biome
    
    // Real Solar System Planet Biomes
    MartianRegolith,    // Mars - dusty red soil
    MartianCanyon,      // Mars - deep canyons and valleys
    MartianPolarIce,    // Mars - polar ice caps
    MartianDunes,       // Mars - sand dunes
    
    VenusLava,       // Venus - molten lava flows
    VenusianPlains,     // Venus - rocky plains
    VenusHighlands,  // Venus - elevated terrain
    
    MercuryCraters,   // Mercury - heavily cratered surface
    MercuryBasalt,    // Mercury - basaltic plains
    MercuryScarp,     // Mercury - cliff-like scarps
    MercurianIce,       // Mercury - cold night side ice formations
    
    JovianClouds,       // Jupiter - gas giant cloud layers
    JovianStorm,        // Jupiter - storm systems like Great Red Spot
    
    SaturnRings,     // Saturn - ring particle fields
    SaturnSurface,    // Saturn - cloud layers
    
    UranusIce,         // Uranus - ice giant surface
    UranusSurface,     // Uranus - methane atmosphere
    
    NeptuneWinds,     // Neptune - extreme wind patterns
    NeptuneIce,       // Neptune - ice formations
    NeptuneSurface,   // Neptune - standard surface terrain
    
    PlutoCryo,          // Pluto - frozen nitrogen plains
    PlutoTholins,       // Pluto - organic compound deposits
    PlutoMountains,     // Pluto - methane/nitrogen mountains
    
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

    public static Biome GetBiome(bool isLand, float temperature, float moisture,
        bool isRainforestMapType = false, bool isScorchedMapType = false,
        bool isInfernalMapType = false, bool isDemonicMapType = false,
        bool isIceWorldMapType = false, bool isMonsoonMapType = false,
        bool isMarsWorldType = false, bool isVenusWorldType = false,
        bool isMercuryWorldType = false, bool isJupiterWorldType = false,
        bool isSaturnWorldType = false, bool isUranusWorldType = false,
        bool isNeptuneWorldType = false, bool isPlutoWorldType = false,
        bool isTitanWorldType = false, bool isEuropaWorldType = false,
        bool isIoWorldType = false, bool isGanymedeWorldType = false,
        bool isCallistoWorldType = false, bool isLunaWorldType = false,
        float latitude = 0f, float longitude = 0f)
    {
        // Debug logging for planet-specific biome assignment (only for non-Earth planets)
        if (isMarsWorldType || isVenusWorldType || isMercuryWorldType || isJupiterWorldType ||
            isSaturnWorldType || isUranusWorldType || isNeptuneWorldType || isPlutoWorldType ||
            isTitanWorldType || isEuropaWorldType || isIoWorldType || isGanymedeWorldType ||
            isCallistoWorldType || isLunaWorldType)
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
            else if (isGanymedeWorldType) planetType = "Ganymede";
            else if (isCallistoWorldType) planetType = "Callisto";
            else if (isLunaWorldType) planetType = "Luna";
            
            // Only log a sample of biome assignments to avoid spam
            if (UnityEngine.Random.Range(0, 1000) < 1) // 0.1% chance to log
            {
                UnityEngine.Debug.Log($"[BiomeHelper] {planetType} biome assignment - Land: {isLand}, Temp: {temperature:F2}, Moisture: {moisture:F2}");
            }
        }

        // === EARTH-ONLY GUARANTEED POLAR OVERRIDE - FIRST PRIORITY ===
        // This MUST execute before any other logic to guarantee polar regions
        bool isEarthPlanet = !isMarsWorldType && !isVenusWorldType && !isMercuryWorldType && !isJupiterWorldType &&
                            !isSaturnWorldType && !isUranusWorldType && !isNeptuneWorldType && !isPlutoWorldType &&
                            !isTitanWorldType && !isEuropaWorldType && !isIoWorldType && !isGanymedeWorldType &&
                            !isCallistoWorldType && !isLunaWorldType;
        
        if (isEarthPlanet)
        {
            float absLatitude = Mathf.Abs(latitude);
            
            // FORCE POLAR CAP: make the very top/bottom strictly Arctic for all tiles (no Glaciers).
            // Using ~80° -> 0.80 in normalized [-1,1]. Adjust if you want a larger/smaller cap.
            const float PolarArcticThreshold = 0.80f; // ~80°
            if (absLatitude >= PolarArcticThreshold)
            {
                return Biome.Arctic;
            }

            // Sub-polar band (~60–80°): bias to Tundra/Taiga for land when cold enough.
            if (absLatitude > 0.67f)
            {
                if (isLand && temperature <= 0.35f)
                {
                    return moisture > 0.6f ? Biome.Taiga : Biome.Tundra;
                }
                // Otherwise fall through to standard Earth logic
            }
        }

        if (!isLand) {
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
                if (moisture > 0.3f) return Biome.MartianCanyon;
                return Biome.MartianRegolith;
            }
            // Hot regions
            if (moisture > 0.4f) return Biome.MartianCanyon;
            return Biome.MartianRegolith; // Default Mars
        }
        
        // VENUS - Complete temperature/moisture coverage
        if (isVenusWorldType) {
            // Hottest regions
            if (temperature > 0.6f) {
                if (moisture < 0.2f) return Biome.VenusLava;
                return Biome.VenusHighlands;
            }
            // Warm regions
            if (temperature > 0.4f) {
                if (moisture < 0.5f) return Biome.VenusianPlains;
                return Biome.VenusHighlands;
            }
            // All other regions
            if (moisture > 0.3f) return Biome.VenusHighlands;
            return Biome.VenusianPlains; // Default Venus
        }
        
        // MERCURY - Day/Night hemispheres based on longitude
        if (isMercuryWorldType) {
            // Determine if this is day side (longitude -90 to +90) or night side (longitude 90 to 270 or -90 to -270)
            float normalizedLong = longitude; // longitude should be in range -180 to +180
            bool isDaySide = (normalizedLong >= -90f && normalizedLong <= 90f);
            
            // Debug logging for Mercury hemisphere assignment
            if (UnityEngine.Random.Range(0, 1000) < 5) // 0.5% chance to log
            {
                string side = isDaySide ? "Day" : "Night";
                UnityEngine.Debug.Log($"[BiomeHelper] Mercury hemisphere - Long: {longitude:F1}°, Side: {side}");
            }
            
            if (isDaySide) {
                // Day side - Hot hemisphere gets traditional Mercury biomes
                if (temperature > 0.5f) {
                    if (moisture < 0.2f) return Biome.MercuryCraters;
                    return Biome.MercuryBasalt;
                }
                // Moderate day side regions
                if (temperature > 0.3f) {
                    if (moisture < 0.3f) return Biome.MercuryBasalt;
                    return Biome.MercuryScarp;
                }
                // Cooler day side areas
                if (moisture < 0.2f) return Biome.MercuryScarp;
                return Biome.MercuryCraters;
            }
            else {
                // Night side - ENTIRE hemisphere gets MercurianIce (realistic tidally locked planet)
                return Biome.MercurianIce;
            }
        }
        
        // JUPITER - Latitude-based: storms at poles, clouds elsewhere
        if (isJupiterWorldType) {
            // Latitude is expected in radians normalized to [-PI/2, PI/2] and then scaled to [-1,1] or already normalized [-1,1].
            // In this project latitude is passed in normalized range [-1,1]. Use absolute value to detect polar caps.
            float absLat = Mathf.Abs(latitude);
            // Consider 70+ degrees latitude as polar region (0.78 in normalized [-1,1])
            if (absLat >= 0.78f) return Biome.JovianStorm; // Polar storms
            return Biome.JovianClouds; // Elsewhere
        }
        
        // SATURN - Complete temperature/moisture coverage
        if (isSaturnWorldType) {
            // Cold regions
            if (temperature < -0.2f) {
                if (moisture > 0.7f) return Biome.SaturnRings;
                return Biome.SaturnSurface;
            }
            // Moderate cold regions
            if (temperature < 0.0f) {
                if (moisture > 0.5f) return Biome.SaturnSurface;
                return Biome.SaturnRings;
            }
            // All other regions
            if (moisture > 0.4f) return Biome.SaturnSurface;
            return Biome.SaturnRings; // Default Saturn
        }
        
        // URANUS - Complete temperature/moisture coverage
        if (isUranusWorldType) {
            // Very cold regions
            if (temperature < -0.4f) {
                if (moisture > 0.7f) return Biome.UranusSurface;
                return Biome.UranusIce;
            }
            // Cold regions
            if (temperature < -0.2f) {
                if (moisture > 0.5f) return Biome.UranusIce;
                return Biome.UranusSurface;
            }
            // All other regions
            if (moisture > 0.6f) return Biome.UranusIce;
            return Biome.UranusSurface; // Default Uranus
        }
        
        // NEPTUNE - Complete temperature/moisture coverage
        if (isNeptuneWorldType) {
            // Very cold regions
            if (temperature < -0.4f) {
                if (moisture > 0.7f) return Biome.NeptuneWinds;
                return Biome.NeptuneIce;
            }
            // Cold regions
            if (temperature < -0.2f) {
                if (moisture > 0.5f) return Biome.NeptuneIce;
                return Biome.NeptuneWinds;
            }
            // All other regions
            if (moisture > 0.6f) return Biome.NeptuneWinds;
            return Biome.NeptuneSurface; // Default Neptune
        }
        
        // PLUTO - Complete temperature/moisture coverage
        if (isPlutoWorldType) {
            // Extremely cold regions
            if (temperature < -0.5f) {
                if (moisture > 0.5f) return Biome.PlutoCryo;
                return Biome.PlutoTholins;
            }
            // Very cold regions
            if (temperature < -0.3f) {
                if (moisture > 0.3f) return Biome.PlutoMountains;
                return Biome.PlutoTholins;
            }
            // All other regions
            if (moisture > 0.4f) return Biome.PlutoCryo;
            return Biome.PlutoTholins; // Default Pluto
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
        
        // LUNA/GANYMEDE/CALLISTO - Complete temperature/moisture coverage
        if (isLunaWorldType || isGanymedeWorldType || isCallistoWorldType) {
            // Cold regions
            if (temperature < 0.0f) {
                if (moisture < 0.2f) return Biome.MoonDunes;
                return Biome.MoonCaves;
            }
            // Moderate regions
            if (temperature < 0.2f) {
                if (moisture > 0.2f) return Biome.MoonCaves;
                return Biome.MoonDunes;
            }
            // All other regions
            if (moisture > 0.3f) return Biome.MoonCaves;
            return Biome.MoonDunes; // Default moon
        }

        // === EARTH-ONLY SPECIAL MAP TYPES ===
        
        // ICE WORLD: Exclusive biomes
        if (isIceWorldMapType)
        {
            if (temperature < 0.25f && moisture > 0.7f)
                return Biome.CryoForest; // Wettest, coldest = CryoForest
            if (temperature < 0.25f && moisture > 0.45f)
                return Biome.IcicleField; // Drier, cold = IcicleField
            // fallback to normal cold/frozen logic below
        }

        // DEMONIC WORLD: Coherent, characteristic-based logic
        if (isDemonicMapType && temperature > 0.7f) {
            if (temperature > 0.85f) {
                if (moisture < 0.3f) return Biome.Brimstone; // Extremely hot and dry
                return Biome.Hellscape; // Extremely hot and wet
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
            isTitanWorldType || isEuropaWorldType || isIoWorldType || isGanymedeWorldType ||
            isCallistoWorldType || isLunaWorldType)
        {
            // If we reach here, the planet-specific logic above missed a temperature/moisture combination
            UnityEngine.Debug.LogError($"[BiomeHelper] CRITICAL ERROR: Planet-specific biome logic failed! " +
                $"Temp: {temperature:F2}, Moisture: {moisture:F2}. " +
                $"Planet: Mars={isMarsWorldType}, Venus={isVenusWorldType}, Mercury={isMercuryWorldType}, " +
                $"Jupiter={isJupiterWorldType}, Saturn={isSaturnWorldType}, Uranus={isUranusWorldType}, " +
                $"Neptune={isNeptuneWorldType}, Pluto={isPlutoWorldType}, Titan={isTitanWorldType}, " +
                $"Europa={isEuropaWorldType}, Io={isIoWorldType}, Ganymede={isGanymedeWorldType}, " +
                $"Callisto={isCallistoWorldType}, Luna={isLunaWorldType}");

            // Emergency fallback - return first planet-specific biome we can find
            
            Debug.LogWarning("[BiomeHelper] EMERGENCY FALLBACK: Assigning first available planet-specific biome.");
            if (isMarsWorldType) return Biome.MartianRegolith;
            if (isVenusWorldType) return Biome.VenusianPlains;
            if (isMercuryWorldType) return Biome.MercuryBasalt;
            if (isJupiterWorldType) return Biome.JovianClouds;
            if (isSaturnWorldType) return Biome.SaturnSurface;
            if (isUranusWorldType) return Biome.UranusIce;
            if (isNeptuneWorldType) return Biome.NeptuneSurface;
            if (isPlutoWorldType) return Biome.PlutoTholins;
            if (isTitanWorldType) return Biome.TitanIce;
            if (isEuropaWorldType) return Biome.EuropaIce;
            if (isIoWorldType) return Biome.IoSulfur;
            if (isGanymedeWorldType || isCallistoWorldType || isLunaWorldType) return Biome.MoonDunes;
            
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

        // The steppe/pines
        if (temperature > 0.30f) {
            if (moisture < 0.60f) return Biome.Steppe;
            return Biome.PineForest;
        }

        // Cold climates (Earth only)
        if (temperature > 0.20f) {
            if (moisture < 0.20f) return Biome.Tundra;
            if (moisture < 0.75f) return Biome.Taiga;
            return Biome.Marsh;
        }

        // EARTH POLAR BIOMES ONLY (temperature <= 0.20f) - Should never be reached by other planets
        if (temperature <= 0.20f) {
            if (moisture < 0.35f) return Biome.Frozen;
            if (moisture < 0.75f) return Biome.Tundra;
            return Biome.Snow;
        }

        // Fallback for any missed cases (should rarely be reached)
        UnityEngine.Debug.LogWarning($"[BiomeHelper] Unexpected biome assignment fallback reached - Temp: {temperature:F2}, Moisture: {moisture:F2}, Planet flags set: {isMarsWorldType || isVenusWorldType || isMercuryWorldType || isJupiterWorldType || isSaturnWorldType || isUranusWorldType || isNeptuneWorldType || isPlutoWorldType || isTitanWorldType || isEuropaWorldType || isIoWorldType || isGanymedeWorldType || isCallistoWorldType || isLunaWorldType}");
        return Biome.Plains;
    }

    /// <summary>
    /// Validate biome assignment - log if inappropriate biomes are assigned to specific planets
    /// </summary>
    public static Biome ValidateAndLogBiome(Biome assignedBiome, bool isMarsWorldType, bool isVenusWorldType, 
        bool isMercuryWorldType, bool isJupiterWorldType, bool isSaturnWorldType, bool isUranusWorldType,
        bool isNeptuneWorldType, bool isPlutoWorldType, bool isTitanWorldType, bool isEuropaWorldType,
        bool isIoWorldType, bool isGanymedeWorldType, bool isCallistoWorldType, bool isLunaWorldType)
    {
        // Check for inappropriate snow/polar biomes on planets that shouldn't have them
        if (assignedBiome == Biome.Snow || assignedBiome == Biome.Glacier || assignedBiome == Biome.Tundra || 
            assignedBiome == Biome.Frozen || assignedBiome == Biome.Arctic)
        {
            if (isVenusWorldType || isMercuryWorldType || isSaturnWorldType || isIoWorldType || 
                isGanymedeWorldType || isCallistoWorldType || isLunaWorldType)
            {
                string planetType = isVenusWorldType ? "Venus" : isMercuryWorldType ? "Mercury" : 
                                   isSaturnWorldType ? "Saturn" : isIoWorldType ? "Io" :
                                   isGanymedeWorldType ? "Ganymede" : isCallistoWorldType ? "Callisto" : "Luna";
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
        Biome.IcicleField => new YieldValues { food = 0, prod = 2, gold = 1, sci = 3, cult = 0 }, // Ice World exclusive - high science
        Biome.CryoForest => new YieldValues { food = 1, prod = 2, gold = 0, sci = 2, cult = 1 }, // Ice World exclusive - balanced
        
        // Mars Biomes - Mining/Science focused
        Biome.MartianRegolith => new YieldValues { food = 0, prod = 2, gold = 1, sci = 3, cult = 0 }, // High science potential
        Biome.MartianCanyon => new YieldValues { food = 0, prod = 3, gold = 2, sci = 2, cult = 1 }, // Rich mineral deposits
        Biome.MartianPolarIce => new YieldValues { food = 1, prod = 1, gold = 0, sci = 2, cult = 0 }, // Water source
        Biome.MartianDunes => new YieldValues { food = 0, prod = 1, gold = 0, sci = 1, cult = 0 }, // Barren but explorable
        
        // Venus Biomes - Extreme/Hostile
        Biome.VenusLava => new YieldValues { food = 0, prod = 5, gold = 3, sci = 1, cult = 0 }, // Extreme production
        Biome.VenusianPlains => new YieldValues { food = 0, prod = 3, gold = 2, sci = 1, cult = 0 }, // Industrial potential
        Biome.VenusHighlands => new YieldValues { food = 0, prod = 2, gold = 1, sci = 2, cult = 0 }, // Elevated research
        
        // Mercury Biomes - Extreme conditions
        Biome.MercuryCraters => new YieldValues { food = 0, prod = 1, gold = 3, sci = 2, cult = 0 }, // Rare metals
        Biome.MercuryBasalt => new YieldValues { food = 0, prod = 4, gold = 1, sci = 1, cult = 0 }, // Construction materials
        Biome.MercuryScarp => new YieldValues { food = 0, prod = 2, gold = 2, sci = 3, cult = 0 }, // Geological interest
        Biome.MercurianIce => new YieldValues { food = 1, prod = 1, gold = 1, sci = 4, cult = 0 }, // Water ice + cold research
        
        // Gas Giant Biomes - Atmospheric/Energy
        Biome.JovianClouds => new YieldValues { food = 0, prod = 2, gold = 4, sci = 3, cult = 1 }, // Gas harvesting
        Biome.JovianStorm => new YieldValues { food = 0, prod = 1, gold = 2, sci = 5, cult = 0 }, // Energy research
        Biome.SaturnRings => new YieldValues { food = 0, prod = 3, gold = 5, sci = 2, cult = 1 }, // Ring mining
        Biome.SaturnSurface => new YieldValues { food = 0, prod = 2, gold = 3, sci = 3, cult = 0 }, // Gas processing
        
        // Ice Giant Biomes
        Biome.UranusIce => new YieldValues { food = 1, prod = 2, gold = 1, sci = 4, cult = 0 }, // Cryogenic research
        Biome.UranusSurface => new YieldValues { food = 0, prod = 3, gold = 2, sci = 3, cult = 0 }, // Fuel production
        Biome.NeptuneWinds => new YieldValues { food = 0, prod = 1, gold = 1, sci = 5, cult = 0 }, // Atmospheric dynamics
        Biome.NeptuneIce => new YieldValues { food = 1, prod = 2, gold = 1, sci = 3, cult = 0 }, // Ice resources
        Biome.NeptuneSurface => new YieldValues { food = 0, prod = 2, gold = 2, sci = 3, cult = 1 }, // Standard Neptune terrain
        
        // Pluto Biomes - Extreme cold/distance
        Biome.PlutoCryo => new YieldValues { food = 0, prod = 1, gold = 1, sci = 4, cult = 2 }, // Frontier science
        Biome.PlutoTholins => new YieldValues { food = 0, prod = 2, gold = 3, sci = 3, cult = 1 }, // Organic chemistry
        Biome.PlutoMountains => new YieldValues { food = 0, prod = 3, gold = 2, sci = 2, cult = 1 }, // Rare formations
        
        // Moon Biomes - Specialized environments
        Biome.TitanLakes => new YieldValues { food = 1, prod = 2, gold = 4, sci = 3, cult = 0 }, // Hydrocarbon wealth
        Biome.TitanDunes => new YieldValues { food = 0, prod = 2, gold = 2, sci = 2, cult = 0 }, // Organic materials
        Biome.TitanIce => new YieldValues { food = 1, prod = 1, gold = 1, sci = 2, cult = 0 }, // Water ice
        Biome.EuropaIce => new YieldValues { food = 2, prod = 1, gold = 1, sci = 3, cult = 0 }, // Subsurface ocean
        Biome.EuropaRidges => new YieldValues { food = 1, prod = 2, gold = 2, sci = 4, cult = 0 }, // Geological activity
        Biome.IoVolcanic => new YieldValues { food = 0, prod = 6, gold = 3, sci = 2, cult = 0 }, // Extreme volcanism
        Biome.IoSulfur => new YieldValues { food = 0, prod = 3, gold = 4, sci = 1, cult = 0 }, // Sulfur mining
        
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
        Biome.IcicleField => 1,   // Minor defense from ice formations
        Biome.CryoForest => 2,    // Good defense from frozen trees
        
        // Real Planet Defense Bonuses
        Biome.MartianCanyon => 3,     // Excellent natural fortifications
        Biome.MartianRegolith => 0,   // No cover on dusty plains
        Biome.MartianPolarIce => 1,   // Some cover from ice formations
        Biome.MartianDunes => 0,      // Shifting sands provide no cover
        
        Biome.VenusLava => 0,      // Too hostile for defensive positions
        Biome.VenusianPlains => 0,    // Flat, no cover
        Biome.VenusHighlands => 2, // Elevated defensive positions
        
        Biome.MercuryCraters => 2,  // Crater rims provide cover
        Biome.MercuryBasalt => 0,   // Flat rocky plains
        Biome.MercuryScarp => 3,    // Cliff walls excellent for defense
        Biome.MercurianIce => 1,      // Some cover from ice formations
        
        Biome.JovianClouds => 1,      // Limited visibility in clouds
        Biome.JovianStorm => 0,       // Too chaotic for defense
        Biome.SaturnRings => 1,    // Ring particles provide some cover
        Biome.SaturnSurface => 1,   // Cloud cover
        
        Biome.UranusIce => 0,        // Flat ice surface
        Biome.UranusSurface => 0,    // Gaseous atmosphere
        Biome.NeptuneWinds => 0,    // Too chaotic for defense
        Biome.NeptuneIce => 0,      // Flat ice surface
        Biome.NeptuneSurface => 1,  // Some terrain features for cover
        
        Biome.PlutoMountains => 3,    // Mountain terrain
        Biome.PlutoCryo => 0,         // Flat frozen plains
        Biome.PlutoTholins => 0,      // Organic deposits, no cover
        
        Biome.TitanLakes => 0,        // Open liquid surfaces
        Biome.TitanDunes => 1,        // Sand dune cover
        Biome.TitanIce => 0,          // Flat ice surfaces
        Biome.EuropaRidges => 2,      // Ice ridge formations
        Biome.EuropaIce => 0,         // Smooth ice plains
        Biome.IoVolcanic => 0,        // Too active/dangerous
        Biome.IoSulfur => 0,          // Flat sulfur plains
        
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
        Biome.IcicleField => 3,   // Difficult traversal through ice spikes
        Biome.CryoForest => 2,    // Frozen trees slow movement
        Biome.Glacier => 4,      // Very high movement cost - glacier traversal
        
        // Real Planet Movement Costs
        Biome.MartianRegolith => 2,   // Dusty, shifting surface
        Biome.MartianCanyon => 3,     // Difficult canyon navigation
        Biome.MartianPolarIce => 2,   // Slippery ice surfaces
        Biome.MartianDunes => 3,      // Shifting sand dunes
        
        Biome.VenusLava => 4,      // Extremely dangerous to traverse
        Biome.VenusianPlains => 2,    // Rocky but navigable
        Biome.VenusHighlands => 2, // Elevated terrain
        
        Biome.MercuryCraters => 3,  // Navigating crater rims
        Biome.MercuryBasalt => 1,   // Solid rock surface
        Biome.MercuryScarp => 4,    // Steep cliff traversal
        Biome.MercurianIce => 2,      // Slippery ice surfaces
        
        Biome.JovianClouds => 2,      // Atmospheric flight
        Biome.JovianStorm => 4,       // Dangerous storm navigation
        Biome.SaturnRings => 3,    // Navigating ring particles
        Biome.SaturnSurface => 2,   // Standard atmospheric travel
        
        Biome.UranusIce => 2,        // Ice surface travel
        Biome.UranusSurface => 3,    // Hazardous atmosphere
        Biome.NeptuneWinds => 4,    // Extreme wind resistance
        Biome.NeptuneIce => 2,      // Standard ice travel
        Biome.NeptuneSurface => 2,  // Standard Neptune terrain
        
        Biome.PlutoCryo => 3,         // Extreme cold conditions
        Biome.PlutoTholins => 2,      // Organic compound terrain
        Biome.PlutoMountains => 3,    // Mountain traversal
        
        Biome.TitanLakes => 2,        // Liquid methane navigation
        Biome.TitanDunes => 3,        // Sand dune traversal
        Biome.TitanIce => 2,          // Ice surface travel
        Biome.EuropaIce => 1,         // Smooth ice, easy travel
        Biome.EuropaRidges => 3,      // Navigating ice ridges
        Biome.IoVolcanic => 4,        // Active volcanic surface
        Biome.IoSulfur => 2,          // Sulfur plains
        
        _ => 1  // Default cost
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
            Biome.Steam => true,
            Biome.Ashlands => true,
            Biome.Scorched => true,
            Biome.Floodlands => true,
            Biome.Hellscape => true,
            Biome.Brimstone => true,
            Biome.Frozen => true,
            Biome.Arctic => true,
            Biome.IcicleField => true,      // Extreme cold damage
            
            // Real Planet Damaging Biomes
            Biome.VenusLava => true,     // Molten lava damage
            Biome.MercuryCraters => true, // Radiation exposure
            Biome.MercuryBasalt => true,  // Extreme temperature swings
            Biome.MercuryScarp => true,   // Radiation exposure
            Biome.MercurianIce => true,     // Extreme cold damage
            Biome.JovianStorm => true,      // Storm damage
            Biome.UranusSurface => true,   // Toxic atmosphere
            Biome.NeptuneWinds => true,   // Extreme wind damage
            Biome.NeptuneSurface => true, // Harsh Neptune conditions
            Biome.PlutoCryo => true,        // Extreme cold
            Biome.IoVolcanic => true,       // Volcanic activity
            Biome.IoSulfur => true,         // Toxic sulfur exposure
            
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
            Biome.IcicleField => 0.15f, // Piercing ice damage
            
            // Real Planet Damage Values
            Biome.VenusLava => 0.50f,     // Extreme heat damage
            Biome.MercuryCraters => 0.20f,  // Radiation damage
            Biome.MercuryBasalt => 0.15f,   // Temperature extremes
            Biome.MercuryScarp => 0.25f,    // High radiation exposure
            Biome.MercurianIce => 0.10f,      // Cold damage (less than other Mercury biomes)
            Biome.JovianStorm => 0.40f,       // Severe storm damage
            Biome.UranusSurface => 0.25f,    // Toxic atmosphere
            Biome.NeptuneWinds => 0.30f,    // Extreme wind shear
            Biome.NeptuneSurface => 0.15f,  // Harsh Neptune conditions
            Biome.PlutoCryo => 0.20f,         // Extreme cold damage
            Biome.IoVolcanic => 0.60f,        // Highest damage - active volcanism
            Biome.IoSulfur => 0.20f,          // Sulfur toxicity
            
            _ => 0f
        };
    }
}
