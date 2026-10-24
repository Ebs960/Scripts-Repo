using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Political Affairs presented directly in four permanently visible dashboard sections.</summary>
public sealed class GovernmentPoliticsUI : GovernmentScreenBase
{
    [Header("Political Events")]
    [SerializeField] private TMP_Text eventTitleText;
    [SerializeField] private TMP_Text eventDescriptionText;
    [SerializeField] private TMP_Text eventExpirationText;
    [SerializeField] private Transform eventOptionsRoot;
    [SerializeField] private Button eventOptionButtonPrefab;
    [SerializeField] private Button previousEventButton;
    [SerializeField] private Button nextEventButton;

    [Header("Elections")]
    [SerializeField] private Slider publicOpinionSlider;
    [SerializeField] private TMP_Text publicOpinionText;
    [SerializeField] private TMP_Text electionThresholdText;
    [SerializeField] private TMP_Text legitimacyText;
    [SerializeField] private TMP_Text electionStatusText;
    [SerializeField] private TMP_Text opinionModifiersText;

    [Header("Council Votes")]
    [SerializeField] private TMP_Text councilProposalText;
    [SerializeField] private TMP_Text councilVoteDetailsText;
    [SerializeField] private TMP_Text councilResultText;

    [Header("Factions")]
    [SerializeField] private TMP_Dropdown factionDropdown;
    [SerializeField] private TMP_Text factionInformationText;
    [SerializeField] private TMP_Text factionDemandText;
    [SerializeField] private TMP_Dropdown demandDropdown;
    [SerializeField] private Button demandAcceptButton;
    [SerializeField] private Button demandRefuseButton;
    [SerializeField] private TMP_Text factionStatusText;

    private readonly List<Button> eventOptionButtons = new List<Button>();
    private readonly Dictionary<FactionDemand, string> demandFailures = new Dictionary<FactionDemand, string>();
    private List<PoliticalEventRecord> activeEvents = new List<PoliticalEventRecord>();
    private List<FactionBloc> availableFactions = new List<FactionBloc>();
    private List<FactionDemand> availableDemands = new List<FactionDemand>();
    private int selectedEventId = -1;
    private FactionBloc selectedFaction;
    private FactionDemand selectedDemand;
    private bool wired;

    protected override void OnCivilizationChanged()
    {
        selectedEventId = -1;
        selectedFaction = null;
        selectedDemand = null;
        demandFailures.Clear();
    }

    public override void Refresh()
    {
        if (civ == null) return;
        EnsureWired();
        RefreshPoliticalEvents();
        RefreshElections();
        RefreshCouncilVote();
        RefreshFactions();
    }

    private void EnsureWired()
    {
        if (wired) return;
        wired = true;
        GovernmentUiUtil.SetClick(previousEventButton, () => CycleEvent(-1));
        GovernmentUiUtil.SetClick(nextEventButton, () => CycleEvent(1));
        GovernmentUiUtil.SetClick(demandAcceptButton, () => ResolveSelectedDemand(true));
        GovernmentUiUtil.SetClick(demandRefuseButton, RequestRefuseSelectedDemand);

        if (factionDropdown != null)
        {
            factionDropdown.onValueChanged.RemoveAllListeners();
            factionDropdown.onValueChanged.AddListener(SelectFactionAt);
        }
        if (demandDropdown != null)
        {
            demandDropdown.onValueChanged.RemoveAllListeners();
            demandDropdown.onValueChanged.AddListener(SelectDemandAt);
        }
    }

