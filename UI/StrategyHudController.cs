// Assets/Scripts/UI/StrategyHudController.cs
using System.Linq;
using UnityEngine;

/// <summary>
/// Top-level HUD controller for the new architecture.
/// Binds already-placed HUD widgets in-scene (no runtime prefab instantiation).
/// Visibility mirrors UIManager.gameplayHudRoot behavior so this HUD appears/disappears
/// for the same reasons as the main gameplay HUD root.
/// </summary>
public class StrategyHudController : MonoBehaviour
{
    [Header("Assigned HUD Widgets (already in scene)")]
    [SerializeField] private HudTopBar topBar;
    [SerializeField] private HudLeftPanel leftPanel;
    [SerializeField] private HudRightPanel rightPanel;
    [SerializeField] private HudBottomBar bottomBar;

    [Header("HUD Roots (optional explicit roots for visibility toggling)")]
    [SerializeField] private GameObject topBarRoot;
    [SerializeField] private GameObject leftPanelRoot;
    [SerializeField] private GameObject rightPanelRoot;
    [SerializeField] private GameObject bottomBarRoot;

    private Civilization currentCiv;

    private void Start()
    {
        // Subscribe to turn changes
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
        }

        // Initialize UI for current player civ
        var playerCiv = CivilizationManager.Instance?.GetAllCivs()?.FirstOrDefault(c => c.isPlayerControlled);
        if (playerCiv != null)
        {
            HandleTurnChanged(playerCiv);
        }
    }

    // Overload to handle TurnManager's (Civilization,int) event signature
    private void HandleTurnChanged(Civilization civ, int turn)
    {
        HandleTurnChanged(civ);
    }

    private void OnDestroy()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
        }
    }

    private void LateUpdate()
    {
        SyncVisibilityWithGameplayHudRoot();
    }

    /// <summary>
    /// Handle turn changes by updating HUD for the new active civilization.
    /// </summary>
    private void HandleTurnChanged(Civilization newCiv)
    {
        if (newCiv == null) return;
        
        currentCiv = newCiv;

        RefreshAllWidgets();
        SyncVisibilityWithGameplayHudRoot();
    }

    /// <summary>
    /// Refresh all HUD widgets with current civilization data.
    /// </summary>
    public void RefreshAllWidgets()
    {
        if (currentCiv == null) return;

        RefreshTopBar();
        RefreshLeftPanel();
        RefreshRightPanel();
        RefreshBottomBar();
    }

    private void RefreshTopBar()
    {
        if (topBar != null)
            topBar.Bind(currentCiv);
    }

    private void RefreshLeftPanel()
    {
        if (leftPanel != null)
            leftPanel.Bind(currentCiv);
    }

    private void RefreshRightPanel()
    {
        if (rightPanel != null)
            rightPanel.Bind(currentCiv);
    }

    private void RefreshBottomBar()
    {
        if (bottomBar != null)
            bottomBar.Bind(currentCiv);
    }

    private bool ShouldHudBeVisible()
    {
        bool playerTurn = currentCiv != null && currentCiv.isPlayerControlled;
        bool gameplayHudVisible = UIManager.Instance == null || UIManager.Instance.gameplayHudRoot == null || UIManager.Instance.gameplayHudRoot.activeSelf;
        return playerTurn && gameplayHudVisible;
    }

    private void SyncVisibilityWithGameplayHudRoot()
    {
        bool visible = ShouldHudBeVisible();
        SetWidgetVisible(topBarRoot, topBar != null ? topBar.gameObject : null, visible);
        SetWidgetVisible(leftPanelRoot, leftPanel != null ? leftPanel.gameObject : null, visible);
        SetWidgetVisible(rightPanelRoot, rightPanel != null ? rightPanel.gameObject : null, visible);
        SetWidgetVisible(bottomBarRoot, bottomBar != null ? bottomBar.gameObject : null, visible);
    }

    private static void SetWidgetVisible(GameObject explicitRoot, GameObject fallbackRoot, bool visible)
    {
        var target = explicitRoot != null ? explicitRoot : fallbackRoot;
        if (target != null)
            target.SetActive(visible);
    }
}
