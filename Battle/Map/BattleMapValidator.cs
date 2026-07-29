using System.Collections.Generic;

public static class BattleMapValidator
{
    public static bool Validate(BattleMap map, int attackerCount, int defenderCount, out string reason)
    {
        reason = string.Empty;
        if (map == null || map.CellCount == 0)
        {
            reason = "empty map";
            return false;
        }

        if (!IsConnected(map))
        {
            reason = "map not connected";
            return false;
        }

        int attackerDeploy = 0;
        int defenderDeploy = 0;

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var c = map.Cells[i];
            if (!c.IsPassable || c.IsWater)
                continue;

            if (c.DeploymentOwner == BattleSide.Attacker)
                attackerDeploy++;
            else if (c.DeploymentOwner == BattleSide.Defender)
                defenderDeploy++;
        }

        if (attackerDeploy < attackerCount)
        {
            reason = "attacker deployment too small";
            return false;
        }

        if (defenderDeploy < defenderCount)
        {
            reason = "defender deployment too small";
            return false;
        }

        bool hasObjective = false;
        for (int i = 0; i < map.Cells.Count; i++)
        {
            if (map.Cells[i].IsObjective)
            {
                hasObjective = true;
                break;
            }
        }

        if (!hasObjective)
        {
            reason = "no objective";
            return false;
        }

        return true;
    }

    private static bool IsConnected(BattleMap map)
    {
        var first = -1;
        for (int i = 0; i < map.Cells.Count; i++)
        {
            if (map.Cells[i].IsPassable)
            {
                first = i;
                break;
            }
        }

        if (first < 0)
            return false;

        var q = new Queue<int>();
        var seen = new HashSet<int>();
        q.Enqueue(first);
        seen.Add(first);

        while (q.Count > 0)
        {
            int current = q.Dequeue();
            var cell = map.Cells[current];
            if (cell.NeighborIndices == null)
                continue;

            for (int i = 0; i < cell.NeighborIndices.Length; i++)
            {
                int n = cell.NeighborIndices[i];
                if (n < 0 || n >= map.Cells.Count)
                    continue;

                if (!map.Cells[n].IsPassable)
                    continue;

                if (seen.Add(n))
                    q.Enqueue(n);
            }
        }

        for (int i = 0; i < map.Cells.Count; i++)
        {
            if (map.Cells[i].IsPassable && !seen.Contains(i))
                return false;
        }

        return true;
    }
}
