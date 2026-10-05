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

    [Header("Candidates")]
    [SerializeField] private GameObject candidatesRoot;
    [SerializeField] private Transform candidatesListRoot;
    [SerializeField] private ScrollRect candidatesScroll;
    [SerializeField] private GovernorCandidateRowUI candidateRowPrefab;
    [SerializeField] private TMP_Text candidateStatusText;

    [Header("Detail")]
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text identityText;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private TMP_Text characterText;
    [SerializeField] private TMP_Text politicalIdentityText;
    [SerializeField] private TMP_Text politicalStatusText;
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
    [SerializeField] private Button convertButton;
    [SerializeField] private Button pressureButton;
    [SerializeField] private Button pardonButton;
    [SerializeField] private Button dismissButton;
    [SerializeField] private TMP_Text personalActionStatusText;

    [Header("Suppressed politics")]
    [SerializeField] private GameObject suppressedNoticeRoot;
    [SerializeField] private TMP_Text suppressedNoticeText;

    private readonly List<GovernorRowUI> rows = new List<GovernorRowUI>();
    private readonly List<GovernorCandidateRowUI> candidateRows = new List<GovernorCandidateRowUI>();
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

        RefreshCandidates();
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

        GovernmentUiUtil.SetClick(grantSeatButton, OnGrantSeatClicked);
        GovernmentUiUtil.SetClick(removeSeatButton, OnRemoveSeatClicked);
        GovernmentUiUtil.SetClick(giftButton, OnGiftClicked);
        GovernmentUiUtil.SetClick(convertButton, OnConvertClicked);
        GovernmentUiUtil.SetClick(pressureButton, OnPressureClicked);
        GovernmentUiUtil.SetClick(pardonButton, OnPardonClicked);
        GovernmentUiUtil.SetClick(dismissButton, OnDismissClicked);
    }

    private Governor SelectedGovernor() => civ?.governors.FirstOrDefault(g => g != null && g.Id == selectedGovernorId);

    private void SelectGovernor(Governor governor)
    {
        bool changed = governor != null && governor.Id != selectedGovernorId;
        selectedGovernorId = governor != null ? governor.Id : -1;
        resetHoldingScroll = changed;
        Refresh();
    }

    private void RefreshCandidates()
    {
        bool visible = civ.governorsEnabled;
        GovernmentUiUtil.SetActive(candidatesRoot, visible);
        if (!visible) return;
        var candidates = (civ.governorCandidates ?? new List<GovernorCandidate>()).Where(c => c != null).ToList();
        bool canAppoint = civ.governors.Count < civ.governorCount;
        float scroll = GovernmentUiUtil.CaptureScroll(candidatesScroll);
        GovernmentUiUtil.FillList(candidatesListRoot, candidateRowPrefab, candidateRows, candidates,
            (row, candidate) => row.Bind(civ, candidate, canAppoint, RequestAppointment));
        GovernmentUiUtil.RestoreScroll(candidatesScroll, scroll);
        GovernmentUiUtil.SetText(candidateStatusText, canAppoint ? (candidates.Count == 0 ? "No candidates are currently available." : string.Empty)
            : $"Governor capacity reached ({civ.governors.Count}/{civ.governorCount})");
    }

    private void RequestAppointment(GovernorCandidate candidate)
    {
        if (candidate == null || panel == null) return;
        string personality = candidate.personalityTraits != null && candidate.personalityTraits.Count > 0
            ? string.Join(" • ", candidate.personalityTraits) : "No notable personality";
        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Appoint {candidate.name}?",
            description = $"Appoint {candidate.name} as a Governor of your civilization?",
            confirmLabel = "APPOINT",
            lines = new List<PoliticalEffectLine>
            {
                new PoliticalEffectLine { label = "Specialization", value = candidate.specialization.ToString() },
                new PoliticalEffectLine { label = "Starting Loyalty", value = GovernmentUiUtil.Signed(candidate.StartingOpinion), beneficial = candidate.StartingOpinion >= 0f },
                new PoliticalEffectLine { label = "Personality", value = personality },
                new PoliticalEffectLine { label = "Religion", value = candidate.personalReligion != null ? GovernmentPresentation.NameOf(candidate.personalReligion) : "No Religion" },
                new PoliticalEffectLine { label = "Culture", value = candidate.personalCulture != null ? GovernmentPresentation.NameOf(candidate.personalCulture) : "No Culture" }
            },
            onConfirm = () =>
            {
                string reason = "Civilization is unavailable.";
                if (civ != null && civ.TryAppointGovernorCandidate(candidate.candidateId, out var governor, out reason))
                {
                    selectedGovernorId = governor.Id;
                    panel.RefreshAllVisible();
                }
                else GovernmentUiUtil.SetText(candidateStatusText, reason);
            }
        });
    }

    private void RefreshDetail(Governor governor)
    {
        GovernmentUiUtil.SetActive(detailRoot, governor != null);
        if (governor == null) return;

        string institution = GovernmentPresentation.GetInstitutionName(civ);
        GovernmentUiUtil.SetImage(portraitImage, GovernorPortraitService.GetSprite(governor.PortraitId));
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.FormatGovernorName(civ, governor));
        string personalities = governor.PersonalityTraits.Count > 0 ? string.Join(" • ", governor.PersonalityTraits) : "No notable personality";
        string traits = governor.Traits.Any(t => t != null) ? string.Join(" • ", governor.Traits.Where(t => t != null).Select(t => t.name)) : "No acquired traits";
        GovernmentUiUtil.SetText(identityText, $"{GovernmentPresentation.GetGovernorTitleSingular(civ)} • {governor.specialization} • Level {governor.Level}\nXP {governor.Experience}");
        GovernmentUiUtil.SetText(statsText, $"Loyalty {GovernmentUiUtil.Signed(governor.Opinion)}  •  Ambition {governor.AmbitionScore}  •  Power {governor.PowerRank}");
        GovernmentUiUtil.SetText(characterText, $"<b>Personality</b>\n{personalities}\n\n<b>Traits</b>\n{traits}");
        GovernmentUiUtil.SetText(politicalIdentityText, $"<b>Religion</b> {(governor.PersonalReligion != null ? GovernmentPresentation.NameOf(governor.PersonalReligion) : "None")}\n<b>Culture</b> {(governor.PersonalCulture != null ? GovernmentPresentation.NameOf(governor.PersonalCulture) : "None")}\n<b>Faction</b> {(governor.Faction != null ? governor.Faction.FactionName : "Unaffiliated")}");
        string councilStatus = governor.IsOnCouncil ? $"Seated on the {institution}" : governor.IsCouncilEligible ? $"Eligible for a seat on the {institution}" : $"Not eligible for the {institution}";
        GovernmentUiUtil.SetText(politicalStatusText, governor.IsInRebellion ? $"{councilStatus}\n<color=#D95C5C>IN OPEN REBELLION</color>" : councilStatus);
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
        bool canPressure = PoliticalActionRules.CanPressureGovernor(civ, governor, out _);
        bool canPardon = PoliticalActionRules.CanPardonGovernor(civ, governor, out _);
        bool canDismiss = PoliticalActionRules.CanDismissGovernor(civ, governor, out _);
        GovernmentUiUtil.SetInteractable(giftButton, canGift);
        GovernmentUiUtil.SetInteractable(convertButton, canConvert);
        GovernmentUiUtil.SetInteractable(pressureButton, canPressure);
        GovernmentUiUtil.SetInteractable(pardonButton, canPardon);
        GovernmentUiUtil.SetInteractable(dismissButton, canDismiss);
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

    private static GovernorHoldingAction GetHoldingAction(Governor owner, Governor selected)
    {
        if (owner == null)
            return GovernorHoldingAction.Assign;

        if (owner == selected)
            return GovernorHoldingAction.Remove;

        return GovernorHoldingAction.Transfer;
    }

    private void BindCity(GovernorHoldingRowUI row, City city, Governor selected)
    {
        Governor owner = city.governor;
        GovernorHoldingAction action = GetHoldingAction(owner, selected);
        row.Bind(GovernmentPresentation.NameOf(city), FormatCityHoldingDetails(civ, city),
            FormatCityHoldingYields(city), OwnerLabel(owner), action, owner != null, civ.governorsEnabled,
            () => ActOnCity(city, owner, selected));
    }

    private void BindHerd(GovernorHoldingRowUI row, Herd herd, Governor selected)
    {
        Governor owner = herd.governor;
        GovernorHoldingAction action = GetHoldingAction(owner, selected);
        row.Bind(GovernmentPresentation.NameOf(herd), FormatHerdHoldingDetails(herd),
            FormatHerdHoldingYields(herd), OwnerLabel(owner), action, owner != null, civ.governorsEnabled,
            () => ActOnHerd(herd, owner, selected));
    }

    private static string FormatCityHoldingDetails(Civilization civilization, City city)
    {
        string details = $"Population {city.Population} • Level {city.level}";
        return civilization != null && civilization.CapitalCity == city ? $"{details} • Capital" : details;
    }

    private static string FormatCityHoldingYields(City city)
        => $"Food {city.GetFoodPerTurn()} • Prod {city.GetProductionPerTurn()} • Gold {city.GetGoldPerTurn()}" +
           $" • Science {city.GetSciencePerTurn()} • Culture {city.GetCulturePerTurn()}" +
           $" • Faith {city.GetFaithPerTurn()} • Policy {city.GetPolicyPointPerTurn()}";

    private static string FormatHerdHoldingDetails(Herd herd)
        => $"{herd.GetTotalAnimalCount()} Livestock • Level {herd.level}";

    private static string FormatHerdHoldingYields(Herd herd)
    {
        var yields = herd.GetAnimalYields();
        return $"Food {yields.Food} • Prod {yields.Production} • Gold {yields.Gold}" +
               $" • Science {yields.Science} • Culture {yields.Culture} • Faith {yields.Faith}";
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

    private void OnPressureClicked()
    {
        var governor = SelectedGovernor();
        if (!PoliticalActionRules.CanPressureGovernor(civ, governor, out string reason))
        { GovernmentUiUtil.SetText(personalActionStatusText, reason); return; }
        float change = PoliticalActionRules.GetPressureOpinionChange(governor);
        panel?.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Pressure {governor.Name}?",
            description = "Use political pressure to suppress open defiance temporarily. The governor will resent the coercion.",
            confirmLabel = "PRESSURE",
            lines = new List<PoliticalEffectLine>
            {
                new PoliticalEffectLine { label = "Policy Points", value = $"-{PoliticalActionRules.GovernorPressurePolicyCost}", harmful = true },
                new PoliticalEffectLine { label = "Loyalty", value = GovernmentUiUtil.Signed(change), harmful = true },
                new PoliticalEffectLine { label = "Duration", value = $"{PoliticalActionRules.GovernorPressureDuration} turns" }
            },
            onConfirm = () =>
            {
                bool ok = PoliticalActionRules.TryPressureGovernor(civ, governor, out string resultReason);
                GovernmentUiUtil.SetText(personalActionStatusText, ok ? $"{governor.Name} is under political pressure." : resultReason);
                panel?.RefreshAllVisible();
            }
        });
    }

    private void OnPardonClicked()
    {
        var governor = SelectedGovernor();
        if (!PoliticalActionRules.CanPardonGovernor(civ, governor, out string reason))
        { GovernmentUiUtil.SetText(personalActionStatusText, reason); return; }
        int grievanceStacks = governor.TotalGrievances();
        int grievanceTypes = governor.Grievances.Count(kv => kv.Value > 0);
        string governorName = GovernmentPresentation.FormatGovernorName(civ, governor);
        panel?.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Pardon {governorName}?",
            description = "Forgive all of this governor's current political grievances.\nHistorical opinion effects will continue to decay normally.",
            confirmLabel = "PARDON",
            lines = new List<PoliticalEffectLine>
            {
                new PoliticalEffectLine { label = "Faith", value = $"-{PoliticalActionRules.GovernorPardonFaithCost}", harmful = true },
                new PoliticalEffectLine { label = "Grievances", value = $"{grievanceStacks} stacks across {grievanceTypes} types will be cleared", beneficial = true },
                new PoliticalEffectLine { label = "Loyalty", value = $"+{PoliticalActionRules.GovernorPardonOpinionBonus:0} for {PoliticalActionRules.GovernorPardonCooldown} turns", beneficial = true },
                new PoliticalEffectLine { label = "Cooldown", value = $"Cannot be pardoned again for {PoliticalActionRules.GovernorPardonCooldown} turns", harmful = true }
            },
            onConfirm = () =>
            {
                if (PoliticalActionRules.TryPardonGovernor(civ, governor, out string resultReason))
                {
                    GovernmentUiUtil.SetText(personalActionStatusText, $"{governorName} has been pardoned.");
                    UIManager.Instance?.ShowNotification($"{governorName} has been pardoned.");
                    panel?.RefreshAllVisible();
                }
                else
                {
                    GovernmentUiUtil.SetText(personalActionStatusText, resultReason);
                }
            }
        });
    }

    private void OnDismissClicked()
    {
        var governor = SelectedGovernor();
        if (!PoliticalActionRules.CanDismissGovernor(civ, governor, out string reason))
        { GovernmentUiUtil.SetText(personalActionStatusText, reason); return; }
        var risk = PoliticalActionRules.PreviewGovernorDismissalRisk(civ, governor);
        var lines = new List<PoliticalEffectLine>
        {
            new PoliticalEffectLine { label = "Policy Points", value = $"-{PoliticalActionRules.GovernorDismissPolicyCost}", harmful = true },
            new PoliticalEffectLine { label = "Rebellion Risk", value = $"{GovernmentPresentation.DismissalRiskLabel(risk.riskPercent)} — {Mathf.RoundToInt(risk.riskPercent)}%", harmful = true }
        };
        AddRiskLine(lines, "Low Loyalty", risk.loyaltyRisk); AddRiskLine(lines, "Ambition", risk.ambitionRisk);
        AddRiskLine(lines, "Political Power", risk.powerRisk); AddRiskLine(lines, "Grievances", risk.grievanceRisk);
        AddRiskLine(lines, "Personality", risk.personalityRisk); AddRiskLine(lines, "Local Garrison", risk.localMilitaryRisk);
        AddRiskLine(lines, "Nearby Royal Army", risk.stateDeterrence); AddRiskLine(lines, "Under Pressure", risk.pressureReduction);
        string name = governor.Name;
        panel?.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Dismiss {name} from office?",
            description = "Removing this governor will leave their holdings unassigned. They may refuse the order and rise in rebellion.",
            confirmLabel = "DISMISS", lines = lines,
            onConfirm = () =>
            {
                bool ok = civ.TryDismissGovernor(governor, out var result);
                if (!ok) GovernmentUiUtil.SetText(personalActionStatusText, result.reason);
                else if (result.outcome == GovernorDismissalOutcome.Submitted)
                { GovernmentUiUtil.SetText(personalActionStatusText, $"{name} submitted and has been removed from office."); selectedGovernorId = -1; }
                else GovernmentUiUtil.SetText(personalActionStatusText, $"{name} refused dismissal and has risen in rebellion.");
                panel?.RefreshAllVisible();
            }
        });
    }

    private static void AddRiskLine(List<PoliticalEffectLine> lines, string label, float value)
    {
        if (Mathf.Abs(value) < 0.5f) return;
        lines.Add(new PoliticalEffectLine { label = label, value = $"{value:+0;-0}%", harmful = value > 0f, beneficial = value < 0f });
    }
}
