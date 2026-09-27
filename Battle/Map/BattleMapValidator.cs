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
            if (!SupportsAnyDomain(c))
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

        var coordinates = new HashSet<(int,int)>();
        for(int i=0;i<map.Cells.Count;i++)
            if(!coordinates.Add((map.Cells[i].LocalQ,map.Cells[i].LocalR))) { reason="duplicate local axial coordinate"; return false; }

        int anchor=map.Cells[0].CampaignTileIndex;
        for(int i=1;i<map.Cells.Count;i++)
            if(map.Cells[i].CampaignTileIndex!=anchor) { reason="tactical map spans multiple strategic tiles"; return false; }

        return true;
    }

    private static bool IsConnected(BattleMap map)
    {
        var first = -1;
        for (int i = 0; i < map.Cells.Count; i++)
        {
            if (SupportsAnyDomain(map.Cells[i]))
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

                if (!SupportsAnyDomain(map.Cells[n]))
                    continue;

                if (seen.Add(n))
                    q.Enqueue(n);
            }
        }

        for (int i = 0; i < map.Cells.Count; i++)
        {
            if (SupportsAnyDomain(map.Cells[i]) && !seen.Contains(i))
                return false;
        }

        return true;
    }

    private static bool SupportsAnyDomain(BattleCell cell) =>
        cell.SupportsLand || cell.SupportsNavalSurface || cell.SupportsUnderwater ||
        cell.SupportsAir || cell.SupportsOrbit || cell.SupportsSpace;
}
