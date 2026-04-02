// BiomeHelper.cs  (single file holding everything)

using UnityEngine;
using System;

/// ---------- ENUM & DATA STRUCTS ----------

/// <summary>
/// Which celestial body this planet represents. Controls biome assignment rules.
/// </summary>
public enum PlanetType
{
    Earth,
    Mars,
    Venus,
    Mercury,
    Jupiter,
    Saturn,
    Uranus,
    Neptune,
    Pluto,
    Titan,
    Europa,
    Luna
}

/// <summary>
/// Earth map variant (only applies when PlanetType == Earth). Controls special biome rules.
/// </summary>
public enum MapType
{
    Standard,   // Default Earth-like biomes
    Infernal,   // Volcanic/fire-themed
    Demonic,    // Hellish terrain
    IceWorld    // Frozen world
}

public enum Biome {
    Ocean, Coast, Desert, Savannah, Plains,
    Temperate,  // Was Forest - now covers all temperate-zone biomes (merged Grassland + Forest)
    Tropical,   // Was Jungle - now covers all tropical-zone biomes
    Glacier, Tundra,
    // 9 intentionally skipped (was Grassland, now merged into Temperate)
    Swamp, Seas,
    River,
    Lake,       // Inland freshwater body
    Lava,       // Inland lava basin used by demonic worlds
    MoonDunes,
    Volcanic,   // Volcanic terrain
    Steamlands, // Steam vents terrain
    Ashlands,   // Charred/ashen terrain
    Scorched,   // Extremely hot and dry
    Hellscape,  // Demonic hostile terrain
    Arctic,     // Coldest polar biome
    IcicleField,// Ice World exclusive biome

    // Real Solar System Planet Biomes
    MartianRegolith, MartianPolarIce, MartianDunes,
    VenusLava, VenusianPlains,
    MercuryPlains, MercurianIce,
    JovianClouds,
    SaturnSurface,
    UranusSurface,
    NeptuneSurface,
    PlutoCryo,
    TitanLakes, TitanDunes, TitanIce,
    EuropaIce, EuropaRidges,

    // Underwater Floor Biomes (ocean tiles only — surface biome stays Ocean for gameplay)
    AbyssalPlains,  // Flat, featureless deep ocean floor
    Trench,         // Deep ocean floor far from land
    
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

    /// <summary>
    /// Determine the underwater floor biome for an Ocean tile based on distance from coast,
    /// temperature, and a per-tile noise value. Returns Biome.Ocean if no special underwater
    /// biome qualifies (i.e. standard ocean floor).
    /// NOTE: Trenches are no longer assigned here — they are stamped as elongated paths
    /// by PlanetGenerator.StampTrenches. This method only handles AbyssalPlains.
    /// </summary>
    /// <param name="distanceFromCoast">BFS tile distance from nearest coast/land. 0 = coast tile itself.</param>
    /// <param name="temperature">Tile temperature (0-1 normalized).</param>
    /// <param name="noise">Deterministic per-tile noise value (0-1) used for probability checks.</param>
    /// <param name="abyssalMinDistance">Min tile distance from coast for AbyssalPlains eligibility.</param>
    /// <param name="abyssalMaxDistance">Max tile distance from coast for AbyssalPlains eligibility.</param>
    /// <param name="abyssalChance">Probability threshold for AbyssalPlains (noise must be below this).</param>
    public static Biome GetUnderwaterBiome(
        int distanceFromCoast, float temperature, float noise,
        int abyssalMinDistance = 3, int abyssalMaxDistance = 8, float abyssalChance = 0.35f)
    {
        // AbyssalPlains: mid-range ocean floor, flat and featureless
        if (distanceFromCoast >= abyssalMinDistance && distanceFromCoast <= abyssalMaxDistance
            && noise < abyssalChance)
        {
            return Biome.AbyssalPlains;
        }

        // Standard ocean floor (Trenches are stamped separately by PlanetGenerator)
        return Biome.Ocean;
    }

