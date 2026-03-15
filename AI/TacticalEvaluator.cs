using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Decides the best action for a single unit by generating all legal commands,
/// scoring them with AIScorer, applying operational role bonuses and empire intent bonuses,
/// and returning the highest-scoring option.
///
/// When an AIContext is provided, cached data (frontiers, resource hotspots, forage targets)
/// is used instead of per-unit BFS scans — significantly reducing repeated work.
/// </summary>
public static class TacticalEvaluator
{
    // Default search depths (used when no budget is provided)
    private const int DEFAULT_APPROACH_SEARCH = 8;
    private const int DEFAULT_FORAGE_SEARCH   = 5;
    private const int DEFAULT_EXPLORE_BFS     = 6;
    private const int DEFAULT_CITY_SITE_SEARCH = 8;
    private const float RETREAT_HP_THRESHOLD = 0.35f;

    /// <summary>
    /// Main entry: returns the best command for this unit, or null if nothing useful to do.
    /// AiBudget controls search depth and candidate limits per difficulty.
    /// </summary>
    public static AICommand DecideBestAction(
        BaseUnit unit, Civilization civ, DangerMap dangerMap, ArmyGroupManager groups,
        AIContext context = null, UnitAssignment assignment = null, EmpireIntent intent = null,
        AiBudget budget = null)
    {
        var candidates = GenerateAllCommands(unit, civ, dangerMap, groups, context, budget);
        if (candidates.Count == 0) return null;

        // Budget: cap candidates scored (Easy AI considers fewer options)
        int maxCandidates = budget != null && budget.MaxCandidatesPerUnit > 0
            ? budget.MaxCandidatesPerUnit : candidates.Count;
        if (candidates.Count > maxCandidates)
        {
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            candidates.RemoveRange(maxCandidates, candidates.Count - maxCandidates);
        }

        // Role gating: filter commands that conflict with the unit's assigned role.
        // Escort units shouldn't chase wildlife; frontline units shouldn't explore.
        if (assignment != null)
            ApplyRoleGating(candidates, unit, assignment);

        // Apply operational role bonuses (steers toward assigned role)
        if (assignment != null && context != null)
            OperationalPlanner.ApplyRoleBonuses(candidates, unit, assignment, context);

        // Apply empire-wide intent bonuses (subtle global nudge)
        if (intent != null)
            OperationalPlanner.ApplyIntentBonuses(candidates, intent);

        // Top-K stochastic choice: when top actions have similar scores,
        // pick randomly among them weighted by score — reduces predictability
        // without making the AI appear irrational.
        return SelectTopK(candidates, budget);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Top-K stochastic selection ("bounded randomness")
    // ═══════════════════════════════════════════════════════════════════

    private const float TOP_K_THRESHOLD = 0.85f; // actions within 85% of best score are candidates
    private const int   TOP_K_MAX       = 4;      // max pool size

    /// <summary>
    /// If the top N actions have scores within 15% of the best, randomly pick among them
    /// weighted by score. Otherwise pick the clear winner deterministically.
    /// This prevents exploitable predictability while keeping decisions rational.
    /// </summary>
    private static AICommand SelectTopK(List<AICommand> candidates, AiBudget budget)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        var best = candidates[0];

        // If budget noise is zero (Expert), always pick deterministically
        if (budget != null && budget.ScoreNoise <= 0f) return best;

        float threshold = best.score * TOP_K_THRESHOLD;
        if (threshold < 0f) threshold = best.score + Mathf.Abs(best.score) * (1f - TOP_K_THRESHOLD);

        // Collect near-best candidates
        int poolSize = 1;
        for (int i = 1; i < candidates.Count && i < TOP_K_MAX; i++)
        {
            if (candidates[i].score >= threshold) poolSize++;
            else break;
        }

        if (poolSize <= 1) return best;

        // Weighted random selection among top K
        float totalWeight = 0f;
        for (int i = 0; i < poolSize; i++)
            totalWeight += Mathf.Max(0.1f, candidates[i].score);
        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        for (int i = 0; i < poolSize; i++)
        {
            cumulative += Mathf.Max(0.1f, candidates[i].score);
            if (roll <= cumulative) return candidates[i];
        }
        return best;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Role gating (operational-tactical seam)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Removes commands that conflict with the unit's assigned role.
    /// This prevents "escort units chasing wildlife" and "frontline units wandering off."
    /// Self-defense (retreat) is never gated — survival always allowed.
    /// </summary>
    private static void ApplyRoleGating(List<AICommand> candidates, BaseUnit unit, UnitAssignment assignment)
    {
        if (assignment.Role == UnitRole.Unassigned) return;

        candidates.RemoveAll(cmd =>
        {
            // Retreat and fortify are always allowed (survival)
            if (cmd is AIRetreatCommand || cmd is AIFortifyCommand || cmd is AIUnstoreCommand)
                return false;

            switch (assignment.Role)
            {
                case UnitRole.Scout:
                    // Scouts shouldn't attack (except self-defense handled by retreat above)
                    // or build. They explore and move.
                    if (cmd is AIAttackCommand || cmd is AIBuildImprovementCommand ||
                        cmd is AISettleCityCommand || cmd is AIForageCommand)
                        return true;
                    break;

                case UnitRole.Defender:
                    // Defenders shouldn't explore or settle — they hold position.
                    if (cmd is AIExploreCommand || cmd is AISettleCityCommand)
                        return true;
                    break;

                case UnitRole.Attacker:
                    // Attackers shouldn't explore, forage, build, or settle.
                    if (cmd is AIExploreCommand || cmd is AIForageCommand ||
                        cmd is AIBuildImprovementCommand || cmd is AISettleCityCommand)
                        return true;
                    break;

                case UnitRole.Gatherer:
                    // Gatherers shouldn't explore far or settle.
                    if (cmd is AIExploreCommand || cmd is AISettleCityCommand)
                        return true;
                    // Can attack animals (hunting) but not other civs
                    if (cmd is AIAttackCommand atk && atk.target != null)
                    {
                        if (atk.target is CombatUnit cu && cu.data != null && cu.data.unitType != CombatCategory.Animal)
                            return true;
                    }
                    break;

                case UnitRole.Builder:
                    // Builders shouldn't explore or attack (except wildlife self-defense)
                    if (cmd is AIExploreCommand || cmd is AISettleCityCommand)
                        return true;
                    if (cmd is AIAttackCommand) return true;
                    break;

                case UnitRole.Settler:
                    // Settlers should only move toward target and settle. No combat, no exploring.
                    if (cmd is AIAttackCommand || cmd is AIExploreCommand ||
                        cmd is AIBuildImprovementCommand || cmd is AIForageCommand)
                        return true;
                    break;
            }
            return false;
        });
    }

    /// <summary>
    /// Generates every legal command for a unit and pre-scores them.
    /// Uses AIContext caches when available to avoid repeated BFS scans.
    /// AiBudget controls search depth per difficulty.
    /// </summary>
    public static List<AICommand> GenerateAllCommands(
        BaseUnit unit, Civilization civ, DangerMap dangerMap, ArmyGroupManager groups,
        AIContext context = null, AiBudget budget = null)
    {
        var commands = new List<AICommand>(16);
        if (unit == null || unit.isStored) return commands;
        int pIndex = unit.planetIndex;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts == null) return commands;

        // Budget-controlled search depths
        int approachRange   = budget?.ApproachSearchRange  ?? DEFAULT_APPROACH_SEARCH;
        int forageRange     = budget?.ForageSearchRange    ?? DEFAULT_FORAGE_SEARCH;
        int exploreRange    = budget?.ExploreSearchRange   ?? DEFAULT_EXPLORE_BFS;
        int citySiteRange   = budget?.CitySiteSearchRange  ?? DEFAULT_CITY_SITE_SEARCH;

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
            GenerateAttacks(cu, civ, dangerMap, commands, needFood);
            GenerateApproaches(cu, civ, dangerMap, ts, commands, needFood, approachRange);
        }

