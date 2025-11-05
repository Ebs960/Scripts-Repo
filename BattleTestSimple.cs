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
    
    [Header("Battle HUD")]
    [Tooltip("Manually assigned battle HUD panel (with HorizontalLayoutGroup for formation buttons). If null, will auto-generate.")]
    public GameObject battleHUDPanel;
    [Tooltip("Layout group for formation buttons inside the battle HUD panel. If null, will try to find or create one.")]
    public HorizontalLayoutGroup formationButtonLayout;
    
    [Header("Battle HUD Button Settings")]
    [Tooltip("Button prefab to use for formation buttons. If null, will auto-generate buttons with the settings below.")]
    public GameObject formationButtonPrefab;
    [Tooltip("Button size (width, height) when auto-generating buttons")]
    public Vector2 buttonSize = new Vector2(240, 44);
    [Tooltip("Button background color when auto-generating buttons")]
    public Color buttonColor = new Color(0, 0, 0, 0.5f);
    [Tooltip("Button text color when auto-generating buttons")]
    public Color buttonTextColor = Color.white;
    [Tooltip("Button text font size when auto-generating buttons")]
    public float buttonFontSize = 20f;
    [Tooltip("Text format for formation buttons.\nPlaceholders: {0}=formationName, {1}=aliveCount, {2}=morale, {3}=totalHealth, {4}=currentHealth, {5}=totalAttack\nExample: \"{0} - {1}/{6} alive - {2}% morale - {3} HP\"")]
    [TextArea(3, 5)]
    public string buttonTextFormat = "{0}  |  {1}  |  Morale {2}%";
    
    [Header("Battle HUD Layout Settings")]
    [Tooltip("Button spacing in the layout group (only used if creating layout group)")]
    public float buttonSpacing = 8f;
    [Tooltip("Layout padding left (only used if creating layout group)")]
    public int layoutPaddingLeft = 10;
    [Tooltip("Layout padding right (only used if creating layout group)")]
    public int layoutPaddingRight = 10;
    [Tooltip("Layout padding top (only used if creating layout group)")]
    public int layoutPaddingTop = 5;
    [Tooltip("Layout padding bottom (only used if creating layout group)")]
    public int layoutPaddingBottom = 5;
    
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
    
    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
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
        // If no specific battlefield mask set, prefer the "Battlefield" layer only
        if (battlefieldLayers == ~0)
        {
            int bf = LayerMask.NameToLayer("Battlefield");
            if (bf != -1) battlefieldLayers = (1 << bf);
        }
        
        // Load available units and civilizations
        LoadAvailableUnits();
        LoadAvailableCivilizations();
        
        // Create UI if needed
        if (testButton == null)
        {
            CreateUI();
        }
        
        // Connect button and dropdowns
        if (testButton != null)
        {
            testButton.onClick.AddListener(StartTest);
            DebugLog("Button connected successfully");
        }
        
        SetupUnitDropdowns();
        SetupCivilizationDropdowns();
        
        UpdateStatus("Select units, civilizations, and click Start Battle!");
    }
    
    void Update()
    {
        // Handle pause key
        if (battleInProgress && Input.GetKeyDown(pauseKey))
        {
            TogglePause();
        }
        
        if (isPaused) return; // Don't update game systems when paused
        
        // Handle camera controls
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
            
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            
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
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
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
            
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Project destination onto battlefield ground
                var grounded = GetGroundPosition(hit.point);
                
                // Move all selected formations - all soldiers in each formation will move
                DebugLog($"Moving {selectedFormations.Count} selected formation(s) to {grounded}");
                foreach (var formation in selectedFormations)
                {
                    if (formation != null)
                    {
                        formation.MoveToPosition(grounded);
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
        Vector3 moveDirection = Vector3.zero;
        
        // WASD movement (relative to camera direction)
        if (Input.GetKey(KeyCode.W)) moveDirection += Camera.main.transform.forward;
        if (Input.GetKey(KeyCode.S)) moveDirection -= Camera.main.transform.forward;
        if (Input.GetKey(KeyCode.A)) moveDirection -= Camera.main.transform.right;
        if (Input.GetKey(KeyCode.D)) moveDirection += Camera.main.transform.right;
        
        // Apply movement
        if (moveDirection != Vector3.zero)
        {
            // Keep Y movement flat (no flying up/down)
            moveDirection.y = 0;
            moveDirection.Normalize();
            Camera.main.transform.Translate(moveDirection * cameraMoveSpeed * Time.deltaTime, Space.World);
        }
        
        // Q/E for left/right rotation (camera rotates in place)
        if (Input.GetKey(KeyCode.Q))
        {
            Camera.main.transform.Rotate(0, -cameraRotateSpeed * Time.deltaTime, 0, Space.World);
        }
        if (Input.GetKey(KeyCode.E))
        {
            Camera.main.transform.Rotate(0, cameraRotateSpeed * Time.deltaTime, 0, Space.World);
        }
        
        // X/C for up/down rotation (camera tilts up/down)
        if (Input.GetKey(KeyCode.X))
        {
            Camera.main.transform.Rotate(-cameraRotateSpeed * Time.deltaTime, 0, 0, Space.Self);
        }
        if (Input.GetKey(KeyCode.C))
        {
            Camera.main.transform.Rotate(cameraRotateSpeed * Time.deltaTime, 0, 0, Space.Self);
        }
    }
    
    void HandleCameraZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            // Simple forward/backward zoom
            Vector3 zoomDirection = Camera.main.transform.forward;
            Camera.main.transform.Translate(zoomDirection * scroll * cameraZoomSpeed, Space.World);
        }
    }
    
    void CreateUI()
    {
        DebugLog("Creating UI...");
        
        // Create canvas on this same GameObject
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();
        
        // Create UI panel to hide when battle starts
        uiPanel = new GameObject("UIPanel");
        uiPanel.transform.SetParent(transform, false);
        var panelRect = uiPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        // Create button
        var buttonGO = new GameObject("TestButton");
        buttonGO.transform.SetParent(uiPanel.transform, false);
        testButton = buttonGO.AddComponent<Button>();
        
        // Add button image
        var image = buttonGO.AddComponent<Image>();
        image.color = Color.green;
        
        // Add button text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = "Start Test";
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        
        // Position button
        var rectTransform = buttonGO.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(200, 50);
        
        // Create status text
        var statusGO = new GameObject("StatusText");
        statusGO.transform.SetParent(uiPanel.transform, false);
        statusText = statusGO.AddComponent<TextMeshProUGUI>();
        statusText.color = Color.black;
        statusText.alignment = TextAlignmentOptions.Top;
        
        var statusRect = statusGO.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = new Vector2(0, 150);
        statusRect.sizeDelta = new Vector2(400, 100);
        
        // Minimal setup panel only with Start button; battle HUD will be created after battle starts
        // Remove legacy dropdown-driven UI per new requirements
        
        DebugLog("UI created successfully");
    }
    
    void CreateUnitSelectionUI()
    {
        // Create attacker selection
        var attackerGroup = new GameObject("AttackerGroup");
        attackerGroup.transform.SetParent(uiPanel.transform, false);
        var attackerRect = attackerGroup.AddComponent<RectTransform>();
        attackerRect.anchorMin = new Vector2(0.5f, 0.5f);
        attackerRect.anchorMax = new Vector2(0.5f, 0.5f);
        attackerRect.anchoredPosition = new Vector2(-150, 50);
        attackerRect.sizeDelta = new Vector2(200, 60);
        
        // Attacker label
        var attackerLabelGO = new GameObject("AttackerLabel");
        attackerLabelGO.transform.SetParent(attackerGroup.transform, false);
        attackerLabel = attackerLabelGO.AddComponent<TextMeshProUGUI>();
        attackerLabel.text = "Attacker Unit:";
        attackerLabel.color = Color.black;
        attackerLabel.fontSize = 9.5f;
        var labelRect = attackerLabelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(0, 30);
        labelRect.offsetMax = new Vector2(0, 0);
        
        // Attacker dropdown
        var attackerDropdownGO = new GameObject("AttackerDropdown");
        attackerDropdownGO.transform.SetParent(attackerGroup.transform, false);
        attackerUnitDropdown = attackerDropdownGO.AddComponent<TMP_Dropdown>();
        var dropdownRect = attackerDropdownGO.GetComponent<RectTransform>();
        dropdownRect.anchorMin = Vector2.zero;
        dropdownRect.anchorMax = Vector2.one;
        dropdownRect.offsetMin = new Vector2(0, 0);
        dropdownRect.offsetMax = new Vector2(0, 30);
        
        // Create defender selection
        var defenderGroup = new GameObject("DefenderGroup");
        defenderGroup.transform.SetParent(uiPanel.transform, false);
        var defenderRect = defenderGroup.AddComponent<RectTransform>();
        defenderRect.anchorMin = new Vector2(0.5f, 0.5f);
        defenderRect.anchorMax = new Vector2(0.5f, 0.5f);
        defenderRect.anchoredPosition = new Vector2(150, 50);
        defenderRect.sizeDelta = new Vector2(200, 60);
        
        // Defender label
        var defenderLabelGO = new GameObject("DefenderLabel");
        defenderLabelGO.transform.SetParent(defenderGroup.transform, false);
        defenderLabel = defenderLabelGO.AddComponent<TextMeshProUGUI>();
        defenderLabel.text = "Defender Unit:";
        defenderLabel.color = Color.black;
        defenderLabel.fontSize = 9.5f;
        var defenderLabelRect = defenderLabelGO.GetComponent<RectTransform>();
        defenderLabelRect.anchorMin = Vector2.zero;
        defenderLabelRect.anchorMax = Vector2.one;
        defenderLabelRect.offsetMin = new Vector2(0, 30);
        defenderLabelRect.offsetMax = new Vector2(0, 0);
        
        // Defender dropdown
        var defenderDropdownGO = new GameObject("DefenderDropdown");
        defenderDropdownGO.transform.SetParent(defenderGroup.transform, false);
        defenderUnitDropdown = defenderDropdownGO.AddComponent<TMP_Dropdown>();
        var defenderDropdownRect = defenderDropdownGO.GetComponent<RectTransform>();
        defenderDropdownRect.anchorMin = Vector2.zero;
        defenderDropdownRect.anchorMax = Vector2.one;
        defenderDropdownRect.offsetMin = new Vector2(0, 0);
        defenderDropdownRect.offsetMax = new Vector2(0, 30);
    }
    
    void CreateCivilizationSelectionUI()
    {
        // Create attacker civilization selection
        var attackerCivGroup = new GameObject("AttackerCivGroup");
        attackerCivGroup.transform.SetParent(uiPanel.transform, false);
        var attackerCivRect = attackerCivGroup.AddComponent<RectTransform>();
        attackerCivRect.anchorMin = new Vector2(0.5f, 0.5f);
        attackerCivRect.anchorMax = new Vector2(0.5f, 0.5f);
        attackerCivRect.anchoredPosition = new Vector2(-150, -50);
        attackerCivRect.sizeDelta = new Vector2(200, 60);
        
        // Attacker civ label
        var attackerCivLabelGO = new GameObject("AttackerCivLabel");
        attackerCivLabelGO.transform.SetParent(attackerCivGroup.transform, false);
        attackerCivLabel = attackerCivLabelGO.AddComponent<TextMeshProUGUI>();
        attackerCivLabel.text = "Attacker Civ:";
        attackerCivLabel.color = Color.black;
        attackerCivLabel.fontSize = 14;
        var civLabelRect = attackerCivLabelGO.GetComponent<RectTransform>();
        civLabelRect.anchorMin = Vector2.zero;
        civLabelRect.anchorMax = Vector2.one;
        civLabelRect.offsetMin = new Vector2(0, 30);
        civLabelRect.offsetMax = new Vector2(0, 0);
        
        // Attacker civ dropdown
        var attackerCivDropdownGO = new GameObject("AttackerCivDropdown");
        attackerCivDropdownGO.transform.SetParent(attackerCivGroup.transform, false);
        attackerCivDropdown = attackerCivDropdownGO.AddComponent<TMP_Dropdown>();
        var civDropdownRect = attackerCivDropdownGO.GetComponent<RectTransform>();
        civDropdownRect.anchorMin = Vector2.zero;
        civDropdownRect.anchorMax = Vector2.one;
        civDropdownRect.offsetMin = new Vector2(0, 0);
        civDropdownRect.offsetMax = new Vector2(0, 30);
        
        // Create defender civilization selection
        var defenderCivGroup = new GameObject("DefenderCivGroup");
        defenderCivGroup.transform.SetParent(uiPanel.transform, false);
        var defenderCivRect = defenderCivGroup.AddComponent<RectTransform>();
        defenderCivRect.anchorMin = new Vector2(0.5f, 0.5f);
        defenderCivRect.anchorMax = new Vector2(0.5f, 0.5f);
        defenderCivRect.anchoredPosition = new Vector2(150, -50);
        defenderCivRect.sizeDelta = new Vector2(200, 60);
        
        // Defender civ label
        var defenderCivLabelGO = new GameObject("DefenderCivLabel");
        defenderCivLabelGO.transform.SetParent(defenderCivGroup.transform, false);
        defenderCivLabel = defenderCivLabelGO.AddComponent<TextMeshProUGUI>();
        defenderCivLabel.text = "Defender Civ:";
        defenderCivLabel.color = Color.black;
        defenderCivLabel.fontSize = 14;
        var defenderCivLabelRect = defenderCivLabelGO.GetComponent<RectTransform>();
        defenderCivLabelRect.anchorMin = Vector2.zero;
        defenderCivLabelRect.anchorMax = Vector2.one;
        defenderCivLabelRect.offsetMin = new Vector2(0, 30);
        defenderCivLabelRect.offsetMax = new Vector2(0, 0);
        
        // Defender civ dropdown
        var defenderCivDropdownGO = new GameObject("DefenderCivDropdown");
        defenderCivDropdownGO.transform.SetParent(defenderCivGroup.transform, false);
        defenderCivDropdown = defenderCivDropdownGO.AddComponent<TMP_Dropdown>();
        var defenderCivDropdownRect = defenderCivDropdownGO.GetComponent<RectTransform>();
        defenderCivDropdownRect.anchorMin = Vector2.zero;
        defenderCivDropdownRect.anchorMax = Vector2.one;
        defenderCivDropdownRect.offsetMin = new Vector2(0, 0);
        defenderCivDropdownRect.offsetMax = new Vector2(0, 30);
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
            var allUnitData = Resources.LoadAll<CombatUnitData>("Units");
            
            if (allUnitData != null && allUnitData.Length > 0)
            {
                // Extract only lightweight metadata (names and stats)
                // DO NOT store references to ScriptableObjects - this prevents memory retention
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
                
                // Clear the loaded array - metadata is now in our lightweight list
                // Clear array references so Unity can unload ScriptableObjects
                System.Array.Clear(allUnitData, 0, allUnitData.Length);
                allUnitData = null; // Remove reference
                
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
                
                // CRITICAL: Unload unused assets immediately to free GPU memory
                // This unloads ScriptableObjects and their referenced prefabs/textures/materials
                // that were loaded by Resources.LoadAll but are no longer referenced
                Resources.UnloadUnusedAssets();
                
                // Force garbage collection to ensure memory is actually freed
                System.GC.Collect();
                
                DebugLog("Unloaded unused assets to free GPU/CPU memory");
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
            var allCivData = Resources.LoadAll<CivData>("Civilizations");
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
    
    void LoadAllUnitsOptimized()
    {
        // MEMORY OPTIMIZED: Load ONLY ScriptableObject data, NOT prefabs
        // This prevents memory issues while giving access to all units
        
        try
        {
            // Load ALL CombatUnitData ScriptableObjects from Resources/Units folder
            // This loads ONLY the ScriptableObject data; prefab references on the SO remain as metadata and are not instantiated
            var allUnitData = Resources.LoadAll<CombatUnitData>("Units");
            
            if (allUnitData != null && allUnitData.Length > 0)
            {
                // Clear prefab references immediately to save memory
                foreach (var unitData in allUnitData)
                {
                    if (unitData != null)
                    {
                        // Keep prefab reference on the ScriptableObject; we will only instantiate on Start
                        availableUnits.Add(unitData);
                    }
                }
                
                DebugLog($"Loaded {availableUnits.Count} unit types (data-only; prefabs referenced via SO)");
                
                // Log some unit names for verification
                for (int i = 0; i < Mathf.Min(5, availableUnits.Count); i++)
                {
                    DebugLog($"  - {availableUnits[i].unitName} (prefab ref present: {(availableUnits[i].prefab != null)})");
                }
                if (availableUnits.Count > 5)
                {
                    DebugLog($"  ... and {availableUnits.Count - 5} more units");
                }
            }
            else
            {
                DebugLog("No CombatUnitData found in Resources/Units folder");
                CreateFallbackUnits();
            }
        }
        catch (System.Exception e)
        {
            DebugLog($"Error loading units: {e.Message}");
            CreateFallbackUnits();
        }
    }
    
    void CreateFallbackUnits()
    {
        // Create minimal fallback units if none found (used when loading full unit data)
        var fallbackData = ScriptableObject.CreateInstance<CombatUnitData>();
        fallbackData.unitName = "Fallback Unit";
        fallbackData.baseAttack = 5;
        fallbackData.baseHealth = 10;
        fallbackData.baseDefense = 3;
        availableUnits.Add(fallbackData);
        DebugLog("Created fallback unit data");
    }
    
    void ResolveSelectedUnitsFromMetadata()
    {
        // Map selected metadata indices to actual CombatUnitData objects by matching unit names
        // This is called after LoadAllUnitsOptimized() to resolve the user's dropdown selections
        
        if (availableUnits.Count == 0)
        {
            DebugLog("Warning: No units loaded, cannot resolve selected units from metadata");
            selectedAttackerUnit = null;
            selectedDefenderUnit = null;
            return;
        }
        
        // Resolve attacker unit
        if (selectedAttackerUnitIndex >= 0 && selectedAttackerUnitIndex < unitMetadata.Count)
        {
            var selectedMetadata = unitMetadata[selectedAttackerUnitIndex];
            selectedAttackerUnit = availableUnits.Find(u => u != null && u.unitName == selectedMetadata.unitName);
            
            if (selectedAttackerUnit == null)
            {
                DebugLog($"Warning: Could not find attacker unit '{selectedMetadata.unitName}' in loaded units, using first available");
                // Find first non-null unit
                selectedAttackerUnit = availableUnits.Find(u => u != null);
                if (selectedAttackerUnit == null)
                {
                    DebugLog("ERROR: No valid units found in availableUnits list!");
                }
            }
            else
            {
                DebugLog($"Resolved attacker unit: {selectedAttackerUnit.unitName}");
            }
        }
        else
        {
            DebugLog("Warning: Invalid attacker unit index, using first available");
            // Find first non-null unit
            selectedAttackerUnit = availableUnits.Find(u => u != null);
            if (selectedAttackerUnit == null)
            {
                DebugLog("ERROR: No valid units found in availableUnits list!");
            }
        }
        
        // Resolve defender unit
        if (selectedDefenderUnitIndex >= 0 && selectedDefenderUnitIndex < unitMetadata.Count)
        {
            var selectedMetadata = unitMetadata[selectedDefenderUnitIndex];
            selectedDefenderUnit = availableUnits.Find(u => u != null && u.unitName == selectedMetadata.unitName);
            
            if (selectedDefenderUnit == null)
            {
                DebugLog($"Warning: Could not find defender unit '{selectedMetadata.unitName}' in loaded units, using first available");
                // Find first non-null unit
                selectedDefenderUnit = availableUnits.Find(u => u != null);
                if (selectedDefenderUnit == null)
                {
                    DebugLog("ERROR: No valid units found in availableUnits list!");
                }
            }
            else
            {
                DebugLog($"Resolved defender unit: {selectedDefenderUnit.unitName}");
            }
        }
        else
        {
            DebugLog("Warning: Invalid defender unit index, using first available");
            // Find first non-null unit
            selectedDefenderUnit = availableUnits.Find(u => u != null);
            if (selectedDefenderUnit == null)
            {
                DebugLog("ERROR: No valid units found in availableUnits list!");
            }
        }
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
        
        // Add civilization options
        var options = new List<string>();
        foreach (var civ in availableCivs)
        {
            string civName = civ != null ? civ.civName : "Unknown Civilization";
            options.Add(civName);
            DebugLog($"  - Added civilization option: {civName}");
        }
        
        // If no civilizations found, add fallback
        if (options.Count == 0)
        {
            options.Add("Default Attacker");
            options.Add("Default Defender");
        }
        
        attackerCivDropdown.AddOptions(options);
        defenderCivDropdown.AddOptions(options);
        
        DebugLog($"Added {options.Count} options to civilization dropdowns");
        
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
    
    public void StartTest()
    {
        Debug.Log("=== BUTTON CLICKED! ===");
        UpdateStatus("Starting test...");
        
        // Load full unit data now (only when battle starts, not in menu)
        // Then resolve the selected metadata indices to actual CombatUnitData objects
        LoadAllUnitsOptimized();
        
        // Resolve selected unit indices from metadata to actual CombatUnitData
        ResolveSelectedUnitsFromMetadata();
        
        try
        {
            // Check for GameManager first
            var gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null)
            {
                gameManager.StartBattleTest();
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
        }
        
        // Instantiate only the two selected civilizations now (data-only until here)
        attackerCivInstance = InstantiateSelectedCiv(selectedAttackerCivData, "AttackerCiv_Instance");
        defenderCivInstance = InstantiateSelectedCiv(selectedDefenderCivData, "DefenderCiv_Instance");

        // Create attacker formations
        List<Vector3> attackerSpawns = mapGenerator != null ? mapGenerator.GetAttackerSpawnPoints() : GetDefaultAttackerSpawns();
        for (int i = 0; i < formationsPerSide && i < attackerSpawns.Count; i++)
        {
            CreateFormation($"AttackerFormation{i + 1}", attackerSpawns[i], Color.red, true);
        }
        
        // Create defender formations
        List<Vector3> defenderSpawns = mapGenerator != null ? mapGenerator.GetDefenderSpawnPoints() : GetDefaultDefenderSpawns();
        for (int i = 0; i < formationsPerSide && i < defenderSpawns.Count; i++)
        {
            CreateFormation($"DefenderFormation{i + 1}", defenderSpawns[i], Color.blue, false);
        }
        
        // Initialize victory manager BEFORE spawning based on menu selections
        // This way we know how many units will be in the battle before we start
        if (victoryManager != null)
        {
            InitializeVictoryManagerFromMenu();
        }
        
        // Also initialize after spawning as a fallback (in case units aren't properly initialized)
        StartCoroutine(InitializeVictoryManagerDelayed());
        
        // Build simple battle HUD along the top with formation info buttons
        CreateBattleHUD();
        
        // Initialize AI system
        InitializeFormationAI();
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
        var attackers = allUnits.Where(u => u != null && u.isAttacker).ToList();
        var defenders = allUnits.Where(u => u != null && !u.isAttacker).ToList();
        
        // Debug: Log unit details to diagnose why units might not be counted
        DebugLog($"Found {allUnits.Count} total units: {attackers.Count} attackers, {defenders.Count} defenders");
        
        // Log sample units from both sides
        foreach (var u in allUnits.Take(10))
        {
            if (u != null)
            {
                DebugLog($"  Unit: {u.name}, isAttacker={u.isAttacker}, hasData={u.data != null}, health={u.currentHealth}, parent={u.transform.parent?.name ?? "null"}");
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
                    foreach (var s in f.soldiers.Take(3))
                    {
                        if (s != null)
                        {
                            var cu = s.GetComponent<CombatUnit>();
                            DebugLog($"    Soldier: {s.name}, hasCombatUnit={cu != null}, parent={s.transform.parent?.name ?? "null"}");
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
        // Clean up when component is destroyed
        CleanupPreviousTest();
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
        
        // Destroy test ground
        var ground = GameObject.Find("TestGround");
        if (ground != null)
        {
            DestroyImmediate(ground);
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
        
        // Force aggressive memory cleanup
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        
        // Unload prefabs from unit data to free memory
        UnloadUnitPrefabs();
        
        // Clear prefab cache to free memory
        ClearPrefabCache();
        
        // Force another cleanup after unloading
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        
        DebugLog("Cleaned up previous test objects and unloaded prefabs");
    }
    
    void CreateFormation(string formationName, Vector3 position, Color teamColor, bool isAttacker)
    {
        // Create formation GameObject (ground immediately)
        GameObject formationGO = new GameObject(formationName);
        formationGO.transform.position = GetGroundPosition(position);
        
        // Add FormationUnit component
        FormationUnit formation = formationGO.AddComponent<FormationUnit>();
        formation.formationName = formationName;
        formation.isAttacker = isAttacker;
        formation.teamColor = teamColor;
        formation.formationCenter = position;
        formation.totalHealth = isAttacker ? 90 : 72; // 10 per soldier * 9 soldiers
        formation.totalAttack = isAttacker ? 18 : 9;  // 2 per soldier * 9 soldiers
        formation.currentHealth = formation.totalHealth;
        
        // Add to formations list FIRST - this allows GetFormationFromUnit to work during soldier creation
        allFormations.Add(formation);
        
        // Create soldiers in formation with proper spacing
        CreateSoldiersInFormation(formation, formationGO.transform.position, teamColor);
        
        // Create world-space badge UI above the formation
        formation.CreateOrUpdateBadgeUI();
        
        DebugLog($"Created {formationName} with {soldiersPerFormation} soldiers");
    }
    
    void CreateSoldiersInFormation(FormationUnit formation, Vector3 centerPosition, Color teamColor)
    {
        DebugLog($"Creating {soldiersPerFormation} soldiers for formation {formation.formationName}");
        
        // Calculate formation positions in a 3x3 grid
        int sideLength = Mathf.CeilToInt(Mathf.Sqrt(soldiersPerFormation));
        float spacing = 3f; // Increased distance between soldiers
        
        for (int i = 0; i < soldiersPerFormation; i++)
        {
            try
            {
                // Calculate position in grid
                int x = i % sideLength;
                int z = i / sideLength;
                
                Vector3 soldierPosition = centerPosition + new Vector3(
                    (x - sideLength / 2f) * spacing,
                    0,
                    (z - sideLength / 2f) * spacing
                );
                
                DebugLog($"Creating soldier {i + 1} at position {soldierPosition}");
                
                // Create soldier using existing CombatUnit system
                GameObject soldier = CreateCombatUnitSoldier($"Soldier{i + 1}", soldierPosition, teamColor, formation.isAttacker);
                
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
                    
                    // Trigger idle animation after initialization
                    var animator = soldier.GetComponent<Animator>();
                    if (animator != null)
                    {
                        // Try to trigger idle animation like CombatUnit does
                        try
                        {
                            animator.SetTrigger("idleYoung");
                        }
                        catch (System.Exception e)
                        {
                            DebugLog($"Could not trigger idle animation for Soldier{i + 1}: {e.Message}");
                        }
                    }
                    
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
    }
    
    GameObject CreateCombatUnitSoldier(string soldierName, Vector3 position, Color teamColor, bool isAttacker)
    {
        try
        {
            // Try to use actual unit prefab if available
            CombatUnitData unitData = isAttacker ? selectedAttackerUnit : selectedDefenderUnit;
            GameObject soldier;
            
            if (unitData != null)
            {
                // Prefer the prefab reference on the ScriptableObject; no name-based lookup
                
                if (unitData.prefab != null)
                {
                    // Use actual unit prefab
                    soldier = Instantiate(unitData.prefab, position, Quaternion.identity);
                    soldier.name = soldierName;
                    // Put soldier on Units layer if present
                    int uLayer = LayerMask.NameToLayer("Units"); if (uLayer != -1) soldier.layer = uLayer;
                    DebugLog($"Created {soldierName} using prefab: {unitData.unitName}");
                }
                else
                {
                    // Fallback to simple unit if prefab not available
                    soldier = CreateFallbackSoldier(soldierName, position, teamColor);
                    int uLayer = LayerMask.NameToLayer("Units"); if (uLayer != -1) soldier.layer = uLayer;
                    DebugLog($"Created {soldierName} using fallback (prefab not found)");
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
                combatUnit = soldier.AddComponent<CombatUnit>();
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
            
            // Initialize with unit data if available
            if (unitData != null)
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
                DebugLog($"No unit data available for {soldierName}, creating basic soldier without initialization");
                // Set basic properties manually for fallback soldiers
                combatUnit.isAttacker = isAttacker;
                combatUnit.battleState = BattleUnitState.Idle;
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
            
            // Log to verify civ data is set
            DebugLog($"Created {runtimeName} with civ data: {civData.civName}");
            
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
        // Keep prefab references on ScriptableObjects so we can instantiate real models when spawning.
        // We are not clearing unitData.prefab anymore to avoid breaking spawning.
        DebugLog("Keeping unit prefab references (no unload)");
    }
    
    void ClearPrefabCache()
    {
        if (onDemandPrefabCache != null)
        {
            onDemandPrefabCache.Clear();
            DebugLog("Cleared on-demand prefab cache");
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
    
    void CreateTestUnit(string name, Vector3 position, Color color, bool isAttacker)
    {
        DebugLog($"Creating {name} at {position}");
        
        var unit = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        unit.name = name;
        unit.transform.position = position;
        
        // Add collider for mouse detection
        var collider = unit.GetComponent<Collider>();
        if (collider == null)
        {
            collider = unit.AddComponent<CapsuleCollider>();
        }
        
        // Color it
        var renderer = unit.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = color;
        }
        
        // Add combat stats
        var combat = unit.AddComponent<UnitCombat>();
        combat.unitName = name;
        combat.isAttacker = isAttacker;
        combat.maxHealth = isAttacker ? 10 : 8;
        combat.attack = isAttacker ? 2 : 1;
        combat.currentHealth = combat.maxHealth;
        
        // Add simple movement script
        var mover = unit.AddComponent<SimpleMover>();
        mover.targetName = isAttacker ? "Defender" : "Attacker";
        mover.speed = 2f;
        mover.debugLogs = showDebugLogs;
        mover.combat = combat; // Link combat to mover
        
        DebugLog($"{name} created with {combat.maxHealth} health and {combat.attack} attack");
    }
    
    void CreateRealUnitFormation(string teamName, CombatUnitData unitData, Vector3 centerPosition, Color teamColor, bool isAttacker)
    {
        DebugLog($"Creating {teamName} formation with {unitData.unitName}");
        
        // Get formation settings from unit data
        int formationSize = unitData.formationSize;
        float formationSpacing = unitData.formationSpacing;
        FormationShape formationShape = unitData.formationShape;
        
        // Create formation positions
        var positions = CalculateFormationPositions(centerPosition, formationSize, formationSpacing, formationShape);
        
        // Create units in formation
        for (int i = 0; i < formationSize && i < positions.Count; i++)
        {
            string unitName = $"{teamName}{i + 1}";
            Vector3 unitPosition = positions[i];
            
            GameObject unitGO;
            
            // Try to instantiate the actual unit prefab
            if (unitData.prefab != null)
            {
                unitGO = Instantiate(unitData.prefab, unitPosition, Quaternion.identity);
                unitGO.name = unitName;
                DebugLog($"Instantiated prefab for {unitName}");
            }
            else
            {
                // Fallback: create simple unit if no prefab
                unitGO = new GameObject(unitName);
                unitGO.transform.position = unitPosition;
                
                // Add collider
                var collider = unitGO.AddComponent<CapsuleCollider>();
                
                // Color it
                var renderer = unitGO.AddComponent<MeshRenderer>();
                var meshFilter = unitGO.AddComponent<MeshFilter>();
                meshFilter.mesh = CreateCapsuleMesh();
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = teamColor;
                
                DebugLog($"Created fallback unit for {unitName} (no prefab found)");
            }
            
            // Add combat stats based on unit data
            var combat = unitGO.AddComponent<UnitCombat>();
            combat.unitName = unitName;
            combat.isAttacker = isAttacker;
            combat.maxHealth = unitData.baseHealth;
            combat.attack = unitData.baseAttack;
            combat.currentHealth = combat.maxHealth;
            
            // Add movement script
            var mover = unitGO.AddComponent<SimpleMover>();
            mover.targetName = isAttacker ? "Defender" : "Attacker";
            mover.speed = 2f;
            mover.debugLogs = false; // Reduce debug spam
            mover.combat = combat;
            
            DebugLog($"Created {unitName} at {unitPosition} (HP: {combat.maxHealth}, Attack: {combat.attack})");
        }
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
    
    Mesh CreateCapsuleMesh()
    {
        // Create a simple capsule mesh for units
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        var mesh = capsule.GetComponent<MeshFilter>().mesh;
        DestroyImmediate(capsule);
        return mesh;
    }
    
    void UpdateStatus(string message)
    {
        // Trimmed per new UI requirements; keep debug log only
        DebugLog($"Status: {message}");
    }

    // --- Battle HUD: Consolidated to use BattleUI exclusively ---
    private void CreateBattleHUD()
    {
        // Find or get BattleUI component
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
        battleUI.UpdateFormationsList(allFormations);
        DebugLog("Using BattleUI component exclusively for battle HUD");
    }
    
    void DebugLog(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[BattleTestSimple] {message}");
        }
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
            if (Vector3.Distance(tf.position, position) < 5f)
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
            StartCoroutine(LoadBattleScene());
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
                mapGenerator = FindFirstObjectByType<BattleMapGenerator>();
            }

            // Generate battle map if map generator exists and is enabled
            if (mapGenerator != null && generateNewMap)
            {
                mapGenerator.GenerateBattleMap(battleMapSize, attackerUnits.Count, defenderUnits.Count);
            }
            else if (mapGenerator == null)
            {
                // Fallback: create simple ground if no map generator
                var ground = GameObject.Find("TestGround");
                if (ground == null)
                {
                    ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    ground.transform.localScale = new Vector3(20, 1, 20);
                    ground.name = "TestGround";
                    ground.transform.position = Vector3.zero;
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
    /// Initialize formation-based AI system
    /// </summary>
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
        
        // Add units to formation
        formation.soldiers = new List<GameObject>();
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] != null)
            {
                units[i].transform.SetParent(formation.transform);
                formation.soldiers.Add(units[i].gameObject);
                
                // Initialize unit for battle
                units[i].InitializeForBattle(isAttacker);
            }
        }
        
        // Calculate formation stats
        int totalHealth = 0;
        int totalAttack = 0;
        foreach (var unit in units)
        {
            if (unit != null)
            {
                totalHealth += unit.MaxHealth;
                totalAttack += unit.CurrentAttack;
            }
        }
        formation.totalHealth = totalHealth;
        formation.totalAttack = totalAttack;
        formation.currentHealth = totalHealth;
        
        // Add to formations list
        allFormations.Add(formation);
        
        // Create world-space badge UI
        formation.CreateOrUpdateBadgeUI();
        
        DebugLog($"Created {formationName} with {units.Count} units");
    }
    
    /// <summary>
    /// Set up battle camera
    /// </summary>
    private void SetupBattleCamera()
    {
        Camera battleCamera = Camera.main;
        if (battleCamera == null)
        {
            GameObject cameraGO = new GameObject("BattleCamera");
            battleCamera = cameraGO.AddComponent<Camera>();
        }

        // Position camera to overview the battlefield
        battleCamera.transform.position = new Vector3(0, 30, -20);
        battleCamera.transform.rotation = Quaternion.Euler(45, 0, 0);
        battleCamera.orthographic = true;
        battleCamera.orthographicSize = 25f;
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

        // Optionally return to main game scene
        if (result != null)
        {
            StartCoroutine(ReturnToMainGame(result));
        }
    }
    
    private IEnumerator ReturnToMainGame(BattleResult result)
    {
        // Load the main game scene
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("MainGame");
        
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        DebugLog("[BattleTestSimple] Returned to main game");
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
    public Color teamColor;
    public List<GameObject> soldiers = new List<GameObject>();
    public Vector3 formationCenter;
    public float formationRadius = 3f;
    
    [Header("Movement")]
    public float moveSpeed = 3f;
    public bool isMoving = false;
    public Vector3 targetPosition;
    public bool isSelected = false;
    
    [Header("Combat")]
    public int totalHealth;
    public int totalAttack;
    public int currentHealth;
    
    [Header("Morale")]
    public int currentMorale = 100;
    public int routingMoraleThreshold = 15;
    public bool isRouted = false;
    
    private CombatUnit[] soldierCombatUnits;
    private Renderer[] selectionRenderers;
    
    // Track active combat coroutines to prevent duplicates
    private Coroutine activeCombatCoroutine;
    
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
        // Unregister this formation when destroyed
        if (FormationAIManager.Instance != null)
        {
            FormationAIManager.Instance.UnregisterFormation(this);
        }
    }
    
    /// <summary>
    /// Refresh soldier arrays - call this when soldiers are added/removed
    /// </summary>
    void RefreshSoldierArrays()
    {
        if (soldiers == null) return;
        
        soldierCombatUnits = new CombatUnit[soldiers.Count];
        selectionRenderers = new Renderer[soldiers.Count];
        
        for (int i = 0; i < soldiers.Count; i++)
        {
            if (soldiers[i] != null)
            {
                soldierCombatUnits[i] = soldiers[i].GetComponent<CombatUnit>();
                selectionRenderers[i] = soldiers[i].GetComponent<Renderer>();
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
                PlayIdleAnimations();
                wasMovingLastFrame = false;
            }
        }
        UpdateBadgeContents();
    }
    
    /// <summary>
    /// Ensure walking animation stays active without resetting triggers
    /// </summary>
    void EnsureWalkingAnimationActive()
    {
        if (soldierCombatUnits == null) return;
        
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Just ensure isMoving is true - the property setter will automatically update IsWalking animator parameter
                // Setting it again is safe even if already true (property setter checks for changes)
                combatUnit.isMoving = true;
            }
        }
    }
    
    public void MoveToPosition(Vector3 position)
    {
        targetPosition = position;
        isMoving = true;
        PlayWalkingAnimations();
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
            
            // Move formation center
            formationCenter += direction * moveSpeed * Time.deltaTime;
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
    
    void UpdateSoldierPositions()
    {
        // Arrange soldiers in formation around the center
        for (int i = 0; i < soldiers.Count; i++)
        {
            if (soldiers[i] != null)
            {
                Vector3 offset = GetFormationOffset(i);
                Vector3 desired = formationCenter + offset;
                soldiers[i].transform.position = Ground(desired);
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
        
        float spacing = 1.5f;
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
            formationCenter = sum / validCount;
        }
    }
    
    // Cache enemy formations to avoid expensive FindObjectsByType calls
    private List<FormationUnit> cachedEnemyFormations = new List<FormationUnit>();
    private float lastEnemyFormationUpdate = 0f;
    private const float ENEMY_FORMATION_UPDATE_INTERVAL = 0.5f; // Update every 0.5 seconds
    
    bool CheckForEnemies()
    {
        // Update cached enemy formations periodically (not every frame)
        if (Time.time - lastEnemyFormationUpdate > ENEMY_FORMATION_UPDATE_INTERVAL)
        {
            UpdateCachedEnemyFormations();
            lastEnemyFormationUpdate = Time.time;
        }
        
        // Check cached formations
        foreach (var formation in cachedEnemyFormations)
        {
            if (formation == null) continue; // Skip destroyed formations
            if (formation.isAttacker == this.isAttacker) continue; // Skip same team
            
            float distance = Vector3.Distance(formationCenter, formation.formationCenter);
            if (distance < formationRadius * 2f)
            {
                // Start combat with enemy formation
                StartCombatWithFormation(formation);
                return true;
            }
        }
        return false;
    }
    
    void UpdateCachedEnemyFormations()
    {
        cachedEnemyFormations.Clear();
        var allFormations = FindObjectsByType<FormationUnit>(FindObjectsSortMode.None);
        foreach (var formation in allFormations)
        {
            if (formation != null && formation.isAttacker != this.isAttacker)
            {
                cachedEnemyFormations.Add(formation);
            }
        }
    }
    
    void StartCombatWithFormation(FormationUnit enemyFormation)
    {
        // Prevent starting multiple combat coroutines for the same formation
        if (activeCombatCoroutine != null)
        {
            return; // Already in combat
        }
        
        if (enemyFormation == null) return;
        
        // Both formations are now in combat
        PlayFightingAnimations();
        enemyFormation.PlayFightingAnimations();
        
        // Start combat damage over time
        activeCombatCoroutine = StartCoroutine(CombatDamageCoroutine(enemyFormation));
    }
    
    System.Collections.IEnumerator CombatDamageCoroutine(FormationUnit enemyFormation)
    {
        var tick = new WaitForSeconds(0.6f);
        
        while (enemyFormation != null && Vector3.Distance(formationCenter, enemyFormation.formationCenter) < formationRadius * 2f)
        {
            yield return tick;
            
            // Safety checks
            if (this == null || enemyFormation == null) break;
            if (soldiers == null || enemyFormation.soldiers == null) break;
            
            // Build alive lists
            var myAlive = new List<GameObject>();
            var enAlive = new List<GameObject>();
            foreach (var s in soldiers) if (s != null) myAlive.Add(s);
            foreach (var s in enemyFormation.soldiers) if (s != null) enAlive.Add(s);
            if (myAlive.Count == 0 || enAlive.Count == 0) break;

            // Determine front directions
            Vector3 dirToEnemy = (enemyFormation.formationCenter - formationCenter).normalized;
            Vector3 dirToMe = -dirToEnemy;

            // Select front lines (closest along forward axis)
            List<GameObject> myFront = SelectFrontLine(myAlive, formationCenter, dirToEnemy, 6);
            List<GameObject> enFront = SelectFrontLine(enAlive, enemyFormation.formationCenter, dirToMe, 6);

            // Pair by nearest
            int pairs = Mathf.Min(myFront.Count, enFront.Count);
            for (int i = 0; i < pairs; i++)
            {
                var a = myFront[i];
                var b = FindNearest(a.transform.position, enFront);
                if (b == null) continue;

                var aCU = a.GetComponent<CombatUnit>();
                var bCU = b.GetComponent<CombatUnit>();
                if (aCU == null || bCU == null) continue;

                // Trigger attack visuals
                aCU.TriggerAnimation("Attack");
                bCU.TriggerAnimation("Hit");

                // Resolve simple damage using existing stat accessors
                int dmgAB = Mathf.Max(0, aCU.CurrentAttack - bCU.CurrentDefense);
                bool bDied = bCU.ApplyDamage(dmgAB, aCU, true);
                if (bDied)
                {
                    HandleSoldierDeath(enemyFormation, b);
                    enemyFormation.currentMorale = Mathf.Max(0, enemyFormation.currentMorale - 5);
                }

                // Counter if still alive
                if (!bDied)
                {
                    int dmgBA = Mathf.Max(0, bCU.CurrentAttack - aCU.CurrentDefense);
                    bool aDied = aCU.ApplyDamage(dmgBA, bCU, true);
                    if (aDied)
                    {
                        HandleSoldierDeath(this, a);
                        currentMorale = Mathf.Max(0, currentMorale - 5);
                    }
                }
            }

            // Reflow positions and check routing
            RemoveNullSoldiers();
            enemyFormation.RemoveNullSoldiers();
            UpdateSoldierPositions();
            enemyFormation.UpdateSoldierPositions();

            if (!isRouted && currentMorale <= routingMoraleThreshold) isRouted = true;
            if (!enemyFormation.isRouted && enemyFormation.currentMorale <= enemyFormation.routingMoraleThreshold) enemyFormation.isRouted = true;

            // End if either formation is wiped
            if (soldiers.Count == 0)
            {
                PlayDeathAnimation();
                DestroyFormation();
                yield break;
            }
            if (enemyFormation.soldiers.Count == 0)
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
    }

    private List<GameObject> SelectFrontLine(List<GameObject> alive, Vector3 center, Vector3 forward, int count)
    {
        // Score by projection along forward (smaller distance toward enemy gets priority)
        return alive.OrderBy(s => Vector3.Dot((s.transform.position - center), forward)).Take(count).ToList();
    }

    private GameObject FindNearest(Vector3 pos, List<GameObject> candidates)
    {
        float best = float.MaxValue; GameObject bestGO = null;
        foreach (var c in candidates)
        {
            if (c == null) continue;
            float d = (c.transform.position - pos).sqrMagnitude;
            if (d < best) { best = d; bestGO = c; }
        }
        return bestGO;
    }

    private void HandleSoldierDeath(FormationUnit owner, GameObject soldier)
    {
        var cu = soldier != null ? soldier.GetComponent<CombatUnit>() : null;
        if (cu != null) cu.TriggerAnimation("death");
        if (soldier != null)
        {
            owner.soldiers.Remove(soldier);
            Destroy(soldier, 1.2f);
        }
    }

    private void RemoveNullSoldiers()
    {
        int removed = 0;
        for (int i = soldiers.Count - 1; i >= 0; i--)
        {
            if (soldiers[i] == null) 
            {
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
    
    void ApplyDamageToFormation(FormationUnit targetFormation)
    {
        // Calculate damage based on attack vs defense
        int damage = Mathf.Max(1, totalAttack - targetFormation.totalAttack / 2);
        targetFormation.currentHealth = Mathf.Max(0, targetFormation.currentHealth - damage);
        
        // Play hit animation on target
        targetFormation.PlayHitAnimation();
        
        // Check if individual soldiers should die
        targetFormation.CheckForSoldierDeaths();
        
        Debug.Log($"{formationName} deals {damage} damage to {targetFormation.formationName} (HP: {targetFormation.currentHealth}/{targetFormation.totalHealth})");
    }
    
    void CheckForSoldierDeaths()
    {
        // Calculate how many soldiers should be alive based on health percentage
        float healthPercentage = (float)currentHealth / totalHealth;
        int soldiersAlive = Mathf.CeilToInt(soldiers.Count * healthPercentage);
        
        // Kill excess soldiers
        for (int i = soldiersAlive; i < soldiers.Count; i++)
        {
            if (soldiers[i] != null)
            {
                // Play death animation for this soldier
                var combatUnit = soldiers[i].GetComponent<CombatUnit>();
                if (combatUnit != null)
                {
                    combatUnit.TriggerAnimation("death");
                }
                
                // Destroy the soldier after a delay
                StartCoroutine(DestroySoldierAfterDelay(soldiers[i], 2f));
            }
        }
    }
    
    System.Collections.IEnumerator DestroySoldierAfterDelay(GameObject soldier, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (soldier != null)
        {
            soldiers.Remove(soldier);
            Destroy(soldier);
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
        
        if (soldierCombatUnits == null) return;
        
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Mark unit as in battle (this prevents UnitMovementController from overriding)
                combatUnit.IsInBattle = true;
                
                // Set walking state - CombatUnit.isMoving property will automatically update IsWalking animator parameter
                combatUnit.isMoving = true;
            }
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

    // --- Badge UI helpers ---
    public void CreateOrUpdateBadgeUI()
    {
        if (badgeCanvas == null)
        {
            var go = new GameObject("FormationBadgeUI");
            // Don't parent WorldSpace canvas - it uses world coordinates
            // go.transform.SetParent(transform, false);
            
            // Put UI on UI layer so it doesn't block unit raycasts
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer != -1)
            {
                go.layer = uiLayer;
            }
            
            badgeCanvas = go.AddComponent<Canvas>();
            badgeCanvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            var raycaster = go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            
            // Disable raycasting on UI elements so they don't block unit selection
            // We'll handle UI interaction separately
            raycaster.enabled = false;
            
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            
            // Put text on UI layer too
            if (uiLayer != -1)
            {
                textGO.layer = uiLayer;
            }
            
            badgeText = textGO.AddComponent<TMPro.TextMeshProUGUI>();
            badgeText.alignment = TMPro.TextAlignmentOptions.Center;
            badgeText.fontSize = 0.5f;
            badgeText.raycastTarget = false; // Don't block raycasts
            var rt = badgeText.rectTransform;
            rt.sizeDelta = new Vector2(5, 5); // Small badge size
        }
        UpdateBadgeContents();
        UpdateBadgePosition();
    }

    private void UpdateBadgeContents()
    {
        if (badgeText == null) return;
        int alive = 0; foreach (var s in soldiers) if (s != null) alive++;
        badgeText.text = $"{formationName}\nMorale {currentMorale}%  |  {alive} units";
    }

    private void UpdateBadgePosition()
    {
        if (badgeCanvas == null) return;
        // Position badge 4 units above formation center (formationCenter should already be on ground)
        // Since badge canvas is WorldSpace and not parented, we use world position directly
        Vector3 groundedCenter = Ground(formationCenter);
        Vector3 badgePos = groundedCenter + new Vector3(0, 4f, 0); // 4 units above ground level
        badgeCanvas.transform.position = badgePos;
        // Face camera
        var cam = Camera.main; 
        if (cam != null)
        {
            badgeCanvas.transform.rotation = Quaternion.LookRotation(badgeCanvas.transform.position - cam.transform.position);
        }
    }
    
    void PlayIdleAnimations()
    {
        // Check if soldierCombatUnits array is initialized
        if (soldierCombatUnits == null || soldierCombatUnits.Length == 0)
        {
            RefreshSoldierArrays();
        }
        
        if (soldierCombatUnits == null) return;
        
        // Reset walking animation flag so we can reinitialize next time
        walkingAnimationsInitialized = false;
        
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Set walking state to false - CombatUnit.isMoving property will automatically update IsWalking animator parameter
                // This will also trigger UpdateIdleAnimation() which handles idle state properly
                combatUnit.isMoving = false;
            }
        }
    }
    
    void PlayFightingAnimations()
    {
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Use CombatUnit's existing animation system
                combatUnit.battleState = BattleUnitState.Attacking;
                combatUnit.TriggerAnimation("Attack");
            }
        }
    }
    
    void PlayAttackAnimation()
    {
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                combatUnit.TriggerAnimation("Attack");
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
        var allUnits = FindObjectsByType<UnitCombat>(FindObjectsSortMode.None);
        
        foreach (var unit in allUnits)
        {
            if (unit != this && unit.isAttacker != this.isAttacker)
            {
                float distance = Vector3.Distance(transform.position, unit.transform.position);
                if (distance < 1.5f) // Fighting range
                {
                    enemies.Add(unit);
                }
            }
        }
        
        return enemies;
    }
    
    void AttackEnemy(UnitCombat enemy)
    {
        if (enemy == null) return;
        
        enemy.TakeDamage(attack);
        Debug.Log($"{unitName} attacks {enemy.unitName} for {attack} damage! {enemy.unitName} has {enemy.currentHealth} health left.");
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
        Debug.Log($"{unitName} has been defeated!");
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
            
            // Deselect other units
            var allMovers = FindObjectsByType<SimpleMover>(FindObjectsSortMode.None);
            foreach (var mover in allMovers)
            {
                if (mover != this)
                {
                    mover.isSelected = false;
                    mover.UpdateSelectionIndicator();
                }
            }
        }
    }
    
    void OnMouseOver()
    {
        if (Input.GetMouseButtonDown(1)) // Right click
        {
            // Move selected unit to this position
            var allMovers = FindObjectsByType<SimpleMover>(FindObjectsSortMode.None);
            foreach (var mover in allMovers)
            {
                if (mover.isSelected)
                {
                    mover.SetMoveTarget(transform.position);
                    DebugLog($"Ordered {mover.gameObject.name} to move to {gameObject.name}");
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
            Debug.Log($"[SimpleMover-{gameObject.name}] {message}");
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
public enum FormationType
{
    Line,       // Standard line formation
    Square,     // Defensive square formation
    Wedge,      // Offensive wedge formation
    Column,     // Fast movement column
    Skirmish    // Loose skirmish formation
}

/// <summary>
/// Data for different formation types (from BattleManager)
/// </summary>
[System.Serializable]
public class FormationData
{
    public FormationType type;
    public string name;
    public float spacing;
    public int maxUnitsPerRow;
    public string description;
}

