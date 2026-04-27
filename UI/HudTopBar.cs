// Assets/Scripts/UI/HudTopBar.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top bar HUD widget showing:
/// - Civilization name and round
/// - Yield displays (food, gold, policy points with per-turn deltas)
/// - Dropdown buttons for tech, culture, religion, policy
/// 
/// Data sourced from Civilization.cached* fields (already computed each turn).
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

    [Header("Panel Buttons")]
    [SerializeField] private Button techButton;
    [SerializeField] private Button cultureButton;
    [SerializeField] private Button religionButton;
    [SerializeField] private Button policyButton;

    private Civilization currentCiv;
    private HudPanelRouter panelRouter;

    private void Start()
    {
        // Find or create panel router
        panelRouter = UnityEngine.Object.FindFirstObjectByType<HudPanelRouter>();
        if (panelRouter == null)
        {
            Debug.LogWarning("HudTopBar: HudPanelRouter not found in scene");
        }
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

        // Wire button listeners
        WireButtonListeners();
    }

    private void WireButtonListeners()
    {
        if (techButton != null)
        {
            techButton.onClick.RemoveAllListeners();
            techButton.onClick.AddListener(() =>
            {
                if (UIManager.Instance != null && currentCiv != null)
                    UIManager.Instance.ShowTechPanel(currentCiv);
            });
        }

        if (cultureButton != null)
        {
            cultureButton.onClick.RemoveAllListeners();
            cultureButton.onClick.AddListener(() =>
            {
                if (UIManager.Instance != null && currentCiv != null)
                    UIManager.Instance.ShowCulturePanel(currentCiv);
            });
        }

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
        if (techButton != null)
            techButton.onClick.RemoveAllListeners();
        if (cultureButton != null)
            cultureButton.onClick.RemoveAllListeners();
        if (religionButton != null)
            religionButton.onClick.RemoveAllListeners();
        if (policyButton != null)
            policyButton.onClick.RemoveAllListeners();
    }
}