    private void RefreshPoliticalEvents()
    {
        activeEvents = PoliticalEventManager.Instance != null
            ? PoliticalEventManager.Instance.GetActiveEventsForCiv(civ).Where(record => record != null).ToList()
            : new List<PoliticalEventRecord>();

        var selected = activeEvents.FirstOrDefault(record => record.id == selectedEventId) ?? activeEvents.FirstOrDefault();
        selectedEventId = selected?.id ?? -1;
        if (selected == null)
        {
            GovernmentUiUtil.SetText(eventTitleText, string.Empty);
            GovernmentUiUtil.SetText(eventDescriptionText, "No political events require your attention.");
            GovernmentUiUtil.SetText(eventExpirationText, string.Empty);
            GovernmentUiUtil.FillList(eventOptionsRoot, eventOptionButtonPrefab, eventOptionButtons,
                Array.Empty<PoliticalEventOption>(), (button, option) => { });
        }
        else
        {
            GovernmentUiUtil.SetText(eventTitleText, selected.title);
            GovernmentUiUtil.SetText(eventDescriptionText, selected.body);
            GovernmentUiUtil.SetText(eventExpirationText,
                $"Expires in {Mathf.Max(0, selected.expiryTurn - PoliticalActionRules.CurrentTurn)} turn(s) (turn {selected.expiryTurn})");
            GovernmentUiUtil.FillList(eventOptionsRoot, eventOptionButtonPrefab, eventOptionButtons,
                selected.options ?? new List<PoliticalEventOption>(), (button, option) =>
                {
                    GovernmentUiUtil.SetText(button.GetComponentInChildren<TMP_Text>(true), option.label);
                    int eventId = selected.id;
                    int optionIndex = selected.options.IndexOf(option);
                    GovernmentUiUtil.SetClick(button, () => ResolveEventOption(eventId, optionIndex));
                });
        }

        bool multiple = activeEvents.Count > 1;
        GovernmentUiUtil.SetActive(previousEventButton != null ? previousEventButton.gameObject : null, multiple);
        GovernmentUiUtil.SetActive(nextEventButton != null ? nextEventButton.gameObject : null, multiple);
    }

    private void CycleEvent(int direction)
    {
        if (activeEvents.Count <= 1) return;
        int index = activeEvents.FindIndex(record => record.id == selectedEventId);
        index = (index + direction + activeEvents.Count) % activeEvents.Count;
        selectedEventId = activeEvents[index].id;
        RefreshPoliticalEvents();
    }

    private void ResolveEventOption(int eventId, int optionIndex)
    {
        PoliticalEventManager.Instance?.ResolveEvent(eventId, optionIndex);
        panel?.RefreshAllVisible();
    }

    private void RefreshElections()
    {
        var state = civ.electionState ?? new ElectionState();
        bool electionsEnabled = civ.currentGovernment?.electionRules?.enabled == true;
        if (publicOpinionSlider != null)
        {
            publicOpinionSlider.minValue = 0f;
            publicOpinionSlider.maxValue = 100f;
            publicOpinionSlider.value = state.publicApproval;
            publicOpinionSlider.interactable = false;
        }

        GovernmentUiUtil.SetText(publicOpinionText, $"Public Opinion: {state.publicApproval:0.#}%");
        GovernmentUiUtil.SetText(electionThresholdText, civ.isPlayerControlled ? "Reelection Threshold: 50%" : string.Empty);
        GovernmentUiUtil.SetText(legitimacyText, $"Government Legitimacy: {state.governmentLegitimacy:0.#}%");
        GovernmentUiUtil.SetText(electionStatusText, FormatElectionStatus(state, electionsEnabled));
        GovernmentUiUtil.SetText(opinionModifiersText, FormatElectionInformation(state, electionsEnabled));
    }

    private string FormatElectionStatus(ElectionState state, bool electionsEnabled)
    {
        if (!electionsEnabled) return "This government does not hold regular elections.";
        if (state.pendingPlayerDefeat) return "Election defeat requires an immediate decision.";
        if (state.activeElection != null && !state.activeElection.resolved)
            return $"Election underway - resolves turn {state.activeElection.resolutionTurn}.";
        return state.nextElectionTurn >= 0
            ? $"Next Election: {Mathf.Max(0, state.nextElectionTurn - PoliticalActionRules.CurrentTurn)} turn(s)"
            : "No election scheduled.";
    }

