using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages per-tile multi-layer occupancy. This is the SINGLE SOURCE OF TRUTH for all occupancy.
/// Supports 4 layers: Surface, Underwater, Atmosphere, Orbit.
/// Supports up to MAX_STACK_SLOTS units per tile per layer (unit stacking, tech-gated).
/// 
/// HexTileData.occupantId is DEPRECATED - do not use it. All occupancy queries should go through
/// this manager via GetOccupantId/GetOccupantObject/SetOccupant/ClearOccupant.
/// </summary>
public class TileOccupancyManager : MonoBehaviour
{
    public const int MAX_STACK_SLOTS = 3;
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
    // occupants[tile, layer, slot] => instance id (0 = none)
    // Slot 0 = front, Slot 1 = middle, Slot 2 = rear
    private int[,,] occupants;
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
        occupants = new int[tileCount, 4, MAX_STACK_SLOTS];
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
                occupants[i, (int)TileLayer.Surface, 0] = tiles[i].occupantId;
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
        int layerIdx = (int)layer;
        // Return first non-zero slot (backward compatible: returns the primary/front occupant)
        for (int s = 0; s < MAX_STACK_SLOTS; s++)
        {
            int id = occupants[tile, layerIdx, s];
            if (id != 0) return id;
        }
        return 0;
    }

    /// <summary>
    /// Get the occupant instance ID at a specific stack slot.
    /// </summary>
    public int GetOccupantIdAtSlot(int tile, TileLayer layer, int slot)
    {
        if (!ValidIndex(tile) || slot < 0 || slot >= MAX_STACK_SLOTS) return 0;
        return occupants[tile, (int)layer, slot];
    }

    /// <summary>
    /// Get all non-zero occupant IDs on a tile/layer.
    /// </summary>
    public List<int> GetAllOccupantIds(int tile, TileLayer layer)
    {
        var result = new List<int>(MAX_STACK_SLOTS);
        if (!ValidIndex(tile)) return result;
        int layerIdx = (int)layer;
        for (int s = 0; s < MAX_STACK_SLOTS; s++)
        {
            int id = occupants[tile, layerIdx, s];
            if (id != 0) result.Add(id);
        }
        return result;
    }

    /// <summary>
    /// Get all occupant GameObjects on a tile/layer (ordered by slot: front first).
    /// </summary>
    public List<GameObject> GetAllOccupantObjects(int tile, TileLayer layer)
    {
        var ids = GetAllOccupantIds(tile, layer);
        var result = new List<GameObject>(ids.Count);
        foreach (int id in ids)
        {
            var obj = UnitRegistry.GetObject(id);
            if (obj != null) result.Add(obj);
        }
        return result;
    }

    /// <summary>
    /// How many units are on this tile/layer.
    /// </summary>
    public int GetOccupantCount(int tile, TileLayer layer)
    {
        if (!ValidIndex(tile)) return 0;
        int layerIdx = (int)layer;
        int count = 0;
        for (int s = 0; s < MAX_STACK_SLOTS; s++)
        {
            if (occupants[tile, layerIdx, s] != 0) count++;
        }
        return count;
    }

    /// <summary>
    /// Find which slot a specific occupant is in. Returns -1 if not found.
    /// </summary>
    public int GetSlotForOccupant(int tile, TileLayer layer, int instanceId)
    {
        if (!ValidIndex(tile) || instanceId == 0) return -1;
        int layerIdx = (int)layer;
        for (int s = 0; s < MAX_STACK_SLOTS; s++)
        {
            if (occupants[tile, layerIdx, s] == instanceId) return s;
        }
        return -1;
    }

    /// <summary>
    /// Check whether a unit can join the stack on this tile (has an available slot
    /// and the stack hasn't reached the civ's tech-gated max).
    /// </summary>
    public bool CanJoinStack(int tile, TileLayer layer, int maxSlots)
    {
        if (!ValidIndex(tile)) return false;
        int layerIdx = (int)layer;
        int filled = 0;
        for (int s = 0; s < MAX_STACK_SLOTS; s++)
        {
            if (occupants[tile, layerIdx, s] != 0) filled++;
        }
        return filled < Mathf.Min(maxSlots, MAX_STACK_SLOTS);
    }

    /// <summary>
    /// Try to add a unit to the stack on this tile. Finds the first empty slot.
    /// Returns the assigned slot index, or -1 if full.
    /// </summary>
    public int TryAddToStack(int tile, TileLayer layer, GameObject occupant, int maxSlots)
    {
        if (!ValidIndex(tile) || occupant == null) return -1;
        int layerIdx = (int)layer;
        int id = occupant.GetInstanceID();
        int limit = Mathf.Min(maxSlots, MAX_STACK_SLOTS);

        // Check if already present
        for (int s = 0; s < limit; s++)
        {
            if (occupants[tile, layerIdx, s] == id) return s;
        }

        // Find first empty slot
        for (int s = 0; s < limit; s++)
        {
            if (occupants[tile, layerIdx, s] == 0)
            {
                occupants[tile, layerIdx, s] = id;
                if (verboseLogging)
                    Debug.Log($"[TileOccupancyManager] TryAddToStack tile={tile} layer={layer} slot={s} occupant={occupant.name} id={id}");
                return s;
            }
        }

        if (verboseLogging)
            Debug.LogWarning($"[TileOccupancyManager] TryAddToStack FULL tile={tile} layer={layer} maxSlots={maxSlots} occupant={occupant.name}");
        return -1;
    }

    /// <summary>
    /// Remove a specific occupant from a tile by instance ID. Compacts remaining slots forward.
    /// </summary>
    public void ClearOccupantById(int tile, TileLayer layer, int instanceId)
    {
        if (!ValidIndex(tile) || instanceId == 0) return;
        int layerIdx = (int)layer;

        for (int s = 0; s < MAX_STACK_SLOTS; s++)
        {
            if (occupants[tile, layerIdx, s] == instanceId)
            {
                occupants[tile, layerIdx, s] = 0;
                // Compact: shift later slots forward to fill the gap
                for (int j = s; j < MAX_STACK_SLOTS - 1; j++)
                {
                    occupants[tile, layerIdx, j] = occupants[tile, layerIdx, j + 1];
                }
                occupants[tile, layerIdx, MAX_STACK_SLOTS - 1] = 0;

                if (verboseLogging)
                    Debug.Log($"[TileOccupancyManager] ClearOccupantById tile={tile} layer={layer} id={instanceId} slot={s}");
                return;
            }
        }
    }

    /// <summary>
    /// Swap two stack slots on a tile. Returns true if both slots were non-empty and the swap succeeded.
    /// Call BaseUnit.SnapToSlotPosition() on both units after this to update their world positions.
    /// </summary>
    public bool SwapStackSlots(int tile, TileLayer layer, int slotA, int slotB)
    {
        if (!ValidIndex(tile)) return false;
        if (slotA < 0 || slotA >= MAX_STACK_SLOTS) return false;
        if (slotB < 0 || slotB >= MAX_STACK_SLOTS) return false;
        if (slotA == slotB) return true;

        int layerIdx = (int)layer;
        int idA = occupants[tile, layerIdx, slotA];
        int idB = occupants[tile, layerIdx, slotB];

        occupants[tile, layerIdx, slotA] = idB;
        occupants[tile, layerIdx, slotB] = idA;

        if (verboseLogging)
            Debug.Log($"[TileOccupancyManager] SwapStackSlots tile={tile} layer={layer} slotA={slotA}(id={idA}) slotB={slotB}(id={idB})");

        return true;
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
    /// Strict occupancy setter (backward compatible: uses slot 0).
    /// For stack-aware placement, use TryAddToStack instead.
    /// </summary>
    public bool TrySetOccupant(int tile, GameObject occupant, TileLayer layer, bool allowOverwrite = false, string reason = null)
    {
        if (!ValidIndex(tile)) return false;

        int layerIdx = (int)layer;
        int existingId = occupants[tile, layerIdx, 0];
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

        occupants[tile, layerIdx, 0] = id;

        if (verboseLogging)
        {
            string name = occupant != null ? occupant.name : "null";
            Debug.Log($"[TileOccupancyManager] SetOccupant tile={tile} layer={layer} slot=0 occupant={name} id={id}");
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

    /// <summary>
    /// Clear slot 0 on a tile/layer (backward compatible).
    /// For stack-aware clearing, use ClearOccupantById instead.
    /// </summary>
    public void ClearOccupant(int tile, TileLayer layer)
    {
        if (!ValidIndex(tile)) return;
        occupants[tile, (int)layer, 0] = 0;
    }

    /// <summary>
    /// Clear ALL occupants from a tile/layer (all slots).
    /// </summary>
    public void ClearAllOccupants(int tile, TileLayer layer)
    {
        if (!ValidIndex(tile)) return;
        int layerIdx = (int)layer;
        for (int s = 0; s < MAX_STACK_SLOTS; s++)
            occupants[tile, layerIdx, s] = 0;
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
