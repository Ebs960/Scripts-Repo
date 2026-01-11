using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class DiplomacyUI : MonoBehaviour
{
    [Header("Main Panel")]
    public GameObject mainPanel;
    public Button closeButton;

    [Header("Left Section - Civilization List")]
    public Transform civListContainer;
    public GameObject civListItemPrefab;
    public ScrollRect civListScroll;

    [Header("Middle Section - Selected Civ Info")]
    public Image selectedCivIcon;
    public TextMeshProUGUI selectedCivName;
    public TextMeshProUGUI selectedCivLeader;
    public TextMeshProUGUI relationshipStatus;
    public TextMeshProUGUI militaryStrengthText;
    public TextMeshProUGUI economyStrengthText;
    public TextMeshProUGUI scienceProgressText;
    public TextMeshProUGUI faithStatusText;
    public TextMeshProUGUI governmentText;
    public TextMeshProUGUI civDescriptionText;

    [Header("Right Section - Diplomatic Actions")]
    public Button declareWarButton;
    public Button proposePeaceButton;
    public Button proposeAllianceButton;
    public Button denounceButton;

    // Current state
    private Civilization playerCiv;
    private Civilization selectedCiv;
    private List<GameObject> civListItems = new List<GameObject>();

    void Start()
    {
        // Validate all required references
        ValidateUIReferences();
        
        // Validate close button setup
        ValidateCloseButton();
        
        // Set up button listeners
        SetupButtonListeners();

        // Hide the entire GameObject initially (instead of just mainPanel)
        gameObject.SetActive(false);
    }
    
    private void ValidateCloseButton()
    {
        if (closeButton != null)
        {
Debug.Log($"[DiplomacyUI] Close button parent: {closeButton.transform.parent?.name}");
            
            // Check if close button is on the main Canvas
            Canvas parentCanvas = closeButton.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
}
            else
            {
                Debug.LogError("[DiplomacyUI] Close button is not on a Canvas! This will cause click issues.");
            }
            
            // Check if the Canvas has GraphicRaycaster
            if (parentCanvas != null && parentCanvas.GetComponent<GraphicRaycaster>() == null)
            {
                Debug.LogError("[DiplomacyUI] Canvas missing GraphicRaycaster! Close button won't receive clicks.");
            }
        }
    }
    
    private void SetupButtonListeners()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => {
Hide();
            });
}
        
        if (declareWarButton != null)
        {
            declareWarButton.onClick.AddListener(OnDeclareWarClicked);
        }
        if (proposePeaceButton != null)
        {
            proposePeaceButton.onClick.AddListener(OnProposePeaceClicked);
        }
        if (proposeAllianceButton != null)
        {
            proposeAllianceButton.onClick.AddListener(OnProposeAllianceClicked);
        }
        if (denounceButton != null)
        {
            denounceButton.onClick.AddListener(OnDenounceClicked);
        }
    }
    
    private void ValidateUIReferences()
    {
// Main Panel
Debug.Log($"  closeButton: {(closeButton != null ? "OK" : "NULL")}");
        
        // Left Section
Debug.Log($"  civListItemPrefab: {(civListItemPrefab != null ? "OK" : "NULL")}");
// Middle Section
Debug.Log($"  selectedCivName: {(selectedCivName != null ? "OK" : "NULL")}");
Debug.Log($"  relationshipStatus: {(relationshipStatus != null ? "OK" : "NULL")}");
        
        // Right Section
Debug.Log($"  proposePeaceButton: {(proposePeaceButton != null ? "OK" : "NULL")}");
Debug.Log($"  denounceButton: {(denounceButton != null ? "OK" : "NULL")}");
        
        // Count critical missing references
        int criticalMissing = 0;
        if (mainPanel == null) criticalMissing++;
        if (civListContainer == null) criticalMissing++;
        if (civListItemPrefab == null) criticalMissing++;
        if (selectedCivIcon == null) criticalMissing++;
        if (declareWarButton == null) criticalMissing++;
        
        if (criticalMissing > 0)
        {
            Debug.LogError($"[DiplomacyUI] {criticalMissing} critical UI references are missing! Diplomacy UI may not function properly.");
        }
        else
        {
}
    }

    public void Show(Civilization playerCiv)
    {
this.playerCiv = playerCiv;
        
        // Simply activate the entire GameObject - this activates everything at once
        gameObject.SetActive(true);
UpdateCivilizationList();
        ClearSelectedCiv();
    }
    
    private void ClearSelectedCiv()
    {
        selectedCiv = null;
        selectedCivIcon.gameObject.SetActive(false);
        selectedCivName.text = "";
        selectedCivLeader.text = "";
        relationshipStatus.text = "";
        militaryStrengthText.text = "";
        economyStrengthText.text = "";
        scienceProgressText.text = "";
        faithStatusText.text = "";
        governmentText.text = "";
        civDescriptionText.text = "";
        
        // Hide diplomatic actions
        declareWarButton.gameObject.SetActive(false);
        proposePeaceButton.gameObject.SetActive(false);
        proposeAllianceButton.gameObject.SetActive(false);
        denounceButton.gameObject.SetActive(false);
    }

    public void Hide()
    {
// Simply deactivate the entire GameObject - this hides everything at once
        gameObject.SetActive(false);
selectedCiv = null;
    }

    private void UpdateCivilizationList()
    {
// Clear existing list items
        foreach (var item in civListItems)
            Destroy(item);
        civListItems.Clear();

        if (CivilizationManager.Instance == null)
        {
            Debug.LogError("[DiplomacyUI] CivilizationManager.Instance is null! Cannot populate civilization list.");
            return;
        }

        var allCivs = CivilizationManager.Instance.GetAllCivs();
        if (allCivs == null)
        {
            Debug.LogError("[DiplomacyUI] GetAllCivs() returned null! Cannot populate civilization list.");
            return;
        }
if (civListItemPrefab == null)
        {
            Debug.LogError("[DiplomacyUI] civListItemPrefab is null! Cannot create civilization list items.");
            return;
        }

        if (civListContainer == null)
        {
            Debug.LogError("[DiplomacyUI] civListContainer is null! Cannot parent civilization list items.");
            return;
        }

        int itemsCreated = 0;
        foreach (var civ in allCivs)
        {
            if (civ == playerCiv) continue;
var listItem = Instantiate(civListItemPrefab, civListContainer);
            var button = listItem.GetComponent<Button>();
            var icon = listItem.GetComponentInChildren<Image>();
            var text = listItem.GetComponentInChildren<TextMeshProUGUI>();

            if (icon != null && civ.civData.icon != null)
                icon.sprite = civ.civData.icon;
            if (text != null)
                text.text = civ.civData.civName;

            button.onClick.AddListener(() => OnCivilizationSelected(civ));
            civListItems.Add(listItem);
            itemsCreated++;
        }
if (itemsCreated == 0)
        {
            Debug.LogWarning("[DiplomacyUI] No civilization list items were created! This could mean no other civilizations exist or all are the player civilization.");
        }
    }

    private void OnCivilizationSelected(Civilization civ)
    {
        selectedCiv = civ;
        UpdateSelectedCivInfo();
        UpdateDiplomaticActions();
    }

    private void UpdateSelectedCivInfo()
    {
        if (selectedCiv == null) return;

        selectedCivIcon.gameObject.SetActive(true);
        selectedCivIcon.sprite = selectedCiv.civData.icon;
        selectedCivName.text = selectedCiv.civData.civName;
        selectedCivLeader.text = $"Leader: {selectedCiv.leader.leaderName}";
        
        // Get relationship status
        var relation = playerCiv.relations[selectedCiv];
        relationshipStatus.text = $"Status: {relation}";

        // Military comparison (total unit strength)
        int playerStrength = 0;
        foreach (var unit in playerCiv.combatUnits)
        {
            playerStrength += unit.CurrentAttack + unit.CurrentDefense;
        }

        int selectedStrength = 0;
        foreach (var unit in selectedCiv.combatUnits)
        {
            selectedStrength += unit.CurrentAttack + unit.CurrentDefense;
        }

        militaryStrengthText.text = $"Military: {(playerStrength > selectedStrength ? "Stronger" : playerStrength < selectedStrength ? "Weaker" : "Equal")}";

        // Economy comparison (gold per turn from all cities)
        int playerGold = 0;
        foreach (var city in playerCiv.cities)
        {
            playerGold += city.GetGoldPerTurn();
        }
        playerGold = Mathf.RoundToInt(playerGold * (1 + playerCiv.goldModifier));

        int selectedGold = 0;
        foreach (var city in selectedCiv.cities)
        {
            selectedGold += city.GetGoldPerTurn();
        }
        selectedGold = Mathf.RoundToInt(selectedGold * (1 + selectedCiv.goldModifier));

        economyStrengthText.text = $"Economy: {(playerGold > selectedGold ? "Stronger" : playerGold < selectedGold ? "Weaker" : "Equal")}";

        // Science progress
        scienceProgressText.text = $"Technology Age: {selectedCiv.currentTech?.techAge.ToString().Replace("Age", " Age") ?? "None"}";

        // Faith status
        faithStatusText.text = $"Religion: {(selectedCiv.hasFoundedReligion ? selectedCiv.foundedReligion.religionName : "None")}";

        // Government
        governmentText.text = $"Government: {selectedCiv.currentGovernment?.governmentName ?? "None"}";

        // Civ Description
        civDescriptionText.text = CivDescriptionGenerator.GenerateDescription(selectedCiv, playerCiv);
    }

    private void UpdateDiplomaticActions()
    {
        if (selectedCiv == null) return;

        var relation = playerCiv.relations[selectedCiv];
        
        // Show/hide appropriate buttons based on current relationship
        declareWarButton.gameObject.SetActive(relation != DiplomaticState.War);
        proposePeaceButton.gameObject.SetActive(relation == DiplomaticState.War);
        proposeAllianceButton.gameObject.SetActive(relation == DiplomaticState.Peace);
        proposeAllianceButton.interactable = relation == DiplomaticState.Peace;
        denounceButton.gameObject.SetActive(relation != DiplomaticState.War);
    }

    private void OnDeclareWarClicked()
    {
        if (selectedCiv == null) return;
        
        playerCiv.SetRelation(selectedCiv, DiplomaticState.War);
        selectedCiv.SetRelation(playerCiv, DiplomaticState.War);
        
        UpdateSelectedCivInfo();
        UpdateDiplomaticActions();
        
        UIManager.Instance.ShowNotification($"War declared against {selectedCiv.civData.civName}!");
    }

    private void OnProposePeaceClicked()
    {
        if (selectedCiv == null) return;
        
        playerCiv.SetRelation(selectedCiv, DiplomaticState.Peace);
        selectedCiv.SetRelation(playerCiv, DiplomaticState.Peace);
        
        UpdateSelectedCivInfo();
        UpdateDiplomaticActions();
        
        UIManager.Instance.ShowNotification($"Peace established with {selectedCiv.civData.civName}");
    }

    private void OnProposeAllianceClicked()
    {
        if (selectedCiv == null) return;
        
        if (playerCiv.relations[selectedCiv] == DiplomaticState.Peace)
        {
            playerCiv.SetRelation(selectedCiv, DiplomaticState.Alliance);
            selectedCiv.SetRelation(playerCiv, DiplomaticState.Alliance);
            
            UpdateSelectedCivInfo();
            UpdateDiplomaticActions();
            
            UIManager.Instance.ShowNotification($"Alliance formed with {selectedCiv.civData.civName}!");
        }
    }

    private void OnDenounceClicked()
    {
        if (selectedCiv == null) return;
        
        // For now, just show a notification and reduce relationship status
        UIManager.Instance.ShowNotification($"Denounced {selectedCiv.civData.civName}!");
        // TODO: Implement actual denouncement mechanics
    }
} 