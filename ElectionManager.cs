using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>National elections. Player elections are approval referendums; AI elections choose political rulers.</summary>
public static class ElectionManager
{
    public const int NationalTermTurns = 25;
    public const float PlayerVictoryThreshold = 50f;
    public const int MaxElectionHistory = 20;

    public static event Action<Civilization> ElectionStateChanged;
    public static event Action<Civilization> PlayerElectionDefeatPending;

    public static bool HasPendingPlayerDefeat
        => PoliticalActionRules.FindPlayerCivilization()?.electionState?.pendingPlayerDefeat == true;

    public static void OnGovernmentChanged(Civilization civ, GovernmentData oldGovernment, GovernmentData newGovernment, int turn)
    {
        if (civ == null) return;
        EnsureState(civ);
        bool newElectoral = newGovernment?.electionRules?.enabled == true;

        civ.electionState.activeElection = null;
        if (!newElectoral)
        {
            civ.electionState.currentOffice = null;
        }
        else if (civ.electionState.nextElectionTurn < 0)
        {
            civ.electionState.currentOffice = null;
            civ.electionState.nextElectionTurn = turn + NationalTermTurns;
        }

        ElectionStateChanged?.Invoke(civ);
    }

    public static void ProcessTurn(Civilization civ, int turn)
    {
        if (civ == null) return;
        EnsureState(civ);
        UpdatePublicOpinion(civ, turn);
        ReconcileCurrentRuler(civ, turn);

        var rules = civ.currentGovernment?.electionRules;
        if (rules == null || !rules.enabled || civ.electionState.pendingPlayerDefeat)
        {
            ElectionStateChanged?.Invoke(civ);
            return;
        }

        if (civ.electionState.nextElectionTurn < 0)
            civ.electionState.nextElectionTurn = turn + NationalTermTurns;

        int leadTurns = civ.isPlayerControlled ? 0 : Mathf.Max(0, rules.campaignLeadTurns);
        if (civ.electionState.activeElection == null && turn >= civ.electionState.nextElectionTurn - leadTurns)
            civ.electionState.activeElection = CreateElection(civ, rules, turn);

        var active = civ.electionState.activeElection;
        if (active != null && !active.resolved && turn >= active.resolutionTurn)
            Resolve(civ, rules, active, turn);

        ElectionStateChanged?.Invoke(civ);
    }

    public static void AddTemporaryOpinionModifier(Civilization civ, string source, float value, int durationTurns)
    {
        if (civ == null || string.IsNullOrWhiteSpace(source) || durationTurns <= 0) return;
        EnsureState(civ);
        var existing = civ.electionState.temporaryOpinionModifiers.FirstOrDefault(m => m != null && m.source == source);
        if (existing == null)
        {
            existing = new PublicOpinionModifier { source = source };
            civ.electionState.temporaryOpinionModifiers.Add(existing);
        }
        existing.value = value;
        existing.duration = durationTurns;
        existing.remainingTurns = durationTurns;
        ElectionStateChanged?.Invoke(civ);
    }

    public static bool EndorseCandidate(Civilization civ, string candidateId, int goldSpend = 0)
    {
        var election = civ?.electionState?.activeElection;
        if (civ == null || civ.isPlayerControlled || election == null || election.resolved
            || !election.candidates.Any(c => c.candidateId == candidateId)) return false;
        int spend = Mathf.Clamp(goldSpend, 0, civ.gold);
        civ.gold -= spend;
        election.endorsedCandidateId = candidateId;
        election.campaignGoldSpent += spend;
        civ.electionState.governmentLegitimacy = Mathf.Clamp(civ.electionState.governmentLegitimacy - 1f - spend / 250f, 0f, 100f);
        ElectionStateChanged?.Invoke(civ);
        return true;
    }

    public static ElectionRecord CreateElection(Civilization civ, ElectionRules rules, int turn)
    {
        EnsureState(civ);
        int id = civ.electionState.electionsHeld + 1;
        int seed = StableHash((civ.civData?.civName ?? civ.name) + ":" + id + ":" + civ.electionState.nextElectionTurn);
        var record = new ElectionRecord
        {
            electionId = id,
            openedTurn = turn,
            resolutionTurn = Mathf.Max(turn, civ.electionState.nextElectionTurn),
            deterministicSeed = seed,
            playerElection = civ.isPlayerControlled,
        };
        record.issues = SelectIssues(civ);
        if (!civ.isPlayerControlled)
            record.candidates = GenerateCandidates(civ, rules, record, seed);
        return record;
    }

    public static void Resolve(Civilization civ, ElectionRules rules, ElectionRecord election, int turn)
    {
        if (civ == null || election == null || election.resolved) return;
        if (civ.isPlayerControlled) ResolvePlayerElection(civ, election, turn);
        else ResolveAiElection(civ, rules, election, turn);
    }

