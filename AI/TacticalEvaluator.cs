using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Decides the best action for a single unit by generating all legal commands,
/// scoring them with AIScorer, and returning the highest-scoring option.
/// </summary>
public static class TacticalEvaluator
{
    private const int MAX_APPROACH_SEARCH = 8;  // max hex distance for approach targets
    private const int MAX_FORAGE_SEARCH   = 5;  // BFS range for nearby forageable tiles
    private const float RETREAT_HP_THRESHOLD = 0.35f;

    /// <summary>
    /// Main entry: returns the best command for this unit, or null if nothing useful to do.
    /// </summary>
    public static AICommand DecideBestAction(BaseUnit unit, Civilization civ, DangerMap dangerMap, ArmyGroupManager groups)
    {
        var candidates = GenerateAllCommands(unit, civ, dangerMap, groups);
        if (candidates.Count == 0) return null;
        return candidates.OrderByDescending(c => c.score).First();
    }

    /// <summary>
    /// Generates every legal command for a unit and pre-scores them.
    /// </summary>
    public static List<AICommand> GenerateAllCommands(BaseUnit unit, Civilization civ, DangerMap dangerMap, ArmyGroupManager groups)
    {
        var commands = new List<AICommand>(16);
        if (unit == null || unit.isStored) return commands;
        int pIndex = unit.planetIndex;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts == null) return commands;

        bool needFood = civ.cities == null || civ.cities.Count == 0 || civ.food < 20;
        float hpRatio = (float)unit.currentHealth / Mathf.Max(1, unit.MaxHealth);

        // ───── Retreat: wounded units near danger should flee ─────
        if (hpRatio < RETREAT_HP_THRESHOLD && dangerMap.IsDangerous(unit.currentTileIndex, unit.BaseAttack * 0.5f))
        {
            var retreatCmd = GenerateRetreat(unit, ts, dangerMap);
            if (retreatCmd != null) commands.Add(retreatCmd);
        }

        // ───── CombatUnit-specific ─────
        if (unit is CombatUnit cu && !cu.hasActedThisTurn)
        {
            // Direct attacks in range
            GenerateAttacks(cu, civ, dangerMap, commands, needFood);

            // Approach targets out of range
            GenerateApproaches(cu, civ, dangerMap, ts, commands, needFood);
        }

        // ───── WorkerUnit-specific ─────
        if (unit is WorkerUnit wu)
        {
            // Forage on current tile
            GenerateForage(wu, civ, dangerMap, commands);

            // Worker attack (hunting animals for food)
            if (needFood)
                GenerateWorkerHunting(wu, civ, dangerMap, commands);

            // Settle city (on current tile or scout for a good site)
            if (wu.CanFoundCityOnCurrentTile())
            {
                var sc = new AISettleCityCommand { unit = wu, planetIndex = pIndex };
                sc.score = AIScorer.ScoreSettleCity(wu, wu.currentTileIndex, dangerMap);
                commands.Add(sc);
            }
            else if (wu.data != null && wu.data.canFoundCity && civ.CanFoundMoreCities())
            {
                GenerateMoveTowardCitySite(wu, civ, dangerMap, ts, commands);
            }

            // Build improvement (shelter, farms, etc.)
            GenerateBuildCommands(wu, civ, dangerMap, commands);

            // Move toward forage (with resource prioritization)
            GenerateMoveTowardForage(wu, civ, dangerMap, ts, commands);

            // Move toward high-value resource tiles
            GenerateMoveTowardResource(wu, civ, dangerMap, ts, commands);

            // Move toward animals to hunt
            if (needFood)
                GenerateMoveTowardAnimal(wu, dangerMap, ts, commands);
        }

        // ───── Exploration: move toward fog-of-war frontier ─────
        GenerateExploration(unit, civ, dangerMap, ts, commands);

        // ───── Fortify (always an option, lowest-priority fallback) ─────
        var fortify = new AIFortifyCommand { unit = unit, planetIndex = pIndex };
        fortify.score = AIScorer.ScoreFortify(unit, dangerMap);
        commands.Add(fortify);

