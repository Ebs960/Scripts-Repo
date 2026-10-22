using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Political Affairs: pending events, noble factions and their demands, recent council votes, and elections.
/// Absorbs what the standalone Political Affairs panel used to show.
/// </summary>
public class GovernmentPoliticsUI : GovernmentScreenBase
{
    private const int MaxVoteHistory = 8;

    [Header("Suppressed politics")]
    [SerializeField] private GameObject suppressedNoticeRoot;
    [SerializeField] private TMP_Text suppressedNoticeText;

    [Header("Events")]
    [SerializeField] private Transform eventsRoot;
    [SerializeField] private PoliticalEventRowUI eventRowPrefab;
    [SerializeField] private TMP_Text noEventsText;

    [Header("Factions")]
    [SerializeField] private GameObject factionsSectionRoot;
    [SerializeField] private Transform factionListRoot;
    [SerializeField] private FactionRowUI factionRowPrefab;
    [SerializeField] private TMP_Text noFactionsText;
    [SerializeField] private GameObject factionDetailRoot;
    [SerializeField] private TMP_Text factionNameText;
    [SerializeField] private TMP_Text factionAlignmentText;
    [SerializeField] private TMP_Text factionLeaderText;
    [SerializeField] private TMP_Text factionPowerText;
    [SerializeField] private TMP_Text factionReligionText;
    [SerializeField] private TMP_Text factionRebellionText;
    [SerializeField] private PoliticalLineRowUI lineRowPrefab;
    [SerializeField] private Transform factionMembersRoot;
    [SerializeField] private Transform demandsRoot;
    [SerializeField] private FactionDemandRowUI demandRowPrefab;
    [SerializeField] private TMP_Text noDemandsText;

    [Header("Council votes")]
    [SerializeField] private GameObject votesSectionRoot;
    [SerializeField] private Transform votesRoot;
    [SerializeField] private CouncilVoteRowUI voteRowPrefab;
    [SerializeField] private TMP_Text noVotesText;

    [Header("Election")]
    [SerializeField] private GameObject electionSectionRoot;
    [SerializeField] private TMP_Text electionSummaryText;
    [SerializeField] private Image currentRulerPortrait;
    [SerializeField] private Slider publicOpinionSlider;
    [SerializeField] private TMP_Text publicOpinionText;
    [SerializeField] private TMP_Text victoryThresholdText;
    [SerializeField] private TMP_Text legitimacyText;
    [SerializeField] private TMP_Text opinionModifiersText;
    [SerializeField] private TMP_Text electionHistoryText;
    [SerializeField] private Transform electionIssuesRoot;
    [SerializeField] private Transform candidatesRoot;
    [SerializeField] private ElectionCandidateRowUI candidateRowPrefab;
    [SerializeField] private TMP_Text electionStatusText;

    private readonly List<PoliticalEventRowUI> eventRows = new List<PoliticalEventRowUI>();
    private readonly List<FactionRowUI> factionRows = new List<FactionRowUI>();
    private readonly List<FactionDemandRowUI> demandRows = new List<FactionDemandRowUI>();
    private readonly List<CouncilVoteRowUI> voteRows = new List<CouncilVoteRowUI>();
    private readonly List<ElectionCandidateRowUI> candidateRows = new List<ElectionCandidateRowUI>();
    private readonly Dictionary<FactionDemand, string> demandFailures = new Dictionary<FactionDemand, string>();
    private PoliticalLineList factionMembers, electionIssues;
    private FactionBloc selectedFaction;

    protected override void OnCivilizationChanged()
    {
        selectedFaction = null;
        demandFailures.Clear();
    }

    public override void Refresh()
    {
        if (civ == null) return;

        bool suppressed = civ.currentGovernment != null && civ.currentGovernment.suppressConventionalPolitics;
        GovernmentUiUtil.SetActive(suppressedNoticeRoot, suppressed);
        if (suppressed)
            GovernmentUiUtil.SetText(suppressedNoticeText, $"{GovernmentPresentation.NameOf(civ.currentGovernment)} suppresses conventional politics; factions and council votes are inactive.");

        RefreshEvents();
        GovernmentUiUtil.SetActive(factionsSectionRoot, true);
        RefreshFactions();
        RefreshVotes();
        RefreshElection();
    }

    // ── Events ──

    private void RefreshEvents()
    {
        var events = PoliticalEventManager.Instance != null
            ? PoliticalEventManager.Instance.GetActiveEventsForCiv(civ)
            : (IReadOnlyList<PoliticalEventRecord>)new List<PoliticalEventRecord>();
        GovernmentUiUtil.FillList(eventsRoot, eventRowPrefab, eventRows, events, (row, record) => row.Bind(record, OnEventOption));
        GovernmentUiUtil.SetText(noEventsText, events.Count == 0 ? "No political events need your attention." : string.Empty);
    }

