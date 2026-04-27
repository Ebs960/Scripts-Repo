// Assets/Scripts/UI/HudBreakdownPopover.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Popover window shown on hover over yield widgets.
/// Displays structured breakdown of yield sources:
/// - City yields
/// - Trade route income
/// - Resource node yields
/// - Unit/worker yields
/// - Herd yields
/// - Flat bonuses
/// - Consumption/upkeep
/// 
/// Content is populated by HudBreakdownService.
/// </summary>
public class HudBreakdownPopover : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Transform contentRoot;
    [SerializeField] private GameObject breakdownItemPrefab;

    [Header("Sizing")]
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private VerticalLayoutGroup layoutGroup;

    /// <summary>
    /// Show breakdown for a specific yield type.
    /// </summary>
    public void Show(string yieldName, object breakdownData)
    {
        if (titleText != null)
            titleText.text = yieldName + " Breakdown";

        // Populate content via HudBreakdownService
        PopulateBreakdown(yieldName, breakdownData);
    }

    private void PopulateBreakdown(string yieldName, object breakdownData)
    {
        if (contentRoot == null) return;

        // Clear existing items
        foreach (Transform child in contentRoot)
        {
            Destroy(child.gameObject);
        }

        // Get breakdown data from service
        var breakdownService = UnityEngine.Object.FindFirstObjectByType<HudBreakdownService>();
        if (breakdownService == null)
        {
            Debug.LogWarning("HudBreakdownPopover: HudBreakdownService not found");
            return;
        }

        // Route to appropriate breakdown getter
        var items = yieldName switch
        {
            "Food" => breakdownService.GetFoodBreakdown(),
            "Gold" => breakdownService.GetGoldBreakdown(),
            "Policy Points" => breakdownService.GetPolicyBreakdown(),
            _ => null
        };

        if (items == null) return;

        // Instantiate breakdown items
        foreach (var item in items)
        {
            if (breakdownItemPrefab != null)
            {
                var instance = Instantiate(breakdownItemPrefab, contentRoot);
                var itemWidget = instance.GetComponent<HudBreakdownItem>();
                if (itemWidget != null)
                    itemWidget.Populate(item);
            }
        }

        // Refresh layout
        if (layoutGroup != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot as RectTransform);
    }

    public void Hide()
    {
        Destroy(gameObject);
    }
}
