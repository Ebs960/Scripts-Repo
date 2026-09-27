using System.Collections.Generic;
using UnityEngine;

public static class BattleDeploymentBuilder
{
    public static void BuildDeploymentZones(BattleMap map, EngagementPreview preview, int depth)
    {
        if (map == null || map.CellCount == 0)
            return;

        if (preview.FortificationProfile != null)
        {
            BuildSiegeDeploymentZones(map);
            AssignRetreatExits(map);
            return;
        }

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

        var scores = new List<(int cellIndex, float score)>(map.CellCount);

        for (int i = 0; i < map.Cells.Count; i++)
        {
            var c = map.Cells[i];
            Vector2 delta = new Vector2(Mathf.Sqrt(3f)*(c.LocalQ+c.LocalR*.5f), 1.5f*c.LocalR);
            float score = Vector2.Dot(delta, preview.ApproachDirectionXZ);
            scores.Add((i, score));
        }

        scores.Sort((a, b) => a.score.CompareTo(b.score));

        // Select complete local edge bands rather than an arbitrary number of campaign tiles.
        int maxDistance=0;
        foreach(var c in map.Cells) maxDistance=Mathf.Max(maxDistance,Mathf.Max(Mathf.Abs(c.LocalQ),Mathf.Max(Mathf.Abs(c.LocalR),Mathf.Abs(c.LocalS))));
        float edgeCutoff=scores[Mathf.Min(scores.Count-1, Mathf.Max(0, depth*2))].score;
        float oppositeCutoff=scores[Mathf.Max(0, scores.Count-1-Mathf.Max(0, depth*2))].score;
        int zoneCount = map.CellCount;
        int assignedA = 0;
        int assignedD = 0;

        for (int i = 0; i < scores.Count; i++)
        {
            var cell = map.Cells[scores[i].cellIndex];
            if (!SupportsAnyDomain(cell))
                continue;

            if (assignedA < zoneCount && scores[i].score <= edgeCutoff)
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

            if (assignedD < zoneCount && scores[i].score >= oppositeCutoff)
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

    private static void BuildSiegeDeploymentZones(BattleMap map)
    {
        foreach (var cell in map.Cells)
        {
            cell.DeploymentOwner = cell.IsFortifiedInterior ? BattleSide.Defender : BattleSide.Attacker;
            cell.IsReinforcementEntry = !cell.IsFortifiedInterior;
        }
        // Perimeter structures are defender positions, but never attacker deployment cells.
        foreach (var cell in map.Cells)
            if (cell.HasHardCover && cell.DeploymentOwner == BattleSide.Attacker)
            { cell.DeploymentOwner = BattleSide.Defender; cell.IsReinforcementEntry = false; }
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
            int exits = Mathf.Min(3, candidates.Count);
            for (int i = 0; i < exits; i++)
            {
                candidates[i].RetreatExitForSide = side;
                candidates[i].StrategicExitTile = candidates[i].CampaignTileIndex;
            }
        }
    }

    private static bool SupportsAnyDomain(BattleCell cell) =>
        cell.SupportsLand || cell.SupportsNavalSurface || cell.SupportsUnderwater ||
        cell.SupportsAir || cell.SupportsOrbit || cell.SupportsSpace;
}
