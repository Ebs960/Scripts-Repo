using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;

/// <summary>
/// Manages multiple planet scenes and solar system data.
/// Handles planet generation, scene switching, and persistence.
/// </summary>
public class SolarSystemManager : MonoBehaviour
{
    public static SolarSystemManager Instance { get; private set; }

    [Header("Solar System Configuration")]
    [Tooltip("Maximum number of planets in the solar system")]
    public int maxPlanets = 8;
    
    [Tooltip("Generate real solar system instead of procedural planets")]
    public bool useRealSolarSystem = false;
    
    [Tooltip("Add fantasy planets (like Demonic worlds) to the real solar system")]
    public bool includeFantasyPlanets = false;
    
    [Tooltip("Prefab for generating new planets")]
    public GameObject planetGeneratorPrefab;
    
    [Tooltip("Prefab for generating moons")]
    public GameObject moonGeneratorPrefab;

    [Header("Scene Management")]
    [Tooltip("Base name for planet scenes (e.g., 'Planet_')")]
    public string planetScenePrefix = "Planet_";
    
    [Tooltip("Main menu scene name")]
    public string mainMenuSceneName = "MainMenu";
    
    [Tooltip("Use dedicated 3D space map scene instead of UI overlay")]
    public bool useSpaceMapScene = false;
    
    [Tooltip("Space map scene name (only used if useSpaceMapScene is true)")]
    public string spaceMapSceneName = "SpaceMap";

    [Header("Current State")]
    public int currentPlanetIndex = 0;
    public string currentPlanetSceneName = "";

    // Planet data storage
    private Dictionary<int, PlanetSceneData> planetData = new Dictionary<int, PlanetSceneData>();
    private Dictionary<string, Scene> loadedScenes = new Dictionary<string, Scene>();
    
    // Events
    public event Action<int> OnPlanetSwitched;
    public event Action<PlanetSceneData> OnPlanetGenerated;
    public event Action OnSolarSystemInitialized;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeSolarSystem();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Initialize the solar system with default planet data
    /// </summary>
    private void InitializeSolarSystem()
    {
        if (useRealSolarSystem)
        {
            CreateRealSolarSystem();
        }
        else
        {
            CreateProceduralSolarSystem();
        }
        
        OnSolarSystemInitialized?.Invoke();
        Debug.Log($"[SolarSystemManager] Solar system initialized with {planetData.Count} celestial bodies");
    }
    
    /// <summary>
    /// Create the real solar system with accurate planet data
    /// </summary>
    private void CreateRealSolarSystem()
    {
        // Create real solar system planets
        var realPlanets = new[]
        {
            CreateRealPlanet(0, "Mercury", PlanetType.Mercury, 0.39f, GameManager.MapSize.Small, 88f, 1407.6f, 0.38f, false, "", -173f),
            CreateRealPlanet(1, "Venus", PlanetType.Venus, 0.72f, GameManager.MapSize.Standard, 225f, -5832.5f, 0.91f, true, "96.5% CO2, 3.5% N2", 464f),
            CreateRealPlanet(2, "Earth", PlanetType.Terran, 1.0f, GameManager.MapSize.Standard, 365f, 24f, 1.0f, true, "78% N2, 21% O2", 15f),
            CreateRealPlanet(3, "Mars", PlanetType.Mars, 1.52f, GameManager.MapSize.Standard, 687f, 24.6f, 0.38f, true, "95% CO2, 3% N2", -65f),
            CreateRealPlanet(4, "Jupiter", PlanetType.Jupiter, 5.2f, GameManager.MapSize.Large, 4333f, 9.9f, 2.36f, true, "89% H2, 10% He", -110f),
            CreateRealPlanet(5, "Saturn", PlanetType.Saturn, 9.5f, GameManager.MapSize.Large, 10759f, 10.7f, 0.916f, true, "96% H2, 3% He", -140f),
            CreateRealPlanet(6, "Uranus", PlanetType.Uranus, 19.2f, GameManager.MapSize.Standard, 30687f, -17.2f, 0.889f, true, "83% H2, 15% He", -195f),
            CreateRealPlanet(7, "Neptune", PlanetType.Neptune, 30.1f, GameManager.MapSize.Standard, 60190f, 16.1f, 1.13f, true, "80% H2, 19% He", -200f),
            CreateRealPlanet(8, "Pluto", PlanetType.Pluto, 39.5f, GameManager.MapSize.Small, 90560f, -153.3f, 0.071f, true, "N2, CH4, CO", -230f)
        };
        
        foreach (var planet in realPlanets)
        {
            planetData[planet.planetIndex] = planet;
        }
        
        // Add fantasy planets if enabled
        if (includeFantasyPlanets)
        {
            AddFantasyPlanets();
        }
        
        // Set Earth as home world
        planetData[2].isHomeWorld = true;
    }
    
