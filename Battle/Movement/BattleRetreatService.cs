using System.Collections.Generic;

/// <summary>Builds deterministic, domain-aware routes to dedicated friendly exits.</summary>
public sealed class BattleRetreatService
{
    private readonly BattlePathfinder pathfinder;

    public BattleRetreatService(BattlePathfinder pathfinder) => this.pathfinder = pathfinder;

    public bool TryFindRoute(BattleSession session, BattleUnitState unit, BattleOccupancy occupancy,
        int requestedExit, out List<int> route, out string reason)
    {
        route = null; reason = string.Empty;
        if (session == null || unit == null || occupancy == null || unit.CellIndex < 0)
        { reason = "unit is not available to retreat"; return false; }

        int bestCost = int.MaxValue;
        for (int i = 0; i < session.Map.CellCount; i++)
        {
            var exit = session.Map.GetCell(i);
            if (exit?.RetreatExitForSide != unit.Side || !exit.Supports(unit.Domain)) continue;
            if (requestedExit >= 0 && i != requestedExit) continue;
            if (!occupancy.CanEnter(unit, i, session.Map)) continue;
            if (!pathfinder.TryFindPath(session, unit, i, occupancy, out var candidate, out int cost)) continue;
            if (CrossesHostileControl(session, unit, candidate)) continue;
            if (cost < bestCost || cost == bestCost && (route == null || i < route[route.Count - 1]))
            { bestCost = cost; route = candidate; }
        }
        if (route == null)
        { reason = requestedExit >= 0 ? "no legal route to the selected retreat exit" : "no legal friendly retreat route"; return false; }
        return true;
    }

    private static bool CrossesHostileControl(BattleSession session, BattleUnitState unit, List<int> route)
    {
        if (unit.Domain != BattleDomain.Land || BattleZoneOfControl.IgnoresZoc(unit)) return false;
        for (int i = 1; i < route.Count; i++)
            if (BattleZoneOfControl.IsEnemyZocCell(session, unit, route[i])) return true;
        return false;
    }
}
