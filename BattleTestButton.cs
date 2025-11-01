using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Button component to quickly start a battle test from the main menu
/// </summary>
public class BattleTestButton : MonoBehaviour
{
    [Header("Battle Test Settings")]
    [Tooltip("Unit data to use for attackers")]
    public CombatUnitData attackerUnitData;
    [Tooltip("Unit data to use for defenders")]
    public CombatUnitData defenderUnitData;
    [Tooltip("Number of units per side")]
    public int unitsPerSide = 3;

    [Header("Scene Loading")]
    [Tooltip("Name of the battle test scene in Build Settings")]
    [SerializeField] private string battleTestSceneName = "BattleTestScene";

    private Button button;

    void Start()
    {
        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(StartBattleTest);
        }
    }

    /// <summary>
    /// Start a battle test scene
    /// </summary>
    public void StartBattleTest()
    {
        Debug.Log("[BattleTestButton] Starting battle test...");

        // Load battle test scene
        if (string.IsNullOrEmpty(battleTestSceneName))
        {
            Debug.LogError("[BattleTestButton] Battle test scene name is empty. Please set 'battleTestSceneName' in the inspector.");
            return;
        }

        SceneManager.LoadScene(battleTestSceneName);
    }

    /// <summary>
    /// Start battle test in current scene (for testing)
    /// </summary>
    public void StartBattleTestInCurrentScene()
    {
        // Find or create BattleTestScene component
    BattleTestScene testScene = null;
#if UNITY_2023_1_OR_NEWER
    testScene = FindFirstObjectByType<BattleTestScene>();
#else
    testScene = FindObjectOfType<BattleTestScene>();
#endif
        if (testScene == null)
        {
            GameObject testGO = new GameObject("BattleTestScene");
            testScene = testGO.AddComponent<BattleTestScene>();
        }

        // Configure test scene
        testScene.attackerUnitData = attackerUnitData;
        testScene.defenderUnitData = defenderUnitData;
        testScene.attackerUnitCount = unitsPerSide;
        testScene.defenderUnitCount = unitsPerSide;

        // Start the test
        testScene.StartTestBattle();
    }
}