    /// <summary>
    /// Create a real solar system planet with accurate data
    /// </summary>
    private PlanetSceneData CreateRealPlanet(int index, string name, PlanetType type, float distance, 
        GameManager.MapSize size, float orbitalPeriod, float rotationPeriod, float gravity, 
        bool hasAtmosphere, string atmosphere, float temperature)
    {
        return new PlanetSceneData
        {
            planetIndex = index,
            planetName = name,
            sceneName = planetScenePrefix + index,
            planetType = type,
            celestialBodyType = CelestialBodyType.Procedural,
            isGenerated = false,
            isCurrentlyLoaded = false,
            isHomeWorld = false,
            distanceFromStar = distance,
            planetSize = size,
            civilizations = new List<CivilizationPresence>(),
            orbitalPeriod = orbitalPeriod,
            rotationPeriod = rotationPeriod,
            gravity = gravity,
            hasAtmosphere = hasAtmosphere,
            atmosphereComposition = atmosphere,
            averageTemperature = temperature,
            description = GetPlanetDescription(type)
        };
    }
    
    /// <summary>
    /// Add fantasy planets to the real solar system (beyond Pluto)
    /// </summary>
    private void AddFantasyPlanets()
    {
        // Demonic world - a hellish planet beyond the outer solar system
        var demonicWorld = new PlanetSceneData
        {
            planetIndex = 200, // High index to avoid conflicts
            planetName = "Infernus",
            sceneName = planetScenePrefix + "200",
            planetType = PlanetType.Demonic,
            celestialBodyType = CelestialBodyType.Procedural,
            isGenerated = false,
            isCurrentlyLoaded = false,
            isHomeWorld = false,
            distanceFromStar = 50.0f, // Far beyond Pluto
            planetSize = GameManager.MapSize.Standard,
            orbitalPeriod = 120000f, // Very long orbital period
            rotationPeriod = 32.0f, // Slightly longer than Earth day
            gravity = 1.3f, // Higher gravity than Earth
            hasAtmosphere = true,
            atmosphereComposition = "Sulfur compounds, methane, carbon dioxide",
            averageTemperature = 200f, // Hot despite distance (internal heat)
            civilizations = new List<CivilizationPresence>(),
            description = GetPlanetDescription(PlanetType.Demonic)
        };
        
        planetData[200] = demonicWorld;
        
        Debug.Log("Added fantasy planets to solar system. Infernus (Demonic World) available for exploration!");
    }
    
    /// <summary>
    /// Create procedural solar system (original behavior)
    /// </summary>
    private void CreateProceduralSolarSystem()
    {
        // Create data for each potential planet
        for (int i = 0; i < maxPlanets; i++)
        {
            planetData[i] = new PlanetSceneData
            {
                planetIndex = i,
                planetName = GeneratePlanetName(i),
                sceneName = planetScenePrefix + i,
                planetType = GetRandomPlanetType(),
                celestialBodyType = CelestialBodyType.Procedural,
                isGenerated = false,
                isCurrentlyLoaded = false,
                distanceFromStar = 1.0f + (i * 0.5f), // Simple distance calculation
                civilizations = new List<CivilizationPresence>()
            };
        }
        
        // Set first planet as home world
        if (planetData.ContainsKey(0))
        {
            planetData[0].isHomeWorld = true;
            planetData[0].planetName = "Earth"; // Home world name
        }
    }

    /// <summary>
    /// Generate a unique name for a planet based on its index
    /// </summary>
    private string GeneratePlanetName(int index)
    {
        string[] planetNames = {
            "Terra Prima", "Verdania", "Crystallos", "Pyrothia", "Aquaticus", 
            "Glacialis", "Volatilis", "Mysteria", "Luminos", "Umbralith",
            "Technoterra", "Biosynth"
        };
        
        if (index < planetNames.Length)
            return planetNames[index];
        
        return $"Planet {index + 1}";
    }

