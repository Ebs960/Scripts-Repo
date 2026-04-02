using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ──────────────────────── Group-level action ────────────────────────

/// <summary>
/// Instead of each unit independently deciding, the group decides one action.
/// Per-unit commands are derived from the group action — giving coherent coordinated behavior.
/// </summary>
public enum GroupAction
{
    Rally,    // group not ready — all members move toward rally tile
    Advance,  // group ready — all members advance toward target
    Attack,   // group at target — all members engage nearby enemies
    Hold,     // group defending a position — fortify or attack nearby threats
    Flank     // group attempting to approach from an off-axis angle
}

/// <summary>
/// A coordinated group of combat units that share a target. Units gather at a rally point
/// before advancing, preventing suicidal trickle attacks.
///
/// Group-level decisions reduce micromanagement: one decision per group instead of per-unit.
/// The group chooses Rally/Advance/Attack/Hold/Flank, then emits per-unit AICommands accordingly.
///
/// Formation frontage: melee units advance toward the enemy, ranged units stay 1-2 tiles behind.
/// Focus-fire: the group picks one priority target (lowest HP ratio * highest threat) and
/// all units that can attack it do so before moving on to secondary targets.
/// Screen doctrine: melee units interpose between ranged allies and the nearest enemy.
/// Flank doctrine: when the group has 4+ units it may split into a main body + flank element.
/// </summary>
public class ArmyGroup
{
    public int GroupId;
    public int TargetTile = -1;
    public int RallyTile = -1;
    public int PlanetIndex;
    public float DesiredStrength;  // total attack power needed before advancing

    private readonly List<CombatUnit> members = new List<CombatUnit>();

    public IReadOnlyList<CombatUnit> Members => members;
    public int Count => members.Count;

    public float CurrentStrength
    {
        get
        {
            float s = 0f;
            foreach (var u in members) if (u != null) s += u.CurrentAttack;
            return s;
        }
    }

    public bool IsReady => CurrentStrength >= DesiredStrength;

    public void AddUnit(CombatUnit unit)
    {
        if (unit != null && !members.Contains(unit)) members.Add(unit);
    }

    public void RemoveUnit(CombatUnit unit) => members.Remove(unit);

    public void CleanupDead()
    {
        members.RemoveAll(u => u == null || u.currentHealth <= 0);
    }

    // ════════════════════════════════════════════════════════
    //  Classification helpers
    // ════════════════════════════════════════════════════════

    private static bool IsRangedUnit(CombatUnit unit)
    {
        return unit != null && unit.data != null && unit.data.isRangedUnit;
    }

    private static bool IsMeleeUnit(CombatUnit unit)
    {
        return unit != null && !IsRangedUnit(unit);
    }

    /// <summary>
    /// Split members into melee-front and ranged-rear lists.
    /// </summary>
    private void ClassifyMembers(out List<CombatUnit> melee, out List<CombatUnit> ranged)
    {
        melee = new List<CombatUnit>();
        ranged = new List<CombatUnit>();
        foreach (var u in members)
        {
            if (u == null || u.hasActedThisTurn || u.isStored) continue;
            if (IsRangedUnit(u)) ranged.Add(u);
            else melee.Add(u);
        }
    }

    // ════════════════════════════════════════════════════════
    //  Focus-fire target selection
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Collect nearby enemies and sort by priority: lowest HP ratio first, then highest
    /// threat value. This ensures the group focuses fire to secure kills.
    /// </summary>
    private List<BaseUnit> CollectAndPrioritizeEnemies(Civilization civ, TileSystem ts, int searchRadius = 4)
    {
        var enemies = new List<BaseUnit>();
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return enemies;

        foreach (var other in allCivs)
        {
            if (other == civ) continue;
            if (other.combatUnits != null)
                foreach (var e in other.combatUnits)
                    if (e != null && e.planetIndex == PlanetIndex &&
                        ts.GetTileDistance(e.currentTileIndex, TargetTile) <= searchRadius)
                        enemies.Add(e);
            if (other.workerUnits != null)
                foreach (var w in other.workerUnits)
                    if (w != null && w.planetIndex == PlanetIndex &&
                        ts.GetTileDistance(w.currentTileIndex, TargetTile) <= searchRadius)
                        enemies.Add(w);
        }

        // Priority: lowest HP ratio first, then highest attack (threat)
        enemies.Sort((a, b) =>
        {
            float hpA = (float)a.currentHealth / Mathf.Max(1, a.MaxHealth);
            float hpB = (float)b.currentHealth / Mathf.Max(1, b.MaxHealth);
            int hpCmp = hpA.CompareTo(hpB);
            if (hpCmp != 0) return hpCmp;
            return b.CurrentAttack.CompareTo(a.CurrentAttack);
        });

        return enemies;
    }

