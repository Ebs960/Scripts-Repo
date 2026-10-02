using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Informational summary of the empire's political state plus a data-driven attention list.</summary>
public class GovernmentOverviewTab : GovernmentTabBase
{
    [Header("Government")]
    [SerializeField] private TMP_Text governmentNameText;
    [SerializeField] private Image governmentIconImage;
    [SerializeField] private Image governmentArtworkImage;
    [SerializeField] private TMP_Text leaderTitleText;
    [SerializeField] private TMP_Text signatureText;
    [SerializeField] private TMP_Text tradeoffText;
    [SerializeField] private TMP_Text institutionText;
    [SerializeField] private Button governmentButton;
    [SerializeField] private Button politicalAffairsButton;

    [Header("Drill-down Tabs")]
    [SerializeField] private GovernmentEntityActionPopup actionPopup;
    [SerializeField] private GovernmentGovernorsTab governorsTab;
    [SerializeField] private GovernmentVassalsTab vassalsTab;
    [SerializeField] private GovernmentPoliciesTab policiesTab;

    [Header("Governors Summary")]
    [SerializeField] private Transform governorsRoot;
    [SerializeField] private GovernmentSummaryRowUI governorRowPrefab;
    [SerializeField] private ScrollRect governorsScroll;
    [SerializeField] private TMP_Text noGovernorsText;

    [Header("Vassal Summary")]
    [SerializeField] private Transform vassalsRoot;
    [SerializeField] private GovernmentSummaryRowUI vassalRowPrefab;
    [SerializeField] private ScrollRect vassalsScroll;
    [SerializeField] private TMP_Text noVassalsText;

    [Serializable]
    private class PolicyAreaBlock
    {
        public PolicyTag tag;
        public TMP_Text headerText;
        public Transform listRoot;
        public PolicyNameButtonUI rowPrefab;
        public GameObject root;
    }

    [Header("Policy Areas")]
    [SerializeField] private List<PolicyAreaBlock> policyAreas = new List<PolicyAreaBlock>();

    [Header("Counts")]
    [SerializeField] private TMP_Text policyPointsText;
    [SerializeField] private TMP_Text governorCountText;
    [SerializeField] private TMP_Text councilSeatsText;
    [SerializeField] private TMP_Text vassalCountText;
    [SerializeField] private TMP_Text factionCountText;

    [Header("Elections (hidden when the government has none)")]
    [SerializeField] private GameObject electionRoot;
    [SerializeField] private TMP_Text approvalText;
    [SerializeField] private TMP_Text legitimacyText;
    [SerializeField] private TMP_Text officeholderText;
    [SerializeField] private TMP_Text nextElectionText;
    [SerializeField] private TMP_Text electionStatusText;

    [Header("Attention / Political Warnings")]
    [SerializeField] private Transform warningsRoot;
    [SerializeField] private PoliticalLineRowUI warningRowPrefab;
    [SerializeField] private GameObject noWarningsRoot;

    private PoliticalLineList warnings;
    private readonly List<GovernmentSummaryRowUI> governorRows = new List<GovernmentSummaryRowUI>();
    private readonly List<GovernmentSummaryRowUI> vassalRows = new List<GovernmentSummaryRowUI>();
    private readonly Dictionary<PolicyAreaBlock, List<PolicyNameButtonUI>> policyRows =
        new Dictionary<PolicyAreaBlock, List<PolicyNameButtonUI>>();
    private bool wired;

