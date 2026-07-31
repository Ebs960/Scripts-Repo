public sealed class BattleReinforcementController
{
    public void DeployRoundReinforcements(BattleSession session, BattleOccupancy occupancy, int round)
    {
        if (session == null || occupancy == null)
            return;

        for (int i = 0; i < session.Reinforcements.Count; i++)
        {
            var g = session.Reinforcements[i];
            if (g == null || g.AvailableFromRound > round || g.Theater != session.Theater)
                continue;
            for (int u = 0; u < session.Units.Count; u++)
            {
                var reserve = session.Units[u];
                if (reserve == null || !reserve.IsReserve || reserve.ReinforcementGroupId != g.ReinforcementGroupId) continue;
                int entry = FindEntry(session, occupancy, reserve, g);
                if (entry < 0) continue;
                reserve.IsReserve = false;
                reserve.CurrentMovePoints = reserve.Snapshot.TacticalMovePoints;
                reserve.CurrentActionPoints = reserve.Snapshot.TacticalActionPoints;
                reserve.HasActed = reserve.Side != session.ActiveSide;
                occupancy.TryMove(reserve, entry, session.Map);
            }
        }
    }

    private static int FindEntry(BattleSession session, BattleOccupancy occupancy, BattleUnitState unit, BattleReinforcementGroup group)
    {
        if (!BattleTheaterResolver.AllowsDomain(session.Theater, unit.Domain, group.EntryMethod == BattleEntryMethod.CarrierLaunch)) return -1;
        if (group.EntryCellIndex >= 0 && occupancy.CanEnter(unit, group.EntryCellIndex, session.Map)) return group.EntryCellIndex;
        for (int i = 0; i < session.Map.Cells.Count; i++)
        {
            var cell = session.Map.Cells[i];
            if (cell.DeploymentOwner != group.Side || !cell.Supports(unit.Domain)) continue;
            if (occupancy.CanEnter(unit, i, session.Map)) return i;
        }
        return -1;
    }
}
