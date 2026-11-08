using UnityEngine;
using UnityEngine.InputSystem;
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
    [SerializeField] private MenuUiSkin menuUiSkin;

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
    private PauseMenuPresentation pausePresentation;
    private bool exitRequestPending;
    // Removed autosaveNotificationCoroutine - no longer needed with UIManager notifications

    [Serializable]
    public class GameSaveData
    {
        public int saveFormatVersion;
        public int worldGenerationSeed;
        public GameSetupDataSnapshot generationSettings;
        public List<PlanetSeedSaveData> planetSeeds;
        public string saveName;
        public string dateTime;
        public int currentTurn;
        public string playerCivName;
        public int playerCivIndex;
        public GameManager.MapSize mapSize;
        public float flatMapWidth;
        public float flatMapHeight;
        public bool enableMultiPlanetSystem;
        public int currentPlanetIndex;
        
        // Add more save data fields as needed
        public Vector3 cameraPosition;
        public Vector3 cameraRotation;
    public bool gameInProgress;
    public bool isAutosave; // Mark if this is an autosave
    public WorldSnapshotData worldSnapshot;
    // Persisted manager/job state
    public List<ImprovementManager.JobAssignmentSaveData> jobAssignments;

    // ===== UNIT STATE =====
    public List<CombatUnitSaveData> combatUnits;
    public List<WorkerUnitSaveData> workerUnits;
    public List<BandSaveData> bands;
    public List<HerdSaveData> herds;
    public List<CivilianAttachmentSaveData> civilianAttachments;
    public List<CivilizationProgressSaveData> civilizationProgress;
    public List<CitySaveData> cities;
    public List<AnimalManager.PlanetEcologyState> ecologyStates;
    // Mission/Legacy/Crisis system
    public List<CrisisManager.MissionStateSaveData> missionStates;
    public CrisisManager.CrisisSaveData crisisState;
    public List<SaveParticipantStateData> participantStates;
        
        public GameSaveData()
        {
            dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            isAutosave = false;
        }
    }

    [Serializable]
    public class PlanetSeedSaveData
    {
        public int planetIndex;
        public int seed;
    }

    [Serializable]
    public class WorldSnapshotData
    {
        public List<ImprovementManager.JobAssignmentSaveData> jobAssignments = new List<ImprovementManager.JobAssignmentSaveData>();
        public List<CombatUnitSaveData> combatUnits = new List<CombatUnitSaveData>();
        public List<WorkerUnitSaveData> workerUnits = new List<WorkerUnitSaveData>();
        public List<BandSaveData> bands = new List<BandSaveData>();
        public List<HerdSaveData> herds = new List<HerdSaveData>();
        public List<CivilianAttachmentSaveData> civilianAttachments = new List<CivilianAttachmentSaveData>();
        public List<CivilizationProgressSaveData> civilizationProgress = new List<CivilizationProgressSaveData>();
        public List<CitySaveData> cities = new List<CitySaveData>();
        public List<CrisisManager.MissionStateSaveData> missionStates = new List<CrisisManager.MissionStateSaveData>();
        public CrisisManager.CrisisSaveData crisisState;

        public bool HasState()
        {
            return (jobAssignments != null && jobAssignments.Count > 0)
                || (combatUnits != null && combatUnits.Count > 0)
                || (workerUnits != null && workerUnits.Count > 0)
                || (bands != null && bands.Count > 0)
                || (herds != null && herds.Count > 0)
                || (civilianAttachments != null && civilianAttachments.Count > 0)
                || (civilizationProgress != null && civilizationProgress.Count > 0)
                || (cities != null && cities.Count > 0)
                || (missionStates != null && missionStates.Count > 0)
                || crisisState != null;
        }
    }

    /// <summary>
    /// Serializable snapshot of a single CombatUnit for save/load.
    /// </summary>
    [Serializable]
    public class CivilizationProgressSaveData
    {
        public int civIndex;
        public List<string> researchedTechNames = new List<string>();
        public string currentTechName;
        public float currentTechProgress;
        public List<string> researchedCultureNames = new List<string>();
        public string currentCultureName;
        public float currentCultureProgress;
        public bool tradeEnabled;
        public bool governorsEnabled;
        public int governorCount;
        public int cityCapFromBonuses;
        public int pantheonCapFromBonuses;
        public float attackBonus;
        public float defenseBonus;
        public float movementBonus;
        public float foodModifier;
        public float productionModifier;
        public float goldModifier;
        public float scienceModifier;
        public float cultureModifier;
        public float faithModifier;
        public List<string> unlockedGovernorTraitNames = new List<string>();
        public List<string> cultureUnlockedPantheonNames = new List<string>();
        public List<string> cultureUnlockedBeliefNames = new List<string>();
        public List<string> customAssignedBeliefNames = new List<string>();
        // Per-civ governor state
        public List<GovernorSaveData> governors = new List<GovernorSaveData>();
        public List<GovernorCandidateSaveData> governorCandidates = new List<GovernorCandidateSaveData>();
        public int nextGovernorCandidateId = 1;
        // Herd queues owned by this civilization (per-herd, identified by planet & tile)
        public List<HerdQueueSaveData> herdQueues = new List<HerdQueueSaveData>();
        // Legacy state
        public List<string> earnedLegacyNames = new List<string>();
        public List<string> activeLegacyNames = new List<string>();
        public ElectionState electionState = new ElectionState();
        public List<UnitLoadoutSaveData> standardLoadouts = new List<UnitLoadoutSaveData>();
    }

    [Serializable]
    public class UnitLoadoutSaveData
    {
        public string archetypeId;
        public bool workerArchetype, useForNewUnits;
        public int configuredSlots;
        public string weaponId, shieldId, bodyId, headId, toolId, miscellaneousId, projectileId;
    }

    [Serializable]
    public class HerdQueueSaveData
    {
        public int planetIndex;
        public int tileIndex;
        // Herd development level (feeds governor PowerRank). Old saves default to 0 and are clamped to 1 on load.
        public int herdLevel = 1;
        public List<HerdProdEntrySaveData> queue = new List<HerdProdEntrySaveData>();
    }

    [Serializable]
    public class HerdProdEntrySaveData
    {
        public string dataName; // BuildingData asset name
        public int remainingPts;
        public int goldCost;
    }

    [Serializable] public class HerdLivestockSaveData { public Herd.HerdSpecies species; public int count; }
    [Serializable] public class HerdDiseaseSaveData { public string dataName; public int turnsRemaining; public float accumulatedPopulationLoss; }
    [Serializable] public class HerdImmunitySaveData { public string dataName; public int turnsRemaining; }

    /// <summary>Herd-owned state only. Units are references into the normal unit snapshots.</summary>
    [Serializable]
    public class HerdSaveData
    {
        public string persistentId;
        public int ownerCivilizationId = -1;
        public int planetIndex;
        public int tileIndex = -1;
        public string herdName;
        public bool isPacked;
        public int movementPoints;
        public int maxMovementPoints;
        public int foodReserve;
        public int level = 1;
        public int baseStorage;
        public int storageCapacity;
        public int baseGarrisonCapacity;
        public int governorId = -1;
        public List<HerdLivestockSaveData> livestock = new List<HerdLivestockSaveData>();
        public List<string> builtBuildingNames = new List<string>();
        public List<HerdProdEntrySaveData> productionQueue = new List<HerdProdEntrySaveData>();
        public List<string> militaryGarrisonUnitIds = new List<string>();
        public List<string> storedCivilianUnitIds = new List<string>();
        public List<HerdDiseaseSaveData> activeDiseases = new List<HerdDiseaseSaveData>();
        public List<HerdImmunitySaveData> diseaseImmunities = new List<HerdImmunitySaveData>();
    }

    [Serializable]
    public class GovernorCandidateSaveData
    {
        public int candidateId;
        public string name;
        public string portraitId;
        public Governor.Specialization specialization;
        public List<PersonalityTrait> personalityTraits = new List<PersonalityTrait>();
        public string personalReligionName;
        public string personalCultureName;
        public int createdRound;
    }

    [Serializable]
    public class GovernorSaveData
    {
        public int id;
        public string name;
        // Stable GovernorPortraitLibrary ID. Empty identifies a legacy save.
        public string portraitId;
        public Governor.Specialization specialization;
        public int level;
        public int experience;
        // Indices into the parent civilization's `cities` list for assigned cities
        public List<int> assignedCityIndices = new List<int>();
        // Herd references (planet + tile) for assigned herds
        public List<HerdRef> assignedHerdRefs = new List<HerdRef>();
        // Trait names assigned to this governor
        public List<string> traitNames = new List<string>();

        // ── Intrinsic political character state (CK-lite system) ──
        public List<PersonalityTrait> personalityTraits = new List<PersonalityTrait>();
        public float opinion = 50f;
        public List<GovernorOpinionModifierSaveData> opinionModifiers = new List<GovernorOpinionModifierSaveData>();
        // Stable asset names for personal religion/culture (null/empty = none)
        public string personalReligionName;
        public string personalCultureName;
        public float loyaltyFloor = -100f;
        public float loyaltyCeiling = 100f;
        public List<GovernorGrievanceSaveData> grievances = new List<GovernorGrievanceSaveData>();
        public bool isCouncilEligible;
        public bool isInRebellion;
        // Once-per-turn opinion tick guard, preserved so mid-turn saves cannot double-tick
        public int lastOpinionTickRound = -1;
    }

    [Serializable]
    public class GovernorOpinionModifierSaveData
    {
        public string reason;
        public float value;
        public int turnsRemaining;
    }

    [Serializable]
    public class GovernorGrievanceSaveData
    {
        public GrievanceSource source;
        public int stacks;
    }

    [Serializable]
    public class HerdRef
    {
        public int planetIndex;
        public int tileIndex;
    }

    [Serializable]
    public class CombatUnitSaveData
    {
        public string persistentId;
        public string unitDataName;   // CombatUnitData.unitName — used to look up the SO
        public int ownerCivIndex;     // index in CivilizationManager.GetAllCivs()
        public int currentTileIndex;
        public int planetIndex;
        public int currentLayer;      // TileLayer as int
        public int currentHealth;
        public int experience;
        public int level;
        public bool hasActedThisTurn;
        public SpaceLocation spaceLocation;
        public int currentSpaceMovementPoints;
        public int currentSpaceTileIndex;
        public List<int> queuedSpacePath = new List<int>();
        public int queuedSpacePathCursor;
        public int spaceFleetId;
        public string militaryFormationId;
        public int militaryFormationType;
        public string militaryFormationName;
        public int armyOrder;
        public int crisisActorTags;
        public int crisisOriginalOwnerCivIndex = -1;
        public float posX, posY, posZ;
        public List<string> storedMissileNames = new List<string>(); // missile inventory
    }

    /// <summary>
    /// Serializable snapshot of a single WorkerUnit for save/load.
    /// </summary>
    [Serializable]
    public class WorkerUnitSaveData
    {
        public string persistentId;
        public string attachedArmyFormationId;
        public string unitDataName;   // WorkerUnitData.unitName
        public int ownerCivIndex;
        public int currentTileIndex;
        public int planetIndex;
        public int currentLayer;
        public int currentHealth;
        public int experience;
        public int level;
        public int currentWorkPoints;
        public int currentMovePoints;
        public float posX, posY, posZ;
    }

    [Serializable]
    public class CitySaveData
    {
        public int ownerCivIndex = -1;
        public int originalOwnerCivIndex = -1;
        public int ownerCityListIndex = -1;
        public bool isCapital;
        public int centerTileIndex = -1;
        public int planetIndex = -1;
        public string cityName;
        public int level;
        public int foodStorage;
        public int foodGrowthRequirement;
        public float loyalty;
        public int productionPerTurn;
        public List<string> builtBuildingNames = new List<string>();
        public List<CityProductionEntrySaveData> productionQueue = new List<CityProductionEntrySaveData>();
        public List<string> storedMissileNames = new List<string>(); // missile inventory
    }

    [Serializable]
    public class CityProductionEntrySaveData
    {
        public City.ProdEntry.Type type;
        public string dataName;
        public int remainingPts;
        public int goldCost;
        public int districtTileIndex = -1;
    }

    [Serializable]
    public class SaveParticipantStateData
    {
        public string key;
        public string json;
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
        if (Keyboard.current == null || !Keyboard.current[Key.Escape].wasPressedThisFrame
            || !canTogglePause || Instance != this)
            return;

        if (ConfirmationDialogService.IsVisible)
            return;
        if (SaveLoadMenuUI.IsVisible)
        {
            SaveLoadMenuUI.CloseCurrent();
            return;
        }
        if (optionsPanel != null && optionsPanel.activeInHierarchy)
        {
            HideOptions();
            return;
        }

        TogglePause();
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
        // Initialise the legacy controls silently: listeners are already attached, and notifying here
        // used to push a saved "music off" state through SetVolume(0), erasing the saved volume.
        if (musicVolumeSlider != null)
        {
            float savedVolume = GameOptionsSettings.GameMusicVolume;
            musicVolumeSlider.SetValueWithoutNotify(savedVolume);
            UpdateMusicVolumeText(savedVolume);
        }

        if (musicEnabledToggle != null)
            musicEnabledToggle.SetIsOnWithoutNotify(GameOptionsSettings.GameMusicEnabled);
    }

    private void EnablePauseToggle()
    {
        canTogglePause = true;
}

    public void TogglePause()
    {
        if (ElectionManager.HasPendingPlayerDefeat) return;
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
isPaused = true;
        Time.timeScale = 0f;

        // Hide save/load UI when opening pause menu (start fresh)
        HideSaveLoadUI();

        // Use UIManager to show pause menu
        if (UIManager.Instance != null)
        {
UIManager.Instance.ShowPauseMenu();
        }
        else if (pauseMenuPanel != null)
        {
pauseMenuPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("[PauseMenuManager] Cannot show pause menu - no UIManager and no pauseMenuPanel!");
        }

        if (pausePresentation == null)
            pausePresentation = PauseMenuPresentation.Create(transform, this, menuUiSkin);
        pausePresentation.Show(CanSaveCurrentCampaign());

        // Update GameManager pause state
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gamePaused = true;
        }
