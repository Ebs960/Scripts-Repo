using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

[System.Serializable]
public class MapTypeSpriteEntry
{
    public string mapTypeName;
    public Sprite sprite;
}

public class MainMenuManager : MonoBehaviour
{
    [Header("Panel References")]
    public GameObject mainMenuPanel;
    public GameObject civSelectionPanel;
    public GameObject leaderSelectionPanel; // New panel for leader selection
    public GameObject gameSetupPanel;

    [Header("Civilization Selection")]
    public Transform civButtonContainer;   // Container for civilization buttons
    public Button civButtonPrefab;         // Prefab for civ name buttons
    public Image selectedCivIcon;          // Image to display selected civ's icon
    public TextMeshProUGUI selectedCivName; // Text to display selected civ's name
    public TextMeshProUGUI selectedCivDescription; // Text to display selected civ's description
    public Button selectCivButton;         // Confirm civ selection
    public Button backFromCivButton;       // Back button to return to main menu

    [Header("Leader Selection")]
    public Transform leaderButtonContainer;
    public Button leaderButtonPrefab;
    public Image selectedLeaderIcon;
    public TextMeshProUGUI selectedLeaderName;
    public TextMeshProUGUI selectedLeaderDescription;
    public Button selectLeaderButton;
    public Button backFromLeaderButton;

    [Header("Main Menu")]
    public Button newGameButton;           // On main menu
    public Button loadGameButton;          // Load saved game (new)
    public Button quitGameButton;          // Quit game (new)

    [Header("Game Setup Controls")]
    // Civilization counts
    public Slider aiCountSlider;
    public TextMeshProUGUI aiCountText;
    public Slider cityStateCountSlider;
    public TextMeshProUGUI cityStateCountText;
    public Slider tribeCountSlider;
    public TextMeshProUGUI tribeCountText;

    // Map settings
    [Header("Map Settings")]
    [Tooltip("Controls the planet's hexasphere subdivision level. Higher values increase the number of tiles and map size.")]
    public TextMeshProUGUI planetSizeText;
    public TMP_Dropdown mapSizeDropdown; // NEW: Dropdown for map size
    public TMP_Dropdown moonSizeDropdown;
    public TextMeshProUGUI moonSizeText;
    
    // Land Mass settings
    [Header("Land Mass Settings")]
    public TMP_Dropdown landPresetDropdown;

    // River settings
    // Note: riverCountSlider min value should be set to 0 in the Inspector
    public Slider riverCountSlider;
    public TextMeshProUGUI riverCountText;

    // Climate settings
    [Header("Climate Settings")]
    public TMP_Dropdown climatePresetDropdown;

    [Header("Moisture Settings")]
    public TMP_Dropdown moisturePresetDropdown;

    [Header("Map Type")]
    public TextMeshProUGUI mapTypeName;
    public Image mapTypeIcon;
    public TextMeshProUGUI mapTypeDescription;

    [Header("Navigation Buttons")]
    public Button backToMenuButton;           // Back button on setup
    public Button startGameButton;            // Final start game button on setup

    [Header("Terrain Settings")]
    public TMP_Dropdown terrainRoughnessDropdown;

    [Header("Map Type Visualization")]
    public List<MapTypeSpriteEntry> mapTypeSpriteEntries = new List<MapTypeSpriteEntry>();

    [Header("Animal Settings")]
    public TMP_Dropdown animalPrevalenceDropdown;

    private CivData selectedCivilization;
    private LeaderData selectedLeader;
    private List<Button> civButtons = new List<Button>();
    private List<Button> leaderButtons = new List<Button>();
    private Color selectedButtonColor = new Color(0.9f, 0.8f, 0.1f, 1f);
    private Color normalButtonColor = Color.white;
    
    // Game Setup values
    private int aiCount = 4;
    private int cityStateCount = 2;
    private int tribeCount = 2;
    private int moonPreset = 2;
    
    // Advanced settings
    private bool enableRivers = true;
    private int riverCount = 10;
    
    // Climate settings - now with 6 options
    private int selectedClimatePreset = 2; // Default to Temperate
    
    // Land Mass settings
    private int selectedLandPreset = 2; // Default to Standard

    // Moisture settings - now with 6 options
    private int selectedMoisturePreset = 2; // Default to Standard
    
    // Animal settings
    private int selectedAnimalPrevalence = 3; // Default to normal
    
