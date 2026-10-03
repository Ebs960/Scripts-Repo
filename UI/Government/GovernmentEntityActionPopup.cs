using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared, authored quick-action surface for an overview governor or vassal row.</summary>
public class GovernmentEntityActionPopup : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private GameObject governorRoot;
    [SerializeField] private GameObject vassalRoot;
    [SerializeField] private Button closeButton;

    [Header("Governor")]
    [SerializeField] private Image governorPortrait;
    [SerializeField] private TMP_Text governorName;
    [SerializeField] private TMP_Text governorOpinion;
    [SerializeField] private TMP_Text governorHoldings;
    [SerializeField] private TMP_Text governorTraits;
    [SerializeField] private TMP_Text governorReligion;
    [SerializeField] private TMP_Text governorFaction;
    [SerializeField] private TMP_Text governorStatus;
    [SerializeField] private Button manageHoldingsButton;
    [SerializeField] private Button councilButton;
    [SerializeField] private TMP_Text councilButtonLabel;
    [SerializeField] private Button giftButton;
    [SerializeField] private TMP_Text giftButtonLabel;
    [SerializeField] private Button convertButton;
    [SerializeField] private Button governorDetailsButton;

    [Header("Vassal")]
    [SerializeField] private Image vassalIcon;
    [SerializeField] private TMP_Text vassalName;
    [SerializeField] private TMP_Text vassalOpinion;
    [SerializeField] private TMP_Text vassalLiberty;
    [SerializeField] private TMP_Text vassalCities;
    [SerializeField] private TMP_Text vassalAutonomy;
    [SerializeField] private TMP_Text vassalTribute;
    [SerializeField] private TMP_Text vassalReligion;
    [SerializeField] private TMP_Text vassalCooldown;
    [SerializeField] private TMP_Text vassalStatus;
    [SerializeField] private Button manageContractButton;
    [SerializeField] private Button replaceGovernorButton;
    [SerializeField] private Button imposeReligionButton;
    [SerializeField] private Button releaseVassalButton;
    [SerializeField] private GameObject independenceDemandRoot;
    [SerializeField] private TMP_Text independenceDemandText;
    [SerializeField] private Button acceptIndependenceButton;
    [SerializeField] private Button rejectIndependenceButton;

    [Header("Replace Governor")]
    [SerializeField] private GameObject replaceGovernorRoot;
    [SerializeField] private TMP_Dropdown replaceCityDropdown;
    [SerializeField] private TMP_Dropdown replacementGovernorDropdown;
    [SerializeField] private Button confirmReplacementButton;
    [SerializeField] private Button cancelReplacementButton;
    [SerializeField] private TMP_Text replacementStatus;

    private Civilization civ;
    private Governor governor;
    private VassalContract contract;
    private GovernmentPanel panel;
    private readonly List<City> replacementCities = new List<City>();
    private readonly List<Governor> replacementGovernors = new List<Governor>();
    private bool includesNewGovernorOption;
    private bool wired;

    private GameObject Root => root != null ? root : gameObject;

    private void Awake() => EnsureWired();

    public void ShowGovernor(Civilization civilization, Governor target, GovernmentPanel governmentPanel)
    {
        if (civilization == null || target == null) return;
        EnsureWired();
        civ = civilization; governor = target; contract = null; panel = governmentPanel;
        GovernmentUiUtil.SetActive(governorRoot, true);
        GovernmentUiUtil.SetActive(vassalRoot, false);
        GovernmentUiUtil.SetActive(replaceGovernorRoot, false);
        Root.SetActive(true);
        RefreshGovernor();
    }

    public void ShowVassal(Civilization overlord, VassalContract target, GovernmentPanel governmentPanel)
    {
        if (overlord == null || target == null) return;
        EnsureWired();
        civ = overlord; governor = null; contract = target; panel = governmentPanel;
        GovernmentUiUtil.SetActive(governorRoot, false);
        GovernmentUiUtil.SetActive(vassalRoot, true);
        GovernmentUiUtil.SetActive(replaceGovernorRoot, false);
        Root.SetActive(true);
        RefreshVassal();
    }

    public void Hide()
    {
        GovernmentUiUtil.SetActive(replaceGovernorRoot, false);
        Root.SetActive(false);
    }

    private void EnsureWired()
    {
        if (wired) return;
        wired = true;
        GovernmentUiUtil.SetClick(closeButton, Hide);
        GovernmentUiUtil.SetClick(manageHoldingsButton, ManageHoldings);
        GovernmentUiUtil.SetClick(councilButton, ChangeCouncilSeat);
        GovernmentUiUtil.SetClick(giftButton, SendGift);
        GovernmentUiUtil.SetClick(convertButton, RequestConversion);
        GovernmentUiUtil.SetClick(governorDetailsButton, OpenGovernorDetails);
        GovernmentUiUtil.SetClick(manageContractButton, OpenVassalDetails);
        GovernmentUiUtil.SetClick(replaceGovernorButton, OpenReplacement);
        GovernmentUiUtil.SetClick(imposeReligionButton, ImposeReligion);
        GovernmentUiUtil.SetClick(releaseVassalButton, ReleaseVassal);
        GovernmentUiUtil.SetClick(confirmReplacementButton, ConfirmReplacement);
        GovernmentUiUtil.SetClick(cancelReplacementButton, () => GovernmentUiUtil.SetActive(replaceGovernorRoot, false));
        GovernmentUiUtil.SetClick(acceptIndependenceButton, AcceptDemand);
        GovernmentUiUtil.SetClick(rejectIndependenceButton, RejectDemand);
        if (replaceCityDropdown != null) replaceCityDropdown.onValueChanged.AddListener(_ => PopulateReplacementGovernors());
    }

    private void RefreshGovernor()
    {
        if (civ == null || governor == null) return;
        GovernmentUiUtil.SetImage(governorPortrait, GovernorPortraitService.GetSprite(governor.PortraitId));
        GovernmentUiUtil.SetText(governorName, GovernmentPresentation.FormatGovernorName(civ, governor));
        GovernmentUiUtil.SetText(governorOpinion, $"Loyalty {GovernmentUiUtil.Signed(governor.Opinion)}");
        GovernmentUiUtil.SetText(governorHoldings, $"{governor.Cities.Count} Cities • Power {governor.PowerRank}");
        GovernmentUiUtil.SetText(governorTraits, string.Join(" • ", governor.PersonalityTraits));
        GovernmentUiUtil.SetText(governorReligion, governor.PersonalReligion != null ? $"Faith: {GovernmentPresentation.NameOf(governor.PersonalReligion)}" : "Faith: none");
        GovernmentUiUtil.SetText(governorFaction, governor.Faction != null ? $"Faction: {governor.Faction.FactionName}" : "Faction: Unaffiliated");
        string institution = GovernmentPresentation.GetInstitutionName(civ);
        GovernmentUiUtil.SetText(councilButtonLabel, governor.IsOnCouncil ? $"Remove From {institution}" : $"Grant {institution} Seat");
        string councilReason;
        bool councilAllowed = governor.IsOnCouncil
            ? PoliticalActionRules.CanRemoveFromCouncil(civ, governor, out councilReason)
            : PoliticalActionRules.CanGrantCouncilSeat(civ, governor, out councilReason);
        bool giftAllowed = PoliticalActionRules.CanSendGovernorGift(civ, governor, out string giftReason);
        bool conversionAllowed = PoliticalActionRules.CanRequestGovernorConversion(civ, governor, out string conversionReason);
        GovernmentUiUtil.SetInteractable(councilButton, councilAllowed);
        GovernmentUiUtil.SetText(giftButtonLabel, $"Send Gift — {PoliticalActionRules.GovernorGiftCost} Gold");
        GovernmentUiUtil.SetInteractable(giftButton, giftAllowed);
        GovernmentUiUtil.SetInteractable(convertButton, conversionAllowed);
        string status = governor.IsInRebellion ? "WARNING: In open rebellion." : null;
        if (string.IsNullOrEmpty(status) && !councilAllowed) status = councilReason;
        if (string.IsNullOrEmpty(status) && !giftAllowed) status = giftReason;
        if (string.IsNullOrEmpty(status) && !conversionAllowed) status = conversionReason;
        GovernmentUiUtil.SetText(governorStatus, status ?? string.Empty);
    }

    private void RefreshVassal()
    {
        var manager = SubjectManager.Instance;
        if (contract == null || manager == null) return;
        var subject = contract.subject;
        GovernmentUiUtil.SetImage(vassalIcon, subject?.civData?.icon);
        GovernmentUiUtil.SetText(vassalName, contract.subjectCivName);
        GovernmentUiUtil.SetText(vassalOpinion, $"Opinion {GovernmentUiUtil.Signed(manager.GetEffectiveSubjectOpinion(contract))}");
        GovernmentUiUtil.SetText(vassalLiberty, $"Liberty {contract.libertyDesire:0} / {contract.EffectiveBreakawayThreshold:0}");
        GovernmentUiUtil.SetText(vassalCities, $"Cities: {subject?.cities?.Count ?? 0}");
        GovernmentUiUtil.SetText(vassalAutonomy, $"Autonomy: {contract.autonomyLevel}");
        GovernmentUiUtil.SetText(vassalTribute, $"Tribute G {contract.goldTributePct:P0} • S {contract.scienceTributePct:P0} • F {contract.foodTributePct:P0}");
        GovernmentUiUtil.SetText(vassalReligion, subject?.StateReligion != null ? $"Faith: {GovernmentPresentation.NameOf(subject.StateReligion)}" : "Faith: none");
        var interference = manager.CanInterfere(civ, subject, manager.CurrentTurn);
        GovernmentUiUtil.SetText(vassalCooldown, interference.success ? "Interference available." : interference.reason);
        GovernmentUiUtil.SetInteractable(replaceGovernorButton, interference.success && subject?.cities?.Count > 0);
        GovernmentUiUtil.SetInteractable(imposeReligionButton, interference.success && civ.StateReligion != null);
        GovernmentUiUtil.SetText(vassalStatus, string.Empty);
        var demand = manager.GetPendingIndependenceDemand(civ, subject);
        GovernmentUiUtil.SetActive(independenceDemandRoot, demand != null);
        if (demand != null) GovernmentUiUtil.SetText(independenceDemandText, $"{contract.subjectCivName} demands full independence (since turn {demand.turnIssued}).");
    }

    private void ManageHoldings() => panel?.ShowGovernorHoldings(governor);

    private void ChangeCouncilSeat()
    {
        if (governor == null) return;
        if (!governor.IsOnCouncil)
        {
            if (PoliticalActionRules.CanGrantCouncilSeat(civ, governor, out string reason) && civ.AddToCouncil(governor)) RefreshAfterAction();
            else GovernmentUiUtil.SetText(governorStatus, reason);
            return;
        }
        if (!PoliticalActionRules.CanRemoveFromCouncil(civ, governor, out string removeReason)) { GovernmentUiUtil.SetText(governorStatus, removeReason); return; }
        string institution = GovernmentPresentation.GetInstitutionName(civ);
        Confirm($"Remove {GovernmentPresentation.FormatGovernorName(civ, governor)} from the {institution}?",
            "Stripping a seat angers the governor and creates a lasting grievance.", "Remove", () => { civ.RemoveFromCouncil(governor); RefreshAfterAction(); });
    }

    private void SendGift()
    {
        if (!PoliticalActionRules.CanSendGovernorGift(civ, governor, out string reason)) { GovernmentUiUtil.SetText(governorStatus, reason); return; }
        civ.AddGold(-PoliticalActionRules.GovernorGiftCost);
        governor.AddOpinionModifier("Received Gift", 15f, 15);
        governor.ClearGrievance(GrievanceSource.PublicInsult);
        UIManager.Instance?.ShowNotification($"Gifts were sent to {GovernmentPresentation.FormatGovernorName(civ, governor)}.");
        RefreshAfterAction();
    }

    private void RequestConversion()
    {
        if (!PoliticalActionRules.CanRequestGovernorConversion(civ, governor, out string reason)) { GovernmentUiUtil.SetText(governorStatus, reason); return; }
        if (governor.TryConvertReligion(civ.StateReligion, false, out string failure))
        {
            UIManager.Instance?.ShowNotification($"{GovernmentPresentation.FormatGovernorName(civ, governor)} converted to the state religion.");
            RefreshAfterAction();
        }
        else GovernmentUiUtil.SetText(governorStatus, failure);
    }

    private void OpenGovernorDetails() { Hide(); panel?.ShowGovernor(governor); }
    private void OpenVassalDetails() { Hide(); panel?.ShowVassal(contract); }

    private void OpenReplacement()
    {
        replacementCities.Clear();
        replacementCities.AddRange(contract?.subject?.cities?.Where(city => city != null && city.owner == contract.subject) ?? Enumerable.Empty<City>());
        replaceCityDropdown?.ClearOptions();
        replaceCityDropdown?.AddOptions(replacementCities.Select(city => city.cityName).ToList());
        if (replaceCityDropdown != null) replaceCityDropdown.SetValueWithoutNotify(0);
        PopulateReplacementGovernors();
        GovernmentUiUtil.SetText(replacementStatus, replacementCities.Count == 0 ? "This subject has no eligible city." : string.Empty);
        GovernmentUiUtil.SetActive(replaceGovernorRoot, true);
    }

    private void PopulateReplacementGovernors()
    {
        replacementGovernors.Clear();
        var city = SelectedReplacementCity();
        if (city != null)
            replacementGovernors.AddRange(contract.subject.governors.Where(g => g != null && g != city.governor));
        var labels = replacementGovernors.Select(g => GovernmentPresentation.FormatGovernorName(contract.subject, g)).ToList();
        includesNewGovernorOption = PoliticalActionRules.CanCreateGovernor(contract?.subject, out _);
        if (includesNewGovernorOption) labels.Add("Appoint New Governor");
        replacementGovernorDropdown?.ClearOptions();
        replacementGovernorDropdown?.AddOptions(labels);
        GovernmentUiUtil.SetInteractable(confirmReplacementButton, city != null && labels.Count > 0);
    }

    private City SelectedReplacementCity() => replaceCityDropdown != null && replaceCityDropdown.value >= 0 && replaceCityDropdown.value < replacementCities.Count ? replacementCities[replaceCityDropdown.value] : null;

    private void ConfirmReplacement()
    {
        var city = SelectedReplacementCity();
        int index = replacementGovernorDropdown != null ? replacementGovernorDropdown.value : -1;
        Governor replacement = index >= 0 && index < replacementGovernors.Count ? replacementGovernors[index] : null;
        bool appoint = includesNewGovernorOption && index == replacementGovernors.Count;
        if (city == null || (replacement == null && !appoint)) return;
        string oldName = city.governor != null ? GovernmentPresentation.FormatGovernorName(contract.subject, city.governor) : "None";
        string newName = appoint ? "Appoint New Governor" : GovernmentPresentation.FormatGovernorName(contract.subject, replacement);
        Confirm("Replace subject governor?",
            $"City: {city.cityName}\nCurrent: {oldName}\nReplacement: {newName}\nSubject resentment +20; local governors become angry; the interference cooldown begins.",
            "Replace", () => Report(SubjectManager.Instance.InterfereReplaceGovernor(civ, contract.subject, city, replacement, appoint, SubjectManager.Instance.CurrentTurn), "Governor replaced.", true));
    }

    private void ImposeReligion()
    {
        Confirm("Impose state religion?", "Forces your state religion, increases resentment, angers subject governors (especially zealous governors), and begins the interference cooldown.",
            "Impose", () => Report(SubjectManager.Instance.InterfereForceReligion(civ, contract.subject, SubjectManager.Instance.CurrentTurn), "State religion imposed."));
    }

    private void ReleaseVassal()
    {
        Confirm($"Release {contract.subjectCivName}?", "The vassal contract and tribute end; the subject becomes independent.", "Release", () =>
        {
            var result = SubjectManager.Instance.TryReleaseSubject(civ, contract.subject, SubjectManager.Instance.CurrentTurn);
            if (result.success) { Hide(); panel?.RefreshAllVisible(); } else GovernmentUiUtil.SetText(vassalStatus, result.reason);
        });
    }

    private void AcceptDemand() => ResolveDemand(true);
    private void RejectDemand() => ResolveDemand(false);
    private void ResolveDemand(bool accept)
    {
        var demand = SubjectManager.Instance?.GetPendingIndependenceDemand(civ, contract?.subject);
        if (demand == null) return;
        Confirm(accept ? "Grant Independence" : "Reject Independence Demand",
            accept ? "The vassal contract ends peacefully and tribute ends." : "War of Independence begins immediately.",
            accept ? "Grant" : "Reject", () =>
            {
                bool success = accept ? SubjectManager.Instance.AcceptIndependenceDemand(demand, SubjectManager.Instance.CurrentTurn) : SubjectManager.Instance.RejectIndependenceDemand(demand);
                if (success) { Hide(); panel?.RefreshAllVisible(); } else GovernmentUiUtil.SetText(vassalStatus, "The demand could not be resolved.");
            });
    }

    private void Confirm(string title, string description, string label, Action action)
    {
        panel?.RequestConfirmation(new PoliticalConfirmRequest { title = title, description = description, confirmLabel = label, onConfirm = action });
    }

    private void Report(SubjectActionResult result, string success, bool closeSubview = false)
    {
        if (!result.success) { GovernmentUiUtil.SetText(vassalStatus, result.reason); GovernmentUiUtil.SetText(replacementStatus, result.reason); return; }
        UIManager.Instance?.ShowNotification(success);
        if (closeSubview) GovernmentUiUtil.SetActive(replaceGovernorRoot, false);
        panel?.RefreshAllVisible();
        RefreshVassal();
    }

    private void RefreshAfterAction() { panel?.RefreshAllVisible(); RefreshGovernor(); }
}
