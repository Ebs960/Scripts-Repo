using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static helper providing Line-of-Sight and Zone-of-Control queries for the hex grid.
///
/// LOS: BFS ray-walk between two tiles checking if mountains or high terrain block vision.
/// ZoC: Tiles adjacent to enemy units impose additional movement cost, preventing free bypass.
/// </summary>
public static class CombatHelpers
{
    // ──────────────── Line of Sight ────────────────

    /// <summary>
    /// Check if there is an unobstructed line of sight between two tiles.
    /// Mountains block LOS. Hills provide advantage (can see over flat terrain).
    /// Units on hills can see further and over one intervening flat tile.
    /// </summary>
    public static bool HasLineOfSight(int fromTile, int toTile, int planetIndex)
    {
        if (fromTile == toTile) return true;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return true; // permissive fallback

        int distance = ts.GetWrappedHexDistance(fromTile, toTile);
        if (distance <= 1) return true; // adjacent tiles always have LOS

        var fromData = ts.GetTileData(fromTile);
        var toData = ts.GetTileData(toTile);
        if (fromData == null || toData == null) return true;

        // Walk the hex path between from and to, check intermediate tiles
        var path = GetHexLinePath(fromTile, toTile, ts);
        if (path == null || path.Count <= 2) return true; // no intermediaries

        bool attackerOnHill = fromData.isHill || fromData.elevationTier == ElevationTier.Hill
                              || fromData.elevationTier == ElevationTier.Mountain;
        bool attackerOnMountain = fromData.isMountain || fromData.elevationTier == ElevationTier.Mountain;

        int blockingCount = 0;

        // Check intermediate tiles (skip first = from, last = to)
        for (int i = 1; i < path.Count - 1; i++)
        {
            var tileData = ts.GetTileData(path[i]);
            if (tileData == null) continue;

            // Mountains always block LOS (unless attacker is also on a mountain)
            if (tileData.isMountain || tileData.elevationTier == ElevationTier.Mountain)
            {
                if (!attackerOnMountain)
                    return false;
            }

            // Hills block LOS for flat-ground attackers if there are multiple
            if (tileData.isHill || tileData.elevationTier == ElevationTier.Hill)
            {
                if (!attackerOnHill)
                {
                    blockingCount++;
                    if (blockingCount >= 2)
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Walk a hex-line from start to end, returning tile indices along the way.
    /// Uses BFS shortest-path walk (not geometric ray) to follow hex grid topology.
    /// </summary>
    private static List<int> GetHexLinePath(int from, int to, TileSystem ts)
    {
        // Simple BFS-based shortest path walk
        int maxDist = ts.GetWrappedHexDistance(from, to);
        if (maxDist <= 0) return null;

        var path = new List<int>(maxDist + 1) { from };
        int current = from;

        for (int step = 0; step < maxDist; step++)
        {
            int[] neighbors = ts.GetNeighbors(current);
            if (neighbors == null) break;

            int bestNeighbor = -1;
            int bestDist = int.MaxValue;

            foreach (int n in neighbors)
            {
                if (n < 0) continue;
                int d = ts.GetWrappedHexDistance(n, to);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestNeighbor = n;
                }
            }

            if (bestNeighbor < 0) break;
            path.Add(bestNeighbor);
            current = bestNeighbor;

            if (current == to) break;
        }

        return path;
    }

    // ──────────────── Zone of Control ────────────────

    /// <summary>
    /// Extra movement cost imposed when entering a tile adjacent to an enemy combat unit.
    /// Returns 0 if no ZoC applies, otherwise returns the additional MP penalty.
    /// </summary>
    public static int GetZoneOfControlCost(int tileIndex, BaseUnit movingUnit, int planetIndex)
    {
        if (movingUnit == null) return 0;

        // Units in orbit are not subject to ZoC
        if (movingUnit.currentLayer == TileLayer.Orbit) return 0;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return 0;

        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return 0;

        int[] neighbors = ts.GetNeighbors(tileIndex);
        if (neighbors == null) return 0;

        bool hasAdjacentEnemy = false;

        foreach (int n in neighbors)
        {
            if (n < 0) continue;
            var obj = occ.GetOccupantObjectWithFallback(n, TileLayer.Surface);
            if (obj == null) continue;

            var unit = obj.GetComponent<CombatUnit>();
            if (unit == null) continue;
            if (unit.owner == movingUnit.owner) continue;
            if (unit.currentHealth <= 0) continue;

            // Enemy combat unit adjacent — ZoC applies
            hasAdjacentEnemy = true;
            break;
        }

        return hasAdjacentEnemy ? 1 : 0; // +1 MP penalty for entering ZoC
    }

    /// <summary>
    /// Check if a unit is currently in an enemy's Zone of Control.
    /// </summary>
    public static bool IsInZoneOfControl(BaseUnit unit)
    {
        if (unit == null || unit.currentTileIndex < 0) return false;
        return GetZoneOfControlCost(unit.currentTileIndex, unit, unit.planetIndex) > 0;
    }

    // ──────────────── Morale Helpers ────────────────

    /// <summary>
    /// Count nearby allied units within a radius (for morale support calculations).
    /// </summary>
    public static int CountNearbyAllies(BaseUnit unit, int radius = 2)
    {
        if (unit == null || unit.currentTileIndex < 0) return 0;

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return 0;

        var occ = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return 0;

        int count = 0;
        var visited = new HashSet<int> { unit.currentTileIndex };
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((unit.currentTileIndex, 0));

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();

            if (dist > 0) // don't count self
            {
                var obj = occ.GetOccupantObjectWithFallback(tile, unit.currentLayer);
                if (obj != null)
                {
                    var ally = obj.GetComponent<BaseUnit>();
                    if (ally != null && ally.owner == unit.owner && ally.currentHealth > 0)
                        count++;
                }
            }

            if (dist >= radius) continue;

            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (n >= 0 && visited.Add(n))
                    queue.Enqueue((n, dist + 1));
            }
        }

        return count;
    }

    /// <summary>
    /// Count nearby enemy units within a radius (for outnumbered/morale penalty calculations).
    /// </summary>
    public static int CountNearbyEnemies(BaseUnit unit, int radius = 2)
    {
        if (unit == null || unit.currentTileIndex < 0) return 0;

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return 0;

        var occ = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return 0;

        int count = 0;
        var visited = new HashSet<int> { unit.currentTileIndex };
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((unit.currentTileIndex, 0));

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();

            if (dist > 0)
            {
                var obj = occ.GetOccupantObjectWithFallback(tile, unit.currentLayer);
                if (obj != null)
                {
                    var enemy = obj.GetComponent<BaseUnit>();
                    if (enemy != null && enemy.owner != unit.owner && enemy.currentHealth > 0)
                        count++;
                }
            }

            if (dist >= radius) continue;

            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (n >= 0 && visited.Add(n))
                    queue.Enqueue((n, dist + 1));
            }
        }

        return count;
    }
}
