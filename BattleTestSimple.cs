using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// Main battle system - handles both formation-based battles and full civilization battles
/// Merged with BattleManager functionality
/// </summary>
public class BattleTestSimple : MonoBehaviour
{
    public static BattleTestSimple Instance { get; private set; }
    
    [Header("Battle Manager Settings")]
    [Tooltip("Size of the battle map in world units")]
    public float battleMapSize = 100f;
    [Tooltip("Distance between attacker and defender formation groups")]
    public float formationGroupSpacing = 20f;
    [Tooltip("Key to pause/resume battle")]
    public KeyCode pauseKey = KeyCode.Escape;
    [Tooltip("Prefab for unit selection indicator")]
    public GameObject selectionIndicatorPrefab;
    
    [Header("Battle State")]
    [Tooltip("Is a battle currently in progress?")]
    public bool battleInProgress = false;
    [Tooltip("Is the battle currently paused?")]
    public bool isPaused = false;
    
    [Header("Battle UI Integration")]
    [Tooltip("BattleUI component - will use this exclusively for UI")]
    public BattleUI battleUI;
    
    [Header("AI System")]
    [Tooltip("AI manager for formation-based AI")]
    public FormationAIManager formationAIManager;
    
    // Battle state from BattleManager
    public Civilization attacker;
    public Civilization defender;
    private List<CombatUnit> attackerUnits = new List<CombatUnit>();
    private List<CombatUnit> defenderUnits = new List<CombatUnit>();
    
    // Events
    public System.Action<BattleResult> OnBattleEnded;
    
    [Header("UI")]
    public Button testButton;
    public TextMeshProUGUI statusText;
    public GameObject uiPanel; // Panel to hide when battle starts
    public TMP_Dropdown attackerUnitDropdown;
    public TMP_Dropdown defenderUnitDropdown;
    public TextMeshProUGUI attackerLabel;
    public TextMeshProUGUI defenderLabel;
    
    // Note: Battle HUD is handled exclusively by BattleUI component
    // All HUD creation and management is done in BattleUI.cs
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    
    [Header("Camera Controls")]
    public float cameraMoveSpeed = 5f;
    public float cameraZoomSpeed = 2f;
    public float cameraRotateSpeed = 50f;
    public float minZoom = 2f;
    public float maxZoom = 20f;
    
    // Metadata-only system: lightweight unit info for dropdown (menu phase)
    private struct UnitMetadata
    {
        public string unitName;
        public int baseHealth;
        public int baseAttack;
        public int baseDefense;
        
        public UnitMetadata(string name, int health, int attack, int defense)
        {
            unitName = name;
            baseHealth = health;
            baseAttack = attack;
            baseDefense = defense;
        }
    }
    
    [Header("Unit Selection")]
    private List<UnitMetadata> unitMetadata = new List<UnitMetadata>(); // Lightweight metadata for dropdown
    private List<CombatUnitData> availableUnits = new List<CombatUnitData>(); // Full unit data (loaded only when battle starts)
    private int selectedAttackerUnitIndex = 0; // Index in metadata list
    private int selectedDefenderUnitIndex = 0; // Index in metadata list
    private CombatUnitData selectedAttackerUnit; // Resolved from metadata when battle starts
    private CombatUnitData selectedDefenderUnit; // Resolved from metadata when battle starts
    
    [Header("Civilization Selection")]
    // Data-only list of civs; we instantiate only the selected ones on Start
    private List<CivData> availableCivs = new List<CivData>();
    private CivData selectedAttackerCivData;
    private CivData selectedDefenderCivData;
    // Runtime instances created only when starting the battle
    private Civilization attackerCivInstance;
    private Civilization defenderCivInstance;
    public TMP_Dropdown attackerCivDropdown;
    public TMP_Dropdown defenderCivDropdown;
    public TextMeshProUGUI attackerCivLabel;
    public TextMeshProUGUI defenderCivLabel;
    
    [Header("Player/AI Control Selection")]
    public TMP_Dropdown attackerControlDropdown;
    public TMP_Dropdown defenderControlDropdown;
    public TextMeshProUGUI attackerControlLabel;
    public TextMeshProUGUI defenderControlLabel;
    
    // Control types: 0 = Player, 1 = AI
    private int attackerControlType = 0; // 0 = Player, 1 = AI
    private int defenderControlType = 1; // 0 = Player, 1 = AI
    
    [Header("Selection System")]
    public Material selectionBoxMaterial;
    public Color selectionColor = new Color(0, 1, 0, 0.3f);
    public Color selectedUnitColor = new Color(0, 1, 0, 0.5f);
    
    [Header("Formation Settings")]
    public int formationsPerSide = 3;
    public int soldiersPerFormation = 9;
    public float formationSpacing = 2f;
    
    [Header("Battle Map")]
    [Tooltip("Battle map generator for creating terrain")]
    public BattleMapGenerator mapGenerator;
    [Tooltip("Victory manager for handling battle outcomes")]
    public BattleVictoryManager victoryManager;
    [Tooltip("Generate a new map for each battle")]
    public bool generateNewMap = true;
    
    [Header("Grounding")]
    [Tooltip("Layers considered battlefield ground for raycast grounding")] 
    public LayerMask battlefieldLayers = ~0; // default: everything
    
    [Header("Prefab Caching (On-Demand)")]
    // Small on-demand cache keyed by normalized unit name; we DO NOT bulk load from Resources
    private Dictionary<string, GameObject> onDemandPrefabCache = new Dictionary<string, GameObject>();
    
    // Selection system variables
    private bool isDragging = false;
    private Vector3 dragStart;
    private Vector3 dragEnd;
    private GameObject selectionBox;
    private List<FormationUnit> selectedFormations = new List<FormationUnit>();
    private List<CombatUnit> selectedUnits = new List<CombatUnit>(); // Track individually selected units
    public List<FormationUnit> allFormations = new List<FormationUnit>();
    
    // Shared Canvas for all formation badges (memory optimization - one Canvas instead of many)
    private Canvas sharedFormationBadgeCanvas;
    
    // Cached test ground reference (avoid GameObject.Find)
    private GameObject cachedTestGround;
    
    // Cached component references (avoid repeated FindFirstObjectByType calls)
    private GameManager cachedGameManager;
    private BattleMapGenerator cachedMapGenerator;
    
    // Reusable temporary lists to avoid allocations
    private List<string> reusableStringList = new List<string>();
    
    // Track all coroutines started by BattleTestSimple for proper cleanup
    private List<Coroutine> trackedCoroutines = new List<Coroutine>();
    
    void Awake()
    {
        // Singleton pattern (removed DontDestroyOnLoad - this is a scene-specific component)
        // DontDestroyOnLoad was causing UI to break because BattleUI gets created in the scene
        if (Instance == null)
        {
            Instance = this;
            // Do NOT use DontDestroyOnLoad - battle test is scene-specific
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        DebugLog("BattleTestSimple started");
        
        // Set up battle camera early to prevent null reference storms
        SetupBattleCamera();
        
        // If no specific battlefield mask set, prefer the "Battlefield" layer only
        if (battlefieldLayers == ~0)
        {
            int bf = LayerMask.NameToLayer("Battlefield");
            if (bf != -1) battlefieldLayers = (1 << bf);
        }
        
        // UI elements must be manually assigned in the Inspector
        // No automatic UI creation - all UI must be set up manually in the scene
        
        // Connect button and dropdowns
        if (testButton != null)
        {
            testButton.onClick.AddListener(StartTest);
            DebugLog("Button connected successfully");
        }
        
        // MEMORY FIX: Defer heavy resource loading to prevent memory spike at startup
        // Load resources in a coroutine over multiple frames instead of all at once
        var loadResourcesCoroutine = StartCoroutine(LoadResourcesAsync());
        trackedCoroutines.Add(loadResourcesCoroutine);
        
        // Set up control dropdowns immediately (no heavy loading needed)
        SetupControlDropdowns();
        
        UpdateStatus("Loading resources...");
    }
    
    /// <summary>
    /// Load resources asynchronously over multiple frames to prevent memory spikes
    /// </summary>
    private IEnumerator LoadResourcesAsync()
    {
        UpdateStatus("Loading battle resources...");
        yield return null; // Wait one frame before starting
        
        // CRITICAL: Initialize only battle test resources (units, civs, projectiles)
        // This prevents loading tech/culture trees that cause massive memory spikes
        ResourceCache.InitializeBattleTestResources();
        yield return null; // Yield after initialization to spread memory allocation
        
        // Load available units (this can be heavy if there are many units)
        // Process in batches to avoid memory spike
        UpdateStatus("Loading units...");
        yield return StartCoroutine(LoadAvailableUnitsAsync());
        
        // Load available civilizations
        UpdateStatus("Loading civilizations...");
        LoadAvailableCivilizations();
        yield return null; // Yield after loading civilizations
        
        // Now set up dropdowns after resources are loaded
        SetupUnitDropdowns();
        SetupCivilizationDropdowns();
        
        UpdateStatus("Select units, civilizations, control types, and click Start Battle!");
    }
    
    /// <summary>
    /// Load unit metadata in batches to prevent memory spikes
    /// </summary>
    private IEnumerator LoadAvailableUnitsAsync()
    {
        // OPTION 3: Metadata-Only Loading System
        // Load only lightweight metadata (names/stats) for dropdown, NOT full ScriptableObjects
        // This prevents memory spikes in menu phase while allowing full unit selection
        
        if (unitMetadata == null)
        {
            unitMetadata = new List<UnitMetadata>();
        }
        
        unitMetadata.Clear();
        
        // Load data first (outside try-catch to allow yields)
        CombatUnitData[] allUnitData = null;
        bool loadSuccess = false;
        
        try
        {
            // Load ALL CombatUnitData ScriptableObjects temporarily to extract metadata
            // NOTE: Resources.LoadAll still loads everything at once, but we can process in batches
            allUnitData = ResourceCache.GetAllCombatUnits();
            loadSuccess = true;
        }
        catch (System.Exception e)
        {
            DebugLog($"Error loading unit data: {e.Message}");
            loadSuccess = false;
        }
        
        yield return null; // Yield after loading to spread memory allocation
        
        // Process units in batches (outside try-catch to allow yields)
        if (loadSuccess && allUnitData != null && allUnitData.Length > 0)
        {
            // Process units in batches to avoid frame spikes
            const int batchSize = 50; // Process 50 units per frame
            int processed = 0;
            
            // Extract only lightweight metadata (names and stats)
            // DO NOT store references to ScriptableObjects - this prevents memory retention
            // CRITICAL: Do NOT modify the ScriptableObjects (don't clear prefab refs) because
            // they're shared with ResourceCache and we need prefabs later for spawning!
            for (int i = 0; i < allUnitData.Length; i++)
            {
                var unitData = allUnitData[i];
                if (unitData != null)
                {
                    var metadata = new UnitMetadata(
                        unitData.unitName ?? "Unknown Unit",
                        unitData.baseHealth,
                        unitData.baseAttack,
                        unitData.baseDefense
                    );
                    unitMetadata.Add(metadata);
                }
                
                processed++;
                // Yield every batchSize units to spread processing across frames
                if (processed >= batchSize)
                {
                    processed = 0;
                    yield return null;
                }
            }
            
            // Clear the local array reference - metadata is now in our lightweight list
            // We don't modify the ScriptableObjects because they're cached in ResourceCache
            allUnitData = null; // Remove local reference (ResourceCache still has them)
            
            DebugLog($"Loaded {unitMetadata.Count} unit metadata entries (lightweight, no ScriptableObject references retained)");
            
            // Log some unit names for verification
            for (int i = 0; i < Mathf.Min(5, unitMetadata.Count); i++)
            {
                DebugLog($"  - {unitMetadata[i].unitName} (HP:{unitMetadata[i].baseHealth}, ATK:{unitMetadata[i].baseAttack})");
            }
            if (unitMetadata.Count > 5)
            {
                DebugLog($"  ... and {unitMetadata.Count - 5} more units");
            }
        }
        else
        {
            DebugLog("No CombatUnitData found in Resources/Units folder");
            CreateFallbackMetadata();
        }
        
        // Ensure we have at least one metadata entry for dropdown
        if (unitMetadata.Count == 0)
        {
            CreateFallbackMetadata();
        }
    }
    
    void Update()
    {
        // CRITICAL: Check for camera before any camera-dependent operations
        var cam = Camera.main;
        if (cam == null)
        {
            // Camera not found - try to set up battle camera
            SetupBattleCamera();
            return; // Skip this frame if still no camera
        }
        
        // Handle pause key
        if (battleInProgress && Input.GetKeyDown(pauseKey))
        {
            TogglePause();
        }
        
        if (isPaused) return; // Don't update game systems when paused
        
        // Handle camera controls (camera is guaranteed to exist at this point)
        HandleCameraMovement();
        HandleCameraZoom();
        HandleSelection();
        HandleFormationMovement();
    }
    
    void HandleSelection()
    {
        // Start drag selection
        if (Input.GetMouseButtonDown(0))
        {
            // More precise UI check - only block if clicking on interactive UI elements (buttons, dropdowns, etc.)
            // Don't block if clicking on unit labels or non-interactive UI
            bool shouldBlockUI = false;
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                // Check if we're clicking on a UI element that should block selection
                var pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                pointerData.position = Input.mousePosition;
                var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, results);
                
                // Only block if we hit an interactive UI element (Button, Toggle, Dropdown, etc.)
                // Don't block if we hit the unit's own UI label or non-interactive text
                foreach (var result in results)
                {
                    // Check if it's part of the unit's own UI (like UnitLabel) - if so, don't block
                    var unitLabel = result.gameObject.GetComponentInParent<UnitLabel>();
                    if (unitLabel != null)
                    {
                        // Allow clicking through unit labels
                        continue;
                    }
                    
                    // Check for interactive UI components that should block
                    if (result.gameObject.GetComponent<UnityEngine.UI.Button>() != null ||
                        result.gameObject.GetComponent<UnityEngine.UI.Toggle>() != null ||
                        result.gameObject.GetComponent<UnityEngine.UI.Dropdown>() != null ||
                        result.gameObject.GetComponent<TMPro.TMP_Dropdown>() != null ||
                        result.gameObject.GetComponent<UnityEngine.UI.ScrollRect>() != null ||
                        result.gameObject.GetComponent<UnityEngine.UI.Slider>() != null)
                    {
                        // This is an interactive UI element, block selection
                        shouldBlockUI = true;
                        break;
                    }
                }
            }
            
            if (shouldBlockUI)
            {
                return; // UI click, let UI handle it
            }
            
            var cam = Camera.main;
            if (cam == null) return; // Safety check
            
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            
            // Use layer mask to prioritize units - check units layer first, exclude UI layer
            int unitsLayer = LayerMask.NameToLayer("Units");
            int uiLayer = LayerMask.NameToLayer("UI");
            
            // Create layer mask that includes everything except UI layer
            LayerMask unitLayerMask = ~0; // Everything
            if (uiLayer != -1)
            {
                unitLayerMask = ~(1 << uiLayer); // Exclude UI layer
            }
            
            // If units layer exists, prioritize it
            if (unitsLayer != -1)
            {
                unitLayerMask = (1 << unitsLayer); // Only check units layer
            }
            
            // Try raycast with units layer first (excludes UI)
            RaycastHit hit = new RaycastHit();
            bool hitUnit = false;
            if (Physics.Raycast(ray, out hit, Mathf.Infinity, unitLayerMask))
            {
                hitUnit = true;
            }
            
            // Fallback to any collider (except UI) if no unit layer or no hit
            if (!hitUnit)
            {
                LayerMask fallbackMask = ~0;
                if (uiLayer != -1)
                {
                    fallbackMask = ~(1 << uiLayer); // Exclude UI layer
                }
                if (Physics.Raycast(ray, out hit, Mathf.Infinity, fallbackMask))
                {
                    hitUnit = true;
                }
            }
            
            if (hitUnit)
            {
                // Check if clicking on a unit first - find its formation
                var clickedUnit = hit.collider?.GetComponent<CombatUnit>();
                FormationUnit clickedFormation = null;
                
                if (clickedUnit != null)
                {
                    // Find the formation this unit belongs to
                    clickedFormation = GetFormationFromUnit(clickedUnit);
                }
                
                // If no formation found from unit, check if clicking on formation directly
                if (clickedFormation == null)
                {
                    clickedFormation = GetFormationAtPosition(hit.point);
                }
                
                if (clickedFormation != null)
                {
                    // Select the formation (not the individual unit)
                    if (!Input.GetKey(KeyCode.LeftControl))
                    {
                        ClearSelection();
                    }
                    SelectFormation(clickedFormation);
                }
                else
                {
                    // Start drag selection
                    isDragging = true;
                    dragStart = hit.point;
                    dragStart.y = 0.1f; // Slightly above ground
                    CreateSelectionBox();
                }
            }
            else
            {
                // Clicked empty space - clear selections if not holding control
                if (!Input.GetKey(KeyCode.LeftControl))
                {
                    ClearSelection();
                }
            }
        }
        
