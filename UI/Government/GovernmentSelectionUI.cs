using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Government choices plus a detail view (large artwork, effects, requirements with exact failure reasons, council
/// outlook). Selecting a government only previews it; adoption is confirmed and routed through PolicyManager.
/// </summary>
public class GovernmentSelectionUI : GovernmentScreenBase
{
    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GovernmentTypeRowUI rowPrefab;
    [SerializeField] private ScrollRect listScroll;
    [SerializeField] private TMP_Text emptyText;

    [Header("Detail")]
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private Image selectedGovernmentArtwork;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text leaderTitleText;
    [SerializeField] private TMP_Text signatureText;
    [SerializeField] private TMP_Text tradeoffText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private TMP_Text councilStructureText;
    [SerializeField] private TMP_Text electionStructureText;
    [SerializeField] private TMP_Text titlesText;
    [SerializeField] private TMP_Text failureText;
    [SerializeField] private TMP_Text actionStatusText;

    [Header("Detail lists (rows come from the line prefab)")]
    [SerializeField] private PoliticalLineRowUI lineRowPrefab;
    [SerializeField] private Transform effectsRoot;
    [SerializeField] private Transform requirementsRoot;
    [SerializeField] private Transform reactionsRoot;

    [Header("Council outlook")]
    [SerializeField] private CouncilOutlookWidget councilOutlook = new CouncilOutlookWidget();

    [Header("Action")]
    [SerializeField] private Button adoptButton;
    [SerializeField] private TMP_Text adoptButtonLabel;

    private struct Entry
    {
        public GovernmentData government;
        public GovernmentAdoptionEvaluation evaluation;
    }

    private readonly List<GovernmentTypeRowUI> rows = new List<GovernmentTypeRowUI>();
    private PoliticalLineList effects, requirements, reactions;
    private GovernmentData selected;
    private bool wired;

    protected override void OnCivilizationChanged() => selected = null;

    public override void Refresh()
    {
        if (civ == null || PolicyManager.Instance == null) return;
        EnsureWired();

        var entries = BuildEntries();
        if (selected == null || !entries.Any(e => e.government == selected))
            selected = civ.currentGovernment != null ? civ.currentGovernment : entries.Select(e => e.government).FirstOrDefault();

        float scroll = GovernmentUiUtil.CaptureScroll(listScroll);
        GovernmentUiUtil.FillList(listRoot, rowPrefab, rows, entries,
            (row, entry) => row.Bind(entry.government, entry.evaluation, entry.government == selected, Select));
        GovernmentUiUtil.RestoreScroll(listScroll, scroll);
        GovernmentUiUtil.SetText(emptyText, entries.Count == 0 ? "No governments are available yet." : string.Empty);

        RefreshDetail();
    }

    private void EnsureWired()
    {
        if (wired) return;
        wired = true;
        GovernmentUiUtil.SetClick(adoptButton, OnAdoptClicked);
    }

    private List<Entry> BuildEntries()
    {
        var governments = new List<GovernmentData>();
        if (civ.currentGovernment != null) governments.Add(civ.currentGovernment);
        foreach (var g in PolicyManager.Instance.GetUnlockedGovernments(civ))
            if (!governments.Contains(g)) governments.Add(g);

        return governments
            .Select(g => new Entry { government = g, evaluation = PolicyManager.Instance.EvaluateGovernment(civ, g) })
            .OrderBy(e => e.evaluation.isCurrentGovernment ? 0 : e.evaluation.canAdopt ? 1 : 2)
            .ThenBy(e => GovernmentPresentation.NameOf(e.government))
            .ToList();
    }

    private void Select(GovernmentData government)
    {
        selected = government;
        GovernmentUiUtil.SetText(actionStatusText, string.Empty);
        Refresh();
    }

    private void RefreshDetail()
    {
        GovernmentUiUtil.SetActive(detailRoot, selected != null);
        if (selected == null) return;

        var evaluation = PolicyManager.Instance.EvaluateGovernment(civ, selected);
        GovernmentUiUtil.SetImage(selectedGovernmentArtwork, selected.governmentArtwork != null ? selected.governmentArtwork : selected.icon);
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.NameOf(selected));
        GovernmentUiUtil.SetText(descriptionText, selected.description);
        GovernmentUiUtil.SetText(leaderTitleText, string.IsNullOrWhiteSpace(selected.leaderTitleSuffix) ? string.Empty : $"Ruler: {selected.leaderTitleSuffix}");
        GovernmentUiUtil.SetText(signatureText, selected.signatureMechanic);
        GovernmentUiUtil.SetText(tradeoffText, selected.majorTradeoff);
        GovernmentUiUtil.SetText(costText, $"Cost: {selected.policyPointCost} policy points (you have {civ.policyPoints})");
        GovernmentUiUtil.SetText(stateText, evaluation.isCurrentGovernment ? "Current government" : evaluation.canAdopt ? "Available" : "Locked");
        GovernmentUiUtil.SetText(councilStructureText, PoliticalEffectSummaryBuilder.DescribeCouncilStructure(selected));
        GovernmentUiUtil.SetText(electionStructureText, PoliticalEffectSummaryBuilder.DescribeElectionStructure(selected));
        GovernmentUiUtil.SetText(titlesText, DescribeTitles(selected));

        effects ??= new PoliticalLineList(effectsRoot, lineRowPrefab);
        requirements ??= new PoliticalLineList(requirementsRoot, lineRowPrefab);
        reactions ??= new PoliticalLineList(reactionsRoot, lineRowPrefab);
        effects.ShowEffects(PoliticalEffectSummaryBuilder.BuildGovernmentEffects(selected));
        requirements.ShowRequirements(PoliticalEffectSummaryBuilder.BuildGovernmentRequirements(civ, selected));
        reactions.ShowEffects(PoliticalEffectSummaryBuilder.BuildGovernorReactionLines(selected.governorOpinionEffects));

        GovernmentUiUtil.SetText(failureText, evaluation.canAdopt || evaluation.isCurrentGovernment ? string.Empty : string.Join("\n", evaluation.failureReasons));

        if (!evaluation.isCurrentGovernment && civ.HasRoyalCouncil)
            councilOutlook.Show(civ, PolicyManager.Instance.PreviewGovernmentVote(civ, selected));
        else
            councilOutlook.Hide();

        GovernmentUiUtil.SetInteractable(adoptButton, evaluation.canAdopt);
        GovernmentUiUtil.SetText(adoptButtonLabel, evaluation.isCurrentGovernment ? "Current Government" : "Adopt Government");
    }

    private static string DescribeTitles(GovernmentData g)
    {
        string governors = string.IsNullOrWhiteSpace(g.governorTitlePlural) ? GovernmentPresentation.DefaultGovernorPlural : g.governorTitlePlural;
        string council = string.IsNullOrWhiteSpace(g.councilMemberTitle) ? GovernmentPresentation.DefaultCouncilMember : g.councilMemberTitle;
        return g.usesRoyalCouncil ? $"Governors are styled {governors}; council members are {council}." : $"Governors are styled {governors}.";
    }

    private void OnAdoptClicked()
    {
        if (civ == null || selected == null || panel == null || PolicyManager.Instance == null) return;
        var target = selected;
        var evaluation = PolicyManager.Instance.EvaluateGovernment(civ, target);
        if (!evaluation.canAdopt)
        {
            GovernmentUiUtil.SetText(actionStatusText, string.Join("\n", evaluation.failureReasons));
            return;
        }

        var lines = PoliticalEffectSummaryBuilder.BuildGovernmentEffects(target);
        lines.AddRange(PoliticalEffectSummaryBuilder.BuildGovernorReactionLines(target.governorOpinionEffects));
        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Adopt {GovernmentPresentation.NameOf(target)}?",
            description = $"Costs {target.policyPointCost} policy points. Policies that require a different government are repealed automatically.",
            icon = target.icon,
            confirmLabel = "Adopt",
            lines = lines,
            onConfirm = () => Adopt(target),
        });
    }

    private void Adopt(GovernmentData target)
    {
        bool ok = civ != null && PolicyManager.Instance.ChangeGovernment(civ, target);
        GovernmentUiUtil.SetText(actionStatusText, ok ? string.Empty : "The change was rejected (the council voted it down, or the requirements changed).");
        panel?.RefreshAllVisible();
    }
}
