using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class CultureUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject culturePanel;
    [SerializeField] private Transform cultureButtonContainer; // ScrollRect's content
    [SerializeField] private GameObject cultureButtonPrefab; // Prefab for culture buttons

    [Header("Info Panel")]
    [SerializeField] private TextMeshProUGUI selectedCultureNameText;
    [SerializeField] private TextMeshProUGUI selectedCultureDescriptionText;
    [SerializeField] private TextMeshProUGUI selectedCultureCostText;
    [SerializeField] private TextMeshProUGUI selectedCulturePrerequisitesText;
    [SerializeField] private TextMeshProUGUI selectedCultureUnlocksText;
    [SerializeField] private Button closeButton;

    private Civilization playerCiv;
    private CultureData currentlySelectedCulture;
    private List<CultureButtonUI> cultureButtons = new List<CultureButtonUI>();

    void Start()
    {
        if (closeButton != null) 
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => 
            {
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.HidePanel("culturePanel");
                }
                else
                {
                    Debug.LogError("CultureUI: UIManager.Instance is null. Cannot hide panel.");
                    if (culturePanel != null) culturePanel.SetActive(false); // Fallback
                }
            });
        }
    }

    public void Show(Civilization civ)
    {
        playerCiv = civ;
        if (playerCiv == null)
        {
            Debug.LogError("CultureUI Show called with null civ");
            return;
        }
        UIManager.Instance.ShowPanel("culturePanel");
        PopulateCultureOptions();
        ClearInfoPanel(); // Do not auto-select any culture
    }

    public void Hide()
    {
        // This method is called by the close button's new listener directly calling GameManager.HideCulturePanel().
        // If the root is being hidden, the internal panel will also be hidden.
        if (culturePanel != null && !this.gameObject.activeInHierarchy) 
        {
            // culturePanel.SetActive(false); // Let its state be tied to the root's state
        }
    }

    void PopulateCultureOptions()
    {
        foreach (Transform child in cultureButtonContainer)
        {
            Destroy(child.gameObject);
        }
        cultureButtons.Clear();

        if (CultureManager.Instance == null || CultureManager.Instance.allCultures == null)
        {
            Debug.LogError("CultureManager or its cultures not available.");
            return;
        }

        foreach (CultureData culture in CultureManager.Instance.allCultures.OrderBy(c => c.cultureCost))
        {
            GameObject buttonGO = Instantiate(cultureButtonPrefab, cultureButtonContainer);
            CultureButtonUI cultureButtonUI = buttonGO.GetComponent<CultureButtonUI>();
            if (cultureButtonUI != null)
            {
                cultureButtonUI.Initialize(culture, this);
                cultureButtons.Add(cultureButtonUI);
                UpdateCultureButtonState(cultureButtonUI, culture);
            }
            else
            {
                Button button = buttonGO.GetComponent<Button>();
                TextMeshProUGUI buttonText = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null) buttonText.text = culture.cultureName;
                if (button != null) button.onClick.AddListener(() => SelectCulture(culture));
            }
        }
        RefreshCultureButtonStates();
    }

    public void SelectCulture(CultureData culture)
    {
        currentlySelectedCulture = culture;
        UpdateInfoPanel(culture);

        // Immediately start adoption if possible
        if (playerCiv != null && playerCiv.CanCultivate(culture))
        {
            playerCiv.StartCulture(culture);
            RefreshUI();
        }

        foreach (var btnUI in cultureButtons)
        {
            btnUI.SetSelected(culture == btnUI.RepresentedCulture);
        }
    }

    void UpdateInfoPanel(CultureData culture)
    {
        if (culture == null)
        {
            ClearInfoPanel();
            return;
        }

        selectedCultureNameText.text = culture.cultureName;
        selectedCultureDescriptionText.text = culture.description;
        selectedCultureCostText.text = $"Cost: {culture.cultureCost} Culture";

        string prereqs = "Prerequisites: ";
        if (culture.requiredCultures != null && culture.requiredCultures.Length > 0)
        {
            prereqs += string.Join(", ", culture.requiredCultures.Select(c => c.cultureName));
        }
        else
        {
            prereqs += "None";
        }
        selectedCulturePrerequisitesText.text = prereqs;

        string unlocks = "Unlocks: ";
        List<string> unlockItems = new List<string>();
        if (culture.unlockedUnits != null) unlockItems.AddRange(culture.unlockedUnits.Select(u => u.unitName + " (Unit)"));
        if (culture.unlockedWorkerUnits != null) unlockItems.AddRange(culture.unlockedWorkerUnits.Select(w => w.unitName + " (Worker)"));
        if (culture.unlockedBuildings != null) unlockItems.AddRange(culture.unlockedBuildings.Select(b => b.buildingName + " (Building)"));
        if (culture.unlockedAbilities != null) unlockItems.AddRange(culture.unlockedAbilities.Select(a => a.abilityName + " (Ability)"));
        if (culture.unlocksPolicies != null) unlockItems.AddRange(culture.unlocksPolicies.Select(p => p.policyName + " (Policy)"));
        if (unlockItems.Count > 0)
        {
            unlocks += string.Join(", ", unlockItems);
        }
        else
        {
            unlocks += "Nothing yet";
        }
        selectedCultureUnlocksText.text = unlocks;
    }
    
    void ClearInfoPanel()
    {
        selectedCultureNameText.text = "Select a Culture";
        selectedCultureDescriptionText.text = "";
        selectedCultureCostText.text = "";
        selectedCulturePrerequisitesText.text = "";
        selectedCultureUnlocksText.text = "";
    }

    public void RefreshUI()
    {
        if (playerCiv == null) return;
        RefreshCultureButtonStates();
        if (playerCiv.currentCulture != null)
        {
            UpdateInfoPanel(playerCiv.currentCulture);
             foreach (var btnUI in cultureButtons)
            {
                btnUI.SetSelected(playerCiv.currentCulture == btnUI.RepresentedCulture);
            }
        }
        else if (currentlySelectedCulture != null)
        {
            UpdateInfoPanel(currentlySelectedCulture);
        }
        else
        {
            ClearInfoPanel();
        }
    }

    private void UpdateCultureButtonState(CultureButtonUI buttonUI, CultureData culture)
    {
        if (playerCiv.researchedCultures.Contains(culture))
        {
            buttonUI.SetState(CultureButtonUI.CultureState.Researched);
        }
        else if (playerCiv.currentCulture == culture)
        {
            buttonUI.SetState(CultureButtonUI.CultureState.Researching);
        }
        else if (playerCiv.CanCultivate(culture))
        {
            buttonUI.SetState(CultureButtonUI.CultureState.Available);
        }
        else
        {
            buttonUI.SetState(CultureButtonUI.CultureState.Locked);
        }
    }
    public void RefreshCultureButtonStates()
    {
        foreach (var btnUI in cultureButtons)
        {
            UpdateCultureButtonState(btnUI, btnUI.RepresentedCulture);
        }
    }
}


