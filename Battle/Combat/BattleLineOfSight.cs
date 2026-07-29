using System.Collections.Generic;
using UnityEngine;

public sealed class BattleLineOfSight
{
    public bool HasLineOfSight(BattleSession session, BattleUnitState attacker, BattleUnitState defender, out BattleLosBlockReason reason)
    {
        reason = BattleLosBlockReason.None;
        if (session == null || attacker == null || defender == null)
        {
            reason = BattleLosBlockReason.InvalidTarget;
            return false;
        }

        float range = Mathf.Max(3f, attacker.Snapshot.Range);
        int dist = session.MapDistance(attacker.CellIndex, defender.CellIndex);
        if (dist > Mathf.FloorToInt(range))
        {
            reason = BattleLosBlockReason.OutOfRange;
            return false;
        }

        if (dist <= 1)
            return true;

        var line = BuildLine(session, attacker.CellIndex, defender.CellIndex);
        for (int i = 1; i < line.Count - 1; i++)
        {
            var cell = session.Map.GetCell(line[i]);
            if (cell == null)
                continue;

            if (cell.IsForest)
            {
                reason = BattleLosBlockReason.BlockedByForest;
                return false;
            }

            if (cell.HasHardCover)
            {
                reason = BattleLosBlockReason.BlockedByStructure;
                return false;
            }

            if (cell.ElevationLevel >= 3)
            {
                reason = BattleLosBlockReason.BlockedByElevation;
                return false;
            }
        }

        return true;
    }

    private static List<int> BuildLine(BattleSession session, int start, int end)
    {
        var map = session?.Map;
        var result = new List<int> { start };
        if (map == null || start == end)
            return result;

        var startCell = map.GetCell(start);
        var endCell = map.GetCell(end);
        if (startCell != null && endCell != null
            && TryBuildCubeLineCampaignTiles(session, startCell.CampaignTileIndex, endCell.CampaignTileIndex, out var campaignLine)
            && campaignLine.Count >= 2)
        {
            var cubeLine = new List<int>(campaignLine.Count);
            for (int i = 0; i < campaignLine.Count; i++)
            {
                if (!map.TryGetBattleIndexForCampaignTile(campaignLine[i], out int battleIndex))
                    return BuildNeighborFallback(session, start, end);

                if (cubeLine.Count == 0 || cubeLine[cubeLine.Count - 1] != battleIndex)
                    cubeLine.Add(battleIndex);
            }

            if (cubeLine.Count >= 2 && cubeLine[cubeLine.Count - 1] == end)
                return cubeLine;
        }

        return BuildNeighborFallback(session, start, end);
    }

    private static List<int> BuildNeighborFallback(BattleSession session, int start, int end)
    {
        var map = session?.Map;
        var result = new List<int> { start };
        if (map == null || start == end)
            return result;

        var startCell = map.GetCell(start);
        var endCell = map.GetCell(end);
        var ts = TileSystem.GetForPlanet(session.PlanetIndex) ?? TileSystem.Instance;
        Vector3 aPos = startCell != null && ts != null ? ts.GetTileCenterFlat(startCell.CampaignTileIndex) : Vector3.zero;
        Vector3 bPos = endCell != null && ts != null ? ts.GetTileCenterFlat(endCell.CampaignTileIndex) : Vector3.zero;

        int current = start;
        var visited = new HashSet<int> { start };

        while (current != end)
        {
            var cell = map.GetCell(current);
            if (cell?.NeighborIndices == null || cell.NeighborIndices.Length == 0)
                break;

            int best = -1;
            int bestScore = int.MaxValue;

            for (int i = 0; i < cell.NeighborIndices.Length; i++)
            {
                int n = cell.NeighborIndices[i];
                if (visited.Contains(n))
                    continue;

                int score = BuildNeighborScore(session, n, end, ts, aPos, bPos);
                if (score < bestScore || (score == bestScore && n < best))
                {
                    best = n;
                    bestScore = score;
                }
            }

            if (best < 0)
                break;

            visited.Add(best);
            current = best;
            result.Add(current);

            if (result.Count > 256)
                break;
        }

        if (result[result.Count - 1] != end)
            result.Add(end);

        return result;
    }