    public static void ConcedePlayerDefeat(Civilization civ)
    {
        if (civ?.electionState?.pendingPlayerDefeat != true) return;
        civ.electionState.pendingPlayerDefeat = false;
        ElectionStateChanged?.Invoke(civ);
        GameManager.Instance?.EndGame(null, null, "Your administration conceded power after losing the national election.");
    }

    public static bool SeizePower(Civilization civ)
    {
        if (civ?.electionState?.pendingPlayerDefeat != true || civ.electionState.authoritarianTakeover) return false;
        GovernmentData target = FindEmergencyAuthoritarianGovernment();
        if (target == null)
        {
            Debug.LogError("[ElectionManager] Seize Power failed: no non-electoral authoritarian government is configured.");
            return false;
        }

        civ.electionState.authoritarianTakeover = true;
        civ.electionState.pendingPlayerDefeat = false;
        civ.electionState.governmentLegitimacy = Mathf.Clamp(civ.electionState.governmentLegitimacy - 65f, 0f, 100f);
        civ.ForceGovernmentTransition(target);

        var result = FindHistory(civ.electionState, civ.electionState.pendingDefeatElectionId);
        if (result != null) result.playerSeizedPower = true;
        if (!civ.electionState.takeoverRebellionTriggered)
        {
            civ.electionState.takeoverRebellionTriggered = true;
            civ.TriggerElectionTakeoverRebellion();
        }

        AddTemporaryOpinionModifier(civ, "Election result overturned", -30f, 25);
        if (GameManager.Instance != null) GameManager.Instance.gamePaused = false;
        ElectionStateChanged?.Invoke(civ);
        return true;
    }

    public static string GetCurrentRulerName(Civilization civ)
        => civ?.electionState?.currentOffice != null
            ? civ.electionState.currentOffice.officeholderName
            : civ?.leader != null ? civ.leader.leaderName : "Leader";

    public static Sprite GetCurrentRulerPortrait(Civilization civ)
    {
        var office = civ?.electionState?.currentOffice;
        var electedPortrait = office != null && !string.IsNullOrWhiteSpace(office.portraitId)
            ? GovernorPortraitService.GetSprite(office.portraitId) : null;
        return electedPortrait != null ? electedPortrait : civ?.leader?.portrait;
    }

    private static void ResolvePlayerElection(Civilization civ, ElectionRecord election, int turn)
    {
        election.resolved = true;
        election.resolutionTurn = turn;
        election.publicOpinionAtElection = civ.electionState.publicApproval;
        election.playerVictory = election.publicOpinionAtElection >= PlayerVictoryThreshold;
        election.outcomeSummary = election.playerVictory ? "Administration reelected" : "Administration defeated";
        civ.electionState.electionsHeld++;
        AddHistory(civ.electionState, election);
        civ.electionState.activeElection = null;

        if (election.playerVictory)
        {
            civ.electionState.nextElectionTurn = turn + NationalTermTurns;
            civ.electionState.governmentLegitimacy = Mathf.Clamp(civ.electionState.governmentLegitimacy + 5f, 0f, 100f);
            return;
        }

        civ.electionState.pendingPlayerDefeat = true;
        civ.electionState.pendingDefeatElectionId = election.electionId;
        if (GameManager.Instance != null) GameManager.Instance.gamePaused = true;
        PlayerElectionDefeatPending?.Invoke(civ);
    }

