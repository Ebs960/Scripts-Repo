using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlord management of vassal contracts: tribute, military obligation, religious policy, release, and
/// independence demands. Every change goes through SubjectManager and is confirmed with its consequences first.
/// </summary>
public class GovernmentVassalsUI : GovernmentScreenBase
{
    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private VassalRowUI rowPrefab;
    [SerializeField] private ScrollRect listScroll;

    [Header("Detail")]
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private TMP_Text subjectNameText;
    [SerializeField] private Image subjectIconImage;
    [SerializeField] private TMP_Text libertyText;
    [SerializeField] private TMP_Text opinionText;
    [SerializeField] private TMP_Text resentmentText;
    [SerializeField] private TMP_Text tributeExhaustionText;
    [SerializeField] private TMP_Text militaryConfidenceText;
    [SerializeField] private TMP_Text stateReligionText;
    [SerializeField] private TMP_Text cooldownText;
    [SerializeField] private TMP_Text statusText;

    [Header("Tribute terms")]
    [SerializeField] private Slider goldSlider;
    [SerializeField] private Slider foodSlider;
    [SerializeField] private TMP_Text goldValueText;
    [SerializeField] private TMP_Text foodValueText;
    [SerializeField] private Button applyTributeButton;

    [Header("Resource demand")]
    [SerializeField] private TMP_Dropdown resourceDropdown;
    [SerializeField] private TMP_Dropdown resourceModeDropdown;
    [SerializeField] private Slider resourceAmountSlider;
    [SerializeField] private TMP_Text resourceAmountText;
    [SerializeField] private Button applyResourceButton;

    [Header("Military obligation")]
    [SerializeField] private Slider obligationSlider;
    [SerializeField] private TMP_Text obligationValueText;
    [SerializeField] private Button applyObligationButton;

    [Header("Religious policy")]
    [SerializeField] private TMP_Dropdown religionRuleDropdown;
    [SerializeField] private Button applyReligionButton;

    [Header("Release / independence")]
    [SerializeField] private Button releaseButton;
    [SerializeField] private GameObject demandRoot;
    [SerializeField] private TMP_Text demandText;
    [SerializeField] private Button acceptDemandButton;
    [SerializeField] private Button rejectDemandButton;

    private readonly List<VassalRowUI> rows = new List<VassalRowUI>();
    private readonly List<ResourceData> resourceOptions = new List<ResourceData>();
    private Civilization selectedSubject;
    private bool hasRequestedFocus;
    private bool wired;

    protected override void OnCivilizationChanged()
    {
        if (!hasRequestedFocus || civ == null || selectedSubject == null
            || SubjectManager.Instance == null || SubjectManager.Instance.GetContract(civ, selectedSubject) == null)
            selectedSubject = null;
        hasRequestedFocus = false;
    }

    public void FocusVassal(VassalContract contract)
    {
        selectedSubject = contract?.subject;
        hasRequestedFocus = selectedSubject != null;
        if (civ != null && gameObject.activeInHierarchy) Refresh();
    }

    public override void Refresh()
    {
        if (civ == null || SubjectManager.Instance == null) return;
        EnsureWired();

        var contracts = SubjectManager.Instance.GetSubjects(civ);
        var selected = contracts.FirstOrDefault(c => c.subject == selectedSubject) ?? contracts.FirstOrDefault();
        selectedSubject = selected?.subject;
        hasRequestedFocus = false;

        float scroll = GovernmentUiUtil.CaptureScroll(listScroll);
        GovernmentUiUtil.FillList(listRoot, rowPrefab, rows, contracts,
            (row, contract) => row.Bind(contract, contract == selected, IsRestless(contract), SelectContract));
        GovernmentUiUtil.RestoreScroll(listScroll, scroll);

        GovernmentUiUtil.SetActive(detailRoot, selected != null);
        if (selected != null) RefreshDetail(selected);
    }

    private static bool IsRestless(VassalContract contract)
        => contract != null && contract.libertyDesire >= contract.EffectiveBreakawayThreshold
            * PoliticalWarningBuilder.VassalLibertyWarningFraction;

