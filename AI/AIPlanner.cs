using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

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

    // Phase timing telemetry (ms per phase, readable by debug overlay)
    public float TimeDangerMap { get; private set; }
    public float TimeContext { get; private set; }
    public float TimeStrategic { get; private set; }
    public float TimeOperational { get; private set; }
    public float TimeTactical { get; private set; }
    public float TimeTotal { get; private set; }

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
        var totalSw = Stopwatch.StartNew();
        var phaseSw = new Stopwatch();
        plannedCommands.Clear();
        assignedUnits.Clear();

        // ── Apply procedural persona (modulates AIScorer weights for this civ) ──
        AIScorer.ApplyPersona(civ.leader);

        // ── Phase 1: Danger Maps ──
        phaseSw.Restart();
        if (budget.EnableDangerMap)
            GenerateDangerMaps(civ);
        else
            dangerMaps.Clear();
        TimeDangerMap = phaseSw.ElapsedMilliseconds;

        // ── Phase 2: Context Cache ──
        phaseSw.Restart();
        currentContext = new AIContext();
        currentContext.Build(civ, dangerMaps, budget);
        TimeContext = phaseSw.ElapsedMilliseconds;

        // ── Phase 3: Strategic (EmpireAI) ──
        phaseSw.Restart();
        EmpireIntent intent = null;
        OperationalPlanner opPlanner = null;
        if (budget.EnableStrategicPlanning)
        {
            var empire = GetOrCreateEmpire(civ);
            empire.UpdateForTurn(civ, currentContext);
            intent = empire.Intent;
        }
        TimeStrategic = phaseSw.ElapsedMilliseconds;

        // ── Phase 4: Operational (OperationalPlanner) ──
        phaseSw.Restart();
        if (budget.EnableStrategicPlanning)
        {
            opPlanner = GetOrCreateOpPlanner(civ);
            opPlanner.UpdateAssignments(civ, intent, currentContext);
        }
        PlanUnstores(civ);
        if (budget.EnableArmyGroups)
        {
            armyGroups.Refresh();
            foreach (var kv in dangerMaps)
                armyGroups.AutoFormGroups(civ, kv.Value, kv.Key, budget.ArmyGroupRange);
        }
        if (budget.EnableArmyGroups)
            PlanArmyGroups(civ, intent);
        TimeOperational = phaseSw.ElapsedMilliseconds;

        // ── Phase 5: Tactical (per-unit TacticalEvaluator) ──
        phaseSw.Restart();
        PlanCombatUnits(civ, intent, opPlanner);
        PlanWorkerUnits(civ, intent, opPlanner);
        if (budget.ScoreNoise > 0f)
            ApplyScoreNoise();
        plannedCommands.Sort((a, b) => b.score.CompareTo(a.score));
        TimeTactical = phaseSw.ElapsedMilliseconds;

        // ── Restore default scoring weights ──
        AIScorer.ResetPersona();

        totalSw.Stop();
        TimeTotal = totalSw.ElapsedMilliseconds;

        if (Debug.isDebugBuild)
        {
            string civName = civ.civData != null ? civ.civData.civName : "?";
            string goalStr = intent != null ? intent.Goal.ToString() : "none";
            Debug.Log($"[AIPlanner] {civName}: {plannedCommands.Count} cmds, goal={goalStr}, " +
                      $"timing: danger={TimeDangerMap:F0}ms ctx={TimeContext:F0}ms " +
                      $"strat={TimeStrategic:F0}ms ops={TimeOperational:F0}ms " +
                      $"tact={TimeTactical:F0}ms total={TimeTotal:F0}ms");
            if (plannedCommands.Count > 0)
            {
                var top = plannedCommands.Take(5)
                    .Select(cmd => $"{cmd.GetType().Name}:{cmd.unit?.name ?? "<null>"}:{cmd.score:F1}");
                Debug.Log($"[AIPlanner] {civName} top cmds -> {string.Join(", ", top)}");
            }
        }
    }

    // ─────────────────────── Phase 2: Execution ───────────────────────

    public void ExecuteCommands()
    {
        int executed = 0;
        int skipped = 0;
        foreach (var cmd in plannedCommands)
        {
            if (cmd == null) continue;
            try
            {
                bool canExecute = cmd.CanExecute();
                if (canExecute)
                {
                    cmd.Execute();
                    executed++;
                    if (Debug.isDebugBuild)
                        Debug.Log($"[AIPlanner] Executed {DescribeCommand(cmd)}");
                }
                else
                {
                    skipped++;
                    if (Debug.isDebugBuild)
                        Debug.LogWarning($"[AIPlanner] Skipped {DescribeCommand(cmd)} because CanExecute() returned false.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AIPlanner] Command failed for {cmd.unit?.name}: {ex.Message}");
            }
        }
        if (Debug.isDebugBuild)
            Debug.Log($"[AIPlanner] ExecuteCommands summary: planned={plannedCommands.Count} executed={executed} skipped={skipped}");
        plannedCommands.Clear();
        assignedUnits.Clear();
    }

    private static string DescribeCommand(AICommand cmd)
    {
        if (cmd == null) return "<null cmd>";

        string target = "";
        switch (cmd)
        {
            case AIMoveCommand mv:
                target = $" targetTile={mv.targetTileIndex}";
                break;
            case AIExploreCommand ex:
                target = $" targetTile={ex.targetTileIndex}";
                break;
            case AIApproachCommand ap:
                target = $" approachTile={ap.approachTileIndex} targetUnit={ap.target?.name ?? "<null>"}";
                break;
            case AIRetreatCommand rt:
                target = $" retreatTile={rt.retreatTileIndex}";
                break;
            case AIAttackCommand atk:
                target = $" targetUnit={atk.target?.name ?? "<null>"}";
                break;
            case AIBuildImprovementCommand build:
                target = $" improvement={build.improvement?.improvementName ?? "<null>"}";
                break;
            case AIForageCommand forage:
                target = $" resource={forage.resource?.resourceName ?? "<null>"}";
                break;
        }

        return $"{cmd.GetType().Name} unit={cmd.unit?.name ?? "<null>"} score={cmd.score:F2}{target}";
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
