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

/// <summary>
/// Fallback icons based on climate index when exact map type name isn't found.
/// </summary>
[System.Serializable]
public class ClimateIconEntry
{
    public Sprite sprite;
}

public class MainMenuManager : MonoBehaviour
{
    [Header("Panel References")]
    public GameObject mainMenuPanel;
    public GameObject civSelectionPanel;
    public GameObject leaderSelectionPanel; // New panel for leader selection
    public GameObject gameSetupPanel;
    public GameObject optionsPanel;        // Options menu panel (new)

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
    public Button optionsButton;           // Options menu (new)
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
    public TMP_Dropdown mapSizeDropdown; // Dropdown for map size
    
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
    
    [Header("Climate Fallback Icons (used when exact map name not found)")]
    [Tooltip("Fallback icons by climate index: 0=Frozen, 1=Cold, 2=Temperate, 3=Warm, 4=Hot, 5=Scorching")]
    public Sprite[] climateFallbackIcons = new Sprite[6];
    
    [Header("Land Type Fallback Icons (secondary fallback)")]
    [Tooltip("Fallback icons by land type: 0=Archipelago, 1=Islands, 2=Standard, 3=Continents, 4=Pangaea")]
    public Sprite[] landTypeFallbackIcons = new Sprite[5];

    [Header("Animal Settings")]
    public TMP_Dropdown animalPrevalenceDropdown;

    [Header("Options Menu Audio Settings")]
    public Slider menuMusicVolumeSlider;
    public TextMeshProUGUI menuMusicVolumeText;
    public Toggle menuMusicEnabledToggle;
    public Button optionsBackButton;
    