        // ───── WorkerUnit-specific ─────
        if (unit is WorkerUnit wu)
        {
            GenerateForage(wu, civ, dangerMap, commands);

            if (needFood)
                GenerateWorkerHunting(wu, civ, dangerMap, commands);

            if (wu.CanFoundCityOnCurrentTile())
            {
                var sc = new AISettleCityCommand { unit = wu, planetIndex = pIndex };
                sc.score = AIScorer.ScoreSettleCity(wu, wu.currentTileIndex, dangerMap);
                commands.Add(sc);
            }
            else if (wu.data != null && wu.data.canFoundCity && civ.CanFoundMoreCities())
            {
                GenerateMoveTowardCitySite(wu, civ, dangerMap, ts, commands, context);
            }

            GenerateBuildCommands(wu, civ, dangerMap, commands);

            // Use cached forage targets when available, fall back to BFS
            if (context != null)
                GenerateMoveTowardForageCached(wu, civ, dangerMap, ts, commands, context);
            else
                GenerateMoveTowardForage(wu, civ, dangerMap, ts, commands, forageRange);

            // Use cached resource hotspots when available
            if (context != null)
                GenerateMoveTowardResourceCached(wu, civ, dangerMap, ts, commands, context);
            else
                GenerateMoveTowardResource(wu, civ, dangerMap, ts, commands, forageRange);

            if (needFood)
                GenerateMoveTowardAnimal(wu, dangerMap, ts, commands, approachRange);
        }

