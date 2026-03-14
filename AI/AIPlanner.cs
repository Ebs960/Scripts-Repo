using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The main AI orchestrator. Plans an entire turn's worth of commands for a civilization,
/// then executes them in priority order. This is the single entry point called by CivilizationManager.
///
/// Full turn pipeline:
///  1. Generate DangerMaps for each planet the civ has units on.
///  2. Build AIContext (per-turn cache: frontiers, resource hotspots, threat summaries).
///  3. Update EmpireAI (persistent strategic state → produces EmpireIntent with HTN pillars/objectives).
///  4. Update OperationalPlanner (turns intent + objectives into per-unit role assignments).
///  5. Unstore sheltered units (when not winter).
///  6. Auto-form army groups for coordinated attacks.
///  7. Plan group-level actions (Rally/Advance/Attack/Hold) for army groups.
///  8. For remaining ungrouped units, use TacticalEvaluator (with context + assignment) for best command.
///  9. Apply score noise from AiBudget (difficulty scaling).
///  10. Sort all commands by score (highest first) and execute sequentially.
///
/// AiBudget controls search depth, candidate limits, and score noise per difficulty,
/// ensuring harder AI thinks deeper rather than getting resource bonuses.
/// </summary>
public class AIPlanner
{
    // Per-planet danger maps (regenerated each turn)
    private readonly Dictionary<int, DangerMap> dangerMaps = new Dictionary<int, DangerMap>();
    private readonly ArmyGroupManager armyGroups = new ArmyGroupManager();

    // Empire layer: persistent per-civ strategic state
    private readonly Dictionary<int, EmpireAI> empireStates = new Dictionary<int, EmpireAI>();
    // Operational layer: persistent per-civ role assignments
    private readonly Dictionary<int, OperationalPlanner> opPlanners = new Dictionary<int, OperationalPlanner>();
    // Per-turn context cache
    private AIContext currentContext;

    // Difficulty-scaling intelligence budget
    private AiBudget budget = AiBudget.ForDifficulty(AIDifficulty.Hard);

    // Planned commands for the current turn
    private readonly List<AICommand> plannedCommands = new List<AICommand>(64);
    private readonly HashSet<int> assignedUnits = new HashSet<int>();

    public IReadOnlyList<AICommand> PlannedCommands => plannedCommands;
    public ArmyGroupManager Groups => armyGroups;
    public AIContext Context => currentContext;
    public AiBudget Budget => budget;

    /// <summary>
    /// Set the intelligence budget (call once at game start or when difficulty changes).
    /// </summary>
    public void SetBudget(AiBudget newBudget) { if (newBudget != null) budget = newBudget; }
    public void SetDifficulty(AIDifficulty difficulty) => budget = AiBudget.ForDifficulty(difficulty);

    /// <summary>
    /// Full AI turn: plan then execute. Called from CivilizationManager.CompleteAITurn.
    /// </summary>
    public void ExecuteTurn(Civilization civ)
    {
        if (civ == null) return;
        PlanTurn(civ);
        ExecuteCommands();
    }

    // ─────────────────────── Phase 1: Planning ───────────────────────

    public void PlanTurn(Civilization civ)
    {
        plannedCommands.Clear();
        assignedUnits.Clear();

        // 1) Generate danger maps (skip on Easy if disabled)
        if (budget.EnableDangerMap)
            GenerateDangerMaps(civ);
        else
            dangerMaps.Clear();

        // 2) Build per-turn context cache (budget controls scan limits)
        currentContext = new AIContext();
        currentContext.Build(civ, dangerMaps, budget);

        // 3) Update empire-level strategic AI (if budget allows)
        EmpireIntent intent = null;
        OperationalPlanner opPlanner = null;

        if (budget.EnableStrategicPlanning)
        {
            var empire = GetOrCreateEmpire(civ);
            empire.UpdateForTurn(civ, currentContext);
            intent = empire.Intent;

            // 4) Update operational planner
            opPlanner = GetOrCreateOpPlanner(civ);
            opPlanner.UpdateAssignments(civ, intent, currentContext);
        }

        // 5) Unstore sheltered units when not winter
        PlanUnstores(civ);

        // 6) Refresh and auto-form army groups (if budget allows)
        if (budget.EnableArmyGroups)
        {
            armyGroups.Refresh();
            foreach (var kv in dangerMaps)
                armyGroups.AutoFormGroups(civ, kv.Value, kv.Key, budget.ArmyGroupRange);
        }

        // 7) Plan group-level actions (group decides, not individual units)
        if (budget.EnableArmyGroups)
            PlanArmyGroups(civ, intent);

        // 8) Plan commands for remaining ungrouped units
        PlanCombatUnits(civ, intent, opPlanner);
        PlanWorkerUnits(civ, intent, opPlanner);

        // 9) Apply score noise from budget (Easy AI makes more mistakes)
        if (budget.ScoreNoise > 0f)
            ApplyScoreNoise();

        // 10) Sort by score (highest first)
        plannedCommands.Sort((a, b) => b.score.CompareTo(a.score));

        // Cap total commands (budget limit)
        // Not needed for correctness but prevents runaway on huge empires

        if (Debug.isDebugBuild)
        {
            string civName = civ.civData != null ? civ.civData.civName : "?";
            string goalStr = intent != null ? intent.Goal.ToString() : "none";
            Debug.Log($"[AIPlanner] Planned {plannedCommands.Count} commands for {civName} " +
                      $"(goal={goalStr} budget={budget.ScoreNoise:F1}noise " +
                      $"combat={civ.combatUnits?.Count ?? 0} workers={civ.workerUnits?.Count ?? 0})");
        }
    }

