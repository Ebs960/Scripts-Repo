using UnityEngine;

public static class GameSetupData
{
    public enum SystemPreset { Random, RealSolarSystem }
    public static SystemPreset systemPreset = SystemPreset.Random; // set from Main Menu

    // Civilization Settings
    public static CivData selectedPlayerCivilizationData;
    public static LeaderData selectedLeaderData;
    public static int numberOfCivilizations;
    public static int numberOfCityStates;
    public static int numberOfTribes;
    
    // Basic Map Settings
    public static int animalPrevalence;
    public static GameManager.MapSize mapSize = GameManager.MapSize.Standard;
    public static bool generateMoon = true;
    
    // Map Generation Settings
    public static int selectedClimatePreset;     // 0=Frozen, 1=Cold, 2=Temperate, etc.
    public static int selectedMoisturePreset;    // 0=Desert, 1=Arid, 2=Standard, etc.
    public static int selectedLandPreset;        // 0=Archipelago, 1=Islands, 2=Standard, etc.
    public static int selectedTerrainPreset;     // 0=Smooth, 1=Rolling, 2=Rocky, etc.
    
    // Special World Types
    public static string mapTypeName;            // Full name of the map type (e.g., "Infernal Highlands")
    public static bool isInfernalWorld;          // For Steam/Volcanic biomes
    public static bool isDemonicWorld;           // For Hellscape/Brimstone biomes
    public static bool isScorchedWorld;          // For Ashlands/CharredForest biomes
    public static bool isRainforestWorld;        // For enhanced rainforest generation
    public static bool isMonsoonWorld;           // For Floodlands biome
    public static bool isIceWorld;               // For Ice World biomes
    
    // Continent Settings
    public static int numberOfContinents;
    
    // Island Settings (new)
    public static int numberOfIslands = 0;
    public static bool generateIslands = false;
    
    // River Settings
    public static bool enableRivers;
    public static int riverCount;
    
    // Lake Settings (influenced by moisture preset)
    public static bool enableLakes = true;
    public static int numberOfLakes = 8;
    // Lake stamping (tile radius) - authoritative representation
    // radius in tiles
    
    // Climate Thresholds
    public static float polarLatitudeThreshold;
    public static float subPolarLatitudeThreshold;
    public static float equatorLatitudeThreshold;
    
    // Moisture Settings
    public static float moistureBias;
    public static float temperatureBias = 0f;
    
    // Tile-based continent size ranges for Small/Standard/Large
    public static int minContinentWidthTilesSmall;
    public static int maxContinentWidthTilesSmall;
    public static int minContinentHeightTilesSmall;
    public static int maxContinentHeightTilesSmall;

    public static int minContinentWidthTilesStandard;
    public static int maxContinentWidthTilesStandard;
    public static int minContinentHeightTilesStandard;
    public static int maxContinentHeightTilesStandard;

    public static int minContinentWidthTilesLarge;
    public static int maxContinentWidthTilesLarge;
    public static int minContinentHeightTilesLarge;
    public static int maxContinentHeightTilesLarge;
    // Tile-based island size ranges for Small/Standard/Large
    public static int minIslandWidthTilesSmall;
    public static int maxIslandWidthTilesSmall;
    public static int minIslandHeightTilesSmall;
    public static int maxIslandHeightTilesSmall;

    public static int minIslandWidthTilesStandard;
    public static int maxIslandWidthTilesStandard;
    public static int minIslandHeightTilesStandard;
    public static int maxIslandHeightTilesStandard;

    public static int minIslandWidthTilesLarge;
    public static int maxIslandWidthTilesLarge;
    public static int minIslandHeightTilesLarge;
    public static int maxIslandHeightTilesLarge;

    // Stamp-based continent sizing (resolved for the active map size)
    public static int continentMinWidthTiles;
    public static int continentMaxWidthTiles;
    public static int continentMinHeightTiles;
    public static int continentMaxHeightTiles;
    public static int continentMinDistanceTiles;
    public static float continentConnectionChance = 0.1f;

    // Stamp-based island sizing (radius in tiles)
    public static int islandMinRadiusTiles;
    public static int islandMaxRadiusTiles;
    public static int islandMinDistanceFromContinents = 6;