    [Header("Options Menu Autosave Settings")]
    public Toggle autosaveEnabledToggle;
    public Slider autosaveIntervalSlider;
    public TextMeshProUGUI autosaveIntervalText;

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
        new LandPresetData { name = "Islands", landThreshold = 0.40f, continents = 2, islands = 15, description = "A few large islands with smaller ones", minWidth = 25f, maxWidth = 70f, minHeight = 25f, maxHeight = 50f },
        new LandPresetData { name = "Standard", landThreshold = 0.35f, continents = 5, islands = 9, description = "Balanced continents and islands", minWidth = 100f, maxWidth = 180f, minHeight = 60f, maxHeight = 150f },
        new LandPresetData { name = "Large Continents", landThreshold = 0.30f, continents = 6, islands = 5, description = "Multiple large continents", minWidth = 180f, maxWidth = 200f, minHeight = 150f, maxHeight = 160f },
        new LandPresetData { name = "Pangaea", landThreshold = 0.20f, continents = 2, islands = 4, description = "One massive supercontinent", minWidth = 200f, maxWidth = 250f, minHeight = 250f, maxHeight = 300f }
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
        if (optionsButton != null) optionsButton.onClick.AddListener(OnOptionsClicked);
        if (quitGameButton != null) quitGameButton.onClick.AddListener(OnQuitGameClicked);
        if (selectCivButton != null) selectCivButton.onClick.AddListener(OnCivSelected);
        if (backToMenuButton != null) backToMenuButton.onClick.AddListener(OnBackToMenuClicked);
        if (backFromCivButton != null) backFromCivButton.onClick.AddListener(OnBackFromCivSelectionClicked);
        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGameClicked);
        
        // Options panel callbacks
        if (optionsBackButton != null) optionsBackButton.onClick.AddListener(OnOptionsBackClicked);
        if (menuMusicVolumeSlider != null) menuMusicVolumeSlider.onValueChanged.AddListener(OnMenuMusicVolumeChanged);
        if (menuMusicEnabledToggle != null) menuMusicEnabledToggle.onValueChanged.AddListener(OnMenuMusicEnabledChanged);
        
        // Autosave settings callbacks
        if (autosaveEnabledToggle != null) autosaveEnabledToggle.onValueChanged.AddListener(OnAutosaveEnabledChanged);
        if (autosaveIntervalSlider != null) autosaveIntervalSlider.onValueChanged.AddListener(OnAutosaveIntervalChanged);
        
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
            moisturePresetDropdown.AddOptions(new List<string> { "Very Low", "Low", "Standard", "High", "Very High", "Extreme" });
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
        
        // Initialize audio settings
        InitializeAudioSettings();
        
        // Initialize autosave settings
        InitializeAutosaveSettings();
        
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
            case GameManager.MapSize.Small: return "Small";
            case GameManager.MapSize.Standard: return "Standard";
            case GameManager.MapSize.Large: return "Large";
            default: return size.ToString();
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

        Sprite matchedSprite = null;
        
        // PRIORITY 1: Try exact name match
        foreach (var entry in mapTypeSpriteEntries)
        {
            if (entry != null && entry.sprite != null && string.Equals(entry.mapTypeName, currentName, System.StringComparison.OrdinalIgnoreCase))
            {
                matchedSprite = entry.sprite;
                break;
            }
        }
        
        // PRIORITY 2: Try partial match (first word of map type name)
        if (matchedSprite == null && !string.IsNullOrEmpty(currentName))
        {
            string firstWord = currentName.Split(' ')[0].ToLower();
            foreach (var entry in mapTypeSpriteEntries)
            {
                if (entry != null && entry.sprite != null && !string.IsNullOrEmpty(entry.mapTypeName))
                {
                    string entryFirstWord = entry.mapTypeName.Split(' ')[0].ToLower();
                    if (entryFirstWord == firstWord)
                    {
                        matchedSprite = entry.sprite;
                        break;
                    }
                }
            }
        }
        
        // PRIORITY 3: Use climate-based fallback icon
        if (matchedSprite == null && climateFallbackIcons != null && selectedClimatePreset >= 0 && selectedClimatePreset < climateFallbackIcons.Length)
        {
            matchedSprite = climateFallbackIcons[selectedClimatePreset];
        }
        
        // PRIORITY 4: Use land-type-based fallback icon
        if (matchedSprite == null && landTypeFallbackIcons != null && selectedLandPreset >= 0 && selectedLandPreset < landTypeFallbackIcons.Length)
        {
            matchedSprite = landTypeFallbackIcons[selectedLandPreset];
        }

        if (matchedSprite != null)
        {
            mapTypeIcon.sprite = matchedSprite;
            mapTypeIcon.gameObject.SetActive(true);
        }
        else
        {
            // No icon available at all - still show something generic or hide
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
        // For now, just log that load was clicked
        // The actual load game functionality will be handled in the pause menu during gameplay
        // or we could create a separate load game scene/panel here
        Debug.Log("Load Game clicked - implement load game scene/panel here");
        
        // TODO: Create a load game panel similar to the pause menu's save/load system
        // This would show available save slots and allow loading before starting a new game
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
        CivData[] allCivs = ResourceCache.GetAllCivDatas();
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
            // Show CivData.description, then bonuses
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(civData.description))
            {
                sb.AppendLine(civData.description.Trim());
            }
            else
            {
                sb.AppendLine($"The {civData.civName} are a notable civilization.");
            }

            // List bonuses
            var bonuses = new List<string>();
            if (civData.productionModifier > 0) bonuses.Add($"+{civData.productionModifier:P0} Production");
            if (civData.goldModifier > 0) bonuses.Add($"+{civData.goldModifier:P0} Gold");
            if (civData.scienceModifier > 0) bonuses.Add($"+{civData.scienceModifier:P0} Science");
            if (civData.cultureModifier > 0) bonuses.Add($"+{civData.cultureModifier:P0} Culture");
            if (civData.faithModifier > 0) bonuses.Add($"+{civData.faithModifier:P0} Faith");
            if (bonuses.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>Bonuses:</b>");
                foreach (var bonus in bonuses)
                {
                    sb.AppendLine("+ " + bonus);
                }
            }
            selectedCivDescription.text = sb.ToString().Trim();
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
        GameSetupData.mapSize = (GameManager.MapSize)mapSizeDropdown.value;
        GameSetupData.generateMoon = true;
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
        // --- New: Ice World and Monsoon World flags ---
        GameSetupData.isIceWorld = mapTypeLower.Contains("ice world") || mapTypeLower.Contains("icicle") || mapTypeLower.Contains("cryo");
        GameSetupData.isMonsoonWorld = mapTypeLower.Contains("monsoon") || mapTypeLower.Contains("floodlands");
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
            case GameManager.MapSize.Small: scale = 0.9f; break;
            case GameManager.MapSize.Standard: scale = 1.0f; break;
            case GameManager.MapSize.Large: scale = 1.15f; break;
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
            // Build a description string: biography, bonuses, then ability
            var sb = new System.Text.StringBuilder();
            // 1. Biography/Description
            if (!string.IsNullOrWhiteSpace(leaderData.biography))
                sb.AppendLine(leaderData.biography.Trim());
            else
                sb.AppendLine($"{leaderData.leaderName} is a notable leader.");

            // 2. Bonuses
            var bonuses = new List<string>();
            if (leaderData.goldModifier != 0) bonuses.Add($"{(leaderData.goldModifier > 0 ? "+" : "")}{leaderData.goldModifier:P0} Gold");
            if (leaderData.scienceModifier != 0) bonuses.Add($"{(leaderData.scienceModifier > 0 ? "+" : "")}{leaderData.scienceModifier:P0} Science");
            if (leaderData.productionModifier != 0) bonuses.Add($"{(leaderData.productionModifier > 0 ? "+" : "")}{leaderData.productionModifier:P0} Production");
            if (leaderData.foodModifier != 0) bonuses.Add($"{(leaderData.foodModifier > 0 ? "+" : "")}{leaderData.foodModifier:P0} Food");
            if (leaderData.cultureModifier != 0) bonuses.Add($"{(leaderData.cultureModifier > 0 ? "+" : "")}{leaderData.cultureModifier:P0} Culture");
            if (leaderData.faithModifier != 0) bonuses.Add($"{(leaderData.faithModifier > 0 ? "+" : "")}{leaderData.faithModifier:P0} Faith");
            if (leaderData.militaryStrengthModifier != 0) bonuses.Add($"{(leaderData.militaryStrengthModifier > 0 ? "+" : "")}{leaderData.militaryStrengthModifier:P0} Military Strength");
            if (bonuses.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>Bonuses:</b>");
                foreach (var bonus in bonuses)
                {
                    sb.AppendLine("+ " + bonus);
                }
            }

            // 3. Ability
            if (!string.IsNullOrWhiteSpace(leaderData.abilityName) || !string.IsNullOrWhiteSpace(leaderData.abilityDescription))
            {
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(leaderData.abilityName))
                    sb.AppendLine($"<b>{leaderData.abilityName}</b>");
                if (!string.IsNullOrWhiteSpace(leaderData.abilityDescription))
                    sb.AppendLine(leaderData.abilityDescription);
            }
            selectedLeaderDescription.text = sb.ToString().Trim();
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

    #region Options Menu Methods

    private void InitializeAudioSettings()
    {
        // Initialize options panel state
        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        // Initialize menu music volume slider
        if (menuMusicVolumeSlider != null)
        {
            float savedVolume = PlayerPrefs.GetFloat("MenuMusicVolume", 0.75f);
            menuMusicVolumeSlider.value = savedVolume;
            UpdateMenuMusicVolumeText(savedVolume);
        }

        // Initialize menu music enabled toggle
        if (menuMusicEnabledToggle != null)
        {
            bool musicEnabled = PlayerPrefs.GetInt("MenuMusicEnabled", 1) == 1;
            menuMusicEnabledToggle.isOn = musicEnabled;
        }
    }

    void OnOptionsClicked()
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        
        if (optionsPanel != null)
            optionsPanel.SetActive(true);
        
        Debug.Log("Options menu opened");
    }

    void OnOptionsBackClicked()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
        
        Debug.Log("Options menu closed");
    }

    void OnMenuMusicVolumeChanged(float volume)
    {
        UpdateMenuMusicVolumeText(volume);
        
        // Update menu music volume
        if (MenuMusicManager.Instance != null)
        {
            MenuMusicManager.Instance.SetVolume(volume);
        }
        
        // Save preference
        PlayerPrefs.SetFloat("MenuMusicVolume", volume);
    }

    private void UpdateMenuMusicVolumeText(float volume)
    {
        if (menuMusicVolumeText != null)
        {
            menuMusicVolumeText.text = Mathf.RoundToInt(volume * 100f) + "%";
        }
    }

    void OnMenuMusicEnabledChanged(bool enabled)
    {
        PlayerPrefs.SetInt("MenuMusicEnabled", enabled ? 1 : 0);
        
        if (MenuMusicManager.Instance != null)
        {
            if (enabled)
            {
                // Restore volume and play music
                float savedVolume = PlayerPrefs.GetFloat("MenuMusicVolume", 0.75f);
                MenuMusicManager.Instance.SetVolume(savedVolume);
                MenuMusicManager.Instance.PlayMenuMusic();
            }
            else
            {
                // Mute music
                MenuMusicManager.Instance.SetVolume(0f);
            }
        }
    }

    #endregion

    #region Autosave Settings Methods

    private void InitializeAutosaveSettings()
    {
        // Initialize autosave enabled toggle
        if (autosaveEnabledToggle != null)
        {
            bool autosaveEnabled = PlayerPrefs.GetInt("AutosaveEnabled", 1) == 1;
            autosaveEnabledToggle.isOn = autosaveEnabled;
        }

        // Initialize autosave interval slider
        if (autosaveIntervalSlider != null)
        {
            autosaveIntervalSlider.minValue = 1f;
            autosaveIntervalSlider.maxValue = 10f;
            autosaveIntervalSlider.wholeNumbers = true;
            
            int savedInterval = PlayerPrefs.GetInt("AutosaveInterval", 3);
            autosaveIntervalSlider.value = savedInterval;
            UpdateAutosaveIntervalText(savedInterval);
        }
    }

    void OnAutosaveEnabledChanged(bool enabled)
    {
        PlayerPrefs.SetInt("AutosaveEnabled", enabled ? 1 : 0);
        Debug.Log($"Autosave {(enabled ? "enabled" : "disabled")}");
    }

    void OnAutosaveIntervalChanged(float interval)
    {
        int intervalInt = Mathf.RoundToInt(interval);
        UpdateAutosaveIntervalText(intervalInt);
        PlayerPrefs.SetInt("AutosaveInterval", intervalInt);
        Debug.Log($"Autosave interval set to {intervalInt} turns");
    }

    private void UpdateAutosaveIntervalText(int interval)
    {
        if (autosaveIntervalText != null)
        {
            autosaveIntervalText.text = $"Every {interval} turn{(interval == 1 ? "" : "s")}";
        }
    }

    #endregion

    #region Main Menu Navigation

    /// <summary>
    /// Show the options menu from main menu
    /// </summary>
    public void ShowOptionsMenu()
    {
        if (optionsPanel != null && mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(false);
            optionsPanel.SetActive(true);
            Debug.Log("[MainMenuManager] Options menu opened");
        }
    }

    /// <summary>
    /// Show the save/load panel (this should integrate with your save system)
    /// </summary>
    public void ShowSaveLoadPanel()
    {
        // For now, just show a debug message
        // In the future, this should open a save/load UI panel
        Debug.Log("[MainMenuManager] Save/Load panel requested - implement your save/load UI here");
        
        // You could instantiate a save/load panel prefab here or 
        // transition to a save/load scene
    }

    /// <summary>
    /// Return to main menu from options
    /// </summary>
    public void ReturnToMainMenu()
    {
        if (optionsPanel != null && mainMenuPanel != null)
        {
            optionsPanel.SetActive(false);
            mainMenuPanel.SetActive(true);
            Debug.Log("[MainMenuManager] Returned to main menu");
        }
    }

    /// <summary>
    /// Quit the game application
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[MainMenuManager] Quitting game...");
        Application.Quit();
        
        // For editor testing
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    #endregion

} 