        // Update drag selection
        if (isDragging && Input.GetMouseButton(0))
        {
            var cam = Camera.main;
            if (cam == null) return; // Safety check
            
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                dragEnd = hit.point;
                dragEnd.y = 0.1f;
                UpdateSelectionBox();
            }
        }
        
        // End drag selection
        if (isDragging && Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            SelectFormationsInBox();
            DestroySelectionBox();
        }
    }
    
    // Double-click detection for running
    private float lastRightClickTime = 0f;
    private const float DOUBLE_CLICK_TIME = 0.3f; // Time window for double-click (in seconds)
    
    void HandleFormationMovement()
    {
        // Handle right-click to move selected formations or units
        if (Input.GetMouseButtonDown(1))
        {
            // More precise UI check - only block if clicking on interactive UI elements (buttons, dropdowns, etc.)
            // Don't block if clicking on unit labels or non-interactive UI
            bool shouldBlockUI = false;
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                // Check if we're clicking on a UI element that should block movement
                var pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                pointerData.position = Input.mousePosition;
                var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, results);
                
                // Only block if we hit an interactive UI element (Button, Toggle, Dropdown, etc.)
                // Don't block if we hit the unit's own UI label or non-interactive text
                foreach (var result in results)
                {
                    // Check if it's part of the unit's own UI (like UnitLabel) - if so, don't block
                    var unitLabel = result.gameObject.GetComponentInParent<UnitLabel>();
                    if (unitLabel != null)
                    {
                        // Allow clicking through unit labels
                        continue;
                    }
                    
                    // Check for interactive UI components that should block
                    if (result.gameObject.GetComponent<UnityEngine.UI.Button>() != null ||
                        result.gameObject.GetComponent<UnityEngine.UI.Toggle>() != null ||
                        result.gameObject.GetComponent<UnityEngine.UI.Dropdown>() != null ||
                        result.gameObject.GetComponent<TMPro.TMP_Dropdown>() != null ||
                        result.gameObject.GetComponent<UnityEngine.UI.ScrollRect>() != null ||
                        result.gameObject.GetComponent<UnityEngine.UI.Slider>() != null)
                    {
                        // This is an interactive UI element, block movement
                        shouldBlockUI = true;
                        break;
                    }
                }
            }
            
            if (shouldBlockUI)
            {
                return; // UI click, let UI handle it
            }
            
            // Only proceed if we have formations selected
            if (selectedFormations.Count == 0)
            {
                return; // Nothing selected, no action
            }
            
            var cam = Camera.main;
            if (cam == null) return; // Safety check
            
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Project destination onto battlefield ground
                var grounded = GetGroundPosition(hit.point);
                
                // Detect double-click for running
                float currentTime = Time.time;
                bool isDoubleClick = (currentTime - lastRightClickTime) < DOUBLE_CLICK_TIME;
                lastRightClickTime = currentTime;
                
                // Determine movement mode: single click = walk, double click = run
                bool isRunning = isDoubleClick;
                
                // Move all selected formations - all soldiers in each formation will move
                DebugLog($"Moving {selectedFormations.Count} selected formation(s) to {grounded} ({(isRunning ? "Running" : "Walking")})");
                foreach (var formation in selectedFormations)
                {
                    if (formation != null)
                    {
                        formation.MoveToPosition(grounded, isRunning);
                    }
                }
            }
        }
    }

    // Helper: find ground position directly below a point (uses colliders in scene)
    private Vector3 GetGroundPosition(Vector3 worldPos)
    {
        Vector3 origin = worldPos + Vector3.up * 100f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit groundHit, 1000f, battlefieldLayers))
            return groundHit.point;
        // Fallback: clamp to y=0 if nothing hit
        return new Vector3(worldPos.x, 0f, worldPos.z);
    }
    
    void HandleCameraMovement()
    {
        var cam = Camera.main;
        if (cam == null) return; // Safety check
        
        Vector3 moveDirection = Vector3.zero;
        
        // WASD movement (relative to camera direction)
        if (Input.GetKey(KeyCode.W)) moveDirection += cam.transform.forward;
        if (Input.GetKey(KeyCode.S)) moveDirection -= cam.transform.forward;
        if (Input.GetKey(KeyCode.A)) moveDirection -= cam.transform.right;
        if (Input.GetKey(KeyCode.D)) moveDirection += cam.transform.right;
        
        // Apply movement
        if (moveDirection != Vector3.zero)
        {
            // Keep Y movement flat (no flying up/down)
            moveDirection.y = 0;
            moveDirection.Normalize();
            cam.transform.Translate(moveDirection * cameraMoveSpeed * Time.deltaTime, Space.World);
        }
        
        // Q/E for left/right rotation (camera rotates in place)
        if (Input.GetKey(KeyCode.Q))
        {
            cam.transform.Rotate(0, -cameraRotateSpeed * Time.deltaTime, 0, Space.World);
        }
        if (Input.GetKey(KeyCode.E))
        {
            cam.transform.Rotate(0, cameraRotateSpeed * Time.deltaTime, 0, Space.World);
        }
        
        // X/C for up/down rotation (camera tilts up/down)
        if (Input.GetKey(KeyCode.X))
        {
            cam.transform.Rotate(-cameraRotateSpeed * Time.deltaTime, 0, 0, Space.Self);
        }
        if (Input.GetKey(KeyCode.C))
        {
            cam.transform.Rotate(cameraRotateSpeed * Time.deltaTime, 0, 0, Space.Self);
        }
    }
    
    void HandleCameraZoom()
    {
        var cam = Camera.main;
        if (cam == null) return; // Safety check
        
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            // Simple forward/backward zoom
            Vector3 zoomDirection = cam.transform.forward;
            cam.transform.Translate(zoomDirection * scroll * cameraZoomSpeed, Space.World);
        }
    }
    
    void LoadAvailableUnits()
    {
        // OPTION 3: Metadata-Only Loading System
        // Load only lightweight metadata (names/stats) for dropdown, NOT full ScriptableObjects
        // This prevents memory spikes in menu phase while allowing full unit selection
        
        if (unitMetadata == null)
        {
            unitMetadata = new List<UnitMetadata>();
        }
        
        unitMetadata.Clear();
        
        try
        {
            // Load ALL CombatUnitData ScriptableObjects temporarily to extract metadata
            var allUnitData = ResourceCache.GetAllCombatUnits();
            
            if (allUnitData != null && allUnitData.Length > 0)
            {
                // Extract only lightweight metadata (names and stats)
                // DO NOT store references to ScriptableObjects - this prevents memory retention
                // CRITICAL: Do NOT modify the ScriptableObjects (don't clear prefab refs) because
                // they're shared with ResourceCache and we need prefabs later for spawning!
                foreach (var unitData in allUnitData)
                {
                    if (unitData != null)
                    {
                        var metadata = new UnitMetadata(
                            unitData.unitName ?? "Unknown Unit",
                            unitData.baseHealth,
                            unitData.baseAttack,
                            unitData.baseDefense
                        );
                        unitMetadata.Add(metadata);
                    }
                }
                
                // Clear the local array reference - metadata is now in our lightweight list
                // We don't modify the ScriptableObjects because they're cached in ResourceCache
                allUnitData = null; // Remove local reference (ResourceCache still has them)
                
                DebugLog($"Loaded {unitMetadata.Count} unit metadata entries (lightweight, no ScriptableObject references retained)");
                
                // Log some unit names for verification
                for (int i = 0; i < Mathf.Min(5, unitMetadata.Count); i++)
                {
                    DebugLog($"  - {unitMetadata[i].unitName} (HP:{unitMetadata[i].baseHealth}, ATK:{unitMetadata[i].baseAttack})");
                }
                if (unitMetadata.Count > 5)
                {
                    DebugLog($"  ... and {unitMetadata.Count - 5} more units");
                }
                
                // NOTE: We do NOT unload assets here because:
                // 1. ResourceCache still holds references to the ScriptableObjects
                // 2. We need prefab references for spawning units later
                // 3. Prefabs will be unloaded after battle ends in CleanupPreviousTest()
                DebugLog("Extracted metadata (prefabs remain loaded for spawning)");
            }
            else
            {
                DebugLog("No CombatUnitData found in Resources/Units folder");
                CreateFallbackMetadata();
            }
        }
        catch (System.Exception e)
        {
            DebugLog($"Error loading unit metadata: {e.Message}");
            CreateFallbackMetadata();
        }
        
        // Ensure we have at least one metadata entry for dropdown
        if (unitMetadata.Count == 0)
        {
            CreateFallbackMetadata();
        }
    }
    
    void CreateFallbackMetadata()
    {
        // Create minimal fallback metadata if none found
        unitMetadata.Add(new UnitMetadata("Fallback Unit", 10, 5, 3));
        DebugLog("Created fallback unit metadata");
    }
    
    void LoadAvailableCivilizations()
    {
        availableCivs.Clear();
        try
        {
            // Data-only load from Resources; no GameObjects are instantiated here
            var allCivData = ResourceCache.GetAllCivDatas();
            if (allCivData != null && allCivData.Length > 0)
            {
                foreach (var civData in allCivData)
                {
                    if (civData != null) availableCivs.Add(civData);
                }
                DebugLog($"Loaded {availableCivs.Count} civilizations (data-only)");
            }

            if (availableCivs.Count == 0)
            {
                DebugLog("No CivData found in Resources/Civilizations, creating default test CivData");
                var attackerCivData = ScriptableObject.CreateInstance<CivData>();
                attackerCivData.civName = "Test Attacker";
                var defenderCivData = ScriptableObject.CreateInstance<CivData>();
                defenderCivData.civName = "Test Defender";
                availableCivs.Add(attackerCivData);
                availableCivs.Add(defenderCivData);
            }
        }
        catch (System.Exception e)
        {
            DebugLog($"Error loading civilizations: {e.Message}");
            var fallback = ScriptableObject.CreateInstance<CivData>();
            fallback.civName = "Fallback Civ";
            availableCivs.Add(fallback);
        }
    }
    
    void CreateDefaultCivilizations()
    {
        // Keep this method for backward compatibility, but now it only creates CivData objects
        var attackerCivData = ScriptableObject.CreateInstance<CivData>();
        attackerCivData.civName = "Test Attacker";
        var defenderCivData = ScriptableObject.CreateInstance<CivData>();
        defenderCivData.civName = "Test Defender";
        availableCivs.Add(attackerCivData);
        availableCivs.Add(defenderCivData);
        DebugLog($"Created {availableCivs.Count} default CivData entries");
    }
    
    /// <summary>
    /// MEMORY OPTIMIZED: Load ONLY the 2 selected units instead of all units.
    /// This saves massive amounts of memory by not keeping all ScriptableObjects in memory.
    /// </summary>
    void LoadSelectedUnitsOnly()
    {
        try
        {
            // Clear the list first
            availableUnits.Clear();
            selectedAttackerUnit = null;
            selectedDefenderUnit = null;
            
            // Get selected unit names from metadata
            string attackerUnitName = null;
            string defenderUnitName = null;
            
            if (selectedAttackerUnitIndex >= 0 && selectedAttackerUnitIndex < unitMetadata.Count)
            {
                attackerUnitName = unitMetadata[selectedAttackerUnitIndex].unitName;
            }
            
            if (selectedDefenderUnitIndex >= 0 && selectedDefenderUnitIndex < unitMetadata.Count)
            {
                defenderUnitName = unitMetadata[selectedDefenderUnitIndex].unitName;
            }
            
            // Get all units temporarily to find the selected ones
            var allUnitData = ResourceCache.GetAllCombatUnits();
            
            if (allUnitData != null && allUnitData.Length > 0)
            {
                // Find and add ONLY the 2 selected units
                foreach (var unitData in allUnitData)
                {
                    if (unitData == null) continue;
                    
                    // Check if this is the attacker unit
                    if (attackerUnitName != null && unitData.unitName == attackerUnitName)
                    {
                        availableUnits.Add(unitData);
                        selectedAttackerUnit = unitData;
                        DebugLog($"Found attacker unit: {unitData.unitName}");
                    }
                    // Check if this is the defender unit (and it's different from attacker)
                    else if (defenderUnitName != null && unitData.unitName == defenderUnitName && unitData != selectedAttackerUnit)
                    {
                        availableUnits.Add(unitData);
                        selectedDefenderUnit = unitData;
                        DebugLog($"Found defender unit: {unitData.unitName}");
                    }
                    
                    // Early exit if we found both units
                    if (selectedAttackerUnit != null && selectedDefenderUnit != null)
                    {
                        break;
                    }
                }
                
                // Clear the temporary array reference immediately to free memory
                allUnitData = null;
                
                // If we didn't find both units, use minimal fallback (MEMORY OPTIMIZED: no prefab loading)
                if (selectedAttackerUnit == null)
                {
                    DebugLog($"Warning: Could not find attacker unit '{attackerUnitName}', using minimal fallback");
                    CreateFallbackUnits();
                    return; // Fallback creates both units, so we're done
                }
                
                if (selectedDefenderUnit == null)
                {
                    DebugLog($"Warning: Could not find defender unit '{defenderUnitName}', using attacker unit for both sides");
                    // Use attacker unit for both sides (better than loading all units)
                    selectedDefenderUnit = selectedAttackerUnit;
                    availableUnits.Add(selectedDefenderUnit);
                }
                
                DebugLog($"Loaded {availableUnits.Count} selected units (instead of all units - memory optimized)");
            }
            else
            {
                DebugLog("No CombatUnitData found in Resources/Units folder");
                CreateFallbackUnits();
            }
        }
        catch (System.Exception e)
        {
            DebugLog($"Error loading selected units: {e.Message}");
            CreateFallbackUnits();
        }
    }
    
    void CreateFallbackUnits()
    {
        // Create minimal fallback units if none found
        var fallbackData = ScriptableObject.CreateInstance<CombatUnitData>();
        fallbackData.unitName = "Fallback Unit";
        fallbackData.baseAttack = 5;
        fallbackData.baseHealth = 10;
        fallbackData.baseDefense = 3;
        availableUnits.Add(fallbackData);
        selectedAttackerUnit = fallbackData;
        selectedDefenderUnit = fallbackData;
        DebugLog("Created fallback unit data");
    }
    
    void SetupUnitDropdowns()
    {
        if (attackerUnitDropdown == null || defenderUnitDropdown == null) return;
        
        // Clear existing options
        attackerUnitDropdown.ClearOptions();
        defenderUnitDropdown.ClearOptions();
        
        // Add unit options from metadata (lightweight, no ScriptableObject references)
        var options = new List<string>();
        foreach (var metadata in unitMetadata)
        {
            options.Add($"{metadata.unitName} (HP:{metadata.baseHealth}, ATK:{metadata.baseAttack})");
        }
        
        // If no units found, add fallback
        if (options.Count == 0)
        {
            options.Add("Default Unit (HP:10, ATK:2)");
        }
        
        attackerUnitDropdown.AddOptions(options);
        defenderUnitDropdown.AddOptions(options);
        
        // Set default selections (store indices, not references)
        selectedAttackerUnitIndex = 0;
        selectedDefenderUnitIndex = 0;
        
        // Add listeners
        attackerUnitDropdown.onValueChanged.AddListener(OnAttackerUnitChanged);
        defenderUnitDropdown.onValueChanged.AddListener(OnDefenderUnitChanged);
    }
    
    void SetupCivilizationDropdowns()
    {
        DebugLog($"Setting up civilization dropdowns. Available civs: {availableCivs.Count}");
        
        if (attackerCivDropdown == null || defenderCivDropdown == null) 
        {
            DebugLog("ERROR: Civilization dropdowns are null!");
            return;
        }
        
        // Clear existing options
        attackerCivDropdown.ClearOptions();
        defenderCivDropdown.ClearOptions();
        
        // Add civilization options - reuse temporary list to avoid allocation
        reusableStringList.Clear();
        foreach (var civ in availableCivs)
        {
            string civName = civ != null ? civ.civName : "Unknown Civilization";
            reusableStringList.Add(civName);
        }
        
        // If no civilizations found, add fallback
        if (reusableStringList.Count == 0)
        {
            reusableStringList.Add("Default Attacker");
            reusableStringList.Add("Default Defender");
        }
        
        attackerCivDropdown.AddOptions(reusableStringList);
        defenderCivDropdown.AddOptions(reusableStringList);
        
        // Set default selections (data-only)
        selectedAttackerCivData = availableCivs.Count > 0 ? availableCivs[0] : null;
        selectedDefenderCivData = availableCivs.Count > 1 ? availableCivs[1] : availableCivs.Count > 0 ? availableCivs[0] : null;
        
        // If no civilizations available, create fallback
        if (availableCivs.Count == 0)
        {
            DebugLog("No civilizations available, creating fallback");
            CreateDefaultCivilizations();
        }
        
        // Add listeners
        attackerCivDropdown.onValueChanged.AddListener(OnAttackerCivChanged);
        defenderCivDropdown.onValueChanged.AddListener(OnDefenderCivChanged);
    }
    
    void OnAttackerUnitChanged(int index)
    {
        if (index >= 0 && index < unitMetadata.Count)
        {
            selectedAttackerUnitIndex = index;
            DebugLog($"Selected attacker unit: {unitMetadata[index].unitName} (index: {index})");
        }
    }
    
    void OnDefenderUnitChanged(int index)
    {
        if (index >= 0 && index < unitMetadata.Count)
        {
            selectedDefenderUnitIndex = index;
            DebugLog($"Selected defender unit: {unitMetadata[index].unitName} (index: {index})");
        }
    }
    
    void OnAttackerCivChanged(int index)
    {
        if (index >= 0 && index < availableCivs.Count)
        {
            selectedAttackerCivData = availableCivs[index];
            string civName = selectedAttackerCivData != null ? selectedAttackerCivData.civName : "Unknown";
            DebugLog($"Selected attacker civilization: {civName}");
        }
    }
    
    void OnDefenderCivChanged(int index)
    {
        if (index >= 0 && index < availableCivs.Count)
        {
            selectedDefenderCivData = availableCivs[index];
            string civName = selectedDefenderCivData != null ? selectedDefenderCivData.civName : "Unknown";
            DebugLog($"Selected defender civilization: {civName}");
        }
    }
    
    void SetupControlDropdowns()
    {
        // Setup attacker control dropdown - reuse temporary list to avoid allocation
        if (attackerControlDropdown != null)
        {
            attackerControlDropdown.ClearOptions();
            reusableStringList.Clear();
            reusableStringList.Add("Player");
            reusableStringList.Add("AI");
            attackerControlDropdown.AddOptions(reusableStringList);
            attackerControlDropdown.value = attackerControlType;
            attackerControlDropdown.onValueChanged.AddListener(OnAttackerControlChanged);
            
            if (attackerControlLabel != null)
            {
                attackerControlLabel.text = "Attacker Control:";
            }
        }
        
        // Setup defender control dropdown - reuse temporary list to avoid allocation
        if (defenderControlDropdown != null)
        {
            defenderControlDropdown.ClearOptions();
            reusableStringList.Clear();
            reusableStringList.Add("Player");
            reusableStringList.Add("AI");
            defenderControlDropdown.AddOptions(reusableStringList);
            defenderControlDropdown.value = defenderControlType;
            defenderControlDropdown.onValueChanged.AddListener(OnDefenderControlChanged);
            
            if (defenderControlLabel != null)
            {
                defenderControlLabel.text = "Defender Control:";
            }
        }
    }
    
    void OnAttackerControlChanged(int index)
    {
        attackerControlType = index; // 0 = Player, 1 = AI
        string controlType = index == 0 ? "Player" : "AI";
        DebugLog($"Attacker control set to: {controlType}");
    }
    
    void OnDefenderControlChanged(int index)
    {
        defenderControlType = index; // 0 = Player, 1 = AI
        string controlType = index == 0 ? "Player" : "AI";
        DebugLog($"Defender control set to: {controlType}");
    }
    
    public void StartTest()
    {
        // Debug.Log removed for performance
        UpdateStatus("Starting test...");
        
        // MEMORY OPTIMIZED: Load ONLY the 2 selected units (not all units!)
        // This saves massive amounts of memory by not loading ScriptableObjects for unused units
        LoadSelectedUnitsOnly();
        
        // CRITICAL: Load prefabs ONLY for the selected units (not all units!)
        // This saves massive amounts of memory by not loading prefabs for unused units
        if (selectedAttackerUnit != null)
        {
            ResourceCache.LoadUnitPrefab(selectedAttackerUnit);
            DebugLog($"Loaded prefab for attacker unit: {selectedAttackerUnit.unitName}");
        }
        if (selectedDefenderUnit != null)
        {
            ResourceCache.LoadUnitPrefab(selectedDefenderUnit);
            DebugLog($"Loaded prefab for defender unit: {selectedDefenderUnit.unitName}");
        }
        
        try
        {
            // Check for GameManager first (use cached reference)
            if (cachedGameManager == null)
            {
                cachedGameManager = FindFirstObjectByType<GameManager>();
            }
            if (cachedGameManager != null)
            {
                cachedGameManager.StartBattleTest();
                UpdateStatus("Battle started via GameManager!");
                return;
            }
            
            // Fallback: create simple test
            CreateSimpleTest();
        }
        catch (System.Exception e)
        {
            UpdateStatus($"Error: {e.Message}");
            Debug.LogError($"BattleTestSimple error: {e}");
        }
    }
    
    void CreateSimpleTest()
    {
        DebugLog("Creating battle with formations...");
        
        // Set battle in progress
        battleInProgress = true;
        isPaused = false;
        
        // Clean up any existing test objects first
        CleanupPreviousTest();
        
        // Clear existing formations
        allFormations.Clear();
        selectedFormations.Clear();
        
        // Hide UI panel
        if (uiPanel != null)
        {
            uiPanel.SetActive(false);
        }
        
        // Generate battle map if enabled
        if (generateNewMap && mapGenerator != null)
        {
            DebugLog("Generating new battle map...");
            mapGenerator.GenerateBattleMap(50f, formationsPerSide * soldiersPerFormation, formationsPerSide * soldiersPerFormation);
        }
        
        // Create ground (fallback if no map generator)
        if (mapGenerator == null)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(20, 1, 20);
            ground.name = "TestGround";
            ground.transform.position = Vector3.zero;
            cachedTestGround = ground; // Cache reference to avoid GameObject.Find
        }
        
        // Instantiate only the two selected civilizations now (data-only until here)
        attackerCivInstance = InstantiateSelectedCiv(selectedAttackerCivData, "AttackerCiv_Instance");
        defenderCivInstance = InstantiateSelectedCiv(selectedDefenderCivData, "DefenderCiv_Instance");

        // Create attacker formations
        List<Vector3> attackerSpawns = mapGenerator != null ? mapGenerator.GetAttackerSpawnPoints() : GetDefaultAttackerSpawns();
        for (int i = 0; i < formationsPerSide && i < attackerSpawns.Count; i++)
        {
            CreateFormation($"AttackerFormation{i + 1}", attackerSpawns[i], Color.red, true, attackerControlType == 0);
        }
        
        // Create defender formations
        List<Vector3> defenderSpawns = mapGenerator != null ? mapGenerator.GetDefenderSpawnPoints() : GetDefaultDefenderSpawns();
        for (int i = 0; i < formationsPerSide && i < defenderSpawns.Count; i++)
        {
            CreateFormation($"DefenderFormation{i + 1}", defenderSpawns[i], Color.blue, false, defenderControlType == 0);
        }
        
        // Initialize victory manager BEFORE spawning based on menu selections
        // This way we know how many units will be in the battle before we start
        if (victoryManager != null)
        {
            InitializeVictoryManagerFromMenu();
        }
        
        // Also initialize after spawning as a fallback (in case units aren't properly initialized)
            var victoryCoroutine = StartCoroutine(InitializeVictoryManagerDelayed());
            trackedCoroutines.Add(victoryCoroutine);
        
        // Build simple battle HUD along the top with formation info buttons
        CreateBattleHUD();
        
        // Create shared Canvas for formation badges (memory optimization)
        CreateSharedFormationBadgeCanvas();
        
        // Initialize AI system
        InitializeFormationAI();
        
        // Update UI after formations are fully created (delayed to ensure all formations are ready)
        var uiUpdateCoroutine = StartCoroutine(UpdateBattleUIDelayed());
        trackedCoroutines.Add(uiUpdateCoroutine);
    }
    
    /// <summary>
    /// Update Battle UI after formations are fully created (delayed to ensure all formations are ready)
    /// </summary>
    System.Collections.IEnumerator UpdateBattleUIDelayed()
    {
        // Wait for formations to be fully created and soldiers initialized
        yield return new WaitForSeconds(0.5f);
        
        if (battleUI != null)
        {
            Debug.Log($"[BattleTestSimple] Updating BattleUI with {allFormations.Count} formations");
            battleUI.UpdateFormationsList(allFormations);
        }
        else
        {
            Debug.LogError("[BattleTestSimple] battleUI is null in UpdateBattleUIDelayed!");
        }
    }
    
    /// <summary>
    /// Initialize victory manager from menu selections BEFORE spawning units
    /// This counts units based on formationsPerSide and soldiersPerFormation
    /// The victory manager should track expected counts and update as units spawn
    /// </summary>
    void InitializeVictoryManagerFromMenu()
    {
        if (victoryManager == null) return;
        
        // Calculate expected unit counts from menu selections
        int expectedAttackerUnits = formationsPerSide * soldiersPerFormation;
        int expectedDefenderUnits = formationsPerSide * soldiersPerFormation;
        
        DebugLog($"Initializing victory manager from menu selections: {expectedAttackerUnits} expected attackers, {expectedDefenderUnits} expected defenders");
        
        // Initialize victory manager with expected counts
        victoryManager.InitializeWithExpectedCounts(expectedAttackerUnits, expectedDefenderUnits);
    }
    
    /// <summary>
    /// Initialize victory manager after units are fully created
    /// Wait until all formations have soldiers with CombatUnit components initialized
    /// </summary>
    System.Collections.IEnumerator InitializeVictoryManagerDelayed()
    {
        if (victoryManager == null) yield break;
        
        // Calculate expected total soldiers (formations per side * soldiers per formation * 2 sides)
        int expectedTotalSoldiers = formationsPerSide * soldiersPerFormation * 2;
        float maxWaitTime = 10f; // Maximum wait time in seconds
        float elapsedTime = 0f;
        
        // Wait until all formations have been created and soldiers initialized
        while (elapsedTime < maxWaitTime)
        {
            // Check if we have the expected number of formations
            if (allFormations.Count >= formationsPerSide * 2)
            {
                // Count total soldiers with CombatUnit components
                int totalSoldiersWithCombatUnit = 0;
                foreach (var formation in allFormations)
                {
                    if (formation == null || formation.soldiers == null) continue;
                    foreach (var soldier in formation.soldiers)
                    {
                        if (soldier != null && soldier.GetComponent<CombatUnit>() != null)
                        {
                            totalSoldiersWithCombatUnit++;
                        }
                    }
                }
                
                // If we have all expected soldiers, proceed
                if (totalSoldiersWithCombatUnit >= expectedTotalSoldiers)
                {
                    DebugLog($"All {totalSoldiersWithCombatUnit} soldiers initialized, initializing victory manager");
                    break;
                }
            }
            
            // Wait a frame and check again
            yield return null;
            elapsedTime += Time.deltaTime;
        }
        
        // Collect all units now that formations are ready
        var allUnits = new List<CombatUnit>();
        foreach (var formation in allFormations)
        {
            if (formation == null) continue;
            foreach (var soldier in formation.soldiers)
            {
                if (soldier == null) continue;
                var combatUnit = soldier.GetComponent<CombatUnit>();
                if (combatUnit != null)
                {
                    allUnits.Add(combatUnit);
                }
            }
        }
        
        // Count all units that exist and have the correct isAttacker flag set
        // Note: Even if initialization failed, units are created and added to formations
        // We check both data and isAttacker flag to ensure they're properly configured
        // Reuse reusableStringList to avoid allocations - but we need lists here, so create minimal temp lists
        var attackers = new List<CombatUnit>(allUnits.Count / 2);
        var defenders = new List<CombatUnit>(allUnits.Count / 2);
        foreach (var u in allUnits)
        {
            if (u != null)
            {
                if (u.isAttacker) attackers.Add(u);
                else defenders.Add(u);
            }
        }
        
        // Debug: Log unit details to diagnose why units might not be counted
        DebugLog($"Found {allUnits.Count} total units: {attackers.Count} attackers, {defenders.Count} defenders");
        
        // Log sample units from both sides (manual loop to avoid LINQ)
        int logCount = 0;
        foreach (var u in allUnits)
        {
            if (logCount >= 10) break; // Limit to 10 units
            if (u != null)
            {
                DebugLog($"  Unit: {u.name}, isAttacker={u.isAttacker}, hasData={u.data != null}, health={u.currentHealth}, parent={u.transform.parent?.name ?? "null"}");
                logCount++;
            }
        }
        
        // If no units found, log all formations to debug
        if (allUnits.Count == 0)
        {
            DebugLog($"WARNING: No units found! Checking formations...");
            foreach (var f in allFormations)
            {
                if (f == null) continue;
                DebugLog($"  Formation: {f.formationName}, soldiers count: {f.soldiers?.Count ?? 0}, isAttacker: {f.isAttacker}");
                if (f.soldiers != null)
                {
                    int soldierLogCount = 0;
                    foreach (var s in f.soldiers)
                    {
                        if (soldierLogCount >= 3) break; // Limit to 3 soldiers
                        if (s != null)
                        {
                            var cu = s.GetComponent<CombatUnit>();
                            DebugLog($"    Soldier: {s.name}, hasCombatUnit={cu != null}, parent={s.transform.parent?.name ?? "null"}");
                            soldierLogCount++;
                        }
                    }
                }
            }
        }
        
        if (attackers.Count > 0 && defenders.Count > 0)
        {
            victoryManager.InitializeBattle(attackers, defenders);
            DebugLog($"Victory manager initialized with {attackers.Count} attackers vs {defenders.Count} defenders");
        }
        else
        {
            DebugLog($"Warning: Victory manager initialization skipped - insufficient units (attackers: {attackers.Count}, defenders: {defenders.Count})");
        }
    }
    
    void OnDestroy()
    {
        // CRITICAL: Remove all event listeners to prevent memory leaks
        RemoveAllEventListeners();
        
        // Stop all tracked coroutines to prevent errors after destruction
        StopAllTrackedCoroutines();
        
        // Clean up when component is destroyed
        CleanupPreviousTest();
    }
    
    /// <summary>
    /// Remove all event listeners to prevent memory leaks
    /// </summary>
    void RemoveAllEventListeners()
    {
        // Remove button listeners
        if (testButton != null)
        {
            testButton.onClick.RemoveListener(StartTest);
        }
        
        // Remove unit dropdown listeners
        if (attackerUnitDropdown != null)
        {
            attackerUnitDropdown.onValueChanged.RemoveListener(OnAttackerUnitChanged);
        }
        if (defenderUnitDropdown != null)
        {
            defenderUnitDropdown.onValueChanged.RemoveListener(OnDefenderUnitChanged);
        }
        
        // Remove civilization dropdown listeners
        if (attackerCivDropdown != null)
        {
            attackerCivDropdown.onValueChanged.RemoveListener(OnAttackerCivChanged);
        }
        if (defenderCivDropdown != null)
        {
            defenderCivDropdown.onValueChanged.RemoveListener(OnDefenderCivChanged);
        }
        
        // Remove control dropdown listeners
        if (attackerControlDropdown != null)
        {
            attackerControlDropdown.onValueChanged.RemoveListener(OnAttackerControlChanged);
        }
        if (defenderControlDropdown != null)
        {
            defenderControlDropdown.onValueChanged.RemoveListener(OnDefenderControlChanged);
        }
        
        // Clear action delegates
        OnBattleEnded = null;
    }
    
    /// <summary>
    /// Stop all tracked coroutines to prevent errors after destruction
    /// </summary>
    void StopAllTrackedCoroutines()
    {
        foreach (var coroutine in trackedCoroutines)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        trackedCoroutines.Clear();
    }
    
    void CleanupPreviousTest()
    {
        // Destroy all existing formations
        foreach (var formation in allFormations)
        {
            if (formation != null)
            {
                DestroyImmediate(formation.gameObject);
            }
        }
        
        // Destroy test ground (use cached reference)
        if (cachedTestGround != null)
        {
            DestroyImmediate(cachedTestGround);
            cachedTestGround = null;
        }
        else
        {
            // Fallback to Find if cache is null (shouldn't happen, but safety check)
            var ground = GameObject.Find("TestGround");
            if (ground != null)
            {
                DestroyImmediate(ground);
            }
        }
        
        // Destroy runtime civ instances if they exist
        if (attackerCivInstance != null)
        {
            DestroyImmediate(attackerCivInstance.gameObject);
            attackerCivInstance = null;
        }
        if (defenderCivInstance != null)
        {
            DestroyImmediate(defenderCivInstance.gameObject);
            defenderCivInstance = null;
        }
        
        // Clear availableUnits list to free memory (no longer needed after battle)
        availableUnits.Clear();
        
        // Unload prefabs from unit data to free memory (clears cached prefabs)
        UnloadUnitPrefabs();
        
        // Clear prefab cache to free memory
        ClearPrefabCache();
        
        // CRITICAL: Clear ResourceCache to release ScriptableObject references
        // This prevents static arrays from holding references permanently
        ResourceCache.Clear();
        
        // Force aggressive memory cleanup
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        
        // Destroy shared formation badge Canvas
        if (sharedFormationBadgeCanvas != null)
        {
            DestroyImmediate(sharedFormationBadgeCanvas.gameObject);
            sharedFormationBadgeCanvas = null;
        }
        
        // Force another cleanup after unloading
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        
        DebugLog("Cleaned up previous test objects and unloaded prefabs");
    }
    
    void CreateFormation(string formationName, Vector3 position, Color teamColor, bool isAttacker, bool isPlayerControlled = true)
    {
        // Create formation GameObject (ground immediately)
        GameObject formationGO = new GameObject(formationName);
        formationGO.transform.position = GetGroundPosition(position);
        
        // Add FormationUnit component
        FormationUnit formation = formationGO.AddComponent<FormationUnit>();
        formation.formationName = formationName;
        formation.isAttacker = isAttacker;
        formation.isPlayerControlled = isPlayerControlled;
        formation.teamColor = teamColor;
        formation.formationCenter = position;
        // Health/attack will be calculated after soldiers are created based on actual formation size from unit data
        // Temporary values - will be updated in CreateSoldiersInFormation
        formation.totalHealth = 0;
        formation.totalAttack = 0;
        formation.currentHealth = 0;
        
        // Add to formations list FIRST - this allows GetFormationFromUnit to work during soldier creation
        allFormations.Add(formation);
        
        // Create soldiers in formation with proper spacing
        CreateSoldiersInFormation(formation, formationGO.transform.position, teamColor);
        
        // Create world-space badge UI above the formation
        formation.CreateOrUpdateBadgeUI();
        
        // Get formation size from unit data for logging
        CombatUnitData unitDataForLog = isAttacker ? selectedAttackerUnit : selectedDefenderUnit;
        int actualFormationSize = unitDataForLog != null ? unitDataForLog.formationSize : soldiersPerFormation;
        DebugLog($"Created {formationName} with {actualFormationSize} soldiers (from unit data: {unitDataForLog?.unitName ?? "none"})");
    }
    
    void CreateSoldiersInFormation(FormationUnit formation, Vector3 centerPosition, Color teamColor)
    {
        // Get formation settings from CombatUnitData (use attacker or defender unit data)
        CombatUnitData unitData = formation.isAttacker ? selectedAttackerUnit : selectedDefenderUnit;
        
        // Use formationSize from unit data, fallback to soldiersPerFormation if no unit data
        int formationSize = unitData != null ? unitData.formationSize : soldiersPerFormation;
        // Validate formation size (must be at least 1)
        formationSize = Mathf.Max(1, formationSize);
        float spacing = unitData != null ? unitData.formationSpacing : 1.5f; // Default to 1.5f if no unit data
        FormationShape shape = unitData != null ? unitData.formationShape : FormationShape.Square;
        
        DebugLog($"Creating {formationSize} soldiers for formation {formation.formationName} (from unit data: {unitData?.unitName ?? "none"})");
        
        // Store spacing in formation for later use
        formation.soldierSpacing = spacing;
        
        // Calculate formation positions based on shape and size from unit data
        int sideLength = Mathf.CeilToInt(Mathf.Sqrt(formationSize));
        
        for (int i = 0; i < formationSize; i++)
        {
            try
            {
                // Calculate position based on formation shape
                Vector3 soldierPosition;
                
                switch (shape)
                {
                    case FormationShape.Square:
                        int x = i % sideLength;
                        int z = i / sideLength;
                        soldierPosition = centerPosition + new Vector3(
                            (x - sideLength / 2f) * spacing,
                            0,
                            (z - sideLength / 2f) * spacing
                        );
                        break;
                        
                    case FormationShape.Circle:
                        float angle = (float)i / formationSize * 2f * Mathf.PI;
                        soldierPosition = centerPosition + new Vector3(
                            Mathf.Cos(angle) * spacing,
                            0,
                            Mathf.Sin(angle) * spacing
                        );
                        break;
                        
                    case FormationShape.Wedge:
                        int row = i / 3;
                        int col = i % 3;
                        soldierPosition = centerPosition + new Vector3(
                            (col - 1) * spacing * (row + 1) * 0.5f,
                            0,
                            row * spacing
                        );
                        break;
                        
                    default:
                        // Fallback to square
                        x = i % sideLength;
                        z = i / sideLength;
                        soldierPosition = centerPosition + new Vector3(
                            (x - sideLength / 2f) * spacing,
                            0,
                            (z - sideLength / 2f) * spacing
                        );
                        break;
                }
                
                DebugLog($"Creating soldier {i + 1} at position {soldierPosition}");
                
                // Create soldier using existing CombatUnit system
                GameObject soldier = CreateCombatUnitSoldier($"Soldier{i + 1}", soldierPosition, teamColor, formation.isAttacker, formation);
                
                if (soldier != null)
                {
                    // CRITICAL: Set parent FIRST - this ensures GetComponentInParent<FormationUnit>() works
                    soldier.transform.SetParent(formation.transform);
                    
                    // Add to formation's soldiers list BEFORE verifying
                    formation.soldiers.Add(soldier);
                    
                    // Verify the unit can find its formation
                    var combatUnit = soldier.GetComponent<CombatUnit>();
                    if (combatUnit != null)
                    {
                        var foundFormation = GetFormationFromUnit(combatUnit);
                        if (foundFormation == null)
                        {
                            DebugLog($"Warning: Soldier {i + 1} added to formation but GetFormationFromUnit returned null");
                        }
                    }
                    
                    // Don't trigger idle animation here - let CombatUnit.Initialize() handle it
                    // The animator controller will naturally transition to Idle state when IsWalking=false
                    
                    DebugLog($"Successfully created and added soldier {i + 1} to formation {formation.formationName}");
                }
                else
                {
                    DebugLog($"ERROR: Failed to create soldier {i + 1}");
                }
            }
            catch (System.Exception e)
            {
                DebugLog($"ERROR creating soldier {i + 1}: {e.Message}");
            }
        }
        
        DebugLog($"Formation {formation.formationName} now has {formation.soldiers.Count} soldiers");
        
        // Calculate formation stats based on actual soldiers created
        if (formation.soldiers.Count > 0)
        {
            int totalHealth = 0;
            int totalAttack = 0;
            
            foreach (var soldier in formation.soldiers)
            {
                if (soldier == null) continue;
                var combatUnit = soldier.GetComponent<CombatUnit>();
                if (combatUnit != null)
                {
                    totalHealth += combatUnit.MaxHealth;
                    totalAttack += combatUnit.CurrentAttack;
                }
            }
            
            formation.totalHealth = totalHealth;
            formation.totalAttack = totalAttack;
            formation.currentHealth = totalHealth;
        }
    }
    
    GameObject CreateCombatUnitSoldier(string soldierName, Vector3 position, Color teamColor, bool isAttacker, FormationUnit formation)
    {
        try
        {
            // Try to use actual unit prefab if available
            CombatUnitData unitData = isAttacker ? selectedAttackerUnit : selectedDefenderUnit;
            GameObject soldier;
            
            if (unitData != null)
            {
                // Use GetPrefab() which loads from prefabPath on-demand
                GameObject prefab = unitData.GetPrefab();
                
                if (prefab != null)
                {
                    // Use actual unit prefab
                    soldier = Instantiate(prefab, position, Quaternion.identity);
                    soldier.name = soldierName;
                    
                    // Put soldier on Units layer if present
                    int uLayer = LayerMask.NameToLayer("Units"); if (uLayer != -1) soldier.layer = uLayer;
                    DebugLog($"Created {soldierName} using prefab: {unitData.unitName} (path: {unitData.prefabPath})");
                }
                else
                {
                    // Fallback to simple unit if prefab not available
                    soldier = CreateFallbackSoldier(soldierName, position, teamColor);
                    int uLayer = LayerMask.NameToLayer("Units"); if (uLayer != -1) soldier.layer = uLayer;
                    DebugLog($"Created {soldierName} using fallback (prefab not found at path: '{unitData.prefabPath}')");
                }
            }
            else
            {
                // No unit data available
                soldier = CreateFallbackSoldier(soldierName, position, teamColor);
                int uLayer = LayerMask.NameToLayer("Units"); if (uLayer != -1) soldier.layer = uLayer;
                DebugLog($"Created {soldierName} using fallback (no unit data)");
            }
            
            // Add CombatUnit component for animations and stats
            var combatUnit = soldier.GetComponent<CombatUnit>();
            if (combatUnit == null)
            {
                // CRITICAL: Check if this is a real prefab or fallback
                bool usingPrefab = unitData != null && unitData.GetPrefab() != null;
                
                // DEBUG: Log what we found
                DebugLog($"[BATTLE DEBUG] '{soldierName}': usingPrefab={usingPrefab}, hasCombatUnit={combatUnit != null}");
                
                if (!usingPrefab)
                {
                    // Fallback soldier - always add CombatUnit
                combatUnit = soldier.AddComponent<CombatUnit>();
                    DebugLog($"[BATTLE DEBUG] Added CombatUnit to fallback soldier '{soldierName}'");
                }
                else
                {
                    // Real prefab but missing CombatUnit - this means the prefab is a model-only prefab
                    // We MUST add CombatUnit for the battle system to work
                    Debug.LogWarning($"[BATTLE DEBUG] Prefab '{soldierName}' (from '{unitData.prefabPath}') is missing CombatUnit component. " +
                        $"This prefab appears to be a model-only prefab. Adding CombatUnit component now.");
                    combatUnit = soldier.AddComponent<CombatUnit>();
                    DebugLog($"[BATTLE DEBUG] Added CombatUnit to prefab instance '{soldierName}' (prefab was model-only)");
                }
            }
            
            // Ensure soldier has proper collider (don't replace if using real prefab)
            var collider = soldier.GetComponent<Collider>();
            if (collider == null)
            {
                // Only add collider if none exists (for fallback soldiers)
                var capsuleCollider = soldier.AddComponent<CapsuleCollider>();
                capsuleCollider.height = 2f;
                capsuleCollider.radius = 0.4f;
                capsuleCollider.center = new Vector3(0, 1f, 0); // Center at body, not feet
            }
            
            // Add UnitSeparation component to prevent overlap
            var unitSeparation = soldier.GetComponent<UnitSeparation>();
            if (unitSeparation == null)
            {
                unitSeparation = soldier.AddComponent<UnitSeparation>();
                // Configure separation settings
                unitSeparation.minSeparation = MIN_UNIT_SEPARATION;
            }
            
            // Ensure soldier has Rigidbody for trigger detection to work
            // Unity requires at least one Rigidbody for OnTriggerEnter to fire
            // IMPORTANT: Both objects need Rigidbodies for triggers to work reliably
            var rigidbody = soldier.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = soldier.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true; // Kinematic so soldiers don't fall, but triggers still work
                rigidbody.useGravity = false; // No gravity needed
                rigidbody.detectCollisions = true; // Ensure collision detection is enabled
                Debug.Log($"[CombatSetup] Added kinematic Rigidbody to {soldier.name} for trigger detection");
            }
            else
            {
                // Ensure existing Rigidbody is kinematic (so soldiers don't fall)
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
                rigidbody.detectCollisions = true; // Ensure collision detection is enabled
            }
            
            // Use existing trigger collider for contact detection (units already have trigger colliders)
            // Find any existing trigger collider on the soldier and ensure it has adequate detection range
            const float MIN_DETECTION_RADIUS = 6.5f; // Minimum radius for reliable contact detection (1.5 units)
            Collider triggerCollider = null;
            var allColliders = soldier.GetComponents<Collider>();
            foreach (var col in allColliders)
            {
                if (col.isTrigger)
                {
                    triggerCollider = col;
                    
                    // Adjust radius if it's too small for reliable detection
                    if (col is SphereCollider sphere)
                    {
                        if (sphere.radius < MIN_DETECTION_RADIUS)
                        {
                            sphere.radius = MIN_DETECTION_RADIUS;
                            Debug.Log($"[CombatSetup] Increased trigger radius on {soldier.name} from {sphere.radius} to {MIN_DETECTION_RADIUS}");
                        }
                    }
                    else if (col is CapsuleCollider capsule)
                    {
                        // For capsule collider, increase radius if too small
                        if (capsule.radius < MIN_DETECTION_RADIUS)
                        {
                            capsule.radius = MIN_DETECTION_RADIUS;
                            Debug.Log($"[CombatSetup] Increased capsule trigger radius on {soldier.name} to {MIN_DETECTION_RADIUS}");
                        }
                    }
                    
                    Debug.Log($"[CombatSetup] Found existing trigger collider on {soldier.name}: {col.GetType().Name}, radius={(col is SphereCollider s ? s.radius : (col is CapsuleCollider c ? c.radius : 0f))}");
                    break;
                }
            }
            
            if (triggerCollider == null)
            {
                Debug.LogWarning($"[CombatSetup] {soldier.name} has no trigger collider! Contact detection may not work. Found {allColliders.Length} collider(s) total.");
                // List all colliders for debugging
                foreach (var col in allColliders)
                {
                    Debug.LogWarning($"[CombatSetup]   - {col.GetType().Name}, isTrigger={col.isTrigger}, enabled={col.enabled}");
                }
            }
            else
            {
                // Ensure trigger collider is enabled and properly configured
                triggerCollider.enabled = true;
                
                // Verify Rigidbody is on the same GameObject or parent of the collider
                // Unity triggers work when Rigidbody is on same GameObject or parent
                var colliderRb = triggerCollider.GetComponent<Rigidbody>();
                if (colliderRb == null)
                {
                    // Check if collider is on a child - Rigidbody should be on parent
                    var parentRb = triggerCollider.GetComponentInParent<Rigidbody>();
                    if (parentRb == null || parentRb.gameObject != soldier)
                    {
                        Debug.LogWarning($"[CombatSetup] {soldier.name}: Trigger collider may not detect properly - Rigidbody should be on same GameObject or parent");
                    }
                }
                
                // Ensure the collider's GameObject is active
                if (!triggerCollider.gameObject.activeInHierarchy)
                {
                    Debug.LogWarning($"[CombatSetup] {soldier.name}: Trigger collider GameObject is not active!");
                }
            }
            
            // Initialize contact tracking for this soldier (on the formation)
            if (!formation.soldierContacts.ContainsKey(soldier))
            {
                formation.soldierContacts[soldier] = new HashSet<GameObject>();
            }
            
            // Initialize attack cooldown for this soldier (on the formation)
            if (!formation.soldierAttackCooldowns.ContainsKey(soldier))
            {
                formation.soldierAttackCooldowns[soldier] = 0f; // Ready to attack immediately
            }
            
            // Add contact detector component to track trigger events
            var contactDetector = soldier.GetComponent<FormationSoldierContactDetector>();
            if (contactDetector == null)
            {
                contactDetector = soldier.AddComponent<FormationSoldierContactDetector>();
                contactDetector.Initialize(formation, soldier);
                Debug.Log($"[CombatSetup] Added FormationSoldierContactDetector to {soldier.name}");
            }
            else
            {
                // Re-initialize in case formation reference changed
                contactDetector.Initialize(formation, soldier);
            }
            
            // Initialize with unit data if available
            if (unitData != null && combatUnit != null)
            {
                try
                {
                    // Use runtime civ instances created at Start
                    Civilization selectedCiv = isAttacker ? attackerCivInstance : defenderCivInstance;
                    
                    if (selectedCiv != null && unitData != null)
                    {
                        combatUnit.Initialize(unitData, selectedCiv);
                        
                        DebugLog($"Initialized {soldierName} with unit data and selected civilization");
                    }
                    else if (unitData != null)
                    {
                        // Fallback to temporary civilization if none selected
                        var tempCiv = CreateTemporaryCivilization(isAttacker);
                        if (tempCiv != null)
                        {
                            combatUnit.Initialize(unitData, tempCiv);
                            DebugLog($"Initialized {soldierName} with unit data and temporary civilization");
                        }
                        else
                        {
                            DebugLog($"Warning: Could not create temp civ for {soldierName}, setting basic properties");
                            // Set basic properties manually if civ creation failed
                            combatUnit.isAttacker = isAttacker;
                            combatUnit.battleState = BattleUnitState.Idle;
                        }
                    }
                    else
                    {
                        DebugLog($"Warning: No unit data for {soldierName}, setting basic properties");
                        // Set basic properties manually if no unit data
                        combatUnit.isAttacker = isAttacker;
                        combatUnit.battleState = BattleUnitState.Idle;
                    }
                    
                    // CRITICAL: Set isAttacker flag and initialize battle state for victory manager
                    // This ensures defenders are properly counted (isAttacker = false for defenders)
                    // Always set the flag, even if Initialize failed
                    combatUnit.isAttacker = isAttacker;
                    
                    // Only call InitializeForBattle if we have unit data and civ properly initialized
                    if (unitData != null && combatUnit.data != null && combatUnit.owner != null)
                    {
                        try
                        {
                            combatUnit.InitializeForBattle(isAttacker);
                            
                            DebugLog($"Set {soldierName} as {(isAttacker ? "attacker" : "defender")} and initialized battle state");
                        }
                        catch (System.Exception e2)
                        {
                            DebugLog($"Warning: InitializeForBattle failed for {soldierName}: {e2.Message}");
                            // Still set basic state
                            combatUnit.battleState = BattleUnitState.Idle;
                        }
                    }
                    else
                    {
                        // Still set the flag even if we can't fully initialize
                        combatUnit.battleState = BattleUnitState.Idle;
                        DebugLog($"Set {soldierName} as {(isAttacker ? "attacker" : "defender")} (basic initialization, data={combatUnit.data != null}, owner={combatUnit.owner != null})");
                    }
                }
                catch (System.Exception e)
                {
                    DebugLog($"ERROR initializing {soldierName}: {e.Message}");
                    DebugLog($"ERROR stack trace: {e.StackTrace}");
                    // Continue without initialization - soldier will still work
                    // CRITICAL: Always set isAttacker flag even if initialization failed
                    // This ensures the victory manager can find the unit
                    combatUnit.isAttacker = isAttacker;
                    combatUnit.battleState = BattleUnitState.Idle;
                    DebugLog($"Set {soldierName} as {(isAttacker ? "attacker" : "defender")} (fallback after error)");
                }
            }
            else
            {
                DebugLog($"No unit data available for {soldierName} or no CombatUnit present; skipping initialization to preserve prefab authoring");
                if (combatUnit != null)
                {
                // Set basic properties manually for fallback soldiers
                combatUnit.isAttacker = isAttacker;
                combatUnit.battleState = BattleUnitState.Idle;
                }
            }
            
            // Add collider for selection
            var selectionCollider = soldier.GetComponent<CapsuleCollider>();
            if (selectionCollider == null)
            {
                selectionCollider = soldier.AddComponent<CapsuleCollider>();
            }
            selectionCollider.isTrigger = false;
            
            // Ground initial spawn precisely on the battlefield
            soldier.transform.position = GetGroundPosition(soldier.transform.position);
            
            return soldier;
        }
        catch (System.Exception e)
        {
            DebugLog($"ERROR creating soldier {soldierName}: {e.Message}");
            return null;
        }
    }

    // Instantiate a Civilization from CivData only when starting the test
    // MEMORY OPTIMIZED: Ensures tech/culture lists are empty for battle-only civs
    private Civilization InstantiateSelectedCiv(CivData civData, string runtimeName)
    {
        try
        {
            if (civData == null)
            {
                DebugLog($"Warning: civData is null when instantiating {runtimeName}");
                return null;
            }
            
            GameObject civGO = new GameObject(string.IsNullOrEmpty(runtimeName) ? "RuntimeCiv" : runtimeName);
            var civ = civGO.AddComponent<Civilization>();
            civ.Initialize(civData, null, false);
            
            // MEMORY OPTIMIZATION: Clear tech/culture lists for battle-only civilizations
            // These are not needed for battle tests and can consume significant memory
            if (civ.researchedTechs != null)
            {
                civ.researchedTechs.Clear();
            }
            if (civ.researchedCultures != null)
            {
                civ.researchedCultures.Clear();
            }
            
            // Log to verify civ data is set
            DebugLog($"Created {runtimeName} with civ data: {civData.civName} (tech/culture lists cleared for memory optimization)");
            
            return civ;
        }
        catch (System.Exception e)
        {
            DebugLog($"Error instantiating civ instance: {e.Message}");
            return null;
        }
    }

    // Load exactly one unit prefab on demand without scanning all resources
    private GameObject LoadUnitPrefabOnDemand(string unitName)
    {
        if (string.IsNullOrEmpty(unitName)) return null;
        string key = unitName.ToLowerInvariant();
        if (onDemandPrefabCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        // Try a small set of precise paths without loading everything
        GameObject loaded = null;
        // Prefer exact unit name under Units/
        loaded = Resources.Load<GameObject>("Units/" + unitName);
        if (loaded == null)
        {
            // Common alt: folder per unit type; keep minimal guess to avoid wide loads
            loaded = Resources.Load<GameObject>("Units/" + unitName + "/" + unitName);
        }
        if (loaded == null)
        {
            // Try simple name variants and a known subfolder used in the project
            string noSpace = unitName.Replace(" ", "");
            string underscore = unitName.Replace(" ", "_");
            // Units root
            loaded = Resources.Load<GameObject>("Units/" + noSpace)
                  ?? Resources.Load<GameObject>("Units/" + underscore);
            if (loaded == null)
            {
                // One-known subfolder guess without scanning entire Resources
                loaded = Resources.Load<GameObject>("Units/Monuments Units/" + unitName)
                      ?? Resources.Load<GameObject>("Units/Monuments Units/" + noSpace)
                      ?? Resources.Load<GameObject>("Units/Monuments Units/" + underscore);
            }
        }
        if (loaded != null)
        {
            onDemandPrefabCache[key] = loaded;
        }
        return loaded;
    }
    
    Civilization CreateTemporaryCivilization(bool isAttacker)
    {
        // Create a temporary civilization for the unit
        GameObject tempCivGO = new GameObject($"TempCiv_{(isAttacker ? "Attacker" : "Defender")}");
        var tempCiv = tempCivGO.AddComponent<Civilization>();
        
        // Create minimal civ data with all required fields
        var civData = ScriptableObject.CreateInstance<CivData>();
        civData.civName = isAttacker ? "Attacker" : "Defender";
        
        // Initialize with proper parameters to avoid null reference
        tempCiv.Initialize(civData, null, false);
        
        // MEMORY OPTIMIZATION: Clear tech/culture lists for temporary battle-only civilizations
        if (tempCiv.researchedTechs != null)
        {
            tempCiv.researchedTechs.Clear();
        }
        if (tempCiv.researchedCultures != null)
        {
            tempCiv.researchedCultures.Clear();
        }
        
        return tempCiv;
    }
    
    void InitializePrefabCache()
    {
        // No-op in on-demand mode. We intentionally do not pre-load any prefabs.
        DebugLog("On-demand prefab mode: skipping eager cache initialization");
    }
    
    GameObject FindPrefabInCache(string unitName)
    {
        // Legacy path retained for compatibility: check the on-demand cache only
        if (string.IsNullOrEmpty(unitName)) return null;
        string key = unitName.ToLowerInvariant();
        if (onDemandPrefabCache.TryGetValue(key, out var prefab) && prefab != null)
        {
            DebugLog($"Found prefab in on-demand cache for {unitName}");
            return prefab;
        }
        return null;
    }
    
    GameObject CreateFallbackSoldier(string soldierName, Vector3 position, Color teamColor)
    {
        // Create a simple fallback soldier
        GameObject soldier = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        soldier.name = soldierName;
        soldier.transform.position = position;
        soldier.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        
        // Color the soldier
        var renderer = soldier.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = teamColor;
        }
        
        // Add Animator component for idle animations
        var animator = soldier.GetComponent<Animator>();
        if (animator == null)
        {
            animator = soldier.AddComponent<Animator>();
        }
        
        return soldier;
    }
    
    
    void UnloadUnitPrefabs()
    {
        // Clear prefab references from ResourceCache to free memory
        // This clears any cached prefabs that were loaded (new system uses paths, so this is just cleanup)
        ResourceCache.UnloadPrefabReferences();
        DebugLog("Unloaded prefab references from cached unit data");
    }
    
    void ClearPrefabCache()
    {
        if (onDemandPrefabCache != null)
        {
            // Clear dictionary to release all prefab references
            onDemandPrefabCache.Clear();
            // Note: Dictionary.Clear() removes all entries and releases references
            // The GameObjects will be garbage collected if no other references exist
            DebugLog("Cleared on-demand prefab cache (released all prefab references)");
        }
    }
    
    GameObject CreateSoldier(string soldierName, Color color)
    {
        GameObject soldier = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        soldier.name = soldierName;
        soldier.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        
        // Color the soldier
        var renderer = soldier.GetComponent<Renderer>();
        renderer.material.color = color;
        
        // Add collider for selection
        var collider = soldier.GetComponent<CapsuleCollider>();
        collider.isTrigger = false;
        
        // Add animator for animations
        var animator = soldier.AddComponent<Animator>();
        // Note: You'll need to assign an Animator Controller with IsWalking and IsFighting parameters
        
        return soldier;
    }
    
    List<Vector3> CalculateFormationPositions(Vector3 center, int formationSize, float spacing, FormationShape shape)
    {
        var positions = new List<Vector3>();
        
        switch (shape)
        {
            case FormationShape.Square:
                int sideLength = Mathf.CeilToInt(Mathf.Sqrt(formationSize));
                for (int i = 0; i < formationSize; i++)
                {
                    int row = i / sideLength;
                    int col = i % sideLength;
                    Vector3 offset = new Vector3(
                        (col - sideLength / 2f) * spacing,
                        0,
                        (row - sideLength / 2f) * spacing
                    );
                    positions.Add(center + offset);
                }
                break;
                
            case FormationShape.Circle:
                for (int i = 0; i < formationSize; i++)
                {
                    float angle = (float)i / formationSize * 2f * Mathf.PI;
                    Vector3 offset = new Vector3(
                        Mathf.Cos(angle) * spacing,
                        0,
                        Mathf.Sin(angle) * spacing
                    );
                    positions.Add(center + offset);
                }
                break;
                
            case FormationShape.Wedge:
                int rows = Mathf.CeilToInt(formationSize / 3f);
                for (int i = 0; i < formationSize; i++)
                {
                    int row = i / 3;
                    int col = i % 3;
                    Vector3 offset = new Vector3(
                        (col - 1) * spacing * (row + 1) * 0.5f,
                        0,
                        row * spacing
                    );
                    positions.Add(center + offset);
                }
                break;
        }
        
        return positions;
    }
    
    void UpdateStatus(string message)
    {
        // Trimmed per new UI requirements; keep debug log only
        DebugLog($"Status: {message}");
    }

    // --- Battle HUD: Consolidated to use BattleUI exclusively ---
    private void CreateBattleHUD()
    {
        // Find or get BattleUI component (battleUI is assigned in Inspector, but fallback if needed)
        if (battleUI == null)
        {
            battleUI = FindFirstObjectByType<BattleUI>();
        }
        
        // Create BattleUI if it doesn't exist
        if (battleUI == null)
        {
            GameObject battleUIGO = new GameObject("BattleUI");
            battleUI = battleUIGO.AddComponent<BattleUI>();
            DebugLog("Created BattleUI component");
        }
        
        // Initialize BattleUI with this battle system
        battleUI.InitializeWithBattleTest(this);
        // Note: UpdateFormationsList will be called by UpdateBattleUIDelayed coroutine
        // after formations are fully initialized - don't call it here to avoid clearing buttons
        DebugLog("Using BattleUI component exclusively for battle HUD");
    }
    
    void DebugLog(string message)
    {
        // Debug.Log removed for performance - use Debug.LogWarning or Debug.LogError for important messages
    }
    
    /// <summary>
    /// Get default attacker spawn points (fallback when no map generator)
    /// </summary>
    List<Vector3> GetDefaultAttackerSpawns()
    {
        var spawns = new List<Vector3>();
        for (int i = 0; i < formationsPerSide; i++)
        {
            spawns.Add(new Vector3(-15, 0, (i - 1) * 12));
        }
        return spawns;
    }
    
    /// <summary>
    /// Get default defender spawn points (fallback when no map generator)
    /// </summary>
    List<Vector3> GetDefaultDefenderSpawns()
    {
        var spawns = new List<Vector3>();
        for (int i = 0; i < formationsPerSide; i++)
        {
            spawns.Add(new Vector3(15, 0, (i - 1) * 12));
        }
        return spawns;
    }
    
    // Selection system methods
    FormationUnit GetFormationAtPosition(Vector3 position)
    {
        // Safely skip destroyed formations
        foreach (var formation in allFormations)
        {
            if (formation == null) continue;
            var tf = formation.transform;
            if (tf == null) continue;
            if ((tf.position - position).sqrMagnitude < 25f) // 5f * 5f = 25f
            {
                return formation;
            }
        }
        return null;
    }
    
    /// <summary>
    /// Find the formation that contains this unit
    /// </summary>
    public FormationUnit GetFormationFromUnit(CombatUnit unit)
    {
        if (unit == null) return null;
        
        // First, check if unit's parent transform is a formation
        var parent = unit.transform.parent;
        if (parent != null)
        {
            var parentFormation = parent.GetComponent<FormationUnit>();
            if (parentFormation != null)
            {
                // Check if it's in allFormations (if available), otherwise just return it if it has the component
                if (allFormations == null || allFormations.Count == 0 || allFormations.Contains(parentFormation))
                {
                    return parentFormation;
                }
            }
        }
        
        // Second, check if unit's parent hierarchy contains a formation
        var formation = unit.GetComponentInParent<FormationUnit>();
        if (formation != null)
        {
            // Check if it's in allFormations (if available), otherwise just return it if it has the component
            if (allFormations == null || allFormations.Count == 0 || allFormations.Contains(formation))
            {
                return formation;
            }
        }
        
        // Third, search all formations for this unit in their soldiers list
        foreach (var f in allFormations)
        {
            if (f == null) continue;
            if (f.soldiers != null && f.soldiers.Contains(unit.gameObject))
            {
                return f;
            }
        }
        
        // Last resort: check if any formation's transform is the parent
        foreach (var f in allFormations)
        {
            if (f == null) continue;
            if (unit.transform.IsChildOf(f.transform))
            {
                return f;
            }
        }
        
        return null;
    }
    
    public void ClearSelection()
    {
        foreach (var formation in selectedFormations)
        {
            if (formation == null) continue;
            formation.SetSelected(false);
        }
        selectedFormations.Clear();
        
        // Also clear UnitSelectionManager selection if it exists (for compatibility)
        if (UnitSelectionManager.Instance != null && UnitSelectionManager.Instance.HasSelectedUnit())
        {
            UnitSelectionManager.Instance.DeselectUnit();
        }
    }
    
    public void SelectFormation(FormationUnit formation)
    {
        if (!selectedFormations.Contains(formation))
        {
            selectedFormations.Add(formation);
            formation.SetSelected(true);
        }
    }
    
    /// <summary>
    /// Select a unit directly - finds its formation and selects that instead
    /// </summary>
    void SelectUnit(CombatUnit unit)
    {
        if (unit == null) return;
        
        // Find the formation this unit belongs to and select that instead
        var formation = GetFormationFromUnit(unit);
        if (formation != null)
        {
            if (!Input.GetKey(KeyCode.LeftControl))
            {
                ClearSelection();
            }
            SelectFormation(formation);
            DebugLog($"Selected formation {formation.formationName} (clicked on unit: {unit.data?.unitName ?? "Unknown"})");
        }
        else
        {
            // Unit not in a formation - still show info via UnitSelectionManager
            if (UnitSelectionManager.Instance != null)
            {
                UnitSelectionManager.Instance.SelectUnit(unit);
            }
            DebugLog($"Clicked on unit {unit.data?.unitName ?? "Unknown"} but it's not in a formation");
        }
    }
    
    /// <summary>
    /// Public method for OnMouseDown to call (provides backup selection path)
    /// </summary>
    public void SelectUnitDirectly(CombatUnit unit)
    {
        SelectUnit(unit);
    }
    
    void CreateSelectionBox()
    {
        if (selectionBox != null) Destroy(selectionBox);
        
        selectionBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
        selectionBox.name = "SelectionBox";
        selectionBox.GetComponent<Collider>().enabled = false;
        
        if (selectionBoxMaterial != null)
        {
            selectionBox.GetComponent<Renderer>().material = selectionBoxMaterial;
        }
        else
        {
            var renderer = selectionBox.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = selectionColor;
            renderer.material.SetFloat("_Mode", 3); // Transparent
            renderer.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            renderer.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            renderer.material.SetInt("_ZWrite", 0);
            renderer.material.DisableKeyword("_ALPHATEST_ON");
            renderer.material.EnableKeyword("_ALPHABLEND_ON");
            renderer.material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            renderer.material.renderQueue = 3000;
        }
    }
    
    void UpdateSelectionBox()
    {
        if (selectionBox == null) return;
        
        Vector3 center = (dragStart + dragEnd) / 2f;
        Vector3 size = new Vector3(
            Mathf.Abs(dragEnd.x - dragStart.x),
            0.1f,
            Mathf.Abs(dragEnd.z - dragStart.z)
        );
        
        selectionBox.transform.position = center;
        selectionBox.transform.localScale = size;
    }
    
    void DestroySelectionBox()
    {
        if (selectionBox != null)
        {
            Destroy(selectionBox);
            selectionBox = null;
        }
    }
    
    void SelectFormationsInBox()
    {
        if (selectionBox == null) return;
        
        Bounds selectionBounds = selectionBox.GetComponent<Renderer>().bounds;
        foreach (var formation in allFormations)
        {
            if (formation == null) continue;
            var tf = formation.transform;
            if (tf == null) continue;
            if (selectionBounds.Contains(tf.position))
            {
                SelectFormation(formation);
            }
        }
    }
    
    // ===== BATTLE MANAGER FUNCTIONALITY (Merged from BattleManager.cs) =====
    
    /// <summary>
    /// Start a battle between two civilizations (from BattleManager)
    /// </summary>
    public void StartBattle(Civilization attackerCiv, Civilization defenderCiv, 
                          List<CombatUnit> attackerUnitsList, List<CombatUnit> defenderUnitsList)
    {
        if (battleInProgress)
        {
            DebugLog("[BattleTestSimple] Battle already in progress!");
            return;
        }

        attacker = attackerCiv;
        defender = defenderCiv;
        attackerUnits = new List<CombatUnit>(attackerUnitsList);
        defenderUnits = new List<CombatUnit>(defenderUnitsList);

        DebugLog($"[BattleTestSimple] Starting battle: {attacker.civData.civName} vs {defender.civData.civName}");
        DebugLog($"[BattleTestSimple] Units: {attackerUnits.Count} vs {defenderUnits.Count}");

        // Check if we should load a battle scene or use current scene
        if (SceneManager.GetActiveScene().name != "BattleScene")
        {
            var loadSceneCoroutine = StartCoroutine(LoadBattleScene());
            trackedCoroutines.Add(loadSceneCoroutine);
        }
        else
        {
            // Already in battle scene, initialize directly
            InitializeBattle();
        }
    }
    
    private IEnumerator LoadBattleScene()
    {
        // Load the battle scene
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("BattleScene");
        
        // Wait until the scene is loaded
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // Initialize battle after scene loads
        yield return new WaitForEndOfFrame();
        InitializeBattle();
    }
    
    private void InitializeBattle()
    {
        try
        {
            battleInProgress = true;
            isPaused = false;

            // Use existing map generator (assigned in Inspector or already exists)
            // BattleMapGenerator is a separate component - don't create it here
            if (mapGenerator == null)
            {
                if (cachedMapGenerator == null)
                {
                    cachedMapGenerator = FindFirstObjectByType<BattleMapGenerator>();
                }
                mapGenerator = cachedMapGenerator;
            }

            // Generate battle map if map generator exists and is enabled
            if (mapGenerator != null && generateNewMap)
            {
                mapGenerator.GenerateBattleMap(battleMapSize, attackerUnits.Count, defenderUnits.Count);
            }
            else if (mapGenerator == null)
            {
                // Fallback: create simple ground if no map generator (use cached reference)
                if (cachedTestGround == null)
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    ground.transform.localScale = new Vector3(20, 1, 20);
                    ground.name = "TestGround";
                    ground.transform.position = Vector3.zero;
                    cachedTestGround = ground; // Cache reference
                }
            }

            // Mark all units as in battle (prevents world map movement from interfering)
            foreach (var unit in attackerUnits)
            {
                if (unit != null) unit.IsInBattle = true;
            }
            foreach (var unit in defenderUnits)
            {
                if (unit != null) unit.IsInBattle = true;
            }

            // Create formations from units
            CreateFormationsFromUnits(attackerUnits, defenderUnits);

            // Initialize battle UI
            CreateBattleHUD();
            
            // Initialize AI system
            InitializeFormationAI();

            // Set up camera
            SetupBattleCamera();

            DebugLog("[BattleTestSimple] Battle initialized successfully!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BattleTestSimple] Error initializing battle: {e.Message}");
            EndBattle(null);
        }
    }
    
    /// <summary>
    /// Create a shared Canvas for all formation badges (memory optimization - one Canvas instead of many)
    /// </summary>
    private void CreateSharedFormationBadgeCanvas()
    {
        if (sharedFormationBadgeCanvas != null) return; // Already created
        
        var go = new GameObject("SharedFormationBadgeCanvas");
        go.transform.SetParent(transform); // Parent to BattleTestSimple
        
        // Put UI on UI layer so it doesn't block unit raycasts
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer != -1)
        {
            go.layer = uiLayer;
        }
        
        sharedFormationBadgeCanvas = go.AddComponent<Canvas>();
        sharedFormationBadgeCanvas.renderMode = RenderMode.WorldSpace;
        var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        var raycaster = go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        raycaster.enabled = false; // Disable raycasting on UI elements
        
        DebugLog("Created shared Canvas for formation badges (memory optimized)");
    }
    
    /// <summary>
    /// Get the shared Canvas for formation badges (creates it if needed)
    /// </summary>
    public Canvas GetSharedFormationBadgeCanvas()
    {
        if (sharedFormationBadgeCanvas == null)
        {
            CreateSharedFormationBadgeCanvas();
        }
        return sharedFormationBadgeCanvas;
    }
    
    private void InitializeFormationAI()
    {
        // Find or create formation AI manager
        if (formationAIManager == null)
        {
            formationAIManager = FormationAIManager.Instance;
            if (formationAIManager == null)
            {
                GameObject aiManagerGO = new GameObject("FormationAIManager");
                formationAIManager = aiManagerGO.AddComponent<FormationAIManager>();
            }
        }
        
        // Formations will register themselves via FormationUnit.Start()
        // No need to manually pass formations - they register when ready
        DebugLog("[BattleTestSimple] Formation AI system initialized. Formations will register themselves.");
    }
    
    /// <summary>
    /// Create formations from lists of units (for BattleManager compatibility)
    /// </summary>
    private void CreateFormationsFromUnits(List<CombatUnit> attackerUnitsList, List<CombatUnit> defenderUnitsList)
    {
        // Group units into formations
        List<Vector3> attackerSpawns = mapGenerator != null ? mapGenerator.GetAttackerSpawnPoints() : GetDefaultAttackerSpawns();
        List<Vector3> defenderSpawns = mapGenerator != null ? mapGenerator.GetDefenderSpawnPoints() : GetDefaultDefenderSpawns();
        
        // Create formations for attackers
        int formationIndex = 0;
        for (int i = 0; i < attackerUnitsList.Count; i += soldiersPerFormation)
        {
            if (formationIndex >= attackerSpawns.Count) break;
            Vector3 spawnPos = attackerSpawns[formationIndex];
            CreateFormationFromUnits($"AttackerFormation{formationIndex + 1}", spawnPos, Color.red, true, 
                attackerUnitsList.GetRange(i, Mathf.Min(soldiersPerFormation, attackerUnitsList.Count - i)));
            formationIndex++;
        }
        
        // Create formations for defenders
        formationIndex = 0;
        for (int i = 0; i < defenderUnitsList.Count; i += soldiersPerFormation)
        {
            if (formationIndex >= defenderSpawns.Count) break;
            Vector3 spawnPos = defenderSpawns[formationIndex];
            CreateFormationFromUnits($"DefenderFormation{formationIndex + 1}", spawnPos, Color.blue, false,
                defenderUnitsList.GetRange(i, Mathf.Min(soldiersPerFormation, defenderUnitsList.Count - i)));
            formationIndex++;
        }
    }
    
    /// <summary>
    /// Create a formation from existing units (for BattleManager compatibility)
    /// </summary>
    private void CreateFormationFromUnits(string formationName, Vector3 position, Color teamColor, bool isAttacker, List<CombatUnit> units)
    {
        // Create formation GameObject
        GameObject formationGO = new GameObject(formationName);
        formationGO.transform.position = GetGroundPosition(position);
        
        // Add FormationUnit component
        FormationUnit formation = formationGO.AddComponent<FormationUnit>();
        formation.formationName = formationName;
        formation.isAttacker = isAttacker;
        formation.teamColor = teamColor;
        formation.formationCenter = position;
        
        // Store reference to source units for soldier count tracking
        formation.sourceUnits = new List<CombatUnit>(units);
        
        // Create soldiers based on each unit's soldierCount
        formation.soldiers = new List<GameObject>();
        int totalSoldiers = 0;
        
        foreach (var unit in units)
        {
            if (unit == null) continue;
            
            // Get formation settings from unit data
            float spacing = unit.data != null ? unit.data.formationSpacing : 1.5f;
            FormationShape shape = unit.data != null ? unit.data.formationShape : FormationShape.Square;
            formation.soldierSpacing = spacing;
            
            // Spawn soldiers based on unit's soldierCount
            int soldiersToSpawn = Mathf.Max(1, unit.soldierCount); // At least 1 soldier
            int sideLength = Mathf.CeilToInt(Mathf.Sqrt(soldiersToSpawn));
            
            for (int i = 0; i < soldiersToSpawn; i++)
            {
                // Calculate position based on formation shape from unit data
                Vector3 soldierPosition;
                
                switch (shape)
                {
                    case FormationShape.Square:
                        int x = i % sideLength;
                        int z = i / sideLength;
                        soldierPosition = position + new Vector3(
                            (x - sideLength / 2f) * spacing,
                            0,
                            (z - sideLength / 2f) * spacing
                        );
                        break;
                        
                    case FormationShape.Circle:
                        float angle = (float)i / soldiersToSpawn * 2f * Mathf.PI;
                        soldierPosition = position + new Vector3(
                            Mathf.Cos(angle) * spacing,
                            0,
                            Mathf.Sin(angle) * spacing
                        );
                        break;
                        
                    case FormationShape.Wedge:
                        int row = i / 3;
                        int col = i % 3;
                        soldierPosition = position + new Vector3(
                            (col - 1) * spacing * (row + 1) * 0.5f,
                            0,
                            row * spacing
                        );
                        break;
                        
                    default:
                        // Fallback to square
                        x = i % sideLength;
                        z = i / sideLength;
                        soldierPosition = position + new Vector3(
                            (x - sideLength / 2f) * spacing,
                            0,
                            (z - sideLength / 2f) * spacing
                        );
                        break;
                }
                
                // Create soldier GameObject
                GameObject soldier = CreateCombatUnitSoldier($"Soldier_{unit.data.unitName}_{i + 1}", soldierPosition, teamColor, isAttacker, formation);
                
                if (soldier != null)
                {
                    soldier.transform.SetParent(formation.transform);
                    formation.soldiers.Add(soldier);
                    
                    // Link soldier to source unit for casualty tracking
                    var soldierCombat = soldier.GetComponent<CombatUnit>();
                    if (soldierCombat != null)
                    {
                        // Store reference to source unit
                        soldierCombat.sourceUnit = unit;
                        soldierCombat.InitializeForBattle(isAttacker);
                    }
                    
                    totalSoldiers++;
                }
            }
        }
        
        // Calculate formation stats based on source units
        int totalHealth = 0;
        int totalAttack = 0;
        int totalSoldierCount = 0;
        int totalMaxSoldierCount = 0;
        
        foreach (var unit in units)
        {
            if (unit != null)
            {
                // Scale stats by soldier count percentage
                float soldierRatio = (float)unit.soldierCount / unit.maxSoldierCount;
                totalHealth += Mathf.RoundToInt(unit.MaxHealth * soldierRatio);
                totalAttack += Mathf.RoundToInt(unit.CurrentAttack * soldierRatio);
                totalSoldierCount += unit.soldierCount;
                totalMaxSoldierCount += unit.maxSoldierCount;
            }
        }
        
        formation.totalHealth = totalHealth;
        formation.totalAttack = totalAttack;
        formation.currentHealth = totalHealth;
        formation.totalSoldierCount = totalSoldierCount;
        formation.maxSoldierCount = totalMaxSoldierCount;
        
        // Add to formations list
        allFormations.Add(formation);
        
        // Create world-space badge UI
        formation.CreateOrUpdateBadgeUI();
        
        DebugLog($"Created {formationName} with {units.Count} units, {totalSoldiers} soldiers (soldierCount: {totalSoldierCount}/{totalMaxSoldierCount})");
    }
    
    /// <summary>
    /// Set up battle camera - creates and tags MainCamera if missing
    /// </summary>
    private void SetupBattleCamera()
    {
        Camera battleCamera = Camera.main;
        if (battleCamera == null)
        {
            GameObject cameraGO = new GameObject("BattleCamera");
            battleCamera = cameraGO.AddComponent<Camera>();
            cameraGO.tag = "MainCamera"; // CRITICAL: Tag as MainCamera so Camera.main works
            DebugLog("Created and tagged BattleCamera as MainCamera");
        }

        // Position camera to overview the battlefield
        battleCamera.transform.position = new Vector3(0, 30, -20);
        battleCamera.transform.rotation = Quaternion.Euler(45, 0, 0);
        battleCamera.orthographic = false; // Use perspective mode
        battleCamera.fieldOfView = 60f; // Set field of view for perspective mode
    }
    
    /// <summary>
    /// Toggle battle pause state
    /// </summary>
    public void TogglePause()
    {
        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;
        
        DebugLog($"[BattleTestSimple] Battle {(isPaused ? "Paused" : "Resumed")}");
    }
    
    /// <summary>
    /// End the current battle
    /// </summary>
    public void EndBattle(BattleResult result)
    {
        if (!battleInProgress) return;

        battleInProgress = false;
        isPaused = false;
        Time.timeScale = 1f;
        ClearSelection();
        
        // Mark all units as no longer in battle (allow world map movement again)
        foreach (var formation in allFormations)
        {
            if (formation != null && formation.soldiers != null)
            {
                foreach (var soldier in formation.soldiers)
                {
                    if (soldier != null)
                    {
                        var combatUnit = soldier.GetComponent<CombatUnit>();
                        if (combatUnit != null)
                        {
                            combatUnit.IsInBattle = false;
                            combatUnit.isMoving = false; // Stop movement when battle ends
                        }
                    }
                }
            }
        }

        // Notify battle ended
        OnBattleEnded?.Invoke(result);
        
        // Notify ArmyManager if battle was from armies
        if (ArmyManager.Instance != null)
        {
            ArmyManager.Instance.OnBattleEnded(result);
        }

        // Optionally return to main game scene
        if (result != null)
        {
            var returnCoroutine = StartCoroutine(ReturnToMainGame(result));
            trackedCoroutines.Add(returnCoroutine);
        }
    }
    
    private IEnumerator ReturnToMainGame(BattleResult result)
    {
        DebugLog("[BattleTestSimple] Returning to main game scene...");
        
        // Load the main game scene (or Game scene if that's what the campaign map uses)
        string mainSceneName = "Game"; // Change to "MainGame" if that's your campaign scene name
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(mainSceneName);
        
        while (!asyncLoad.isDone)
        {
            yield return null;
        }
        
        DebugLog($"[BattleTestSimple] Returned to {mainSceneName} scene");
    }
    
    /// <summary>
    /// Check if battle is currently in progress
    /// </summary>
    public bool IsBattleInProgress => battleInProgress;

    /// <summary>
    /// Check if battle is currently paused
    /// </summary>
    public bool IsPaused => isPaused;

    /// <summary>
    /// Get all units of a specific civilization
    /// </summary>
    public List<CombatUnit> GetUnits(Civilization civ)
    {
        if (civ == attacker) return attackerUnits;
        if (civ == defender) return defenderUnits;
        return new List<CombatUnit>();
    }
}

