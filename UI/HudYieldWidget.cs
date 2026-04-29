// Assets/Scripts/UI/HudYieldWidget.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Reusable widget for displaying a single yield metric on the top bar.
/// Shows: icon, current amount, and per-turn delta.
/// 
/// Supports hover-to-expand breakdown popover (via HudBreakdownPopover).
/// </summary>
public class HudYieldWidget : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private Image iconImage;
    [SerializeField] private bool allowRuntimeIconOverride = false;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private TextMeshProUGUI deltaText;
    [SerializeField] private Color positiveDeltaColor = Color.green;
    [SerializeField] private Color negativeDeltaColor = Color.red;

    [Header("Hover Popover")]
    [SerializeField] private GameObject breakdownPopoverPrefab;
    [SerializeField] private GameObject breakdownItemPrefab;
    private HudBreakdownPopover popoverInstance;
    private EventTrigger hoverEventTrigger;

    private string yieldName;
    private int currentAmount;
    private int deltaPerTurn;

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
        hoverEventTrigger = GetComponent<EventTrigger>();
        if (hoverEventTrigger == null)
            hoverEventTrigger = gameObject.AddComponent<EventTrigger>();

        hoverEventTrigger.triggers.Clear();

        var pointerEnterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        pointerEnterEntry.callback.AddListener(data => ShowBreakdownPopover());
        hoverEventTrigger.triggers.Add(pointerEnterEntry);

        var pointerExitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        pointerExitEntry.callback.AddListener(data => HideBreakdownPopover());
        hoverEventTrigger.triggers.Add(pointerExitEntry);
    }

    private void UnwireHoverListeners()
    {
        if (hoverEventTrigger != null)
            hoverEventTrigger.triggers.Clear();
    }

    /// <summary>
    /// Bind this widget to yield data.
    /// </summary>
    public void Bind(string name, int currentValue, int delta, Sprite icon)
    {
        yieldName = name;
        currentAmount = currentValue;
        deltaPerTurn = delta;

        // Update display

        // By default preserve prefab-assigned icon visuals at runtime.
        // Only override icon sprite when explicitly enabled.
        if (allowRuntimeIconOverride && iconImage != null && icon != null)
            iconImage.sprite = icon;

        if (amountText != null)
            amountText.text = currentValue.ToString("N0");

        if (deltaText != null)
        {
            deltaText.text = (deltaPerTurn >= 0 ? "+" : "") + deltaPerTurn.ToString("N0") + "/turn";
            deltaText.color = deltaPerTurn >= 0 ? positiveDeltaColor : negativeDeltaColor;
        }
    }

    private void ShowBreakdownPopover()
    {
        if (breakdownPopoverPrefab == null) return;

        // Destroy existing popover
        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);

        // Get root canvas to parent popover for top-rendering
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) return;

        // Instantiate as child of canvas to ensure it renders on top
        var popoverGO = Instantiate(breakdownPopoverPrefab, rootCanvas.transform, false);
        popoverInstance = popoverGO.GetComponent<HudBreakdownPopover>();
        
        if (popoverInstance != null)
        {
            PositionPopoverUnderIcon(popoverGO.GetComponent<RectTransform>());
            popoverInstance.Show(yieldName, GetBreakdownData());
        }
    }

    private void HideBreakdownPopover()
    {
        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);
        popoverInstance = null;
    }

    /// <summary>
    /// Get breakdown data for this yield (called by popover).
    /// Override or extend as needed per yield type.
    /// </summary>
    private object GetBreakdownData()
    {
        // Placeholder: return simplified structure
        // In full implementation, this would call HudBreakdownService.GetFoodBreakdown(), etc.
        return new
        {
            yieldName,
            currentAmount,
            deltaPerTurn
        };
    }

    private void PositionPopoverUnderIcon(RectTransform popoverRect)
    {
        if (popoverRect == null) return;
        var sourceRect = transform as RectTransform;
        if (sourceRect == null) return;

        Canvas canvas = popoverRect.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        
        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;
        
        // Get the widget's position in screen space
        var widgetWorldCorners = new Vector3[4];
        sourceRect.GetWorldCorners(widgetWorldCorners);
        Vector3 widgetBottomLeft = widgetWorldCorners[0];
        
        // Convert to canvas local space
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, widgetBottomLeft),
            canvas.worldCamera,
            out Vector2 canvasLocalPos))
        {
            popoverRect.anchorMin = Vector2.zero;
            popoverRect.anchorMax = Vector2.zero;
            popoverRect.pivot = new Vector2(0f, 1f);
            popoverRect.anchoredPosition = canvasLocalPos;
            popoverRect.localScale = Vector3.one;
        }
    }
}
