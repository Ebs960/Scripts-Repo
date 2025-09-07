using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

/// <summary>
/// Comprehensive pause menu system that handles pause/resume, save/load, options, and exit functionality.
/// Also includes a complete save/load game system and audio options management.
/// </summary>
public class PauseMenuManager : MonoBehaviour
{
    public static PauseMenuManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject pauseMenuPanel;
    public Button resumeButton;
    public Button saveGameButton;
    public Button loadGameButton;
    public Button optionsButton;
    public Button exitToMainMenuButton;
    public Button exitGameButton;

    [Header("Options Menu")]
    public GameObject optionsPanel;
    public Button optionsBackButton;

    [Header("Audio Settings")]
    public Slider musicVolumeSlider;
    public TextMeshProUGUI musicVolumeText;
    public Toggle musicEnabledToggle;

    [Header("Save/Load System")]
    public GameObject saveLoadPanel; // Panel containing all save/load UI elements
    public Transform saveSlotContainer;
    public GameObject saveSlotButtonPrefab;
    public Button createNewSaveButton;
    public Button deleteSaveButton;
    public TextMeshProUGUI saveStatusText;
    public int maxSaveSlots = 10;

    [Header("Autosave System")]
    [Tooltip("Enable automatic saving every X turns")]
    public bool enableAutosave = true;
    [Tooltip("Number of turns between autosaves")]
    public int autosaveInterval = 5;
    [Tooltip("Maximum number of autosave files to keep")]
    public int maxAutosaveFiles = 3;
    [Tooltip("Show notification when autosave occurs")]
    public bool showAutosaveNotification = true;
    // Removed autosaveNotificationText - now using UIManager.ShowNotification instead

    private bool isPaused = false;
    private bool canTogglePause = true;
    private List<SaveSlotButton> saveSlotButtons = new List<SaveSlotButton>();
    private int selectedSaveSlot = -1;
    private int lastAutosaveTurn = -1;
    // Removed autosaveNotificationCoroutine - no longer needed with UIManager notifications

    [Serializable]
    public class GameSaveData
    {
        public string saveName;
        public string dateTime;
        public int currentTurn;
        public string playerCivName;
        public int playerCivIndex;
        public GameManager.MapSize mapSize;
        public bool enableMultiPlanetSystem;
        public int currentPlanetIndex;
        
        // Add more save data fields as needed
        public Vector3 cameraPosition;
        public Vector3 cameraRotation;
    public bool gameInProgress;
    public bool isAutosave; // Mark if this is an autosave
    // Persisted manager/job state
    public List<ImprovementManager.JobAssignmentSaveData> jobAssignments;
        
        public GameSaveData()
        {
            dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            isAutosave = false;
        }
    }

    [Serializable]
    public class SaveSlotData
    {
        public int slotIndex;
        public bool hasData;
        public GameSaveData saveData;
        
        public SaveSlotData(int index)
        {
            slotIndex = index;
            hasData = false;
            saveData = null;
        }
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        // Initialize UI - make sure pause menu starts hidden
        if (UIManager.Instance != null)
        {
            UIManager.Instance.HidePauseMenu();
        }
        else if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }
        
        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        // Hide save/load UI elements at startup
        HideSaveLoadUI();

        // Setup button events
        SetupButtonEvents();
        
        // Initialize audio settings
        InitializeAudioSettings();
        
        // Initialize save system
        InitializeSaveSystem();
        
        // Initialize autosave system
        InitializeAutosaveSystem();

