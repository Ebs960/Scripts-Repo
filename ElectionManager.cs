using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Data-driven national elections. CouncilElection events remain council-seat vacancies.</summary>
public static class ElectionManager
{
    public static void OnGovernmentChanged(Civilization civ, GovernmentData oldGovernment, GovernmentData newGovernment, int turn)
    {
        if (civ == null) return;
        civ.electionState ??= new ElectionState();
        ElectionRules rules = newGovernment?.electionRules;
        if (rules == null || !rules.enabled)
        {
            civ.electionState.activeElection = null;
            civ.electionState.currentOffice = null;
            civ.electionState.nextElectionTurn = -1;
            return;
        }

        // A newly adopted electoral constitution schedules a campaign rather than silently appointing a winner.
        civ.electionState.activeElection = null;
        civ.electionState.currentOffice = null;
        civ.electionState.nextElectionTurn = turn + Mathf.Max(1, rules.campaignLeadTurns);
        civ.electionState.governmentLegitimacy = Mathf.Clamp(civ.electionState.governmentLegitimacy, 35f, 65f);
    }

    public static void ProcessTurn(Civilization civ, int turn)
    {
        ElectionRules rules = civ?.currentGovernment?.electionRules;
        if (civ == null || rules == null || !rules.enabled) return;
        civ.electionState ??= new ElectionState();
        UpdateApproval(civ);

        if (civ.electionState.activeElection == null && turn >= civ.electionState.nextElectionTurn - Mathf.Max(0, rules.campaignLeadTurns))
            civ.electionState.activeElection = CreateElection(civ, rules, turn);

        if (civ.electionState.activeElection != null && !civ.electionState.activeElection.resolved
            && turn >= civ.electionState.activeElection.resolutionTurn)
            Resolve(civ, rules, civ.electionState.activeElection, turn);
    }

    public static bool EndorseCandidate(Civilization civ, string candidateId, int goldSpend = 0)
    {
        var election = civ?.electionState?.activeElection;
        if (election == null || election.resolved || !election.candidates.Any(c => c.candidateId == candidateId)) return false;
        int spend = Mathf.Clamp(goldSpend, 0, civ.gold);
        civ.gold -= spend;
        election.endorsedCandidateId = candidateId;
        election.campaignGoldSpent += spend;
        // Intervention costs legitimacy; it influences support but can never directly select the winner.
        civ.electionState.governmentLegitimacy = Mathf.Clamp(civ.electionState.governmentLegitimacy - 1f - spend / 250f, 0f, 100f);
        return true;
    }

    public static ElectionRecord CreateElection(Civilization civ, ElectionRules rules, int turn)
    {
        int id = civ.electionState.electionsHeld + 1;
        int seed = StableHash((civ.civData?.civName ?? civ.name) + ":" + id + ":" + turn);
        var record = new ElectionRecord { electionId = id, openedTurn = turn, resolutionTurn = Mathf.Max(turn, civ.electionState.nextElectionTurn), deterministicSeed = seed };
        record.issues = SelectIssues(civ);
        record.candidates = GenerateCandidates(civ, rules, record, seed);
        return record;
    }

    public static void Resolve(Civilization civ, ElectionRules rules, ElectionRecord election, int turn)
    {
        if (election == null || election.resolved || election.candidates.Count == 0) return;
        foreach (var candidate in election.candidates)
        {
            float issue = candidate.priorities.Count == 0 ? 0f : election.issues.Where(i => candidate.priorities.Contains(i.issue)).Sum(i => i.salience) / candidate.priorities.Count;
            float incumbent = candidate.incumbent ? (civ.electionState.publicApproval - 50f) / 100f : 0f;
            float endorsement = candidate.candidateId == election.endorsedCandidateId
                ? Mathf.Min(0.12f, 0.035f + election.campaignGoldSpent / 1000f) : 0f;
            float noise = DeterministicNoise(election.deterministicSeed, candidate.candidateId) * Mathf.Clamp(rules.volatility, 0f, 0.25f);
            candidate.finalSupport = candidate.competence * 0.18f
                + candidate.publicAppeal * Mathf.Clamp01(rules.publicOpinionWeight)
                + candidate.eliteAppeal * Mathf.Clamp01(rules.governorEliteWeight)
                + issue * 0.22f + incumbent + endorsement + noise;
        }
        var ordered = election.candidates.OrderByDescending(c => c.finalSupport).ThenBy(c => c.candidateId, StringComparer.Ordinal).ToList();
        var winner = ordered[0];
        election.winnerCandidateId = winner.candidateId;
        election.winningMargin = ordered.Count > 1 ? Mathf.Clamp01(winner.finalSupport - ordered[1].finalSupport) : 1f;
        election.resolved = true;
        civ.electionState.electionsHeld++;
        civ.electionState.currentOffice = new ElectedOfficeRecord
        {
            candidateId = winner.candidateId, officeholderName = winner.displayName, factionName = winner.factionName,
            title = string.IsNullOrWhiteSpace(rules.executiveTitle) ? civ.currentGovernment.leaderTitleSuffix : rules.executiveTitle,
            electionWonTurn = turn, termEndTurn = turn + Mathf.Max(1, rules.termLengthTurns), mandate = new List<ElectionIssueType>(winner.priorities)
        };
        civ.electionState.nextElectionTurn = civ.electionState.currentOffice.termEndTurn;
        civ.electionState.governmentLegitimacy = Mathf.Clamp(civ.electionState.governmentLegitimacy + 8f + election.winningMargin * 12f, 0f, 100f);
        civ.electionState.publicApproval = Mathf.Clamp(civ.electionState.publicApproval + 2f, 0f, 100f);
    }

