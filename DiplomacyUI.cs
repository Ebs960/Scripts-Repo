using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

/// <summary>
/// Full diplomacy negotiation screen.
/// Layout:
///   - Left sidebar: list of known civilisations (same as before)
///   - Centre-top: Leader presentation (background, portrait/animation, name plate, dialogue)
///   - Centre-bottom: Deal table (two columns – "Our Offer" / "Their Offer")
///   - Bottom bar: action buttons (Propose Deal, Accept, Reject, Declare War, Make Peace, Cancel)
/// </summary>
public class DiplomacyUI : MonoBehaviour
{
    // ────────────────────────── Inspector References ──────────────────────────
    [Header("Main Panel")]
    public GameObject mainPanel;
    public Button closeButton;

    [Header("Left Section – Civ List")]
    public Transform civListContainer;
    public GameObject civListItemPrefab;
    public ScrollRect civListScroll;

    [Header("Leader Presentation")]
    [Tooltip("Full-screen or large image behind the leader (palace / throne room)")]
    public Image leaderBackground;
    [Tooltip("RawImage used for animated GIF leader moods. Optional but recommended when using GIFs.")]
    public RawImage leaderGifImage;
    [Tooltip("GIF playback helper for animated leader moods.")]
    public GifImagePlayer leaderGifPlayer;
    [Tooltip("Leader portrait image (swapped per mood when no Animator)")]
    public Image leaderPortraitImage;
    [Tooltip("Optional Animator on the leader portrait GameObject")]
    public Animator leaderAnimator;
    [Tooltip("Leader name label")]
    public TextMeshProUGUI leaderNameText;
    [Tooltip("Civ name label (below leader name)")]
    public TextMeshProUGUI leaderCivNameText;
    [Tooltip("Leader dialogue / reaction text")]
    public TextMeshProUGUI leaderDialogueText;
    [SerializeField] private Sprite neutralLeaderPortrait;

    [Header("Ledger Tabs")]
    [SerializeField] private Button overviewTabButton;
    [SerializeField] private Button negotiateTabButton;
    [SerializeField] private Button subjectsTabButton;
    [SerializeField] private GameObject overviewContent;
    [SerializeField] private GameObject negotiateContent;
    [SerializeField] private GameObject subjectsContent;
    [SerializeField] private TextMeshProUGUI emptyStateText;
    [SerializeField] private TextMeshProUGUI dealStatusText;
    [SerializeField] private TextMeshProUGUI subjectSummaryText;
    [SerializeField] private TextMeshProUGUI subjectCooldownText;
    [SerializeField] private Sprite ledgerBackgroundSprite;
    [SerializeField] private Sprite civilizationRowSprite;
    [SerializeField] private Sprite leaderPortraitFrameSprite;
    [SerializeField] private Sprite actionButtonSprite;
    [SerializeField] private Sprite dealItemRowSprite;
    [SerializeField] private Sprite itemPickerSprite;
    [SerializeField] private Sprite confirmationDialogSprite;
    [SerializeField] private DiplomacyConfirmationDialog confirmationDialog;

    [Header("Relationship Info")]
    public TextMeshProUGUI relationshipStatusText;
    public TextMeshProUGUI reputationText;
    public TextMeshProUGUI trustText;

    [Header("Civ Comparison (optional – keep for overview)")]
    public TextMeshProUGUI militaryStrengthText;
    public TextMeshProUGUI economyStrengthText;
    public TextMeshProUGUI scienceProgressText;
    public TextMeshProUGUI faithStatusText;
    public TextMeshProUGUI governmentText;

    [Header("Deal Table – Our Offer (left column)")]
    public Transform ourOfferContainer;
    public GameObject dealItemPrefab;
    public Button addGoldButton;
    public Button addResourceButton;
    public Button addTechButton;
    public Button addCityButton;

    [Header("Deal Table – Their Offer / Our Demands (right column)")]
    public Transform theirOfferContainer;
    public Button demandGoldButton;
    public Button demandResourceButton;
    public Button demandTechButton;
    public Button demandCityButton;

    [Header("Item Picker Popup")]
    [Tooltip("Panel that appears when choosing a resource/tech/city to add")]
    public GameObject itemPickerPanel;
    public Transform itemPickerContainer;
    public GameObject itemPickerRowPrefab;
    public TextMeshProUGUI itemPickerTitleText;
    public Button itemPickerCloseButton;
    [SerializeField] private Button itemPickerConfirmButton;
    [SerializeField] private TextMeshProUGUI itemPickerDetailText;
    public TMP_InputField goldInputField;
    public GameObject goldInputPanel;
    [SerializeField] private TMP_InputField resourceQuantityInputField;
    [SerializeField] private GameObject resourceQuantityPanel;

    [Header("Action Buttons")]
    public Button proposeDealButton;
    public Button acceptDealButton;
    public Button rejectDealButton;
    public Button offerVassalageButton;
    public Button breakVassalageButton;
    public Button raiseTributeButton;
    public Button lowerTributeButton;
    public Button forceReligionButton;
    public Button replaceGovernorButton;
    public Button declareWarButton;
    public Button makePeaceButton;
    public Button cancelButton;

    // ────────────────────────── Runtime State ──────────────────────────
    private Civilization playerCiv;
    private Civilization selectedCiv;
    private DiplomaticOffer currentOffer;
    private List<GameObject> civListItems = new List<GameObject>();
    private List<GameObject> ourOfferItems = new List<GameObject>();
    private List<GameObject> theirOfferItems = new List<GameObject>();
    private List<GameObject> pickerItems = new List<GameObject>();
    private readonly Dictionary<Civilization, DiplomaticOffer> outgoingDrafts = new Dictionary<Civilization, DiplomaticOffer>();
    private LeaderMood currentMood = LeaderMood.Idle;
    private LedgerTab activeTab = LedgerTab.Overview;
    private bool listenersRegistered;
    private bool isExecutingOffer;
    private bool pickerForPlayerGive;
    private ResourceData pendingResource;
    private int pendingResourceMaximum;
    private PickerMode pickerMode;

    private enum LedgerTab
    {
        Overview,
        Negotiate,
        Subjects
    }

    private enum PickerMode
    {
        None,
        Gold,
        ResourceQuantity
    }

    /// <summary>True when the AI has sent a proposal to the player (accept/reject mode).</summary>
    private bool isIncomingProposal;

    // ────────────────────────── Lifecycle ──────────────────────────
    private void Awake()
    {
        SetupButtonListeners();
        ApplyAssignedArtwork();
    }

    private void OnEnable()
    {
        if (DiplomacyManager.Instance != null)
            DiplomacyManager.Instance.OnDiplomacyChanged += HandleDiplomacyChanged;
        SubjectManager.ContractChanged += HandleSubjectContractChanged;
    }

    private void OnDisable()
    {
        if (DiplomacyManager.Instance != null)
            DiplomacyManager.Instance.OnDiplomacyChanged -= HandleDiplomacyChanged;
        SubjectManager.ContractChanged -= HandleSubjectContractChanged;
        CloseItemPicker();
        confirmationDialog?.HideWithoutCallback();
    }

