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

    public override void Refresh()
    {
        if (civ == null) return;
        var government = civ.currentGovernment;

        GovernmentUiUtil.SetText(governmentNameText, government != null ? GovernmentPresentation.NameOf(government) : "No government");
        GovernmentUiUtil.SetImage(governmentIconImage, government != null ? government.icon : null);
        GovernmentUiUtil.SetImage(governmentArtworkImage, government != null ? government.governmentArtwork : null);
        GovernmentUiUtil.SetText(leaderTitleText, GovernmentPresentation.GetLeaderTitle(civ));
        GovernmentUiUtil.SetText(signatureText, government != null ? government.signatureMechanic : string.Empty);
        GovernmentUiUtil.SetText(tradeoffText, government != null ? government.majorTradeoff : string.Empty);
        GovernmentUiUtil.SetText(institutionText, civ.HasRoyalCouncil
            ? PoliticalEffectSummaryBuilder.DescribeCouncilStructure(government)
            : $"No {GovernmentPresentation.GetInstitutionName(civ).ToLowerInvariant()} votes on decisions.");

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
