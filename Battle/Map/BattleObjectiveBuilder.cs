using System.Collections.Generic;

public static class BattleObjectiveBuilder
{
    public static BattleObjective BuildObjective(BattleMap map)
    {
        int candidate = -1;
        BattleObjectiveType type = BattleObjectiveType.Elimination;

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var c = map.Cells[i];
            if (!SupportsAnyDomain(c))
                continue;

            if (c.DeploymentOwner == BattleSide.Defender && c.HasPort)
            {
                candidate = i;
                type = BattleObjectiveType.PortCapture;
                break;
            }
            if (c.DeploymentOwner == BattleSide.Defender && c.HasBeach)
            {
                candidate = i;
                type = BattleObjectiveType.Beachhead;
                break;
            }
            if (c.DeploymentOwner == BattleSide.Defender)
            {
                candidate = i;
                type = c.SupportsNavalSurface && !c.SupportsLand ? BattleObjectiveType.NavalControl : BattleObjectiveType.LandControl;
                break;
            }
        }

        if (candidate < 0)
        {
            for (int i = 0; i < map.Cells.Count; i++)
            {
                var c = map.Cells[i];
                if (SupportsAnyDomain(c))
                {
                    candidate = i;
                    break;
                }
            }
        }

        if (candidate >= 0)
            map.Cells[candidate].IsObjective = true;

        return new BattleObjective
        {
            CellIndex = candidate,
            Owner = BattleSide.Defender,
            Type = type,
        };
    }

    private static bool SupportsAnyDomain(BattleCell cell) =>
        cell.SupportsLand || cell.SupportsNavalSurface || cell.SupportsUnderwater ||
        cell.SupportsAir || cell.SupportsOrbit || cell.SupportsSpace;
}