    public override void Refresh()
    {
        if (civ == null) return;
        EnsureWired();
        var government = civ.currentGovernment;

        GovernmentUiUtil.SetText(governmentNameText, government != null ? GovernmentPresentation.NameOf(government) : "No government");
        GovernmentUiUtil.SetImage(governmentIconImage, government != null ? government.icon : null);
        GovernmentUiUtil.SetImage(governmentArtworkImage, government != null ? government.governmentArtwork : null);
        GovernmentUiUtil.SetText(leaderTitleText, GovernmentPresentation.GetLeaderTitle(civ));
        GovernmentUiUtil.SetText(signatureText, government != null ? government.signatureMechanic : string.Empty);
        GovernmentUiUtil.SetText(tradeoffText, government != null ? government.majorTradeoff : string.Empty);
        GovernmentUiUtil.SetText(institutionText, $"Institution: {GovernmentPresentation.GetInstitutionName(civ)}");

        GovernmentUiUtil.SetText(policyPointsText, $"Policy points: {civ.policyPoints}");
        GovernmentUiUtil.SetText(governorCountText, GovernmentPresentation.FormatGovernorCap(civ));
        GovernmentUiUtil.SetText(councilSeatsText, civ.HasRoyalCouncil
            ? $"{GovernmentPresentation.FormatCouncilSeats(civ)} ({civ.MaxCouncilSeats - civ.royalCouncil.Count} free)"
            : string.Empty);
        GovernmentUiUtil.SetText(vassalCountText, $"Vassals: {civ.ActiveVassalCount}");
        GovernmentUiUtil.SetText(factionCountText, government != null && government.suppressConventionalPolitics
            ? "Factions: suppressed"
            : $"Factions: {civ.nobleFactions?.Count ?? 0}");

        RefreshElection(government);
        RefreshWarnings();
        RefreshGovernors();
        RefreshVassals();
        RefreshPolicyAreas();
    }

    private void EnsureWired()
    {
        if (wired) return;
        wired = true;
        GovernmentUiUtil.SetClick(governmentButton, () => panel?.OpenTab(GovernmentTab.Government));
        GovernmentUiUtil.SetClick(politicalAffairsButton, () => panel?.OpenTab(GovernmentTab.Politics));
    }

    private void RefreshGovernors()
    {
        var governors = civ.governors.Where(governor => governor != null).ToList();
        float scroll = GovernmentUiUtil.CaptureScroll(governorsScroll);
        GovernmentUiUtil.FillList(governorsRoot, governorRowPrefab, governorRows, governors, (row, governor) =>
        {
            string traits = string.Join(", ", governor.PersonalityTraits.Take(2));
            string faith = governor.PersonalReligion != null
                ? GovernmentPresentation.NameOf(governor.PersonalReligion) : "none";
            string secondary = string.IsNullOrEmpty(traits) ? $"Faith: {faith}" : $"{traits} • Faith: {faith}";
            row.Bind(
                GovernorPortraitService.GetSprite(governor.PortraitId),
                GovernmentPresentation.FormatGovernorName(civ, governor),
                $"Loyalty {GovernmentUiUtil.Signed(governor.Opinion)} • {governor.Cities.Count} Cities",
                secondary,
                governor.IsInRebellion || governor.Opinion < 0,
                () =>
                {
                    actionPopup?.ShowGovernor(civ, governor, panel);
                });
        });
        GovernmentUiUtil.RestoreScroll(governorsScroll, scroll);
        GovernmentUiUtil.SetText(noGovernorsText, governors.Count == 0 ? "No governors." : string.Empty);
    }

    private void RefreshVassals()
    {
        var manager = SubjectManager.Instance;
        var contracts = manager != null ? manager.GetSubjects(civ) : new List<VassalContract>();
        float scroll = GovernmentUiUtil.CaptureScroll(vassalsScroll);
        GovernmentUiUtil.FillList(vassalsRoot, vassalRowPrefab, vassalRows, contracts, (row, contract) =>
        {
            var subject = contract.subject;
            string faith = subject?.StateReligion != null
                ? GovernmentPresentation.NameOf(subject.StateReligion) : "none";
            row.Bind(
                subject?.civData?.icon,
                contract.subjectCivName,
                $"Opinion {GovernmentUiUtil.Signed(manager.GetEffectiveSubjectOpinion(contract))} • Liberty {contract.libertyDesire:0} • {subject?.cities?.Count ?? 0} Cities",
                $"Faith: {faith}",
                contract.libertyDesire >= contract.EffectiveBreakawayThreshold * 0.75f,
                () =>
                {
                    actionPopup?.ShowVassal(civ, contract, panel);
                });
        });
        GovernmentUiUtil.RestoreScroll(vassalsScroll, scroll);
        GovernmentUiUtil.SetText(noVassalsText, contracts.Count == 0 ? "You have no vassals." : string.Empty);
    }

