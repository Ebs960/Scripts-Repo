using System.Collections.Generic;
using UnityEngine;

public static class MapTypeNameGenerator
{
    private static readonly string[][] baseNames = {
        new[] { "Polar", "Arctic", "Frozen", "Glacial", "Ice", "Frost" },
        new[] { "Northern", "Boreal", "Taiga", "Tundra", "Pine", "Snow" },
        new[] { "Dry", "Grassy", "Temperate", "Lush", "Misty", "Emerald" },
        new[] { "Savanna", "Sunlit", "Tropical", "Fertile", "Rainforest", "Monsoon" },
        new[] { "Desert", "Arid", "Sweltering", "Oasis", "Jungle", "Steam" },
        new[] { "Scorched", "Barren", "Burning", "Mirage", "Infernal", "Demonic" }
    };

    private static readonly string[] oceanTerrain = { "Shards", "Atoll", "Isles", "Archipelago", "Seas", "Chain" };
    
    private static readonly string[] waterTerrain = { "Ponds", "Waters", "Lagoons", "Rivers", "Lakes", "Coves"};

    private static readonly string[][] elevationTerrain = {
        new[] { "Basin", "Plains", "Lowlands", "Valley", "Flats", "Coasts" },
        new[] { "Hills", "Highlands", "Ridges", "Heights", "Uplands", "Cliffs" },
        new[] { "Peaks", "Range", "Mountains", "Summit", "Crags", "Alps" }
    };

    private static readonly string[][] elevationTerrainContinents = {
        new[] { "Tablelands", "Plains", "Low Plateaus", "Country", "Steppes", "Coastal Plain" }, // Low elevation
        new[] { "Plateaus", "Uplands", "Escarpments", "Highlands", "Continental Rise", "Shield" }, // Hilly
        new[] { "Massif", "Peaks", "Continental Divide", "Great Range", "Crest", "Summits" } // Mountainous
    };

    private static readonly string[] pangaeaTypes = { "Expanse", "Vastness", "Frontier", "Wilderness", "Dominion", "Heartland" };

    private static readonly string[][] pangaeaMods = {
        new[] { "Great", "Endless", "Vast", "Boundless", "Sweeping", "Ancient" },
        new[] { "Rolling", "Rugged", "Forested", "Windswept", "Untamed", "Wild" },
        new[] { "Towering", "Majestic", "Colossal", "Mighty", "Stony", "Sheer" }
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