using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Consolidated monolithic TileSystem combining ownership, fog, data access, and input event fan-out.
/// </summary>
public class TileSystem : MonoBehaviour
{
    // Per-planet TileSystem instances (true multi-planet gameplay).
    private static readonly Dictionary<int, TileSystem> _byPlanetIndex = new();

    /// <summary>
    /// Convenience accessor for the *current planet's* TileSystem.
    /// For multi-planet logic, prefer `GetForPlanet(planetIndex)`.
    /// </summary>
    public static TileSystem Instance => GetForPlanet((GameManager.Instance != null) ? GameManager.Instance.currentPlanetIndex : 0);

    public static TileSystem GetForPlanet(int planetIndex)
    {
        _byPlanetIndex.TryGetValue(planetIndex, out var ts);
        return ts;
    }

    [Header("Configuration")] public int civCapacity = 8;
    [Tooltip("Enable fog of war globally.")] public bool enableFogOfWar = true;
    [Tooltip("Local player civ for merged vision.")] public int localPlayerCivId = 0;
    [Tooltip("Allied civ ids (merged vision includes these)." )] public List<int> alliedCivs = new();
    [Tooltip("Max expected owners (defines palette size)." )] public int maxOwners = 16;

    [Header("State Arrays (public read-only accessors)")]
    [SerializeField] private HexTileData[] tiles;              // Canonical tile data array (single planet scope for now)
    [SerializeField] private int[] ownerByTile;                // -1 = neutral
    [SerializeField] private byte[][] fogByCiv;                // [civ][tile] 0/1/2
    [SerializeField] private byte[] mergedFog;                 // merged local+allies
    [SerializeField] private Color[] ownerColors;              // index=civId
    // Optional spatial data caches (will be populated when integrated)
    private Vector3[] tileCenters;                              // per-tile world centers
    private int[][] neighbors;                                  // adjacency lists
    
    // Religion storage (centralized)
    // Pressures are stored per tile as a serializable-style list equivalent for runtime
    public struct ReligionPressureEntry { public ReligionData religion; public float pressure; }
    private List<ReligionPressureEntry>[] religionPressures; // per-tile list
    private bool[] holySiteFlags;                                // per-tile holy site marker
    private DistrictData[] holySiteDistrict;                     // district placed at holy site (optional)

    [Header("Planet References")]
    [SerializeField] private PlanetGenerator planetRef;          // primary planet (single-planet scope)
    [Tooltip("Which planet this TileSystem instance belongs to.")]
    [SerializeField] public int planetIndex = -1;

    [Header("Runtime Flags")] public bool isReady;

    [Header("Diagnostics")]
    [Tooltip("Logs tile ownership changes and warns on unsafe ownership writes. Recommended only while diagnosing ownership bugs.")]
    [SerializeField] private bool debugTileOwnership = false;
    [Tooltip("Includes stack traces for unsafe ownership writes (can be noisy/slow).")]
    [SerializeField] private bool debugTileOwnershipVerbose = false;
    private bool _suppressOwnershipGuards = false;

    [Header("Input / Raycast Settings")]
    [Tooltip("Camera for raycasts (auto-detects if null)")] public Camera mainCamera;
    [Tooltip("Maximum raycast distance for tile input")] public float maxRaycastDistance = 1000f;
    [Tooltip("Layer mask used for tile raycasts")] public LayerMask tileRaycastMask = -1;
    private int lastHoveredTileIndex = -1;
    private Vector2 _lastMouseScreenPos = new Vector2(-999f, -999f);

    // Cached references for picking (avoid per-frame FindAnyObjectByType in hot path)
    private WorldPicker cachedWorldPicker;
    private HexMapChunkManager cachedChunkManager;

    // Dirty tracking
    private readonly HashSet<int> _dirtyOverlayTiles = new();

    // Reusable buffers
    private readonly List<int> _fogChangedBuffer = new(256);

    // Events
    public event Action<int,int,int> OnTileOwnerChanged;         // (tile, oldOwner, newOwner)
    public event Action<int,List<int>> OnFogChanged;             // (civId, changedTiles)
    public event Action<int,Vector3> OnTileHovered;              // (tile, worldPos)
    public event Action OnTileHoverExited;                       // hover exit
    public event Action<int,Vector3> OnTileClicked;              // (tile, worldPos)

