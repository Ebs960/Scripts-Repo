using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Helper UI component used by TechUI. Attach this to your techButtonPrefab.
/// It handles displaying the tech name and changing the button background color
/// based on research state and selection.
/// </summary>
[RequireComponent(typeof(Button))]
public class TechButtonUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public enum TechState { Available, Researched, Researching, Locked }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI techNameText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image iconImage; // <-- Add this line for the icon

    [Header("Colors")]
    [SerializeField] private Color researchedColor = Color.green;
    [SerializeField] private Color researchingColor = Color.yellow;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color lockedColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.cyan;

    public TechData RepresentedTech { get; private set; }

    private TechUI techUI;
    private Button button;
    private bool isSelected;
    private TechState currentState;
    
    // Expose background image for external callers that need to update its color
    public Image BackgroundImage => backgroundImage;

    /// <summary>
    /// Initializes the button with the tech data and owning TechUI.
    /// </summary>
    public void Initialize(TechData tech, TechUI ownerUI)
    {
        RepresentedTech = tech;
        techUI = ownerUI;

        // Auto-find components if not assigned (for procedurally created nodes)

        if (techNameText == null)
            techNameText = GetComponentInChildren<TextMeshProUGUI>();
        if (backgroundImage == null)
            backgroundImage = GetComponent<Image>();
        if (iconImage == null)
        {
            // Try to find an Image named "Icon" in children (common prefab pattern)
            var images = GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img.gameObject.name.ToLower().Contains("icon"))
                {
                    iconImage = img;
                    break;
                }
            }
        }

        if (techNameText != null)
            techNameText.text = tech.techName;
        if (iconImage != null)
            iconImage.sprite = tech.techIcon;

        button = GetComponent<Button>();
        if (button != null)
        {
            if (backgroundImage != null)
                button.targetGraphic = backgroundImage;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => techUI.SelectTech(RepresentedTech));
        }

        RefreshButtonColorBlock();
        RefreshColor();
    }

    public void SetState(TechState state)
    {
        currentState = state;
        if (button == null) button = GetComponent<Button>();
        if (button != null)
        {
            // Allow interaction only when the tech is available or currently researching
            button.interactable = (state == TechState.Available || state == TechState.Researching);
        }
        RefreshButtonColorBlock();
        RefreshColor();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        RefreshButtonColorBlock();
        RefreshColor();
    }

    private void RefreshColor()
    {
        if (backgroundImage == null) return;
        if (isSelected)
        {
            Color c = selectedColor; c.a = 1f;
            backgroundImage.color = c;
            return;
        }

        switch (currentState)
        {
            case TechState.Researched:
                { Color c = researchedColor; c.a = 1f; backgroundImage.color = c; }
                break;
            case TechState.Researching:
                { Color c = researchingColor; c.a = 1f; backgroundImage.color = c; }
                break;
            case TechState.Available:
                { Color c = availableColor; c.a = 1f; backgroundImage.color = c; }
                break;
            case TechState.Locked:
                { Color c = lockedColor; c.a = 1f; backgroundImage.color = c; }
                break;
        }
    }

    private void RefreshButtonColorBlock()
    {
        if (button == null) return;

        Color baseColor = GetDisplayColorForCurrentState();
        baseColor.a = 1f;
        Color hoverColor = currentState == TechState.Locked || isSelected
            ? baseColor
            : Color.Lerp(baseColor, Color.white, 0.18f);
        hoverColor.a = 1f;
        Color pressedColor = currentState == TechState.Locked || isSelected
            ? baseColor
            : Color.Lerp(baseColor, Color.black, 0.12f);
        pressedColor.a = 1f;

        ColorBlock colors = button.colors;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        colors.normalColor = baseColor;
        colors.highlightedColor = hoverColor;
        colors.selectedColor = baseColor;
        colors.pressedColor = pressedColor;
        colors.disabledColor = baseColor;
        button.colors = colors;
    }

    private Color GetDisplayColorForCurrentState()
    {
        if (isSelected)
            return selectedColor;

        switch (currentState)
        {
            case TechState.Researched:
                return researchedColor;
            case TechState.Researching:
                return researchingColor;
            case TechState.Available:
                return availableColor;
            case TechState.Locked:
            default:
                return lockedColor;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (RepresentedTech == null) return;
        TooltipSystem.Instance?.ShowTechTooltip(RepresentedTech, null);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipSystem.Instance?.RequestHideTooltip();
    }
} 