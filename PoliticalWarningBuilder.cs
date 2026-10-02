using System.Collections.Generic;
using System.Linq;

public enum PoliticalWarningSeverity { Info, Caution, Critical }

public enum PoliticalWarningDestination
{
    None,
    GovernmentSelection,
    Policies,
    Governors,
    Vassals,
    Politics
}

/// <summary>An item in the Overview "Attention" list with a warning-specific click destination.</summary>
public struct PoliticalWarning
{
    public PoliticalWarningSeverity severity;
    public string text;
    public PoliticalWarningDestination destination;
}

/// <summary>Data-driven political warnings. All thresholds live here so UI code never hardcodes them.</summary>
public static class PoliticalWarningBuilder
{
    public const float LowLegitimacyThreshold = 30f;
    public const float VassalLibertyWarningFraction = 0.6f;
    public const float GovernorDiscontentOpinion = -20f;
    public const int RecentVoteWindowTurns = 8;

    public static List<PoliticalWarning> Build(Civilization civ)
    {
        var warnings = new List<PoliticalWarning>();
        if (civ == null) return warnings;

        bool conventional = civ.currentGovernment == null || !civ.currentGovernment.suppressConventionalPolitics;
        string governors = GovernmentPresentation.GetGovernorTitlePlural(civ).ToLowerInvariant();

        if (conventional)
        {
            int demands = 0, rebelFactions = 0;
            if (civ.nobleFactions != null)
                foreach (var bloc in civ.nobleFactions)
                {
                    if (bloc == null) continue;
                    demands += bloc.ActiveDemands?.Count ?? 0;
                    if (bloc.IsInRebellion) rebelFactions++;
                }
            if (rebelFactions > 0)
                Add(warnings, PoliticalWarningSeverity.Critical, $"{rebelFactions} faction(s) in open rebellion.", PoliticalWarningDestination.Politics);
            if (demands > 0)
                Add(warnings, PoliticalWarningSeverity.Caution, $"{demands} faction demand(s) awaiting your answer.", PoliticalWarningDestination.Politics);

            int rebelGovernors = 0, discontented = 0, unseated = 0;
            if (civ.governors != null)
                foreach (var g in civ.governors)
                {
                    if (g == null) continue;
                    if (g.IsInRebellion) rebelGovernors++;
                    else if (g.Opinion <= GovernorDiscontentOpinion) discontented++;
                    if (civ.HasRoyalCouncil && !g.IsOnCouncil && g.IsCouncilEligible) unseated++;
                }
            if (rebelGovernors > 0)
                Add(warnings, PoliticalWarningSeverity.Critical, $"{rebelGovernors} of your {governors} are in rebellion.", PoliticalWarningDestination.Governors);
            if (discontented > 0)
                Add(warnings, PoliticalWarningSeverity.Caution, $"{discontented} of your {governors} are deeply discontented.", PoliticalWarningDestination.Governors);
            if (unseated > 0)
                Add(warnings, PoliticalWarningSeverity.Caution,
                    $"{unseated} powerful {governors} have no seat in the {GovernmentPresentation.GetInstitutionName(civ)}.", PoliticalWarningDestination.Governors);
        }

        int events = PoliticalEventManager.Instance != null ? PoliticalEventManager.Instance.GetActiveEventsForCiv(civ).Count : 0;
        if (events > 0)
            Add(warnings, PoliticalWarningSeverity.Info, $"{events} political event(s) need a decision.", PoliticalWarningDestination.Politics);

        if (SubjectManager.Instance != null)
            foreach (var contract in SubjectManager.Instance.GetSubjects(civ))
            {
                if (contract == null) continue;
                string subject = contract.subjectCivName;
                if (SubjectManager.Instance.GetPendingIndependenceDemand(civ, contract.subject) != null)
                    Add(warnings, PoliticalWarningSeverity.Critical, $"{subject} demands independence.", PoliticalWarningDestination.Vassals);
                else if (contract.libertyDesire >= contract.EffectiveBreakawayThreshold * VassalLibertyWarningFraction)
                    Add(warnings, PoliticalWarningSeverity.Caution,
                        $"{subject} is restless (liberty desire {contract.libertyDesire:0}/{contract.EffectiveBreakawayThreshold:0}).", PoliticalWarningDestination.Vassals);
            }

        var rules = civ.currentGovernment?.electionRules;
        if (rules != null && rules.enabled && civ.electionState != null)
        {
            if (civ.electionState.governmentLegitimacy < LowLegitimacyThreshold)
                Add(warnings, PoliticalWarningSeverity.Critical, $"Legitimacy is very low ({civ.electionState.governmentLegitimacy:0}%).", PoliticalWarningDestination.Politics);
            if (civ.electionState.activeElection != null && !civ.electionState.activeElection.resolved)
                Add(warnings, PoliticalWarningSeverity.Info, "An election is underway.", PoliticalWarningDestination.Politics);
        }

        var votes = CouncilVoteService.GetRecentResults(civ);
        int round = TurnManager.Instance != null ? TurnManager.Instance.round : 0;
        var lastVote = votes.LastOrDefault(v => v != null && v.applicable);
        if (lastVote != null && !lastVote.passed && round - lastVote.recordedTurn <= RecentVoteWindowTurns)
            Add(warnings, PoliticalWarningSeverity.Caution, $"The {GovernmentPresentation.GetInstitutionName(civ)} recently rejected: {lastVote.proposalDescription}.", PoliticalWarningDestination.Politics);

        warnings.Sort((a, b) => b.severity.CompareTo(a.severity));
        return warnings;
    }

    private static void Add(List<PoliticalWarning> list, PoliticalWarningSeverity severity, string text, PoliticalWarningDestination destination)
        => list.Add(new PoliticalWarning { severity = severity, text = text, destination = destination });
}
