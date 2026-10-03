using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Governor roster and detail view. Titles come from the current government; council actions use
/// PoliticalActionRules so button state and disabled reasons share one source of truth.
/// </summary>
public class GovernmentGovernorsUI : GovernmentScreenBase
{
    [Header("Roster")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GovernorRowUI rowPrefab;
    [SerializeField] private ScrollRect listScroll;
    [SerializeField] private TMP_Text rosterHeaderText;
    [SerializeField] private TMP_Text emptyText;

    [Header("Create")]
    [SerializeField] private GameObject createRoot;
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private TMP_Dropdown specializationDropdown;
    [SerializeField] private Button createButton;
    [SerializeField] private TMP_Text createButtonLabel;
    [SerializeField] private TMP_Text createStatusText;

    [Header("Detail")]
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text specializationText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text opinionText;
    [SerializeField] private TMP_Text ambitionText;
    [SerializeField] private TMP_Text powerText;
    [SerializeField] private TMP_Text personalityText;
    [SerializeField] private TMP_Text traitsText;
    [SerializeField] private TMP_Text religionText;
    [SerializeField] private TMP_Text cultureText;
    [SerializeField] private TMP_Text factionText;
    [SerializeField] private TMP_Text councilStatusText;
    [SerializeField] private TMP_Text rebellionText;
    [SerializeField] private TMP_Text grievancesText;

    [Header("Political Status")]
    [SerializeField] private PoliticalLineRowUI lineRowPrefab;
    [SerializeField] private Transform opinionModifiersRoot;

    [Header("Holdings")]
    [SerializeField] private ScrollRect citiesScroll;
    [SerializeField] private Transform citiesRoot;
    [SerializeField] private ScrollRect herdsScroll;
    [SerializeField] private Transform herdsRoot;
    [SerializeField] private GovernorHoldingRowUI holdingRowPrefab;

    [Header("Council actions")]
    [SerializeField] private GameObject councilActionsRoot;
    [SerializeField] private Button grantSeatButton;
    [SerializeField] private TMP_Text grantSeatLabel;
    [SerializeField] private Button removeSeatButton;
    [SerializeField] private TMP_Text removeSeatLabel;
    [SerializeField] private TMP_Text councilReasonText;

    [Header("Personal Actions")]
    [SerializeField] private Button giftButton;
    [SerializeField] private TMP_Text giftButtonLabel;
    [SerializeField] private Button convertButton;
    [SerializeField] private TMP_Text convertButtonLabel;
    [SerializeField] private TMP_Text personalActionStatusText;

    [Header("Suppressed politics")]
    [SerializeField] private GameObject suppressedNoticeRoot;
    [SerializeField] private TMP_Text suppressedNoticeText;

    private readonly List<GovernorRowUI> rows = new List<GovernorRowUI>();
    private readonly List<GovernorHoldingRowUI> cityHoldingRows = new List<GovernorHoldingRowUI>();
    private readonly List<GovernorHoldingRowUI> herdHoldingRows = new List<GovernorHoldingRowUI>();
    private PoliticalLineList opinionModifiers;
    private int selectedGovernorId = -1;
    private bool hasRequestedFocus;
    private bool resetHoldingScroll;
    private bool wired;

    protected override void OnCivilizationChanged()
    {
        if (!hasRequestedFocus || civ == null || !civ.governors.Any(g => g != null && g.Id == selectedGovernorId))
            selectedGovernorId = -1;
        hasRequestedFocus = false;
    }

    public void FocusGovernor(Governor governor)
    {
        if (governor == null) return;
        selectedGovernorId = governor.Id;
        hasRequestedFocus = true;
        resetHoldingScroll = true;
        if (civ != null && gameObject.activeInHierarchy) Refresh();
    }

    public override void Refresh()
    {
        if (civ == null) return;
        EnsureWired();

        string plural = GovernmentPresentation.GetGovernorTitlePlural(civ);
        GovernmentUiUtil.SetText(rosterHeaderText, $"{plural} ({GovernmentPresentation.FormatGovernorCap(civ)})");

        var governors = civ.governors.Where(g => g != null).ToList();
        var selected = governors.FirstOrDefault(g => g.Id == selectedGovernorId) ?? governors.FirstOrDefault();
        selectedGovernorId = selected != null ? selected.Id : -1;
        hasRequestedFocus = false;

        float scroll = GovernmentUiUtil.CaptureScroll(listScroll);
        GovernmentUiUtil.FillList(listRoot, rowPrefab, rows, governors,
            (row, governor) => row.Bind(civ, governor, governor == selected, SelectGovernor));
        GovernmentUiUtil.RestoreScroll(listScroll, scroll);
        GovernmentUiUtil.SetText(emptyText, governors.Count == 0
            ? (civ.governorsEnabled ? $"No {plural.ToLowerInvariant()} have been appointed." : $"{plural} have not been unlocked yet.")
            : string.Empty);

        bool suppressed = civ.currentGovernment != null && civ.currentGovernment.suppressConventionalPolitics;
        GovernmentUiUtil.SetActive(suppressedNoticeRoot, suppressed);
        if (suppressed)
            GovernmentUiUtil.SetText(suppressedNoticeText, $"{GovernmentPresentation.NameOf(civ.currentGovernment)} suppresses conventional politics; factions and councils are inactive.");

        RefreshCreate();
        RefreshDetail(selected);
        if (resetHoldingScroll)
        {
            ResetHoldingScrolls();
            resetHoldingScroll = false;
        }
    }

    private void EnsureWired()
    {
        if (wired) return;
        wired = true;

        if (specializationDropdown != null)
        {
            specializationDropdown.ClearOptions();
            specializationDropdown.AddOptions(Enum.GetNames(typeof(Governor.Specialization)).ToList());
            specializationDropdown.SetValueWithoutNotify(0);
        }
        GovernmentUiUtil.SetClick(createButton, OnCreateClicked);
        GovernmentUiUtil.SetClick(grantSeatButton, OnGrantSeatClicked);
        GovernmentUiUtil.SetClick(removeSeatButton, OnRemoveSeatClicked);
        GovernmentUiUtil.SetClick(giftButton, OnGiftClicked);
        GovernmentUiUtil.SetClick(convertButton, OnConvertClicked);
    }

    private Governor SelectedGovernor() => civ?.governors.FirstOrDefault(g => g != null && g.Id == selectedGovernorId);

    private void SelectGovernor(Governor governor)
    {
        bool changed = governor != null && governor.Id != selectedGovernorId;
        selectedGovernorId = governor != null ? governor.Id : -1;
        resetHoldingScroll = changed;
        Refresh();
    }

    private void RefreshCreate()
    {
        GovernmentUiUtil.SetActive(createRoot, civ.governorsEnabled);
        GovernmentUiUtil.SetText(createButtonLabel, GovernmentPresentation.FormatCreateGovernorLabel(civ));
        bool can = PoliticalActionRules.CanCreateGovernor(civ, out string reason);
        GovernmentUiUtil.SetInteractable(createButton, can);
        GovernmentUiUtil.SetText(createStatusText, can ? string.Empty : reason);
    }

    private void RefreshDetail(Governor governor)
    {
        GovernmentUiUtil.SetActive(detailRoot, governor != null);
        if (governor == null) return;

        string institution = GovernmentPresentation.GetInstitutionName(civ);
        GovernmentUiUtil.SetImage(portraitImage, GovernorPortraitService.GetSprite(governor.PortraitId));
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.FormatGovernorName(civ, governor));
        GovernmentUiUtil.SetText(titleText, GovernmentPresentation.GetGovernorTitleSingular(civ));
        GovernmentUiUtil.SetText(specializationText, governor.specialization.ToString());
        GovernmentUiUtil.SetText(levelText, $"Level {governor.Level} (XP {governor.Experience})");
        GovernmentUiUtil.SetText(opinionText, $"Opinion {GovernmentUiUtil.Signed(governor.Opinion)}");
        GovernmentUiUtil.SetText(ambitionText, $"Ambition {governor.AmbitionScore}");
        GovernmentUiUtil.SetText(powerText, $"Power {governor.PowerRank}");
        GovernmentUiUtil.SetText(personalityText, governor.PersonalityTraits.Count > 0 ? string.Join(", ", governor.PersonalityTraits) : "No notable personality");
        GovernmentUiUtil.SetText(traitsText, governor.Traits.Count > 0
            ? string.Join(", ", governor.Traits.Where(t => t != null).Select(t => t.name)) : "No traits");
        GovernmentUiUtil.SetText(religionText, governor.PersonalReligion != null ? $"Faith: {GovernmentPresentation.NameOf(governor.PersonalReligion)}" : "Faith: none");
        GovernmentUiUtil.SetText(cultureText, governor.PersonalCulture != null ? $"Culture: {GovernmentPresentation.NameOf(governor.PersonalCulture)}" : "Culture: none");
        GovernmentUiUtil.SetText(factionText, governor.Faction != null ? $"Faction: {governor.Faction.FactionName}" : "Faction: unaffiliated");
        GovernmentUiUtil.SetText(councilStatusText, governor.IsOnCouncil ? $"Seated on the {institution}"
            : governor.IsCouncilEligible ? $"Eligible for a seat on the {institution}" : $"Not eligible for the {institution}");
        GovernmentUiUtil.SetText(rebellionText, governor.IsInRebellion ? "IN OPEN REBELLION" : string.Empty);
        GovernmentUiUtil.SetText(grievancesText, governor.Grievances.Count == 0
            ? "No grievances"
            : string.Join("\n", governor.Grievances.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} x{kv.Value}")));

        opinionModifiers ??= new PoliticalLineList(opinionModifiersRoot, lineRowPrefab);
        opinionModifiers.Show(governor.OpinionModifiers, (row, m) => row.BindText(m.reason, GovernmentUiUtil.Signed(m.value)));
        RefreshHoldings(governor);

        GovernmentUiUtil.SetActive(councilActionsRoot, civ.HasRoyalCouncil);
        GovernmentUiUtil.SetText(grantSeatLabel, $"Grant {institution} Seat");
        GovernmentUiUtil.SetText(removeSeatLabel, $"Remove From {institution}");
        bool canGrant = PoliticalActionRules.CanGrantCouncilSeat(civ, governor, out string grantReason);
        bool canRemove = PoliticalActionRules.CanRemoveFromCouncil(civ, governor, out string removeReason);
        GovernmentUiUtil.SetInteractable(grantSeatButton, canGrant);
        GovernmentUiUtil.SetInteractable(removeSeatButton, canRemove);
        GovernmentUiUtil.SetActive(grantSeatButton != null ? grantSeatButton.gameObject : null, !governor.IsOnCouncil);
        GovernmentUiUtil.SetActive(removeSeatButton != null ? removeSeatButton.gameObject : null, governor.IsOnCouncil);
        GovernmentUiUtil.SetText(councilReasonText, governor.IsOnCouncil ? (canRemove ? string.Empty : removeReason) : (canGrant ? string.Empty : grantReason));

        bool canGift = PoliticalActionRules.CanSendGovernorGift(civ, governor, out string giftReason);
        bool canConvert = PoliticalActionRules.CanRequestGovernorConversion(civ, governor, out string convertReason);
        GovernmentUiUtil.SetText(giftButtonLabel, $"Send Gift — {PoliticalActionRules.GovernorGiftCost} Gold");
        GovernmentUiUtil.SetText(convertButtonLabel, "Ask to Convert");
        GovernmentUiUtil.SetInteractable(giftButton, canGift);
        GovernmentUiUtil.SetInteractable(convertButton, canConvert);
        GovernmentUiUtil.SetText(personalActionStatusText, !canGift ? giftReason : !canConvert ? convertReason : string.Empty);
    }

    private void OnCreateClicked()
    {
        if (civ == null) return;
        if (!PoliticalActionRules.CanCreateGovernor(civ, out string reason))
        {
            GovernmentUiUtil.SetText(createStatusText, reason);
            return;
        }

        string governorName = nameInput != null ? nameInput.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(governorName))
        {
            GovernmentUiUtil.SetText(createStatusText, $"Enter a name for the new {GovernmentPresentation.GetGovernorTitleSingular(civ).ToLowerInvariant()}.");
            return;
        }

        var specialization = Governor.Specialization.Military;
        if (specializationDropdown != null)
        {
            int count = Enum.GetNames(typeof(Governor.Specialization)).Length;
            specialization = (Governor.Specialization)Mathf.Clamp(specializationDropdown.value, 0, count - 1);
        }

        var created = civ.CreateGovernor(governorName, specialization);
        if (created == null)
        {
            GovernmentUiUtil.SetText(createStatusText, "Could not create a new governor.");
            return;
        }

        if (nameInput != null) nameInput.text = string.Empty;
        selectedGovernorId = created.Id;
        panel?.RefreshAllVisible();
    }