    /// <summary>
    /// Get a random planet type for generation
    /// </summary>
    private PlanetType GetRandomPlanetType()
    {
        Array values = Enum.GetValues(typeof(PlanetType));
        return (PlanetType)values.GetValue(UnityEngine.Random.Range(0, values.Length));
    }

    /// <summary>
    /// Switch to a different planet scene
    /// </summary>
    public void SwitchToPlanet(int planetIndex)
    {
        if (!planetData.ContainsKey(planetIndex))
        {
            Debug.LogError($"[SolarSystemManager] Planet {planetIndex} does not exist!");
            return;
        }

        StartCoroutine(SwitchToPlanetCoroutine(planetIndex));
    }

    /// <summary>
    /// Coroutine to handle planet switching with loading
    /// </summary>
    private IEnumerator SwitchToPlanetCoroutine(int planetIndex)
    {
        PlanetSceneData targetPlanet = planetData[planetIndex];
        
        // Show loading UI
        yield return ShowLoadingScreen($"Traveling to {targetPlanet.planetName}...");

        // If planet hasn't been generated, generate it first
        if (!targetPlanet.isGenerated)
        {
            yield return GeneratePlanet(planetIndex);
        }

        // Load the planet scene
        yield return LoadPlanetScene(targetPlanet.sceneName);

        // Update current planet
        currentPlanetIndex = planetIndex;
        currentPlanetSceneName = targetPlanet.sceneName;
        
        // Mark as currently loaded
        foreach (var planet in planetData.Values)
            planet.isCurrentlyLoaded = false;
        targetPlanet.isCurrentlyLoaded = true;

        // Hide loading UI
        yield return HideLoadingScreen();

        OnPlanetSwitched?.Invoke(planetIndex);
        Debug.Log($"[SolarSystemManager] Switched to planet {planetIndex}: {targetPlanet.planetName}");
    }

    /// <summary>
    /// Generate a new planet with the specified parameters
    /// </summary>
    private IEnumerator GeneratePlanet(int planetIndex)
    {
        PlanetSceneData planet = planetData[planetIndex];
        
        Debug.Log($"[SolarSystemManager] Generating planet {planetIndex}: {planet.planetName}");
        
        // Create a new scene for this planet
        Scene planetScene = SceneManager.CreateScene(planet.sceneName);
        SceneManager.SetActiveScene(planetScene);
        
        // Store the scene reference
        loadedScenes[planet.sceneName] = planetScene;

        // Configure GameSetupData for this planet type
        ConfigureGameSetupForPlanet(planet);

        // Create GameManager for this planet
        GameObject gameManagerGO = new GameObject("GameManager");
        GameManager planetGameManager = gameManagerGO.AddComponent<GameManager>();
        
        // Move GameManager to the planet scene
        SceneManager.MoveGameObjectToScene(gameManagerGO, planetScene);

        // Wait for generation to complete
        yield return planetGameManager.StartNewGame();

        // Mark planet as generated
        planet.isGenerated = true;
        planet.generationDate = DateTime.Now;
        
        // Scan for civilizations on this planet
        yield return ScanPlanetCivilizations(planet);

        OnPlanetGenerated?.Invoke(planet);
        Debug.Log($"[SolarSystemManager] Planet {planetIndex} generation complete");
    }