    // ─────────────────────── Phase 2: Execution ───────────────────────

    public void ExecuteCommands()
    {
        foreach (var cmd in plannedCommands)
        {
            if (cmd == null) continue;
            try
            {
                if (cmd.CanExecute())
                    cmd.Execute();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AIPlanner] Command failed for {cmd.unit?.name}: {ex.Message}");
            }
        }
        plannedCommands.Clear();
        assignedUnits.Clear();
    }

    // ─────────────────────── Per-civ persistent state ───────────────────────

    private EmpireAI GetOrCreateEmpire(Civilization civ)
    {
        int id = civ.GetInstanceID();
        if (!empireStates.TryGetValue(id, out var e))
        {
            e = new EmpireAI();
            empireStates[id] = e;
        }
        return e;
    }

    private OperationalPlanner GetOrCreateOpPlanner(Civilization civ)
    {
        int id = civ.GetInstanceID();
        if (!opPlanners.TryGetValue(id, out var p))
        {
            p = new OperationalPlanner();
            opPlanners[id] = p;
        }
        return p;
    }

    // ─────────────────────── Danger map generation ───────────────────────

    private void GenerateDangerMaps(Civilization civ)
    {
        dangerMaps.Clear();
        var planets = new HashSet<int>();

        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.planetIndex >= 0) planets.Add(u.planetIndex);
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.planetIndex >= 0) planets.Add(w.planetIndex);

        foreach (int pIndex in planets)
        {
            var dm = new DangerMap();
            dm.Generate(civ, pIndex);
            dangerMaps[pIndex] = dm;
        }
    }

    public DangerMap GetDangerMap(int planetIndex)
    {
        dangerMaps.TryGetValue(planetIndex, out var dm);
        return dm;
    }

    // ─────────────────────── Unstore planning ───────────────────────

    private void PlanUnstores(Civilization civ)
    {
        if (ClimateManager.Instance == null) return;

        void TryUnstore(BaseUnit unit)
        {
            if (unit == null || !unit.isStored || unit.storedInImprovement == null) return;
            if (ClimateManager.Instance.GetSeasonForPlanet(unit.planetIndex) == Season.Winter) return;
            var cmd = new AIUnstoreCommand { unit = unit, planetIndex = unit.planetIndex, score = 100f };
            plannedCommands.Add(cmd);
            assignedUnits.Add(unit.GetInstanceID());
        }

        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits) TryUnstore(u);
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits) TryUnstore(w);
    }

    // ─────────────────────── Group-level planning ───────────────────────
    // Instead of each grouped unit independently deciding, the group decides ONE action
    // and derives per-unit commands. This gives coherent coordinated behavior.

    private void PlanArmyGroups(Civilization civ, EmpireIntent intent)
    {
        foreach (var group in armyGroups.Groups)
        {
            if (group.Count == 0) continue;
            var dm = GetDangerMap(group.PlanetIndex);
            if (dm == null) dm = new DangerMap();

            GroupAction action = group.DecideAction(dm);
            var commands = group.ExpandToCommands(action, civ, dm);

            foreach (var cmd in commands)
            {
                plannedCommands.Add(cmd);
                if (cmd.unit != null) assignedUnits.Add(cmd.unit.GetInstanceID());
            }
        }
    }

    // ─────────────────────── Combat unit planning ───────────────────────

    private void PlanCombatUnits(Civilization civ, EmpireIntent intent, OperationalPlanner opPlanner)
    {
        if (civ.combatUnits == null) return;
        foreach (var unit in civ.combatUnits)
        {
            if (unit == null || unit.hasActedThisTurn || unit.IsInOrbit) continue;
            if (assignedUnits.Contains(unit.GetInstanceID())) continue;
            if (unit.isStored) continue;
            if (unit.planetIndex < 0) continue;

            var dm = GetDangerMap(unit.planetIndex);
            if (dm == null) dm = new DangerMap();

            var assignment = opPlanner?.GetAssignment(unit);
            var cmd = TacticalEvaluator.DecideBestAction(unit, civ, dm, armyGroups, currentContext, assignment, intent, budget);
            if (cmd != null)
            {
                plannedCommands.Add(cmd);
                assignedUnits.Add(unit.GetInstanceID());
            }
        }
    }

    // ─────────────────────── Worker unit planning ───────────────────────

    private void PlanWorkerUnits(Civilization civ, EmpireIntent intent, OperationalPlanner opPlanner)
    {
        if (civ.workerUnits == null) return;
        foreach (var worker in civ.workerUnits)
        {
            if (worker == null || worker.currentTileIndex < 0) continue;
            if (assignedUnits.Contains(worker.GetInstanceID())) continue;
            if (worker.isStored) continue;
            if (worker.planetIndex < 0) continue;

            var dm = GetDangerMap(worker.planetIndex);
            if (dm == null) dm = new DangerMap();

            var assignment = opPlanner?.GetAssignment(worker);
            var cmd = TacticalEvaluator.DecideBestAction(worker, civ, dm, armyGroups, currentContext, assignment, intent, budget);
            if (cmd != null)
            {
                plannedCommands.Add(cmd);
                assignedUnits.Add(worker.GetInstanceID());
            }
        }
    }

    // ─────────────────────── Score noise (difficulty scaling) ───────────────────────

    private void ApplyScoreNoise()
    {
        float noise = budget.ScoreNoise;
        foreach (var cmd in plannedCommands)
            cmd.score += Random.Range(-noise, noise);
    }
}