    private string FormatElectionInformation(ElectionState state, bool electionsEnabled)
    {
        var sections = new List<string>();
        if (state.opinionBreakdown != null && state.opinionBreakdown.Count > 0)
        {
            sections.Add(string.Join("\n", state.opinionBreakdown
                .Where(entry => entry != null && entry.source != "Baseline")
                .OrderByDescending(entry => entry.value)
                .Select(entry => $"{entry.source}: {entry.value:+0.#;-0.#;0}")));
        }
        else sections.Add("No Public Opinion modifiers.");

        if (!civ.isPlayerControlled && electionsEnabled)
        {
            sections.Add($"Current ruler: {ElectionManager.GetCurrentRulerName(civ)}");
            if (state.activeElection?.candidates != null && !state.activeElection.resolved)
                sections.Add("Candidates:\n" + string.Join("\n", state.activeElection.candidates
                    .Where(candidate => candidate != null)
                    .Select(candidate => $"{candidate.displayName} - {candidate.factionName ?? "Independent"}")));
            if (state.history != null && state.history.Count > 0)
                sections.Add("Recent elections:\n" + string.Join("\n", state.history
                    .Where(record => record != null).Reverse().Take(3)
                    .Select(record => $"Turn {record.resolutionTurn}: {record.outcomeSummary}")));
        }
        return string.Join("\n\n", sections.Where(section => !string.IsNullOrWhiteSpace(section)));
    }

    private void RefreshCouncilVote()
    {
        if (!civ.HasRoyalCouncil)
        {
            GovernmentUiUtil.SetText(councilProposalText, "This government does not maintain a council.");
            GovernmentUiUtil.SetText(councilVoteDetailsText, string.Empty);
            GovernmentUiUtil.SetText(councilResultText, string.Empty);
            return;
        }

        var latest = CouncilVoteService.GetRecentResults(civ)?.LastOrDefault(result => result != null && result.applicable);
        if (latest == null)
        {
            GovernmentUiUtil.SetText(councilProposalText, "No recent council votes.");
            GovernmentUiUtil.SetText(councilVoteDetailsText, string.Empty);
            GovernmentUiUtil.SetText(councilResultText, string.Empty);
            return;
        }

        GovernmentUiUtil.SetText(councilProposalText, $"Proposal: {latest.proposalDescription}");
        GovernmentUiUtil.SetText(councilVoteDetailsText,
            $"Votes: {latest.yesVotes} Yes / {latest.noVotes} No\nRequired: {latest.requiredYesVotes} Yes");
        GovernmentUiUtil.SetText(councilResultText, latest.passed
            ? "<color=#6AA84F>Result: Passed</color>"
            : "<color=#E06666>Result: Rejected</color>");
    }

    private void RefreshFactions()
    {
        bool suppressed = civ.currentGovernment != null && civ.currentGovernment.suppressConventionalPolitics;
        availableFactions = suppressed || civ.nobleFactions == null
            ? new List<FactionBloc>()
            : civ.nobleFactions.Where(faction => faction != null).ToList();

        if (selectedFaction == null || !availableFactions.Contains(selectedFaction))
            selectedFaction = availableFactions.FirstOrDefault();
        PopulateFactionDropdown();

        if (selectedFaction == null)
        {
            GovernmentUiUtil.SetText(factionInformationText, string.Empty);
            GovernmentUiUtil.SetText(factionDemandText, string.Empty);
            GovernmentUiUtil.SetText(factionStatusText, suppressed
                ? "Conventional political factions are inactive under this government."
                : "No political factions currently exist.");
            SetDemandControls(false);
            return;
        }

        string religion = selectedFaction.ReligiousIdentity != null
            ? $"\nReligion: {GovernmentPresentation.NameOf(selectedFaction.ReligiousIdentity)} ({selectedFaction.ReligiousGoal})"
            : string.Empty;
        GovernmentUiUtil.SetText(factionInformationText,
            $"{selectedFaction.FactionName}\n" +
            $"Alignment: {selectedFaction.Alignment}\n" +
            $"Leader: {(selectedFaction.Leader != null ? GovernmentPresentation.FormatGovernorName(civ, selectedFaction.Leader) : "None")}\n" +
            $"Political Power: {selectedFaction.ComputePower():0.#}\n" +
            $"Members: {selectedFaction.Members.Count(member => member != null)}" + religion + "\n" +
            $"Rebellion: {(selectedFaction.IsInRebellion ? "IN OPEN REBELLION" : "No")}");

        availableDemands = selectedFaction.ActiveDemands?.Where(demand => demand != null).ToList()
            ?? new List<FactionDemand>();
        if (selectedDemand == null || !availableDemands.Contains(selectedDemand))
            selectedDemand = availableDemands.FirstOrDefault();
        PopulateDemandDropdown();
        RefreshSelectedDemand();
    }

