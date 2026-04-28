// Assets/Scripts/UI/HudYieldWidget.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

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
    private Button hoverButton;
    private HudBreakdownPopover popoverInstance;

    private string yieldName;
    private int currentAmount;
    private int deltaPerTurn;

    private void Awake()
    {
        hoverButton = GetComponent<Button>();
        if (hoverButton == null)
            hoverButton = gameObject.AddComponent<Button>();
    }

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
        if (hoverButton == null) return;

        var eventTrigger = GetComponent<EventTrigger>();
        if (eventTrigger == null)
            eventTrigger = gameObject.AddComponent<EventTrigger>();

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

        // Instantiate and position
        var popoverGO = Instantiate(breakdownPopoverPrefab, transform.parent);
        popoverInstance = popoverGO.GetComponent<HudBreakdownPopover>();
        
        if (popoverInstance != null)
            popoverInstance.Show(yieldName, GetBreakdownData());
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
}