    private void OnGrantSeatClicked()
    {
        var governor = SelectedGovernor();
        if (governor == null) return;
        if (!PoliticalActionRules.CanGrantCouncilSeat(civ, governor, out string reason))
        {
            GovernmentUiUtil.SetText(councilReasonText, reason);
            return;
        }

        civ.AddToCouncil(governor);
        panel?.RefreshAllVisible();
    }

    private void OnRemoveSeatClicked()
    {
        var governor = SelectedGovernor();
        if (governor == null || panel == null) return;
        if (!PoliticalActionRules.CanRemoveFromCouncil(civ, governor, out string reason))
        {
            GovernmentUiUtil.SetText(councilReasonText, reason);
            return;
        }

        string institution = GovernmentPresentation.GetInstitutionName(civ);
        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Remove {GovernmentPresentation.FormatGovernorName(civ, governor)} from the {institution}?",
            description = "Stripping a seat angers the governor and leaves a lasting grievance.",
            confirmLabel = "Remove",
            lines = new List<PoliticalEffectLine>(),
            onConfirm = () =>
            {
                if (civ != null && civ.RemoveFromCouncil(governor)) panel.RefreshAllVisible();
            },
        });
    }

    private void RefreshHoldings(Governor governor)
    {
        float cityScroll = GovernmentUiUtil.CaptureScroll(citiesScroll);
        float herdScroll = GovernmentUiUtil.CaptureScroll(herdsScroll);
        var cities = (civ.cities ?? new List<City>()).Where(c => c != null)
            .OrderBy(c => HoldingGroup(c.governor, governor)).ThenBy(GovernmentPresentation.NameOf).ToList();
        var herds = (civ.herds ?? new List<Herd>()).Where(h => h != null)
            .OrderBy(h => HoldingGroup(h.governor, governor)).ThenBy(GovernmentPresentation.NameOf).ToList();
        GovernmentUiUtil.FillList(citiesRoot, holdingRowPrefab, cityHoldingRows, cities,
            (row, city) => BindCity(row, city, governor));
        GovernmentUiUtil.FillList(herdsRoot, holdingRowPrefab, herdHoldingRows, herds,
            (row, herd) => BindHerd(row, herd, governor));
        GovernmentUiUtil.RestoreScroll(citiesScroll, cityScroll);
        GovernmentUiUtil.RestoreScroll(herdsScroll, herdScroll);
    }

    private static int HoldingGroup(Governor owner, Governor selected)
        => owner == selected ? 0 : owner == null ? 1 : 2;

    private string OwnerLabel(Governor owner) => owner == null
        ? "Unassigned" : $"Controlled by {GovernmentPresentation.FormatGovernorName(civ, owner)}";

    private void BindCity(GovernorHoldingRowUI row, City city, Governor selected)
    {
        Governor owner = city.governor;
        string action = owner == null ? "ASSIGN" : owner == selected ? "REMOVE" : "TRANSFER";
        row.Bind(null, GovernmentPresentation.NameOf(city), $"Population {city.Population} • Level {city.level}",
            OwnerLabel(owner), action, owner != null, civ.governorsEnabled, () => ActOnCity(city, owner, selected));
    }

    private void BindHerd(GovernorHoldingRowUI row, Herd herd, Governor selected)
    {
        Governor owner = herd.governor;
        string action = owner == null ? "ASSIGN" : owner == selected ? "REMOVE" : "TRANSFER";
        row.Bind(null, GovernmentPresentation.NameOf(herd), $"Level {herd.level}", OwnerLabel(owner), action,
            owner != null, civ.governorsEnabled, () => ActOnHerd(herd, owner, selected));
    }

    private void ActOnCity(City city, Governor owner, Governor selected)
    {
        if (owner == null) { ApplyHoldingChange(civ.AssignGovernorToCity(selected, city)); return; }
        bool remove = owner == selected;
        var preview = Civilization.PreviewHoldingChange(owner, remove ? null : selected, true);
        var request = HoldingConfirmation(remove ? "Revoke City?" : $"Transfer {GovernmentPresentation.NameOf(city)}?",
            remove ? $"Removing {GovernmentPresentation.NameOf(city)} from {GovernmentPresentation.FormatGovernorName(civ, owner)} will leave the city unassigned."
                : $"Transfer {GovernmentPresentation.NameOf(city)} from {GovernmentPresentation.FormatGovernorName(civ, owner)} to {GovernmentPresentation.FormatGovernorName(civ, selected)}?",
            remove, owner, remove ? null : selected, preview,
            () => ApplyHoldingChange(remove ? civ.RemoveGovernorFromCity(selected, city) : civ.AssignGovernorToCity(selected, city)));
        panel?.RequestConfirmation(request);
    }

    private void ActOnHerd(Herd herd, Governor owner, Governor selected)
    {
        if (owner == null) { ApplyHoldingChange(civ.AssignGovernorToHerd(selected, herd)); return; }
        bool remove = owner == selected;
        var preview = Civilization.PreviewHoldingChange(owner, remove ? null : selected, false);
        var request = HoldingConfirmation(remove ? "Revoke Herd?" : $"Transfer {GovernmentPresentation.NameOf(herd)}?",
            remove ? $"Removing {GovernmentPresentation.NameOf(herd)} from {GovernmentPresentation.FormatGovernorName(civ, owner)} will leave it unassigned."
                : $"Transfer {GovernmentPresentation.NameOf(herd)} from {GovernmentPresentation.FormatGovernorName(civ, owner)} to {GovernmentPresentation.FormatGovernorName(civ, selected)}?",
            remove, owner, remove ? null : selected, preview,
            () => ApplyHoldingChange(remove ? civ.RemoveGovernorFromHerd(selected, herd) : civ.AssignGovernorToHerd(selected, herd)));
        panel?.RequestConfirmation(request);
    }

    private PoliticalConfirmRequest HoldingConfirmation(string title, string description, bool remove, Governor oldOwner,
        Governor recipient, Civilization.HoldingOpinionPreview preview, Action action)
    {
        var request = new PoliticalConfirmRequest { title = title, description = description,
            confirmLabel = remove ? "REMOVE" : "TRANSFER", onConfirm = action };
        request.lines.Add(new PoliticalEffectLine { label = GovernmentPresentation.FormatGovernorName(civ, oldOwner),
            value = $"{Mathf.RoundToInt(preview.oldGovernorChange):+0;-0;0} Loyalty", harmful = true });
        if (preview.addsGrievance)
            request.lines.Add(new PoliticalEffectLine { label = "Political grievance", value = "City Reassigned", harmful = true });
        if (recipient != null)
            request.lines.Add(new PoliticalEffectLine { label = GovernmentPresentation.FormatGovernorName(civ, recipient),
                value = $"{Mathf.RoundToInt(preview.newGovernorChange):+0;-0;0} Loyalty", beneficial = true });
        return request;
    }

    private void ApplyHoldingChange(bool succeeded)
    {
        if (!succeeded) GovernmentUiUtil.SetText(personalActionStatusText, "That assignment could not be made.");
        panel?.RefreshAllVisible();
    }

    private void ResetHoldingScrolls()
    {
        if (citiesScroll != null) citiesScroll.verticalNormalizedPosition = 1f;
        if (herdsScroll != null) herdsScroll.verticalNormalizedPosition = 1f;
    }

    private void OnGiftClicked()
    {
        var governor = SelectedGovernor();
        if (!PoliticalActionRules.CanSendGovernorGift(civ, governor, out string reason))
        { GovernmentUiUtil.SetText(personalActionStatusText, reason); return; }
        civ.AddGold(-PoliticalActionRules.GovernorGiftCost);
        governor.AddOpinionModifier("Received Gift", 15f, 15);
        governor.ClearGrievance(GrievanceSource.PublicInsult);
        UIManager.Instance?.ShowNotification($"Gifts were sent to {GovernmentPresentation.FormatGovernorName(civ, governor)}.");
        panel?.RefreshAllVisible();
    }

    private void OnConvertClicked()
    {
        var governor = SelectedGovernor();
        if (!PoliticalActionRules.CanRequestGovernorConversion(civ, governor, out string reason))
        { GovernmentUiUtil.SetText(personalActionStatusText, reason); return; }
        if (!governor.TryConvertReligion(civ.StateReligion, forced: false, out string failureReason))
        { GovernmentUiUtil.SetText(personalActionStatusText, failureReason); return; }
        UIManager.Instance?.ShowNotification($"{GovernmentPresentation.FormatGovernorName(civ, governor)} converted to the state religion.");
        panel?.RefreshAllVisible();
    }
}