        // Allow pausing after a short delay to prevent immediate pause on game start
        Invoke(nameof(EnablePauseToggle), 1f);
    }

    void Update()
    {
        // Check for escape key - ONLY handle if we're the singleton instance
        if (Input.GetKeyDown(KeyCode.Escape) && canTogglePause && Instance == this)
        {
            Debug.Log($"[PauseMenuManager] Escape key pressed. canTogglePause: {canTogglePause}, isPaused: {isPaused}");
            TogglePause();
        }
    }

    private void SetupButtonEvents()
    {
        if (resumeButton != null)
            resumeButton.onClick.AddListener(ResumeGame);
        
        if (saveGameButton != null)
            saveGameButton.onClick.AddListener(ShowSaveMenu);
        
        if (loadGameButton != null)
            loadGameButton.onClick.AddListener(ShowLoadMenu);
        
        if (optionsButton != null)
            optionsButton.onClick.AddListener(ShowOptions);
        
        if (exitToMainMenuButton != null)
            exitToMainMenuButton.onClick.AddListener(ExitToMainMenu);
        
        if (exitGameButton != null)
            exitGameButton.onClick.AddListener(ExitGame);
        
        if (optionsBackButton != null)
            optionsBackButton.onClick.AddListener(HideOptions);

        if (createNewSaveButton != null)
            createNewSaveButton.onClick.AddListener(CreateNewSave);
        
        if (deleteSaveButton != null)
            deleteSaveButton.onClick.AddListener(DeleteSelectedSave);

        // Audio setting events
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        
        if (musicEnabledToggle != null)
            musicEnabledToggle.onValueChanged.AddListener(OnMusicEnabledChanged);
    }

    private void InitializeAudioSettings()
    {
        // Initialize music volume slider
        if (musicVolumeSlider != null)
        {
            float savedVolume = PlayerPrefs.GetFloat("GameMusicVolume", 0.75f);
            musicVolumeSlider.value = savedVolume;
            UpdateMusicVolumeText(savedVolume);
        }

        // Initialize music enabled toggle
        if (musicEnabledToggle != null)
        {
            bool musicEnabled = PlayerPrefs.GetInt("MusicEnabled", 1) == 1;
            musicEnabledToggle.isOn = musicEnabled;
        }
    }

    private void EnablePauseToggle()
    {
        canTogglePause = true;
        Debug.Log("[PauseMenuManager] Pause toggle enabled");
    }

    public void TogglePause()
    {
        Debug.Log($"[PauseMenuManager] TogglePause called. Current state - isPaused: {isPaused}");
        
        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        if (isPaused) return;

        Debug.Log("[PauseMenuManager] PauseGame called");
        isPaused = true;
        Time.timeScale = 0f;

        // Hide save/load UI when opening pause menu (start fresh)
        HideSaveLoadUI();

        // Use UIManager to show pause menu
        if (UIManager.Instance != null)
        {
            Debug.Log("[PauseMenuManager] Showing pause menu via UIManager");
            UIManager.Instance.ShowPauseMenu();
        }
        else if (pauseMenuPanel != null)
        {
            Debug.Log("[PauseMenuManager] Showing pause menu directly (UIManager not available)");
            pauseMenuPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("[PauseMenuManager] Cannot show pause menu - no UIManager and no pauseMenuPanel!");
        }

        // Update GameManager pause state
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gamePaused = true;
        }

        Debug.Log("Game Paused");
    }

    public void ResumeGame()
    {
        if (!isPaused) return;

        Debug.Log("[PauseMenuManager] ResumeGame called");
        isPaused = false;
        Time.timeScale = 1f;

        // Use UIManager to hide pause menu
        if (UIManager.Instance != null)
        {
            Debug.Log("[PauseMenuManager] Hiding pause menu via UIManager");
            UIManager.Instance.HidePauseMenu();
        }
        else if (pauseMenuPanel != null)
        {
            Debug.Log("[PauseMenuManager] Hiding pause menu directly");
            pauseMenuPanel.SetActive(false);
        }

        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        // Update GameManager pause state
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gamePaused = false;
        }

        Debug.Log("Game Resumed");
    }

    /// <summary>
    /// Hide all save/load UI elements at startup
    /// </summary>
    private void HideSaveLoadUI()
    {
        Debug.Log("[PauseMenuManager] Hiding save/load UI elements at startup");
        
        // Hide the main save/load panel if it exists
        if (saveLoadPanel != null)
        {
            saveLoadPanel.SetActive(false);
            Debug.Log("[PauseMenuManager] Save/load panel hidden");
        }
        
        // Hide individual save/load components as fallback
        if (saveSlotContainer != null && saveSlotContainer.gameObject != null)
        {
            saveSlotContainer.gameObject.SetActive(false);
            Debug.Log("[PauseMenuManager] Save slot container hidden");
        }
        
        if (createNewSaveButton != null && createNewSaveButton.gameObject != null)
        {
            createNewSaveButton.gameObject.SetActive(false);
        }
        
        if (deleteSaveButton != null && deleteSaveButton.gameObject != null)
        {
            deleteSaveButton.gameObject.SetActive(false);
        }
        
        if (saveStatusText != null && saveStatusText.gameObject != null)
        {
            saveStatusText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Show save/load UI elements when save/load is accessed
    /// </summary>
    private void ShowSaveLoadUI()
    {
        Debug.Log("[PauseMenuManager] Showing save/load UI elements");
        
        // Show the main save/load panel if it exists
        if (saveLoadPanel != null)
        {
            saveLoadPanel.SetActive(true);
            Debug.Log("[PauseMenuManager] Save/load panel shown");
        }
        
        // Show individual save/load components as fallback
        if (saveSlotContainer != null && saveSlotContainer.gameObject != null)
        {
            saveSlotContainer.gameObject.SetActive(true);
        }
        
        if (createNewSaveButton != null && createNewSaveButton.gameObject != null)
        {
            createNewSaveButton.gameObject.SetActive(true);
        }
        
        if (deleteSaveButton != null && deleteSaveButton.gameObject != null)
        {
            deleteSaveButton.gameObject.SetActive(true);
        }
        
        if (saveStatusText != null && saveStatusText.gameObject != null)
        {
            saveStatusText.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Return to main pause menu from save/load UI
    /// </summary>
    public void ReturnToPauseMenu()
    {
        Debug.Log("[PauseMenuManager] Returning to main pause menu from save/load");
        HideSaveLoadUI();
        // The main pause menu should already be active, so we don't need to show it again
    }

    public void SaveGame()
    {
        if (selectedSaveSlot < 0 || selectedSaveSlot >= maxSaveSlots)
        {
            UpdateSaveStatus("Please select a save slot first.");
            return;
        }

        try
        {
            GameSaveData saveData = CreateSaveData();
            SaveSlotData slotData = new SaveSlotData(selectedSaveSlot)
            {
                hasData = true,
                saveData = saveData
            };

            string saveDirectory = GetSaveDirectory();
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            string filePath = Path.Combine(saveDirectory, $"save_slot_{selectedSaveSlot}.json");
            string jsonData = JsonUtility.ToJson(slotData, true);
            File.WriteAllText(filePath, jsonData);

            UpdateSaveStatus($"Game saved to slot {selectedSaveSlot + 1}!");
            RefreshSaveSlots();
            
            Debug.Log($"Game saved successfully to slot {selectedSaveSlot}");
        }
        catch (System.Exception e)
        {
            UpdateSaveStatus("Failed to save game!");
            Debug.LogError($"Save failed: {e.Message}");
        }
    }

    public void LoadGame()
    {
        if (selectedSaveSlot < 0 || selectedSaveSlot >= maxSaveSlots)
        {
            UpdateSaveStatus("Please select a save slot first.");
            return;
        }

        try
        {
            string filePath = Path.Combine(GetSaveDirectory(), $"save_slot_{selectedSaveSlot}.json");
            
            if (!File.Exists(filePath))
            {
                UpdateSaveStatus("No save data found in this slot.");
                return;
            }

            string jsonData = File.ReadAllText(filePath);
            SaveSlotData slotData = JsonUtility.FromJson<SaveSlotData>(jsonData);

            if (slotData?.hasData == true && slotData.saveData != null)
            {
                ApplySaveData(slotData.saveData);
                UpdateSaveStatus($"Game loaded from slot {selectedSaveSlot + 1}!");
                ResumeGame();
                Debug.Log($"Game loaded successfully from slot {selectedSaveSlot}");
            }
            else
            {
                UpdateSaveStatus("Save data is corrupted or empty.");
            }
        }
        catch (System.Exception e)
        {
            UpdateSaveStatus("Failed to load game!");
            Debug.LogError($"Load failed: {e.Message}");
        }
    }

    private GameSaveData CreateSaveData()
    {
        GameSaveData saveData = new GameSaveData();
        
        if (GameManager.Instance != null)
        {
            saveData.currentTurn = GameManager.Instance.currentTurn;
            saveData.mapSize = GameManager.Instance.mapSize;
            saveData.enableMultiPlanetSystem = GameManager.Instance.enableMultiPlanetSystem;
            saveData.currentPlanetIndex = GameManager.Instance.currentPlanetIndex;
            saveData.gameInProgress = GameManager.Instance.gameInProgress;

            // Get player civilization info
            if (CivilizationManager.Instance?.playerCiv != null)
            {
                saveData.playerCivName = CivilizationManager.Instance.playerCiv.civData.civName;
                // Find the index of the player civilization in the civilizations list
                var allCivs = CivilizationManager.Instance.GetAllCivs();
                saveData.playerCivIndex = allCivs.IndexOf(CivilizationManager.Instance.playerCiv);
            }
        }

        // Get camera position/rotation if available
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            saveData.cameraPosition = mainCamera.transform.position;
            saveData.cameraRotation = mainCamera.transform.eulerAngles;
        }

        // Generate save name based on current game state
        saveData.saveName = GenerateSaveName();

        // Export improvement manager job assignments if available
        try
        {
            saveData.jobAssignments = ImprovementManager.Instance?.ExportJobAssignments();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to export job assignments: {e.Message}");
            saveData.jobAssignments = null;
        }

        return saveData;
    }

    private void ApplySaveData(GameSaveData saveData)
    {
        // Delegate full apply/load orchestration to GameManager so it can ensure
        // managers and units are initialized in the correct order before importing assignments.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.LoadGameFromSaveData(saveData);
        }
        else
        {
            // Fallback: apply simple fields now
            if (GameManager.Instance != null)
            {
                GameManager.Instance.currentTurn = saveData.currentTurn;
                GameManager.Instance.gameInProgress = saveData.gameInProgress;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.transform.position = saveData.cameraPosition;
                mainCamera.transform.eulerAngles = saveData.cameraRotation;
            }

            Debug.Log($"Applied save data (partial): Turn {saveData.currentTurn}, Player: {saveData.playerCivName}");
        }
    }

    private string GenerateSaveName()
    {
        string civName = "Unknown";
        if (CivilizationManager.Instance?.playerCiv?.civData != null)
        {
            civName = CivilizationManager.Instance.playerCiv.civData.civName;
        }

        int turn = GameManager.Instance?.currentTurn ?? 0;
        return $"{civName} - Turn {turn}";
    }

    private string GetSaveDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "Saves");
    }

    public void ShowOptions()
    {
        // Hide pause menu and show options
        if (UIManager.Instance != null)
        {
            UIManager.Instance.HidePauseMenu();
        }
        else if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }
        
        if (optionsPanel != null)
            optionsPanel.SetActive(true);
    }

    public void HideOptions()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
        
        // Show pause menu again
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowPauseMenu();
        }
        else if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
        }
    }

    public void ExitToMainMenu()
    {
        Debug.Log("Exit to Main Menu clicked");
        
        // Resume time before changing scenes
        Time.timeScale = 1f;
        
        // Stop game music
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.StopMusicImmediate();
        }
        
        // Load main menu scene
        SceneManager.LoadScene("MainMenu");
    }

    public void ExitGame()
    {
        Debug.Log("Exit Game clicked");
        
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    #region Audio Settings

    private void OnMusicVolumeChanged(float volume)
    {
        UpdateMusicVolumeText(volume);
        
        // Update music volume
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.SetVolume(volume);
        }
        
        // Save preference
        PlayerPrefs.SetFloat("GameMusicVolume", volume);
    }

    private void UpdateMusicVolumeText(float volume)
    {
        if (musicVolumeText != null)
        {
            musicVolumeText.text = Mathf.RoundToInt(volume * 100f) + "%";
        }
    }

    private void OnMusicEnabledChanged(bool enabled)
    {
        PlayerPrefs.SetInt("MusicEnabled", enabled ? 1 : 0);
        
        if (MusicManager.Instance != null)
        {
            if (enabled)
            {
                // Restore volume and play music
                float savedVolume = PlayerPrefs.GetFloat("GameMusicVolume", 0.75f);
                MusicManager.Instance.SetVolume(savedVolume);
                MusicManager.Instance.PlayMusic();
            }
            else
            {
                // Mute music
                MusicManager.Instance.SetVolume(0f);
            }
        }
    }

    #endregion

    #region Save/Load Menu Management

    private void InitializeSaveSystem()
    {
        Debug.Log("[PauseMenuManager] Initializing save system (UI remains hidden until needed)");
        // Don't refresh save slots immediately - wait until save/load is actually accessed
        // This prevents save slot UI from appearing at startup
    }

    public void ShowSaveMenu()
    {
        Debug.Log("[PauseMenuManager] ShowSaveMenu called");
        ShowSaveLoadUI();
        RefreshSaveSlots();
        UpdateSaveStatus("Select a slot to save your game.");
        // You can add specific save menu UI here if needed
    }

    public void ShowLoadMenu()
    {
        Debug.Log("[PauseMenuManager] ShowLoadMenu called");
        ShowSaveLoadUI();
        RefreshSaveSlots();
        UpdateSaveStatus("Select a slot to load your game.");
        // You can add specific load menu UI here if needed
    }

    private void RefreshSaveSlots()
    {
        Debug.Log("[PauseMenuManager] RefreshSaveSlots called");
        
        // Clear existing save slot buttons
        foreach (var button in saveSlotButtons)
        {
            if (button?.gameObject != null)
                Destroy(button.gameObject);
        }
        saveSlotButtons.Clear();

        // Create save slot buttons
        if (saveSlotContainer != null && saveSlotButtonPrefab != null)
        {
            Debug.Log($"[PauseMenuManager] Creating {maxSaveSlots} save slot buttons");
            for (int i = 0; i < maxSaveSlots; i++)
            {
                CreateSaveSlotButton(i);
            }
        }
        else
        {
            Debug.LogWarning("[PauseMenuManager] saveSlotContainer or saveSlotButtonPrefab is null - cannot create save slots");
        }
    }

    private void CreateSaveSlotButton(int slotIndex)
    {
        GameObject buttonObj = Instantiate(saveSlotButtonPrefab, saveSlotContainer);
        SaveSlotButton slotButton = buttonObj.GetComponent<SaveSlotButton>();
        
        if (slotButton == null)
        {
            slotButton = buttonObj.AddComponent<SaveSlotButton>();
        }

        slotButton.slotIndex = slotIndex;
        slotButton.pauseMenuManager = this;

        // Load save data for this slot
        SaveSlotData slotData = LoadSaveSlotData(slotIndex);
        slotButton.SetupSlot(slotData);

        saveSlotButtons.Add(slotButton);
    }

    private SaveSlotData LoadSaveSlotData(int slotIndex)
    {
        try
        {
            string filePath = Path.Combine(GetSaveDirectory(), $"save_slot_{slotIndex}.json");
            
            if (File.Exists(filePath))
            {
                string jsonData = File.ReadAllText(filePath);
                return JsonUtility.FromJson<SaveSlotData>(jsonData);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load save slot {slotIndex}: {e.Message}");
        }

        return new SaveSlotData(slotIndex);
    }

    public void SelectSaveSlot(int slotIndex)
    {
        selectedSaveSlot = slotIndex;
        
        // Update button visual states
        foreach (var button in saveSlotButtons)
        {
            button.SetSelected(button.slotIndex == slotIndex);
        }

        UpdateSaveStatus($"Selected slot {slotIndex + 1}");
    }

    public void CreateNewSave()
    {
        if (selectedSaveSlot >= 0)
        {
            SaveGame();
        }
        else
        {
            UpdateSaveStatus("Please select a slot first.");
        }
    }

    public void DeleteSelectedSave()
    {
        if (selectedSaveSlot < 0 || selectedSaveSlot >= maxSaveSlots)
        {
            UpdateSaveStatus("Please select a save slot first.");
            return;
        }

        try
        {
            string filePath = Path.Combine(GetSaveDirectory(), $"save_slot_{selectedSaveSlot}.json");
            
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                UpdateSaveStatus($"Save slot {selectedSaveSlot + 1} deleted.");
                RefreshSaveSlots();
                selectedSaveSlot = -1;
            }
            else
            {
                UpdateSaveStatus("No save data found in this slot.");
            }
        }
        catch (System.Exception e)
        {
            UpdateSaveStatus("Failed to delete save!");
            Debug.LogError($"Delete failed: {e.Message}");
        }
    }

    private void UpdateSaveStatus(string message)
    {
        if (saveStatusText != null)
        {
            saveStatusText.text = message;
        }
        Debug.Log($"Save Status: {message}");
    }

    #endregion

    #region Save Slot Button Class

    [System.Serializable]
    public class SaveSlotButton : MonoBehaviour
    {
        [Header("UI Components")]
        public Button button;
        public TextMeshProUGUI slotNameText;
        public TextMeshProUGUI saveInfoText;
        public Image backgroundImage;

        [Header("Visual States")]
        public Color normalColor = Color.white;
        public Color selectedColor = Color.yellow;
        public Color emptyColor = Color.gray;

        [HideInInspector] public int slotIndex;
        [HideInInspector] public PauseMenuManager pauseMenuManager;
        [HideInInspector] public bool hasData;

        void Awake()
        {
            if (button == null)
                button = GetComponent<Button>();
            
            if (button != null)
                button.onClick.AddListener(OnButtonClicked);
        }

        public void SetupSlot(SaveSlotData slotData)
        {
            hasData = slotData.hasData;

            if (slotNameText != null)
            {
                slotNameText.text = $"Slot {slotIndex + 1}";
            }

            if (saveInfoText != null)
            {
                if (hasData && slotData.saveData != null)
                {
                    saveInfoText.text = $"{slotData.saveData.saveName}\n{slotData.saveData.dateTime}";
                }
                else
                {
                    saveInfoText.text = "Empty Slot";
                }
            }

            UpdateVisualState(false);
        }

        public void SetSelected(bool selected)
        {
            UpdateVisualState(selected);
        }

        private void UpdateVisualState(bool selected)
        {
            if (backgroundImage != null)
            {
                if (selected)
                {
                    backgroundImage.color = selectedColor;
                }
                else if (hasData)
                {
                    backgroundImage.color = normalColor;
                }
                else
                {
                    backgroundImage.color = emptyColor;
                }
            }
        }

        private void OnButtonClicked()
        {
            if (pauseMenuManager != null)
            {
                pauseMenuManager.SelectSaveSlot(slotIndex);
            }
        }
    }

    #endregion

    #region Autosave System

    private void InitializeAutosaveSystem()
    {
        if (!enableAutosave) return;
        
        // Subscribe to turn events if TurnManager exists
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += OnTurnChanged;
        }
        
        // Subscribe to game state changes
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted += OnGameStarted;
        }
        
        Debug.Log($"Autosave system initialized. Interval: {autosaveInterval} turns, Max files: {maxAutosaveFiles}");
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= OnTurnChanged;
        }
        
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted -= OnGameStarted;
        }
    }
    
    private void OnGameStarted()
    {
        // Reset autosave tracking when a new game starts
        lastAutosaveTurn = -1;
        Debug.Log("Game started - autosave tracking reset");
    }
    
    private void OnTurnChanged(Civilization civ, int newTurn)
    {
        if (!enableAutosave) return;
        
        // Only track autosaves for the player civilization
        if (civ != CivilizationManager.Instance?.playerCiv) return;
        
        // Check if it's time for an autosave
        if (newTurn > 0 && (lastAutosaveTurn == -1 || newTurn - lastAutosaveTurn >= autosaveInterval))
        {
            PerformAutosave();
            lastAutosaveTurn = newTurn;
        }
    }
    
    public void PerformAutosave()
    {
        if (!enableAutosave) return;
        
        try
        {
            // Create autosave data
            GameSaveData saveData = CreateSaveData();
            saveData.isAutosave = true;
            saveData.saveName = GenerateAutosaveName();
            
            // Clean up old autosaves
            CleanupOldAutosaves();
            
            // Save the autosave
            string autosaveDirectory = GetAutosaveDirectory();
            if (!Directory.Exists(autosaveDirectory))
            {
                Directory.CreateDirectory(autosaveDirectory);
            }
            
            string fileName = $"autosave_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string filePath = Path.Combine(autosaveDirectory, fileName);
            
            SaveSlotData slotData = new SaveSlotData(-1) // Use -1 to indicate autosave
            {
                hasData = true,
                saveData = saveData
            };
            
            string jsonData = JsonUtility.ToJson(slotData, true);
            File.WriteAllText(filePath, jsonData);
            
            Debug.Log($"Autosave completed: {fileName}");
            
            // Show notification if enabled
            if (showAutosaveNotification)
            {
                ShowAutosaveNotification("Game autosaved");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Autosave failed: {e.Message}");
            
            if (showAutosaveNotification)
            {
                ShowAutosaveNotification("Autosave failed!");
            }
        }
    }
    
    private void CleanupOldAutosaves()
    {
        try
        {
            string autosaveDirectory = GetAutosaveDirectory();
            if (!Directory.Exists(autosaveDirectory)) return;
            
            // Get all autosave files and sort by creation time (newest first)
            var autosaveFiles = Directory.GetFiles(autosaveDirectory, "autosave_*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToArray();
            
            // Delete files beyond the max count
            for (int i = maxAutosaveFiles; i < autosaveFiles.Length; i++)
            {
                try
                {
                    autosaveFiles[i].Delete();
                    Debug.Log($"Deleted old autosave: {autosaveFiles[i].Name}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to delete old autosave {autosaveFiles[i].Name}: {e.Message}");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to cleanup old autosaves: {e.Message}");
        }
    }
    
    private string GenerateAutosaveName()
    {
        string civName = "Unknown";
        if (CivilizationManager.Instance?.playerCiv?.civData != null)
        {
            civName = CivilizationManager.Instance.playerCiv.civData.civName;
        }
        
        int turn = GameManager.Instance?.currentTurn ?? 0;
        return $"[AUTO] {civName} - Turn {turn}";
    }
    
    private string GetAutosaveDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "Saves", "Autosaves");
    }
    
    public List<GameSaveData> GetAutosaves()
    {
        var autosaves = new List<GameSaveData>();
        
        try
        {
            string autosaveDirectory = GetAutosaveDirectory();
            if (!Directory.Exists(autosaveDirectory)) return autosaves;
            
            var autosaveFiles = Directory.GetFiles(autosaveDirectory, "autosave_*.json");
            
            foreach (var filePath in autosaveFiles)
            {
                try
                {
                    string jsonData = File.ReadAllText(filePath);
                    SaveSlotData slotData = JsonUtility.FromJson<SaveSlotData>(jsonData);
                    
                    if (slotData?.hasData == true && slotData.saveData != null)
                    {
                        autosaves.Add(slotData.saveData);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to load autosave {Path.GetFileName(filePath)}: {e.Message}");
                }
            }
            
            // Sort by creation time (newest first)
            autosaves = autosaves.OrderByDescending(a => DateTime.Parse(a.dateTime)).ToList();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to get autosaves: {e.Message}");
        }
        
        return autosaves;
    }
    
    public void LoadAutosave(GameSaveData autosaveData)
    {
        if (autosaveData == null) return;
        
        try
        {
            ApplySaveData(autosaveData);
            UpdateSaveStatus($"Loaded autosave from Turn {autosaveData.currentTurn}");
            ResumeGame();
            Debug.Log($"Autosave loaded successfully: {autosaveData.saveName}");
        }
        catch (System.Exception e)
        {
            UpdateSaveStatus("Failed to load autosave!");
            Debug.LogError($"Failed to load autosave: {e.Message}");
        }
    }
    
    private void ShowAutosaveNotification(string message)
    {
        if (!showAutosaveNotification) return;
        
        // Use UIManager's notification system instead of custom UI
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowNotification(message);
        }
        else
        {
            Debug.Log($"Autosave: {message}"); // Fallback to console
        }
    }
    
    // Removed DisplayAutosaveNotification coroutine - no longer needed since UIManager handles it
    
    // Manual autosave trigger (can be called from UI button)
    public void TriggerManualAutosave()
    {
        PerformAutosave();
    }
    
    // Enable/disable autosave at runtime
    public void SetAutosaveEnabled(bool enabled)
    {
        enableAutosave = enabled;
        PlayerPrefs.SetInt("AutosaveEnabled", enabled ? 1 : 0);
        
        if (enabled)
        {
            Debug.Log("Autosave enabled");
        }
        else
        {
            Debug.Log("Autosave disabled");
        }
    }
    
    // Change autosave interval at runtime
    public void SetAutosaveInterval(int interval)
    {
        autosaveInterval = Mathf.Max(1, interval);
        PlayerPrefs.SetInt("AutosaveInterval", autosaveInterval);
        Debug.Log($"Autosave interval set to {autosaveInterval} turns");
    }

    #endregion

    // Public property to check if game is paused
    public bool IsPaused => isPaused;
}
