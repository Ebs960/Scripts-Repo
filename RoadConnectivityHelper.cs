using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Utility for finding cities connected by roads (improvements with isRoad=true) and aggregating their bonuses.
/// </summary>
public static class RoadConnectivityHelper
{
    // Simple cache: key = city center tile index, value = (version, TileYield)
    private static readonly System.Collections.Generic.Dictionary<int, (int version, TileYield yield)> _cache = new System.Collections.Generic.Dictionary<int, (int, TileYield)>();

    /// <summary>
    /// Find all cities connected to the given city via contiguous road tiles (isRoad=true).
    /// Returns a list of connected City objects excluding the source city.
    /// </summary>
    public static List<City> FindConnectedCities(City sourceCity)
    {
        var result = new List<City>();
        if (sourceCity == null || sourceCity.owner == null) return result;
        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        // Start BFS only from adjacent road tiles (roads must touch the city territory)
        var neighborTiles = TileSystem.Instance.GetNeighbors(sourceCity.centerTileIndex);
        foreach (int t in neighborTiles)
        {
            var td = TileSystem.Instance.GetTileData(t);
            if (td == null) continue;
            if (td.improvement != null && td.improvement.isRoad)
            {
                queue.Enqueue(t);
                visited.Add(t);
            }
        }

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            var td = TileSystem.Instance.GetTileData(idx);
            if (td == null) continue;

            // If the tile has a city that's not the source, add it
            if (td.controllingCity != null && td.controllingCity != sourceCity)
            {
                if (!result.Contains(td.controllingCity)) result.Add(td.controllingCity);
            }

            // Explore neighbors that are roads
            foreach (int n in TileSystem.Instance.GetNeighbors(idx))
            {
                if (visited.Contains(n)) continue;
                var nd = TileSystem.Instance.GetTileData(n);
                if (nd == null) continue;
                if (nd.improvement != null && nd.improvement.isRoad)
                {
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Aggregate the connected bonuses to apply to a city given all connecting road tiles between source and target.
    /// For simplicity, this sums the connected*PerTurn fields from the contiguous road tiles adjacent to the city.
    /// </summary>
    public static TileYield AggregateConnectedBonusesForCity(City city)
    {
        var total = new TileYield();
        if (city == null) return total;

        int currentVersion = ImprovementManager.Instance != null ? ImprovementManager.Instance.roadNetworkVersion : 0;
        if (_cache.TryGetValue(city.centerTileIndex, out var cached))
        {
            if (cached.version == currentVersion)
                return cached.yield;
        }

        // We will perform a BFS from road tiles adjacent to the city and compute, for each reachable city,
        // the best bottleneck (min along path) of connected*PerTurn values for that path. Then sum those per-connection yields.

    var neighborTiles = TileSystem.Instance.GetNeighbors(city.centerTileIndex);
        var queue = new Queue<int>();

        // Track best known bottleneck sum metric for a tile to avoid reprocessing poor paths
        var bestMetric = new System.Collections.Generic.Dictionary<int, long>();

        // Initialize queue with adjacent road tiles, each with initial bottleneck equal to that tile's configured connection yield
        foreach (int t in neighborTiles)
        {
            var td = TileSystem.Instance.GetTileData(t);
            if (td == null || td.improvement == null) continue;
            var imp = td.improvement;
            if (!imp.isRoad) continue;

            var cfgYield = ImprovementManager.Instance != null ? ImprovementManager.Instance.GetConnectionYieldForImprovement(imp) : new TileYield();
            queue.Enqueue(t);
            long metric = (long)cfgYield.Gold + cfgYield.Production + cfgYield.Science + cfgYield.Culture + cfgYield.Faith + cfgYield.Policy;
            bestMetric[t] = metric;
        }

        // For tracking connected cities and their best bottleneck yields
        var bestCityYields = new System.Collections.Generic.Dictionary<City, TileYield>();

        // We also need to remember, for each tile during BFS, the current bottleneck yields arriving to it.
        var tileBottlenecks = new System.Collections.Generic.Dictionary<int, TileYield>();

        // Initialize tileBottlenecks from starting tiles using configured yields
        foreach (var kv in bestMetric)
        {
            int t = kv.Key;
            var td = TileSystem.Instance.GetTileData(t);
            var imp = td.improvement;
            var by = ImprovementManager.Instance != null ? ImprovementManager.Instance.GetConnectionYieldForImprovement(imp) : new TileYield();
            tileBottlenecks[t] = by;
        }

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            var td = TileSystem.Instance.GetTileData(idx);
            if (td == null) continue;

            // If this tile has an adjacent city that's not the source, record the current bottleneck as a candidate
            if (td.controllingCity != null && td.controllingCity != city)
            {
                var cur = tileBottlenecks.ContainsKey(idx) ? tileBottlenecks[idx] : new TileYield();
                if (!bestCityYields.ContainsKey(td.controllingCity))
                {
                    bestCityYields[td.controllingCity] = cur;
                }
                else
                {
                    // Choose the connection with the higher summed metric
                    var existing = bestCityYields[td.controllingCity];
                    long existingMetric = existing.Gold + existing.Production + existing.Science + existing.Culture + existing.Faith + existing.Policy;
                    long curMetric = cur.Gold + cur.Production + cur.Science + cur.Culture + cur.Faith + cur.Policy;
                    if (curMetric > existingMetric) bestCityYields[td.controllingCity] = cur;
                }
            }

            // Expand neighbors (only along road tiles)
            foreach (int n in TileSystem.Instance.GetNeighbors(idx))
            {
                var nd = TileSystem.Instance.GetTileData(n);
                if (nd == null || nd.improvement == null || !nd.improvement.isRoad) continue;

                // Compute new bottleneck as min of current bottleneck and this neighbor's configured connection yield
                TileYield incoming = tileBottlenecks.ContainsKey(idx) ? tileBottlenecks[idx] : new TileYield { Food = int.MaxValue, Production = int.MaxValue, Gold = int.MaxValue, Science = int.MaxValue, Culture = int.MaxValue, Policy = int.MaxValue, Faith = int.MaxValue };
                var imp = nd.improvement;
                var cfg = ImprovementManager.Instance != null ? ImprovementManager.Instance.GetConnectionYieldForImprovement(imp) : new TileYield();
                TileYield next = new TileYield
                {
                    Gold = Mathf.Min(incoming.Gold, cfg.Gold),
                    Production = Mathf.Min(incoming.Production, cfg.Production),
                    Science = Mathf.Min(incoming.Science, cfg.Science),
                    Culture = Mathf.Min(incoming.Culture, cfg.Culture),
                    Faith = Mathf.Min(incoming.Faith, cfg.Faith),
                    Policy = Mathf.Min(incoming.Policy, cfg.Policy)
                };

                long metric = (long)next.Gold + next.Production + next.Science + next.Culture + next.Faith + next.Policy;

                bool shouldEnqueue = false;
                if (!bestMetric.ContainsKey(n) || metric > bestMetric[n])
                {
                    bestMetric[n] = metric;
                    shouldEnqueue = true;
                }

                if (shouldEnqueue)
                {
                    tileBottlenecks[n] = next;
                    queue.Enqueue(n);
                }
            }
        }

        // Sum the best yields for each connected city (one bonus per connected city)
        foreach (var kv in bestCityYields)
        {
            var by = kv.Value;
            // If any channel remained int.MaxValue (meaning path had only one tile), clamp to 0
            if (by.Gold == int.MaxValue) by.Gold = 0;
            if (by.Production == int.MaxValue) by.Production = 0;
            if (by.Science == int.MaxValue) by.Science = 0;
            if (by.Culture == int.MaxValue) by.Culture = 0;
            if (by.Faith == int.MaxValue) by.Faith = 0;
            if (by.Policy == int.MaxValue) by.Policy = 0;

            total.Gold += by.Gold;
            total.Production += by.Production;
            total.Science += by.Science;
            total.Culture += by.Culture;
            total.Faith += by.Faith;
            total.Policy += by.Policy;
        }

        // Cache result
        if (ImprovementManager.Instance != null)
            _cache[city.centerTileIndex] = (ImprovementManager.Instance.roadNetworkVersion, total);

        return total;
    }
}