    private void Update()
    {
        if (!gameObject.activeInHierarchy || Keyboard.current == null || !Keyboard.current[Key.Escape].wasPressedThisFrame)
            return;

        if (itemPickerPanel != null && itemPickerPanel.activeInHierarchy)
            CloseItemPicker();
        else if (confirmationDialog != null && confirmationDialog.IsVisible)
            confirmationDialog.Cancel();
        else
            Hide();
    }

    private void SetupButtonListeners()
    {
        if (listenersRegistered) return;
        listenersRegistered = true;

        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        // Add-to-deal buttons (our side)
        if (addGoldButton != null)       addGoldButton.onClick.AddListener(() => OpenGoldInput(true));
        if (addResourceButton != null)   addResourceButton.onClick.AddListener(() => OpenResourcePicker(true));
        if (addTechButton != null)       addTechButton.onClick.AddListener(() => OpenTechPicker(true));
        if (addCityButton != null)       addCityButton.onClick.AddListener(() => OpenCityPicker(true));

        // Add-to-deal buttons (their side / our demands)
        if (demandGoldButton != null)    demandGoldButton.onClick.AddListener(() => OpenGoldInput(false));
        if (demandResourceButton != null) demandResourceButton.onClick.AddListener(() => OpenResourcePicker(false));
        if (demandTechButton != null)    demandTechButton.onClick.AddListener(() => OpenTechPicker(false));
        if (demandCityButton != null)    demandCityButton.onClick.AddListener(() => OpenCityPicker(false));

        // Action buttons
        if (proposeDealButton != null)   proposeDealButton.onClick.AddListener(OnProposeDealClicked);
        if (acceptDealButton != null)    acceptDealButton.onClick.AddListener(OnAcceptDealClicked);
        if (rejectDealButton != null)    rejectDealButton.onClick.AddListener(OnRejectDealClicked);
        if (offerVassalageButton != null) offerVassalageButton.onClick.AddListener(OnOfferVassalageClicked);
        if (breakVassalageButton != null) breakVassalageButton.onClick.AddListener(OnBreakVassalageClicked);
        if (raiseTributeButton != null) raiseTributeButton.onClick.AddListener(OnRaiseTributeClicked);
        if (lowerTributeButton != null) lowerTributeButton.onClick.AddListener(OnLowerTributeClicked);
        if (forceReligionButton != null) forceReligionButton.onClick.AddListener(OnForceReligionClicked);
        if (replaceGovernorButton != null) replaceGovernorButton.onClick.AddListener(OnReplaceGovernorClicked);
        if (declareWarButton != null)    declareWarButton.onClick.AddListener(OnDeclareWarClicked);
        if (makePeaceButton != null)     makePeaceButton.onClick.AddListener(OnMakePeaceClicked);
        if (cancelButton != null)        cancelButton.onClick.AddListener(Hide);

        // Item picker close
        if (itemPickerCloseButton != null) itemPickerCloseButton.onClick.AddListener(CloseItemPicker);
        if (itemPickerConfirmButton != null) itemPickerConfirmButton.onClick.AddListener(ConfirmPickerSelection);

        if (overviewTabButton != null) overviewTabButton.onClick.AddListener(ShowOverviewTab);
        if (negotiateTabButton != null) negotiateTabButton.onClick.AddListener(ShowNegotiateTab);
        if (subjectsTabButton != null) subjectsTabButton.onClick.AddListener(ShowSubjectsTab);
    }

    // ────────────────────────── Public API ──────────────────────────

    /// <summary>Open the diplomacy screen from the player's perspective.</summary>
    public void Show(Civilization playerCiv)
    {
        if (isIncomingProposal && currentOffer != null)
        {
            UIManager.Instance?.ShowNotification("Resolve the incoming proposal before opening another negotiation.");
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            return;
        }
        if (playerCiv == null) return;

        this.playerCiv = playerCiv;
        isIncomingProposal = false;
        selectedCiv = null;
        currentOffer = null;
        activeTab = LedgerTab.Overview;
        gameObject.SetActive(true);
        UpdateCivilizationList();
        ClearNegotiationPanel();
        SetActiveTab(LedgerTab.Overview);
        CloseItemPicker();
    }

    /// <summary>
    /// Open the diplomacy screen with a specific civ pre-selected 
    /// and an incoming AI proposal to accept/reject.
    /// </summary>
    public void ShowIncomingProposal(Civilization playerCiv, DiplomaticOffer offer)
    {
        if (playerCiv == null || offer == null || offer.proposer == null || offer.recipient != playerCiv)
            return;
        if (isIncomingProposal && currentOffer != null && currentOffer != offer)
        {
            UIManager.Instance?.ShowNotification("Resolve the current incoming proposal before viewing another.");
            return;
        }
        this.playerCiv = playerCiv;
        this.selectedCiv = offer.proposer;
        isIncomingProposal = true;
        currentOffer = offer;
        gameObject.SetActive(true);
        UpdateCivilizationList();
        RefreshLeaderPresentation();
        RefreshDealTable();
        RefreshActionButtons();
        SetActiveTab(LedgerTab.Negotiate);
        SetLeaderMood(LeaderMood.Speaking);
        SetDialogue($"{ElectionManager.GetCurrentRulerName(selectedCiv)} has a proposal for you.");
        CloseItemPicker();
    }

    public void Hide()
    {
        if (confirmationDialog != null && confirmationDialog.IsVisible)
        {
            confirmationDialog.Cancel();
            return;
        }
        if (itemPickerPanel != null && itemPickerPanel.activeInHierarchy)
        {
            CloseItemPicker();
            return;
        }
        if (isIncomingProposal && currentOffer != null)
        {
            UIManager.Instance?.ShowNotification("Accept or reject the incoming proposal before closing diplomacy.");
            return;
        }
        gameObject.SetActive(false);
        selectedCiv = null;
        currentOffer = null;
    }

    public void ShowOverviewTab() => SetActiveTab(LedgerTab.Overview);
    public void ShowNegotiateTab() => SetActiveTab(LedgerTab.Negotiate);
    public void ShowSubjectsTab() => SetActiveTab(LedgerTab.Subjects);

    private void SetActiveTab(LedgerTab tab)
    {
        if (tab == LedgerTab.Subjects && !HasRelevantSubjectContract())
        {
            RefreshSubjectSummary();
            return;
        }

        activeTab = tab;
        if (overviewContent != null) overviewContent.SetActive(selectedCiv != null && tab == LedgerTab.Overview);
        if (negotiateContent != null) negotiateContent.SetActive(selectedCiv != null && tab == LedgerTab.Negotiate);
        if (subjectsContent != null) subjectsContent.SetActive(selectedCiv != null && tab == LedgerTab.Subjects);
        if (emptyStateText != null) emptyStateText.gameObject.SetActive(selectedCiv == null);
        if (overviewTabButton != null) overviewTabButton.interactable = selectedCiv != null;
        if (negotiateTabButton != null) negotiateTabButton.interactable = selectedCiv != null;
        if (subjectsTabButton != null)
        {
            subjectsTabButton.interactable = HasRelevantSubjectContract();
            if (!subjectsTabButton.interactable)
                AddTooltip(subjectsTabButton.gameObject, "Subjects", "This civilization has no active subject contract with you.");
        }
        RefreshActionButtons();
        RefreshSubjectSummary();
    }

