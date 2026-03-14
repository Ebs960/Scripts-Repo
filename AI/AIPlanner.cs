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
///  3. Update EmpireAI (persistent strategic state → produces EmpireIntent).
///  4. Update OperationalPlanner (turns intent into per-unit role assignments).
///  5. Unstore sheltered units (when not winter).
///  6. Auto-form army groups for coordinated attacks.
///  7. For each unit, use TacticalEvaluator (with context + assignment) to pick the best command.
///  8. Sort all commands by score (highest first) and execute sequentially.
///  9. After unit commands: seasonal shelter building, improvement upgrades, tech/culture.
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

    // Planned commands for the current turn
    private readonly List<AICommand> plannedCommands = new List<AICommand>(64);
    private readonly HashSet<int> assignedUnits = new HashSet<int>();

    public IReadOnlyList<AICommand> PlannedCommands => plannedCommands;
    public ArmyGroupManager Groups => armyGroups;
    public AIContext Context => currentContext;

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

        // 1) Generate danger maps for planets this civ has units on
        GenerateDangerMaps(civ);

        // 2) Build per-turn context cache (replaces per-unit BFS in TacticalEvaluator)
        currentContext = new AIContext();
        currentContext.Build(civ, dangerMaps);

        // 3) Update empire-level strategic AI (persistent across turns)
        var empire = GetOrCreateEmpire(civ);
        empire.UpdateForTurn(civ, currentContext);

        // 4) Update operational planner (turns intent into unit role assignments)
        var opPlanner = GetOrCreateOpPlanner(civ);
        opPlanner.UpdateAssignments(civ, empire.Intent, currentContext);

        // 5) Unstore sheltered units when not winter
        PlanUnstores(civ);

        // 6) Refresh and auto-form army groups
        armyGroups.Refresh();
        foreach (var kv in dangerMaps)
            armyGroups.AutoFormGroups(civ, kv.Value, kv.Key);

        // 7) Plan commands for each unit (combat first, then workers)
        PlanCombatUnits(civ, empire.Intent, opPlanner);
        PlanWorkerUnits(civ, empire.Intent, opPlanner);

        // 8) Sort by score (highest first) so the most impactful actions execute first
        plannedCommands.Sort((a, b) => b.score.CompareTo(a.score));

        if (Debug.isDebugBuild)
        {
            string civName = civ.civData != null ? civ.civData.civName : "?";
            Debug.Log($"[AIPlanner] Planned {plannedCommands.Count} commands for {civName} " +
                      $"(goal={empire.CurrentGoal} combat={civ.combatUnits?.Count ?? 0} workers={civ.workerUnits?.Count ?? 0})");
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
            if (dm == null) continue;

            var assignment = opPlanner.GetAssignment(unit);
            var cmd = TacticalEvaluator.DecideBestAction(unit, civ, dm, armyGroups, currentContext, assignment, intent);
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
            if (dm == null) continue;

            var assignment = opPlanner.GetAssignment(worker);
            var cmd = TacticalEvaluator.DecideBestAction(worker, civ, dm, armyGroups, currentContext, assignment, intent);
            if (cmd != null)
            {
                plannedCommands.Add(cmd);
                assignedUnits.Add(worker.GetInstanceID());
            }
        }
    }
}
