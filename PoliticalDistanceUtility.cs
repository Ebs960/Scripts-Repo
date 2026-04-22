using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Shared helpers for political loyalty effects based on distance from a civilization's capital.
/// </summary>
public static class PoliticalDistanceUtility
{
    // Treat cross-planet links as very far away for political penalties
    // Default fallbacks used when no config asset is present in Resources
    private const int DefaultCrossPlanetDistance = 150;
    private const int DefaultMaxSearchDepth = 150;
    private const int DefaultMinDistanceThreshold = 16;
    private const float DefaultGovernorPenaltyPerTile = 0.6f;
    private const float DefaultGovernorPenaltyCap = 25f;
    private const float DefaultVassalLibertyPerTile = 0.08f;
    private const float DefaultVassalLibertyCap = 4f;

    private static PoliticalDistanceConfig _config;
    private static PoliticalDistanceConfig Config => _config ?? (_config = Resources.Load<PoliticalDistanceConfig>("PoliticalDistanceConfig"));

    /// <summary>
    /// Permanent opinion penalty for governors/lords whose domain is far from the capital.
    /// Uses the average distance of all governed cities/herds.
    /// </summary>
    public static float GetGovernorDistancePenalty(Governor governor)
    {
        if (governor == null) return 0f;

        var civ = governor.Cities.FirstOrDefault(c => c != null)?.owner
               ?? governor.Herds.FirstOrDefault(h => h != null)?.owner;
        if (civ == null) return 0f;

        civ.EnsureCapitalCity();
        var capital = civ.CapitalCity;
        if (capital == null) return 0f;

        var distances = new List<int>();

        foreach (var city in governor.Cities)
        {
            if (city == null || city.owner != civ) continue;
            distances.Add(GetDistanceBetweenHoldings(
                capital.planetIndex,
                capital.centerTileIndex,
                city.planetIndex,
                city.centerTileIndex));
        }

        foreach (var herd in governor.Herds)
        {
            if (herd == null || herd.owner != civ || herd.currentTileIndex < 0) continue;
            distances.Add(GetDistanceBetweenHoldings(
                capital.planetIndex,
                capital.centerTileIndex,
                herd.planetIndex,
                herd.currentTileIndex));
        }

        if (distances.Count == 0) return 0f;

        float averageDistance = (float)distances.Average();
        int minThreshold = Config != null ? Config.minDistanceThreshold : DefaultMinDistanceThreshold;
        float perTile = Config != null ? Config.governorPenaltyPerTile : DefaultGovernorPenaltyPerTile;
        float cap = Config != null ? Config.governorPenaltyCap : DefaultGovernorPenaltyCap;

        float excessDistance = Mathf.Max(0f, averageDistance - minThreshold);
        return Mathf.Clamp(excessDistance * perTile, 0f, cap);
    }

    /// <summary>
    /// Per-turn liberty pressure for subjects far from their overlord's capital.
    /// </summary>
    public static float GetVassalDistanceLibertyPressure(VassalContract contract)
    {
        if (contract?.overlord == null || contract.subject == null) return 0f;

        contract.overlord.EnsureCapitalCity();
        contract.subject.EnsureCapitalCity();

        var overlordCapital = contract.overlord.CapitalCity;
        var subjectCapital = contract.subject.CapitalCity;
        if (overlordCapital == null || subjectCapital == null) return 0f;

        float distance = GetDistanceBetweenHoldings(
            overlordCapital.planetIndex,
            overlordCapital.centerTileIndex,
            subjectCapital.planetIndex,
            subjectCapital.centerTileIndex);

        // Start applying vassal liberty pressure once distance exceeds 16 tiles
        int minThreshold = Config != null ? Config.minDistanceThreshold : DefaultMinDistanceThreshold;
        float perTile = Config != null ? Config.vassalLibertyPerTile : DefaultVassalLibertyPerTile;
        float cap = Config != null ? Config.vassalLibertyCap : DefaultVassalLibertyCap;

        float excessDistance = Mathf.Max(0f, distance - minThreshold);
        return Mathf.Clamp(excessDistance * perTile, 0f, cap);
    }

    private static int GetDistanceBetweenHoldings(int fromPlanet, int fromTile, int toPlanet, int toTile)
    {
        int crossPlanet = Config != null ? Config.maxCrossPlanetDistance : DefaultCrossPlanetDistance;
        int maxDepth = Config != null ? Config.maxSearchDepth : DefaultMaxSearchDepth;

        if (fromTile < 0 || toTile < 0) return crossPlanet;
        if (fromPlanet != toPlanet) return crossPlanet;
        return GetTileDistance(fromPlanet, fromTile, toTile, maxDepth);
    }

    private static int GetTileDistance(int planetIndex, int startTile, int targetTile, int maxDepth)
    {
        if (startTile == targetTile) return 0;

        var tileSystem = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        int crossPlanet = Config != null ? Config.maxCrossPlanetDistance : DefaultCrossPlanetDistance;
        if (tileSystem == null) return crossPlanet;

        var visited = new HashSet<int> { startTile };
        var frontier = new Queue<(int tile, int distance)>();
        frontier.Enqueue((startTile, 0));

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (current.distance >= maxDepth) break;

            foreach (int neighbor in tileSystem.GetNeighbors(current.tile))
            {
                if (!visited.Add(neighbor)) continue;
                if (neighbor == targetTile) return current.distance + 1;
                frontier.Enqueue((neighbor, current.distance + 1));
            }
        }

        return crossPlanet;
    }
}