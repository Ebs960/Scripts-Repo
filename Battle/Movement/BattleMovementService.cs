using System.Collections.Generic;

public sealed class BattleMovementService
{
    private readonly BattlePathfinder pathfinder;

    public BattleMovementService(BattlePathfinder pathfinder)
    {
        this.pathfinder = pathfinder;
    }

    public bool TryMove(BattleSession session, BattleUnitState unit, int destination, BattleOccupancy occupancy, out List<int> path)
    {
        path = null;

        if (!pathfinder.TryFindPath(session, unit, destination, occupancy, out var computedPath, out int moveCost))
            return false;

        if (moveCost > unit.CurrentMovePoints)
            return false;

        if (!occupancy.TryMove(unit, destination, session.Map))
            return false;

        unit.CurrentMovePoints -= moveCost;
        unit.HasMoved = true;
        unit.IsDefending = false;
        if (unit.Domain == BattleDomain.Land
            && !BattleZoneOfControl.IgnoresZoc(unit)
            && BattleZoneOfControl.IsEnemyZocCell(session, unit, destination))
            unit.CurrentMovePoints = 0;
        path = computedPath;
        return true;
    }

    public bool TryGetPath(BattleSession session, BattleUnitState unit, int destination, BattleOccupancy occupancy, out List<int> path)
    {
        path = null;
        if (session == null || unit == null || occupancy == null)
            return false;

        if (!pathfinder.TryFindPath(session, unit, destination, occupancy, out path, out int cost))
            return false;

        return cost <= unit.CurrentMovePoints;
    }
}
