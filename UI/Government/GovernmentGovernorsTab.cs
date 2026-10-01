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
public class GovernmentGovernorsTab : GovernmentTabBase
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

    [Header("Detail lists (rows come from the line prefab)")]
    [SerializeField] private PoliticalLineRowUI lineRowPrefab;
    [SerializeField] private Transform opinionModifiersRoot;
    [SerializeField] private Transform citiesRoot;
    [SerializeField] private Transform herdsRoot;

    [Header("Council actions")]
    [SerializeField] private GameObject councilActionsRoot;
    [SerializeField] private Button grantSeatButton;
    [SerializeField] private TMP_Text grantSeatLabel;
    [SerializeField] private Button removeSeatButton;
    [SerializeField] private TMP_Text removeSeatLabel;
    [SerializeField] private TMP_Text councilReasonText;

    [Header("Holdings")]
    [SerializeField] private Button manageHoldingsButton;
    [SerializeField] private GovernorHoldingsPanelUI holdingsPanel;

    [Header("Suppressed politics")]
    [SerializeField] private GameObject suppressedNoticeRoot;
    [SerializeField] private TMP_Text suppressedNoticeText;

    private readonly List<GovernorRowUI> rows = new List<GovernorRowUI>();
    private PoliticalLineList opinionModifiers, cities, herds;
    private int selectedGovernorId = -1;
    private bool wired;

    protected override void OnCivilizationChanged()
    {
        selectedGovernorId = -1;
        if (holdingsPanel != null && holdingsPanel.IsVisible) holdingsPanel.Hide();
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
        GovernmentUiUtil.SetClick(manageHoldingsButton, OnManageHoldingsClicked);
    }

    private Governor SelectedGovernor() => civ?.governors.FirstOrDefault(g => g != null && g.Id == selectedGovernorId);

    private void SelectGovernor(Governor governor)
    {
        selectedGovernorId = governor != null ? governor.Id : -1;
        if (holdingsPanel != null && holdingsPanel.IsVisible) holdingsPanel.Hide();
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
        cities ??= new PoliticalLineList(citiesRoot, lineRowPrefab);
        herds ??= new PoliticalLineList(herdsRoot, lineRowPrefab);
        opinionModifiers.Show(governor.OpinionModifiers, (row, m) => row.BindText(m.reason, GovernmentUiUtil.Signed(m.value)));
        cities.ShowTexts(governor.Cities.Where(c => c != null).Select(c => GovernmentPresentation.NameOf(c)).ToList());
        herds.ShowTexts(governor.Herds.Where(h => h != null).Select(h => GovernmentPresentation.NameOf(h)).ToList());

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

        GovernmentUiUtil.SetInteractable(manageHoldingsButton, civ.governorsEnabled);
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

    private void OnManageHoldingsClicked()
    {
        var governor = SelectedGovernor();
        if (governor == null || holdingsPanel == null) return;
        holdingsPanel.Show(civ, governor, () => panel?.RefreshAllVisible());
    }
}
