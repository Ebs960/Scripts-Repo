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

    [Header("Continent Placement (Stamping)")]
    [SerializeField]
    private bool overrideContinentMinDistance = false;

    [SerializeField]
    [Tooltip("Used only if overrideContinentMinDistance is true")]
    private int continentMinDistanceOverrideTiles = 6;

    [Header("References")]
    public PlanetGenerator planetGenerator;
    public CivilizationManager civilizationManager;
    public ClimateManager climateManager;
    public DiplomacyManager diplomacyManager;

    [Header("Multi-Planet System")]
    [Tooltip("Multi-planet generation is always enabled. This legacy flag is retained for save compatibility.")]
    [HideInInspector]
    public bool enableMultiPlanetSystem = true;
    [Tooltip("Maximum number of planets to generate")]
    public int maxPlanets = 8;
    [Tooltip("Generate real solar system instead of procedural planets")]
    public bool useRealSolarSystem = false;

    // Multi-planet storage
    private Dictionary<int, PlanetGenerator> planetGenerators = new Dictionary<int, PlanetGenerator>();
    private Dictionary<int, CivilizationManager> planetCivManagers = new Dictionary<int, CivilizationManager>();
    private Dictionary<int, ClimateManager> planetClimateManagers = new Dictionary<int, ClimateManager>();
    private Dictionary<int, PlanetData> planetData = new Dictionary<int, PlanetData>();
    
    // Planet lifecycle events (event-driven readiness)
    public event Action<int> OnPlanetGridBuilt;
    public event Action<int> OnPlanetSurfaceGenerated;
    public event Action<int> OnPlanetManagersAttached;
    public event Action<int> OnPlanetReady;
    public static event Action<PlanetGenerator> OnPlanetFullyGenerated;
    private List<string> realBodies;
    private int totalPlanets;

    public int currentPlanetIndex = 0;
    public PlanetGenerator GetPlanetGenerator(int planetIndex) => planetGenerators.TryGetValue(planetIndex, out var gen) ? gen : null;
    public ClimateManager GetClimateManager(int planetIndex) => planetClimateManagers.TryGetValue(planetIndex, out var cm) ? cm : ClimateManager.Instance;
    public Dictionary<int, PlanetData> GetPlanetData() => planetData;
    
    /// <summary>
    /// Get the currently active planet generator (multi-planet aware)
    /// </summary>
    public PlanetGenerator GetCurrentPlanetGenerator()
    {
        if (planetGenerators.TryGetValue(currentPlanetIndex, out var generator))
            return generator;
        return planetGenerator;
    }
    
    /// <summary>
    /// Get the currently active climate manager (multi-planet aware)
    /// </summary>
    public ClimateManager GetCurrentClimateManager()
    {
        return GetClimateManager(currentPlanetIndex);
    }
    
    /// <summary>
    /// Set the current planet index and update references (multi-planet mode)
    /// </summary>
    public void SetCurrentPlanet(int planetIndex)
    {
        // Multi-planet is always enabled in runtime; directly set current planet.
        if (!planetGenerators.ContainsKey(planetIndex))
        {
            Debug.LogWarning($"[GameManager] Planet {planetIndex} does not exist");
            return;
        }
currentPlanetIndex = planetIndex;
        climateManager = GetClimateManager(currentPlanetIndex);
        
        // Rebind TileSystem to the new current planet
        var gen = GetPlanetGenerator(currentPlanetIndex);
        if (TileSystem.Instance != null && gen != null)
        {
            TileSystem.Instance.InitializeFromPlanet(gen);
        }
    }

    [Header("Game State")]
    public bool gameInProgress = false;
    
    // Private field for pause state
    private bool _gamePaused = false;
    
    // Public property that triggers event when changed
    public bool gamePaused 
    { 
        get => _gamePaused; 
        set 
        { 
            if (_gamePaused != value)
            {
                _gamePaused = value;
                OnGamePaused?.Invoke(value);
            }
        } 
    }
    
    public int currentTurn = 0;
    // Removed: private bool _spawnedCivsAndAnimals = false; - no longer needed with centralized spawning

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
    [Tooltip("Loading panel prefab for game initialization (replaces GameSceneInitializer)")]
    public GameObject loadingPanelPrefab;
    
    [Header("Minimap Configuration")]
    [Tooltip("MinimapColorProvider ScriptableObject asset for minimap rendering")]
    public MinimapColorProvider minimapColorProvider;

    [Header("Global UI Audio")]
    [Tooltip("Click sound played for all UI Buttons across all scenes.")]
    public AudioClip uiClickClip;
    [Range(0f,1f)] public float uiClickVolume = 1f;
    private AudioSource uiAudioSource;
    private readonly HashSet<UnityEngine.UI.Button> wiredButtons = new HashSet<UnityEngine.UI.Button>();

    private GameObject instantiatedCameraGO; // Store reference to the instantiated camera
    private SpaceLoadingPanelController spaceLoadingPanel; // Reference to space loading panel
    private LoadingPanelController cachedLoadingPanel; // Cached reference to loading panel (performance optimization)

    //Tile grid and lookup ---
    [System.Serializable]
    public class HexTileData
    {
        public int tileIndex;
        public float u, v; // Equirectangular UV (0-1)
        public int biomeIndex;
        public float height; // 0-1, for heightmap alpha
        public int food, production, gold, science, culture;
        public string name;
    }

    // --- References to high-res planet textures and grid ---

    public SphericalHexGrid planetGrid;

    public List<HexTileData> hexTiles = new List<HexTileData>();

    [Header("Flat Map Size (Flat-Only)")]
    [Tooltip("Flat map width in world units (X extent)." )]
    public float flatMapWidth = 512f;
    [Tooltip("Flat map height in world units (Z extent)." )]
    public float flatMapHeight = 256f;
    [Tooltip("Y height of the flat map plane (used for world placement)." )]
    public float flatPlaneY = 0f;
    [Tooltip("Terrain displacement/height scale (how much renderElevation 0-1 translates to world Y offset). Should match HexMapChunkManager.displacementStrength.")]
    public float terrainDisplacementStrength = 5.0f;

    public float GetTerrainDisplacementStrength() => terrainDisplacementStrength;

    // Flat-map-only: tile resolution by size preset
    public static void GetFlatTileResolution(MapSize size, out int tilesX, out int tilesZ)
    {
        switch (size)
        {
            case MapSize.Small: tilesX = 96; tilesZ = 48; break;
            case MapSize.Standard: tilesX = 148; tilesZ = 74; break;
            case MapSize.Large: tilesX = 256; tilesZ = 128; break;
            default: tilesX = 148; tilesZ = 74; break;
        }
    }

    // Flat-map-only: map width/height by size preset
    public static void GetFlatMapSizeParams(MapSize size, out float width, out float height)
    {
        switch (size)
        {
            case MapSize.Small: width = 384f; height = 192f; break;
            case MapSize.Standard: width = 592f; height = 296f; break;
            case MapSize.Large: width = 1024f; height = 514f; break;
            default: width = 592f; height = 296f; break;
        }
    }

    private void ApplyStampSettingsForMapSize(MapSize size)
    {
        int continentMinW = GameSetupData.minContinentWidthTilesStandard;
        int continentMaxW = GameSetupData.maxContinentWidthTilesStandard;
        int continentMinH = GameSetupData.minContinentHeightTilesStandard;
        int continentMaxH = GameSetupData.maxContinentHeightTilesStandard;
        int islandMinW = GameSetupData.minIslandWidthTilesStandard;
        int islandMaxW = GameSetupData.maxIslandWidthTilesStandard;
        int islandMinH = GameSetupData.minIslandHeightTilesStandard;
        int islandMaxH = GameSetupData.maxIslandHeightTilesStandard;

        switch (size)
        {
            case MapSize.Small:
                continentMinW = GameSetupData.minContinentWidthTilesSmall;
                continentMaxW = GameSetupData.maxContinentWidthTilesSmall;
                continentMinH = GameSetupData.minContinentHeightTilesSmall;
                continentMaxH = GameSetupData.maxContinentHeightTilesSmall;
                islandMinW = GameSetupData.minIslandWidthTilesSmall;
                islandMaxW = GameSetupData.maxIslandWidthTilesSmall;
                islandMinH = GameSetupData.minIslandHeightTilesSmall;
                islandMaxH = GameSetupData.maxIslandHeightTilesSmall;
                break;
            case MapSize.Large:
                continentMinW = GameSetupData.minContinentWidthTilesLarge;
                continentMaxW = GameSetupData.maxContinentWidthTilesLarge;
                continentMinH = GameSetupData.minContinentHeightTilesLarge;
                continentMaxH = GameSetupData.maxContinentHeightTilesLarge;
                islandMinW = GameSetupData.minIslandWidthTilesLarge;
                islandMaxW = GameSetupData.maxIslandWidthTilesLarge;
                islandMinH = GameSetupData.minIslandHeightTilesLarge;
                islandMaxH = GameSetupData.maxIslandHeightTilesLarge;
                break;
        }

        GameSetupData.continentMinWidthTiles = continentMinW;
        GameSetupData.continentMaxWidthTiles = continentMaxW;
        GameSetupData.continentMinHeightTiles = continentMinH;
        GameSetupData.continentMaxHeightTiles = continentMaxH;
        int autoMinDistance =
            Mathf.Max(
                2,
                Mathf.RoundToInt(
                    Mathf.Min(continentMinW, continentMinH) * 0.35f
                )
            );

        if (overrideContinentMinDistance)
        {
            GameSetupData.continentMinDistanceTiles =
                Mathf.Max(1, continentMinDistanceOverrideTiles);
        }
        else
        {
            GameSetupData.continentMinDistanceTiles = autoMinDistance;
        }

        int minIslandDim = Mathf.Min(islandMinW, islandMinH);
        int maxIslandDim = Mathf.Max(islandMaxW, islandMaxH);
        GameSetupData.islandMinRadiusTiles = Mathf.Max(1, minIslandDim / 2);
        GameSetupData.islandMaxRadiusTiles = Mathf.Max(GameSetupData.islandMinRadiusTiles, maxIslandDim / 2);
        GameSetupData.islandMinDistanceFromContinents = Mathf.Max(2, GameSetupData.islandMinRadiusTiles);

        if (GameSetupData.lakeMinRadiusTiles <= 0)
        {
            GameSetupData.lakeMinRadiusTiles = 3;
        }
        if (GameSetupData.lakeMaxRadiusTiles <= 0)
        {
            GameSetupData.lakeMaxRadiusTiles = Mathf.Max(3, 12);
        }
    }

    public void SetFlatMapDimensionsFromSize(MapSize size)
    {
        GetFlatMapSizeParams(size, out flatMapWidth, out flatMapHeight);
}

    public float GetFlatMapWidth() => flatMapWidth;
    public float GetFlatMapHeight() => flatMapHeight;
    public float GetFlatPlaneY() => flatPlaneY;

    private void Awake()
    {

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

        // Initialize ResourceCache early (before any Resources.LoadAll calls)
        ResourceCache.Initialize();

        // Initialize global UI audio system
        SetupGlobalUIAudio();

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
        // Set flat map dimensions from chosen size (flat-only)
        SetFlatMapDimensionsFromSize(mapSize);
        animalPrevalence = GameSetupData.animalPrevalence;
        generateMoon = GameSetupData.generateMoon;

        
    }

    private const string GameSceneName = "Game";
    private bool _autoInitStarted = false;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded_AutoInit;
        SceneManager.sceneUnloaded += OnSceneUnloaded_Cleanup;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded_AutoInit;
        SceneManager.sceneUnloaded -= OnSceneUnloaded_Cleanup;
    }

    /// <summary>
    /// MEMORY FIX: Clean up GPU caches and resources when scenes unload to prevent memory leaks in editor
    /// </summary>
    private void OnSceneUnloaded_Cleanup(Scene scene)
    {
// Clear GPU texture/buffer caches (these persist as static and leak in editor)
        PlanetTextureBakerGPU.ClearAllCaches();
        
        // Clear ResourceCache to free ScriptableObject references
        ResourceCache.Clear();
        
        // Clear TileSystem caches if it exists
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.ClearAllCaches();
        }
        
        // Request garbage collection to free memory immediately
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
    }

    private void OnApplicationQuit()
    {
        // Final cleanup on application quit
        PlanetTextureBakerGPU.ClearAllCaches();
        ResourceCache.Clear();
    }

    private void Start()
    {
        // IMPORTANT:
        // Do NOT start world generation from MainMenu.
        // Game initialization should start only when the "Game" scene is active.
        TryAutoInitializeForActiveScene();
    }

    private void OnSceneLoaded_AutoInit(Scene scene, LoadSceneMode mode)
    {
        // If we persist across scenes, Start() will not run again—so we trigger auto-init here.
        TryAutoInitializeForActiveScene();
    }

    private void TryAutoInitializeForActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return;

        // Only auto-start in the gameplay scene.
        if (!string.Equals(scene.name, GameSceneName, StringComparison.OrdinalIgnoreCase))
            return;

        // Guard: never start twice.
        if (_autoInitStarted) return;
        if (gameInProgress) return;

        _autoInitStarted = true;
            StartCoroutine(InitializeGameScene(loadingPanelPrefab));
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

    // Add guard to prevent multiple FindCoreManagersInScene calls
    private bool _managersInitialized = false;

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
        
        
        return cache;
    }

    /// <summary>
    /// Finds and assigns references to core managers in the current scene.
    /// Creates managers if they don't exist.
    /// This should be called after the Game scene is loaded.
    /// </summary>
    private void FindCoreManagersInScene()
    {
        // GUARD: Prevent multiple initialization
        if (_managersInitialized)
        {
            return;
        }

        // Ensure TileSystem exists early (before other managers need tile data)
        if (TileSystem.Instance == null)
        {
            GameObject tileSystemGO = new GameObject("TileSystem");
            tileSystemGO.AddComponent<TileSystem>();
        }
        else
        {
            
        }

        // Create SpaceRouteManager for interplanetary travel
        if (SpaceRouteManager.Instance == null)
        {
            if (spaceRouteManagerPrefab != null)
            {
                GameObject spaceRouteManagerGO = Instantiate(spaceRouteManagerPrefab);
            }
            else
            {
                GameObject spaceRouteManagerGO = new GameObject("SpaceRouteManager");
                spaceRouteManagerGO.AddComponent<SpaceRouteManager>();
            }
        }
        else
        {
            
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
            
        // Create UnitReinforcementManager if it doesn't exist
        if (UnitReinforcementManager.Instance == null)
        {
            GameObject reinforcementManagerGO = new GameObject("UnitReinforcementManager");
            reinforcementManagerGO.AddComponent<UnitReinforcementManager>();
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

        // Cache LoadingPanelController for performance (used frequently)
        cachedLoadingPanel = foundManagers.loadingPanelController;

        // Mark managers as initialized to prevent duplicate creation
        _managersInitialized = true;
        
    }

    /// <summary>
    /// Instantiate and configure the planet and moon generators from prefabs
    /// </summary>
    private void CreateGenerators()
    {
        

        if (planetGeneratorPrefab != null)
        {
            GameObject planetGO = Instantiate(planetGeneratorPrefab);
            planetGenerator = planetGO.GetComponent<PlanetGenerator>();


            // Assign the loading panel controller if present (use cached reference)
            if (cachedLoadingPanel == null)
                cachedLoadingPanel = FindAnyObjectByType<LoadingPanelController>();
            if (planetGenerator != null && cachedLoadingPanel != null)
            {
                planetGenerator.SetLoadingPanel(cachedLoadingPanel);
            }

            // --- Use flat map size preset ---
            GetFlatMapSizeParams(mapSize, out float width, out float height);
            GetFlatTileResolution(mapSize, out int tilesX, out int tilesZ);

            // Generate flat grid using explicit dimensions
            if (planetGenerator != null)
            {
                planetGenerator.Grid.GenerateFlatGrid(tilesX, tilesZ, width, height);
            }

            // Configure planet generator with GameSetupData settings
            planetGenerator.SetMapTypeName(GameSetupData.mapTypeName);

            ApplyStampSettingsForMapSize(GameSetupData.mapSize);

            // Moisture and temperature settings
            planetGenerator.moistureBias = GameSetupData.moistureBias;
            planetGenerator.temperatureBias = GameSetupData.temperatureBias;

            // Land generation settings (allowed overrides only)
            planetGenerator.numberOfContinents = GameSetupData.numberOfContinents;
            planetGenerator.enableRivers = GameSetupData.enableRivers;
            planetGenerator.numberOfIslands = GameSetupData.numberOfIslands;
            planetGenerator.generateIslands = GameSetupData.generateIslands;
            planetGenerator.enableLakes = GameSetupData.enableLakes;
            planetGenerator.numberOfLakes = GameSetupData.numberOfLakes;
            planetGenerator.lakeMinRadiusTiles = GameSetupData.lakeMinRadiusTiles;
            planetGenerator.lakeMaxRadiusTiles = GameSetupData.lakeMaxRadiusTiles;
            planetGenerator.lakeMinDistanceFromCoast = GameSetupData.lakeMinDistanceFromCoast;

            // DIAGNOSTIC: log what we applied to the generator (tile-based ranges)
            Debug.Log($"[GameManager][Diag] Applied GameSetupData to PlanetGenerator: continents={planetGenerator.numberOfContinents}, islands={planetGenerator.numberOfIslands}, generateIslands={planetGenerator.generateIslands}");

            // Preserve tuning from the PlanetGenerator prefab so the generator's inspector remains the source of truth.
            Debug.Log("[GameManager][Diag] Preserving PlanetGenerator prefab tuning; preset tuning overrides skipped.");

            // Ensure island/rivers/lakes flags and counts come from GameSetupData
            planetGenerator.generateIslands = GameSetupData.generateIslands;
            planetGenerator.numberOfIslands = GameSetupData.numberOfIslands;
            planetGenerator.enableRivers = GameSetupData.enableRivers;
            planetGenerator.enableLakes = GameSetupData.enableLakes;
            planetGenerator.numberOfLakes = GameSetupData.numberOfLakes;
            planetGenerator.lakeMinRadiusTiles = GameSetupData.lakeMinRadiusTiles;
            planetGenerator.lakeMaxRadiusTiles = GameSetupData.lakeMaxRadiusTiles;
            planetGenerator.lakeMinDistanceFromCoast = GameSetupData.lakeMinDistanceFromCoast;



            // TileSystem will be initialized after surface generation
        }
        else
        {
            Debug.LogError("PlanetGenerator prefab is not assigned in GameManager!");
        }

        // Moon bodies are now generated as PlanetGenerator entries in the multi-planet system.
    }

    /// <summary>
    /// Initialize game scene (called automatically in Start() or can be called directly)
    /// Handles loading panel setup and game initialization
    /// </summary>
    public IEnumerator InitializeGameScene(GameObject loadingPanelPrefabOverride = null)
    {
        // Wait a frame to let Awake() run everywhere else
        yield return null;

        // Use override prefab if provided, otherwise use the field
        GameObject prefabToUse = loadingPanelPrefabOverride ?? loadingPanelPrefab;
        
        // Spawn loading panel if prefab provided and not already cached
        if (prefabToUse != null && cachedLoadingPanel == null)
        {
            GameObject loadingPanelInstance = Instantiate(prefabToUse);
            loadingPanelInstance.SetActive(true);
            cachedLoadingPanel = loadingPanelInstance.GetComponent<LoadingPanelController>();
            yield return null; // Wait a frame to ensure UI updates
        }

        // Start the game
        if (!gameInProgress)
            yield return StartCoroutine(StartNewGame());

        // Optional delay so player sees 100% for a moment
        yield return new WaitForSeconds(0.5f);

        // Wait for game to be ready
        yield return new WaitUntil(() => gameInProgress);

        // TileHoverSystem is self-initializing, no setup needed
    }

    /// <summary>
    /// Starts a new game with current settings
    /// </summary>
    public IEnumerator StartNewGame()
    {
        
        
        // Reset manager initialization flag for new game
        _managersInitialized = false;
        
        
        // Reset ResourceManager if it exists
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.ResetForNewGame();
        }

        
        
        // Multi-planet is always enabled; start multi-planet flow.
        yield return StartCoroutine(StartMultiPlanetGame());

        yield return null;
    }

    /// <summary>
    /// Start single planet game (original behavior)
    /// </summary>
    private IEnumerator StartSinglePlanetGame()
    {
        

        // --- CRITICAL: Refresh settings from GameSetupData ---
        
        selectedPlayerCivilizationData = GameSetupData.selectedPlayerCivilizationData;
        numberOfCivilizations = GameSetupData.numberOfCivilizations;
        numberOfCityStates = GameSetupData.numberOfCityStates;
        numberOfTribes = GameSetupData.numberOfTribes;
        mapSize = GameSetupData.mapSize;
        animalPrevalence = GameSetupData.animalPrevalence;
        generateMoon = GameSetupData.generateMoon;
        
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
                unitMovementController.SetReferences(grid, planetGenerator);
                
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

        

        // Generate minimap now that the planet is ready
        var minimapUI = FindAnyObjectByType<MinimapUI>();
        

        // Single-planet water mesh generation removed; multi-planet flow handles water generation.

        // Flat equirectangular surface map (presentation-only) — build it during loading so the game starts in surface view cleanly.
        // Uses HexMapChunkManager (chunk-based with seamless wrap)
        UpdateLoadingProgress(0.75f, "Building surface map...");
        var chunkManagerSingle = FindAnyObjectByType<HexMapChunkManager>(FindObjectsInactive.Include);
        
        if (chunkManagerSingle != null && planetGenerator != null)
        {
            chunkManagerSingle.Rebuild(planetGenerator);
            float oldFlatY = flatPlaneY;
            flatPlaneY = chunkManagerSingle.transform.position.y;
            Debug.Log($"[GameManager] flatPlaneY updated from ChunkManager (single): old={oldFlatY:F3} new={flatPlaneY:F3} chunkMgr='{chunkManagerSingle.gameObject.name}' pos={chunkManagerSingle.transform.position.ToString("F3")} rot={chunkManagerSingle.transform.rotation.eulerAngles.ToString("F1")}");
        }

        if (minimapUI != null)
        {
            
            UpdateLoadingProgress(0.8f, "Generating minimaps...");
            
            // Start minimap generation
            minimapUI.StartMinimapGeneration();
            
            
            // Wait for minimaps to be pre-generated
            while (!minimapUI.MinimapsPreGenerated)
            {
                yield return null;
            }
            
            
            UpdateLoadingProgress(0.9f, "Minimaps complete...");
        }
        else
        {
            Debug.LogWarning("[GameManager] MinimapUI component not found! Skipping minimap generation.");
            UpdateLoadingProgress(0.9f, "Minimap generation skipped...");
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

            // REMOVED: Music initialization moved to end of CivilizationManager.SpawnCivilizations()
            // This ensures all civs are fully spawned before music tracks are initialized
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

        

        // Initialize UI after civilizations are spawned
        yield return new WaitForEndOfFrame(); // Give everything a frame to settle
        
        InitializeUI();
        

        

        // Game is now ready
        OnGameStarted?.Invoke();

        // Start game music now that everything is loaded and the loading panel will be hidden
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayMusic();
        }

        

        yield return null;
    }

    /// <summary>
    /// Start multi-planet game
    /// </summary>
    private IEnumerator StartMultiPlanetGame()
    {
        

        // --- CRITICAL: Refresh settings from GameSetupData ---
        selectedPlayerCivilizationData = GameSetupData.selectedPlayerCivilizationData;
        numberOfCivilizations = GameSetupData.numberOfCivilizations;
        numberOfCityStates = GameSetupData.numberOfCityStates;
        numberOfTribes = GameSetupData.numberOfTribes;
        mapSize = GameSetupData.mapSize;
        animalPrevalence = GameSetupData.animalPrevalence;
        generateMoon = GameSetupData.generateMoon;
        // --- End Refresh ---
        
        // CRITICAL FIX: Create managers FIRST before planet generation
        // This ensures CivilizationManager and AnimalManager exist when spawn events fire
        FindCoreManagersInScene();
        
        // Initialize and generate all planets AFTER managers exist
        yield return StartCoroutine(InitializeMultiPlanetSystem());

        // FIXED: Always start with Earth (planet index 0) for civilization spawning
        // Do NOT use planetData.Keys.First() as it's unpredictable!
        if (planetData.ContainsKey(0))
        {
            currentPlanetIndex = 0; // Force Earth
            
        }
        else
        {
            Debug.LogError("[GameManager] Earth (planet index 0) not found in planetData! Cannot spawn civilizations.");
            if (planetData.Count > 0)
            {
                currentPlanetIndex = planetData.Keys.First();
                Debug.LogWarning($"[GameManager] Falling back to planet index {currentPlanetIndex}");
            }
        }

        // Set references on UnitMovementController now that planets and managers exist
        var unitMovementController = FindAnyObjectByType<UnitMovementController>();
        if (unitMovementController != null)
        {
            var currentPlanet = GetCurrentPlanetGenerator();
            if (currentPlanet != null)
            {
                var grid = currentPlanet.Grid;
                unitMovementController.SetReferences(grid, currentPlanet);
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

        

        // Trigger minimap generation now that planets are ready
        // Try multiple ways to find MinimapUI
        var minimapUI = FindAnyObjectByType<MinimapUI>();

        // Earth-only water mesh generation (multi-planet)
        var earthGen = GetPlanetGenerator(0);
        if (earthGen != null && earthGen.HasGeneratedSurface)
        {
            var waterGen = earthGen.GetComponentInChildren<WaterMeshGenerator>();
            if (waterGen != null)
            {
                waterGen.Generate(earthGen);
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] Earth surface not ready for water mesh generation (multi-planet mode)");
        }
        
        
        if (minimapUI == null)
        {
            // Try the newer first object method
            minimapUI = FindFirstObjectByType<MinimapUI>();
            
        }
        
        if (minimapUI == null)
        {
            // Try including inactive objects
            minimapUI = FindAnyObjectByType<MinimapUI>(FindObjectsInactive.Include);
            
        }
        
        
        
        // Debug: List all MinimapUI components in the scene
        var allMinimapUIs = FindObjectsByType<MinimapUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        
        if (allMinimapUIs != null)
        {
            for (int i = 0; i < allMinimapUIs.Length; i++)
            {
                var ui = allMinimapUIs[i];
                
            }
        }
        
        if (minimapUI != null)
        {
            
            
            // Since we generate planets sequentially and wait for each to complete,
            // all surfaces should be ready by this point
            UpdateLoadingProgress(0.70f, "Generating minimaps...");
            
            
            // If the UI is configured to bulk pre-generate, run and wait; otherwise, rely on event-driven generation
            if (minimapUI.PreGenerateAll)
            {
                minimapUI.StartMinimapGeneration();
                while (!minimapUI.MinimapsPreGenerated)
                    yield return null;
                UpdateLoadingProgress(0.80f, "Minimaps complete...");
            }
            else
            {
                // Event-driven mode: MinimapUI will generate textures per-planet as events fire; no blocking here
                UpdateLoadingProgress(0.80f, "Minimap generation deferred...");
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] (Multi-Planet) MinimapUI component not found! Skipping minimap generation.");
            UpdateLoadingProgress(0.80f, "Minimap generation skipped...");
        }

        // Flat equirectangular surface map (presentation-only) — build it during loading for the current planet.
        // Uses HexMapChunkManager (chunk-based with seamless wrap)
        UpdateLoadingProgress(0.82f, "Building surface map...");
        var chunkManagerMulti = FindAnyObjectByType<HexMapChunkManager>(FindObjectsInactive.Include);
        var genForFlat = GetCurrentPlanetGenerator();
        
        if (chunkManagerMulti != null && genForFlat != null)
        {
            chunkManagerMulti.Rebuild(genForFlat);
            float oldFlatY = flatPlaneY;
            flatPlaneY = chunkManagerMulti.transform.position.y;
            Debug.Log($"[GameManager] flatPlaneY updated from ChunkManager (multi): old={oldFlatY:F3} new={flatPlaneY:F3} chunkMgr='{chunkManagerMulti.gameObject.name}' pos={chunkManagerMulti.transform.position.ToString("F3")} rot={chunkManagerMulti.transform.rotation.eulerAngles.ToString("F1")}");
        }

        // Update loading progress - UI setup
        UpdateLoadingProgress(0.85f, "Setting up interface systems...");
        


        // SunBillboard removed in flat-only refactor

        

        // Update loading progress - UI initialization
        UpdateLoadingProgress(0.95f, "Initializing interface...");

        // Initialize UI after civilizations are spawned
        yield return new WaitForEndOfFrame(); // Give everything a frame to settle
        
        InitializeUI();
        

        

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

        
    }

    private void ApplyRealPlanetIdentity(PlanetGenerator g, string bodyName)
    {
        g.ClearRealPlanetFlags();
        g.allowOceans = true; g.enableRivers = true; g.allowIslands = true;

        switch (bodyName)
        {
            case "Earth":
                break;
            case "Mars":
                g.isMarsWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Venus":
                g.isVenusWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Mercury":
                g.isMercuryWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Jupiter":
                g.isJupiterWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Saturn":
                g.isSaturnWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Uranus":
                g.isUranusWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Neptune":
                g.isNeptuneWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Io":
                g.isIoWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Europa":
                g.isEuropaWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Ganymede":
                g.isGanymedeWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Callisto":
                g.isCallistoWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Titan":
                g.isTitanWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Luna":
                g.isLunaWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            case "Pluto":
                g.isPlutoWorldType = true;
                g.allowOceans = false; g.enableRivers = false; g.allowIslands = false;
                break;
            default:
                break;
        }
    }

    // NOTE: MoonGenerator has been removed. Moons are treated as regular PlanetGenerator bodies
    // (CelestialBodyType.Moon) within the multi-planet system.

    /// <summary>
    /// Initialize the multi-planet system with multiple planets
    /// </summary>
    private IEnumerator InitializeMultiPlanetSystem()
    {
        

        // Update loading progress - Starting multi-planet system
        UpdateLoadingProgress(0.05f, "Initializing solar system...");

        
        
        if (GameSetupData.systemPreset == GameSetupData.SystemPreset.RealSolarSystem || useRealSolarSystem)
        {
            realBodies = new List<string>
            {
                "Earth",
                "Luna",
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
                celestialBodyType = (name == "Luna" || name == "Io" || name == "Europa" || name == "Ganymede" || name == "Callisto" || name == "Titan")
                    ? CelestialBodyType.Moon
                    : CelestialBodyType.Planet,
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
            
            
            // Update loading progress for planet generation
            float planetProgress = 0.1f + (0.6f * i / totalPlanets); // 10% to 70% for planet generation
            string planetName = (GameSetupData.systemPreset == GameSetupData.SystemPreset.RealSolarSystem || useRealSolarSystem)
                ? realBodies[i] : $"Planet {i + 1}";
            UpdateLoadingProgress(planetProgress, $"Generating {planetName}...");
            
            Vector3 position = GetPlanetPosition(i, realBodies[i]);
            yield return StartCoroutine(GenerateMultiPlanet(i, position));
            
            
            // Extra yield to ensure everything is fully settled before next planet
            yield return new WaitForEndOfFrame();
            yield return null;
        }

        // Update loading progress - Planet generation complete
        UpdateLoadingProgress(0.70f, "Planet generation complete!");
        
        
        
        // Move spawning logic here - after all planets are generated but before game completion
        
        UpdateLoadingProgress(0.75f, "Spawning civilizations and animals...");
        yield return StartCoroutine(SpawnCivsAndAnimalsOnAllPlanets());
        
        // Now that spawning is complete, spawn resources
        UpdateLoadingProgress(0.85f, "Spawning strategic resources...");
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.SpawnResourcesWhenReady();
        }
    }

    /// <summary>
    /// Spawn civilizations and animals on all planets after generation is complete
    /// </summary>
    private IEnumerator SpawnCivsAndAnimalsOnAllPlanets()
    {
        
        
        // Spawn civilizations and animals only on Earth (planet 0)
        var earthPlanetGen = GetPlanetGenerator(0);
        if (earthPlanetGen != null && earthPlanetGen.HasGeneratedSurface)
        {
            
            // Ensure TileSystem is initialized to Earth before spawning
            if (TileSystem.Instance != null)
            {
                TileSystem.Instance.InitializeFromPlanet(earthPlanetGen);
            }
            
            // Spawn civilizations on Earth
            if (civilizationManager != null)
            {
                
                CivData playerCivData = GameSetupData.selectedPlayerCivilizationData;
                if (playerCivData == null)
                {
                    Debug.LogWarning("No player civilization selected in GameSetupData. Using default.");
                }
                
                civilizationManager.SpawnCivilizations(playerCivData, 4, 2, 2);
            }
            
            // Spawn animals on Earth (only once!)
            var animalManagerInstance = FindAnyObjectByType<AnimalManager>();
            if (animalManagerInstance != null)
            {
                
                animalManagerInstance.SpawnInitialAnimals();
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] Earth (planet 0) not ready for spawning");
        }
        
        
        yield break;
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
            case "Luna":
                return new Vector3(moonDistance, 0, 0); // Luna near Earth
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
            "Luna" => PlanetType.Barren,
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
            "Luna" or "Io" or "Europa" or "Ganymede" or "Callisto" or "Titan" => MapSize.Small,
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
            "Luna" => 1.0f, // Earth's distance
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
            "Luna" => 27.32f,
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
            "Luna" => -20f,
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
            "Luna" => "Earth's moon - an airless, cratered world of grey regolith and ancient impacts",
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
        generator.planetIndex = planetIndex;

        if (planetData.ContainsKey(planetIndex))
            planetData[planetIndex].planetName = body;

        // Only apply planet identity settings for non-Earth planets
        // Earth should keep its original prefab settings
        if (body != "Earth")
        {
            ApplyRealPlanetIdentity(generator, body);
        }
        else
        {
            
        }

        // Build grid using flat dimensions
        MapSize sizePreset = (body == "Earth") ? GameSetupData.mapSize : MapSize.Standard;
        GetFlatMapSizeParams(sizePreset, out float width, out float height);
        GetFlatTileResolution(sizePreset, out int tilesX, out int tilesZ);
        ApplyStampSettingsForMapSize(sizePreset);
        generator.Grid.GenerateFlatGrid(tilesX, tilesZ, width, height);
    // Notify grid built
    OnPlanetGridBuilt?.Invoke(planetIndex);

        // Only apply high-level GameSetupData overrides here. The `PlanetGenerator` prefab
        // should remain authoritative for tuning parameters and tile-range presets.
        generator.currentMapTypeName = GameSetupData.mapTypeName ?? "";
        // Biome logic (allowed to be influenced by presets)
        generator.moistureBias = GameSetupData.moistureBias;
        generator.temperatureBias = GameSetupData.temperatureBias;
        // Land counts (preset-driven)
        generator.numberOfContinents = GameSetupData.numberOfContinents;
        generator.numberOfIslands = GameSetupData.numberOfIslands;
        generator.generateIslands = GameSetupData.generateIslands;

        Debug.Log($"[GameManager][Diag] Multi-planet apply: continents={generator.numberOfContinents}, islands={generator.numberOfIslands}, generateIslands={generator.generateIslands} (preserving noise tuning on prefab)");

        // Rivers & lakes (allowed preset-driven settings)
        generator.enableRivers = GameSetupData.enableRivers;

        generator.enableLakes = GameSetupData.enableLakes;
        generator.numberOfLakes = GameSetupData.numberOfLakes;
        generator.lakeMinRadiusTiles = GameSetupData.lakeMinRadiusTiles;
        generator.lakeMaxRadiusTiles = GameSetupData.lakeMaxRadiusTiles;
        generator.lakeMinDistanceFromCoast = GameSetupData.lakeMinDistanceFromCoast;

    
    yield return StartCoroutine(generator.GenerateSurface());
    // NOTE: EnsureVisualsSpawned removed - new system uses texture-based rendering (FlatMapTextureRenderer)
    
    // CRITICAL FIX: Register the planet generator BEFORE firing events
    // This ensures the generator is available when spawn events fire
    planetGenerators[planetIndex] = generator;

    if (climateManagerPrefab != null)
    {
        var cmGO = Instantiate(climateManagerPrefab);
        cmGO.name = $"ClimateManager_Planet_{planetIndex}";
        cmGO.transform.SetParent(planetGO.transform, false);
        var cm = cmGO.GetComponent<ClimateManager>();
        if (cm != null)
        {
            cm.isGlobalClimateManager = false;
            cm.planetIndex = planetIndex;
            planetClimateManagers[planetIndex] = cm;
        }
    }

    // TileSystem will be bound to the active planet as needed
    
    // Now fire the surface generated event - spawning will find the registered generator
    
    OnPlanetSurfaceGenerated?.Invoke(planetIndex);

        // Planet generation complete - per-planet ClimateManager attached above
        

    // Managers attached/configured
    OnPlanetManagersAttached?.Invoke(planetIndex);

        // Extra safety yield to ensure everything is completely finished
        yield return new WaitForEndOfFrame();
        yield return null;

    // Planet fully generated (surface, biomes, rivers, and managers)
    OnPlanetFullyGenerated?.Invoke(generator);

    // Planet fully ready
    OnPlanetReady?.Invoke(planetIndex);
    
    }



    /// <summary>
    /// Switch to a different planet in multi-planet mode
    /// </summary>
    public IEnumerator SwitchToMultiPlanet(int planetIndex)
    {
        // Multi-planet is always enabled; legacy guard removed.

        if (!planetGenerators.ContainsKey(planetIndex))
        {
            Debug.LogWarning($"[GameManager] Planet {planetIndex} does not exist");
            yield break;
        }

        
        currentPlanetIndex = planetIndex;

        // Ensure grid is built and surface generated if needed
        var generator = planetGenerators[planetIndex];
        if (generator != null && !generator.Grid.IsBuilt)
        {
            var sizePreset = planetData[planetIndex].planetSize;
            GetFlatMapSizeParams(sizePreset, out float width, out float height);
            GetFlatTileResolution(sizePreset, out int tilesX, out int tilesZ);
            generator.Grid.GenerateFlatGrid(tilesX, tilesZ, width, height);
        }
        if (generator != null && generator.Grid.TileCount > 0 && !generator.HasGeneratedSurface)
        {
            
            yield return StartCoroutine(generator.GenerateSurface());
            // NOTE: EnsureVisualsSpawned removed - new system uses texture-based rendering (FlatMapTextureRenderer)
            OnPlanetFullyGenerated?.Invoke(generator);
        }

        // Update references in other systems
        // Rebind TileSystem to this planet
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.InitializeFromPlanet(generator);
        }

        
    }

    /// <summary>
    /// Handles map generation process
    /// </summary>
    private IEnumerator GenerateMap()
    {
        
        // Use GenerateSurface as a coroutine and wait for all map generation to finish
        yield return StartCoroutine(planetGenerator.GenerateSurface());
        // NOTE: EnsureVisualsSpawned removed - new system uses texture-based rendering (FlatMapTextureRenderer)

        // SunBillboard removed in flat-only refactor

        // Initialize TileSystem once the planet is generated
        if (TileSystem.Instance != null && planetGenerator != null)
        {
            TileSystem.Instance.InitializeFromPlanet(planetGenerator);
        }

        OnPlanetFullyGenerated?.Invoke(planetGenerator);
    


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
                
            }
            else
            {
                Debug.LogWarning("[GameManager] Space loading panel prefab does not have SpaceLoadingPanelController component");
            }
        }
    }

    /// <summary>
    /// Switch view to Earth's moon (Luna). Moons are treated as regular planets (CelestialBodyType.Moon).
    /// </summary>
    public void GoToEarthMoon()
    {
        // Find Luna body index
        int lunaIndex = -1;
            foreach (var kv in planetData)
            {
            if (string.Equals(kv.Value.planetName, "Luna", StringComparison.OrdinalIgnoreCase))
                {
                lunaIndex = kv.Key;
                    break;
                }
            }
        if (lunaIndex < 0)
            {
            Debug.LogWarning("[GameManager] Luna not found in planetData. Ensure the multi-planet system includes Luna.");
            return;
        }
        // Multi-planet always enabled; no runtime guard required.
        SetCurrentPlanet(lunaIndex);
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
        // Use cached reference for performance
        if (cachedLoadingPanel == null)
            cachedLoadingPanel = FindAnyObjectByType<LoadingPanelController>();
        if (cachedLoadingPanel != null)
        {
            cachedLoadingPanel.SetProgress(progress);
            cachedLoadingPanel.SetStatus(status);
        }
    }

    /// <summary>
    /// Hide the loading panel when game initialization is complete
    /// </summary>
    private void HideLoadingPanel()
    {
        // Use cached reference for performance
        if (cachedLoadingPanel == null)
            cachedLoadingPanel = FindAnyObjectByType<LoadingPanelController>();
        if (cachedLoadingPanel != null)
        {
            cachedLoadingPanel.HideLoading();
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
        
        
        // Clear object pools
        if (SimpleObjectPool.Instance != null)
        {
            SimpleObjectPool.Instance.ClearAllPools();
        }
        
        // Clear tile data caches
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.ClearAllCaches();
        }
        
        // AUDIO FIX: Clean up music manager resources
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.CleanupAudioResources();
        }
        
        // Clear planet/moon generator references
        planetGenerators.Clear();
        planetCivManagers.Clear();
        planetData.Clear();
        
        // Clear hex tiles data
        hexTiles.Clear();
        
        // Force garbage collection
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        
        
    }

    /// <summary>
    /// Save the current game state to a file
    /// </summary>
    public void SaveGame(string saveName)
    {
        try
        {
            // Create save data
            PauseMenuManager.GameSaveData saveData = new PauseMenuManager.GameSaveData
            {
                saveName = saveName,
                currentTurn = currentTurn,
                mapSize = mapSize,
                enableMultiPlanetSystem = enableMultiPlanetSystem,
                currentPlanetIndex = currentPlanetIndex,
                gameInProgress = gameInProgress,
                flatMapWidth = GetFlatMapWidth(),
                flatMapHeight = GetFlatMapHeight(),
            };
            
            // Get player civilization info
            if (civilizationManager != null && civilizationManager.playerCiv != null)
            {
                saveData.playerCivName = civilizationManager.playerCiv.civData.civName;
                var allCivs = civilizationManager.GetAllCivs();
                saveData.playerCivIndex = allCivs.IndexOf(civilizationManager.playerCiv);
            }
            
            // Get camera position/rotation
            if (Camera.main != null)
            {
                saveData.cameraPosition = Camera.main.transform.position;
                saveData.cameraRotation = Camera.main.transform.eulerAngles;
            }
            
            // Export improvement manager job assignments
            if (ImprovementManager.Instance != null)
            {
                saveData.jobAssignments = ImprovementManager.Instance.ExportJobAssignments();
            }
            
            // Override save name if provided
            if (!string.IsNullOrEmpty(saveName))
            {
                saveData.saveName = saveName;
            }
            
            // Create save directory if it doesn't exist
            string saveDirectory = System.IO.Path.Combine(Application.persistentDataPath, "Saves");
            if (!System.IO.Directory.Exists(saveDirectory))
            {
                System.IO.Directory.CreateDirectory(saveDirectory);
            }
            
            // Save to file
            string fileName = string.IsNullOrEmpty(saveName) ? $"save_{System.DateTime.Now:yyyyMMdd_HHmmss}.json" : $"{saveName}.json";
            string filePath = System.IO.Path.Combine(saveDirectory, fileName);
            string jsonData = JsonUtility.ToJson(saveData, true);
            System.IO.File.WriteAllText(filePath, jsonData);
// Show notification if UIManager is available
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"Game saved: {saveData.saveName}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GameManager] Failed to save game: {e.Message}");
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification("Failed to save game!");
            }
        }
    }

    /// <summary>
    /// Load a saved game from a file
    /// </summary>
    public void LoadGame(string saveName)
    {
        try
        {
            string saveDirectory = System.IO.Path.Combine(Application.persistentDataPath, "Saves");
            string fileName = string.IsNullOrEmpty(saveName) ? "save.json" : $"{saveName}.json";
            string filePath = System.IO.Path.Combine(saveDirectory, fileName);
            
            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogWarning($"[GameManager] Save file not found: {filePath}");
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.ShowNotification($"Save file not found: {saveName}");
                }
                return;
            }
            
            // Read and parse save data
            string jsonData = System.IO.File.ReadAllText(filePath);
            PauseMenuManager.GameSaveData saveData = JsonUtility.FromJson<PauseMenuManager.GameSaveData>(jsonData);
            
            if (saveData == null)
            {
                Debug.LogError("[GameManager] Failed to parse save data");
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.ShowNotification("Failed to load game: corrupted save data");
                }
                return;
            }
            
            // Use existing LoadGameFromSaveData method
            LoadGameFromSaveData(saveData);
