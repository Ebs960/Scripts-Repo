using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

/// <summary>
/// Custom UI for the space map showing planets and civilizations
/// </summary>
public class SpaceMapUI : MonoBehaviour
{
    [Header("UI References")]
    public Canvas spaceMapCanvas;
    public GameObject spaceMapPanel;
    public Button closeButton;
    public TextMeshProUGUI titleText;
    
    [Header("Planet Display")]
    public Transform planetContainer;
    public GameObject planetButtonPrefab;
    public RectTransform solarSystemView;
    
    [Header("Planet Info Panel")]
    public GameObject planetInfoPanel;
    public TextMeshProUGUI planetNameText;
    public TextMeshProUGUI planetTypeText;
    public TextMeshProUGUI planetStatusText;
    public TextMeshProUGUI distanceText;
    public Image planetIcon;
    public Button travelButton;
    public Button cancelButton;
    
    [Header("Civilization List")]
    public Transform civilizationContainer;
    public GameObject civilizationEntryPrefab;
    public TextMeshProUGUI noCivilizationsText;
    
    [Header("Visual Settings")]
    public float planetSpacing = 100f;
    public Color homeWorldColor = Color.yellow;
    public Color visitedPlanetColor = Color.green;
    public Color unvisitedPlanetColor = Color.gray;
    public Color currentPlanetColor = Color.cyan;

    private SolarSystemManager solarSystemManager;
    private List<PlanetButton> planetButtons = new List<PlanetButton>();
    private PlanetSceneData selectedPlanet;
    private bool isInitialized = false;

    void Awake()
    {
        SetupUIReferences();
        
        // IMPORTANT: Hide the space map UI immediately
        Hide();
    }

    /// <summary>
    /// Setup UI references if not assigned in inspector
    /// </summary>
    private void SetupUIReferences()
    {
        if (spaceMapCanvas == null)
        {
            spaceMapCanvas = GetComponent<Canvas>();
            if (spaceMapCanvas == null)
            {
                spaceMapCanvas = gameObject.AddComponent<Canvas>();
                spaceMapCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                spaceMapCanvas.sortingOrder = 100; // High priority overlay
            }
        }

        if (spaceMapPanel == null)
        {
            CreateSpaceMapPanel();
        }
    }

    /// <summary>
    /// Create the main space map panel programmatically
    /// </summary>
    private void CreateSpaceMapPanel()
    {
        // Main panel
        spaceMapPanel = CreateUIElement("SpaceMapPanel", spaceMapCanvas.transform);
        spaceMapPanel.AddComponent<Image>().color = new Color(0, 0, 0.2f, 0.9f); // Dark blue background
        
        RectTransform panelRect = spaceMapPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Title
        GameObject titleGO = CreateUIElement("Title", spaceMapPanel.transform);
        titleText = titleGO.AddComponent<TextMeshProUGUI>();
        titleText.text = "Solar System Map";
        titleText.fontSize = 36;
        titleText.alignment = TextAlignmentOptions.Center;
        
        RectTransform titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 0.9f);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        // Close button
        GameObject closeGO = CreateUIElement("CloseButton", spaceMapPanel.transform);
        closeButton = closeGO.AddComponent<Button>();
        closeGO.AddComponent<Image>().color = Color.red;
        
        TextMeshProUGUI closeText = CreateUIElement("Text", closeGO.transform).AddComponent<TextMeshProUGUI>();
        closeText.text = "X";
        closeText.fontSize = 24;
        closeText.alignment = TextAlignmentOptions.Center;
        closeText.color = Color.white;
        
        RectTransform closeRect = closeGO.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(0.95f, 0.9f);
        closeRect.anchorMax = new Vector2(1, 1);
        closeRect.offsetMin = Vector2.zero;
        closeRect.offsetMax = Vector2.zero;

