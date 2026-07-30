using System.Collections.Generic;
using UnityEngine;

public static class BattleDeploymentBuilder
{
    public static void BuildDeploymentZones(BattleMap map, EngagementPreview preview, int depth)
    {
        if (map == null || map.CellCount == 0)
            return;

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
                assignedD++;
                continue;
            }

            break;
        }
    }

    private static bool SupportsAnyDomain(BattleCell cell) =>
        cell.SupportsLand || cell.SupportsNavalSurface || cell.SupportsUnderwater ||
        cell.SupportsAir || cell.SupportsOrbit || cell.SupportsSpace;
}
