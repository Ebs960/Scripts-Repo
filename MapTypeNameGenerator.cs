using System.Collections.Generic;
using UnityEngine;

public static class MapTypeNameGenerator
{
    private static readonly string[][] baseNames = {
        new[] { "Polar", "Arctic", "Frozen", "Glacial", "Ice", "Frost", "Permafrost", "White" },
        new[] { "Northern", "Boreal", "Taiga", "Pine", "Snow", "Frost", "Evergreen", "Fir" },
        new[] { "Dry", "Grassy", "Temperate", "Lush", "Misty", "Emerald", "Verdant", "Meadow" },
        new[] { "Savanna", "Sunlit", "Tropical", "Fertile", "Rainforest", "Monsoon", "Verdure", "Canopy" },
        new[] { "Desert", "Arid", "Sweltering", "Oasis", "Tropical", "Steamlands", "Sahara", "Sandsea" },
        new[] { "Scorched", "Barren", "Burning", "Mirage", "Infernal", "Demonic", "Charred", "Ashen" }
    };

    private static readonly string[] oceanTerrain = { "Shards", "Atoll", "Isles", "Archipelago", "Seas", "Chain", "Cays", "Banks" };
    
    private static readonly string[] waterTerrain = { "Ponds", "Waters", "Lagoons", "Rivers", "Lakes", "Coves", "Bights", "Estuaries" };

    private static readonly string[][] elevationTerrain = {
        new[] { "Basin", "Plains", "Lowlands", "Valley", "Flats", "Coasts", "Meadow", "Fen" },
        new[] { "Hills", "Highlands", "Ridges", "Heights", "Uplands", "Cliffs", "Bluffs", "Escarpment" },
        new[] { "Peaks", "Range", "Mountains", "Summit", "Crags", "Alps", "Spire", "Highland" }
    };

    private static readonly string[][] elevationTerrainContinents = {
        new[] { "Tablelands", "Plains", "Low Plateaus", "Country", "Steppes", "Coastal Plain", "Heartland", "Lowlands" }, // Low elevation
        new[] { "Plateaus", "Uplands", "Escarpments", "Highlands", "Continental Rise", "Shield", "Buttes", "Badlands" }, // Hilly
        new[] { "Massif", "Peaks", "Continental Divide", "Great Range", "Crest", "Summits", "High Range", "Pinnacles" } // Mountainous
    };

    private static readonly string[] pangaeaTypes = { "Expanse", "Vastness", "Frontier", "Wilderness", "Dominion", "Heartland", "Realm", "Union" };

    private static readonly string[][] pangaeaMods = {
        new[] { "Great", "Endless", "Vast", "Boundless", "Sweeping", "Ancient", "Primal", "Primeval" },
        new[] { "Rolling", "Rugged", "Forested", "Windswept", "Untamed", "Wild", "Verdant", "Broad" },
        new[] { "Towering", "Majestic", "Colossal", "Mighty", "Stony", "Sheer", "Monolithic", "Skyborne" }
    };

    public static string GetMapTypeName(int climate, int moisture, int land, int elevation)
    {
        // Clamp indices to prevent out of range errors
        climate = Mathf.Clamp(climate, 0, baseNames.Length - 1);
        moisture = Mathf.Clamp(moisture, 0, waterTerrain.Length - 1);
        elevation = Mathf.Clamp(elevation, 0, elevationTerrain.Length - 1);

        if (land == 0) // Archipelago
            return $"{baseNames[climate][moisture]} {oceanTerrain[moisture]}";
        if (land == 1) // Islands
            return $"{baseNames[climate][moisture]} {waterTerrain[moisture]}";
        if (land == 2) // Standard/classic
            return $"{baseNames[climate][moisture]} {elevationTerrain[elevation][moisture]}";
        if (land == 3) // Continents
            return $"{baseNames[climate][moisture]} {elevationTerrainContinents[elevation][moisture]}";
        if (land == 4) // Pangaea
            return $"{pangaeaMods[elevation][moisture]} {baseNames[climate][moisture]} {pangaeaTypes[moisture]}";
        // Default to standard/classic if out of range
        return $"{baseNames[climate][moisture]} {elevationTerrain[elevation][moisture]}";
    }

    public static List<string> BuildAllNames()
    {
        var names = new List<string>();

        for (int climate = 0; climate < baseNames.Length; climate++)
            for (int moisture = 0; moisture < waterTerrain.Length; moisture++)
                for (int land = 0; land <= 4; land++)
                    for (int elev = 0; elev < elevationTerrain.Length; elev++)
                    {
                        try
                        {
                            string name = GetMapTypeName(climate, moisture, land, elev);
                            names.Add(name);
                        }
                        catch (System.Exception e)
                        {
                            UnityEngine.Debug.LogError($"Error generating name for climate={climate}, moisture={moisture}, land={land}, elev={elev}: {e.Message}");
                        }
                    }

        names.Sort();
        return names;
    }
} 