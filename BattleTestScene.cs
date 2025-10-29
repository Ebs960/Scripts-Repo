using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Battle test scene manager for quickly testing battle features
/// </summary>
public class BattleTestScene : MonoBehaviour
{
    [Header("Test Configuration")]
    [Tooltip("Number of attacker units to spawn")]
    public int attackerUnitCount = 3;
    [Tooltip("Number of defender units to spawn")]
    public int defenderUnitCount = 3;
    [Tooltip("Unit data to use for attackers")]
    public CombatUnitData attackerUnitData;
    [Tooltip("Unit data to use for defenders")]
    public CombatUnitData defenderUnitData;
    [Tooltip("Equipment to give to all units")]
    public EquipmentData testWeapon;
    [Tooltip("Projectile to give to all units")]
    public GameCombat.ProjectileData testProjectile;

    [Header("Battle Settings")]
    [Tooltip("Formation spacing between units")]
    public float formationSpacing = 2f;
    [Tooltip("Distance between attacker and defender formations")]
    public float battleDistance = 10f;

    [Header("UI References")]
    [Tooltip("Battle UI prefab to instantiate")]
    public GameObject battleUIPrefab;

    private BattleManager battleManager;
    private List<CombatUnit> testAttackerUnits = new List<CombatUnit>();
    private List<CombatUnit> testDefenderUnits = new List<CombatUnit>();

    void Start()
    {
        // Create test battle
        StartTestBattle();
    }

    void Update()
    {
        // Quick restart with R key
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartTestBattle();
        }

        // Spawn more attackers with A key
        if (Input.GetKeyDown(KeyCode.A))
        {
            SpawnAdditionalUnit(true);
        }