    // Climate preset values - each entry contains (polarThreshold, subPolarThreshold, equatorThreshold)
    private readonly (float polar, float subPolar, float equator)[] climatePresets = new[] {
        (0.60f, 0.15f, 0.01f),  // Frozen: enormous polar regions, minimal tropics
        (0.70f, 0.30f, 0.05f),  // Cold: large polar regions, large tundra
        (0.80f, 0.60f, 0.20f),  // Temperate: medium polar regions (default)
        (0.85f, 0.70f, 0.30f),  // Warm: smaller polar regions
        (0.90f, 0.75f, 0.50f),  // Hot: small polar regions
        (0.95f, 0.85f, 0.60f)   // Scorching: minimal polar regions, small tropical band
    };
    
    // Land mass preset values - revised for proper continent/island distinction
    private readonly LandPresetData[] landPresets = new[] {
        new LandPresetData { name = "Archipelago", landThreshold = 0.50f, continents = 0, islands = 25, description = "Many small scattered islands", minWidth = 10f, maxWidth = 50f, minHeight = 20f, maxHeight = 30f },
        new LandPresetData { name = "Islands", landThreshold = 0.40f, continents = 2, islands = 15, description = "A few large islands with smaller ones", minWidth = 25f, maxWidth = 45f, minHeight = 15f, maxHeight = 35f },
        new LandPresetData { name = "Standard", landThreshold = 0.40f, continents = 5, islands = 9, description = "Balanced continents and islands", minWidth = 55f, maxWidth = 80f, minHeight = 50f, maxHeight = 90f },
        new LandPresetData { name = "Large Continents", landThreshold = 0.35f, continents = 4, islands = 5, description = "Multiple large continents", minWidth = 60f, maxWidth = 120f, minHeight = 80f, maxHeight = 160f },
        new LandPresetData { name = "Pangaea", landThreshold = 0.32f, continents = 1, islands = 4, description = "One massive supercontinent", minWidth = 180f, maxWidth = 250f, minHeight = 250f, maxHeight = 300f }
    };

    // Moisture preset values
    private readonly (float frequency, float bias)[] moisturePresets = new[] {
        (2.0f, -0.25f),  // Desert: Very dry, minimal moisture
        (2.5f, -0.15f),  // Arid: Lower frequency and drier bias
        (4.0f, 0.0f),    // Standard: Balanced moisture (default)
        (5.0f, 0.1f),    // Moist: Higher frequency and wetter bias
        (6.0f, 0.2f),    // Wet: High moisture for many forests/jungles
        (7.0f, 0.3f)     // Oceanic: Extremely wet world with minimal deserts
    };
    
    // Terrain roughness presets (combines hills and mountains)
    private readonly (float hills, float mountains)[] terrainPresets = new[] {
        (0.4f, 0.6f),   // Smooth: few hills, almost no mountains
        (0.5f, 0.7f),   // Rolling: moderate hills, few mountains
        (0.6f, 0.8f),   // Rocky: many hills, some mountains
        (0.7f, 0.85f),  // Mountainous: lots of hills and mountains
        (0.8f, 0.9f)    // Alpine: extremely mountainous
    };

    private int selectedTerrainPreset = 2; // Default to Rocky

    [Header("References")]
    private GameManager gameManager; // Reference to GameManager

    [System.Serializable]
    public struct LandPresetData
    {
        public string name;
        public float landThreshold;
        public int continents;
        public int islands;
        public string description;
        public float minWidth, maxWidth;
        public float minHeight, maxHeight;
    }

    [System.Serializable]
    public struct MoonSizePreset
    {
        public string name;
        public int subdivisions;
    }

    private readonly MoonSizePreset[] moonSizePresets = new[] {
        new MoonSizePreset { name = "None", subdivisions = 0 },
        new MoonSizePreset { name = "Tiny", subdivisions = 8 },
        new MoonSizePreset { name = "Small", subdivisions = 12 },
        new MoonSizePreset { name = "Standard", subdivisions = 16 },
        new MoonSizePreset { name = "Large", subdivisions = 20 }
    };

    void Start()
    {
        // Get GameManager reference
        gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogError("GameManager not found!");
        }

        // Initialize panels: show only main menu at start
        mainMenuPanel.SetActive(true);
        civSelectionPanel.SetActive(false);
        leaderSelectionPanel.SetActive(false);
        gameSetupPanel.SetActive(false);
        
        // Start menu music
        if (MenuMusicManager.Instance != null)
        {
            MenuMusicManager.Instance.PlayMenuMusic();
        }
        
