using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A coordinated group of combat units that share a target. Units gather at a rally point
/// before advancing, preventing suicidal trickle attacks.
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
