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
        
        // Set up button listeners
        closeButton.onClick.AddListener(Hide);
        declareWarButton.onClick.AddListener(OnDeclareWarClicked);
        proposePeaceButton.onClick.AddListener(OnProposePeaceClicked);
        proposeAllianceButton.onClick.AddListener(OnProposeAllianceClicked);
        denounceButton.onClick.AddListener(OnDenounceClicked);

        // Hide panel initially
        mainPanel.SetActive(false);
    }
    
    private void ValidateUIReferences()
    {
        Debug.Log("[DiplomacyUI] Validating UI references:");
        
        // Main Panel
        Debug.Log($"  mainPanel: {(mainPanel != null ? "OK" : "NULL")}");
        Debug.Log($"  closeButton: {(closeButton != null ? "OK" : "NULL")}");
        
        // Left Section
        Debug.Log($"  civListContainer: {(civListContainer != null ? "OK" : "NULL")}");
        Debug.Log($"  civListItemPrefab: {(civListItemPrefab != null ? "OK" : "NULL")}");
        Debug.Log($"  civListScroll: {(civListScroll != null ? "OK" : "NULL")}");
        
        // Middle Section
        Debug.Log($"  selectedCivIcon: {(selectedCivIcon != null ? "OK" : "NULL")}");
        Debug.Log($"  selectedCivName: {(selectedCivName != null ? "OK" : "NULL")}");
        Debug.Log($"  selectedCivLeader: {(selectedCivLeader != null ? "OK" : "NULL")}");
        Debug.Log($"  relationshipStatus: {(relationshipStatus != null ? "OK" : "NULL")}");
        
        // Right Section
        Debug.Log($"  declareWarButton: {(declareWarButton != null ? "OK" : "NULL")}");
        Debug.Log($"  proposePeaceButton: {(proposePeaceButton != null ? "OK" : "NULL")}");
        Debug.Log($"  proposeAllianceButton: {(proposeAllianceButton != null ? "OK" : "NULL")}");
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
            Debug.Log("[DiplomacyUI] All critical UI references are assigned correctly.");
        }
    }

    public void Show(Civilization playerCiv)
    {
        Debug.Log("[DiplomacyUI] Show() called");
        this.playerCiv = playerCiv;
        
        // Ensure the main panel is active first
        if (mainPanel != null)
        {
            mainPanel.SetActive(true);
            Debug.Log("[DiplomacyUI] Main panel activated");
        }
        else
        {
            Debug.LogError("[DiplomacyUI] Main panel is null! Cannot show diplomacy UI.");
            return;
        }
        
        // Ensure all three sections are properly activated
        ActivateLeftSection();
        ActivateMiddleSection();
        ActivateRightSection();
        
        UpdateCivilizationList();
        ClearSelectedCiv();
    }
    
    private void ActivateLeftSection()
    {
        // Left Section - Civilization List
        if (civListContainer != null)
        {
            civListContainer.gameObject.SetActive(true);
            Debug.Log("[DiplomacyUI] Left section (civ list) activated");
            
            // Also activate parent if it's a separate panel
            Transform parent = civListContainer.parent;
            if (parent != null && parent != mainPanel.transform)
            {
                parent.gameObject.SetActive(true);
                Debug.Log($"[DiplomacyUI] Left section parent '{parent.name}' activated");
            }
        }
        else
        {
            Debug.LogError("[DiplomacyUI] civListContainer is null! Left section cannot be shown.");
        }
        
        if (civListScroll != null)
        {
            civListScroll.gameObject.SetActive(true);
            Debug.Log("[DiplomacyUI] Civ list scroll rect activated");
        }
        else
        {
            Debug.LogWarning("[DiplomacyUI] civListScroll is null! Scroll functionality may not work.");
        }
    }
    
    private void ActivateMiddleSection()
    {
        // Middle Section - Selected Civ Info
        if (selectedCivIcon != null)
        {
            Transform middleParent = selectedCivIcon.transform.parent;
            if (middleParent != null && middleParent != mainPanel.transform)
            {
                middleParent.gameObject.SetActive(true);
                Debug.Log($"[DiplomacyUI] Middle section parent '{middleParent.name}' activated");
            }
            Debug.Log("[DiplomacyUI] Middle section (selected civ info) activated");
        }
        else
        {
            Debug.LogError("[DiplomacyUI] selectedCivIcon is null! Middle section cannot be shown.");
        }
    }
    
    private void ActivateRightSection()
    {
        // Right Section - Diplomatic Actions
        if (declareWarButton != null)
        {
            Transform actionsParent = declareWarButton.transform.parent;
            if (actionsParent != null && actionsParent != mainPanel.transform)
            {
                actionsParent.gameObject.SetActive(true);
                Debug.Log($"[DiplomacyUI] Right section parent '{actionsParent.name}' activated");
            }
            Debug.Log("[DiplomacyUI] Right section (diplomatic actions) activated");
        }
        else
        {
            Debug.LogError("[DiplomacyUI] declareWarButton is null! Right section cannot be shown.");
        }
    }

    public void Hide()
    {
        Debug.Log("[DiplomacyUI] Hide() called - closing all diplomacy panels");
        
        // Deactivate main panel
        if (mainPanel != null)
        {
            mainPanel.SetActive(false);
            Debug.Log("[DiplomacyUI] Main panel deactivated");
        }
        
        // Ensure all three sections are properly deactivated
        DeactivateLeftSection();
        DeactivateMiddleSection();
        DeactivateRightSection();
        
        selectedCiv = null;
        Debug.Log("[DiplomacyUI] All diplomacy panels closed");
    }
    
    private void DeactivateLeftSection()
    {
        // Left Section - Civilization List
        if (civListContainer != null)
        {
            // Deactivate parent if it's a separate panel
            Transform parent = civListContainer.parent;
            if (parent != null && parent != mainPanel.transform)
            {
                parent.gameObject.SetActive(false);
                Debug.Log($"[DiplomacyUI] Left section parent '{parent.name}' deactivated");
            }
            else
            {
                civListContainer.gameObject.SetActive(false);
                Debug.Log("[DiplomacyUI] Left section (civ list) deactivated");
            }
        }
        
        if (civListScroll != null)
        {
            civListScroll.gameObject.SetActive(false);
        }
    }
    
    private void DeactivateMiddleSection()
    {
        // Middle Section - Selected Civ Info
        if (selectedCivIcon != null)
        {
            Transform middleParent = selectedCivIcon.transform.parent;
            if (middleParent != null && middleParent != mainPanel.transform)
            {
                middleParent.gameObject.SetActive(false);
                Debug.Log($"[DiplomacyUI] Middle section parent '{middleParent.name}' deactivated");
            }
        }
    }
    
    private void DeactivateRightSection()
    {
        // Right Section - Diplomatic Actions
        if (declareWarButton != null)
        {
            Transform actionsParent = declareWarButton.transform.parent;
            if (actionsParent != null && actionsParent != mainPanel.transform)
            {
                actionsParent.gameObject.SetActive(false);
                Debug.Log($"[DiplomacyUI] Right section parent '{actionsParent.name}' deactivated");
            }
        }
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
        Debug.Log("[DiplomacyUI] UpdateCivilizationList called");
        
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

        Debug.Log($"[DiplomacyUI] Found {allCivs.Count} civilizations");

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

            Debug.Log($"[DiplomacyUI] Creating list item for {civ.civData.civName}");
            
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
        
        Debug.Log($"[DiplomacyUI] Created {itemsCreated} civilization list items");
        
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