    /// <summary>
    /// Build a ranged-unit target list that includes enemies already attackable, enemies that can
    /// threaten the ranged unit on their next turn, and enemies that are only worth pressuring when
    /// the melee screen is actually in position to cover the ranged line.
    /// </summary>
    private List<BaseUnit> CollectRangedPriorityEnemies(Civilization civ, CombatUnit rangedUnit, List<CombatUnit> meleeScreen, TileSystem ts)
    {
        var enemies = new List<BaseUnit>();
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null || rangedUnit == null) return enemies;

        int directRange = Mathf.FloorToInt(rangedUnit.CurrentRange);
        int maneuverReach = directRange + Mathf.Max(1, rangedUnit.GetStartingMovePoints());

        foreach (var other in allCivs)
        {
            if (other == civ) continue;

            if (other.combatUnits != null)
            {
                foreach (var enemy in other.combatUnits)
                {
                    if (ShouldConsiderRangedEnemy(rangedUnit, enemy, meleeScreen, ts, directRange, maneuverReach))
                        enemies.Add(enemy);
                }
            }

            if (other.workerUnits != null)
            {
                foreach (var enemy in other.workerUnits)
                {
                    if (ShouldConsiderRangedEnemy(rangedUnit, enemy, meleeScreen, ts, directRange, maneuverReach))
                        enemies.Add(enemy);
                }
            }
        }

        enemies.Sort((a, b) => ScoreRangedEnemyCandidate(rangedUnit, b, meleeScreen, ts)
            .CompareTo(ScoreRangedEnemyCandidate(rangedUnit, a, meleeScreen, ts)));

