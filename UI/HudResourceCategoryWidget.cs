// Assets/Scripts/UI/HudResourceCategoryWidget.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Single resource category widget for the HUD top bar.
/// Displays: icon, category name, current count, per-turn yield.
/// Supports hover-to-expand breakdown showing each resource in the category.
/// </summary>
public class HudResourceCategoryWidget : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private Image categoryIcon;
    [SerializeField] private TextMeshProUGUI categoryNameText;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private TextMeshProUGUI yieldText;
    [SerializeField] private Color positiveYieldColor = Color.green;
    [SerializeField] private Color negativeYieldColor = Color.red;

    [Header("Hover Popover")]
    [SerializeField] private GameObject breakdownPopoverPrefab;
    [SerializeField] private GameObject breakdownItemPrefab;
    private HudResourceCategoryPopover popoverInstance;

    private ResourceCategory categoryDefinition;
    private Civilization currentCiv;
    private int currentCount;
    private int yieldPerTurn;

    private void Start()
    {
        WireHoverListeners();
    }

    private void OnDestroy()
    {
        UnwireHoverListeners();
        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);
    }

    private void WireHoverListeners()
    {
        var eventTrigger = GetComponent<EventTrigger>();
        if (eventTrigger == null)
            eventTrigger = gameObject.AddComponent<EventTrigger>();

        // Clear existing triggers
        eventTrigger.triggers.Clear();

        // Hover Enter: Show popover
        var pointerEnterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        pointerEnterEntry.callback.AddListener(data => ShowBreakdownPopover());
        eventTrigger.triggers.Add(pointerEnterEntry);

        // Hover Exit: Hide popover
        var pointerExitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        pointerExitEntry.callback.AddListener(data => HideBreakdownPopover());
        eventTrigger.triggers.Add(pointerExitEntry);
    }

    private void UnwireHoverListeners()
    {
        var eventTrigger = GetComponent<EventTrigger>();
        if (eventTrigger != null)
            eventTrigger.triggers.Clear();
    }

    /// <summary>
    /// Bind this widget to a resource category and civilization, then populate displays.
    /// </summary>
    public void Bind(Civilization civ, ResourceCategory category, int count, int yieldPerTurnValue)
    {
        currentCiv = civ;
        categoryDefinition = category;
        currentCount = count;
        yieldPerTurn = yieldPerTurnValue;

        // Update display
        if (categoryNameText != null)
            categoryNameText.text = GetCategoryDisplayName(category);

        if (countText != null)
            countText.text = count.ToString("N0");

        if (yieldText != null)
        {
            yieldText.text = (yieldPerTurn >= 0 ? "+" : "") + yieldPerTurn.ToString("N0") + "/turn";
            yieldText.color = yieldPerTurn >= 0 ? positiveYieldColor : negativeYieldColor;
        }
    }

    private void ShowBreakdownPopover()
    {
        if (breakdownPopoverPrefab == null || currentCiv == null) return;

        // Destroy existing popover
        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);

        // Get root canvas to parent popover for top rendering
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) return;

        // Instantiate as child of root canvas to ensure it renders on top
        var popoverGO = Instantiate(breakdownPopoverPrefab, rootCanvas.transform, false);
        popoverGO.transform.SetAsLastSibling();

        // Position popover with the same X as the widget and Y offset below it
        PositionPopoverNearWidget(popoverGO.GetComponent<RectTransform>());

        popoverInstance = popoverGO.GetComponent<HudResourceCategoryPopover>();

        if (popoverInstance != null)
        {
            var allResources = ResourceCategoryProviderUtility.GetMergedInventory(currentCiv, categoryDefinition);
            popoverInstance.Show(categoryDefinition.ToString(), allResources, breakdownItemPrefab);
        }
    }

    private void HideBreakdownPopover()
    {
        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);
        popoverInstance = null;
    }

    private void PositionPopoverNearWidget(RectTransform popoverRect)
    {
        if (popoverRect == null) return;
        var widgetRect = transform as RectTransform;
        if (widgetRect == null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;

        Vector2 widgetScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, widgetRect.position);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, widgetScreenPos, canvas.worldCamera, out Vector2 canvasLocalPos))
            return;

        popoverRect.anchorMin = Vector2.zero;
        popoverRect.anchorMax = Vector2.zero;
        popoverRect.pivot = new Vector2(0.5f, 1f);
        popoverRect.anchoredPosition = canvasLocalPos + new Vector2(0f, -90f);
        popoverRect.localScale = Vector3.one;

        var size = popoverRect.sizeDelta;
        float halfWidth = size.x * 0.5f;
        float minX = -canvasRect.sizeDelta.x * 0.5f + halfWidth;
        float maxX = canvasRect.sizeDelta.x * 0.5f - halfWidth;
        float maxY = canvasRect.sizeDelta.y * 0.5f;
        float minY = -canvasRect.sizeDelta.y;

        popoverRect.anchoredPosition = new Vector2(
            Mathf.Clamp(popoverRect.anchoredPosition.x, minX, maxX),
            Mathf.Clamp(popoverRect.anchoredPosition.y, minY, maxY)
        );
    }

    private static string GetCategoryDisplayName(ResourceCategory category)
    {
        return category.ToString();
    }
}