// Show notification if UIManager is available
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"Game loaded: {saveData.saveName}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GameManager] Failed to load game: {e.Message}");
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification("Failed to load game!");
            }
        }
    }

    /// <summary>
    /// Apply a loaded GameSaveData to the current runtime. This method will orchestrate
    /// initialization of core managers and then apply the save fields, finally importing
    /// improvement manager job assignments after units are registered.
    /// </summary>
    public void LoadGameFromSaveData(PauseMenuManager.GameSaveData saveData)
    {
        StartCoroutine(LoadGameFromSaveDataRoutine(saveData));
    }

    private System.Collections.IEnumerator LoadGameFromSaveDataRoutine(PauseMenuManager.GameSaveData saveData)
    {
        

        // Basic fields
        currentTurn = saveData.currentTurn;
        gameInProgress = saveData.gameInProgress;
        mapSize = saveData.mapSize;
        // Restore flat map dimensions (fallback to preset if missing)
        if (saveData.flatMapWidth > 0f && saveData.flatMapHeight > 0f)
        {
            flatMapWidth = saveData.flatMapWidth;
            flatMapHeight = saveData.flatMapHeight;
}
        else
        {
            SetFlatMapDimensionsFromSize(mapSize);
        }
        enableMultiPlanetSystem = saveData.enableMultiPlanetSystem;
        currentPlanetIndex = saveData.currentPlanetIndex;

        // Apply camera transform after scene objects exist
        yield return null; // wait a frame
        if (Camera.main != null)
        {
            Camera.main.transform.position = saveData.cameraPosition;
            Camera.main.transform.eulerAngles = saveData.cameraRotation;
        }

        // Ensure core managers are present
        FindCoreManagersInScene();

        // Wait a frame so that managers/units created in FindCoreManagersInScene have Awake/Start called
        yield return null;

        // If CivilizationManager needs to restore player civ index, attempt to do so
        try
        {
            if (CivilizationManager.Instance != null && saveData.playerCivIndex >= 0)
            {
                var allCivs = CivilizationManager.Instance.GetAllCivs();
                if (saveData.playerCivIndex < allCivs.Count)
                    CivilizationManager.Instance.playerCiv = allCivs[saveData.playerCivIndex];
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to restore player civ index: {e.Message}");
        }

        // Import improvement manager assignments AFTER units are present and registered
        if (saveData.jobAssignments != null && saveData.jobAssignments.Count > 0)
        {
            // Allow a small delay for UnitRegistry to populate (in case units are spawned next frame)
            yield return null;
            try
            {
                ImprovementManager.Instance?.ImportJobAssignments(saveData.jobAssignments);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to import job assignments: {e.Message}");
            }
        }

        
    }

    // --- Global UI Audio System ---
    
    /// <summary>
    /// Initialize the global UI audio system that works across all scenes
    /// </summary>
    private void SetupGlobalUIAudio()
    {
        // Ensure we have an AudioSource for UI sounds
        uiAudioSource = GetComponent<AudioSource>();
        if (uiAudioSource == null)
            uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;
        uiAudioSource.spatialBlend = 0f; // 2D sound

        // Wire click sounds for all buttons in the current scene
        WireAllButtonsInScene();
        
        // Subscribe to scene loaded events to wire buttons in new scenes
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// Called when a new scene is loaded to wire up UI audio
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Wire buttons in the newly loaded scene
        StartCoroutine(WireButtonsAfterFrameDelay());
    }

    /// <summary>
    /// Wait a frame then wire buttons (ensures UI is fully initialized)
    /// </summary>
    private System.Collections.IEnumerator WireButtonsAfterFrameDelay()
    {
        yield return null; // Wait one frame
        WireAllButtonsInScene();
    }

    /// <summary>
    /// Find and wire all buttons in the current scene for click audio
    /// </summary>
    private void WireAllButtonsInScene()
    {
        var buttons = FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None);
        foreach (var button in buttons)
        {
            WireButton(button);
        }
        
    }

    /// <summary>
    /// Wire a single button for click audio if not already wired
    /// </summary>
    private void WireButton(UnityEngine.UI.Button button)
    {
        if (button == null || wiredButtons.Contains(button)) return;

        button.onClick.AddListener(PlayUIClick);
        wiredButtons.Add(button);
    }

    /// <summary>
    /// Play the UI click sound
    /// </summary>
    public void PlayUIClick()
    {
        if (uiClickClip != null && uiAudioSource != null)
        {
            uiAudioSource.PlayOneShot(uiClickClip, uiClickVolume);
        }
    }

    /// <summary>
    /// Public method for manually wiring buttons (useful for dynamically created UI)
    /// </summary>
    public void WireButtonForAudio(UnityEngine.UI.Button button)
    {
        WireButton(button);
    }

    /// <summary>
    /// Start a battle test for quick testing of battle features
    /// </summary>
    [ContextMenu("Start Battle Test")]
    public void StartBattleTest()
    {
// Create test civilizations
        Civilization attackerCiv = CreateTestCivilization("Test Attacker", true);
        Civilization defenderCiv = CreateTestCivilization("Test Defender", false);

        // Get some test units (use first available unit data)
        var allUnitData = ResourceCache.GetAllCombatUnits();
        CombatUnitData testUnitData = allUnitData.Length > 0 ? allUnitData[0] : null;

        if (testUnitData == null)
        {
            Debug.LogError("[GameManager] No unit data found for battle test!");
            return;
        }

        // Spawn test units
        List<CombatUnit> attackerUnits = SpawnTestUnits(attackerCiv, testUnitData, 3, true);
        List<CombatUnit> defenderUnits = SpawnTestUnits(defenderCiv, testUnitData, 3, false);

        // Start battle
        if (BattleTestSimple.Instance != null)
        {
            BattleTestSimple.Instance.StartBattle(attackerCiv, defenderCiv, attackerUnits, defenderUnits);
        }
        else
        {
            Debug.LogError("[GameManager] BattleManager not found! Make sure BattleManager is in the scene.");
        }
    }

    /// <summary>
    /// Create a test civilization for battle testing
    /// </summary>
    private Civilization CreateTestCivilization(string name, bool isAttacker)
    {
        GameObject civGO = new GameObject(name);
        Civilization civ = civGO.AddComponent<Civilization>();
        
        CivData civData = ScriptableObject.CreateInstance<CivData>();
        civData.civName = name;
        civ.Initialize(civData, null, false);
        
        return civ;
    }

    /// <summary>
    /// Spawn test units for battle testing
    /// </summary>
    private List<CombatUnit> SpawnTestUnits(Civilization civ, CombatUnitData unitData, int count, bool isAttacker)
    {
        List<CombatUnit> units = new List<CombatUnit>();
        Vector3 formationCenter = isAttacker ? new Vector3(-5, 0, 0) : new Vector3(5, 0, 0);

        for (int i = 0; i < count; i++)
        {
            int row = i / 3;
            int col = i % 3;
            Vector3 offset = new Vector3((col - 1) * 2f, 0, row * 2f);
            Vector3 unitPosition = formationCenter + offset;

            var unitPrefab = unitData.GetPrefab();
            if (unitPrefab == null)
            {
                Debug.LogError($"[GameManager] Cannot spawn unit {unitData.unitName}: prefab not found in Addressables. Make sure prefab is marked as Addressable with address matching unitName.");
                continue; // Skip this unit
            }
            
            GameObject unitGO = Instantiate(unitPrefab, unitPosition, Quaternion.identity);
            CombatUnit unit = unitGO.GetComponent<CombatUnit>();
            if (unit == null)
            {
                Debug.LogError($"[GameManager] Spawned prefab for {unitData.unitName} is missing CombatUnit component.");
                Destroy(unitGO);
                continue; // Skip this unit
            }
            
                unit.Initialize(unitData, civ);
                unit.InitializeForBattle(isAttacker);
                units.Add(unit);
        }

        return units;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /* DEPRECATED: Old spawning logic replaced by SpawnCivsAndAnimalsOnAllPlanets()
    // =================== Event-driven spawn gating ===================
    private void EnsureSpawnAfterEarthReady()
    {
// Check if Earth is already ready (might happen if called after generation)
        var earth = GetPlanetGenerator(0);
        if (earth != null && earth.HasGeneratedSurface)
        {
if (!_spawnedCivsAndAnimals)
                StartCoroutine(SpawnCivsAndAnimals());
            return;
        }

        // Set up event listener for when Earth surface is generated
void OnEarthSurface(int idx)
        {
if (idx != 0) 
            {
return; // Only care about Earth (index 0)
            }
OnPlanetSurfaceGenerated -= OnEarthSurface; // Unsubscribe to prevent multiple calls
            if (!_spawnedCivsAndAnimals)
            {
StartCoroutine(SpawnCivsAndAnimals());
            }
            else
            {
}
        }
        OnPlanetSurfaceGenerated += OnEarthSurface;
        
        // FALLBACK: Also start a polling coroutine in case the event doesn't fire
        StartCoroutine(SpawnWhenEarthReadyPolling());
    }

    private System.Collections.IEnumerator SpawnWhenEarthReadyPolling()
    {
var earth = GetPlanetGenerator(0);
        int maxWaitFrames = 600; // 10 seconds at 60fps
        int waitFrames = 0;
        
        while ((earth == null || !earth.HasGeneratedSurface) && waitFrames < maxWaitFrames)
        {
            earth = GetPlanetGenerator(0);
            waitFrames++;
            yield return null;
        }
        
        if (waitFrames >= maxWaitFrames)
        {
            Debug.LogWarning("[GameManager] Timeout waiting for Earth surface generation in polling fallback");
        }
        
        if (!_spawnedCivsAndAnimals)
        {
yield return StartCoroutine(SpawnCivsAndAnimals());
        }
        else
        {
}
    }

    private System.Collections.IEnumerator SpawnCivsAndAnimals()
    {
_spawnedCivsAndAnimals = true;

        if (enableMultiPlanetSystem && currentPlanetIndex != 0)
        {
            Debug.LogWarning("[GameManager] Forcing Earth (0) context before spawning");
            currentPlanetIndex = 0;
        }
        // Civs
        UpdateLoadingProgress(0.75f, "Spawning civilizations...");
if (civilizationManager != null)
        {
CivData playerCivData = GameSetupData.selectedPlayerCivilizationData;
            if (playerCivData == null)
                Debug.LogWarning("No player civilization selected in GameSetupData. Using default.");

            civilizationManager.SpawnCivilizations(
                playerCivData,
                numberOfCivilizations,
                numberOfCityStates,
                numberOfTribes);

            if (MusicManager.Instance != null)
                MusicManager.Instance.InitializeMusicTracks();
        }
        else
        {
            Debug.LogError("CivilizationManager not found. Can't spawn civilizations.");
        }

        // Animals
        UpdateLoadingProgress(0.85f, "Spawning wildlife...");
var animalManagerInstance = FindAnyObjectByType<AnimalManager>();
if (animalManagerInstance != null)
        {
            animalManagerInstance.SpawnInitialAnimals();
}
        else
        {
            Debug.LogWarning("GameManager: AnimalManager not found, cannot spawn initial animals.");
        }

        yield break;
    }
    */ // End DEPRECATED spawning logic
}
