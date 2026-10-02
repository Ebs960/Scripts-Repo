using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Policy browser: Active / Available / Locked filters, category and name filters, a compact list of authored name
/// buttons, and a detail view with effects, exact requirement results, council outlook, and adopt/repeal actions.
/// </summary>
public class GovernmentPoliciesTab : GovernmentTabBase
{
    [Header("Filters")]
    [SerializeField] private GovernmentNavButtonUI activeFilterButton;
    [SerializeField] private GovernmentNavButtonUI availableFilterButton;
    [SerializeField] private GovernmentNavButtonUI lockedFilterButton;
    [SerializeField] private TMP_Dropdown tagFilterDropdown;
    [SerializeField] private TMP_InputField searchInput;

    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private PolicyNameButtonUI rowPrefab;
    [SerializeField] private ScrollRect listScroll;
    [SerializeField] private TMP_Text emptyText;

    [Header("Detail")]
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text tagsText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text failureText;
    [SerializeField] private TMP_Text actionStatusText;

    [Header("Detail lists (rows come from the line prefab)")]
    [SerializeField] private PoliticalLineRowUI lineRowPrefab;
    [SerializeField] private Transform effectsRoot;
    [SerializeField] private Transform requirementsRoot;
    [SerializeField] private Transform relationsRoot;
    [SerializeField] private Transform reactionsRoot;

    [Header("Council outlook")]
    [SerializeField] private CouncilOutlookWidget councilOutlook = new CouncilOutlookWidget();

    [Header("Actions")]
    [SerializeField] private Button adoptButton;
    [SerializeField] private Button repealButton;

    private struct Entry
    {
        public PolicyData policy;
        public PolicyAdoptionEvaluation evaluation;
    }

    private readonly List<PolicyNameButtonUI> rows = new List<PolicyNameButtonUI>();
    private PoliticalLineList effects, requirements, relations, reactions;
    private PolicyListState filter = PolicyListState.Available;
    private PolicyTag? tagFilter;
    private string search = string.Empty;
    private PolicyData selected;
    private bool hasRequestedFocus;
    private bool wired;

    protected override void OnCivilizationChanged()
    {
        if (!hasRequestedFocus) selected = null;
    }

    public void FocusPolicy(PolicyData policy)
    {
        if (policy == null) return;
        selected = policy;
        hasRequestedFocus = true;
        if (civ != null && gameObject.activeInHierarchy) Refresh();
    }

    public override void Refresh()
    {
        if (civ == null || PolicyManager.Instance == null) return;
        EnsureWired();

        if (selected != null && hasRequestedFocus)
        {
            var requestedEvaluation = PolicyManager.Instance.EvaluatePolicy(civ, selected);
            filter = requestedEvaluation.State;
            if (!MatchesTag(selected))
            {
                tagFilter = null;
                if (tagFilterDropdown != null) tagFilterDropdown.SetValueWithoutNotify(0);
            }
            if (!MatchesSearch(selected))
            {
                search = string.Empty;
                if (searchInput != null) searchInput.SetTextWithoutNotify(string.Empty);
            }
            hasRequestedFocus = false;
        }

        var all = new List<Entry>();
        foreach (var policy in PolicyManager.Instance.allPolicies)
        {
            if (policy == null) continue;
            all.Add(new Entry { policy = policy, evaluation = PolicyManager.Instance.EvaluatePolicy(civ, policy) });
        }

        int active = all.Count(e => e.evaluation.State == PolicyListState.Active);
        int available = all.Count(e => e.evaluation.State == PolicyListState.Available);
        int locked = all.Count(e => e.evaluation.State == PolicyListState.Locked);
        if (activeFilterButton != null) activeFilterButton.Bind($"Active ({active})", filter == PolicyListState.Active, () => SetFilter(PolicyListState.Active));
        if (availableFilterButton != null) availableFilterButton.Bind($"Available ({available})", filter == PolicyListState.Available, () => SetFilter(PolicyListState.Available));
        if (lockedFilterButton != null) lockedFilterButton.Bind($"Locked ({locked})", filter == PolicyListState.Locked, () => SetFilter(PolicyListState.Locked));

        var entries = all
            .Where(e => e.evaluation.State == filter && MatchesTag(e.policy) && MatchesSearch(e.policy))
            .OrderBy(e => GovernmentPresentation.NameOf(e.policy))
            .ToList();

        if (selected != null && !entries.Any(e => e.policy == selected)) selected = null;
        if (selected == null && entries.Count > 0) selected = entries[0].policy;

        float scroll = GovernmentUiUtil.CaptureScroll(listScroll);
        GovernmentUiUtil.FillList(listRoot, rowPrefab, rows, entries,
            (row, entry) => row.Bind(entry.policy, entry.evaluation, entry.policy == selected, Select));
        GovernmentUiUtil.RestoreScroll(listScroll, scroll);
        GovernmentUiUtil.SetText(emptyText, entries.Count == 0 ? EmptyMessage() : string.Empty);

        RefreshDetail();
    }

