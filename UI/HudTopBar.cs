// Assets/Scripts/UI/HudTopBar.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top bar HUD widget showing:
/// - Civilization name and round
/// - Yield displays (food, gold, policy points with per-turn deltas)
/// - Resource category displays (manually placed widgets)
/// 
/// Data sourced from Civilization.cached* fields (already computed each turn).
/// Resource widgets are manually placed in the scene/prefab; not generated at runtime.
/// </summary>
public class HudTopBar : MonoBehaviour
{
    [Header("Info Display")]
    [SerializeField] private TextMeshProUGUI civNameText;
    [SerializeField] private TextMeshProUGUI roundText;

    [Header("Yield Widgets")]
    [SerializeField] private HudYieldWidget foodYieldWidget;
    [SerializeField] private HudYieldWidget goldYieldWidget;
    [SerializeField] private HudYieldWidget policyYieldWidget;

    [Header("Resource Categories (Manually Placed)")]
    [SerializeField] private HudResourceCategoryWidget[] resourceCategoryWidgets = new HudResourceCategoryWidget[6];
    [SerializeField] private ResourceCategory[] allResourceCategories = new ResourceCategory[6];

    [Header("Panel Buttons (optional)")]
    [SerializeField] private Button religionButton;
    [SerializeField] private Button policyButton;
    [SerializeField] private HudPanelRouter panelRouter;

    private Civilization currentCiv;

    private void Awake()
    {
        if (panelRouter == null)
            panelRouter = GetComponentInParent<HudPanelRouter>();
    }

    /// <summary>
    /// Bind this widget to a civilization and populate displays.
    /// </summary>
    public void Bind(Civilization civ)
    {
        currentCiv = civ;
        if (currentCiv == null)
        {
            Debug.LogWarning("HudTopBar.Bind: Civilization is null");
            return;
        }

        // Update info display
        if (civNameText != null)
            civNameText.text = currentCiv.civData != null && !string.IsNullOrEmpty(currentCiv.civData.civName) ? currentCiv.civData.civName : (currentCiv.name ?? "Unknown");

        if (roundText != null)
        {
            var round = TurnManager.Instance?.round ?? 0;
            roundText.text = $"Turn {round}";
        }

        // Bind yield widgets
        // Food: display per-turn delta (production - consumption)
        if (foodYieldWidget != null)
        {
            int foodDelta = currentCiv.cachedFoodPerTurn - currentCiv.cachedFoodConsumption;
            foodYieldWidget.Bind("Food", currentCiv.food, foodDelta, null);
        }

        // Gold: display per-turn production
        if (goldYieldWidget != null)
        {
            goldYieldWidget.Bind("Gold", currentCiv.gold, currentCiv.cachedGoldPerTurn, null);
        }

        // Policy Points: display per-turn production
        if (policyYieldWidget != null)
        {
            policyYieldWidget.Bind("Policy Points", currentCiv.policyPoints, currentCiv.cachedPolicyPerTurn, null);
        }

        // Wire panel router
        if (panelRouter != null)
            panelRouter.SetCurrentCivilization(currentCiv);

        // Bind resource category widgets
        BindResourceCategories();

        WireButtonListeners();
    }

    /// <summary>
    /// Bind manually-placed resource category widgets for current civilization.
    /// Widgets are pre-placed in the scene/prefab; this just updates their display data.
    /// </summary>
    private void BindResourceCategories()
    {
        if (currentCiv == null || resourceCategoryWidgets == null || allResourceCategories == null) return;

        // Bind each pre-placed widget to its corresponding category
        for (int i = 0; i < resourceCategoryWidgets.Length; i++)
        {
            if (resourceCategoryWidgets[i] == null || i >= allResourceCategories.Length)
                continue;

            var widget = resourceCategoryWidgets[i];
            var category = allResourceCategories[i];

            int count = ResourceCategoryProviderUtility.GetTotalCount(currentCiv, category);
            int yieldPerTurn = ResourceCategoryProviderUtility.GetYieldPerTurn(currentCiv, category);

            widget.Bind(currentCiv, category, count, yieldPerTurn);
        }
    }

    private void WireButtonListeners()
    {
        if (religionButton != null)
        {
            religionButton.onClick.RemoveAllListeners();
            religionButton.onClick.AddListener(() =>
            {
                if (UIManager.Instance != null && currentCiv != null)
                    UIManager.Instance.ShowReligionPanel(currentCiv);
            });
        }

        if (policyButton != null)
        {
            policyButton.onClick.RemoveAllListeners();
            policyButton.onClick.AddListener(() =>
            {
                if (UIManager.Instance != null)
                    UIManager.Instance.ShowPanel("GovernmentPanel");
            });
        }
    }

    private void OnDestroy()
    {
        if (religionButton != null)
            religionButton.onClick.RemoveAllListeners();
        if (policyButton != null)
            policyButton.onClick.RemoveAllListeners();
    }

}