        // Solar system view
        GameObject solarViewGO = CreateUIElement("SolarSystemView", spaceMapPanel.transform);
        solarSystemView = solarViewGO.GetComponent<RectTransform>();
        solarSystemView.anchorMin = new Vector2(0, 0.4f);
        solarSystemView.anchorMax = new Vector2(1, 0.9f);
        solarSystemView.offsetMin = Vector2.zero;
        solarSystemView.offsetMax = Vector2.zero;

        // Planet container
        GameObject planetContainerGO = CreateUIElement("PlanetContainer", solarSystemView);
        planetContainer = planetContainerGO.transform;
        
        // Planet info panel
        CreatePlanetInfoPanel();
        
        // Setup button events
        closeButton.onClick.AddListener(Hide);
    }

    /// <summary>
    /// Create planet info panel
    /// </summary>
    private void CreatePlanetInfoPanel()
    {
        planetInfoPanel = CreateUIElement("PlanetInfoPanel", spaceMapPanel.transform);
        planetInfoPanel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.3f, 0.8f);
        
        RectTransform infoRect = planetInfoPanel.GetComponent<RectTransform>();
        infoRect.anchorMin = new Vector2(0, 0);
        infoRect.anchorMax = new Vector2(1, 0.4f);
        infoRect.offsetMin = Vector2.zero;
        infoRect.offsetMax = Vector2.zero;

        // Planet name
        GameObject nameGO = CreateUIElement("PlanetName", planetInfoPanel.transform);
        planetNameText = nameGO.AddComponent<TextMeshProUGUI>();
        planetNameText.fontSize = 28;
        planetNameText.alignment = TextAlignmentOptions.Center;
        
        RectTransform nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.8f);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;

        // Travel button
        GameObject travelGO = CreateUIElement("TravelButton", planetInfoPanel.transform);
        travelButton = travelGO.AddComponent<Button>();
        travelGO.AddComponent<Image>().color = Color.green;
        
        TextMeshProUGUI travelText = CreateUIElement("Text", travelGO.transform).AddComponent<TextMeshProUGUI>();
        travelText.text = "Travel to Planet";
        travelText.fontSize = 18;
        travelText.alignment = TextAlignmentOptions.Center;
        travelText.color = Color.white;
        
        RectTransform travelRect = travelGO.GetComponent<RectTransform>();
        travelRect.anchorMin = new Vector2(0.7f, 0.1f);
        travelRect.anchorMax = new Vector2(0.95f, 0.3f);
        travelRect.offsetMin = Vector2.zero;
        travelRect.offsetMax = Vector2.zero;

        // Civilization container
        GameObject civContainerGO = CreateUIElement("CivilizationContainer", planetInfoPanel.transform);
        civilizationContainer = civContainerGO.transform;
        
        RectTransform civRect = civContainerGO.GetComponent<RectTransform>();
        civRect.anchorMin = new Vector2(0.05f, 0.1f);
        civRect.anchorMax = new Vector2(0.65f, 0.7f);
        civRect.offsetMin = Vector2.zero;
        civRect.offsetMax = Vector2.zero;

        planetInfoPanel.SetActive(false);
    }

    /// <summary>
    /// Helper to create UI elements
    /// </summary>
    private GameObject CreateUIElement(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    /// <summary>
    /// Initialize the space map with solar system data
    /// </summary>
    public void Initialize(SolarSystemManager manager)
    {
        solarSystemManager = manager;
        
        if (solarSystemManager != null)
        {
            CreatePlanetButtons();
            isInitialized = true;
        }
    }

    /// <summary>
    /// Create buttons for each planet in the solar system
    /// </summary>
    private void CreatePlanetButtons()
    {
        // Clear existing buttons
        foreach (var button in planetButtons)
        {
            if (button != null && button.gameObject != null)
                DestroyImmediate(button.gameObject);
        }
        planetButtons.Clear();

        List<PlanetSceneData> planets = solarSystemManager.GetAllPlanets();
        
        for (int i = 0; i < planets.Count; i++)
        {
            CreatePlanetButton(planets[i], i);
        }
    }

    /// <summary>
    /// Create a button for a specific planet
    /// </summary>
    private void CreatePlanetButton(PlanetSceneData planet, int index)
    {
        GameObject buttonGO = CreateUIElement($"Planet_{planet.planetIndex}", planetContainer);
        PlanetButton planetButton = buttonGO.AddComponent<PlanetButton>();
        
        // Add visual components
        Image buttonImage = buttonGO.AddComponent<Image>();
        Button button = buttonGO.AddComponent<Button>();
        
        // Setup button appearance
        Color buttonColor = GetPlanetColor(planet);
        buttonImage.color = buttonColor;
        
        // Position button
        RectTransform buttonRect = buttonGO.GetComponent<RectTransform>();
        
        // Adjust size based on planet type (gas giants larger, small planets smaller)
        Vector2 buttonSize = GetPlanetButtonSize(planet);
        buttonRect.sizeDelta = buttonSize;
        
        // Position planets based on their distance from star for real solar system
        Vector2 position = GetPlanetPosition(planet, index);
        buttonRect.anchoredPosition = position;
        
        // Add planet label
        GameObject labelGO = CreateUIElement("Label", buttonGO.transform);
        TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = planet.planetName;
        label.fontSize = Mathf.Max(8, Mathf.Min(12, buttonSize.x / 8)); // Scale font with button size
        label.alignment = TextAlignmentOptions.Center;
        
        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, -0.5f);
        labelRect.anchorMax = new Vector2(1, 0);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        
        // Add visual indicator for real planets
        if (planet.celestialBodyType == CelestialBodyType.RealPlanet || planet.celestialBodyType == CelestialBodyType.RealMoon)
        {
            GameObject indicatorGO = CreateUIElement("RealPlanetIndicator", buttonGO.transform);
            Image indicator = indicatorGO.AddComponent<Image>();
            indicator.color = Color.yellow;
            
            RectTransform indicatorRect = indicatorGO.GetComponent<RectTransform>();
            indicatorRect.sizeDelta = new Vector2(10, 10);
            indicatorRect.anchoredPosition = new Vector2(buttonSize.x/2 - 5, buttonSize.y/2 - 5);
        }
        
        // Setup button component
        planetButton.Initialize(planet, this);
        button.onClick.AddListener(() => SelectPlanet(planet));
        
        planetButtons.Add(planetButton);
    }
    
    /// <summary>
    /// Get the appropriate size for a planet button
    /// </summary>
    private Vector2 GetPlanetButtonSize(PlanetSceneData planet)
    {
        // Default size
        Vector2 baseSize = new Vector2(80, 80);
        
        if (planet.celestialBodyType == CelestialBodyType.RealPlanet || planet.celestialBodyType == CelestialBodyType.RealMoon)
        {
            switch (planet.planetType)
            {
                case PlanetType.Jupiter:
                case PlanetType.Saturn:
                    return new Vector2(120, 120); // Gas giants are larger
                case PlanetType.Mercury:
                case PlanetType.Pluto:
                    return new Vector2(50, 50); // Small planets/dwarf planets
                case PlanetType.Luna:
                case PlanetType.Io:
                case PlanetType.Europa:
                case PlanetType.Enceladus:
                case PlanetType.Callisto:
                    return new Vector2(35, 35); // Moons are smaller
                case PlanetType.Titan:
                case PlanetType.Ganymede:
                    return new Vector2(45, 45); // Large moons
                case PlanetType.Demonic:
                    return new Vector2(90, 90); // Special hellish world, slightly larger
                default:
                    return new Vector2(70, 70); // Earth-sized planets
            }
        }
        
        return baseSize;
    }
    
    /// <summary>
    /// Get the position for a planet button
    /// </summary>
    private Vector2 GetPlanetPosition(PlanetSceneData planet, int index)
    {
        if (planet.celestialBodyType == CelestialBodyType.RealPlanet || planet.celestialBodyType == CelestialBodyType.RealMoon)
        {
            // Position based on actual distance from star (scaled for UI)
            float scaledDistance = Mathf.Log(planet.distanceFromStar + 1) * 80f; // Logarithmic scaling
            float xPos = -400 + scaledDistance; // Start from left side
            
            // Add some vertical variation for visual appeal
            float yPos = (planet.planetIndex % 2 == 0) ? 10 : -10;
            
            return new Vector2(xPos, yPos);
        }
        else
        {
            // Simple linear positioning for procedural planets
            float xPos = -300 + (index * planetSpacing);
            return new Vector2(xPos, 0);
        }
    }

    /// <summary>
    /// Get the appropriate color for a planet based on its status
    /// </summary>
    private Color GetPlanetColor(PlanetSceneData planet)
    {
        if (planet.isHomeWorld)
            return homeWorldColor;
        else if (solarSystemManager.currentPlanetIndex == planet.planetIndex)
            return currentPlanetColor;
        else if (planet.isGenerated)
            return visitedPlanetColor;
        else
            return unvisitedPlanetColor;
    }

    /// <summary>
    /// Select a planet and show its information
    /// </summary>
    public void SelectPlanet(PlanetSceneData planet)
    {
        selectedPlanet = planet;
        ShowPlanetInfo(planet);
    }

    /// <summary>
    /// Display information about the selected planet
    /// </summary>
    private void ShowPlanetInfo(PlanetSceneData planet)
    {
        planetInfoPanel.SetActive(true);
        
        // Update planet info
        planetNameText.text = planet.planetName;
        
        // Update planet type
        if (planetTypeText != null)
        {
            string typeText = planet.planetType.ToString();
            if (planet.celestialBodyType == CelestialBodyType.RealPlanet)
            {
                typeText += " (Real Solar System)";
            }
            else if (planet.celestialBodyType == CelestialBodyType.RealMoon)
            {
                typeText += " (Real Moon)";
            }
            planetTypeText.text = typeText;
        }
        
        // Update distance information
        if (distanceText != null)
        {
            if (planet.celestialBodyType == CelestialBodyType.RealPlanet || planet.celestialBodyType == CelestialBodyType.RealMoon)
            {
                distanceText.text = $"Distance: {planet.distanceFromStar:F2} AU from Sun";
                if (planet.orbitalPeriod > 0)
                {
                    distanceText.text += $"\nOrbital Period: {planet.orbitalPeriod:F0} Earth days";
                }
                if (planet.averageTemperature != 0)
                {
                    distanceText.text += $"\nTemperature: {planet.averageTemperature:F0}°C";
                }
            }
            else
            {
                distanceText.text = $"Distance: {planet.distanceFromStar:F2} units from star";
            }
        }
        
        // Update planet status with description
        if (planetStatusText != null)
        {
            string statusText = "";
            if (!string.IsNullOrEmpty(planet.description))
            {
                statusText = planet.description;
            }
            else
            {
                statusText = planet.isGenerated ? "Explored" : "Unexplored";
            }
            
            if (planet.hasAtmosphere && !string.IsNullOrEmpty(planet.atmosphereComposition))
            {
                statusText += $"\n\nAtmosphere: {planet.atmosphereComposition}";
            }
            
            if (planet.gravity != 0 && planet.gravity != 1)
            {
                statusText += $"\nGravity: {planet.gravity:F2}g";
            }
            
            planetStatusText.text = statusText;
        }
        
        // Update travel button
        if (planet.planetIndex == solarSystemManager.currentPlanetIndex)
        {
            travelButton.GetComponentInChildren<TextMeshProUGUI>().text = "Current Planet";
            travelButton.interactable = false;
        }
        else
        {
            string buttonText = planet.isGenerated ? "Travel to Planet" : "Explore Planet";
            travelButton.GetComponentInChildren<TextMeshProUGUI>().text = buttonText;
            travelButton.interactable = true;
            travelButton.onClick.RemoveAllListeners();
            travelButton.onClick.AddListener(() => ConfirmTravel(planet));
        }
        
        // Update civilizations list
        UpdateCivilizationList(planet);
    }

    /// <summary>
    /// Update the civilization list for the selected planet
    /// </summary>
    private void UpdateCivilizationList(PlanetSceneData planet)
    {
        // Clear existing civilization entries
        foreach (Transform child in civilizationContainer)
        {
            DestroyImmediate(child.gameObject);
        }

        if (planet.civilizations == null || planet.civilizations.Count == 0)
        {
            if (!planet.isGenerated)
            {
                CreateCivilizationEntry("Unknown - Planet not explored", Color.gray);
            }
            else
            {
                CreateCivilizationEntry("No civilizations detected", Color.white);
            }
        }
        else
        {
            foreach (var civ in planet.civilizations)
            {
                Color civColor = civ.isPlayer ? Color.cyan : (civ.isAlive ? Color.green : Color.red);
                string civText = $"{civ.civilizationName} - {civ.leaderName}";
                if (civ.isPlayer) civText += " (Player)";
                CreateCivilizationEntry(civText, civColor);
            }
        }
    }

    /// <summary>
    /// Create a civilization entry in the list
    /// </summary>
    private void CreateCivilizationEntry(string text, Color color)
    {
        GameObject entryGO = CreateUIElement("CivEntry", civilizationContainer);
        TextMeshProUGUI entryText = entryGO.AddComponent<TextMeshProUGUI>();
        entryText.text = text;
        entryText.fontSize = 14;
        entryText.color = color;
        
        RectTransform entryRect = entryGO.GetComponent<RectTransform>();
        entryRect.sizeDelta = new Vector2(0, 25);
    }

    /// <summary>
    /// Confirm travel to the selected planet
    /// </summary>
    private void ConfirmTravel(PlanetSceneData planet)
    {
        string message = planet.isGenerated ? 
            $"Travel to {planet.planetName}?" : 
            $"Explore {planet.planetName}? This will generate a new world.";
            
        // TODO: Show confirmation dialog
        // For now, just travel directly
        TravelToPlanet(planet);
    }

    /// <summary>
    /// Travel to the specified planet
    /// </summary>
    private void TravelToPlanet(PlanetSceneData planet)
    {
        Hide();
        solarSystemManager.SwitchToPlanet(planet.planetIndex);
    }

    /// <summary>
    /// Show the space map UI
    /// </summary>
    public void Show()
    {
        if (!isInitialized && solarSystemManager != null)
        {
            Initialize(solarSystemManager);
        }
        
        gameObject.SetActive(true);
        spaceMapPanel.SetActive(true);
        
        // Refresh planet data
        RefreshPlanetData();
    }

    /// <summary>
    /// Hide the space map UI
    /// </summary>
    public void Hide()
    {
        if (spaceMapPanel != null)
            spaceMapPanel.SetActive(false);
        
        if (planetInfoPanel != null)
            planetInfoPanel.SetActive(false);
            
        if (spaceMapCanvas != null)
            spaceMapCanvas.gameObject.SetActive(false);
            
        Debug.Log("[SpaceMapUI] Space map UI hidden");
    }

    /// <summary>
    /// Refresh planet data and update UI
    /// </summary>
    private void RefreshPlanetData()
    {
        if (solarSystemManager == null) return;
        
        // Update planet button colors
        for (int i = 0; i < planetButtons.Count; i++)
        {
            if (planetButtons[i] != null)
            {
                PlanetSceneData planet = solarSystemManager.GetPlanetData(i);
                if (planet != null)
                {
                    Image buttonImage = planetButtons[i].GetComponent<Image>();
                    if (buttonImage != null)
                    {
                        buttonImage.color = GetPlanetColor(planet);
                    }
                }
            }
        }
    }
}

/// <summary>
/// Component for individual planet buttons
/// </summary>
public class PlanetButton : MonoBehaviour
{
    private PlanetSceneData planetData;
    private SpaceMapUI spaceMapUI;

    public void Initialize(PlanetSceneData data, SpaceMapUI ui)
    {
        planetData = data;
        spaceMapUI = ui;
    }

    public PlanetSceneData GetPlanetData()
    {
        return planetData;
    }
}
