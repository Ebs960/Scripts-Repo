using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// Manages real-time Total War style battles between civilizations
/// </summary>
public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [Header("Battle Settings")]
    [Tooltip("Prefab for the battle scene setup")]
    public GameObject battleScenePrefab;
    [Tooltip("Size of the battle map in world units")]
    public float battleMapSize = 100f;
    [Tooltip("Distance between units in formation")]
    public float unitSpacing = 2f;
    [Tooltip("Distance between attacker and defender formations")]
    public float formationSpacing = 20f;

    [Header("Battle UI")]
    [Tooltip("Prefab for battle UI panel")]
    public GameObject battleUIPrefab;
    [Tooltip("Prefab for unit selection indicator")]
    public GameObject selectionIndicatorPrefab;
    
    [Header("Battle Controls")]
    [Tooltip("Key to pause/resume battle")]
    public KeyCode pauseKey = KeyCode.Escape;

    [Header("Formations")]
    [Tooltip("Available formation types for units")]
    public FormationData[] availableFormations;

    // Battle state
    private bool battleInProgress = false;
    private bool isPaused = false;
    public Civilization attacker;
    public Civilization defender;
    private List<CombatUnit> attackerUnits = new List<CombatUnit>();
    private List<CombatUnit> defenderUnits = new List<CombatUnit>();
    private List<CombatUnit> selectedUnits = new List<CombatUnit>();
    private BattleMapGenerator mapGenerator;
    private BattleUI battleUI;
    private BattleAIManager aiManager;

    // Events
    public System.Action<BattleResult> OnBattleEnded;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Start a battle between two civilizations
    /// </summary>
    public void StartBattle(Civilization attackerCiv, Civilization defenderCiv, 
                          List<CombatUnit> attackerUnitsList, List<CombatUnit> defenderUnitsList)
    {
        if (battleInProgress)
        {
            Debug.LogWarning("[BattleManager] Battle already in progress!");
            return;
        }

        attacker = attackerCiv;
        defender = defenderCiv;
        attackerUnits = new List<CombatUnit>(attackerUnitsList);
        defenderUnits = new List<CombatUnit>(defenderUnitsList);

        Debug.Log($"[BattleManager] Starting battle: {attacker.civData.civName} vs {defender.civData.civName}");
        Debug.Log($"[BattleManager] Units: {attackerUnits.Count} vs {defenderUnits.Count}");

        StartCoroutine(LoadBattleScene());
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

            // Find or create map generator
            mapGenerator = FindFirstObjectByType<BattleMapGenerator>();
            if (mapGenerator == null)
            {
                GameObject mapGenGO = new GameObject("BattleMapGenerator");
                mapGenerator = mapGenGO.AddComponent<BattleMapGenerator>();
            }

            // Generate battle map
            mapGenerator.GenerateBattleMap(battleMapSize, attackerUnits.Count, defenderUnits.Count);

            // Position units in formations
            PositionUnitsInFormations();

            // Initialize battle UI
            InitializeBattleUI();
            
            // Initialize AI
            InitializeAI();

            // Set up camera
            SetupBattleCamera();

            Debug.Log("[BattleManager] Battle initialized successfully!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BattleManager] Error initializing battle: {e.Message}");
            EndBattle(null);
        }
    }

    private void PositionUnitsInFormations()
    {
        // Position attacker units on the left side
        Vector3 attackerPosition = new Vector3(-formationSpacing, 0, 0);
        PositionUnitsInFormation(attackerUnits, attackerPosition, FormationType.Line, true);

        // Position defender units on the right side
        Vector3 defenderPosition = new Vector3(formationSpacing, 0, 0);
        PositionUnitsInFormation(defenderUnits, defenderPosition, FormationType.Line, false);
    }

    private void PositionUnitsInFormation(List<CombatUnit> units, Vector3 centerPosition, 
                                       FormationType formation, bool isAttacker)
    {
        if (units == null || units.Count == 0) return;

        // Calculate formation layout for all units (including single units)
        int unitsPerRow = Mathf.CeilToInt(Mathf.Sqrt(units.Count));
        int rows = Mathf.CeilToInt((float)units.Count / unitsPerRow);

        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] == null) continue;

            int row = i / unitsPerRow;
            int col = i % unitsPerRow;

            // Calculate position within formation
            Vector3 offset = new Vector3(
                (col - unitsPerRow / 2f) * unitSpacing,
                0,
                (row - rows / 2f) * unitSpacing
            );

            Vector3 unitPosition = centerPosition + offset;

            // Instantiate unit in battle scene
            GameObject unitGO = Instantiate(units[i].gameObject, unitPosition, Quaternion.identity);
            CombatUnit battleUnit = unitGO.GetComponent<CombatUnit>();
            
            if (battleUnit != null)
            {
                // Set up battle unit
                battleUnit.InitializeForBattle(isAttacker);
                
                // Add to battle units list
                if (isAttacker)
                    attackerUnits[i] = battleUnit;
                else
                    defenderUnits[i] = battleUnit;
            }
        }
    }

    private void InitializeBattleUI()
    {
        if (battleUIPrefab != null)
        {
            GameObject uiGO = Instantiate(battleUIPrefab);
            battleUI = uiGO.GetComponent<BattleUI>();
            if (battleUI != null)
            {
                battleUI.Initialize(this);
            }
        }
    }

    private void SetupBattleCamera()
    {
        // Find or create battle camera
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
    
    private void InitializeAI()
    {
        // Create AI manager
        GameObject aiManagerGO = new GameObject("BattleAIManager");
        aiManager = aiManagerGO.AddComponent<BattleAIManager>();
        
        // Add AI components to all units
        foreach (var unit in attackerUnits)
        {
            if (unit != null)
            {
                if (unit.GetComponent<BattleAI>() == null)
                {
                    unit.gameObject.AddComponent<BattleAI>();
                }
                if (unit.GetComponent<TacticalScripts>() == null)
                {
                    unit.gameObject.AddComponent<TacticalScripts>();
                }
                if (unit.GetComponent<EnhancedTargetSelection>() == null)
                {
                    unit.gameObject.AddComponent<EnhancedTargetSelection>();
                }
            }
        }
        
        foreach (var unit in defenderUnits)
        {
            if (unit != null)
            {
                if (unit.GetComponent<BattleAI>() == null)
                {
                    unit.gameObject.AddComponent<BattleAI>();
                }
                if (unit.GetComponent<TacticalScripts>() == null)
                {
                    unit.gameObject.AddComponent<TacticalScripts>();
                }
                if (unit.GetComponent<EnhancedTargetSelection>() == null)
                {
                    unit.gameObject.AddComponent<EnhancedTargetSelection>();
                }
            }
        }
        
        Debug.Log("[BattleManager] AI system initialized.");
    }

    /// <summary>
    /// Select units for battle control
    /// </summary>
    public void SelectUnits(List<CombatUnit> units)
    {
        // Clear previous selection
        ClearSelection();

        selectedUnits = new List<CombatUnit>(units);
        
        // Add selection indicators
        foreach (var unit in selectedUnits)
        {
            if (unit != null)
            {
                AddSelectionIndicator(unit);
            }
        }

        Debug.Log($"[BattleManager] Selected {selectedUnits.Count} units");
    }

    /// <summary>
    /// Clear current unit selection
    /// </summary>
    public void ClearSelection()
    {
        // Remove selection indicators
        foreach (var unit in selectedUnits)
        {
            if (unit != null)
            {
                RemoveSelectionIndicator(unit);
            }
        }

        selectedUnits.Clear();
    }

    private void AddSelectionIndicator(CombatUnit unit)
    {
        if (selectionIndicatorPrefab != null)
        {
            GameObject indicator = Instantiate(selectionIndicatorPrefab, unit.transform);
            indicator.name = "SelectionIndicator";
        }
    }

    private void RemoveSelectionIndicator(CombatUnit unit)
    {
        Transform indicator = unit.transform.Find("SelectionIndicator");
        if (indicator != null)
        {
            Destroy(indicator.gameObject);
        }
    }

    /// <summary>
    /// Move selected units to target position
    /// </summary>
    public void MoveSelectedUnits(Vector3 targetPosition)
    {
        if (selectedUnits == null || selectedUnits.Count == 0) return;

        foreach (var unit in selectedUnits)
        {
            if (unit != null)
            {
                unit.MoveToPosition(targetPosition);
            }
        }
    }

    /// <summary>
    /// Attack target with selected units
    /// </summary>
    public void AttackTarget(CombatUnit target)
    {
        if (selectedUnits == null || selectedUnits.Count == 0 || target == null) return;

        foreach (var unit in selectedUnits)
        {
            if (unit != null && unit.CanAttack(target))
            {
                unit.AttackTarget(target);
            }
        }
    }

    /// <summary>
    /// End the current battle
    /// </summary>
    public void EndBattle(BattleResult result)
    {
        if (!battleInProgress) return;

        battleInProgress = false;
        ClearSelection();

        // Return to main game scene
        StartCoroutine(ReturnToMainGame(result));
    }

    private IEnumerator ReturnToMainGame(BattleResult result)
    {
        // Load the main game scene
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("MainGame"); // Adjust scene name as needed
        
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // Notify battle ended
        OnBattleEnded?.Invoke(result);

        Debug.Log("[BattleManager] Returned to main game");
    }

    void Update()
    {
        if (battleInProgress && Input.GetKeyDown(pauseKey))
        {
            TogglePause();
        }
    }

    /// <summary>
    /// Toggle battle pause state
    /// </summary>
    public void TogglePause()
    {
        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;
        
        Debug.Log($"[BattleManager] Battle {(isPaused ? "Paused" : "Resumed")}");
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

/// <summary>
/// Formation types for unit positioning
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
/// Data for different formation types
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

/// <summary>
/// Result of a completed battle
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