    /// <summary>
    /// Configure GameSetupData based on planet type
    /// </summary>
    private void ConfigureGameSetupForPlanet(PlanetSceneData planet)
    {
        // Reset to defaults first
        GameSetupData.InitializeDefaults();
        
        // Configure based on planet type
        switch (planet.planetType)
        {
            case PlanetType.Terran:
                GameSetupData.mapTypeName = "Temperate Continental";
                GameSetupData.numberOfContinents = UnityEngine.Random.Range(4, 7);
                break;
                
            case PlanetType.Desert:
                GameSetupData.mapTypeName = "Scorched Pangaea";
                GameSetupData.numberOfContinents = UnityEngine.Random.Range(1, 3);
                GameSetupData.moistureBias = -0.3f;
                GameSetupData.temperatureBias = 0.4f;
                break;
                
            case PlanetType.Ocean:
                GameSetupData.mapTypeName = "Tropical Archipelago";
                GameSetupData.numberOfContinents = 0;
                GameSetupData.numberOfIslands = UnityEngine.Random.Range(15, 25);
                GameSetupData.moistureBias = 0.3f;
                break;
                
            case PlanetType.Ice:
                GameSetupData.mapTypeName = "Frozen Tundra";
                GameSetupData.temperatureBias = -0.5f;
                GameSetupData.moistureBias = -0.2f;
                break;
                
            case PlanetType.Volcanic:
                GameSetupData.mapTypeName = "Infernal Mountains";
                GameSetupData.temperatureBias = 0.6f;
                GameSetupData.numberOfContinents = UnityEngine.Random.Range(2, 5);
                break;
                
            case PlanetType.Jungle:
                GameSetupData.mapTypeName = "Rainforest Continental";
                GameSetupData.moistureBias = 0.4f;
                GameSetupData.temperatureBias = 0.2f;
                break;
        }
        
        // Randomize other settings
        GameSetupData.numberOfCivilizations = UnityEngine.Random.Range(3, 8);
        GameSetupData.numberOfCityStates = UnityEngine.Random.Range(1, 4);
        GameSetupData.mapSize = (GameManager.MapSize)UnityEngine.Random.Range(0, 3);
    }

    /// <summary>
    /// Scan a planet for existing civilizations
    /// </summary>
    private IEnumerator ScanPlanetCivilizations(PlanetSceneData planet)
    {
        planet.civilizations.Clear();
        
        // Find CivilizationManager in the planet scene
        CivilizationManager civManager = FindFirstObjectByType<CivilizationManager>();
        if (civManager != null && civManager.civilizations != null)
        {
            foreach (var civ in civManager.civilizations)
            {
                if (civ != null)
                {
                    planet.civilizations.Add(new CivilizationPresence
                    {
                        civilizationName = civ.civData?.civName ?? "Unknown Civilization",
                        leaderName = civ.leader?.leaderName ?? "Unknown Leader",
                        isPlayer = civ.isPlayerControlled,
                        cityCount = civ.cities?.Count ?? 0,
                        isAlive = (civ.cities?.Count > 0) || (civ.combatUnits?.Count > 0) || (civ.workerUnits?.Count > 0) // A civilization is alive if it has cities OR any units
                    });
                }
            }
        }
        
        Debug.Log($"[SolarSystemManager] Found {planet.civilizations.Count} civilizations on {planet.planetName}");
        yield return null;
    }

    /// <summary>
    /// Load a planet scene (assumes it already exists)
    /// </summary>
    private IEnumerator LoadPlanetScene(string sceneName)
    {
        if (loadedScenes.ContainsKey(sceneName))
        {
            // Scene already loaded, just activate it
            SceneManager.SetActiveScene(loadedScenes[sceneName]);
        }
        else
        {
            // Load scene from disk (if it exists)
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            yield return asyncLoad;
        }
    }

    /// <summary>
    /// Open the space map UI
    /// </summary>
    public void OpenSpaceMap()
    {
        // Load space map scene or show space map UI
        StartCoroutine(OpenSpaceMapCoroutine());
    }

    private IEnumerator OpenSpaceMapCoroutine()
    {
        yield return ShowLoadingScreen("Opening star chart...");
        
        if (useSpaceMapScene)
        {
            // Try to load space map scene
            AsyncOperation asyncLoad = null;
            try
            {
                asyncLoad = SceneManager.LoadSceneAsync(spaceMapSceneName, LoadSceneMode.Additive);
            }
            catch
            {
                asyncLoad = null;
            }
            
            if (asyncLoad != null)
            {
                yield return asyncLoad;
            }
            else
            {
                // If space map scene doesn't exist, fallback to UI overlay
                CreateSpaceMapOverlay();
            }
        }
        else
        {
            // Use UI overlay instead of scene
            CreateSpaceMapOverlay();
        }
        
        yield return HideLoadingScreen();
    }

