using System.Collections.Generic;
using UnityEngine;

public static class BattleDeploymentBuilder
{
    public static void BuildDeploymentZones(BattleMap map, EngagementPreview preview, int depth)
    {
        if (map == null || map.CellCount == 0)
            return;

        if (preview.Theater == BattleTheater.DeepSpace)
        {
            int count = Mathf.Max(1, depth * 3);
            for (int i = 0; i < map.CellCount && i < count; i++)
            {
                map.Cells[i].DeploymentOwner = BattleSide.Attacker;
                map.Cells[i].IsReinforcementEntry = true;
            }
            for (int i = map.CellCount - 1, assigned = 0; i >= 0 && assigned < count; i--)
                if (!map.Cells[i].DeploymentOwner.HasValue)
                {
                    map.Cells[i].DeploymentOwner = BattleSide.Defender;
                    map.Cells[i].IsReinforcementEntry = true;
                    assigned++;
                }
            AssignRetreatExits(map);
            return;
        }

        var ts = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        if (ts == null)
            return;

        Vector3 center = ts.GetTileCenterFlat(preview.AnchorTile);
        var scores = new List<(int cellIndex, float score)>(map.CellCount);

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var c = map.Cells[i];
            Vector3 p = ts.GetTileCenterFlat(c.CampaignTileIndex);
            Vector2 delta = new Vector2(p.x - center.x, p.z - center.z);
            float score = Vector2.Dot(delta, preview.ApproachDirectionXZ);
            scores.Add((i, score));
        }

        scores.Sort((a, b) => a.score.CompareTo(b.score));

        int zoneCount = Mathf.Max(1, depth * 3);
        int assignedA = 0;
        int assignedD = 0;

        for (int i = 0; i < scores.Count; i++)
        {
            var cell = map.Cells[scores[i].cellIndex];
            if (!SupportsAnyDomain(cell))
                continue;

            if (assignedA < zoneCount)
            {
                cell.DeploymentOwner = BattleSide.Attacker;
                cell.IsReinforcementEntry = true;
                assignedA++;
                continue;
            }

            break;
        }

        for (int i = scores.Count - 1; i >= 0; i--)
        {
            var cell = map.Cells[scores[i].cellIndex];
            if (!SupportsAnyDomain(cell) || cell.DeploymentOwner.HasValue)
                continue;

            if (assignedD < zoneCount)
            {
                cell.DeploymentOwner = BattleSide.Defender;
                cell.IsReinforcementEntry = true;
                assignedD++;
                continue;
            }

            break;
        }

        AssignRetreatExits(map);
    }

    private static void AssignRetreatExits(BattleMap map)
    {
        foreach (BattleSide side in System.Enum.GetValues(typeof(BattleSide)))
        {
            var candidates = new List<BattleCell>();
            int minimumDegree = int.MaxValue;
            for (int i = 0; i < map.Cells.Count; i++)
            {
                var cell = map.Cells[i];
                if (cell.DeploymentOwner != side) continue;
                int degree = cell.NeighborIndices?.Length ?? 0;
                if (degree < minimumDegree) { candidates.Clear(); minimumDegree = degree; }
                if (degree == minimumDegree) candidates.Add(cell);
            }
            candidates.Sort((a, b) => a.BattleIndex.CompareTo(b.BattleIndex));
            int exits = Mathf.Min(2, candidates.Count);
            for (int i = 0; i < exits; i++) candidates[i].RetreatExitForSide = side;
        }
    }

    private static bool SupportsAnyDomain(BattleCell cell) =>
        cell.SupportsLand || cell.SupportsNavalSurface || cell.SupportsUnderwater ||
        cell.SupportsAir || cell.SupportsOrbit || cell.SupportsSpace;
}
