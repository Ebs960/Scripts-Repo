using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages per-tile multi-layer occupancy while remaining compatible with legacy HexTileData.occupantId.
/// - Surface layer mirrors HexTileData.occupantId for compatibility.
/// - Other layers are stored separately.
/// </summary>
public class TileOccupancyManager : MonoBehaviour
{
    public static TileOccupancyManager Instance { get; private set; }

    [Header("Debug")]
    [Tooltip("When true, log when code falls back to legacy HexTileData.occupantId instead of the occupancy manager")]
    public bool logLegacyFallbacks = false;

    private int tileCount;
    // occupants[tile][layer] => instance id (0 = none)
    private int[,] occupants;
    // One-time warning flags to avoid log spam
    private bool warnedNotInitialized = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        // Enable fallback logging in editor and development builds to aid migration
        try { if (Debug.isDebugBuild) logLegacyFallbacks = true; } catch { }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        occupants = null;
    }

    public void Initialize(int tileCount)
    {
        this.tileCount = tileCount;
        occupants = new int[tileCount, 4];
    }

    public void MigrateLegacyOccupants(HexTileData[] tiles)
    {
        if (tiles == null || occupants == null) return;
        int len = Math.Min(tiles.Length, tileCount);
        for (int i = 0; i < len; i++)
        {
            if (tiles[i] != null && tiles[i].occupantId != 0)
            {
                occupants[i, (int)TileLayer.Surface] = tiles[i].occupantId;
            }
        }
    }

    public int GetOccupantId(int tile, TileLayer layer)
    {
        if (!ValidIndex(tile)) return 0;
        return occupants[tile, (int)layer];
    }

    public GameObject GetOccupantObject(int tile, TileLayer layer)
    {
        int id = GetOccupantId(tile, layer);
        if (id == 0) return null;
        return UnitRegistry.GetObject(id);
    }

    /// <summary>
    /// Get occupant object for a given tile & layer, falling back to legacy HexTileData.occupantId
    /// if the occupancy manager has no record. When fallback logging is enabled this will
    /// emit a warning showing the tile and layer so the code path can be migrated.
    /// </summary>
    public GameObject GetOccupantObjectWithFallback(int tile, TileLayer layer)
    {
        // First, if we have a valid index and the occupancy manager knows about it, return that
        if (ValidIndex(tile))
        {
            var obj = GetOccupantObject(tile, layer);
            if (obj != null) return obj;
        }

        // Legacy fallback: check HexTileData.occupantId so older code still works
        var ts = TileSystem.Instance;
        if (ts != null)
        {
            var td = ts.GetTileData(tile);
            if (td != null && td.occupantId != 0)
            {
                if (logLegacyFallbacks)
                {
                    Debug.LogWarning($"[TileOccupancyManager] Legacy fallback used for tile={tile}, layer={layer}. Found occupantId={td.occupantId}. Please migrate callers to use the occupancy manager.");
                }
                return UnitRegistry.GetObject(td.occupantId);
            }
        }

        return null;
    }

    /// <summary>
    /// Static helper that returns the occupant GameObject for a tile/layer, searching the
    /// occupancy manager first (if present) and falling back to per-planet HexTileData
    /// `occupantId` when necessary. When a `planetIndex` is provided, the helper will
    /// attempt to read the backing PlanetGenerator for that planet (useful for multi-planet scans).
    /// </summary>
    public static GameObject GetOccupantObjectForTileWithFallback(int tile, TileLayer layer, int planetIndex = -1)
    {
        // If occupancy manager instance exists, prefer it (it also mirrors Surface -> HexTileData)
        if (Instance != null)
        {
            try
            {
                var obj = Instance.GetOccupantObjectWithFallback(tile, layer);
                if (obj != null) return obj;
            }
            catch { /* ignore and try planet-level fallback */ }
        }

        // If a specific planet is requested, try to query its PlanetGenerator's HexTileData
        if (planetIndex >= 0 && GameManager.Instance != null)
        {
            var pg = GameManager.Instance.GetPlanetGenerator(planetIndex) ?? GameManager.Instance.planetGenerator;
            if (pg != null)
            {
                var td = pg.GetHexTileData(tile);
                if (td != null && td.occupantId != 0) return UnitRegistry.GetObject(td.occupantId);
            }
        }

        // Fallback to current TileSystem tile data (covers single-planet or current-planet cases)
        var ts = TileSystem.Instance;
        if (ts != null)
        {
            var td = ts.GetTileData(tile);
            if (td != null && td.occupantId != 0) return UnitRegistry.GetObject(td.occupantId);
        }

        return null;
    }

    // Convenience: get surface occupant object (legacy common case)
    public GameObject GetSurfaceOccupantObject(int tile)
    {
        return GetOccupantObject(tile, TileLayer.Surface);
    }

    // Try get any occupant object on any layer; prefers Surface, then Underwater, then Atmosphere, then Orbit
    public GameObject TryGetAnyOccupantObject(int tile)
    {
        if (!ValidIndex(tile)) return null;
        GameObject obj = GetOccupantObject(tile, TileLayer.Surface);
        if (obj != null) return obj;
        obj = GetOccupantObject(tile, TileLayer.Underwater);
        if (obj != null) return obj;
        obj = GetOccupantObject(tile, TileLayer.Atmosphere);
        if (obj != null) return obj;
        return GetOccupantObject(tile, TileLayer.Orbit);
    }

    // Try get occupant object on specific layer, return true if found
    public bool TryGetOccupantObject(int tile, TileLayer layer, out GameObject obj)
    {
        obj = GetOccupantObject(tile, layer);
        return obj != null;
    }

    public void SetOccupant(int tile, GameObject occupant, TileLayer layer)
    {
        if (!ValidIndex(tile)) return;
        int id = occupant != null ? occupant.GetInstanceID() : 0;
        occupants[tile, (int)layer] = id;

        // Keep legacy HexTileData.occupantId in sync for Surface layer (compatibility)
        if (layer == TileLayer.Surface)
        {
            var ts = TileSystem.Instance;
            if (ts != null)
            {
                var td = ts.GetTileData(tile);
                if (td != null)
                {
                    td.occupantId = id;
                    ts.SetTileData(tile, td);
                }
            }
        }
    }

    public void ClearOccupant(int tile, TileLayer layer)
    {
        SetOccupant(tile, null, layer);
    }

    private bool ValidIndex(int tile)
    {
        if (occupants == null)
        {
            if (!warnedNotInitialized)
            {
                Debug.LogWarning("[TileOccupancyManager] Occupancy manager not initialized. Call TileOccupancyManager.Instance.Initialize(tileCount) after planet/grid generation so occupancy lookups don't fall back to legacy HexTileData.occupantId.");
                warnedNotInitialized = true;
            }
            return false;
        }
        if (tile < 0 || tile >= tileCount)
        {
            return false;
        }
        return true;
    }
}
