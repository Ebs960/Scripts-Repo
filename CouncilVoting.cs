using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A ruler proposal put before the Royal Council for a vote.
/// </summary>
public class CouncilProposalContext
{
    /// <summary>Veto domains implicated by this proposal (flags; any overlap with the government's domains triggers a vote).</summary>
    public VetoDomain domains = VetoDomain.None;
    /// <summary>Policy being adopted or revoked, when relevant.</summary>
    public PolicyData targetPolicy;
    /// <summary>True when the proposal removes targetPolicy instead of adopting it.</summary>
    public bool policyIsRevocation;
    /// <summary>Government being switched to, when relevant.</summary>
    public GovernmentData targetGovernment;
    /// <summary>Governor whose title/position is affected, when relevant.</summary>
    public Governor targetGovernor;
    /// <summary>Foreign civilization involved (e.g. war target), when relevant.</summary>
    public Civilization targetCivilization;
    /// <summary>Optional numeric context (e.g. tax delta) where relevant.</summary>
    public float numericContext;
    /// <summary>Human-readable proposal description shown in vote summaries.</summary>
    public string description = "Proposal";
}

/// <summary>A single seated governor's vote on a proposal.</summary>
public struct CouncilVote
{
    public int governorId;
    public string governorName;
    public bool approve;
    public float score;
    /// <summary>The strongest single factor behind this vote.</summary>
    public string primaryReason;
}

/// <summary>Outcome of a council vote on a ruler proposal.</summary>
public class CouncilVoteResult
{
    /// <summary>False when no vote was actually held (no council, domain not vetoable, or empty council).</summary>
    public bool applicable;
    public int yesVotes;
    public int noVotes;
    public int requiredYesVotes;
    /// <summary>True when the proposal may proceed (vote passed, or no effective council veto exists).</summary>
    public bool passed;
    public List<CouncilVote> individualVotes = new List<CouncilVote>();
    public string proposalDescription;

    public string Summary => applicable
        ? $"Council Vote: {(passed ? "PASSED" : "FAILED")} {yesVotes}\u2013{noVotes}"
        : "No council vote required";
}

/// <summary>
/// Centralized council vote authority. All systems that previously checked a
/// binary council veto must request a vote here instead. Voting is fully
/// deterministic: each seated governor's YES/NO is derived from opinion,
/// personality, specialization, religion, faction demands, grievances, and the
/// shared PoliticalPreferenceScorer — never from random rolls.
/// </summary>
public static class CouncilVoteService
{
    private const float OpinionWeight = 0.4f;
    private const float GrievanceWeight = -3f;
    private const int ResultsKeptPerCiv = 5;

    // Recent vote results per civ (runtime only) so UI can show who voted how.
    private static readonly Dictionary<Civilization, List<CouncilVoteResult>> recentResults
        = new Dictionary<Civilization, List<CouncilVoteResult>>();

    public static IReadOnlyList<CouncilVoteResult> GetRecentResults(Civilization civ)
        => civ != null && recentResults.TryGetValue(civ, out var list)
            ? list
            : (IReadOnlyList<CouncilVoteResult>)System.Array.Empty<CouncilVoteResult>();

    /// <summary>
    /// Evaluate a proposal. Returns a passed result with applicable=false when
    /// no council veto applies (no council government, domain not vetoable, or
    /// zero seated governors). A tie fails when a vote is actually required.
    /// </summary>
    public static CouncilVoteResult Evaluate(Civilization civ, CouncilProposalContext proposal)
    {
        var result = new CouncilVoteResult
        {
            proposalDescription = proposal?.description,
            passed = true,
            applicable = false,
        };

        if (civ == null || proposal == null) return result;
        if (!civ.HasRoyalCouncil) return result;
        if ((civ.ActiveVetoDomains & proposal.domains) == VetoDomain.None) return result;

        var voters = civ.royalCouncil
            .Where(g => g != null && !g.IsInRebellion)
            .ToList();
        if (voters.Count == 0) return result; // Empty seats do not vote; no effective veto.

        result.applicable = true;
        foreach (var voter in voters)
        {
            var vote = EvaluateVote(voter, civ, proposal);
            result.individualVotes.Add(vote);
            if (vote.approve) result.yesVotes++;
            else result.noVotes++;
        }

        result.requiredYesVotes = voters.Count / 2 + 1; // strict majority; ties fail
        result.passed = result.yesVotes >= result.requiredYesVotes;

        RememberResult(civ, result);
        return result;
    }

    /// <summary>Show the vote summary to the player when the deciding civ is player-controlled.</summary>
    public static void NotifyPlayer(Civilization civ, CouncilVoteResult result)
    {
        if (civ == null || result == null || !result.applicable) return;
        if (!civ.isPlayerControlled || UIManager.Instance == null) return;
        string suffix = string.IsNullOrEmpty(result.proposalDescription) ? "" : $" \u2014 {result.proposalDescription}";
        UIManager.Instance.ShowNotification($"{result.Summary}{suffix}");
    }

    // ── Per-governor scoring ──────────────────────────────────────────────────

