using System.Collections.Generic;
using UnityEngine;

// ──────────────────────── Unit roles ────────────────────────

public enum UnitRole
{
    Unassigned,   // no directive — TacticalEvaluator picks freely
    Attacker,     // move toward / attack a specific target
    Defender,     // stay near / protect a tile (usually a city)
    Scout,        // explore the map
    Gatherer,     // forage / hunt for food
    HunterGatherer, // nomadic survival: hunt, forage, and seek food/resources before scouting
    Builder,      // build improvements
    Settler       // found a city at a specific location
}

// ──────────────────────── Unit assignment ────────────────────────

/// <summary>
/// An operational-level directive for one unit. Does NOT dictate the exact move —
/// it tells TacticalEvaluator what kind of action to bias toward and what target matters.
/// </summary>
public class UnitAssignment
{
    public int UnitInstanceId;
    public UnitRole Role;
    public int TargetTile = -1;        // destination or area to stay near
    public int TargetCivId = -1;       // for Attacker: which civ we're fighting
    public int AssignedTurn;
    public float Priority;

    public bool IsStale(int currentTurn, int maxAge = 8) => (currentTurn - AssignedTurn) > maxAge;
}

// ──────────────────────── OperationalPlanner ────────────────────────

/// <summary>
/// Mid-level planner that turns EmpireIntent into multi-turn unit assignments.
///
/// Responsibilities:
///   - Assign combat units to attack targets or city defense
///   - Assign workers to food gathering, building, or settling
///   - Assign idle units as scouts
///   - Maintain assignments across turns (only reassign when stale or invalidated)
///   - Does NOT micromanage individual moves — that's TacticalEvaluator's job
///
/// The planner runs once per AI turn between EmpireAI and the per-unit tactical loop.
/// </summary>
public class OperationalPlanner
{
    private readonly Dictionary<int, UnitAssignment> assignments = new(32);

    public IReadOnlyDictionary<int, UnitAssignment> Assignments => assignments;

    /// <summary>
    /// Get the current assignment for a unit, or null if unassigned.
    /// </summary>
    public UnitAssignment GetAssignment(BaseUnit unit)
    {
        if (unit == null) return null;
        assignments.TryGetValue(unit.GetRuntimeId(), out var a);
        return a;
    }