    /// <summary>
    /// Create space map UI as overlay if scene doesn't exist
    /// </summary>
    private void CreateSpaceMapOverlay()
    {
        // Find or create SpaceMapUI
        SpaceMapUI spaceMapUI = FindFirstObjectByType<SpaceMapUI>();
        if (spaceMapUI == null)
        {
            GameObject spaceMapGO = new GameObject("SpaceMapUI");
            spaceMapUI = spaceMapGO.AddComponent<SpaceMapUI>();
        }
        
        spaceMapUI.Initialize(this);
        spaceMapUI.Show();
    }

    // Helper methods for loading screen
    private IEnumerator ShowLoadingScreen(string message)
    {
        // Try to use the enhanced loading panel system first
        LoadingPanelController loadingPanel = FindFirstObjectByType<LoadingPanelController>();
        if (loadingPanel != null)
        {
            loadingPanel.ShowLoadingAuto(message); // Auto-detects space travel
        }
        else
        {
            // Try using singleton space loading panel
            LoadingPanelController.ShowSpaceLoadingStatic(message);
            
            // Fallback to legacy system if no space loading available
            if (SpaceLoadingPanelController.Instance == null && PlanetTransitionLoader.Instance != null)
            {
                PlanetTransitionLoader.Instance.Show(GetPlanetData(currentPlanetIndex)?.planetName ?? "Unknown Planet");
                PlanetTransitionLoader.Instance.SetStatus(message);
            }
            else if (SpaceLoadingPanelController.Instance == null)
            {
                Debug.Log($"[Loading] {message}");
            }
        }
        yield return new WaitForSeconds(0.5f);
    }

    private IEnumerator HideLoadingScreen()
    {
        // Try to use the enhanced loading panel system first
        LoadingPanelController loadingPanel = FindFirstObjectByType<LoadingPanelController>();
        if (loadingPanel != null)
        {
            loadingPanel.HideAllLoading(); // Hides both regular and space loading
        }
        else
        {
            // Try using singleton space loading panel
            LoadingPanelController.HideSpaceLoadingStatic();
            
            // Fallback to legacy system
            if (SpaceLoadingPanelController.Instance == null && PlanetTransitionLoader.Instance != null)
            {
                PlanetTransitionLoader.Instance.Hide();
            }
        }
        yield return new WaitForSeconds(0.2f);
    }

    // Public API methods
    public PlanetSceneData GetPlanetData(int planetIndex)
    {
        return planetData.ContainsKey(planetIndex) ? planetData[planetIndex] : null;
    }

    public List<PlanetSceneData> GetAllPlanets()
    {
        return new List<PlanetSceneData>(planetData.Values);
    }

    public PlanetSceneData GetCurrentPlanet()
    {
        return GetPlanetData(currentPlanetIndex);
    }

    public bool IsPlanetGenerated(int planetIndex)
    {
        return planetData.ContainsKey(planetIndex) && planetData[planetIndex].isGenerated;
    }
    