    private void EnsureWired()
    {
        if (wired) return;
        wired = true;

        ConfigureSlider(goldSlider, 0f, SubjectManager.MaxTributePct, false);
        ConfigureSlider(foodSlider, 0f, SubjectManager.MaxTributePct, false);
        ConfigureSlider(obligationSlider, 0f, SubjectManager.MaxMilitaryObligation, true);
        ConfigureSlider(resourceAmountSlider, 0f, SubjectManager.MaxResourceDemand, true);

        if (goldSlider != null) goldSlider.onValueChanged.AddListener(_ => UpdateSliderLabels());
        if (foodSlider != null) foodSlider.onValueChanged.AddListener(_ => UpdateSliderLabels());
        if (obligationSlider != null) obligationSlider.onValueChanged.AddListener(_ => UpdateSliderLabels());
        if (resourceAmountSlider != null) resourceAmountSlider.onValueChanged.AddListener(_ => UpdateSliderLabels());

        if (resourceModeDropdown != null)
        {
            resourceModeDropdown.ClearOptions();
            resourceModeDropdown.AddOptions(new List<string> { "One-time demand", "Per turn" });
        }

        if (religionRuleDropdown != null)
        {
            religionRuleDropdown.ClearOptions();
            religionRuleDropdown.AddOptions(new List<string>
            {
                "Full Tolerance", "Limited Tolerance", "State Religion Required", "Forced Conversion",
            });
        }

        GovernmentUiUtil.SetClick(applyTributeButton, OnApplyTribute);
        GovernmentUiUtil.SetClick(applyObligationButton, OnApplyObligation);
        GovernmentUiUtil.SetClick(applyReligionButton, OnApplyReligion);
        GovernmentUiUtil.SetClick(applyResourceButton, OnApplyResource);
        GovernmentUiUtil.SetClick(releaseButton, OnReleaseClicked);
        GovernmentUiUtil.SetClick(acceptDemandButton, OnAcceptDemand);
        GovernmentUiUtil.SetClick(rejectDemandButton, OnRejectDemand);
    }

    private static void ConfigureSlider(Slider slider, float min, float max, bool whole)
    {
        if (slider == null) return;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = whole;
    }

    private VassalContract SelectedContract()
        => civ != null && selectedSubject != null && SubjectManager.Instance != null
            ? SubjectManager.Instance.GetContract(civ, selectedSubject)
            : null;

    private void SelectContract(VassalContract contract)
    {
        selectedSubject = contract?.subject;
        GovernmentUiUtil.SetText(statusText, string.Empty);
        Refresh();
    }

    private void RefreshDetail(VassalContract contract)
    {
        EnsureResourceOptions();
        var manager = SubjectManager.Instance;
        int turn = manager.CurrentTurn;
        float opinion = manager.GetEffectiveSubjectOpinion(contract);
        Sprite icon = contract != null && contract.subject != null && contract.subject.civData != null
            ? contract.subject.civData.icon
            : null;

        GovernmentUiUtil.SetImage(subjectIconImage, icon);
        GovernmentUiUtil.SetText(subjectNameText, contract.subjectCivName);
        GovernmentUiUtil.SetText(libertyText, $"Liberty desire {contract.libertyDesire:0} / {contract.EffectiveBreakawayThreshold:0}");
        GovernmentUiUtil.SetText(opinionText, $"Opinion of you {GovernmentUiUtil.Signed(opinion)}");
        GovernmentUiUtil.SetText(resentmentText, $"Resentment {contract.resentment:0}");
        GovernmentUiUtil.SetText(tributeExhaustionText, $"Tribute exhaustion {contract.tributeExhaustion:0}");
        GovernmentUiUtil.SetText(militaryConfidenceText, $"Military confidence {contract.militaryConfidence:0}");
        GovernmentUiUtil.SetText(stateReligionText, contract.subject?.StateReligion != null
            ? $"State Religion: {GovernmentPresentation.NameOf(contract.subject.StateReligion)}"
            : "State Religion: No Religion");

        var check = manager.CanInterfere(civ, contract.subject, turn);
        GovernmentUiUtil.SetText(cooldownText, check.success ? "Terms can be changed now." : check.reason);
        if (goldSlider != null) goldSlider.interactable = check.success;
        if (foodSlider != null) foodSlider.interactable = check.success;
        if (obligationSlider != null) obligationSlider.interactable = check.success;
        if (religionRuleDropdown != null) religionRuleDropdown.interactable = check.success;
        if (resourceDropdown != null) resourceDropdown.interactable = check.success;
        if (resourceModeDropdown != null) resourceModeDropdown.interactable = check.success;
        if (resourceAmountSlider != null) resourceAmountSlider.interactable = check.success;
        GovernmentUiUtil.SetInteractable(applyTributeButton, check.success);
        GovernmentUiUtil.SetInteractable(applyObligationButton, check.success);
        GovernmentUiUtil.SetInteractable(applyReligionButton, check.success);
        GovernmentUiUtil.SetInteractable(applyResourceButton, check.success && resourceOptions.Count > 0);

        // Reset editors to the live contract values each refresh so they never show stale proposals.
        if (goldSlider != null) goldSlider.SetValueWithoutNotify(contract.goldTributePct);
        if (foodSlider != null) foodSlider.SetValueWithoutNotify(contract.foodTributePct);
        if (obligationSlider != null) obligationSlider.SetValueWithoutNotify(contract.militaryObligationCount);
        if (religionRuleDropdown != null) religionRuleDropdown.SetValueWithoutNotify((int)contract.religionRule);
        bool standingDemand = contract.resourceTribute != null && contract.resourceTributePerTurn > 0;
        int standingIndex = standingDemand ? resourceOptions.IndexOf(contract.resourceTribute) : -1;
        if (resourceDropdown != null && standingIndex >= 0) resourceDropdown.SetValueWithoutNotify(standingIndex);
        if (resourceModeDropdown != null) resourceModeDropdown.SetValueWithoutNotify(standingDemand ? 1 : 0);
        if (resourceAmountSlider != null) resourceAmountSlider.SetValueWithoutNotify(standingDemand ? contract.resourceTributePerTurn : 1);
        UpdateSliderLabels();

        var demand = manager.GetPendingIndependenceDemand(civ, contract.subject);
        GovernmentUiUtil.SetActive(demandRoot, demand != null);
        if (demand != null)
            GovernmentUiUtil.SetText(demandText, $"{contract.subjectCivName} demands full independence (since turn {demand.turnIssued}).");
        GovernmentUiUtil.SetInteractable(releaseButton, true);
    }