        // ───── Group coordination: if part of an army group, boost approach toward group target ─────
        if (groups != null && unit is CombatUnit groupCu)
        {
            var group = groups.GetGroupForUnit(groupCu);
            if (group != null && group.TargetTile >= 0)
            {
                foreach (var cmd in commands)
                {
                    if (cmd is AIMoveCommand mv)
                    {
                        int distToTarget = ts.GetTileDistance(mv.targetTileIndex, group.TargetTile);
                        if (distToTarget < ts.GetTileDistance(unit.currentTileIndex, group.TargetTile))
                            mv.score += 5f; // bonus for moving toward group target
                    }
                }
            }
        }

        return commands;
    }

    // ─────────────────────── Attack generation ───────────────────────

    private static void GenerateAttacks(CombatUnit cu, Civilization civ, DangerMap dangerMap, List<AICommand> commands, bool needFood)
    {
        // Enemies
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs != null)
        {
            foreach (var otherCiv in allCivs)
            {
                if (otherCiv == civ) continue;
                if (otherCiv.combatUnits != null)
                {
                    foreach (var enemy in otherCiv.combatUnits)
                    {
                        if (enemy == null || !cu.CanAttack(enemy)) continue;
                        var cmd = new AIAttackCommand { unit = cu, target = enemy, planetIndex = cu.planetIndex };
                        cmd.score = AIScorer.ScoreAttack(cu, enemy, dangerMap);
                        commands.Add(cmd);
                    }
                }
                if (otherCiv.workerUnits != null)
                {
                    foreach (var ew in otherCiv.workerUnits)
                    {
                        if (ew == null || !cu.CanAttack(ew)) continue;
                        var cmd = new AIAttackCommand { unit = cu, target = ew, planetIndex = cu.planetIndex };
                        cmd.score = AIScorer.ScoreAttack(cu, ew, dangerMap);
                        commands.Add(cmd);
                    }
                }
            }
        }

        // Animals (for food when needed)
        if (needFood && AnimalManager.Instance != null)
        {
            foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
            {
                if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
                if (animal.planetIndex != cu.planetIndex) continue;
                if (cu.CanAttack(animal))
                {
                    var cmd = new AIAttackCommand { unit = cu, target = animal, planetIndex = cu.planetIndex };
                    cmd.score = AIScorer.ScoreAttack(cu, animal, dangerMap);
                    commands.Add(cmd);
                }
            }
        }
    }

    // ─────────────────────── Approach generation ───────────────────────

    private static void GenerateApproaches(CombatUnit cu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, bool needFood)
    {
        BaseUnit bestTarget = null;
        int bestDist = int.MaxValue;

        // Nearest enemy
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs != null)
        {
            foreach (var otherCiv in allCivs)
            {
                if (otherCiv == civ || otherCiv.combatUnits == null) continue;
                foreach (var enemy in otherCiv.combatUnits)
                {
                    if (enemy == null || enemy.planetIndex != cu.planetIndex || enemy.currentTileIndex < 0) continue;
                    int d = ts.GetTileDistance(cu.currentTileIndex, enemy.currentTileIndex);
                    if (d > 1 && d < bestDist && d <= MAX_APPROACH_SEARCH) { bestDist = d; bestTarget = enemy; }
                }
            }
        }

        // Nearest animal when food is needed
        if (needFood && AnimalManager.Instance != null)
        {
            foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
            {
                if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
                if (animal.planetIndex != cu.planetIndex || animal.currentTileIndex < 0) continue;
                int d = ts.GetTileDistance(cu.currentTileIndex, animal.currentTileIndex);
                if (d > 1 && d < bestDist && d <= MAX_APPROACH_SEARCH) { bestDist = d; bestTarget = animal; }
            }
        }

        if (bestTarget == null) return;

        // Find best adjacent tile to target to move toward
        int[] targetNeighbors = ts.GetNeighbors(bestTarget.currentTileIndex);
        if (targetNeighbors == null) return;
        int bestApproach = -1;
        float bestApproachScore = float.MinValue;
        foreach (int n in targetNeighbors)
        {
            if (!cu.CanMoveTo(n)) continue;
            float s = AIScorer.ScoreTileForMovement(cu, n, bestTarget.currentTileIndex, dangerMap);
            if (s > bestApproachScore) { bestApproachScore = s; bestApproach = n; }
        }
        if (bestApproach >= 0)
        {
            var cmd = new AIApproachCommand { unit = cu, target = bestTarget, approachTileIndex = bestApproach, planetIndex = cu.planetIndex };
            cmd.score = bestApproachScore + 3f;
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Retreat generation ───────────────────────

    private static AIRetreatCommand GenerateRetreat(BaseUnit unit, TileSystem ts, DangerMap dangerMap)
    {
        int[] neighbors = ts.GetNeighbors(unit.currentTileIndex);
        if (neighbors == null) return null;
        int bestTile = -1;
        float bestScore = float.MinValue;
        foreach (int n in neighbors)
        {
            if (!unit.CanMoveTo(n)) continue;
            float s = AIScorer.ScoreRetreat(unit, n, dangerMap);
            if (s > bestScore) { bestScore = s; bestTile = n; }
        }
        if (bestTile < 0) return null;
        var cmd = new AIRetreatCommand { unit = unit, retreatTileIndex = bestTile, planetIndex = unit.planetIndex };
        cmd.score = bestScore;
        return cmd;
    }

    // ─────────────────────── Worker: forage ───────────────────────

    private static void GenerateForage(WorkerUnit wu, Civilization civ, DangerMap dangerMap, List<AICommand> commands)
    {
        if (wu.currentWorkPoints <= 0) return;
        var rm = ResourceManager.Instance;
        if (rm == null) return;
        var inst = rm.GetResourceInstanceAtTile(wu.currentTileIndex, wu.planetIndex);
        if (inst == null || inst.data == null || !inst.data.canBeForaged) return;
        if (!wu.CanForage(inst.data, wu.currentTileIndex)) return;
        var cmd = new AIForageCommand { unit = wu, resource = inst.data, planetIndex = wu.planetIndex };
        cmd.score = AIScorer.ScoreForage(wu, inst, dangerMap);
        commands.Add(cmd);
    }

    // ─────────────────────── Worker: hunting ───────────────────────

    private static void GenerateWorkerHunting(WorkerUnit wu, Civilization civ, DangerMap dangerMap, List<AICommand> commands)
    {
        if (AnimalManager.Instance == null) return;
        foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
        {
            if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
            if (animal.planetIndex != wu.planetIndex) continue;
            if (!wu.CanAttack(animal)) continue;
            var cmd = new AIAttackCommand { unit = wu, target = animal, planetIndex = wu.planetIndex };
            cmd.score = AIScorer.ScoreAttack(wu, animal, dangerMap);
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Worker: build improvement ───────────────────────

    private static void GenerateBuildCommands(WorkerUnit wu, Civilization civ, DangerMap dangerMap, List<AICommand> commands)
    {
        if (wu.currentWorkPoints <= 0 || civ == null) return;
        var available = civ.GetAvailableImprovementsForWorker(wu.data, wu.currentTileIndex, wu.planetIndex);
        if (available == null) return;
        // Skip if tile already has a build job
        if (ImprovementManager.Instance != null && ImprovementManager.Instance.HasBuildJobAtTile(wu.currentTileIndex, wu.planetIndex)) return;

        foreach (var imp in available)
        {
            if (imp == null) continue;
            var cmd = new AIBuildImprovementCommand { unit = wu, improvement = imp, planetIndex = wu.planetIndex };
            cmd.score = AIScorer.ScoreBuildImprovement(wu, imp, wu.currentTileIndex, dangerMap);
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Worker: move toward forage ───────────────────────

    private static void GenerateMoveTowardForage(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;

        // BFS from worker tile
        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((wu.currentTileIndex, 0));
        visited.Add(wu.currentTileIndex);
        int bestTile = -1;
        float bestScore = float.MinValue;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist > MAX_FORAGE_SEARCH) continue;
            var inst = rm.GetResourceInstanceAtTile(tile, wu.planetIndex);
            if (inst != null && inst.data != null && inst.data.canBeForaged && tile != wu.currentTileIndex)
            {
                float s = inst.data.forageFood * AIScorer.W_FORAGE_FOOD - dist * 1.5f;
                s += dangerMap.GetDanger(tile) * AIScorer.W_DANGER_PENALTY;
                if (s > bestScore) { bestScore = s; bestTile = tile; }
            }
            if (dist >= MAX_FORAGE_SEARCH) continue;
            foreach (int n in ts.GetNeighbors(tile))
            {
                if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, dist + 1)); }
            }
        }

        if (bestTile >= 0 && wu.CanMoveTo(bestTile))
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Worker: move toward animal ───────────────────────

    private static void GenerateMoveTowardAnimal(WorkerUnit wu, DangerMap dangerMap, TileSystem ts, List<AICommand> commands)
    {
        if (AnimalManager.Instance == null) return;
        CombatUnit nearest = null;
        int nearestDist = int.MaxValue;
        foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
        {
            if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
            if (animal.planetIndex != wu.planetIndex || animal.currentTileIndex < 0) continue;
            int d = ts.GetTileDistance(wu.currentTileIndex, animal.currentTileIndex);
            if (d > 1 && d < nearestDist && d <= MAX_APPROACH_SEARCH) { nearestDist = d; nearest = animal; }
        }
        if (nearest == null) return;

        int[] targetNeighbors = ts.GetNeighbors(nearest.currentTileIndex);
        if (targetNeighbors == null) return;
        int bestApproach = -1;
        float bestScore = float.MinValue;
        foreach (int n in targetNeighbors)
        {
            if (!wu.CanMoveTo(n)) continue;
            float s = AIScorer.ScoreTileForMovement(wu, n, nearest.currentTileIndex, dangerMap);
            if (s > bestScore) { bestScore = s; bestApproach = n; }
        }
        if (bestApproach >= 0)
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestApproach, planetIndex = wu.planetIndex };
            cmd.score = bestScore + 1f;
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Worker: move toward good city site ───────────────────────

    private const int MAX_CITY_SITE_SEARCH = 8;

    /// <summary>
    /// For settlers (canFoundCity workers): BFS for the best nearby city site and move toward it.
    /// </summary>
    private static void GenerateMoveTowardCitySite(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands)
    {
        var visited = new System.Collections.Generic.HashSet<int>();
        var queue = new System.Collections.Generic.Queue<(int tile, int dist)>();
        queue.Enqueue((wu.currentTileIndex, 0));
        visited.Add(wu.currentTileIndex);

        int bestTile = -1;
        float bestScore = 8f; // minimum threshold — only move if the site is good enough

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist > MAX_CITY_SITE_SEARCH) continue;

            if (dist > 0)
            {
                var td = ts.GetTileData(tile);
                if (td != null && td.isLand)
                {
                    // Check minimum city distance (same check as CanFoundCityOnCurrentTile)
                    bool tooClose = false;
                    var allCivs = CivilizationManager.Instance?.GetAllCivs();
                    if (allCivs != null)
                    {
                        foreach (var c in allCivs)
                        {
                            if (c.cities == null) continue;
                            foreach (var city in c.cities)
                            {
                                if (city == null) continue;
                                int d = ts.GetWrappedHexDistance(tile, city.centerTileIndex);
                                if (d >= 0 && d < 4) { tooClose = true; break; }
                            }
                            if (tooClose) break;
                        }
                    }
                    if (!tooClose)
                    {
                        float s = AIScorer.ScoreSettleCity(wu, tile, dangerMap) - dist * 0.5f;
                        if (s > bestScore) { bestScore = s; bestTile = tile; }
                    }
                }
            }

            if (dist >= MAX_CITY_SITE_SEARCH) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, dist + 1)); }
            }
        }

        if (bestTile >= 0 && wu.CanMoveTo(bestTile))
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Worker: move toward high-value resource ───────────────────────

    /// <summary>
    /// Uses resource prioritization scoring to route workers toward the most valuable
    /// unimproved resource within range (prefer tiles the civ already owns or neutral).
    /// </summary>
    private static void GenerateMoveTowardResource(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands)
    {
        var visited = new System.Collections.Generic.HashSet<int>();
        var queue = new System.Collections.Generic.Queue<(int tile, int dist)>();
        queue.Enqueue((wu.currentTileIndex, 0));
        visited.Add(wu.currentTileIndex);

        int bestTile = -1;
        float bestScore = 3f; // threshold

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist > MAX_FORAGE_SEARCH) continue;

            if (dist > 0)
            {
                var td = ts.GetTileData(tile);
                if (td != null && td.resource != null && td.isLand)
                {
                    // Prefer tiles owned by us or neutral (not enemy)
                    bool accessible = td.owner == null || td.owner == civ;
                    if (accessible)
                    {
                        float s = AIScorer.ScoreResourceTile(civ, td, tile, dangerMap) - dist * 1f;
                        if (s > bestScore) { bestScore = s; bestTile = tile; }
                    }
                }
            }

            if (dist >= MAX_FORAGE_SEARCH) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, dist + 1)); }
            }
        }

        if (bestTile >= 0 && bestTile != wu.currentTileIndex && wu.CanMoveTo(bestTile))
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Exploration: fog frontier ───────────────────────

    private const int MAX_EXPLORE_BFS = 6;

    /// <summary>
    /// Generates exploration move commands toward tiles on the fog-of-war frontier.
    /// Prefers moveable tiles that border the most unexplored (fog=0) neighbors,
    /// so the unit maximizes map reveal per move.
    /// </summary>
    private static void GenerateExploration(BaseUnit unit, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands)
    {
        if (UnitVisionManager.Instance == null) return;
        int civId = UnitVisionManager.GetCivIndex(civ);
        if (civId < 0) return;
        byte[] fog = ts.GetFogForCiv(civId);
        if (fog == null) return;

        // BFS from the unit to find the best frontier tile within range
        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((unit.currentTileIndex, 0));
        visited.Add(unit.currentTileIndex);

        int bestTile = -1;
        float bestScore = float.MinValue;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();

            // Only consider tiles we can actually move to (skip start tile)
            if (dist > 0 && unit.CanMoveTo(tile))
            {
                // Count how many neighbors of this tile are unexplored (fog == 0)
                int unexploredCount = 0;
                int[] tileNeighbors = ts.GetNeighbors(tile);
                if (tileNeighbors != null)
                {
                    foreach (int nn in tileNeighbors)
                    {
                        if (nn >= 0 && nn < fog.Length && fog[nn] == 0) unexploredCount++;
                    }
                }

                if (unexploredCount > 0)
                {
                    float s = AIScorer.ScoreExplore(unit, tile, unexploredCount, dangerMap);
                    if (s > bestScore) { bestScore = s; bestTile = tile; }
                }
            }

            if (dist >= MAX_EXPLORE_BFS) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (n >= 0 && !visited.Contains(n))
                {
                    // Only BFS through explored tiles (fog > 0) — avoid pathing through fog
                    if (n < fog.Length && fog[n] > 0)
                    {
                        visited.Add(n);
                        queue.Enqueue((n, dist + 1));
                    }
                    else
                    {
                        visited.Add(n); // mark as visited so we don't revisit
                    }
                }
            }
        }

        if (bestTile >= 0)
        {
            var cmd = new AIExploreCommand { unit = unit, targetTileIndex = bestTile, planetIndex = unit.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }
}