// Formation Unit class - represents a group of soldiers that move together
public class FormationUnit : MonoBehaviour
{
    [Header("Formation Settings")]
    public string formationName;
    public bool isAttacker;
    public bool isPlayerControlled = true; // If false, AI controls this formation
    public Color teamColor;
    public List<GameObject> soldiers = new List<GameObject>();
    public Vector3 formationCenter;
    public float formationRadius = 3f;
    
    [Header("Movement")]
    public float moveSpeed = 3f;
    public float soldierSpacing = 3f; // Spacing between soldiers in formation (consistent for creation and movement)
    public bool isMoving = false;
    public Vector3 targetPosition;
    public bool isSelected = false;
    [Tooltip("Whether formation is currently running (double right-click) or walking (single right-click)")]
    public bool isRunning = false;
    [Tooltip("Speed multiplier when running (1.5 = 50% faster)")]
    public float runSpeedMultiplier = 1.5f;
    
    [Header("Combat")]
    public int totalHealth;
    public int totalAttack;
    public int currentHealth;
    
    [Header("Soldier Count")]
    [Tooltip("Total current soldier count across all source units")]
    public int totalSoldierCount = 0;
    [Tooltip("Maximum soldier count across all source units")]
    public int maxSoldierCount = 0;
    [Tooltip("Reference to source units (for casualty tracking)")]
    public List<CombatUnit> sourceUnits = new List<CombatUnit>();
    