    private void EnsureResourceOptions()
    {
        if (resourceOptions.Count > 0 || resourceDropdown == null) return;
        resourceOptions.AddRange(ResourceCache.GetAllResourceData()
            .Where(r => r != null && !string.IsNullOrEmpty(r.resourceName)).OrderBy(r => r.resourceName));
        resourceDropdown.ClearOptions();
        resourceDropdown.AddOptions(resourceOptions.Select(r => r.resourceName).ToList());
    }

    private void UpdateSliderLabels()
    {
        GovernmentUiUtil.SetText(goldValueText, goldSlider != null ? $"{goldSlider.value:P0}" : string.Empty);
        GovernmentUiUtil.SetText(foodValueText, foodSlider != null ? $"{foodSlider.value:P0}" : string.Empty);
        GovernmentUiUtil.SetText(resourceAmountText, resourceAmountSlider != null ? $"{Mathf.RoundToInt(resourceAmountSlider.value)}" : string.Empty);
        GovernmentUiUtil.SetText(obligationValueText, obligationSlider != null ? $"{Mathf.RoundToInt(obligationSlider.value)} units" : string.Empty);
    }

    private static PoliticalEffectLine Line(string label, string value, bool beneficial = false, bool harmful = false)
        => new PoliticalEffectLine { label = label, value = value, beneficial = beneficial, harmful = harmful };

