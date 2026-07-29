using System.Collections.Generic;
using UnityEngine;

public sealed class BattlePathfinder
{
    private readonly BattleRuleset ruleset;

    public BattlePathfinder(BattleRuleset ruleset)
    {
        this.ruleset = ruleset;
    }

    public bool TryFindPath(BattleSession session, BattleUnitState unit, int destination, BattleOccupancy occupancy, out List<int> path, out int totalCost)
    {
        path = null;
        totalCost = 0;

        if (session == null || unit == null || occupancy == null)
            return false;

        if (unit.CellIndex < 0 || destination < 0)
            return false;

        if (unit.CellIndex == destination)
        {
            path = new List<int> { destination };
            return true;
        }

        var dist = new Dictionary<int, int>();
        var prev = new Dictionary<int, int>();
        var open = new List<int> { unit.CellIndex };
        dist[unit.CellIndex] = 0;

        while (open.Count > 0)
        {
            int current = ExtractLowest(session, open, dist, destination);
            if (current == destination)
                break;

            var cell = session.Map.GetCell(current);
            if (cell?.NeighborIndices == null)
                continue;

            for (int i = 0; i < cell.NeighborIndices.Length; i++)
            {
                int n = cell.NeighborIndices[i];
                var targetCell = session.Map.GetCell(n);
                if (targetCell == null || !targetCell.IsPassable)
                    continue;

                if (targetCell.IsWater)
                    continue;

                if (occupancy.IsOccupied(n) && n != destination)
                    continue;

                int stepCost = ComputeStepCost(cell, targetCell, unit, session);
                if (stepCost >= 99)
                    continue;

                int cand = dist[current] + stepCost;
                if (!dist.TryGetValue(n, out int best) || cand < best)
                {
                    dist[n] = cand;
                    prev[n] = current;
                    if (!open.Contains(n))
                        open.Add(n);
                }
            }
        }

        if (!dist.ContainsKey(destination))
            return false;

        totalCost = dist[destination];
        var reverse = new List<int>();
        int cursor = destination;
        reverse.Add(cursor);

        while (cursor != unit.CellIndex)
        {
            if (!prev.TryGetValue(cursor, out int p))
                return false;

            cursor = p;
            reverse.Add(cursor);
        }

        reverse.Reverse();
        path = reverse;
        return true;
    }

    private int ComputeStepCost(BattleCell from, BattleCell to, BattleUnitState unit, BattleSession session)
    {
        int cost = 1;

        int delta = to.ElevationLevel - from.ElevationLevel;
        if (delta >= ruleset.cliffDeltaThreshold && !(unit.Snapshot?.TacticalProfile?.canCrossCliffs ?? false))
            return 999;

        if (delta > 0)
            cost += ruleset.uphillCost;

        if (to.HasRiver && !(unit.Snapshot?.TacticalProfile?.ignoresRiverPenalty ?? false))
            cost += ruleset.riverEnterCost;

        if (to.IsForest && !(unit.Snapshot?.TacticalProfile?.ignoresForestMovementPenalty ?? false))
        {
            var role = unit.Snapshot?.TacticalProfile != null ? unit.Snapshot.TacticalProfile.role : BattleRole.LineInfantry;
            cost += role == BattleRole.HeavyInfantry || role == BattleRole.Cavalry ? ruleset.forestMoveCostHeavy : ruleset.forestMoveCostDefault;
        }

        if (!BattleZoneOfControl.IgnoresZoc(unit) && BattleZoneOfControl.IsEnemyZocCell(session, unit, to.BattleIndex))
            cost += 99;

        return cost;
    }

    private static int ExtractLowest(BattleSession session, List<int> open, Dictionary<int, int> dist, int destination)
    {
        int bestIdx = 0;
        int bestTile = open[0];
        int bestScore = dist[bestTile] + HexDistanceHeuristic(session, bestTile, destination);

        for (int i = 1; i < open.Count; i++)
        {
            int tile = open[i];
            int score = dist[tile] + HexDistanceHeuristic(session, tile, destination);
            if (score < bestScore || (score == bestScore && tile < bestTile))
            {
                bestIdx = i;
                bestTile = tile;
                bestScore = score;
            }
        }

        open.RemoveAt(bestIdx);
        return bestTile;
    }

    private static int HexDistanceHeuristic(BattleSession session, int a, int b)
    {
        var map = session?.Map;
        var from = map?.GetCell(a);
        var to = map?.GetCell(b);
        if (from == null || to == null)
            return Mathf.Abs(a - b);

        var ts = TileSystem.GetForPlanet(session.PlanetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady())
            return Mathf.Abs(a - b);

        int wrapped = ts.GetWrappedHexDistance(from.CampaignTileIndex, to.CampaignTileIndex);
        if (wrapped < 0)
            return Mathf.Abs(a - b);

        return wrapped;
    }
}
