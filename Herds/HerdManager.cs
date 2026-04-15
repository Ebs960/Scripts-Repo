using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry and helper methods for herd grazing / forage sharing.
/// Tracks registered herds and computes per-herd forage share from nearby tiles.
/// Uses a spatial index (planet+tile → herd list) for O(1) adjacency lookups.
/// </summary>
public class HerdManager : MonoBehaviour
{
    public static HerdManager Instance { get; private set; }

    private readonly List<Herd> _registeredHerds = new List<Herd>();

    // Spatial index: (planetIndex, tileIndex) → list of herds on that tile
    private readonly Dictionary<long, List<Herd>> _tileToHerds = new Dictionary<long, List<Herd>>();

    // Dirty flag — call MarkDirty() whenever a herd moves or is registered/unregistered
    private bool _spatialDirty = true;

    void Awake()
    {
        if (Instance == null) Instance = this; else if (Instance != this) Destroy(this);
    }

    private static long TileKey(int planetIndex, int tileIndex) => ((long)planetIndex << 32) | (uint)tileIndex;

    /// <summary>Mark the spatial index as needing rebuild (call when any herd moves).</summary>
    public void MarkDirty() { _spatialDirty = true; }

    private void RebuildSpatialIndex()
    {
        _spatialDirty = false;
        // Clear existing lists but reuse the dictionary entries
        foreach (var kv in _tileToHerds) kv.Value.Clear();

        foreach (var h in _registeredHerds)
        {
            if (h == null || h.currentTileIndex < 0) continue;
            long key = TileKey(h.planetIndex, h.currentTileIndex);
            if (!_tileToHerds.TryGetValue(key, out var list))
            {
                list = new List<Herd>(4);
                _tileToHerds[key] = list;
            }
            list.Add(h);
        }
    }

    private void EnsureSpatialIndex()
    {
        if (_spatialDirty) RebuildSpatialIndex();
    }

    public void RegisterHerd(Herd h)
    {
        if (h == null) return;
        if (!_registeredHerds.Contains(h)) _registeredHerds.Add(h);
        _spatialDirty = true;
    }

    public void UnregisterHerd(Herd h)
    {
        if (h == null) return;
        _registeredHerds.Remove(h);
        _spatialDirty = true;
    }

    /// <summary>
    /// Count how many herds are adjacent to or on the given tile (same planet).
    /// Uses spatial index for O(neighbors) lookup instead of scanning all herds.
    /// </summary>
    public int GetHerdsAdjacentCount(int planetIndex, int tileIndex)
    {
        EnsureSpatialIndex();

        int count = 0;

        // Herds on this tile
        long key = TileKey(planetIndex, tileIndex);
        if (_tileToHerds.TryGetValue(key, out var onTile)) count += onTile.Count;

        // Herds on neighbor tiles
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null)
        {
            int[] neighbors = ts.GetNeighbors(tileIndex);
            if (neighbors != null)
            {
                for (int i = 0; i < neighbors.Length; i++)
                {
                    long nKey = TileKey(planetIndex, neighbors[i]);
                    if (_tileToHerds.TryGetValue(nKey, out var onNeighbor)) count += onNeighbor.Count;
                }
            }
        }

        return count;
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

        TileYield yields;
        if (herd != null && herd.owner != null)
            yields = HexTileData.GetTotalYieldWithReligion(herd.owner, td);
        else
            yields = td.GetTotalYield();

        int tileForage = Mathf.Max(0, yields.Food);
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
