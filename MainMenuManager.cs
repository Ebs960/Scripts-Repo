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
    public TextMeshProUGUI selectedCivBonuses; // Text to display selected civ's bonuses
    public Button selectCivButton;         // Confirm civ selection
    public Button backFromCivButton;       // Back button to return to main menu

    [System.Serializable]
    public struct CivilizationSelectionEntry
    {
        public CivData civData;
        public Button civButton;
        [Tooltip("Optional icon image on the civ button entry.")]
        public Image buttonIconImage;
        [Tooltip("Optional panel/image target whose Source Image will be replaced for this civ entry when enabled.")]
        public Image backgroundTargetImage;
        [Tooltip("Optional background sprite to apply to the target image for this civ entry.")]
        public Sprite backgroundSprite;
    }

    [Header("Manual Civilization Button Entries")]
    public List<CivilizationSelectionEntry> civSelectionEntries = new List<CivilizationSelectionEntry>();

    [Header("Civ Entry Background Overrides")]
    [Tooltip("Optional feature toggle. When enabled, each civ entry can apply its own background sprite to an assigned Image.")]
    public bool enableCivEntryBackgroundOverrides = false;

    [Header("Leader Selection")]
    public Transform leaderButtonContainer;
    public Button leaderButtonPrefab;
    [Tooltip("Panel background image for the whole leader selection panel.")]
    public Image leaderSelectionBackgroundImage;
    public TextMeshProUGUI selectedLeaderName;
    public TextMeshProUGUI selectedLeaderDescription;
    public TextMeshProUGUI selectedLeaderBonuses;
    public Button selectLeaderButton;
    public Button backFromLeaderButton;

    [System.Serializable]
    public struct LeaderSelectionEntry
    {
        public LeaderData leaderData;
        [Tooltip("Optional background sprite to apply to the whole leader selection panel when this leader is selected.")]
        public Sprite backgroundSprite;
    }

    [Header("Leader Data Entries")]
    public List<LeaderSelectionEntry> leaderSelectionEntries = new List<LeaderSelectionEntry>();

    [Header("Leader Panel Background Overrides")]
    [Tooltip("Optional feature toggle. When enabled, each leader entry can override the whole leader selection panel background.")]
    public bool enableLeaderEntryBackgroundOverrides = false;

    [Header("Main Menu")]
    public Button newGameButton;           // On main menu
    public Button loadGameButton;          // Load saved game (new)
    public Button optionsButton;           // Options menu (new)
    public Button quitGameButton;          // Quit game (new)

    [Header("Game Setup Controls")]
    // Civilization counts (now dropdowns using TextMeshPro)
    public TMP_Dropdown aiCountDropdown;
    public TMP_Dropdown cityStateCountDropdown;
    public TMP_Dropdown tribeCountDropdown;

    // Map settings
    [Header("Map Settings")]
    [Tooltip("Controls the flat grid resolution. Higher values increase the number of tiles and map size.")]
    public TMP_Dropdown mapSizeDropdown; // Dropdown for map size
    
    // Land Mass settings
    [Header("Land Mass Settings")]
    public TMP_Dropdown landPresetDropdown;

    // River settings (deprecated - removed slider/text)

    // Climate settings
    [Header("Climate Settings")]
    public TMP_Dropdown climatePresetDropdown;

    [Header("Moisture Settings")]
    public TMP_Dropdown moisturePresetDropdown;
    public Toggle randomWorldSeedToggle;
    public TMP_InputField worldSeedInput;
    public TMP_Dropdown waterwaysDropdown;
    public TMP_Dropdown resourcesDropdown;
    public TMP_Dropdown startingSpreadDropdown;

    [Header("Map Type")]
    public TextMeshProUGUI mapTypeName;
    public Image mapTypeIcon;
    public TextMeshProUGUI mapTypeDescription;

    [Header("Planet Preview")]
    [Tooltip("Optional MenuPlanetPreview sphere in the setup UI. Automatically updated when climate/moisture/land settings change.")]
    public MenuPlanetPreview planetPreview;

    [Header("Placeholder Icons")]
    [Tooltip("Placeholder icon used when a civilization has no icon assigned.")]
    public Sprite placeholderCivIcon;

    [Header("Navigation Buttons")]
    public Button backToMenuButton;           // Back button on setup
    public Button startGameButton;            // Final start game button on setup

    [Header("Terrain Settings")]
    public TMP_Dropdown terrainRoughnessDropdown;
    [Header("New World Settings")]
    public Toggle enableNewWorldToggle;
    public TMP_InputField newWorldContinentCountInput;
    public TMP_InputField newWorldBufferInput;
    public Toggle enableSecondNewWorldToggle;
    public TMP_InputField secondNewWorldBufferInput;
    public Toggle carveNewWorldOnTerrestrialToggle;

    [Header("Map Type Visualization")]
    public List<MapTypeSpriteEntry> mapTypeSpriteEntries = new List<MapTypeSpriteEntry>();
    
    [Header("Climate Fallback Icons (used when exact map name not found)")]
    [Tooltip("Fallback icons by climate index: 0=Frozen, 1=Cold, 2=Temperate, 3=Warm, 4=Hot, 5=Scorching")]
    public Sprite[] climateFallbackIcons = new Sprite[6];
    
    [Header("Land Type Fallback Icons (secondary fallback)")]
    [Tooltip("Fallback icons by land type: 0=Archipelago, 1=Islands, 2=Standard, 3=Continents, 4=Pangaea, 5=Terrestrial")]
    public Sprite[] landTypeFallbackIcons = new Sprite[6];

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
    
    // Climate settings - now with 6 options
    private int selectedClimatePreset = 2; // Default to Temperate
    
    // Land Mass settings
    private int selectedLandPreset = 2; // Default to Standard

    // Moisture settings - now with 6 options
    private int selectedMoisturePreset = 2; // Default to Standard
    
    // Animal settings
    private int selectedAnimalPrevalence = 3; // Default to normal
    private int selectedWaterwaysPreset = 1; // Standard
    private int selectedResourcesPreset = 1; // Standard
    private int selectedStartingSpread = 1; // Balanced
    
    // Climate preset values - each entry contains (polarThreshold, subPolarThreshold, equatorThreshold)
    private readonly (float polar, float subPolar, float equator)[] climatePresets = new[] {
        (0.50f, 0.24f, 0.05f),  // Frozen: enormous polar regions, minimal tropics
        (0.70f, 0.30f, 0.05f),  // Cold: large polar regions, large tundra
        (0.85f, 0.55f, 0.15f),  // Temperate: medium polar regions (default)
        (0.85f, 0.70f, 0.30f),  // Warm: smaller polar regions
        (0.90f, 0.75f, 0.50f),  // Hot: small polar regions
        (0.95f, 0.85f, 0.60f)   // Scorching: minimal polar regions, small tropical band
    };
    
    // Land mass preset values - revised for proper continent/island distinction
    private readonly LandPresetData[] landPresets = new[] {
        new LandPresetData { name = "Archipelago", continents = 0, islands = 25, continentSizeMultiplier = 1.0f, description = "Many small scattered islands" },
        new LandPresetData { name = "Islands", continents = 2, islands = 15, continentSizeMultiplier = 1.0f, description = "A few large islands with smaller ones" },
        new LandPresetData { name = "Standard", continents = 3, islands = 6, continentSizeMultiplier = 1.00f, description = "Balanced continents and islands" },
        new LandPresetData { name = "Large Continents", continents = 4, islands = 5, continentSizeMultiplier = 1.3f, description = "Multiple large continents" },
        new LandPresetData { name = "Pangaea", continents = 1, islands = 2, continentSizeMultiplier = 2.3f, description = "One massive supercontinent" },
        new LandPresetData { name = "Terrestrial", continents = 2, islands = 2, continentSizeMultiplier = 1.85f, description = "A world dominated by sprawling landmasses" }
    };

    // Moisture preset values
    private readonly (float frequency, float bias)[] moisturePresets = new[] {
    (2.5f, -0.20f),  // Desert: Very dry, minimal moisture
    (3.5f, -0.08f),  // Arid: Lower frequency and drier bias
    (5.0f, 0.00f),   // Standard: Slightly wetter to reduce overwhelming deserts
    (5.7f, 0.1f),    // Moist: Higher frequency and wetter bias
    (6.0f, 0.2f),    // Wet: High moisture for many forests/jungles
    (7.0f, 0.2f)     // Oceanic: Extremely wet world with minimal deserts
};
    
    // Terrain roughness presets (combines hills and mountains)
    private readonly (float hills, float mountains)[] terrainPresets = new[] {
        (0.4f, 0.6f),   // Smooth: few hills, almost no mountains
        (0.5f, 0.7f),   // Rolling: moderate hills, few mountains
        (0.65f, 0.8f),   // Rocky: many hills, some mountains
        (0.7f, 0.85f),  // Mountainous: lots of hills and mountains
        (0.8f, 0.9f)    // Alpine: extremely mountainous
    };

    private int selectedTerrainPreset = 2; // Default to Rocky

    // Preview update cache (prevents resending unchanged values).
    private int lastPreviewLandPreset = -1;
    private int lastPreviewClimatePreset = -1;
    private int lastPreviewMoisturePreset = -1;
    private int lastPreviewWaterwaysPreset = -1;
    private int lastPreviewTerrainPreset = -1;
    private int lastPreviewMapSize = -1;
    private int lastPreviewParsedSeed = int.MinValue;
    private bool? lastPreviewRandomSeed = null;
    private float lastPreviewMapStyle = float.NaN;
    
    // Flag to prevent UpdatePlanetPreview during early initialization
    private bool _previewInitializedOnce = false;

    [Header("References")]
    private GameManager gameManager; // Reference to GameManager

    [System.Serializable]
    public struct LandPresetData
    {
        public string name;
        public int continents;
        public int islands;
        public float continentSizeMultiplier; // 1.0 = default sizes, >1 = larger continents
        public string description;
    }


    void Start()
    {
        Debug.Log("[MainMenuManager] Start() called");
        
        // Get GameManager reference
        gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogError("GameManager not found!");
        }

        // Initialize panels: show only main menu at start
        Debug.Log("[MainMenuManager] Initializing panels...");
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
        else
            Debug.LogError("[MainMenuManager] mainMenuPanel is NULL!");
            
        if (civSelectionPanel != null)
            civSelectionPanel.SetActive(false);
        else
            Debug.LogError("[MainMenuManager] civSelectionPanel is NULL!");
            
        if (leaderSelectionPanel != null)
            leaderSelectionPanel.SetActive(false);
        else
            Debug.LogError("[MainMenuManager] leaderSelectionPanel is NULL!");
            
        if (gameSetupPanel != null)
            gameSetupPanel.SetActive(false);
        else
            Debug.LogError("[MainMenuManager] gameSetupPanel is NULL!");
            
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
        else
            Debug.LogError("[MainMenuManager] optionsPanel is NULL!");
        
        Debug.Log("[MainMenuManager] Panels initialized. optionsPanel is now DISABLED.");
        
        // Initialize audio settings EARLY to ensure optionsPanel state is correct
        Debug.Log("[MainMenuManager] Calling InitializeAudioSettings early...");
        try
        {
            InitializeAudioSettings();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MainMenuManager] Exception in InitializeAudioSettings: {ex}");
        }
        
        // Start menu music
        if (MenuMusicManager.Instance != null)
        {
            MenuMusicManager.Instance.PlayMenuMusic();
        }
        else
        {
            Debug.LogWarning("[MainMenuManager] MenuMusicManager.Instance is NULL - no music will play");
        }
        
        // Hook up button callbacks
        Debug.Log("[MainMenuManager] Hooking up button callbacks...");
        if (newGameButton != null) 
            newGameButton.onClick.AddListener(OnNewGameClicked);
        else
            Debug.LogError("[MainMenuManager] newGameButton is NULL!");
            
        if (loadGameButton != null) 
            loadGameButton.onClick.AddListener(OnLoadGameClicked);
        else
            Debug.LogWarning("[MainMenuManager] loadGameButton is NULL");
            
        if (optionsButton != null) 
            optionsButton.onClick.AddListener(OnOptionsClicked);
        else
            Debug.LogError("[MainMenuManager] optionsButton is NULL!");
            
        if (quitGameButton != null) 
            quitGameButton.onClick.AddListener(OnQuitGameClicked);
        else
            Debug.LogWarning("[MainMenuManager] quitGameButton is NULL");
            
        if (selectCivButton != null) 
            selectCivButton.onClick.AddListener(OnCivSelected);
        else
            Debug.LogWarning("[MainMenuManager] selectCivButton is NULL");
            
        if (backToMenuButton != null) 
            backToMenuButton.onClick.AddListener(OnBackToMenuClicked);
        else
            Debug.LogWarning("[MainMenuManager] backToMenuButton is NULL");
            
        if (backFromCivButton != null) 
            backFromCivButton.onClick.AddListener(OnBackFromCivSelectionClicked);
        else
            Debug.LogWarning("[MainMenuManager] backFromCivButton is NULL");
            
        if (startGameButton != null) 
            startGameButton.onClick.AddListener(OnStartGameClicked);
        else
            Debug.LogWarning("[MainMenuManager] startGameButton is NULL");
        
        // Options panel callbacks
        Debug.Log("[MainMenuManager] Hooking up options panel callbacks...");
        if (optionsBackButton != null) 
            optionsBackButton.onClick.AddListener(OnOptionsBackClicked);
        else
            Debug.LogError("[MainMenuManager] optionsBackButton is NULL!");
            
        if (menuMusicVolumeSlider != null) 
            menuMusicVolumeSlider.onValueChanged.AddListener(OnMenuMusicVolumeChanged);
        else
            Debug.LogWarning("[MainMenuManager] menuMusicVolumeSlider is NULL");
            
        if (menuMusicEnabledToggle != null) 
            menuMusicEnabledToggle.onValueChanged.AddListener(OnMenuMusicEnabledChanged);
        else
            Debug.LogWarning("[MainMenuManager] menuMusicEnabledToggle is NULL");
        
        // Autosave settings callbacks
        if (autosaveEnabledToggle != null) 
            autosaveEnabledToggle.onValueChanged.AddListener(OnAutosaveEnabledChanged);
        else
            Debug.LogWarning("[MainMenuManager] autosaveEnabledToggle is NULL");
            
        if (autosaveIntervalSlider != null) 
            autosaveIntervalSlider.onValueChanged.AddListener(OnAutosaveIntervalChanged);
        else
            Debug.LogWarning("[MainMenuManager] autosaveIntervalSlider is NULL");
        
        // New Leader Panel Buttons
        if (selectLeaderButton != null) 
            selectLeaderButton.onClick.AddListener(OnLeaderSelected);
        else
            Debug.LogWarning("[MainMenuManager] selectLeaderButton is NULL");
            
        if (backFromLeaderButton != null) 
            backFromLeaderButton.onClick.AddListener(OnBackFromLeaderSelectionClicked);
        else
            Debug.LogWarning("[MainMenuManager] backFromLeaderButton is NULL");
        
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
                "Pangaea",        // land == 4
                "Terrestrial"     // land == 5
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
        if (waterwaysDropdown != null)
        {
            waterwaysDropdown.ClearOptions();
            waterwaysDropdown.AddOptions(new List<string> { "Sparse", "Standard", "Abundant" });
            waterwaysDropdown.value = selectedWaterwaysPreset;
            waterwaysDropdown.onValueChanged.AddListener(OnWaterwaysChanged);
        }
        if (resourcesDropdown != null)
        {
            resourcesDropdown.ClearOptions();
            resourcesDropdown.AddOptions(new List<string> { "Scarce", "Standard", "Rich", "Legendary" });
            resourcesDropdown.value = selectedResourcesPreset;
            resourcesDropdown.onValueChanged.AddListener(OnResourcesPresetChanged);
        }
        if (startingSpreadDropdown != null)
        {
            startingSpreadDropdown.ClearOptions();
            startingSpreadDropdown.AddOptions(new List<string> { "Close", "Balanced", "Distant" });
            startingSpreadDropdown.value = selectedStartingSpread;
            startingSpreadDropdown.onValueChanged.AddListener(OnStartingSpreadChanged);
        }
        if (randomWorldSeedToggle != null)
        {
            randomWorldSeedToggle.isOn = true;
            randomWorldSeedToggle.onValueChanged.AddListener(OnRandomSeedToggleChanged);
        }
        if (worldSeedInput != null)
        {
            worldSeedInput.text = "839201";
            worldSeedInput.interactable = randomWorldSeedToggle == null || !randomWorldSeedToggle.isOn;
            worldSeedInput.onEndEdit.AddListener(OnWorldSeedEdited);
        }
        
        // Initialize terrain roughness dropdown if available
        if (terrainRoughnessDropdown != null)
        {
            terrainRoughnessDropdown.ClearOptions();
            terrainRoughnessDropdown.AddOptions(new List<string> { "Flat", "Smooth", "Standard", "Mountainous", "Alpine" });
            terrainRoughnessDropdown.value = selectedTerrainPreset;
            terrainRoughnessDropdown.onValueChanged.AddListener(OnTerrainPresetChanged);
        }
        
        // Initialize all sliders and toggles
        InitializeControls();
        
        // Initialize selected civ icon with placeholder
        if (selectedCivIcon != null)
        {
            selectedCivIcon.sprite = placeholderCivIcon;
            selectedCivIcon.gameObject.SetActive(true);
        }
        
        if (selectedCivDescription != null)
        {
            selectedCivDescription.text = "";
        }

        if (selectedCivBonuses != null)
        {
            selectedCivBonuses.text = "";
        }
        
        if (animalPrevalenceDropdown != null)
        {
            animalPrevalenceDropdown.ClearOptions();
            animalPrevalenceDropdown.AddOptions(new List<string> { "Dead", "Sparse", "Scarce", "Normal", "Lively", "Bustling" });
            animalPrevalenceDropdown.value = selectedAnimalPrevalence;
            animalPrevalenceDropdown.onValueChanged.AddListener(OnAnimalPrevalenceChanged);
            Debug.Log("[MainMenuManager] Animal prevalence dropdown initialized");
        }
        else
            Debug.LogError("[MainMenuManager] animalPrevalenceDropdown is NULL!");

        // Initialize map size dropdown
        Debug.Log("[MainMenuManager] Initializing map size dropdown...");
        try { InitializeMapSizeDropdown(); }
        catch (System.Exception ex) { Debug.LogError($"[MainMenuManager] Exception in InitializeMapSizeDropdown: {ex}"); }
        
        // Initialize New World UI controls
        Debug.Log("[MainMenuManager] Initializing new world controls...");
        try { InitializeNewWorldControls(); }
        catch (System.Exception ex) { Debug.LogError($"[MainMenuManager] Exception in InitializeNewWorldControls: {ex}"); }

        // NOTE: InitializeAudioSettings was ALREADY CALLED EARLY in Start()

        // Initialize autosave settings
        Debug.Log("[MainMenuManager] Initializing autosave settings...");
        try { InitializeAutosaveSettings(); }
        catch (System.Exception ex) { Debug.LogError($"[MainMenuManager] Exception in InitializeAutosaveSettings: {ex}"); }

        // Update the map type name only after setup dropdowns are populated.
        Debug.Log("[MainMenuManager] Updating map type name...");
        SafeUpdateMapTypeName();

        Debug.Log("[MainMenuManager] Validating dropdowns...");
        ValidateDropdown("AI Count", aiCountDropdown, 9);
        ValidateDropdown("City States", cityStateCountDropdown, 7);
        ValidateDropdown("Tribes", tribeCountDropdown, 7);
        ValidateDropdown("Wildlife", animalPrevalenceDropdown, 6);
        ValidateDropdown("Map Size", mapSizeDropdown, 3);
        
        Debug.Log("[MainMenuManager] Start() completed successfully");
    }

    private void Update()
    {
        // On first frame where preview textures are ready, update the map type and preview
        if (!_previewInitializedOnce && planetPreview != null && planetPreview.HasGeneratedWorldTextures)
        {
            _previewInitializedOnce = true;
            Debug.Log("[MainMenuManager] Planet preview textures are ready - calling UpdateMapTypeName.");
            try
            {
                UpdateMapTypeName();
                Debug.Log("[MainMenuManager] UpdateMapTypeName completed successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in deferred UpdateMapTypeName: {ex}");
            }
        }
    }

    private void SafeUpdateMapTypeName()
    {
        try
        {
            UpdateMapTypeName();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MainMenuManager] UpdateMapTypeName failed after dropdown setup: {ex}");
        }
    }

    private void ValidateDropdown(string label, TMP_Dropdown dropdown, int expectedOptions)
    {
        int optionCount = dropdown != null && dropdown.options != null ? dropdown.options.Count : 0;
        if (optionCount <= 1 || optionCount < expectedOptions)
        {
            string path = dropdown != null ? GetTransformPath(dropdown.transform) : "<unassigned>";
            Debug.LogWarning($"[MainMenuManager] Dropdown '{label}' has {optionCount} option(s); expected {expectedOptions}. Path: {path}");
        }
    }

    private string GetTransformPath(Transform target)
    {
        if (target == null)
            return "<null>";

        var path = target.name;
        while (target.parent != null)
        {
            target = target.parent;
            path = $"{target.name}/{path}";
        }

        return path;
    }
    
    private void InitializeControls()
    {
        Debug.Log("[MainMenuManager] InitializeControls() called");
        
        // Initialize AI count dropdown (0-8)
        if (aiCountDropdown != null)
        {
            var opts = new List<string>();
            for (int i = 0; i <= 8; i++) opts.Add(i.ToString());
            aiCountDropdown.ClearOptions();
            aiCountDropdown.AddOptions(opts);
            aiCountDropdown.value = Mathf.Clamp(aiCount, 0, 8);
            aiCountDropdown.onValueChanged.AddListener(OnAICountChanged);
            Debug.Log("[MainMenuManager] AI count dropdown initialized");
        }
        else
            Debug.LogError("[MainMenuManager] aiCountDropdown is NULL in InitializeControls!");

        // Initialize city-state count dropdown (0-6)
        if (cityStateCountDropdown != null)
        {
            var opts = new List<string>();
            for (int i = 0; i <= 6; i++) opts.Add(i.ToString());
            cityStateCountDropdown.ClearOptions();
            cityStateCountDropdown.AddOptions(opts);
            cityStateCountDropdown.value = Mathf.Clamp(cityStateCount, 0, 6);
            cityStateCountDropdown.onValueChanged.AddListener(OnCityStateCountChanged);
            Debug.Log("[MainMenuManager] City state count dropdown initialized");
        }
        else
            Debug.LogError("[MainMenuManager] cityStateCountDropdown is NULL in InitializeControls!");
            
        UpdateCityStateCountText();

        // Initialize tribe count dropdown (0-6)
        if (tribeCountDropdown != null)
        {
            var opts = new List<string>();
            for (int i = 0; i <= 6; i++) opts.Add(i.ToString());
            tribeCountDropdown.ClearOptions();
            tribeCountDropdown.AddOptions(opts);
            tribeCountDropdown.value = Mathf.Clamp(tribeCount, 0, 6);
            tribeCountDropdown.onValueChanged.AddListener(OnTribeCountChanged);
            Debug.Log("[MainMenuManager] Tribe count dropdown initialized");
        }
        else
            Debug.LogError("[MainMenuManager] tribeCountDropdown is NULL in InitializeControls!");
            
        UpdateTribeCountText();

        Debug.Log("[MainMenuManager] NOT calling UpdateMapTypeName during InitializeControls - will call after preview is ready");
        
        // River UI removed (deprecated). River count still determined by moisture preset.
        
        // Update preset icons ONLY (no preview update yet)
        Debug.Log("[MainMenuManager] Updating preset icons (no preview yet)...");
        UpdatePresetIcons();
        
        Debug.Log("[MainMenuManager] InitializeControls() completed");
    }
    
    #region Value Change Handlers and Text Updates
    
    // Civilization Counts
    private void OnAICountChanged(int value)
    {
        aiCount = value;
        GameSetupData.numberOfCivilizations = aiCount;  // Immediately save to GameSetupData
        UpdateMapTypeName(); // Update description with new civ count
    }
    
    private void OnCityStateCountChanged(int value)
    {
        cityStateCount = value;
        GameSetupData.numberOfCityStates = cityStateCount;  // Immediately save to GameSetupData
        UpdateCityStateCountText();
        UpdateMapTypeName(); // Update description with new city-state count
    }
    
    private void UpdateCityStateCountText()
    {
        // UI text removed; nothing to update here.
    }
    
    private void OnTribeCountChanged(int value)
    {
        tribeCount = value;
        GameSetupData.numberOfTribes = tribeCount;  // Immediately save to GameSetupData
        UpdateTribeCountText();
        UpdateMapTypeName(); // Update description with new tribe count
    }
    
    private void UpdateTribeCountText()
    {
        // UI text removed; nothing to update here.
    }
    
    // Map Settings
    private void OnMapSizeChanged(int value)
    {
        Debug.Log($"[MainMenuManager] OnMapSizeChanged called with value: {value}");
        GameManager.MapSize selectedSize = (GameManager.MapSize)value;
        GameSetupData.mapSize = selectedSize;
        Debug.Log($"[MainMenuManager] Map size set to {selectedSize}");
        UpdatePlanetSizeText();
        UpdatePlanetPreview();
    }
    
    private void UpdatePlanetSizeText()
    {
        GameManager.MapSize size = GameSetupData.mapSize;
        // Flat-only: show width x height based on size preset
        GameManager.GetFlatMapSizeParams(size, out float width, out float height);
        string displayName = GetMapSizeDisplayName(size);
        // Planet size UI text removed; nothing to update here.
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
    
    
    
    // River UI deprecated (river count still tracked internally via moisture preset)
    
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
    private void OnWaterwaysChanged(int value)
    {
        selectedWaterwaysPreset = Mathf.Clamp(value, 0, 2);
        UpdatePlanetPreview();
        UpdateMapTypeName();
    }
    private void OnResourcesPresetChanged(int value)
    {
        selectedResourcesPreset = Mathf.Clamp(value, 0, 3);
        UpdatePlanetPreview();
    }
    private void OnStartingSpreadChanged(int value) => selectedStartingSpread = Mathf.Clamp(value, 0, 2);
    private void OnRandomSeedToggleChanged(bool isRandom)
    {
        if (worldSeedInput != null) worldSeedInput.interactable = !isRandom;
        UpdatePlanetPreview();
    }
    private void OnWorldSeedEdited(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!int.TryParse(text.Trim(), out _))
            worldSeedInput.text = "839201";
        UpdatePlanetPreview();
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
            mapTypeName.text = mapTypeNameStr;
        }

        // Update the map type description
        if (mapTypeDescription != null)
        {
            string description = MapTypeDescriptionGenerator.GetDescription(climateIndex, moistureIndex, landIndex, elevationCategory, aiCount, cityStateCount, tribeCount, selectedAnimalPrevalence, selectedWaterwaysPreset);
            mapTypeDescription.text = description;
        }

        // Update map type icon
        UpdateMapTypeIcon();

        // Update planet preview sphere if assigned
        UpdatePlanetPreview();
    }

    private int GetElevationCategory()
    {
        var terrainPreset = terrainPresets[selectedTerrainPreset];
        if (terrainPreset.hills >= 0.8f && terrainPreset.mountains >= 0.9f)
            return 3; // Alpine (extreme mountains)
        if (terrainPreset.hills >= 0.7f && terrainPreset.mountains >= 0.85f)
            return 2; // Mountainous
        if (terrainPreset.hills >= 0.5f)
            return 1; // Hilly
        return 0; // Low
    }

    /// <summary>
    /// Push current map settings to the MenuPlanetPreview sphere (if assigned).
    /// Maps the existing dropdown indices to the preview's shader parameters.
    /// </summary>
    private void UpdatePlanetPreview()
    {
        // SAFETY: Skip if preview not ready yet (still initializing GPU resources)
        if (planetPreview == null)
        {
            Debug.LogWarning("[MainMenuManager] UpdatePlanetPreview: planetPreview is NULL - skipping");
            return;
        }

        // Land shape: map selectedLandPreset to scale/threshold.
        //  
        float[] landScales     = { 4.5f, 3.2f, 2.2f, 1.4f, 0.82f, 0.65f };
        float[] landThresholds = { 0.64f, 0.62f, 0.53f, 0.49f, 0.40f, 0.29f };
        int landIdx = Mathf.Clamp(selectedLandPreset, 0, landScales.Length - 1);
        if (lastPreviewLandPreset != selectedLandPreset)
        {
            try
            {
                planetPreview.SetLandPreset(landScales[landIdx], landThresholds[landIdx], landIdx);
                lastPreviewLandPreset = selectedLandPreset;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in SetLandPreset: {ex}");
            }
        }

        // Temperature: map selectedClimatePreset (0-5) to 0–1
        // 0=Frozen→0, 5=Scorching→1
        float temp = Mathf.Clamp01(selectedClimatePreset / 5f);
        if (lastPreviewClimatePreset != selectedClimatePreset)
        {
            try
            {
                planetPreview.SetTemperature(temp);
                lastPreviewClimatePreset = selectedClimatePreset;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in SetTemperature: {ex}");
            }
        }

        // Moisture: map selectedMoisturePreset (0-5) to 0–1
        // 0=Very Low→0, 5=Extreme→1
        float moist = Mathf.Clamp01(selectedMoisturePreset / 5f);
        if (lastPreviewMoisturePreset != selectedMoisturePreset)
        {
            try
            {
                planetPreview.SetMoisture(moist);
                lastPreviewMoisturePreset = selectedMoisturePreset;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in SetMoisture: {ex}");
            }
        }

        if (lastPreviewWaterwaysPreset != selectedWaterwaysPreset)
        {
            try
            {
                planetPreview.SetWaterwaysPreset(selectedWaterwaysPreset);
                lastPreviewWaterwaysPreset = selectedWaterwaysPreset;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in SetWaterwaysPreset: {ex}");
            }
        }

        // Elevation: map terrain roughness preset directly to preserve five distinct categories.
        int elevCat = GetElevationCategory();
        float elev = selectedTerrainPreset switch
        {
            0 => 0.10f,
            1 => 0.30f,
            2 => 0.50f,
            3 => 0.75f,
            4 => 1.00f,
            _ => 0.50f
        };
        if (lastPreviewTerrainPreset != selectedTerrainPreset)
        {
            try
            {
                planetPreview.SetTerrainRoughnessPreset(selectedTerrainPreset);
                planetPreview.SetElevation(elev);
                lastPreviewTerrainPreset = selectedTerrainPreset;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in SetElevation: {ex}");
            }
        }

        // Map style: 0 = normal, 0.5 = infernal, 1.0 = demonic
        // Detect from generated map type name (same logic as GameSetupData flags).
        string previewName = MapTypeNameGenerator.GetMapTypeName(
            selectedClimatePreset, selectedMoisturePreset, selectedLandPreset, elevCat);
        string lower = previewName.ToLower();
        float mapStyleVal = 0f;
        if (lower.Contains("demonic") || lower.Contains("hellscape"))
            mapStyleVal = 1f;   // Demonic: darkest, most intense
        else if (lower.Contains("infernal"))
            mapStyleVal = 0.5f; // Infernal: volcanic, lava oceans

        if (float.IsNaN(lastPreviewMapStyle) || !Mathf.Approximately(lastPreviewMapStyle, mapStyleVal))
        {
            try
            {
                planetPreview.SetMapStyle(mapStyleVal);
                lastPreviewMapStyle = mapStyleVal;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in SetMapStyle: {ex}");
            }
        }

        float sizeScale = GameSetupData.mapSize == GameManager.MapSize.Small ? 0.86f :
                          (GameSetupData.mapSize == GameManager.MapSize.Large ? 1.18f : 1.0f);
        int currentMapSizeValue = (int)GameSetupData.mapSize;
        if (lastPreviewMapSize != currentMapSizeValue)
        {
            try
            {
                planetPreview.SetPlanetScaleMultiplier(sizeScale);
                lastPreviewMapSize = currentMapSizeValue;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in SetPlanetScaleMultiplier: {ex}");
            }
        }

        bool randomSeed = randomWorldSeedToggle == null || randomWorldSeedToggle.isOn;
        int parsedSeed = 839201;
        if (worldSeedInput != null && int.TryParse(worldSeedInput.text, out int seedVal)) parsedSeed = seedVal;

        bool seedModeChanged = !lastPreviewRandomSeed.HasValue || lastPreviewRandomSeed.Value != randomSeed;
        bool seedValueChanged = lastPreviewParsedSeed != parsedSeed;

        if (seedModeChanged || seedValueChanged)
        {
            try
            {
                planetPreview.SetWorldSeed(parsedSeed, randomSeed);
                lastPreviewRandomSeed = randomSeed;
                lastPreviewParsedSeed = parsedSeed;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in SetWorldSeed: {ex}");
            }
        }
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
        civButtons.Clear();
        
        // Show placeholder civ icon until a civ is selected
        if (selectedCivIcon != null)
        {
            selectedCivIcon.sprite = placeholderCivIcon;
            selectedCivIcon.gameObject.SetActive(true);
        }
        
        if (selectedCivName != null)
        {
            selectedCivName.text = "";
        }
        
        if (selectedCivDescription != null)
        {
            selectedCivDescription.text = "";
        }

        if (selectedCivBonuses != null)
        {
            selectedCivBonuses.text = "";
        }
        
        if (selectCivButton != null)
            selectCivButton.interactable = false;
        
        selectedCivilization = null;

        bool hasManualEntries = civSelectionEntries != null && civSelectionEntries.Count > 0;
        if (hasManualEntries)
        {
            foreach (var entry in civSelectionEntries)
            {
                if (entry.civData == null || entry.civButton == null)
                    continue;

                if (entry.civData.isTribe || entry.civData.isCityState)
                {
                    entry.civButton.gameObject.SetActive(false);
                    continue;
                }

                var button = entry.civButton;
                var civData = entry.civData;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OnCivButtonClicked(button, civData));

                var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                    buttonText.text = civData.civName;
                if (entry.buttonIconImage != null)
                    entry.buttonIconImage.sprite = civData.icon != null ? civData.icon : placeholderCivIcon;

                button.gameObject.SetActive(true);
                civButtons.Add(button);
            }

            return;
        }
        
        // Fallback: load all CivData assets from Resources/Civilizations
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
    
    private void ApplyCivEntryBackground(CivData civData)
    {
        if (!enableCivEntryBackgroundOverrides || civData == null || civSelectionEntries == null)
            return;

        for (int i = 0; i < civSelectionEntries.Count; i++)
        {
            var entry = civSelectionEntries[i];
            if (entry.civData != civData) continue;

            if (entry.backgroundTargetImage != null && entry.backgroundSprite != null)
            {
                entry.backgroundTargetImage.sprite = entry.backgroundSprite;
            }
            return;
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

        ApplyCivEntryBackground(civData);
        
        // Show the civilization name
        if (selectedCivName != null)
        {
            selectedCivName.text = civData.civName;
        }
        
        // Show the civilization icon (use placeholder if none assigned)
        if (selectedCivIcon != null)
        {
            selectedCivIcon.sprite = civData.icon != null ? civData.icon : placeholderCivIcon;
            selectedCivIcon.gameObject.SetActive(true);
        }
        
        // Show civilization description only
        if (selectedCivDescription != null)
        {
            if (!string.IsNullOrWhiteSpace(civData.description))
            {
                selectedCivDescription.text = civData.description.Trim();
            }
            else
            {
                selectedCivDescription.text = $"The {civData.civName} are a notable civilization.";
            }
        }

        // Show civilization bonuses + unique access
        if (selectedCivBonuses != null)
        {
            var civBonuses = new List<string>();
            if (civData.foodModifier != 0f) civBonuses.Add($"{(civData.foodModifier > 0 ? "+" : "")}{civData.foodModifier:P0} Food");
            if (civData.productionModifier != 0f) civBonuses.Add($"{(civData.productionModifier > 0 ? "+" : "")}{civData.productionModifier:P0} Production");
            if (civData.goldModifier != 0f) civBonuses.Add($"{(civData.goldModifier > 0 ? "+" : "")}{civData.goldModifier:P0} Gold");
            if (civData.scienceModifier != 0f) civBonuses.Add($"{(civData.scienceModifier > 0 ? "+" : "")}{civData.scienceModifier:P0} Science");
            if (civData.cultureModifier != 0f) civBonuses.Add($"{(civData.cultureModifier > 0 ? "+" : "")}{civData.cultureModifier:P0} Culture");
            if (civData.faithModifier != 0f) civBonuses.Add($"{(civData.faithModifier > 0 ? "+" : "")}{civData.faithModifier:P0} Faith");
            if (civData.attackBonus != 0f) civBonuses.Add($"{(civData.attackBonus > 0 ? "+" : "")}{civData.attackBonus:P0} Attack");
            if (civData.defenseBonus != 0f) civBonuses.Add($"{(civData.defenseBonus > 0 ? "+" : "")}{civData.defenseBonus:P0} Defense");
            if (civData.movementBonus != 0f) civBonuses.Add($"{(civData.movementBonus > 0 ? "+" : "")}{civData.movementBonus:P0} Movement");

            var uniqueAccess = new List<string>();
            if (civData.uniqueUnits != null)
            {
                foreach (var unit in civData.uniqueUnits)
                {
                    if (unit != null && !string.IsNullOrWhiteSpace(unit.unitName))
                    {
                        uniqueAccess.Add($"Unique Unit: {unit.unitName}");
                    }
                }
            }
            if (civData.uniqueBuildings != null)
            {
                foreach (var building in civData.uniqueBuildings)
                {
                    if (building != null && !string.IsNullOrWhiteSpace(building.buildingName))
                    {
                        uniqueAccess.Add($"Unique Building: {building.buildingName}");
                    }
                }
            }

            var allEntries = new List<string>();
            allEntries.AddRange(civBonuses);
            allEntries.AddRange(uniqueAccess);

            if (allEntries.Count > 0)
            {
                selectedCivBonuses.text = string.Join("\n", allEntries);
            }
            else
            {
                selectedCivBonuses.text = "No civilization bonuses.";
            }
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
        GameSetupData.selectedWaterwaysPreset = selectedWaterwaysPreset;
        GameSetupData.selectedResourcesPreset = selectedResourcesPreset;
        GameSetupData.selectedStartingSpread = selectedStartingSpread;
        GameSetupData.useRandomWorldSeed = randomWorldSeedToggle == null || randomWorldSeedToggle.isOn;
        int parsedSeed = 839201;
        if (worldSeedInput != null && int.TryParse(worldSeedInput.text, out int seedVal)) parsedSeed = seedVal;
        GameSetupData.worldSeed = parsedSeed;
        
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
                          mapTypeLower.Contains("hellscape");
        GameSetupData.isScorchedWorld = mapTypeLower.Contains("scorched") || 
                                       mapTypeLower.Contains("ashlands") || 
                                       mapTypeLower.Contains("charred");
        // --- New: Ice World flag
        GameSetupData.isIceWorld = mapTypeLower.Contains("ice world") || mapTypeLower.Contains("icicle") || mapTypeLower.Contains("cryo");
        // River settings
        GameSetupData.enableRivers = true;
        int[] waterwayRivers = { 2, 6, 10 };
        int[] waterwayLakes = { 3, 8, 14 };
        int[] lakeMin = { 1, 1, 2 };
        int[] lakeMax = { 1, 2, 3 };
        GameSetupData.riverCount = waterwayRivers[Mathf.Clamp(selectedWaterwaysPreset, 0, 2)];
        GameSetupData.numberOfLakes = waterwayLakes[Mathf.Clamp(selectedWaterwaysPreset, 0, 2)];
        GameSetupData.lakeMinRadiusTiles = lakeMin[Mathf.Clamp(selectedWaterwaysPreset, 0, 2)];
        GameSetupData.lakeMaxRadiusTiles = lakeMax[Mathf.Clamp(selectedWaterwaysPreset, 0, 2)];
        float[] resourceMult = { 0.7f, 1f, 1.35f, 1.8f };
        GameSetupData.resourceSpawnMultiplier = resourceMult[Mathf.Clamp(selectedResourcesPreset, 0, 3)];
        
        GameSetupData.enableLakes = true;
        
        // Get current climate thresholds from presets
        var climateThresholds = climatePresets[selectedClimatePreset];
        GameSetupData.polarLatitudeThreshold = climateThresholds.polar;
        GameSetupData.subPolarLatitudeThreshold = climateThresholds.subPolar;
        GameSetupData.equatorLatitudeThreshold = climateThresholds.equator;
        
        // Get current moisture settings from presets
        var moistureSettings = moisturePresets[selectedMoisturePreset];
        GameSetupData.moistureBias = moistureSettings.bias;

        // Set temperatureBias and moistureBias for strong climate impact
        float[] tempBiases = { -0.30f, -0.15f, 0.1f, 0.15f, 0.2f, 0.25f }; // Frozen to Scorching
        float[] moistBiases = { -0.30f, -0.15f, -0.05f, 0.1f, 0.2f, 0.32f }; // Desert to Oceanic
        GameSetupData.temperatureBias = tempBiases[Mathf.Clamp(selectedClimatePreset, 0, tempBiases.Length-1)];
        GameSetupData.moistureBias += moistBiases[Mathf.Clamp(selectedMoisturePreset, 0, moistBiases.Length-1)];
        
        // Land generation settings (counts/toggles only; prefab owns tuning and size ranges)
        var landPreset = landPresets[selectedLandPreset];
        GameSetupData.numberOfContinents = landPreset.continents;
        GameSetupData.numberOfIslands = landPreset.islands;
        GameSetupData.generateIslands = landPreset.islands > 0;
        GameSetupData.continentSizeMultiplier = landPreset.continentSizeMultiplier;

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
        Debug.Log($"[MainMenuManager] OnAnimalPrevalenceChanged called with value: {value}");
        selectedAnimalPrevalence = value;
        GameSetupData.animalPrevalence = selectedAnimalPrevalence;  // Immediately save to GameSetupData
        Debug.Log($"[MainMenuManager] Animal prevalence set to {selectedAnimalPrevalence}");
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

        // Reset display until one is chosen
        if (selectedLeaderName != null) selectedLeaderName.text = "Select a Leader";
        if (selectedLeaderDescription != null) selectedLeaderDescription.text = "";
        if (selectedLeaderBonuses != null) selectedLeaderBonuses.text = "";
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
            var buttonLabel = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonLabel != null)
                buttonLabel.text = leaderData.leaderName;
            button.onClick.AddListener(() => OnLeaderButtonClicked(button, leaderData));
            leaderButtons.Add(button);
        }
    }

    private void ApplyLeaderEntryBackground(LeaderData leaderData)
    {
        if (!enableLeaderEntryBackgroundOverrides || leaderData == null || leaderSelectionEntries == null)
            return;

        for (int i = 0; i < leaderSelectionEntries.Count; i++)
        {
            var entry = leaderSelectionEntries[i];
            if (entry.leaderData != leaderData) continue;

            if (leaderSelectionBackgroundImage != null && entry.backgroundSprite != null)
            {
                leaderSelectionBackgroundImage.sprite = entry.backgroundSprite;
            }
            return;
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
        ApplyLeaderEntryBackground(leaderData);

        // Update display
        if (selectedLeaderName != null)
        {
            selectedLeaderName.text = leaderData.leaderName;
        }
        if (selectedLeaderDescription != null)
        {
            // Build a description string: biography and ability
            var sb = new System.Text.StringBuilder();
            // 1. Biography/Description
            if (!string.IsNullOrWhiteSpace(leaderData.biography))
                sb.AppendLine(leaderData.biography.Trim());
            else
                sb.AppendLine($"{leaderData.leaderName} is a notable leader.");

            // 2. Ability
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

        if (selectedLeaderBonuses != null)
        {
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
                selectedLeaderBonuses.text = string.Join("\n", bonuses);
            }
            else
            {
                selectedLeaderBonuses.text = "No leader bonuses.";
            }
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
        Debug.Log("[MainMenuManager] InitializeMapSizeDropdown() called");
        
        if (mapSizeDropdown == null)
        {
            Debug.LogError("[MainMenuManager] mapSizeDropdown is NULL in InitializeMapSizeDropdown!");
            return;
        }
        
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
        
        Debug.Log($"[MainMenuManager] Map size dropdown initialized with {options.Count} options, value set to {mapSizeDropdown.value}");
    }

    private void InitializeNewWorldControls()
    {
        if (enableNewWorldToggle != null)
        {
            enableNewWorldToggle.isOn = GameSetupData.enableNewWorld;
            enableNewWorldToggle.onValueChanged.AddListener(OnEnableNewWorldChanged);
        }
        if (newWorldBufferInput != null)
        {
            newWorldBufferInput.text = GameSetupData.newWorldBufferTiles.ToString();
            newWorldBufferInput.onEndEdit.AddListener(OnNewWorldBufferChanged);
        }
        if (newWorldContinentCountInput != null)
        {
            newWorldContinentCountInput.text = Mathf.Max(1, GameSetupData.newWorldContinentCount).ToString();
            newWorldContinentCountInput.onEndEdit.AddListener(OnNewWorldContinentCountChanged);
        }
        if (enableSecondNewWorldToggle != null)
        {
            enableSecondNewWorldToggle.isOn = GameSetupData.enableSecondNewWorld;
            enableSecondNewWorldToggle.onValueChanged.AddListener(OnEnableSecondNewWorldChanged);
        }
        if (secondNewWorldBufferInput != null)
        {
            secondNewWorldBufferInput.text = GameSetupData.secondNewWorldBufferTiles.ToString();
            secondNewWorldBufferInput.onEndEdit.AddListener(OnSecondNewWorldBufferChanged);
        }
        if (carveNewWorldOnTerrestrialToggle != null)
        {
            carveNewWorldOnTerrestrialToggle.isOn = GameSetupData.carveNewWorldOnTerrestrial;
            carveNewWorldOnTerrestrialToggle.onValueChanged.AddListener(OnCarveNewWorldChanged);
        }

        if (PlanetGenerator.Instance != null)
        {
            var pg = PlanetGenerator.Instance;
            if (enableNewWorldToggle != null) enableNewWorldToggle.isOn = pg.enableNewWorld;
            if (newWorldContinentCountInput != null) newWorldContinentCountInput.text = Mathf.Max(1, pg.newWorldContinentCount).ToString();
            if (newWorldBufferInput != null) newWorldBufferInput.text = pg.newWorldBufferTiles.ToString();
            if (enableSecondNewWorldToggle != null) enableSecondNewWorldToggle.isOn = pg.enableSecondNewWorld;
            if (secondNewWorldBufferInput != null) secondNewWorldBufferInput.text = pg.secondNewWorldBufferTiles.ToString();
            if (carveNewWorldOnTerrestrialToggle != null) carveNewWorldOnTerrestrialToggle.isOn = pg.carveNewWorldOnTerrestrial;
        }
    }

    private void OnEnableNewWorldChanged(bool val)
    {
        GameSetupData.enableNewWorld = val;
        if (PlanetGenerator.Instance != null) PlanetGenerator.Instance.enableNewWorld = val;
    }

    private void OnNewWorldBufferChanged(string text)
    {
        if (int.TryParse(text, out int v))
        {
            v = Mathf.Clamp(v, 1, 64);
            GameSetupData.newWorldBufferTiles = v;
            if (PlanetGenerator.Instance != null) PlanetGenerator.Instance.newWorldBufferTiles = v;
        }
    }

    private void OnNewWorldContinentCountChanged(string text)
    {
        if (int.TryParse(text, out int v))
        {
            v = Mathf.Clamp(v, 1, 8);
            GameSetupData.newWorldContinentCount = v;
            if (PlanetGenerator.Instance != null) PlanetGenerator.Instance.newWorldContinentCount = v;
        }
    }

    private void OnEnableSecondNewWorldChanged(bool val)
    {
        GameSetupData.enableSecondNewWorld = val;
        if (PlanetGenerator.Instance != null) PlanetGenerator.Instance.enableSecondNewWorld = val;
    }

    private void OnSecondNewWorldBufferChanged(string text)
    {
        if (int.TryParse(text, out int v))
        {
            v = Mathf.Clamp(v, 0, 128);
            GameSetupData.secondNewWorldBufferTiles = v;
            if (PlanetGenerator.Instance != null) PlanetGenerator.Instance.secondNewWorldBufferTiles = v;
        }
    }

    private void OnCarveNewWorldChanged(bool val)
    {
        GameSetupData.carveNewWorldOnTerrestrial = val;
        if (PlanetGenerator.Instance != null) PlanetGenerator.Instance.carveNewWorldOnTerrestrial = val;
    }

    #region Options Menu Methods

    private void InitializeAudioSettings()
    {
        Debug.Log("[MainMenuManager] InitializeAudioSettings() called");
        
        // Initialize options panel state
        if (optionsPanel != null)
        {
            optionsPanel.SetActive(false);
            Debug.Log("[MainMenuManager] Options panel initialized and DISABLED in InitializeAudioSettings");
        }
        else
            Debug.LogError("[MainMenuManager] InitializeAudioSettings: optionsPanel is NULL!");

        // Initialize menu music volume slider
        if (menuMusicVolumeSlider != null)
        {
            try
            {
                float savedVolume = PlayerPrefs.GetFloat("MenuMusicVolume", 0.75f);
                menuMusicVolumeSlider.value = savedVolume;
                UpdateMenuMusicVolumeText(savedVolume);
                Debug.Log($"[MainMenuManager] Menu music volume set to {savedVolume}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception setting menu music volume: {ex}");
            }
        }
        else
            Debug.LogWarning("[MainMenuManager] menuMusicVolumeSlider is NULL in InitializeAudioSettings");

        // Initialize menu music enabled toggle
        if (menuMusicEnabledToggle != null)
        {
            try
            {
                bool musicEnabled = PlayerPrefs.GetInt("MenuMusicEnabled", 1) == 1;
                menuMusicEnabledToggle.isOn = musicEnabled;
                Debug.Log($"[MainMenuManager] Menu music enabled set to {musicEnabled}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception setting menu music enabled: {ex}");
            }
        }
        else
            Debug.LogWarning("[MainMenuManager] menuMusicEnabledToggle is NULL in InitializeAudioSettings");
            
        Debug.Log("[MainMenuManager] InitializeAudioSettings() completed");
    }

    void OnOptionsClicked()
    {
        Debug.Log("[MainMenuManager] OnOptionsClicked called");
        if (mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(false);
            Debug.Log("[MainMenuManager] Main menu panel disabled");
        }
        else
            Debug.LogError("[MainMenuManager] OnOptionsClicked: mainMenuPanel is NULL!");
        
        if (optionsPanel != null)
        {
            optionsPanel.SetActive(true);
            Debug.Log("[MainMenuManager] Options panel ENABLED");
        }
        else
            Debug.LogError("[MainMenuManager] OnOptionsClicked: optionsPanel is NULL!");
    }

    void OnOptionsBackClicked()
    {
        Debug.Log("[MainMenuManager] OnOptionsBackClicked called");
        if (optionsPanel != null)
        {
            optionsPanel.SetActive(false);
            Debug.Log("[MainMenuManager] Options panel DISABLED");
        }
        else
            Debug.LogError("[MainMenuManager] OnOptionsBackClicked: optionsPanel is NULL!");
        
        if (mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(true);
            Debug.Log("[MainMenuManager] Main menu panel re-enabled");
        }
        else
            Debug.LogError("[MainMenuManager] OnOptionsBackClicked: mainMenuPanel is NULL!");
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
}

    void OnAutosaveIntervalChanged(float interval)
    {
        int intervalInt = Mathf.RoundToInt(interval);
        UpdateAutosaveIntervalText(intervalInt);
        PlayerPrefs.SetInt("AutosaveInterval", intervalInt);
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
}
    }

    /// <summary>
    /// Show the save/load panel (this should integrate with your save system)
    /// </summary>
    public void ShowSaveLoadPanel()
    {
        // For now, just show a debug message
        // In the future, this should open a save/load UI panel
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
}
    }

    /// <summary>
    /// Quit the game application
    /// </summary>
    public void QuitGame()
    {
Application.Quit();
        
        // For editor testing
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    #endregion

} 
