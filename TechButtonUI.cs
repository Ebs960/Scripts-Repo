using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Helper UI component used by TechUI. Attach this to your techButtonPrefab.
/// It handles displaying the tech name and changing the button background color
/// based on research state and selection.
/// </summary>
[RequireComponent(typeof(Button))]
public class TechButtonUI : MonoBehaviour
{
    public enum TechState { Available, Researched, Researching, Locked }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI techNameText;
    [SerializeField] private Image backgroundImage;

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

    /// <summary>
    /// Initializes the button with the tech data and owning TechUI.
    /// </summary>
    public void Initialize(TechData tech, TechUI ownerUI)
    {
        RepresentedTech = tech;
        techUI = ownerUI;

        if (techNameText != null)
            techNameText.text = tech.techName;

        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => techUI.SelectTech(RepresentedTech));
        }
    }

    public void SetState(TechState state)
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
            case TechState.Researched:
                backgroundImage.color = researchedColor;
                break;
            case TechState.Researching:
                backgroundImage.color = researchingColor;
                break;
            case TechState.Available:
                backgroundImage.color = availableColor;
                break;
            case TechState.Locked:
                backgroundImage.color = lockedColor;
                break;
        }
    }
} 