        return enemies;
    }

    private bool ShouldConsiderRangedEnemy(CombatUnit rangedUnit, BaseUnit enemy, List<CombatUnit> meleeScreen, TileSystem ts, int directRange, int maneuverReach)
    {
        if (enemy == null || enemy.currentHealth <= 0 || enemy.planetIndex != PlanetIndex)
            return false;

        int distToUnit = ts.GetTileDistance(rangedUnit.currentTileIndex, enemy.currentTileIndex);
        bool canAttackNow = distToUnit <= directRange && CanAttackTarget(rangedUnit, enemy);
        bool threatensNextTurn = CanEnemyThreatenNextTurn(enemy, rangedUnit, ts);
        bool screenedAdvance = HasStableMeleeScreen(meleeScreen, rangedUnit, enemy, ts);
        bool nearObjective = TargetTile >= 0 && ts.GetTileDistance(enemy.currentTileIndex, TargetTile) <= maneuverReach;

        if (canAttackNow) return true;
        if (threatensNextTurn) return true;
        if (screenedAdvance && nearObjective && distToUnit <= maneuverReach) return true;

        return false;
    }

    private static bool CanEnemyThreatenNextTurn(BaseUnit enemy, CombatUnit rangedUnit, TileSystem ts)
    {
        if (enemy == null || rangedUnit == null) return false;

        int dist = ts.GetTileDistance(enemy.currentTileIndex, rangedUnit.currentTileIndex);
        int threatReach = Mathf.FloorToInt(enemy.CurrentRange) + Mathf.Max(1, enemy.GetStartingMovePoints());
        return dist <= threatReach;
    }

    private static bool HasStableMeleeScreen(List<CombatUnit> meleeScreen, CombatUnit rangedUnit, BaseUnit enemy, TileSystem ts)
    {
        if (meleeScreen == null || meleeScreen.Count == 0 || rangedUnit == null || enemy == null)
            return false;

        int rangedDist = ts.GetTileDistance(rangedUnit.currentTileIndex, enemy.currentTileIndex);
        int screeners = 0;
        float screenStrength = 0f;

        foreach (var melee in meleeScreen)
        {
            if (melee == null || melee.currentHealth <= 0) continue;

            int meleeDist = ts.GetTileDistance(melee.currentTileIndex, enemy.currentTileIndex);
            int supportDist = ts.GetTileDistance(melee.currentTileIndex, rangedUnit.currentTileIndex);

            if (meleeDist >= rangedDist) continue;
            if (supportDist > 2) continue;

            screeners++;
            screenStrength += melee.currentHealth + melee.CurrentDefense + melee.CurrentAttack * 0.5f;
            if (meleeDist <= 1) screenStrength += 6f;
        }

        if (screeners == 0) return false;

        float threatStrength = enemy.currentHealth + enemy.CurrentDefense * 0.5f + enemy.CurrentAttack * 1.25f;
        return screeners >= 2 || screenStrength >= threatStrength;
    }

    private float ScoreRangedEnemyCandidate(CombatUnit rangedUnit, BaseUnit enemy, List<CombatUnit> meleeScreen, TileSystem ts)
    {
        if (enemy == null) return float.MinValue;

        int distToUnit = ts.GetTileDistance(rangedUnit.currentTileIndex, enemy.currentTileIndex);
        int directRange = Mathf.FloorToInt(rangedUnit.CurrentRange);
        bool canAttackNow = CanAttackTarget(rangedUnit, enemy);
        bool threatensNextTurn = CanEnemyThreatenNextTurn(enemy, rangedUnit, ts);
        bool screenedAdvance = HasStableMeleeScreen(meleeScreen, rangedUnit, enemy, ts);

        float score = FocusFireScore(enemy);
        if (canAttackNow) score += 12f;
        if (threatensNextTurn) score += 10f;
        if (screenedAdvance) score += 4f;
        else if (!canAttackNow) score -= 8f;

        score -= Mathf.Max(0, distToUnit - directRange) * 1.5f;
        if (TargetTile >= 0)
            score -= ts.GetTileDistance(enemy.currentTileIndex, TargetTile) * 0.35f;

        return score;
    }

    /// <summary>
    /// Score a focus-fire target. Incentivizes finishing off wounded high-value targets.
    /// </summary>
    private static float FocusFireScore(BaseUnit enemy)
    {
        if (enemy == null) return float.MinValue;
        float hpRatio = (float)enemy.currentHealth / Mathf.Max(1, enemy.MaxHealth);
        return (1f - hpRatio) * 15f + enemy.BaseAttack * 1.5f;
    }

    // ════════════════════════════════════════════════════════
    //  Group-level decision + command expansion
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Decide the group's collective action based on readiness and proximity to target.
    /// </summary>
    public GroupAction DecideAction(DangerMap dangerMap)
    {
        if (Count == 0) return GroupAction.Hold;
        var ts = TileSystem.GetForPlanet(PlanetIndex) ?? TileSystem.Instance;
        if (ts == null) return GroupAction.Hold;

        // Average distance of members to target
        float avgDistToTarget = 0f;
        int count = 0;
        foreach (var u in members)
        {
            if (u == null || u.currentTileIndex < 0) continue;
            avgDistToTarget += ts.GetTileDistance(u.currentTileIndex, TargetTile);
            count++;
        }
        if (count > 0) avgDistToTarget /= count;

        // At target: Attack
        if (avgDistToTarget <= 2f) return GroupAction.Attack;

        // Ready and can advance: consider flanking if group is large enough
        if (IsReady)
        {
            if (count >= 3 && avgDistToTarget >= 3f && avgDistToTarget <= 6f)
                return GroupAction.Flank;
            return GroupAction.Advance;
        }

        // Not ready: Rally (gather at rally point)
        return GroupAction.Rally;
    }

    /// <summary>
    /// Expand the group action into per-unit AICommands. Returns commands for all members.
    /// This replaces per-unit TacticalEvaluator calls for grouped units.
    /// </summary>
    public List<AICommand> ExpandToCommands(GroupAction action, Civilization civ, DangerMap dangerMap)
    {
        var commands = new List<AICommand>(members.Count);
        var ts = TileSystem.GetForPlanet(PlanetIndex) ?? TileSystem.Instance;
        if (ts == null) return commands;

        switch (action)
        {
            case GroupAction.Rally:
                ExpandRally(commands, ts, dangerMap);
                break;
            case GroupAction.Advance:
                ExpandAdvance(commands, civ, ts, dangerMap);
                break;
            case GroupAction.Attack:
                ExpandAttack(commands, civ, ts, dangerMap);
                break;
            case GroupAction.Hold:
                ExpandHold(commands, ts, dangerMap);
                break;
            case GroupAction.Flank:
                ExpandFlank(commands, civ, ts, dangerMap);
                break;
        }
        return commands;
    }

    private void ExpandRally(List<AICommand> commands, TileSystem ts, DangerMap dangerMap)
    {
        int target = RallyTile >= 0 ? RallyTile : TargetTile;
        foreach (var unit in members)
        {
            if (unit == null || unit.hasActedThisTurn || unit.isStored) continue;
            int dist = ts.GetTileDistance(unit.currentTileIndex, target);
            if (dist <= 1)
            {
                // Already at rally — fortify while waiting
                commands.Add(new AIFortifyCommand
                {
                    unit = unit, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreFortify(unit, dangerMap) + 3f
                });
            }
            else
            {
                // Move toward rally point via best adjacent tile
                int bestTile = FindBestStepToward(unit, target, ts, dangerMap);
                if (bestTile >= 0)
                {
                    commands.Add(new AIMoveCommand
                    {
                        unit = unit, targetTileIndex = bestTile, planetIndex = PlanetIndex,
                        score = AIScorer.ScoreTileForMovement(unit, bestTile, target, dangerMap) + 8f
                    });
                }
            }
        }
    }

    /// <summary>
    /// Advance with formation frontage: melee units move toward the target first,
    /// ranged units stay behind the melee line and engage opportunistically.
    /// Melee units that are adjacent to enemies screen ranged allies by attacking.
    /// </summary>
    private void ExpandAdvance(List<AICommand> commands, Civilization civ, TileSystem ts, DangerMap dangerMap)
    {
        ClassifyMembers(out var melee, out var ranged);

        // ── Melee vanguard: advance toward target, attack if adjacent ──
        foreach (var unit in melee)
        {
            var adjacent = FindAdjacentEnemy(unit, ts);
            if (adjacent != null && CanAttackTarget(unit, adjacent))
            {
                commands.Add(new AIAttackCommand
                {
                    unit = unit, target = adjacent, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreAttack(unit, adjacent, dangerMap) + 5f
                });
                continue;
            }

            int bestTile = FindBestStepToward(unit, TargetTile, ts, dangerMap);
            if (bestTile >= 0)
            {
                commands.Add(new AIMoveCommand
                {
                    unit = unit, targetTileIndex = bestTile, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreTileForMovement(unit, bestTile, TargetTile, dangerMap) + 10f
                });
            }
        }

        // ── Ranged rearguard: stay behind melee, fire if in range ──
        int meleeCenter = GetCentroid(melee, ts);

        foreach (var unit in ranged)
        {
            // Try to attack enemies in range first
            var enemies = CollectRangedPriorityEnemies(civ, unit, melee, ts);
            AICommand bestCmd = null;
            float bestScore = float.MinValue;
            foreach (var enemy in enemies)
            {
                if (enemy == null || !CanAttackTarget(unit, enemy)) continue;
                float s = AIScorer.ScoreAttack(unit, enemy, dangerMap) + FocusFireScore(enemy);
                if (s > bestScore) { bestScore = s; bestCmd = new AIAttackCommand { unit = unit, target = enemy, planetIndex = PlanetIndex, score = s }; }
            }

            if (bestCmd != null)
            {
                commands.Add(bestCmd);
                continue;
            }

            // No target in range: move toward a tile behind the melee center
            var priorityEnemy = enemies.Count > 0 ? enemies[0] : null;
            bool urgentThreat = priorityEnemy != null && CanEnemyThreatenNextTurn(priorityEnemy, unit, ts);
            bool stableScreen = priorityEnemy != null && HasStableMeleeScreen(melee, unit, priorityEnemy, ts);
            int screenAnchor = priorityEnemy != null ? priorityEnemy.currentTileIndex : TargetTile;
            int screenTile = FindScreenPosition(unit, meleeCenter, screenAnchor, ts, dangerMap, urgentThreat && !stableScreen);
            if (screenTile >= 0)
            {
                commands.Add(new AIMoveCommand
                {
                    unit = unit, targetTileIndex = screenTile, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreTileForMovement(unit, screenTile, screenAnchor, dangerMap) + (urgentThreat && !stableScreen ? 10f : 8f)
                });
            }
            else
            {
                int bestTile = FindBestStepToward(unit, TargetTile, ts, dangerMap);
                if (bestTile >= 0)
                    commands.Add(new AIMoveCommand
                    {
                        unit = unit, targetTileIndex = bestTile, planetIndex = PlanetIndex,
                        score = AIScorer.ScoreTileForMovement(unit, bestTile, TargetTile, dangerMap) + 7f
                    });
            }
        }
    }

    /// <summary>
    /// Attack with coordinated focus fire. The group picks the highest-priority target
    /// (lowest HP ratio * highest threat) and all units that can attack it do so.
    /// Tracks allocated damage to avoid overkilling one target while ignoring others.
    /// </summary>
    private void ExpandAttack(List<AICommand> commands, Civilization civ, TileSystem ts, DangerMap dangerMap)
    {
        var enemies = CollectAndPrioritizeEnemies(civ, ts, 4);
        ClassifyMembers(out var melee, out var ranged);

        // Track damage allocated per enemy to avoid overkill
        var damageAllocated = new Dictionary<BaseUnit, int>();

        // Process all active units: melee first (they need adjacency), then ranged
        var allActive = new List<CombatUnit>(melee);
        allActive.AddRange(ranged);

        foreach (var unit in allActive)
        {
            AICommand bestCmd = null;
            float bestScore = float.MinValue;

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.currentHealth <= 0 || !CanAttackTarget(unit, enemy)) continue;

                float s = AIScorer.ScoreAttack(unit, enemy, dangerMap);

                // Focus-fire coordination: check remaining HP after allocated damage
                int alreadyAllocated = 0;
                damageAllocated.TryGetValue(enemy, out alreadyAllocated);
                int remainingHP = enemy.currentHealth - alreadyAllocated;

                if (remainingHP <= 0) continue; // already overkilling this target

                // Big bonus for finishing off a target
                if (unit.CurrentAttack >= remainingHP)
                    s += 12f;
                else
                    s += 5f;

                s += FocusFireScore(enemy) * 0.5f;

                if (s > bestScore) { bestScore = s; bestCmd = new AIAttackCommand { unit = unit, target = enemy, planetIndex = PlanetIndex, score = s }; }
            }

            if (bestCmd != null)
            {
                commands.Add(bestCmd);
                var target = ((AIAttackCommand)bestCmd).target;
                if (!damageAllocated.ContainsKey(target)) damageAllocated[target] = 0;
                damageAllocated[target] += unit.CurrentAttack;
            }
            else
            {
                // No one in range — melee advance, ranged reposition
                if (IsMeleeUnit(unit))
                {
                    int bestTile = FindBestStepToward(unit, TargetTile, ts, dangerMap);
                    if (bestTile >= 0)
                        commands.Add(new AIMoveCommand
                        {
                            unit = unit, targetTileIndex = bestTile, planetIndex = PlanetIndex,
                            score = AIScorer.ScoreTileForMovement(unit, bestTile, TargetTile, dangerMap) + 6f
                        });
                }
                else
                {
                    int meleeCenter = GetCentroid(melee, ts);
                    int screenTile = FindScreenPosition(unit, meleeCenter, TargetTile, ts, dangerMap);
                    if (screenTile >= 0)
                        commands.Add(new AIMoveCommand
                        {
                            unit = unit, targetTileIndex = screenTile, planetIndex = PlanetIndex,
                            score = AIScorer.ScoreTileForMovement(unit, screenTile, TargetTile, dangerMap) + 4f
                        });
                }
            }
        }
    }

    private void ExpandHold(List<AICommand> commands, TileSystem ts, DangerMap dangerMap)
    {
        foreach (var unit in members)
        {
            if (unit == null || unit.hasActedThisTurn || unit.isStored) continue;

            // Attack adjacent enemies if any
            var adjacent = FindAdjacentEnemy(unit, ts);
            if (adjacent != null && CanAttackTarget(unit, adjacent))
            {
                commands.Add(new AIAttackCommand
                {
                    unit = unit, target = adjacent, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreAttack(unit, adjacent, dangerMap) + 4f
                });
            }
            else
            {
                commands.Add(new AIFortifyCommand
                {
                    unit = unit, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreFortify(unit, dangerMap) + 5f
                });
            }
        }
    }

    // ──── Helpers ────

    /// <summary>
    /// Flank doctrine: split the group into a main body and a flank element.
    /// The main body advances directly toward the target (pinning force),
    /// while the flank element approaches from an off-axis angle.
    /// Ranged units always stay in the main body; excess melee units form the flank.
    /// </summary>
    private void ExpandFlank(List<AICommand> commands, Civilization civ, TileSystem ts, DangerMap dangerMap)
    {
        ClassifyMembers(out var melee, out var ranged);

        // Need at least 2 melee to form a flank element
        if (melee.Count < 2)
        {
            ExpandAdvance(commands, civ, ts, dangerMap);
            return;
        }

        // Split melee: 2/3 main body, 1/3 flank
        int flankCount = Mathf.Max(1, melee.Count / 3);
        var flankUnits = melee.GetRange(melee.Count - flankCount, flankCount);
        var mainMelee = melee.GetRange(0, melee.Count - flankCount);

        int flankTarget = FindFlankTile(ts, TargetTile, dangerMap);

        // ── Main body: advance directly ──
        foreach (var unit in mainMelee)
        {
            var adjacent = FindAdjacentEnemy(unit, ts);
            if (adjacent != null && CanAttackTarget(unit, adjacent))
            {
                commands.Add(new AIAttackCommand
                {
                    unit = unit, target = adjacent, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreAttack(unit, adjacent, dangerMap) + 5f
                });
                continue;
            }

            int bestTile = FindBestStepToward(unit, TargetTile, ts, dangerMap);
            if (bestTile >= 0)
                commands.Add(new AIMoveCommand
                {
                    unit = unit, targetTileIndex = bestTile, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreTileForMovement(unit, bestTile, TargetTile, dangerMap) + 10f
                });
        }

        // Ranged units fire or stay screened
        int meleeCenter = GetCentroid(mainMelee, ts);
        foreach (var unit in ranged)
        {
            var enemies = CollectRangedPriorityEnemies(civ, unit, mainMelee, ts);
            AICommand bestCmd = null;
            float bestScore = float.MinValue;
            foreach (var enemy in enemies)
            {
                if (enemy == null || !CanAttackTarget(unit, enemy)) continue;
                float s = AIScorer.ScoreAttack(unit, enemy, dangerMap) + FocusFireScore(enemy);
                if (s > bestScore) { bestScore = s; bestCmd = new AIAttackCommand { unit = unit, target = enemy, planetIndex = PlanetIndex, score = s }; }
            }

            if (bestCmd != null)
            {
                commands.Add(bestCmd);
            }
            else
            {
                var priorityEnemy = enemies.Count > 0 ? enemies[0] : null;
                bool urgentThreat = priorityEnemy != null && CanEnemyThreatenNextTurn(priorityEnemy, unit, ts);
                bool stableScreen = priorityEnemy != null && HasStableMeleeScreen(mainMelee, unit, priorityEnemy, ts);
                int screenAnchor = priorityEnemy != null ? priorityEnemy.currentTileIndex : TargetTile;
                int screenTile = FindScreenPosition(unit, meleeCenter, screenAnchor, ts, dangerMap, urgentThreat && !stableScreen);
                int moveTo = screenTile >= 0 ? screenTile : FindBestStepToward(unit, TargetTile, ts, dangerMap);
                if (moveTo >= 0)
                    commands.Add(new AIMoveCommand
                    {
                        unit = unit, targetTileIndex = moveTo, planetIndex = PlanetIndex,
                        score = AIScorer.ScoreTileForMovement(unit, moveTo, screenAnchor, dangerMap) + (urgentThreat && !stableScreen ? 9f : 7f)
                    });
            }
        }

        // ── Flank element: approach from the side ──
        foreach (var unit in flankUnits)
        {
            var adjacent = FindAdjacentEnemy(unit, ts);
            if (adjacent != null && CanAttackTarget(unit, adjacent))
            {
                commands.Add(new AIAttackCommand
                {
                    unit = unit, target = adjacent, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreAttack(unit, adjacent, dangerMap) + 9f // flank bonus
                });
                continue;
            }

            int bestTile = FindBestStepToward(unit, flankTarget, ts, dangerMap);
            if (bestTile >= 0)
                commands.Add(new AIMoveCommand
                {
                    unit = unit, targetTileIndex = bestTile, planetIndex = PlanetIndex,
                    score = AIScorer.ScoreTileForMovement(unit, bestTile, flankTarget, dangerMap) + 10f
                });
        }
    }

    private static bool CanAttackTarget(CombatUnit attacker, BaseUnit target)
    {
        if (target is CombatUnit ct) return attacker.CanAttack(ct);
        if (target is WorkerUnit wt) return attacker.CanAttack(wt);
        return false;
    }

    private static int FindBestStepToward(BaseUnit unit, int target, TileSystem ts, DangerMap dangerMap)
    {
        int[] neighbors = ts.GetNeighbors(unit.currentTileIndex);
        if (neighbors == null) return -1;
        int best = -1;
        float bestScore = float.MinValue;
        foreach (int n in neighbors)
        {
            if (n < 0 || !unit.CanMoveTo(n)) continue;
            float s = AIScorer.ScoreTileForMovement(unit, n, target, dangerMap);
            if (s > bestScore) { bestScore = s; best = n; }
        }
        return best;
    }

    private static BaseUnit FindAdjacentEnemy(CombatUnit unit, TileSystem ts)
    {
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return null;
        int[] neighbors = ts.GetNeighbors(unit.currentTileIndex);
        if (neighbors == null) return null;

        var adjacent = new HashSet<int>(neighbors);
        adjacent.Add(unit.currentTileIndex);

        foreach (var civ in allCivs)
        {
            if (civ == unit.owner) continue;
            if (civ.combatUnits != null)
                foreach (var e in civ.combatUnits)
                    if (e != null && e.planetIndex == unit.planetIndex && adjacent.Contains(e.currentTileIndex))
                        return e;
            if (civ.workerUnits != null)
                foreach (var w in civ.workerUnits)
                    if (w != null && w.planetIndex == unit.planetIndex && adjacent.Contains(w.currentTileIndex))
                        return w;
        }
        return null;
    }

    /// <summary>
    /// Get the centroid tile index of a set of units (tile that minimizes total distance to all).
    /// </summary>
    private static int GetCentroid(List<CombatUnit> units, TileSystem ts)
    {
        if (units == null || units.Count == 0) return -1;
        if (units.Count == 1) return units[0]?.currentTileIndex ?? -1;

        int bestTile = -1;
        int bestTotal = int.MaxValue;
        foreach (var u in units)
        {
            if (u == null) continue;
            int total = 0;
            foreach (var v in units)
            {
                if (v == null || v == u) continue;
                total += ts.GetTileDistance(u.currentTileIndex, v.currentTileIndex);
            }
            if (total < bestTotal) { bestTotal = total; bestTile = u.currentTileIndex; }
        }
        return bestTile;
    }

    /// <summary>
    /// Find a tile for a ranged unit that is behind the melee center relative to the target.
    /// Prefers tiles with elevation and low danger.
    /// </summary>
    private static int FindScreenPosition(CombatUnit rangedUnit, int meleeCenter, int targetTile, TileSystem ts, DangerMap dangerMap, bool preferDeepScreen = false)
    {
        if (meleeCenter < 0) return -1;
        int[] neighbors = ts.GetNeighbors(rangedUnit.currentTileIndex);
        if (neighbors == null) return -1;

        int meleeDist = ts.GetTileDistance(meleeCenter, targetTile);
        int best = -1;
        float bestScore = float.MinValue;

        foreach (int n in neighbors)
        {
            if (n < 0 || !rangedUnit.CanMoveTo(n)) continue;
            int distToTarget = ts.GetTileDistance(n, targetTile);
            int distToMelee = ts.GetTileDistance(n, meleeCenter);

            float s = 0f;
            if (distToTarget > meleeDist)
                s += preferDeepScreen ? 6f : 4f; // behind melee line
            else if (distToTarget == meleeDist)
                s += preferDeepScreen ? -1f : 1f;
            else
                s -= preferDeepScreen ? 6f : 3f; // in front of melee = bad for ranged

            if (preferDeepScreen)
                s -= Mathf.Max(0, distToMelee - 2) * 2f;

            s -= dangerMap.GetDanger(n) * 1.5f;

            var td = ts.GetTileData(n);
            if (td != null && td.isHill) s += 3f; // elevation advantage for ranged

            if (s > bestScore) { bestScore = s; best = n; }
        }
        return best;
    }

    /// <summary>
    /// Find a flank approach tile: 2-3 steps from target, off-axis from the group centroid.
    /// </summary>
    private int FindFlankTile(TileSystem ts, int targetTile, DangerMap dangerMap)
    {
        int groupCenter = GetCentroid(members.Cast<CombatUnit>().ToList(), ts);
        if (groupCenter < 0) return targetTile;

        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((targetTile, 0));
        visited.Add(targetTile);

        int directDist = ts.GetTileDistance(groupCenter, targetTile);
        int best = -1;
        float bestScore = float.MinValue;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist >= 2 && dist <= 3)
            {
                int distToCenter = ts.GetTileDistance(tile, groupCenter);
                float angleDiff = Mathf.Abs(distToCenter - directDist);
                float s = angleDiff * 3f;
                s -= dangerMap.GetDanger(tile) * 1.0f;
                var td = ts.GetTileData(tile);
                if (td != null && td.isLand) s += 1f;
                else s -= 100f;
                if (s > bestScore) { bestScore = s; best = tile; }
            }
            if (dist >= 3) continue;
            foreach (int n in ts.GetNeighbors(tile))
            {
                if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, dist + 1)); }
            }
        }
        return best >= 0 ? best : targetTile;
    }
}