    /// <summary>
    /// Get description text for a planet type
    /// </summary>
    private string GetPlanetDescription(PlanetType planetType)
    {
        switch (planetType)
        {
            // Real Solar System Bodies
            case PlanetType.Mercury:
                return "A small, scorching world with extreme temperature variations. Rich in metals but with no atmosphere for protection.";
            case PlanetType.Venus:
                return "A hellish world shrouded in thick clouds of sulfuric acid. Surface temperatures hot enough to melt lead.";
            case PlanetType.Terran:
                return "A balanced world with moderate temperatures, breathable atmosphere, and diverse biomes. Perfect for civilization.";
            case PlanetType.Mars:
                return "The red planet - a cold, dusty world with ancient riverbeds and polar ice caps. Rich in iron oxide.";
            case PlanetType.Jupiter:
                return "A massive gas giant with swirling storms and numerous moons. No solid surface but incredible atmospheric resources.";
            case PlanetType.Saturn:
                return "A beautiful ringed gas giant with low density and spectacular ring system. Rich in hydrogen and helium.";
            case PlanetType.Uranus:
                return "An ice giant tilted on its side with a cold methane atmosphere and faint ring system.";
            case PlanetType.Neptune:
                return "The windiest planet with supersonic storms and a deep blue methane atmosphere.";
            case PlanetType.Pluto:
                return "A small, frozen dwarf planet on the edge of the solar system with complex nitrogen glaciers.";
            
            // Major Moons
            case PlanetType.Luna:
                return "Earth's faithful companion with ancient impact craters and valuable mineral deposits.";
            case PlanetType.Io:
                return "The most volcanically active body in the solar system, with sulfur geysers and lava lakes.";
            case PlanetType.Europa:
                return "An icy moon hiding a vast subsurface ocean beneath its frozen shell.";
            case PlanetType.Titan:
                return "Saturn's largest moon with thick atmosphere and hydrocarbon lakes and rivers.";
            case PlanetType.Enceladus:
                return "An ice-covered moon with geysers of water erupting from its south pole.";
            case PlanetType.Ganymede:
                return "The largest moon in the solar system with its own magnetic field and subsurface ocean.";
            case PlanetType.Callisto:
                return "A heavily cratered ice-rock moon that records the early history of the solar system.";
            
            // Procedural planet types
            case PlanetType.Desert:
                return "A hot, arid world dominated by sand dunes and sparse vegetation. Water is scarce but precious.";
            case PlanetType.Ocean:
                return "A water world with vast oceans and scattered islands. Rich marine life and naval civilizations.";
            case PlanetType.Ice:
                return "A frozen world where ice and snow dominate. Hardy civilizations survive in underground shelters.";
            case PlanetType.Volcanic:
                return "A tectonically active world with frequent eruptions. Dangerous but rich in rare minerals.";
            case PlanetType.Jungle:
                return "A hot, humid world covered in dense vegetation. Diverse ecosystems but difficult to navigate.";
            case PlanetType.Rocky:
                return "A barren, mountainous world with little atmosphere. Excellent for mining operations.";
            case PlanetType.Gas:
                return "A gas giant with no solid surface. Flying cities harvest atmospheric resources.";
            case PlanetType.Demonic:
                return "A hellish realm of fire and brimstone. Ancient evils stir beneath the burning surface.";
            
            default:
                return "An unknown world waiting to be explored and understood.";
        }
    }
    