    void Awake()
    {
        // Register only if planetIndex is already known (scene-placed TileSystems).
        // Runtime-created TileSystems set planetIndex in InitializeFromPlanet and register there.
        if (planetIndex >= 0)
        {
            if (_byPlanetIndex.TryGetValue(planetIndex, out var existing) && existing != null && existing != this)
            {
                Debug.LogWarning($"[TileSystem] Duplicate TileSystem detected for planetIndex={planetIndex}. Keeping '{existing.name}', destroying '{name}'.");
                Destroy(gameObject);
                return;
            }
            _byPlanetIndex[planetIndex] = this;
        }
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void OnDestroy()
    {
        if (_byPlanetIndex.TryGetValue(planetIndex, out var existing) && existing == this)
        {
            _byPlanetIndex.Remove(planetIndex);
            // MEMORY FIX: Clear all large arrays to help garbage collector
// Clear tile data arrays
            tiles = null;
            ownerByTile = null;
            tileCenters = null;
            ownerColors = null;
            
            // Clear fog arrays
            if (fogByCiv != null)
            {
                for (int i = 0; i < fogByCiv.Length; i++)
                {
                    fogByCiv[i] = null;
                }
                fogByCiv = null;
            }
            mergedFog = null;
            
            // Clear neighbor arrays
            if (neighbors != null)
            {
                for (int i = 0; i < neighbors.Length; i++)
                {
                    neighbors[i] = null;
                }
                neighbors = null;
            }
            
            // Clear religion data
            if (religionPressures != null)
            {
                for (int i = 0; i < religionPressures.Length; i++)
                {
                    religionPressures[i]?.Clear();
                    religionPressures[i] = null;
                }
                religionPressures = null;
            }
            holySiteFlags = null;
            holySiteDistrict = null;
            
            // Clear dirty tracking
            _dirtyOverlayTiles.Clear();
            _fogChangedBuffer.Clear();
            
            // Clear events to prevent memory leaks from subscriptions
            OnTileOwnerChanged = null;
            OnFogChanged = null;
            OnTileHovered = null;
            OnTileHoverExited = null;
            OnTileClicked = null;

            isReady = false;
        }
    }

    void Update()
    {
    // Input handling
        // Only the currently active planet's TileSystem should process input.
        if (GameManager.Instance != null && GameManager.Instance.currentPlanetIndex != planetIndex) return;
        if (!isReady) return;
        if (mainCamera == null) mainCamera = Camera.main;
        // HDRP / some scenes may not tag a camera as MainCamera. Fall back to any camera so tile hover/click still work.
        if (mainCamera == null) mainCamera = FindAnyObjectByType<Camera>();
        if (mainCamera == null) return;

        // MIGRATED: Check InputManager priority before processing (Background priority for TileSystem)
        if (InputManager.Instance != null && !InputManager.Instance.CanProcessInput(InputManager.InputPriority.Background))
            return;

        // Read mouse position once
        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        bool mouseMovedThisFrame = (mousePos - _lastMouseScreenPos).sqrMagnitude > 0.25f;
        bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        // Only raycast when mouse moved or user clicked — saves ~1.5ms/frame when idle
        if (mouseMovedThisFrame || clicked)
        {
            _lastMouseScreenPos = mousePos;
            var hit = GetMouseHitInfo();
            if (hit.hit)
            {
                int tileIndex = hit.tileIndex;
                if (tileIndex >= 0 && tileIndex != lastHoveredTileIndex)
                {
                    lastHoveredTileIndex = tileIndex;
                    OnTileHovered?.Invoke(tileIndex, hit.worldPosition);
                }
            }
            else
            {
                if (lastHoveredTileIndex >= 0)
                {
                    lastHoveredTileIndex = -1;
                    OnTileHoverExited?.Invoke();
                }
            }

            // Click (only if not over UI)
            if (clicked)
            {
                if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
                    return;
                if (hit.hit && hit.tileIndex >= 0)
                {
                    OnTileClicked?.Invoke(hit.tileIndex, hit.worldPosition);
                }
            }
        }
    }

    #region Initialization
    public void InitializeFromPlanet(PlanetGenerator planetGen)
    {
        if (planetGen == null || planetGen.Grid == null) { Debug.LogWarning("[TileSystem] Planet generator missing grid."); return; }
        // Bind this TileSystem to the planet and register it.
        planetIndex = planetGen.planetIndex;
        if (_byPlanetIndex.TryGetValue(planetIndex, out var existing) && existing != null && existing != this)
        {
            Debug.LogWarning($"[TileSystem] InitializeFromPlanet found existing TileSystem for planetIndex={planetIndex}. Skipping initialization on '{name}'.");
            return;
        }
        _byPlanetIndex[planetIndex] = this;
        int tileCount = planetGen.Grid.TileCount;
        planetRef = planetGen;
        tiles = new HexTileData[tileCount];
        ownerByTile = new int[tileCount]; for (int i=0;i<tileCount;i++) ownerByTile[i] = -1;
        tileCenters = planetGen.Grid.tileCenters; // direct reference
        // Copy neighbors to jagged array for quick access
        neighbors = new int[tileCount][];
        for (int i=0;i<tileCount;i++)
        {
            var list = planetGen.Grid.neighbors[i];
            neighbors[i] = list != null ? list.ToArray() : System.Array.Empty<int>();
        }

        // Populate canonical tile data from PlanetGenerator
        int fallbackCreated = 0;
        for (int i = 0; i < tileCount; i++)
        {
            var td = planetGen.GetHexTileData(i);
            if (td == null)
            {
                // Defensive fallback: synthesize minimal data so systems don't break
                var biome = planetGen.GetBaseBiome(i);
                bool isHill = planetGen.IsTileHill(i);
                float elev = planetGen.GetTileElevation(i);
                bool isLand = biome != Biome.Ocean && biome != Biome.Seas && biome != Biome.Coast && biome != Biome.River && biome != Biome.Glacier;
                #pragma warning disable 612, 618  // Suppress obsolete warning for occupantId initialization
                tiles[i] = new HexTileData
                {
                    biome = biome,
                    isLand = isLand,
                    isHill = isHill,
                    elevation = elev,
                    elevationTier = isHill ? ElevationTier.Hill : ElevationTier.Flat,
                    isPassable = true,
                    movementCost = BiomeHelper.GetMovementCost(biome),
                    temperature = 0f,
                    moisture = 0f,
                    occupantId = 0,
                    isMoonTile = false
                };
                #pragma warning restore 612, 618
                fallbackCreated++;
            }
            else
            {
                // Share the same instance so upstream generator/state remain consistent
                tiles[i] = td;
            }
        }
        if (fallbackCreated > 0)
        {
            Debug.LogWarning($"[TileSystem] {fallbackCreated} tiles had no generator data; created fallback entries.");
        }
        // Initialize per-planet occupancy manager and migrate legacy occupant ids
        // IMPORTANT: In multi-planet gameplay each planet needs its own occupancy storage.
        var occMgrObj = GetComponentInChildren<TileOccupancyManager>();
        if (occMgrObj == null)
        {
            var go = new GameObject($"_TileOccupancyManager_P{planetIndex}");
            go.transform.SetParent(transform, false);
            occMgrObj = go.AddComponent<TileOccupancyManager>();
            occMgrObj.planetIndex = planetIndex;
        }
        else
        {
            occMgrObj.planetIndex = planetIndex;
        }
        occMgrObj.Initialize(tileCount);
        // Legacy migration for old saves - suppress obsolete warning
        #pragma warning disable 612, 618
        occMgrObj.MigrateLegacyOccupants(tiles);
        #pragma warning restore 612, 618
        AllocateOwnerColors();
        AllocateFog(tileCount);
    AllocateReligion(tileCount);
        RebuildMergedFog();
        isReady = true;
        if (!alliedCivs.Contains(localPlayerCivId)) alliedCivs.Add(localPlayerCivId);
        
    }

    private void AllocateOwnerColors()
    {
        if (ownerColors == null || ownerColors.Length < maxOwners)
        {
            var arr = new Color[maxOwners];
            for (int i=0;i<maxOwners;i++)
            {
                float h = (i * 0.61803398875f) % 1f;
                arr[i] = Color.HSVToRGB(h, 0.65f, 0.95f);
            }
            ownerColors = arr;
        }
    }

    private void AllocateFog(int tileCount)
    {
        fogByCiv = new byte[civCapacity][];
        for (int c=0;c<civCapacity;c++)
        {
            var arr = new byte[tileCount];
            if (!enableFogOfWar) for (int i=0;i<tileCount;i++) arr[i] = 2; // visible
            fogByCiv[c] = arr;
        }
        mergedFog = new byte[tileCount];
    }

    private void AllocateReligion(int tileCount)
    {
        religionPressures = new List<ReligionPressureEntry>[tileCount];
        holySiteFlags = new bool[tileCount];
        holySiteDistrict = new DistrictData[tileCount];
    }
    #endregion

    #region Ownership
    public int GetOwner(int tile) => (ownerByTile != null && tile >=0 && tile < ownerByTile.Length) ? ownerByTile[tile] : -1;
    public int[] GetOwnerArray() => ownerByTile;
    public Color[] GetOwnerColors() => ownerColors;

    public void SetOwner(int tile, int newOwner)
    {
        if (!isReady || ownerByTile == null) return;
        if (tile < 0 || tile >= ownerByTile.Length) return;
        if (newOwner >= maxOwners) { Debug.LogWarning($"[TileSystem] newOwner {newOwner} >= maxOwners {maxOwners}"); return; }
        int oldOwner = ownerByTile[tile];
        if (oldOwner == newOwner) return;
        ownerByTile[tile] = newOwner;
        _dirtyOverlayTiles.Add(tile);
        OnTileOwnerChanged?.Invoke(tile, oldOwner, newOwner);
    }

    /// <summary>
    /// Centralized ownership setter (authoritative).
    /// Updates:
    /// - HexTileData.owner and HexTileData.controllingCity
    /// - Civilization.ownedTilesByPlanet (remove from previous owner, add to new owner)
    /// - ownerByTile overlay array + OnTileOwnerChanged event (best-effort)
    /// </summary>
    public bool SetTileOwner(int tile, Civilization newOwner, City controllingCity = null, bool updateCivOwnedSets = true, bool updateOverlay = true)
    {
        if (!isReady) return false;
        if (tiles == null) return false;
        if (tile < 0 || tile >= tiles.Length) return false;

        var td = GetTileData(tile);
        if (td == null) return false;

        var prevOwner = td.owner;
        var prevCity = td.controllingCity;
        if (prevOwner == newOwner && td.controllingCity == controllingCity)
        {
            // Keep generator/state consistent even if no logical change.
            if (planetRef != null) planetRef.SetHexTileData(tile, td);
            return true;
        }

        if (debugTileOwnership)
        {
            string prevOwnerName = prevOwner != null ? prevOwner.name : "null";
            string newOwnerName = newOwner != null ? newOwner.name : "null";
            string prevCityName = prevCity != null ? prevCity.name : "null";
            string newCityName = controllingCity != null ? controllingCity.name : "null";
            Debug.Log($"[TileSystem][SetTileOwner] planet={planetIndex} tile={tile} owner {prevOwnerName}->{newOwnerName} controllingCity {prevCityName}->{newCityName}");
        }

        if (updateCivOwnedSets)
        {
            if (prevOwner != null && prevOwner.ownedTilesByPlanet != null)
            {
                if (prevOwner.ownedTilesByPlanet.TryGetValue(planetIndex, out var prevSet) && prevSet != null)
                    prevSet.Remove(tile);

                // Maintain biome ownership aggregates (Tech/Culture prereq optimization).
                prevOwner.NotifyOwnedTileBiomeChanged(planetIndex, td.biome, nowOwned: false);
            }

            if (newOwner != null)
            {
                if (newOwner.ownedTilesByPlanet == null) newOwner.ownedTilesByPlanet = new Dictionary<int, HashSet<int>>();
                if (!newOwner.ownedTilesByPlanet.TryGetValue(planetIndex, out var newSet) || newSet == null)
                {
                    newSet = new HashSet<int>();
                    newOwner.ownedTilesByPlanet[planetIndex] = newSet;
                }
                newSet.Add(tile);

                // Maintain biome ownership aggregates (Tech/Culture prereq optimization).
                newOwner.NotifyOwnedTileBiomeChanged(planetIndex, td.biome, nowOwned: true);
            }
        }

        td.owner = newOwner;
        td.controllingCity = controllingCity;
        _suppressOwnershipGuards = true;
        try
        {
            SetTileData(tile, td);
        }
        finally
        {
            _suppressOwnershipGuards = false;
        }
        if (planetRef != null)
        {
            planetRef.suppressOwnershipGuards = true;
            try { planetRef.SetHexTileData(tile, td); }
            finally { planetRef.suppressOwnershipGuards = false; }
        }

        if (updateOverlay)
        {
            int newOwnerId = -1;
            if (newOwner != null && CivilizationManager.Instance != null)
            {
                // Best-effort: map Civilization to an overlay owner index by its registration order.
                int idx = CivilizationManager.Instance.GetCivIndex(newOwner);
                if (idx >= 0) newOwnerId = idx;
            }

            if (newOwnerId >= 0 && newOwnerId < maxOwners)
            {
                SetOwner(tile, newOwnerId);
            }
            else if (newOwnerId >= maxOwners)
            {
                Debug.LogWarning($"[TileSystem] Overlay owner id {newOwnerId} >= maxOwners {maxOwners}; skipping overlay update for tile {tile}.");
            }
            else if (newOwner == null)
            {
                // Neutral.
                SetOwner(tile, -1);
            }
        }

        return true;
    }

    /// <summary>
    /// Convenience wrapper to set ownership on a specific planet's TileSystem.
    /// </summary>
    public static bool SetTileOwnerOnPlanet(int planetIndex, int tile, Civilization newOwner, City controllingCity = null, bool updateCivOwnedSets = true, bool updateOverlay = true)
    {
        var ts = GetForPlanet(planetIndex);
        if (ts == null || !ts.isReady) return false;
        return ts.SetTileOwner(tile, newOwner, controllingCity, updateCivOwnedSets, updateOverlay);
    }
    #endregion

    #region Fog
    public byte[] GetMergedFogArray() => mergedFog;
    public byte[] GetFogForCiv(int civ) => (civ>=0 && civ < civCapacity) ? fogByCiv[civ] : null;

    public void RevealTiles(int civId, IEnumerable<int> tilesEnum)
    {
        if (!isReady || fogByCiv == null) return;
        if (civId < 0 || civId >= fogByCiv.Length) return;
        var vis = fogByCiv[civId];
        _fogChangedBuffer.Clear();
        foreach (var t in tilesEnum)
        {
            if (t < 0 || t >= vis.Length) continue;
            if (vis[t] != 2) { vis[t] = 2; _fogChangedBuffer.Add(t); _dirtyOverlayTiles.Add(t); }
        }
        if (_fogChangedBuffer.Count > 0)
        {
            if (civId == localPlayerCivId || alliedCivs.Contains(civId)) RebuildMergedFogTiles(_fogChangedBuffer);
            OnFogChanged?.Invoke(civId, _fogChangedBuffer);
        }
    }

    public void ApplyVisionHashSet(int civId, HashSet<int> newVision)
    {
        if (!enableFogOfWar) return;
        if (!isReady || fogByCiv == null) return;
        if (civId < 0 || civId >= fogByCiv.Length) return;
        var vis = fogByCiv[civId];
        _fogChangedBuffer.Clear();
        // Downgrade
        for (int i=0;i<vis.Length;i++)
        {
            if (vis[i] == 2 && !newVision.Contains(i)) { vis[i] = 1; _fogChangedBuffer.Add(i); _dirtyOverlayTiles.Add(i); }
        }
        // Promote
        foreach (var t in newVision)
        {
            if (t < 0 || t >= vis.Length) continue;
            if (vis[t] != 2) { vis[t] = 2; _fogChangedBuffer.Add(t); _dirtyOverlayTiles.Add(t); }
        }
        if (_fogChangedBuffer.Count > 0)
        {
            if (civId == localPlayerCivId || alliedCivs.Contains(civId)) RebuildMergedFogTiles(_fogChangedBuffer);
            OnFogChanged?.Invoke(civId, _fogChangedBuffer);
        }
    }

    private void RebuildMergedFog()
    {
        if (mergedFog == null || fogByCiv == null) return;
        if (!enableFogOfWar)
        {
            for (int i=0;i<mergedFog.Length;i++) mergedFog[i] = 2; return;
        }
        var baseArr = GetFogForCiv(localPlayerCivId) ?? fogByCiv[0];
        Array.Copy(baseArr, mergedFog, mergedFog.Length);
        for (int i=0;i<alliedCivs.Count;i++)
        {
            int civ = alliedCivs[i]; if (civ == localPlayerCivId) continue; if (civ <0 || civ>=civCapacity) continue;
            var arr = fogByCiv[civ]; if (arr == null) continue;
            for (int t=0;t<mergedFog.Length;t++) if (arr[t] > mergedFog[t]) mergedFog[t] = arr[t];
        }
    }

    private void RebuildMergedFogTiles(List<int> tilesChanged)
    {
        if (mergedFog == null || fogByCiv == null) return;
        if (!enableFogOfWar)
        {
            foreach (var t in tilesChanged) if (t>=0 && t<mergedFog.Length) mergedFog[t] = 2; return;
        }
        var localArr = GetFogForCiv(localPlayerCivId) ?? fogByCiv[0];
        foreach (var t in tilesChanged)
        {
            if (t < 0 || t >= mergedFog.Length) continue;
            byte best = localArr[t];
            for (int i=0;i<alliedCivs.Count;i++)
            {
                int civ = alliedCivs[i]; if (civ == localPlayerCivId) continue; if (civ <0 || civ>=civCapacity) continue;
                var arr = fogByCiv[civ]; if (arr == null) continue;
                if (arr[t] > best) best = arr[t];
            }
            mergedFog[t] = best;
        }
    }

    public void AddAlly(int civId) { if (!alliedCivs.Contains(civId)) { alliedCivs.Add(civId); RebuildMergedFog(); MarkAllTilesDirty(); } }
    public void RemoveAlly(int civId) { if (civId == localPlayerCivId) return; if (alliedCivs.Remove(civId)) { RebuildMergedFog(); MarkAllTilesDirty(); } }
    public void SetLocalPlayerCiv(int civId) { localPlayerCivId = civId; if (!alliedCivs.Contains(civId)) alliedCivs.Add(civId); RebuildMergedFog(); MarkAllTilesDirty(); }
    #endregion

    #region Input Raycast Helpers
    public (bool hit, int tileIndex, Vector3 worldPosition) GetMouseHitInfo()
    {
        // NEW SYSTEM: Use WorldPicker for texture-based picking (flat map)
        if (cachedWorldPicker == null)
        {
            cachedWorldPicker = FindAnyObjectByType<WorldPicker>();
        }
        if (cachedWorldPicker != null)
        {
            if (cachedWorldPicker.TryPickTileIndex(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero, out int tileIndex, out Vector3 worldPos))
            {
                return (true, tileIndex, worldPos);
            }

            // If a WorldPicker exists, trust it as the authoritative path.
            // Falling back here can silently reintroduce flat-map picking errors.
            return (false, -1, Vector3.zero);
        }
        
        // FALLBACK: Raycast against flat map quad (if WorldPicker not available)
        Ray ray = mainCamera != null ? mainCamera.ScreenPointToRay(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector3.zero) : default;
        if (mainCamera == null) return (false, -1, Vector3.zero);

        // Use HexMapChunkManager (chunk-based map renderer)
        if (cachedChunkManager == null)
        {
            cachedChunkManager = FindAnyObjectByType<HexMapChunkManager>();
        }
        if (cachedChunkManager != null && cachedChunkManager.IsBuilt)
        {
            var chunkCollider = cachedChunkManager.PickingCollider;
            if (chunkCollider != null && chunkCollider.Raycast(ray, out RaycastHit hitInfo, maxRaycastDistance))
            {
                Vector2 uv = cachedChunkManager.GetUVFromWorldPosition(hitInfo.point);
                int tileIndex = cachedChunkManager.GetTileIndexAtUV(uv.x, uv.y);
                if (tileIndex >= 0)
                    return (true, tileIndex, hitInfo.point);
            }
        }
        
        return (false, -1, Vector3.zero);
    }
    #endregion

    #region Overlay & Dirty Tracking
    public void MarkTileDirty(int tile) { if (tile>=0) _dirtyOverlayTiles.Add(tile); }
    public void MarkTilesDirty(IEnumerable<int> tilesEnum) { foreach (var t in tilesEnum) if (t>=0) _dirtyOverlayTiles.Add(t); }
    public void MarkAllTilesDirty() { if (ownerByTile == null) return; for (int i=0;i<ownerByTile.Length;i++) _dirtyOverlayTiles.Add(i); }
    public HashSet<int> GetDirtyOverlaySet() => _dirtyOverlayTiles;
    public void ClearDirtyOverlaySet() => _dirtyOverlayTiles.Clear();
    #endregion

		#region Tile Data Access / Mutations (stubs)
    public HexTileData GetTileData(int tile) => (tiles != null && tile >=0 && tile < tiles.Length) ? tiles[tile] : null;

    public void SetTileData(int tile, HexTileData data)
    {
        if (!isReady || tiles == null) return;
        if (tile < 0 || tile >= tiles.Length) return;

        // Guardrail: prevent silent ownership desync by detecting direct writes to owner/controllingCity.
        // All gameplay ownership changes should go through SetTileOwner so ownedTilesByPlanet stays correct.
        if (!_suppressOwnershipGuards)
        {
            var prev = tiles[tile];
            if (prev != null && data != null)
            {
                bool ownerChanged = !ReferenceEquals(prev.owner, data.owner);
                bool controllingCityChanged = !ReferenceEquals(prev.controllingCity, data.controllingCity);
                if (ownerChanged || controllingCityChanged)
                {
                    string prevOwnerName = prev.owner != null ? prev.owner.name : "null";
                    string newOwnerName = data.owner != null ? data.owner.name : "null";
                    string prevCityName = prev.controllingCity != null ? prev.controllingCity.name : "null";
                    string newCityName = data.controllingCity != null ? data.controllingCity.name : "null";

                    if (debugTileOwnership || debugTileOwnershipVerbose)
                    {
                        Debug.LogWarning(
                            $"[TileSystem][OwnershipGuard] Direct SetTileData changed ownership fields. " +
                            $"planet={planetIndex} tile={tile} owner {prevOwnerName}->{newOwnerName} controllingCity {prevCityName}->{newCityName}. " +
                            $"Use TileSystem.SetTileOwner(...) instead.");

                        if (debugTileOwnershipVerbose)
                        {
                            Debug.LogWarning($"[TileSystem][OwnershipGuard] StackTrace:\n{Environment.StackTrace}");
                        }
                    }
                }
            }
        }

        tiles[tile] = data;
        _dirtyOverlayTiles.Add(tile);
        // Could raise a generic OnTileDataChanged later
    }
    // Thin flat-map alias: legacy calls now return flat centers
    public Vector3 GetTileCenter(int tile) => GetTileCenterFlat(tile);
    /// <summary>
    /// Get the planar (flat map) center for a tile. Uses X/Z from tile center and sets Y to the flat map plane height.
    /// </summary>
    public Vector3 GetTileCenterFlat(int tile)
    {
        if (tileCenters == null || tile < 0 || tile >= tileCenters.Length) return Vector3.zero;
        var c = tileCenters[tile];
        float flatY = 0f;
        if (GameManager.Instance != null)
        {
            flatY = GameManager.Instance.GetFlatPlaneY();
        }
        else if (planetRef != null)
        {
            flatY = planetRef.transform.position.y;
        }
        return new Vector3(c.x, flatY, c.z);
    }
    public int[] GetNeighbors(int tile) => (neighbors != null && tile >=0 && tile < neighbors.Length) ? neighbors[tile] : System.Array.Empty<int>();
    public bool IsReady() => isReady;
		#endregion

    #region Multi-Planet Support
    /// <summary>
    /// Gets tile data from a specific planet.
    /// With per-planet TileSystems, this prefers the planet's TileSystem (so ownership/fog/etc remain correct).
    /// </summary>
    public HexTileData GetTileDataFromPlanet(int tile, int planetIndex)
    {
        var ts = GetForPlanet(planetIndex);
        if (ts != null && ts.isReady)
        {
            return ts.GetTileData(tile);
        }

        // Fallback: query the planet generator directly (covers very early generation, before TileSystem init).
        PlanetGenerator planetGen = GetPlanetGeneratorForIndex(planetIndex);
        if (planetGen != null) return planetGen.GetHexTileData(tile);

        return GetTileData(tile);
    }
    
    /// <summary>
    /// Sets tile data on a specific planet by updating the planet generator directly.
    /// This allows updating tile data on planets other than the current one.
    /// </summary>
    public void SetTileDataOnPlanet(int tile, HexTileData data, int planetIndex)
    {
        if (data == null) return;

        // Prefer writing through the planet's TileSystem (keeps dirty flags and derived state consistent).
        var ts = GetForPlanet(planetIndex);
        if (ts != null && ts.isReady)
        {
            ts.SetTileData(tile, data);
            // Also update the generator for that planet so generator/state remain consistent.
            if (ts.planetRef != null)
            {
                ts.planetRef.suppressOwnershipGuards = true;
                try { ts.planetRef.SetHexTileData(tile, data); }
                finally { ts.planetRef.suppressOwnershipGuards = false; }
            }
            return;
        }

        // Fallback: update generator directly (very early generation).
        PlanetGenerator planetGen = GetPlanetGeneratorForIndex(planetIndex);
        if (planetGen != null) planetGen.SetHexTileData(tile, data);
    }
    
    /// <summary>
    /// Gets the tile center position from a specific planet's grid.
    /// This allows querying tile positions from planets other than the current one.
    /// </summary>
    public Vector3 GetTileCenterFromPlanet(int tile, int planetIndex)
    {
        var ts = GetForPlanet(planetIndex);
        if (ts != null && ts.isReady)
        {
            return ts.GetTileCenterFlat(tile);
        }

        // Fallback: query generator directly.
        PlanetGenerator planetGen = GetPlanetGeneratorForIndex(planetIndex);
        if (planetGen != null && planetGen.Grid != null)
        {
            var grid = planetGen.Grid;
            if (tile >= 0 && tile < grid.tileCenters.Length)
            {
                var c = grid.tileCenters[tile];
                float yPlane = planetGen.transform.position.y;
                return new Vector3(c.x, yPlane, c.z);
            }
        }

        return GetTileCenterFlat(tile);
    }
    
    /// <summary>
    /// Gets the owner of a tile on a specific planet.
    /// NOTE: Currently ownership is only tracked for the current planet in TileSystem.
    /// For true multi-planet ownership, this would need per-planet ownership storage.
    /// </summary>
    public int GetOwnerFromPlanet(int tile, int planetIndex)
    {
        var ts = GetForPlanet(planetIndex);
        if (ts != null && ts.isReady) return ts.GetOwner(tile);
        return -1;
    }
    
    /// <summary>
    /// Helper method to get a planet generator by index
    /// </summary>
    private PlanetGenerator GetPlanetGeneratorForIndex(int planetIndex)
    {
        if (GameManager.Instance == null) return null;
        var gen = GameManager.Instance.GetPlanetGenerator(planetIndex);
        if (gen != null) return gen;
        // Fallback: return current generator for index 0
        if (planetIndex == 0) return GameManager.Instance.GetCurrentPlanetGenerator();
        return null;
    }
    #endregion

    #region Religion Helpers
    public void AddReligionPressure(int tile, ReligionData religion, float amount)
    {
        if (!isReady || religion == null || amount == 0f) return;
        if (tile < 0 || religionPressures == null || tile >= religionPressures.Length) return;
        var list = religionPressures[tile];
        if (list == null) { list = new List<ReligionPressureEntry>(2); religionPressures[tile] = list; }
        for (int i=0;i<list.Count;i++)
        {
            if (list[i].religion == religion)
            {
                var e = list[i]; e.pressure += amount; list[i] = e; return;
            }
        }
        list.Add(new ReligionPressureEntry { religion = religion, pressure = amount });
    }

    public ReligionData GetDominantReligion(int tile)
    {
        if (!isReady || religionPressures == null || tile < 0 || tile >= religionPressures.Length) return null;
        var list = religionPressures[tile]; if (list == null || list.Count == 0) return null;
        ReligionData best = null; float bestVal = 0f;
        for (int i=0;i<list.Count;i++) { var e = list[i]; if (e.religion == null) continue; if (e.pressure > bestVal) { bestVal = e.pressure; best = e.religion; } }
        return best;
    }

    public IReadOnlyList<ReligionPressureEntry> GetReligionPressures(int tile)
    {
        if (!isReady || religionPressures == null || tile < 0 || tile >= religionPressures.Length) return null;
        return religionPressures[tile];
    }

    public void SetHolySite(int tile, bool isHolySite, DistrictData district)
    {
        if (!isReady || tile < 0 || tile >= holySiteFlags.Length) return;
        holySiteFlags[tile] = isHolySite;
        holySiteDistrict[tile] = isHolySite ? district : null;
    }

    public bool HasHolySite(int tile)
    { return isReady && tile >= 0 && holySiteFlags != null && tile < holySiteFlags.Length && holySiteFlags[tile]; }

    public DistrictData GetHolySiteDistrict(int tile)
    { return (!isReady || tile < 0 || holySiteDistrict == null || tile >= holySiteDistrict.Length) ? null : holySiteDistrict[tile]; }
    #endregion

    #region Range / Distance
    /// <summary>
    /// Get hex-step distance between two tiles using BFS (respects adjacency, not Euclidean).
    /// Returns -1 if no path exists (isolated tiles).
    /// </summary>
    public int GetTileDistance(int a, int b)
    {
        if (!isReady || a < 0 || b < 0 || a >= neighbors.Length || b >= neighbors.Length) return -1;
        if (a == b) return 0;
        HashSet<int> visited = new HashSet<int> { a };
        Queue<(int idx, int dist)> q = new(); q.Enqueue((a, 0));
        while (q.Count > 0)
        {
            var (idx, dist) = q.Dequeue();
            var neigh = neighbors[idx]; if (neigh == null) continue;
            for (int i = 0; i < neigh.Length; i++)
            {
                int n = neigh[i];
                if (n == b) return dist + 1;
                if (visited.Add(n)) q.Enqueue((n, dist + 1));
            }
        }
        return -1; // No path
    }
    
    /// <summary>
    /// Get Euclidean distance for continuous movement/physics (not pathfinding).
    /// </summary>
    public float GetTileDistanceFlat(int a, int b) => Vector3.Distance(GetTileCenterFlat(a), GetTileCenterFlat(b));

    public List<int> GetTilesWithinSteps(int start, int steps)
    {
        var result = new List<int>(); if (!isReady || steps <= 0) return result;
        HashSet<int> visited = new HashSet<int> { start };
        Queue<(int idx,int depth)> q = new(); q.Enqueue((start,0));
        while (q.Count>0)
        {
            var (idx, depth) = q.Dequeue(); if (depth >= steps) continue;
            var neigh = GetNeighbors(idx); if (neigh == null) continue;
            for (int i=0;i<neigh.Length;i++)
            { int n = neigh[i]; if (visited.Add(n)) { result.Add(n); q.Enqueue((n, depth+1)); } }
        }
        return result;
    }
    #endregion

		#region Surface / Accessibility / Occupancy
    public Vector3 GetTileSurfacePosition(int tile, float unitOffset = 0f)
    {
        // Get flat center position
        var c = GetTileCenterFlat(tile);
        
        // Get terrain elevation to calculate actual Y position
        float terrainY = c.y;
        var td = GetTileData(tile);
        if (td != null)
        {
            // Elevation is already in world-space units — add directly to terrain Y
            terrainY += td.elevation;
        }
        
        return new Vector3(c.x, terrainY + unitOffset, c.z);
    }

    public bool IsTileAccessible(int tile, bool mustBeLand, int unitId, TileLayer layer = TileLayer.Surface)
    {
        var td = GetTileData(tile); if (td == null) return false;
        if (mustBeLand && !td.isLand) return false;
        // TileOccupancyManager is the single source of truth for occupancy
        var occMgr = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        int occ = occMgr != null ? occMgr.GetOccupantId(tile, layer) : 0;
        return occ == 0 || occ == unitId;
    }

    public void SetTileOccupant(int tile, GameObject occupant, TileLayer layer = TileLayer.Surface)
    {
        var td = GetTileData(tile); if (td == null) return;
        if (occupant == null)
        {
            // Clear occupant via occupancy manager
            (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.ClearOccupant(tile, layer);
            return;
        }

        Civilization unitOwner = null;
        var cu = occupant.GetComponent<CombatUnit>(); if (cu != null) unitOwner = cu.owner;
        if (unitOwner == null) { var wu = occupant.GetComponent<WorkerUnit>(); if (wu != null) unitOwner = wu.owner; }
        if (td.improvementOwner != null && unitOwner != null && unitOwner != td.improvementOwner)
        { Debug.LogWarning($"[TileSystem] Prevented {occupant.name} from occupying tile {tile} owned by {td.improvementOwner.civData?.civName}."); return; }

        // Set occupant (layer-aware)
        (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(tile, occupant, layer);
    }

    public void ClearTileOccupant(int tile, TileLayer layer = TileLayer.Surface) => SetTileOccupant(tile, null, layer);
    #endregion

    // Overload for legacy calls that passed planetIndex (ignored in single-planet scope)
    public Vector3 GetTileSurfacePosition(int tile, float unitOffset, int planetIndex) => GetTileSurfacePosition(tile, unitOffset);

    // Cleanup hook called by GameManager during scene transitions
    public void ClearAllCaches()
    {
        _dirtyOverlayTiles.Clear();
        // Intentionally retains tiles/owners until reinitialized via InitializeFromPlanet
        if (religionPressures != null)
        {
            for (int i=0;i<religionPressures.Length;i++)
            {
                religionPressures[i]?.Clear();
                religionPressures[i] = null;
            }
        }
        holySiteFlags = null;
        holySiteDistrict = null;
    }
}
