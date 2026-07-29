using System.Collections.Generic;
using UnityEngine;

public static class BattleElevationResolver
{
    public static void QuantizeElevations(BattleMap map, TileSystem tileSystem)
    {
        if (map == null || tileSystem == null || map.CellCount == 0)
            return;

        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var td = tileSystem.GetTileData(map.Cells[i].CampaignTileIndex);
            if (td == null)
                continue;

            min = Mathf.Min(min, td.elevation);
            max = Mathf.Max(max, td.elevation);
        }

        float span = Mathf.Max(0.01f, max - min);

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            var td = tileSystem.GetTileData(cell.CampaignTileIndex);
            if (td == null)
            {
                cell.ElevationLevel = (int)BattleElevationLevel.Level;
                continue;
            }

            float norm = (td.elevation - min) / span;
            int level = norm < 0.2f ? 0 : norm < 0.5f ? 1 : norm < 0.8f ? 2 : 3;
            if (td.isMountain)
                level = 3;
            else if (td.isHill)
                level = Mathf.Max(level, 2);

            cell.ElevationLevel = Mathf.Clamp(level, 0, 3);
        }

        ApplyCliffEdges(map);
    }

    private static void ApplyCliffEdges(BattleMap map)
    {
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            if (cell.NeighborIndices == null)
                continue;

            for (int n = 0; n < cell.NeighborIndices.Length; n++)
            {
                int neighIdx = cell.NeighborIndices[n];
                var neigh = map.GetCell(neighIdx);
                if (neigh == null)
                    continue;

                int delta = Mathf.Abs(cell.ElevationLevel - neigh.ElevationLevel);
                cell.SetCliffTowardNeighbor(neighIdx, delta >= 2);
            }
        }
    }
}
