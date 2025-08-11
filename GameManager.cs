using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Central manager for game state and flow.
/// Handles game initialization, save/load, game settings, and provides
/// access to other core systems.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Generator Prefabs")]
    [Tooltip("PlanetGenerator prefab to instantiate - assign 'New Map Shit/Earth.prefab'")]
    public GameObject planetGeneratorPrefab;
    [Tooltip("Generic planet prefab for non-Earth planets (Mars, Venus, etc.)")]
    public GameObject genericPlanetPrefab;
    [Tooltip("MoonGenerator prefab to instantiate - assign 'New Map Shit/Moon.prefab'")]
    public GameObject moonGeneratorPrefab;

    [Header("Manager Prefabs")]
    [Tooltip("CivilizationManager prefab with pre-configured references")]
    public GameObject civilizationManagerPrefab;
    [Tooltip("ClimateManager prefab with pre-configured settings")]
    public GameObject climateManagerPrefab;
    [Tooltip("TurnManager prefab with pre-configured settings")]
    public GameObject turnManagerPrefab;
    [Tooltip("UnitSelectionManager prefab for handling unit selection and movement")]
    public GameObject unitSelectionManagerPrefab;
    [Tooltip("UnitMovementController prefab for handling unit pathfinding and movement")]
    public GameObject unitMovementControllerPrefab;
    [Tooltip("PolicyManager prefab for handling policies and governments")]
    public GameObject policyManagerPrefab;
    [Tooltip("DiplomacyManager prefab for handling diplomatic relations")]
    public GameObject diplomacyManagerPrefab;
    [Tooltip("ResourceManager prefab for handling resource management")]
    public GameObject resourceManagerPrefab;
    [Tooltip("ReligionManager prefab for handling religion systems")]
    public GameObject religionManagerPrefab;
    [Tooltip("AnimalManager prefab for spawning and controlling animals")]
    public GameObject animalManagerPrefab;

    [Header("Space System Prefabs")]
    [Tooltip("AncientRuinsManager prefab for handling ancient ruins discovery")]
    public GameObject ancientRuinsManagerPrefab;
    [Tooltip("SpaceRouteManager prefab for handling interplanetary unit travel")]
    public GameObject spaceRouteManagerPrefab;

    [Header("Game Settings")]
    public CivData selectedPlayerCivilizationData;
    public int numberOfCivilizations = 4;
    public int numberOfCityStates = 2;
    public int numberOfTribes = 2;

    // Animal prevalence: 0=dead, 1=sparse, 2=scarce, 3=normal, 4=lively, 5=bustling
    [Range(0, 5)]
    public int animalPrevalence = 3;

    public enum MapSize { Small, Standard, Large }   // 0,1,2
    [Header("Map Settings")]
    public MapSize mapSize = MapSize.Standard;
    public bool generateMoon = true;

    [Header("References")]
    public PlanetGenerator planetGenerator;
    public MoonGenerator moonGenerator;
    public CivilizationManager civilizationManager;
    public ClimateManager climateManager;
    public DiplomacyManager diplomacyManager;

    [Header("Multi-Planet System")]
    [Tooltip("Enable multi-planet generation and management")]
    public bool enableMultiPlanetSystem = false;
    [Tooltip("Maximum number of planets to generate")]
    public int maxPlanets = 8;
    [Tooltip("Generate real solar system instead of procedural planets")]
    public bool useRealSolarSystem = false;

    // Multi-planet storage
    private Dictionary<int, PlanetGenerator> planetGenerators = new Dictionary<int, PlanetGenerator>();
    private Dictionary<int, MoonGenerator> moonGenerators = new Dictionary<int, MoonGenerator>();
    private Dictionary<int, ClimateManager> planetClimateManagers = new Dictionary<int, ClimateManager>();
    private Dictionary<int, CivilizationManager> planetCivManagers = new Dictionary<int, CivilizationManager>();
    private Dictionary<int, PlanetData> planetData = new Dictionary<int, PlanetData>();
    private List<string> realBodies;
    private int totalPlanets;

    public int currentPlanetIndex = 0;
    public PlanetGenerator GetPlanetGenerator(int planetIndex) => planetGenerators.TryGetValue(planetIndex, out var gen) ? gen : null;
    public MoonGenerator GetMoonGenerator(int planetIndex) => moonGenerators.TryGetValue(planetIndex, out var moon) ? moon : null;
    public ClimateManager GetClimateManager(int planetIndex) => planetClimateManagers.TryGetValue(planetIndex, out var mgr) ? mgr : null;
    public Dictionary<int, PlanetData> GetPlanetData() => planetData;
    
    /// <summary>
    /// Get the currently active planet generator (multi-planet aware)
    /// </summary>
    public PlanetGenerator GetCurrentPlanetGenerator()
    {
        if (enableMultiPlanetSystem)
        {
            return planetGenerators.TryGetValue(currentPlanetIndex, out var generator) ? generator : null;
        }
        return planetGenerator;
    }
    
    /// <summary>
    /// Get the currently active climate manager (multi-planet aware)
    /// </summary>
    public ClimateManager GetCurrentClimateManager()
    {
        if (enableMultiPlanetSystem)
        {
            return planetClimateManagers.TryGetValue(currentPlanetIndex, out var climate) ? climate : null;
        }
        return climateManager;
    }
    
    /// <summary>
    /// Get the currently active moon generator (multi-planet aware)
    /// </summary>
    public MoonGenerator GetCurrentMoonGenerator()
    {
        if (enableMultiPlanetSystem)
        {
            return moonGenerators.TryGetValue(currentPlanetIndex, out var moon) ? moon : null;
        }
        return moonGenerator;
    }
    
    /// <summary>
    /// Set the current planet index and update references (multi-planet mode)
    /// </summary>
    public void SetCurrentPlanet(int planetIndex)
    {
        if (!enableMultiPlanetSystem)
        {
            Debug.LogWarning("[GameManager] SetCurrentPlanet called but multi-planet system is disabled");
            return;
        }
        
        if (!planetGenerators.ContainsKey(planetIndex))
        {
            Debug.LogWarning($"[GameManager] Planet {planetIndex} does not exist");
            return;
        }
        
        Debug.Log($"[GameManager] Switching current planet to {planetIndex}");
        currentPlanetIndex = planetIndex;
        
        // Update references in other systems that need to know about the current planet
        if (TileDataHelper.Instance != null)
        {
            TileDataHelper.Instance.UpdateReferences();
        }
    }

    [Header("Game State")]
    public bool gameInProgress = false;
    public bool gamePaused = false;
    public int currentTurn = 0;

    // Enums for multi-planet system
    public enum PlanetType
    {
        Terran,
        Desert,
        Ocean,
        Ice,
        Volcanic,
        Gas_Giant,
        Barren,
        Jungle,
        Tundra
    }

    public enum CelestialBodyType
    {
        Planet,
        Moon,
        Asteroid,
        Comet,
        Space_Station
    }

    // Data structure for multi-planet system
    [System.Serializable]
    public class PlanetData
    {
        public int planetIndex;
        public string planetName;
        public PlanetType planetType;
        public CelestialBodyType celestialBodyType;
        public MapSize planetSize;
        public bool isHomeWorld;
        public bool isExplored;
        public bool isColonized;
        public Vector3 worldPosition; // Position in space
        public float distanceFromHome; // Distance from home world
        public List<string> civilizationNames = new List<string>(); // Civs present on this planet
        public List<string> moonNames = new List<string>(); // Names of moons orbiting this planet
        
        // Additional properties for compatibility
        public float distanceFromStar; // Distance from star (for display purposes)
        public float orbitalPeriod; // Orbital period in days
        public float averageTemperature; // Average temperature in Celsius
        public string description; // Planet description
        public bool isGenerated; // Whether planet has been generated (same as isExplored for compatibility)
        
        // Atmosphere properties (determined by planet type)
        public bool hasAtmosphere; // Whether planet has atmosphere
        public string atmosphereComposition; // Atmosphere composition description
        
        // Civilization data (populated when civs actually settle the planet)
        public List<CivData> civilizations = new List<CivData>(); // Real civilizations that have settled here
        
        public PlanetData()
        {
            celestialBodyType = CelestialBodyType.Planet;
            isExplored = false;
            isColonized = false;
            distanceFromHome = 0f;
            distanceFromStar = 0f;
            orbitalPeriod = 0f;
            averageTemperature = 0f;
            description = "";
            isGenerated = false;
            hasAtmosphere = false; // Will be set based on planet type
            atmosphereComposition = ""; // Will be set based on planet type
        }
    }

    // Events
    public event Action OnGameStarted;
    public event Action<bool> OnGamePaused;
    public event Action OnGameEnded;

    // Manager references
    public TurnManager turnManager;

    [Header("UI Prefabs")]
    public GameObject playerUIPrefab;
    public GameObject planetaryCameraPrefab; // Assign 'New Map Shit/Camera Controller.prefab'
    public GameObject spaceLoadingPanelPrefab; // Assign space loading panel prefab
    
    [Header("Minimap Configuration")]
    [Tooltip("MinimapColorProvider ScriptableObject asset for minimap rendering")]
    public MinimapColorProvider minimapColorProvider;

    private GameObject instantiatedCameraGO; // Store reference to the instantiated camera
    private SpaceLoadingPanelController spaceLoadingPanel; // Reference to space loading panel

    //Tile grid and lookup ---
    [System.Serializable]
    public class HexTileData
    {
        public int tileIndex;
        public float latitude, longitude; // Center of tile (deg)
        public float u, v; // Equirectangular UV (0-1)
        public int biomeIndex;
        public float height; // 0-1, for heightmap alpha
        public int food, production, gold, science, culture;
        public string name;
        public Vector3 centerUnitVector; // For fast 3D lookup
    }

    // --- References to high-res planet textures and grid ---

    public SphericalHexGrid planetGrid;

    public List<HexTileData> hexTiles = new List<HexTileData>();


    // --- Fast nearest tile lookup (by 3D unit vector) ---
    public HexTileData GetNearestHexTile(Vector3 worldPoint, Transform planetTransform)
    {
        Vector3 local = planetTransform.InverseTransformPoint(worldPoint).normalized;
        float minDist = float.MaxValue;
        HexTileData nearest = null;
        for (int i = 0; i < hexTiles.Count; i++)
        {
            float dist = (hexTiles[i].centerUnitVector - local).sqrMagnitude;
            if (dist < minDist)
            {
                minDist = dist;
                nearest = hexTiles[i];
            }
        }
        return nearest;
    }

    // --- Utility: Convert lat/lon to unit vector ---
    public static Vector3 LatLonToUnitVector(float latitude, float longitude)
    {
        float latRad = latitude * Mathf.Deg2Rad;
        float lonRad = longitude * Mathf.Deg2Rad;
        float x = Mathf.Cos(latRad) * Mathf.Sin(lonRad);
        float y = Mathf.Sin(latRad);
        float z = Mathf.Cos(latRad) * Mathf.Cos(lonRad);
        return new Vector3(x, y, z).normalized;
    }

    // --- Public API: Get tile info at world point ---
    public HexTileData GetHexTileAtWorldPoint(Vector3 worldPoint)
    {
        return GetNearestHexTile(worldPoint, this.transform);
    }

    // Helper to get subdivisions and radius from preset
    public static void GetMapSizeParams(MapSize size, out int subdivisions, out float radius)
    {
        switch (size)
        {
            case MapSize.Small: subdivisions = 3; radius = 20f; break;   // 162 tiles
            case MapSize.Standard: subdivisions = 4; radius = 25f; break;   // 642 tiles
            case MapSize.Large: subdivisions = 5; radius = 100f; break;   // 2 562 tiles
            default: subdivisions = 4; radius = 25f; break;
        }
    }

    public static float GetMoonRadius(MapSize size)
    {
        GetMapSizeParams(size, out _, out float planetRadius);
        return planetRadius / 5f;
    }

    private void Awake()
    {
        Debug.Log("[GameManager] Awake called.");

        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Debug.LogWarning($"GameManager: Duplicate instance found! Destroying {gameObject.name}, keeping {Instance.gameObject.name}");
            Destroy(gameObject);
            return;
        }

        // Initialize GameSetupData with defaults if not already set
        if (GameSetupData.selectedPlayerCivilizationData == null && string.IsNullOrEmpty(GameSetupData.mapTypeName))
        {
            GameSetupData.InitializeDefaults();
        }

        // Read civilization settings from GameSetupData
        selectedPlayerCivilizationData = GameSetupData.selectedPlayerCivilizationData;
        numberOfCivilizations = GameSetupData.numberOfCivilizations;
        numberOfCityStates = GameSetupData.numberOfCityStates;
        numberOfTribes = GameSetupData.numberOfTribes;
        mapSize = GameSetupData.mapSize;
        animalPrevalence = GameSetupData.animalPrevalence;
        generateMoon = GameSetupData.generateMoon;

        Debug.Log("=== GameManager.Awake() COMPLETED ===");
    }

    private void Start()
    {
        Debug.Log("[GameManager] Start called.");
    }

    /// <summary>
    /// Cache structure for batched manager finding
    /// </summary>
    private struct ManagerCache
    {
        public CivilizationManager civilizationManager;
        public ClimateManager climateManager;
        public TurnManager turnManager;
        public UnitSelectionManager unitSelectionManager;
        public UnitMovementController unitMovementController;
        public PolicyManager policyManager;
        public DiplomacyManager diplomacyManager;
        public ResourceManager resourceManager;
        public ReligionManager religionManager;
        public AnimalManager animalManager;
        public AncientRuinsManager ancientRuinsManager;
        public LoadingPanelController loadingPanelController;
        public PlanetaryCameraManager cameraManager;
    }

    /// <summary>
    /// PERFORMANCE FIX: Batch all FindAnyObjectByType calls into a single scene search
    /// </summary>
    private ManagerCache CacheAllManagerReferences()
    {
        // Find all managers in one pass through the scene
        var allComponents = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        
        ManagerCache cache = new ManagerCache();
        
        foreach (var component in allComponents)
        {
            switch (component)
            {
                case CivilizationManager cm: cache.civilizationManager = cm; break;
                case ClimateManager clm: cache.climateManager = clm; break;
                case TurnManager tm: cache.turnManager = tm; break;
                case UnitSelectionManager usm: cache.unitSelectionManager = usm; break;
                case UnitMovementController umc: cache.unitMovementController = umc; break;
                case PolicyManager pm: cache.policyManager = pm; break;
                case DiplomacyManager dm: cache.diplomacyManager = dm; break;
                case ResourceManager rm: cache.resourceManager = rm; break;
                case ReligionManager rgnm: cache.religionManager = rgnm; break;
                case AnimalManager am: cache.animalManager = am; break;
                case AncientRuinsManager arm: cache.ancientRuinsManager = arm; break;
                case LoadingPanelController lpc: cache.loadingPanelController = lpc; break;
                case PlanetaryCameraManager pcm: cache.cameraManager = pcm; break;
            }
        }
        
        Debug.Log($"[GameManager] Cached manager references in one scene search");
        return cache;
    }

    /// <summary>
    /// Finds and assigns references to core managers in the current scene.
    /// Creates managers if they don't exist.
    /// This should be called after the Game scene is loaded.
    /// </summary>
    private void FindCoreManagersInScene()
    {
        // CRITICAL: Create TileDataHelper FIRST (before other managers need it)
        if (TileDataHelper.Instance == null)
        {
            GameObject tileDataHelperGO = new GameObject("TileDataHelper");
            tileDataHelperGO.AddComponent<TileDataHelper>();
            Debug.Log("[GameManager] Created TileDataHelper");
        }
        else
        {
            Debug.Log("[GameManager] TileDataHelper already exists");
        }

        // Create SpaceRouteManager for interplanetary travel
        if (SpaceRouteManager.Instance == null)
        {
            if (spaceRouteManagerPrefab != null)
            {
                GameObject spaceRouteManagerGO = Instantiate(spaceRouteManagerPrefab);
                Debug.Log("[GameManager] Created SpaceRouteManager from prefab");
            }
            else
            {
                GameObject spaceRouteManagerGO = new GameObject("SpaceRouteManager");
                spaceRouteManagerGO.AddComponent<SpaceRouteManager>();
                Debug.Log("[GameManager] Created SpaceRouteManager (no prefab assigned)");
            }
        }
        else
        {
            Debug.Log("[GameManager] SpaceRouteManager already exists");
        }
        
        // PERFORMANCE FIX: Batch all FindAnyObjectByType calls together
        // This reduces the number of expensive scene searches from 15+ to 1
        var foundManagers = CacheAllManagerReferences();
        
        // Find or create CivilizationManager
        civilizationManager = foundManagers.civilizationManager;
        if (civilizationManager == null)
        {
            if (civilizationManagerPrefab != null)
            {
                GameObject civManagerGO = Instantiate(civilizationManagerPrefab);
                civilizationManager = civManagerGO.GetComponent<CivilizationManager>();
            }
            else
            {
                Debug.LogError("GameManager: CivilizationManager not found and no prefab assigned!");
            }
        }

        // Find or create ClimateManager
        climateManager = foundManagers.climateManager;
        if (climateManager == null)
        {
            if (climateManagerPrefab != null)
            {
                GameObject climateManagerGO = Instantiate(climateManagerPrefab);
                climateManager = climateManagerGO.GetComponent<ClimateManager>();
            }
            else
            {
                Debug.LogError("GameManager: ClimateManager not found and no prefab assigned!");
            }
        }

        diplomacyManager = foundManagers.diplomacyManager;

        // Find or create TurnManager
        turnManager = foundManagers.turnManager;
        if (turnManager == null)
        {
            if (turnManagerPrefab != null)
            {
                GameObject turnManagerGO = Instantiate(turnManagerPrefab);
                turnManager = turnManagerGO.GetComponent<TurnManager>();
            }
            else
            {
                Debug.LogError("GameManager: TurnManager not found and no prefab assigned!");
            }
        }

        // Find or create UnitSelectionManager
        var unitSelectionManager = foundManagers.unitSelectionManager;
        if (unitSelectionManager == null)
        {
            if (unitSelectionManagerPrefab != null)
            {

                GameObject unitSelectionManagerGO = Instantiate(unitSelectionManagerPrefab);
                unitSelectionManager = unitSelectionManagerGO.GetComponent<UnitSelectionManager>();
            }
            else
            {
                Debug.Log("GameManager: UnitSelectionManager not found and no prefab assigned, creating basic instance...");
                GameObject unitSelectionManagerGO = new GameObject("UnitSelectionManager");
                unitSelectionManager = unitSelectionManagerGO.AddComponent<UnitSelectionManager>();
            }
        }

        // Find or create UnitMovementController
        var unitMovementControllerObj = foundManagers.unitMovementController;
        if (unitMovementControllerObj == null)
        {
            if (unitMovementControllerPrefab != null)
            {
                GameObject unitMovementControllerGO = Instantiate(unitMovementControllerPrefab);
                unitMovementControllerObj = unitMovementControllerGO.GetComponent<UnitMovementController>();
            }
            else
            {
                Debug.Log("GameManager: UnitMovementController not found and no prefab assigned, creating basic instance...");
                GameObject unitMovementControllerGO = new GameObject("UnitMovementController");
                unitMovementControllerObj = unitMovementControllerGO.AddComponent<UnitMovementController>();
            }
        }
        // (We don't store unitMovementControllerObj in a public field here; we'll find it when needed)

        // Find or create PolicyManager
        var policyManager = foundManagers.policyManager;
        if (policyManager == null)
        {
            if (policyManagerPrefab != null)
            {
                GameObject policyManagerGO = Instantiate(policyManagerPrefab);
                policyManager = policyManagerGO.GetComponent<PolicyManager>();
            }
            else
            {
                Debug.LogError("GameManager: PolicyManager not found and no prefab assigned!");
            }
        }

        // Find or create DiplomacyManager
        diplomacyManager = foundManagers.diplomacyManager;
        if (diplomacyManager == null)
        {
            if (diplomacyManagerPrefab != null)
            {
                GameObject diplomacyManagerGO = Instantiate(diplomacyManagerPrefab);
                diplomacyManager = diplomacyManagerGO.GetComponent<DiplomacyManager>();
            }
            else
            {
                Debug.LogError("GameManager: DiplomacyManager not found and no prefab assigned!");
            }
        }

        // Find or create ResourceManager
        var resourceManager = foundManagers.resourceManager;
        if (resourceManager == null)
        {
            if (resourceManagerPrefab != null)
            {
                GameObject resourceManagerGO = Instantiate(resourceManagerPrefab);
                resourceManager = resourceManagerGO.GetComponent<ResourceManager>();
            }
            else
            {
                Debug.LogError("GameManager: ResourceManager not found and no prefab assigned!");
            }
        }

        // Find or create ReligionManager
        var religionManager = foundManagers.religionManager;
        if (religionManager == null)
        {
            if (religionManagerPrefab != null)
            {
                GameObject religionManagerGO = Instantiate(religionManagerPrefab);
                religionManager = religionManagerGO.GetComponent<ReligionManager>();
            }
            else
            {
                Debug.LogError("GameManager: ReligionManager not found and no prefab assigned!");
            }
        }

        // Find or create AnimalManager
        var animalManager = foundManagers.animalManager;
        if (animalManager == null)
        {
            if (animalManagerPrefab != null)
            {
                GameObject animalManagerGO = Instantiate(animalManagerPrefab);
                animalManager = animalManagerGO.GetComponent<AnimalManager>();
            }
            else
            {
                Debug.LogError("GameManager: AnimalManager not found and no prefab assigned!");
            }
        }

        // Find or create AncientRuinsManager
        var ancientRuinsManager = foundManagers.ancientRuinsManager;
        if (ancientRuinsManager == null)
        {
            if (ancientRuinsManagerPrefab != null)
            {
                GameObject ancientRuinsManagerGO = Instantiate(ancientRuinsManagerPrefab);
                ancientRuinsManager = ancientRuinsManagerGO.GetComponent<AncientRuinsManager>();
            }
            else
            {
                Debug.LogError("GameManager: AncientRuinsManager not found and no prefab assigned!");
            }
        }

    }

    /// <summary>
    /// Instantiate and configure the planet and moon generators from prefabs
    /// </summary>
    private void CreateGenerators()
    {
        Debug.Log($"[CreateGenerators] incoming mapSize = {mapSize}  ({(int)mapSize})");

        if (planetGeneratorPrefab != null)
        {
            GameObject planetGO = Instantiate(planetGeneratorPrefab);
            planetGenerator = planetGO.GetComponent<PlanetGenerator>();


            // Assign the loading panel controller if present
            var loadingPanelController = FindAnyObjectByType<LoadingPanelController>();
            if (planetGenerator != null && loadingPanelController != null)
            {
                planetGenerator.SetLoadingPanel(loadingPanelController);
            }

            // --- Use map size preset ---
            int subdivisions; float radius;
            GetMapSizeParams(mapSize, out subdivisions, out radius);


            // Generate grid data using the new proper geodesic hexasphere system with the correct radius
            if (planetGenerator != null)
            {
                planetGenerator.radius = radius; // Set the radius property
                planetGenerator.Grid.GenerateFromSubdivision(subdivisions, radius);
                // No more hexasphereRenderer setup needed
            }

            // Configure planet generator with GameSetupData settings
            planetGenerator.SetMapTypeName(GameSetupData.mapTypeName);

            // Climate settings
            planetGenerator.polarLatitudeThreshold = GameSetupData.polarLatitudeThreshold;
            planetGenerator.subPolarLatitudeThreshold = GameSetupData.subPolarLatitudeThreshold;
            planetGenerator.equatorLatitudeThreshold = GameSetupData.equatorLatitudeThreshold;

            // Moisture and temperature settings
            planetGenerator.moistureBias = GameSetupData.moistureBias;
            planetGenerator.temperatureBias = GameSetupData.temperatureBias;

            // Land generation settings
            planetGenerator.landThreshold = GameSetupData.landThreshold;
            planetGenerator.maxContinentWidthDegrees = GameSetupData.maxContinentWidthDegrees;
            planetGenerator.maxContinentHeightDegrees = GameSetupData.maxContinentHeightDegrees;
            planetGenerator.seedPositionVariance = GameSetupData.seedPositionVariance;
            planetGenerator.numberOfContinents = GameSetupData.numberOfContinents;

            // River settings
            planetGenerator.enableRivers = GameSetupData.enableRivers;
            planetGenerator.minRiversPerContinent = GameSetupData.riverCount;
            planetGenerator.maxRiversPerContinent = GameSetupData.riverCount + 1;

            // Island generation settings
            planetGenerator.numberOfIslands = GameSetupData.numberOfIslands;
            planetGenerator.generateIslands = GameSetupData.generateIslands;



            // Notify TileDataHelper of the new generator
            if (TileDataHelper.Instance != null)
            {
                TileDataHelper.Instance.UpdateReferences();
            }
        }
        else
        {
            Debug.LogError("PlanetGenerator prefab is not assigned in GameManager!");
        }

        // Instantiate MoonGenerator from prefab if moon generation is enabled
        if (generateMoon && moonGeneratorPrefab != null)
        {
            GameObject moonGO = Instantiate(moonGeneratorPrefab);
            moonGenerator = moonGO.GetComponent<MoonGenerator>();

            // Position the moon away from the planet
            moonGO.transform.position = new Vector3(15f, 40f, 0f); // offset position

            if (moonGenerator != null)
            {
                // Configure moon generator with reduced subdivisions proportional to size
                GetMapSizeParams(mapSize, out int planetSubdivisions, out float planetRadius);
                float moonRadius = planetRadius / 2.5f;

                // Scale moon subdivisions: since radius is 1/5th, reduce subdivisions by 2 levels
                // This gives approximately 1/5th the tile count
                int moonSubdivisions = Mathf.Max(2, planetSubdivisions - 2); // Minimum of 3 to avoid too few tiles

                // Assign loading panel controller if present
                var loadingPanelController = FindAnyObjectByType<LoadingPanelController>();
                if (loadingPanelController != null)
                {
                    moonGenerator.SetLoadingPanel(loadingPanelController);
                }

                // Configure moon with correct radius and reduced subdivisions
                moonGenerator.ConfigureMoon(moonSubdivisions, moonRadius);

                Debug.Log($"[GameManager] Moon configured with subdivisions: {moonSubdivisions} (planet: {planetSubdivisions}), radius: {moonRadius:F1} (planet: {planetRadius:F1})");

                // No more hexasphereRenderer setup needed for moon

                // Notify TileDataHelper of the new generator
                if (TileDataHelper.Instance != null)
                {
                    TileDataHelper.Instance.UpdateReferences();
                }
            }
            else
            {
                Debug.LogError("MoonGenerator prefab does not have a MoonGenerator component!");
            }
        }
    }

    /// <summary>
    /// Starts a new game with current settings
    /// </summary>
    public IEnumerator StartNewGame()
    {
        Debug.Log("[GameManager] StartNewGame called");

        if (enableMultiPlanetSystem)
        {
            yield return StartCoroutine(StartMultiPlanetGame());
        }
        else
        {
            yield return StartCoroutine(StartSinglePlanetGame());
        }

        yield return null;
    }

    /// <summary>
    /// Start single planet game (original behavior)
    /// </summary>
    private IEnumerator StartSinglePlanetGame()
    {
        Debug.Log("[GameManager] StartNewGame coroutine started.");

        // --- CRITICAL: Refresh settings from GameSetupData ---
        Debug.Log("GameManager.StartNewGame(): Refreshing settings from GameSetupData.");
        selectedPlayerCivilizationData = GameSetupData.selectedPlayerCivilizationData;
        numberOfCivilizations = GameSetupData.numberOfCivilizations;
        numberOfCityStates = GameSetupData.numberOfCityStates;
        numberOfTribes = GameSetupData.numberOfTribes;
        mapSize = GameSetupData.mapSize;
        animalPrevalence = GameSetupData.animalPrevalence;
        generateMoon = GameSetupData.generateMoon;
        Debug.Log($"GameManager.StartNewGame() - Refreshed Counts: AI: {numberOfCivilizations}, CS: {numberOfCityStates}, Tribes: {numberOfTribes}");
        // --- End Refresh ---

        // Instantiate and configure generators (Planet first, then managers)
        CreateGenerators();
        // Ensure all core managers are present in the scene (after planet creation)
        FindCoreManagersInScene();

        // Set references on UnitMovementController now that planet and managers exist
        var unitMovementController = FindAnyObjectByType<UnitMovementController>();
        if (unitMovementController != null)
        {
            if (planetGenerator != null)
            {
                var grid = planetGenerator.Grid;
                unitMovementController.SetReferences(grid, planetGenerator, moonGenerator);
                Debug.Log("[GameManager] Set UnitMovementController references to current planet and moon generators");
            }
            else
            {
                Debug.LogWarning("GameManager: PlanetGenerator is null, cannot set UnitMovementController references!");
            }
        }
        else
        {
            Debug.LogWarning("GameManager: UnitMovementController not found after generator creation!");
        }

        // --- Camera instantiation ---
        if (planetaryCameraPrefab != null && Camera.main == null)
        {
            instantiatedCameraGO = Instantiate(planetaryCameraPrefab);
            instantiatedCameraGO.tag = "MainCamera";
            instantiatedCameraGO.SetActive(true);

            // Ensure the camera has an AudioListener
            if (instantiatedCameraGO.GetComponent<AudioListener>() == null)
            {
                instantiatedCameraGO.AddComponent<AudioListener>();
            }

            // Ensure camera has latest generator references
            var cameraManager = instantiatedCameraGO.GetComponent<PlanetaryCameraManager>();
            if (cameraManager != null)
            {
                Debug.Log("GameManager: Refreshed camera references after instantiation.");
            }
        }
        else if (Camera.main != null)
        {
            instantiatedCameraGO = Camera.main.gameObject;
        }
        else
        {
            Debug.LogWarning("GameManager: planetaryCameraPrefab not assigned!");
        }

        // --- Assign observer after camera is instantiated ---


        // Reset game state
        currentTurn = 0;
        gameInProgress = true;
        gamePaused = false;

        Debug.Log("=== STARTING MAP GENERATION ===");

        {
            // Generate the map (planet and optionally moon) using regular system
            if (planetGenerator != null)
            {
                yield return StartCoroutine(GenerateMap());
            }
            else
            {
                Debug.LogError("PlanetGenerator not created. Can't start game.");
                yield break;
            }
        }

        Debug.Log("=== MAP GENERATION COMPLETE ===");

        // Generate minimap now that the planet is ready
        var minimapUI = FindAnyObjectByType<MinimapUI>();
        if (minimapUI != null)
        {
            Debug.Log("[GameManager] Starting minimap generation...");
            
            // Start minimap generation
            minimapUI.StartMinimapGeneration();
            
            // Wait for minimaps to be pre-generated
            while (!minimapUI.MinimapsPreGenerated)
            {
                yield return null;
            }
            
            Debug.Log("[GameManager] Minimap pre-generation complete");
        }

        // Spawn civilizations
        if (civilizationManager != null)
        {
            CivData playerCivData = GameSetupData.selectedPlayerCivilizationData;
            if (playerCivData == null)
            {
                Debug.LogWarning("No player civilization selected in GameSetupData. CivilizationManager will select a default.");
            }
            civilizationManager.SpawnCivilizations(
                playerCivData,
                numberOfCivilizations,
                numberOfCityStates,
                numberOfTribes);

            // Initialize the music manager with the newly spawned civs
            if (MusicManager.Instance != null)
            {
                MusicManager.Instance.InitializeMusicTracks();
            }
        }
        else
        {
            Debug.LogError("CivilizationManager not found. Can't spawn civilizations.");
        }

        // Spawn initial animals
        var animalManagerInstance = FindAnyObjectByType<AnimalManager>();
        if (animalManagerInstance != null)
        {
            if (planetGenerator != null)
            {
                // AnimalManager now gets grid and planet data from TileDataHelper
                animalManagerInstance.SpawnInitialAnimals();
            }
            else
            {
                Debug.LogWarning("GameManager: PlanetGenerator is null, cannot spawn initial animals.");
            }
        }
        else
        {
            Debug.LogWarning("GameManager: AnimalManager not found, cannot spawn initial animals.");
        }

        Debug.Log("=== STARTING UI INITIALIZATION ===");

        // Initialize UI after civilizations are spawned
        yield return new WaitForEndOfFrame(); // Give everything a frame to settle
        Debug.Log("[GameManager] Calling InitializeUI...");
        InitializeUI();
        Debug.Log("[GameManager] InitializeUI finished.");

        Debug.Log("=== UI INITIALIZATION COMPLETE ===");

        // Game is now ready
        OnGameStarted?.Invoke();

        // Start game music now that everything is loaded and the loading panel will be hidden
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayMusic();
        }

        Debug.Log("=== GameManager.StartNewGame() COMPLETED SUCCESSFULLY ===");

        yield return null;
    }

    /// <summary>
    /// Start multi-planet game
    /// </summary>
    private IEnumerator StartMultiPlanetGame()
    {
        Debug.Log("[GameManager] Starting multi-planet game");

        // --- CRITICAL: Refresh settings from GameSetupData ---
        Debug.Log("GameManager.StartMultiPlanetGame(): Refreshing settings from GameSetupData.");
        selectedPlayerCivilizationData = GameSetupData.selectedPlayerCivilizationData;
        numberOfCivilizations = GameSetupData.numberOfCivilizations;
        numberOfCityStates = GameSetupData.numberOfCityStates;
        numberOfTribes = GameSetupData.numberOfTribes;
        mapSize = GameSetupData.mapSize;
        animalPrevalence = GameSetupData.animalPrevalence;
        generateMoon = GameSetupData.generateMoon;
        Debug.Log($"GameManager.StartMultiPlanetGame() - Refreshed Counts: AI: {numberOfCivilizations}, CS: {numberOfCityStates}, Tribes: {numberOfTribes}");
        // --- End Refresh ---
        
        // CRITICAL: Don't create managers until planets exist!
        // This prevents singleton conflicts and ensures proper execution order

        // Initialize and generate all planets FIRST
        yield return StartCoroutine(InitializeMultiPlanetSystem());

        // NOW it's safe to create managers since planets exist
        FindCoreManagersInScene();

        // Start with the first planet
        if (planetData.Count > 0)
        {
            currentPlanetIndex = planetData.Keys.First();
            Debug.Log($"[GameManager] Setting current planet to {currentPlanetIndex}");
        }

        // Set references on UnitMovementController now that planets and managers exist
        var unitMovementController = FindAnyObjectByType<UnitMovementController>();
        if (unitMovementController != null)
        {
            var currentPlanet = GetCurrentPlanetGenerator();
            var currentMoon = GetCurrentMoonGenerator();
            if (currentPlanet != null)
            {
                var grid = currentPlanet.Grid;
                unitMovementController.SetReferences(grid, currentPlanet, currentMoon);
                Debug.Log("[GameManager] Set UnitMovementController references to current planet and moon generators");
            }
            else
            {
                Debug.LogWarning("GameManager: Current PlanetGenerator is null, cannot set UnitMovementController references!");
            }
        }
        else
        {
            Debug.LogWarning("GameManager: UnitMovementController not found after generator creation!");
        }

        // Continue with the rest of game initialization (copied from StartSinglePlanetGame)
        
        // --- Camera instantiation ---
        if (planetaryCameraPrefab != null && Camera.main == null)
        {
            instantiatedCameraGO = Instantiate(planetaryCameraPrefab);
            instantiatedCameraGO.tag = "MainCamera";
            instantiatedCameraGO.SetActive(true);

            // Ensure the camera has an AudioListener
            if (instantiatedCameraGO.GetComponent<AudioListener>() == null)
            {
                instantiatedCameraGO.AddComponent<AudioListener>();
            }

            // Ensure camera has latest generator references
            var cameraManager = instantiatedCameraGO.GetComponent<PlanetaryCameraManager>();
            if (cameraManager != null)
            {
                Debug.Log("GameManager: Refreshed camera references after instantiation.");
            }
        }
        else if (Camera.main != null)
        {
            instantiatedCameraGO = Camera.main.gameObject;
        }
        else
        {
            Debug.LogWarning("GameManager: planetaryCameraPrefab not assigned!");
        }

        // Reset game state
        currentTurn = 0;
        gameInProgress = true;
        gamePaused = false;

        Debug.Log("=== MAP GENERATION COMPLETE (Multi-Planet) ===");

        // Trigger minimap generation now that planets are ready
        var minimapUI = FindAnyObjectByType<MinimapUI>();
        if (minimapUI != null)
        {
            UpdateLoadingProgress(0.70f, "Waiting for planet surfaces...");
            Debug.Log("[GameManager] Waiting for planet surfaces to be generated...");
            
            // Wait for all planet surfaces to be generated before creating minimaps
            bool allSurfacesReady = false;
            int maxWaitFrames = 300; // 5 seconds at 60fps
            int waitFrames = 0;
            
            while (!allSurfacesReady && waitFrames < maxWaitFrames)
            {
                allSurfacesReady = true;
                
                // Check if all planets have generated their surfaces
                for (int i = 0; i < totalPlanets; i++)
                {
                    var planetGen = GetPlanetGenerator(i);
                    if (planetGen != null && !planetGen.HasGeneratedSurface)
                    {
                        allSurfacesReady = false;
                        break;
                    }
                }
                
                if (!allSurfacesReady)
                {
                    waitFrames++;
                    yield return null;
                }
            }
            
            if (allSurfacesReady)
            {
                Debug.Log("[GameManager] All planet surfaces generated, starting minimap generation...");
            }
            else
            {
                Debug.LogWarning("[GameManager] Timeout waiting for planet surfaces, proceeding with minimap generation anyway...");
            }
            
            UpdateLoadingProgress(0.72f, "Generating minimaps...");
            Debug.Log("[GameManager] Starting minimap generation...");
            
            // Start minimap generation
            minimapUI.StartMinimapGeneration();
            
            // Wait for minimaps to be pre-generated
            while (!minimapUI.MinimapsPreGenerated)
            {
                yield return null;
            }
            
            Debug.Log("[GameManager] Minimap pre-generation complete");
        }

        // Update loading progress - Civilization spawning
        UpdateLoadingProgress(0.75f, "Spawning civilizations...");
        
        // Spawn civilizations
        if (civilizationManager != null)
        {
            CivData playerCivData = GameSetupData.selectedPlayerCivilizationData;
            if (playerCivData == null)
            {
                Debug.LogWarning("No player civilization selected in GameSetupData. CivilizationManager will select a default.");
            }
            civilizationManager.SpawnCivilizations(
                playerCivData,
                numberOfCivilizations,
                numberOfCityStates,
                numberOfTribes);

            // Initialize the music manager with the newly spawned civs
            if (MusicManager.Instance != null)
            {
                MusicManager.Instance.InitializeMusicTracks();
            }
        }
        else
        {
            Debug.LogError("CivilizationManager not found. Can't spawn civilizations.");
        }

        // Update loading progress - Animal spawning
        UpdateLoadingProgress(0.85f, "Spawning wildlife...");
        
        // Spawn initial animals
        var animalManagerInstance = FindAnyObjectByType<AnimalManager>();
        if (animalManagerInstance != null)
        {
            // AnimalManager now gets grid and planet data from TileDataHelper
            animalManagerInstance.SpawnInitialAnimals();
        }
        else
        {
            Debug.LogWarning("GameManager: AnimalManager not found, cannot spawn initial animals.");
        }

        // Update loading progress - Minimap setup
        UpdateLoadingProgress(0.90f, "Setting up minimaps...");
        


        // Configure SunBillboard for multi-planet system
        var sunBB = FindAnyObjectByType<SunBillboard>();
        if (sunBB != null)
        {
            var currentPlanet = GetCurrentPlanetGenerator();
            if (currentPlanet != null)
            {
                sunBB.SetBaseRadius(currentPlanet.radius);
                Debug.Log($"[GameManager] SunBillboard radius set to {currentPlanet.radius} for current planet");
            }
            else
            {
                Debug.LogWarning("[GameManager] Current PlanetGenerator is null, cannot set SunBillboard radius!");
            }
        }
        else
        {
            Debug.Log("[GameManager] No SunBillboard found in scene");
        }

        Debug.Log("=== STARTING UI INITIALIZATION (Multi-Planet) ===");

        // Update loading progress - UI initialization
        UpdateLoadingProgress(0.95f, "Initializing interface...");

        // Initialize UI after civilizations are spawned
        yield return new WaitForEndOfFrame(); // Give everything a frame to settle
        Debug.Log("[GameManager] Calling InitializeUI...");
        InitializeUI();
        Debug.Log("[GameManager] InitializeUI finished.");

        Debug.Log("=== UI INITIALIZATION COMPLETE (Multi-Planet) ===");

        // Update loading progress - Final steps
        UpdateLoadingProgress(1.0f, "Game ready!");

        // Game is now ready
        OnGameStarted?.Invoke();

        // CRITICAL: Hide loading panel now that game is ready
        HideLoadingPanel();

        // Start game music now that everything is loaded and the loading panel is hidden
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayMusic();
        }

        Debug.Log("=== GameManager.StartMultiPlanetGame() COMPLETED SUCCESSFULLY ===");
    }

    private void ApplyRealPlanetIdentity(PlanetGenerator g, string bodyName)
    {
        g.ClearRealPlanetFlags();
        g.allowOceans = true; g.allowRivers = true; g.allowIslands = true;

        switch (bodyName)
        {
            case "Earth":
                break;
            case "Mars":
                g.isMarsWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Venus":
                g.isVenusWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Mercury":
                g.isMercuryWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Jupiter":
                g.isJupiterWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Saturn":
                g.isSaturnWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Uranus":
                g.isUranusWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Neptune":
                g.isNeptuneWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Io":
                g.isIoWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Europa":
                g.isEuropaWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Ganymede":
                g.isGanymedeWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Callisto":
                g.isCallistoWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Titan":
                g.isTitanWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Luna":
                g.isLunaWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            case "Pluto":
                g.isPlutoWorldType = true;
                g.allowOceans = false; g.allowRivers = false; g.allowIslands = false;
                break;
            default:
                break;
        }
    }

    private MoonGenerator CreateMoonForPlanet(int planetIndex, PlanetGenerator generator, string bodyName)
    {
        MoonGenerator moonGen = null;

        switch (bodyName)
        {
            case "Earth":
                if (generateMoon)
                {
                    if (moonGeneratorPrefab != null)
                    {
                        GameObject moonGO = Instantiate(moonGeneratorPrefab);
                        moonGO.name = $"Planet_{planetIndex}_Moon";
                        moonGO.transform.position = generator.transform.position + new Vector3(15f, 40f, 0f);

                        moonGen = moonGO.GetComponent<MoonGenerator>();
                        if (moonGen != null)
                        {
                            float moonRadius = generator.radius / 2.5f;
                            int moonSubdivisions = Mathf.Max(2, generator.subdivisions - 2);

                            var loadingPanelController = FindAnyObjectByType<LoadingPanelController>();
                            if (loadingPanelController != null)
                                moonGen.SetLoadingPanel(loadingPanelController);

                            moonGen.ConfigureMoon(moonSubdivisions, moonRadius);
                        }
                        else
                        {
                            Debug.LogError("MoonGenerator prefab does not have a MoonGenerator component!");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[GameManager] moonGeneratorPrefab is NULL for Earth! Creating basic MoonGenerator.");
                        GameObject moonGO = new GameObject($"Planet_{planetIndex}_Moon");
                        moonGO.transform.position = generator.transform.position + new Vector3(15f, 40f, 0f);
                        
                        moonGen = moonGO.AddComponent<MoonGenerator>();
                        float moonRadius = generator.radius / 2.5f;
                        int moonSubdivisions = Mathf.Max(2, generator.subdivisions - 2);

                        var loadingPanelController = FindAnyObjectByType<LoadingPanelController>();
                        if (loadingPanelController != null)
                            moonGen.SetLoadingPanel(loadingPanelController);

                        moonGen.ConfigureMoon(moonSubdivisions, moonRadius);
                    }
                }
                break;

            // Future moons for other bodies can be handled here
            default:
                break;
        }

        if (moonGen != null)
        {
            moonGenerators[planetIndex] = moonGen;
            if (bodyName == "Earth")
                moonGenerator = moonGen;
            if (TileDataHelper.Instance != null)
                TileDataHelper.Instance.RegisterMoon(planetIndex, moonGen);
        }
        else
        {
            moonGenerators[planetIndex] = null;
        }

        return moonGen;
    }

    /// <summary>
    /// Initialize the multi-planet system with multiple planets
    /// </summary>
    private IEnumerator InitializeMultiPlanetSystem()
    {
        Debug.Log("[GameManager] Initializing multi-planet system");

        // Update loading progress - Starting multi-planet system
        UpdateLoadingProgress(0.05f, "Initializing solar system...");

        Debug.Log($"[GameManager] GameSetupData.systemPreset = {GameSetupData.systemPreset}");
        Debug.Log($"[GameManager] useRealSolarSystem field = {useRealSolarSystem}");
        
        if (GameSetupData.systemPreset == GameSetupData.SystemPreset.RealSolarSystem || useRealSolarSystem)
        {
            realBodies = new List<string>
            {
                "Earth",
                "Mars", "Venus", "Mercury",
                "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto",
                "Io", "Europa", "Ganymede", "Callisto", "Titan"
            };
            totalPlanets = realBodies.Count;
        }
        else
        {
            // Procedural system with basic planets
            realBodies = new List<string> { "Earth", "Mars", "Venus" };
            totalPlanets = realBodies.Count;
        }

        planetData.Clear();
        for (int i = 0; i < totalPlanets; i++)
        {
            string name = (GameSetupData.systemPreset == GameSetupData.SystemPreset.RealSolarSystem || useRealSolarSystem)
                ? realBodies[i]
                : $"Planet {i + 1}";

            PlanetData planet = new PlanetData
            {
                planetIndex = i,
                planetName = name,
                planetType = GetPlanetType(name),
                planetSize = GetPlanetSize(name),
                isHomeWorld = (i == 0),
                distanceFromStar = GetDistanceFromStar(name),
                orbitalPeriod = GetOrbitalPeriod(name),
                averageTemperature = GetAverageTemperature(name),
                description = GetPlanetDescription(name)
            };

            if (name == "Earth")
                planet.moonNames.Add("Luna");

            planet.isGenerated = planet.isExplored;
            planetData[i] = planet;
        }

        // CRITICAL FIX: Generate planets ONE AT A TIME completely
        // This prevents the MissingReferenceException by ensuring each planet finishes fully
        for (int i = 0; i < totalPlanets; i++)
        {
            Debug.Log($"[GameManager] Starting generation of planet {i} (waiting for complete finish before next)");
            
            // Update loading progress for planet generation
            float planetProgress = 0.1f + (0.6f * i / totalPlanets); // 10% to 70% for planet generation
            string planetName = (GameSetupData.systemPreset == GameSetupData.SystemPreset.RealSolarSystem || useRealSolarSystem)
                ? realBodies[i] : $"Planet {i + 1}";
            UpdateLoadingProgress(planetProgress, $"Generating {planetName}...");
            
            Vector3 position = GetPlanetPosition(i, realBodies[i]);
            yield return StartCoroutine(GenerateMultiPlanet(i, position));
            Debug.Log($"[GameManager] Planet {i} generation COMPLETELY FINISHED - moving to next");
            
            // Extra yield to ensure everything is fully settled before next planet
            yield return new WaitForEndOfFrame();
            yield return null;
        }

        // Update loading progress - Planet generation complete
        UpdateLoadingProgress(0.70f, "Planet generation complete!");
        
        Debug.Log($"[GameManager] Multi-planet system initialized with {planetData.Count} planets");
    }

    /// <summary>
    /// Get the world position for a planet/moon based on its type and relationship to parent planets
    /// </summary>
    private Vector3 GetPlanetPosition(int planetIndex, string bodyName)
    {
        float baseSpacing = 2000f; // Base spacing between planetary systems
        float moonDistance = 300f; // Distance of moons from their parent planet
        
        switch (bodyName)
        {
            // Inner planets
            case "Earth":
                return new Vector3(0, 0, 0); // Earth at origin
            case "Mars":
                return new Vector3(baseSpacing, 0, 0);
            case "Venus":
                return new Vector3(-baseSpacing, 0, 0);
            case "Mercury":
                return new Vector3(-baseSpacing * 2, 0, 0);
                
            // Outer planets
            case "Jupiter":
                return new Vector3(baseSpacing * 2, 0, 0);
            case "Saturn":
                return new Vector3(baseSpacing * 3, 0, 0);
            case "Uranus":
                return new Vector3(baseSpacing * 4, 0, 0);
            case "Neptune":
                return new Vector3(baseSpacing * 5, 0, 0);
            case "Pluto":
                return new Vector3(baseSpacing * 6, 0, 0);
                
            // Jupiter's moons - positioned around Jupiter
            case "Io":
                return new Vector3(baseSpacing * 2 + moonDistance, 0, moonDistance);
            case "Europa":
                return new Vector3(baseSpacing * 2 - moonDistance, 0, moonDistance);
            case "Ganymede":
                return new Vector3(baseSpacing * 2 + moonDistance, 0, -moonDistance);
            case "Callisto":
                return new Vector3(baseSpacing * 2 - moonDistance, 0, -moonDistance);
                
            // Saturn's moon - positioned near Saturn
            case "Titan":
                return new Vector3(baseSpacing * 3 + moonDistance, 0, 0);
                
            default:
                // Fallback positioning
                return new Vector3(planetIndex * 1000f, 0, 0);
        }
    }

    /// <summary>
    /// Get the appropriate planet type for a celestial body
    /// </summary>
    private PlanetType GetPlanetType(string bodyName)
    {
        return bodyName switch
        {
            "Earth" => PlanetType.Terran,
            "Mars" => PlanetType.Desert,
            "Venus" => PlanetType.Volcanic,
            "Mercury" => PlanetType.Barren,
            "Jupiter" => PlanetType.Gas_Giant,
            "Saturn" => PlanetType.Gas_Giant,
            "Uranus" => PlanetType.Ice,
            "Neptune" => PlanetType.Ice,
            "Pluto" => PlanetType.Ice,
            "Io" => PlanetType.Volcanic,
            "Europa" => PlanetType.Ice,
            "Ganymede" => PlanetType.Ice,
            "Callisto" => PlanetType.Barren,
            "Titan" => PlanetType.Tundra,
            _ => PlanetType.Terran
        };
    }

    /// <summary>
    /// Get the appropriate size for a celestial body
    /// </summary>
    private MapSize GetPlanetSize(string bodyName)
    {
        return bodyName switch
        {
            "Earth" => MapSize.Large,
            "Mars" => MapSize.Standard,
            "Venus" => MapSize.Standard,
            "Mercury" => MapSize.Small,
            "Jupiter" => MapSize.Large,
            "Saturn" => MapSize.Large,
            "Uranus" => MapSize.Standard,
            "Neptune" => MapSize.Standard,
            "Pluto" => MapSize.Small,
            // Moons are generally smaller
            "Io" or "Europa" or "Ganymede" or "Callisto" or "Titan" => MapSize.Small,
            _ => MapSize.Standard
        };
    }

    /// <summary>
    /// Get realistic distance from star for celestial bodies
    /// </summary>
    private float GetDistanceFromStar(string bodyName)
    {
        return bodyName switch
        {
            "Mercury" => 0.39f,
            "Venus" => 0.72f,
            "Earth" => 1.0f,
            "Mars" => 1.52f,
            "Jupiter" => 5.2f,
            "Saturn" => 9.5f,
            "Uranus" => 19.2f,
            "Neptune" => 30.1f,
            "Pluto" => 39.5f,
            // Moons have same distance as their parent planet
            "Io" or "Europa" or "Ganymede" or "Callisto" => 5.2f, // Jupiter's distance
            "Titan" => 9.5f, // Saturn's distance
            _ => 1.0f
        };
    }

    /// <summary>
    /// Get realistic orbital period for celestial bodies
    /// </summary>
    private float GetOrbitalPeriod(string bodyName)
    {
        return bodyName switch
        {
            "Mercury" => 88f,
            "Venus" => 225f,
            "Earth" => 365f,
            "Mars" => 687f,
            "Jupiter" => 4333f,
            "Saturn" => 10759f,
            "Uranus" => 30687f,
            "Neptune" => 60190f,
            "Pluto" => 90560f,
            // Moons orbit their parent planet, not the sun
            "Io" => 1.77f,
            "Europa" => 3.55f,
            "Ganymede" => 7.15f,
            "Callisto" => 16.69f,
            "Titan" => 15.95f,
            _ => 365f
        };
    }

    /// <summary>
    /// Get realistic average temperature for celestial bodies
    /// </summary>
    private float GetAverageTemperature(string bodyName)
    {
        return bodyName switch
        {
            "Mercury" => 167f,
            "Venus" => 464f,
            "Earth" => 15f,
            "Mars" => -65f,
            "Jupiter" => -110f,
            "Saturn" => -140f,
            "Uranus" => -195f,
            "Neptune" => -200f,
            "Pluto" => -230f,
            "Io" => -130f,
            "Europa" => -160f,
            "Ganymede" => -180f,
            "Callisto" => -185f,
            "Titan" => -179f,
            _ => 15f
        };
    }

    /// <summary>
    /// Get descriptive text for celestial bodies
    /// </summary>
    private string GetPlanetDescription(string bodyName)
    {
        return bodyName switch
        {
            "Earth" => "The blue marble - humanity's home world with vast oceans and diverse biomes",
            "Mars" => "The red planet - a cold, desert world with ancient riverbeds and polar ice caps",
            "Venus" => "The morning star - a volcanic hell world shrouded in thick, toxic atmosphere",
            "Mercury" => "The innermost planet - a scorched, cratered world of extreme temperatures",
            "Jupiter" => "The gas giant - a massive storm-wracked world with dozens of moons",
            "Saturn" => "The ringed planet - a beautiful gas giant adorned with spectacular ice rings",
            "Uranus" => "The ice giant - a tilted world of methane clouds and faint rings",
            "Neptune" => "The windy planet - a deep blue ice giant with the fastest winds in the solar system",
            "Pluto" => "The dwarf planet - a distant, frozen world at the edge of the solar system",
            "Io" => "Jupiter's volcanic moon - the most geologically active body in the solar system",
            "Europa" => "Jupiter's ice moon - hiding a subsurface ocean beneath its frozen crust",
            "Ganymede" => "Jupiter's largest moon - bigger than Mercury with its own magnetic field",
            "Callisto" => "Jupiter's cratered moon - an ancient, heavily bombarded ice world",
            "Titan" => "Saturn's largest moon - shrouded in thick atmosphere with hydrocarbon lakes",
            _ => "A mysterious world waiting to be explored"
        };
    }

    /// <summary>
    /// Generate a single planet for the multi-planet system
    /// </summary>
    private IEnumerator GenerateMultiPlanet(int planetIndex, Vector3 position)
    {
        Debug.Log($"[GameManager] Generating planet {planetIndex} at {position}");

        // Determine which prefab to use based on planet type
        string body = (GameSetupData.systemPreset == GameSetupData.SystemPreset.RealSolarSystem || useRealSolarSystem)
            ? realBodies[planetIndex]
            : (planetIndex == 0 ? "Earth" : "Mars");

        GameObject prefabToUse = null;
        
        // Use Earth prefab for Earth, generic prefab for others
        if (body == "Earth")
        {
            prefabToUse = planetGeneratorPrefab;
            if (prefabToUse == null)
            {
                Debug.LogError($"[GameManager] planetGeneratorPrefab is NULL for Earth!");
                yield break;
            }
        }
        else
        {
            prefabToUse = genericPlanetPrefab;
            // If generic prefab is missing, fall back to Earth prefab and log warning
            if (prefabToUse == null)
            {
                Debug.LogWarning($"[GameManager] genericPlanetPrefab is NULL for {body}! Using Earth prefab as fallback.");
                prefabToUse = planetGeneratorPrefab;
                if (prefabToUse == null)
                {
                    Debug.LogError($"[GameManager] Both planet prefabs are NULL for planet {planetIndex}!");
                    yield break;
                }
            }
        }

        GameObject planetGO = Instantiate(prefabToUse);
        planetGO.name = $"Planet_{planetIndex}_Generator_{body}";
        planetGO.transform.position = position;

        var generator = planetGO.GetComponent<PlanetGenerator>();
        if (generator == null)
        {
            Debug.LogError($"[GameManager] Planet prefab missing PlanetGenerator component!");
            Destroy(planetGO);
            yield break;
        }

        if (planetData.ContainsKey(planetIndex))
            planetData[planetIndex].planetName = body;

        ApplyRealPlanetIdentity(generator, body);

        if (body == "Earth")
        {
            GetMapSizeParams(GameSetupData.mapSize, out int subdiv, out float rad);
            generator.subdivisions = subdiv;
            generator.radius = rad;
        }
        else
        {
            GetMapSizeParams(MapSize.Standard, out int subdiv, out float rad);
            generator.subdivisions = subdiv;
            generator.radius = rad;
        }

        generator.Grid.GenerateFromSubdivision(generator.subdivisions, generator.radius);

        if (body == "Earth")
        {
            generator.currentMapTypeName = GameSetupData.mapTypeName ?? "";
            generator.polarLatitudeThreshold = GameSetupData.polarLatitudeThreshold;
            generator.subPolarLatitudeThreshold = GameSetupData.subPolarLatitudeThreshold;
            generator.equatorLatitudeThreshold = GameSetupData.equatorLatitudeThreshold;
            generator.moistureBias = GameSetupData.moistureBias;
            generator.temperatureBias = GameSetupData.temperatureBias;
            generator.landThreshold = GameSetupData.landThreshold;
        }

        Debug.Log($"[GameManager] Starting surface generation for planet {planetIndex}");
        yield return StartCoroutine(generator.GenerateSurface());
        Debug.Log($"[GameManager] Surface generation complete for planet {planetIndex}");

        // Ensure generator is still valid after surface generation
        if (generator == null || generator.gameObject == null)
        {
            Debug.LogError($"[GameManager] Planet generator {planetIndex} became invalid during surface generation!");
            yield break;
        }

        planetGenerators[planetIndex] = generator;

        if (TileDataHelper.Instance != null)
        {
            TileDataHelper.Instance.RegisterPlanet(planetIndex, generator);
        }

        var moonGen = CreateMoonForPlanet(planetIndex, generator, body);
        if (moonGen != null)
        {
            Debug.Log($"[GameManager] Starting moon generation for planet {planetIndex}");
            yield return StartCoroutine(moonGen.GenerateSurface());
            Debug.Log($"[GameManager] Moon generation complete for planet {planetIndex}");
        }

        // Use climate manager attached to the planet prefab
        var climateManager = planetGO.GetComponent<ClimateManager>();
        if (climateManager == null)
        {
            // Look for climate manager in children
            climateManager = planetGO.GetComponentInChildren<ClimateManager>();
        }
        
        if (climateManager != null)
        {
            Debug.Log($"[GameManager] Found ClimateManager attached to planet {planetIndex} ({body})");
            climateManager.planetIndex = planetIndex;
            planetClimateManagers[planetIndex] = climateManager;
        }
        else
        {
            Debug.LogWarning($"[GameManager] No ClimateManager found on planet {planetIndex} ({body})! Creating basic ClimateManager.");
            GameObject climateGO = new GameObject($"Planet_{planetIndex}_ClimateManager");
            climateGO.transform.SetParent(planetGO.transform);
            climateManager = climateGO.AddComponent<ClimateManager>();
            climateManager.planetIndex = planetIndex;
            planetClimateManagers[planetIndex] = climateManager;
        }

        // Extra safety yield to ensure everything is completely finished
        yield return new WaitForEndOfFrame();
        yield return null;

        Debug.Log($"[GameManager] Planet {planetIndex} generation COMPLETELY FINISHED");
    }



    /// <summary>
    /// Switch to a different planet in multi-planet mode
    /// </summary>
    public IEnumerator SwitchToMultiPlanet(int planetIndex)
    {
        if (!enableMultiPlanetSystem)
        {
            Debug.LogWarning("[GameManager] SwitchToMultiPlanet called but multi-planet system is disabled");
            yield break;
        }

        if (!planetGenerators.ContainsKey(planetIndex))
        {
            Debug.LogWarning($"[GameManager] Planet {planetIndex} does not exist");
            yield break;
        }

        Debug.Log($"[GameManager] Switching to planet {planetIndex}");
        currentPlanetIndex = planetIndex;
        moonGenerator = moonGenerators.TryGetValue(planetIndex, out var mg) ? mg : null;

        // Ensure grid is built and surface generated if needed
        var generator = planetGenerators[planetIndex];
        if (generator != null && !generator.Grid.IsBuilt)
        {
            GetMapSizeParams(planetData[planetIndex].planetSize, out int subDivs, out float rad);
            generator.subdivisions = subDivs;
            generator.radius = rad;
            generator.Grid.GenerateFromSubdivision(subDivs, rad);
        }
        if (generator != null && generator.Grid.TileCount > 0 && !generator.HasGeneratedSurface)
        {
            Debug.Log($"[GameManager] Generating surface for planet {planetIndex}");
            yield return StartCoroutine(generator.GenerateSurface());
        }

        if (moonGenerator != null && moonGenerator.Grid.TileCount > 0 && moonGenerator.Tiles.Count == 0)
        {
            Debug.Log($"[GameManager] Generating moon for planet {planetIndex}");
            yield return StartCoroutine(moonGenerator.GenerateSurface());
        }

        // Update references in other systems
        if (TileDataHelper.Instance != null)
        {
            TileDataHelper.Instance.UpdateReferences();
        }

        Debug.Log($"[GameManager] Successfully switched to planet {planetIndex}");
    }

    /// <summary>
    /// Handles map generation process
    /// </summary>
    private IEnumerator GenerateMap()
    {
        Debug.Log("Generating planet...");
        // Use GenerateSurface as a coroutine and wait for all map generation to finish
        yield return StartCoroutine(planetGenerator.GenerateSurface());

        // Automatically update SunBillboard radius after planet is generated
        var sunBB = FindAnyObjectByType<SunBillboard>();
        if (sunBB != null && planetGenerator != null)
        {
            sunBB.SetBaseRadius(planetGenerator.radius);
        }

        // Generate moon if enabled
        if (generateMoon && moonGenerator != null)
        {
            Debug.Log("Generating moon...");
            // Wait a frame to ensure planet is fully initialized
            yield return null;
            // Call GenerateSurface on the moon generator as a coroutine
            yield return StartCoroutine(moonGenerator.GenerateSurface());
        }

        Debug.Log("Map generation complete!");


    }



    /// <summary>
    /// Public method to generate the world with a callback when finished
    /// </summary>
    public void GenerateWorld(Action onComplete = null)
    {
        StartCoroutine(GenerateWorldRoutine(onComplete));
    }

    /// <summary>
    /// Coroutine to handle world generation with callback
    /// </summary>
    public IEnumerator GenerateWorldRoutine(Action onComplete)
    {
        yield return StartCoroutine(GenerateMap());

        // Map generation is complete, call the callback
        onComplete?.Invoke();
    }

    /// <summary>
    /// Initialize UI components after game setup is complete
    /// </summary>
    public void InitializeUI()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.HideAllPanels();
            if (UIManager.Instance.playerUI != null)
                UIManager.Instance.playerUI.SetActive(true);
        }

        // Initialize space loading panel if prefab is assigned
        InitializeSpaceLoadingPanel();
    }

    /// <summary>
    /// Initialize the space loading panel for future space travel
    /// </summary>
    private void InitializeSpaceLoadingPanel()
    {
        if (spaceLoadingPanelPrefab != null && spaceLoadingPanel == null)
        {
            GameObject spaceLoadingGO = Instantiate(spaceLoadingPanelPrefab);
            spaceLoadingPanel = spaceLoadingGO.GetComponent<SpaceLoadingPanelController>();

            if (spaceLoadingPanel != null)
            {
                // Ensure it starts hidden
                spaceLoadingPanel.HideSpaceLoading();
                Debug.Log("[GameManager] Space loading panel initialized");
            }
            else
            {
                Debug.LogWarning("[GameManager] Space loading panel prefab does not have SpaceLoadingPanelController component");
            }
        }
    }

    /// <summary>
    /// Switch view to Earth's moon (Luna) in multi-planet setups, or to the single moon in single-planet mode.
    /// </summary>
    public void GoToEarthMoon()
    {
        // Determine Earth index if using real system; otherwise assume index 0
        int earthIndex = 0;
        if (enableMultiPlanetSystem)
        {
            foreach (var kv in planetData)
            {
                if (string.Equals(kv.Value.planetName, "Earth", StringComparison.OrdinalIgnoreCase))
                {
                    earthIndex = kv.Key;
                    break;
                }
            }

            if (currentPlanetIndex != earthIndex)
            {
                // Switch current planet to Earth synchronously if already generated
                if (planetGenerators.ContainsKey(earthIndex))
                {
                    currentPlanetIndex = earthIndex;
                    moonGenerator = moonGenerators.TryGetValue(earthIndex, out var mg) ? mg : null;
                    TileDataHelper.Instance?.UpdateReferences();
                }
                else
                {
                    Debug.LogWarning($"[GameManager] Earth index {earthIndex} not ready.");
                }
            }
        }

        // Ensure Earth has a moon
        var currentMoon = GetCurrentMoonGenerator();
        if (currentMoon == null)
        {
            Debug.LogWarning("[GameManager] No moon generator available for current planet. Cannot go to moon.");
            return;
        }

        // Move camera to orbit the moon
        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        if (camMgr != null)
        {
            camMgr.SwitchToMoon(true);
            Debug.Log("[GameManager] Switched camera to Earth's moon.");
        }
        else
        {
            Debug.LogWarning("[GameManager] PlanetaryCameraManager not found. Cannot switch camera to moon.");
        }
    }

    /// <summary>
    /// Show space travel loading screen (for future space travel features)
    /// </summary>
    public void ShowSpaceTravel(string destination, GameObject[] playerSpaceships = null)
    {
        if (spaceLoadingPanel != null)
        {
            string status = $"Traveling to {destination}...";
            spaceLoadingPanel.ShowSpaceLoading(status, playerSpaceships);
            Debug.Log($"[GameManager] Started space travel to {destination}");
        }
        else
        {
            Debug.LogWarning("[GameManager] No space loading panel available for space travel");
        }
    }

    /// <summary>
    /// Hide space travel loading screen
    /// </summary>
    public void HideSpaceTravel()
    {
        if (spaceLoadingPanel != null)
        {
            spaceLoadingPanel.HideSpaceLoading();
            Debug.Log("[GameManager] Space travel completed");
        }
    }

    /// <summary>
    /// Update space travel progress (0.0 to 1.0)
    /// </summary>
    public void UpdateSpaceTravelProgress(float progress, string status = "")
    {
        if (spaceLoadingPanel != null)
        {
            spaceLoadingPanel.SetProgress(progress);
            if (!string.IsNullOrEmpty(status))
            {
                spaceLoadingPanel.SetStatus(status);
            }
        }
    }

    /// <summary>
    /// Ends the current game and returns to main menu
    /// </summary>
    public void EndGame()
    {
        gameInProgress = false;
        gamePaused = false;
        Time.timeScale = 1f;
        OnGameEnded?.Invoke();

        // PERFORMANCE FIX: Clean up memory before scene transition
        CleanupMemory();

        // Return to main menu scene
        SceneManager.LoadScene("MainMenu");
    }
    
    /// <summary>
    /// Update loading progress during game initialization
    /// </summary>
    private void UpdateLoadingProgress(float progress, string status)
    {
        var loadingPanelController = FindAnyObjectByType<LoadingPanelController>();
        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(progress);
            loadingPanelController.SetStatus(status);
            Debug.Log($"[GameManager] Loading: {progress:P0} - {status}");
        }
    }

    /// <summary>
    /// Hide the loading panel when game initialization is complete
    /// </summary>
    private void HideLoadingPanel()
    {
        var loadingPanelController = FindAnyObjectByType<LoadingPanelController>();
        if (loadingPanelController != null)
        {
            loadingPanelController.HideLoading();
            Debug.Log("[GameManager] Loading panel hidden - game ready to play!");
        }
        else
        {
            Debug.LogWarning("[GameManager] No LoadingPanelController found to hide");
        }
    }

    /// <summary>
    /// Clean up memory to prevent leaks and improve performance
    /// </summary>
    private void CleanupMemory()
    {
        Debug.Log("[GameManager] Starting memory cleanup...");
        
        // Clear object pools
        if (SimpleObjectPool.Instance != null)
        {
            SimpleObjectPool.Instance.ClearAllPools();
        }
        
        // Clear tile data caches
        if (TileDataHelper.Instance != null)
        {
            TileDataHelper.Instance.ClearAllCaches();
        }
        
        // Clear planet/moon generator references
        planetGenerators.Clear();
        moonGenerators.Clear();
        planetClimateManagers.Clear();
        planetCivManagers.Clear();
        planetData.Clear();
        
        // Clear hex tiles data
        hexTiles.Clear();
        
        // Force garbage collection
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        
        Debug.Log("[GameManager] Memory cleanup completed");
    }

    /// <summary>
    /// Save the current game state
    /// </summary>
    public void SaveGame(string saveName)
    {
        Debug.Log($"Saving game as {saveName}...");
        // Implement your save game logic here
    }

    /// <summary>
    /// Load a saved game
    /// </summary>
    public void LoadGame(string saveName)
    {
        Debug.Log($"Loading game {saveName}...");
        // Implement your load game logic here
    }
}