    private void PopulateFactionDropdown()
    {
        if (factionDropdown == null) return;
        factionDropdown.ClearOptions();
        factionDropdown.AddOptions(availableFactions.Count > 0
            ? availableFactions.Select(faction => faction.FactionName).ToList()
            : new List<string> { "No factions" });
        factionDropdown.SetValueWithoutNotify(Mathf.Max(0, availableFactions.IndexOf(selectedFaction)));
        factionDropdown.interactable = availableFactions.Count > 1;
    }

    private void PopulateDemandDropdown()
    {
        if (demandDropdown == null) return;
        demandDropdown.ClearOptions();
        demandDropdown.AddOptions(availableDemands.Select((demand, index) =>
            string.IsNullOrWhiteSpace(demand.description) ? $"Demand {index + 1}" : demand.description).ToList());
        demandDropdown.SetValueWithoutNotify(Mathf.Max(0, availableDemands.IndexOf(selectedDemand)));
        GovernmentUiUtil.SetActive(demandDropdown.gameObject, availableDemands.Count > 1);
    }

    private void RefreshSelectedDemand()
    {
        bool hasDemand = selectedDemand != null;
        GovernmentUiUtil.SetText(factionDemandText, hasDemand
            ? $"Current Demand: {selectedDemand.description}\nExpires: turn {selectedDemand.issuedTurn + selectedDemand.expiryTurns}"
            : "No outstanding demands.");
        string failure = null;
        if (hasDemand) demandFailures.TryGetValue(selectedDemand, out failure);
        GovernmentUiUtil.SetText(factionStatusText, failure);
        SetDemandControls(hasDemand);
    }

    private void SetDemandControls(bool available)
    {
        GovernmentUiUtil.SetInteractable(demandAcceptButton, available);
        GovernmentUiUtil.SetInteractable(demandRefuseButton, available);
        if (!available && demandDropdown != null) GovernmentUiUtil.SetActive(demandDropdown.gameObject, false);
    }

    private void SelectFactionAt(int index)
    {
        if (index < 0 || index >= availableFactions.Count) return;
        selectedFaction = availableFactions[index];
        selectedDemand = null;
        RefreshFactions();
    }

    private void SelectDemandAt(int index)
    {
        if (index < 0 || index >= availableDemands.Count) return;
        selectedDemand = availableDemands[index];
        RefreshSelectedDemand();
    }

    private void ResolveSelectedDemand(bool accepted)
    {
        if (selectedFaction == null || selectedDemand == null) return;
        var demand = selectedDemand;
        var result = civ.ResolveFactionDemandDetailed(selectedFaction, demand, accepted, PoliticalActionRules.CurrentTurn);
        if (result.success) demandFailures.Remove(demand);
        else demandFailures[demand] = result.failureReason;
        panel?.RefreshAllVisible();
    }

    private void RequestRefuseSelectedDemand()
    {
        if (selectedFaction == null || selectedDemand == null) return;
        if (panel == null)
        {
            ResolveSelectedDemand(false);
            return;
        }
        panel.RequestConfirmation(new PoliticalConfirmRequest
        {
            title = "Refuse this demand?",
            onConfirm = () => ResolveSelectedDemand(false),
        });
    }
}