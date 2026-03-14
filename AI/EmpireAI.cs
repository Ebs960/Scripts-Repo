using System.Collections.Generic;
using UnityEngine;

// ──────────────────────── Enums ────────────────────────

public enum StrategicGoal
{
    Survive,    // existential threat: famine, about to lose last city
    Explore,    // map largely unknown, need to scout
    Expand,     // found new cities — economy is stable enough
    Develop,    // build up economy, tech, culture
    Defend,     // significant threats near our borders
    Attack      // active war campaign against a target
}

public enum EconomyFocus
{
    Balanced,
    Food,
    Production,
    Gold,
    Science,
    Culture
}

public enum DefensePosture
{
    Aggressive,  // push forward, accept risk
    Balanced,
    Defensive    // pull back, protect assets
}

// ──────────────────────── EmpireIntent (output each turn) ────────────────────────

/// <summary>
/// Read-only snapshot of what the empire wants this turn.
/// Consumed by OperationalPlanner (role assignment) and TacticalEvaluator (score modifiers).
/// </summary>
public class EmpireIntent
{
    public StrategicGoal Goal;
    public EconomyFocus Economy;
    public DefensePosture Posture;
    public float RiskTolerance;        // 0–1
    public float ExplorationPriority;  // 0–1
    public readonly List<WarTarget> WarTargets = new();
    public readonly List<ExpansionTarget> ExpansionTargets = new();

    // Score modifiers applied to all commands of the matching type
    public float AttackBonus;
    public float ExploreBonus;
    public float ForageBonus;
    public float BuildBonus;
    public float SettleBonus;
    public float DefendBonus;
}

public struct WarTarget
{
    public int CivInstanceId;
    public int PreferredCityTile;   // -1 if none
    public float Priority;          // higher = more urgent
    public int AssignedTurn;
}

public struct ExpansionTarget
{
    public int TileIndex;
    public int PlanetIndex;
    public float Score;
    public int DiscoveredTurn;
}

// ──────────────────────── EmpireAI (persistent per-civ state) ────────────────────────

/// <summary>
/// Holds persistent strategic state for one civilization across multiple turns.
/// Updated once per AI turn. Outputs an EmpireIntent that steers the OperationalPlanner
/// and TacticalEvaluator without micromanaging individual unit moves.
///
/// Responsibilities:
///   - Choose a strategic goal (Survive/Explore/Expand/Develop/Defend/Attack)
///   - Maintain war target list and grudge memory
///   - Set economy focus, defense posture, and risk tolerance
///   - Provide score modifiers so tactical actions align with strategy
/// </summary>
public class EmpireAI
{
    // ──── Persistent state ────
    public StrategicGoal CurrentGoal { get; private set; } = StrategicGoal.Explore;
    public StrategicGoal PreviousGoal { get; private set; } = StrategicGoal.Explore;
    public int TurnsSinceGoalChange { get; private set; }
    public int LastUpdateTurn { get; private set; } = -1;

    // Grudge ledger: civInstanceId → accumulated grievance (decays slowly)
    private readonly Dictionary<int, float> grudges = new();

    // Current intent (rebuilt each turn)
    private readonly EmpireIntent intent = new();
    public EmpireIntent Intent => intent;

    // ──── Tuning ────
    private const float GRUDGE_DECAY = 0.92f;       // per-turn multiplier
    private const float GOAL_MOMENTUM = 3f;          // bonus for keeping the same goal
    private const int   MIN_EXPLORE_PERCENT = 25;     // below this we want to explore
    private const int   FOOD_CRISIS_THRESHOLD = 5;
    private const float WAR_DESIRE_THRESHOLD = 12f;   // minimum ScoreWarDecision to add war target

    // ════════════════════════════════════════════════════════
    //  Public API
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Update all strategic state and produce a fresh EmpireIntent for this turn.
    /// Called once per AI turn before OperationalPlanner.
    /// </summary>
    public void UpdateForTurn(Civilization civ, AIContext ctx)
    {
        if (civ == null || ctx == null) return;
        int turn = ctx.TurnNumber;
        if (turn == LastUpdateTurn) return; // already updated
        LastUpdateTurn = turn;

        UpdateGrudges(civ);
        UpdateWarTargets(civ, ctx);
        UpdateExpansionTargets(civ, ctx);

        StrategicGoal chosen = EvaluateGoal(civ, ctx);
        if (chosen != CurrentGoal)
        {
            PreviousGoal = CurrentGoal;
            CurrentGoal = chosen;
            TurnsSinceGoalChange = 0;
        }
        else
        {
            TurnsSinceGoalChange++;
        }

        BuildIntent(civ, ctx);
    }