    [Header("Morale")]
    public int currentMorale = 100;
    public int routingMoraleThreshold = 15;
    public bool isRouted = false;
    
    [Header("Experience")]
    public int experience = 0;
    public int experienceLevel = 1;
    private int experienceToNextLevel = 100;
    
    [Header("Charge & Flanking")]
    public float flankingBonusMultiplier = 1.3f; // 30% bonus damage from flank
    public float rearAttackBonusMultiplier = 1.5f; // 50% bonus damage from rear
    public bool isCharging = false; // Made public for access from coroutines
    private float chargeDistanceThreshold = 5f; // Distance to enemy to be considered charging
    private FormationUnit currentEnemyTarget = null;
    public bool hasAppliedChargeBonus = false; // Made public for access from coroutines - Track if charge bonus was applied this engagement
    
    // Combat state tracking
    public bool isInCombat = false; // Track if formation is actively in melee combat (public for AI access)
    private const float MELEE_RANGE = 1.5f; // Distance for melee contact (used for trigger radius)
    private const float MELEE_RANGE_SQUARED = MELEE_RANGE * MELEE_RANGE;
    
    // Collision-based contact tracking (replaces distance checks)
    // Each soldier tracks enemies in contact via trigger colliders
    public Dictionary<GameObject, HashSet<GameObject>> soldierContacts = new Dictionary<GameObject, HashSet<GameObject>>();
    
