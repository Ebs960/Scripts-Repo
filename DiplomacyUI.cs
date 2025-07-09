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
        // Set up button listeners
        closeButton.onClick.AddListener(Hide);
        declareWarButton.onClick.AddListener(OnDeclareWarClicked);
        proposePeaceButton.onClick.AddListener(OnProposePeaceClicked);
        proposeAllianceButton.onClick.AddListener(OnProposeAllianceClicked);
        denounceButton.onClick.AddListener(OnDenounceClicked);

        // Hide panel initially
        mainPanel.SetActive(false);
    }

    public void Show(Civilization playerCiv)
    {
        this.playerCiv = playerCiv;
        mainPanel.SetActive(true);
        UpdateCivilizationList();
        ClearSelectedCiv();
    }

    public void Hide()
    {
        mainPanel.SetActive(false);
        selectedCiv = null;
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

    private void UpdateCivilizationList()
    {
        // Clear existing list items
        foreach (var item in civListItems)
            Destroy(item);
        civListItems.Clear();

        var allCivs = CivilizationManager.Instance.GetAllCivs();
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