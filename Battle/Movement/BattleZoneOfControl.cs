public static class BattleZoneOfControl
{
    public static bool ExertsZoc(BattleUnitState unit)
    {
        if (unit?.Snapshot?.TacticalProfile == null)
            return true;

        return unit.Snapshot.TacticalProfile.exertsZoneOfControl;
    }

    public static bool IgnoresZoc(BattleUnitState unit)
    {
        return unit?.Snapshot?.TacticalProfile != null && unit.Snapshot.TacticalProfile.ignoresZoneOfControl;
    }

    public static bool IsEnemyZocCell(BattleSession session, BattleUnitState mover, int cellIndex)
    {
        if (session == null || mover == null)
            return false;

        for (int i = 0; i < session.Units.Count; i++)
        {
            var u = session.Units[i];
            if (u == null || !u.IsAliveAndActive || u.Side == mover.Side || !ExertsZoc(u))
                continue;

            var enemyCell = session.Map.GetCell(u.CellIndex);
            if (enemyCell?.NeighborIndices == null)
                continue;

            for (int n = 0; n < enemyCell.NeighborIndices.Length; n++)
            {
                if (enemyCell.NeighborIndices[n] == cellIndex)
                    return true;
            }
        }

        return false;
    }
}
