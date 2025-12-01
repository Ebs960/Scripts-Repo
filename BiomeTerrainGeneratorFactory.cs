using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Factory for creating biome-specific terrain generators
/// </summary>
public static class BiomeTerrainGeneratorFactory
{
    private static Dictionary<Biome, IBiomeTerrainGenerator> generatorCache = new Dictionary<Biome, IBiomeTerrainGenerator>();
    
    /// <summary>
    /// Get or create a terrain generator for a specific biome
    /// </summary>
    public static IBiomeTerrainGenerator GetGenerator(Biome biome)
    {
        // Check cache first
        if (generatorCache.TryGetValue(biome, out IBiomeTerrainGenerator cached))
        {
            return cached;
        }
        
        // Create new generator based on biome
        IBiomeTerrainGenerator generator = CreateGenerator(biome);
        
        // Cache it
        if (generator != null)
        {
            generatorCache[biome] = generator;
        }
        
        return generator;
    }
    
    /// <summary>
    /// Create a MoonTerrainGenerator configured for a specific body type
    /// </summary>
    private static MoonTerrainGenerator CreateMoonGenerator(MoonBodyType bodyType)
    {
        MoonTerrainGenerator generator = new MoonTerrainGenerator();
        generator.ConfigureForBody(bodyType);
        return generator;
    }
    
    private static IBiomeTerrainGenerator CreateGenerator(Biome biome)
    {
        return biome switch
        {
            // === FLAT/PLAINS BIOMES ===
            Biome.Plains => new PlainsTerrainGenerator(),
            Biome.Grassland => new PlainsTerrainGenerator(),
            Biome.Savannah => new PlainsTerrainGenerator(),
            Biome.Steppe => new PlainsTerrainGenerator(),
            
            // === DESERT BIOMES ===
            Biome.Desert => new DesertTerrainGenerator(),
            Biome.Scorched => new DesertTerrainGenerator(),
            Biome.Ashlands => new DesertTerrainGenerator(),
            
            // === MOUNTAIN/ROUGH BIOMES ===
            Biome.Mountain => new MountainTerrainGenerator(),
            Biome.Volcanic => new MountainTerrainGenerator(),
            Biome.Hellscape => new MountainTerrainGenerator(),
            Biome.Brimstone => new MountainTerrainGenerator(),
            
            // === FOREST/JUNGLE BIOMES ===
            Biome.Forest => new ForestTerrainGenerator(),
            Biome.Jungle => new ForestTerrainGenerator(),
            Biome.Rainforest => new ForestTerrainGenerator(),
            Biome.Taiga => new ForestTerrainGenerator(),
            Biome.PineForest => new ForestTerrainGenerator(),
            Biome.CharredForest => new ForestTerrainGenerator(), // Charred but still hilly
            
            // === SWAMP/MARSH BIOMES ===
            Biome.Swamp => new SwampTerrainGenerator(),
            Biome.Marsh => new SwampTerrainGenerator(),
            Biome.Floodlands => new SwampTerrainGenerator(),
            
            // === ICE/SNOW BIOMES ===
            Biome.Snow => new IceTerrainGenerator(),
            Biome.Glacier => new IceTerrainGenerator(),
            Biome.Tundra => new IceTerrainGenerator(),
            Biome.Frozen => new IceTerrainGenerator(),
            Biome.Arctic => new IceTerrainGenerator(),
            Biome.IcicleField => new IceTerrainGenerator(),
            Biome.CryoForest => new IceTerrainGenerator(),
            
            // === OCEAN/WATER BIOMES ===
            Biome.Ocean => new OceanTerrainGenerator(),
            Biome.Coast => new OceanTerrainGenerator(),
            Biome.Seas => new OceanTerrainGenerator(),
            Biome.River => new OceanTerrainGenerator(),
            
            // === PLANET-SPECIFIC BIOMES ===
            // Mars (desert-like with canyons)
            Biome.MartianRegolith => new DesertTerrainGenerator(),
            Biome.MartianDunes => new DesertTerrainGenerator(),
            Biome.MartianCanyon => new MountainTerrainGenerator(),
            Biome.MartianPolarIce => new IceTerrainGenerator(),
            
            // Venus (volcanic/desert)
            Biome.VenusLava => new MountainTerrainGenerator(),
            Biome.VenusianPlains => new DesertTerrainGenerator(),
            Biome.VenusHighlands => new MountainTerrainGenerator(),
            
            // === MERCURY - Cratered Moon-like terrain ===
            Biome.MercuryCraters => CreateMoonGenerator(MoonBodyType.Mercury),
            Biome.MercuryBasalt => CreateMoonGenerator(MoonBodyType.Mercury),
            Biome.MercuryScarp => CreateMoonGenerator(MoonBodyType.Mercury),
            Biome.MercurianIce => CreateMoonGenerator(MoonBodyType.Mercury),
            
            // Gas Giants (flat cloud layers)
            Biome.JovianClouds => new OceanTerrainGenerator(),
            Biome.JovianStorm => new OceanTerrainGenerator(),
            Biome.SaturnRings => new OceanTerrainGenerator(),
            Biome.SaturnSurface => new OceanTerrainGenerator(),
            Biome.UranusIce => new IceTerrainGenerator(),
            Biome.UranusSurface => new OceanTerrainGenerator(),
            Biome.NeptuneWinds => new OceanTerrainGenerator(),
            Biome.NeptuneIce => new IceTerrainGenerator(),
            Biome.NeptuneSurface => new OceanTerrainGenerator(),
            
            // === PLUTO - Cratered icy dwarf planet ===
            Biome.PlutoCryo => CreateMoonGenerator(MoonBodyType.Pluto),
            Biome.PlutoTholins => CreateMoonGenerator(MoonBodyType.Pluto),
            Biome.PlutoMountains => CreateMoonGenerator(MoonBodyType.Pluto),
            
            // === TITAN - Moon with atmosphere and lakes ===
            Biome.TitanLakes => new OceanTerrainGenerator(), // Keep ocean for liquid methane lakes
            Biome.TitanDunes => CreateMoonGenerator(MoonBodyType.Titan),
            Biome.TitanIce => CreateMoonGenerator(MoonBodyType.Titan),
            
            // === EUROPA - Icy moon with ridges ===
            Biome.EuropaIce => CreateMoonGenerator(MoonBodyType.Europa),
            Biome.EuropaRidges => CreateMoonGenerator(MoonBodyType.Europa),
            
            // === IO - Volcanic moon ===
            Biome.IoVolcanic => CreateMoonGenerator(MoonBodyType.Io),
            Biome.IoSulfur => CreateMoonGenerator(MoonBodyType.Io),
            
            // === EARTH'S MOON - Classic lunar terrain ===
            Biome.MoonDunes => CreateMoonGenerator(MoonBodyType.EarthMoon),
            Biome.MoonCaves => CreateMoonGenerator(MoonBodyType.EarthMoon),
            
            // Special
            Biome.Steam => new SwampTerrainGenerator(), // Steam vents in flat areas
            
            // Default fallback
            _ => new PlainsTerrainGenerator()
        };
    }
    
    /// <summary>
    /// Clear the generator cache (useful for testing or memory management)
    /// </summary>
    public static void ClearCache()
    {
        generatorCache.Clear();
    }
}

