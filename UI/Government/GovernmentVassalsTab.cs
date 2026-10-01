using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlord management of vassal contracts: tribute, autonomy, military obligation, religious policy, release, and
/// independence demands. Every change goes through SubjectManager and is confirmed with its consequences first.
/// </summary>
public class GovernmentVassalsTab : GovernmentTabBase
{
    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private VassalRowUI rowPrefab;
    [SerializeField] private ScrollRect listScroll;
    [SerializeField] private TMP_Text emptyText;

    [Header("Detail")]
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private TMP_Text subjectNameText;
    [SerializeField] private TMP_Text libertyText;
    [SerializeField] private TMP_Text opinionText;
    [SerializeField] private TMP_Text resentmentText;
    [SerializeField] private TMP_Text tributeExhaustionText;
    [SerializeField] private TMP_Text militaryConfidenceText;
    [SerializeField] private TMP_Text contractAgeText;
    [SerializeField] private TMP_Text cooldownText;
    [SerializeField] private TMP_Text statusText;

    [Header("Tribute terms")]
    [SerializeField] private Slider goldSlider;
    [SerializeField] private Slider scienceSlider;
    [SerializeField] private Slider foodSlider;
    [SerializeField] private TMP_Text goldValueText;
    [SerializeField] private TMP_Text scienceValueText;
    [SerializeField] private TMP_Text foodValueText;
    [SerializeField] private Button applyTributeButton;

    [Header("Autonomy")]
    [SerializeField] private Slider autonomySlider;
    [SerializeField] private TMP_Text autonomyValueText;
    [SerializeField] private Button applyAutonomyButton;

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
    private Civilization selectedSubject;
    private bool wired;

    protected override void OnCivilizationChanged() => selectedSubject = null;

    public override void Refresh()
    {
        if (civ == null || SubjectManager.Instance == null) return;
        EnsureWired();

        var contracts = SubjectManager.Instance.GetSubjects(civ);
        var selected = contracts.FirstOrDefault(c => c.subject == selectedSubject) ?? contracts.FirstOrDefault();
        selectedSubject = selected?.subject;

        float scroll = GovernmentUiUtil.CaptureScroll(listScroll);
        GovernmentUiUtil.FillList(listRoot, rowPrefab, rows, contracts,
            (row, contract) => row.Bind(contract, contract == selected, IsRestless(contract), SelectContract));
        GovernmentUiUtil.RestoreScroll(listScroll, scroll);
        GovernmentUiUtil.SetText(emptyText, contracts.Count == 0 ? "You have no vassals." : string.Empty);

        GovernmentUiUtil.SetActive(detailRoot, selected != null);
        if (selected != null) RefreshDetail(selected);
    }

    private static bool IsRestless(VassalContract contract)
        => contract != null && contract.libertyDesire >= contract.EffectiveBreakawayThreshold * 0.75f;

    private void EnsureWired()
    {
        if (wired) return;
        wired = true;

        ConfigureSlider(goldSlider, 0f, SubjectManager.MaxTributePct, false);
        ConfigureSlider(scienceSlider, 0f, SubjectManager.MaxTributePct, false);
        ConfigureSlider(foodSlider, 0f, SubjectManager.MaxTributePct, false);
        ConfigureSlider(autonomySlider, 0f, 100f, true);
        ConfigureSlider(obligationSlider, 0f, SubjectManager.MaxMilitaryObligation, true);

        if (goldSlider != null) goldSlider.onValueChanged.AddListener(_ => UpdateSliderLabels());
        if (scienceSlider != null) scienceSlider.onValueChanged.AddListener(_ => UpdateSliderLabels());
        if (foodSlider != null) foodSlider.onValueChanged.AddListener(_ => UpdateSliderLabels());
        if (autonomySlider != null) autonomySlider.onValueChanged.AddListener(_ => UpdateSliderLabels());
        if (obligationSlider != null) obligationSlider.onValueChanged.AddListener(_ => UpdateSliderLabels());

        if (religionRuleDropdown != null)
        {
            religionRuleDropdown.ClearOptions();
            religionRuleDropdown.AddOptions(new List<string>
            {
                "Full Tolerance", "Limited Tolerance", "State Religion Required", "Forced Conversion",
            });
        }

        GovernmentUiUtil.SetClick(applyTributeButton, OnApplyTribute);
        GovernmentUiUtil.SetClick(applyAutonomyButton, OnApplyAutonomy);
        GovernmentUiUtil.SetClick(applyObligationButton, OnApplyObligation);
        GovernmentUiUtil.SetClick(applyReligionButton, OnApplyReligion);
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
        var manager = SubjectManager.Instance;
        int turn = manager.CurrentTurn;
        float opinion = manager.GetEffectiveSubjectOpinion(contract);

        GovernmentUiUtil.SetText(subjectNameText, contract.subjectCivName);
        GovernmentUiUtil.SetText(libertyText, $"Liberty desire {contract.libertyDesire:0} / {contract.EffectiveBreakawayThreshold:0}");
        GovernmentUiUtil.SetText(opinionText, $"Opinion of you {GovernmentUiUtil.Signed(opinion)}");
        GovernmentUiUtil.SetText(resentmentText, $"Resentment {contract.resentment:0}");
        GovernmentUiUtil.SetText(tributeExhaustionText, $"Tribute exhaustion {contract.tributeExhaustion:0}");
        GovernmentUiUtil.SetText(militaryConfidenceText, $"Military confidence {contract.militaryConfidence:0}");
        GovernmentUiUtil.SetText(contractAgeText, $"{(contract.isCapitulated ? "Capitulated" : "Voluntary")} vassal for {Mathf.Max(0, turn - contract.contractStartTurn)} turns");

        var check = manager.CanInterfere(civ, contract.subject, turn);
        GovernmentUiUtil.SetText(cooldownText, check.success ? "Terms can be changed now." : check.reason);
        GovernmentUiUtil.SetInteractable(applyTributeButton, check.success);
        GovernmentUiUtil.SetInteractable(applyAutonomyButton, check.success);
        GovernmentUiUtil.SetInteractable(applyObligationButton, check.success);
        GovernmentUiUtil.SetInteractable(applyReligionButton, check.success);

        // Reset editors to the live contract values each refresh so they never show stale proposals.
        if (goldSlider != null) goldSlider.SetValueWithoutNotify(contract.goldTributePct);
        if (scienceSlider != null) scienceSlider.SetValueWithoutNotify(contract.scienceTributePct);
        if (foodSlider != null) foodSlider.SetValueWithoutNotify(contract.foodTributePct);
        if (autonomySlider != null) autonomySlider.SetValueWithoutNotify(contract.autonomyLevel);
        if (obligationSlider != null) obligationSlider.SetValueWithoutNotify(contract.militaryObligationCount);
        if (religionRuleDropdown != null) religionRuleDropdown.SetValueWithoutNotify((int)contract.religionRule);
        UpdateSliderLabels();

        var demand = manager.GetPendingIndependenceDemand(civ, contract.subject);
        GovernmentUiUtil.SetActive(demandRoot, demand != null);
        if (demand != null)
            GovernmentUiUtil.SetText(demandText, $"{contract.subjectCivName} demands full independence (since turn {demand.turnIssued}).");
        GovernmentUiUtil.SetInteractable(releaseButton, true);
    }