    // ════════════════════════════════════════════════════════
    //  Main update — call once per AI turn
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluate all units and assign/reassign roles based on the current EmpireIntent and AIContext.
    /// Existing valid assignments are kept (multi-turn persistence).
    /// </summary>
    public void UpdateAssignments(Civilization civ, EmpireIntent intent, AIContext ctx)
    {
        if (civ == null || intent == null || ctx == null) return;
        int turn = ctx.TurnNumber;

        // 1. Prune dead/stale assignments
        PruneAssignments(civ, turn);

        // 2. Collect unassigned units
        var freeCombat = new List<CombatUnit>();
        var freeWorkers = new List<WorkerUnit>();
        CollectFreeUnits(civ, freeCombat, freeWorkers);

        // 3. Assign based on goal priority
        //    Higher-priority roles claim units first.
        switch (intent.Goal)
        {
            case StrategicGoal.Survive:
                if (!ctx.HasCities || ctx.NeedFood) AssignHunterGatherers(freeWorkers, civ, ctx, turn);
                else AssignGatherers(freeWorkers, civ, ctx, turn);
                AssignDefenders(freeCombat, civ, ctx, turn);
                AssignScouts(freeCombat, freeWorkers, ctx, turn);
                break;

            case StrategicGoal.Explore:
                // Early nomadic starts must prioritize survival/expansion work before scouting.
                if (!ctx.HasCities || ctx.NeedFood)
                {
                    AssignSettlers(freeWorkers, civ, intent, ctx, turn);
                    AssignHunterGatherers(freeWorkers, civ, ctx, turn);
                    AssignBuilders(freeWorkers, civ, ctx, turn);
                }
                AssignScouts(freeCombat, freeWorkers, ctx, turn);
                AssignGatherers(freeWorkers, civ, ctx, turn);
                AssignDefenders(freeCombat, civ, ctx, turn);
                break;

            case StrategicGoal.Expand:
                AssignSettlers(freeWorkers, civ, intent, ctx, turn);
                if (!ctx.HasCities || ctx.NeedFood) AssignHunterGatherers(freeWorkers, civ, ctx, turn);
                else AssignGatherers(freeWorkers, civ, ctx, turn);
                AssignDefenders(freeCombat, civ, ctx, turn);
                AssignScouts(freeCombat, freeWorkers, ctx, turn);
                break;

            case StrategicGoal.Develop:
                AssignBuilders(freeWorkers, civ, ctx, turn);
                if (!ctx.HasCities || ctx.NeedFood) AssignHunterGatherers(freeWorkers, civ, ctx, turn);
                else AssignGatherers(freeWorkers, civ, ctx, turn);
                AssignDefenders(freeCombat, civ, ctx, turn);
                AssignScouts(freeCombat, freeWorkers, ctx, turn);
                break;

            case StrategicGoal.Defend:
                AssignDefenders(freeCombat, civ, ctx, turn);
                if (!ctx.HasCities || ctx.NeedFood) AssignHunterGatherers(freeWorkers, civ, ctx, turn);
                else AssignGatherers(freeWorkers, civ, ctx, turn);
                AssignBuilders(freeWorkers, civ, ctx, turn);
                break;

            case StrategicGoal.Attack:
                AssignAttackers(freeCombat, civ, intent, ctx, turn);
                AssignDefenders(freeCombat, civ, ctx, turn);
                if (!ctx.HasCities || ctx.NeedFood) AssignHunterGatherers(freeWorkers, civ, ctx, turn);
                else AssignGatherers(freeWorkers, civ, ctx, turn);
                AssignSettlers(freeWorkers, civ, intent, ctx, turn);
                break;
        }

        // 4. Anything still free → scout
        AssignScouts(freeCombat, freeWorkers, ctx, turn);

        if (Debug.isDebugBuild)
        {
            int[] counts = new int[System.Enum.GetValues(typeof(UnitRole)).Length];
            foreach (var kv in assignments) counts[(int)kv.Value.Role]++;
            string civName = civ.civData != null ? civ.civData.civName : "?";
            Debug.Log($"[OpPlanner] {civName}: Atk={counts[(int)UnitRole.Attacker]} " +
                      $"Def={counts[(int)UnitRole.Defender]} Scout={counts[(int)UnitRole.Scout]} " +
                      $"Gather={counts[(int)UnitRole.Gatherer]} HG={counts[(int)UnitRole.HunterGatherer]} Build={counts[(int)UnitRole.Builder]} " +
                      $"Settle={counts[(int)UnitRole.Settler]} Free={counts[(int)UnitRole.Unassigned]}");
        }
    }

    // ════════════════════════════════════════════════════════
    //  Pruning
    // ════════════════════════════════════════════════════════

