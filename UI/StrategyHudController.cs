// Assets/Scripts/UI/StrategyHudController.cs
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top-level HUD controller for the new architecture.
/// Manages instantiation and data-binding of HUD widgets under serialized anchors.
/// All screen positions are configurable via Inspector (no hardcoded anchoredPositions).
/// </summary>
public class StrategyHudController : MonoBehaviour
{
    [Header("HUD Anchors")]
    [SerializeField] private Transform topBarAnchor;
    [SerializeField] private Transform leftScienceCultureAnchor;
    [SerializeField] private Transform rightLayerDropdownAnchor;
    [SerializeField] private Transform bottomBarAnchor;

    [Header("Widget Prefabs")]
    [SerializeField] private GameObject topBarPrefab;
    [SerializeField] private GameObject leftScienceCulturePrefab;
    [SerializeField] private GameObject rightLayerDropdownPrefab;
    [SerializeField] private GameObject bottomBarPrefab;

    private GameObject topBarInstance;
    private GameObject leftScienceCultureInstance;
    private GameObject rightLayerDropdownInstance;
    private GameObject bottomBarInstance;

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

    /// <summary>
    /// Handle turn changes by updating HUD for the new active civilization.
    /// </summary>
    private void HandleTurnChanged(Civilization newCiv)
    {
        if (newCiv == null) return;
        
        currentCiv = newCiv;

        // Only update HUD if it's the player's turn
        if (!newCiv.isPlayerControlled)
        {
            HideAllWidgets();
            return;
        }

        RefreshAllWidgets();
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
        if (topBarAnchor == null) return;

        // Destroy existing instance
        if (topBarInstance != null)
            Destroy(topBarInstance);

        // Instantiate new widget under anchor
        if (topBarPrefab != null)
        {
            topBarInstance = Instantiate(topBarPrefab, topBarAnchor);
            // Position is controlled by RectTransform anchoring
            topBarInstance.name = "TopBar_Instance";

            // Bind data
            var topBarWidget = topBarInstance.GetComponent<HudTopBar>();
            if (topBarWidget != null)
                topBarWidget.Bind(currentCiv);
        }
    }

    private void RefreshLeftPanel()
    {
        if (leftScienceCultureAnchor == null) return;

        // Destroy existing instance
        if (leftScienceCultureInstance != null)
            Destroy(leftScienceCultureInstance);

        // Instantiate new widget under anchor
        if (leftScienceCulturePrefab != null)
        {
            leftScienceCultureInstance = Instantiate(leftScienceCulturePrefab, leftScienceCultureAnchor);
            leftScienceCultureInstance.name = "LeftPanel_Instance";

            // Bind data
            var leftWidget = leftScienceCultureInstance.GetComponent<HudLeftPanel>();
            if (leftWidget != null)
                leftWidget.Bind(currentCiv);
        }
    }

    private void RefreshRightPanel()
    {
        if (rightLayerDropdownAnchor == null) return;

        // Destroy existing instance
        if (rightLayerDropdownInstance != null)
            Destroy(rightLayerDropdownInstance);

        // Instantiate new widget under anchor
        if (rightLayerDropdownPrefab != null)
        {
            rightLayerDropdownInstance = Instantiate(rightLayerDropdownPrefab, rightLayerDropdownAnchor);
            rightLayerDropdownInstance.name = "RightPanel_Instance";

            // Bind data
            var rightWidget = rightLayerDropdownInstance.GetComponent<HudRightPanel>();
            if (rightWidget != null)
                rightWidget.Bind(currentCiv);
        }
    }

    private void RefreshBottomBar()
    {
        if (bottomBarAnchor == null) return;

        // Destroy existing instance
        if (bottomBarInstance != null)
            Destroy(bottomBarInstance);

        // Instantiate new widget under anchor
        if (bottomBarPrefab != null)
        {
            bottomBarInstance = Instantiate(bottomBarPrefab, bottomBarAnchor);
            bottomBarInstance.name = "BottomBar_Instance";

            // Bind data
            var bottomWidget = bottomBarInstance.GetComponent<HudBottomBar>();
            if (bottomWidget != null)
                bottomWidget.Bind(currentCiv);
        }
    }

    /// <summary>
    /// Hide all widgets when it's not the player's turn.
    /// </summary>
    private void HideAllWidgets()
    {
        if (topBarInstance != null)
            topBarInstance.SetActive(false);
        if (leftScienceCultureInstance != null)
            leftScienceCultureInstance.SetActive(false);
        if (rightLayerDropdownInstance != null)
            rightLayerDropdownInstance.SetActive(false);
        if (bottomBarInstance != null)
            bottomBarInstance.SetActive(false);
    }

    /// <summary>
    /// Show all widgets (call this when returning to player's turn).
    /// </summary>
    public void ShowAllWidgets()
    {
        if (topBarInstance != null)
            topBarInstance.SetActive(true);
        if (leftScienceCultureInstance != null)
            leftScienceCultureInstance.SetActive(true);
        if (rightLayerDropdownInstance != null)
            rightLayerDropdownInstance.SetActive(true);
        if (bottomBarInstance != null)
            bottomBarInstance.SetActive(true);
    }
}
