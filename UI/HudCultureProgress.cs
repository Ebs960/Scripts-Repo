// Assets/Scripts/UI/HudCultureProgress.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Culture progress widget for left HUD panel.
/// Shows current culture progress with yield icon and per-turn delta.
/// Displays breakdown on hover.
/// </summary>
public class HudCultureProgress : MonoBehaviour
{
    [Header("Progress Display")]
    [SerializeField] private Image progressBar;
    [SerializeField] private Image cultureIcon; // Icon of the currently adopted culture
    [SerializeField] private TextMeshProUGUI cultureNameText;
    [SerializeField] private TextMeshProUGUI progressText;

    [Header("Yield Display")]
    [SerializeField] private Image yieldIcon;
    [SerializeField] private GameObject yieldHoverTarget;
    [SerializeField] private HudYieldWidget yieldWidget;
    [SerializeField] private TextMeshProUGUI yieldPerTurnText;
    [SerializeField] private Color positiveYieldColor = Color.green;
    [SerializeField] private Color negativeYieldColor = Color.red;

    [Header("Interaction")]
    [SerializeField] private Button mainButton; // Click to open culture panel
    [SerializeField] private GameObject breakdownPopoverPrefab;
    [SerializeField] private Sprite placeholderCultureIcon;
    private HudBreakdownPopover popoverInstance;
    private EventTrigger hoverEventTrigger;

    private Civilization currentCiv;

    private void Start()
    {
        if (mainButton != null)
        {
            mainButton.onClick.AddListener(() =>
            {
                if (UIManager.Instance != null && currentCiv != null)
                    UIManager.Instance.ShowCulturePanel(currentCiv);
            });
        }

        if (yieldWidget == null)
            WireHoverListeners();
    }

    private void OnDestroy()
    {
        if (yieldWidget == null)
            UnwireHoverListeners();
        if (mainButton != null)
            mainButton.onClick.RemoveAllListeners();
        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);
    }

    private void WireHoverListeners()
    {
        var hoverTarget = yieldHoverTarget != null ? yieldHoverTarget : (yieldIcon != null ? yieldIcon.gameObject : gameObject);

        var graphic = hoverTarget.GetComponent<Graphic>();
        if (graphic != null)
            graphic.raycastTarget = true;

        var canvasGroup = hoverTarget.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = true;

        hoverEventTrigger = hoverTarget.GetComponent<EventTrigger>();
        if (hoverEventTrigger == null)
            hoverEventTrigger = hoverTarget.AddComponent<EventTrigger>();

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

    public void Bind(Civilization civ)
    {
        currentCiv = civ;
        if (civ == null) return;

        var adoptingCulture = civ.currentCulture;
        if (adoptingCulture != null)
        {
            if (cultureNameText != null)
                cultureNameText.text = adoptingCulture.cultureName;

            // Display the culture icon
            if (cultureIcon != null)
                cultureIcon.sprite = adoptingCulture.cultureIcon != null ? adoptingCulture.cultureIcon : placeholderCultureIcon;
        }
        else
        {
            if (cultureNameText != null)
                cultureNameText.text = "Culture";
            if (cultureIcon != null)
                cultureIcon.sprite = placeholderCultureIcon;
        }

        if (adoptingCulture != null && adoptingCulture.cultureCost > 0)
        {
            float progressPct = civ.currentCultureProgress / (float)adoptingCulture.cultureCost;
            if (progressBar != null)
                progressBar.fillAmount = Mathf.Clamp01(progressPct);

            if (progressText != null)
                progressText.text = $"{Mathf.FloorToInt(civ.currentCultureProgress)}/{adoptingCulture.cultureCost}";

            Debug.Log($"[HudCultureProgress] Bind civ={civ.civData?.civName} culture={adoptingCulture.cultureName} progress={civ.currentCultureProgress} cost={adoptingCulture.cultureCost} fill={(progressBar != null ? progressBar.fillAmount : -1f)}");
        }
        else
        {
            if (progressBar != null)
                progressBar.fillAmount = 0;
            if (progressText != null)
                progressText.text = "0/0";
        }

        UpdateYieldDisplay(civ);
    }

    private void UpdateYieldDisplay(Civilization civ)
    {
        int culturePerTurn = civ.cachedCulturePerTurn;

        if (yieldWidget != null)
            yieldWidget.Bind("Culture", civ.culture, culturePerTurn, null);

        if (yieldPerTurnText != null)
        {
            yieldPerTurnText.text = (culturePerTurn >= 0 ? "+" : "") + culturePerTurn.ToString("N0") + "/turn";
            yieldPerTurnText.color = culturePerTurn >= 0 ? positiveYieldColor : negativeYieldColor;
        }
    }

    private void ShowBreakdownPopover()
    {
        if (yieldWidget != null) return;
        if (breakdownPopoverPrefab == null || currentCiv == null) return;

        if (popoverInstance != null)
            Destroy(popoverInstance.gameObject);

        Canvas widgetCanvas = GetComponentInParent<Canvas>();
        if (widgetCanvas == null) return;
        var rootCanvas = widgetCanvas.rootCanvas != null ? widgetCanvas.rootCanvas : widgetCanvas;

        var popoverGO = Instantiate(breakdownPopoverPrefab, rootCanvas.transform, false);
        popoverGO.transform.SetAsLastSibling();
        popoverInstance = popoverGO.GetComponent<HudBreakdownPopover>();
        
        if (popoverInstance != null)
            popoverInstance.ShowAtSource("Culture", null, transform as RectTransform, new Vector2(0f, -12f));
    }

    private void HideBreakdownPopover()
    {
        if (popoverInstance != null)
            popoverInstance.NotifySourceHoverExit();
    }
}
