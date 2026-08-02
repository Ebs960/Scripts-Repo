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
                reserve.HasEnteredBattle = true;
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
        for (int i = 0; i < group.EntryCellIndices.Count; i++)
        {
            int entry = group.EntryCellIndices[i];
            var candidate = session.Map.GetCell(entry);
            if (candidate != null && candidate.IsReinforcementEntry
                && candidate.DeploymentOwner == group.Side && candidate.Supports(unit.Domain)
                && occupancy.CanEnter(unit, entry, session.Map))
                return entry;
        }
        var cell = session.Map.GetCell(group.EntryCellIndex);
        return cell != null && cell.IsReinforcementEntry && cell.DeploymentOwner == group.Side
            && cell.Supports(unit.Domain) && occupancy.CanEnter(unit, group.EntryCellIndex, session.Map)
            ? group.EntryCellIndex : -1;
    }
}