    // ════════════════════════════════════════════════════════
    //  Goal evaluation — the strategic "brain"
    // ════════════════════════════════════════════════════════

    private StrategicGoal EvaluateGoal(Civilization civ, AIContext ctx)
    {
        // Score each possible goal; highest wins.
        // Momentum bonus keeps the AI from flip-flopping every turn.

        float survive  = ScoreSurvive(civ, ctx);
        float explore  = ScoreExplore(civ, ctx);
        float expand   = ScoreExpand(civ, ctx);
        float develop  = ScoreDevelop(civ, ctx);
        float defend   = ScoreDefend(civ, ctx);
        float attack   = ScoreAttackGoal(civ, ctx);

        // Momentum: bias toward keeping the current goal
        void AddMomentum(StrategicGoal g, ref float s) { if (g == CurrentGoal) s += GOAL_MOMENTUM; }
        AddMomentum(StrategicGoal.Survive, ref survive);
        AddMomentum(StrategicGoal.Explore, ref explore);
        AddMomentum(StrategicGoal.Expand, ref expand);
        AddMomentum(StrategicGoal.Develop, ref develop);
        AddMomentum(StrategicGoal.Defend, ref defend);
        AddMomentum(StrategicGoal.Attack, ref attack);

        // Pick highest
        StrategicGoal best = StrategicGoal.Develop;
        float bestScore = develop;
        void Check(StrategicGoal g, float s) { if (s > bestScore) { bestScore = s; best = g; } }
        Check(StrategicGoal.Survive, survive);
        Check(StrategicGoal.Explore, explore);
        Check(StrategicGoal.Expand, expand);
        Check(StrategicGoal.Defend, defend);
        Check(StrategicGoal.Attack, attack);

        return best;
    }

    private float ScoreSurvive(Civilization civ, AIContext ctx)
    {
        float s = 0f;
        if (ctx.IsFamine) s += 20f;
        if (civ.food <= FOOD_CRISIS_THRESHOLD && !ctx.HasCities) s += 25f;
        int totalUnits = (civ.combatUnits?.Count ?? 0) + (civ.workerUnits?.Count ?? 0);
        if (totalUnits <= 1) s += 15f;
        if (ctx.HasCities && civ.cities.Count == 1)
        {
            // Threat near our only city
            foreach (var kv in ctx.ThreatByPlanet)
                if (kv.Value.EnemyCombatUnits > 0) s += 10f;
        }
        return s;
    }

    private float ScoreExplore(Civilization civ, AIContext ctx)
    {
        float unexplored = 1f - ctx.ExplorationPercent;
        float s = unexplored * 30f; // 70% unexplored → 21, 30% → 9
        if (ctx.ExplorationPercent < MIN_EXPLORE_PERCENT / 100f) s += 8f;
        // Leader personality
        if (civ.leader != null && civ.leader.expansion >= 7) s += 4f;
        // Less valuable if we have immediate threats
        foreach (var kv in ctx.ThreatByPlanet)
            if (kv.Value.EnemyCombatUnits > 2) s -= 5f;
        return s;
    }

    private float ScoreExpand(Civilization civ, AIContext ctx)
    {
        float s = 0f;
        if (!civ.CanFoundMoreCities()) return -100f;
        bool hasSettler = false;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null && w.data.canFoundCity) { hasSettler = true; break; }
        if (!hasSettler) return -50f;

        // Economy must be healthy enough to sustain a new city
        int netFood = civ.GetNetFoodPerTurn();
        if (netFood > 0) s += 10f;
        if (civ.food > 30) s += 5f;

        // Good city sites available?
        int sites = 0;
        foreach (var kv in ctx.CitySites) sites += kv.Value?.Count ?? 0;
        s += Mathf.Min(15f, sites * 3f);

