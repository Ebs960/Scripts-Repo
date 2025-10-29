using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple script to set up and test the battle system
/// Attach this to a GameObject in your battle scene
/// </summary>
public class BattleTestSetup : MonoBehaviour
{
    [Header("Test Settings")]
    [Tooltip("Number of units per side for testing")]
    public int unitsPerSide = 3;
    
    [Tooltip("Unit data to use for testing")]
    public CombatUnitData testUnitData;
    
    [Header("UI References")]
    public Button startBattleButton;
    public Button pauseButton;
    public Text statusText;
    
    [Header("Battle References")]
    public BattleManager battleManager;
    public BattleMapGenerator mapGenerator;
    public BattleUI battleUI;
    
    void Start()
    {
        // Auto-find components if not assigned
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();
        if (mapGenerator == null)
            mapGenerator = FindFirstObjectByType<BattleMapGenerator>();
        if (battleUI == null)
            battleUI = FindFirstObjectByType<BattleUI>();
            
        // Set up UI
        if (startBattleButton != null)
            startBattleButton.onClick.AddListener(StartTestBattle);
        if (pauseButton != null)
            pauseButton.onClick.AddListener(TogglePause);
            
        // Load test unit data if not assigned
        if (testUnitData == null)
        {
            var allUnits = Resources.LoadAll<CombatUnitData>("Units");
            if (allUnits.Length > 0)
                testUnitData = allUnits[0];
        }
        
        UpdateStatus("Ready to start battle test");
    }
    
    public void StartTestBattle()
    {
        if (battleManager == null)
        {
            Debug.LogError("BattleManager not found!");
            UpdateStatus("Error: BattleManager not found");
            return;
        }
        
        if (testUnitData == null)
        {
            Debug.LogError("No test unit data assigned!");
            UpdateStatus("Error: No test unit data");
            return;
        }
        
        UpdateStatus("Starting battle test...");
        
        // Create test civilizations
        var attackerCiv = CreateTestCivilization("Test Attacker", true);
        var defenderCiv = CreateTestCivilization("Test Defender", false);
        
        // Spawn test units
        var attackerUnits = SpawnTestUnits(attackerCiv, testUnitData, unitsPerSide, true);
        var defenderUnits = SpawnTestUnits(defenderCiv, testUnitData, unitsPerSide, false);
        
        // Start battle
        battleManager.StartBattle(attackerCiv, defenderCiv, attackerUnits, defenderUnits);
        
        UpdateStatus("Battle started! Use right-click to move/attack, Escape to pause");
    }
    
    public void TogglePause()
    {
        if (battleManager != null)
        {
            battleManager.TogglePause();
            UpdateStatus(battleManager.IsPaused ? "Battle Paused" : "Battle Resumed");
        }
    }
    
    private Civilization CreateTestCivilization(string name, bool isAttacker)
    {
        // Create a simple test civilization
        var civGO = new GameObject(name);
        var civ = civGO.AddComponent<Civilization>();
        
        // Set up basic civilization data
        var civData = ScriptableObject.CreateInstance<CivData>();
        civData.civName = name;
        civData.attackBonus = isAttacker ? 0.1f : 0f; // Slight advantage for attacker
        
        civ.Initialize(civData, null, false);
        return civ;
    }
    
    private System.Collections.Generic.List<CombatUnit> SpawnTestUnits(Civilization civ, CombatUnitData unitData, int count, bool isAttacker)
    {
        var units = new System.Collections.Generic.List<CombatUnit>();
        
        for (int i = 0; i < count; i++)
        {
            // Create unit GameObject
            var unitGO = new GameObject($"Test {unitData.unitName} {i + 1}");
            var unit = unitGO.AddComponent<CombatUnit>();
            
            // Initialize unit
            unit.Initialize(unitData, civ);
            
            // Position units in formation
            float spacing = 3f;
            Vector3 basePos = isAttacker ? new Vector3(-20, 0, 0) : new Vector3(20, 0, 0);
            Vector3 offset = new Vector3(0, 0, (i - count / 2f) * spacing);
            unitGO.transform.position = basePos + offset;
            
            // Set up for battle
            unit.InitializeForBattle(isAttacker);
            
            units.Add(unit);
        }
        
        return units;
    }
    
    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log($"[BattleTestSetup] {message}");
    }
}
