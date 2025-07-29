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
    
    [Tooltip("Prefab for generating new planets")]
    public GameObject planetGeneratorPrefab;
    
    [Tooltip("Prefab for generating moons")]
    public GameObject moonGeneratorPrefab;

    [Header("Scene Management")]
    [Tooltip("Base name for planet scenes (e.g., 'Planet_')")]
    public string planetScenePrefix = "Planet_";
    
    [Tooltip("Main menu scene name")]
    public string mainMenuSceneName = "MainMenu";
    
    [Tooltip("Space map scene name")]
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
        
        // Add major moons
        AddRealMoons();
        
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
            celestialBodyType = CelestialBodyType.RealPlanet,
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
    /// Add major moons to the real solar system
    /// </summary>
    private void AddRealMoons()
    {
        // Earth's Moon
        var luna = CreateRealMoon(100, "Luna", PlanetType.Luna, 2, GameManager.MapSize.Small);
        planetData[2].moons.Add(luna);
        
        // Jupiter's major moons
        var io = CreateRealMoon(101, "Io", PlanetType.Io, 4, GameManager.MapSize.Small);
        var europa = CreateRealMoon(102, "Europa", PlanetType.Europa, 4, GameManager.MapSize.Small);
        var ganymede = CreateRealMoon(103, "Ganymede", PlanetType.Ganymede, 4, GameManager.MapSize.Standard);
        var callisto = CreateRealMoon(104, "Callisto", PlanetType.Callisto, 4, GameManager.MapSize.Small);
        
        planetData[4].moons.AddRange(new[] { io, europa, ganymede, callisto });
        
        // Saturn's major moons
        var titan = CreateRealMoon(105, "Titan", PlanetType.Titan, 5, GameManager.MapSize.Standard);
        var enceladus = CreateRealMoon(106, "Enceladus", PlanetType.Enceladus, 5, GameManager.MapSize.Small);
        
        planetData[5].moons.AddRange(new[] { titan, enceladus });
    }
    
    /// <summary>
    /// Create a real moon with specific properties
    /// </summary>
    private PlanetSceneData CreateRealMoon(int index, string name, PlanetType type, int parentPlanet, GameManager.MapSize size)
    {
        return new PlanetSceneData
        {
            planetIndex = index,
            planetName = name,
            sceneName = planetScenePrefix + index,
            planetType = type,
            celestialBodyType = CelestialBodyType.RealMoon,
            isGenerated = false,
            isCurrentlyLoaded = false,
            isHomeWorld = false,
            distanceFromStar = planetData[parentPlanet].distanceFromStar,
            planetSize = size,
            civilizations = new List<CivilizationPresence>(),
            description = GetPlanetDescription(type)
        };
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
            planetData[0].planetName = "Terra Prima"; // Home world name
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
        CivilizationManager civManager = FindObjectOfType<CivilizationManager>();
        if (civManager != null && civManager.civilizations != null)
        {
            foreach (var civ in civManager.civilizations)
            {
                if (civ != null)
                {
                    planet.civilizations.Add(new CivilizationPresence
                    {
                        civilizationName = civ.civilizationName,
                        leaderName = civ.leaderName,
                        isPlayer = civ.isPlayer,
                        cityCount = civ.cities?.Count ?? 0,
                        isAlive = civ.isAlive
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
        
        // Try to load space map scene, or create UI overlay
        try
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(spaceMapSceneName, LoadSceneMode.Additive);
            yield return asyncLoad;
        }
        catch
        {
            // If space map scene doesn't exist, create UI overlay instead
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
        SpaceMapUI spaceMapUI = FindObjectOfType<SpaceMapUI>();
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
        if (PlanetTransitionLoader.Instance != null)
        {
            PlanetTransitionLoader.Instance.Show(GetPlanetData(currentPlanetIndex)?.planetName ?? "Unknown Planet");
            PlanetTransitionLoader.Instance.SetStatus(message);
        }
        else
        {
            Debug.Log($"[Loading] {message}");
        }
        yield return new WaitForSeconds(0.5f);
    }

    private IEnumerator HideLoadingScreen()
    {
        if (PlanetTransitionLoader.Instance != null)
        {
            PlanetTransitionLoader.Instance.Hide();
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
            
            default:
                return "An unknown world waiting to be explored and understood.";
        }
    }
    
    /// <summary>
    /// Configure game setup data for a specific planet type
    /// </summary>
    public GameSetupData ConfigureGameSetupForPlanet(PlanetType planetType, GameManager.MapSize mapSize)
    {
        var gameSetup = new GameSetupData();
        
        // Set the map size
        gameSetup.mapSize = mapSize;
        
        // Configure based on planet type using real PlanetGenerator parameters
        switch (planetType)
        {
            case PlanetType.Mercury:
                gameSetup.mapSize = GameManager.MapSize.Small;
                gameSetup.temperatureBias = 0.65f; // Extremely hot
                gameSetup.moistureBias = -0.3f; // No atmosphere, very dry
                gameSetup.landThreshold = 0.2f; // Mostly cratered surface
                gameSetup.mountainThreshold = 0.7f; // Many craters = mountains
                gameSetup.hillThreshold = 0.5f; // Rough terrain
                break;
                
            case PlanetType.Venus:
                gameSetup.temperatureBias = 0.65f; // Hellishly hot
                gameSetup.moistureBias = -0.25f; // Acid clouds but surface is dry
                gameSetup.landThreshold = 0.35f; // Mostly solid surface
                gameSetup.mountainThreshold = 0.75f; // Volcanic mountains
                gameSetup.hillThreshold = 0.6f; // Rough volcanic terrain
                gameSetup.isInfernalWorld = true; // Gets volcanic/steam biomes
                break;
                
            case PlanetType.Terran:
                gameSetup.temperatureBias = 0.0f; // Earth-like moderate
                gameSetup.moistureBias = 0.0f; // Earth-like balanced
                gameSetup.landThreshold = 0.4f; // Earth-like land/water ratio
                gameSetup.mountainThreshold = 0.8f; // Standard mountains
                gameSetup.hillThreshold = 0.6f; // Standard hills
                gameSetup.numberOfContinents = 6; // Earth-like continents
                break;
                
            case PlanetType.Mars:
                gameSetup.temperatureBias = -0.5f; // Very cold
                gameSetup.moistureBias = -0.2f; // Dry, thin atmosphere
                gameSetup.landThreshold = 0.15f; // Mostly solid surface, some polar ice
                gameSetup.mountainThreshold = 0.7f; // Olympus Mons and canyons
                gameSetup.hillThreshold = 0.5f; // Varied terrain
                gameSetup.numberOfContinents = 2; // Distinctive hemispheres
                break;
                
            case PlanetType.Jupiter:
                gameSetup.temperatureBias = -0.6f; // Very cold
                gameSetup.moistureBias = 0.3f; // Dense gas atmosphere
                gameSetup.landThreshold = 0.0f; // No solid surface
                gameSetup.mountainThreshold = 1.0f; // No mountains
                gameSetup.hillThreshold = 1.0f; // No hills
                gameSetup.mapSize = GameManager.MapSize.Large;
                break;
                
            case PlanetType.Saturn:
                gameSetup.temperatureBias = -0.65f; // Extremely cold
                gameSetup.moistureBias = 0.3f; // Dense gas atmosphere
                gameSetup.landThreshold = 0.0f; // No solid surface
                gameSetup.mountainThreshold = 1.0f; // No mountains
                gameSetup.hillThreshold = 1.0f; // No hills
                gameSetup.mapSize = GameManager.MapSize.Large;
                break;
                
            case PlanetType.Uranus:
            case PlanetType.Neptune:
                gameSetup.temperatureBias = -0.65f; // Ice giants are extremely cold
                gameSetup.moistureBias = 0.2f; // Ice and gas
                gameSetup.landThreshold = 0.1f; // Mostly gas with some ice
                gameSetup.mountainThreshold = 0.9f; // Ice formations
                gameSetup.hillThreshold = 0.7f; // Ice terrain
                break;
                
            case PlanetType.Pluto:
                gameSetup.mapSize = GameManager.MapSize.Small;
                gameSetup.temperatureBias = -0.65f; // Extremely cold
                gameSetup.moistureBias = -0.1f; // Some nitrogen ice
                gameSetup.landThreshold = 0.3f; // Solid surface with varied terrain
                gameSetup.mountainThreshold = 0.8f; // Some terrain variation
                gameSetup.hillThreshold = 0.6f; // Gentle ice features
                gameSetup.isIceWorld = true; // Gets ice world biomes
                break;
                
            case PlanetType.Luna:
                gameSetup.mapSize = GameManager.MapSize.Small;
                gameSetup.temperatureBias = -0.3f; // Cold but varies with day/night
                gameSetup.moistureBias = -0.3f; // No atmosphere or water
                gameSetup.landThreshold = 0.35f; // Solid surface
                gameSetup.mountainThreshold = 0.6f; // Many crater rims
                gameSetup.hillThreshold = 0.4f; // Crater and mare terrain
                break;
                
            case PlanetType.Io:
                gameSetup.mapSize = GameManager.MapSize.Small;
                gameSetup.temperatureBias = -0.4f; // Cold despite volcanism
                gameSetup.moistureBias = -0.25f; // Sulfur, not water
                gameSetup.landThreshold = 0.4f; // Solid volcanic surface
                gameSetup.mountainThreshold = 0.5f; // Active volcanoes
                gameSetup.hillThreshold = 0.3f; // Constant volcanic activity
                gameSetup.isInfernalWorld = true; // Volcanic world
                break;
                
            case PlanetType.Europa:
                gameSetup.mapSize = GameManager.MapSize.Small;
                gameSetup.temperatureBias = -0.6f; // Very cold
                gameSetup.moistureBias = 0.1f; // Subsurface ocean
                gameSetup.landThreshold = 0.6f; // Ice shell over ocean
                gameSetup.mountainThreshold = 0.9f; // Smooth ice surface
                gameSetup.hillThreshold = 0.8f; // Some ice ridges
                gameSetup.isIceWorld = true; // Ice world
                break;
                
            case PlanetType.Titan:
                gameSetup.temperatureBias = -0.6f; // Very cold
                gameSetup.moistureBias = 0.15f; // Hydrocarbon lakes
                gameSetup.landThreshold = 0.45f; // Mix of land and lakes
                gameSetup.mountainThreshold = 0.85f; // Some terrain variation
                gameSetup.hillThreshold = 0.7f; // Gentle terrain
                break;
                
            case PlanetType.Enceladus:
                gameSetup.mapSize = GameManager.MapSize.Small;
                gameSetup.temperatureBias = -0.65f; // Extremely cold
                gameSetup.moistureBias = 0.0f; // Ice and geysers
                gameSetup.landThreshold = 0.5f; // Ice surface
                gameSetup.mountainThreshold = 0.9f; // Smooth with some ridges
                gameSetup.hillThreshold = 0.8f; // Gentle ice terrain
                gameSetup.isIceWorld = true; // Ice world
                break;
                
            case PlanetType.Ganymede:
            case PlanetType.Callisto:
                gameSetup.temperatureBias = -0.6f; // Very cold
                gameSetup.moistureBias = -0.1f; // Ice and rock
                gameSetup.landThreshold = 0.4f; // Mixed ice/rock surface
                gameSetup.mountainThreshold = 0.75f; // Crater rims and ridges
                gameSetup.hillThreshold = 0.6f; // Varied icy terrain
                gameSetup.isIceWorld = true; // Ice world
                break;
                
            // Procedural planet defaults
            case PlanetType.Desert:
                gameSetup.temperatureBias = 0.3f; // Hot
                gameSetup.moistureBias = -0.2f; // Dry
                gameSetup.landThreshold = 0.5f; // Mostly land
                gameSetup.mountainThreshold = 0.8f; // Some mountains
                gameSetup.hillThreshold = 0.6f; // Rolling dunes
                break;
                
            case PlanetType.Ocean:
                gameSetup.temperatureBias = 0.0f; // Moderate
                gameSetup.moistureBias = 0.25f; // Very wet
                gameSetup.landThreshold = 0.2f; // Mostly water
                gameSetup.mountainThreshold = 0.85f; // Island peaks
                gameSetup.hillThreshold = 0.7f; // Archipelago terrain
                break;
                
            case PlanetType.Ice:
                gameSetup.temperatureBias = -0.4f; // Cold
                gameSetup.moistureBias = 0.1f; // Frozen water
                gameSetup.landThreshold = 0.4f; // Mix of ice and land
                gameSetup.mountainThreshold = 0.8f; // Ice mountains
                gameSetup.hillThreshold = 0.6f; // Ice hills
                gameSetup.isIceWorld = true; // Ice world biomes
                break;
                
            case PlanetType.Volcanic:
                gameSetup.temperatureBias = 0.4f; // Hot
                gameSetup.moistureBias = 0.0f; // Variable
                gameSetup.landThreshold = 0.45f; // Land with volcanic features
                gameSetup.mountainThreshold = 0.6f; // Many volcanoes
                gameSetup.hillThreshold = 0.4f; // Volcanic terrain
                gameSetup.isInfernalWorld = true; // Volcanic world
                break;
                
            case PlanetType.Jungle:
                gameSetup.temperatureBias = 0.2f; // Warm
                gameSetup.moistureBias = 0.3f; // Very wet
                gameSetup.landThreshold = 0.45f; // Good land/water balance
                gameSetup.mountainThreshold = 0.85f; // Some peaks
                gameSetup.hillThreshold = 0.7f; // Rolling jungle
                gameSetup.isRainforestWorld = true; // Enhanced jungle biomes
                break;
                
            case PlanetType.Rocky:
                gameSetup.temperatureBias = -0.1f; // Cool
                gameSetup.moistureBias = -0.1f; // Dry
                gameSetup.landThreshold = 0.5f; // Mostly rocky land
                gameSetup.mountainThreshold = 0.7f; // Many mountains
                gameSetup.hillThreshold = 0.5f; // Very rocky
                break;
                
            case PlanetType.Gas:
                gameSetup.temperatureBias = -0.3f; // Cold
                gameSetup.moistureBias = 0.3f; // Dense atmosphere
                gameSetup.landThreshold = 0.0f; // No solid surface
                gameSetup.mountainThreshold = 1.0f; // No mountains
                gameSetup.hillThreshold = 1.0f; // No hills
                gameSetup.mapSize = GameManager.MapSize.Large;
                break;
                
            default:
                // Default balanced settings
                gameSetup.temperatureBias = 0.0f;
                gameSetup.moistureBias = 0.0f;
                gameSetup.landThreshold = 0.4f;
                gameSetup.mountainThreshold = 0.8f;
                gameSetup.hillThreshold = 0.6f;
                break;
        }
        
        return gameSetup;
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
    public List<PlanetSceneData> moons = new List<PlanetSceneData>(); // For planets with moons
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
    Callisto    // Heavily cratered moon
}

/// <summary>
/// Determines how a celestial body should be generated
/// </summary>
public enum CelestialBodyType
{
    Procedural,     // Generated using standard algorithms
    RealPlanet,     // Based on real solar system data
    RealMoon        // Moon of a real planet
}