    private static bool TryBuildCubeLineCampaignTiles(BattleSession session, int startCampaignTile, int endCampaignTile, out List<int> line)
    {
        line = null;

        var gm = GameManager.Instance;
        var pg = gm != null ? gm.GetPlanetGenerator(session.PlanetIndex) : null;
        var grid = pg != null ? pg.Grid : null;
        if (grid == null || !grid.IsBuilt || grid.Width <= 0 || grid.Height <= 0)
            return false;

        int width = grid.Width;
        int height = grid.Height;
        int tileCount = grid.TileCount;

        if (startCampaignTile < 0 || endCampaignTile < 0 || startCampaignTile >= tileCount || endCampaignTile >= tileCount)
            return false;

        int startRow = startCampaignTile / width;
        int startCol = startCampaignTile % width;
        int endRow = endCampaignTile / width;
        int endCol = endCampaignTile % width;

        var a = OddRToCube(startRow, startCol);
        var b = SelectWrappedTargetCube(a, endRow, endCol, width);
        int dist = CubeDistance(a, b);

        if (dist <= 0)
        {
            line = new List<int> { startCampaignTile };
            return true;
        }

        var result = new List<int>(dist + 1);
        for (int i = 0; i <= dist; i++)
        {
            float t = (float)i / dist;
            var c = CubeRound(CubeLerp(a, b, t));
            int row = c.z;
            int col = c.x + ((row - (row & 1)) / 2);
            col = Mod(col, width);

            if (row < 0 || row >= height)
                continue;

            int tile = row * width + col;
            if (result.Count == 0 || result[result.Count - 1] != tile)
                result.Add(tile);
        }

        if (result.Count == 0)
            return false;

        if (result[0] != startCampaignTile)
            result.Insert(0, startCampaignTile);
        if (result[result.Count - 1] != endCampaignTile)
            result.Add(endCampaignTile);

        line = result;
        return true;
    }

    private static Vector3Int SelectWrappedTargetCube(Vector3Int fromCube, int targetRow, int targetCol, int width)
    {
        var best = OddRToCube(targetRow, targetCol);
        int bestDistance = CubeDistance(fromCube, best);

        for (int wrapOffset = -1; wrapOffset <= 1; wrapOffset++)
        {
            var candidate = OddRToCube(targetRow, targetCol + wrapOffset * width);
            int distance = CubeDistance(fromCube, candidate);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static Vector3Int OddRToCube(int row, int col)
    {
        int x = col - ((row - (row & 1)) / 2);
        int z = row;
        int y = -x - z;
        return new Vector3Int(x, y, z);
    }

    private static int CubeDistance(Vector3Int a, Vector3Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        int dz = Mathf.Abs(a.z - b.z);
        return Mathf.Max(dx, Mathf.Max(dy, dz));
    }

    private static Vector3 CubeLerp(Vector3Int a, Vector3Int b, float t)
    {
        return new Vector3(
            Mathf.Lerp(a.x, b.x, t),
            Mathf.Lerp(a.y, b.y, t),
            Mathf.Lerp(a.z, b.z, t));
    }

    private static Vector3Int CubeRound(Vector3 cube)
    {
        int rx = Mathf.RoundToInt(cube.x);
        int ry = Mathf.RoundToInt(cube.y);
        int rz = Mathf.RoundToInt(cube.z);

        float xDiff = Mathf.Abs(rx - cube.x);
        float yDiff = Mathf.Abs(ry - cube.y);
        float zDiff = Mathf.Abs(rz - cube.z);

        if (xDiff > yDiff && xDiff > zDiff)
            rx = -ry - rz;
        else if (yDiff > zDiff)
            ry = -rx - rz;
        else
            rz = -rx - ry;

        return new Vector3Int(rx, ry, rz);
    }

    private static int Mod(int value, int modulo)
    {
        int r = value % modulo;
        return r < 0 ? r + modulo : r;
    }

    private static int BuildNeighborScore(BattleSession session, int candidateCellIndex, int endCellIndex, TileSystem ts, Vector3 lineStart, Vector3 lineEnd)
    {
        var map = session?.Map;
        var candidate = map?.GetCell(candidateCellIndex);
        var end = map?.GetCell(endCellIndex);
        if (candidate == null || end == null)
            return int.MaxValue / 4;

        int wrapped = int.MaxValue / 8;
        if (ts != null && ts.IsReady())
        {
            wrapped = ts.GetWrappedHexDistance(candidate.CampaignTileIndex, end.CampaignTileIndex);
            if (wrapped < 0)
                wrapped = int.MaxValue / 8;
        }

        // Tie-break toward cells nearest the direct segment in world space.
        int linePenalty = 0;
        if (ts != null)
        {
            Vector3 p = ts.GetTileCenterFlat(candidate.CampaignTileIndex);
            float distToLine = DistancePointToSegmentXZ(p, lineStart, lineEnd);
            linePenalty = Mathf.RoundToInt(distToLine * 100f);
        }

        return wrapped * 1000 + linePenalty;
    }

    private static float DistancePointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 pv = new Vector2(p.x, p.z);
        Vector2 av = new Vector2(a.x, a.z);
        Vector2 bv = new Vector2(b.x, b.z);
        Vector2 ab = bv - av;
        float mag = ab.sqrMagnitude;
        if (mag < 0.00001f)
            return Vector2.Distance(pv, av);

        float t = Mathf.Clamp01(Vector2.Dot(pv - av, ab) / mag);
        Vector2 closest = av + ab * t;
        return Vector2.Distance(pv, closest);
    }
}