/// <summary>
/// Manages all army groups for a civilization. Groups are re-evaluated each turn:
/// dead units are removed, under-strength groups are dissolved, and new groups
/// are formed when multiple units share the same objective.
/// </summary>
public class ArmyGroupManager
{
    private readonly List<ArmyGroup> groups = new List<ArmyGroup>();
    private readonly Dictionary<int, ArmyGroup> unitToGroup = new Dictionary<int, ArmyGroup>();
    private int nextGroupId = 1;

    public IReadOnlyList<ArmyGroup> Groups => groups;

    /// <summary>
    /// Get the group a unit belongs to, or null.
    /// </summary>
    public ArmyGroup GetGroupForUnit(CombatUnit unit)
    {
        if (unit == null) return null;
        unitToGroup.TryGetValue(unit.GetInstanceID(), out var g);
        return g;
    }

    /// <summary>
    /// Create a new army group targeting the given tile.
    /// </summary>
    public ArmyGroup CreateGroup(int targetTile, int rallyTile, int planetIndex, float desiredStrength)
    {
        var group = new ArmyGroup
        {
            GroupId = nextGroupId++,
            TargetTile = targetTile,
            RallyTile = rallyTile,
            PlanetIndex = planetIndex,
            DesiredStrength = desiredStrength
        };
        groups.Add(group);
        return group;
    }