    // Individual attack cooldowns per unit (replaces synchronized attacks)
    public Dictionary<GameObject, float> soldierAttackCooldowns = new Dictionary<GameObject, float>();
    private const float BASE_ATTACK_COOLDOWN = 1.2f; // Base time between attacks per unit
    
    // Formation reformation after combat
    private bool needsReformation = false;
    private const float REFORMATION_SPEED = 5f; // Speed at which soldiers return to formation
    
    // Formation integrity system - decreases during prolonged combat, allowing looser formations
    public float formationIntegrity = 1.0f; // 1.0 = tight formation, 0.0 = completely loose (public for cross-formation access)
    private const float FORMATION_INTEGRITY_DECAY_RATE = 0.05f; // Per combat tick
    private const float FORMATION_INTEGRITY_RECOVERY_RATE = 0.1f; // Per second when not in combat
    private const float MIN_FORMATION_INTEGRITY = 0.3f; // Minimum integrity (never completely loose)
    private const float MAX_FORMATION_SPACING_MULTIPLIER = 2.5f; // Max spacing when integrity is low
    
    // Enhanced flanking - defensive penalties and morale shocks
    private const float REAR_ATTACK_DEFENSE_PENALTY = 0.5f; // 50% defense reduction when attacked from rear
    private const float FLANK_ATTACK_DEFENSE_PENALTY = 0.25f; // 25% defense reduction when flanked
    private const float REAR_ATTACK_MORALE_SHOCK = 5f; // Morale loss when attacked from rear
    private const float FLANK_ATTACK_MORALE_SHOCK = 2f; // Morale loss when flanked
    
    // Disengagement tracking - for attacks of opportunity
    private Dictionary<GameObject, float> soldierLastContactTime = new Dictionary<GameObject, float>();
    private const float DISENGAGEMENT_WINDOW = 1.0f; // Time window for disengagement penalty
    private const float DISENGAGEMENT_MORALE_PENALTY = 3f; // Morale loss for disengaging
    
    private CombatUnit[] soldierCombatUnits;
    private Renderer[] selectionRenderers;
    
    // Track active combat coroutines to prevent duplicates
    private Coroutine activeCombatCoroutine;
    private List<Coroutine> activeCoroutines = new List<Coroutine>(); // Track all active coroutines for cleanup
    private HashSet<GameObject> soldiersMarkedForDestruction = new HashSet<GameObject>(); // Track soldiers being destroyed to prevent double removal
    
    // Reusable lists for CombatDamageCoroutine (avoid allocations)
    private List<GameObject> reusableMyAliveList = new List<GameObject>();
    private List<GameObject> reusableEnAliveList = new List<GameObject>();
    private List<GameObject> reusableMyInContactList = new List<GameObject>();
    private List<GameObject> reusableEnInContactList = new List<GameObject>();
    private HashSet<GameObject> reusableEnInContactSet = new HashSet<GameObject>();
    
    // Reusable collections for AdvanceRearUnitsToFillGaps (avoid allocations)
    private List<GameObject> reusableAliveSoldiersList = new List<GameObject>();
    private Dictionary<int, List<GameObject>> reusableRowsDictionary = new Dictionary<int, List<GameObject>>();
    private List<int> reusableSortedRowsList = new List<int>();
    
    // Dictionary to map GameObjects to CombatUnit references (for fast lookup in combat)
    private Dictionary<GameObject, CombatUnit> soldierCombatUnitCache = new Dictionary<GameObject, CombatUnit>();
    
    // Throttle health and badge updates
    private bool healthUpdateDirty = false;
    private bool badgeUpdateDirty = false;
    private float lastHealthUpdateTime = 0f;
    private const float HEALTH_UPDATE_THROTTLE = 0.1f; // Update at most every 0.1 seconds

    // Per-soldier combat offsets (allows individuals to step forward during melee)
    private Dictionary<GameObject, Vector3> soldierOffsetOverrides = new Dictionary<GameObject, Vector3>();
    private Dictionary<GameObject, Coroutine> soldierOffsetCoroutines = new Dictionary<GameObject, Coroutine>();
    private Dictionary<GameObject, Coroutine> soldierFacingCoroutines = new Dictionary<GameObject, Coroutine>(); // Track facing coroutines for cleanup
    private const float SOLDIER_APPROACH_DURATION = 0.2f;
    private const float SOLDIER_COMBAT_SEPARATION = 0.8f;
    private const float MIN_UNIT_SEPARATION = 1.2f; // Minimum distance between units to prevent overlap (buffer zone)
    
    // Badge refresh timer (avoid updating UI text every frame)
    private float badgeUpdateTimer = 0f;
    private const float BADGE_UPDATE_INTERVAL = 0.5f;
    
    // Cached FindObjectsByType results to avoid expensive scene searches
    private static FormationUnit[] cachedAllFormations;
    private static float lastFormationCacheUpdate = 0f;
    private const float FORMATION_CACHE_UPDATE_INTERVAL = 0.5f; // Update cache every 0.5 seconds
    
    // Badge UI
    private Canvas badgeCanvas;
    private TMPro.TextMeshProUGUI badgeText;
    
    void Start()
    {
        // Get all soldier combat units
        RefreshSoldierArrays();
        
        // Calculate formation center
        UpdateFormationCenter();
        CreateOrUpdateBadgeUI();
        
        // Register this formation with FormationAIManager (after fully initialized)
        if (FormationAIManager.Instance != null)
        {
            FormationAIManager.Instance.RegisterFormation(this);
        }
    }
    
    void OnDestroy()
    {
        // CRITICAL: Stop ALL coroutines to prevent errors after destruction
        if (activeCombatCoroutine != null)
        {
            StopCoroutine(activeCombatCoroutine);
            activeCombatCoroutine = null;
        }
        
        // Stop all tracked coroutines
        foreach (var coroutine in activeCoroutines)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        activeCoroutines.Clear();
        
        // Stop all soldier offset coroutines
        foreach (var coroutine in soldierOffsetCoroutines.Values)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        soldierOffsetCoroutines.Clear();
        
        // Stop all soldier facing coroutines
        foreach (var coroutine in soldierFacingCoroutines.Values)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        soldierFacingCoroutines.Clear();
        
        // Clear contact tracking
        soldierContacts.Clear();
        
        // Unregister this formation when destroyed
        if (FormationAIManager.Instance != null)
        {
            FormationAIManager.Instance.UnregisterFormation(this);
        }
    }
    
    /// <summary>
    /// Refresh soldier arrays - call this when soldiers are added/removed
    /// Also caches CombatUnit references to avoid repeated GetComponent calls
    /// </summary>
    void RefreshSoldierArrays()
    {
        if (soldiers == null) return;
        
        // Remove null soldiers first (manual loop to avoid LINQ allocation)
        for (int i = soldiers.Count - 1; i >= 0; i--)
        {
            if (soldiers[i] == null)
            {
                soldiers.RemoveAt(i);
            }
        }
        
        // Clean up contact tracking and cooldowns for destroyed soldiers
        var soldiersToRemove = new List<GameObject>();
        foreach (var soldierKey in soldierContacts.Keys)
        {
            if (soldierKey == null || !soldiers.Contains(soldierKey))
            {
                soldiersToRemove.Add(soldierKey);
            }
        }
        foreach (var soldierKey in soldiersToRemove)
        {
            soldierContacts.Remove(soldierKey);
            soldierAttackCooldowns.Remove(soldierKey);
        }
        
        // Also clean up cooldowns for destroyed soldiers
        soldiersToRemove.Clear();
        foreach (var soldierKey in soldierAttackCooldowns.Keys)
        {
            if (soldierKey == null || !soldiers.Contains(soldierKey))
            {
                soldiersToRemove.Add(soldierKey);
            }
        }
        foreach (var soldierKey in soldiersToRemove)
        {
            soldierAttackCooldowns.Remove(soldierKey);
        }
        
        // Also remove destroyed soldiers from other soldiers' contact lists
        foreach (var contactList in soldierContacts.Values)
        {
            contactList.RemoveWhere(s => s == null || !soldiers.Contains(s));
        }
        
        soldierCombatUnits = new CombatUnit[soldiers.Count];
        selectionRenderers = new Renderer[soldiers.Count];
        
        soldierCombatUnitCache.Clear(); // Clear cache when refreshing
        for (int i = 0; i < soldiers.Count; i++)
        {
            if (soldiers[i] != null)
            {
                // Cache CombatUnit reference to avoid repeated GetComponent calls
                var combatUnit = soldiers[i].GetComponent<CombatUnit>();
                soldierCombatUnits[i] = combatUnit;
                selectionRenderers[i] = soldiers[i].GetComponent<Renderer>();
                
                // Also cache in dictionary for fast lookup by GameObject
                if (combatUnit != null)
                {
                    soldierCombatUnitCache[soldiers[i]] = combatUnit;
                }
            }
        }
    }

    private Vector3 GetDefaultSoldierWorldPosition(GameObject soldier)
    {
        int index = soldiers.IndexOf(soldier);
        if (index == -1) return soldier != null ? soldier.transform.position : formationCenter;
        return formationCenter + GetFormationOffset(index);
    }

    public void SetSoldierOffsetToWorld(GameObject soldier, Vector3 targetWorldPosition, float duration)
    {
        if (soldier == null) return;
        int index = soldiers.IndexOf(soldier);
        if (index == -1) return;
        Vector3 defaultPos = Ground(GetDefaultSoldierWorldPosition(soldier));
        Vector3 targetOffset = targetWorldPosition - defaultPos;
        StartSoldierOffsetAnimation(soldier, targetOffset, duration);
    }

    private void StartSoldierOffsetAnimation(GameObject soldier, Vector3 targetOffset, float duration)
    {
        if (soldier == null) return;
        if (soldierOffsetCoroutines.TryGetValue(soldier, out var existing) && existing != null)
        {
            StopCoroutine(existing);
            soldierOffsetCoroutines.Remove(soldier);
        }
        var routine = StartCoroutine(AnimateSoldierOffset(soldier, targetOffset, duration));
        soldierOffsetCoroutines[soldier] = routine;
    }

    private IEnumerator AnimateSoldierOffset(GameObject soldier, Vector3 targetOffset, float duration)
    {
        Vector3 startOffset = soldierOffsetOverrides.TryGetValue(soldier, out var current) ? current : Vector3.zero;
        float elapsed = 0f;
        duration = Mathf.Max(0.001f, duration);

        while (elapsed < duration)
        {
            if (soldier == null)
            {
                soldierOffsetOverrides.Remove(soldier);
                soldierOffsetCoroutines.Remove(soldier);
                yield break;
            }
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector3 newOffset = Vector3.Lerp(startOffset, targetOffset, t);
            soldierOffsetOverrides[soldier] = newOffset;
            yield return null;
        }

        if (targetOffset == Vector3.zero)
        {
            soldierOffsetOverrides.Remove(soldier);
        }
        else
        {
            soldierOffsetOverrides[soldier] = targetOffset;
        }
        soldierOffsetCoroutines.Remove(soldier);
    }

    private void ClearSoldierOffsetImmediate(GameObject soldier)
    {
        if (soldier == null) return;
        if (soldierOffsetCoroutines.TryGetValue(soldier, out var existing) && existing != null)
        {
            StopCoroutine(existing);
        }
        soldierOffsetCoroutines.Remove(soldier);
        soldierOffsetOverrides.Remove(soldier);
    }

    private void ResetSoldierOffsets(float duration = 0.25f)
    {
        foreach (var soldier in soldiers)
        {
            if (soldier != null)
            {
                StartSoldierOffsetAnimation(soldier, Vector3.zero, duration);
            }
        }
    }
    
    private bool wasMovingLastFrame = false;
    
    void Update()
    {
        if (isMoving)
        {
            // Ensure walking animations are playing while moving
            MoveFormation();
            
            // Only play walking animations when starting to move OR periodically to ensure they stay active
            // Don't call every frame - just ensure IsWalking is set to true
            if (!walkingAnimationsInitialized || Time.frameCount % 60 == 0) // Every 60 frames (~1 second)
            {
                // Debug.Log removed for performance
            PlayWalkingAnimations();
            }
            else
            {
                // Just ensure IsWalking stays true without resetting triggers
                EnsureWalkingAnimationActive();
            }
            
            wasMovingLastFrame = true;
        }
        else
        {
            // Only play idle animations when transitioning from moving to idle
            if (wasMovingLastFrame)
            {
                // Debug.Log removed for performance
                PlayIdleAnimations();
                wasMovingLastFrame = false;
            }
        }

        // Refresh badge text periodically (not every frame) to keep UI responsive
        badgeUpdateTimer += Time.deltaTime;
        if (badgeUpdateTimer >= BADGE_UPDATE_INTERVAL)
        {
            badgeUpdateTimer = 0f;
            if (badgeUpdateDirty)
            {
                UpdateBadgeContents();
                badgeUpdateDirty = false;
            }
        }
        
        // Also check throttled health/badge updates
        if (Time.time - lastHealthUpdateTime >= HEALTH_UPDATE_THROTTLE)
        {
            if (healthUpdateDirty)
            {
                UpdateFormationHealthFromSoldiers();
                healthUpdateDirty = false;
            }
            if (badgeUpdateDirty)
            {
                UpdateBadgeContents();
                badgeUpdateDirty = false;
            }
            lastHealthUpdateTime = Time.time;
        }
        
        // Handle formation reformation after combat (gradually return soldiers to formation grid)
        if (needsReformation && !isInCombat && !isMoving)
        {
            // Reformation is handled by coroutine, but we can also update positions here
            // The coroutine will set needsReformation = false when complete
        }
        
        // Recover formation integrity when not in combat
        if (!isInCombat && formationIntegrity < 1.0f)
        {
            formationIntegrity = Mathf.Min(1.0f, formationIntegrity + FORMATION_INTEGRITY_RECOVERY_RATE * Time.deltaTime);
        }
    }
    
    /// <summary>
    /// Find which formation a soldier belongs to (helper for contact detection)
    /// </summary>
    public FormationUnit FindFormationForSoldier(GameObject soldier)
    {
        if (soldier == null) return null;
        
        // Check if this soldier belongs to this formation
        if (soldiers != null && soldiers.Contains(soldier))
        {
            return this;
        }
        
        // Check other formations (use cached formations to avoid expensive search)
        UpdateFormationCacheIfNeeded();
        if (cachedAllFormations != null)
        {
            foreach (var formation in cachedAllFormations)
            {
                if (formation != null && formation != this && formation.soldiers != null && formation.soldiers.Contains(soldier))
                {
                    return formation;
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Add enemy contact for a soldier (called by FormationSoldierContactDetector)
    /// </summary>
    public void AddSoldierContact(GameObject soldier, GameObject enemy)
    {
        if (soldier == null || enemy == null) return;
        
        if (!soldierContacts.ContainsKey(soldier))
        {
            soldierContacts[soldier] = new HashSet<GameObject>();
        }
        
        soldierContacts[soldier].Add(enemy);
    }
    
    /// <summary>
    /// Remove enemy contact for a soldier (called by FormationSoldierContactDetector)
    /// </summary>
    public void RemoveSoldierContact(GameObject soldier, GameObject enemy)
    {
        if (soldier == null || enemy == null) return;
        
        if (soldierContacts.TryGetValue(soldier, out var contacts))
        {
            contacts.Remove(enemy);
            if (contacts.Count == 0)
            {
                soldierContacts.Remove(soldier);
            }
        }
    }
    
    /// <summary>
    /// Reform formation after combat ends - gradually return soldiers to formation grid
    /// </summary>
    System.Collections.IEnumerator ReformFormationAfterCombat()
    {
        // Wait a moment before starting reformation (let combat fully end)
        yield return new WaitForSeconds(0.5f);
        
        // Gradually reform over 2 seconds
        float reformationDuration = 2f;
        float elapsed = 0f;
        
        while (elapsed < reformationDuration && !isInCombat && !isMoving)
        {
            elapsed += Time.deltaTime;
            
            // Gradually move soldiers back to formation positions
            // UpdateSoldierPositions will handle the interpolation smoothly
            UpdateSoldierPositions();
            
            yield return null;
        }
        
        // Ensure final formation positions (full enforcement)
        UpdateSoldierPositions();
        
        // Clear any remaining combat offsets
        ResetSoldierOffsets(0.5f);
        
        // Restore formation integrity after reformation
        formationIntegrity = 1.0f;
        
        // Reformation complete
        needsReformation = false;
    }
    
    /// <summary>
    /// Ensure walking animation stays active without resetting triggers
    /// </summary>
    void EnsureWalkingAnimationActive()
    {
        if (soldierCombatUnits == null)
        {
            Debug.LogWarning($"[FormationUnit] {formationName}: EnsureWalkingAnimationActive called but soldierCombatUnits is null");
            return;
        }
        
        int count = 0;
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Just ensure isMoving is true - the property setter will automatically update IsWalking animator parameter
                // Setting it again is safe even if already true (property setter checks for changes)
                bool wasMoving = combatUnit.isMoving;
                combatUnit.isMoving = true;
                count++;
                
                // Check if animator is actually in walk state (only log if not transitioning)
                // Use GetComponentInChildren to find Animator on child objects (matches CombatUnit behavior)
                var animator = combatUnit.GetComponentInChildren<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                    bool isInWalkState = stateInfo.IsName("Walk") || stateInfo.IsName("Walking");
                    bool isWalkingParam = animator.GetBool("IsWalking");
                    bool isTransitioning = animator.IsInTransition(0);
                    
                    // Only warn if not in walk state AND not transitioning (transitioning is expected and fine)
                    // Debug removed - animation is working correctly, this was a false positive
                    // if (!isInWalkState && isWalkingParam && !isTransitioning)
                    // {
                    //     Debug.LogWarning($"[FormationUnit] {formationName}: {combatUnit.gameObject.name} - IsWalking={isWalkingParam} but NOT in Walk state! Current state: {GetStateName(animator)}, normalizedTime: {stateInfo.normalizedTime:F2}");
                    // }
                }
            }
        }
        
        if (count > 0 && Time.frameCount % 60 == 0) // Only log periodically
        {
            // Debug.Log removed for performance
        }
    }
    
    /// <summary>
    /// Helper to get animator state name for debugging
    /// </summary>
    private string GetStateName(Animator anim)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return "No Animator";
        
        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        if (anim.layerCount > 0)
        {
            AnimatorClipInfo[] clipInfo = anim.GetCurrentAnimatorClipInfo(0);
            if (clipInfo != null && clipInfo.Length > 0)
            {
                return clipInfo[0].clip.name;
            }
        }
        
        // Check common states
        if (stateInfo.IsName("Idle") || stateInfo.IsName("idle")) return "Idle";
        if (stateInfo.IsName("Walk") || stateInfo.IsName("Walking")) return "Walk";
        if (stateInfo.IsName("Attack")) return "Attack";
        if (stateInfo.IsName("Hit")) return "Hit";
        if (stateInfo.IsName("Death")) return "Death";
        
        return $"Unknown (normalizedTime: {stateInfo.normalizedTime:F2})";
    }
    
    public void MoveToPosition(Vector3 position, bool running = false)
    {
        // Clamp target position to battlefield bounds to prevent off-map movement
        targetPosition = ClampFormationToBattlefieldBounds(position);
        isMoving = true;
        isRunning = running;
        PlayWalkingAnimations();
        
        // Ensure formation center doesn't jump - soldiers will smoothly move toward new destination
        // The formation center will move smoothly in MoveFormation() via interpolation
    }
    
    void MoveFormation()
    {
        Vector3 direction = (targetPosition - formationCenter).normalized;
        float distance = Vector3.Distance(formationCenter, targetPosition);
        
        if (distance > 0.5f)
        {
            // If routed, invert direction to move away
            if (isRouted) direction = -direction;
            
            // Rotate formation to face movement direction
            if (direction.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
            }
            
            // Calculate formation movement speed (slowest unit - all soldiers move together)
            float formationSpeed = CalculateFormationMoveSpeed();
            
            // Apply running speed multiplier if running
            if (isRunning)
            {
                formationSpeed *= runSpeedMultiplier;
            }
            
            // Move formation center
            Vector3 newPosition = formationCenter + direction * formationSpeed * Time.deltaTime;
            
            // Clamp to battlefield bounds (prevents routed formations from leaving the map)
            newPosition = ClampFormationToBattlefieldBounds(newPosition);
            
            formationCenter = newPosition;
            
            // Keep center on ground
            formationCenter = Ground(formationCenter);
            
            // Update soldier positions
            UpdateSoldierPositions();
            
            // Don't call PlayWalkingAnimations here - Update() handles it
            // This prevents calling it multiple times per frame
            
            // Check for enemies in range
            if (CheckForEnemies())
            {
                StopMoving();
                PlayFightingAnimations();
            }
        }
        else
        {
            StopMoving();
        }
    }
    
    /// <summary>
    /// Calculate formation movement speed - all soldiers move at the same speed (slowest unit's speed)
    /// This ensures the formation stays together
    /// </summary>
    private float CalculateFormationMoveSpeed()
    {
        if (soldiers == null || soldiers.Count == 0) return moveSpeed; // Fallback to base speed
        
        float slowestSpeed = float.MaxValue;
        bool foundAny = false;
        
        foreach (var soldier in soldiers)
        {
            if (soldier == null) continue;
            var combatUnit = soldier.GetComponent<CombatUnit>();
            if (combatUnit != null)
            {
                float effectiveSpeed = combatUnit.EffectiveMoveSpeed;
                if (effectiveSpeed < slowestSpeed)
                {
                    slowestSpeed = effectiveSpeed;
                    foundAny = true;
                }
            }
        }
        
        // Return slowest speed so all soldiers move together, or base speed if none found
        return foundAny ? slowestSpeed : moveSpeed;
    }
    
