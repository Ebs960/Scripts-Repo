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
    public static int minLakeSize = 3;
    public static int maxLakeSize = 12;
    public static float lakeElevationThreshold = 0.25f;
    public static bool connectRiversToLakes = true;
    
    // Climate Thresholds
    public static float polarLatitudeThreshold;
    public static float subPolarLatitudeThreshold;
    public static float equatorLatitudeThreshold;
    
    // Moisture Settings
    public static float moistureBias;
    public static float temperatureBias = 0f;
    
    // Land Generation
    public static float landThreshold;
    public static float seedPositionVariance;
    
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
    // Advanced continent tuning (exposed so menus/presets can set them)
    public static float continentDomainWarp = 0.25f;
    public static float continentMacroAmplitude = 0.35f;
    public static float coastlineWarpAmplitude = 0.12f;
    public static float coastlineFineWarp = 0.08f;
    public static float voronoiContinentInfluence = 0.0f;
    public static float voronoiElevationInfluence = 0.12f;

    // Island tuning
    public static float islandNoiseFrequency = 1.8f;
    public static float islandInnerRadius = 0.25f;
    public static float islandOuterRadius = 0.9f;

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
        numberOfIslands = 8;
        generateIslands = true;
        
        // Set default river settings
        enableRivers = true;
        riverCount = 10;
        
        // Set default lake settings (influenced by moisture in ApplyMoisturePreset)
        enableLakes = true;
        numberOfLakes = 8;
        minLakeSize = 3;
        maxLakeSize = 12;
        lakeElevationThreshold = 0.25f;
        connectRiversToLakes = true;
        
        // Set default climate thresholds (temperate)
        polarLatitudeThreshold = 0.8f;
        subPolarLatitudeThreshold = 0.6f;
        equatorLatitudeThreshold = 0.2f;
        
        // Set default moisture settings
        moistureBias = 0f;
        temperatureBias = 0f;
        
        // Set default land generation settings
        landThreshold = 0.45f; // More reasonable default for balanced land/water
        seedPositionVariance = 0.1f;

        // Advanced continent defaults (match PlanetGenerator defaults)
        continentDomainWarp = 0.25f;
        continentMacroAmplitude = 0.35f;
        coastlineWarpAmplitude = 0.12f;
        coastlineFineWarp = 0.08f;
        voronoiContinentInfluence = 0.0f;
        voronoiElevationInfluence = 0.12f;

        // Tile-based continent sizing defaults
        minContinentWidthTilesSmall = 80; maxContinentWidthTilesSmall = 200;
        minContinentHeightTilesSmall = 40; maxContinentHeightTilesSmall = 100;

        minContinentWidthTilesStandard = 200; maxContinentWidthTilesStandard = 400;
        minContinentHeightTilesStandard = 100; maxContinentHeightTilesStandard = 200;

        minContinentWidthTilesLarge = 400; maxContinentWidthTilesLarge = 800;
        minContinentHeightTilesLarge = 200; maxContinentHeightTilesLarge = 400;

        // Tile-based island sizing defaults (smaller than continents)
        minIslandWidthTilesSmall = 8; maxIslandWidthTilesSmall = 24;
        minIslandHeightTilesSmall = 4; maxIslandHeightTilesSmall = 12;

        minIslandWidthTilesStandard = 20; maxIslandWidthTilesStandard = 60;
        minIslandHeightTilesStandard = 10; maxIslandHeightTilesStandard = 30;

        minIslandWidthTilesLarge = 40; maxIslandWidthTilesLarge = 120;
        minIslandHeightTilesLarge = 20; maxIslandHeightTilesLarge = 60;

        // Island defaults (noise/falloff)
        islandNoiseFrequency = 1.8f;
        islandInnerRadius = 0.25f;
        islandOuterRadius = 0.9f;

        // River defaults
        minRiverLength = 8;
        minRiversPerContinent = 1;
        maxRiversPerContinent = 2;
    }
} 