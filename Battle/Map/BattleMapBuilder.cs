using System.Collections.Generic;
using UnityEngine;

public sealed class BattleMapBuilder
{
    private readonly BattleRuleset ruleset;

    public BattleMapBuilder(BattleRuleset ruleset)
    {
        this.ruleset = ruleset;
    }

    public BattleMap Build(EngagementPreview preview)
    {
        var ts = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        if (ts == null)
            return null;

        int targetCells = ruleset.GetTargetCellCount(preview.TotalUnits);
        var selected = SelectConnectedTiles(ts, preview.AnchorTile, targetCells);
        if (selected.Count == 0)
            return null;

        var map = new BattleMap();
        var indexByCampaign = new Dictionary<int, int>(selected.Count);

        for (int i = 0; i < selected.Count; i++)
        {
            int campaignTile = selected[i];
            indexByCampaign[campaignTile] = i;
            var td = ts.GetTileData(campaignTile);

            var cell = new BattleCell
            {
                BattleIndex = i,
                CampaignTileIndex = campaignTile,
                Biome = td != null ? td.biome : Biome.Plains,
                IsPassable = td != null && td.isPassable,
                IsWater = td != null && td.IsWaterTile,
                SupportsLand = td != null && td.isPassable && !td.IsWaterTile,
                SupportsNavalSurface = td != null && td.IsWaterTile,
                SupportsUnderwater = td != null && td.IsWaterTile,
                SupportsAir = true,
                SupportsOrbit = true,
                WaterDepthLevel = td != null && td.IsWaterTile ? 1 : 0,
                IsForest = td != null && (td.biome == Biome.Temperate || td.biome == Biome.Tropical),
                HasRiver = td != null && td.isRiver,
                HasSoftCover = td != null && (td.biome == Biome.Temperate || td.biome == Biome.Tropical),
                HasHardCover = td != null && td.improvementDefenseAdd > 0,
            };

            map.AddCell(cell);
        }

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            var neighCampaign = ts.GetNeighbors(cell.CampaignTileIndex);
            var neigh = new List<int>(6);
            for (int n = 0; n < neighCampaign.Length; n++)
            {
                if (indexByCampaign.TryGetValue(neighCampaign[n], out int mapped))
                    neigh.Add(mapped);
            }

            cell.NeighborIndices = neigh.ToArray();
        }

        BattleElevationResolver.QuantizeElevations(map, ts);
        return map;
    }

    private static List<int> SelectConnectedTiles(TileSystem ts, int anchorTile, int targetCells)
    {
        var selected = new List<int>(targetCells);
        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        visited.Add(anchorTile);
        queue.Enqueue(anchorTile);

        while (queue.Count > 0 && selected.Count < targetCells)
        {
            int tile = queue.Dequeue();
            selected.Add(tile);

            var neigh = ts.GetNeighbors(tile);
            for (int i = 0; i < neigh.Length; i++)
            {
                int n = neigh[i];
                if (visited.Contains(n))
                    continue;

                var td = ts.GetTileData(n);
                if (td == null)
                    continue;

                visited.Add(n);
                queue.Enqueue(n);
            }
        }

        return selected;
    }
}
