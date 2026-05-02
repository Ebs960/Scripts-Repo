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
    [SerializeField] private Image techIcon; // Icon of the currently researched tech
    [SerializeField] private TextMeshProUGUI techNameText;
    [SerializeField] private TextMeshProUGUI progressText;

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

            float progressPct = civ.currentTechProgress / (float)researchTech.scienceCost;
            if (progressBar != null)
                progressBar.fillAmount = Mathf.Clamp01(progressPct);

            if (progressText != null)
                progressText.text = $"{civ.currentTechProgress}/{researchTech.scienceCost}";
        }
        else
        {
            if (techNameText != null)
                techNameText.text = "No Research";
            if (techIcon != null)
                techIcon.sprite = placeholderTechIcon;
            if (progressBar != null)
                progressBar.fillAmount = 0;
            if (progressText != null)
                progressText.text = "0/0";
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

        var popoverGO = Instantiate(breakdownPopoverPrefab, transform.parent);
        popoverInstance = popoverGO.GetComponent<HudBreakdownPopover>();
        
        if (popoverInstance != null)
            popoverInstance.Show("Science", null);
    }

    private void HideBreakdownPopover()
    {
        if (popoverInstance != null)
            popoverInstance.NotifySourceHoverExit();
    }
}