// Inform InputManager: elevate to Modal and disable gameplay input
        if (InputManager.Instance != null)
        {
            InputManager.Instance.SetPriority(InputManager.InputPriority.Modal);
            InputManager.Instance.SetInputEnabled(false);
        }
    }

    public void ResumeGame()
    {
        if (!isPaused) return;
isPaused = false;
        SaveLoadMenuUI.CloseForTransition();
        ConfirmationDialogService.DismissActive();
        pausePresentation?.Hide();
        HideSaveLoadUI();
        Time.timeScale = 1f;

        // Use UIManager to hide pause menu
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

        // Update GameManager pause state
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gamePaused = false;
        }
// Restore InputManager to background priority and re-enable input
        if (InputManager.Instance != null)
        {
            InputManager.Instance.SetPriority(InputManager.InputPriority.Background);
            InputManager.Instance.SetInputEnabled(true);
        }
    }

    /// <summary>
    /// Hide all save/load UI elements at startup
    /// </summary>
    private void HideSaveLoadUI()
    {
// Hide the main save/load panel if it exists
        if (saveLoadPanel != null)
        {
            saveLoadPanel.SetActive(false);
}
        
        // Hide individual save/load components as fallback
        if (saveSlotContainer != null && saveSlotContainer.gameObject != null)
        {
            saveSlotContainer.gameObject.SetActive(false);
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
// Show the main save/load panel if it exists
        if (saveLoadPanel != null)
        {
            saveLoadPanel.SetActive(true);
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
        HideSaveLoadUI();
        if (isPaused)
        {
            if (pausePresentation == null)
                pausePresentation = PauseMenuPresentation.Create(transform, this, menuUiSkin);
            pausePresentation.Show(CanSaveCurrentCampaign());
        }
        // The main pause menu should already be active, so we don't need to show it again
    }

    public void SaveGame()
    {
        if (selectedSaveSlot < 0 || selectedSaveSlot >= maxSaveSlots)
        {
            UpdateSaveStatus("Please select a save slot first.");
            return;
        }

        if (!CanSaveCurrentCampaign())
        {
            UpdateSaveStatus("Saving is disabled while a tactical battle is active.");
            return;
        }

        SaveGameRepository.SaveEntry selected = SaveGameRepository.ReadManualSlot(selectedSaveSlot);
        if (!selected.IsEmpty)
        {
            ConfirmationDialogService.ShowConfirmation("OVERWRITE SAVE?",
                "This will replace the selected save. Continue?", () => WriteSelectedManualSlot(),
                confirmLabel: "OVERWRITE", cancelLabel: "CANCEL", owner: this, skin: menuUiSkin);
            return;
        }

        WriteSelectedManualSlot();
    }

    private void WriteSelectedManualSlot()
    {
        if (selectedSaveSlot < 0 || selectedSaveSlot >= maxSaveSlots || !CanSaveCurrentCampaign())
            return;
        try
        {
            GameSaveData saveData = CreateSaveData();
            SaveGameRepository.WriteManualSlot(selectedSaveSlot, saveData);

            UpdateSaveStatus($"Game saved to slot {selectedSaveSlot + 1}!");
            RefreshSaveSlots();
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
            SaveGameRepository.SaveEntry entry = SaveGameRepository.ReadManualSlot(selectedSaveSlot);
            if (entry.IsEmpty || !entry.IsValid)
            {
                UpdateSaveStatus("No save data found in this slot.");
                return;
            }
            SaveGameRepository.Load(entry);
            if (entry.CanLoad)
            {
                LoadSelectedSave(entry);
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
        if (GameManager.Instance != null)
        {
            return GameManager.Instance.BuildSaveData(GenerateSaveName());
        }

        GameSaveData saveData = new GameSaveData();
        saveData.saveName = GenerateSaveName();
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
        pausePresentation?.Hide();
        SaveLoadMenuUI.CloseForTransition();
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
        {
            optionsPanel.SetActive(true);
            FirstDraftOptionsMenu.Open(optionsPanel, HideOptions, "PAUSED");
        }
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
        if (pausePresentation == null)
            pausePresentation = PauseMenuPresentation.Create(transform, this, menuUiSkin);
        pausePresentation.Show(CanSaveCurrentCampaign());
    }

    public void ExitToMainMenu()
        {
            if (exitRequestPending) return;
            exitRequestPending = true;
            bool opened = ConfirmationDialogService.ShowConfirmation("EXIT TO MAIN MENU?",
                "Any progress since your last save may be lost.", () =>
                {
                    ClosePauseOverlays();
                    Time.timeScale = 1f;
                    if (GameManager.Instance != null)
                        GameManager.Instance.EndGame();
                    else
                    {
                        MusicManager.Instance?.StopMusicImmediate();
                        SceneManager.LoadScene("MainMenu");
                    }
                },
                onCancel: () => exitRequestPending = false,
                confirmLabel: "EXIT", cancelLabel: "CANCEL", owner: this, skin: menuUiSkin);
            if (!opened)
                exitRequestPending = false;
        }

    public void ExitGame()
    {
        if (exitRequestPending) return;
        exitRequestPending = true;
        bool opened = ConfirmationDialogService.ShowConfirmation("EXIT GAME?",
            "Any progress since your last save may be lost.", () =>
            {
                ClosePauseOverlays();
                Time.timeScale = 1f;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            },
            onCancel: () => exitRequestPending = false,
            confirmLabel: "EXIT", cancelLabel: "CANCEL", owner: this, skin: menuUiSkin);
        if (!opened)
            exitRequestPending = false;
    }

    #region Audio Settings

    private void OnMusicVolumeChanged(float volume)
    {
        UpdateMusicVolumeText(volume);
        GameOptionsSettings.SetGameMusicVolume(volume);
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
        // Shared implementation: stops/restores playback without touching the saved volume.
        GameOptionsSettings.SetGameMusicEnabled(enabled);
    }

    #endregion

    #region Save/Load Menu Management

    private void InitializeSaveSystem()
    {
// Don't refresh save slots immediately - wait until save/load is actually accessed
        // This prevents save slot UI from appearing at startup
    }

    public void ShowSaveMenu()
    {
        if (!CanSaveCurrentCampaign())
        {
            UIManager.Instance?.ShowNotification("Saving is unavailable during tactical encounters.");
            return;
        }

        pausePresentation?.Hide();
        ShowSaveLoadUI();
        SaveLoadMenuUI.Show(SaveLoadMenuUI.BrowserMode.Save, maxSaveSlots, menuUiSkin, this,
            CreateSaveData, null, ReturnToPauseMenu);
    }

    public void ShowLoadMenu()
    {
        pausePresentation?.Hide();
        ShowSaveLoadUI();
        SaveLoadMenuUI.Show(SaveLoadMenuUI.BrowserMode.Load, maxSaveSlots, menuUiSkin, this,
            null, LoadSelectedSave, ReturnToPauseMenu);
    }

    private bool CanSaveCurrentCampaign()
    {
        return GameInteractionStateService.GetOrCreate().Mode == GameInteractionMode.Campaign;
    }

    private void LoadSelectedSave(SaveGameRepository.SaveEntry entry)
    {
        if (entry == null || !entry.CanLoad)
            return;
        if (GameManager.Instance == null)
        {
            UIManager.Instance?.ShowNotification("The game manager is unavailable.");
            return;
        }
        if (!SaveGameRepository.IsRestorable(entry.Data, out string reason))
        {
            UIManager.Instance?.ShowNotification(reason);
            return;
        }

        ConfirmationDialogService.ShowConfirmation("LOAD GAME?",
            "Loading another campaign will replace your current session.", () =>
            {
                SaveLoadMenuUI.CloseForTransition();
                GameManager.Instance.BeginLoadSave(entry.Data);
            },
            confirmLabel: "LOAD", cancelLabel: "CANCEL", owner: this, skin: menuUiSkin);
    }

    private void ClosePauseOverlays()
    {
        SaveLoadMenuUI.CloseForTransition();
        ConfirmationDialogService.DismissActive();
        pausePresentation?.Hide();
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
        if (UIManager.Instance != null)
            UIManager.Instance.HidePauseMenu();
    }

    private void RefreshSaveSlots()
    {
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
            var entry = SaveGameRepository.ReadManualSlot(slotIndex);
            if (!entry.IsEmpty && entry.IsValid)
                return new SaveSlotData(slotIndex) { hasData = true, saveData = SaveGameRepository.Load(entry) };
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

        SaveGameRepository.SaveEntry entry = SaveGameRepository.ReadManualSlot(selectedSaveSlot);
        if (entry.IsEmpty)
        {
            UpdateSaveStatus("No save data found in this slot.");
            return;
        }
        ConfirmationDialogService.ShowConfirmation("DELETE SAVE?",
            "This save will be permanently deleted.", () =>
            {
                try
                {
                    SaveGameRepository.Delete(entry);
                    UpdateSaveStatus($"Save slot {selectedSaveSlot + 1} deleted.");
                    RefreshSaveSlots();
                    selectedSaveSlot = -1;
                }
                catch (System.Exception e)
                {
                    UpdateSaveStatus("Failed to delete save!");
                    Debug.LogError($"Delete failed: {e.Message}");
                }
            },
            confirmLabel: "DELETE", cancelLabel: "CANCEL", destructive: true, owner: this, skin: menuUiSkin);
    }

    private void UpdateSaveStatus(string message)
    {
        if (saveStatusText != null)
        {
            saveStatusText.text = message;
        }
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

    private bool turnEventsSubscribed;
    private bool gameStartSubscribed;

    private void InitializeAutosaveSystem()
    {
        // Preferences written by either Options menu override the serialized defaults so the gameplay
        // autosave matches what the player configured in the Main Menu.
        ApplyAutosavePreferences();
        GameOptionsSettings.AutosaveSettingsChanged -= ApplyAutosavePreferences;
        GameOptionsSettings.AutosaveSettingsChanged += ApplyAutosavePreferences;

        // Always subscribe, even when autosave starts disabled, so enabling it mid-game works.
        TrySubscribeTurnEvents();

        if (GameManager.Instance != null && !gameStartSubscribed)
        {
            GameManager.Instance.OnGameStarted += OnGameStarted;
            gameStartSubscribed = true;
        }
    }

    private void ApplyAutosavePreferences()
    {
        enableAutosave = GameOptionsSettings.AutosaveEnabled;
        autosaveInterval = GameOptionsSettings.AutosaveInterval;
        if (enableAutosave) TrySubscribeTurnEvents();
    }

    // Guarded so repeated calls (Start, game start, re-enabling autosave) never double-subscribe.
    private void TrySubscribeTurnEvents()
    {
        if (turnEventsSubscribed || TurnManager.Instance == null) return;
        TurnManager.Instance.OnTurnChanged += OnTurnChanged;
        turnEventsSubscribed = true;
    }
    
    private void OnDestroy()
    {
        GameOptionsSettings.AutosaveSettingsChanged -= ApplyAutosavePreferences;

        // Unsubscribe from events to prevent memory leaks
        if (turnEventsSubscribed && TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= OnTurnChanged;
        }
        turnEventsSubscribed = false;
        
        if (gameStartSubscribed && GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted -= OnGameStarted;
        }
        gameStartSubscribed = false;
    }
    
    private void OnGameStarted()
    {
        // Reset autosave tracking when a new game starts
        lastAutosaveTurn = -1;
        // TurnManager may not have existed when this menu started.
        TrySubscribeTurnEvents();
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
        if (GameInteractionStateService.GetOrCreate().Mode != GameInteractionMode.Campaign)
            return;
        
        try
        {
            // Create autosave data
            GameSaveData saveData = CreateSaveData();
            saveData.isAutosave = true;
            saveData.saveName = GenerateAutosaveName();
            
            string fileName = $"autosave_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json";
            SaveGameRepository.WriteAutosave(fileName, saveData);
            CleanupOldAutosaves();
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
            SaveGameRepository.CleanupAutosaves(maxAutosaveFiles);
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
        try
        {
            return SaveGameRepository.Discover(maxSaveSlots)
                .Where(entry => entry.IsAutosave && entry.IsValid)
                .Select(SaveGameRepository.Load)
                .Where(data => data != null)
                .ToList();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to get autosaves: {e.Message}");
            return new List<GameSaveData>();
        }
    }
    
    public void LoadAutosave(GameSaveData autosaveData)
    {
        if (autosaveData == null) return;

        ConfirmationDialogService.ShowConfirmation("LOAD GAME?",
            "Loading another campaign will replace your current session.", () =>
            {
                if (GameManager.Instance != null)
                    GameManager.Instance.BeginLoadSave(autosaveData);
            },
            confirmLabel: "LOAD", cancelLabel: "CANCEL", owner: this, skin: menuUiSkin);
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
// Fallback to console
        }
    }
    
    // Removed DisplayAutosaveNotification coroutine - no longer needed since UIManager handles it
    
    // Manual autosave trigger (can be called from UI button)
    public void TriggerManualAutosave()
    {
        PerformAutosave();
    }
    
    // Enable/disable autosave at runtime (shared preference; the settings event applies it here)
    public void SetAutosaveEnabled(bool enabled)
    {
        GameOptionsSettings.SetAutosaveEnabled(enabled);
    }
    
    // Change autosave interval at runtime (shared preference; the settings event applies it here)
    public void SetAutosaveInterval(int interval)
    {
        GameOptionsSettings.SetAutosaveInterval(interval);
    }

    #endregion

    // Public property to check if game is paused
    public bool IsPaused => isPaused;
}