    private void EnsureWired()
    {
        if (wired) return;
        wired = true;

        if (tagFilterDropdown != null)
        {
            tagFilterDropdown.ClearOptions();
            var options = new List<string> { "All Categories" };
            options.AddRange(Enum.GetNames(typeof(PolicyTag)));
            tagFilterDropdown.AddOptions(options);
            tagFilterDropdown.SetValueWithoutNotify(0);
            tagFilterDropdown.onValueChanged.AddListener(OnTagChanged);
        }
        if (searchInput != null) searchInput.onValueChanged.AddListener(OnSearchChanged);
        GovernmentUiUtil.SetClick(adoptButton, OnAdoptClicked);
        GovernmentUiUtil.SetClick(repealButton, OnRepealClicked);
    }

    private void OnTagChanged(int index)
    {
        tagFilter = index <= 0 ? (PolicyTag?)null : (PolicyTag)(index - 1);
        Refresh();
    }

    private void OnSearchChanged(string text)
    {
        search = text ?? string.Empty;
        Refresh();
    }

    private void SetFilter(PolicyListState state)
    {
        filter = state;
        selected = null;
        Refresh();
    }

    private bool MatchesTag(PolicyData policy)
        => !tagFilter.HasValue || (policy.policyTags != null && policy.policyTags.Contains(tagFilter.Value));

    private bool MatchesSearch(PolicyData policy)
        => string.IsNullOrWhiteSpace(search)
           || GovernmentPresentation.NameOf(policy).IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

    private string EmptyMessage()
    {
        switch (filter)
        {
            case PolicyListState.Active: return "No policies are active.";
            case PolicyListState.Available: return "No policies are available to adopt.";
            default: return "No locked policies match the current filters.";
        }
    }

    private void Select(PolicyData policy)
    {
        selected = policy;
        GovernmentUiUtil.SetText(actionStatusText, string.Empty);
        Refresh();
    }

    private void RefreshDetail()
    {
        GovernmentUiUtil.SetActive(detailRoot, selected != null);
        if (selected == null) return;

        var evaluation = PolicyManager.Instance.EvaluatePolicy(civ, selected);
        GovernmentUiUtil.SetImage(iconImage, selected.icon);
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.NameOf(selected));
        GovernmentUiUtil.SetText(descriptionText, selected.description);
        GovernmentUiUtil.SetText(tagsText, PoliticalEffectSummaryBuilder.FormatPolicyTags(selected));
        GovernmentUiUtil.SetText(costText, $"Cost: {selected.policyPointCost} policy points (you have {civ.policyPoints})");
        GovernmentUiUtil.SetText(statusText, evaluation.alreadyActive ? "Active" : evaluation.canAdopt ? "Available" : "Locked");
        GovernmentUiUtil.SetText(failureText, evaluation.alreadyActive || evaluation.canAdopt ? string.Empty : string.Join("\n", evaluation.failureReasons));