        // Leader preference
        if (civ.leader != null)
        {
            if (civ.leader.primaryAgenda == LeaderAgenda.Expansionist) s += 8f;
            if (civ.leader.expansion >= 7) s += 4f;
        }
        return s;
    }

    private float ScoreDevelop(Civilization civ, AIContext ctx)
    {
        float s = 10f; // always a reasonable default
        if (ctx.HasCities) s += 5f;
        int netFood = civ.GetNetFoodPerTurn();
        if (netFood < 0) s -= 5f; // economy struggling — develop to fix it
        if (civ.leader != null)
        {
            if (civ.leader.primaryAgenda == LeaderAgenda.Scientific) s += 6f;
            if (civ.leader.primaryAgenda == LeaderAgenda.Cultural) s += 4f;
            if (civ.leader.primaryAgenda == LeaderAgenda.Economic) s += 5f;
        }
        return s;
    }

    private float ScoreDefend(Civilization civ, AIContext ctx)
    {
        float s = 0f;
        foreach (var kv in ctx.ThreatByPlanet)
        {
            var t = kv.Value;
            s += t.EnemyCombatUnits * 3f;
            s += t.PredatorAnimals * 1f;
        }
        // Stronger if we're weak relative to threats
        if (ctx.TotalEnemyStrength > ctx.TotalMilitaryStrength * 1.2f) s += 10f;
        // Weaker if we're much stronger
        if (ctx.TotalMilitaryStrength > ctx.TotalEnemyStrength * 2f) s -= 5f;
        return s;
    }

    private float ScoreAttackGoal(Civilization civ, AIContext ctx)
    {
        float s = 0f;
        if (intent.WarTargets.Count == 0 && WarTargetCandidates(civ, ctx) == 0) return -10f;
        if (intent.WarTargets.Count > 0) s += 10f;
        // Military advantage
        float ratio = ctx.TotalMilitaryStrength / Mathf.Max(1f, ctx.TotalEnemyStrength);
        s += (ratio - 1f) * 6f;
        // Leader personality
        if (civ.leader != null)
        {
            if (civ.leader.isWarmonger) s += 8f;
            if (civ.leader.primaryAgenda == LeaderAgenda.Militaristic) s += 5f;
            if (civ.leader.aggressiveness >= 8) s += 4f;
        }
        return s;
    }

    private int WarTargetCandidates(Civilization civ, AIContext ctx)
    {
        int count = 0;
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return 0;
        foreach (var other in allCivs)
        {
            if (other == civ) continue;
            float warScore = AIScorer.ScoreWarDecision(civ, other);
            if (warScore >= WAR_DESIRE_THRESHOLD) count++;
        }
        return count;
    }

    // ════════════════════════════════════════════════════════
    //  Grudge management
    // ════════════════════════════════════════════════════════

    private void UpdateGrudges(Civilization civ)
    {
        // Decay existing grudges
        var keys = new List<int>(grudges.Keys);
        foreach (int k in keys)
        {
            grudges[k] *= GRUDGE_DECAY;
            if (grudges[k] < 0.5f) grudges.Remove(k);
        }

        // Increment from recent diplomatic events
        try
        {
            if (DiplomacyManager.Instance == null) return;
            var memory = DiplomacyManager.Instance.GetDiplomaticMemory(civ);
            var allCivs = CivilizationManager.Instance?.GetAllCivs();
            if (allCivs == null) return;

            foreach (var other in allCivs)
            {
                if (other == civ) continue;
                int id = other.GetInstanceID();
                float add = 0f;
                if (memory.HasRecentEvent(other, DiplomaticEventType.DeclaredWar, 5)) add += 20f;
                if (memory.HasRecentEvent(other, DiplomaticEventType.BrokePeace, 5)) add += 15f;
                if (memory.HasRecentEvent(other, DiplomaticEventType.AttackedAlly, 5)) add += 10f;
                if (memory.HasRecentEvent(other, DiplomaticEventType.Denounced, 5)) add += 5f;
                if (add > 0)
                {
                    if (!grudges.ContainsKey(id)) grudges[id] = 0f;
                    grudges[id] += add;
                }
            }
        }
        catch { }
    }

    public float GetGrudge(Civilization other)
    {
        if (other == null) return 0f;
        grudges.TryGetValue(other.GetInstanceID(), out float g);
        return g;
    }

    // ════════════════════════════════════════════════════════
    //  War targets
    // ════════════════════════════════════════════════════════

    private void UpdateWarTargets(Civilization civ, AIContext ctx)
    {
        // Remove stale targets (civ destroyed, peace made)
        intent.WarTargets.RemoveAll(wt =>
        {
            var allCivs = CivilizationManager.Instance?.GetAllCivs();
            if (allCivs == null) return true;
            foreach (var c in allCivs)
            {
                if (c.GetInstanceID() == wt.CivInstanceId)
                {
                    // Still at war?
                    if (civ.relations != null && civ.relations.TryGetValue(c, out var state) && state == DiplomaticState.War)
                        return false;
                    // Not at war but still have desire?
                    float desire = AIScorer.ScoreWarDecision(civ, c);
                    return desire < WAR_DESIRE_THRESHOLD * 0.5f;
                }
            }
            return true; // civ not found
        });

        // Consider new targets
        var allCivsList = CivilizationManager.Instance?.GetAllCivs();
        if (allCivsList == null) return;
        foreach (var other in allCivsList)
        {
            if (other == civ) continue;
            int otherId = other.GetInstanceID();
            bool alreadyTarget = false;
            foreach (var wt in intent.WarTargets)
                if (wt.CivInstanceId == otherId) { alreadyTarget = true; break; }
            if (alreadyTarget) continue;

            float warScore = AIScorer.ScoreWarDecision(civ, other);
            if (warScore >= WAR_DESIRE_THRESHOLD)
            {
                int cityTile = FindBestCityTarget(other, civ);
                intent.WarTargets.Add(new WarTarget
                {
                    CivInstanceId = otherId,
                    PreferredCityTile = cityTile,
                    Priority = warScore,
                    AssignedTurn = ctx.TurnNumber
                });
            }
        }

        // Sort by priority
        intent.WarTargets.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    private static int FindBestCityTarget(Civilization defender, Civilization attacker)
    {
        if (defender.cities == null || defender.cities.Count == 0) return -1;
        City best = null;
        int bestDist = int.MaxValue;
        foreach (var city in defender.cities)
        {
            if (city == null) continue;
            if (attacker.combatUnits != null)
            {
                foreach (var u in attacker.combatUnits)
                {
                    if (u == null || u.planetIndex != city.planetIndex) continue;
                    var ts = TileSystem.GetForPlanet(u.planetIndex);
                    if (ts == null) continue;
                    int d = ts.GetTileDistance(u.currentTileIndex, city.centerTileIndex);
                    if (d < bestDist) { bestDist = d; best = city; }
                }
            }
        }
        return best != null ? best.centerTileIndex : defender.cities[0].centerTileIndex;
    }

    // ════════════════════════════════════════════════════════
    //  Expansion targets
    // ════════════════════════════════════════════════════════

    private void UpdateExpansionTargets(Civilization civ, AIContext ctx)
    {
        // Remove stale targets (tile now owned, too old)
        intent.ExpansionTargets.RemoveAll(et =>
        {
            var ts = TileSystem.GetForPlanet(et.PlanetIndex);
            if (ts == null) return true;
            var td = ts.GetTileData(et.TileIndex);
            if (td == null) return true;
            if (td.owner != null && td.owner != civ) return true; // someone else settled nearby
            if (ctx.TurnNumber - et.DiscoveredTurn > 30) return true; // stale
            return false;
        });

        // Add new candidates from AIContext
        foreach (var kv in ctx.CitySites)
        {
            if (kv.Value == null) continue;
            foreach (var site in kv.Value)
            {
                bool alreadyTracked = false;
                foreach (var et in intent.ExpansionTargets)
                    if (et.TileIndex == site.TileIndex && et.PlanetIndex == site.PlanetIndex) { alreadyTracked = true; break; }
                if (alreadyTracked) continue;

                intent.ExpansionTargets.Add(new ExpansionTarget
                {
                    TileIndex = site.TileIndex,
                    PlanetIndex = site.PlanetIndex,
                    Score = site.Score,
                    DiscoveredTurn = ctx.TurnNumber
                });
            }
        }

        // Keep top 5
        intent.ExpansionTargets.Sort((a, b) => b.Score.CompareTo(a.Score));
        while (intent.ExpansionTargets.Count > 5)
            intent.ExpansionTargets.RemoveAt(intent.ExpansionTargets.Count - 1);
    }

    // ════════════════════════════════════════════════════════
    //  Build the intent object with score modifiers
    // ════════════════════════════════════════════════════════

    private void BuildIntent(Civilization civ, AIContext ctx)
    {
        intent.Goal = CurrentGoal;
        intent.Economy = ChooseEconomyFocus(civ, ctx);
        intent.Posture = ChoosePosture(civ, ctx);
        intent.RiskTolerance = ComputeRiskTolerance(civ, ctx);
        intent.ExplorationPriority = ComputeExplorationPriority(ctx);

        // Score modifiers: subtle nudges per goal
        intent.AttackBonus = 0f;
        intent.ExploreBonus = 0f;
        intent.ForageBonus = 0f;
        intent.BuildBonus = 0f;
        intent.SettleBonus = 0f;
        intent.DefendBonus = 0f;

        switch (CurrentGoal)
        {
            case StrategicGoal.Survive:
                intent.ForageBonus = 5f;
                intent.DefendBonus = 3f;
                break;
            case StrategicGoal.Explore:
                intent.ExploreBonus = 5f;
                break;
            case StrategicGoal.Expand:
                intent.SettleBonus = 6f;
                intent.ExploreBonus = 2f;
                break;
            case StrategicGoal.Develop:
                intent.BuildBonus = 4f;
                intent.ForageBonus = 2f;
                break;
            case StrategicGoal.Defend:
                intent.DefendBonus = 5f;
                intent.AttackBonus = 2f;
                break;
            case StrategicGoal.Attack:
                intent.AttackBonus = 6f;
                break;
        }

        if (Debug.isDebugBuild)
        {
            string civName = civ.civData != null ? civ.civData.civName : "?";
            Debug.Log($"[EmpireAI] {civName}: Goal={CurrentGoal} Economy={intent.Economy} " +
                      $"Posture={intent.Posture} Risk={intent.RiskTolerance:F2} " +
                      $"WarTargets={intent.WarTargets.Count} ExpTargets={intent.ExpansionTargets.Count}");
        }
    }

    // ──── Economy focus ────

    private EconomyFocus ChooseEconomyFocus(Civilization civ, AIContext ctx)
    {
        if (ctx.IsFamine || civ.food < FOOD_CRISIS_THRESHOLD) return EconomyFocus.Food;
        if (CurrentGoal == StrategicGoal.Survive) return EconomyFocus.Food;

        if (civ.leader != null)
        {
            switch (civ.leader.primaryAgenda)
            {
                case LeaderAgenda.Scientific: return EconomyFocus.Science;
                case LeaderAgenda.Cultural:   return EconomyFocus.Culture;
                case LeaderAgenda.Economic:   return EconomyFocus.Gold;
            }
        }

        // If we have cities and production is low, focus production
        if (ctx.HasCities)
        {
            float totalProd = 0f;
            foreach (var city in civ.cities)
                if (city != null) totalProd += city.GetProductionPerTurn();
            if (totalProd < civ.cities.Count * 3f) return EconomyFocus.Production;
        }

        return EconomyFocus.Balanced;
    }

    // ──── Defense posture ────

    private DefensePosture ChoosePosture(Civilization civ, AIContext ctx)
    {
        if (CurrentGoal == StrategicGoal.Attack) return DefensePosture.Aggressive;
        if (CurrentGoal == StrategicGoal.Survive || CurrentGoal == StrategicGoal.Defend)
            return DefensePosture.Defensive;

        float threatRatio = ctx.TotalEnemyStrength / Mathf.Max(1f, ctx.TotalMilitaryStrength);
        if (threatRatio > 1.5f) return DefensePosture.Defensive;
        if (threatRatio < 0.5f && civ.leader != null && civ.leader.aggressiveness >= 7)
            return DefensePosture.Aggressive;

        return DefensePosture.Balanced;
    }

    // ──── Risk tolerance ────

    private float ComputeRiskTolerance(Civilization civ, AIContext ctx)
    {
        float risk = 0.5f; // baseline

        if (civ.leader != null)
        {
            risk += (civ.leader.aggressiveness - 5) * 0.04f; // ±0.2 from personality
            if (civ.leader.isWarmonger) risk += 0.1f;
        }

        // Desperate civs take more risks
        if (ctx.IsFamine) risk += 0.15f;
        if (CurrentGoal == StrategicGoal.Survive) risk += 0.2f;

        // Strong military = can afford more risk
        float ratio = ctx.TotalMilitaryStrength / Mathf.Max(1f, ctx.TotalEnemyStrength);
        risk += Mathf.Clamp((ratio - 1f) * 0.1f, -0.2f, 0.2f);

        return Mathf.Clamp01(risk);
    }

    // ──── Exploration priority ────

    private float ComputeExplorationPriority(AIContext ctx)
    {
        float unexplored = 1f - ctx.ExplorationPercent;
        float p = unexplored; // 70% unexplored → 0.7 priority
        if (CurrentGoal == StrategicGoal.Explore) p = Mathf.Max(p, 0.7f);
        if (CurrentGoal == StrategicGoal.Attack) p *= 0.3f;
        return Mathf.Clamp01(p);
    }
}