    private void RefreshPolicyAreas()
    {
        var manager = PolicyManager.Instance;
        foreach (var block in policyAreas)
        {
            if (block == null) continue;
            GovernmentUiUtil.SetText(block.headerText, PolicyAreaName(block.tag));
            if (!policyRows.TryGetValue(block, out var rows))
            {
                rows = new List<PolicyNameButtonUI>();
                policyRows.Add(block, rows);
            }

            var entries = manager == null
                ? new List<KeyValuePair<PolicyData, PolicyAdoptionEvaluation>>()
                : manager.allPolicies
                    .Where(policy => policy != null && policy.policyTags != null && policy.policyTags.Contains(block.tag))
                    .Select(policy => new KeyValuePair<PolicyData, PolicyAdoptionEvaluation>(policy, manager.EvaluatePolicy(civ, policy)))
                    .Where(entry => entry.Value.State == PolicyListState.Active || entry.Value.State == PolicyListState.Available)
                    .OrderBy(entry => entry.Value.State == PolicyListState.Active ? 0 : 1)
                    .ThenBy(entry => GovernmentPresentation.NameOf(entry.Key))
                    .ToList();

            GovernmentUiUtil.FillList(block.listRoot, block.rowPrefab, rows, entries, (row, entry) =>
                row.Bind(entry.Key, entry.Value, false, policy =>
                {
                    policiesTab?.FocusPolicy(policy);
                    panel?.OpenTab(GovernmentTab.Policies);
                }));
            GovernmentUiUtil.SetActive(block.root, entries.Count > 0);
        }
    }

    private static string PolicyAreaName(PolicyTag tag)
    {
        switch (tag)
        {
            case PolicyTag.Rights: return "Civil Rights";
            case PolicyTag.Law: return "Law & Justice";
            case PolicyTag.Digital: return "Digital Policy";
            case PolicyTag.Synthetic: return "Synthetic Life";
            case PolicyTag.Space: return "Space & Planetary";
            default: return tag.ToString();
        }
    }

    private void RefreshElection(GovernmentData government)
    {
        var rules = government != null ? government.electionRules : null;
        bool elections = rules != null && rules.enabled;
        GovernmentUiUtil.SetActive(electionRoot, elections);
        if (!elections) return;

        var state = civ.electionState ?? new ElectionState();
        GovernmentUiUtil.SetText(approvalText, $"Public approval {state.publicApproval:0}%");
        GovernmentUiUtil.SetText(legitimacyText, $"Legitimacy {state.governmentLegitimacy:0}%");
        GovernmentUiUtil.SetText(officeholderText, state.currentOffice != null
            ? $"{state.currentOffice.title}: {state.currentOffice.officeholderName}" : "No officeholder");
        GovernmentUiUtil.SetText(nextElectionText, state.nextElectionTurn >= 0 ? $"Next election: turn {state.nextElectionTurn}" : "No election scheduled");
        var active = state.activeElection;
        GovernmentUiUtil.SetText(electionStatusText, active != null && !active.resolved
            ? $"Election underway ({active.candidates.Count} candidates)" : "No active election");
    }

    private void RefreshWarnings()
    {
        warnings ??= new PoliticalLineList(warningsRoot, warningRowPrefab);
        var list = PoliticalWarningBuilder.Build(civ);
        warnings.Show(list, (row, warning) => row.BindWarning(warning, tab => panel?.OpenTab(tab)));
        GovernmentUiUtil.SetActive(noWarningsRoot, list.Count == 0);
    }
}