    private static void ResolveAiElection(Civilization civ, ElectionRules rules, ElectionRecord election, int turn)
    {
        if (election.candidates == null || election.candidates.Count == 0)
            election.candidates = GenerateCandidates(civ, rules, election, election.deterministicSeed);
        if (election.candidates.Count == 0) return;

        foreach (var candidate in election.candidates)
        {
            float issue = candidate.priorities.Count == 0 ? 0f : election.issues.Where(i => candidate.priorities.Contains(i.issue)).Sum(i => i.salience) / candidate.priorities.Count;
            float incumbent = candidate.incumbent ? (civ.electionState.publicApproval - 50f) / 100f : 0f;
            float endorsement = candidate.candidateId == election.endorsedCandidateId ? Mathf.Min(0.12f, 0.035f + election.campaignGoldSpent / 1000f) : 0f;
            float noise = DeterministicNoise(election.deterministicSeed, candidate.candidateId) * Mathf.Clamp(rules.volatility, 0f, 0.25f);
            candidate.finalSupport = candidate.competence * 0.18f
                + candidate.publicAppeal * Mathf.Clamp01(rules.publicOpinionWeight)
                + candidate.eliteAppeal * Mathf.Clamp01(rules.governorEliteWeight)
                + issue * 0.22f + incumbent + endorsement + noise;
        }

        var ordered = election.candidates.OrderByDescending(c => c.finalSupport).ThenBy(c => c.candidateId, StringComparer.Ordinal).ToList();
        var winner = ordered[0];
        var governor = winner.governorId >= 0 ? civ.governors?.FirstOrDefault(g => g != null && g.Id == winner.governorId) : null;
        election.winnerCandidateId = winner.candidateId;
        election.winningMargin = ordered.Count > 1 ? Mathf.Clamp01(winner.finalSupport - ordered[1].finalSupport) : 1f;
        election.resolved = true;
        election.resolutionTurn = turn;
        election.outcomeSummary = winner.displayName + " elected";
        civ.electionState.electionsHeld++;
        civ.electionState.currentOffice = new ElectedOfficeRecord
        {
            candidateId = winner.candidateId,
            officeholderName = winner.displayName,
            governorId = winner.governorId,
            portraitId = governor?.PortraitId,
            factionName = winner.factionName,
            title = string.IsNullOrWhiteSpace(rules.executiveTitle) ? civ.currentGovernment.leaderTitleSuffix : rules.executiveTitle,
            electionWonTurn = turn,
            termEndTurn = turn + NationalTermTurns,
            mandate = new List<ElectionIssueType>(winner.priorities),
        };
        civ.electionState.nextElectionTurn = turn + NationalTermTurns;
        civ.electionState.governmentLegitimacy = Mathf.Clamp(civ.electionState.governmentLegitimacy + 8f + election.winningMargin * 12f, 0f, 100f);
        AddHistory(civ.electionState, election);
        civ.electionState.activeElection = null;
    }

    private static void UpdatePublicOpinion(Civilization civ, int turn)
    {
        var state = civ.electionState;
        if (state.lastPublicOpinionUpdateTurn == turn) return;
        state.lastPublicOpinionUpdateTurn = turn;
        state.opinionBreakdown.Clear();
        AddBreakdown(state, "Baseline", 50f);
        AddBreakdown(state, "Economy", Mathf.Clamp(civ.cachedGoldPerTurn / 20f, -1f, 1f) * 12f);
        AddBreakdown(state, "Food security", Mathf.Clamp(civ.cachedFoodPerTurn / 20f, -1f, 1f) * 10f);
        float order = civ.cities == null || !civ.cities.Any(c => c != null) ? 0f
            : (civ.cities.Where(c => c != null).Average(c => c.loyalty) / 50f - 1f) * 15f;
        AddBreakdown(state, "City loyalty", order);
        AddBreakdown(state, "War weariness", -civ.warWeariness * 25f);
        AddBreakdown(state, "Public unrest", -civ.unrestModifier * 15f);
        AddBreakdown(state, "Government", civ.currentGovernment != null ? civ.currentGovernment.publicOpinionModifier : 0f);
        AddBreakdown(state, "Active policies", civ.activePolicies?.Where(p => p != null).Sum(p => p.publicOpinionModifier) ?? 0f);
        AddBreakdown(state, "Political leadership", CalculateGovernorContribution(civ));

        for (int i = state.temporaryOpinionModifiers.Count - 1; i >= 0; i--)
        {
            var modifier = state.temporaryOpinionModifiers[i];
            if (modifier == null || modifier.remainingTurns <= 0)
            {
                state.temporaryOpinionModifiers.RemoveAt(i);
                continue;
            }
            AddBreakdown(state, modifier.source, modifier.value);
            modifier.remainingTurns--;
        }

        float target = state.opinionBreakdown.Sum(entry => entry.value);
        state.publicApproval = Mathf.Clamp(Mathf.Lerp(state.publicApproval, target, 0.12f), 0f, 100f);
        state.governmentLegitimacy = Mathf.Clamp(state.governmentLegitimacy - Mathf.Max(0f, 35f - state.publicApproval) * 0.01f, 0f, 100f);
    }

    private static float CalculateGovernorContribution(Civilization civ)
    {
        var governors = civ.governors?.Where(g => g != null && !g.IsInRebellion).ToList();
        if (governors == null || governors.Count == 0) return 0f;
        float totalWeight = 0f, weightedOpinion = 0f;
        foreach (var governor in governors)
        {
            float weight = 1f + Mathf.Max(0, governor.PowerRank);
            totalWeight += weight;
            weightedOpinion += governor.Opinion * weight;
        }
        return Mathf.Clamp(((weightedOpinion / Mathf.Max(1f, totalWeight)) - 50f) * 0.2f, -10f, 10f);
    }

    private static void ReconcileCurrentRuler(Civilization civ, int turn)
    {
        var office = civ.electionState.currentOffice;
        if (office == null || office.governorId < 0) return;
        var governor = civ.governors?.FirstOrDefault(g => g != null && g.Id == office.governorId && !g.IsInRebellion);
        if (governor != null) return;
        office.candidateId = "caretaker:" + civ.electionState.electionsHeld;
        office.officeholderName = "Caretaker Administration";
        office.governorId = -1;
        office.portraitId = null;
        office.factionName = null;
        office.electionWonTurn = turn;
    }

