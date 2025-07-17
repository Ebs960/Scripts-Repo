public static class GameSetupData
{
    // Civilization Settings
    public static CivData selectedPlayerCivilizationData;
    public static LeaderData selectedLeaderData;
    public static int numberOfCivilizations;
    public static int numberOfCityStates;
    public static int numberOfTribes;
    
    // Basic Map Settings
    public static int moonSize;
    public static int animalPrevalence;
    public static GameManager.MapSize mapSize = GameManager.MapSize.Standard;
    public static GameManager.MapSize moonMapSize = GameManager.MapSize.Standard; // Moon map size
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
    
    // Continent Settings
    public static int numberOfContinents;
    
    // Island Settings (new)
    public static int numberOfIslands = 0;
    public static bool generateIslands = false;
    
    // River Settings
    public static bool enableRivers;
    public static int riverCount;
    
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
    
    // New fields
    public static float maxContinentWidthDegrees = 0f;
    public static float maxContinentHeightDegrees = 0f;
    
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
        moonMapSize = GameManager.MapSize.Standard; // Moon uses same size as planet by default
        moonSize = 3;
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
        
        // Set default continent and island settings (Standard preset)
        numberOfContinents = 4;
        numberOfIslands = 8;
        generateIslands = true;
        
        // Set default river settings
        enableRivers = true;
        riverCount = 10;
        
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
    }
} 