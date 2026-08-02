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
        if (preview.Theater == BattleTheater.DeepSpace)
            return BuildSpaceMap(preview);
        var ts = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        if (ts == null)
            return null;

        int targetCells = ruleset.GetTargetCellCount(preview.TotalUnits, new System.Random(preview.RandomSeed));
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
                HasPort = td != null && ((td.improvement != null && td.improvement.isPort)
                    || (td.district != null && td.district.isPort)),
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

        // Beaches belong to passable coastal land cells, never arbitrary water.
        // Deep water requires a water cell surrounded predominantly by water.
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            int waterNeighbors = 0;
            for (int n = 0; n < cell.NeighborIndices.Length; n++)
                if (map.GetCell(cell.NeighborIndices[n])?.IsWater == true) waterNeighbors++;
            cell.HasBeach = cell.SupportsLand && waterNeighbors > 0;
            if (cell.IsWater)
                cell.WaterDepthLevel = waterNeighbors >= Mathf.Max(3, cell.NeighborIndices.Length - 1) ? 2 : 1;
            // A port must have actual navigable coastal access.
            if (cell.HasPort && waterNeighbors == 0) cell.HasPort = false;
        }

        BattleElevationResolver.QuantizeElevations(map, ts);
        preview.PlanetaryEnvironment = ClassifyPlanetaryEnvironment(map);
        return map;
    }

    private static PlanetaryBattleEnvironment ClassifyPlanetaryEnvironment(BattleMap map)
    {
        int water = 0;
        int land = 0;
        bool hasPort = false;
        bool hasBeach = false;
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            if (cell.IsWater) water++; else land++;
            hasPort |= cell.HasPort;
            hasBeach |= cell.HasBeach;
        }
        if (hasPort) return PlanetaryBattleEnvironment.Port;
        if (hasBeach && water > 0 && land > 0) return PlanetaryBattleEnvironment.Amphibious;
        if (water == 0) return PlanetaryBattleEnvironment.Inland;
        if (land == 0) return PlanetaryBattleEnvironment.OpenOcean;
        if (water > land * 2) return PlanetaryBattleEnvironment.Archipelago;
        if (land > water * 2) return PlanetaryBattleEnvironment.Coastal;
        return PlanetaryBattleEnvironment.Mixed;
    }

    private BattleMap BuildSpaceMap(EngagementPreview preview)
    {
        var grid = SpaceWorldManager.Instance != null ? SpaceWorldManager.Instance.Grid
            : (SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : null);
        if (grid == null || grid.GetTile(preview.AnchorTile) == null) return null;
        int target = ruleset.GetTargetCellCount(preview.TotalUnits, new System.Random(preview.RandomSeed));
        var selected = new List<int>(); var seen = new HashSet<int>(); var queue = new Queue<int>();
        seen.Add(preview.AnchorTile); queue.Enqueue(preview.AnchorTile);
        while (queue.Count > 0 && selected.Count < target)
        {
            int tile = queue.Dequeue(); selected.Add(tile);
            foreach (int n in grid.GetNeighbors(tile)) if (seen.Add(n)) queue.Enqueue(n);
        }
        var map = new BattleMap(); var indices = new Dictionary<int, int>();
        for (int i = 0; i < selected.Count; i++)
        {
            indices[selected[i]] = i; var source = grid.GetTile(selected[i]);
            map.AddCell(new BattleCell { BattleIndex = i, CampaignTileIndex = selected[i], IsPassable = !source.blocksMovement,
                SupportsSpace = !source.blocksMovement, SupportsAir = false, SupportsOrbit = false });
        }
        for (int i = 0; i < selected.Count; i++)
        {
            var neighbors = new List<int>(); foreach (int n in grid.GetNeighbors(selected[i])) if (indices.TryGetValue(n, out int mapped)) neighbors.Add(mapped);
            map.Cells[i].NeighborIndices = neighbors.ToArray();
        }
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