    private void PruneAssignments(Civilization civ, int currentTurn)
    {
        // Build a HashSet of alive unit IDs for O(1) lookup instead of O(n) scan
        var aliveIds = new HashSet<int>();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits) if (u != null) aliveIds.Add(u.GetRuntimeId());
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits) if (w != null) aliveIds.Add(w.GetRuntimeId());

        var toRemove = new List<int>();
        foreach (var kv in assignments)
        {
            var a = kv.Value;
            // Stale?
            if (a.IsStale(currentTurn)) { toRemove.Add(kv.Key); continue; }
            // Unit still alive and belongs to civ?
            if (!aliveIds.Contains(kv.Key)) { toRemove.Add(kv.Key); continue; }
            // Settler whose target got taken?
            if (a.Role == UnitRole.Settler && a.TargetTile >= 0)
            {
                var ts = TileSystem.GetForPlanet(0) ?? TileSystem.Instance;
                if (ts != null)
                {
                    var td = ts.GetTileData(a.TargetTile);
                    if (td != null && td.owner != null && td.owner != civ) toRemove.Add(kv.Key);
                }
            }
        }
        foreach (int id in toRemove) assignments.Remove(id);
    }

    // ════════════════════════════════════════════════════════
    //  Collect unassigned units
    // ════════════════════════════════════════════════════════

    private void CollectFreeUnits(Civilization civ, List<CombatUnit> freeCombat, List<WorkerUnit> freeWorkers)
    {
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
            {
                if (u == null || u.isStored || u.IsInOrbit || u.currentHealth <= 0) continue;
                if (!assignments.ContainsKey(u.GetRuntimeId())) freeCombat.Add(u);
            }
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
            {
                if (w == null || w.isStored || w.currentHealth <= 0) continue;
                if (!assignments.ContainsKey(w.GetRuntimeId())) freeWorkers.Add(w);
            }
    }

    // ════════════════════════════════════════════════════════
    //  Role assignment methods
    // ════════════════════════════════════════════════════════

    // ──── Attackers ────

    private void AssignAttackers(List<CombatUnit> pool, Civilization civ, EmpireIntent intent, AIContext ctx, int turn)
    {
        if (intent.WarTargets.Count == 0 || pool.Count == 0) return;

        // Distribute combat units across war targets (highest priority first)
        foreach (var wt in intent.WarTargets)
        {
            if (pool.Count == 0) break;
            int assignCount = Mathf.Max(1, pool.Count / Mathf.Max(1, intent.WarTargets.Count));

            // Sort pool by distance to target city (closest first)
            if (wt.PreferredCityTile >= 0)
            {
                pool.Sort((a, b) =>
                {
                    var ts = TileSystem.GetForPlanet(a.planetIndex) ?? TileSystem.Instance;
                    if (ts == null) return 0;
                    int da = ts.GetTileDistance(a.currentTileIndex, wt.PreferredCityTile);
                    int db = ts.GetTileDistance(b.currentTileIndex, wt.PreferredCityTile);
                    return da.CompareTo(db);
                });
            }

            for (int i = 0; i < assignCount && pool.Count > 0; i++)
            {
                var unit = pool[0];
                pool.RemoveAt(0);
                Assign(unit, UnitRole.Attacker, wt.PreferredCityTile, turn, wt.Priority, wt.CivInstanceId);
            }
        }
    }

    // ──── Defenders ────

    private void AssignDefenders(List<CombatUnit> pool, Civilization civ, AIContext ctx, int turn)
    {
        if (pool.Count == 0 || civ.cities == null || civ.cities.Count == 0) return;

        // Assign one defender per city that has threats nearby
        foreach (var city in civ.cities)
        {
            if (city == null || pool.Count == 0) continue;
            var threats = ctx.GetThreats(city.planetIndex);
            bool needsDefense = threats.EnemyCombatUnits > 0 || threats.PredatorAnimals > 1;
            // Always assign at least one defender if we have multiple combat units
            if (!needsDefense && pool.Count <= 2) continue;

            // Find closest unit to this city
            CombatUnit closest = null;
            int closestDist = int.MaxValue;
            foreach (var u in pool)
            {
                if (u.planetIndex != city.planetIndex) continue;
                var ts = TileSystem.GetForPlanet(u.planetIndex) ?? TileSystem.Instance;
                if (ts == null) continue;
                int d = ts.GetTileDistance(u.currentTileIndex, city.centerTileIndex);
                if (d < closestDist) { closestDist = d; closest = u; }
            }
            if (closest != null)
            {
                pool.Remove(closest);
                Assign(closest, UnitRole.Defender, city.centerTileIndex, turn, 8f);
            }
        }
    }

    // ──── Scouts ────

    private void AssignScouts(List<CombatUnit> combatPool, List<WorkerUnit> workerPool, AIContext ctx, int turn)
    {
        // Prefer assigning combat units as scouts (safer), then workers if no combat units left
        int scoutsWanted = 0;
        foreach (var kv in ctx.FrontierTiles)
            if (kv.Value != null && kv.Value.Count > 0) scoutsWanted++;
        scoutsWanted = Mathf.Max(1, scoutsWanted);

        // Cap scouts: don't over-invest
        int currentScouts = 0;
        foreach (var kv in assignments) if (kv.Value.Role == UnitRole.Scout) currentScouts++;
        scoutsWanted = Mathf.Max(0, scoutsWanted - currentScouts);

        for (int i = 0; i < scoutsWanted && combatPool.Count > 0; i++)
        {
            var unit = combatPool[combatPool.Count - 1]; // take from back (weakest first is fine)
            combatPool.RemoveAt(combatPool.Count - 1);

            // Target: a frontier tile on the unit's planet
            int target = FindFrontierTarget(unit, ctx);
            Assign(unit, UnitRole.Scout, target, turn, 3f);
        }

        // Use remaining free workers as scouts only when the civ is stable enough
        // that workers are not urgently needed for settling/food/build tasks.
        if (!ctx.HasCities || ctx.NeedFood) return;

        // Use remaining free workers as scouts if combat pool exhausted
        for (int i = 0; i < scoutsWanted && workerPool.Count > 0; i++)
        {
            // Only workers that don't have better things to do
            var w = workerPool[workerPool.Count - 1];
            if (w != null && w.data != null && w.data.canFoundCity) continue;
            workerPool.RemoveAt(workerPool.Count - 1);
            int target = FindFrontierTarget(w, ctx);
            Assign(w, UnitRole.Scout, target, turn, 2f);
        }
    }

    private static int FindFrontierTarget(BaseUnit unit, AIContext ctx)
    {
        var frontier = ctx.GetFrontier(unit.planetIndex);
        if (frontier == null || frontier.Count == 0) return -1;

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return -1;

        int best = -1, bestDist = int.MaxValue;
        foreach (int tile in frontier)
        {
            int d = ts.GetTileDistance(unit.currentTileIndex, tile);
            if (d < bestDist) { bestDist = d; best = tile; }
        }
        return best;
    }

    // ──── Gatherers ────

    private void AssignGatherers(List<WorkerUnit> pool, Civilization civ, AIContext ctx, int turn)
    {
        if (pool.Count == 0) return;

        // How many gatherers do we need?
        int wanted = ctx.IsFamine ? pool.Count : (ctx.NeedFood ? Mathf.CeilToInt(pool.Count * 0.6f) : Mathf.CeilToInt(pool.Count * 0.3f));

        for (int i = 0; i < wanted && pool.Count > 0; i++)
        {
            var worker = pool[0];
            pool.RemoveAt(0);

            // Best forage target on this planet
            int target = FindBestForageTarget(worker, ctx);
            Assign(worker, UnitRole.Gatherer, target, turn, ctx.IsFamine ? 10f : 5f);
        }
    }

    private static int FindBestForageTarget(WorkerUnit unit, AIContext ctx)
    {
        var forages = ctx.GetForageTargets(unit.planetIndex);
        if (forages == null || forages.Count == 0) return -1;

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return forages[0].TileIndex;

        int best = -1;
        float bestScore = float.MinValue;
        foreach (var f in forages)
        {
            int d = ts.GetTileDistance(unit.currentTileIndex, f.TileIndex);
            float s = f.Score - d * 1.5f;
            if (s > bestScore) { bestScore = s; best = f.TileIndex; }
        }
        return best;
    }

    // ──── Hunter-gatherers ────

    private void AssignHunterGatherers(List<WorkerUnit> pool, Civilization civ, AIContext ctx, int turn)
    {
        if (pool.Count == 0) return;

        int wanted = !ctx.HasCities ? pool.Count : (ctx.IsFamine ? pool.Count : Mathf.CeilToInt(pool.Count * 0.8f));
        wanted = Mathf.Clamp(wanted, 1, pool.Count);

        for (int i = 0; i < wanted && pool.Count > 0; i++)
        {
            var worker = pool[0];
            pool.RemoveAt(0);
            int target = FindBestHunterGathererTarget(worker, ctx);
            Assign(worker, UnitRole.HunterGatherer, target, turn, !ctx.HasCities ? 9f : 7f);
        }
    }

    private static int FindBestHunterGathererTarget(WorkerUnit unit, AIContext ctx)
    {
        int forageTarget = FindBestForageTarget(unit, ctx);
        if (forageTarget >= 0) return forageTarget;

        var hotspots = ctx.GetResourceHotspots(unit.planetIndex);
        if (hotspots != null && hotspots.Count > 0)
        {
            var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
            int best = -1;
            float bestScore = float.MinValue;
            foreach (var h in hotspots)
            {
                int dist = ts != null ? ts.GetTileDistance(unit.currentTileIndex, h.TileIndex) : 0;
                float score = h.Score - dist;
                if (score > bestScore) { bestScore = score; best = h.TileIndex; }
            }
            if (best >= 0) return best;
        }

        return FindFrontierTarget(unit, ctx);
    }

    // ──── Builders ────

    private void AssignBuilders(List<WorkerUnit> pool, Civilization civ, AIContext ctx, int turn)
    {
        if (pool.Count == 0) return;

        // Check if shelter building is urgent
        bool shelterUrgent = false;
        if (ClimateManager.Instance != null)
        {
            foreach (var kv in ctx.DangerMaps)
            {
                int turnsToWinter = ClimateManager.Instance.GetTurnsUntilWinter(kv.Key);
                if (turnsToWinter <= ClimateManager.Instance.turnsPerSeason + 2) { shelterUrgent = true; break; }
            }
        }

        int wanted = shelterUrgent ? Mathf.CeilToInt(pool.Count * 0.5f) : Mathf.CeilToInt(pool.Count * 0.3f);

        for (int i = 0; i < wanted && pool.Count > 0; i++)
        {
            var worker = pool[0];
            pool.RemoveAt(0);
            Assign(worker, UnitRole.Builder, worker.currentTileIndex, turn, shelterUrgent ? 7f : 4f);
        }
    }

    // ──── Settlers ────

    private void AssignSettlers(List<WorkerUnit> pool, Civilization civ, EmpireIntent intent, AIContext ctx, int turn)
    {
        if (pool.Count == 0 || !civ.CanFoundMoreCities()) return;
        if (intent.ExpansionTargets.Count == 0) return;

        // Find workers capable of founding cities
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            if (intent.ExpansionTargets.Count == 0) break;
            var w = pool[i];
            if (w.data == null || !w.data.canFoundCity) continue;

            // Assign to best expansion target
            var target = intent.ExpansionTargets[0];
            pool.RemoveAt(i);
            Assign(w, UnitRole.Settler, target.TileIndex, turn, target.Score * 0.5f);
            intent.ExpansionTargets.RemoveAt(0); // claimed
        }
    }

    // ════════════════════════════════════════════════════════
    //  Assignment helper
    // ════════════════════════════════════════════════════════

    private void Assign(BaseUnit unit, UnitRole role, int targetTile, int turn, float priority, int targetCivId = -1)
    {
        int id = unit.GetRuntimeId();
        assignments[id] = new UnitAssignment
        {
            UnitInstanceId = id,
            Role = role,
            TargetTile = targetTile,
            TargetCivId = targetCivId,
            AssignedTurn = turn,
            Priority = priority
        };
    }

    // ════════════════════════════════════════════════════════
    //  Score bonus: translate assignment into command score modifiers
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Apply role-based score bonuses to a list of commands for a single unit.
    /// Called by TacticalEvaluator after generating and base-scoring all commands.
    /// Bonuses steer units toward their assigned role without overriding safety (retreat).
    /// </summary>
    public static void ApplyRoleBonuses(List<AICommand> commands, BaseUnit unit, UnitAssignment assignment, AIContext ctx)
    {
        if (assignment == null || commands == null || commands.Count == 0) return;

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;

        foreach (var cmd in commands)
        {
            float bonus = 0f;

            switch (assignment.Role)
            {
                case UnitRole.Attacker:
                    if (cmd is AIAttackCommand atk)
                    {
                        bonus += 5f;
                        // Extra bonus if attacking the assigned civ
                        if (assignment.TargetCivId >= 0 && atk.target != null)
                        {
                            var targetOwner = GetUnitOwner(atk.target);
                            if (targetOwner != null && targetOwner.GetRuntimeId() == assignment.TargetCivId)
                                bonus += 5f;
                        }
                    }
                    else if (cmd is AIApproachCommand app && assignment.TargetTile >= 0 && ts != null)
                    {
                        int distAfter = ts.GetTileDistance(app.approachTileIndex, assignment.TargetTile);
                        int distBefore = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                        if (distAfter < distBefore) bonus += 6f;
                    }
                    else if (cmd is AIMoveCommand mv && assignment.TargetTile >= 0 && ts != null)
                    {
                        int distAfter = ts.GetTileDistance(mv.targetTileIndex, assignment.TargetTile);
                        int distBefore = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                        if (distAfter < distBefore) bonus += 4f;
                    }
                    break;

                case UnitRole.Defender:
                    if (cmd is AIFortifyCommand && assignment.TargetTile >= 0 && ts != null)
                    {
                        int dist = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                        if (dist <= 2) bonus += 8f;
                        else bonus += 3f;
                    }
                    else if (cmd is AIAttackCommand)
                    {
                        // Defenders still attack enemies near their post
                        if (assignment.TargetTile >= 0 && ts != null)
                        {
                            int dist = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                            if (dist <= 3) bonus += 4f;
                        }
                    }
                    else if (cmd is AIMoveCommand mvd && assignment.TargetTile >= 0 && ts != null)
                    {
                        int distAfter = ts.GetTileDistance(mvd.targetTileIndex, assignment.TargetTile);
                        int distBefore = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                        if (distAfter < distBefore) bonus += 5f;
                    }
                    break;

                case UnitRole.Scout:
                    if (cmd is AIExploreCommand) bonus += 8f;
                    else if (cmd is AIMoveCommand mvs && assignment.TargetTile >= 0 && ts != null)
                    {
                        int distAfter = ts.GetTileDistance(mvs.targetTileIndex, assignment.TargetTile);
                        int distBefore = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                        if (distAfter < distBefore) bonus += 5f;
                    }
                    break;

                case UnitRole.Gatherer:
                    if (cmd is AIForageCommand) bonus += 8f;
                    else if (cmd is AIAttackCommand atkG)
                    {
                        // Hunting animals is gathering
                        if (atkG.target is CombatUnit cu && cu.data != null && cu.data.unitType == CombatCategory.Animal)
                            bonus += 6f;
                    }
                    else if (cmd is AIMoveCommand mvg && assignment.TargetTile >= 0 && ts != null)
                    {
                        int distAfter = ts.GetTileDistance(mvg.targetTileIndex, assignment.TargetTile);
                        int distBefore = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                        if (distAfter < distBefore) bonus += 4f;
                    }
                    break;

                case UnitRole.HunterGatherer:
                    if (cmd is AIForageCommand) bonus += 10f;
                    else if (cmd is AIAttackCommand atkHg)
                    {
                        if (atkHg.target is CombatUnit cuHg && cuHg.data != null && cuHg.data.unitType == CombatCategory.Animal)
                            bonus += 8f;
                    }
                    else if (cmd is AIMoveCommand mvHg && assignment.TargetTile >= 0 && ts != null)
                    {
                        int distAfter = ts.GetTileDistance(mvHg.targetTileIndex, assignment.TargetTile);
                        int distBefore = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                        if (distAfter < distBefore) bonus += 6f;
                    }
                    else if (cmd is AIExploreCommand) bonus += 2f; // fallback only when survival targets are absent
                    break;

                case UnitRole.Builder:
                    if (cmd is AIBuildImprovementCommand) bonus += 8f;
                    break;

                case UnitRole.Settler:
                    if (cmd is AISettleCityCommand) bonus += 12f;
                    else if (cmd is AIMoveCommand mvSet && assignment.TargetTile >= 0 && ts != null)
                    {
                        int distAfter = ts.GetTileDistance(mvSet.targetTileIndex, assignment.TargetTile);
                        int distBefore = ts.GetTileDistance(unit.currentTileIndex, assignment.TargetTile);
                        if (distAfter < distBefore) bonus += 8f;
                    }
                    break;
            }

            cmd.score += bonus;
        }
    }

    /// <summary>
    /// Apply empire-wide intent bonuses (subtle global nudges from the strategic layer).
    /// Called after role bonuses.
    /// </summary>
    public static void ApplyIntentBonuses(List<AICommand> commands, EmpireIntent intent)
    {
        if (intent == null || commands == null) return;
        foreach (var cmd in commands)
        {
            if (cmd is AIAttackCommand || cmd is AIApproachCommand)
                cmd.score += intent.AttackBonus;
            else if (cmd is AIExploreCommand)
                cmd.score += intent.ExploreBonus;
            else if (cmd is AIForageCommand)
                cmd.score += intent.ForageBonus;
            else if (cmd is AIBuildImprovementCommand)
                cmd.score += intent.BuildBonus;
            else if (cmd is AISettleCityCommand)
                cmd.score += intent.SettleBonus;
            else if (cmd is AIFortifyCommand)
                cmd.score += intent.DefendBonus;
        }
    }

    // ════════════════════════════════════════════════════════
    //  Utility
    // ════════════════════════════════════════════════════════

    private static Civilization GetUnitOwner(BaseUnit unit)
    {
        if (unit is CombatUnit cu) return cu.owner;
        if (unit is WorkerUnit wu) return wu.owner;
        return null;
    }

    public void Clear() => assignments.Clear();
}