    private static GovernmentData FindEmergencyAuthoritarianGovernment()
    {
        var governments = PolicyManager.Instance != null
            ? PolicyManager.Instance.allGovernments
            : ResourceCache.GetAllGovernmentData().Where(g => g != null).ToList();
        bool Eligible(GovernmentData government) => government != null
            && government.electionRules?.enabled != true
            && (government.archetypes & (GovernmentArchetypeFlags.CentralizedExecutive
                | GovernmentArchetypeFlags.HereditaryExecutive | GovernmentArchetypeFlags.MachineRule
                | GovernmentArchetypeFlags.Militarized | GovernmentArchetypeFlags.Oligarchic)) != 0;
        return governments?.FirstOrDefault(g => g != null && g.emergencyAuthoritarianGovernment && Eligible(g))
            ?? governments?.FirstOrDefault(Eligible);
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
            Issue(ElectionIssueType.Vassals, civ.ActiveVassalCount * .2f, "Relations with subject states"),
        };
        return all.OrderByDescending(i => i.salience).ThenBy(i => i.issue).Take(3).ToList();
    }

    private static ElectionIssueRecord Issue(ElectionIssueType type, float salience, string summary)
        => new ElectionIssueRecord { issue = type, salience = Mathf.Clamp01(salience), summary = summary };

    private static List<ElectionCandidateRecord> GenerateCandidates(Civilization civ, ElectionRules rules, ElectionRecord election, int seed)
    {
        int count = Mathf.Clamp(rules.candidateCount, 2, 4);
        var result = new List<ElectionCandidateRecord>();
        var eligible = (civ.governors ?? new List<Governor>()).Where(g => g != null && !g.IsInRebellion).OrderByDescending(g => g.PowerRank).ThenBy(g => g.Id).ToList();
        for (int i = 0; i < count; i++)
        {
            Governor governor = i < eligible.Count ? eligible[i] : null;
            string id = governor != null ? "governor:" + governor.Id : "civilian:" + election.electionId + ":" + i;
            float noise = (DeterministicNoise(seed, id) + 1f) * .5f;
            var candidate = new ElectionCandidateRecord
            {
                candidateId = id,
                displayName = governor != null ? governor.Name : "Civic Candidate " + (i + 1),
                governorId = governor?.Id ?? -1,
                factionName = governor?.Faction?.FactionName,
                incumbent = rules.incumbentEligible && civ.electionState.currentOffice?.candidateId == id,
                competence = governor != null ? Mathf.Clamp01((governor.Level + 2f) / 12f) : .4f + noise * .3f,
                eliteAppeal = governor != null ? Mathf.Clamp01(governor.PowerRank / 100f) : .25f + noise * .35f,
                publicAppeal = governor != null ? Mathf.Clamp01((governor.Opinion + 100f) / 200f) : .35f + (1f - noise) * .35f,
            };
            if (election.issues.Count > 0)
            {
                candidate.priorities.Add(election.issues[(i + Mathf.Abs(seed)) % election.issues.Count].issue);
                candidate.priorities.Add(election.issues[(i + 1 + Mathf.Abs(seed)) % election.issues.Count].issue);
            }
            result.Add(candidate);
        }
        return result;
    }

    private static void EnsureState(Civilization civ)
    {
        civ.electionState ??= new ElectionState();
        civ.electionState.temporaryOpinionModifiers ??= new List<PublicOpinionModifier>();
        civ.electionState.opinionBreakdown ??= new List<PublicOpinionBreakdownEntry>();
        civ.electionState.history ??= new List<ElectionRecord>();
    }

    private static void AddBreakdown(ElectionState state, string source, float value)
    {
        if (!Mathf.Approximately(value, 0f)) state.opinionBreakdown.Add(new PublicOpinionBreakdownEntry { source = source, value = value });
    }

    private static void AddHistory(ElectionState state, ElectionRecord election)
    {
        state.history.Add(election);
        while (state.history.Count > MaxElectionHistory) state.history.RemoveAt(0);
    }

    private static ElectionRecord FindHistory(ElectionState state, int electionId)
        => state.history?.LastOrDefault(record => record != null && record.electionId == electionId);

    private static int StableHash(string value)
    {
        unchecked { int hash = 17; foreach (char character in value ?? "") hash = hash * 31 + character; return hash; }
    }

    private static float DeterministicNoise(int seed, string key)
    {
        uint value = (uint)(seed ^ StableHash(key));
        value ^= value << 13; value ^= value >> 17; value ^= value << 5;
        return (value / (float)uint.MaxValue) * 2f - 1f;
    }
}