    private static CouncilVote EvaluateVote(Governor voter, Civilization civ, CouncilProposalContext proposal)
    {
        var reasons = new List<(string label, float weight)>();

        void Add(string label, float weight)
        {
            if (!Mathf.Approximately(weight, 0f))
                reasons.Add((label, weight));
        }

        // Baseline: relationship with the ruler.
        Add("Opinion of the ruler", voter.Opinion * OpinionWeight);
        Add("Accumulated grievances", voter.TotalGrievances() * GrievanceWeight);

        // General disposition personalities.
        if (voter.HasPersonality(PersonalityTrait.Loyal)) Add("Loyal to the ruler", 15f);
        if (voter.HasPersonality(PersonalityTrait.Content)) Add("Content with their station", 8f);
        if (voter.HasPersonality(PersonalityTrait.Ambitious)) Add("Ambitious scheming", -8f);
        if (voter.HasPersonality(PersonalityTrait.Deceitful)) Add("Deceitful nature", -4f);

        // War proposals.
        if ((proposal.domains & VetoDomain.WarDeclaration) != VetoDomain.None)
        {
            if (voter.specialization == Governor.Specialization.Military) Add("Favors military ventures", 20f);
            if (voter.HasPersonality(PersonalityTrait.Brave)) Add("Brave — welcomes war", 12f);
            if (voter.HasPersonality(PersonalityTrait.Craven)) Add("Craven — fears war", -20f);
        }

        // Tax proposals.
        if ((proposal.domains & VetoDomain.Taxation) != VetoDomain.None)
        {
            if (voter.HasPersonality(PersonalityTrait.Greedy)) Add("Greedy — resents taxation", -15f);
            if (voter.HasPersonality(PersonalityTrait.Generous)) Add("Generous — tolerates taxation", 5f);
            if (proposal.numericContext > 0f) Add("Opposes higher taxes", -10f);
        }

        // Religious proposals.
        if ((proposal.domains & VetoDomain.Religion) != VetoDomain.None
            && !voter.HasPersonality(PersonalityTrait.Cynical))
        {
            bool mismatch = civ.foundedReligion != null
                && voter.PersonalReligion != null
                && voter.PersonalReligion != civ.foundedReligion;
            float zeal = voter.HasPersonality(PersonalityTrait.Zealous) ? 2f : 1f;
            if (mismatch) Add("Follows a different faith", -20f * zeal);
            else if (voter.HasPersonality(PersonalityTrait.Zealous)) Add("Zealous for the state faith", 15f);
        }

        // Title revocation: direct personal stake dominates everything else.
        if ((proposal.domains & VetoDomain.TitleRevocation) != VetoDomain.None && proposal.targetGovernor != null)
        {
            if (proposal.targetGovernor == voter) Add("Own position at stake", -100f);
            else if (voter.Faction != null && proposal.targetGovernor.Faction == voter.Faction)
                Add("Faction ally targeted", -25f);
        }

        // Government change: use the shared preference scorer.
        if ((proposal.domains & (VetoDomain.GovernmentChange | VetoDomain.Succession)) != VetoDomain.None
            && proposal.targetGovernment != null)
        {
            Add("Government preference",
                PoliticalPreferenceScorer.ScoreGovernmentForGovernor(proposal.targetGovernment, voter, civ) * 0.5f);
        }

        // Policy adopt/revoke: use the shared preference scorer.
        if (proposal.targetPolicy != null
            && (proposal.domains & (VetoDomain.PolicyChange | VetoDomain.Religion | VetoDomain.Taxation)) != VetoDomain.None)
        {
            float pref = PoliticalPreferenceScorer.ScorePolicyForGovernor(proposal.targetPolicy, voter, civ) * 0.5f;
            Add("Policy preference", proposal.policyIsRevocation ? -pref : pref);
        }

        // Faction demand alignment: supporting or blocking the exact demanded target.
        if (voter.Faction?.ActiveDemands != null)
        {
            foreach (var demand in voter.Faction.ActiveDemands)
            {
                if (demand == null) continue;
                if (demand.type == FactionDemandType.AdoptPolicy && demand.targetPolicy == proposal.targetPolicy && proposal.targetPolicy != null)
                    Add("Faction demands this policy", proposal.policyIsRevocation ? -50f : 50f);
                if (demand.type == FactionDemandType.RevokePolicy && demand.targetPolicy == proposal.targetPolicy && proposal.targetPolicy != null)
                    Add("Faction demands repeal", proposal.policyIsRevocation ? 50f : -50f);
                if (demand.type == FactionDemandType.ChangeGovernment && demand.targetGovernment == proposal.targetGovernment && proposal.targetGovernment != null)
                    Add("Faction demands this government", 50f);
            }
        }

        float score = 0f;
        foreach (var (_, weight) in reasons) score += weight;

        string primary = "No strong feelings";
        float strongest = 0f;
        foreach (var (label, weight) in reasons)
        {
            if (Mathf.Abs(weight) > Mathf.Abs(strongest))
            {
                strongest = weight;
                primary = label;
            }
        }

        return new CouncilVote
        {
            governorId = voter.Id,
            governorName = voter.Name,
            approve = score > 0f,
            score = score,
            primaryReason = primary,
        };
    }

    private static void RememberResult(Civilization civ, CouncilVoteResult result)
    {
        if (!recentResults.TryGetValue(civ, out var list))
        {
            list = new List<CouncilVoteResult>();
            recentResults[civ] = list;
        }

        list.Add(result);
        while (list.Count > ResultsKeptPerCiv)
            list.RemoveAt(0);
    }
}