    void UpdateSoldierPositions()
    {
        // During combat, allow soldiers more freedom - don't force them into rigid formation grid
        // This prevents teleporting and allows natural melee behavior
        // Exception: During reformation, gradually move soldiers back to formation
        if (isInCombat && !needsReformation)
        {
            // Apply formation integrity - lower integrity = looser formation
            // Soldiers can drift further from formation positions based on integrity
            for (int i = 0; i < soldiers.Count; i++)
            {
                if (soldiers[i] != null)
                {
                    Vector3 offset = GetFormationOffset(i);
                    Vector3 desired = formationCenter + offset;
                    if (soldierOffsetOverrides.TryGetValue(soldiers[i], out var extraOffset)) { desired += extraOffset; }
                    Vector3 currentPos = soldiers[i].transform.position;
                    Vector3 targetPos = Ground(desired);
                    targetPos = ClampFormationToBattlefieldBounds(targetPos);
                    
                    // Blend between current position and formation position based on integrity
                    // Low integrity = stay closer to current position (loose formation)
                    // High integrity = move toward formation position (tight formation)
                    Vector3 blendedTarget = Vector3.Lerp(currentPos, targetPos, formationIntegrity);
                    
                    float moveSpeed = 10f * formationIntegrity; // Slower movement when loose
                    Vector3 newPos = Vector3.MoveTowards(currentPos, blendedTarget, moveSpeed * Time.deltaTime);
                    
                    // Apply separation to prevent overlap with nearby units (using UnitSeparation component)
                    var unitSeparation = soldiers[i].GetComponent<UnitSeparation>();
                    if (unitSeparation != null)
                    {
                        newPos = unitSeparation.ApplySeparation(newPos);
                    }
                    
                    newPos = ClampFormationToBattlefieldBounds(newPos);
                    soldiers[i].transform.position = newPos;
                }
            }
            UpdateBadgePosition();
            return;
        }
        
        // Arrange soldiers in formation around the center (only when not in combat)
        // Use interpolation for smooth movement instead of instant teleporting
        for (int i = 0; i < soldiers.Count; i++)
        {
            if (soldiers[i] != null)
            {
                Vector3 offset = GetFormationOffset(i);
                Vector3 desired = formationCenter + offset;
                if (soldierOffsetOverrides.TryGetValue(soldiers[i], out var extraOffset))
                {
                    desired += extraOffset;
                }
                
                // Interpolate position instead of directly setting it (prevents teleporting)
                Vector3 currentPos = soldiers[i].transform.position;
                Vector3 targetPos = Ground(desired);
                // Clamp target position to battlefield bounds
                targetPos = ClampFormationToBattlefieldBounds(targetPos);
                float moveSpeed = 10f; // Speed of interpolation
                Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, moveSpeed * Time.deltaTime);
                
                // Apply separation to prevent overlap with nearby units (using UnitSeparation component)
                var unitSeparation = soldiers[i].GetComponent<UnitSeparation>();
                if (unitSeparation != null)
                {
                    newPos = unitSeparation.ApplySeparation(newPos);
                }
                
                // Clamp new position to ensure soldiers don't leave the map during movement
                newPos = ClampFormationToBattlefieldBounds(newPos);
                soldiers[i].transform.position = newPos;
            }
        }
        UpdateBadgePosition();
    }
    
    Vector3 GetFormationOffset(int soldierIndex)
    {
        // Simple square formation
        int sideLength = Mathf.CeilToInt(Mathf.Sqrt(soldiers.Count));
        int x = soldierIndex % sideLength;
        int z = soldierIndex / sideLength;
        
        // Get spacing from first soldier's CombatUnitData (all soldiers in formation should have same spacing)
        float spacing = soldierSpacing; // Default to stored spacing
        if (soldiers.Count > 0 && soldiers[0] != null)
        {
            var combatUnit = soldiers[0].GetComponent<CombatUnit>();
            if (combatUnit != null && combatUnit.data != null)
            {
                spacing = combatUnit.data.formationSpacing; // Use spacing from CombatUnitData
            }
        }
        
        return new Vector3(
            (x - sideLength / 2f) * spacing,
            0,
            (z - sideLength / 2f) * spacing
        );
    }
    
    void UpdateFormationCenter()
    {
        if (soldiers == null || soldiers.Count == 0)
        {
            return;
        }
        
        // Don't update formation center from soldier positions when moving toward a target
        // This prevents teleportation - the center should move smoothly via MoveFormation()
        // Only update center from soldiers when not actively moving (e.g., during combat or idle)
        if (isMoving && !isInCombat)
        {
            return; // Let MoveFormation() handle center movement smoothly
        }
        
        Vector3 sum = Vector3.zero;
        int validCount = 0;
        foreach (var soldier in soldiers)
        {
            if (soldier != null)
            {
                sum += soldier.transform.position;
                validCount++;
            }
        }
        
        // Prevent division by zero
        if (validCount > 0)
        {
            // Smoothly interpolate center instead of directly setting it to prevent teleportation
            Vector3 calculatedCenter = sum / validCount;
            calculatedCenter = ClampFormationToBattlefieldBounds(calculatedCenter);
            
            // Only update if not moving (during combat or idle, smooth updates are fine)
            if (!isMoving)
            {
                formationCenter = Vector3.Lerp(formationCenter, calculatedCenter, Time.deltaTime * 2f);
            }
        }
    }
    
    // Cache enemy formations to avoid expensive FindObjectsByType calls
    private List<FormationUnit> cachedEnemyFormations = new List<FormationUnit>();
    private float lastEnemyFormationUpdate = 0f;
    private const float ENEMY_FORMATION_UPDATE_INTERVAL = 0.5f; // Update every 0.5 seconds
    
    bool CheckForEnemies()
    {
        // Combat is now initiated by trigger-based contact detection (FormationSoldierContactDetector)
        // This method checks if we should stop formation movement
        
        // CRITICAL FIX: Don't stop formation movement just because one soldier made contact!
        // Allow formation to continue moving so ALL soldiers can engage, not just the first one
        // Only stop formation movement when most/all soldiers are in contact
        
        if (soldierContacts == null || soldiers == null) return false;
        
        // Count how many soldiers are in contact
        int soldiersInContact = 0;
        int totalAliveSoldiers = 0;
        
        foreach (var soldier in soldiers)
        {
            if (soldier == null || soldiersMarkedForDestruction.Contains(soldier)) continue;
            totalAliveSoldiers++;
            
            if (soldierContacts.TryGetValue(soldier, out var contacts) && contacts != null && contacts.Count > 0)
            {
                soldiersInContact++;
            }
        }
        
        // Only stop formation movement if most soldiers (75%+) are already in contact
        // This allows the formation to continue moving so remaining soldiers can engage
        if (totalAliveSoldiers > 0)
        {
            float contactRatio = (float)soldiersInContact / totalAliveSoldiers;
            if (contactRatio >= 0.75f)
            {
                // Most soldiers are in contact - formation can stop moving
                // Individual soldiers will still move toward their targets in CombatDamageCoroutine
                return true;
            }
        }
        
        // Not enough soldiers in contact yet - continue moving so more can engage
        return false;
    }
    
    void UpdateCachedEnemyFormations()
    {
        cachedEnemyFormations.Clear();
        
        // Use cached formations array to avoid expensive FindObjectsByType call
        UpdateFormationCacheIfNeeded();
        
        if (cachedAllFormations != null)
        {
            foreach (var formation in cachedAllFormations)
        {
            if (formation != null && formation.isAttacker != this.isAttacker)
            {
                cachedEnemyFormations.Add(formation);
            }
            }
        }
    }
    
    void UpdateFormationCacheIfNeeded()
    {
        if (Time.time - lastFormationCacheUpdate > FORMATION_CACHE_UPDATE_INTERVAL)
        {
            cachedAllFormations = FindObjectsByType<FormationUnit>(FindObjectsSortMode.None);
            lastFormationCacheUpdate = Time.time;
        }
    }
    
    public void StartCombatWithFormation(FormationUnit enemyFormation)
    {
        // Prevent starting multiple combat coroutines for the same formation
        if (activeCombatCoroutine != null)
        {
            Debug.Log($"[Combat] {formationName} already in combat, skipping");
            return; // Already in combat
        }
        
        if (enemyFormation == null)
        {
            Debug.LogWarning($"[Combat] {formationName} tried to start combat with null enemy formation");
            return;
        }
        
        Debug.Log($"[Combat] Starting combat: {formationName} vs {enemyFormation.formationName}");
        
        currentEnemyTarget = enemyFormation;
        
        // Mark both formations as in combat (relaxes formation enforcement)
        isInCombat = true;
        enemyFormation.isInCombat = true;
        
        // Check if we're charging (moving toward enemy and close enough)
        UpdateChargeState(enemyFormation);
        
        // Reset charge bonus flag for new engagement
        hasAppliedChargeBonus = false;
        
        // Both formations are now in combat
        PlayFightingAnimations();
        enemyFormation.PlayFightingAnimations();
        
        // Start combat damage over time
        activeCombatCoroutine = StartCoroutine(CombatDamageCoroutine(enemyFormation));
        Debug.Log($"[Combat] Started CombatDamageCoroutine for {formationName}");
    }
    
    /// <summary>
    /// Update whether this formation is charging toward the enemy
    /// </summary>
    void UpdateChargeState(FormationUnit enemyFormation)
    {
        if (enemyFormation == null) return;
        
        float distanceToEnemy = Vector3.Distance(formationCenter, enemyFormation.formationCenter);
        
        // Check if we're moving toward enemy and within charge distance
        if (isMoving && distanceToEnemy <= chargeDistanceThreshold)
        {
            Vector3 directionToEnemy = (enemyFormation.formationCenter - formationCenter).normalized;
            Vector3 movementDirection = (targetPosition - formationCenter).normalized;
            
            // Check if we're moving toward the enemy (dot product > 0.5 means roughly same direction)
            float dotProduct = Vector3.Dot(directionToEnemy, movementDirection);
            isCharging = dotProduct > 0.5f;
        }
        else
        {
            isCharging = false;
        }
    }
    
    /// <summary>
    /// Calculate attack angle relative to enemy formation facing direction
    /// Returns: 0 = front, 90 = flank, 180 = rear
    /// </summary>
    float CalculateAttackAngle(FormationUnit attacker, FormationUnit defender)
    {
        if (attacker == null || defender == null) return 0f;
        
        // Calculate direction from defender to attacker
        Vector3 directionToAttacker = (attacker.formationCenter - defender.formationCenter).normalized;
        
        // Get defender's actual facing direction from transform rotation
        Vector3 defenderFacing = defender.transform.forward;
        
        // If defender has no facing (zero vector), use direction to attacker as fallback
        if (defenderFacing.sqrMagnitude < 0.01f)
        {
            defenderFacing = directionToAttacker;
        }
        
        // Calculate angle between defender's facing and attack direction
        float angle = Vector3.Angle(defenderFacing, directionToAttacker);
        
        return angle;
    }
    
    /// <summary>
    /// Determine if attack is from flank or rear and return bonus multiplier
    /// </summary>
    float GetFlankingBonus(FormationUnit attacker, FormationUnit defender)
    {
        float attackAngle = CalculateAttackAngle(attacker, defender);
        
        // Rear attack: 135-180 degrees
        if (attackAngle >= 135f)
        {
            // Debug.Log removed for performance
            return rearAttackBonusMultiplier;
        }
        // Flank attack: 45-135 degrees
        else if (attackAngle >= 45f)
        {
            // Debug.Log removed for performance
            return flankingBonusMultiplier;
        }
        // Front attack: 0-45 degrees
        else
        {
            return 1.0f; // No bonus
        }
    }
    
    /// <summary>
    /// Gain experience from combat actions
    /// </summary>
    void GainExperience(int amount, string reason = "")
    {
        experience += amount;
        // Debug.Log removed for performance
        
        // Check for level up
        while (experience >= experienceToNextLevel)
        {
            experience -= experienceToNextLevel;
            experienceLevel++;
            experienceToNextLevel = Mathf.RoundToInt(experienceToNextLevel * 1.5f); // Exponential growth
            
            // Apply level up bonuses
            totalAttack = Mathf.RoundToInt(totalAttack * 1.1f); // 10% attack increase
            totalHealth = Mathf.RoundToInt(totalHealth * 1.1f); // 10% health increase
            currentHealth = Mathf.Min(currentHealth, totalHealth); // Heal up to new max
            
            // Debug.Log removed for performance
        }

        badgeUpdateDirty = true;
    }
    
    System.Collections.IEnumerator CombatDamageCoroutine(FormationUnit enemyFormation)
    {
        var tick = new WaitForSeconds(0.6f);
        
        Debug.Log($"[CombatDamageCoroutine] Started for {formationName} vs {enemyFormation.formationName}");
        
        // COMBAT CONTINUATION: Use per-unit contact instead of formation-center distance
        // Combat continues as long as ANY soldiers are in melee range
        while (enemyFormation != null)
        {
            yield return tick;
            
            // Safety checks
            if (this == null || enemyFormation == null)
            {
                Debug.Log($"[CombatDamageCoroutine] Formation destroyed, ending combat");
                break;
            }
            if (soldiers == null || enemyFormation.soldiers == null)
            {
                Debug.Log($"[CombatDamageCoroutine] No soldiers, ending combat");
                break;
            }
            
            // Decrease formation integrity during prolonged combat (formation loosens up)
            formationIntegrity = Mathf.Max(MIN_FORMATION_INTEGRITY, formationIntegrity - FORMATION_INTEGRITY_DECAY_RATE);
            enemyFormation.formationIntegrity = Mathf.Max(MIN_FORMATION_INTEGRITY, enemyFormation.formationIntegrity - FORMATION_INTEGRITY_DECAY_RATE);
            
            // Track contact times for disengagement detection
            foreach (var soldier in reusableMyAliveList)
            {
                if (soldier != null && soldierContacts.ContainsKey(soldier) && soldierContacts[soldier].Count > 0)
                {
                    soldierLastContactTime[soldier] = Time.time;
                }
            }
            
            // Check for disengagements (soldiers that were in contact but no longer are)
            var disengagingSoldiers = new List<GameObject>();
            foreach (var kvp in soldierLastContactTime)
            {
                if (kvp.Key == null) continue;
                bool stillInContact = soldierContacts.ContainsKey(kvp.Key) && soldierContacts[kvp.Key].Count > 0;
                if (!stillInContact && Time.time - kvp.Value < DISENGAGEMENT_WINDOW)
                {
                    // Recently disengaged - apply penalty
                    disengagingSoldiers.Add(kvp.Key);
                }
            }
            foreach (var soldier in disengagingSoldiers)
            {
                // Apply disengagement morale penalty
                currentMorale = Mathf.Max(0, (int)(currentMorale - DISENGAGEMENT_MORALE_PENALTY));
                
                // Attack of opportunity: enemies get a free attack on disengaging unit
                if (soldier != null && enemyFormation != null)
                {
                    // Find nearest enemy soldier to the disengaging unit
                    GameObject nearestEnemy = null;
                    float nearestDistance = float.MaxValue;
                    
                    foreach (var enemySoldier in reusableEnAliveList)
                    {
                        if (enemySoldier == null) continue;
                        float dist = Vector3.Distance(soldier.transform.position, enemySoldier.transform.position);
                        if (dist < nearestDistance && dist < MELEE_RANGE * 2f) // Within extended range for opportunity attack
                        {
                            nearestDistance = dist;
                            nearestEnemy = enemySoldier;
                        }
                    }
                    
                    // If enemy is in range, perform attack of opportunity
                    if (nearestEnemy != null)
                    {
                        var disengagingCU = soldier.GetComponent<CombatUnit>();
                        var enemyCU = nearestEnemy.GetComponent<CombatUnit>();
                        
                        if (disengagingCU != null && enemyCU != null)
                        {
                            // Free attack with bonus damage (disengaging unit is vulnerable)
                            int opportunityDamage = Mathf.Max(1, Mathf.RoundToInt(enemyCU.CurrentAttack * 1.5f)); // 50% bonus
                            bool killed = disengagingCU.ApplyDamage(opportunityDamage, enemyCU, false);
                            
                            if (killed)
                            {
                                HandleSoldierDeath(this, soldier);
                            }
                        }
                    }
                }
                
                soldierLastContactTime.Remove(soldier);
            }
            
            // Build alive lists (reuse lists to avoid allocations)
            reusableMyAliveList.Clear();
            reusableEnAliveList.Clear();
            foreach (var s in soldiers) if (s != null && !soldiersMarkedForDestruction.Contains(s)) reusableMyAliveList.Add(s);
            foreach (var s in enemyFormation.soldiers) if (s != null && !enemyFormation.soldiersMarkedForDestruction.Contains(s)) reusableEnAliveList.Add(s);
            if (reusableMyAliveList.Count == 0 || reusableEnAliveList.Count == 0) break;

            // COLLISION-BASED CONTACT DETECTION: Use trigger colliders instead of distance checks
            // This handles multiple units at same distance naturally - Unity's physics handles it
            reusableMyInContactList.Clear();
            reusableEnInContactList.Clear();
            reusableEnInContactSet.Clear(); // For O(1) lookup
            
            // Get contacts from collision system (populated by FormationSoldierContactDetector)
            int totalContacts = 0;
            foreach (var mySoldier in reusableMyAliveList)
            {
                if (mySoldier == null) continue;
                
                // Get enemies in contact with this soldier (from trigger colliders)
                if (soldierContacts.TryGetValue(mySoldier, out var contacts) && contacts.Count > 0)
                {
                    totalContacts += contacts.Count;
                    
                    // Find nearest enemy from contacts (multiple enemies could be in range)
                GameObject nearestEnemy = null;
                float nearestDistanceSquared = float.MaxValue;
                
                    foreach (var enemy in contacts)
                    {
                        if (enemy == null) continue;
                        if (!reusableEnAliveList.Contains(enemy)) continue; // Enemy is dead
                        if (reusableEnInContactSet.Contains(enemy)) continue; // Already paired
                    
                        // Find which formation this enemy belongs to
                        FormationUnit enemySoldierFormation = FindFormationForSoldier(enemy);
                        if (enemySoldierFormation == null || enemySoldierFormation != enemyFormation) continue;
                        
                        // Check if it's actually an enemy
                        if (enemyFormation.isAttacker == this.isAttacker) continue; // Same team
                        
                        // Use distance to pick nearest if multiple enemies in contact
                        Vector3 diff = mySoldier.transform.position - enemy.transform.position;
                        float distanceSquared = diff.sqrMagnitude;
                        
                        if (distanceSquared < nearestDistanceSquared)
                    {
                        nearestDistanceSquared = distanceSquared;
                            nearestEnemy = enemy;
                    }
                }
                
                // If found contact, add both to lists
                if (nearestEnemy != null)
                {
                    reusableMyInContactList.Add(mySoldier);
                    reusableEnInContactList.Add(nearestEnemy);
                    reusableEnInContactSet.Add(nearestEnemy); // Mark as paired
                }
            }
            }
            
            Debug.Log($"[CombatDamageCoroutine] {formationName}: Found {totalContacts} total contacts, {reusableMyInContactList.Count} paired contacts");
            
            // Debug: Log soldier contact dictionary state and check distances
            if (totalContacts == 0 && reusableMyAliveList.Count > 0 && reusableEnAliveList.Count > 0)
            {
                Debug.LogWarning($"[CombatDamageCoroutine] {formationName}: No contacts found! Checking distances and trigger setup...");
                Debug.LogWarning($"[CombatDamageCoroutine] {formationName}: soldierContacts has {soldierContacts.Count} entries");
                
                // Check distances between nearest soldiers
                if (reusableMyAliveList.Count > 0 && reusableEnAliveList.Count > 0)
                {
                    var mySoldier = reusableMyAliveList[0];
                    var enSoldier = reusableEnAliveList[0];
                    if (mySoldier != null && enSoldier != null)
                    {
                        float distance = Vector3.Distance(mySoldier.transform.position, enSoldier.transform.position);
                        Debug.LogWarning($"[CombatDamageCoroutine] {formationName}: Distance between {mySoldier.name} and {enSoldier.name}: {distance:F2} units");
                        
                        // Check trigger collider setup
                        var myTrigger = mySoldier.GetComponent<Collider>();
                        var enTrigger = enSoldier.GetComponent<Collider>();
                        if (myTrigger != null && myTrigger.isTrigger)
                        {
                            float myRadius = myTrigger is SphereCollider sphere ? sphere.radius : (myTrigger is CapsuleCollider capsule ? capsule.radius : 0f);
                            Debug.LogWarning($"[CombatDamageCoroutine] {formationName}: {mySoldier.name} trigger: {myTrigger.GetType().Name}, radius={myRadius}, enabled={myTrigger.enabled}");
                        }
                        if (enTrigger != null && enTrigger.isTrigger)
                        {
                            float enRadius = enTrigger is SphereCollider sphere ? sphere.radius : (enTrigger is CapsuleCollider capsule ? capsule.radius : 0f);
                            Debug.LogWarning($"[CombatDamageCoroutine] {formationName}: {enSoldier.name} trigger: {enTrigger.GetType().Name}, radius={enRadius}, enabled={enTrigger.enabled}");
                        }
                        
                        // Check if contact detector exists
                        var myDetector = mySoldier.GetComponent<FormationSoldierContactDetector>();
                        var enDetector = enSoldier.GetComponent<FormationSoldierContactDetector>();
                        Debug.LogWarning($"[CombatDamageCoroutine] {formationName}: {mySoldier.name} has detector: {myDetector != null}, {enSoldier.name} has detector: {enDetector != null}");
                    }
                }
                
                foreach (var kvp in soldierContacts)
                {
                    if (kvp.Key != null && kvp.Value != null)
                    {
                        Debug.LogWarning($"[CombatDamageCoroutine] {formationName}: {kvp.Key.name} has {kvp.Value.Count} contacts");
                    }
                }
                Debug.LogWarning($"[CombatDamageCoroutine] {formationName}: Alive soldiers: {reusableMyAliveList.Count}, Enemy alive: {reusableEnAliveList.Count}");
            }
            
            // COMBAT CONTINUATION CHECK: If no soldiers are in contact, combat ends
            if (reusableMyInContactList.Count == 0)
            {
                // No more units in melee range - combat has ended
                isInCombat = false;
                enemyFormation.isInCombat = false;
                needsReformation = true; // Mark for reformation after combat
                enemyFormation.needsReformation = true;
                break;
            }
            
            // Process ALL soldiers in contact independently - each can attack their own target
            // This allows multiple soldiers to attack simultaneously, not just one pair
            Debug.Log($"[CombatDamageCoroutine] {formationName}: Processing {reusableMyInContactList.Count} soldiers in contact");
            
            // Process each of our soldiers that has an enemy in contact
            for (int i = 0; i < reusableMyInContactList.Count; i++)
            {
                var mySoldier = reusableMyInContactList[i];
                var enemySoldier = reusableEnInContactList[i];
                if (mySoldier == null || enemySoldier == null) continue;

                // Use cached CombatUnit references
                if (!soldierCombatUnitCache.TryGetValue(mySoldier, out var myCU))
                {
                    myCU = mySoldier.GetComponent<CombatUnit>();
                    if (myCU != null) soldierCombatUnitCache[mySoldier] = myCU;
                }
                if (!enemyFormation.soldierCombatUnitCache.TryGetValue(enemySoldier, out var enemyCU))
                {
                    enemyCU = enemySoldier.GetComponent<CombatUnit>();
                    if (enemyCU != null) enemyFormation.soldierCombatUnitCache[enemySoldier] = enemyCU;
                }
                if (myCU == null || enemyCU == null) continue;

                // CRITICAL: Make soldiers face their target continuously during combat
                // Start facing coroutine if not already running (will be cleaned up when combat ends)
                if (!soldierFacingCoroutines.ContainsKey(mySoldier))
                {
                    var faceCoroutine = StartCoroutine(FaceTargetContinuously(mySoldier, enemySoldier));
                    soldierFacingCoroutines[mySoldier] = faceCoroutine;
                    activeCoroutines.Add(faceCoroutine);
                }

                // CRITICAL: Soldiers should continuously move toward their target enemy
                // This allows individual soldiers to engage, not just the formation center
                Vector3 toEnemy = enemySoldier.transform.position - mySoldier.transform.position;
                float distance = toEnemy.magnitude;
                
                // Move toward enemy if not in perfect melee range (unless routing)
                if (!isRouted && distance > 0.5f && distance < 3.0f) // Move if between 0.5 and 3 units away
                {
                    Vector3 pursuitDirection = toEnemy.normalized;
                    float pursuitSpeed = 3f; // Speed to close with enemy
                    Vector3 newPos = Vector3.MoveTowards(mySoldier.transform.position, enemySoldier.transform.position, pursuitSpeed * Time.deltaTime);
                    newPos = Ground(newPos);
                    
                    // Apply separation to prevent overlap with nearby units (using UnitSeparation component)
                    var unitSeparation = mySoldier.GetComponent<UnitSeparation>();
                    if (unitSeparation != null)
                    {
                        newPos = unitSeparation.ApplySeparation(newPos);
                    }
                    
                    newPos = ClampFormationToBattlefieldBounds(newPos);
                    mySoldier.transform.position = newPos;
                }

                // Check individual attack cooldown - each unit has its own cooldown
                float cooldown = soldierAttackCooldowns.TryGetValue(mySoldier, out var cd) ? cd : 0f;
                if (cooldown > 0f)
                {
                    // Still on cooldown, skip attack but continue moving/facing
                    // Cooldown will be reduced at end of loop
                    continue;
                }
                
                // Ready to attack - check if enemy is still in range
                if (distance <= MELEE_RANGE * 1.5f) // Slightly extended range for attack
                {
                    Debug.Log($"[CombatDamageCoroutine] {mySoldier.name} ready to attack {enemySoldier.name}");
                    
                    // Attack is ready - start attack and set cooldown
                    Debug.Log($"[CombatDamageCoroutine] Starting StaggeredAttack: {mySoldier.name} -> {enemySoldier.name}");
                    var attackCoroutine = StartCoroutine(StaggeredAttack(myCU, enemyCU, 0f, this, enemyFormation));
                    if (attackCoroutine != null)
                    {
                        activeCoroutines.Add(attackCoroutine);
                    }
                    
                    // Set individual attack cooldown for this unit
                    soldierAttackCooldowns[mySoldier] = BASE_ATTACK_COOLDOWN;
                }
            }
            
            // Update cooldowns for all soldiers (reduce by tick duration)
            // This happens once per combat tick, not per unit
            var cooldownKeys = new List<GameObject>(soldierAttackCooldowns.Keys);
            foreach (var soldierKey in cooldownKeys)
            {
                if (soldierKey == null) continue;
                float currentCooldown = soldierAttackCooldowns[soldierKey];
                if (currentCooldown > 0f)
                {
                    soldierAttackCooldowns[soldierKey] = Mathf.Max(0f, currentCooldown - 0.6f);
                }
            }
            
            // Clean up completed coroutines from activeCoroutines list (manual loop to avoid LINQ allocation)
            for (int i = activeCoroutines.Count - 1; i >= 0; i--)
            {
                if (activeCoroutines[i] == null)
                {
                    activeCoroutines.RemoveAt(i);
                }
            }

            // Apply physical interactions (pushback, knockback, formation pressure)
            // Note: Pushback disabled during combat to prevent teleporting
            ApplyPhysicalInteractions(enemyFormation);
            
            // Update formation centers (but don't force soldiers to snap to new positions during combat)
            // Formation center is used for badge position and general tracking, not for forcing soldier positions
            UpdateFormationCenter();
            enemyFormation.UpdateFormationCenter();

            // Reflow positions and check routing
            RemoveNullSoldiers();
            enemyFormation.RemoveNullSoldiers();
            
            // Refresh soldier arrays after removals
            RefreshSoldierArrays();
            enemyFormation.RefreshSoldierArrays();
            
            // During combat, don't force soldiers into formation grid - let them maintain combat positions
            // Only reform if not in combat (handled by Update() calling UpdateSoldierPositions when isInCombat is false)
            // AdvanceRearUnitsToFillGaps();
            // enemyFormation.AdvanceRearUnitsToFillGaps();
            
            // Don't call UpdateSoldierPositions during combat - it's disabled in the method itself
            // This allows soldiers to maintain their combat positions naturally
            
            // Throttled health and badge updates
            if (Time.time - lastHealthUpdateTime >= HEALTH_UPDATE_THROTTLE)
            {
                if (healthUpdateDirty)
                {
                    UpdateFormationHealthFromSoldiers();
                    healthUpdateDirty = false;
                }
                if (badgeUpdateDirty)
                {
                    UpdateBadgeContents();
                    badgeUpdateDirty = false;
                }
                lastHealthUpdateTime = Time.time;
            }

            if (!isRouted && currentMorale <= routingMoraleThreshold)
            {
                isRouted = true;
                // Route all soldiers in the formation
                RouteAllSoldiers();
            }
            if (!enemyFormation.isRouted && enemyFormation.currentMorale <= enemyFormation.routingMoraleThreshold)
            {
                enemyFormation.isRouted = true;
                enemyFormation.RouteAllSoldiers();
            }

            // End if either formation is wiped (check both current count and if all are marked for destruction)
            bool allMySoldiersDead = true;
            foreach (var s in soldiers)
            {
                if (s != null && !soldiersMarkedForDestruction.Contains(s))
                {
                    allMySoldiersDead = false;
                    break;
                }
            }
            if (soldiers.Count == 0 || allMySoldiersDead)
            {
                PlayDeathAnimation();
                DestroyFormation();
                yield break;
            }
            
            bool allEnemySoldiersDead = true;
            foreach (var s in enemyFormation.soldiers)
            {
                if (s != null && !enemyFormation.soldiersMarkedForDestruction.Contains(s))
                {
                    allEnemySoldiersDead = false;
                    break;
                }
            }
            if (enemyFormation.soldiers.Count == 0 || allEnemySoldiersDead)
            {
                enemyFormation.PlayDeathAnimation();
                enemyFormation.DestroyFormation();
                yield break;
            }
        }

        PlayIdleAnimations();
        if (enemyFormation != null)
        {
            enemyFormation.PlayIdleAnimations();
        }
        
        // Clear active combat coroutine reference
        activeCombatCoroutine = null;
        
        // Clean up all tracked coroutines (manual loop to avoid LINQ allocation)
        for (int i = activeCoroutines.Count - 1; i >= 0; i--)
        {
            if (activeCoroutines[i] == null)
            {
                activeCoroutines.RemoveAt(i);
            }
        }
        
        // Start formation reformation after combat ends
        if (needsReformation)
        {
            StartCoroutine(ReformFormationAfterCombat());
        }
        if (enemyFormation != null && enemyFormation.needsReformation)
        {
            enemyFormation.StartCoroutine(enemyFormation.ReformFormationAfterCombat());
        }
    }

    private void HandleSoldierDeath(FormationUnit owner, GameObject soldier)
    {
        if (soldier == null || owner == null) return;
        
        // Mark soldier for destruction to prevent double removal
        if (owner.soldiersMarkedForDestruction.Contains(soldier))
        {
            return; // Already being destroyed
        }
        owner.soldiersMarkedForDestruction.Add(soldier);
        
        var cu = soldier.GetComponent<CombatUnit>();
        if (cu != null) cu.TriggerAnimation("Death"); // Use capitalized to match parameter hash
        
        // Track casualty for soldier count reduction using sourceUnit reference
        // NOTE: This is the SINGLE mechanism for unit death - individual HP hits 0 via ApplyDamage()
        // Formation health is calculated FROM individual soldier health, not the other way around
        
        // Remove soldier immediately from list
        if (owner.soldiers.Contains(soldier))
        {
            owner.soldiers.Remove(soldier);
        }
        owner.ClearSoldierOffsetImmediate(soldier);
        
        // Clean up contact tracking and cooldowns for this soldier
        owner.soldierContacts.Remove(soldier);
        owner.soldierAttackCooldowns.Remove(soldier);
        
        // Also remove this soldier from other soldiers' contact lists
        foreach (var contactList in owner.soldierContacts.Values)
        {
            contactList.Remove(soldier);
        }
        
        // Refresh arrays after removal
        owner.RefreshSoldierArrays();
        
        // Mark for badge update
        owner.badgeUpdateDirty = true;
        
        // Destroy soldier after delay (but don't remove from list again)
        Destroy(soldier, 1.2f);
    }

    private void RemoveNullSoldiers()
    {
        int removed = 0;
        for (int i = soldiers.Count - 1; i >= 0; i--)
        {
            var soldier = soldiers[i];
            if (soldier == null) 
            {
                ClearSoldierOffsetImmediate(soldier);
                soldiers.RemoveAt(i);
                removed++;
            }
        }
        
        // Refresh arrays if soldiers were removed
        if (removed > 0)
        {
            RefreshSoldierArrays();
        }
    }
    
    // REMOVED: ApplyDamageToFormation() - This used proportional kill system based on formation health fraction
    // All combat now uses individual HP-based system via StaggeredAttack() -> ApplyDamage() on CombatUnit
    // Formation health is calculated FROM individual soldier health, not the other way around
    
    // REMOVED: CheckForSoldierDeaths() - Proportional kill system retired
    // Death is now handled purely by individual HP: when CombatUnit.currentHealth <= 0, ApplyDamage() returns true
    // and HandleSoldierDeath() is called. This is the single, clear mechanism for unit death.
    // Formation health is derived from individual soldier health via UpdateFormationHealthFromSoldiers()
    
    // Legacy method removed - proportional kill system retired
    // All deaths now handled via individual HP: CombatUnit.ApplyDamage() -> HandleSoldierDeath()
    
    System.Collections.IEnumerator DestroySoldierAfterDelay(GameObject soldier, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (soldier != null)
        {
            // Only remove if still in list (may have been removed by HandleSoldierDeath)
            if (soldiers.Contains(soldier))
            {
                soldiers.Remove(soldier);
                RefreshSoldierArrays();
            }
            
            // Remove from marked set
            soldiersMarkedForDestruction.Remove(soldier);
            
            Destroy(soldier);
            
            // After soldier dies, advance rear units to fill gaps
            AdvanceRearUnitsToFillGaps();
        }
    }
    
    /// <summary>
    /// Staggered attack coroutine - creates wave effect across formation
    /// </summary>
    System.Collections.IEnumerator StaggeredAttack(CombatUnit attacker, CombatUnit defender, float delay, FormationUnit attackerFormation, FormationUnit defenderFormation)
    {
        // Wait for stagger delay
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }
        
        // Safety checks
        if (attacker == null || defender == null || attackerFormation == null || defenderFormation == null) yield break;
        if (attacker.gameObject == null || defender.gameObject == null) yield break;

        // Bring soldiers into close combat positions for more natural fights
        yield return BringSoldiersTogether(attacker, defender, attackerFormation, defenderFormation);
        
        // Make units face their targets immediately (continuous facing is handled in CombatDamageCoroutine)
        // Just ensure they face each other at the start of attack
        Vector3 attackerToDefender = defender.transform.position - attacker.transform.position;
        attackerToDefender.y = 0;
        if (attackerToDefender.sqrMagnitude > 0.01f)
        {
            attacker.transform.rotation = Quaternion.LookRotation(attackerToDefender.normalized);
        }
        
        Vector3 defenderToAttacker = attacker.transform.position - defender.transform.position;
        defenderToAttacker.y = 0;
        if (defenderToAttacker.sqrMagnitude > 0.01f)
        {
            defender.transform.rotation = Quaternion.LookRotation(defenderToAttacker.normalized);
        }
        
        // Clean up null coroutines periodically (manual loop to avoid LINQ allocation)
        for (int i = activeCoroutines.Count - 1; i >= 0; i--)
        {
            if (activeCoroutines[i] == null)
            {
                activeCoroutines.RemoveAt(i);
            }
        }
        
        // CRITICAL: Set units to Attacking state so continuous attack animations play
        // IsAttacking bool will be automatically set to true in CombatUnit.Update() -> UpdateAttackingAnimation()
        attacker.battleState = BattleUnitState.Attacking;
        defender.battleState = BattleUnitState.Attacking;
        
        // Hit animation will play when ApplyDamage is called (defender gets hit)
        // No need to trigger Attack - IsAttacking bool handles it automatically
        
        // Calculate base damage
        int baseDmgAB = Mathf.Max(0, attacker.CurrentAttack - defender.CurrentDefense);
        Debug.Log($"[StaggeredAttack] {attacker.gameObject.name} attacking {defender.gameObject.name}: baseAttack={attacker.CurrentAttack}, defenderDefense={defender.CurrentDefense}, baseDamage={baseDmgAB}");
        
        // Apply charge bonus (only for melee attacks, only on first contact, and only if unit has charge bonus)
        float damageMultiplierAB = 1.0f;
        bool isMeleeAttack = true; // This is melee combat
        if (isMeleeAttack && attackerFormation.isCharging && !attackerFormation.hasAppliedChargeBonus && attacker.data != null)
        {
            float unitChargeBonus = attacker.data.chargeBonusMultiplier;
            if (unitChargeBonus > 1.0f) // Only apply if unit has a charge bonus
            {
                damageMultiplierAB *= unitChargeBonus;
                attackerFormation.hasAppliedChargeBonus = true;
                Debug.Log($"[StaggeredAttack] Applied charge bonus: {unitChargeBonus}x");
            }
        }
        
        // Apply flanking/rear attack bonus
        float flankingBonusAB = GetFlankingBonus(attackerFormation, defenderFormation);
        damageMultiplierAB *= flankingBonusAB;
        
        // Enhanced flanking: Apply defensive penalties and morale shocks
        float attackAngle = CalculateAttackAngle(attackerFormation, defenderFormation);
        float defensePenalty = 1.0f;
        float moraleShock = 0f;
        
        if (attackAngle >= 135f) // Rear attack
        {
            defensePenalty = 1.0f - REAR_ATTACK_DEFENSE_PENALTY; // 50% defense reduction
            moraleShock = REAR_ATTACK_MORALE_SHOCK;
            // Visual indicator: could add particle effect or UI indicator here
        }
        else if (attackAngle >= 45f) // Flank attack
        {
            defensePenalty = 1.0f - FLANK_ATTACK_DEFENSE_PENALTY; // 25% defense reduction
            moraleShock = FLANK_ATTACK_MORALE_SHOCK;
        }
        
        // Apply defense penalty to damage calculation
        int adjustedDefense = Mathf.RoundToInt(defender.CurrentDefense * defensePenalty);
        baseDmgAB = Mathf.Max(0, attacker.CurrentAttack - adjustedDefense);
        
        // Apply morale shock to defender formation
        if (moraleShock > 0f)
        {
            defenderFormation.currentMorale = Mathf.Max(0, (int)(defenderFormation.currentMorale - moraleShock));
            defenderFormation.badgeUpdateDirty = true;
        }
        
        // Calculate final damage
        int finalDmgAB = Mathf.RoundToInt(baseDmgAB * damageMultiplierAB);
        Debug.Log($"[StaggeredAttack] Final damage after bonuses: {finalDmgAB} (multiplier: {damageMultiplierAB})");
        
        // Apply knockback on heavy hits
        if (finalDmgAB > 5) // Heavy hit threshold
        {
            ApplyKnockback(defender.gameObject, attacker.gameObject.transform.position, 0.3f);
        }
        
        // Gain experience from dealing damage
        if (finalDmgAB > 0)
        {
            attackerFormation.GainExperience(finalDmgAB, $"melee damage dealt");
        }
        
        Debug.Log($"[StaggeredAttack] About to call ApplyDamage on {defender.gameObject.name} with damage={finalDmgAB}");
        bool bDied = defender.ApplyDamage(finalDmgAB, attacker, true); // true = melee attack
        
        // CRITICAL: Mark formation health as dirty (will be updated in throttled update)
        defenderFormation.healthUpdateDirty = true;
        
        Debug.Log($"[StaggeredAttack] {defender.gameObject.name} died: {bDied}");
        if (bDied)
        {
            HandleSoldierDeath(defenderFormation, defender.gameObject);
            defenderFormation.currentMorale = Mathf.Max(0, defenderFormation.currentMorale - 5);
            // Gain experience from kill
            attackerFormation.GainExperience(10, $"killed enemy soldier");
        }
        
        // CRITICAL: Counter-attack immediately if defender is still alive
        // This ensures soldiers always attack back when hit (like Total War)
        if (!bDied && defender != null && attacker != null)
        {
            // Very short delay before counter-attack (almost immediate)
            yield return new WaitForSeconds(0.1f);
            
            // Safety check again
            if (attacker == null || defender == null || attacker.gameObject == null || defender.gameObject == null) yield break;
            
            // Make defender face attacker for counter-attack
            Vector3 counterDefenderToAttacker = attacker.transform.position - defender.transform.position;
            counterDefenderToAttacker.y = 0;
            if (counterDefenderToAttacker.sqrMagnitude > 0.01f)
            {
                defender.transform.rotation = Quaternion.LookRotation(counterDefenderToAttacker.normalized);
            }
            
            // Set defender to attacking state so animation plays
            defender.battleState = BattleUnitState.Attacking;
            
            int baseDmgBA = Mathf.Max(0, defender.CurrentAttack - attacker.CurrentDefense);
            
            // Apply flanking bonus for counter-attack
            float flankingBonusBA = GetFlankingBonus(defenderFormation, attackerFormation);
            
            // Enhanced flanking: Apply defensive penalties for counter-attack too
            float counterAttackAngle = CalculateAttackAngle(defenderFormation, attackerFormation);
            float counterDefensePenalty = 1.0f;
            
            if (counterAttackAngle >= 135f) // Rear attack
            {
                counterDefensePenalty = 1.0f - REAR_ATTACK_DEFENSE_PENALTY;
            }
            else if (counterAttackAngle >= 45f) // Flank attack
            {
                counterDefensePenalty = 1.0f - FLANK_ATTACK_DEFENSE_PENALTY;
            }
            
            int counterAdjustedDefense = Mathf.RoundToInt(attacker.CurrentDefense * counterDefensePenalty);
            baseDmgBA = Mathf.Max(0, defender.CurrentAttack - counterAdjustedDefense);
            
            int finalDmgBA = Mathf.RoundToInt(baseDmgBA * flankingBonusBA);
            
            Debug.Log($"[StaggeredAttack] Counter-attack: {defender.gameObject.name} -> {attacker.gameObject.name}, damage={finalDmgBA}");
            
            // Apply knockback on heavy counter-hits
            if (finalDmgBA > 5) // Heavy hit threshold
            {
                ApplyKnockback(attacker.gameObject, defender.gameObject.transform.position, 0.3f);
            }
            
            // Gain experience from dealing damage
            if (finalDmgBA > 0)
            {
                defenderFormation.GainExperience(finalDmgBA, $"melee damage dealt");
            }
            
            bool aDied = attacker.ApplyDamage(finalDmgBA, defender, true);
            
            // CRITICAL: Mark formation health as dirty (will be updated in throttled update)
            attackerFormation.healthUpdateDirty = true;
            
            if (aDied)
            {
                HandleSoldierDeath(attackerFormation, attacker.gameObject);
                attackerFormation.currentMorale = Mathf.Max(0, attackerFormation.currentMorale - 5);
                // Gain experience from kill
                defenderFormation.GainExperience(10, $"killed enemy soldier");
            }
        }
        
        // DON'T reset battle state to Idle here - keep units in Attacking state for continuous combat!
        // The CombatDamageCoroutine will keep calling StaggeredAttack as long as units are in range
        // This ensures IsAttacking bool stays true and attack animations loop continuously
        
        // Units will automatically return to Idle when combat ends (formations move apart)
        // or when one dies (handled in HandleSoldierDeath)
    }

    private IEnumerator BringSoldiersTogether(CombatUnit attacker, CombatUnit defender, FormationUnit attackerFormation, FormationUnit defenderFormation)
    {
        if (attacker == null || defender == null) yield break;

        Vector3 direction = defender.transform.position - attacker.transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = attacker.transform.forward;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.forward;
            }
        }
        direction = direction.normalized;

        Vector3 midpoint = (attacker.transform.position + defender.transform.position) * 0.5f;
        float separation = SOLDIER_COMBAT_SEPARATION;

        Vector3 attackerTarget = midpoint - direction * (separation * 0.5f);
        Vector3 defenderTarget = midpoint + direction * (separation * 0.5f);

        attackerTarget = attackerFormation != null ? attackerFormation.GetGroundedPosition(attackerTarget) : attackerTarget;
        defenderTarget = defenderFormation != null ? defenderFormation.GetGroundedPosition(defenderTarget) : defenderTarget;

        attackerFormation?.SetSoldierOffsetToWorld(attacker.gameObject, attackerTarget, SOLDIER_APPROACH_DURATION);
        defenderFormation?.SetSoldierOffsetToWorld(defender.gameObject, defenderTarget, SOLDIER_APPROACH_DURATION);

        yield return new WaitForSeconds(SOLDIER_APPROACH_DURATION);
    }
    
    /// <summary>
    /// Apply physical interactions between formations (pushback, knockback, formation pressure)
    /// </summary>
    void ApplyPhysicalInteractions(FormationUnit enemyFormation)
    {
        if (enemyFormation == null) return;
        
        // During combat, don't apply pushback that causes teleporting
        // Instead, let individual units handle positioning naturally
        // Pushback is disabled during melee to prevent formation center jumping
        
        // Note: Formation compression removed - it was causing teleporting by directly setting positions
        // During combat, soldiers maintain their positions naturally without forced compression
    }
    
    /// <summary>
    /// Apply knockback to a unit when hit hard
    /// </summary>
    void ApplyKnockback(GameObject unit, Vector3 fromPosition, float knockbackDistance)
    {
        if (unit == null) return;
        
        // Use smooth knockback coroutine instead of instant teleporting
        StartCoroutine(SmoothKnockback(unit, fromPosition, knockbackDistance));
    }
    
    /// <summary>
    /// Smooth knockback using interpolation instead of instant position setting
    /// </summary>
    System.Collections.IEnumerator SmoothKnockback(GameObject unit, Vector3 fromPosition, float knockbackDistance)
    {
        if (unit == null) yield break;
        
        Vector3 startPos = unit.transform.position;
        Vector3 knockbackDirection = (startPos - fromPosition).normalized;
        knockbackDirection.y = 0; // Keep on ground
        
        Vector3 targetPos = Ground(startPos + knockbackDirection * knockbackDistance);
        // Clamp knockback target to battlefield bounds to prevent units from being knocked off-map
        targetPos = ClampFormationToBattlefieldBounds(targetPos);
        
        float duration = 0.15f; // Short duration for knockback
        float elapsed = 0f;
        
        while (elapsed < duration && unit != null)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            // Use smooth interpolation (ease out for natural deceleration)
            t = 1f - Mathf.Pow(1f - t, 3f); // Cubic ease out
            
            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            // Clamp during interpolation to prevent leaving bounds mid-knockback
            currentPos = ClampFormationToBattlefieldBounds(currentPos);
            unit.transform.position = currentPos;
            yield return null;
        }
        
        // Ensure final position is set and clamped
        if (unit != null)
        {
            Vector3 finalPos = Ground(targetPos);
            finalPos = ClampFormationToBattlefieldBounds(finalPos);
            unit.transform.position = finalPos;
        }
    }
    
    /// <summary>
    /// Make a unit face its target continuously during combat
    /// Runs until combat ends or unit dies
    /// </summary>
    System.Collections.IEnumerator FaceTargetContinuously(GameObject unit, GameObject target)
    {
        // Run continuously while both units exist and are in combat
        while (unit != null && target != null && isInCombat)
        {
            // Check if target is still alive (immediate exit on death)
            if (unit == null || target == null) yield break;
            
            // Check if target's CombatUnit is dead
            var targetCombatUnit = target.GetComponent<CombatUnit>();
            if (targetCombatUnit != null && targetCombatUnit.battleState == BattleUnitState.Dead)
            {
                yield break; // Stop immediately if target dies
            }
            
            Vector3 directionToTarget = (target.transform.position - unit.transform.position).normalized;
            directionToTarget.y = 0; // Keep rotation on horizontal plane
            
            if (directionToTarget.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                unit.transform.rotation = Quaternion.Slerp(unit.transform.rotation, targetRotation, Time.deltaTime * 10f);
            }
            
            yield return null;
        }
    }
    
    /// <summary>
    /// Advance rear units to fill gaps when front units die (handles irregular formations)
    /// </summary>
    void AdvanceRearUnitsToFillGaps()
    {
        if (soldiers == null || soldiers.Count == 0) return;
        
        // Find enemy formation to determine forward direction
        FormationUnit enemyFormation = FindNearestEnemyFormation();
        if (enemyFormation == null) return;
        
        Vector3 directionToEnemy = (enemyFormation.formationCenter - formationCenter).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, directionToEnemy).normalized;
        
        // Build list of alive soldiers with their positions (reuse list to avoid allocation)
        reusableAliveSoldiersList.Clear();
        foreach (var soldier in soldiers)
        {
            if (soldier != null)
            {
                reusableAliveSoldiersList.Add(soldier);
            }
        }
        
        if (reusableAliveSoldiersList.Count == 0) return;
        
        // Sort soldiers by distance from enemy (front to back)
        reusableAliveSoldiersList.Sort((a, b) =>
        {
            float distA = Vector3.Dot((a.transform.position - formationCenter), directionToEnemy);
            float distB = Vector3.Dot((b.transform.position - formationCenter), directionToEnemy);
            return distA.CompareTo(distB);
        });
        
        // Calculate expected formation dimensions based on alive count
        int expectedRows = Mathf.CeilToInt(Mathf.Sqrt(reusableAliveSoldiersList.Count));
        int expectedCols = Mathf.CeilToInt((float)reusableAliveSoldiersList.Count / expectedRows);
        
        // Find gaps in front rows and fill from rear
        float rowSpacing = soldierSpacing;
        float colSpacing = soldierSpacing;
        
        // Group soldiers by approximate row (based on forward distance) - reuse dictionary
        reusableRowsDictionary.Clear();
        foreach (var list in reusableRowsDictionary.Values)
        {
            if (list != null) list.Clear();
        }
        
        for (int i = 0; i < reusableAliveSoldiersList.Count; i++)
        {
            Vector3 pos = reusableAliveSoldiersList[i].transform.position;
            float forwardDist = Vector3.Dot((pos - formationCenter), directionToEnemy);
            int rowIndex = Mathf.RoundToInt(forwardDist / rowSpacing);
            
            if (!reusableRowsDictionary.ContainsKey(rowIndex))
            {
                reusableRowsDictionary[rowIndex] = new List<GameObject>();
            }
            reusableRowsDictionary[rowIndex].Add(reusableAliveSoldiersList[i]);
        }
        
        // Find gaps in front rows and fill from rear - reuse list
        reusableSortedRowsList.Clear();
        reusableSortedRowsList.AddRange(reusableRowsDictionary.Keys);
        reusableSortedRowsList.Sort(); // Front to back
        
        for (int r = 0; r < reusableSortedRowsList.Count - 1; r++)
        {
            int currentRow = reusableSortedRowsList[r];
            int nextRow = reusableSortedRowsList[r + 1];
            
            List<GameObject> currentRowSoldiers = reusableRowsDictionary[currentRow];
            List<GameObject> nextRowSoldiers = reusableRowsDictionary[nextRow];
            
            // Calculate expected soldiers per row
            int expectedInRow = Mathf.Min(expectedCols, reusableAliveSoldiersList.Count - r * expectedCols);
            
            // If current row has gaps (fewer soldiers than expected), fill from next row
            if (currentRowSoldiers.Count < expectedInRow && nextRowSoldiers.Count > 0)
            {
                int gapsToFill = expectedInRow - currentRowSoldiers.Count;
                int unitsToMove = Mathf.Min(gapsToFill, nextRowSoldiers.Count);
                
                // Move units from next row to fill gaps in current row
                for (int i = 0; i < unitsToMove; i++)
                {
                    GameObject unitToMove = nextRowSoldiers[i];
                    if (unitToMove == null) continue;
                    
                    // Calculate target position in current row
                    int targetCol = currentRowSoldiers.Count;
                    Vector3 targetOffset = directionToEnemy * (currentRow * rowSpacing) + right * ((targetCol - expectedCols / 2f) * colSpacing);
                    Vector3 targetPosition = Ground(formationCenter + targetOffset);
                    
                    // Move unit smoothly to fill gap
                    var moveCoroutine = StartCoroutine(MoveUnitToPosition(unitToMove, targetPosition));
                    if (moveCoroutine != null) activeCoroutines.Add(moveCoroutine);
                    
                    // Update row assignment
                    currentRowSoldiers.Add(unitToMove);
                    nextRowSoldiers.RemoveAt(i);
                    i--; // Adjust index after removal
                }
            }
        }
        
        // Clean up null entries
        RemoveNullSoldiers();
    }
    
    /// <summary>
    /// Find nearest enemy formation (uses cached formations for performance)
    /// </summary>
    FormationUnit FindNearestEnemyFormation()
    {
        FormationUnit nearest = null;
        float nearestDistance = float.MaxValue;
        
        // Use cached enemy formations if available and recent
        List<FormationUnit> formationsToCheck;
        if (Time.time - lastEnemyFormationUpdate < ENEMY_FORMATION_UPDATE_INTERVAL && cachedEnemyFormations.Count > 0)
        {
            formationsToCheck = cachedEnemyFormations;
        }
        else
        {
            // Update cache using cached array to avoid expensive FindObjectsByType call
            UpdateFormationCacheIfNeeded();
            cachedEnemyFormations.Clear();
            
            if (cachedAllFormations != null)
            {
                foreach (var formation in cachedAllFormations)
                {
                    if (formation != null && formation != this && formation.isAttacker != this.isAttacker)
                    {
                        cachedEnemyFormations.Add(formation);
                    }
                }
            }
            lastEnemyFormationUpdate = Time.time;
            formationsToCheck = cachedEnemyFormations;
        }
        
        foreach (var formation in formationsToCheck)
        {
            if (formation == null || formation == this) continue;
            if (formation.isAttacker == this.isAttacker) continue; // Same side
            
            float distance = Vector3.Distance(formationCenter, formation.formationCenter);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = formation;
            }
        }
        
        return nearest;
    }
    
    /// <summary>
    /// Move unit smoothly to target position (for filling gaps)
    /// </summary>
    System.Collections.IEnumerator MoveUnitToPosition(GameObject unit, Vector3 targetPosition)
    {
        if (unit == null) yield break;
        
        Vector3 startPosition = unit.transform.position;
        // Clamp target position to battlefield bounds
        targetPosition = ClampFormationToBattlefieldBounds(targetPosition);
        float distance = Vector3.Distance(startPosition, targetPosition);
        float moveSpeed = 3f; // Same as formation move speed
        float duration = distance / moveSpeed;
        float elapsed = 0f;
        
        // Mark unit as moving for animation
        var combatUnit = unit.GetComponent<CombatUnit>();
        if (combatUnit != null)
        {
            combatUnit.isMoving = true;
        }
        
        while (elapsed < duration && unit != null)
        {
            if (unit == null) yield break;
            
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            
            // Smooth movement with easing
            float easedT = t * t * (3f - 2f * t); // Smoothstep easing
            Vector3 currentPosition = Vector3.Lerp(startPosition, targetPosition, easedT);
            // Clamp during movement to prevent leaving bounds
            currentPosition = ClampFormationToBattlefieldBounds(currentPosition);
            unit.transform.position = Ground(currentPosition);
            
            yield return null;
        }
        
        // Ensure unit reaches exact target position (clamped)
        if (unit != null)
        {
            Vector3 finalPos = Ground(targetPosition);
            finalPos = ClampFormationToBattlefieldBounds(finalPos);
            unit.transform.position = finalPos;
            
            // Stop moving animation
            if (combatUnit != null)
            {
                combatUnit.isMoving = false;
            }
            
            // Update formation center after unit has moved
            UpdateFormationCenter();
        }
    }
    
    void DestroyFormation()
    {
        // Stop any active combat coroutines
        if (activeCombatCoroutine != null)
        {
            StopCoroutine(activeCombatCoroutine);
            activeCombatCoroutine = null;
        }
        
        // Stop all tracked coroutines
        foreach (var coroutine in activeCoroutines)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        activeCoroutines.Clear();
        
        // Clear marked soldiers set
        soldiersMarkedForDestruction.Clear();
        
        // Destroy badge UI before destroying formation
        if (badgeCanvas != null)
        {
            // Badge is now a child of shared Canvas, so just destroy the text GameObject
            if (badgeText != null)
            {
                Destroy(badgeText.gameObject);
                badgeText = null;
            }
            // Note: badgeCanvas is no longer used (we use shared Canvas now)
        }
        
        // Destroy all soldiers in formation
        foreach (var soldier in soldiers)
        {
            if (soldier != null)
            {
                Destroy(soldier);
            }
        }
        
        // Clear references
        soldiers.Clear();
        soldierCombatUnits = null;
        selectionRenderers = null;
        
        // Destroy formation GameObject
        Destroy(gameObject);
    }
    
    void StopMoving()
    {
        isMoving = false;
        walkingAnimationsInitialized = false; // Reset flag for next time
        PlayIdleAnimations();
    }
    
    // Track if we've initialized walking animations to avoid resetting triggers every frame
    private bool walkingAnimationsInitialized = false;
    
    void PlayWalkingAnimations()
    {
        // Check if soldierCombatUnits array is initialized
        if (soldierCombatUnits == null || soldierCombatUnits.Length == 0)
        {
            RefreshSoldierArrays();
        }
        
        if (soldierCombatUnits == null)
        {
            Debug.LogWarning($"[FormationUnit] {formationName}: PlayWalkingAnimations called but soldierCombatUnits is null");
            return;
        }
        
        int count = 0;
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Mark unit as in battle (this prevents UnitMovementController from overriding)
                combatUnit.IsInBattle = true;
                
                // Set walking state - CombatUnit.isMoving property will automatically update IsWalking animator parameter
                bool wasMoving = combatUnit.isMoving;
                combatUnit.isMoving = true;
                count++;
                
                if (!wasMoving)
                {
                    // Debug.Log removed for performance
                }
            }
        }
        
        if (count > 0 && !walkingAnimationsInitialized)
        {
            // Debug.Log removed for performance
        }
        
        // Mark as initialized after first call
        walkingAnimationsInitialized = true;
    }

    // Formation-level grounding helper - cache BattleTestSimple reference
    private static BattleTestSimple cachedBattleTest;
    private static float lastBattleTestCacheUpdate = 0f;
    private const float BATTLE_TEST_CACHE_INTERVAL = 1f; // Update every second
    
    private Vector3 Ground(Vector3 pos)
    {
        // Cache BattleTestSimple to avoid expensive FindFirstObjectByType calls
        if (cachedBattleTest == null || Time.time - lastBattleTestCacheUpdate > BATTLE_TEST_CACHE_INTERVAL)
        {
            cachedBattleTest = FindFirstObjectByType<BattleTestSimple>();
            lastBattleTestCacheUpdate = Time.time;
        }
        
        LayerMask layers = cachedBattleTest != null ? cachedBattleTest.battlefieldLayers : ~0;
        Vector3 origin = pos + Vector3.up * 100f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f, layers))
            return hit.point;
        return new Vector3(pos.x, 0f, pos.z);
    }
    
    /// <summary>
    /// Route all soldiers in this formation (set them to routing state)
    /// </summary>
    private void RouteAllSoldiers()
    {
        foreach (var soldier in soldiers)
        {
            if (soldier == null) continue;
            
            var combatUnit = soldier.GetComponent<CombatUnit>();
            if (combatUnit != null)
            {
                // Set routed flag and routing state
                combatUnit.SetRouted(true);
                combatUnit.battleState = BattleUnitState.Routing;
                // StartRetreat will be called automatically when battleState is set to Routing
                combatUnit.SetBattleState(BattleUnitState.Routing);
            }
        }
    }
    
    /// <summary>
    /// Clamp formation position to stay within battlefield bounds (prevents routed formations from leaving the map)
    /// </summary>
    private Vector3 ClampFormationToBattlefieldBounds(Vector3 position)
    {
        // Get battlefield size from BattleTestSimple if available
        float battlefieldSize = 100f; // Default size
        if (BattleTestSimple.Instance != null)
        {
            battlefieldSize = BattleTestSimple.Instance.battleMapSize;
        }
        
        // Clamp to a square battlefield centered at origin
        // Battlefield extends from -battlefieldSize/2 to +battlefieldSize/2 on X and Z axes
        float halfSize = battlefieldSize * 0.5f;
        float margin = 5f; // Keep formations 5 units away from edge
        
        position.x = Mathf.Clamp(position.x, -halfSize + margin, halfSize - margin);
        position.z = Mathf.Clamp(position.z, -halfSize + margin, halfSize - margin);
        
        // Keep Y position (height) unchanged - let it stay on ground
        
        return position;
    }

    public Vector3 GetGroundedPosition(Vector3 pos)
    {
        return Ground(pos);
    }

    /// <summary>
    /// ARCHITECTURE: Formation health is ALWAYS derived from individual soldier health.
    /// This is the single source of truth for formation health during combat.
    /// Individual soldiers take damage via CombatUnit.ApplyDamage(), and when their HP hits 0, they die.
    /// Formation health is then recalculated from remaining soldiers.
    /// DO NOT set formation health directly - always call this method after soldier health changes.
    /// </summary>
    public void UpdateFormationHealthFromSoldiers()
    {
        if (soldiers == null || soldiers.Count == 0)
        {
            currentHealth = 0;
            return;
        }
        
        int totalSoldierHealth = 0;
        int totalSoldierMaxHealth = 0;
        
        foreach (var soldier in soldiers)
        {
            if (soldier == null) continue;
            
            var combatUnit = soldier.GetComponent<CombatUnit>();
            if (combatUnit != null)
            {
                totalSoldierHealth += Mathf.Max(0, combatUnit.currentHealth);
                totalSoldierMaxHealth += combatUnit.MaxHealth;
            }
        }
        
        // Update formation health to match total soldier health
        // This is the ONLY way formation health should be updated during combat
        currentHealth = totalSoldierHealth;
        totalHealth = Mathf.Max(totalHealth, totalSoldierMaxHealth); // Ensure totalHealth is at least the sum
        
        // Mark badge for update (will be updated in throttled update)
        badgeUpdateDirty = true;
    }
    
    // --- Badge UI helpers ---
    // MEMORY OPTIMIZED: Uses shared Canvas instead of creating one per formation
    public void CreateOrUpdateBadgeUI()
    {
        // Get shared Canvas from BattleTestSimple (memory optimization - one Canvas for all badges)
        Canvas sharedCanvas = null;
        if (BattleTestSimple.Instance != null)
            {
            sharedCanvas = BattleTestSimple.Instance.GetSharedFormationBadgeCanvas();
        }
        
        if (sharedCanvas == null)
        {
            Debug.LogWarning($"[FormationUnit] {formationName}: Shared Canvas not available, badge UI disabled");
            return;
        }
        
        // Create badge GameObject as child of shared Canvas (if not already created)
        if (badgeText == null)
        {
            var textGO = new GameObject($"Badge_{formationName}");
            textGO.transform.SetParent(sharedCanvas.transform, false);
            
            // Put text on UI layer so it doesn't block unit raycasts
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer != -1)
            {
                textGO.layer = uiLayer;
            }
            
            badgeText = textGO.AddComponent<TMPro.TextMeshProUGUI>();
            badgeText.alignment = TMPro.TextAlignmentOptions.Center;
            badgeText.fontSize = 0.3f;
            badgeText.raycastTarget = false; // Don't block raycasts
            var rt = badgeText.rectTransform;
            rt.sizeDelta = new Vector2(3, 3); // Small badge size
        }
        
        UpdateBadgeContents();
        UpdateBadgePosition();
    }

    public void UpdateBadgeContents()
    {
        if (badgeText == null) return;
        int alive = 0;
        int totalHp = 0;
        int currentHp = 0;
        float totalFatigue = 0f;
        int totalAmmo = 0;
        int maxAmmo = 0;
        bool isRangedFormation = false;
        
        foreach (var s in soldiers)
        {
            if (s == null) continue;
            var cu = s.GetComponent<CombatUnit>();
            if (cu == null) continue;
            alive++;
            totalHp += cu.MaxHealth;
            currentHp += Mathf.Max(0, cu.currentHealth);
            totalFatigue += cu.currentFatigue;
            
            // Track ammo for ranged units
            if (cu.data != null && cu.data.isRangedUnit)
            {
                isRangedFormation = true;
                totalAmmo += cu.currentAmmo;
                maxAmmo += cu.data.maxAmmo;
            }
        }
        
        totalHp = Mathf.Max(totalHp, 1);
        int morale = Mathf.Clamp(currentMorale, 0, 100);
        int avgFatigue = alive > 0 ? Mathf.RoundToInt(totalFatigue / alive) : 0;
        
        // Calculate soldier count from source units
        int currentSoldierCount = 0;
        int maxSoldierCount = 0;
        if (sourceUnits != null && sourceUnits.Count > 0)
        {
            foreach (var sourceUnit in sourceUnits)
            {
                if (sourceUnit != null)
                {
                    currentSoldierCount += sourceUnit.soldierCount;
                    maxSoldierCount += sourceUnit.maxSoldierCount;
                }
            }
        }
        else
        {
            // Fallback: use formation's stored values
            currentSoldierCount = totalSoldierCount;
            maxSoldierCount = this.maxSoldierCount;
        }
        
        // Build badge text with soldier count, fatigue and optional ammo
        string badgeStr = $"{formationName}\nUnits {alive}/{soldiers.Count} | Soldiers {currentSoldierCount}/{maxSoldierCount}\nHP {currentHp}/{totalHp} | Morale {morale}% | Fatigue {avgFatigue}%";
        
        if (isRangedFormation)
        {
            badgeStr += $"\nAmmo {totalAmmo}/{maxAmmo}";
        }
        
        badgeText.text = badgeStr;
    }

    private void UpdateBadgePosition()
    {
        if (badgeText == null) return;
        
        // Position badge 4 units above formation center (formationCenter should already be on ground)
        // Since badge is now a child of shared WorldSpace Canvas, we position it relative to Canvas
        Vector3 groundedCenter = Ground(formationCenter);
        Vector3 badgeWorldPos = groundedCenter + new Vector3(0, 4f, 0); // 4 units above ground level
        
        // Get shared Canvas to convert world position to local position
        Canvas sharedCanvas = null;
        if (BattleTestSimple.Instance != null)
        {
            sharedCanvas = BattleTestSimple.Instance.GetSharedFormationBadgeCanvas();
        }
        
        if (sharedCanvas != null)
        {
            // Convert world position to local position relative to Canvas
            badgeText.transform.position = badgeWorldPos;
            
        // Face camera
        var cam = Camera.main; 
        if (cam != null)
        {
                badgeText.transform.rotation = Quaternion.LookRotation(badgeText.transform.position - cam.transform.position);
            }
        }
    }
    
    void PlayIdleAnimations()
    {
        // Check if soldierCombatUnits array is initialized
        if (soldierCombatUnits == null || soldierCombatUnits.Length == 0)
        {
            RefreshSoldierArrays();
        }
        
        if (soldierCombatUnits == null)
        {
            Debug.LogWarning($"[FormationUnit] {formationName}: PlayIdleAnimations called but soldierCombatUnits is null");
            return;
        }
        
        // Reset walking animation flag so we can reinitialize next time
        walkingAnimationsInitialized = false;
        
        int count = 0;
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // CRITICAL: Reset battle state to Idle when combat ends
                // This will clear IsAttacking bool and allow idle animations
                combatUnit.battleState = BattleUnitState.Idle;
                
                // Set walking state to false - CombatUnit.isMoving property will automatically update IsWalking animator parameter
                // This will also trigger UpdateIdleAnimation() which handles idle state properly
                bool wasMoving = combatUnit.isMoving;
                combatUnit.isMoving = false;
                count++;
                
                if (wasMoving)
                {
                    // Debug.Log removed for performance
                }
            }
        }
        
        if (count > 0)
                {
            // Debug.Log removed for performance
        }

        // Slowly relax combat offsets back to default when idle
        ResetSoldierOffsets();
    }
    
    void PlayFightingAnimations()
    {
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Set battle state to Attacking - IsAttacking bool will be automatically set in Update()
                combatUnit.battleState = BattleUnitState.Attacking;
                // No need to trigger Attack - IsAttacking bool handles continuous attack animations
            }
        }
    }
    
    void PlayAttackAnimation()
    {
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Set battle state to Attacking - IsAttacking bool handles the animation
                combatUnit.battleState = BattleUnitState.Attacking;
                // No trigger needed - IsAttacking bool is set automatically in Update()
            }
        }
    }
    
    void PlayDeathAnimation()
    {
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                combatUnit.TriggerAnimation("Death");
            }
        }
    }
    
    void PlayHitAnimation()
    {
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                combatUnit.TriggerAnimation("Hit");
            }
        }
    }
    
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        
        // Update visual selection indicators
        foreach (var renderer in selectionRenderers)
        {
            if (renderer != null)
            {
                if (selected)
                {
                    // Add selection highlight
                    renderer.material.color = Color.green;
                }
                else
                {
                    // Remove selection highlight
                    renderer.material.color = teamColor;
                }
            }
        }
    }
}

