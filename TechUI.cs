using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class TechUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject techPanel;
    [SerializeField] private Transform techButtonContainer; // ScrollRect's content
    [SerializeField] private GameObject techButtonPrefab; // Prefab for tech buttons

    [Header("Info Panel")]
    [SerializeField] private TextMeshProUGUI selectedTechNameText;
    [SerializeField] private TextMeshProUGUI selectedTechDescriptionText;
    [SerializeField] private TextMeshProUGUI selectedTechCostText;
    [SerializeField] private TextMeshProUGUI selectedTechPrerequisitesText;
    [SerializeField] private TextMeshProUGUI selectedTechUnlocksText;
    [SerializeField] private Button closeButton;

    private Civilization playerCiv;
    private TechData currentlySelectedTech;
    private List<TechButtonUI> techButtons = new List<TechButtonUI>(); // To manage button states

    void Start()
    {
        if (closeButton != null) 
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => 
            {
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.HidePanel("techPanel");
                }
                else
                {
                    Debug.LogError("TechUI: UIManager.Instance is null. Cannot hide panel.");
                    if (techPanel != null) techPanel.SetActive(false); 
                }
            });
        }
    }

    public void Show(Civilization civ)
    {
        playerCiv = civ;
        if (playerCiv == null)
        {
            Debug.LogError("TechUI Show called with null civ");
            return;
        }
        // Hide other panels (unit info, city, etc) when Tech UI is shown
        if (UIManager.Instance != null) {
            if (UIManager.Instance.unitInfoPanel != null)
                UIManager.Instance.unitInfoPanel.SetActive(false);
            if (UIManager.Instance.cityPanel != null)
                UIManager.Instance.cityPanel.SetActive(false);
            // Add more panels here if needed
        }
        UIManager.Instance.ShowPanel("techPanel");
        PopulateTechTree();
        ClearInfoPanel(); // Do not auto-select any tech
    }

    public void Hide()
    {
        UIManager.Instance.HidePanel("techPanel");
        // Restore other panels (unit info, city, etc) when Tech UI is closed
        if (UIManager.Instance != null) {
            if (UIManager.Instance.unitInfoPanel != null)
                UIManager.Instance.unitInfoPanel.SetActive(true);
            // Do not restore city panel unless it was open before, but for now, leave it hidden
        }
    }

    void PopulateTechTree()
    {
        // Clear existing buttons
        foreach (Transform child in techButtonContainer)
        {
            Destroy(child.gameObject);
        }
        techButtons.Clear();

        if (TechManager.Instance == null || TechManager.Instance.allTechs == null)
        {
            Debug.LogError("TechManager or its techs not available.");
            return;
        }

        foreach (TechData tech in TechManager.Instance.allTechs.OrderBy(t => t.scienceCost))
        {
            GameObject buttonGO = Instantiate(techButtonPrefab, techButtonContainer);
            TechButtonUI techButtonUI = buttonGO.GetComponent<TechButtonUI>();
            if (techButtonUI != null)
            {
                techButtonUI.Initialize(tech, this);
                techButtons.Add(techButtonUI);
                UpdateTechButtonState(techButtonUI, tech);
            }
            else
            {
                // Fallback if TechButtonUI script is not on the prefab
                Button button = buttonGO.GetComponent<Button>();
                TextMeshProUGUI buttonText = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null) buttonText.text = tech.techName;
                if (button != null) button.onClick.AddListener(() => SelectTech(tech));
            }
        }
        RefreshTechButtonStates();
    }

    public void SelectTech(TechData tech)
    {
        currentlySelectedTech = tech;
        UpdateInfoPanel(tech);

        Debug.Log($"[TechUI] Attempting to research: {tech?.techName}");
        if (playerCiv != null && playerCiv.CanResearch(tech))
        {
            Debug.Log($"[TechUI] CanResearch returned TRUE for {tech.techName}. Calling StartResearch.");
            playerCiv.StartResearch(tech);
            RefreshUI();
        }
        else
        {
            Debug.Log($"[TechUI] CanResearch returned FALSE for {tech?.techName}.");
        }

        foreach (var btnUI in techButtons)
        {
            btnUI.SetSelected(tech == btnUI.RepresentedTech);
        }
    }

    void UpdateInfoPanel(TechData tech)
    {
        if (tech == null)
        {
            ClearInfoPanel();
            return;
        }

        selectedTechNameText.text = tech.techName;
        selectedTechDescriptionText.text = tech.description;
        selectedTechCostText.text = $"Cost: {tech.scienceCost} Science";

        string prereqs = "Prerequisites: ";
        if (tech.requiredTechnologies != null && tech.requiredTechnologies.Length > 0)
        {
            prereqs += string.Join(", ", tech.requiredTechnologies.Select(t => t.techName));
        }
        else
        {
            prereqs += "None";
        }
        selectedTechPrerequisitesText.text = prereqs;

        string unlocks = "Unlocks: ";
        List<string> unlockItems = new List<string>();
        if (tech.unlockedUnits != null) unlockItems.AddRange(tech.unlockedUnits.Select(u => u.unitName + " (Unit)"));
        if (tech.unlockedWorkerUnits != null) unlockItems.AddRange(tech.unlockedWorkerUnits.Select(w => w.unitName + " (Worker)"));
        if (tech.unlockedBuildings != null) unlockItems.AddRange(tech.unlockedBuildings.Select(b => b.buildingName + " (Building)"));
        // Add other unlock types here (abilities, policies, etc.)
        if (unlockItems.Count > 0)
        {
            unlocks += string.Join(", ", unlockItems);
        }
        else
        {
            unlocks += "Nothing yet";
        }
        selectedTechUnlocksText.text = unlocks;
    }
    
    void ClearInfoPanel()
    {
        selectedTechNameText.text = "Select a Technology";
        selectedTechDescriptionText.text = "";
        selectedTechCostText.text = "";
        selectedTechPrerequisitesText.text = "";
        selectedTechUnlocksText.text = "";
    }

    public void RefreshUI()
    {
        Debug.Log("[TechUI] RefreshUI called");
        if (playerCiv == null) return;
        // Update button states
        RefreshTechButtonStates();
        // Update info panel for the currently selected tech or current research
        if (playerCiv.currentTech != null)
        {
            UpdateInfoPanel(playerCiv.currentTech);
             foreach (var btnUI in techButtons)
            {
                btnUI.SetSelected(playerCiv.currentTech == btnUI.RepresentedTech);
            }
        }
        else if (currentlySelectedTech != null)
        {
            UpdateInfoPanel(currentlySelectedTech);
        } else {
            ClearInfoPanel();
        }
    }

    private void UpdateTechButtonState(TechButtonUI buttonUI, TechData tech)
    {
        if (playerCiv.researchedTechs.Contains(tech))
        {
            buttonUI.SetState(TechButtonUI.TechState.Researched);
        }
        else if (playerCiv.currentTech == tech)
        {
            buttonUI.SetState(TechButtonUI.TechState.Researching);
        }
        else if (playerCiv.CanResearch(tech))
        {
            buttonUI.SetState(TechButtonUI.TechState.Available);
        }
        else
        {
            buttonUI.SetState(TechButtonUI.TechState.Locked);
        }
    }
    
    public void RefreshTechButtonStates()
    {
        foreach (var btnUI in techButtons)
        {
            UpdateTechButtonState(btnUI, btnUI.RepresentedTech);
        }
    }
}

