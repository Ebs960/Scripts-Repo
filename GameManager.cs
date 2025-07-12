using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;
using SpaceGraphicsToolkit;
using SpaceGraphicsToolkit.Landscape;
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

    [Header("SGT Volumetrics")]
    [Tooltip("SGT Volume Manager prefab to instantiate for volumetric effects.")]
    public GameObject sgtVolumeManagerPrefab;
    private GameObject instantiatedSgtVolumeManagerGO;

    [Header("Game Settings")]
    public CivData selectedPlayerCivilizationData;
    public int numberOfCivilizations = 4;
    public int numberOfCityStates = 2;
    public int numberOfTribes = 2;
    
    // Animal prevalence: 0=dead, 1=sparse, 2=scarce, 3=normal, 4=lively, 5=bustling
    [Range(0,5)]
    public int animalPrevalence = 3;
    
    public enum MapSize { Micro, Tiny, Small, Standard, Large, Huge, Gigantic }
    [Header("Map Settings")]
    public MapSize mapSize = MapSize.Standard;
    public int moonSize = 10;   // Moon subdivisions (if enabled)
    public bool generateMoon = true;
    
    [Header("References")]
    public PlanetGenerator planetGenerator;
    public MoonGenerator moonGenerator;
    public CivilizationManager civilizationManager;
    public ClimateManager climateManager;
    public DiplomacyManager diplomacyManager;
    
    [Header("Game State")]
    public bool gameInProgress = false;
    public bool gamePaused = false;
    public int currentTurn = 0;
    
    // Events
    public event Action OnGameStarted;
    public event Action<bool> OnGamePaused;
    public event Action OnGameEnded;
    
    // Manager references
    private TurnManager turnManager;
    private DiplomacyManager dipManager;
    
    [Header("UI Prefabs")]
    public GameObject playerUIPrefab;
    public GameObject planetaryCameraPrefab; // Assign 'New Map Shit/Camera Controller.prefab'
    
    private GameObject instantiatedCameraGO; // Store reference to the instantiated camera
    
    // --- SGT-compatible tile grid and lookup ---
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

    public List<HexTileData> hexTiles = new List<HexTileData>();
    public Texture2D biomeIndexMap;
    public Texture2D heightMap;
    public int tileGridWidth;
    public int tileGridHeight;
    public int biomeCount;
    
    // --- World generation: build tile grid and maps ---
    public IEnumerator GenerateTileGridAndMaps()
    {
        hexTiles.Clear();
        // Get biome count from Biome enum
        biomeCount = System.Enum.GetValues(typeof(Biome)).Length;
        Debug.Log($"[GameManager] biomeCount set to {biomeCount}");
        // Ensure PlanetGenerator and grid exist
        if (planetGenerator == null || planetGenerator.Grid == null)
        {
            Debug.LogError("[GameManager] planetGenerator or IcoSphereGrid missing!");
            yield break;
        }
        var grid = planetGenerator.Grid;
        int numTiles = grid.TileCount;
        tileGridWidth = Mathf.CeilToInt(Mathf.Sqrt(numTiles));
        tileGridHeight = tileGridWidth; // Make square for now, or use equirectangular if needed
        Debug.Log($"[GameManager] tileGridWidth/Height set to {tileGridWidth}");
        biomeIndexMap = new Texture2D(tileGridWidth, tileGridHeight, TextureFormat.RGBA32, false);
        heightMap = new Texture2D(tileGridWidth, tileGridHeight, TextureFormat.RGBA32, false);
        for (int y = 0; y < tileGridHeight; y++)
        {
            float v = (float)y / (tileGridHeight - 1);
            float latitude = v * 180f - 90f;
            for (int x = 0; x < tileGridWidth; x++)
            {
                float u = (float)x / (tileGridWidth - 1);
                float longitude = u * 360f - 180f;
                // Find nearest tile in IcoSphereGrid
                Vector3 dir = LatLonToUnitVector(latitude, longitude);
                int tileIdx = grid.GetTileAtPosition(dir);
                if (tileIdx < 0 || tileIdx >= numTiles)
                {
                    Debug.LogWarning($"[GameManager] Invalid tileIdx {tileIdx} for lat {latitude}, lon {longitude}");
                    continue;
                }
                // Retrieve tile data (HexTileData from PlanetGenerator's data)
                var tileData = TileDataHelper.Instance != null ? TileDataHelper.Instance.GetTileData(tileIdx).tileData : null;
                if (tileData == null)
                {
                    Debug.LogWarning($"[GameManager] No HexTileData for tileIdx {tileIdx}");
                    continue;
                }
                int biomeIndex = (int)tileData.biome;
                float heightVal = tileData.elevation;
                var yields = BiomeHelper.Yields((Biome)biomeIndex);
                var tile = new HexTileData
                {
                    tileIndex = tileIdx,
                    latitude = latitude,
                    longitude = longitude,
                    u = u,
                    v = v,
                    biomeIndex = biomeIndex,
                    height = heightVal,
                    food = yields.food,
                    production = yields.prod,
                    gold = yields.gold,
                    science = yields.sci,
                    culture = yields.cult,
                    name = ((Biome)biomeIndex).ToString(),
                    centerUnitVector = dir
                };
                hexTiles.Add(tile);
                // Set biome index map (red channel normalized by biomeCount-1)
                float biomeNorm = biomeIndex / (float)(biomeCount - 1);
                biomeIndexMap.SetPixel(x, y, new Color(biomeNorm, 0, 0, 1));
                // Set heightmap (alpha channel)
                heightMap.SetPixel(x, y, new Color(0, 0, 0, heightVal));
            }

            if (y % 10 == 0)
            {
                yield return null;
            }
        }
        biomeIndexMap.Apply();
        heightMap.Apply();
        Debug.Log("[GameManager] Finished GenerateTileGridAndMaps");
    }

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
            case MapSize.Micro: subdivisions = 10; radius = 10.0f; break;
            case MapSize.Tiny: subdivisions = 14; radius = 14.0f; break;
            case MapSize.Small: subdivisions = 18; radius = 18.0f; break;
            case MapSize.Standard: subdivisions = 21; radius = 21.0f; break;
            case MapSize.Large: subdivisions = 24; radius = 24.0f; break;
            case MapSize.Huge: subdivisions = 27; radius = 27.0f; break;
            case MapSize.Gigantic: subdivisions = 30; radius = 30.0f; break;
            default: subdivisions = 21; radius = 21.0f; break;
        }
    }
    
    private void Awake()
    {
        Debug.Log("[GameManager] Awake called.");
        
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("GameManager: Singleton instance created and set to DontDestroyOnLoad");
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
            Debug.Log("GameSetupData initialized with default values");
        }
        
        // Read civilization settings from GameSetupData
        selectedPlayerCivilizationData = GameSetupData.selectedPlayerCivilizationData;
        numberOfCivilizations = GameSetupData.numberOfCivilizations;
        numberOfCityStates = GameSetupData.numberOfCityStates;
        numberOfTribes = GameSetupData.numberOfTribes;
        mapSize = GameSetupData.mapSize;
        moonSize = GameSetupData.moonSize;
        animalPrevalence = GameSetupData.animalPrevalence;
        generateMoon = GameSetupData.generateMoon;
        
        Debug.Log("=== GameManager.Awake() COMPLETED ===");
    }
    
    private void Start()
    {
        Debug.Log("[GameManager] Start called.");
        // ... existing code ...
    }
    
    /// <summary>
    /// Finds and assigns references to core managers in the current scene.
    /// Creates managers if they don't exist.
    /// This should be called after the Game scene is loaded.
    /// </summary>
    private void FindCoreManagersInScene()
    {
        // Find or create CivilizationManager
        civilizationManager = FindAnyObjectByType<CivilizationManager>();
        if (civilizationManager == null)
        {
            if (civilizationManagerPrefab != null)
            {
                Debug.Log("GameManager: CivilizationManager not found in scene, creating from prefab...");
                GameObject civManagerGO = Instantiate(civilizationManagerPrefab);
                civilizationManager = civManagerGO.GetComponent<CivilizationManager>();
            }
            else
            {
                Debug.LogError("GameManager: CivilizationManager not found and no prefab assigned!");
            }
        }
        
        // Find or create ClimateManager
        climateManager = FindAnyObjectByType<ClimateManager>();
        if (climateManager == null)
        {
            if (climateManagerPrefab != null)
            {
                Debug.Log("GameManager: ClimateManager not found in scene, creating from prefab...");
                GameObject climateManagerGO = Instantiate(climateManagerPrefab);
                climateManager = climateManagerGO.GetComponent<ClimateManager>();
            }
            else
            {
                Debug.LogError("GameManager: ClimateManager not found and no prefab assigned!");
            }
        }
        
        diplomacyManager = FindAnyObjectByType<DiplomacyManager>();
        
        // Find or create TurnManager
        turnManager = FindAnyObjectByType<TurnManager>();
        if (turnManager == null)
        {
            if (turnManagerPrefab != null)
            {
                Debug.Log("GameManager: TurnManager not found in scene, creating from prefab...");
                GameObject turnManagerGO = Instantiate(turnManagerPrefab);
                turnManager = turnManagerGO.GetComponent<TurnManager>();
            }
            else
            {
                Debug.LogError("GameManager: TurnManager not found and no prefab assigned!");
            }
        }
        
        // Find or create UnitSelectionManager
        var unitSelectionManager = FindAnyObjectByType<UnitSelectionManager>();
        if (unitSelectionManager == null)
        {
            if (unitSelectionManagerPrefab != null)
            {
                Debug.Log("GameManager: UnitSelectionManager not found in scene, creating from prefab...");
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
        var unitMovementControllerObj = FindAnyObjectByType<UnitMovementController>();
        if (unitMovementControllerObj == null)
        {
            if (unitMovementControllerPrefab != null)
            {
                Debug.Log("GameManager: UnitMovementController not found in scene, creating from prefab...");
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
        var policyManager = FindAnyObjectByType<PolicyManager>();
        if (policyManager == null)
        {
            if (policyManagerPrefab != null)
            {
                Debug.Log("GameManager: PolicyManager not found in scene, creating from prefab...");
                GameObject policyManagerGO = Instantiate(policyManagerPrefab);
                policyManager = policyManagerGO.GetComponent<PolicyManager>();
            }
            else
            {
                Debug.LogError("GameManager: PolicyManager not found and no prefab assigned!");
            }
        }
        
        // Find or create DiplomacyManager
        diplomacyManager = FindAnyObjectByType<DiplomacyManager>();
        if (diplomacyManager == null)
        {
            if (diplomacyManagerPrefab != null)
            {
                Debug.Log("GameManager: DiplomacyManager not found in scene, creating from prefab...");
                GameObject diplomacyManagerGO = Instantiate(diplomacyManagerPrefab);
                diplomacyManager = diplomacyManagerGO.GetComponent<DiplomacyManager>();
            }
            else
            {
                Debug.LogError("GameManager: DiplomacyManager not found and no prefab assigned!");
            }
        }
        
        // Find or create ResourceManager
        var resourceManager = FindAnyObjectByType<ResourceManager>();
        if (resourceManager == null)
        {
            if (resourceManagerPrefab != null)
            {
                Debug.Log("GameManager: ResourceManager not found in scene, creating from prefab...");
                GameObject resourceManagerGO = Instantiate(resourceManagerPrefab);
                resourceManager = resourceManagerGO.GetComponent<ResourceManager>();
            }
            else
            {
                Debug.LogError("GameManager: ResourceManager not found and no prefab assigned!");
            }
        }
        
        // Find or create ReligionManager
        var religionManager = FindAnyObjectByType<ReligionManager>();
        if (religionManager == null)
        {
            if (religionManagerPrefab != null)
            {
                Debug.Log("GameManager: ReligionManager not found in scene, creating from prefab...");
                GameObject religionManagerGO = Instantiate(religionManagerPrefab);
                religionManager = religionManagerGO.GetComponent<ReligionManager>();
            }
            else
            {
                Debug.LogError("GameManager: ReligionManager not found and no prefab assigned!");
            }
        }
        
        // Find or create AnimalManager
        var animalManager = FindAnyObjectByType<AnimalManager>();
        if (animalManager == null)
        {
            if (animalManagerPrefab != null)
            {
                Debug.Log("GameManager: AnimalManager not found in scene, creating from prefab...");
                GameObject animalManagerGO = Instantiate(animalManagerPrefab);
                animalManager = animalManagerGO.GetComponent<AnimalManager>();
            }
            else
            {
                Debug.LogError("GameManager: AnimalManager not found and no prefab assigned!");
            }
        }
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
            Debug.Log("[GameManager] PlanetGenerator instantiated.");

            // Ensure a PlanetForgeSphereInitializer exists, searching in children
            var initializer = planetGO.GetComponentInChildren<PlanetForgeSphereInitializer>();
            if (initializer == null)
            {
                Debug.LogWarning("PlanetForgeSphereInitializer not found on prefab or its children, adding it to the root.");
                initializer = planetGO.AddComponent<PlanetForgeSphereInitializer>();
            }

            // Assign the loading panel controller if present
            var loadingPanelController = FindAnyObjectByType<LoadingPanelController>();
            if (planetGenerator != null && loadingPanelController != null)
            {
                planetGenerator.SetLoadingPanel(loadingPanelController);
            }

            // --- Use map size preset ---
            int subdivisions; float radius;
            GetMapSizeParams(mapSize, out subdivisions, out radius);

            initializer.radius = radius;
            initializer.Setup();

            // Set SphereLandscape radius if present, searching in children
            var sphereLandscape = planetGO.GetComponentInChildren<SgtSphereLandscape>();
            if (sphereLandscape != null)
            {
                sphereLandscape.Radius = radius;
            }
            // Generate grid data using the new IcoSphereGrid system with the correct radius
            if (planetGenerator != null)
            {
                planetGenerator.subdivisions = subdivisions;
                planetGenerator.Grid.Generate(subdivisions, radius);
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
            
            Debug.Log($"PlanetGenerator created and configured from prefab with continent size {planetGenerator.maxContinentWidthDegrees}x{planetGenerator.maxContinentHeightDegrees}");
            
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
            Debug.Log("[GameManager] MoonGenerator instantiated.");
            
            // Position the moon away from the planet
            moonGO.transform.position = new Vector3(15f, 40f, 0f); // offset position
            Debug.Log("Moon positioned at distance from planet");
            
            if (moonGenerator != null)
            {
                // Configure moon generator
                moonGenerator.subdivisions = GameSetupData.moonSize;
                // Assign loading panel controller if present
                var loadingPanelController = FindAnyObjectByType<LoadingPanelController>();
                if (loadingPanelController != null)
                {
                    moonGenerator.SetLoadingPanel(loadingPanelController);
                }
                // Pass biome settings from planet generator
                if (planetGenerator != null)
                {
                    moonGenerator.SetBiomeSettings(planetGenerator.biomeSettings);
                    Debug.Log("[GameManager] MoonGenerator biomeSettings set from PlanetGenerator.");
                }
                Debug.Log("MoonGenerator created and configured from prefab");
                
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
        Debug.Log("[GameManager] StartNewGame coroutine started.");

        // --- CRITICAL: Refresh settings from GameSetupData ---
        Debug.Log("GameManager.StartNewGame(): Refreshing settings from GameSetupData.");
        selectedPlayerCivilizationData = GameSetupData.selectedPlayerCivilizationData;
        numberOfCivilizations = GameSetupData.numberOfCivilizations;
        numberOfCityStates = GameSetupData.numberOfCityStates;
        numberOfTribes = GameSetupData.numberOfTribes;
        mapSize = GameSetupData.mapSize;
        moonSize = GameSetupData.moonSize;
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
        if (unitMovementController != null && planetGenerator != null)
        {
            var grid = planetGenerator.Grid;
            unitMovementController.SetReferences(grid, planetGenerator, moonGenerator);
            Debug.Log("GameManager: Set references on UnitMovementController after generator creation.");
        }
        else if (unitMovementController == null)
        {
            Debug.LogWarning("GameManager: UnitMovementController not found after generator creation!");
        }
        else if (planetGenerator == null)
        {
            Debug.LogWarning("GameManager: PlanetGenerator is null, cannot set UnitMovementController references!");
        }
        
        // --- Camera instantiation ---
        if (planetaryCameraPrefab != null && Camera.main == null)
        {
            instantiatedCameraGO = Instantiate(planetaryCameraPrefab);
            instantiatedCameraGO.tag = "MainCamera";
            instantiatedCameraGO.SetActive(true);
            Debug.Log("GameManager: Instantiated PlanetaryCameraManager prefab as MainCamera.");

            // Ensure the camera has an AudioListener
            if (instantiatedCameraGO.GetComponent<AudioListener>() == null)
            {
                instantiatedCameraGO.AddComponent<AudioListener>();
                Debug.LogWarning("GameManager: Added missing AudioListener to the main camera.");
            }
            
            // Ensure camera has latest generator references
            var cameraManager = instantiatedCameraGO.GetComponent<PlanetaryCameraManager>();
            if (cameraManager != null)
            {
                Debug.Log("GameManager: Refreshed camera references after instantiation.");
            }

            // --- SGT Volume Manager instantiation and observer assignment ---
            if (sgtVolumeManagerPrefab != null)
            {
                var existingSgtVolumeManager = FindAnyObjectByType<SpaceGraphicsToolkit.Volumetrics.SgtVolumeManager>();
                if (existingSgtVolumeManager == null)
                {
                    instantiatedSgtVolumeManagerGO = Instantiate(sgtVolumeManagerPrefab);
                    instantiatedSgtVolumeManagerGO.name = "SgtVolumeManager";
                    Debug.Log("GameManager: Instantiated SGT Volume Manager prefab.");
                }
                else
                {
                    instantiatedSgtVolumeManagerGO = existingSgtVolumeManager.gameObject;
                }

                // Assign the observer property to the camera's Camera component
                var sgtVolumeManager = instantiatedSgtVolumeManagerGO.GetComponent<SpaceGraphicsToolkit.Volumetrics.SgtVolumeManager>();
                var cameraComponent = instantiatedCameraGO.GetComponent<Camera>();
                if (sgtVolumeManager != null && cameraComponent != null)
                {
                    sgtVolumeManager.Observer = cameraComponent;
                    Debug.Log("GameManager: Assigned planetary camera as observer to SGT Volume Manager.");
                }
                else
                {
                    Debug.LogWarning("GameManager: Could not assign observer to SGT Volume Manager (missing component).");
                }
            }
            else
            {
                Debug.LogWarning("GameManager: sgtVolumeManagerPrefab not assigned!");
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
        if (instantiatedCameraGO != null)
        {
            foreach (var landscape in FindObjectsByType<SgtSphereLandscape>(FindObjectsSortMode.None))
            {
                if (landscape != null)
                {
                    landscape.Observers.Clear();
                    landscape.Observers.Add(instantiatedCameraGO.transform);
                }
            }
        }

        // Reset game state
        currentTurn = 0;
        gameInProgress = true;
        gamePaused = false;
        
        Debug.Log("=== STARTING MAP GENERATION ===");
        
        // Generate the map (planet and optionally moon)
        if (planetGenerator != null)
        {
            yield return StartCoroutine(GenerateMap());
        }
        else
        {
            Debug.LogError("PlanetGenerator not created. Can't start game.");
            yield break;
        }
        
        Debug.Log("=== MAP GENERATION COMPLETE ===");
        
        // Spawn civilizations
        if (civilizationManager != null)
        {
            CivData playerCivData = GameSetupData.selectedPlayerCivilizationData;
            if (playerCivData == null)
            {
                Debug.LogWarning("No player civilization selected in GameSetupData. CivilizationManager will select a default.");
            }
            else
            {
                Debug.Log($"Spawning civilizations with player as: {playerCivData.civName}");
            }
            Debug.Log($"GameManager passing to SpawnCivilizations - Player: {playerCivData?.civName ?? "NULL"}, AI Count: {numberOfCivilizations}, CS Count: {numberOfCityStates}, Tribe Count: {numberOfTribes}");
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
        if (animalManagerInstance != null && planetGenerator != null)
        {
            // AnimalManager now gets grid and planet data from TileDataHelper
            animalManagerInstance.SpawnInitialAnimals();
            Debug.Log("GameManager: Initial animals spawned.");
        }
        else
        {
            Debug.LogWarning("GameManager: AnimalManager or PlanetGenerator not found, cannot spawn initial animals.");
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
    /// Handles map generation process
    /// </summary>
    private IEnumerator GenerateMap()
    {
        Debug.Log("Generating planet...");
        // Use GenerateSurface as a coroutine and wait for all map generation to finish
        yield return StartCoroutine(planetGenerator.GenerateSurface());
        
        // Generate moon if enabled
        if (generateMoon && moonGenerator != null)
        {
            Debug.Log("Generating moon...");
            // Wait a frame to ensure planet is fully initialized
            yield return null;
            // Call GenerateSurface on the moon generator as a coroutine
            yield return StartCoroutine(moonGenerator.GenerateSurface());
        }
        
        // --- NEW: Scatter biome prefabs automatically ---
        var scatterer = FindFirstObjectByType<BiomePrefabScatterer>();
        if (scatterer != null)
        {
            // Pass the loading panel controller from planetGenerator
            yield return StartCoroutine(scatterer.ScatterAllPrefabsCoroutine(planetGenerator.GetLoadingPanel()));
            Debug.Log("BiomePrefabScatterer: Prefabs scattered after map generation.");
        }
        else
        {
            Debug.LogWarning("BiomePrefabScatterer not found in scene. No biome prefabs scattered.");
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
    }
    
    public void SetPaused(bool paused)
    {
        gamePaused = paused;
        Time.timeScale = paused ? 0f : 1f;
        OnGamePaused?.Invoke(paused);
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
        
        // Return to main menu scene
        SceneManager.LoadScene("MainMenu");
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

    // Method to find and load all main managers and systems
    private void FindAndInitializeManagers()
    {
        turnManager = FindAnyObjectByType<TurnManager>();
        dipManager = FindAnyObjectByType<DiplomacyManager>();
        civilizationManager = FindAnyObjectByType<CivilizationManager>();
    }

} 