    public static Biome GetBiome(bool isLand, float temperature, float moisture,
        MapType mapType = MapType.Standard,
        PlanetType planetType = PlanetType.Earth,
        float northSouth = 0f, float eastWest = 0f)
    {
        if (!isLand) {
            // On IceWorld maps, glaciers extend much further from the poles (higher temp threshold)
            if (mapType == MapType.IceWorld && temperature <= 0.35f) return Biome.Glacier;
            if (temperature <= 0.035f) return Biome.Glacier;
            return Biome.Ocean;
        }
        
        // === EXPLICIT PLANET-SPECIFIC BIOME RULES - NO FALLBACKS ===
        
        // MARS - Complete temperature/moisture coverage
        if (planetType == PlanetType.Mars) {
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
        if (planetType == PlanetType.Venus) {
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
        if (planetType == PlanetType.Mercury) {
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
        if (planetType == PlanetType.Jupiter) {
            // North/south is passed in normalized range [-1,1]. Use absolute value to detect polar caps.
            float absLat = Mathf.Abs(northSouth);
            // Consider 70+ degrees as polar region (0.78 in normalized [-1,1])
            if (absLat >= 0.78f) return Biome.JovianClouds; // Polar storms
            return Biome.JovianClouds; // Elsewhere
        }
        
        // SATURN - Complete temperature/moisture coverage
        if (planetType == PlanetType.Saturn) {
            return Biome.SaturnSurface; // Default Saturn
        }
        
        // URANUS - Complete temperature/moisture coverage
        if (planetType == PlanetType.Uranus) {
            return Biome.UranusSurface; // Default Uranus
        }
        
        // NEPTUNE - Complete temperature/moisture coverage
        if (planetType == PlanetType.Neptune) {
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
        if (planetType == PlanetType.Pluto) {
            return Biome.PlutoCryo; // Default Pluto
        }
        
        // TITAN - Complete temperature/moisture coverage
        if (planetType == PlanetType.Titan) {
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
        if (planetType == PlanetType.Europa) {
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
        
        // IO removed: no Io-specific biomes
        
        // LUNA - Just Moon Dunes for now
        if (planetType == PlanetType.Luna) {
            return Biome.MoonDunes; // Default moon
        }

        // === EARTH-ONLY SPECIAL MAP TYPES ===
        
        // ICE WORLD: Exclusive biomes — thresholds pushed far toward the equator
        if (mapType == MapType.IceWorld)
        {
            if (temperature < 0.45f)
                return Biome.IcicleField; // was 0.25f — covers most of the map
            if (temperature < 0.62f && moisture > 0.15f)
                return Biome.Arctic; // was 0.40f — mid-latitudes
            // fallback to normal cold/frozen logic below
        }

        // DEMONIC WORLD: Coherent, characteristic-based logic
        if (mapType == MapType.Demonic && temperature > 0.7f) {
            if (temperature > 0.85f) {
                // Brimstone removed: fallback to Hellscape for extremely hot regions
                return Biome.Hellscape; // Extremely hot region
            }
            if (temperature > 0.7f) {
                if (moisture < 0.2f) return Biome.Scorched; // Very hot, very dry
                if (moisture < 0.65f) return Biome.Ashlands; // Very hot, dry
                return Biome.Steamlands; // Very hot, wet
            }
        }

        // For extremely high temperatures in infernal maps
        if (mapType == MapType.Infernal && temperature > 0.35f) {
            if (moisture > 0.95f) return Biome.Steamlands;
            if (moisture > 0.85f) return Biome.Ashlands; // Hot + Very Wet = Steamlands vents
            return Biome.Volcanic;                          // Very hot = Volcanic terrain
        }

        // === STANDARD EARTH BIOME LOGIC ===
        // Temperature bands: Arctic <0.15, Cold 0.15-0.35, Temperate 0.35-0.55, Warm 0.55-0.75, Hot >0.75
        
        // Hot climates (>0.91)
        if (temperature > 0.91f) {
            if (moisture < 0.38f) return Biome.Desert;
            if (moisture < 0.50f) return Biome.Savannah;
            if (moisture < 0.80f) return Biome.Tropical;
            return Biome.Swamp;
        }

        // Temperate climates (0.45-0.82)
        if (temperature > 0.45f) {
            if (moisture < 0.22f) return Biome.Plains;
            if (moisture < 0.819) return Biome.Temperate;
            return Biome.Swamp;
        }

        // Cold climates (0.23-0.50)
        if (temperature > 0.23f) {
            return Biome.Tundra;
        }

       
        if (temperature <= 0.23f) {
            return Biome.Arctic;
        }

        return Biome.Plains;
    }

    /// <summary>
    /// Validate biome assignment - log if inappropriate biomes are assigned to specific planets
    /// </summary>
    public static Biome ValidateAndLogBiome(Biome assignedBiome, PlanetType planetType)
    {
        if (assignedBiome == Biome.Glacier || assignedBiome == Biome.Tundra || assignedBiome == Biome.Arctic)
        {
            if (planetType == PlanetType.Venus || planetType == PlanetType.Mercury ||
                planetType == PlanetType.Saturn ||
                planetType == PlanetType.Luna)
            {
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
        Biome.Temperate => new YieldValues { food = 2, prod = 1, gold = 0, sci = 0, cult = 1 },
        Biome.Tropical => new YieldValues { food = 2, prod = 0, gold = 0, sci = 2, cult = 1 },
        Biome.Glacier => new YieldValues { food = 0, prod = 0, gold = 1, sci = 2, cult = 1 },
        Biome.Tundra => new YieldValues { food = 1, prod = 1, gold = 0, sci = 1, cult = 1 },
        Biome.Swamp => new YieldValues { food = 2, prod = 0, gold = 0, sci = 1, cult = 2 },
        Biome.River => new YieldValues { food = 1, prod = 0, gold = 1, sci = 1, cult = 1 },
        Biome.Lake => new YieldValues { food = 3, prod = 0, gold = 1, sci = 0, cult = 2 },
        Biome.Lava => new YieldValues { food = 0, prod = 5, gold = 0, sci = 2, cult = 0 },
        Biome.MoonDunes => new YieldValues { food = 0, prod = 1, gold = 0, sci = 1, cult = 0 },
        Biome.Volcanic => new YieldValues { food = 0, prod = 3, gold = 2, sci = 0, cult = 0 },
        Biome.Steamlands => new YieldValues { food = 0, prod = 2, gold = 3, sci = 0, cult = 0 },
        Biome.Ashlands => new YieldValues { food = 1, prod = 2, gold = 0, sci = 1, cult = 1 },
        Biome.Scorched => new YieldValues { food = 0, prod = 1, gold = 2, sci = 2, cult = 0 },
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

        // Underwater Floor Biomes
        Biome.AbyssalPlains => new YieldValues { food = 0, prod = 1, gold = 2, sci = 3, cult = 0 },
        Biome.Trench => new YieldValues { food = 0, prod = 2, gold = 3, sci = 3, cult = 0 },

        _ => new YieldValues { food = 1, prod = 1, gold = 1, sci = 1, cult = 1 }
    };

    // Returns only temperate-allowed biomes regardless of temperature extremes
    public static Biome GetTemperateBiome(float moisture)
    {
        if (moisture > 0.8f) return Biome.Swamp;
        if (moisture > 0.40f) return Biome.Temperate;
        return Biome.Plains;
    }
    
    /// <summary>
    /// Returns the defensive bonus for a given biome.
    /// </summary>
    public static int GetDefenseBonus(Biome biome) => biome switch {
        Biome.Temperate => 1,
        Biome.Tropical => 2,
        Biome.Volcanic => 4,
        Biome.Steamlands => 2,
        Biome.Ashlands => 1,
        Biome.Scorched => 0,
        Biome.Hellscape => 0,
        Biome.Arctic => 0,
        Biome.IcicleField => 1,

        // Planet-specific
        Biome.MartianRegolith => 0,
        Biome.MartianPolarIce => 1,
        Biome.MartianDunes => 0,

        Biome.VenusLava => 0,
        Biome.VenusianPlains => 0,

        Biome.Lava => 0,
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

        // Underwater Floor Biomes
        Biome.AbyssalPlains => 0,
        Biome.Trench => 0,

        _ => 0
    };
    
    /// <summary>
    /// Returns the movement cost for a given biome.
    /// </summary>
    public static int GetMovementCost(Biome biome) => biome switch {
        Biome.Plains => 1,
        Biome.Desert => 1,
        Biome.Tundra => 1,
        Biome.Savannah => 1,
        Biome.Coast => 1,

        Biome.Temperate => 1,
        Biome.Tropical => 2,
        Biome.Swamp => 3,

        Biome.Ocean => 1,
        Biome.Seas => 1,
        Biome.Lake => 2,
        Biome.Lava => 2,
        Biome.River => 3,

        Biome.Volcanic => 3,
        Biome.Steamlands => 2,
        Biome.Ashlands => 2,
        Biome.Scorched => 3,
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

        // Underwater Floor Biomes
        Biome.AbyssalPlains => 2,
        Biome.Trench => 3,

        _ => 1
    };

    public static bool IsLavaBiome(Biome biome) => biome == Biome.Lava;

    public static bool CanUnitTraverseLava(UnityEngine.MonoBehaviour unit)
    {
        if (unit is CombatUnit combatUnit)
        {
            if (combatUnit.data == null)
                return false;

            if (combatUnit.data.immuneToLava)
                return true;

            if (combatUnit.data.unitType == CombatCategory.LavaSwimmer)
                return true;

            if (combatUnit.data is DemonUnitData demonData)
                return demonData.canCrossLava;

            return false;
        }

        if (unit is WorkerUnit workerUnit)
            return workerUnit.data != null && workerUnit.data.immuneToLava;

        return false;
    }
    
    
    /// <summary>
    /// Returns the effective movement cost for a specific tile, taking into account
    /// improvements on the tile (e.g., roads that provide movement bonuses).
    /// Unit parameter is optional for future expansion (unit-specific effects).
    /// </summary>
    public static int GetMovementCost(HexTileData tile, UnityEngine.MonoBehaviour unit = null)
    {
        if (tile == null) return 99;
        int baseCost = GetMovementCost(tile.biome);
        bool canTraverseLava = unit != null && tile.biome == Biome.Lava && CanUnitTraverseLava(unit);

        if (unit != null)
        {
            if (unit is WorkerUnit)
            {
                if (!tile.isLand && !canTraverseLava) return 99;
            }
            else if (unit is CombatUnit combatUnit)
            {
                if (combatUnit.currentLayer != TileLayer.Orbit && !tile.isLand)
                {
                    if (!canTraverseLava)
                    {
                        switch (combatUnit.data != null ? combatUnit.data.unitType : CombatCategory.Spearman)
                        {
                            case CombatCategory.Ship:
                            case CombatCategory.Boat:
                            case CombatCategory.Submarine:
                            case CombatCategory.SeaCrawler:
                                break;
                            default:
                                return 99;
                        }
                    }
                }
            }
            else if (unit is BaseUnit baseUnit)
            {
                if (baseUnit.currentLayer != TileLayer.Orbit && !tile.isLand && !canTraverseLava)
                    return 99;
            }
        }

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
            Biome.Lava => true,
            Biome.Volcanic => true,
            Biome.Steamlands => true,
            Biome.Ashlands => true,
            Biome.Scorched => true,
            Biome.Hellscape => true,
            Biome.Arctic => true,
            Biome.IcicleField => true,
            Biome.Desert => true,

            // Planet-specific damaging biomes
            Biome.VenusLava => true,
            Biome.MercuryPlains => true,
            Biome.MercurianIce => true,
            Biome.UranusSurface => true,
            Biome.NeptuneSurface => true,
            Biome.PlutoCryo => true,

            // Underwater Floor Biomes
            Biome.Trench => true,  // crushing deep-sea pressure

            _ => false
        };
    }
    
    /// <summary>
    /// Returns the damage percentage for a biome (0-1 value)
    /// </summary>
    public static float GetBiomeDamage(Biome biome)
    {
        return biome switch {
            Biome.Lava => 0.50f,
            Biome.Volcanic => 0.15f,
            Biome.Steamlands => 0.10f,
            Biome.Ashlands => 0.05f,
            Biome.Scorched => 0.20f,
            Biome.Hellscape => 0.30f,
            Biome.Arctic => 0.05f,
            Biome.IcicleField => 0.15f,
            Biome.Desert => 0.10f,

            // Planet-specific values
            Biome.VenusLava => 0.50f,
            Biome.MercuryPlains => 0.20f,
            Biome.MercurianIce => 0.10f,
            Biome.UranusSurface => 0.25f,
            Biome.NeptuneSurface => 0.15f,
            Biome.PlutoCryo => 0.20f,

            // Underwater Floor Biomes
            Biome.Trench => 0.10f,

            _ => 0f
        };
    }

    // ─────────── ORBIT LAYER HELPERS ───────────

    /// <summary>
    /// Movement cost for a unit moving tile-to-tile while in orbit.
    /// Orbit has no terrain friction — all tiles cost the same.
    /// Individual unit data may override via CombatUnitData.orbitMovementCost.
    /// </summary>
    public const int DefaultOrbitMovementCost = 1;

    /// <summary>
    /// Defense bonus for units in orbit. No terrain cover in space.
    /// </summary>
    public const int OrbitDefenseBonus = 0;
}