// Helper script for the TechButton prefab (TechButtonUI.cs)
// You would create this script and attach it to your techButtonPrefab
/*
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TechButtonUI : MonoBehaviour
{
    public TechData RepresentedTech { get; private set; }
    private TechUI techUI; // Reference to the main TechUI

    [SerializeField] private TextMeshProUGUI techNameText;
    [SerializeField] private Image iconImage; // Optional: if techs have icons
    [SerializeField] private Image backgroundImage; // To change color based on state

    [SerializeField] private Color researchedColor = Color.green;
    [SerializeField] private Color researchingColor = Color.yellow;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color lockedColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.cyan; // For when this tech is selected in the info panel

    private Button button;
    private bool isSelected = false;


    public enum TechState { Available, Researched, Researching, Locked }
    private TechState currentState;

    public void Initialize(TechData tech, TechUI ownerUI)
    {
        RepresentedTech = tech;
        techUI = ownerUI;
        techNameText.text = tech.techName;
        // if (iconImage != null && tech.icon != null) iconImage.sprite = tech.icon;

        button = GetComponent<Button>();
        button.onClick.AddListener(() => techUI.SelectTech(RepresentedTech));
    }

    public void SetState(TechState state)
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
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        SetState(currentState); // Re-apply color based on new selection state
    }
}
*/ 