// Assets/Scripts/UI/HudResourceCategoryPopover.cs
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Popover shown on hover over resource category widgets.
/// Displays list of individual resources in that category with their quantities.
/// </summary>
public class HudResourceCategoryPopover : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Transform contentRoot;
    [SerializeField] private GameObject resourceItemPrefab;

    [Header("Sizing")]
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private VerticalLayoutGroup layoutGroup;

    /// <summary>
    /// Show breakdown for resource category, listing each resource and quantity.
    /// </summary>
    public void Show(string categoryName, Dictionary<ResourceData, int> resources, GameObject itemPrefab)
    {
        if (titleText != null)
            titleText.text = categoryName;

        PopulateResources(resources, itemPrefab);
    }

    private void PopulateResources(Dictionary<ResourceData, int> resources, GameObject itemPrefab)
    {
        if (contentRoot == null) return;

        // Clear existing items
        foreach (Transform child in contentRoot)
        {
            Destroy(child.gameObject);
        }

        if (resources == null || resources.Count == 0)
        {
            if (itemPrefab != null)
            {
                var instance = Instantiate(itemPrefab, contentRoot);
                var itemWidget = instance.GetComponent<HudBreakdownItem>();
                if (itemWidget != null)
                    itemWidget.Populate(new HudBreakdownService.BreakdownItem 
                    { 
                        source = "No resources", 
                        amount = 0,
                        category = ""
                    });
            }
            return;
        }

        // Instantiate item for each resource
        foreach (var kvp in resources)
        {
            if (itemPrefab != null && kvp.Key != null)
            {
                var instance = Instantiate(itemPrefab, contentRoot);
                var itemWidget = instance.GetComponent<HudBreakdownItem>();
                if (itemWidget != null)
                {
                    itemWidget.Populate(new HudBreakdownService.BreakdownItem 
                    { 
                        source = kvp.Key.resourceName, 
                        amount = kvp.Value,
                        category = ""
                    });
                }
            }
        }
    }
}
