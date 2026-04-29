// Assets/Scripts/UI/HudYieldWidget.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Reusable widget for displaying a single yield metric on the top bar.
/// Shows: icon, current amount, and per-turn delta.
/// 
/// Supports hover-to-expand breakdown popover (via HudBreakdownPopover).
/// </summary>
public class HudYieldWidget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
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
    private HudBreakdownPopover popoverInstance;

    private string yieldName;
    private int currentAmount;
    private int deltaPerTurn;

    private void OnDestroy()
    {
        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);
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

    public void OnPointerEnter(PointerEventData eventData)
    {
        ShowBreakdownPopover(eventData != null ? (Vector2?)eventData.position : null);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HideBreakdownPopover();
    }

    private void ShowBreakdownPopover(Vector2? pointerScreenPosition = null)
    {
        if (breakdownPopoverPrefab == null) return;

        // Destroy existing popover
        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);

        // Instantiate and position
        var popoverGO = Instantiate(breakdownPopoverPrefab, transform);
        popoverInstance = popoverGO.GetComponent<HudBreakdownPopover>();
        
        if (popoverInstance != null)
        {
            PositionPopoverUnderIcon(popoverGO.GetComponent<RectTransform>());
            popoverInstance.Show(yieldName, GetBreakdownData(), pointerScreenPosition);
        }
    }

    private void HideBreakdownPopover()
    {
        if (popoverInstance != null)
            popoverInstance.NotifySourceHoverExit();
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

        popoverRect.anchorMin = new Vector2(0f, 1f);
        popoverRect.anchorMax = new Vector2(0f, 1f);
        popoverRect.pivot = new Vector2(0f, 1f);
        popoverRect.anchoredPosition = new Vector2(0f, -sourceRect.rect.height);
        popoverRect.localScale = Vector3.one;
    }
}
