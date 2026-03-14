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
    Hold      // group defending a position — fortify or attack nearby threats
}

/// <summary>
/// A coordinated group of combat units that share a target. Units gather at a rally point
/// before advancing, preventing suicidal trickle attacks.
///
/// Group-level decisions reduce micromanagement: one decision per group instead of per-unit.
/// The group chooses Rally/Advance/Attack/Hold, then emits per-unit AICommands accordingly.
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

        // Ready and can advance: Advance
        if (IsReady) return GroupAction.Advance;

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
                ExpandAdvance(commands, ts, dangerMap);
                break;
            case GroupAction.Attack:
                ExpandAttack(commands, civ, ts, dangerMap);
                break;
            case GroupAction.Hold:
                ExpandHold(commands, ts, dangerMap);
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

    private void ExpandAdvance(List<AICommand> commands, TileSystem ts, DangerMap dangerMap)
    {
        foreach (var unit in members)
        {
            if (unit == null || unit.hasActedThisTurn || unit.isStored) continue;

            // Can we attack something adjacent? Opportunistic attacks while advancing.
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

            // Otherwise move toward target
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
    }

    private void ExpandAttack(List<AICommand> commands, Civilization civ, TileSystem ts, DangerMap dangerMap)
    {
        // Collect all attackable enemies near the target
        var enemies = new List<BaseUnit>();
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs != null)
        {
            foreach (var other in allCivs)
            {
                if (other == civ) continue;
                if (other.combatUnits != null)
                    foreach (var e in other.combatUnits)
                        if (e != null && e.planetIndex == PlanetIndex &&
                            ts.GetTileDistance(e.currentTileIndex, TargetTile) <= 3)
                            enemies.Add(e);
                if (other.workerUnits != null)
                    foreach (var w in other.workerUnits)
                        if (w != null && w.planetIndex == PlanetIndex &&
                            ts.GetTileDistance(w.currentTileIndex, TargetTile) <= 3)
                            enemies.Add(w);
            }
        }

        // Sort enemies by threat (highest attack first) — focus fire
        enemies.Sort((a, b) => b.CurrentAttack.CompareTo(a.CurrentAttack));

        foreach (var unit in members)
        {
            if (unit == null || unit.hasActedThisTurn || unit.isStored) continue;

            // Find best target this unit can attack
            AICommand bestCmd = null;
            float bestScore = float.MinValue;

            foreach (var enemy in enemies)
            {
                if (enemy == null || !CanAttackTarget(unit, enemy)) continue;
                float s = AIScorer.ScoreAttack(unit, enemy, dangerMap) + 5f;
                // Focus fire bonus: higher score if other group members can also hit this target
                s += 3f;
                if (s > bestScore) { bestScore = s; bestCmd = new AIAttackCommand { unit = unit, target = enemy, planetIndex = PlanetIndex, score = s }; }
            }

            if (bestCmd != null)
            {
                commands.Add(bestCmd);
            }
            else
            {
                // No one in range — move closer
                int bestTile = FindBestStepToward(unit, TargetTile, ts, dangerMap);
                if (bestTile >= 0)
                    commands.Add(new AIMoveCommand
                    {
                        unit = unit, targetTileIndex = bestTile, planetIndex = PlanetIndex,
                        score = AIScorer.ScoreTileForMovement(unit, bestTile, TargetTile, dangerMap) + 6f
                    });
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