// Helper script for the CultureButton prefab (CultureButtonUI.cs)
// You would create this script and attach it to your cultureButtonPrefab
/*
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CultureButtonUI : MonoBehaviour
{
    public CultureData RepresentedCulture { get; private set; }
    private CultureUI cultureUI;

    [SerializeField] private TextMeshProUGUI cultureNameText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Image backgroundImage;

    [SerializeField] private Color researchedColor = Color.green;
    [SerializeField] private Color researchingColor = Color.yellow;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color lockedColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.cyan;
    
    private Button button;
    private bool isSelected = false;

    public enum CultureState { Available, Researched, Researching, Locked }
    private CultureState currentState;

    public void Initialize(CultureData culture, CultureUI ownerUI)
    {
        RepresentedCulture = culture;
        cultureUI = ownerUI;
        cultureNameText.text = culture.cultureName;
        // if (iconImage != null && culture.icon != null) iconImage.sprite = culture.icon;

        button = GetComponent<Button>();
        button.onClick.AddListener(() => cultureUI.SelectCulture(RepresentedCulture));
    }

    public void SetState(CultureState state)
    {
        currentState = state;
        if (isSelected)
        {
            backgroundImage.color = selectedColor;
        }
        else
        {
            switch (state)
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

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        SetState(currentState); // Re-apply color based on new selection state
    }
}
*/ 