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

    [Header("Planet Icons")]
    public PlanetTypeSprite[] planetTypeIcons;
    private Dictionary<PlanetType, Sprite> planetIconDict;

    private SolarSystemManager solarSystemManager;
    private List<PlanetButton> planetButtons = new List<PlanetButton>();
    private PlanetSceneData selectedPlanet;
    private bool isInitialized = false;

    void Awake()
    {
        SetupUIReferences();
        
        // IMPORTANT: Hide the space map UI immediately
        Hide();

        // Build planet icon dictionary
        planetIconDict = new Dictionary<PlanetType, Sprite>();
        if (planetTypeIcons != null)
        {
            foreach (var entry in planetTypeIcons)
            {
                if (!planetIconDict.ContainsKey(entry.planetType) && entry.icon != null)
                    planetIconDict.Add(entry.planetType, entry.icon);
            }
        }
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
                Destroy(button.gameObject);
        }
        planetButtons.Clear();

        List<PlanetSceneData> planets = solarSystemManager.GetAllPlanets();
        
        // Find home world to use as center reference
        PlanetSceneData homeWorld = planets.FirstOrDefault(p => p.isHomeWorld);
        
        // Sort planets by distance from star for proper layout
        var sortedPlanets = planets.OrderBy(p => p.distanceFromStar).ToList();
        
        Vector2 center = solarSystemView != null ? Vector2.zero : Vector2.zero;
        float maxRadius = solarSystemView != null ? Mathf.Min(solarSystemView.rect.width, solarSystemView.rect.height) * 0.4f : 300f;
        
        // Find max distance for scaling
        float maxDistance = sortedPlanets.Count > 0 ? sortedPlanets.Max(p => p.distanceFromStar) : 1f;
        
        for (int i = 0; i < sortedPlanets.Count; i++)
        {
            PlanetSceneData planet = sortedPlanets[i];
            
            // Calculate position based on distance from star
            float normalizedDistance = planet.distanceFromStar / maxDistance;
            float radius = planet.isHomeWorld ? 0f : normalizedDistance * maxRadius;
            
            // Distribute planets around their orbital distance
            float angle = (360f / Mathf.Max(sortedPlanets.Count, 1)) * i * Mathf.Deg2Rad;
            Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            
            CreatePlanetButton(planet, i, pos);
        }
    }

    /// <summary>
    /// Create a button for a specific planet
    /// </summary>
    private void CreatePlanetButton(PlanetSceneData planet, int index, Vector2 position)
    {
        GameObject buttonGO = CreateUIElement($"Planet_{planet.planetIndex}", planetContainer);
        PlanetButton planetButton = buttonGO.AddComponent<PlanetButton>();

        // Add visual components
        Image buttonImage = buttonGO.AddComponent<Image>();
        Button button = buttonGO.AddComponent<Button>();

        // Use sprite - debug if missing
        if (planetIconDict != null && planetIconDict.TryGetValue(planet.planetType, out var iconSprite))
        {
            buttonImage.sprite = iconSprite;
            buttonImage.type = Image.Type.Simple;
            buttonImage.preserveAspect = true;
            buttonImage.color = Color.white; // Keep sprite natural color
        }
        else
        {
            // Debug missing sprite
            Debug.LogWarning($"[SpaceMapUI] No sprite found for planet type: {planet.planetType} (Planet: {planet.planetName}). Assign a sprite in the Planet Type Icons array.");
            buttonImage.color = Color.white; // Default white background
        }

        // Position button based on distance from star
        RectTransform rect = buttonGO.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = GetPlanetButtonSize(planet);

        // Setup button click
        button.onClick.AddListener(() => SelectPlanet(planet));

        planetButton.Initialize(planet, this);
        planetButtons.Add(planetButton);
    }
    
    /// <summary>
    /// Get the appropriate size for a planet button
    /// </summary>
    private Vector2 GetPlanetButtonSize(PlanetSceneData planet)
    {
        // Default size
        Vector2 baseSize = new Vector2(80, 80);
        
        if (planet.celestialBodyType == CelestialBodyType.RealPlanet)
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
        if (planet.celestialBodyType == CelestialBodyType.RealPlanet)
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
    /// Select a planet and show its information
    /// </summary>
    public void SelectPlanet(PlanetSceneData planet)
    {
        selectedPlanet = planet;
        
        // Update visual highlighting for all planet buttons
        foreach (var planetButton in planetButtons)
        {
            if (planetButton != null && planetButton.gameObject != null)
            {
                var buttonImage = planetButton.GetComponent<Image>();
                var outline = planetButton.GetComponent<Outline>();
                
                // Add or update outline for selected planet
                if (planetButton.GetPlanetData().planetIndex == planet.planetIndex)
                {
                    // This is the selected planet - add highlight
                    if (outline == null)
                        outline = planetButton.gameObject.AddComponent<Outline>();
                    
                    outline.effectColor = Color.yellow;
                    outline.effectDistance = new Vector2(3, 3);
                    outline.enabled = true;
                }
                else
                {
                    // Not selected - remove highlight
                    if (outline != null)
                        outline.enabled = false;
                }
            }
        }
        
        ShowPlanetInfo(planet);
        UpdateCivilizationList(planet);
        
        if (planetInfoPanel != null)
            planetInfoPanel.SetActive(true);
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
        }
        
        // Update distance information
        if (distanceText != null)
        {
            if (planet.celestialBodyType == CelestialBodyType.RealPlanet)
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
        // Hide the entire canvas or root object
        if (spaceMapCanvas != null)
        {
            spaceMapCanvas.gameObject.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
        Debug.Log("[SpaceMapUI] Space map UI hidden");
    }

    /// <summary>
    /// Refresh planet data and update UI
    /// </summary>
    private void RefreshPlanetData()
    {
        if (solarSystemManager == null) return;
        
        // Update planet button selection highlighting
        for (int i = 0; i < planetButtons.Count; i++)
        {
            if (planetButtons[i] != null)
            {
                PlanetSceneData planet = solarSystemManager.GetPlanetData(i);
                if (planet != null)
                {
                    Outline outline = planetButtons[i].GetComponent<Outline>();
                    if (outline != null)
                    {
                        // Highlight current planet
                        outline.enabled = (solarSystemManager.currentPlanetIndex == planet.planetIndex);
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

[System.Serializable]
public struct PlanetTypeSprite
{
    public PlanetType planetType;
    public Sprite icon;
}
