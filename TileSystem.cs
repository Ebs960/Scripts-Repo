using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Consolidated monolithic TileSystem combining ownership, fog, data access, and input event fan-out.
/// </summary>
public class TileSystem : MonoBehaviour
{
    public static TileSystem Instance { get; private set; }

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

    [Header("Runtime Flags")] public bool isReady;

    [Header("Input / Raycast Settings")]
    [Tooltip("Camera for raycasts (auto-detects if null)")] public Camera mainCamera;
    [Tooltip("Maximum raycast distance for tile input")] public float maxRaycastDistance = 1000f;
    [Tooltip("Layer mask used for tile raycasts")] public LayerMask tileRaycastMask = -1;
    private int lastHoveredTileIndex = -1;

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
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
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
            
            // Clear singleton reference
            Instance = null;
            
            isReady = false;
        }
    }

    void Update()
    {
    // Input handling
        if (!isReady) return;
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        // MIGRATED: Check InputManager priority before processing (Background priority for TileSystem)
        if (InputManager.Instance != null && !InputManager.Instance.CanProcessInput(InputManager.InputPriority.Background))
            return;

        // Hover (always processed unless UI is blocking)
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
        if (Input.GetMouseButtonDown(0))
        {
            // MIGRATED: Check UI blocking before processing clicks
            if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
                return;
                
            var ch = hit; if (!ch.hit) ch = GetMouseHitInfo();
            if (ch.hit && ch.tileIndex >= 0)
            {
                OnTileClicked?.Invoke(ch.tileIndex, ch.worldPosition);
            }
        }
    }

    #region Initialization
    public void InitializeFromPlanet(PlanetGenerator planetGen)
    {
        if (planetGen == null || planetGen.Grid == null) { Debug.LogWarning("[TileSystem] Planet generator missing grid."); return; }
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
        // Initialize occupancy manager and migrate legacy occupant ids
        var occMgrObj = FindObjectOfType<TileOccupancyManager>();
        if (occMgrObj == null)
        {
            var go = new GameObject("_TileOccupancyManager");
            occMgrObj = go.AddComponent<TileOccupancyManager>();
        }
        if (occMgrObj != null)
        {
            occMgrObj.Initialize(tileCount);
            occMgrObj.MigrateLegacyOccupants(tiles);
        }
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
        var worldPicker = FindAnyObjectByType<WorldPicker>();
        if (worldPicker != null)
        {
            if (worldPicker.TryPickTileIndex(Input.mousePosition, out int tileIndex, out Vector3 worldPos))
            {
                return (true, tileIndex, worldPos);
            }
        }
        
        // FALLBACK: Raycast against flat map quad (if WorldPicker not available)
        Ray ray = mainCamera != null ? mainCamera.ScreenPointToRay(Input.mousePosition) : default;
        if (mainCamera == null) return (false, -1, Vector3.zero);

        // Use HexMapChunkManager (chunk-based map renderer)
        var chunkManager = FindAnyObjectByType<HexMapChunkManager>();
        if (chunkManager != null && chunkManager.IsBuilt)
        {
            var chunkCollider = chunkManager.PickingCollider;
            if (chunkCollider != null && chunkCollider.Raycast(ray, out RaycastHit hitInfo, maxRaycastDistance))
            {
                Vector2 uv = chunkManager.GetUVFromWorldPosition(hitInfo.point);
                int tileIndex = chunkManager.GetTileIndexAtUV(uv.x, uv.y);
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
    /// Gets tile data from a specific planet by querying the planet generator directly.
    /// This allows querying tile data from planets other than the current one.
    /// </summary>
    public HexTileData GetTileDataFromPlanet(int tile, int planetIndex)
    {
        // If querying current planet, use the cached array for performance
        if (planetRef != null && isReady)
        {
            int currentPlanetIndex = (GameManager.Instance != null) ? GameManager.Instance.currentPlanetIndex : 0;
            
            if (planetIndex == currentPlanetIndex)
            {
                return GetTileData(tile);
            }
        }
        
        // Query the specific planet's generator
        PlanetGenerator planetGen = GetPlanetGeneratorForIndex(planetIndex);
        if (planetGen != null)
        {
            return planetGen.GetHexTileData(tile);
        }
        
        // Fallback to current planet if requested planet not found
        return GetTileData(tile);
    }
    
    /// <summary>
    /// Sets tile data on a specific planet by updating the planet generator directly.
    /// This allows updating tile data on planets other than the current one.
    /// </summary>
    public void SetTileDataOnPlanet(int tile, HexTileData data, int planetIndex)
    {
        if (data == null) return;
        
        // If updating current planet, use the cached array and mark dirty
        if (planetRef != null && isReady)
        {
            int currentPlanetIndex = (GameManager.Instance != null) ? GameManager.Instance.currentPlanetIndex : 0;
            
            if (planetIndex == currentPlanetIndex)
            {
                SetTileData(tile, data);
                // Also update the planet generator to keep in sync
                if (planetRef != null)
                {
                    planetRef.SetHexTileData(tile, data);
                }
                return;
            }
        }
        
        // Update the specific planet's generator
        PlanetGenerator planetGen = GetPlanetGeneratorForIndex(planetIndex);
        if (planetGen != null)
        {
            planetGen.SetHexTileData(tile, data);
        }
    }
    
    /// <summary>
    /// Gets the tile center position from a specific planet's grid.
    /// This allows querying tile positions from planets other than the current one.
    /// </summary>
    public Vector3 GetTileCenterFromPlanet(int tile, int planetIndex)
    {
        // If querying current planet, reuse flat center
        if (planetRef != null && isReady)
        {
            int currentPlanetIndex = (GameManager.Instance != null) ? GameManager.Instance.currentPlanetIndex : 0;
            
            if (planetIndex == currentPlanetIndex)
            {
                return GetTileCenterFlat(tile);
            }
        }
        
        // Query the specific planet's grid and project to its flat plane
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
        
        // Fallback to current planet flat center
        return GetTileCenterFlat(tile);
    }
    
    /// <summary>
    /// Gets the owner of a tile on a specific planet.
    /// NOTE: Currently ownership is only tracked for the current planet in TileSystem.
    /// For true multi-planet ownership, this would need per-planet ownership storage.
    /// </summary>
    public int GetOwnerFromPlanet(int tile, int planetIndex)
    {
        // If querying current planet, use the cached array
        if (planetRef != null && isReady)
        {
            int currentPlanetIndex = -1;
            if (GameManager.Instance != null)
            {
                currentPlanetIndex = GameManager.Instance.enableMultiPlanetSystem 
                    ? GameManager.Instance.currentPlanetIndex 
                    : 0;
            }
            
            if (planetIndex == currentPlanetIndex)
            {
                return GetOwner(tile);
            }
        }
        
        // TODO: For true multi-planet support, we'd need per-planet ownership storage
        // For now, ownership is only tracked for the current planet
        // Return -1 (neutral) for other planets
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
        if (td != null && GameManager.Instance != null)
        {
            // Use renderElevation (0-1) * displacement strength to get actual world Y offset
            float displacementStrength = GameManager.Instance.GetTerrainDisplacementStrength();
            terrainY += td.renderElevation * displacementStrength;
        }
        
        return new Vector3(c.x, terrainY + unitOffset, c.z);
    }

    public bool IsTileAccessible(int tile, bool mustBeLand, int unitId)
    {
        var td = GetTileData(tile); if (td == null) return false;
        if (mustBeLand && !td.isLand) return false;
        // Movement points removed - tiles are always accessible (movement speed is fatigue-based)
        int occ = td.occupantId;
        if (TileOccupancyManager.Instance != null)
            occ = TileOccupancyManager.Instance.GetOccupantId(tile, TileLayer.Surface);
        return occ == 0 || occ == unitId;
    }

    public void SetTileOccupant(int tile, GameObject occupant)
    {
        var td = GetTileData(tile); if (td == null) return;
        if (occupant == null)
        {
            // Clear surface occupant via occupancy manager
            TileOccupancyManager.Instance?.ClearOccupant(tile, TileLayer.Surface);
            return;
        }

        Civilization unitOwner = null;
        var cu = occupant.GetComponent<CombatUnit>(); if (cu != null) unitOwner = cu.owner;
        if (unitOwner == null) { var wu = occupant.GetComponent<WorkerUnit>(); if (wu != null) unitOwner = wu.owner; }
        if (td.improvementOwner != null && unitOwner != null && unitOwner != td.improvementOwner)
        { Debug.LogWarning($"[TileSystem] Prevented {occupant.name} from occupying tile {tile} owned by {td.improvementOwner.civData?.civName}."); return; }

        // Set as surface occupant (legacy compatibility)
        TileOccupancyManager.Instance?.SetOccupant(tile, occupant, TileLayer.Surface);
    }

    public void ClearTileOccupant(int tile) => SetTileOccupant(tile, null);
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
