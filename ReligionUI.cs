using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class ReligionUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Panel that contains all religion UI elements")]
    public GameObject religionPanel;
    
    [Header("Pantheon UI")]
    [Tooltip("Container for pantheon founding UI elements")]
    public GameObject pantheonFoundingPanel;
    [Tooltip("Dropdown to select pantheon")]
    public TMP_Dropdown pantheonDropdown;
    [Tooltip("Dropdown to select founder belief")]
    public TMP_Dropdown founderBeliefDropdown;
    [Tooltip("Button to found the selected pantheon")]
    public Button foundPantheonButton;
    [Tooltip("Text showing pantheon faith cost")]
    public TextMeshProUGUI pantheonCostText;
    
    // Pantheon upgrade controls are now part of the Pantheon UI (place these inside the pantheon panel in the Inspector)
    [Tooltip("Panel or container that contains pantheon upgrade controls (place inside the pantheon UI)")]
    public GameObject pantheonUpgradePanel;
    [Tooltip("Dropdown to select which founded pantheon to upgrade")]
    public TMP_Dropdown pantheonUpgradeDropdown;
    [Tooltip("Button to perform the upgrade")]
    public Button upgradePantheonButton;
    [Tooltip("Info text describing the selected upgrade")]
    public TextMeshProUGUI upgradeInfoText;
    
    [Header("Religion UI")]
    [Tooltip("Container for religion founding UI elements")]
    public GameObject religionFoundingPanel;
    [Tooltip("Dropdown to select religion")]
    public TMP_Dropdown religionDropdown;
    [Tooltip("Button to found the selected religion")]
    public Button foundReligionButton;
    [Tooltip("Dropdown to select city with Holy Site")]
    public TMP_Dropdown holySiteCityDropdown;
    [Tooltip("Text showing religion faith cost")]
    public TextMeshProUGUI religionCostText;
    
    [Header("Religion Info Panel")]
    [Tooltip("Panel showing current religion information")]
    public GameObject religionInfoPanel;
    [Tooltip("Image of current pantheon/religion")]
    public Image religionIcon;
    [Tooltip("Name of current pantheon/religion")]
    public TextMeshProUGUI religionNameText;
    [Tooltip("Description of pantheon belief effects")]
    public TextMeshProUGUI beliefDescriptionText;
    [Tooltip("Current faith per turn")]
    public TextMeshProUGUI faithPerTurnText;
    [Tooltip("Current faith amount")]
    public TextMeshProUGUI faithAmountText;
    
    // Current data
    private Civilization playerCiv;
    private List<PantheonData> availablePantheons = new List<PantheonData>();
    private List<BeliefData> availableFounderBeliefs = new List<BeliefData>();
    private List<ReligionData> availableReligions = new List<ReligionData>();
    private List<City> holySiteCities = new List<City>();
    
    // Cached manager reference to avoid repeated FindAnyObjectByType calls
    private ReligionManager _cachedReligionManager;

    [Header("Belief Drag & Drop")]
    [Tooltip("Standalone panel used for belief assignment UI")]
    public GameObject beliefPanel;
    [Tooltip("Button on the religion panel that opens the separate belief panel")]
    public UnityEngine.UI.Button openBeliefPanelButton;
    [Tooltip("Optional button on the belief panel that closes it")]
    public UnityEngine.UI.Button closeBeliefPanelButton;
    [Tooltip("Parent transform that will contain generated belief buttons (assign in Inspector)")]
    public Transform beliefListContainer;
    [Tooltip("Prefab for a belief button. Expected to have a BeliefButtonUI component.")]
    public GameObject beliefButtonPrefab;
    [Tooltip("Optional canvas transform to reparent dragged items to while dragging")]
    public RectTransform dragLayer;

    [Header("Belief Slots")]
    public BeliefSlotUI survivalSlot;
    public BeliefSlotUI harvestSlot;
    public BeliefSlotUI ritualSlot;
    public BeliefSlotUI warfareSlot;
    public BeliefSlotUI knowledgeSlot;

    // Assigned beliefs by category (UI-level selection)
    private readonly System.Collections.Generic.Dictionary<BeliefCategory, BeliefData> _assignedBeliefs = new System.Collections.Generic.Dictionary<BeliefCategory, BeliefData>();
    [Header("Apply Controls")]
    public UnityEngine.UI.Button applyBeliefsButton;
    public UnityEngine.UI.Button cancelBeliefsButton;
    
    void Start()
    {
        // Cache manager reference to avoid repeated FindAnyObjectByType calls
        _cachedReligionManager = FindAnyObjectByType<ReligionManager>();
        // Set up event listeners
        foundPantheonButton.onClick.AddListener(OnFoundPantheonClicked);
        foundReligionButton.onClick.AddListener(OnFoundReligionClicked);
        // Upgrade listeners
        if (upgradePantheonButton != null)
            upgradePantheonButton.onClick.AddListener(OnUpgradePantheonClicked);
        if (openBeliefPanelButton != null)
            openBeliefPanelButton.onClick.AddListener(OpenBeliefPanel);
        if (closeBeliefPanelButton != null)
            closeBeliefPanelButton.onClick.AddListener(CloseBeliefPanel);
        
        // Set up dropdown change listeners
        pantheonDropdown.onValueChanged.AddListener(OnPantheonSelected);
        if (pantheonUpgradeDropdown != null)
            pantheonUpgradeDropdown.onValueChanged.AddListener(OnPantheonUpgradeSelected);
        religionDropdown.onValueChanged.AddListener(OnReligionSelected);
        
        // Hide the panel initially
        religionPanel.SetActive(false);
        if (beliefPanel != null)
            beliefPanel.SetActive(false);
        // Populate belief list if prefabs/containers are set
        if (beliefListContainer != null && beliefButtonPrefab != null)
            PopulateBeliefList();

        if (applyBeliefsButton != null)
            applyBeliefsButton.onClick.AddListener(OnApplyBeliefsClicked);
        if (cancelBeliefsButton != null)
            cancelBeliefsButton.onClick.AddListener(OnCancelBeliefsClicked);

        // Ensure slot owners are set so clear buttons work
        if (survivalSlot != null) survivalSlot.owner = this;
        if (harvestSlot != null) harvestSlot.owner = this;
        if (ritualSlot != null) ritualSlot.owner = this;
        if (warfareSlot != null) warfareSlot.owner = this;
        if (knowledgeSlot != null) knowledgeSlot.owner = this;
    }

    private void Awake()
    {
        if (beliefPanel != null)
            beliefPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (beliefPanel != null)
            beliefPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        UnsubscribeFromPlayerCiv();
    }

    private void OnDisable()
    {
        CloseBeliefPanel();
    }
    
    /// <summary>
    /// Call this method to show the religion UI with the player's civilization
    /// </summary>
    public void Show(Civilization playerCiv)
    {
        if (this.playerCiv != playerCiv)
            UnsubscribeFromPlayerCiv();

        this.playerCiv = playerCiv;
        SubscribeToPlayerCiv();
        SyncAssignedBeliefsFromCiv();
        PopulateBeliefList();
        
        // Update UI state based on player civilization's religion status
        UpdateUIState();
        
        // Show the panel
        religionPanel.SetActive(true);
        CloseBeliefPanel();

        // Refresh belief slot visuals from assigned map
        RefreshBeliefSlots();
    }

    public void OpenBeliefPanel()
    {
        if (beliefPanel == null)
            return;

        SyncAssignedBeliefsFromCiv();
        PopulateBeliefList();
        RefreshBeliefSlots();
        beliefPanel.SetActive(true);
    }

    public void CloseBeliefPanel()
    {
        if (beliefPanel != null)
            beliefPanel.SetActive(false);
    }

    private void RefreshBeliefSlots()
    {
        survivalSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Survival) ? _assignedBeliefs[BeliefCategory.Survival] : null);
        harvestSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Harvest) ? _assignedBeliefs[BeliefCategory.Harvest] : null);
        ritualSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Ritual) ? _assignedBeliefs[BeliefCategory.Ritual] : null);
        warfareSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Warfare) ? _assignedBeliefs[BeliefCategory.Warfare] : null);
        knowledgeSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Knowledge) ? _assignedBeliefs[BeliefCategory.Knowledge] : null);
    }

    /// <summary>
    /// Populate the belief list UI, grouping buttons by category.
    /// </summary>
    public void PopulateBeliefList()
    {
        if (beliefListContainer == null || beliefButtonPrefab == null) return;

        // Clear existing
        for (int i = beliefListContainer.childCount - 1; i >= 0; i--)
        {
            var c = beliefListContainer.GetChild(i);
            Destroy(c.gameObject);
        }

        // Gather all beliefs available from loaded assets.
        var allBeliefs = new System.Collections.Generic.List<BeliefData>();
        var resourceBeliefs = Resources.LoadAll<BeliefData>("");
        if (resourceBeliefs != null)
        {
            foreach (var belief in resourceBeliefs)
                if (belief != null && !allBeliefs.Contains(belief)) allBeliefs.Add(belief);
        }

        // Group by category and create header + buttons
        foreach (BeliefCategory cat in System.Enum.GetValues(typeof(BeliefCategory)))
        {
            // Header
            var headerGO = new GameObject("Header_" + cat.ToString(), typeof(RectTransform));
            headerGO.transform.SetParent(beliefListContainer, false);
            var headerText = headerGO.AddComponent<TextMeshProUGUI>();
            headerText.text = cat.ToString();

            // Buttons for this category
            foreach (var b in allBeliefs)
            {
                if (b == null) continue;
                if (b.category != cat) continue;
                if (playerCiv != null && !playerCiv.CanUseBelief(b)) continue;
                if (playerCiv != null && playerCiv.HasActiveBeliefInCategory(b.category) && playerCiv.GetCustomBeliefInCategory(b.category) != b)
                    continue;
                var btnGO = Instantiate(beliefButtonPrefab, beliefListContainer);
                var btn = btnGO.GetComponent<BeliefButtonUI>();
                if (btn != null) btn.Initialize(b, this, dragLayer);
            }
        }
    }

    /// <summary>
    /// Called by BeliefSlotUI when a belief is dropped onto a slot.
    /// </summary>
    public void AssignBeliefToCategory(BeliefCategory category, BeliefData belief)
    {
        // Enforce single belief per category at UI-level (replace)
        if (belief == null)
        {
            _assignedBeliefs.Remove(category);
        }
        else if (playerCiv != null && !playerCiv.CanUseBelief(belief))
        {
            return;
        }
        else
        {
            _assignedBeliefs[category] = belief;
        }
        RefreshBeliefSlots();
    }

    /// <summary>
    /// Clear the assigned belief for the category (UI-level) and optionally apply immediately.
    /// </summary>
    public void ClearBeliefCategory(BeliefCategory category)
    {
        if (_assignedBeliefs.ContainsKey(category))
            _assignedBeliefs.Remove(category);
        RefreshBeliefSlots();
    }

    private void OnApplyBeliefsClicked()
    {
        if (playerCiv == null) return;

        var failed = new System.Collections.Generic.List<BeliefCategory>();
        foreach (var kv in _assignedBeliefs)
        {
            var cat = kv.Key;
            var belief = kv.Value;
            if (belief == null) continue;

            bool ok = playerCiv.SetCustomBelief(cat, belief);
            if (!ok) failed.Add(cat);
        }

        if (failed.Count > 0)
        {
            Debug.LogWarning($"[ReligionUI] Failed to apply beliefs for categories: {string.Join(",", failed)} (conflict with existing pantheon/religion beliefs)");
        }
        else
        {
            Debug.Log("[ReligionUI] Beliefs applied to civilization.");
        }
    }

    private void OnCancelBeliefsClicked()
    {
        // Revert assigned beliefs to current civ custom assignments
        if (playerCiv == null)
        {
            _assignedBeliefs.Clear();
            RefreshBeliefSlots();
            return;
        }

        _assignedBeliefs.Clear();
        foreach (BeliefCategory cat in System.Enum.GetValues(typeof(BeliefCategory)))
        {
            var b = playerCiv.GetCustomBeliefInCategory(cat);
            if (b != null) _assignedBeliefs[cat] = b;
        }
        RefreshBeliefSlots();
    }
    
    /// <summary>
    /// Hide the religion UI
    /// </summary>
    public void Hide()
    {
        UnsubscribeFromPlayerCiv();
        CloseBeliefPanel();
        religionPanel.SetActive(false);
    }

    private void SubscribeToPlayerCiv()
    {
        if (playerCiv != null)
            playerCiv.OnBeliefsChanged += HandleBeliefsChanged;
    }

    private void UnsubscribeFromPlayerCiv()
    {
        if (playerCiv != null)
            playerCiv.OnBeliefsChanged -= HandleBeliefsChanged;
    }

    private void HandleBeliefsChanged()
    {
        SyncAssignedBeliefsFromCiv();
        PopulateBeliefList();
        UpdateUIState();
        RefreshBeliefSlots();
    }

    private void RefreshFounderBeliefOptions(PantheonData selectedPantheon)
    {
        availableFounderBeliefs.Clear();

        if (selectedPantheon != null)
        {
            var allBeliefs = Resources.LoadAll<BeliefData>("");
            if (allBeliefs != null)
            {
                foreach (var belief in allBeliefs)
                {
                    if (belief == null) continue;
                    if (playerCiv != null && playerCiv.HasActiveBeliefInCategory(belief.category) && playerCiv.GetCustomBeliefInCategory(belief.category) != belief)
                        continue;
                    if (playerCiv != null && !playerCiv.CanUseBeliefForPantheon(selectedPantheon, belief))
                        continue;
                    availableFounderBeliefs.Add(belief);
                }
            }
        }

        founderBeliefDropdown.ClearOptions();
        if (availableFounderBeliefs.Count > 0)
        {
            List<string> beliefNames = new List<string>();
            foreach (BeliefData belief in availableFounderBeliefs)
                beliefNames.Add(belief.beliefName);
            founderBeliefDropdown.AddOptions(beliefNames);
        }
        else
        {
            founderBeliefDropdown.AddOptions(new List<string> { "No Available Beliefs" });
        }
    }

    private void SyncAssignedBeliefsFromCiv()
    {
        _assignedBeliefs.Clear();
        if (playerCiv == null) return;

        foreach (BeliefCategory cat in System.Enum.GetValues(typeof(BeliefCategory)))
        {
            var belief = playerCiv.GetCustomBeliefInCategory(cat);
            if (belief != null)
                _assignedBeliefs[cat] = belief;
        }
    }
    
    /// <summary>
    /// Update UI elements based on player's current religion state
    /// </summary>
    private void UpdateUIState()
    {
        if (playerCiv == null)
            return;

        if (openBeliefPanelButton != null)
            openBeliefPanelButton.interactable = true;
            
        // Update faith amount and per turn
        faithAmountText.text = $"Faith: {playerCiv.faith}";
        
        // Calculate faith per turn from all cities
        int faithPerTurn = 0;
        foreach (City city in playerCiv.cities)
        {
            faithPerTurn += city.GetFaithPerTurn();
        }
        faithPerTurnText.text = $"Faith Per Turn: +{faithPerTurn}";
        
    // Check pantheon state (support multiple pantheons)
        if (playerCiv.foundedPantheons == null || playerCiv.foundedPantheons.Count == 0)
        {
            // Show pantheon founding panel
            pantheonFoundingPanel.SetActive(true);
            religionFoundingPanel.SetActive(false);
            religionInfoPanel.SetActive(false);
            
            // Get available pantheons from ReligionManager, plus any unlocked by adopted cultures
            availablePantheons = ReligionManager.Instance.GetAvailablePantheons();
            if (playerCiv.cultureUnlockedPantheons != null)
            {
                foreach (var cp in playerCiv.cultureUnlockedPantheons)
                    if (cp != null && !availablePantheons.Contains(cp)) availablePantheons.Add(cp);
            }
            
            // Update pantheon dropdown
            pantheonDropdown.ClearOptions();
            List<string> pantheonNames = new List<string>();
            foreach (PantheonData pantheon in availablePantheons)
            {
                pantheonNames.Add(pantheon.pantheonName);
            }
            pantheonDropdown.AddOptions(pantheonNames);
            
            // Update founder belief dropdown based on selected pantheon
            if (availablePantheons.Count > 0 && pantheonDropdown.value >= 0)
            {
                PantheonData selectedPantheon = availablePantheons[pantheonDropdown.value];
                RefreshFounderBeliefOptions(selectedPantheon);
            }
            
            // Update cost and button state
            if (availablePantheons.Count > 0 && pantheonDropdown.value >= 0)
            {
                PantheonData selectedPantheon = availablePantheons[pantheonDropdown.value];
                int cost = playerCiv.GetPantheonCost(selectedPantheon);
                pantheonCostText.text = $"Cost: {cost} Faith";
                foundPantheonButton.interactable = playerCiv.faith >= cost && availableFounderBeliefs.Count > 0;
            }
            else
            {
                pantheonCostText.text = "No pantheons available";
                foundPantheonButton.interactable = false;
            }
        }
        else
        {
            // Show pantheon info
            pantheonFoundingPanel.SetActive(false);
            religionInfoPanel.SetActive(true);
            // Display the first founded pantheon as the primary one in the UI
            var primaryPantheon = playerCiv.foundedPantheons != null && playerCiv.foundedPantheons.Count > 0 ? playerCiv.foundedPantheons[0] : null;
            var primaryBelief = (primaryPantheon != null && playerCiv.chosenFounderBeliefs != null && playerCiv.chosenFounderBeliefs.ContainsKey(primaryPantheon)) ? playerCiv.chosenFounderBeliefs[primaryPantheon] : null;
            religionNameText.text = primaryPantheon != null ? primaryPantheon.pantheonName : "-";
            beliefDescriptionText.text = primaryBelief != null ? primaryBelief.description : "-";
            
            if (primaryPantheon != null && primaryPantheon.icon != null)
                religionIcon.sprite = primaryPantheon.icon;
            
            // If player has a pantheon but no religion, show religion founding panel
            if (!playerCiv.hasFoundedReligion)
            {
                // Check if player has researched the required tech or adopted a culture that unlocks religion
                bool hasUnlockedReligion = false;
                if (playerCiv.researchedTechs != null)
                {
                    foreach (var tech in playerCiv.researchedTechs)
                    {
                        if (tech != null && tech.unlocksReligion)
                        {
                            hasUnlockedReligion = true;
                            break;
                        }
                    }
                }
                if (!hasUnlockedReligion && playerCiv.researchedCultures != null)
                {
                    foreach (var cult in playerCiv.researchedCultures)
                    {
                        if (cult != null && cult.unlocksReligion)
                        {
                            hasUnlockedReligion = true;
                            break;
                        }
                    }
                }
                
                // Only show if they have the tech
                religionFoundingPanel.SetActive(hasUnlockedReligion);
                
                // Get cities with Holy Sites
                holySiteCities.Clear();
                foreach (City city in playerCiv.cities)
                {
                    if (city.HasHolySite())
                        holySiteCities.Add(city);
                }
                
                // Update holy site city dropdown
                holySiteCityDropdown.ClearOptions();
                
                if (holySiteCities.Count > 0)
                {
                    List<string> cityNames = new List<string>();
                    foreach (City city in holySiteCities)
                    {
                        cityNames.Add(city.cityName);
                    }
                    holySiteCityDropdown.AddOptions(cityNames);
                    
                    // Enable religion founding if there are holy sites
                    foundReligionButton.interactable = true;
                }
                else
                {
                    holySiteCityDropdown.AddOptions(new List<string> { "No Holy Sites" });
                    foundReligionButton.interactable = false;
                }
                
                // Get available religions from ReligionManager
                availableReligions.Clear();
                
                // Use cached reference to avoid expensive FindAnyObjectByType call
                if (_cachedReligionManager == null)
                    _cachedReligionManager = FindAnyObjectByType<ReligionManager>();
                if (_cachedReligionManager != null)
                {
                    availableReligions = _cachedReligionManager.GetAvailableReligions();
                    availableReligions.RemoveAll(r => r == null
                        || (playerCiv.foundedPantheons == null || !playerCiv.foundedPantheons.Contains(r.requiredPantheon))
                        || (r.founderBelief != null && playerCiv.HasActiveBeliefInCategory(r.founderBelief.category)));
                }
                
                // Update religion dropdown
                religionDropdown.ClearOptions();
                
                if (availableReligions.Count > 0)
                {
                    List<string> religionNames = new List<string>();
                    foreach (ReligionData religion in availableReligions)
                    {
                        religionNames.Add(religion.religionName);
                    }
                    
                    if (religionNames.Count > 0)
                    {
                        religionDropdown.AddOptions(religionNames);
                        OnReligionSelected(0); // Update cost display
                    }
                    else
                    {
                        religionDropdown.AddOptions(new List<string> { "No Available Religions" });
                        foundReligionButton.interactable = false;
                    }
                }
                else
                {
                    religionDropdown.AddOptions(new List<string> { "No Available Religions" });
                    foundReligionButton.interactable = false;
                }
            }
            else
            {
                // Player already has a religion
                religionFoundingPanel.SetActive(false);
                
                // Update religion info
                religionNameText.text = playerCiv.foundedReligion.religionName;
                beliefDescriptionText.text = $"Founder Belief: {playerCiv.foundedReligion.founderBelief.description}";
                
                if (playerCiv.foundedReligion.icon != null)
                    religionIcon.sprite = playerCiv.foundedReligion.icon;
                
                // Get cities with Holy Sites
                holySiteCities.Clear();
                foreach (City city in playerCiv.cities)
                {
                    if (city.HasHolySite())
                        holySiteCities.Add(city);
                }
                
                // Update holy site city dropdown
                holySiteCityDropdown.ClearOptions();
                
                if (holySiteCities.Count > 0)
                {
                    List<string> cityNames = new List<string>();
                    foreach (City city in holySiteCities)
                    {
                        cityNames.Add(city.cityName);
                    }
                    holySiteCityDropdown.AddOptions(cityNames);
                    
                    // Enable religion founding if there are holy sites
                    foundReligionButton.interactable = true;
                }
                else
                {
                    holySiteCityDropdown.AddOptions(new List<string> { "No Holy Sites" });
                    foundReligionButton.interactable = false;
                }
            }
        }
            // Populate pantheon upgrade UI: list founded pantheons that are spirits and can upgrade
            if (pantheonUpgradePanel != null && pantheonUpgradeDropdown != null && upgradePantheonButton != null && upgradeInfoText != null)
            {
                var upgradable = new List<PantheonData>();
                if (playerCiv.foundedPantheons != null)
                {
                    foreach (var p in playerCiv.foundedPantheons)
                    {
                        if (p == null) continue;
                        if (p.IsSpirit && p.canUpgradeToGod && p.upgradedPantheon != null)
                            upgradable.Add(p);
                    }
                }

                if (upgradable.Count > 0)
                {
                    pantheonUpgradePanel.SetActive(true);
                    pantheonUpgradeDropdown.ClearOptions();
                    List<string> names = new List<string>();
                    foreach (var p in upgradable) names.Add(p.pantheonName);
                    pantheonUpgradeDropdown.AddOptions(names);
                    pantheonUpgradeDropdown.value = 0;
                    pantheonUpgradeDropdown.RefreshShownValue();
                    upgradePantheonButton.interactable = true;
                    // Show info for first
                    var sel = upgradable[0];
                    upgradeInfoText.text = sel.upgradedPantheon != null ? $"Upgrades to: {sel.upgradedPantheon.pantheonName}" : "No upgrade configured.";
                }
                else
                {
                    pantheonUpgradePanel.SetActive(false);
                }
            }
    }

    /// <summary>
    /// Called when the user selects a pantheon from the upgrade dropdown
    /// </summary>
    private void OnPantheonUpgradeSelected(int index)
    {
        if (playerCiv == null || pantheonUpgradeDropdown == null || pantheonUpgradePanel == null) return;
        // Rebuild the same eligible list to find selected asset
        var upgradable = new List<PantheonData>();
        if (playerCiv.foundedPantheons != null)
        {
            foreach (var p in playerCiv.foundedPantheons)
            {
                if (p == null) continue;
                if (p.IsSpirit && p.canUpgradeToGod && p.upgradedPantheon != null)
                    upgradable.Add(p);
            }
        }
        if (index < 0 || index >= upgradable.Count)
        {
            upgradeInfoText.text = "";
            upgradePantheonButton.interactable = false;
            return;
        }
        var selected = upgradable[index];
        upgradeInfoText.text = selected.upgradedPantheon != null ? $"Upgrades to: {selected.upgradedPantheon.pantheonName}" : "No upgrade configured.";
        upgradePantheonButton.interactable = true;
    }

    /// <summary>
    /// Called when the user clicks the Upgrade button
    /// </summary>
    private void OnUpgradePantheonClicked()
    {
        if (playerCiv == null || pantheonUpgradeDropdown == null) return;
        // Find selected pantheon in the eligible list
        var upgradable = new List<PantheonData>();
        if (playerCiv.foundedPantheons != null)
        {
            foreach (var p in playerCiv.foundedPantheons)
            {
                if (p == null) continue;
                if (p.IsSpirit && p.canUpgradeToGod && p.upgradedPantheon != null)
                    upgradable.Add(p);
            }
        }
        int idx = pantheonUpgradeDropdown.value;
        if (idx < 0 || idx >= upgradable.Count) return;
        var toUpgrade = upgradable[idx];
        bool ok = playerCiv.UpgradePantheon(toUpgrade);
        if (ok)
        {
            // Refresh UI state
            UpdateUIState();
        }
    }
    
    /// <summary>
    /// Called when a pantheon is selected from the dropdown
    /// </summary>
    private void OnPantheonSelected(int index)
    {
        if (index < 0 || index >= availablePantheons.Count)
            return;
            
        PantheonData selectedPantheon = availablePantheons[index];

        RefreshFounderBeliefOptions(selectedPantheon);

        // Update cost text
        pantheonCostText.text = $"Cost: {selectedPantheon.faithCost} Faith";

        // Enable found button if player has enough faith and at least one founder belief is valid
        foundPantheonButton.interactable = playerCiv.faith >= selectedPantheon.faithCost && availableFounderBeliefs.Count > 0;
    }
    
    /// <summary>
    /// Called when a religion is selected from the dropdown
    /// </summary>
    private void OnReligionSelected(int index)
    {
        if (index < 0 || index >= availableReligions.Count)
            return;
            
        ReligionData selectedReligion = availableReligions[index];
        
        // Update cost text
        religionCostText.text = $"Cost: {selectedReligion.faithCost} Faith";
        
        // Enable found button if player has enough faith
        foundReligionButton.interactable = playerCiv.faith >= selectedReligion.faithCost &&
                                          holySiteCities.Count > 0;
    }
    
    /// <summary>
    /// Called when the found pantheon button is clicked
    /// </summary>
    private void OnFoundPantheonClicked()
    {
        if (pantheonDropdown.value < 0 || pantheonDropdown.value >= availablePantheons.Count ||
            founderBeliefDropdown.value < 0 || founderBeliefDropdown.value >= availableFounderBeliefs.Count)
            return;
            
        PantheonData selectedPantheon = availablePantheons[pantheonDropdown.value];
        BeliefData selectedBelief = availableFounderBeliefs[founderBeliefDropdown.value];
        
        // Attempt to found the pantheon
        if (playerCiv.FoundPantheon(selectedPantheon, selectedBelief))
        {
            // Update UI if successful
            UpdateUIState();
        }
    }
    
    /// <summary>
    /// Called when the found religion button is clicked
    /// </summary>
    private void OnFoundReligionClicked()
    {
        if (religionDropdown.value < 0 || religionDropdown.value >= availableReligions.Count ||
            holySiteCityDropdown.value < 0 || holySiteCityDropdown.value >= holySiteCities.Count)
            return;
            
        ReligionData selectedReligion = availableReligions[religionDropdown.value];
        City selectedCity = holySiteCities[holySiteCityDropdown.value];
        
        // Attempt to found the religion
        if (playerCiv.FoundReligion(selectedReligion, selectedCity))
        {
            // Register the new religion with the ReligionManager
            // Use cached reference to avoid expensive FindAnyObjectByType call
            if (_cachedReligionManager == null)
                _cachedReligionManager = FindAnyObjectByType<ReligionManager>();
            if (_cachedReligionManager != null)
            {
                _cachedReligionManager.RegisterFoundedReligion(selectedReligion, playerCiv);
            }
            
            // Update UI if successful
            UpdateUIState();
        }
    }
} 