    private void ApplyAssignedArtwork()
    {
        if (mainPanel != null && ledgerBackgroundSprite != null)
        {
            var image = mainPanel.GetComponent<Image>();
            if (image != null) image.sprite = ledgerBackgroundSprite;
        }
        if (leaderPortraitFrameSprite != null && leaderPortraitImage != null)
        {
            var frame = leaderPortraitImage.transform.parent != null
                ? leaderPortraitImage.transform.parent.GetComponent<Image>()
                : null;
            if (frame != null) frame.sprite = leaderPortraitFrameSprite;
        }
        if (actionButtonSprite != null)
            foreach (var button in GetComponentsInChildren<Button>(true))
                if (button != null && button.targetGraphic is Image image) image.sprite = actionButtonSprite;
        if (itemPickerSprite != null && itemPickerPanel != null)
        {
            var image = itemPickerPanel.GetComponent<Image>();
            if (image != null) image.sprite = itemPickerSprite;
        }
    }

    private static void AddTooltip(GameObject target, string title, string description)
    {
        if (target == null) return;
        var tooltip = target.GetComponent<SimpleTooltipTarget>();
        if (tooltip == null) tooltip = target.AddComponent<SimpleTooltipTarget>();
        tooltip.Bind(title, description);
    }

    // ────────────────────────── Civ List ──────────────────────────

    private void UpdateCivilizationList()
    {
        if (CivilizationManager.Instance == null || civListItemPrefab == null || civListContainer == null) return;

        var civilizations = CivilizationManager.Instance.GetAllCivs()
            .Where(civ => civ != null && civ != playerCiv)
            .ToList();
        for (int i = 0; i < civilizations.Count; i++)
        {
            if (i >= civListItems.Count)
            {
                var instance = Instantiate(civListItemPrefab, civListContainer);
                civListItems.Add(instance);
            }

            var listItem = civListItems[i];
            listItem.SetActive(true);
            var row = listItem.GetComponent<CivilizationSelectionRow>();
            if (row == null) row = listItem.AddComponent<CivilizationSelectionRow>();
            var civ = civilizations[i];
            var relation = DiplomacyManager.Instance != null
                ? DiplomacyManager.Instance.GetRelationship(civ, playerCiv).ToString()
                : DiplomaticState.Peace.ToString();
            row.Bind(civ, relation, civ == selectedCiv, OnCivSelected, civilizationRowSprite);
        }

        for (int i = civilizations.Count; i < civListItems.Count; i++)
            civListItems[i].SetActive(false);
    }

    private void OnCivSelected(Civilization civ)
    {
        if (civ == null || civ == playerCiv) return;
        if (isIncomingProposal && currentOffer != null && civ != currentOffer.proposer)
        {
            UIManager.Instance?.ShowNotification("Resolve the incoming proposal before switching civilizations.");
            return;
        }

        selectedCiv = civ;
        isIncomingProposal = false;
        if (!outgoingDrafts.TryGetValue(civ, out currentOffer))
        {
            currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = civ };
            outgoingDrafts[civ] = currentOffer;
        }
        else
        {
            currentOffer.proposer = playerCiv;
            currentOffer.recipient = civ;
        }
        activeTab = LedgerTab.Overview;
        UpdateCivilizationList();
        RefreshLeaderPresentation();
        RefreshDealTable();
        RefreshSubjectSummary();
        SetActiveTab(LedgerTab.Overview);
        RefreshActionButtons();
        // Determine initial mood from relationship
        var relation = DiplomacyManager.Instance.GetRelationship(selectedCiv, playerCiv);
        if (relation == DiplomaticState.War)
            SetLeaderMood(LeaderMood.Angry);
        else if (relation == DiplomaticState.Alliance)
            SetLeaderMood(LeaderMood.Happy);
        else
            SetLeaderMood(LeaderMood.Idle);

