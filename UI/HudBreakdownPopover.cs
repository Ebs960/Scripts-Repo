// Assets/Scripts/UI/HudBreakdownPopover.cs
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
public class HudBreakdownPopover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Display")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Transform contentRoot;
    [SerializeField] private GameObject breakdownItemPrefab;

    [Header("Sizing")]
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private VerticalLayoutGroup layoutGroup;
    [SerializeField] private float hoverLockDelaySeconds = 0.75f;

    private bool isPointerOverPopover;
    private bool isHoverLocked;
    private bool pendingSourceExit;
    private Coroutine lockRoutine;
    private readonly List<HudBreakdownItem> pooledItemWidgets = new List<HudBreakdownItem>();
    private int pooledItemCountInUse;

    /// <summary>
    /// Show breakdown for a specific yield type.
    /// </summary>
    public void Show(string yieldName, object breakdownData, Vector2? pointerScreenPosition = null)
    {
        if (titleText != null)
            titleText.text = yieldName + " Breakdown";

        // Populate content via HudBreakdownService
        PopulateBreakdown(yieldName, breakdownData);

        BeginHoverLockCountdown();
    }

    private void PopulateBreakdown(string yieldName, object breakdownData)
    {
        if (contentRoot == null) return;

        pooledItemCountInUse = 0;

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
            "Science" => breakdownService.GetScienceBreakdown(),
            "Culture" => breakdownService.GetCultureBreakdown(),
            "Faith" => breakdownService.GetFaithBreakdown(),
            _ => null
        };

        if (items == null) return;

        var mergedItems = MergeBreakdownItems(items);

        // Instantiate breakdown items
        foreach (var item in mergedItems)
        {
            if (breakdownItemPrefab != null)
            {
                var itemWidget = GetOrCreatePooledItemWidget();
                if (itemWidget != null)
                    itemWidget.Populate(item);
            }
        }

        HideUnusedPooledItems();

        // Refresh layout
        if (layoutGroup != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot as RectTransform);
    }


    private static List<HudBreakdownService.BreakdownItem> MergeBreakdownItems(List<HudBreakdownService.BreakdownItem> items)
    {
        var order = new List<string>();
        var merged = new Dictionary<string, HudBreakdownService.BreakdownItem>();

        foreach (var item in items)
        {
            if (!merged.TryGetValue(item.source, out var existing))
            {
                merged[item.source] = item;
                order.Add(item.source);
                continue;
            }

            existing.amount += item.amount;
            merged[item.source] = existing;
        }

        var result = new List<HudBreakdownService.BreakdownItem>(order.Count);
        foreach (var key in order)
        {
            var item = merged[key];
            if (item.amount != 0)
                result.Add(item);
        }

        return result;
    }


    public void NotifySourceHoverExit()
    {
        if (!isHoverLocked)
        {
            pendingSourceExit = true;
            return;
        }

        if (!isPointerOverPopover)
            Hide();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOverPopover = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerOverPopover = false;
        if (isHoverLocked)
            Hide();
    }

    private void BeginHoverLockCountdown()
    {
        isHoverLocked = false;
        pendingSourceExit = false;

        if (lockRoutine != null)
            StopCoroutine(lockRoutine);

        lockRoutine = StartCoroutine(HoverLockRoutine());
    }

    private IEnumerator HoverLockRoutine()
    {
        yield return new WaitForSeconds(hoverLockDelaySeconds);
        isHoverLocked = true;

        if (pendingSourceExit && !isPointerOverPopover)
            Hide();

        lockRoutine = null;
    }

    private void PositionTopAtMouse(Vector2? pointerScreenPosition = null)
    {
        var rect = rectTransform != null ? rectTransform : transform as RectTransform;
        if (rect == null)
            return;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        var canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null)
            return;

        Vector2 screenPoint = pointerScreenPosition ?? UIPointerUtil.GetScreenPositionOrCenter();
        var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, cam, out var localPoint))
        {
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = localPoint;
        }
    }

    public void Hide()
    {
        if (lockRoutine != null)
            StopCoroutine(lockRoutine);

        Destroy(gameObject);
    }

    private HudBreakdownItem GetOrCreatePooledItemWidget()
    {
        while (pooledItemCountInUse >= pooledItemWidgets.Count)
        {
            var instance = Instantiate(breakdownItemPrefab, contentRoot);
            var widget = instance != null ? instance.GetComponent<HudBreakdownItem>() : null;
            if (widget == null)
            {
                if (instance != null) Destroy(instance);
                return null;
            }
            pooledItemWidgets.Add(widget);
        }

        var pooled = pooledItemWidgets[pooledItemCountInUse++];
        if (pooled != null)
            pooled.gameObject.SetActive(true);
        return pooled;
    }

    private void HideUnusedPooledItems()
    {
        for (int i = pooledItemCountInUse; i < pooledItemWidgets.Count; i++)
        {
            var widget = pooledItemWidgets[i];
            if (widget != null)
                widget.gameObject.SetActive(false);
        }
    }
}
