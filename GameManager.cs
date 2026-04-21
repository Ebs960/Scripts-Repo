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
    [Header("Diagnostics")]
    [Tooltip("When enabled, diagnostics from systems will only run for the first created planet (planet index 0). Disable to allow diagnostics on all planets.")]
    public bool restrictDiagnosticsToFirstPlanet = true;
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

        int previousPlanetIndex = currentPlanetIndex;
        currentPlanetIndex = planetIndex;
        climateManager = GetClimateManager(currentPlanetIndex);

        // Invalidate WorldPicker cache so stale tile indices from the old LUT are not returned
        var worldPicker = FindAnyObjectByType<WorldPicker>();
        if (worldPicker != null) worldPicker.InvalidateCache();

        // Per-planet TileSystems: do NOT reinitialize tile state on switch.
        // Ensure the destination planet has a TileSystem instance (created during generation).
        var gen = GetPlanetGenerator(currentPlanetIndex);
        if (gen != null)
        {
            // Activate the new planet's GameObject (and all children: water, resources, units, etc.)
            gen.gameObject.SetActive(true);
            EnsureTileSystemForPlanet(gen);
        }

        // Deactivate the previous planet's GameObject (if switching to a different planet)
        if (previousPlanetIndex != planetIndex && planetGenerators.ContainsKey(previousPlanetIndex))
        {
            var prevGen = planetGenerators[previousPlanetIndex];
            if (prevGen != null)
            {
                prevGen.gameObject.SetActive(false);
                Debug.Log($"[GameManager] Deactivated planet {previousPlanetIndex} GameObject on switch to planet {planetIndex}");
            }
        }
    }

    /// <summary>
    /// Ensure a per-planet TileSystem exists and is initialized for a generated planet.
    /// This is required for true multi-planet gameplay (per-planet ownership/fog/occupancy).
    /// </summary>
    private TileSystem EnsureTileSystemForPlanet(PlanetGenerator generator)
    {
        if (generator == null) return null;
        int idx = generator.planetIndex;
        var existing = TileSystem.GetForPlanet(idx);
        if (existing != null) return existing;

        var go = new GameObject($"TileSystem_Planet_{idx}");
        // Parent under the planet for organization; TileSystem input is gated to currentPlanetIndex.
        go.transform.SetParent(generator.transform, false);
        var ts = go.AddComponent<TileSystem>();
        ts.planetIndex = idx;
        ts.InitializeFromPlanet(generator);
        return ts;
    }

    private System.Collections.IEnumerator WaitUntilTileSystemReadyAndSpawn(int planetIndex, AnimalManager animalManager)
    {
        if (animalManager == null) yield break;
        // Wait until TileSystem for the given planet exists and reports ready.
        while (true)
        {
            var ts = TileSystem.GetForPlanet(planetIndex);
            if (ts != null && ts.IsReady()) break;
            yield return null;
        }
        animalManager.SpawnInitialAnimalsOnPlanet(planetIndex);
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
        Tropical,
        Tundra
    }

    // Explicit planet layer types used for gameplay and generation
    public enum PlanetLayerType
    {
        Surface,
        Underwater,
        Mantle,
        Atmosphere,
        Orbit
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
        // Explicit per-planet supported layers (controls generation and gameplay systems)
        public List<PlanetLayerConfig> supportedLayers = new List<PlanetLayerConfig>();
        
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
            supportedLayers = new List<PlanetLayerConfig>();
        }
    }

    // Per-layer configuration describing which layers exist and which are playable
    [System.Serializable]
    public class PlanetLayerConfig
    {
        public PlanetLayerType layerType;
        public bool hasTiles = false; // whether this layer has a tile grid
        public bool isPlayable = false; // whether players/units can occupy this layer
    }

    // Events
    public event Action OnGameStarted;
    public event Action<bool> OnGamePaused;
    public event Action OnGameEnded;

    // Manager references
    public TurnManager turnManager;
    [Header("Planet Configs")]
    [Tooltip("Optional per-planet ScriptableObject configs. If provided, the matching config's supportedLayers will be copied into runtime PlanetData by name.")]
    public PlanetConfig[] planetConfigs = new PlanetConfig[0];

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

    public HexGrid planetGrid;

    public List<HexTileData> hexTiles = new List<HexTileData>();

    [Header("Flat Map Size (Flat-Only)")]
    [Tooltip("Flat map width in world units (X extent)." )]
    public float flatMapWidth = 512f;
    [Tooltip("Flat map height in world units (Z extent)." )]
    public float flatMapHeight = 256f;
    [Tooltip("Y height of the flat map plane (used for world placement)." )]
    public float flatPlaneY = 0f;


    // Flat-map-only: tile resolution by size preset
    public static void GetFlatTileResolution(MapSize size, out int tilesX, out int tilesZ)
    {
        switch (size)
        {
            case MapSize.Small: tilesX = 96; tilesZ = 48; break;
            case MapSize.Standard: tilesX = 120; tilesZ = 69; break;
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
            case MapSize.Standard: width = 600f; height = 300f; break;
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

        // Apply continent size multiplier from land preset (e.g. Pangaea = 3.5x)
        float sizeMul = GameSetupData.continentSizeMultiplier;
        continentMinW = Mathf.RoundToInt(continentMinW * sizeMul);
        continentMaxW = Mathf.RoundToInt(continentMaxW * sizeMul);
        continentMinH = Mathf.RoundToInt(continentMinH * sizeMul);
        continentMaxH = Mathf.RoundToInt(continentMaxH * sizeMul);

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

        // Initialize space loading panel early so it's ready for planet switches
        InitializeSpaceLoadingPanel();
        
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
        PlanetTextureBaker.ClearAllCaches();
        BiomeVisualDatabase.ClearAllCachedSurfaceLibraries();
        
        // Clear ResourceCache to free ScriptableObject references
        ResourceCache.Clear();
        
        // Clear TileSystem caches if it exists
        // Multi-planet: clear caches for all per-planet TileSystems
        if (planetGenerators != null && planetGenerators.Count > 0)
        {
            foreach (var kv in planetGenerators)
            {
                var ts = TileSystem.GetForPlanet(kv.Key);
                ts?.ClearAllCaches();
            }
        }
        else
        {
            TileSystem.Instance?.ClearAllCaches();
        }
        
        // Reset InputManager priority so it doesn't stay stuck at Modal/UI across scene loads
        if (InputManager.Instance != null)
        {
            InputManager.Instance.SetPriority(InputManager.InputPriority.Background);
            InputManager.Instance.SetInputEnabled(true);
        }

        // Request garbage collection to free memory immediately
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
    }

    private void OnApplicationQuit()
    {
        // Final cleanup on application quit
        PlanetTextureBaker.ClearAllCaches();
        BiomeVisualDatabase.ClearAllCachedSurfaceLibraries();
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
        public MinimapUI minimapUI;
        public HexMapChunkManager hexMapChunkManager;
    }
    
    // Persistent cache of scene references to avoid repeated FindAnyObjectByType calls during loading
    private ManagerCache _cachedManagers;

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
                case MinimapUI mui: cache.minimapUI = mui; break;
                case HexMapChunkManager hmc: cache.hexMapChunkManager = hmc; break;
            }
        }
        
        // Store in persistent instance cache for later use
        _cachedManagers = cache;
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

        // TileSystems are now per-planet and are created/initialized during planet generation.
        // Do not create a global TileSystem singleton here.

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
        // Ensure the persistent cache and public reference reflect the created/found manager
        _cachedManagers.animalManager = animalManager;

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

            // If we have a config for Earth, assign it to the editor/preview generator
            if (planetGenerator != null && planetConfigs != null && planetConfigs.Length > 0)
            {
                try
                {
                    var cfg = planetConfigs.FirstOrDefault(c => c != null && c.planetName == "Earth");
                    if (cfg != null)
                        planetGenerator.planetConfig = cfg;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GameManager] Could not assign Earth PlanetConfig to preview generator: {ex.Message}");
                }
            }


            // Assign the loading panel controller if present (use cached reference)
            if (cachedLoadingPanel == null)
                cachedLoadingPanel = _cachedManagers.loadingPanelController;
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
            planetGenerator.ApplyTerrainPreset(GameSetupData.selectedTerrainPreset);

            ApplyStampSettingsForMapSize(GameSetupData.mapSize);

            // Moisture and temperature settings
            planetGenerator.moistureBias = GameSetupData.moistureBias;
            planetGenerator.temperatureBias = GameSetupData.temperatureBias;

            // Land generation settings (allowed overrides only)
            planetGenerator.numberOfContinents = GameSetupData.numberOfContinents;
            planetGenerator.numberOfIslands = GameSetupData.numberOfIslands;
            planetGenerator.generateIslands = GameSetupData.generateIslands;
            
            // Water features must be gated by Underwater layer support.
            // If the planet has no authoritative layer config/data (legacy), preserve legacy behavior (allow).
            bool hasLayerAuthority = (planetGenerator.planetConfig != null &&
                                     planetGenerator.planetConfig.supportedLayers != null &&
                                     planetGenerator.planetConfig.supportedLayers.Count > 0);
            if (!hasLayerAuthority)
            {
                try
                {
                    var pd = GetPlanetData();
                    if (pd != null && pd.ContainsKey(planetGenerator.planetIndex) &&
                        pd[planetGenerator.planetIndex] != null &&
                        pd[planetGenerator.planetIndex].supportedLayers != null &&
                        pd[planetGenerator.planetIndex].supportedLayers.Count > 0)
                    {
                        hasLayerAuthority = true;
                    }
                }
                catch { /* ignore */ }
            }
            bool supportsUnderwater = hasLayerAuthority
                ? planetGenerator.HasLayer(PlanetLayerType.Underwater)
                : true;

            planetGenerator.enableRivers = supportsUnderwater && GameSetupData.enableRivers;
            planetGenerator.enableLakes  = supportsUnderwater && GameSetupData.enableLakes;
            planetGenerator.numberOfLakes = supportsUnderwater ? GameSetupData.numberOfLakes : 0;
            planetGenerator.lakeMinRadiusTiles = GameSetupData.lakeMinRadiusTiles;
            planetGenerator.lakeMaxRadiusTiles = GameSetupData.lakeMaxRadiusTiles;
            planetGenerator.lakeMinDistanceFromCoast = GameSetupData.lakeMinDistanceFromCoast;

            // DIAGNOSTIC: log what we applied to the generator (tile-based ranges)
            Debug.Log($"[GameManager][Diag] Applied GameSetupData to PlanetGenerator: continents={planetGenerator.numberOfContinents}, islands={planetGenerator.numberOfIslands}, generateIslands={planetGenerator.generateIslands}");

            // Terrain preset applied above via ApplyTerrainPreset; other tuning preserved from prefab.

            // Ensure island/rivers/lakes flags and counts come from GameSetupData
            planetGenerator.generateIslands = GameSetupData.generateIslands;
            planetGenerator.numberOfIslands = GameSetupData.numberOfIslands;
            // Water features already gated above; don't overwrite here.
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
        // Use override prefab if provided, otherwise use the field
        GameObject prefabToUse = loadingPanelPrefabOverride ?? loadingPanelPrefab;
        
        // Spawn loading panel IMMEDIATELY (before any yield) so UI is hidden from frame 1
        if (prefabToUse != null && cachedLoadingPanel == null)
        {
            GameObject loadingPanelInstance = Instantiate(prefabToUse);
            loadingPanelInstance.SetActive(true);
            cachedLoadingPanel = loadingPanelInstance.GetComponent<LoadingPanelController>();
        }

        // Now wait a frame to let Awake() run everywhere else
        yield return null;

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
        // Uses cached reference from CacheAllManagerReferences() instead of expensive scene search
        var unitMovementController = _cachedManagers.unitMovementController;
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
        // Uses cached reference from CacheAllManagerReferences() instead of expensive scene search
        var minimapUI = _cachedManagers.minimapUI;
        if (minimapUI == null) minimapUI = FindAnyObjectByType<MinimapUI>(FindObjectsInactive.Include);

        // Single-planet water mesh generation removed; multi-planet flow handles water generation.

        // REMOVED: Explicit Rebuild() call here was redundant.
        // HexMapChunkManager.HandlePlanetReady (subscribed to OnPlanetReady) already calls BuildChunks()
        // for the current planet. Calling Rebuild() here caused BuildChunks to run TWICE.
        UpdateLoadingProgress(0.75f, "Building surface map...");
        var chunkManagerSingle = _cachedManagers.hexMapChunkManager;
        if (chunkManagerSingle != null)
        {
            float oldFlatY = flatPlaneY;
            flatPlaneY = chunkManagerSingle.transform.position.y;
            Debug.Log($"[GameManager] flatPlaneY updated from ChunkManager (single): old={oldFlatY:F3} new={flatPlaneY:F3} chunkMgr='{chunkManagerSingle.gameObject.name}' pos={chunkManagerSingle.transform.position.ToString("F3")} rot={chunkManagerSingle.transform.rotation.eulerAngles.ToString("F1")}");
        }

        if (minimapUI != null)
        {
            UpdateLoadingProgress(0.8f, "Generating minimaps...");

            // Start minimap generation and time it
            Debug.Log("[GameManager] Starting minimap generation...");
            float mmStart = Time.realtimeSinceStartup;
            minimapUI.StartMinimapGeneration();

            // Wait for minimaps to be pre-generated
            while (!minimapUI.MinimapsPreGenerated)
            {
                yield return null;
            }
            float mmElapsed = Time.realtimeSinceStartup - mmStart;
            Debug.Log($"[GameManager] Minimap generation completed in {mmElapsed:F3}s");

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
        }
        else
        {
            Debug.LogError("CivilizationManager not found. Can't spawn civilizations.");
        }

        // Spawn initial animals — uses cached reference. Wait for TileSystem if necessary.
        var animalManagerInstance = _cachedManagers.animalManager;
        if (animalManagerInstance != null)
        {
            if (planetGenerator != null)
            {
                int pIndex = planetGenerator.planetIndex;
                var ts = TileSystem.GetForPlanet(pIndex);
                if (ts == null || !ts.IsReady())
                {
                    StartCoroutine(WaitUntilTileSystemReadyAndSpawn(pIndex, animalManagerInstance));
                }
                else
                {
                    animalManagerInstance.SpawnInitialAnimals();
                }
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
        

        

        // Hide loading panel before startup audio/events so generation is fully finished first.
        HideLoadingPanel();

        // Game is now ready
        OnGameStarted?.Invoke();

        // Start game music now that everything is loaded and the loading panel is hidden
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

        // Set references on UnitMovementController — uses cached reference
        var unitMovementController = _cachedManagers.unitMovementController;
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
        // Uses cached reference; falls back to scene search only if cache missed
        var minimapUI = _cachedManagers.minimapUI;
        if (minimapUI == null) minimapUI = FindAnyObjectByType<MinimapUI>(FindObjectsInactive.Include);


        
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

        // REMOVED: Explicit Rebuild() call here was redundant.
        // HexMapChunkManager.HandlePlanetReady (subscribed to OnPlanetReady) already calls BuildChunks()
        // for the current planet. Calling Rebuild() here caused BuildChunks to run TWICE — wasting the
        // entire first coroutine (batched LUT, biome maps, heightmap, meshes) and doubling init time.
        // The flatPlaneY update now uses the cached chunk manager reference.
        UpdateLoadingProgress(0.82f, "Building surface map...");
        var chunkManagerMulti = _cachedManagers.hexMapChunkManager;
        if (chunkManagerMulti != null)
        {
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

        // CRITICAL: Hide loading panel now that game is ready
        HideLoadingPanel();

        // Game is now ready
        OnGameStarted?.Invoke();

        // Start game music now that everything is loaded and the loading panel is hidden
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayMusic();
        }

        
    }

    private void ApplyRealPlanetIdentity(PlanetGenerator g, string bodyName)
    {
        g.planetType = global::PlanetType.Earth;
        g.allowOceans = true; g.enableRivers = true; g.allowIslands = true;

        // Parse planet type from body name
        if (System.Enum.TryParse<global::PlanetType>(bodyName, out var parsedType))
        {
            g.planetType = parsedType;
        }

        // Non-Earth planets don't have oceans, rivers, or islands
        if (g.planetType != global::PlanetType.Earth)
        {
            g.allowOceans = false;
            g.enableRivers = false;
            g.allowIslands = false;
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
                "Europa", "Titan"
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
                celestialBodyType = (name == "Luna" || name == "Europa" || name == "Titan")
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

            // If a matching PlanetConfig ScriptableObject exists (by name), copy its supported layers
            if (planetConfigs != null && planetConfigs.Length > 0)
            {
                try
                {
                    var cfg = planetConfigs.FirstOrDefault(c => c != null && c.planetName == planet.planetName);
                    if (cfg != null)
                    {
                        // Map authoritative cfg.supportedLayers (enum list) into runtime PlanetLayerConfig entries
                        planet.supportedLayers = new List<PlanetLayerConfig>();
                        if (cfg.supportedLayers != null)
                        {
                            foreach (var layer in cfg.supportedLayers)
                            {
                                var plc = new PlanetLayerConfig
                                {
                                    layerType = layer,
                                    hasTiles = (layer == PlanetLayerType.Surface || layer == PlanetLayerType.Underwater),
                                    isPlayable = (layer == PlanetLayerType.Surface)
                                };
                                planet.supportedLayers.Add(plc);
                            }
                        }

                        // Optional convenience: set hasAtmosphere flag from layers
                        planet.hasAtmosphere = planet.supportedLayers.Exists(l => l.layerType == PlanetLayerType.Atmosphere);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[GameManager] Failed to apply PlanetConfig for {planet.planetName}: {ex.Message}");
                }
            }

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
            
            // Ensure Earth has a per-planet TileSystem before spawning.
            EnsureTileSystemForPlanet(earthPlanetGen);
            
            // Spawn civilizations on Earth
            if (civilizationManager != null)
            {
                
                CivData playerCivData = GameSetupData.selectedPlayerCivilizationData;
                if (playerCivData == null)
                {
                    Debug.LogWarning("No player civilization selected in GameSetupData. Using default.");
                }
                
                civilizationManager.SpawnCivilizations(
                    playerCivData,
                    numberOfCivilizations,
                    numberOfCityStates,
                    numberOfTribes);
            }
            
            // Spawn animals on Earth (only once!) — uses cached reference. Wait for TileSystem readiness.
            var animalManagerInstance = _cachedManagers.animalManager;
            if (animalManagerInstance != null)
            {
                int pIndex = 0;
                var ts = TileSystem.GetForPlanet(pIndex);
                if (ts == null || !ts.IsReady())
                {
                    StartCoroutine(WaitUntilTileSystemReadyAndSpawn(pIndex, animalManagerInstance));
                }
                else
                {
                    animalManagerInstance.SpawnInitialAnimalsOnPlanet(pIndex);
                }
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
            case "Europa":
                return new Vector3(baseSpacing * 2 - moonDistance, 0, moonDistance);
                
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
            "Europa" => PlanetType.Ice,
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
            "Earth" => MapSize.Standard,
            "Mars" => MapSize.Standard,
            "Venus" => MapSize.Standard,
            "Mercury" => MapSize.Small,
            "Jupiter" => MapSize.Large,
            "Saturn" => MapSize.Large,
            "Uranus" => MapSize.Standard,
            "Neptune" => MapSize.Standard,
            "Pluto" => MapSize.Small,
            // Moons are generally smaller
            "Luna" or "Europa" or "Titan" => MapSize.Small,
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
            "Europa" => 5.2f, // Jupiter's distance
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
            "Europa" => 3.55f,
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
            "Europa" => -160f,
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
            
            "Europa" => "Jupiter's ice moon - hiding a subsurface ocean beneath its frozen crust",
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

        // Assign authoritative PlanetConfig (if available) to the generated PlanetGenerator
        if (planetConfigs != null && planetConfigs.Length > 0)
        {
            try
            {
                var cfg = planetConfigs.FirstOrDefault(c => c != null && c.planetName == body);
                if (cfg != null)
                {
                    generator.planetConfig = cfg;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameManager] Failed to assign PlanetConfig for {body}: {ex.Message}");
            }
        }

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

        // Apply GameSetupData overrides. The prefab's Inspector values serve as the
        // "Standard" baseline; ApplyTerrainPreset overwrites only the elevation knobs
        // (exponent, maxElev, hill/mountain thresholds, ridgeStrength) to match the
        // player's terrain-roughness dropdown selection.
        generator.SetMapTypeName(GameSetupData.mapTypeName ?? "");
        generator.ApplyTerrainPreset(GameSetupData.selectedTerrainPreset);
        // Biome logic (allowed to be influenced by presets)
        generator.moistureBias = GameSetupData.moistureBias;
        generator.temperatureBias = GameSetupData.temperatureBias;
        // Land counts (preset-driven)
        generator.numberOfContinents = GameSetupData.numberOfContinents;
        generator.numberOfIslands = GameSetupData.numberOfIslands;
        generator.generateIslands = GameSetupData.generateIslands;

        // Rivers & lakes (allowed preset-driven settings)
        // Water features must be gated by Underwater layer support.
        // PlanetConfig is assigned above when available, so HasLayer() is authoritative here.
        bool hasLayerAuthority2 = (generator.planetConfig != null &&
                                  generator.planetConfig.supportedLayers != null &&
                                  generator.planetConfig.supportedLayers.Count > 0);
        if (!hasLayerAuthority2)
        {
            try
            {
                var allPd = GetPlanetData();
                if (allPd != null && allPd.ContainsKey(generator.planetIndex) &&
                    allPd[generator.planetIndex] != null &&
                    allPd[generator.planetIndex].supportedLayers != null &&
                    allPd[generator.planetIndex].supportedLayers.Count > 0)
                {
                    hasLayerAuthority2 = true;
                }
            }
            catch { /* ignore */ }
        }
        bool supportsUnderwater2 = hasLayerAuthority2
            ? generator.HasLayer(PlanetLayerType.Underwater)
            : true;

        generator.enableRivers = supportsUnderwater2 && GameSetupData.enableRivers;
        generator.enableLakes  = supportsUnderwater2 && GameSetupData.enableLakes;
        generator.numberOfLakes = supportsUnderwater2 ? GameSetupData.numberOfLakes : 0;
        generator.lakeMinRadiusTiles = GameSetupData.lakeMinRadiusTiles;
        generator.lakeMaxRadiusTiles = GameSetupData.lakeMaxRadiusTiles;
        generator.lakeMinDistanceFromCoast = GameSetupData.lakeMinDistanceFromCoast;

    
    yield return StartCoroutine(generator.GenerateSurface());
    // NOTE: EnsureVisualsSpawned removed - new system uses texture-based rendering (FlatMapTextureRenderer)
    
    // CRITICAL FIX: Register the planet generator BEFORE firing events
    // This ensures the generator is available when spawn events fire
    planetGenerators[planetIndex] = generator;

    // Apply data-driven layer setup (do not use planet type conditionals)
    if (planetData.ContainsKey(planetIndex) && planetData[planetIndex] != null)
    {
        try
        {
            generator.ApplyPlanetLayers(planetData[planetIndex]);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameManager] Failed to apply planet layers for planet {planetIndex}: {ex.Message}");
        }
    }

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

    // Ensure per-planet TileSystem exists and is initialized before notifying listeners.
    // This makes OnPlanetFullyGenerated / OnPlanetReady safe for tile queries on that planet.
    EnsureTileSystemForPlanet(generator);

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

    // Deactivate this planet's GameObject if it's not the current planet.
    // This ensures only the active planet's water surfaces, resources, units, etc. are visible.
    // HexMapChunkManager is outside the planet hierarchy and will rebuild when switching.
    if (planetIndex != currentPlanetIndex)
    {
        planetGO.SetActive(false);
        // Debug.Log — Planet deactivated after generation (disabled to reduce console noise)
    }
    
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

        // Show space loading screen while the world is being built
        string destName = planetData.ContainsKey(planetIndex) ? planetData[planetIndex].planetName : $"Planet {planetIndex}";
        ShowSpaceTravel(destName);
        UpdateSpaceTravelProgress(0.05f, "Initiating travel...");
        yield return null; // let UI render

        int previousPlanetIndex = currentPlanetIndex;
        currentPlanetIndex = planetIndex;

        // Ensure grid is built and surface generated if needed
        var generator = planetGenerators[planetIndex];

        // Activate destination planet BEFORE generation/switching so coroutines can run on it
        if (generator != null)
        {
            generator.gameObject.SetActive(true);
            Debug.Log($"[GameManager] Activated planet {planetIndex} GameObject for switch");
        }

        UpdateSpaceTravelProgress(0.15f, "Building grid...");
        yield return null;

        bool surfaceJustGenerated = false;
        if (generator != null && !generator.Grid.IsBuilt)
        {
            var sizePreset = planetData[planetIndex].planetSize;
            GetFlatMapSizeParams(sizePreset, out float width, out float height);
            GetFlatTileResolution(sizePreset, out int tilesX, out int tilesZ);
            generator.Grid.GenerateFlatGrid(tilesX, tilesZ, width, height);
        }

        UpdateSpaceTravelProgress(0.30f, "Generating surface...");
        yield return null;

        if (generator != null && generator.Grid.TileCount > 0 && !generator.HasGeneratedSurface)
        {
            yield return StartCoroutine(generator.GenerateSurface());
            // NOTE: EnsureVisualsSpawned removed - new system uses texture-based rendering (FlatMapTextureRenderer)
            surfaceJustGenerated = true;
        }

        UpdateSpaceTravelProgress(0.75f, "Initializing tile system...");
        yield return null;

        // Per-planet TileSystems: ensure destination planet has one; do NOT reinitialize tile state on switch.
        EnsureTileSystemForPlanet(generator);

        // Match initial generation ordering: TileSystem binds first, then notify.
        if (surfaceJustGenerated)
        {
            OnPlanetFullyGenerated?.Invoke(generator);
        }

        UpdateSpaceTravelProgress(0.90f, $"Arriving at {destName}...");
        yield return null;

        // Ensure planet-ready listeners rebuild on planet switch.
        OnPlanetReady?.Invoke(planetIndex);

        // Deactivate the previous planet's GameObject (and all children: water, resources, units, etc.)
        if (previousPlanetIndex != planetIndex && planetGenerators.ContainsKey(previousPlanetIndex))
        {
            var prevGen = planetGenerators[previousPlanetIndex];
            if (prevGen != null)
            {
                prevGen.gameObject.SetActive(false);
                Debug.Log($"[GameManager] Deactivated planet {previousPlanetIndex} GameObject on switch to planet {planetIndex}");
            }
        }

        UpdateSpaceTravelProgress(1.0f, "Complete!");
        // Brief pause so the player sees 100%
        yield return new WaitForSeconds(0.5f);
        HideSpaceTravel();
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

        // Ensure per-planet TileSystem exists/initialized for the generated planet.
        EnsureTileSystemForPlanet(planetGenerator);

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
            {
                UIManager.Instance.playerUI.SetActive(true);

                // Refresh layer dropdown now that planet generation is complete
                // (Start-time refresh runs too early — LayerManager isn't added until ApplyPlanetLayers)
                var pui = UIManager.Instance.playerUI.GetComponent<PlayerUI>();
                if (pui == null) pui = UIManager.Instance.playerUI.GetComponentInChildren<PlayerUI>();
                if (pui != null) pui.RefreshLayerDropdown();
            }
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
        // Use cached reference for performance; fall back to ManagerCache, then scene search
        if (cachedLoadingPanel == null)
            cachedLoadingPanel = _cachedManagers.loadingPanelController;
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
        // Use cached reference for performance; fall back to ManagerCache, then scene search
        if (cachedLoadingPanel == null)
            cachedLoadingPanel = _cachedManagers.loadingPanelController;
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
        if (planetGenerators != null && planetGenerators.Count > 0)
        {
            foreach (var kv in planetGenerators)
            {
                var ts = TileSystem.GetForPlanet(kv.Key);
                ts?.ClearAllCaches();
            }
        }
        else
        {
            TileSystem.Instance?.ClearAllCaches();
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
    public PauseMenuManager.GameSaveData BuildSaveData(string saveName = null, bool isAutosave = false)
    {
        PauseMenuManager.GameSaveData saveData = new PauseMenuManager.GameSaveData
        {
            saveName = string.IsNullOrEmpty(saveName) ? $"save_{System.DateTime.Now:yyyyMMdd_HHmmss}" : saveName,
            currentTurn = currentTurn,
            mapSize = mapSize,
            enableMultiPlanetSystem = true,
            currentPlanetIndex = currentPlanetIndex,
            gameInProgress = gameInProgress,
            flatMapWidth = GetFlatMapWidth(),
            flatMapHeight = GetFlatMapHeight(),
            isAutosave = isAutosave,
            combatUnits = new List<PauseMenuManager.CombatUnitSaveData>(),
            workerUnits = new List<PauseMenuManager.WorkerUnitSaveData>(),
            civilizationProgress = new List<PauseMenuManager.CivilizationProgressSaveData>(),
            cities = new List<PauseMenuManager.CitySaveData>(),
            participantStates = new List<PauseMenuManager.SaveParticipantStateData>()
        };

        var worldSnapshot = CaptureWorldSnapshot();
        saveData.worldSnapshot = worldSnapshot;
        saveData.jobAssignments = worldSnapshot.jobAssignments;
        saveData.combatUnits = worldSnapshot.combatUnits;
        saveData.workerUnits = worldSnapshot.workerUnits;
        saveData.civilizationProgress = worldSnapshot.civilizationProgress;
        saveData.cities = worldSnapshot.cities;
        saveData.missionStates = worldSnapshot.missionStates;
        saveData.crisisState = worldSnapshot.crisisState;

        if (civilizationManager != null && civilizationManager.playerCiv != null)
        {
            saveData.playerCivName = civilizationManager.playerCiv.civData.civName;
            saveData.playerCivIndex = civilizationManager.GetCivIndex(civilizationManager.playerCiv);
        }

        if (Camera.main != null)
        {
            saveData.cameraPosition = Camera.main.transform.position;
            saveData.cameraRotation = Camera.main.transform.eulerAngles;
        }

        saveData.participantStates = SaveGameRegistry.CaptureAll();
        return saveData;
    }

    private PauseMenuManager.WorldSnapshotData CaptureWorldSnapshot()
    {
        var snapshot = new PauseMenuManager.WorldSnapshotData();

        if (ImprovementManager.Instance != null)
            snapshot.jobAssignments = ImprovementManager.Instance.ExportJobAssignments();

        if (civilizationManager != null)
        {
            var allCivs = civilizationManager.GetAllCivs();
            for (int civIdx = 0; civIdx < allCivs.Count; civIdx++)
            {
                var civ = allCivs[civIdx];
                if (civ == null) continue;

                var civProgress = new PauseMenuManager.CivilizationProgressSaveData
                {
                    civIndex = civIdx,
                    currentTechName = civ.currentTech != null ? civ.currentTech.name : null,
                    currentTechProgress = civ.currentTechProgress,
                    currentCultureName = civ.currentCulture != null ? civ.currentCulture.name : null,
                    currentCultureProgress = civ.currentCultureProgress,
                    tradeEnabled = civ.tradeEnabled,
                    governorsEnabled = civ.governorsEnabled,
                    governorCount = civ.governorCount,
                    cityCapFromBonuses = civ.GetCityCapBonusForSave(),
                    pantheonCapFromBonuses = civ.pantheonCapFromBonuses,
                    attackBonus = civ.attackBonus,
                    defenseBonus = civ.defenseBonus,
                    movementBonus = civ.movementBonus,
                    foodModifier = civ.foodModifier,
                    productionModifier = civ.productionModifier,
                    goldModifier = civ.goldModifier,
                    scienceModifier = civ.scienceModifier,
                    cultureModifier = civ.cultureModifier,
                    faithModifier = civ.faithModifier
                };

                if (civ.researchedTechs != null)
                    foreach (var tech in civ.researchedTechs)
                        if (tech != null) civProgress.researchedTechNames.Add(tech.name);
                if (civ.researchedCultures != null)
                    foreach (var culture in civ.researchedCultures)
                        if (culture != null) civProgress.researchedCultureNames.Add(culture.name);
                if (civ.unlockedGovernorTraits != null)
                    foreach (var trait in civ.unlockedGovernorTraits)
                        if (trait != null) civProgress.unlockedGovernorTraitNames.Add(trait.name);
                if (civ.cultureUnlockedPantheons != null)
                    foreach (var pantheon in civ.cultureUnlockedPantheons)
                        if (pantheon != null) civProgress.cultureUnlockedPantheonNames.Add(pantheon.name);
                if (civ.cultureUnlockedBeliefs != null)
                    foreach (var belief in civ.cultureUnlockedBeliefs)
                        if (belief != null) civProgress.cultureUnlockedBeliefNames.Add(belief.name);
                if (civ.customAssignedBeliefs != null)
                    foreach (var belief in civ.customAssignedBeliefs)
                        if (belief != null) civProgress.customAssignedBeliefNames.Add(belief.name);
                if (civ.earnedLegacies != null)
                    foreach (var leg in civ.earnedLegacies)
                        if (leg != null) civProgress.earnedLegacyNames.Add(leg.legacyName);
                if (civ.activeLegacies != null)
                    foreach (var leg in civ.activeLegacies)
                        if (leg != null) civProgress.activeLegacyNames.Add(leg.legacyName);

                if (civ.governors != null)
                {
                    for (int g = 0; g < civ.governors.Count; g++)
                    {
                        var gov = civ.governors[g];
                        if (gov == null) continue;
                        var gsd = new PauseMenuManager.GovernorSaveData
                        {
                            id = gov.Id,
                            name = gov.Name,
                            specialization = gov.specialization,
                            level = gov.Level,
                            experience = gov.Experience
                        };
                        if (gov.Cities != null && civ.cities != null)
                        {
                            foreach (var city in gov.Cities)
                            {
                                if (city == null) continue;
                                int idx = civ.cities.IndexOf(city);
                                if (idx >= 0) gsd.assignedCityIndices.Add(idx);
                            }
                        }
                        if (gov.Herds != null)
                        {
                            foreach (var herd in gov.Herds)
                            {
                                if (herd == null) continue;
                                gsd.assignedHerdRefs.Add(new PauseMenuManager.HerdRef { planetIndex = herd.planetIndex, tileIndex = herd.currentTileIndex });
                            }
                        }
                        if (gov.Traits != null)
                        {
                            foreach (var t in gov.Traits)
                            {
                                if (t != null) gsd.traitNames.Add(t.traitName);
                            }
                        }
                        civProgress.governors.Add(gsd);
                    }
                }

                try
                {
                    if (civ.herds != null)
                    {
                        foreach (var h in civ.herds)
                        {
                            if (h == null) continue;
                            var hq = new PauseMenuManager.HerdQueueSaveData
                            {
                                planetIndex = h.planetIndex,
                                tileIndex = h.currentTileIndex
                            };
                            if (h.productionQueue != null && h.productionQueue.Count > 0)
                            {
                                foreach (var e in h.productionQueue)
                                {
                                    if (e == null || e.data == null) continue;
                                    hq.queue.Add(new PauseMenuManager.HerdProdEntrySaveData
                                    {
                                        dataName = e.data.name,
                                        remainingPts = e.remainingPts,
                                        goldCost = e.goldCost
                                    });
                                }
                            }
                            civProgress.herdQueues.Add(hq);
                        }
                    }
                }
                catch { }

                snapshot.civilizationProgress.Add(civProgress);

                if (civ.cities != null)
                {
                    for (int cityIdx = 0; cityIdx < civ.cities.Count; cityIdx++)
                    {
                        var city = civ.cities[cityIdx];
                        if (city == null) continue;

                        var citySave = new PauseMenuManager.CitySaveData
                        {
                            ownerCivIndex = civIdx,
                            originalOwnerCivIndex = civilizationManager.GetCivIndex(city.OriginalOwner),
                            ownerCityListIndex = cityIdx,
                            isCapital = city.isCapital,
                            centerTileIndex = city.centerTileIndex,
                            planetIndex = city.planetIndex,
                            cityName = city.cityName,
                            level = city.level,
                            foodStorage = city.foodStorage,
                            foodGrowthRequirement = city.foodGrowthRequirement,
                            loyalty = city.loyalty,
                            productionPerTurn = city.productionPerTurn
                        };

                        if (city.builtBuildings != null)
                        {
                            foreach (var (data, _) in city.builtBuildings)
                            {
                                if (data != null)
                                    citySave.builtBuildingNames.Add(data.name);
                            }
                        }

                        if (city.productionQueue != null)
                        {
                            foreach (var entry in city.productionQueue)
                            {
                                if (entry == null || entry.data == null) continue;
                                citySave.productionQueue.Add(new PauseMenuManager.CityProductionEntrySaveData
                                {
                                    type = entry.type,
                                    dataName = entry.data.name,
                                    remainingPts = entry.remainingPts,
                                    goldCost = entry.goldCost,
                                    districtTileIndex = entry.data is DistrictData district && city.TryGetQueuedDistrictTile(district, out int tileIndex)
                                        ? tileIndex
                                        : -1
                                });
                            }
                        }

                        // Save city missile inventory
                        if (city.storedMissiles != null)
                        {
                            foreach (var missile in city.storedMissiles)
                                if (missile != null) citySave.storedMissileNames.Add(missile.missileName);
                        }

                        snapshot.cities.Add(citySave);
                    }
                }

                if (civ.combatUnits != null)
                {
                    foreach (var unit in civ.combatUnits)
                    {
                        if (unit == null || unit.data == null) continue;
                        var unitSave = new PauseMenuManager.CombatUnitSaveData
                        {
                            unitDataName = unit.data.unitName,
                            ownerCivIndex = civIdx,
                            currentTileIndex = unit.currentTileIndex,
                            planetIndex = unit.planetIndex,
                            currentLayer = (int)unit.currentLayer,
                            currentHealth = unit.currentHealth,
                            experience = unit.experience,
                            level = unit.level,
                            hasActedThisTurn = unit.hasActedThisTurn,
                            posX = unit.transform.position.x,
                            posY = unit.transform.position.y,
                            posZ = unit.transform.position.z,
                        };
                        // Save unit missile inventory
                        if (unit.storedMissiles != null)
                            foreach (var m in unit.storedMissiles)
                                if (m != null) unitSave.storedMissileNames.Add(m.missileName);
                        snapshot.combatUnits.Add(unitSave);
                    }
                }

                if (civ.workerUnits != null)
                {
                    foreach (var worker in civ.workerUnits)
                    {
                        if (worker == null || worker.data == null) continue;
                        snapshot.workerUnits.Add(new PauseMenuManager.WorkerUnitSaveData
                        {
                            unitDataName = worker.data.unitName,
                            ownerCivIndex = civIdx,
                            currentTileIndex = worker.currentTileIndex,
                            planetIndex = worker.planetIndex,
                            currentLayer = (int)worker.currentLayer,
                            currentHealth = worker.currentHealth,
                            experience = worker.experience,
                            level = worker.level,
                            currentWorkPoints = worker.currentWorkPoints,
                            currentMovePoints = worker.currentMovePoints,
                            posX = worker.transform.position.x,
                            posY = worker.transform.position.y,
                            posZ = worker.transform.position.z,
                        });
                    }
                }
            }
        }

        return snapshot;
    }

    private static PauseMenuManager.WorldSnapshotData GetWorldSnapshot(PauseMenuManager.GameSaveData saveData)
    {
        if (saveData?.worldSnapshot != null && saveData.worldSnapshot.HasState())
            return saveData.worldSnapshot;

        return new PauseMenuManager.WorldSnapshotData
        {
            jobAssignments = saveData?.jobAssignments ?? new List<ImprovementManager.JobAssignmentSaveData>(),
            combatUnits = saveData?.combatUnits ?? new List<PauseMenuManager.CombatUnitSaveData>(),
            workerUnits = saveData?.workerUnits ?? new List<PauseMenuManager.WorkerUnitSaveData>(),
            civilizationProgress = saveData?.civilizationProgress ?? new List<PauseMenuManager.CivilizationProgressSaveData>(),
            cities = saveData?.cities ?? new List<PauseMenuManager.CitySaveData>(),
            missionStates = saveData?.missionStates ?? new List<CrisisManager.MissionStateSaveData>(),
            crisisState = saveData?.crisisState
        };
    }

    public void SaveGame(string saveName)
    {
        try
        {
            PauseMenuManager.GameSaveData saveData = BuildSaveData(saveName);

            string saveDirectory = System.IO.Path.Combine(Application.persistentDataPath, "Saves");
            if (!System.IO.Directory.Exists(saveDirectory))
            {
                System.IO.Directory.CreateDirectory(saveDirectory);
            }

            string fileName = string.IsNullOrEmpty(saveName) ? $"save_{System.DateTime.Now:yyyyMMdd_HHmmss}.json" : $"{saveName}.json";
            string filePath = System.IO.Path.Combine(saveDirectory, fileName);
            string jsonData = JsonUtility.ToJson(saveData, true);
            System.IO.File.WriteAllText(filePath, jsonData);

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
        var worldSnapshot = GetWorldSnapshot(saveData);
        

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
        // Legacy: enableMultiPlanetSystem is always true, ignore loaded value for compatibility
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

        try
        {
            RestoreCityStatesFromSnapshot(worldSnapshot);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to restore city state: {e.Message}\n{e.StackTrace}");
        }

        try
        {
            RestoreCivilizationProgressFromSnapshot(worldSnapshot);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to restore civilization progression: {e.Message}\n{e.StackTrace}");
        }

        // Import improvement manager assignments AFTER units are present and registered
        if (worldSnapshot.jobAssignments != null && worldSnapshot.jobAssignments.Count > 0)
        {
            // Allow a small delay for UnitRegistry to populate (in case units are spawned next frame)
            yield return null;
            try
            {
                ImprovementManager.Instance?.ImportJobAssignments(worldSnapshot.jobAssignments);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to import job assignments: {e.Message}");
            }
        }

        // ===== Restore units from save data =====
        yield return null;
        try
        {
            RestoreUnitsFromSnapshot(worldSnapshot);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to restore units from save data: {e.Message}\n{e.StackTrace}");
        }

        try
        {
            SaveGameRegistry.RestoreAll(saveData.participantStates);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to restore save participants: {e.Message}");
        }

        try
        {
            bool hasCrisisParticipantState = saveData.participantStates != null
                && saveData.participantStates.Any(s => s != null && string.Equals(s.key, "crisis-manager", StringComparison.OrdinalIgnoreCase));
            if (!hasCrisisParticipantState && CrisisManager.Instance != null)
            {
                if (worldSnapshot.missionStates != null)
                    CrisisManager.Instance.ImportMissionStates(worldSnapshot.missionStates);
                if (worldSnapshot.crisisState != null)
                    CrisisManager.Instance.ImportCrisisState(worldSnapshot.crisisState);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to restore legacy crisis state: {e.Message}");
        }

        
    }

    private void RestoreCityStatesFromSnapshot(PauseMenuManager.WorldSnapshotData snapshot)
    {
        if (snapshot?.cities == null || snapshot.cities.Count == 0) return;

        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return;

        var cityLookup = new Dictionary<(int planetIndex, int tileIndex), City>();
        foreach (var city in FindObjectsByType<City>(FindObjectsSortMode.None))
        {
            if (city == null) continue;
            cityLookup[(city.planetIndex, city.centerTileIndex)] = city;
        }

        var buildingLookup = BuildAssetLookup(ResourceCache.GetAllBuildings(), b => b.buildingName);
        var combatUnitLookup = BuildAssetLookup(ResourceCache.GetAllCombatUnits(), u => u.unitName);
        var workerLookup = BuildAssetLookup(ResourceCache.GetAllWorkerUnits(), w => w.unitName);
        var equipmentLookup = BuildAssetLookup(ResourceCache.GetAllEquipment(), e => e.equipmentName);
        var projectileLookup = BuildAssetLookup(ResourceCache.GetAllProjectiles(), p => p.projectileName);
        var districtLookup = BuildAssetLookup(ResourceCache.GetAllDistricts(), d => d.districtName);

        foreach (var civ in allCivs)
        {
            civ?.cities?.Clear();
        }

        foreach (var cityData in snapshot.cities.OrderBy(c => c.ownerCivIndex).ThenBy(c => c.ownerCityListIndex))
        {
            if (cityData == null) continue;
            if (!cityLookup.TryGetValue((cityData.planetIndex, cityData.centerTileIndex), out var city) || city == null)
            {
                Debug.LogWarning($"[SaveLoad] Could not find city at planet {cityData.planetIndex}, tile {cityData.centerTileIndex}.");
                continue;
            }

            Civilization ownerCiv = cityData.ownerCivIndex >= 0 && cityData.ownerCivIndex < allCivs.Count ? allCivs[cityData.ownerCivIndex] : city.owner;
            Civilization originalOwnerCiv = cityData.originalOwnerCivIndex >= 0 && cityData.originalOwnerCivIndex < allCivs.Count ? allCivs[cityData.originalOwnerCivIndex] : ownerCiv;

            city.owner = ownerCiv;
            city.OriginalOwner = originalOwnerCiv ?? ownerCiv;
            city.isCapital = cityData.isCapital;
            city.cityName = cityData.cityName;
            city.level = cityData.level;
            city.foodStorage = cityData.foodStorage;
            city.foodGrowthRequirement = cityData.foodGrowthRequirement;
            city.loyalty = cityData.loyalty;
            city.productionPerTurn = cityData.productionPerTurn;

            if (ownerCiv != null)
            {
                ownerCiv.AddCity(city);
                if (cityData.isCapital)
                    ownerCiv.SetCapitalCity(city);
            }

            var savedBuildings = new List<BuildingData>();
            if (cityData.builtBuildingNames != null)
            {
                foreach (var buildingName in cityData.builtBuildingNames)
                {
                    if (string.IsNullOrWhiteSpace(buildingName)) continue;
                    if (buildingLookup.TryGetValue(buildingName, out var building) && building != null)
                        savedBuildings.Add(building);
                }
            }
            city.RestoreBuiltBuildingsForSave(savedBuildings);

            var restoredQueue = new List<City.ProdEntry>();
            var districtTargets = new Dictionary<DistrictData, int>();
            if (cityData.productionQueue != null)
            {
                foreach (var entryData in cityData.productionQueue)
                {
                    if (entryData == null || string.IsNullOrWhiteSpace(entryData.dataName)) continue;

                    ScriptableObject resolvedData = entryData.type switch
                    {
                        City.ProdEntry.Type.Unit when combatUnitLookup.TryGetValue(entryData.dataName, out var unit) => unit,
                        City.ProdEntry.Type.Worker when workerLookup.TryGetValue(entryData.dataName, out var worker) => worker,
                        City.ProdEntry.Type.Building when buildingLookup.TryGetValue(entryData.dataName, out var building) => building,
                        City.ProdEntry.Type.District when districtLookup.TryGetValue(entryData.dataName, out var district) => district,
                        City.ProdEntry.Type.Equipment when equipmentLookup.TryGetValue(entryData.dataName, out var equipment) => equipment,
                        City.ProdEntry.Type.Projectile when projectileLookup.TryGetValue(entryData.dataName, out var projectile) => projectile,
                        _ => null
                    };

                    if (resolvedData == null) continue;

                    var restoredEntry = CreateCityProductionEntryForRestore(resolvedData, entryData.type, entryData.goldCost);
                    if (restoredEntry == null) continue;
                    restoredEntry.remainingPts = entryData.remainingPts;
                    restoredEntry.goldCost = entryData.goldCost;
                    restoredQueue.Add(restoredEntry);

                    if (resolvedData is DistrictData districtData && entryData.districtTileIndex >= 0)
                    {
                        districtTargets[districtData] = entryData.districtTileIndex;
                    }
                }
            }

            city.RestoreProductionQueueForSave(restoredQueue, districtTargets);

            // Restore city missile inventory
            if (cityData.storedMissileNames != null && cityData.storedMissileNames.Count > 0)
            {
                var missileLookup = BuildAssetLookup(ResourceCache.GetAllMissiles(), m => m.missileName);
                city.storedMissiles.Clear();
                foreach (var name in cityData.storedMissileNames)
                    if (missileLookup.TryGetValue(name, out var md)) city.storedMissiles.Add(md);
            }
        }
    }

    private static City.ProdEntry CreateCityProductionEntryForRestore(ScriptableObject data, City.ProdEntry.Type type, int goldCost)
    {
        switch (data)
        {
            case CombatUnitData unit:
                return new City.ProdEntry(unit, unit.productionCost, goldCost, unit.requiredResources, unit.requiredTerrains, unit.requiresCoastalCity, unit.requiresHarbor, type);
            case WorkerUnitData worker:
                return new City.ProdEntry(worker, worker.productionCost, goldCost, worker.requiredResources, worker.requiredTerrains, worker.requiresCoastalCity, worker.requiresHarbor, type);
            case BuildingData building:
                return new City.ProdEntry(building, building.productionCost, goldCost, building.requiredResources, building.requiredTerrains, false, false, type);
            case DistrictData district:
                return new City.ProdEntry(district, district.productionCost, goldCost, null, district.allowedBiomes, district.requiresCoastal, false, type);
            case EquipmentData equipment:
                return new City.ProdEntry(equipment, equipment.productionCost, goldCost, null, null, false, false, type);
            case GameCombat.ProjectileData projectile:
                return new City.ProdEntry(projectile, projectile.productionCost, goldCost, projectile.requiredResources, null, false, false, type);
            case MissileData missile:
                return new City.ProdEntry(missile, missile.productionCost, goldCost, null, null, false, false, type);
            default:
                return null;
        }
    }

    private void RestoreCivilizationProgressFromSnapshot(PauseMenuManager.WorldSnapshotData snapshot)
    {
        if (snapshot?.civilizationProgress == null || snapshot.civilizationProgress.Count == 0) return;

        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return;

        var techLookup = BuildAssetLookup(ResourceCache.GetAllTechData(), t => t.techName);
        var cultureLookup = BuildAssetLookup(ResourceCache.GetAllCultureData(), c => c.cultureName);
        var governorTraitLookup = BuildAssetLookup(Resources.LoadAll<GovernorTrait>(""), t => t.traitName);
        var pantheonLookup = BuildAssetLookup(Resources.LoadAll<PantheonData>(""), p => p.pantheonName);
        var beliefLookup = BuildAssetLookup(Resources.LoadAll<BeliefData>(""), b => b.beliefName);

        foreach (var progress in snapshot.civilizationProgress)
        {
            if (progress == null) continue;
            if (progress.civIndex < 0 || progress.civIndex >= allCivs.Count) continue;

            var civ = allCivs[progress.civIndex];
            if (civ == null) continue;

            var researchedTechs = ResolveAssets(progress.researchedTechNames, techLookup);
            var researchedCultures = ResolveAssets(progress.researchedCultureNames, cultureLookup);
            var unlockedGovernorTraits = ResolveAssets(progress.unlockedGovernorTraitNames, governorTraitLookup);
            var unlockedPantheons = ResolveAssets(progress.cultureUnlockedPantheonNames, pantheonLookup);
            var unlockedBeliefs = ResolveAssets(progress.cultureUnlockedBeliefNames, beliefLookup);
            var customAssignedBeliefs = ResolveAssets(progress.customAssignedBeliefNames, beliefLookup);

            techLookup.TryGetValue(progress.currentTechName ?? string.Empty, out var currentTech);
            cultureLookup.TryGetValue(progress.currentCultureName ?? string.Empty, out var currentCulture);

            civ.RestoreProgressionState(
                researchedTechs,
                currentTech,
                progress.currentTechProgress,
                researchedCultures,
                currentCulture,
                progress.currentCultureProgress,
                progress.tradeEnabled,
                progress.governorsEnabled,
                progress.governorCount,
                progress.cityCapFromBonuses,
                progress.pantheonCapFromBonuses,
                progress.attackBonus,
                progress.defenseBonus,
                progress.movementBonus,
                progress.foodModifier,
                progress.productionModifier,
                progress.goldModifier,
                progress.scienceModifier,
                progress.cultureModifier,
                progress.faithModifier,
                unlockedGovernorTraits,
                unlockedPantheons,
                unlockedBeliefs,
                customAssignedBeliefs);

            // Restore herd production queues saved for this civilization
            try
            {
                if (progress.herdQueues != null && progress.herdQueues.Count > 0)
                {
                    // Build lookup for buildings by asset name
                    var buildingLookup = BuildAssetLookup(ResourceCache.GetAllBuildings(), b => b.buildingName);

                    foreach (var hq in progress.herdQueues)
                    {
                        if (hq == null) continue;
                        Herd targetHerd = null;
                        if (civ.herds != null)
                        {
                            foreach (var h in civ.herds)
                            {
                                if (h == null) continue;
                                if (h.planetIndex == hq.planetIndex && h.currentTileIndex == hq.tileIndex)
                                {
                                    targetHerd = h; break;
                                }
                            }
                        }

                        // If no existing herd found, create one at the saved tile
                        if (targetHerd == null)
                        {
                            try
                            {
                                var go = new GameObject($"Herd_{(civ.civData != null ? civ.civData.civName : civ.name)}_{hq.tileIndex}");
                                var herd = go.AddComponent<Herd>();
                                herd.owner = civ;
                                try { herd.herdName = civ.GetNewHerdName(); } catch { }
                                herd.planetIndex = hq.planetIndex;
                                herd.currentTileIndex = hq.tileIndex;
                                targetHerd = herd;
                            }
                            catch { }
                        }

                        if (targetHerd == null) continue;
                        // Clear existing queue and repopulate
                        try
                        {
                            targetHerd.productionQueue = targetHerd.productionQueue ?? new System.Collections.Generic.List<Herd.ProdEntry>();
                            targetHerd.productionQueue.Clear();
                            if (hq.queue != null)
                            {
                                foreach (var pe in hq.queue)
                                {
                                    if (pe == null || string.IsNullOrWhiteSpace(pe.dataName)) continue;
                                    if (!buildingLookup.TryGetValue(pe.dataName, out var bd) || bd == null) continue;
                                    var entry = new Herd.ProdEntry(bd, bd.productionCost, bd.goldCost, bd.requiredResources, bd.requiredTerrains);
                                    entry.remainingPts = pe.remainingPts;
                                    entry.goldCost = pe.goldCost;
                                    targetHerd.productionQueue.Add(entry);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SaveLoad] Failed to restore herd queues for civ {civ.civData?.civName ?? progress.civIndex.ToString()}: {ex}");
            }

            // Reconstruct governors from save data for this civilization
            try
            {
                // Clear any existing governors and recreate from saved entries
                civ.governors = civ.governors ?? new System.Collections.Generic.List<Governor>();
                civ.governors.Clear();
                if (progress.governors != null)
                {
                    foreach (var gsd in progress.governors)
                    {
                        if (gsd == null) continue;
                        // Create governor (this will respect governorsEnabled and governorCount)
                        var newGov = civ.CreateGovernor(gsd.name ?? "Governor", gsd.specialization);
                        if (newGov == null) continue;
                        // Restore level and experience via reflection (Level/Experience have private setters)
                        var govType = typeof(Governor);
                        var levelProp = govType.GetProperty("Level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        var expProp = govType.GetProperty("Experience", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        try
                        {
                            if (levelProp != null) levelProp.SetValue(newGov, gsd.level);
                            if (expProp != null) expProp.SetValue(newGov, gsd.experience);
                        }
                        catch { }

                        // Restore traits
                        if (gsd.traitNames != null && gsd.traitNames.Count > 0)
                        {
                            foreach (var tname in gsd.traitNames)
                            {
                                if (string.IsNullOrWhiteSpace(tname)) continue;
                                if (governorTraitLookup.TryGetValue(tname, out var trait) && trait != null && !newGov.Traits.Contains(trait))
                                    newGov.Traits.Add(trait);
                            }
                        }

                        // Assign to cities by saved indices
                        if (gsd.assignedCityIndices != null && civ.cities != null && civ.cities.Count > 0)
                        {
                            foreach (var cityIdx in gsd.assignedCityIndices)
                            {
                                if (cityIdx < 0 || cityIdx >= civ.cities.Count) continue;
                                var assignCity = civ.cities[cityIdx];
                                if (assignCity != null)
                                {
                                    civ.AssignGovernorToCity(newGov, assignCity);
                                }
                            }
                        }
                        // Assign to herds by saved refs (planet+tile)
                        if (gsd.assignedHerdRefs != null && gsd.assignedHerdRefs.Count > 0)
                        {
                            foreach (var href in gsd.assignedHerdRefs)
                            {
                                if (href == null) continue;
                                Herd targetHerd = null;
                                try
                                {
                                    // Try to find an existing herd matching planet+tile
                                    if (civ.herds != null)
                                    {
                                        foreach (var hh in civ.herds)
                                            if (hh != null && hh.planetIndex == href.planetIndex && hh.currentTileIndex == href.tileIndex)
                                                { targetHerd = hh; break; }
                                    }
                                    // If not found, create one at the saved tile
                                    if (targetHerd == null)
                                    {
                                        var go = new GameObject($"Herd_{(civ.civData != null ? civ.civData.civName : civ.name)}_{href.tileIndex}");
                                        var herd = go.AddComponent<Herd>();
                                        herd.owner = civ;
                                        try { herd.herdName = civ.GetNewHerdName(); } catch { }
                                        herd.planetIndex = href.planetIndex;
                                        herd.currentTileIndex = href.tileIndex;
                                        targetHerd = herd;
                                    }
                                }
                                catch { }

                                if (targetHerd != null) civ.AssignGovernorToHerd(newGov, targetHerd);
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SaveLoad] Failed to restore governors for civ {civ.civData?.civName ?? progress.civIndex.ToString()}: {ex}");
            }

            // Restore legacy state
            try
            {
                IEnumerable<LegacyData> legacySources = LegacyManager.Instance != null
                    ? (IEnumerable<LegacyData>)LegacyManager.Instance.allLegacies
                    : Resources.LoadAll<LegacyData>("");
                var legacyLookup = BuildAssetLookup<LegacyData>(legacySources, l => l.legacyName);

                civ.earnedLegacies = ResolveAssets<LegacyData>(progress.earnedLegacyNames, legacyLookup);
                civ.activeLegacies = ResolveAssets<LegacyData>(progress.activeLegacyNames, legacyLookup);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SaveLoad] Failed to restore legacies for civ {civ.civData?.civName ?? progress.civIndex.ToString()}: {ex}");
            }
        }
    }

    private static Dictionary<string, T> BuildAssetLookup<T>(IEnumerable<T> assets, Func<T, string> displayNameSelector) where T : ScriptableObject
    {
        var lookup = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (assets == null) return lookup;

        foreach (var asset in assets)
        {
            if (asset == null) continue;

            if (!string.IsNullOrWhiteSpace(asset.name))
                lookup[asset.name] = asset;

            string displayName = displayNameSelector != null ? displayNameSelector(asset) : null;
            if (!string.IsNullOrWhiteSpace(displayName))
                lookup[displayName] = asset;
        }

        return lookup;
    }

    private static List<T> ResolveAssets<T>(IEnumerable<string> names, Dictionary<string, T> lookup) where T : ScriptableObject
    {
        var assets = new List<T>();
        if (names == null || lookup == null) return assets;

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (lookup.TryGetValue(name, out var asset) && asset != null && !assets.Contains(asset))
                assets.Add(asset);
        }

        return assets;
    }

    /// <summary>
    /// Restore all combat and worker units from serialized save data.
    /// </summary>
    private void RestoreUnitsFromSnapshot(PauseMenuManager.WorldSnapshotData snapshot)
    {
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return;

        // Look-up tables for unit data SOs
        var combatUnitDataLookup = new System.Collections.Generic.Dictionary<string, CombatUnitData>();
        foreach (var ud in ResourceCache.GetAllCombatUnits())
        {
            if (ud != null && !string.IsNullOrEmpty(ud.unitName))
                combatUnitDataLookup[ud.unitName] = ud;
        }

        var workerUnitDataLookup = new System.Collections.Generic.Dictionary<string, WorkerUnitData>();
        foreach (var wd in ResourceCache.GetAllWorkerUnits())
        {
            if (wd != null && !string.IsNullOrEmpty(wd.unitName))
                workerUnitDataLookup[wd.unitName] = wd;
        }

        // Restore combat units
        if (snapshot.combatUnits != null)
        {
            foreach (var usd in snapshot.combatUnits)
            {
                if (usd == null) continue;
                if (!combatUnitDataLookup.TryGetValue(usd.unitDataName, out var unitData))
                {
                    Debug.LogWarning($"[SaveLoad] CombatUnitData '{usd.unitDataName}' not found in ResourceCache, skipping.");
                    continue;
                }
                if (usd.ownerCivIndex < 0 || usd.ownerCivIndex >= allCivs.Count)
                {
                    Debug.LogWarning($"[SaveLoad] Invalid ownerCivIndex {usd.ownerCivIndex} for unit '{usd.unitDataName}', skipping.");
                    continue;
                }
                var civ = allCivs[usd.ownerCivIndex];

                var prefab = unitData.GetPrefab(civ);
                if (prefab == null)
                {
                    Debug.LogWarning($"[SaveLoad] Prefab not found for '{usd.unitDataName}', skipping.");
                    continue;
                }

                var ts = TileSystem.GetForPlanet(usd.planetIndex) ?? TileSystem.Instance;
                Vector3 spawnPos = ts != null ? ts.GetTileCenterFlat(usd.currentTileIndex) : new Vector3(usd.posX, usd.posY, usd.posZ);

                var go = Instantiate(prefab, spawnPos, Quaternion.identity);
                var pg = GetPlanetGenerator(usd.planetIndex) ?? GetCurrentPlanetGenerator();
                if (pg != null) go.transform.SetParent(pg.transform, true);

                var unit = go.GetComponent<CombatUnit>();
                if (unit == null)
                {
                    Debug.LogWarning($"[SaveLoad] Spawned prefab for '{usd.unitDataName}' has no CombatUnit component, skipping.");
                    Destroy(go);
                    continue;
                }

                unit.Initialize(unitData, civ);
                unit.planetIndex = usd.planetIndex;
                unit.currentTileIndex = usd.currentTileIndex;
                unit.RestoreState(usd.currentHealth, usd.experience, usd.level,
                                  usd.hasActedThisTurn, (TileLayer)usd.currentLayer);

                // Restore unit missile inventory
                if (usd.storedMissileNames != null && usd.storedMissileNames.Count > 0)
                {
                    var missileLookup = BuildAssetLookup(ResourceCache.GetAllMissiles(), m => m.missileName);
                    unit.storedMissiles.Clear();
                    foreach (var name in usd.storedMissileNames)
                        if (missileLookup.TryGetValue(name, out var md)) unit.storedMissiles.Add(md);
                }

                if (!civ.combatUnits.Contains(unit))
                    civ.combatUnits.Add(unit);
                try { unit.RegisterToRegistry(); } catch { }
                try { (TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(unit.currentTileIndex, unit.gameObject, (TileLayer)usd.currentLayer); } catch { }
            }
        }

        // Restore worker units
        if (snapshot.workerUnits != null)
        {
            foreach (var wsd in snapshot.workerUnits)
            {
                if (wsd == null) continue;
                if (!workerUnitDataLookup.TryGetValue(wsd.unitDataName, out var workerData))
                {
                    Debug.LogWarning($"[SaveLoad] WorkerUnitData '{wsd.unitDataName}' not found in ResourceCache, skipping.");
                    continue;
                }
                if (wsd.ownerCivIndex < 0 || wsd.ownerCivIndex >= allCivs.Count)
                {
                    Debug.LogWarning($"[SaveLoad] Invalid ownerCivIndex {wsd.ownerCivIndex} for worker '{wsd.unitDataName}', skipping.");
                    continue;
                }
                var civ = allCivs[wsd.ownerCivIndex];

                var workerPrefab = workerData.GetPrefab(civ);
                if (workerPrefab == null)
                {
                    Debug.LogWarning($"[SaveLoad] Prefab not found for worker '{wsd.unitDataName}', skipping.");
                    continue;
                }

                var ts = TileSystem.GetForPlanet(wsd.planetIndex) ?? TileSystem.Instance;
                Vector3 spawnPos = ts != null ? ts.GetTileCenterFlat(wsd.currentTileIndex) : new Vector3(wsd.posX, wsd.posY, wsd.posZ);

                var go = Instantiate(workerPrefab, spawnPos, Quaternion.identity);
                var pg = GetPlanetGenerator(wsd.planetIndex) ?? GetCurrentPlanetGenerator();
                if (pg != null) go.transform.SetParent(pg.transform, true);

                var worker = go.GetComponent<WorkerUnit>();
                if (worker == null)
                {
                    Debug.LogWarning($"[SaveLoad] Spawned prefab for worker '{wsd.unitDataName}' has no WorkerUnit component, skipping.");
                    Destroy(go);
                    continue;
                }

                worker.Initialize(workerData, civ, wsd.currentTileIndex);
                worker.planetIndex = wsd.planetIndex;
                worker.RestoreState(wsd.currentHealth, wsd.currentWorkPoints, wsd.currentMovePoints,
                                    (TileLayer)wsd.currentLayer);
                worker.RestoreProgression(wsd.experience, wsd.level);

                if (!civ.workerUnits.Contains(worker))
                    civ.workerUnits.Add(worker);
                try { worker.RegisterToRegistry(); } catch { }
                try { (TileOccupancyManager.GetForPlanet(worker.planetIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(worker.currentTileIndex, worker.gameObject, (TileLayer)wsd.currentLayer); } catch { }
            }
        }

        Debug.Log($"[SaveLoad] Restored {snapshot.combatUnits?.Count ?? 0} combat units and {snapshot.workerUnits?.Count ?? 0} worker units.");
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

        if (currentPlanetIndex != 0)
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

        // Animals — uses cached reference
        UpdateLoadingProgress(0.85f, "Spawning wildlife...");
var animalManagerInstance = _cachedManagers.animalManager;
if (animalManagerInstance != null)
{
    int pIndex = 0;
    var ts = TileSystem.GetForPlanet(pIndex);
    if (ts == null || !ts.IsReady())
    {
        StartCoroutine(WaitUntilTileSystemReadyAndSpawn(pIndex, animalManagerInstance));
    }
    else
    {
        animalManagerInstance.SpawnInitialAnimals();
    }
}
        else
        {
            Debug.LogWarning("GameManager: AnimalManager not found, cannot spawn initial animals.");
        }

        yield break;
    }
    */ // End DEPRECATED spawning logic
}