    private void OnEventOption(int eventId, int optionIndex)
    {
        PoliticalEventManager.Instance?.ResolveEvent(eventId, optionIndex);
        panel?.RefreshAllVisible();
    }

    // ── Factions ──

    private void RefreshFactions()
    {
        var factions = civ.nobleFactions.Where(f => f != null).ToList();
        if (selectedFaction == null || !factions.Contains(selectedFaction)) selectedFaction = factions.FirstOrDefault();

        GovernmentUiUtil.FillList(factionListRoot, factionRowPrefab, factionRows, factions,
            (row, faction) => row.Bind(civ, faction, faction == selectedFaction, SelectFaction));
        bool suppressed = civ.currentGovernment != null && civ.currentGovernment.suppressConventionalPolitics;
        GovernmentUiUtil.SetText(noFactionsText, factions.Count == 0
            ? suppressed ? "Conventional political factions are inactive under this government." : "No political factions currently exist."
            : string.Empty);
        GovernmentUiUtil.SetActive(factionDetailRoot, selectedFaction != null);
        if (selectedFaction != null) RefreshFactionDetail(selectedFaction);
    }

    private void SelectFaction(FactionBloc faction)
    {
        selectedFaction = faction;
        Refresh();
    }

    private void RefreshFactionDetail(FactionBloc faction)
    {
        GovernmentUiUtil.SetText(factionNameText, faction.FactionName);
        GovernmentUiUtil.SetText(factionAlignmentText, $"Alignment: {faction.Alignment}");
        GovernmentUiUtil.SetText(factionLeaderText, faction.Leader != null ? $"Leader: {GovernmentPresentation.FormatGovernorName(civ, faction.Leader)}" : "No leader");
        GovernmentUiUtil.SetText(factionPowerText, $"Power {faction.ComputePower():0.#} ({faction.Members.Count} members)");
        GovernmentUiUtil.SetText(factionReligionText, faction.ReligiousIdentity != null
            ? $"Faith: {GovernmentPresentation.NameOf(faction.ReligiousIdentity)} ({faction.ReligiousGoal})" : string.Empty);
        GovernmentUiUtil.SetText(factionRebellionText, faction.IsInRebellion ? "IN OPEN REBELLION" : string.Empty);

        factionMembers ??= new PoliticalLineList(factionMembersRoot, lineRowPrefab);
        factionMembers.Show(faction.Members.Where(m => m != null).ToList(),
            (row, member) => row.BindText(GovernmentPresentation.FormatGovernorName(civ, member), $"Opinion {GovernmentUiUtil.Signed(member.Opinion)}"));

        var demands = faction.ActiveDemands.Where(d => d != null).ToList();
        GovernmentUiUtil.FillList(demandsRoot, demandRowPrefab, demandRows, demands, (row, demand) =>
        {
            demandFailures.TryGetValue(demand, out string failure);
            row.Bind(faction, demand, failure, OnResolveDemand);
        });
        GovernmentUiUtil.SetText(noDemandsText, demands.Count == 0 ? "This faction has no outstanding demands." : string.Empty);
    }

