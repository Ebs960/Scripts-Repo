using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry and helper methods for herd grazing / forage sharing.
/// Tracks registered herds and computes per-herd forage share from nearby tiles.
/// </summary>
public class HerdManager : MonoBehaviour
{
    public static HerdManager Instance { get; private set; }

    private readonly List<Herd> _registeredHerds = new List<Herd>();

    void Awake()
    {
        if (Instance == null) Instance = this; else if (Instance != this) Destroy(this);
    }

    public void RegisterHerd(Herd h)
    {
        if (h == null) return;
        if (!_registeredHerds.Contains(h)) _registeredHerds.Add(h);
    }

    public void UnregisterHerd(Herd h)
    {
        if (h == null) return;
        _registeredHerds.Remove(h);
    }

    /// <summary>
    /// Count how many herds are adjacent to or on the given tile (same planet).
    /// Adjacent means herd.currentTileIndex == tileIndex or herd is on a neighbor tile.
    /// </summary>
    public int GetHerdsAdjacentCount(int planetIndex, int tileIndex)
    {
        int count = 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        int[] neighbors = ts != null ? ts.GetNeighbors(tileIndex) : System.Array.Empty<int>();
        foreach (var h in _registeredHerds)
        {
            if (h == null) continue;
            if (h.planetIndex != planetIndex) continue;
            if (h.currentTileIndex == tileIndex) { count++; continue; }
            // check neighbor equality
            foreach (var n in neighbors)
            {
                if (n == h.currentTileIndex) { count++; break; }
            }
        }
        return Mathf.Max(0, count);
    }

    /// <summary>
    /// Compute forage share for a herd from a single tile. Splits tile forage among adjacent herds.
    /// Uses tile's base food yield as forage potential.
    /// </summary>
    public int ComputeHerdForageShare(int planetIndex, int tileIndex, Herd herd)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return 0;
        var td = ts.GetTileData(tileIndex);
        if (td == null) return 0;

        int tileForage = Mathf.Max(0, td.food);
        int herdCount = GetHerdsAdjacentCount(planetIndex, tileIndex);
        if (herdCount <= 0) return 0;

        return Mathf.FloorToInt((float)tileForage / herdCount);
    }

    public int GetConsumptionIntervalForBiome(Biome b)
    {
        // Temperate and Tropical: every turn. Plains and Savannah: every 4 turns.
        if (b == Biome.Temperate || b == Biome.Tropical) return 1;
        if (b == Biome.Plains || b == Biome.Savannah) return 4;
        return 1;
    }
}