    /// <summary>
    /// Configure game setup data for a specific planet type
    /// </summary>
    public void ConfigureGameSetupForPlanet(PlanetType planetType, GameManager.MapSize mapSize)
    {
        // Set the map size
        GameSetupData.mapSize = mapSize;
        
        // Configure based on planet type using real PlanetGenerator parameters
        switch (planetType)
        {
            case PlanetType.Mercury:
                GameSetupData.mapSize = GameManager.MapSize.Small;
                GameSetupData.temperatureBias = 0.65f; // Extremely hot
                GameSetupData.moistureBias = -0.3f; // No atmosphere, very dry
                GameSetupData.landThreshold = 0.2f; // Mostly cratered surface
                break;
                
            case PlanetType.Venus:
                GameSetupData.temperatureBias = 0.65f; // Hellishly hot
                GameSetupData.moistureBias = -0.25f; // Acid clouds but surface is dry
                GameSetupData.landThreshold = 0.35f; // Mostly solid surface
                GameSetupData.isInfernalWorld = true; // Gets volcanic/steam biomes
                break;
                
            case PlanetType.Terran:
                GameSetupData.temperatureBias = 0.0f; // Earth-like moderate
                GameSetupData.moistureBias = 0.0f; // Earth-like balanced
                GameSetupData.landThreshold = 0.4f; // Earth-like land/water ratio
                GameSetupData.numberOfContinents = 6; // Earth-like continents
                break;
                
            case PlanetType.Mars:
                GameSetupData.temperatureBias = -0.5f; // Very cold
                GameSetupData.moistureBias = -0.2f; // Dry, thin atmosphere
                GameSetupData.landThreshold = 0.15f; // Mostly solid surface, some polar ice
                GameSetupData.numberOfContinents = 2; // Distinctive hemispheres
                break;
                
            case PlanetType.Jupiter:
                GameSetupData.temperatureBias = -0.6f; // Very cold
                GameSetupData.moistureBias = 0.3f; // Dense gas atmosphere
                GameSetupData.landThreshold = 0.0f; // No solid surface
                GameSetupData.mapSize = GameManager.MapSize.Large;
                break;
                
            case PlanetType.Saturn:
                GameSetupData.temperatureBias = -0.65f; // Extremely cold
                GameSetupData.moistureBias = 0.3f; // Dense gas atmosphere
                GameSetupData.landThreshold = 0.0f; // No solid surface
                GameSetupData.mapSize = GameManager.MapSize.Large;
                break;
                
            case PlanetType.Uranus:
            case PlanetType.Neptune:
                GameSetupData.temperatureBias = -0.65f; // Ice giants are extremely cold
                GameSetupData.moistureBias = 0.2f; // Ice and gas
                GameSetupData.landThreshold = 0.1f; // Mostly gas with some ice
                break;
                
            case PlanetType.Pluto:
                GameSetupData.mapSize = GameManager.MapSize.Small;
                GameSetupData.temperatureBias = -0.65f; // Extremely cold
                GameSetupData.moistureBias = -0.1f; // Some nitrogen ice
                GameSetupData.landThreshold = 0.3f; // Solid surface with varied terrain
                GameSetupData.isIceWorld = true; // Gets ice world biomes
                break;
                
            case PlanetType.Luna:
                GameSetupData.mapSize = GameManager.MapSize.Small;
                GameSetupData.temperatureBias = -0.3f; // Cold but varies with day/night
                GameSetupData.moistureBias = -0.3f; // No atmosphere or water
                GameSetupData.landThreshold = 0.35f; // Solid surface
                break;
                
            case PlanetType.Io:
                GameSetupData.mapSize = GameManager.MapSize.Small;
                GameSetupData.temperatureBias = -0.4f; // Cold despite volcanism
                GameSetupData.moistureBias = -0.25f; // Sulfur, not water
                GameSetupData.landThreshold = 0.4f; // Solid volcanic surface
                GameSetupData.isInfernalWorld = true; // Volcanic world
                break;
                
            case PlanetType.Europa:
                GameSetupData.mapSize = GameManager.MapSize.Small;
                GameSetupData.temperatureBias = -0.6f; // Very cold
                GameSetupData.moistureBias = 0.1f; // Subsurface ocean
                GameSetupData.landThreshold = 0.6f; // Ice shell over ocean
                GameSetupData.isIceWorld = true; // Ice world
                break;
                
            case PlanetType.Titan:
                GameSetupData.temperatureBias = -0.6f; // Very cold
                GameSetupData.moistureBias = 0.15f; // Hydrocarbon lakes
                GameSetupData.landThreshold = 0.45f; // Mix of land and lakes
                break;
                
            case PlanetType.Enceladus:
                GameSetupData.mapSize = GameManager.MapSize.Small;
                GameSetupData.temperatureBias = -0.65f; // Extremely cold
                GameSetupData.moistureBias = 0.0f; // Ice and geysers
                GameSetupData.landThreshold = 0.5f; // Ice surface
                GameSetupData.isIceWorld = true; // Ice world
                break;
                
            case PlanetType.Ganymede:
            case PlanetType.Callisto:
                GameSetupData.temperatureBias = -0.6f; // Very cold
                GameSetupData.moistureBias = -0.1f; // Ice and rock
                GameSetupData.landThreshold = 0.4f; // Mixed ice/rock surface
                GameSetupData.isIceWorld = true; // Ice world
                break;
                
            // Procedural planet defaults
            case PlanetType.Desert:
                GameSetupData.temperatureBias = 0.3f; // Hot
                GameSetupData.moistureBias = -0.2f; // Dry
                GameSetupData.landThreshold = 0.5f; // Mostly land
                break;
                
            case PlanetType.Ocean:
                GameSetupData.temperatureBias = 0.0f; // Moderate
                GameSetupData.moistureBias = 0.25f; // Very wet
                GameSetupData.landThreshold = 0.2f; // Mostly water
                break;
                
            case PlanetType.Ice:
                GameSetupData.temperatureBias = -0.4f; // Cold
                GameSetupData.moistureBias = 0.1f; // Frozen water
                GameSetupData.landThreshold = 0.4f; // Mix of ice and land
                GameSetupData.isIceWorld = true; // Ice world biomes
                break;
                
            case PlanetType.Volcanic:
                GameSetupData.temperatureBias = 0.4f; // Hot
                GameSetupData.moistureBias = 0.0f; // Variable
                GameSetupData.landThreshold = 0.45f; // Land with volcanic features
                GameSetupData.isInfernalWorld = true; // Volcanic world
                break;
                
            case PlanetType.Jungle:
                GameSetupData.temperatureBias = 0.2f; // Warm
                GameSetupData.moistureBias = 0.3f; // Very wet
                GameSetupData.landThreshold = 0.45f; // Good land/water balance
                GameSetupData.isRainforestWorld = true; // Enhanced jungle biomes
                break;
                
            case PlanetType.Rocky:
                GameSetupData.temperatureBias = -0.1f; // Cool
                GameSetupData.moistureBias = -0.1f; // Dry
                GameSetupData.landThreshold = 0.5f; // Mostly rocky land
                break;
                
            case PlanetType.Gas:
                GameSetupData.temperatureBias = -0.3f; // Cold
                GameSetupData.moistureBias = 0.3f; // Dense atmosphere
                GameSetupData.landThreshold = 0.0f; // No solid surface
                GameSetupData.mapSize = GameManager.MapSize.Large;
                break;
                
            case PlanetType.Demonic:
                GameSetupData.temperatureBias = 0.65f; // Hellishly hot
                GameSetupData.moistureBias = -0.15f; // Dry hellscape with occasional lava
                GameSetupData.landThreshold = 0.45f; // Solid hellish terrain
                GameSetupData.isDemonicWorld = true; // Enable demonic map features
                break;
                
            default:
                // Default balanced settings
                GameSetupData.temperatureBias = 0.0f;
                GameSetupData.moistureBias = 0.0f;
                GameSetupData.landThreshold = 0.4f;
                break;
        }
    }
}