/// <summary>
/// Unit combat stats and fighting logic
/// </summary>
public class UnitCombat : MonoBehaviour
{
    public string unitName = "";
    public bool isAttacker = true;
    public int maxHealth = 10;
    public int attack = 1;
    public int currentHealth = 10;
    
    private GameObject healthLabel;
    private float lastAttackTime = 0f;
    private float attackInterval = 1f; // Attack every second
    
    // Cached FindObjectsByType results to avoid expensive scene searches
    private static UnitCombat[] cachedAllCombatUnits;
    private static float lastCombatUnitCacheUpdate = 0f;
    private const float COMBAT_UNIT_CACHE_UPDATE_INTERVAL = 0.3f;
    
    void Start()
    {
        CreateHealthLabel();
        UpdateHealthLabel();
    }
    
    void Update()
    {
        // Check for nearby enemies to fight
        var nearbyEnemies = FindNearbyEnemies();
        if (nearbyEnemies.Count > 0 && Time.time - lastAttackTime > attackInterval)
        {
            AttackEnemy(nearbyEnemies[0]);
            lastAttackTime = Time.time;
        }
        
        // Update health label position
        if (healthLabel != null)
        {
            healthLabel.transform.position = transform.position + Vector3.up * 2f;
        }
    }
    
    public List<UnitCombat> FindNearbyEnemies()
    {
        var enemies = new List<UnitCombat>();
        
        // Use cached combat units array to avoid expensive FindObjectsByType call
        if (Time.time - lastCombatUnitCacheUpdate > COMBAT_UNIT_CACHE_UPDATE_INTERVAL)
        {
            cachedAllCombatUnits = FindObjectsByType<UnitCombat>(FindObjectsSortMode.None);
            lastCombatUnitCacheUpdate = Time.time;
        }
        
        if (cachedAllCombatUnits != null)
        {
            foreach (var unit in cachedAllCombatUnits)
        {
                if (unit != null && unit != this && unit.isAttacker != this.isAttacker)
            {
                float distance = Vector3.Distance(transform.position, unit.transform.position);
                if (distance < 1.5f) // Fighting range
                {
                    enemies.Add(unit);
                    }
                }
            }
        }
        
        return enemies;
    }
    