    private void OnResolveDemand(FactionBloc faction, FactionDemand demand, bool accepted)
    {
        if (civ == null || faction == null || demand == null) return;
        if (accepted)
        {
            Resolve(faction, demand, true);
            return;
        }

        if (panel == null) { Resolve(faction, demand, false); return; }
        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = "Refuse this demand?",
            description = $"{faction.FactionName} will be angered. Persistent refusal can lead to rebellion.",
            confirmLabel = "Refuse",
            lines = new List<PoliticalEffectLine>
            {
                new PoliticalEffectLine { label = "Members", value = "Opinion falls and grievances grow", harmful = true },
            },
            onConfirm = () => Resolve(faction, demand, false),
        });
    }

    private void Resolve(FactionBloc faction, FactionDemand demand, bool accepted)
    {
        var result = civ.ResolveFactionDemandDetailed(faction, demand, accepted, PoliticalActionRules.CurrentTurn);
        if (result.success) demandFailures.Remove(demand);
        else demandFailures[demand] = result.failureReason;
        panel?.RefreshAllVisible();
    }

    // ── Council votes ──

    private void RefreshVotes()
    {
        bool show = civ.HasRoyalCouncil;
        GovernmentUiUtil.SetActive(votesSectionRoot, true);

        var recent = CouncilVoteService.GetRecentResults(civ);
        var results = !show || recent == null
            ? new List<CouncilVoteResult>()
            : recent.Where(r => r != null && r.applicable).Reverse().Take(MaxVoteHistory).ToList();
        GovernmentUiUtil.FillList(votesRoot, voteRowPrefab, voteRows, results, (row, result) => row.BindResult(result));
        GovernmentUiUtil.SetText(noVotesText, !show ? "This government does not maintain a council."
            : results.Count == 0 ? $"The {GovernmentPresentation.GetInstitutionName(civ).ToLowerInvariant()} has not voted on anything recently."
            : string.Empty);
    }

    // ── Election ──

    private void RefreshElection()
    {
        var rules = civ.currentGovernment != null ? civ.currentGovernment.electionRules : null;
        bool elections = rules != null && rules.enabled;
        GovernmentUiUtil.SetActive(electionSectionRoot, true);

        var state = civ.electionState ?? new ElectionState();
        var election = state.activeElection;
        bool open = elections && election != null && !election.resolved;

        if (publicOpinionSlider != null)
        {
            publicOpinionSlider.minValue = 0f;
            publicOpinionSlider.maxValue = 100f;
            publicOpinionSlider.value = state.publicApproval;
            publicOpinionSlider.interactable = false;
        }
        GovernmentUiUtil.SetText(publicOpinionText, $"Public Opinion: {state.publicApproval:0.#}%");
        GovernmentUiUtil.SetText(victoryThresholdText, civ.isPlayerControlled ? "Reelection threshold: 50%" : string.Empty);
        GovernmentUiUtil.SetText(legitimacyText, $"Government Legitimacy: {state.governmentLegitimacy:0.#}%");
        GovernmentUiUtil.SetText(opinionModifiersText, FormatOpinionBreakdown(state));
        GovernmentUiUtil.SetText(electionHistoryText, FormatElectionHistory(state));
        GovernmentUiUtil.SetImage(currentRulerPortrait, civ.isPlayerControlled ? null : ElectionManager.GetCurrentRulerPortrait(civ));

        GovernmentUiUtil.SetText(electionSummaryText, !elections ? "This government does not hold regular elections."
            : civ.isPlayerControlled ? "The administration remains in power with at least 50% Public Opinion."
            : state.currentOffice != null ? $"{state.currentOffice.title} {state.currentOffice.officeholderName} (term ends turn {state.currentOffice.termEndTurn})"
            : $"Current ruler: {ElectionManager.GetCurrentRulerName(civ)}");
        GovernmentUiUtil.SetText(electionStatusText, state.pendingPlayerDefeat ? "Election defeat requires an immediate decision."
            : open
            ? $"Election underway; results on turn {election.resolutionTurn}."
            : elections && state.nextElectionTurn >= 0
                ? $"Next election: turn {state.nextElectionTurn} ({Mathf.Max(0, state.nextElectionTurn - PoliticalActionRules.CurrentTurn)} turns remaining)."
                : "No election scheduled.");

        electionIssues ??= new PoliticalLineList(electionIssuesRoot, lineRowPrefab);
        electionIssues.ShowTexts(open && !civ.isPlayerControlled ? election.issues.Select(i => $"{i.issue}: {i.summary}").ToList() : new List<string>());

        var candidates = open && !civ.isPlayerControlled ? election.candidates : new List<ElectionCandidateRecord>();
        GovernmentUiUtil.FillList(candidatesRoot, candidateRowPrefab, candidateRows, candidates,
            (row, candidate) => row.Bind(candidate, open && election.endorsedCandidateId == candidate.candidateId, open, false, OnEndorse));
    }

    private static string FormatOpinionBreakdown(ElectionState state)
    {
        if (state.opinionBreakdown == null || state.opinionBreakdown.Count == 0) return "No Public Opinion modifiers.";
        return string.Join("\n", state.opinionBreakdown
            .Where(entry => entry != null && entry.source != "Baseline")
            .OrderByDescending(entry => entry.value)
            .Select(entry => $"{entry.source}: {entry.value:+0.#;-0.#;0}"));
    }

    private static string FormatElectionHistory(ElectionState state)
    {
        if (state.history == null || state.history.Count == 0) return "No national elections have been recorded.";
        return string.Join("\n", state.history.Where(record => record != null).Reverse().Take(5).Select(record =>
            record.playerElection
                ? $"Turn {record.resolutionTurn}: {(record.playerVictory ? "Reelected" : record.playerSeizedPower ? "Defeated; power seized" : "Defeated")} ({record.publicOpinionAtElection:0.#}%)"
                : $"Turn {record.resolutionTurn}: {record.outcomeSummary}"));
    }

    private void OnEndorse(ElectionCandidateRecord candidate)
    {
        if (civ == null || candidate == null || panel == null) return;
        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Endorse {candidate.displayName}?",
            description = "An endorsement raises their support but can never decide the result outright.",
            confirmLabel = "Endorse",
            lines = new List<PoliticalEffectLine>
            {
                new PoliticalEffectLine { label = "Government legitimacy", value = "Falls slightly", harmful = true },
                new PoliticalEffectLine { label = "Candidate support", value = "Rises", beneficial = true },
            },
            onConfirm = () =>
            {
                ElectionManager.EndorseCandidate(civ, candidate.candidateId);
                panel.RefreshAllVisible();
            },
        });
    }
}