/// <summary>
/// Data structure for storing planet information
/// </summary>
[System.Serializable]
public class PlanetSceneData
{
    public int planetIndex;
    public string planetName;
    public string sceneName;
    public PlanetType planetType;
    public CelestialBodyType celestialBodyType;
    public bool isGenerated;
    public bool isCurrentlyLoaded;
    public bool isHomeWorld;
    public float distanceFromStar;
    public DateTime generationDate;
    public List<CivilizationPresence> civilizations = new List<CivilizationPresence>();
    
    // Additional planet properties
    public GameManager.MapSize planetSize;
    public int totalTiles;
    public string mapTypeName;
    public string description;
    
    // Real planet specific data
    public float orbitalPeriod;     // Days to orbit sun
    public float rotationPeriod;    // Hours for one day
    public float gravity;           // Relative to Earth
    public bool hasAtmosphere;
    public string atmosphereComposition;
    public float averageTemperature; // Celsius
}

/// <summary>
/// Information about civilizations present on a planet
/// </summary>
[System.Serializable]
public class CivilizationPresence
{
    public string civilizationName;
    public string leaderName;
    public bool isPlayer;
    public int cityCount;
    public bool isAlive;
    public string status; // "Thriving", "Struggling", "Extinct", etc.
}

/// <summary>
/// Types of planets that can be generated
/// </summary>
public enum PlanetType
{
    // Procedural planet types
    Terran,     // Earth-like, balanced
    Desert,     // Hot and dry
    Ocean,      // Mostly water
    Ice,        // Cold and frozen
    Volcanic,   // Hot with lots of mountains
    Jungle,     // Hot and humid
    Rocky,      // Barren and mountainous
    Gas,        // Gas giant (no surface)
    
    // Real Solar System Bodies
    Mercury,    // Closest to sun, extreme temperatures
    Venus,      // Acidic atmosphere, volcanic
    Earth,      // Home world (if using real solar system)
    Mars,       // Red planet, dusty and cold
    Jupiter,    // Gas giant with storms
    Saturn,     // Ringed gas giant
    Uranus,     // Ice giant
    Neptune,    // Windy ice giant
    Pluto,      // Dwarf planet, frozen
    
    // Major Moons
    Luna,       // Earth's moon
    Io,         // Volcanic moon of Jupiter
    Europa,     // Icy moon with subsurface ocean
    Titan,      // Saturn's moon with lakes
    Enceladus,  // Saturn's icy moon
    Ganymede,   // Largest moon in solar system
    Callisto,   // Heavily cratered moon
    
    // Special/Fantasy Planet Types
    Demonic     // Hellish planet with demonic features
}

/// <summary>
/// Determines how a celestial body should be generated
/// </summary>
public enum CelestialBodyType
{
    Procedural,     // Generated using standard algorithms
    RealPlanet      // Based on real solar system data
}