    void AttackEnemy(UnitCombat enemy)
    {
        if (enemy == null) return;
        
        enemy.TakeDamage(attack);
        // Debug.Log removed for performance
    }
    
    public void TakeDamage(int damage)
    {
        currentHealth = Mathf.Max(0, currentHealth - damage);
        UpdateHealthLabel();
        
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    void Die()
    {
        // Debug.Log removed for performance
        if (healthLabel != null)
        {
            Destroy(healthLabel);
        }
        Destroy(gameObject);
    }
    
    void CreateHealthLabel()
    {
        // Create a simple text label above the unit
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;
        
        healthLabel = new GameObject("HealthLabel");
        healthLabel.transform.SetParent(canvas.transform, false);
        
        var text = healthLabel.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.color = Color.black;
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 14;
        
        var rect = healthLabel.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(100, 30);
    }
    
    void UpdateHealthLabel()
    {
        if (healthLabel != null)
        {
            var text = healthLabel.GetComponent<Text>();
            text.text = $"{unitName}\n{currentHealth}/{maxHealth} HP";
        }
    }
}

/// <summary>
/// Simple movement script with click controls
/// </summary>
public class SimpleMover : MonoBehaviour
{
    public string targetName = "";
    public float speed = 1f;
    public bool debugLogs = true;
    public UnitCombat combat; // Reference to combat component
    
    private GameObject target;
    private float lastSearch = 0f;
    private Vector3 moveTarget;
    private bool hasMoveTarget = false;
    public bool isSelected = false;
    private GameObject selectionIndicator;
    
    // Cached FindObjectsByType results to avoid expensive scene searches
    private static SimpleMover[] cachedAllMovers;
    private static float lastMoverCacheUpdate = 0f;
    private const float MOVER_CACHE_UPDATE_INTERVAL = 0.3f;
    
    void Start()
    {
        DebugLog("SimpleMover started");
        FindTarget();
        
        // Units start stationary - no automatic movement
        DebugLog("Unit ready - waiting for commands");
    }
    
    void Update()
    {
        // Search for target every 5 seconds to reduce debug spam
        if (Time.time - lastSearch > 5f)
        {
            FindTarget();
            lastSearch = Time.time;
        }
        
        // Check if we're in combat range with an enemy
        if (IsInCombatRange())
        {
            // Stop moving if we're fighting
            hasMoveTarget = false;
            return;
        }
        
        // Handle movement - only move if given a command
        if (hasMoveTarget)
        {
            MoveToTarget();
        }
        // No automatic movement - units wait for player commands
    }
    
    bool IsInCombatRange()
    {
        if (combat == null) return false;
        
        // Check for nearby enemies
        var nearbyEnemies = combat.FindNearbyEnemies();
        return nearbyEnemies.Count > 0;
    }
    
    void MoveToTarget()
    {
        Vector3 direction = (moveTarget - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, moveTarget);
        
        if (distance > 0.1f)
        {
            transform.position += direction * speed * Time.deltaTime;
            DebugLog($"Moving to target. Distance: {distance:F2}");
        }
        else
        {
            DebugLog("Reached target!");
            hasMoveTarget = false;
        }
    }
    
    void OnMouseDown()
    {
        if (Input.GetMouseButtonDown(0)) // Left click
        {
            // Select this unit
            isSelected = true;
            DebugLog("Selected!");
            UpdateSelectionIndicator();
            
            // Deselect other units - use cached movers array to avoid expensive FindObjectsByType call
            if (Time.time - lastMoverCacheUpdate > MOVER_CACHE_UPDATE_INTERVAL)
            {
                cachedAllMovers = FindObjectsByType<SimpleMover>(FindObjectsSortMode.None);
                lastMoverCacheUpdate = Time.time;
            }
            
            if (cachedAllMovers != null)
            {
                foreach (var mover in cachedAllMovers)
            {
                    if (mover != null && mover != this)
                {
                    mover.isSelected = false;
                    mover.UpdateSelectionIndicator();
                    }
                }
            }
        }
    }
    
    void OnMouseOver()
    {
        if (Input.GetMouseButtonDown(1)) // Right click
        {
            // Move selected unit to this position - use cached movers array to avoid expensive FindObjectsByType call
            if (Time.time - lastMoverCacheUpdate > MOVER_CACHE_UPDATE_INTERVAL)
            {
                cachedAllMovers = FindObjectsByType<SimpleMover>(FindObjectsSortMode.None);
                lastMoverCacheUpdate = Time.time;
            }
            
            if (cachedAllMovers != null)
            {
                foreach (var mover in cachedAllMovers)
            {
                    if (mover != null && mover.isSelected)
                {
                    mover.SetMoveTarget(transform.position);
                    DebugLog($"Ordered {mover.gameObject.name} to move to {gameObject.name}");
                    }
                }
            }
        }
    }
    
    public void SetMoveTarget(Vector3 targetPosition)
    {
        moveTarget = targetPosition;
        hasMoveTarget = true;
        DebugLog($"New move target set: {targetPosition}");
    }
    
    void UpdateSelectionIndicator()
    {
        if (isSelected)
        {
            if (selectionIndicator == null)
            {
                // Create selection indicator
                selectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                selectionIndicator.name = "SelectionIndicator";
                selectionIndicator.transform.SetParent(transform);
                selectionIndicator.transform.localPosition = new Vector3(0, -0.5f, 0);
                selectionIndicator.transform.localScale = new Vector3(1.2f, 0.1f, 1.2f);
                
                // Make it a bright color
                var renderer = selectionIndicator.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = Color.yellow;
                }
                
                // Remove collider so it doesn't interfere with clicking
                var collider = selectionIndicator.GetComponent<Collider>();
                if (collider != null)
                {
                    DestroyImmediate(collider);
                }
            }
            selectionIndicator.SetActive(true);
        }
        else
        {
            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(false);
            }
        }
    }
    
    void FindTarget()
    {
        target = GameObject.Find(targetName);
        if (target != null)
        {
            // Only log when target is first found
            if (debugLogs)
            {
                DebugLog($"Found target: {target.name}");
            }
        }
        else
        {
            // Only log once when target is not found, not every time
            if (debugLogs)
            {
                DebugLog($"Target not found: {targetName}");
            }
        }
    }
    
    void DebugLog(string message)
    {
        if (debugLogs)
        {
            // Debug.Log removed for performance
        }
    }
}

/// <summary>
/// Result of a completed battle (from BattleManager)
/// </summary>
[System.Serializable]
public class BattleResult
{
    public Civilization winner;
    public Civilization loser;
    public List<CombatUnit> survivingUnits;
    public int casualties;
    public int experienceGained;
    public Dictionary<ResourceData, int> loot;
    public float battleDuration;
}

/// <summary>
/// Formation types for unit positioning (from BattleManager)
/// </summary>