    private void Confirm(string title, string description, string confirmLabel, List<PoliticalEffectLine> lines, Action action)
    {
        if (panel == null) return;
        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = title,
            description = description,
            confirmLabel = confirmLabel,
            lines = lines,
            onConfirm = action,
        });
    }

    private void Report(SubjectActionResult result, string successMessage)
    {
        GovernmentUiUtil.SetText(statusText, result.success ? successMessage : result.reason);
        if (result.success) UIManager.Instance?.ShowNotification(successMessage);
        panel?.RefreshAllVisible();
    }

    private string CooldownNote(VassalContract contract)
        => $"Any change starts a {contract.interferenceCooldown}-turn cooldown before you can change this contract again.";

    private void OnApplyTribute()
    {
        var contract = SelectedContract();
        if (contract == null || goldSlider == null || foodSlider == null) return;
        float gold = goldSlider.value, food = foodSlider.value;
        float change = (gold + food) - (contract.goldTributePct + contract.foodTributePct);
        if (Mathf.Approximately(gold, contract.goldTributePct)
            && Mathf.Approximately(food, contract.foodTributePct))
        {
            GovernmentUiUtil.SetText(statusText, "Tribute terms are unchanged.");
            return;
        }

        var subject = contract.subject;
        var lines = new List<PoliticalEffectLine>
        {
            Line("Gold tribute", $"{contract.goldTributePct:P0} -> {gold:P0}"),
            Line("Food tribute", $"{contract.foodTributePct:P0} -> {food:P0}"),
            change > 0f
                ? Line("Their governors", "Resent a heavier burden", harmful: true)
                : Line("Their opinion and liberty", "Opinion rises, liberty desire falls", beneficial: true),
            Line("Resentment", "Any change adds resentment", harmful: true),
        };
        Confirm($"Change tribute from {contract.subjectCivName}?", CooldownNote(contract), "Apply", lines, () =>
            Report(SubjectManager.Instance.TrySetTributeTerms(civ, subject, gold, food, SubjectManager.Instance.CurrentTurn), "Tribute terms updated."));
    }

    private void OnApplyObligation()
    {
        var contract = SelectedContract();
        if (contract == null || obligationSlider == null) return;
        int count = Mathf.RoundToInt(obligationSlider.value);
        int delta = count - contract.militaryObligationCount;
        if (delta == 0) { GovernmentUiUtil.SetText(statusText, "Military obligation is unchanged."); return; }

        var subject = contract.subject;
        var lines = new List<PoliticalEffectLine>
        {
            Line("Units owed in wartime", $"{contract.militaryObligationCount} -> {count}"),
        };
        if (delta > 0) lines.Add(Line("Resentment", "Rises with each extra unit", harmful: true));
        Confirm($"Change military obligation of {contract.subjectCivName}?", CooldownNote(contract), "Apply", lines, () =>
            Report(SubjectManager.Instance.TrySetMilitaryObligation(civ, subject, count, SubjectManager.Instance.CurrentTurn), "Military obligation updated."));
    }

    private void OnApplyReligion()
    {
        var contract = SelectedContract();
        if (contract == null || religionRuleDropdown == null) return;
        var rule = (ReligionToleranceRule)Mathf.Clamp(religionRuleDropdown.value, 0, Enum.GetValues(typeof(ReligionToleranceRule)).Length - 1);
        if (rule == contract.religionRule) { GovernmentUiUtil.SetText(statusText, "This religious policy is already in force."); return; }

        var subject = contract.subject;
        bool tightening = (int)rule > (int)contract.religionRule;
        var lines = new List<PoliticalEffectLine>
        {
            Line("Religious policy", $"{contract.religionRule} -> {rule}"),
            tightening
                ? Line("Resentment and unrest", "Rises; devout governors turn hostile", harmful: true)
                : Line("Their opinion", "Rises", beneficial: true),
        };
        if (rule == ReligionToleranceRule.ForcedConversion)
        {
            if (civ.StateReligion == null)
            {
                GovernmentUiUtil.SetText(statusText, "Your civilization has no state religion to impose.");
                return;
            }
            lines.Add(Line("State religion imposed", GovernmentPresentation.NameOf(civ.StateReligion), harmful: true));
            lines.Add(Line("Forced conversion", "Heavy liberty desire growth every turn", harmful: true));
            lines.Add(Line("Their governors", "Become angry; zealous governors react strongly", harmful: true));
        }
        Confirm($"Change religious policy for {contract.subjectCivName}?", CooldownNote(contract), "Apply", lines, () =>
            Report(SubjectManager.Instance.TrySetReligionRule(civ, subject, rule, SubjectManager.Instance.CurrentTurn), "Religious policy updated."));
    }

    private void OnApplyResource()
    {
        var contract = SelectedContract();
        if (contract == null || resourceDropdown == null || resourceAmountSlider == null) return;
        int index = resourceDropdown.value;
        if (index < 0 || index >= resourceOptions.Count) { GovernmentUiUtil.SetText(statusText, "Choose a resource to demand."); return; }

        var resource = resourceOptions[index];
        int amount = Mathf.RoundToInt(resourceAmountSlider.value);
        bool perTurn = resourceModeDropdown != null && resourceModeDropdown.value == 1;
        var subject = contract.subject;

        List<PoliticalEffectLine> lines;
        string title, description, confirmLabel, success;
        if (!perTurn)
        {
            if (amount <= 0) { GovernmentUiUtil.SetText(statusText, "Choose an amount to demand."); return; }
            if (subject.GetResourceCount(resource) < amount)
            {
                GovernmentUiUtil.SetText(statusText, $"{contract.subjectCivName} does not have {amount} {resource.resourceName}.");
                return;
            }
            title = $"Demand {amount} {resource.resourceName} from {contract.subjectCivName}?";
            description = "They hand the goods over now. This is an open act of extortion: it infuriates the subject and its governors. " + CooldownNote(contract);
            confirmLabel = "Demand";
            success = $"Took {amount} {resource.resourceName}.";
            lines = new List<PoliticalEffectLine>
            {
                Line("You receive", $"{amount} {resource.resourceName} (once)", beneficial: true),
                Line("Subject resentment", "Rises sharply", harmful: true),
                Line("Their opinion and governors", "Fall sharply", harmful: true),
            };
        }
        else if (amount == 0)
        {
            if (contract.resourceTribute == null || contract.resourceTributePerTurn <= 0)
            {
                GovernmentUiUtil.SetText(statusText, "There is no standing resource demand to end.");
                return;
            }
            title = $"End the {contract.resourceTribute.resourceName} demand on {contract.subjectCivName}?";
            description = CooldownNote(contract);
            confirmLabel = "End demand";
            success = "Resource demand ended.";
            lines = new List<PoliticalEffectLine> { Line("Their opinion", "Rises", beneficial: true) };
        }
        else
        {
            title = $"Demand {amount} {resource.resourceName} per turn from {contract.subjectCivName}?";
            description = "They must pay every turn from their stockpile. A standing demand breeds lasting resentment and drains the subject's patience. " + CooldownNote(contract);
            confirmLabel = "Demand";
            success = "Standing resource demand set.";
            lines = new List<PoliticalEffectLine>
            {
                Line("You receive", $"Up to {amount} {resource.resourceName} each turn", beneficial: true),
                Line("Subject resentment", "Rises sharply", harmful: true),
                Line("Tribute exhaustion and opinion", "Worsen every turn", harmful: true),
                Line("Their governors", "Become angry", harmful: true),
            };
        }

        Confirm(title, description, confirmLabel, lines, () =>
            Report(SubjectManager.Instance.TryDemandResources(civ, subject, resource, amount, perTurn,
                SubjectManager.Instance.CurrentTurn), success));
    }

    private void OnReleaseClicked()
    {
        var contract = SelectedContract();
        if (contract == null) return;
        var subject = contract.subject;
        Confirm($"Release {contract.subjectCivName}?",
            "Ends the vassal contract peacefully. You lose their tribute and the subject becomes an independent realm.",
            "Release",
            new List<PoliticalEffectLine>(),
            () => Report(SubjectManager.Instance.TryReleaseSubject(civ, subject, SubjectManager.Instance.CurrentTurn), $"{contract.subjectCivName} has been released."));
    }

    private void OnAcceptDemand()
    {
        var contract = SelectedContract();
        if (contract == null) return;
        var demand = SubjectManager.Instance.GetPendingIndependenceDemand(civ, contract.subject);
        if (demand == null) return;
        Confirm($"Grant independence to {contract.subjectCivName}?",
            "The vassal contract ends peacefully and they remember the gesture favorably.",
            "Grant",
            new List<PoliticalEffectLine>(),
            () => Report(SubjectManager.Instance.AcceptIndependenceDemand(demand, SubjectManager.Instance.CurrentTurn)
                ? SubjectActionResult.Ok() : SubjectActionResult.Fail("The demand could not be accepted."), $"{contract.subjectCivName} is now independent."));
    }

    private void OnRejectDemand()
    {
        var contract = SelectedContract();
        if (contract == null) return;
        var demand = SubjectManager.Instance.GetPendingIndependenceDemand(civ, contract.subject);
        if (demand == null) return;
        Confirm($"Reject {contract.subjectCivName}'s demand?",
            "Rejecting ends the contract and starts a War of Independence.",
            "Reject",
            new List<PoliticalEffectLine> { Line("War of Independence", "Begins immediately", harmful: true) },
            () => Report(SubjectManager.Instance.RejectIndependenceDemand(demand)
                ? SubjectActionResult.Ok() : SubjectActionResult.Fail("The demand could not be rejected."), "The War of Independence has begun."));
    }
}
