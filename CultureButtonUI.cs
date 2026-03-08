using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Helper UI component used by CultureUI. Attach this to your cultureButtonPrefab.
/// It displays the culture name and updates its appearance based on state/selection.
/// </summary>
[RequireComponent(typeof(Button))]
public class CultureButtonUI : MonoBehaviour
{
    public enum CultureState { Available, Researched, Researching, Locked }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI cultureNameText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image iconImage;

    [Header("Colors")]
    [SerializeField] private Color researchedColor = Color.green;
    [SerializeField] private Color researchingColor = Color.yellow;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color lockedColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.cyan;

    public CultureData RepresentedCulture { get; private set; }

    private CultureUI cultureUI;
    private Button button;
    private bool isSelected;
    private CultureState currentState;

    public void Initialize(CultureData culture, CultureUI ownerUI)
    {
        RepresentedCulture = culture;
        cultureUI = ownerUI;

        if (cultureNameText == null)
            cultureNameText = GetComponentInChildren<TextMeshProUGUI>();
        if (backgroundImage == null)
            backgroundImage = GetComponent<Image>();
        if (iconImage == null)
        {
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

        if (cultureNameText != null)
            cultureNameText.text = culture.cultureName;
        if (iconImage != null)
            iconImage.sprite = culture.cultureIcon;

        button = GetComponent<Button>();
        if (button != null)
        {
            if (backgroundImage != null)
                button.targetGraphic = backgroundImage;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => cultureUI.SelectCulture(RepresentedCulture));
        }

        RefreshButtonColorBlock();
        RefreshColor();
    }

    public void SetState(CultureState state)
    {
        currentState = state;
        if (button == null) button = GetComponent<Button>();
        if (button != null)
        {
            button.interactable = (state != CultureState.Researched);
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
            backgroundImage.color = selectedColor;
            return;
        }
        switch (currentState)
        {
            case CultureState.Researched:
                backgroundImage.color = researchedColor;
                break;
            case CultureState.Researching:
                backgroundImage.color = researchingColor;
                break;
            case CultureState.Available:
                backgroundImage.color = availableColor;
                break;
            case CultureState.Locked:
                backgroundImage.color = lockedColor;
                break;
        }
    }

    private void RefreshButtonColorBlock()
    {
        if (button == null) return;

        Color baseColor = GetDisplayColorForCurrentState();
        Color hoverColor = currentState == CultureState.Locked || isSelected
            ? baseColor
            : Color.Lerp(baseColor, Color.white, 0.18f);
        Color pressedColor = currentState == CultureState.Locked || isSelected
            ? baseColor
            : Color.Lerp(baseColor, Color.black, 0.12f);

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
            case CultureState.Researched:
                return researchedColor;
            case CultureState.Researching:
                return researchingColor;
            case CultureState.Available:
                return availableColor;
            case CultureState.Locked:
            default:
                return lockedColor;
        }
    }
} 