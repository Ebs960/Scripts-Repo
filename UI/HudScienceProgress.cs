// Assets/Scripts/UI/HudScienceProgress.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Science progress widget for left HUD panel.
/// Shows current tech research progress with yield icon and per-turn delta.
/// Displays breakdown on hover.
/// </summary>
public class HudScienceProgress : MonoBehaviour
{
    [Header("Progress Display")]
    [SerializeField] private Image progressBar;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Image techIcon; // Icon of the currently researched tech
    [SerializeField] private TextMeshProUGUI techNameText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private TextMeshProUGUI turnsRemainingText;

    [Header("Yield Display")]
    [SerializeField] private Image yieldIcon;
    [SerializeField] private GameObject yieldHoverTarget;
    [SerializeField] private HudYieldWidget yieldWidget;
    [SerializeField] private TextMeshProUGUI yieldPerTurnText;
    [SerializeField] private Color positiveYieldColor = Color.green;
    [SerializeField] private Color negativeYieldColor = Color.red;

    [Header("Interaction")]
    [SerializeField] private Button mainButton; // Click to open tech panel
    [SerializeField] private GameObject breakdownPopoverPrefab;
    [SerializeField] private Sprite placeholderTechIcon;
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
                    UIManager.Instance.ShowTechPanel(currentCiv);
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

        var researchTech = civ.currentTech;
        if (researchTech != null)
        {
            if (techNameText != null)
                techNameText.text = researchTech.techName;

            // Display the tech icon
            if (techIcon != null)
                techIcon.sprite = researchTech.techIcon != null ? researchTech.techIcon : placeholderTechIcon;

            if (researchTech.scienceCost <= 0)
            {
                SetProgressValue(0f);
                if (progressText != null) progressText.text = "0/0";
                if (turnsRemainingText != null) turnsRemainingText.text = "Turns: 0";
            }
            else
            {
                float progressPct = civ.currentTechProgress / (float)researchTech.scienceCost;
                SetProgressValue(progressPct);

                if (progressText != null)
                    progressText.text = $"{Mathf.FloorToInt(civ.currentTechProgress)}/{researchTech.scienceCost}";

                if (turnsRemainingText != null)
                {
                    float remaining = Mathf.Max(0f, researchTech.scienceCost - civ.currentTechProgress);
                    int sciencePerTurn = Mathf.Max(0, civ.cachedSciencePerTurn);
                    turnsRemainingText.text = sciencePerTurn > 0
                        ? $"Turns: {Mathf.CeilToInt(remaining / sciencePerTurn)}"
                        : "Turns: —";
                }
            }

        }
        else
        {
            if (techNameText != null)
                techNameText.text = "No Research";
            if (techIcon != null)
                techIcon.sprite = placeholderTechIcon;
            SetProgressValue(0f);
            if (progressText != null)
                progressText.text = "0/0";
            if (turnsRemainingText != null)
                turnsRemainingText.text = "Turns: —";
        }

        UpdateYieldDisplay(civ);
    }

    private void UpdateYieldDisplay(Civilization civ)
    {
        int sciencePerTurn = civ.cachedSciencePerTurn;

        if (yieldWidget != null)
            yieldWidget.Bind("Science", civ.science, sciencePerTurn, null);

        if (yieldPerTurnText != null)
        {
            yieldPerTurnText.text = (sciencePerTurn >= 0 ? "+" : "") + sciencePerTurn.ToString("N0") + "/turn";
            yieldPerTurnText.color = sciencePerTurn >= 0 ? positiveYieldColor : negativeYieldColor;
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
            popoverInstance.ShowAtSource("Science", null, transform as RectTransform, new Vector2(0f, -24f));
    }

    private void SetProgressValue(float progress)
    {
        float clamped = Mathf.Clamp01(progress);
        if (progressSlider != null)
            progressSlider.value = clamped;
        else if (progressBar != null)
            progressBar.fillAmount = clamped;
    }

    private void HideBreakdownPopover()
    {
        if (popoverInstance != null)
            popoverInstance.NotifySourceHoverExit();
    }
}