    // Stamp-based lake sizing (radius in tiles)
    public static int lakeMinRadiusTiles;
    public static int lakeMaxRadiusTiles;
    public static int lakeMinDistanceFromCoast = 2;
    // River tuning
    public static int minRiverLength = 8;
    public static int minRiversPerContinent = 1;
    public static int maxRiversPerContinent = 2;
    
    /// <summary>
    /// Initialize GameSetupData with default values to prevent null/empty errors
    /// </summary>
    public static void InitializeDefaults()
    {
        // Civilization settings
        selectedPlayerCivilizationData = null;
        numberOfCivilizations = 4;
        numberOfCityStates = 2;
        numberOfTribes = 2;
        
        // Map settings
        mapSize = GameManager.MapSize.Standard;
        animalPrevalence = 3;
        generateMoon = true;
        
        // Set default generation settings
        selectedClimatePreset = 2; // Temperate
        selectedMoisturePreset = 2; // Standard
        selectedLandPreset = 2; // Standard
        selectedTerrainPreset = 2; // Rocky
        
        // Set default map type
        mapTypeName = "Temperate Plains";
        isInfernalWorld = false;
        isDemonicWorld = false;
        isScorchedWorld = false;
        isRainforestWorld = false;
        isMonsoonWorld = false;
        isIceWorld = false;
        
        // Set default continent and island settings (Standard preset)
        numberOfContinents = 4;
        numberOfIslands = 2;
        generateIslands = true;
        
        // Set default river settings
        enableRivers = true;
        riverCount = 10;
        
        // Set default lake settings (radius-based)
        enableLakes = true;
        numberOfLakes = 8;
        
        // Set default climate thresholds (temperate)
        polarLatitudeThreshold = 0.8f;
        subPolarLatitudeThreshold = 0.6f;
        equatorLatitudeThreshold = 0.2f;
        
        // Set default moisture settings
        moistureBias = 0f;
        temperatureBias = 0f;
        
        // Tile-based continent sizing defaults
        minContinentWidthTilesSmall = 80; maxContinentWidthTilesSmall = 200;
        minContinentHeightTilesSmall = 40; maxContinentHeightTilesSmall = 100;

        minContinentWidthTilesStandard = 27; maxContinentWidthTilesStandard = 39;
        minContinentHeightTilesStandard = 20; maxContinentHeightTilesStandard = 35;

        minContinentWidthTilesLarge = 400; maxContinentWidthTilesLarge = 800;
        minContinentHeightTilesLarge = 200; maxContinentHeightTilesLarge = 400;

        // Tile-based island sizing defaults (smaller than continents)
        minIslandWidthTilesSmall = 8; maxIslandWidthTilesSmall = 24;
        minIslandHeightTilesSmall = 4; maxIslandHeightTilesSmall = 12;

        minIslandWidthTilesStandard = 7; maxIslandWidthTilesStandard = 9;
        minIslandHeightTilesStandard = 5; maxIslandHeightTilesStandard = 9;

        minIslandWidthTilesLarge = 40; maxIslandWidthTilesLarge = 120;
        minIslandHeightTilesLarge = 20; maxIslandHeightTilesLarge = 60;

        // River defaults
        minRiverLength = 8;
        minRiversPerContinent = 1;
        maxRiversPerContinent = 2;

        // Stamp-based defaults (Standard map size)
        continentMinWidthTiles = minContinentWidthTilesStandard;
        continentMaxWidthTiles = maxContinentWidthTilesStandard;
        continentMinHeightTiles = minContinentHeightTilesStandard;
        continentMaxHeightTiles = maxContinentHeightTilesStandard;
        continentMinDistanceTiles = 8;
        continentConnectionChance = 0.1f;

        islandMinRadiusTiles = Mathf.Max(1, minIslandWidthTilesStandard / 2);
        islandMaxRadiusTiles = Mathf.Max(islandMinRadiusTiles, maxIslandWidthTilesStandard / 2);
        islandMinDistanceFromContinents = 6;

        lakeMinRadiusTiles = 3;
        lakeMaxRadiusTiles = 9;
        lakeMinDistanceFromCoast = 2;
    }
}