    private static void UpdateApproval(Civilization civ)
    {
        float economy = Mathf.Clamp(civ.cachedGoldPerTurn / 20f, -1f, 1f);
        float food = Mathf.Clamp(civ.cachedFoodPerTurn / 20f, -1f, 1f);
        float order = civ.cities == null || civ.cities.Count == 0 ? 0f : civ.cities.Average(c => c != null ? c.loyalty : 50f) / 50f - 1f;
        float target = 50f + economy * 12f + food * 10f + order * 15f - civ.warWeariness * 25f - civ.unrestModifier * 15f;
        civ.electionState.publicApproval = Mathf.Clamp(Mathf.Lerp(civ.electionState.publicApproval, target, 0.12f), 0f, 100f);
        civ.electionState.governmentLegitimacy = Mathf.Clamp(civ.electionState.governmentLegitimacy - Mathf.Max(0f, 35f - civ.electionState.publicApproval) * 0.01f, 0f, 100f);
    }

    private static List<ElectionIssueRecord> SelectIssues(Civilization civ)
    {
        var all = new List<ElectionIssueRecord>
        {
            Issue(ElectionIssueType.Economy, Mathf.Abs(civ.cachedGoldPerTurn) / 20f + .2f, $"Treasury trend: {civ.cachedGoldPerTurn:+#;-#;0}"),
            Issue(ElectionIssueType.FoodSecurity, Mathf.Abs(civ.cachedFoodPerTurn) / 20f + (civ.famineActive ? 1f : 0f), civ.famineActive ? "Famine and food security" : "Food security"),
            Issue(civ.warWeariness > .2f ? ElectionIssueType.War : ElectionIssueType.Peace, civ.warWeariness + .15f, $"War weariness: {civ.warWeariness:P0}"),
            Issue(ElectionIssueType.PublicOrder, Mathf.Abs(civ.unrestModifier) + .2f, "Loyalty and public order"),
            Issue(ElectionIssueType.Trade, Mathf.Abs(civ.domesticTradeModifier) + Mathf.Abs(civ.foreignTradeModifier) + .1f, "Trade and commerce"),
            Issue(ElectionIssueType.Vassals, civ.ActiveVassalCount * .2f, "Relations with subject states")
        };
        return all.OrderByDescending(i => i.salience).ThenBy(i => i.issue).Take(3).ToList();
    }

    private static ElectionIssueRecord Issue(ElectionIssueType type, float salience, string summary) => new ElectionIssueRecord { issue = type, salience = Mathf.Clamp01(salience), summary = summary };

    private static List<ElectionCandidateRecord> GenerateCandidates(Civilization civ, ElectionRules rules, ElectionRecord election, int seed)
    {
        int count = Mathf.Clamp(rules.candidateCount, 2, 4);
        var result = new List<ElectionCandidateRecord>();
        var eligible = (civ.governors ?? new List<Governor>()).Where(g => g != null && !g.IsInRebellion).OrderByDescending(g => g.PowerRank).ThenBy(g => g.Id).ToList();
        for (int i = 0; i < count; i++)
        {
            Governor g = i < eligible.Count ? eligible[i] : null;
            string id = g != null ? "governor:" + g.Id : "civilian:" + election.electionId + ":" + i;
            float n = (DeterministicNoise(seed, id) + 1f) * .5f;
            var c = new ElectionCandidateRecord {
                candidateId = id, displayName = g != null ? g.Name : "Civic Candidate " + (i + 1), governorId = g?.Id ?? -1,
                factionName = g?.Faction?.FactionName, incumbent = rules.incumbentEligible && civ.electionState.currentOffice?.candidateId == id,
                competence = g != null ? Mathf.Clamp01((g.Level + 2f) / 12f) : .4f + n * .3f,
                eliteAppeal = g != null ? Mathf.Clamp01(g.PowerRank / 100f) : .25f + n * .35f,
                publicAppeal = g != null ? Mathf.Clamp01((g.Opinion + 100f) / 200f) : .35f + (1f - n) * .35f
            };
            c.priorities.Add(election.issues[(i + Mathf.Abs(seed)) % election.issues.Count].issue);
            c.priorities.Add(election.issues[(i + 1 + Mathf.Abs(seed)) % election.issues.Count].issue);
            result.Add(c);
        }
        return result;
    }

    private static int StableHash(string value) { unchecked { int h = 17; foreach (char c in value ?? "") h = h * 31 + c; return h; } }
    private static float DeterministicNoise(int seed, string key) { uint x = (uint)(seed ^ StableHash(key)); x ^= x << 13; x ^= x >> 17; x ^= x << 5; return (x / (float)uint.MaxValue) * 2f - 1f; }
}