        // Spawn more defenders with D key
        if (Input.GetKeyDown(KeyCode.D))
        {
            SpawnAdditionalUnit(false);
        }
    }

    /// <summary>
    /// Start a test battle with configured units
    /// </summary>
    public void StartTestBattle()
    {
        Debug.Log("[BattleTestScene] Starting test battle...");

        // Create test civilizations
        Civilization attackerCiv = CreateTestCivilization("Test Attacker", true);
        Civilization defenderCiv = CreateTestCivilization("Test Defender", false);

        // Spawn attacker units
        SpawnTestUnits(attackerCiv, attackerUnitData, attackerUnitCount, true);

        // Spawn defender units
        SpawnTestUnits(defenderCiv, defenderUnitData, defenderUnitCount, false);

        // Start the battle
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.StartBattle(attackerCiv, defenderCiv, testAttackerUnits, testDefenderUnits);
        }
        else
        {
            Debug.LogError("[BattleTestScene] BattleManager not found! Make sure BattleManager is in the scene.");
        }

        // Setup UI
        SetupBattleUI();
    }

    /// <summary>
    /// Create a test civilization
    /// </summary>
    private Civilization CreateTestCivilization(string name, bool isAttacker)
    {
        GameObject civGO = new GameObject(name);
        Civilization civ = civGO.AddComponent<Civilization>();
        
        // Create basic civ data
        CivData civData = ScriptableObject.CreateInstance<CivData>();
        civData.civName = name;
        civ.Initialize(civData, null, false);
        
        return civ;
    }

    /// <summary>
    /// Spawn test units for a civilization
    /// </summary>
    private void SpawnTestUnits(Civilization civ, CombatUnitData unitData, int count, bool isAttacker)
    {
        if (unitData == null)
        {
            Debug.LogError($"[BattleTestScene] Unit data is null for {(isAttacker ? "attacker" : "defender")}!");
            return;
        }

        Vector3 formationCenter = isAttacker ? 
            new Vector3(-battleDistance/2, 0, 0) : 
            new Vector3(battleDistance/2, 0, 0);

        for (int i = 0; i < count; i++)
        {
            // Calculate position in formation
            int row = i / 3; // 3 units per row
            int col = i % 3;
            Vector3 offset = new Vector3(
                (col - 1) * formationSpacing,
                0,
                row * formationSpacing
            );
            Vector3 unitPosition = formationCenter + offset;

            // Instantiate unit
            GameObject unitGO = Instantiate(unitData.prefab, unitPosition, Quaternion.identity);
            CombatUnit unit = unitGO.GetComponent<CombatUnit>();
            
            if (unit != null)
            {
                unit.Initialize(unitData, civ);
                unit.InitializeForBattle(isAttacker);

                // Equip test weapon if available
                if (testWeapon != null)
                {
                    unit.EquipItem(testWeapon);
                }

                // Set test projectile if available
                if (testProjectile != null)
                {
                    unit.ActiveProjectile = testProjectile;
                }

                // Add to appropriate list
                if (isAttacker)
                {
                    testAttackerUnits.Add(unit);
                }
                else
                {
                    testDefenderUnits.Add(unit);
                }
            }
        }
    }

    /// <summary>
    /// Spawn an additional unit during battle
    /// </summary>
    public void SpawnAdditionalUnit(bool isAttacker)
    {
        CombatUnitData unitData = isAttacker ? attackerUnitData : defenderUnitData;
        if (unitData == null) return;

        Civilization civ = isAttacker ? 
            testAttackerUnits[0].owner : 
            testDefenderUnits[0].owner;

        // Spawn at random position near formation
        Vector3 formationCenter = isAttacker ? 
            new Vector3(-battleDistance/2, 0, 0) : 
            new Vector3(battleDistance/2, 0, 0);
        
        Vector3 randomOffset = new Vector3(
            Random.Range(-3f, 3f),
            0,
            Random.Range(-3f, 3f)
        );
        Vector3 spawnPosition = formationCenter + randomOffset;

        GameObject unitGO = Instantiate(unitData.prefab, spawnPosition, Quaternion.identity);
        CombatUnit unit = unitGO.GetComponent<CombatUnit>();
        
        if (unit != null)
        {
            unit.Initialize(unitData, civ);
            unit.InitializeForBattle(isAttacker);

            if (testWeapon != null) unit.EquipItem(testWeapon);
            if (testProjectile != null) unit.ActiveProjectile = testProjectile;

            if (isAttacker)
            {
                testAttackerUnits.Add(unit);
            }
            else
            {
                testDefenderUnits.Add(unit);
            }

            Debug.Log($"[BattleTestScene] Spawned additional {(isAttacker ? "attacker" : "defender")} unit");
        }
    }

    /// <summary>
    /// Setup battle UI
    /// </summary>
    private void SetupBattleUI()
    {
        if (battleUIPrefab != null)
        {
            GameObject uiGO = Instantiate(battleUIPrefab);
            BattleUI battleUI = uiGO.GetComponent<BattleUI>();
            if (battleUI != null && BattleManager.Instance != null)
            {
                battleUI.Initialize(BattleManager.Instance);
            }
        }
    }

    /// <summary>
    /// Restart the test battle
    /// </summary>
    public void RestartTestBattle()
    {
        Debug.Log("[BattleTestScene] Restarting test battle...");

        // Clear existing units
        foreach (var unit in testAttackerUnits)
        {
            if (unit != null) Destroy(unit.gameObject);
        }
        foreach (var unit in testDefenderUnits)
        {
            if (unit != null) Destroy(unit.gameObject);
        }

        testAttackerUnits.Clear();
        testDefenderUnits.Clear();

        // Restart battle
        StartTestBattle();
    }

    /// <summary>
    /// Draw formation positions in scene view
    /// </summary>
    void OnDrawGizmos()
    {
        if (Application.isPlaying) return;

        // Draw attacker formation
        Gizmos.color = Color.red;
        Vector3 attackerCenter = new Vector3(-battleDistance/2, 0, 0);
        for (int i = 0; i < attackerUnitCount; i++)
        {
            int row = i / 3;
            int col = i % 3;
            Vector3 offset = new Vector3((col - 1) * formationSpacing, 0, row * formationSpacing);
            Gizmos.DrawWireCube(attackerCenter + offset, Vector3.one);
        }

        // Draw defender formation
        Gizmos.color = Color.blue;
        Vector3 defenderCenter = new Vector3(battleDistance/2, 0, 0);
        for (int i = 0; i < defenderUnitCount; i++)
        {
            int row = i / 3;
            int col = i % 3;
            Vector3 offset = new Vector3((col - 1) * formationSpacing, 0, row * formationSpacing);
            Gizmos.DrawWireCube(defenderCenter + offset, Vector3.one);
        }
    }
}
