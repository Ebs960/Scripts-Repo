using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Text;

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

    [Header("Presentation Hooks (Optional)")]
    public Button closeButton;
    public TextMeshProUGUI actionFeedbackText;
    public TextMeshProUGUI beliefCostText;
    public TextMeshProUGUI pantheonCountText;
    public TextMeshProUGUI foundedPantheonsText;
    public TextMeshProUGUI foundedReligionText;
    public TextMeshProUGUI stateReligionText;
    public TextMeshProUGUI cityReligionOverviewText;
    public Image selectedReligionIcon;
    public TextMeshProUGUI religionRequirementsText;
    public TextMeshProUGUI upgradeDetailsText;

    private Civilization _subscribedCiv;
    private TurnManager _subscribedTurnManager;
    private bool _editingBeliefs;
    private bool _refreshPending;
    private bool _generatedCloseButton;
    private string _selectedReligionDetails;
    private string _selectedUpgradeDetails;
    private readonly List<PantheonData> _upgradeOptions = new List<PantheonData>();
    private readonly List<ReligionFoundingInfo> _religionOptions = new List<ReligionFoundingInfo>();
    private IReadOnlyList<CityReligionInfo> _cityReligions = new List<CityReligionInfo>();
    public IReadOnlyList<ReligionFoundingInfo> ReligionOptions => _religionOptions.AsReadOnly();
    public IReadOnlyList<CityReligionInfo> CityReligions => _cityReligions;
    
    void Awake()
    {
        // Cache manager reference to avoid repeated FindAnyObjectByType calls
        _cachedReligionManager = FindAnyObjectByType<ReligionManager>();
        if (closeButton == null && closeBeliefPanelButton != null && religionPanel != null)
        {
            closeButton = Instantiate(closeBeliefPanelButton, religionPanel.transform, false);
            closeButton.name = "Close Religion";
            closeButton.gameObject.SetActive(true);
            _generatedCloseButton = true;
        }
        // Set up event listeners
        if (foundPantheonButton != null) foundPantheonButton.onClick.AddListener(OnFoundPantheonClicked);
        if (foundReligionButton != null) foundReligionButton.onClick.AddListener(OnFoundReligionClicked);
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        // Upgrade listeners
        if (upgradePantheonButton != null)
            upgradePantheonButton.onClick.AddListener(OnUpgradePantheonClicked);
        if (openBeliefPanelButton != null)
            openBeliefPanelButton.onClick.AddListener(OpenBeliefPanel);
        if (closeBeliefPanelButton != null)
            closeBeliefPanelButton.onClick.AddListener(CloseBeliefPanel);
        
        // Set up dropdown change listeners
        if (pantheonDropdown != null) pantheonDropdown.onValueChanged.AddListener(OnPantheonSelected);
        if (pantheonUpgradeDropdown != null)
            pantheonUpgradeDropdown.onValueChanged.AddListener(OnPantheonUpgradeSelected);
        if (religionDropdown != null) religionDropdown.onValueChanged.AddListener(OnReligionSelected);
        if (holySiteCityDropdown != null) holySiteCityDropdown.onValueChanged.AddListener(OnHolySiteSelected);
        
        // Hide the panel initially
        if (beliefPanel != null)
            beliefPanel.SetActive(false);
        // Populate belief list if prefabs/containers are set
        if (beliefListContainer != null && beliefButtonPrefab != null)
            PopulateBeliefList();

        if (applyBeliefsButton != null)
            applyBeliefsButton.onClick.AddListener(OnApplyBeliefsClicked);
        if (cancelBeliefsButton != null)
            cancelBeliefsButton.onClick.AddListener(OnCancelBeliefsClicked);

        ConfigureBeliefSlots();
        ConfigureStatusText(pantheonCostText);
        ConfigureStatusText(religionCostText);
        ConfigureStatusText(upgradeInfoText);
        ConfigureStatusText(religionNameText);
        if (pantheonDropdown != null) ConfigureStatusText(pantheonDropdown.captionText);
        if (religionDropdown != null) ConfigureStatusText(religionDropdown.captionText);
        EnsureDropdownIcons(pantheonDropdown);
        EnsureDropdownIcons(pantheonUpgradeDropdown);
        EnsureDropdownIcons(religionDropdown);
        EnsureScrollableDetails();
    }

    private static void EnsureDropdownIcons(TMP_Dropdown dropdown)
    {
        if (dropdown == null) return;
        if (dropdown.captionImage == null && dropdown.captionText != null)
            dropdown.captionImage = CreateDropdownIcon(dropdown.captionText);
        if (dropdown.itemImage == null && dropdown.itemText != null)
            dropdown.itemImage = CreateDropdownIcon(dropdown.itemText);
        dropdown.RefreshShownValue();
    }

    private static Image CreateDropdownIcon(TMP_Text label)
    {
        var iconObject = new GameObject("Option Icon", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)iconObject.transform;
        rect.SetParent(label.transform, false);
        rect.anchorMin = new Vector2(0f, .5f);
        rect.anchorMax = rect.anchorMin;
        rect.sizeDelta = new Vector2(20f, 20f);
        rect.anchoredPosition = new Vector2(10f, 0f);
        var margin = label.margin;
        margin.x += 26f;
        label.margin = margin;
        var image = iconObject.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private void EnsureScrollableDetails()
    {
        if (beliefDescriptionText == null || beliefDescriptionText.GetComponentInParent<ScrollRect>() != null) return;
        var content = beliefDescriptionText.rectTransform;
        var scrollObject = new GameObject("Religion Details Scroll", typeof(RectTransform), typeof(ScrollRect));
        var frame = (RectTransform)scrollObject.transform;
        frame.SetParent(content.parent, false);
        frame.SetSiblingIndex(content.GetSiblingIndex());
        frame.anchorMin = content.anchorMin;
        frame.anchorMax = content.anchorMax;
        frame.pivot = content.pivot;
        frame.sizeDelta = content.sizeDelta;
        frame.anchoredPosition = content.anchoredPosition;
        frame.localRotation = content.localRotation;
        frame.localScale = content.localScale;

        var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        var viewport = (RectTransform)viewportObject.transform;
        viewport.SetParent(frame, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewportObject.GetComponent<Image>().color = Color.clear;
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        beliefDescriptionText.textWrappingMode = TextWrappingModes.Normal;
        beliefDescriptionText.overflowMode = TextOverflowModes.Overflow;
        var fitter = beliefDescriptionText.GetComponent<ContentSizeFitter>() ?? beliefDescriptionText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
    }

    private static void ConfigureStatusText(TMP_Text text)
    {
        if (text == null) return;
        text.fontSizeMax = Mathf.Max(12f, text.fontSize);
        text.fontSizeMin = 12f;
        text.enableAutoSizing = true;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void ConfigureBeliefSlots()
    {
        if (survivalSlot != null) { survivalSlot.owner = this; survivalSlot.category = BeliefCategory.Survival; }
        if (harvestSlot != null) { harvestSlot.owner = this; harvestSlot.category = BeliefCategory.Harvest; }
        if (ritualSlot != null) { ritualSlot.owner = this; ritualSlot.category = BeliefCategory.Ritual; }
        if (warfareSlot != null) { warfareSlot.owner = this; warfareSlot.category = BeliefCategory.Warfare; }
        if (knowledgeSlot != null) { knowledgeSlot.owner = this; knowledgeSlot.category = BeliefCategory.Knowledge; }
    }

    private void OnEnable()
    {
        SubscribeToPlayerCiv();
        if (!_editingBeliefs)
        {
            SyncAssignedBeliefsFromCiv();
            RefreshBeliefSlots();
        }
        _refreshPending = true;
    }

    private void OnDestroy()
    {
        UnsubscribeFromPlayerCiv();
    }

    private void OnDisable()
    {
        UnsubscribeFromPlayerCiv();
        CloseBeliefPanel();
    }

    private void LateUpdate()
    {
        if (!_refreshPending || playerCiv == null || religionPanel == null || !religionPanel.activeInHierarchy) return;
        _refreshPending = false;
        UpdateUIState();
    }
    
    /// <summary>
    /// Call this method to show the religion UI with the player's civilization
    /// </summary>
    public void Show(Civilization playerCiv)
    {
        if (playerCiv == null)
        {
            Hide();
            this.playerCiv = null;
            _editingBeliefs = false;
            SyncAssignedBeliefsFromCiv();
            return;
        }
        if (this.playerCiv != playerCiv)
        {
            UnsubscribeFromPlayerCiv();
            _editingBeliefs = false;
            if (actionFeedbackText != null) actionFeedbackText.text = string.Empty;
        }

        this.playerCiv = playerCiv;
        if (religionPanel != null) religionPanel.SetActive(true);
        SubscribeToPlayerCiv();
        if (!_editingBeliefs) SyncAssignedBeliefsFromCiv();
        PopulateBeliefList();
        
        // Update UI state based on player civilization's religion status
        UpdateUIState();
        
        // Show the panel
        CloseBeliefPanel();

        // Refresh belief slot visuals from assigned map
        RefreshBeliefSlots();
    }

    public void OpenBeliefPanel()
    {
        if (beliefPanel == null)
            return;

        if (!_editingBeliefs) SyncAssignedBeliefsFromCiv();
        _editingBeliefs = true;
        PopulateBeliefList();
        RefreshBeliefSlots();
        RefreshBeliefCost();
        beliefPanel.SetActive(true);
        if (_generatedCloseButton && closeButton != null) closeButton.gameObject.SetActive(false);
    }

    public void CloseBeliefPanel()
    {
        if (beliefPanel != null)
            beliefPanel.SetActive(false);
        if (_generatedCloseButton && closeButton != null) closeButton.gameObject.SetActive(true);
    }

    private void RefreshBeliefSlots()
    {
        survivalSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Survival) ? _assignedBeliefs[BeliefCategory.Survival] : null);
        harvestSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Harvest) ? _assignedBeliefs[BeliefCategory.Harvest] : null);
        ritualSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Ritual) ? _assignedBeliefs[BeliefCategory.Ritual] : null);
        warfareSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Warfare) ? _assignedBeliefs[BeliefCategory.Warfare] : null);
        knowledgeSlot?.SetAssigned(_assignedBeliefs.ContainsKey(BeliefCategory.Knowledge) ? _assignedBeliefs[BeliefCategory.Knowledge] : null);
    }

    public static string BuildBeliefEffectSummary(BeliefData belief, bool compact = false)
    {
        return ReligionEffectDescriptions.BuildBeliefEffectSummary(belief, compact);
    }

    public static string BuildPantheonEffectSummary(PantheonData pantheon, bool compact = false)
    {
        return ReligionEffectDescriptions.BuildPantheonEffectSummary(pantheon, compact);
    }

    private void RefreshReligionSummaryText()
    {
        if (beliefDescriptionText == null)
            return;

        if (playerCiv == null)
        {
            beliefDescriptionText.text = "-";
            return;
        }

        var summary = new StringBuilder();

        summary.AppendLine($"Founded Religion: {(playerCiv.foundedReligion != null ? playerCiv.foundedReligion.religionName : "None")}");
        summary.AppendLine($"State Religion: {(playerCiv.StateReligion != null ? playerCiv.StateReligion.religionName : "None")}");

        if (religionRequirementsText == null && !string.IsNullOrEmpty(_selectedReligionDetails))
            summary.AppendLine("\nSelected religion:\n" + _selectedReligionDetails);
        if (upgradeDetailsText == null && !string.IsNullOrEmpty(_selectedUpgradeDetails))
            summary.AppendLine("\nSelected upgrade:\n" + _selectedUpgradeDetails);

        if (playerCiv.hasFoundedReligion && playerCiv.foundedReligion != null && !string.IsNullOrWhiteSpace(playerCiv.foundedReligion.description))
            summary.AppendLine(playerCiv.foundedReligion.description.Trim());

        if (playerCiv.foundedPantheons != null && playerCiv.foundedPantheons.Count > 0)
        {
            if (summary.Length > 0)
                summary.AppendLine();

            summary.AppendLine("Pantheons:");
            foreach (var pantheon in playerCiv.foundedPantheons)
            {
                if (pantheon == null)
                    continue;

                summary.Append("- ");
                summary.Append(pantheon.pantheonName);
                string pantheonSummary = BuildPantheonEffectSummary(pantheon, false);
                if (!string.IsNullOrWhiteSpace(pantheonSummary))
                {
                    summary.Append(": ");
                    summary.AppendLine(pantheonSummary.Replace("\n", "\n  "));
                }
                else
                {
                    summary.AppendLine();
                }
            }
        }

        var activeBeliefs = new List<BeliefData>();
        foreach (var belief in playerCiv.EnumerateActiveBeliefs())
        {
            if (belief != null)
                activeBeliefs.Add(belief);
        }

        if (activeBeliefs.Count > 0)
        {
            if (summary.Length > 0)
                summary.AppendLine();

            summary.AppendLine("Beliefs:");
            foreach (var belief in activeBeliefs)
            {
                summary.Append("- ");
                summary.Append(belief.beliefName);
                string beliefSummary = BuildBeliefEffectSummary(belief, false);
                if (!string.IsNullOrWhiteSpace(beliefSummary))
                {
                    summary.Append(": ");
                    summary.AppendLine(beliefSummary.Replace("\n", "\n  "));
                }
                else
                {
                    summary.AppendLine();
                }
            }
        }

        beliefDescriptionText.text = summary.Length > 0 ? summary.ToString().TrimEnd() : "No pantheon or belief bonuses assigned yet.";
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
        var resourceBeliefs = ResourceCache.GetAllBeliefData();
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
        if (belief != null && belief.category != category)
        {
            ShowActionFeedback($"{belief.beliefName} belongs in {belief.category}, not {category}.");
            return;
        }
        _editingBeliefs = true;
        // Enforce single belief per category at UI-level (replace)
        if (belief == null)
        {
            _assignedBeliefs.Remove(category);
        }
        else if (playerCiv != null && !playerCiv.CanUseBelief(belief))
        {
            ShowActionFeedback($"{belief.beliefName} requires a compatible pantheon.");
            return;
        }
        else
        {
            _assignedBeliefs[category] = belief;
        }
        RefreshBeliefSlots();
        RefreshBeliefCost();
    }

    /// <summary>
    /// Clear the assigned belief for the category (UI-level) and optionally apply immediately.
    /// </summary>
    public void ClearBeliefCategory(BeliefCategory category)
    {
        _editingBeliefs = true;
        if (_assignedBeliefs.ContainsKey(category))
            _assignedBeliefs.Remove(category);
        RefreshBeliefSlots();
        RefreshBeliefCost();
    }

    private void OnApplyBeliefsClicked()
    {
        if (playerCiv == null) return;
        var selection = new Dictionary<BeliefCategory, BeliefData>(_assignedBeliefs);
        if (!playerCiv.TryApplyCustomBeliefs(selection, out string reason))
        {
            ShowActionFeedback(reason);
            return;
        }
        SyncAssignedBeliefsFromCiv();
        _editingBeliefs = false;
        RefreshBeliefSlots();
        UpdateUIState();
        ShowActionFeedback("Beliefs applied.");
    }

    private void OnCancelBeliefsClicked()
    {
        SyncAssignedBeliefsFromCiv();
        _editingBeliefs = false;
        RefreshBeliefSlots();
        RefreshBeliefCost();
        ShowActionFeedback("Pending beliefs canceled.");
    }

    public void ShowActionFeedback(string message)
    {
        if (actionFeedbackText != null) actionFeedbackText.text = message;
        else UIManager.Instance?.ShowNotification(message);
    }

    private void RefreshBeliefCost()
    {
        long cost = 0;
        if (playerCiv != null)
            foreach (var selection in _assignedBeliefs)
                if (selection.Value != null && playerCiv.GetCustomBeliefInCategory(selection.Key) != selection.Value)
                    cost += playerCiv.GetBeliefFaithCost(selection.Value);
        if (beliefCostText != null) beliefCostText.text = $"Pending beliefs: {cost} Faith";
        else if (applyBeliefsButton != null)
        {
            var label = applyBeliefsButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = $"Apply ({cost} Faith)";
        }
        if (applyBeliefsButton != null) applyBeliefsButton.interactable = playerCiv != null && cost <= playerCiv.faith;
    }
    
    /// <summary>
    /// Hide the religion UI
    /// </summary>
    public void Hide()
    {
        UnsubscribeFromPlayerCiv();
        CloseBeliefPanel();
        if (religionPanel != null) religionPanel.SetActive(false);
    }

    private void SubscribeToPlayerCiv()
    {
        if (playerCiv == null || (_subscribedCiv == playerCiv && _subscribedTurnManager == TurnManager.Instance)) return;
        UnsubscribeFromPlayerCiv();
        _subscribedCiv = playerCiv;
        _subscribedCiv.OnBeliefsChanged += HandleBeliefsChanged;
        _subscribedCiv.OnFaithChanged += HandleFaithChanged;
        ReligionPoliticsService.OnStateReligionChanged += HandleStateReligionChanged;
        _subscribedTurnManager = TurnManager.Instance;
        if (_subscribedTurnManager != null) _subscribedTurnManager.OnTurnChanged += HandleTurnChanged;
    }

    private void UnsubscribeFromPlayerCiv()
    {
        if (_subscribedCiv != null)
        {
            _subscribedCiv.OnBeliefsChanged -= HandleBeliefsChanged;
            _subscribedCiv.OnFaithChanged -= HandleFaithChanged;
        }
        if (_subscribedTurnManager != null) _subscribedTurnManager.OnTurnChanged -= HandleTurnChanged;
        ReligionPoliticsService.OnStateReligionChanged -= HandleStateReligionChanged;
        _subscribedCiv = null;
        _subscribedTurnManager = null;
    }

    private void HandleBeliefsChanged()
    {
        if (!_editingBeliefs)
        {
            SyncAssignedBeliefsFromCiv();
            PopulateBeliefList();
        }
        _refreshPending = true;
        RefreshBeliefSlots();
    }

    private void HandleFaithChanged(int total, int delta) => _refreshPending = true;
    private void HandleTurnChanged(Civilization civ, int turn) => _refreshPending = true;
    private void HandleStateReligionChanged(Civilization civ, ReligionData previous, ReligionData current, StateReligionChangeReason reason)
    {
        if (civ == playerCiv) _refreshPending = true;
    }

    private void OnHolySiteSelected(int index)
    {
        if (playerCiv != null) RefreshReligionOptions();
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
        _refreshPending = false;
        if (_cachedReligionManager == null) _cachedReligionManager = ReligionManager.Instance;
        if (openBeliefPanelButton != null) openBeliefPanelButton.interactable = true;
        if (faithAmountText != null) faithAmountText.text = $"Faith: {playerCiv.faith}";
        int faithPerTurn = 0;
        if (playerCiv.cities != null)
            foreach (var city in playerCiv.cities)
                if (city != null) faithPerTurn += city.GetFaithPerTurn();
        if (faithPerTurnText != null) faithPerTurnText.text = $"Faith Per Turn: {faithPerTurn:+0;-0;0}";

        RefreshPantheonOptions();
        RefreshReligionOptions();
        RefreshReligionIdentity();
        RefreshBeliefCost();
        _cityReligions = ReligionPresentation.BuildCityOverview(playerCiv, _cachedReligionManager).AsReadOnly();
        if (cityReligionOverviewText != null) cityReligionOverviewText.text = ReligionPresentation.CityOverviewText(_cityReligions);
    }

    private static T Selected<T>(TMP_Dropdown dropdown, IList<T> options) where T : class
    {
        return dropdown != null && dropdown.value >= 0 && dropdown.value < options.Count ? options[dropdown.value] : null;
    }

    private static void SetOptions(TMP_Dropdown dropdown, List<TMP_Dropdown.OptionData> options, int index, string emptyText)
    {
        if (dropdown == null) return;
        dropdown.ClearOptions();
        dropdown.AddOptions(options.Count > 0 ? options : new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(emptyText) });
        dropdown.SetValueWithoutNotify(Mathf.Max(0, index));
        dropdown.interactable = options.Count > 0;
        dropdown.RefreshShownValue();
    }

    private void RefreshPantheonOptions()
    {
        var previous = Selected(pantheonDropdown, availablePantheons);
        availablePantheons = _cachedReligionManager != null ? _cachedReligionManager.GetAvailablePantheons() : new List<PantheonData>();
        if (playerCiv.cultureUnlockedPantheons != null)
            foreach (var pantheon in playerCiv.cultureUnlockedPantheons)
                if (pantheon != null && pantheon.IsSpirit && !availablePantheons.Contains(pantheon) &&
                    (_cachedReligionManager == null || !_cachedReligionManager.IsPantheonClaimed(pantheon))) availablePantheons.Add(pantheon);
        availablePantheons.RemoveAll(pantheon => pantheon == null || (playerCiv.foundedPantheons != null && playerCiv.foundedPantheons.Contains(pantheon)));
        var options = new List<TMP_Dropdown.OptionData>();
        foreach (var pantheon in availablePantheons) options.Add(new TMP_Dropdown.OptionData(pantheon.pantheonName, pantheon.icon, Color.white));
        SetOptions(pantheonDropdown, options, availablePantheons.IndexOf(previous), "No unclaimed pantheons");
        if (pantheonFoundingPanel != null) pantheonFoundingPanel.SetActive(true);
        OnPantheonSelected(pantheonDropdown != null ? pantheonDropdown.value : -1);

        var previousUpgrade = Selected(pantheonUpgradeDropdown, _upgradeOptions);
        _upgradeOptions.Clear();
        options = new List<TMP_Dropdown.OptionData>();
        if (playerCiv.foundedPantheons != null)
            foreach (var pantheon in playerCiv.foundedPantheons)
                if (pantheon != null && pantheon.IsSpirit && pantheon.canUpgradeToGod && pantheon.upgradedPantheon != null)
                {
                    _upgradeOptions.Add(pantheon);
                    options.Add(new TMP_Dropdown.OptionData(pantheon.pantheonName, pantheon.icon, Color.white));
                }
        SetOptions(pantheonUpgradeDropdown, options, _upgradeOptions.IndexOf(previousUpgrade), "No upgrades available");
        if (pantheonUpgradePanel != null) pantheonUpgradePanel.SetActive(_upgradeOptions.Count > 0);
        OnPantheonUpgradeSelected(pantheonUpgradeDropdown != null ? pantheonUpgradeDropdown.value : -1);
    }

    private void RefreshReligionOptions()
    {
        var previousCity = Selected(holySiteCityDropdown, holySiteCities);
        holySiteCities.Clear();
        var options = new List<TMP_Dropdown.OptionData>();
        if (playerCiv.cities != null)
            foreach (var city in playerCiv.cities)
                if (city != null && city.owner == playerCiv && city.HasHolySite())
                {
                    holySiteCities.Add(city);
                    options.Add(new TMP_Dropdown.OptionData(city.cityName));
                }
        SetOptions(holySiteCityDropdown, options, holySiteCities.IndexOf(previousCity), "No Holy Sites");
        var previousReligion = Selected(religionDropdown, availableReligions);
        availableReligions.Clear();
        _religionOptions.Clear();
        options = new List<TMP_Dropdown.OptionData>();
        var religions = ResourceCache.GetAllReligionData();
        if (religions != null)
            foreach (var religion in religions)
            {
                if (religion == null || availableReligions.Contains(religion)) continue;
                var info = new ReligionFoundingInfo(playerCiv, religion, Selected(holySiteCityDropdown, holySiteCities), _cachedReligionManager);
                availableReligions.Add(religion);
                _religionOptions.Add(info);
                options.Add(new TMP_Dropdown.OptionData($"{info.Name} ({info.FaithCost} Faith) - {(info.IsFounded ? "Founded" : info.Availability.CanFound ? "Available" : "Locked")}", info.Icon, Color.white));
            }
        SetOptions(religionDropdown, options, availableReligions.IndexOf(previousReligion), "No religions configured");
        if (religionFoundingPanel != null) religionFoundingPanel.SetActive(true);
        OnReligionSelected(religionDropdown != null ? religionDropdown.value : -1);
    }

    private void RefreshReligionIdentity()
    {
        if (religionInfoPanel != null) religionInfoPanel.SetActive(true);
        string founded = $"Founded religion: {ReligionPresentation.ReligionName(playerCiv.foundedReligion)}";
        string state = $"State religion: {ReligionPresentation.ReligionName(playerCiv.StateReligion)}";
        if (foundedReligionText != null) foundedReligionText.text = founded;
        if (stateReligionText != null) stateReligionText.text = state;
        if (religionNameText != null) religionNameText.text = founded + "\n" + state;
        if (religionIcon != null)
        {
            religionIcon.sprite = playerCiv.foundedReligion != null ? playerCiv.foundedReligion.icon : null;
            religionIcon.enabled = religionIcon.sprite != null;
        }
        var owned = new List<string>();
        if (playerCiv.foundedPantheons != null)
            foreach (var pantheon in playerCiv.foundedPantheons)
                if (pantheon != null) owned.Add($"{pantheon.pantheonName} ({pantheon.tier})");
        if (foundedPantheonsText != null) foundedPantheonsText.text = owned.Count > 0 ? string.Join("\n", owned) : "No pantheons founded.";
        RefreshReligionSummaryText();
    }

    /// <summary>
    /// Called when the user selects a pantheon from the upgrade dropdown
    /// </summary>
    private void OnPantheonUpgradeSelected(int index)
    {
        if (playerCiv == null) return;
        if (index < 0 || index >= _upgradeOptions.Count)
        {
            _selectedUpgradeDetails = string.Empty;
            if (upgradeDetailsText != null) upgradeDetailsText.text = string.Empty;
            if (upgradeInfoText != null) upgradeInfoText.text = "No upgrades available.";
            if (upgradePantheonButton != null) upgradePantheonButton.interactable = false;
            return;
        }
        var spirit = _upgradeOptions[index];
        var god = spirit.upgradedPantheon;
        bool canUpgrade = playerCiv.CanUpgradePantheon(spirit, out string reason);
        if (upgradePantheonButton != null) upgradePantheonButton.interactable = canUpgrade;
        _selectedUpgradeDetails = $"{spirit.pantheonName} -> {god.pantheonName}\nCost: {god.faithCost} Faith\nRequires: owned Spirit with a configured God upgrade.\n{(canUpgrade ? "Available" : reason)}\n{BuildPantheonEffectSummary(god)}";
        if (upgradeDetailsText != null) upgradeDetailsText.text = _selectedUpgradeDetails;
        if (upgradeInfoText != null)
            upgradeInfoText.text = $"{spirit.pantheonName} -> {god.pantheonName}\n{god.faithCost} Faith | {(canUpgrade ? "Available" : "Locked")}";
        RefreshReligionSummaryText();
    }

    /// <summary>
    /// Called when the user clicks the Upgrade button
    /// </summary>
    private void OnUpgradePantheonClicked()
    {
        if (playerCiv == null) return;
        var spirit = Selected(pantheonUpgradeDropdown, _upgradeOptions);
        if (!playerCiv.CanUpgradePantheon(spirit, out string reason))
        {
            ShowActionFeedback(reason);
            return;
        }
        ShowActionFeedback(playerCiv.UpgradePantheon(spirit) ? "Pantheon upgraded." : "Pantheon upgrade failed.");
        UpdateUIState();
    }
    
    /// <summary>
    /// Called when a pantheon is selected from the dropdown
    /// </summary>
    private void OnPantheonSelected(int index)
    {
        if (playerCiv == null) return;
        string count = $"Pantheons: {playerCiv.foundedPantheons?.Count ?? 0} / {playerCiv.CurrentPantheonCap}";
        if (pantheonCountText != null) pantheonCountText.text = count;
        string reason = playerCiv.CanFoundMorePantheons() ? "No unclaimed pantheons available." : "Pantheon capacity reached.";
        bool canFound = false;
        string details = string.Empty;
        if (index >= 0 && index < availablePantheons.Count)
        {
            var pantheon = availablePantheons[index];
            canFound = playerCiv.CanFoundPantheon(pantheon, out reason);
            details = $"\nCost: {playerCiv.GetPantheonCost(pantheon)} Faith";
        }
        if (pantheonCostText != null) pantheonCostText.text = count + details + "\n" + (canFound ? "Available" : reason);
        if (foundPantheonButton != null) foundPantheonButton.interactable = canFound;
    }
    
    /// <summary>
    /// Called when a religion is selected from the dropdown
    /// </summary>
    private void OnReligionSelected(int index)
    {
        if (playerCiv == null) return;
        if (index < 0 || index >= availableReligions.Count)
        {
            _selectedReligionDetails = string.Empty;
            if (religionRequirementsText != null) religionRequirementsText.text = string.Empty;
            if (religionCostText != null) religionCostText.text = "No religions configured.";
            if (foundReligionButton != null) foundReligionButton.interactable = false;
            if (selectedReligionIcon != null) selectedReligionIcon.enabled = false;
            return;
        }
        var info = new ReligionFoundingInfo(playerCiv, availableReligions[index], Selected(holySiteCityDropdown, holySiteCities), _cachedReligionManager);
        if (index < _religionOptions.Count) _religionOptions[index] = info;
        _selectedReligionDetails = $"{info.Name}\nCost: {info.FaithCost} Faith\n{info.Requirements}\nAlready founded: {(info.IsFounded ? "Yes" : "No")}\n{(info.Availability.CanFound ? "Available" : info.Availability.FailureReason)}";
        if (religionRequirementsText != null) religionRequirementsText.text = _selectedReligionDetails;
        if (religionCostText != null)
            religionCostText.text = $"Cost: {info.FaithCost} Faith | {(info.Availability.CanFound ? "Available" : info.Availability.FailureReason)}";
        if (selectedReligionIcon != null)
        {
            selectedReligionIcon.sprite = info.Icon;
            selectedReligionIcon.enabled = info.Icon != null;
        }
        if (foundReligionButton != null) foundReligionButton.interactable = info.Availability.CanFound;
        RefreshReligionSummaryText();
    }
    
    /// <summary>
    /// Called when the found pantheon button is clicked
    /// </summary>
    private void OnFoundPantheonClicked()
    {
        if (playerCiv == null) return;
        var pantheon = Selected(pantheonDropdown, availablePantheons);
        if (!playerCiv.CanFoundPantheon(pantheon, out string reason))
        {
            ShowActionFeedback(reason);
            return;
        }
        ShowActionFeedback(playerCiv.FoundPantheon(pantheon) ? "Pantheon founded." : "Pantheon founding failed.");
        UpdateUIState();
    }
    
    /// <summary>
    /// Called when the found religion button is clicked
    /// </summary>
    private void OnFoundReligionClicked()
    {
        if (playerCiv == null) return;
        var religion = Selected(religionDropdown, availableReligions);
        var city = Selected(holySiteCityDropdown, holySiteCities);
        var availability = _cachedReligionManager != null ? _cachedReligionManager.CanFoundReligion(playerCiv, religion, city, true)
            : ReligionAvailabilityResult.Fail("Religion manager is unavailable.");
        if (!availability.CanFound)
        {
            ShowActionFeedback(availability.FailureReason);
            return;
        }
        ShowActionFeedback(playerCiv.FoundReligion(religion, city) ? "Religion founded." : "Religion founding failed.");
        UpdateUIState();
    }
}