        // Hook up button callbacks
        if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGameClicked);
        if (loadGameButton != null) loadGameButton.onClick.AddListener(OnLoadGameClicked);
        if (quitGameButton != null) quitGameButton.onClick.AddListener(OnQuitGameClicked);
        if (selectCivButton != null) selectCivButton.onClick.AddListener(OnCivSelected);
        if (backToMenuButton != null) backToMenuButton.onClick.AddListener(OnBackToMenuClicked);
        if (backFromCivButton != null) backFromCivButton.onClick.AddListener(OnBackFromCivSelectionClicked);
        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGameClicked);
        
        // New Leader Panel Buttons
        if (selectLeaderButton != null) selectLeaderButton.onClick.AddListener(OnLeaderSelected);
        if (backFromLeaderButton != null) backFromLeaderButton.onClick.AddListener(OnBackFromLeaderSelectionClicked);
        
        // Initialize climate preset dropdown if available
        if (climatePresetDropdown != null)
        {
            // Clear and populate options
            climatePresetDropdown.ClearOptions();
            climatePresetDropdown.AddOptions(new List<string> { "Frozen", "Cold", "Temperate", "Warm", "Hot", "Scorching" });
            climatePresetDropdown.value = selectedClimatePreset;
            climatePresetDropdown.onValueChanged.AddListener(OnClimatePresetChanged);
        }
        
        // Initialize land preset dropdown if available
        if (landPresetDropdown != null)
        {
            // Clear and populate options
            landPresetDropdown.ClearOptions();
            landPresetDropdown.AddOptions(new List<string> {
                "Archipelago",    // land == 0
                "Islands",        // land == 1
                "Standard",       // land == 2 (classic fallback)
                "Large Continents",     // land == 3 (two-word)
                "Pangaea"         // land == 4 (three-word)
            });
            landPresetDropdown.value = selectedLandPreset;
            landPresetDropdown.onValueChanged.AddListener(OnLandPresetChanged);
        }
        
        // Initialize moisture preset dropdown if available
        if (moisturePresetDropdown != null)
        {
            // Clear and populate options
            moisturePresetDropdown.ClearOptions();
            moisturePresetDropdown.AddOptions(new List<string> { "Desert", "Arid", "Standard", "Moist", "Wet", "Oceanic" });
            moisturePresetDropdown.value = selectedMoisturePreset;
            moisturePresetDropdown.onValueChanged.AddListener(OnMoisturePresetChanged);
        }
        
        // Initialize terrain roughness dropdown if available
        if (terrainRoughnessDropdown != null)
        {
            terrainRoughnessDropdown.ClearOptions();
            terrainRoughnessDropdown.AddOptions(new List<string> { "Flat", "Smooth", "Rocky", "Mountainous", "Alpine" });
            terrainRoughnessDropdown.value = selectedTerrainPreset;
            terrainRoughnessDropdown.onValueChanged.AddListener(OnTerrainPresetChanged);
        }
        
        // Update the map type name based on the initial settings
        UpdateMapTypeName();
            
        // Initialize all sliders and toggles
        InitializeControls();
        
        // Update selected civ icon
        if (selectedCivIcon != null)
        {
            selectedCivIcon.sprite = null;
            selectedCivIcon.gameObject.SetActive(false);
        }
        
        if (selectedCivDescription != null)
        {
            selectedCivDescription.text = "";
        }

        if (animalPrevalenceDropdown != null)
        {
            animalPrevalenceDropdown.ClearOptions();
            animalPrevalenceDropdown.AddOptions(new List<string> { "Dead", "Sparse", "Scarce", "Normal", "Lively", "Bustling" });
            animalPrevalenceDropdown.value = selectedAnimalPrevalence;
            animalPrevalenceDropdown.onValueChanged.AddListener(OnAnimalPrevalenceChanged);
        }

        // Initialize map size dropdown
        InitializeMapSizeDropdown();
    }
    
    private void InitializeControls()
    {
        // Initialize AI count slider
        aiCountSlider.minValue = 0;
        aiCountSlider.maxValue = 8;
        aiCountSlider.wholeNumbers = true;
        aiCountSlider.value = aiCount;
        aiCountSlider.onValueChanged.AddListener(OnAICountChanged);
        UpdateAICountText();

        // Initialize city-state count slider
        cityStateCountSlider.minValue = 0;
        cityStateCountSlider.maxValue = 6;
        cityStateCountSlider.wholeNumbers = true;
        cityStateCountSlider.value = cityStateCount;
        cityStateCountSlider.onValueChanged.AddListener(OnCityStateCountChanged);
        UpdateCityStateCountText();

        // Initialize tribe count slider
        tribeCountSlider.minValue = 0;
        tribeCountSlider.maxValue = 6;
        tribeCountSlider.wholeNumbers = true;
        tribeCountSlider.value = tribeCount;
        tribeCountSlider.onValueChanged.AddListener(OnTribeCountChanged);
        UpdateTribeCountText();


        // Initialize moon size slider
        InitializeMoonSizeDropdown();

        
        // River Settings
        if (riverCountSlider != null)
        {
            riverCountSlider.minValue = 0;
            riverCountSlider.maxValue = 20;
            riverCountSlider.wholeNumbers = true;
            riverCountSlider.value = riverCount;
            riverCountSlider.onValueChanged.AddListener(OnRiverCountChanged);
            UpdateRiverCountText();
        }
        
        // Update the map type display
        UpdateMapTypeName();
        
        // Update preset icons
        UpdatePresetIcons();
    }
    
    #region Value Change Handlers and Text Updates
    
    // Civilization Counts
    private void OnAICountChanged(float value)
    {
        aiCount = Mathf.RoundToInt(value);
        UpdateAICountText();
        UpdateMapTypeName(); // Update description with new civ count
    }
    
    private void UpdateAICountText()
    {
        if (aiCountText != null)
            aiCountText.text = $"AI Civilizations: {aiCount}";
    }
    
    private void OnCityStateCountChanged(float value)
    {
        cityStateCount = Mathf.RoundToInt(value);
        UpdateCityStateCountText();
        UpdateMapTypeName(); // Update description with new city-state count
    }
    
    private void UpdateCityStateCountText()
    {
        if (cityStateCountText != null)
            cityStateCountText.text = $"City States: {cityStateCount}";
    }
    
    private void OnTribeCountChanged(float value)
    {
        tribeCount = Mathf.RoundToInt(value);
        UpdateTribeCountText();
        UpdateMapTypeName(); // Update description with new tribe count
    }
    
    private void UpdateTribeCountText()
    {
        if (tribeCountText != null)
            tribeCountText.text = $"Tribes: {tribeCount}";
    }
    
    // Map Settings
    private void OnMapSizeChanged(int value)
    {
        GameManager.MapSize selectedSize = (GameManager.MapSize)value;
        GameSetupData.mapSize = selectedSize;
        UpdatePlanetSizeText();
    }
    
    private void UpdatePlanetSizeText()
    {
        GameManager.MapSize size = GameSetupData.mapSize;
        int subdivisions; float radius;
        GameManager.GetMapSizeParams(size, out subdivisions, out radius);
        string displayName = GetMapSizeDisplayName(size);
        if (planetSizeText != null)
            planetSizeText.text = $"Map Size: {displayName}";
    }
    
    private string GetMapSizeDisplayName(GameManager.MapSize size)
    {
        switch (size)
        {
            case GameManager.MapSize.Micro: return "Micro";
            case GameManager.MapSize.Tiny: return "Tiny";
            case GameManager.MapSize.Small: return "Small";
            case GameManager.MapSize.Standard: return "Standard";
            case GameManager.MapSize.Large: return "Large";
            case GameManager.MapSize.Huge: return "Huge";
            case GameManager.MapSize.Gigantic: return "Gigantic";
            default: return size.ToString();
        }
    }

    private int EstimateHexTiles(int subdivisions)
    {
        // Approximate formula for number of hex tiles on a geodesic sphere
        return 10 * subdivisions * subdivisions + 2;
    }
    
    private void OnMoonSizeChanged(int value)
    {
        moonPreset = value;
        GameSetupData.moonSize = moonSizePresets[value].subdivisions;
        GameSetupData.generateMoon = GameSetupData.moonSize > 0;
        UpdateMoonSizeText();
    }
    
    private void UpdateMoonSizeText()
    {
        if (moonSizeText != null)
        {
            string sizeDescription = moonSizePresets[moonPreset].name;
            int subdivisions = moonSizePresets[moonPreset].subdivisions;
            
            if (subdivisions > 0)
            {
                moonSizeText.text = $"Moon Size: {sizeDescription} ({subdivisions} subdivisions)";
            }
            else
            {
                moonSizeText.text = "Moon: None";
            }
        }
    }
    
    
    // River Settings
    private void OnRiverCountChanged(float value)
    {
        riverCount = Mathf.RoundToInt(value);
        // Set enableRivers based on river count
        enableRivers = riverCount > 0;
        UpdateRiverCountText();
        UpdateMapTypeName(); // Update description when river count changes
    }
    
    private void UpdateRiverCountText()
    {
        if (riverCountText != null)
        {
            if (riverCount <= 0)
            {
                riverCountText.text = "Rivers: Disabled";
            }
            else
            {
                riverCountText.text = $"Rivers: {riverCount}";
            }
        }
    }
    
    // Climate Settings
    private void OnClimatePresetChanged(int value)
    {
        selectedClimatePreset = value;
        
        // Update icons and map type when climate changes
        UpdatePresetIcons();
        UpdateMapTypeName();
    }
    
    private (float polar, float subPolar, float equator) GetCurrentClimateThresholds()
    {
        if (selectedClimatePreset >= 0 && selectedClimatePreset < climatePresets.Length)
            return climatePresets[selectedClimatePreset];
            
        // Default to temperate
        return climatePresets[2];
    }
    
    // Moisture Settings
    private void OnMoisturePresetChanged(int value)
    {
        selectedMoisturePreset = value;
        
        // Update icons and map type when moisture changes
        UpdatePresetIcons();
        UpdateMapTypeName();
    }
    
    private (float frequency, float bias) GetCurrentMoistureSettings()
    {
        if (selectedMoisturePreset >= 0 && selectedMoisturePreset < moisturePresets.Length)
            return moisturePresets[selectedMoisturePreset];
            
        // Default to standard
        return moisturePresets[2];
    }
    
    // Update the preset icons based on selections
    private void UpdatePresetIcons()
    {
        // Update map type icon only
        UpdateMapTypeIcon();
    }
    
    // Called when map type name and visuals need to be updated
    private void UpdateMapTypeName()
    {
        // Calculate indices for lookup
        int climateIndex = selectedClimatePreset;
        int moistureIndex = selectedMoisturePreset;
        int landIndex = selectedLandPreset; // This now directly maps to MapTypeNameGenerator's land types
        int elevationCategory = GetElevationCategory();

        // Get the name from the MapTypeNameGenerator
        string mapTypeNameStr = MapTypeNameGenerator.GetMapTypeName(climateIndex, moistureIndex, landIndex, elevationCategory);

        // Set the map type name text
        if (mapTypeName != null)
        {
            mapTypeName.text = $"Map Type: {mapTypeNameStr}";
        }

        // Update the map type description
        if (mapTypeDescription != null)
        {
            string description = MapTypeDescriptionGenerator.GetDescription(climateIndex, moistureIndex, landIndex, elevationCategory, aiCount, cityStateCount, tribeCount, selectedAnimalPrevalence);
            mapTypeDescription.text = description;
        }

        // Update map type icon
        UpdateMapTypeIcon();
    }

    private int GetElevationCategory()
    {
        var terrainPreset = terrainPresets[selectedTerrainPreset];
        if (terrainPreset.hills >= 0.7f && terrainPreset.mountains >= 0.85f)
            return 2; // Mountainous
        if (terrainPreset.hills >= 0.5f)
            return 1; // Hilly
        return 0; // Low
    }

    #endregion

    #region UI Navigation and Game Flow

    // Updates the map type icon based on current selections
    private void UpdateMapTypeIcon()
    {
        if (mapTypeIcon == null) return;

        // Retrieve current map type name (without the "Map Type:" prefix)
        string currentName = mapTypeName != null ? mapTypeName.text.Replace("Map Type: ", "").Trim() : string.Empty;

        // Try to find a sprite that matches this name
        Sprite matchedSprite = null;
        foreach (var entry in mapTypeSpriteEntries)
        {
            if (entry != null && entry.sprite != null && string.Equals(entry.mapTypeName, currentName, System.StringComparison.OrdinalIgnoreCase))
            {
                matchedSprite = entry.sprite;
                break;
            }
        }

        if (matchedSprite != null)
        {
            mapTypeIcon.sprite = matchedSprite;
            mapTypeIcon.gameObject.SetActive(true);
        }
        else
        {
            mapTypeIcon.gameObject.SetActive(false);
        }
    }

    // Called when "New Game" is clicked: show civ selection panel
    void OnNewGameClicked()
    {
        mainMenuPanel.SetActive(false);
        civSelectionPanel.SetActive(true);
        
        // Populate civilization list
        PopulateCivilizationList();
    }
    
    // Called when "Load Game" is clicked
    void OnLoadGameClicked()
    {
        // TODO: Implement game loading
        Debug.Log("Load game not implemented yet");
    }
    
    // Called when "Quit Game" is clicked
    void OnQuitGameClicked()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
    
    // Populates the civilization list with available civilizations
    void PopulateCivilizationList()
    {
        // Clear existing buttons
        foreach (var button in civButtons)
        {
            if (button != null)
                Destroy(button.gameObject);
        }
        civButtons.Clear();
        
        // Hide selected civ icon and disable selection button until a civ is selected
        if (selectedCivIcon != null)
        {
            selectedCivIcon.sprite = null;
            selectedCivIcon.gameObject.SetActive(false);
        }
        
        if (selectedCivName != null)
        {
            selectedCivName.text = "";
        }
        
        if (selectedCivDescription != null)
        {
            selectedCivDescription.text = "";
        }
        
        if (selectCivButton != null)
            selectCivButton.interactable = false;
        
        selectedCivilization = null;
        
        // Load all CivData assets from Resources/Civilizations
        CivData[] allCivs = Resources.LoadAll<CivData>("Civilizations");
        if (allCivs == null || allCivs.Length == 0)
        {
            Debug.LogError("No civilizations found in Resources/Civilizations!");
            return;
        }
        
        // Only show playable civs (not tribes or city-states)
        var playableCivs = new List<CivData>();
        foreach (var civData in allCivs)
        {
            if (civData != null && !civData.isTribe && !civData.isCityState)
                playableCivs.Add(civData);
        }
        
        // Create buttons for each civilization
        foreach (var civData in playableCivs)
        {
            if (civData == null || civButtonContainer == null || civButtonPrefab == null)
                continue;
                
            // Create a button for this civilization
            Button button = Instantiate(civButtonPrefab, civButtonContainer);
            
            // Set the button text to the civilization name
            TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = civData.civName;
            }
                
            // Add click handler
            button.onClick.AddListener(() => OnCivButtonClicked(button, civData));
            
            // Add to button list
            civButtons.Add(button);
        }
    }
    
    // Called when a civilization button is clicked
    void OnCivButtonClicked(Button clickedButton, CivData civData)
    {
        // Update button colors for all buttons
        foreach (var button in civButtons)
        {
            ColorBlock colors = button.colors;
            if (button == clickedButton)
            {
                colors.normalColor = selectedButtonColor;
                colors.highlightedColor = selectedButtonColor;
                colors.selectedColor = selectedButtonColor;
            }
            else
            {
                colors.normalColor = normalButtonColor;
                colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
                colors.selectedColor = normalButtonColor;
            }
            button.colors = colors;
        }
        
        // Store the selected civilization
        selectedCivilization = civData;
        
        // Show the civilization name
        if (selectedCivName != null)
        {
            selectedCivName.text = civData.civName;
        }
        
        // Show the civilization icon
        if (selectedCivIcon != null && civData.icon != null)
        {
            selectedCivIcon.sprite = civData.icon;
            selectedCivIcon.gameObject.SetActive(true);
        }
        
        // Show civilization description
        if (selectedCivDescription != null)
        {
            string description = CivDescriptionGenerator.GenerateDescription(civData, null);
            selectedCivDescription.text = description;
        }
        
        // Enable the select button
        if (selectCivButton != null)
            selectCivButton.interactable = true;
    }

    // Called when civ selection is confirmed
    void OnCivSelected()
    {
        // Ensure a civilization is selected
        if (selectedCivilization == null)
        {
            Debug.LogWarning("No civilization selected!");
            return;
        }
        
        civSelectionPanel.SetActive(false);
        leaderSelectionPanel.SetActive(true);
        PopulateLeaderList();
    }

    // Called when backing out to main menu
    void OnBackToMenuClicked()
    {
        gameSetupPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    // Called when all settings are done and "Start Game" is clicked
    void OnStartGameClicked()
    {
        // Store choices in GameSetupData before switching scenes
        // Civilization settings
        GameSetupData.selectedPlayerCivilizationData = selectedCivilization;
        GameSetupData.selectedLeaderData = selectedLeader; // Store selected leader
        GameSetupData.numberOfCivilizations = aiCount;
        GameSetupData.numberOfCityStates = cityStateCount;
        GameSetupData.numberOfTribes = tribeCount;
        
        // Basic map settings
        GameSetupData.moonSize = moonPreset;
        GameSetupData.animalPrevalence = selectedAnimalPrevalence;

        // Map generation settings
        GameSetupData.selectedClimatePreset = selectedClimatePreset;
        GameSetupData.selectedMoisturePreset = selectedMoisturePreset;
        GameSetupData.selectedLandPreset = selectedLandPreset;
        GameSetupData.selectedTerrainPreset = selectedTerrainPreset;
        
        // Get the map type name and check for special world types
        string mapTypeNameStr = MapTypeNameGenerator.GetMapTypeName(
            selectedClimatePreset,
            selectedMoisturePreset,
            selectedLandPreset,
            selectedTerrainPreset);
        
        GameSetupData.mapTypeName = mapTypeNameStr;
        
        // Check for special world types based on map name
        string mapTypeLower = mapTypeNameStr.ToLower();
        GameSetupData.isInfernalWorld = mapTypeLower.Contains("infernal");
        GameSetupData.isDemonicWorld = mapTypeLower.Contains("demonic") || 
                                      mapTypeLower.Contains("hellscape") || 
                                      mapTypeLower.Contains("brimstone");
        GameSetupData.isScorchedWorld = mapTypeLower.Contains("scorched") || 
                                       mapTypeLower.Contains("ashlands") || 
                                       mapTypeLower.Contains("charred");
        // River settings
        GameSetupData.enableRivers = enableRivers;
        GameSetupData.riverCount = riverCount;
        
        // Get current climate thresholds from presets
        var climateThresholds = climatePresets[selectedClimatePreset];
        GameSetupData.polarLatitudeThreshold = climateThresholds.polar;
        GameSetupData.subPolarLatitudeThreshold = climateThresholds.subPolar;
        GameSetupData.equatorLatitudeThreshold = climateThresholds.equator;
        
        // Get current moisture settings from presets
        var moistureSettings = moisturePresets[selectedMoisturePreset];
        GameSetupData.moistureBias = moistureSettings.bias;

        // Set temperatureBias and moistureBias for strong climate impact
        float[] tempBiases = { -0.30f, -0.15f, 0f, 0.1f, 0.2f, 0.30f }; // Frozen to Scorching
        float[] moistBiases = { -0.45f, -0.15f, 0f, 0.1f, 0.2f, 0.45f }; // Desert to Oceanic
        GameSetupData.temperatureBias = tempBiases[Mathf.Clamp(selectedClimatePreset, 0, tempBiases.Length-1)];
        GameSetupData.moistureBias += moistBiases[Mathf.Clamp(selectedMoisturePreset, 0, moistBiases.Length-1)];
        
        // Land generation settings
        var landPreset = landPresets[selectedLandPreset];
        GameSetupData.landThreshold = landPreset.landThreshold;
        GameSetupData.numberOfContinents = landPreset.continents;
        GameSetupData.numberOfIslands = landPreset.islands;
        GameSetupData.generateIslands = landPreset.islands > 0;
        GameSetupData.seedPositionVariance = 0.85f;
        float width = UnityEngine.Random.Range(landPreset.minWidth, landPreset.maxWidth);
        float height = UnityEngine.Random.Range(landPreset.minHeight, landPreset.maxHeight);
        // --- Map size scaling ---
        float scale = 1.0f;
        switch (GameSetupData.mapSize)
        {
            case GameManager.MapSize.Micro: scale = 0.7f; break;
            case GameManager.MapSize.Tiny: scale = 0.8f; break;
            case GameManager.MapSize.Small: scale = 0.9f; break;
            case GameManager.MapSize.Standard: scale = 1.0f; break;
            case GameManager.MapSize.Large: scale = 1.15f; break;
            case GameManager.MapSize.Huge: scale = 1.3f; break;
            case GameManager.MapSize.Gigantic: scale = 1.5f; break;
        }
        GameSetupData.maxContinentWidthDegrees = width * scale;
        GameSetupData.maxContinentHeightDegrees = height * scale;

        // Initialize game music with selected civilization
        if (MusicManager.Instance != null && selectedCivilization != null)
        {
            MusicManager.Instance.InitializeMusicTracks();
        }

        // Load the gameplay scene (make sure it is added to Build Settings)
        UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
    }

    // Called when land preset is changed
    void OnLandPresetChanged(int value)
    {
        selectedLandPreset = value;
        
        // Update map type name when land preset changes
        UpdateMapTypeName();
    }

    private void OnTerrainPresetChanged(int value)
    {
        selectedTerrainPreset = value;
        
        // Update map type when terrain roughness changes
        UpdateMapTypeName();
    }
    
    // Called when the back button is clicked on civ selection screen
    void OnBackFromCivSelectionClicked()
    {
        civSelectionPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    // Animal settings
    private void OnAnimalPrevalenceChanged(int value)
    {
        selectedAnimalPrevalence = value;
        UpdateMapTypeName();
    }

    // --- New Leader Selection Methods ---

    void PopulateLeaderList()
    {
        // Clear existing buttons
        foreach (var button in leaderButtons)
        {
            if (button != null) Destroy(button.gameObject);
        }
        leaderButtons.Clear();

        // Reset display
        if (selectedLeaderIcon != null) selectedLeaderIcon.gameObject.SetActive(false);
        if (selectedLeaderName != null) selectedLeaderName.text = "Select a Leader";
        if (selectedLeaderDescription != null) selectedLeaderDescription.text = "";
        if (selectLeaderButton != null) selectLeaderButton.interactable = false;
        selectedLeader = null;

        if (selectedCivilization == null || selectedCivilization.availableLeaders == null || selectedCivilization.availableLeaders.Count == 0)
        {
            Debug.LogError($"Civilization '{selectedCivilization?.civName}' has no available leaders assigned!");
            return;
        }

        // Create buttons for each leader
        foreach (var leaderData in selectedCivilization.availableLeaders)
        {
            Button button = Instantiate(leaderButtonPrefab, leaderButtonContainer);
            button.GetComponentInChildren<TextMeshProUGUI>().text = leaderData.leaderName;
            button.onClick.AddListener(() => OnLeaderButtonClicked(button, leaderData));
            leaderButtons.Add(button);
        }
    }

    void OnLeaderButtonClicked(Button clickedButton, LeaderData leaderData)
    {
        // Highlight selected button
        foreach (var button in leaderButtons)
        {
            button.interactable = (button != clickedButton);
        }
        
        selectedLeader = leaderData;
        if (selectLeaderButton != null) selectLeaderButton.interactable = true;

        // Update display
        if (selectedLeaderIcon != null && leaderData.portrait != null)
        {
            selectedLeaderIcon.sprite = leaderData.portrait;
            selectedLeaderIcon.gameObject.SetActive(true);
        }
        if (selectedLeaderName != null)
        {
            selectedLeaderName.text = leaderData.leaderName;
        }
        if (selectedLeaderDescription != null)
        {
            // Build a description string from leader bonuses
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>{leaderData.abilityName}</b>");
            sb.AppendLine(leaderData.abilityDescription);
            sb.AppendLine();
            sb.AppendLine("<b>Bonuses:</b>");
            if (leaderData.goldModifier != 0) sb.AppendLine($"- {leaderData.goldModifier:P0} Gold");
            if (leaderData.scienceModifier != 0) sb.AppendLine($"- {leaderData.scienceModifier:P0} Science");
            if (leaderData.productionModifier != 0) sb.AppendLine($"- {leaderData.productionModifier:P0} Production");
            if (leaderData.foodModifier != 0) sb.AppendLine($"- {leaderData.foodModifier:P0} Food");
            if (leaderData.cultureModifier != 0) sb.AppendLine($"- {leaderData.cultureModifier:P0} Culture");
            if (leaderData.faithModifier != 0) sb.AppendLine($"- {leaderData.faithModifier:P0} Faith");
            if (leaderData.militaryStrengthModifier != 0) sb.AppendLine($"- {leaderData.militaryStrengthModifier:P0} Military Strength");
            selectedLeaderDescription.text = sb.ToString();
        }
    }

    void OnLeaderSelected()
    {
        if (selectedLeader == null)
        {
            Debug.LogWarning("No leader selected!");
            return;
        }
        leaderSelectionPanel.SetActive(false);
        gameSetupPanel.SetActive(true);
    }

    void OnBackFromLeaderSelectionClicked()
    {
        leaderSelectionPanel.SetActive(false);
        civSelectionPanel.SetActive(true);
    }

    // --- End New Leader Selection Methods ---
    #endregion

    private void InitializeMapSizeDropdown()
    {
        mapSizeDropdown.ClearOptions();
        var options = new List<string>();
        foreach (GameManager.MapSize size in System.Enum.GetValues(typeof(GameManager.MapSize)))
        {
            options.Add(GetMapSizeDisplayName(size));
        }
        mapSizeDropdown.AddOptions(options);
        mapSizeDropdown.value = (int)GameSetupData.mapSize;
        mapSizeDropdown.onValueChanged.AddListener(OnMapSizeChanged);
        UpdatePlanetSizeText();
    }

    private void InitializeMoonSizeDropdown()
    {
        moonSizeDropdown.ClearOptions();
        var options = new List<string>();
        foreach (var preset in moonSizePresets)
        {
            options.Add(preset.name);
        }
        moonSizeDropdown.AddOptions(options);
        moonSizeDropdown.value = moonPreset;
        moonSizeDropdown.onValueChanged.AddListener(OnMoonSizeChanged);
        UpdateMoonSizeText();
    }
} 