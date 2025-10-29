using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Super simple battle test - just creates two units that move toward each other
/// No conflicts with existing code
/// </summary>
public class BattleTestSimple : MonoBehaviour
{
    [Header("UI")]
    public Button testButton;
    public TextMeshProUGUI statusText;
    public GameObject uiPanel; // Panel to hide when battle starts
    public TMP_Dropdown attackerUnitDropdown;
    public TMP_Dropdown defenderUnitDropdown;
    public TextMeshProUGUI attackerLabel;
    public TextMeshProUGUI defenderLabel;
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    
    [Header("Camera Controls")]
    public float cameraMoveSpeed = 5f;
    public float cameraZoomSpeed = 2f;
    public float cameraRotateSpeed = 50f;
    public float minZoom = 2f;
    public float maxZoom = 20f;
    
    [Header("Unit Selection")]
    private List<CombatUnitData> availableUnits = new List<CombatUnitData>();
    private CombatUnitData selectedAttackerUnit;
    private CombatUnitData selectedDefenderUnit;
    
    [Header("Civilization Selection")]
    private List<Civilization> availableCivs = new List<Civilization>();
    private Civilization selectedAttackerCiv;
    private Civilization selectedDefenderCiv;
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
    
    [Header("Prefab Caching")]
    private Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
    private bool prefabCacheInitialized = false;
    
    // Selection system variables
    private bool isDragging = false;
    private Vector3 dragStart;
    private Vector3 dragEnd;
    private GameObject selectionBox;
    private List<FormationUnit> selectedFormations = new List<FormationUnit>();
    private List<FormationUnit> allFormations = new List<FormationUnit>();
    
    void Start()
    {
        DebugLog("BattleTestSimple started");
        
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
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Check if clicking on a formation
                FormationUnit clickedFormation = GetFormationAtPosition(hit.point);
                if (clickedFormation != null)
                {
                    // Single formation selection
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
        // Handle right-click to move selected formations
        if (Input.GetMouseButtonDown(1) && selectedFormations.Count > 0)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Move all selected formations to the clicked position
                foreach (var formation in selectedFormations)
                {
                    formation.MoveToPosition(hit.point);
                }
            }
        }
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
        statusText.color = Color.white;
        statusText.alignment = TextAlignmentOptions.Top;
        
        var statusRect = statusGO.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = new Vector2(0, 150);
        statusRect.sizeDelta = new Vector2(400, 100);
        
        // Create unit selection UI
        CreateUnitSelectionUI();
        
        // Create civilization selection UI
        CreateCivilizationSelectionUI();
        
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
        attackerLabel.color = Color.white;
        attackerLabel.fontSize = 14;
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
        defenderLabel.color = Color.white;
        defenderLabel.fontSize = 14;
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
        attackerCivLabel.color = Color.white;
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
        defenderCivLabel.color = Color.white;
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
        availableUnits.Clear();
        
        // MEMORY OPTIMIZATION: Load all unit data (lightweight) but prefabs on-demand
        // This gives you access to all units without memory issues
        LoadAllUnitsOptimized();
        