        SetDialogue(GetGreeting(selectedCiv, relation));
    }

    private List<DealItem> GetPlayerGiveItems()
        => currentOffer == null ? null : isIncomingProposal ? currentOffer.recipientItems : currentOffer.proposerItems;

    private List<DealItem> GetPlayerReceiveItems()
        => currentOffer == null ? null : isIncomingProposal ? currentOffer.proposerItems : currentOffer.recipientItems;

    private bool HasRelevantSubjectContract()
    {
        if (playerCiv == null || selectedCiv == null || SubjectManager.Instance == null) return false;
        return SubjectManager.Instance.GetContract(playerCiv, selectedCiv) != null
            || SubjectManager.Instance.GetContract(selectedCiv, playerCiv) != null;
    }

    private void RefreshSubjectSummary()
    {
        if (subjectSummaryText == null && subjectCooldownText == null) return;
        var manager = SubjectManager.Instance;
        var contract = manager != null && playerCiv != null && selectedCiv != null
            ? manager.GetContract(playerCiv, selectedCiv)
            : null;
        bool isPlayerOverlord = contract != null;
        contract ??= manager != null && playerCiv != null && selectedCiv != null
            ? manager.GetContract(selectedCiv, playerCiv)
            : null;

        if (contract == null)
        {
            if (subjectSummaryText != null) subjectSummaryText.text = "No active subject contract with this civilization.";
            if (subjectCooldownText != null) subjectCooldownText.text = string.Empty;
            return;
        }

        if (subjectSummaryText != null)
        {
            string perspective = isPlayerOverlord ? "Subject" : "Overlord";
            subjectSummaryText.text = isPlayerOverlord
                ? $"Gold tribute: {contract.goldTributePct:P0}\nContract: {(contract.isCapitulated ? "Capitulated" : "Voluntary")}\nSubject opinion: {manager.GetEffectiveSubjectOpinion(contract):0}\nLiberty desire: {contract.libertyDesire:0}"
                : $"This civilization is your overlord.\nContract: {(contract.isCapitulated ? "Capitulated" : "Voluntary")}\nYour liberty desire: {contract.libertyDesire:0}";
        }

        if (subjectCooldownText != null)
        {
            var check = isPlayerOverlord
                ? manager.CanInterfere(playerCiv, selectedCiv, TurnManager.Instance?.round ?? 0)
                : SubjectActionResult.Fail("Only the overlord can change these subject terms.");
            subjectCooldownText.text = check.success ? "Interventions are available." : check.reason;
        }
    }

    private void HandleDiplomacyChanged(Civilization a, Civilization b, DiplomaticState state)
    {
        if (!gameObject.activeInHierarchy || playerCiv == null || selectedCiv == null
            || (a != playerCiv && b != playerCiv && a != selectedCiv && b != selectedCiv))
            return;
        UpdateCivilizationList();
        RefreshLeaderPresentation();
        RefreshActionButtons();
        RefreshSubjectSummary();
    }

    private void HandleSubjectContractChanged(Civilization overlord, Civilization subject)
    {
        if (!gameObject.activeInHierarchy || playerCiv == null || selectedCiv == null) return;
        if ((overlord == playerCiv && subject == selectedCiv)
            || (overlord == selectedCiv && subject == playerCiv))
        {
            RefreshSubjectSummary();
            RefreshActionButtons();
        }
    }

    // ────────────────────────── Leader Presentation ──────────────────────────

    private void RefreshLeaderPresentation()
    {
        if (selectedCiv == null) return;
        var leader = selectedCiv.leader;
        if (leader == null) return;

        // Background
        if (leaderBackground != null)
        {
            if (leader.background != null)
            {
                leaderBackground.sprite = leader.background;
                leaderBackground.gameObject.SetActive(true);
            }
            else
            {
                leaderBackground.gameObject.SetActive(false);
            }
        }

        var office = selectedCiv.electionState?.currentOffice;
        if (office != null && office.governorId >= 0)
            UpdateElectedRulerVisual(ElectionManager.GetCurrentRulerPortrait(selectedCiv));
        else
            UpdateLeaderVisual(leader, currentMood);

        // Name plates
        if (leaderNameText != null) leaderNameText.text = ElectionManager.GetCurrentRulerName(selectedCiv);
        if (leaderCivNameText != null) leaderCivNameText.text = selectedCiv.civData != null ? selectedCiv.civData.civName : "";

        // Relationship info
        if (relationshipStatusText != null)
        {
            var rel = DiplomacyManager.Instance.GetRelationship(playerCiv, selectedCiv);
            relationshipStatusText.text = $"Status: {rel}";
        }
        if (reputationText != null || trustText != null)
        {
            if (reputationText != null)
                reputationText.text = $"Reputation: {DiplomacyManager.Instance.GetEffectiveReputation(selectedCiv, playerCiv)}";
            if (trustText != null)
                trustText.text = $"Trust: {DiplomacyManager.Instance.GetEffectiveTrustLevel(selectedCiv, playerCiv)}/10";
        }

        // Optional comparison fields
        RefreshComparisonFields();
    }

    private void RefreshComparisonFields()
    {
        if (selectedCiv == null) return;

        if (militaryStrengthText != null)
        {
            int pStr = 0, sStr = 0;
            foreach (var u in playerCiv.combatUnits) pStr += u.CurrentAttack + u.CurrentDefense;
            foreach (var u in selectedCiv.combatUnits) sStr += u.CurrentAttack + u.CurrentDefense;
            militaryStrengthText.text = $"Military: {(pStr > sStr ? "Stronger" : pStr < sStr ? "Weaker" : "Equal")}";
        }
        if (economyStrengthText != null)
        {
            int pGold = 0, sGold = 0;
            foreach (var c in playerCiv.cities) pGold += c.GetGoldPerTurn();
            foreach (var c in selectedCiv.cities) sGold += c.GetGoldPerTurn();
            pGold = Mathf.RoundToInt(pGold * (1 + playerCiv.goldModifier));
            sGold = Mathf.RoundToInt(sGold * (1 + selectedCiv.goldModifier));
            economyStrengthText.text = $"Economy: {(pGold > sGold ? "Stronger" : pGold < sGold ? "Weaker" : "Equal")}";
        }
        if (scienceProgressText != null)
            scienceProgressText.text = $"Tech Age: {selectedCiv.currentTech?.techAge.ToString().Replace("Age", " Age") ?? "None"}";
        if (faithStatusText != null)
            faithStatusText.text = $"State Religion: {(selectedCiv.StateReligion != null ? selectedCiv.StateReligion.religionName : "None")}";
        if (governmentText != null)
            governmentText.text = $"Government: {selectedCiv.currentGovernment?.governmentName ?? "None"}";
    }

    // ────────────────────────── Leader Mood & Dialogue ──────────────────────────

    public void SetLeaderMood(LeaderMood mood)
    {
        currentMood = mood;
        if (selectedCiv == null || selectedCiv.leader == null) return;
        if (selectedCiv.electionState?.currentOffice?.governorId >= 0)
        {
            UpdateElectedRulerVisual(ElectionManager.GetCurrentRulerPortrait(selectedCiv));
            return;
        }
        UpdateLeaderVisual(selectedCiv.leader, mood);
    }

    private void UpdateElectedRulerVisual(Sprite portrait)
    {
        leaderGifPlayer?.StopPlayback(true);
        if (leaderGifImage != null) leaderGifImage.gameObject.SetActive(false);
        if (leaderAnimator != null) leaderAnimator.gameObject.SetActive(false);
        if (leaderPortraitImage != null)
        {
            leaderPortraitImage.sprite = portrait;
            leaderPortraitImage.gameObject.SetActive(portrait != null);
        }
    }

    private void UpdateLeaderVisual(LeaderData leader, LeaderMood mood)
    {
        bool playingGif = false;
        string gifPath = leader.GetMoodGifPath(mood);
        if (leaderGifPlayer != null)
            playingGif = leaderGifPlayer.PlayFromStreamingAssets(gifPath);

        if (leaderGifImage != null)
            leaderGifImage.gameObject.SetActive(playingGif);

        if (playingGif)
        {
            if (leaderPortraitImage != null)
                leaderPortraitImage.gameObject.SetActive(false);
            if (leaderAnimator != null)
                leaderAnimator.gameObject.SetActive(false);
            return;
        }

        if (leaderGifPlayer != null)
            leaderGifPlayer.StopPlayback(true);

        if (leaderAnimator != null)
        {
            if (leader.leaderAnimator != null)
            {
                leaderAnimator.runtimeAnimatorController = leader.leaderAnimator;
                leaderAnimator.gameObject.SetActive(true);
                leaderAnimator.SetTrigger(LeaderData.GetAnimTrigger(mood));
                if (leaderPortraitImage != null)
                    leaderPortraitImage.gameObject.SetActive(false);
                return;
            }

            leaderAnimator.gameObject.SetActive(false);
        }

        if (leaderPortraitImage != null)
        {
            leaderPortraitImage.sprite = leader.GetMoodSprite(mood);
            leaderPortraitImage.gameObject.SetActive(true);
        }
    }

    public void SetDialogue(string text)
    {
        if (leaderDialogueText != null) leaderDialogueText.text = text;
    }

    private string GetGreeting(Civilization civ, DiplomaticState relation)
    {
        string name = ElectionManager.GetCurrentRulerName(civ);
        return relation switch
        {
            DiplomaticState.War     => $"\"{name}\" glares at you with contempt.",
            DiplomaticState.Alliance => $"\"{name}\" greets you warmly as an ally.",
            DiplomaticState.Trade   => $"\"{name}\" nods in acknowledgement of your trade pact.",
            _                       => $"\"{name}\" regards you cautiously.",
        };
    }

    // ────────────────────────── Deal Table ──────────────────────────

    private void RefreshDealTable()
    {
        ClearDealColumn(ourOfferItems, ourOfferContainer);
        ClearDealColumn(theirOfferItems, theirOfferContainer);

        if (currentOffer == null) return;

        PopulateDealColumn(GetPlayerGiveItems(), ourOfferContainer, ourOfferItems, !isIncomingProposal);
        PopulateDealColumn(GetPlayerReceiveItems(), theirOfferContainer, theirOfferItems, !isIncomingProposal);
        if (dealStatusText != null && !isIncomingProposal) dealStatusText.text = string.Empty;
    }

    private void PopulateDealColumn(List<DealItem> items, Transform container, List<GameObject> trackedObjects, bool allowRemove)
    {
        if (container == null || dealItemPrefab == null) return;

        foreach (var item in items)
        {
            var row = Instantiate(dealItemPrefab, container);
            var text = row.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = item.GetDisplayText();

            // Optional icon
            var icon = row.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                Sprite s = GetDealItemIcon(item);
                if (s != null) { icon.sprite = s; icon.gameObject.SetActive(true); }
                else icon.gameObject.SetActive(false);
            }

            // Remove button (only in edit mode)
            if (allowRemove)
            {
                var removeBtn = row.GetComponentInChildren<Button>();
                if (removeBtn != null)
                {
                    var captured = item;
                    removeBtn.onClick.AddListener(() =>
                    {
                        items.Remove(captured);
                        RefreshDealTable();
                    });
                }
            }

            trackedObjects.Add(row);
        }
    }

    private Sprite GetDealItemIcon(DealItem item)
    {
        switch (item.itemType)
        {
            case DealItemType.Resource:  return item.resource?.icon;
            case DealItemType.Technology: return item.tech?.techIcon;
            case DealItemType.City:
                return item.city?.owner?.civData?.icon;
            default: return null;
        }
    }

    private void ClearDealColumn(List<GameObject> tracked, Transform container)
    {
        foreach (var go in tracked) Destroy(go);
        tracked.Clear();
    }

    private void ClearNegotiationPanel()
    {
        ClearDealColumn(ourOfferItems, ourOfferContainer);
        ClearDealColumn(theirOfferItems, theirOfferContainer);

        if (leaderBackground != null) leaderBackground.gameObject.SetActive(false);
        if (leaderGifPlayer != null) leaderGifPlayer.StopPlayback(true);
        if (leaderGifImage != null) leaderGifImage.gameObject.SetActive(false);
        if (leaderPortraitImage != null) leaderPortraitImage.gameObject.SetActive(false);
        if (leaderAnimator != null) leaderAnimator.gameObject.SetActive(false);
        if (leaderNameText != null) leaderNameText.text = "";
        if (leaderCivNameText != null) leaderCivNameText.text = "";
        if (leaderDialogueText != null) leaderDialogueText.text = "Select a civilization to negotiate with.";
        if (relationshipStatusText != null) relationshipStatusText.text = "";
        if (reputationText != null) reputationText.text = "";
        if (trustText != null) trustText.text = "";
        if (militaryStrengthText != null) militaryStrengthText.text = "";
        if (economyStrengthText != null) economyStrengthText.text = "";
        if (scienceProgressText != null) scienceProgressText.text = "";
        if (faithStatusText != null) faithStatusText.text = "";
        if (governmentText != null) governmentText.text = "";

        RefreshActionButtons();
        if (emptyStateText != null) emptyStateText.gameObject.SetActive(true);
    }

    // ────────────────────────── Action Buttons ──────────────────────────

    private void RefreshActionButtons()
    {
        bool hasCiv = selectedCiv != null;
        var relation = hasCiv && DiplomacyManager.Instance != null
            ? DiplomacyManager.Instance.GetRelationship(playerCiv, selectedCiv)
            : DiplomaticState.Peace;
        bool atWar = relation == DiplomaticState.War;
        bool alreadyVassal = relation == DiplomaticState.Vassal;
        var subjectContract = hasCiv && SubjectManager.Instance != null
            ? SubjectManager.Instance.GetContract(playerCiv, selectedCiv)
            : null;
        bool isOurSubject = subjectContract != null;
        bool onOverview = activeTab == LedgerTab.Overview;
        bool onNegotiation = activeTab == LedgerTab.Negotiate;
        bool onSubjects = activeTab == LedgerTab.Subjects;
        bool canEditOffer = hasCiv && onNegotiation && !isIncomingProposal && currentOffer != null;
        bool canAcceptOffer = hasCiv && onNegotiation && isIncomingProposal;

        if (proposeDealButton != null)
        {
            proposeDealButton.gameObject.SetActive(canEditOffer);
            bool valid = canEditOffer && DiplomacyManager.Instance != null
                && DiplomacyManager.Instance.TryValidateComplexDeal(currentOffer, out string reason);
            proposeDealButton.interactable = valid;
            if (canEditOffer && !valid)
                AddTooltip(proposeDealButton.gameObject, "Proposal unavailable", reason ?? "Add a valid item to the offer.");
        }
        if (acceptDealButton != null)
            acceptDealButton.gameObject.SetActive(canAcceptOffer);
        if (rejectDealButton != null)
            rejectDealButton.gameObject.SetActive(canAcceptOffer);

        SetButtonVisible(addGoldButton, canEditOffer);
        SetButtonVisible(addResourceButton, canEditOffer);
        SetButtonVisible(addTechButton, canEditOffer);
        SetButtonVisible(addCityButton, canEditOffer);
        SetButtonVisible(demandGoldButton, canEditOffer);
        SetButtonVisible(demandResourceButton, canEditOffer);
        SetButtonVisible(demandTechButton, canEditOffer);
        SetButtonVisible(demandCityButton, canEditOffer);

        if (offerVassalageButton != null)
            offerVassalageButton.gameObject.SetActive(hasCiv && onOverview && !isIncomingProposal && !atWar && !alreadyVassal);
        if (breakVassalageButton != null)
            breakVassalageButton.gameObject.SetActive(isOurSubject && onSubjects);
        if (raiseTributeButton != null)
            raiseTributeButton.gameObject.SetActive(isOurSubject && onSubjects);
        if (lowerTributeButton != null)
            lowerTributeButton.gameObject.SetActive(isOurSubject && onSubjects);
        if (forceReligionButton != null)
            forceReligionButton.gameObject.SetActive(isOurSubject && onSubjects);
        if (replaceGovernorButton != null)
            replaceGovernorButton.gameObject.SetActive(isOurSubject && onSubjects);

        if (declareWarButton != null)
            declareWarButton.gameObject.SetActive(hasCiv && onOverview && !atWar);
        if (makePeaceButton != null)
            makePeaceButton.gameObject.SetActive(hasCiv && onOverview && atWar);

        if (cancelButton != null)
            cancelButton.gameObject.SetActive(true);

        if (subjectsTabButton != null)
        {
            subjectsTabButton.interactable = HasRelevantSubjectContract();
            if (!subjectsTabButton.interactable)
                AddTooltip(subjectsTabButton.gameObject, "Subjects unavailable", "A subject contract must exist between these civilizations.");
        }

        if (isOurSubject && SubjectManager.Instance != null)
        {
            var check = SubjectManager.Instance.CanInterfere(playerCiv, selectedCiv, TurnManager.Instance?.round ?? 0);
            foreach (var button in new[] { raiseTributeButton, lowerTributeButton, forceReligionButton, replaceGovernorButton })
            {
                if (button == null) continue;
                button.interactable = check.success;
                if (!check.success) AddTooltip(button.gameObject, "Intervention unavailable", check.reason);
            }
        }
    }

    private static void SetButtonVisible(Button button, bool visible)
    {
        if (button != null) button.gameObject.SetActive(visible);
    }

    // ────────────────────────── Item Pickers ──────────────────────────

    /// <param name="isOurSide">True = adding to our offer; false = adding to our demands.</param>
    private void OpenGoldInput(bool isOurSide)
    {
        if (selectedCiv == null || isIncomingProposal) return;
        pickerForPlayerGive = isOurSide;
        pickerMode = PickerMode.Gold;
        if (goldInputPanel != null) goldInputPanel.SetActive(true);
        if (resourceQuantityPanel != null) resourceQuantityPanel.SetActive(false);
        if (itemPickerPanel != null) itemPickerPanel.SetActive(true);
        if (itemPickerContainer != null) itemPickerContainer.gameObject.SetActive(false);
        if (itemPickerTitleText != null)
            itemPickerTitleText.text = isOurSide ? "Offer Gold" : "Demand Gold";

        if (goldInputField != null)
        {
            goldInputField.text = "";
            goldInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
        }
        var giver = GetGiverForUiSide(isOurSide);
        int available = giver != null ? Mathf.Max(0, giver.gold - GetCommittedGold(isOurSide)) : 0;
        if (itemPickerDetailText != null) itemPickerDetailText.text = $"Available: {available} Gold";
    }

    private void OpenResourcePicker(bool isOurSide)
    {
        if (selectedCiv == null || isIncomingProposal) return;
        pickerForPlayerGive = isOurSide;
        pickerMode = PickerMode.None;
        OpenItemPicker(isOurSide ? "Offer Resource" : "Demand Resource");
        if (goldInputPanel != null) goldInputPanel.SetActive(false);
        if (resourceQuantityPanel != null) resourceQuantityPanel.SetActive(false);

        // List resources owned by the appropriate civ
        var sourceCiv = isOurSide ? playerCiv : selectedCiv;
        if (sourceCiv.resourceStockpile == null) return;

        foreach (var kvp in sourceCiv.resourceStockpile)
        {
            int available = kvp.Value - GetCommittedResourceAmount(isOurSide, kvp.Key);
            if (available <= 0) continue;
            var res = kvp.Key;
            var row = Instantiate(itemPickerRowPrefab, itemPickerContainer);
            var text = row.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = $"{res.resourceName} (x{available})";
            var icon = row.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null && res.icon != null) { icon.sprite = res.icon; icon.gameObject.SetActive(true); }

            var capturedRes = res;
            var capturedAvailable = available;
            var btn = row.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() =>
            {
                pendingResource = capturedRes;
                pendingResourceMaximum = capturedAvailable;
                pickerForPlayerGive = isOurSide;
                pickerMode = PickerMode.ResourceQuantity;
                if (itemPickerTitleText != null) itemPickerTitleText.text = $"Choose {capturedRes.resourceName} quantity";
                if (itemPickerDetailText != null) itemPickerDetailText.text = $"Available: {capturedAvailable}";
                if (itemPickerContainer != null) itemPickerContainer.gameObject.SetActive(false);
                if (resourceQuantityPanel != null) resourceQuantityPanel.SetActive(true);
                if (resourceQuantityInputField != null)
                {
                    resourceQuantityInputField.text = "1";
                    resourceQuantityInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
                }
            });
            pickerItems.Add(row);
        }
    }

    private void OpenTechPicker(bool isOurSide)
    {
        if (selectedCiv == null || isIncomingProposal) return;
        OpenItemPicker(isOurSide ? "Offer Technology" : "Demand Technology");
        if (goldInputPanel != null) goldInputPanel.SetActive(false);

        // Techs that the source civ has but the other doesn't
        var sourceCiv = isOurSide ? playerCiv : selectedCiv;
        var targetCiv = isOurSide ? selectedCiv : playerCiv;

        if (sourceCiv.researchedTechs == null) return;
        foreach (var tech in sourceCiv.researchedTechs)
        {
            if (tech == null || targetCiv.researchedTechs == null || targetCiv.researchedTechs.Contains(tech)) continue;
            if (currentOffer.proposerItems.Concat(currentOffer.recipientItems)
                .Any(item => item != null && item.itemType == DealItemType.Technology && item.tech == tech)) continue;
            var row = Instantiate(itemPickerRowPrefab, itemPickerContainer);
            var text = row.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = tech.techName;
            var icon = row.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null && tech.techIcon != null) { icon.sprite = tech.techIcon; icon.gameObject.SetActive(true); }

            var capturedTech = tech;
            var btn = row.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() =>
            {
                AddDealItem(new DealItem { itemType = DealItemType.Technology, tech = capturedTech }, isOurSide);
                CloseItemPicker();
            });
            pickerItems.Add(row);
        }
    }

    private void OpenCityPicker(bool isOurSide)
    {
        if (selectedCiv == null || isIncomingProposal) return;
        OpenItemPicker(isOurSide ? "Offer City" : "Demand City");
        if (goldInputPanel != null) goldInputPanel.SetActive(false);

        var sourceCiv = isOurSide ? playerCiv : selectedCiv;

        if (sourceCiv.cities == null) return;
        foreach (var city in sourceCiv.cities)
        {
            if (city == null || city.owner != sourceCiv || city.isCapital) continue;
            if (currentOffer.proposerItems.Concat(currentOffer.recipientItems)
                .Any(item => item != null && item.itemType == DealItemType.City && item.city == city)) continue;
            var row = Instantiate(itemPickerRowPrefab, itemPickerContainer);
            var text = row.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = $"{city.cityName} (Pop {city.level})";

            var capturedCity = city;
            var btn = row.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() =>
            {
                AddDealItem(new DealItem { itemType = DealItemType.City, city = capturedCity }, isOurSide);
                CloseItemPicker();
            });
            pickerItems.Add(row);
        }
    }

    private void OpenItemPicker(string title)
    {
        ClearPickerItems();
        if (itemPickerPanel != null) itemPickerPanel.SetActive(true);
        if (itemPickerContainer != null) itemPickerContainer.gameObject.SetActive(true);
        if (goldInputPanel != null) goldInputPanel.SetActive(false);
        if (resourceQuantityPanel != null) resourceQuantityPanel.SetActive(false);
        if (itemPickerTitleText != null) itemPickerTitleText.text = title;
    }

    private void CloseItemPicker()
    {
        ClearPickerItems();
        if (itemPickerPanel != null) itemPickerPanel.SetActive(false);
        if (goldInputPanel != null) goldInputPanel.SetActive(false);
        if (resourceQuantityPanel != null) resourceQuantityPanel.SetActive(false);
        pendingResource = null;
        pendingResourceMaximum = 0;
        pickerMode = PickerMode.None;
    }

    private void ConfirmPickerSelection()
    {
        if (selectedCiv == null || isIncomingProposal || currentOffer == null) return;
        if (pickerMode == PickerMode.Gold)
        {
            if (goldInputField == null || !int.TryParse(goldInputField.text, out int amount) || amount <= 0)
            {
                SetDealError("Enter a positive whole-number gold amount.");
                return;
            }
            var giver = GetGiverForUiSide(pickerForPlayerGive);
            int available = giver != null ? Mathf.Max(0, giver.gold - GetCommittedGold(pickerForPlayerGive)) : 0;
            if (amount > available)
            {
                SetDealError($"The giver has only {available} uncommitted Gold available.");
                return;
            }
            AddDealItem(new DealItem { itemType = DealItemType.Gold, goldAmount = amount }, pickerForPlayerGive);
            CloseItemPicker();
        }
        else if (pickerMode == PickerMode.ResourceQuantity)
        {
            if (resourceQuantityInputField == null
                || !int.TryParse(resourceQuantityInputField.text, out int amount)
                || amount < 1 || amount > pendingResourceMaximum)
            {
                SetDealError($"Choose a whole number from 1 to {pendingResourceMaximum}.");
                return;
            }
            AddDealItem(new DealItem
            {
                itemType = DealItemType.Resource,
                resource = pendingResource,
                resourceAmount = amount
            }, pickerForPlayerGive);
            CloseItemPicker();
        }
    }

    private Civilization GetGiverForUiSide(bool playerGive)
        => playerGive ? playerCiv : selectedCiv;

    private List<DealItem> GetDraftItemsForUiSide(bool playerGive)
        => playerGive ? currentOffer?.proposerItems : currentOffer?.recipientItems;

    private int GetCommittedGold(bool playerGive)
    {
        long total = 0;
        var items = GetDraftItemsForUiSide(playerGive);
        if (items == null) return 0;
        foreach (var item in items)
        {
            if (item == null) continue;
            if (item.itemType == DealItemType.Gold) total += item.goldAmount;
            else if (item.itemType == DealItemType.GoldPerTurn)
                total += (long)item.goldPerTurn * item.goldPerTurnDuration;
        }
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private int GetCommittedResourceAmount(bool playerGive, ResourceData resource)
    {
        int total = 0;
        var items = GetDraftItemsForUiSide(playerGive);
        if (items == null) return 0;
        foreach (var item in items)
            if (item != null && item.itemType == DealItemType.Resource && item.resource == resource)
                total = item.resourceAmount > int.MaxValue - total ? int.MaxValue : total + item.resourceAmount;
        return total;
    }

    private void ClearPickerItems()
    {
        foreach (var go in pickerItems) Destroy(go);
        pickerItems.Clear();
    }

    private void AddDealItem(DealItem item, bool isOurSide)
    {
        if (currentOffer == null || selectedCiv == null || isIncomingProposal || item == null) return;
        if (item.itemType == DealItemType.Technology
            && currentOffer.proposerItems.Concat(currentOffer.recipientItems)
                .Any(existing => existing != null && existing.itemType == DealItemType.Technology && existing.tech == item.tech))
        {
            SetDealError("That technology is already included in this deal.");
            return;
        }
        if (item.itemType == DealItemType.City
            && currentOffer.proposerItems.Concat(currentOffer.recipientItems)
                .Any(existing => existing != null && existing.itemType == DealItemType.City && existing.city == item.city))
        {
            SetDealError("That city is already included in this deal.");
            return;
        }
        if (isOurSide)
            currentOffer.proposerItems.Add(item);
        else
            currentOffer.recipientItems.Add(item);
        if (selectedCiv != null) outgoingDrafts[selectedCiv] = currentOffer;
        if (dealStatusText != null) dealStatusText.text = string.Empty;
        RefreshDealTable();
        RefreshActionButtons();
    }

    private void SetDealError(string message)
    {
        if (dealStatusText != null) dealStatusText.text = message;
        UIManager.Instance?.ShowNotification(message);
    }

    // ────────────────────────── Action Handlers ──────────────────────────

    private void OnProposeDealClicked()
    {
        if (selectedCiv == null || currentOffer == null || isIncomingProposal || isExecutingOffer) return;
        currentOffer.recipient = selectedCiv;

        if (DiplomacyManager.Instance == null)
        {
            SetDealError("Diplomacy is not initialized.");
            return;
        }
        if (!DiplomacyManager.Instance.TryValidateComplexDeal(currentOffer, out string reason))
        {
            SetDealError(reason);
            return;
        }

        bool containsCity = currentOffer.proposerItems.Concat(currentOffer.recipientItems)
            .Any(item => item != null && item.itemType == DealItemType.City);
        if (containsCity)
        {
            if (confirmationDialog == null)
            {
                SetDealError("City transfers require the diplomacy confirmation dialog to be assigned.");
                return;
            }
            confirmationDialog.Show("Propose city transfer?",
                "The city will transfer only if the recipient accepts and the deal remains valid.",
                "Propose", "Cancel", SubmitOutgoingProposal);
            return;
        }

        SubmitOutgoingProposal();
    }

    private void SubmitOutgoingProposal()
    {
        if (selectedCiv == null || currentOffer == null || isIncomingProposal || isExecutingOffer
            || DiplomacyManager.Instance == null) return;
        currentOffer.recipient = selectedCiv;
        if (!DiplomacyManager.Instance.TryValidateComplexDeal(currentOffer, out string reason))
        {
            SetDealError(reason);
            return;
        }

        isExecutingOffer = true;
        if (proposeDealButton != null) proposeDealButton.interactable = false;

        SetLeaderMood(LeaderMood.Speaking);
        SetDialogue("Hmm… let me consider your proposal.");

        bool accepted = DiplomacyManager.Instance.EvaluateComplexDeal(selectedCiv, currentOffer);

        if (accepted)
        {
            if (!DiplomacyManager.Instance.ExecuteComplexDeal(currentOffer))
            {
                DiplomacyManager.Instance.TryValidateComplexDeal(currentOffer, out reason);
                SetDealError(reason ?? "The deal could not be executed. Review it and try again.");
                isExecutingOffer = false;
                RefreshActionButtons();
                return;
            }
            SetLeaderMood(LeaderMood.Agreement);
            SetDialogue($"{ElectionManager.GetCurrentRulerName(selectedCiv)} accepts your deal!");
            UIManager.Instance?.ShowNotification($"Deal accepted by {selectedCiv.civData?.civName ?? selectedCiv.name}!");
            outgoingDrafts.Remove(selectedCiv);
        }
        else
        {
            SetLeaderMood(LeaderMood.Angry);
            SetDialogue($"{ElectionManager.GetCurrentRulerName(selectedCiv)} rejects your proposal.");
            UIManager.Instance?.ShowNotification($"Deal rejected by {selectedCiv.civData?.civName ?? selectedCiv.name}. The draft remains available for revision.");
        }

        if (accepted)
            currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        else
            outgoingDrafts[selectedCiv] = currentOffer;
        isExecutingOffer = false;
        RefreshDealTable();
        RefreshActionButtons();
        RefreshLeaderPresentation();
    }

    private void OnAcceptDealClicked()
    {
        if (currentOffer == null || !isIncomingProposal || isExecutingOffer || DiplomacyManager.Instance == null) return;
        isExecutingOffer = true;
        if (!DiplomacyManager.Instance.ExecuteComplexDeal(currentOffer))
        {
            DiplomacyManager.Instance.TryValidateComplexDeal(currentOffer, out string reason);
            SetDealError(reason ?? "This proposal is no longer valid. It remains open for review.");
            isExecutingOffer = false;
            RefreshActionButtons();
            return;
        }
        SetLeaderMood(LeaderMood.Agreement);
        SetDialogue("A wise decision.");
        UIManager.Instance?.ShowNotification($"Deal accepted with {selectedCiv.civData?.civName ?? selectedCiv.name}!");
        isIncomingProposal = false;
        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        isExecutingOffer = false;
        RefreshDealTable();
        RefreshActionButtons();
        RefreshLeaderPresentation();
    }

    private void OnRejectDealClicked()
    {
        if (currentOffer == null || !isIncomingProposal || isExecutingOffer) return;
        SetLeaderMood(LeaderMood.Angry);
        SetDialogue("You will regret this.");
        // Record refusal in diplomatic memory
        var memory = DiplomacyManager.Instance.GetDiplomaticMemory(selectedCiv);
        memory.RecordEvent(playerCiv, DiplomaticEventType.RefusedTrade);
        UIManager.Instance?.ShowNotification($"Deal rejected from {selectedCiv.civData?.civName ?? selectedCiv.name}.");
        isIncomingProposal = false;
        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        RefreshDealTable();
        RefreshActionButtons();
    }

    private void OnDeclareWarClicked()
    {
        if (selectedCiv == null || DiplomacyManager.Instance == null || confirmationDialog == null) return;
        var target = selectedCiv;
        confirmationDialog.Show("Declare war?", $"This will place your civilization at war with {target.civData?.civName ?? target.name}.",
            "Declare War", "Cancel", () =>
            {
                if (DiplomacyManager.Instance == null || playerCiv == null || target == null) return;
                DiplomacyManager.Instance.SetState(playerCiv, target, DiplomaticState.War);
                SetLeaderMood(LeaderMood.DeclareWar);
                SetDialogue($"So be it! {ElectionManager.GetCurrentRulerName(target)} prepares for war!");
                UIManager.Instance?.ShowNotification($"War declared against {target.civData?.civName ?? target.name}.");
                RefreshLeaderPresentation();
                RefreshActionButtons();
            });
    }

    private void OnOfferVassalageClicked()
    {
        if (selectedCiv == null) return;

        DiplomacyManager.Instance.ProposeDeal(playerCiv, selectedCiv, DealType.Vassal);

        var relation = DiplomacyManager.Instance.GetRelationship(playerCiv, selectedCiv);
        if (relation == DiplomaticState.Vassal)
        {
            SetLeaderMood(LeaderMood.Agreement);
            SetDialogue($"{ElectionManager.GetCurrentRulerName(selectedCiv)} submits to your rule.");
            UIManager.Instance.ShowNotification($"{selectedCiv.civData?.civName ?? selectedCiv.name} accepted vassalage.");
        }
        else
        {
            SetLeaderMood(LeaderMood.Angry);
            SetDialogue($"{ElectionManager.GetCurrentRulerName(selectedCiv)} rejects your demand for submission.");
            UIManager.Instance.ShowNotification($"{selectedCiv.civData?.civName ?? selectedCiv.name} refused vassalage.");
        }

        RefreshLeaderPresentation();
        RefreshActionButtons();
    }

    private void OnBreakVassalageClicked()
    {
        if (selectedCiv == null || SubjectManager.Instance == null || confirmationDialog == null) return;
        var subject = selectedCiv;
        confirmationDialog.Show("Release this subject?", "The contract ends peacefully and all tribute stops.",
            "Release", "Cancel", () => ReportSubjectAction(
                SubjectManager.Instance.TryReleaseSubject(playerCiv, subject, TurnManager.Instance?.round ?? 0),
                $"Vassalage ended with {subject.civData?.civName ?? subject.name}."));
    }

    private void OnRaiseTributeClicked()
    {
        if (selectedCiv == null || SubjectManager.Instance == null) return;
        var contract = SubjectManager.Instance.GetContract(playerCiv, selectedCiv);
        if (contract == null) return;

        float newRate = Mathf.Clamp(contract.goldTributePct + 0.05f, 0f, SubjectManager.MaxTributePct);
        if (Mathf.Approximately(newRate, contract.goldTributePct))
        {
            UIManager.Instance?.ShowNotification("Gold tribute is already at its maximum.");
            return;
        }
        ReportSubjectAction(SubjectManager.Instance.TrySetTributeTerms(playerCiv, selectedCiv, newRate,
            contract.foodTributePct, TurnManager.Instance?.round ?? 0),
            $"Tribute raised to {Mathf.RoundToInt(newRate * 100f)}% for {selectedCiv.civData?.civName ?? selectedCiv.name}.");
    }

    private void OnLowerTributeClicked()
    {
        if (selectedCiv == null || SubjectManager.Instance == null) return;
        var contract = SubjectManager.Instance.GetContract(playerCiv, selectedCiv);
        if (contract == null) return;

        float newRate = Mathf.Clamp(contract.goldTributePct - 0.05f, 0f, SubjectManager.MaxTributePct);
        if (Mathf.Approximately(newRate, contract.goldTributePct))
        {
            UIManager.Instance?.ShowNotification("Gold tribute is already at its minimum.");
            return;
        }
        ReportSubjectAction(SubjectManager.Instance.TrySetTributeTerms(playerCiv, selectedCiv, newRate,
            contract.foodTributePct, TurnManager.Instance?.round ?? 0),
            $"Tribute lowered to {Mathf.RoundToInt(newRate * 100f)}% for {selectedCiv.civData?.civName ?? selectedCiv.name}.");
    }

    private void OnForceReligionClicked()
    {
        if (selectedCiv == null || SubjectManager.Instance == null) return;
        ReportSubjectAction(SubjectManager.Instance.InterfereForceReligion(playerCiv, selectedCiv, TurnManager.Instance?.round ?? 0),
            $"Religion pressure applied to {selectedCiv.civData?.civName ?? selectedCiv.name}.");
    }

    private void OnReplaceGovernorClicked()
    {
        if (selectedCiv == null || SubjectManager.Instance == null) return;
        ReportSubjectAction(SubjectManager.Instance.InterfereReplaceGovernor(playerCiv, selectedCiv, null, TurnManager.Instance?.round ?? 0),
            $"Governor replaced in {selectedCiv.civData?.civName ?? selectedCiv.name}.");
    }

    private void OnMakePeaceClicked()
    {
        if (selectedCiv == null || DiplomacyManager.Instance == null || isExecutingOffer) return;

        // Add peace as a deal item and propose
        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        currentOffer.proposerItems.Add(new DealItem { itemType = DealItemType.MakePeace });
        currentOffer.recipientItems.Add(new DealItem { itemType = DealItemType.MakePeace });

        bool accepted = DiplomacyManager.Instance.EvaluateComplexDeal(selectedCiv, currentOffer);
        if (accepted)
        {
            if (!DiplomacyManager.Instance.ExecuteComplexDeal(currentOffer))
            {
                DiplomacyManager.Instance.TryValidateComplexDeal(currentOffer, out string reason);
                SetDealError(reason ?? "The peace offer could not be executed.");
                return;
            }
            SetLeaderMood(LeaderMood.Agreement);
            SetDialogue("Peace at last. Let us rebuild.");
            UIManager.Instance?.ShowNotification($"Peace established with {selectedCiv.civData?.civName ?? selectedCiv.name}!");
        }
        else
        {
            SetLeaderMood(LeaderMood.Angry);
            SetDialogue("There will be no peace. Not yet.");
            UIManager.Instance?.ShowNotification($"{selectedCiv.civData?.civName ?? selectedCiv.name} refuses peace.");
        }

        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        RefreshDealTable();
        RefreshActionButtons();
        RefreshLeaderPresentation();
    }

    private void ReportSubjectAction(SubjectActionResult result, string successMessage)
    {
        string message = result.success ? successMessage : result.reason;
        if (dealStatusText != null && activeTab == LedgerTab.Subjects) dealStatusText.text = message;
        UIManager.Instance?.ShowNotification(message);
        if (result.success)
        {
            SetDialogue(successMessage);
            RefreshLeaderPresentation();
        }
        RefreshSubjectSummary();
        RefreshActionButtons();
    }
}