        // ───── Exploration: use cached frontiers when available ─────
        if (context != null)
            GenerateExplorationCached(unit, civ, dangerMap, ts, commands, context, exploreRange);
        else
            GenerateExploration(unit, civ, dangerMap, ts, commands, exploreRange);

        // ───── Fortify (always an option, lowest-priority fallback) ─────
        var fortify = new AIFortifyCommand { unit = unit, planetIndex = pIndex };
        fortify.score = AIScorer.ScoreFortify(unit, dangerMap);
        commands.Add(fortify);

        // ───── Group coordination: boost approach toward group target ─────
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
                            mv.score += 5f;
                    }
                }
            }
        }

        return commands;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Cache-backed generators (use AIContext to skip BFS)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Uses AIContext.FrontierTiles to find the best exploration target without per-unit BFS.
    /// </summary>
    private static void GenerateExplorationCached(BaseUnit unit, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, AIContext ctx, int exploreRange = 6)
    {
        var frontier = ctx.GetFrontier(unit.planetIndex);
        if (frontier == null || frontier.Count == 0) return;

        int bestTile = -1;
        float bestScore = float.MinValue;
        int moveRange = exploreRange;

        foreach (int tile in frontier)
        {
            int dist = ts.GetTileDistance(unit.currentTileIndex, tile);
            if (dist > moveRange || dist == 0) continue;
            if (!unit.CanMoveTo(tile)) continue;

            // Count unexplored neighbors for this frontier tile
            int civId = UnitVisionManager.GetCivIndex(civ);
            byte[] fog = civId >= 0 ? ts.GetFogForCiv(civId) : null;
            int unexplored = 0;
            if (fog != null)
            {
                int[] neighbors = ts.GetNeighbors(tile);
                if (neighbors != null)
                    foreach (int n in neighbors)
                        if (n >= 0 && n < fog.Length && fog[n] == 0) unexplored++;
            }
            if (unexplored == 0) continue;

            float s = AIScorer.ScoreExplore(unit, tile, unexplored, dangerMap);
            if (s > bestScore) { bestScore = s; bestTile = tile; }
        }

        if (bestTile >= 0)
        {
            var cmd = new AIExploreCommand { unit = unit, targetTileIndex = bestTile, planetIndex = unit.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    /// <summary>
    /// Uses AIContext.ForageTargets to find the best forage destination without per-unit BFS.
    /// </summary>
    private static void GenerateMoveTowardForageCached(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, AIContext ctx)
    {
        var forages = ctx.GetForageTargets(wu.planetIndex);
        if (forages == null || forages.Count == 0) return;

        int bestTile = -1;
        float bestScore = float.MinValue;

        foreach (var f in forages)
        {
            if (f.TileIndex == wu.currentTileIndex) continue;
            int dist = ts.GetTileDistance(wu.currentTileIndex, f.TileIndex);
            if (dist > DEFAULT_FORAGE_SEARCH) continue;
            float s = f.Score - dist * 1.5f;
            if (s > bestScore) { bestScore = s; bestTile = f.TileIndex; }
        }

        if (bestTile >= 0 && wu.CanMoveTo(bestTile))
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    /// <summary>
    /// Uses AIContext.ResourceHotspots to find the best resource tile without per-unit BFS.
    /// </summary>
    private static void GenerateMoveTowardResourceCached(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, AIContext ctx)
    {
        var hotspots = ctx.GetResourceHotspots(wu.planetIndex);
        if (hotspots == null || hotspots.Count == 0) return;

        int bestTile = -1;
        float bestScore = 3f; // threshold

        foreach (var h in hotspots)
        {
            if (h.TileIndex == wu.currentTileIndex) continue;
            int dist = ts.GetTileDistance(wu.currentTileIndex, h.TileIndex);
            if (dist > DEFAULT_FORAGE_SEARCH) continue;
            float s = h.Score - dist * 1f;
            if (s > bestScore) { bestScore = s; bestTile = h.TileIndex; }
        }

        if (bestTile >= 0 && wu.CanMoveTo(bestTile))
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Original generators (BFS-based fallbacks when no AIContext)
    // ═══════════════════════════════════════════════════════════════════

    // ─────────────────────── Attack generation ───────────────────────

    private static void GenerateAttacks(CombatUnit cu, Civilization civ, DangerMap dangerMap, List<AICommand> commands, bool needFood)
    {
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs != null)
        {
            foreach (var otherCiv in allCivs)
            {
                if (otherCiv == civ) continue;
                if (otherCiv.combatUnits != null)
                    foreach (var enemy in otherCiv.combatUnits)
                    {
                        if (enemy == null || !cu.CanAttack(enemy)) continue;
                        var cmd = new AIAttackCommand { unit = cu, target = enemy, planetIndex = cu.planetIndex };
                        cmd.score = AIScorer.ScoreAttack(cu, enemy, dangerMap);
                        commands.Add(cmd);
                    }
                if (otherCiv.workerUnits != null)
                    foreach (var ew in otherCiv.workerUnits)
                    {
                        if (ew == null || !cu.CanAttack(ew)) continue;
                        var cmd = new AIAttackCommand { unit = cu, target = ew, planetIndex = cu.planetIndex };
                        cmd.score = AIScorer.ScoreAttack(cu, ew, dangerMap);
                        commands.Add(cmd);
                    }
            }
        }

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

    private static void GenerateApproaches(CombatUnit cu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, bool needFood, int approachRange = 8)
    {
        BaseUnit bestTarget = null;
        int bestDist = int.MaxValue;

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
                    if (d > 1 && d < bestDist && d <= approachRange) { bestDist = d; bestTarget = enemy; }
                }
            }
        }

        if (needFood && AnimalManager.Instance != null)
        {
            foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
            {
                if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
                if (animal.planetIndex != cu.planetIndex || animal.currentTileIndex < 0) continue;
                int d = ts.GetTileDistance(cu.currentTileIndex, animal.currentTileIndex);
                if (d > 1 && d < bestDist && d <= approachRange) { bestDist = d; bestTarget = animal; }
            }
        }

        if (bestTarget == null) return;

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
        if (ImprovementManager.Instance != null && ImprovementManager.Instance.HasBuildJobAtTile(wu.currentTileIndex, wu.planetIndex)) return;

        foreach (var imp in available)
        {
            if (imp == null) continue;
            var cmd = new AIBuildImprovementCommand { unit = wu, improvement = imp, planetIndex = wu.planetIndex };
            cmd.score = AIScorer.ScoreBuildImprovement(wu, imp, wu.currentTileIndex, dangerMap);
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Worker: move toward forage (BFS fallback) ───────────────────────

    private static void GenerateMoveTowardForage(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, int forageRange = 5)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;

        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((wu.currentTileIndex, 0));
        visited.Add(wu.currentTileIndex);
        int bestTile = -1;
        float bestScore = float.MinValue;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist > forageRange) continue;
            var inst = rm.GetResourceInstanceAtTile(tile, wu.planetIndex);
            if (inst != null && inst.data != null && inst.data.canBeForaged && tile != wu.currentTileIndex)
            {
                float s = inst.data.forageFood * AIScorer.W_FORAGE_FOOD - dist * 1.5f;
                s += dangerMap.GetDanger(tile) * AIScorer.W_DANGER_PENALTY;
                if (s > bestScore) { bestScore = s; bestTile = tile; }
            }
            if (dist >= forageRange) continue;
            foreach (int n in ts.GetNeighbors(tile))
                if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, dist + 1)); }
        }

        if (bestTile >= 0 && wu.CanMoveTo(bestTile))
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Worker: move toward animal ───────────────────────

    private static void GenerateMoveTowardAnimal(WorkerUnit wu, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, int approachRange = 8)
    {
        if (AnimalManager.Instance == null) return;
        CombatUnit nearest = null;
        int nearestDist = int.MaxValue;
        foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
        {
            if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
            if (animal.planetIndex != wu.planetIndex || animal.currentTileIndex < 0) continue;
            int d = ts.GetTileDistance(wu.currentTileIndex, animal.currentTileIndex);
            if (d > 1 && d < nearestDist && d <= approachRange) { nearestDist = d; nearest = animal; }
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

    private static void GenerateMoveTowardCitySite(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, AIContext context)
    {
        // Use cached city sites when available
        if (context != null)
        {
            var sites = context.GetCitySites(wu.planetIndex);
            if (sites != null && sites.Count > 0)
            {
                int bestTile = -1;
                float bestScore = 8f;
                foreach (var site in sites)
                {
                    int dist = ts.GetTileDistance(wu.currentTileIndex, site.TileIndex);
                    if (dist > DEFAULT_CITY_SITE_SEARCH) continue;
                    float s = site.Score - dist * 0.5f;
                    if (s > bestScore) { bestScore = s; bestTile = site.TileIndex; }
                }
                if (bestTile >= 0 && wu.CanMoveTo(bestTile))
                {
                    var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
                    cmd.score = bestScore;
                    commands.Add(cmd);
                    return;
                }
            }
        }

        // BFS fallback
        GenerateMoveTowardCitySiteBFS(wu, civ, dangerMap, ts, commands);
    }

    private static void GenerateMoveTowardCitySiteBFS(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((wu.currentTileIndex, 0));
        visited.Add(wu.currentTileIndex);

        int bestTile = -1;
        float bestScore = 8f;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist > DEFAULT_CITY_SITE_SEARCH) continue;

            if (dist > 0)
            {
                var td = ts.GetTileData(tile);
                if (td != null && td.isLand)
                {
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

            if (dist >= DEFAULT_CITY_SITE_SEARCH) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
                if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, dist + 1)); }
        }

        if (bestTile >= 0 && wu.CanMoveTo(bestTile))
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Worker: move toward high-value resource (BFS fallback) ───────────────────────

    private static void GenerateMoveTowardResource(WorkerUnit wu, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, int forageRange = 5)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((wu.currentTileIndex, 0));
        visited.Add(wu.currentTileIndex);

        int bestTile = -1;
        float bestScore = 3f;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist > forageRange) continue;

            if (dist > 0)
            {
                var td = ts.GetTileData(tile);
                if (td != null && td.resource != null && td.isLand)
                {
                    bool accessible = td.owner == null || td.owner == civ;
                    if (accessible)
                    {
                        float s = AIScorer.ScoreResourceTile(civ, td, tile, dangerMap) - dist * 1f;
                        if (s > bestScore) { bestScore = s; bestTile = tile; }
                    }
                }
            }

            if (dist >= forageRange) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
                if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, dist + 1)); }
        }

        if (bestTile >= 0 && bestTile != wu.currentTileIndex && wu.CanMoveTo(bestTile))
        {
            var cmd = new AIMoveCommand { unit = wu, targetTileIndex = bestTile, planetIndex = wu.planetIndex };
            cmd.score = bestScore;
            commands.Add(cmd);
        }
    }

    // ─────────────────────── Exploration: fog frontier (BFS fallback) ───────────────────────

    private static void GenerateExploration(BaseUnit unit, Civilization civ, DangerMap dangerMap, TileSystem ts, List<AICommand> commands, int exploreRange = 6)
    {
        if (UnitVisionManager.Instance == null) return;
        int civId = UnitVisionManager.GetCivIndex(civ);
        if (civId < 0) return;
        byte[] fog = ts.GetFogForCiv(civId);
        if (fog == null) return;

        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((unit.currentTileIndex, 0));
        visited.Add(unit.currentTileIndex);

        int bestTile = -1;
        float bestScore = float.MinValue;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();

            if (dist > 0 && unit.CanMoveTo(tile))
            {
                int unexploredCount = 0;
                int[] tileNeighbors = ts.GetNeighbors(tile);
                if (tileNeighbors != null)
                    foreach (int nn in tileNeighbors)
                        if (nn >= 0 && nn < fog.Length && fog[nn] == 0) unexploredCount++;

                if (unexploredCount > 0)
                {
                    float s = AIScorer.ScoreExplore(unit, tile, unexploredCount, dangerMap);
                    if (s > bestScore) { bestScore = s; bestTile = tile; }
                }
            }

            if (dist >= exploreRange) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (n >= 0 && !visited.Contains(n))
                {
                    if (n < fog.Length && fog[n] > 0)
                    {
                        visited.Add(n);
                        queue.Enqueue((n, dist + 1));
                    }
                    else
                    {
                        visited.Add(n);
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
