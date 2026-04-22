using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

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
    public TMP_InputField goldInputField;
    public GameObject goldInputPanel;

    [Header("Action Buttons")]
    public Button proposeDealButton;
    public Button acceptDealButton;
    public Button rejectDealButton;
    public Button offerVassalageButton;
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
    private LeaderMood currentMood = LeaderMood.Idle;

    /// <summary>True when the AI has sent a proposal to the player (accept/reject mode).</summary>
    private bool isIncomingProposal;

    // ────────────────────────── Lifecycle ──────────────────────────
    void Start()
    {
        SetupButtonListeners();
        gameObject.SetActive(false);
    }

    private void SetupButtonListeners()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }

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
        if (declareWarButton != null)    declareWarButton.onClick.AddListener(OnDeclareWarClicked);
        if (makePeaceButton != null)     makePeaceButton.onClick.AddListener(OnMakePeaceClicked);
        if (cancelButton != null)        cancelButton.onClick.AddListener(Hide);

        // Item picker close
        if (itemPickerCloseButton != null) itemPickerCloseButton.onClick.AddListener(CloseItemPicker);
    }

    // ────────────────────────── Public API ──────────────────────────

    /// <summary>Open the diplomacy screen from the player's perspective.</summary>
    public void Show(Civilization playerCiv)
    {
        this.playerCiv = playerCiv;
        isIncomingProposal = false;
        currentOffer = new DiplomaticOffer { proposer = playerCiv };
        gameObject.SetActive(true);
        UpdateCivilizationList();
        ClearNegotiationPanel();
        CloseItemPicker();
    }

    /// <summary>
    /// Open the diplomacy screen with a specific civ pre-selected 
    /// and an incoming AI proposal to accept/reject.
    /// </summary>
    public void ShowIncomingProposal(Civilization playerCiv, DiplomaticOffer offer)
    {
        this.playerCiv = playerCiv;
        this.selectedCiv = offer.proposer;
        isIncomingProposal = true;
        currentOffer = offer;
        gameObject.SetActive(true);
        UpdateCivilizationList();
        RefreshLeaderPresentation();
        RefreshDealTable();
        RefreshActionButtons();
        SetLeaderMood(LeaderMood.Speaking);
        SetDialogue($"{selectedCiv.leader.leaderName} has a proposal for you.");
        CloseItemPicker();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        selectedCiv = null;
        currentOffer = null;
        CloseItemPicker();
    }

    // ────────────────────────── Civ List ──────────────────────────

    private void UpdateCivilizationList()
    {
        foreach (var item in civListItems) Destroy(item);
        civListItems.Clear();

        if (CivilizationManager.Instance == null || civListItemPrefab == null || civListContainer == null) return;

        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
        {
            if (civ == playerCiv) continue;
            var listItem = Instantiate(civListItemPrefab, civListContainer);
            var button = listItem.GetComponent<Button>();
            var icon = listItem.GetComponentInChildren<Image>();
            var text = listItem.GetComponentInChildren<TextMeshProUGUI>();

            if (icon != null && civ.civData != null && civ.civData.icon != null)
                icon.sprite = civ.civData.icon;
            if (text != null)
                text.text = civ.civData != null ? civ.civData.civName : "???";

            var captured = civ;
            if (button != null) button.onClick.AddListener(() => OnCivSelected(captured));
            civListItems.Add(listItem);
        }
    }

    private void OnCivSelected(Civilization civ)
    {
        selectedCiv = civ;
        isIncomingProposal = false;
        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        RefreshLeaderPresentation();
        RefreshDealTable();
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

        UpdateLeaderVisual(leader, currentMood);

        // Name plates
        if (leaderNameText != null)   leaderNameText.text = leader.leaderName;
        if (leaderCivNameText != null) leaderCivNameText.text = selectedCiv.civData != null ? selectedCiv.civData.civName : "";

        // Relationship info
        if (relationshipStatusText != null)
        {
            var rel = DiplomacyManager.Instance.GetRelationship(playerCiv, selectedCiv);
            relationshipStatusText.text = $"Status: {rel}";
        }
        if (reputationText != null || trustText != null)
        {
            var memory = DiplomacyManager.Instance.GetDiplomaticMemory(selectedCiv);
            if (reputationText != null)
                reputationText.text = $"Reputation: {memory.GetReputation(playerCiv)}";
            if (trustText != null)
                trustText.text = $"Trust: {memory.GetTrustLevel(playerCiv)}/10";
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
            faithStatusText.text = $"Religion: {(selectedCiv.hasFoundedReligion ? selectedCiv.foundedReligion.religionName : "None")}";
        if (governmentText != null)
            governmentText.text = $"Government: {selectedCiv.currentGovernment?.governmentName ?? "None"}";
    }

    // ────────────────────────── Leader Mood & Dialogue ──────────────────────────

    public void SetLeaderMood(LeaderMood mood)
    {
        currentMood = mood;
        if (selectedCiv == null || selectedCiv.leader == null) return;
        UpdateLeaderVisual(selectedCiv.leader, mood);
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
        string name = civ.leader != null ? civ.leader.leaderName : "Leader";
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

        // "Our offer" = what the proposer gives
        var ourItems = isIncomingProposal ? currentOffer.recipientItems : currentOffer.proposerItems;
        var theirItems = isIncomingProposal ? currentOffer.proposerItems : currentOffer.recipientItems;

        PopulateDealColumn(ourItems, ourOfferContainer, ourOfferItems, !isIncomingProposal);
        PopulateDealColumn(theirItems, theirOfferContainer, theirOfferItems, !isIncomingProposal);
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
    }

    // ────────────────────────── Action Buttons ──────────────────────────

    private void RefreshActionButtons()
    {
        bool hasCiv = selectedCiv != null;
        var relation = hasCiv
            ? DiplomacyManager.Instance.GetRelationship(playerCiv, selectedCiv)
            : DiplomaticState.Peace;
        bool atWar = relation == DiplomaticState.War;
        bool alreadyVassal = relation == DiplomaticState.Vassal;

        // Proposal flow
        if (proposeDealButton != null)
            proposeDealButton.gameObject.SetActive(hasCiv && !isIncomingProposal);
        if (acceptDealButton != null)
            acceptDealButton.gameObject.SetActive(hasCiv && isIncomingProposal);
        if (rejectDealButton != null)
            rejectDealButton.gameObject.SetActive(hasCiv && isIncomingProposal);
        if (offerVassalageButton != null)
            offerVassalageButton.gameObject.SetActive(hasCiv && !isIncomingProposal && !atWar && !alreadyVassal);

        // War / Peace
        if (declareWarButton != null)
            declareWarButton.gameObject.SetActive(hasCiv && !atWar);
        if (makePeaceButton != null)
            makePeaceButton.gameObject.SetActive(hasCiv && atWar);

        // Cancel always visible when a civ is selected
        if (cancelButton != null)
            cancelButton.gameObject.SetActive(hasCiv);
    }

    // ────────────────────────── Item Pickers ──────────────────────────

    /// <param name="isOurSide">True = adding to our offer; false = adding to our demands.</param>
    private void OpenGoldInput(bool isOurSide)
    {
        if (selectedCiv == null) return;
        if (goldInputPanel != null) goldInputPanel.SetActive(true);
        if (itemPickerPanel != null) itemPickerPanel.SetActive(true);
        if (itemPickerContainer != null) itemPickerContainer.gameObject.SetActive(false);
        if (itemPickerTitleText != null)
            itemPickerTitleText.text = isOurSide ? "Offer Gold" : "Demand Gold";

        if (goldInputField != null)
        {
            goldInputField.text = "";
            goldInputField.onEndEdit.RemoveAllListeners();
            goldInputField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int amount) && amount > 0)
                {
                    var di = new DealItem { itemType = DealItemType.Gold, goldAmount = amount };
                    AddDealItem(di, isOurSide);
                }
                CloseItemPicker();
            });
        }
    }

    private void OpenResourcePicker(bool isOurSide)
    {
        if (selectedCiv == null) return;
        OpenItemPicker(isOurSide ? "Offer Resource" : "Demand Resource");
        if (goldInputPanel != null) goldInputPanel.SetActive(false);

        // List resources owned by the appropriate civ
        var sourceCiv = isOurSide ? playerCiv : selectedCiv;
        if (sourceCiv.resourceStockpile == null) return;

        foreach (var kvp in sourceCiv.resourceStockpile)
        {
            if (kvp.Value <= 0) continue;
            var res = kvp.Key;
            var row = Instantiate(itemPickerRowPrefab, itemPickerContainer);
            var text = row.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = $"{res.resourceName} (x{kvp.Value})";
            var icon = row.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null && res.icon != null) { icon.sprite = res.icon; icon.gameObject.SetActive(true); }

            var capturedRes = res;
            var btn = row.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() =>
            {
                AddDealItem(new DealItem { itemType = DealItemType.Resource, resource = capturedRes, resourceAmount = 1 }, isOurSide);
                CloseItemPicker();
            });
            pickerItems.Add(row);
        }
    }

    private void OpenTechPicker(bool isOurSide)
    {
        if (selectedCiv == null) return;
        OpenItemPicker(isOurSide ? "Offer Technology" : "Demand Technology");
        if (goldInputPanel != null) goldInputPanel.SetActive(false);

        // Techs that the source civ has but the other doesn't
        var sourceCiv = isOurSide ? playerCiv : selectedCiv;
        var targetCiv = isOurSide ? selectedCiv : playerCiv;

        foreach (var tech in sourceCiv.researchedTechs)
        {
            if (targetCiv.researchedTechs.Contains(tech)) continue; // They already have it
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
        if (selectedCiv == null) return;
        OpenItemPicker(isOurSide ? "Offer City" : "Demand City");
        if (goldInputPanel != null) goldInputPanel.SetActive(false);

        var sourceCiv = isOurSide ? playerCiv : selectedCiv;

        foreach (var city in sourceCiv.cities)
        {
            if (city.isCapital) continue; // Can't trade capitals
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
        if (itemPickerTitleText != null) itemPickerTitleText.text = title;
    }

    private void CloseItemPicker()
    {
        ClearPickerItems();
        if (itemPickerPanel != null) itemPickerPanel.SetActive(false);
        if (goldInputPanel != null) goldInputPanel.SetActive(false);
    }

    private void ClearPickerItems()
    {
        foreach (var go in pickerItems) Destroy(go);
        pickerItems.Clear();
    }

    private void AddDealItem(DealItem item, bool isOurSide)
    {
        if (currentOffer == null) return;
        if (isOurSide)
            currentOffer.proposerItems.Add(item);
        else
            currentOffer.recipientItems.Add(item);
        RefreshDealTable();
    }

    // ────────────────────────── Action Handlers ──────────────────────────

    private void OnProposeDealClicked()
    {
        if (selectedCiv == null || currentOffer == null) return;
        currentOffer.recipient = selectedCiv;

        SetLeaderMood(LeaderMood.Speaking);
        SetDialogue("Hmm… let me consider your proposal.");

        // Ask DiplomacyManager to evaluate
        bool accepted = DiplomacyManager.Instance.EvaluateComplexDeal(selectedCiv, currentOffer);

        if (accepted)
        {
            DiplomacyManager.Instance.ExecuteComplexDeal(currentOffer);
            SetLeaderMood(LeaderMood.Agreement);
            SetDialogue($"{selectedCiv.leader.leaderName} accepts your deal!");
            UIManager.Instance.ShowNotification($"Deal accepted by {selectedCiv.civData.civName}!");
        }
        else
        {
            SetLeaderMood(LeaderMood.Angry);
            SetDialogue($"{selectedCiv.leader.leaderName} rejects your proposal.");
            UIManager.Instance.ShowNotification($"Deal rejected by {selectedCiv.civData.civName}.");
        }

        // Reset deal table after a short pause (player can see reaction)
        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        RefreshDealTable();
        RefreshActionButtons();
        RefreshLeaderPresentation();
    }

    private void OnAcceptDealClicked()
    {
        if (currentOffer == null) return;
        DiplomacyManager.Instance.ExecuteComplexDeal(currentOffer);
        SetLeaderMood(LeaderMood.Agreement);
        SetDialogue("A wise decision.");
        UIManager.Instance.ShowNotification($"Deal accepted with {selectedCiv.civData.civName}!");
        isIncomingProposal = false;
        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        RefreshDealTable();
        RefreshActionButtons();
        RefreshLeaderPresentation();
    }

    private void OnRejectDealClicked()
    {
        if (currentOffer == null) return;
        SetLeaderMood(LeaderMood.Angry);
        SetDialogue("You will regret this.");
        // Record refusal in diplomatic memory
        var memory = DiplomacyManager.Instance.GetDiplomaticMemory(selectedCiv);
        memory.RecordEvent(playerCiv, DiplomaticEventType.RefusedTrade);
        UIManager.Instance.ShowNotification($"Deal rejected from {selectedCiv.civData.civName}.");
        isIncomingProposal = false;
        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        RefreshDealTable();
        RefreshActionButtons();
    }

    private void OnDeclareWarClicked()
    {
        if (selectedCiv == null) return;
        SetLeaderMood(LeaderMood.DeclareWar);
        SetDialogue($"So be it! {selectedCiv.leader.leaderName} prepares for war!");

        DiplomacyManager.Instance.SetState(playerCiv, selectedCiv, DiplomaticState.War);
        UIManager.Instance.ShowNotification($"War declared against {selectedCiv.civData.civName}!");

        RefreshLeaderPresentation();
        RefreshActionButtons();
    }

    private void OnOfferVassalageClicked()
    {
        if (selectedCiv == null) return;

        DiplomacyManager.Instance.ProposeDeal(playerCiv, selectedCiv, DealType.Vassal);

        var relation = DiplomacyManager.Instance.GetRelationship(playerCiv, selectedCiv);
        if (relation == DiplomaticState.Vassal)
        {
            SetLeaderMood(LeaderMood.Agreement);
            SetDialogue($"{selectedCiv.leader.leaderName} submits to your rule.");
            UIManager.Instance.ShowNotification($"{selectedCiv.civData?.civName ?? selectedCiv.name} accepted vassalage.");
        }
        else
        {
            SetLeaderMood(LeaderMood.Angry);
            SetDialogue($"{selectedCiv.leader.leaderName} rejects your demand for submission.");
            UIManager.Instance.ShowNotification($"{selectedCiv.civData?.civName ?? selectedCiv.name} refused vassalage.");
        }

        RefreshLeaderPresentation();
        RefreshActionButtons();
    }

    private void OnMakePeaceClicked()
    {
        if (selectedCiv == null) return;

        // Add peace as a deal item and propose
        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        currentOffer.proposerItems.Add(new DealItem { itemType = DealItemType.MakePeace });
        currentOffer.recipientItems.Add(new DealItem { itemType = DealItemType.MakePeace });

        bool accepted = DiplomacyManager.Instance.EvaluateComplexDeal(selectedCiv, currentOffer);
        if (accepted)
        {
            DiplomacyManager.Instance.ExecuteComplexDeal(currentOffer);
            SetLeaderMood(LeaderMood.Agreement);
            SetDialogue("Peace at last. Let us rebuild.");
            UIManager.Instance.ShowNotification($"Peace established with {selectedCiv.civData.civName}!");
        }
        else
        {
            SetLeaderMood(LeaderMood.Angry);
            SetDialogue("There will be no peace. Not yet.");
            UIManager.Instance.ShowNotification($"{selectedCiv.civData.civName} refuses peace.");
        }

        currentOffer = new DiplomaticOffer { proposer = playerCiv, recipient = selectedCiv };
        RefreshDealTable();
        RefreshActionButtons();
        RefreshLeaderPresentation();
    }
} 