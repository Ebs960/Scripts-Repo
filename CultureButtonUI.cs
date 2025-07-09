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

        if (cultureNameText != null)
            cultureNameText.text = culture.cultureName;

        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => cultureUI.SelectCulture(RepresentedCulture));
        }
    }

    public void SetState(CultureState state)
    {
        currentState = state;
        RefreshColor();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
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
} 