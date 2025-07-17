using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;
using SpaceGraphicsToolkit;
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

    [Header("Game State")]
    public bool gameInProgress = false;
    public bool gamePaused = false;
    public int currentTurn = 0;

    // Events
    public event Action OnGameStarted;
    public event Action<bool> OnGamePaused;
    public event Action OnGameEnded;

    // Manager references
    public TurnManager turnManager;
    public DiplomacyManager dipManager;

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

    // --- References to high-res planet textures and grid ---
    public Texture2D planetHeightTex;
    public Texture2D planetAlbedoTex;
    public SphericalHexGrid planetGrid;

    /// <summary>
    /// Called by PlanetGenerator to provide the high-res textures and grid after generation.
    /// </summary>
    public void SetPlanetTextures(Texture2D height, Texture2D albedo, SphericalHexGrid grid)
    {
        planetHeightTex = height;
        planetAlbedoTex = albedo;
        planetGrid = grid;
        Debug.Log($"[GameManager] SetPlanetTextures: height={height?.width}x{height?.height}, albedo={albedo?.width}x{albedo?.height}, grid tiles={grid?.TileCount}");
    }

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
            case MapSize.Small:    subdivisions = 3; radius = 20f; break;   // 162 tiles
            case MapSize.Standard: subdivisions = 4; radius = 25f; break;   // 642 tiles
            case MapSize.Large:    subdivisions = 5; radius = 30f; break;   // 2 562 tiles
            default:               subdivisions = 4; radius = 25f; break;
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
                
                // Configure the hexasphere renderer for the new system
                if (planetGenerator.hexasphereRenderer != null)
                {
                    planetGenerator.hexasphereRenderer.generatorSource = planetGenerator;
                    planetGenerator.hexasphereRenderer.usePerTileBiomeData = true;
                    planetGenerator.hexasphereRenderer.useSeparateVertices = true;
                }
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
                // Configure moon generator with the same subdivision as planet
                GetMapSizeParams(mapSize, out int moonSubdivisions, out float planetRadius);
                float moonRadius = planetRadius / 5f;

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
                }
                
                // Configure moon with correct radius and subdivisions
                moonGenerator.ConfigureMoon(moonSubdivisions, moonRadius);
                
                // Rebuild moon mesh with correct radius
                if (moonGenerator.hexasphereRenderer != null)
                {
                    moonGenerator.hexasphereRenderer.generatorSource = moonGenerator;
                    moonGenerator.hexasphereRenderer.BuildMesh(moonGenerator.Grid);
                }

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