        effects ??= new PoliticalLineList(effectsRoot, lineRowPrefab);
        requirements ??= new PoliticalLineList(requirementsRoot, lineRowPrefab);
        relations ??= new PoliticalLineList(relationsRoot, lineRowPrefab);
        reactions ??= new PoliticalLineList(reactionsRoot, lineRowPrefab);
        effects.ShowEffects(PoliticalEffectSummaryBuilder.BuildPolicyEffects(selected));
        requirements.ShowRequirements(PoliticalEffectSummaryBuilder.BuildPolicyRequirements(civ, selected));
        relations.ShowTexts(BuildRelationLines(selected));
        reactions.ShowEffects(PoliticalEffectSummaryBuilder.BuildGovernorReactionLines(selected.governorOpinionEffects));

        if (civ.HasRoyalCouncil)
            councilOutlook.Show(civ, PolicyManager.Instance.PreviewPolicyVote(civ, selected, evaluation.alreadyActive));
        else
            councilOutlook.Hide();

        GovernmentUiUtil.SetActive(adoptButton != null ? adoptButton.gameObject : null, !evaluation.alreadyActive);
        GovernmentUiUtil.SetActive(repealButton != null ? repealButton.gameObject : null, evaluation.alreadyActive);
        GovernmentUiUtil.SetInteractable(adoptButton, evaluation.canAdopt);
        GovernmentUiUtil.SetInteractable(repealButton, evaluation.alreadyActive);
    }

    private static List<string> BuildRelationLines(PolicyData policy)
    {
        var lines = new List<string>();
        AddRelation(lines, "Requires policy", policy.requiredPolicies);
        AddRelation(lines, "Conflicts with", policy.incompatiblePolicies);
        AddRelation(lines, "Replaces", policy.supersedesPolicies);
        return lines;
    }

    private static void AddRelation(List<string> lines, string label, PolicyData[] policies)
    {
        if (policies == null) return;
        var names = policies.Where(p => p != null).Select(GovernmentPresentation.NameOf).ToList();
        if (names.Count > 0) lines.Add($"{label}: {string.Join(", ", names)}");
    }

    private void OnAdoptClicked()
    {
        if (civ == null || selected == null || panel == null || PolicyManager.Instance == null) return;
        var target = selected;
        var evaluation = PolicyManager.Instance.EvaluatePolicy(civ, target);
        if (!evaluation.canAdopt)
        {
            GovernmentUiUtil.SetText(actionStatusText, string.Join("\n", evaluation.failureReasons));
            return;
        }

        var lines = PoliticalEffectSummaryBuilder.BuildPolicyEffects(target);
        lines.AddRange(PoliticalEffectSummaryBuilder.BuildGovernorReactionLines(target.governorOpinionEffects));
        string description = $"Costs {target.policyPointCost} policy points.";
        if (target.supersedesPolicies != null && target.supersedesPolicies.Any(p => p != null && civ.activePolicies.Contains(p)))
            description += " Policies it replaces are repealed.";

        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Adopt {GovernmentPresentation.NameOf(target)}?",
            description = description,
            icon = target.icon,
            confirmLabel = "Adopt",
            lines = lines,
            onConfirm = () => Adopt(target),
        });
    }

    private void Adopt(PolicyData target)
    {
        bool ok = civ != null && PolicyManager.Instance.AdoptPolicy(civ, target);
        GovernmentUiUtil.SetText(actionStatusText, ok ? string.Empty : "The policy was rejected (the council voted it down, or the requirements changed).");
        panel?.RefreshAllVisible();
    }

    private void OnRepealClicked()
    {
        if (civ == null || selected == null || panel == null || PolicyManager.Instance == null) return;
        var target = selected;
        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Repeal {GovernmentPresentation.NameOf(target)}?",
            description = "Repealing a policy does not refund its policy points. Its bonuses end immediately.",
            icon = target.icon,
            confirmLabel = "Repeal",
            lines = new List<PoliticalEffectLine>(),
            onConfirm = () => Repeal(target),
        });
    }

    private void Repeal(PolicyData target)
    {
        bool ok = civ != null && PolicyManager.Instance.RevokePolicy(civ, target);
        GovernmentUiUtil.SetText(actionStatusText, ok ? string.Empty : "The council voted against repealing this policy.");
        panel?.RefreshAllVisible();
    }
}
