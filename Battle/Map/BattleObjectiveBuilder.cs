using System.Collections.Generic;

public static class BattleObjectiveBuilder
{
    public static BattleObjective BuildObjective(BattleMap map)
    {
        int candidate = -1;

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var c = map.Cells[i];
            if (!SupportsAnyDomain(c))
                continue;

            if (c.DeploymentOwner == BattleSide.Defender)
            {
                candidate = i;
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
        };
    }

    private static bool SupportsAnyDomain(BattleCell cell) =>
        cell.SupportsLand || cell.SupportsNavalSurface || cell.SupportsUnderwater ||
        cell.SupportsAir || cell.SupportsOrbit || cell.SupportsSpace;
}