        DebugLog($"Loaded {availableUnits.Count} unit types (memory optimized)");
    }
    
    void LoadAvailableCivilizations()
    {
        availableCivs.Clear();
        
        try
        {
            // Load civilizations from Resources/Civilizations folder
            var allCivData = Resources.LoadAll<CivData>("Civilizations");
            
            if (allCivData != null && allCivData.Length > 0)
            {
                DebugLog($"Found {allCivData.Length} civilization data files in Resources/Civilizations");
                
                // Create civilization GameObjects for each data file
                foreach (var civData in allCivData)
                {
                    if (civData != null)
                    {
                        var civGO = new GameObject($"Civ_{civData.civName}");
                        var civ = civGO.AddComponent<Civilization>();
                        
                        try
                        {
                            civ.Initialize(civData, null, false);
                            availableCivs.Add(civ);
                            DebugLog($"  - Created civilization: {civData.civName}");
                        }
                        catch (System.Exception e)
                        {
                            DebugLog($"Error initializing civilization {civData.civName}: {e.Message}");
                            DestroyImmediate(civGO);
                        }
                    }
                }
            }
            
            if (availableCivs.Count == 0)
            {
                DebugLog("No civilizations found in Resources/Civilizations, creating default test civilizations");
                CreateDefaultCivilizations();
            }
            else
            {
                DebugLog($"Successfully loaded {availableCivs.Count} civilizations from Resources");
            }
        }
        catch (System.Exception e)
        {
            DebugLog($"Error loading civilizations: {e.Message}");
            CreateDefaultCivilizations();
        }
    }
    
    void CreateDefaultCivilizations()
    {
        try
        {
            // Create attacker civilization
            var attackerCivGO = new GameObject("TestAttackerCiv");
            var attackerCiv = attackerCivGO.AddComponent<Civilization>();
            var attackerCivData = ScriptableObject.CreateInstance<CivData>();
            attackerCivData.civName = "Test Attacker";
            
            // Initialize with proper error handling
            try
            {
                attackerCiv.Initialize(attackerCivData, null, false);
                availableCivs.Add(attackerCiv);
                DebugLog("Created attacker civilization successfully");
            }
            catch (System.Exception e)
            {
                DebugLog($"Error creating attacker civilization: {e.Message}");
                DestroyImmediate(attackerCivGO);
            }
            
            // Create defender civilization
            var defenderCivGO = new GameObject("TestDefenderCiv");
            var defenderCiv = defenderCivGO.AddComponent<Civilization>();
            var defenderCivData = ScriptableObject.CreateInstance<CivData>();
            defenderCivData.civName = "Test Defender";
            
            try
            {
                defenderCiv.Initialize(defenderCivData, null, false);
                availableCivs.Add(defenderCiv);
                DebugLog("Created defender civilization successfully");
            }
            catch (System.Exception e)
            {
                DebugLog($"Error creating defender civilization: {e.Message}");
                DestroyImmediate(defenderCivGO);
            }
            
            DebugLog($"Created {availableCivs.Count} default test civilizations");
        }
        catch (System.Exception e)
        {
            DebugLog($"Error in CreateDefaultCivilizations: {e.Message}");
        }
    }
    
    void LoadAllUnitsOptimized()
    {
        // MEMORY OPTIMIZED: Load ONLY ScriptableObject data, NOT prefabs
        // This prevents memory issues while giving access to all units
        
        try
        {
            // Load ALL CombatUnitData ScriptableObjects from Resources/Units folder
            // This loads ONLY the ScriptableObject data, not prefabs
            var allUnitData = Resources.LoadAll<CombatUnitData>("Units");
            
            if (allUnitData != null && allUnitData.Length > 0)
            {
                // Clear prefab references immediately to save memory
                foreach (var unitData in allUnitData)
                {
                    if (unitData != null)
                    {
                        // Clear prefab reference to prevent memory issues
                        unitData.prefab = null;
                        availableUnits.Add(unitData);
                    }
                }
                
                DebugLog($"Loaded {availableUnits.Count} unit types (data only - prefabs will load on-demand)");
                
                // Log some unit names for verification
                for (int i = 0; i < Mathf.Min(5, availableUnits.Count); i++)
                {
                    DebugLog($"  - {availableUnits[i].unitName} (prefab: NO - will load on-demand)");
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
        // Create minimal fallback units if none found
        var fallbackData = ScriptableObject.CreateInstance<CombatUnitData>();
        fallbackData.unitName = "Fallback Unit";
        fallbackData.baseAttack = 5;
        fallbackData.baseHealth = 10;
        fallbackData.baseDefense = 3;
        availableUnits.Add(fallbackData);
        DebugLog("Created fallback unit data");
    }
    
    void SetupUnitDropdowns()
    {
        if (attackerUnitDropdown == null || defenderUnitDropdown == null) return;
        
        // Clear existing options
        attackerUnitDropdown.ClearOptions();
        defenderUnitDropdown.ClearOptions();
        
        // Add unit options
        var options = new List<string>();
        foreach (var unit in availableUnits)
        {
            options.Add($"{unit.unitName} (HP:{unit.baseHealth}, ATK:{unit.baseAttack})");
        }
        
        // If no units found, add fallback
        if (options.Count == 0)
        {
            options.Add("Default Unit (HP:10, ATK:2)");
        }
        
        attackerUnitDropdown.AddOptions(options);
        defenderUnitDropdown.AddOptions(options);
        
        // Set default selections
        selectedAttackerUnit = availableUnits.Count > 0 ? availableUnits[0] : null;
        selectedDefenderUnit = availableUnits.Count > 0 ? availableUnits[0] : null;
        
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
            string civName = civ.civData != null ? civ.civData.civName : "Unknown Civilization";
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
        
        // Set default selections
        selectedAttackerCiv = availableCivs.Count > 0 ? availableCivs[0] : null;
        selectedDefenderCiv = availableCivs.Count > 1 ? availableCivs[1] : availableCivs.Count > 0 ? availableCivs[0] : null;
        
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
        if (index >= 0 && index < availableUnits.Count)
        {
            selectedAttackerUnit = availableUnits[index];
            DebugLog($"Selected attacker unit: {selectedAttackerUnit.unitName}");
        }
    }
    
    void OnDefenderUnitChanged(int index)
    {
        if (index >= 0 && index < availableUnits.Count)
        {
            selectedDefenderUnit = availableUnits[index];
            DebugLog($"Selected defender unit: {selectedDefenderUnit.unitName}");
        }
    }
    
    void OnAttackerCivChanged(int index)
    {
        if (index >= 0 && index < availableCivs.Count)
        {
            selectedAttackerCiv = availableCivs[index];
            string civName = selectedAttackerCiv.civData != null ? selectedAttackerCiv.civData.civName : "Unknown";
            DebugLog($"Selected attacker civilization: {civName}");
        }
    }
    
    void OnDefenderCivChanged(int index)
    {
        if (index >= 0 && index < availableCivs.Count)
        {
            selectedDefenderCiv = availableCivs[index];
            string civName = selectedDefenderCiv.civData != null ? selectedDefenderCiv.civData.civName : "Unknown";
            DebugLog($"Selected defender civilization: {civName}");
        }
    }
    
    public void StartTest()
    {
        Debug.Log("=== BUTTON CLICKED! ===");
        UpdateStatus("Starting test...");
        
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
        
        // Create ground
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(20, 1, 20);
        ground.name = "TestGround";
        ground.transform.position = Vector3.zero;
        
        // Create attacker formations with better spacing
        for (int i = 0; i < formationsPerSide; i++)
        {
            Vector3 position = new Vector3(-15, 0, (i - 1) * 12); // More spacing between formations
            CreateFormation($"AttackerFormation{i + 1}", position, Color.red, true);
        }
        
        // Create defender formations with better spacing
        for (int i = 0; i < formationsPerSide; i++)
        {
            Vector3 position = new Vector3(15, 0, (i - 1) * 12); // More spacing between formations
            CreateFormation($"DefenderFormation{i + 1}", position, Color.blue, false);
        }
        
        UpdateStatus("Battle started! Left-click to select formations, drag to select multiple. Right-click to move!");
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
        // Create formation GameObject
        GameObject formationGO = new GameObject(formationName);
        formationGO.transform.position = position;
        
        // Add FormationUnit component
        FormationUnit formation = formationGO.AddComponent<FormationUnit>();
        formation.formationName = formationName;
        formation.isAttacker = isAttacker;
        formation.teamColor = teamColor;
        formation.formationCenter = position;
        formation.totalHealth = isAttacker ? 90 : 72; // 10 per soldier * 9 soldiers
        formation.totalAttack = isAttacker ? 18 : 9;  // 2 per soldier * 9 soldiers
        formation.currentHealth = formation.totalHealth;
        
        // Create soldiers in formation with proper spacing
        CreateSoldiersInFormation(formation, position, teamColor);
        
        // Add to formations list
        allFormations.Add(formation);
        
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
                    soldier.transform.SetParent(formation.transform);
                    
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
                    
                    formation.soldiers.Add(soldier);
                    DebugLog($"Successfully created and added soldier {i + 1} to formation");
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
                // Check if prefab is already loaded, if not load it on-demand
                if (unitData.prefab == null)
                {
                    // Find the actual prefab using cached lookup
                    GameObject loadedPrefab = FindPrefabInCache(unitData.unitName);
                    
                    if (loadedPrefab != null)
                    {
                        unitData.prefab = loadedPrefab;
                        DebugLog($"Found prefab for {unitData.unitName}: {loadedPrefab.name}");
                    }
                    else
                    {
                        DebugLog($"No prefab found for {unitData.unitName}");
                    }
                }
                
                if (unitData.prefab != null)
                {
                    // Use actual unit prefab
                    soldier = Instantiate(unitData.prefab, position, Quaternion.identity);
                    soldier.name = soldierName;
                    DebugLog($"Created {soldierName} using prefab: {unitData.unitName}");
                }
                else
                {
                    // Fallback to simple unit if prefab not available
                    soldier = CreateFallbackSoldier(soldierName, position, teamColor);
                    DebugLog($"Created {soldierName} using fallback (prefab not found)");
                }
            }
            else
            {
                // No unit data available
                soldier = CreateFallbackSoldier(soldierName, position, teamColor);
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
                    // Use selected civilization instead of creating temporary one
                    Civilization selectedCiv = isAttacker ? selectedAttackerCiv : selectedDefenderCiv;
                    
                    if (selectedCiv != null)
                    {
                        combatUnit.Initialize(unitData, selectedCiv);
                        DebugLog($"Initialized {soldierName} with unit data and selected civilization");
                    }
                    else
                    {
                        // Fallback to temporary civilization if none selected
                        var tempCiv = CreateTemporaryCivilization(isAttacker);
                        combatUnit.Initialize(unitData, tempCiv);
                        DebugLog($"Initialized {soldierName} with unit data and temporary civilization");
                    }
                }
                catch (System.Exception e)
                {
                    DebugLog($"ERROR initializing {soldierName}: {e.Message}");
                    // Continue without initialization - soldier will still work
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
            var collider = soldier.GetComponent<CapsuleCollider>();
            if (collider == null)
            {
                collider = soldier.AddComponent<CapsuleCollider>();
            }
            collider.isTrigger = false;
            
            return soldier;
        }
        catch (System.Exception e)
        {
            DebugLog($"ERROR creating soldier {soldierName}: {e.Message}");
            return null;
        }
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
        if (prefabCacheInitialized) return;
        
        DebugLog("Initializing prefab cache...");
        
        try
        {
            // Only search in Units folder to avoid loading everything
            var allPrefabs = Resources.LoadAll<GameObject>("Units");
            
            foreach (var prefab in allPrefabs)
            {
                if (prefab == null) continue;
                
                // Store prefab with its name as key
                string prefabName = prefab.name.ToLower();
                prefabCache[prefabName] = prefab;
                
                // Also store common variations
                if (prefabName.EndsWith("prefab"))
                {
                    string baseName = prefabName.Substring(0, prefabName.Length - 6);
                    prefabCache[baseName] = prefab;
                }
                else if (prefabName.EndsWith("_prefab"))
                {
                    string baseName = prefabName.Substring(0, prefabName.Length - 7);
                    prefabCache[baseName] = prefab;
                }
                else if (prefabName.EndsWith("model"))
                {
                    string baseName = prefabName.Substring(0, prefabName.Length - 5);
                    prefabCache[baseName] = prefab;
                }
                else if (prefabName.EndsWith("_model"))
                {
                    string baseName = prefabName.Substring(0, prefabName.Length - 6);
                    prefabCache[baseName] = prefab;
                }
            }
            
            prefabCacheInitialized = true;
            DebugLog($"Prefab cache initialized with {prefabCache.Count} entries");
        }
        catch (System.Exception e)
        {
            DebugLog($"Error initializing prefab cache: {e.Message}");
            prefabCacheInitialized = true; // Mark as initialized to avoid retrying
        }
    }
    
    GameObject FindPrefabInCache(string unitName)
    {
        // Initialize cache if needed
        if (!prefabCacheInitialized)
        {
            InitializePrefabCache();
        }
        
        // Look up in cache
        string searchName = unitName.ToLower();
        
        if (prefabCache.TryGetValue(searchName, out GameObject prefab))
        {
            DebugLog($"Found prefab in cache: {prefab.name} for unit {unitName}");
            return prefab;
        }
        
        DebugLog($"No prefab found in cache for unit: {unitName}");
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
        // Clear prefab references to free memory
        // Note: Can't use Resources.UnloadAsset on GameObjects, just clear references
        foreach (var unitData in availableUnits)
        {
            if (unitData != null && unitData.prefab != null)
            {
                // Just clear the reference - Unity will garbage collect if not used
                unitData.prefab = null;
            }
        }
        DebugLog("Cleared unit prefab references to free memory");
    }
    
    void ClearPrefabCache()
    {
        prefabCache.Clear();
        prefabCacheInitialized = false;
        DebugLog("Cleared prefab cache");
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
        if (statusText != null)
        {
            statusText.text = message;
        }
        DebugLog($"Status: {message}");
    }
    
    void DebugLog(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[BattleTestSimple] {message}");
        }
    }
    
    // Selection system methods
    FormationUnit GetFormationAtPosition(Vector3 position)
    {
        foreach (var formation in allFormations)
        {
            if (Vector3.Distance(formation.transform.position, position) < 5f)
            {
                return formation;
            }
        }
        return null;
    }
    
    void ClearSelection()
    {
        foreach (var formation in selectedFormations)
        {
            formation.SetSelected(false);
        }
        selectedFormations.Clear();
    }
    
    void SelectFormation(FormationUnit formation)
    {
        if (!selectedFormations.Contains(formation))
        {
            selectedFormations.Add(formation);
            formation.SetSelected(true);
        }
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
            if (selectionBounds.Contains(formation.transform.position))
            {
                SelectFormation(formation);
            }
        }
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
    
    private CombatUnit[] soldierCombatUnits;
    private Renderer[] selectionRenderers;
    
    void Start()
    {
        // Get all soldier combat units
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
        
        // Calculate formation center
        UpdateFormationCenter();
    }
    
    void Update()
    {
        if (isMoving)
        {
            MoveFormation();
        }
        else
        {
            PlayIdleAnimations();
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
            // Move formation center
            formationCenter += direction * moveSpeed * Time.deltaTime;
            
            // Update soldier positions
            UpdateSoldierPositions();
            
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
                soldiers[i].transform.position = formationCenter + offset;
            }
        }
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
        if (soldiers.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (var soldier in soldiers)
            {
                if (soldier != null)
                {
                    sum += soldier.transform.position;
                }
            }
            formationCenter = sum / soldiers.Count;
        }
    }
    
    bool CheckForEnemies()
    {
        // Find enemy formations
        var allFormations = FindObjectsByType<FormationUnit>(FindObjectsSortMode.None);
        foreach (var formation in allFormations)
        {
            if (formation.isAttacker != this.isAttacker)
            {
                float distance = Vector3.Distance(formationCenter, formation.formationCenter);
                if (distance < formationRadius * 2f)
                {
                    // Start combat with enemy formation
                    StartCombatWithFormation(formation);
                    return true;
                }
            }
        }
        return false;
    }
    
    void StartCombatWithFormation(FormationUnit enemyFormation)
    {
        // Both formations are now in combat
        PlayFightingAnimations();
        enemyFormation.PlayFightingAnimations();
        
        // Start combat damage over time
        StartCoroutine(CombatDamageCoroutine(enemyFormation));
    }
    
    System.Collections.IEnumerator CombatDamageCoroutine(FormationUnit enemyFormation)
    {
        while (Vector3.Distance(formationCenter, enemyFormation.formationCenter) < formationRadius * 2f)
        {
            // Deal damage to each other
            yield return new WaitForSeconds(1f); // Damage every second
            
            // Attack animation
            PlayAttackAnimation();
            enemyFormation.PlayAttackAnimation();
            
            // Apply damage
            ApplyDamageToFormation(enemyFormation);
            enemyFormation.ApplyDamageToFormation(this);
            
            // Check if formation is dead
            if (currentHealth <= 0)
            {
                PlayDeathAnimation();
                DestroyFormation();
                yield break;
            }
            
            if (enemyFormation.currentHealth <= 0)
            {
                enemyFormation.PlayDeathAnimation();
                enemyFormation.DestroyFormation();
                yield break;
            }
        }
        
        // Combat ended - return to idle
        PlayIdleAnimations();
        enemyFormation.PlayIdleAnimations();
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
                    combatUnit.TriggerAnimation("Death");
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
        // Destroy all soldiers in formation
        foreach (var soldier in soldiers)
        {
            if (soldier != null)
            {
                Destroy(soldier);
            }
        }
        
        // Destroy formation GameObject
        Destroy(gameObject);
    }
    
    void StopMoving()
    {
        isMoving = false;
        PlayIdleAnimations();
    }
    
    void PlayWalkingAnimations()
    {
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Use CombatUnit's existing animation system
                combatUnit.isMoving = true;
                // CombatUnit will handle the actual animation triggers
            }
        }
    }
    
    void PlayIdleAnimations()
    {
        foreach (var combatUnit in soldierCombatUnits)
        {
            if (combatUnit != null)
            {
                // Use CombatUnit's existing animation system
                combatUnit.isMoving = false;
                // CombatUnit will handle the actual animation triggers
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
        text.color = Color.white;
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