    public void AssignUnit(CombatUnit unit, ArmyGroup group)
    {
        if (unit == null || group == null) return;
        int id = unit.GetInstanceID();
        if (unitToGroup.TryGetValue(id, out var oldGroup) && oldGroup != group)
            oldGroup.RemoveUnit(unit);
        group.AddUnit(unit);
        unitToGroup[id] = group;
    }

    /// <summary>
    /// Cleanup dead units and dissolve empty groups. Call at the start of each turn.
    /// </summary>
    public void Refresh()
    {
        foreach (var g in groups) g.CleanupDead();
        // Remove empty groups
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].Count == 0) groups.RemoveAt(i);
        }
        // Rebuild lookup
        unitToGroup.Clear();
        foreach (var g in groups)
            foreach (var u in g.Members)
                if (u != null) unitToGroup[u.GetInstanceID()] = g;
    }

    /// <summary>
    /// Auto-form groups from units that share nearby objectives (e.g., all targeting the same enemy city).
    /// Units within groupRange of each other AND targeting similar areas get grouped.
    /// </summary>
    public void AutoFormGroups(Civilization civ, DangerMap dangerMap, int planetIndex, int groupRange = 6)
    {
        if (civ == null || civ.combatUnits == null) return;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        // Find enemy city targets
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return;
        var enemyCities = new List<City>();
        foreach (var other in allCivs)
        {
            if (other == civ || other.cities == null) continue;
            foreach (var city in other.cities)
            {
                if (city != null && city.planetIndex == planetIndex) enemyCities.Add(city);
            }
        }
        if (enemyCities.Count == 0) return;

        // For each enemy city, gather nearby free (ungrouped) combat units
        foreach (var city in enemyCities)
        {
            var nearbyUnits = new List<CombatUnit>();
            foreach (var u in civ.combatUnits)
            {
                if (u == null || u.hasActedThisTurn || u.IsInOrbit || u.planetIndex != planetIndex) continue;
                if (GetGroupForUnit(u) != null) continue;
                int d = ts.GetTileDistance(u.currentTileIndex, city.centerTileIndex);
                if (d <= groupRange) nearbyUnits.Add(u);
            }
            if (nearbyUnits.Count < 2) continue; // need at least 2 for a group

            float totalStrength = nearbyUnits.Sum(u => u.CurrentAttack);
            float desiredStrength = Mathf.Max(10f, city.level * 8f);
            if (totalStrength < desiredStrength * 0.4f) continue; // not enough units yet

            // Pick a rally tile: tile closest to city that is safe
            int rallyTile = FindRallyTile(ts, city.centerTileIndex, nearbyUnits, dangerMap);

            var group = CreateGroup(city.centerTileIndex, rallyTile, planetIndex, desiredStrength);
            foreach (var u in nearbyUnits) AssignUnit(u, group);
        }
    }

    private int FindRallyTile(TileSystem ts, int targetTile, List<CombatUnit> units, DangerMap dangerMap)
    {
        // Pick the tile 2 steps from target with lowest danger
        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((targetTile, 0));
        visited.Add(targetTile);
        int best = -1;
        float bestScore = float.MinValue;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist >= 2 && dist <= 3)
            {
                float s = -dangerMap.GetDanger(tile);
                if (s > bestScore) { bestScore = s; best = tile; }
            }
            if (dist >= 3) continue;
            foreach (int n in ts.GetNeighbors(tile))
            {
                if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, dist + 1)); }
            }
        }
        return best >= 0 ? best : targetTile;
    }

    public void Clear()
    {
        groups.Clear();
        unitToGroup.Clear();
    }
}
