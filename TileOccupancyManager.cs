using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages per-tile multi-layer occupancy. This is the SINGLE SOURCE OF TRUTH for all occupancy.
/// Supports 4 layers: Surface, Underwater, Atmosphere, Orbit.
/// 
/// HexTileData.occupantId is DEPRECATED - do not use it. All occupancy queries should go through
/// this manager via GetOccupantId/GetOccupantObject/SetOccupant/ClearOccupant.
/// </summary>
public class TileOccupancyManager : MonoBehaviour
{
    // Per-planet occupancy (required for true multi-planet gameplay).
    private static readonly Dictionary<int, TileOccupancyManager> _byPlanetIndex = new();

    /// <summary>
    /// Convenience accessor for the *current planet's* occupancy manager.
    /// For multi-planet logic, prefer GetForPlanet(planetIndex).
    /// </summary>
    public static TileOccupancyManager Instance => GetForPlanet((GameManager.Instance != null) ? GameManager.Instance.currentPlanetIndex : 0);

    public static TileOccupancyManager GetForPlanet(int planetIndex)
    {
        _byPlanetIndex.TryGetValue(planetIndex, out var om);
        return om;
    }

    [Header("Debug")]
    [Tooltip("Enable verbose logging for occupancy operations")]
    public bool verboseLogging = false;

    private int tileCount;
    // occupants[tile][layer] => instance id (0 = none)
    private int[,] occupants;
    // One-time warning flags to avoid log spam
    private bool warnedNotInitialized = false;

    [Tooltip("Which planet this occupancy manager belongs to.")]
    [SerializeField] public int planetIndex = -1;

    void Awake()
    {
        // Register only if planetIndex is already known (scene-placed managers).
        // Runtime-created managers set planetIndex before Initialize() and register there.
        if (planetIndex >= 0)
        {
            if (_byPlanetIndex.TryGetValue(planetIndex, out var existing) && existing != null && existing != this)
            {
                Debug.LogWarning($"[TileOccupancyManager] Duplicate occupancy manager detected for planetIndex={planetIndex}. Keeping '{existing.name}', destroying '{name}'.");
                Destroy(gameObject);
                return;
            }
            _byPlanetIndex[planetIndex] = this;
        }
        // Enable verbose logging in editor and development builds
        try { if (Debug.isDebugBuild) verboseLogging = false; } catch { }
    }

    void OnDestroy()
    {
        if (_byPlanetIndex.TryGetValue(planetIndex, out var existing) && existing == this)
        {
            _byPlanetIndex.Remove(planetIndex);
        }
        occupants = null;
    }

    public void Initialize(int tileCount)
    {
        // Ensure we're registered for this planet (planetIndex is assigned by the owning TileSystem).
        if (planetIndex >= 0)
        {
            if (_byPlanetIndex.TryGetValue(planetIndex, out var existing) && existing != null && existing != this)
            {
                Debug.LogWarning($"[TileOccupancyManager] Initialize found existing occupancy manager for planetIndex={planetIndex}. Will initialize existing '{existing.name}' and destroy duplicate '{name}'.");
                // Ensure the existing manager is initialized.
                if (existing.occupants == null || existing.tileCount != tileCount)
                {
                    existing.Initialize(tileCount);
                }
                Destroy(gameObject);
                return;
            }
            _byPlanetIndex[planetIndex] = this;
        }
        this.tileCount = tileCount;
        occupants = new int[tileCount, 4];
    }

    /// <summary>
    /// LEGACY: Migrate old HexTileData.occupantId values into the occupancy manager.
    /// Only needed for loading old save files that used the deprecated occupantId field.
    /// New code should NOT rely on this - units should re-register via SetOccupant on load.
    /// </summary>
    [Obsolete("Legacy migration for old saves. Units should register via SetOccupant on load.")]
    public void MigrateLegacyOccupants(HexTileData[] tiles)
    {
        if (tiles == null || occupants == null) return;
        int len = Math.Min(tiles.Length, tileCount);
        int migrated = 0;
        for (int i = 0; i < len; i++)
        {
            #pragma warning disable 612, 618  // Suppress obsolete warning for occupantId
            if (tiles[i] != null && tiles[i].occupantId != 0)
            {
                occupants[i, (int)TileLayer.Surface] = tiles[i].occupantId;
                migrated++;
            }
            #pragma warning restore 612, 618
        }
        if (migrated > 0 && verboseLogging)
        {
            Debug.Log($"[TileOccupancyManager] Migrated {migrated} legacy occupants from HexTileData.occupantId");
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
    /// Get occupant object for a given tile & layer.
    /// This is the authoritative source for occupancy - no fallback to legacy fields.
    /// </summary>
    public GameObject GetOccupantObjectWithFallback(int tile, TileLayer layer)
    {
        // TileOccupancyManager is the single source of truth - no fallback needed
        return GetOccupantObject(tile, layer);
    }

    /// <summary>
    /// Static helper that returns the occupant GameObject for a tile/layer.
    /// Uses the occupancy manager for the specified planet (or current planet if not specified).
    /// This is the authoritative source for occupancy - no fallback to legacy HexTileData.occupantId.
    /// </summary>
    public static GameObject GetOccupantObjectForTileWithFallback(int tile, TileLayer layer, int planetIndex = -1)
    {
        // Get the appropriate occupancy manager
        var om = (planetIndex >= 0) ? GetForPlanet(planetIndex) : Instance;
        
        if (om != null)
        {
            return om.GetOccupantObject(tile, layer);
        }

        // No occupancy manager available - this shouldn't happen in normal gameplay
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

    /// <summary>
    /// Strict occupancy setter. Returns false instead of silently overwriting a different occupant.
    /// </summary>
    public bool TrySetOccupant(int tile, GameObject occupant, TileLayer layer, bool allowOverwrite = false, string reason = null)
    {
        if (!ValidIndex(tile)) return false;

        int layerIdx = (int)layer;
        int existingId = occupants[tile, layerIdx];
        int id = occupant != null ? occupant.GetInstanceID() : 0;

        if (occupant != null && existingId != 0 && existingId != id)
        {
            var existingObj = UnitRegistry.GetObject(existingId);
            string existingName = existingObj != null ? existingObj.name : $"id={existingId}";

            if (!allowOverwrite)
            {
                Debug.LogWarning($"[TileOccupancyManager] TrySetOccupant rejected tile={tile} layer={layer}: existing occupant '{existingName}' blocks '{occupant.name}'.");
                return false;
            }

            string overwriteReason = string.IsNullOrWhiteSpace(reason) ? "no reason provided" : reason;
            Debug.LogWarning($"[TileOccupancyManager] TrySetOccupant forced overwrite tile={tile} layer={layer}: replacing '{existingName}' with '{occupant.name}'. reason={overwriteReason}");
        }

        occupants[tile, layerIdx] = id;

        if (verboseLogging)
        {
            string name = occupant != null ? occupant.name : "null";
            Debug.Log($"[TileOccupancyManager] SetOccupant tile={tile} layer={layer} occupant={name} id={id}");
        }

        return true;
    }

    /// <summary>
    /// Set the occupant for a tile on a specific layer.
    /// This is the ONLY way to set occupancy - do not write to HexTileData.occupantId directly.
    /// Conflicting writes fail closed instead of overwriting another occupant.
    /// </summary>
    public void SetOccupant(int tile, GameObject occupant, TileLayer layer)
    {
        TrySetOccupant(tile, occupant, layer);
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
                Debug.LogWarning("[TileOccupancyManager] Occupancy manager not initialized. Call Initialize(tileCount) after planet/grid generation.");
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
