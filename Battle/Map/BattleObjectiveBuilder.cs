using System.Collections.Generic;

public static class BattleObjectiveBuilder
{
    public static BattleObjective BuildObjective(BattleMap map)
    {
        int candidate = -1;

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var c = map.Cells[i];
            if (!c.IsPassable || c.IsWater)
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
                if (c.IsPassable && !c.IsWater)
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
}