    private void UpdateSliderLabels()
    {
        GovernmentUiUtil.SetText(goldValueText, goldSlider != null ? $"{goldSlider.value:P0}" : string.Empty);
        GovernmentUiUtil.SetText(scienceValueText, scienceSlider != null ? $"{scienceSlider.value:P0}" : string.Empty);
        GovernmentUiUtil.SetText(foodValueText, foodSlider != null ? $"{foodSlider.value:P0}" : string.Empty);
        GovernmentUiUtil.SetText(autonomyValueText, autonomySlider != null ? $"{Mathf.RoundToInt(autonomySlider.value)}" : string.Empty);
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
        if (contract == null || goldSlider == null || scienceSlider == null || foodSlider == null) return;
        float gold = goldSlider.value, science = scienceSlider.value, food = foodSlider.value;
        float change = (gold + science + food) - (contract.goldTributePct + contract.scienceTributePct + contract.foodTributePct);
        if (Mathf.Approximately(gold, contract.goldTributePct)
            && Mathf.Approximately(science, contract.scienceTributePct)
            && Mathf.Approximately(food, contract.foodTributePct))
        {
            GovernmentUiUtil.SetText(statusText, "Tribute terms are unchanged.");
            return;
        }

        var subject = contract.subject;
        var lines = new List<PoliticalEffectLine>
        {
            Line("Gold tribute", $"{contract.goldTributePct:P0} -> {gold:P0}"),
            Line("Science tribute", $"{contract.scienceTributePct:P0} -> {science:P0}"),
            Line("Food tribute", $"{contract.foodTributePct:P0} -> {food:P0}"),
            change > 0f
                ? Line("Their governors", "Resent a heavier burden", harmful: true)
                : Line("Their opinion and liberty", "Opinion rises, liberty desire falls", beneficial: true),
            Line("Resentment", "Any change adds resentment", harmful: true),
        };
        Confirm($"Change tribute from {contract.subjectCivName}?", CooldownNote(contract), "Apply", lines, () =>
            Report(SubjectManager.Instance.TrySetTributeTerms(civ, subject, gold, science, food, SubjectManager.Instance.CurrentTurn), "Tribute terms updated."));
    }

    private void OnApplyAutonomy()
    {
        var contract = SelectedContract();
        if (contract == null || autonomySlider == null) return;
        int autonomy = Mathf.RoundToInt(autonomySlider.value);
        int delta = autonomy - contract.autonomyLevel;
        if (delta == 0) { GovernmentUiUtil.SetText(statusText, "Autonomy is unchanged."); return; }

        var subject = contract.subject;
        var lines = new List<PoliticalEffectLine>
        {
            Line("Autonomy", $"{contract.autonomyLevel} -> {autonomy}"),
            delta > 0
                ? Line("Their opinion and liberty", "Opinion rises, liberty desire falls", beneficial: true)
                : Line("Resentment", "Grows with every point removed", harmful: true),
        };
        if (delta <= -10) lines.Add(Line("Their governors", "Lose a privilege and gain a grievance", harmful: true));
        Confirm($"Change autonomy of {contract.subjectCivName}?", CooldownNote(contract), "Apply", lines, () =>
            Report(SubjectManager.Instance.TrySetAutonomy(civ, subject, autonomy, SubjectManager.Instance.CurrentTurn), "Autonomy updated."));
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
            lines.Add(Line("Forced conversion", "Heavy liberty desire growth every turn", harmful: true));
        Confirm($"Change religious policy for {contract.subjectCivName}?", CooldownNote(contract), "Apply", lines, () =>
            Report(SubjectManager.Instance.TrySetReligionRule(civ, subject, rule, SubjectManager.Instance.CurrentTurn), "Religious policy updated."));
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
