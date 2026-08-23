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
    private bool subscribedToCivRegistry;

    [Header("State Arrays (public read-only accessors)")]
    [SerializeField] private HexTileData[] tiles;              // Canonical tile data array (single planet scope for now)
    public int TileCount => tiles != null ? tiles.Length : 0;
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
    private ReligionData[] holySiteReligions;                    // identity survives capture/owner changes

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
    public event Action<int, IReadOnlyList<int>> OnReligionPressureChanged; // (planet, changed tiles)
    public event Action<int, IReadOnlyList<int>> OnAdministrationChanged;   // (planet, changed tiles)
    public event Action<int,Vector3> OnTileHovered;              // (tile, worldPos)
    public event Action OnTileHoverExited;                       // hover exit
    // Consumable tile-click handler: return true to consume the click and stop propagation.
    public delegate bool TileClickHandler(int tileIndex, Vector3 worldPos);
    public event TileClickHandler OnTileClicked;                 // (tile, worldPos) -> bool consumed
    // Resource change event: (tileIndex, oldResource, newResource)
    public event Action<int, ResourceData, ResourceData> OnTileResourceChanged;

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
        ApplyDynamicCapacitiesFromRegistry(forceExactWhenNotReady: true);
        TrySubscribeToCivilizationRegistry();
    }

    void OnDestroy()
    {
        UnsubscribeFromCivilizationRegistry();
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
            holySiteReligions = null;
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
            OnTileResourceChanged = null;

            isReady = false;
        }
    }

    void Update()
    {
    // Late subscribe in case CivilizationManager was created after this TileSystem
        if (!subscribedToCivRegistry)
            TrySubscribeToCivilizationRegistry();

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
                    if (OnTileClicked != null)
                    {
                        // Invoke each subscriber in order; stop when a subscriber returns true (consumed)
                        foreach (TileClickHandler h in OnTileClicked.GetInvocationList())
                        {
                            try
                            {
                                if (h.Invoke(hit.tileIndex, hit.worldPosition))
                                {
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[TileSystem] Exception in OnTileClicked handler: {ex}");
                            }
                        }
                    }
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
                bool isLand = biome != Biome.Ocean && biome != Biome.Seas && biome != Biome.Coast && biome != Biome.River && biome != Biome.Lava && biome != Biome.Glacier;
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
        ApplyDynamicCapacitiesFromRegistry(forceExactWhenNotReady: true);
        AllocateOwnerColors();
        AllocateFog(tileCount);
    AllocateReligion(tileCount);
        RebuildMergedFog();
        isReady = true;
        if (!alliedCivs.Contains(localPlayerCivId)) alliedCivs.Add(localPlayerCivId);
        
    }

    private void TrySubscribeToCivilizationRegistry()
    {
        var civManager = CivilizationManager.Instance;
        if (civManager == null || subscribedToCivRegistry)
            return;

        civManager.OnCivilizationRegistryChanged -= HandleCivilizationRegistryChanged;
        civManager.OnCivilizationRegistryChanged += HandleCivilizationRegistryChanged;
        subscribedToCivRegistry = true;
    }

    private void UnsubscribeFromCivilizationRegistry()
    {
        if (!subscribedToCivRegistry)
            return;

        if (CivilizationManager.Instance != null)
            CivilizationManager.Instance.OnCivilizationRegistryChanged -= HandleCivilizationRegistryChanged;

        subscribedToCivRegistry = false;
    }

    private void HandleCivilizationRegistryChanged(int requiredSlotCapacity)
    {
        ApplyDynamicCapacitiesFromRegistry(forceExactWhenNotReady: false, explicitRegisteredCount: requiredSlotCapacity);
    }

    private int ResolveRegisteredCivilizationCount(int explicitRegisteredCount = -1)
    {
        if (explicitRegisteredCount >= 0)
            return explicitRegisteredCount;

        // Fall back to the monotonic slot capacity (never shrinks), not civs.Count (which shrinks
        // on elimination) - required capacity must cover the highest stable actor slot ever assigned.
        return CivilizationManager.Instance != null ? CivilizationManager.Instance.MapActorSlotCapacity : 0;
    }

    private void ApplyDynamicCapacitiesFromRegistry(bool forceExactWhenNotReady, int explicitRegisteredCount = -1)
    {
        int required = Mathf.Max(1, ResolveRegisteredCivilizationCount(explicitRegisteredCount));

        if (!isReady || forceExactWhenNotReady)
        {
            civCapacity = required;
            maxOwners = required;
            return;
        }

        int oldCivCapacity = civCapacity;
        int oldMaxOwners = maxOwners;

        civCapacity = Mathf.Max(civCapacity, required);
        maxOwners = Mathf.Max(maxOwners, required);

        if (maxOwners > oldMaxOwners)
            EnsureOwnerColorCapacity(maxOwners);

        if (civCapacity > oldCivCapacity)
            EnsureFogCapacity(civCapacity);
    }

    private void EnsureOwnerColorCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= 0)
            requiredCapacity = 1;

        if (ownerColors != null && ownerColors.Length >= requiredCapacity)
            return;

        var arr = new Color[requiredCapacity];
        if (ownerColors != null)
            Array.Copy(ownerColors, arr, ownerColors.Length);

        int start = ownerColors != null ? ownerColors.Length : 0;
        for (int i = start; i < requiredCapacity; i++)
        {
            float h = (i * 0.61803398875f) % 1f;
            arr[i] = Color.HSVToRGB(h, 0.65f, 0.95f);
        }

        ownerColors = arr;
    }

    private void EnsureFogCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= 0)
            requiredCapacity = 1;

        if (ownerByTile == null)
            return;

        if (fogByCiv != null && fogByCiv.Length >= requiredCapacity)
            return;

        var nextFog = new byte[requiredCapacity][];
        int tileCount = ownerByTile.Length;
        int copyCount = fogByCiv != null ? Mathf.Min(fogByCiv.Length, requiredCapacity) : 0;

        for (int c = 0; c < copyCount; c++)
            nextFog[c] = fogByCiv[c];

        for (int c = copyCount; c < requiredCapacity; c++)
        {
            var arr = new byte[tileCount];
            if (!enableFogOfWar)
            {
                for (int i = 0; i < tileCount; i++)
                    arr[i] = 2;
            }
            nextFog[c] = arr;
        }

        fogByCiv = nextFog;
        RebuildMergedFog();
        MarkAllTilesDirty();
    }

    private void AllocateOwnerColors()
    {
        EnsureOwnerColorCapacity(maxOwners);
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
        holySiteReligions = new ReligionData[tileCount];
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
            if (newOwner != null)
            {
                // Stable per-session slot, not registration-order index: never shifts when
                // another civilization is eliminated (see Civilization.MapActorSlot).
                int idx = newOwner.MapActorSlot;
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

        if (prevCity != controllingCity)
            NotifyAdministrationChanged(new[] { tile });

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
    public Vector3 GetTileCenter(int tile) => (tileCenters != null && tile >= 0 && tile < tileCenters.Length) ? tileCenters[tile] : Vector3.zero;
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
    /// Atomically set the resource on a tile for the given planet and raise an event so listeners
    /// (e.g., ResourceManager, UI) can react to visual/instance lifecycle changes.
    /// </summary>
    public static void SetResourceOnTile(ResourceData resource, int tileIndex, int planetIndex)
    {
        var ts = GetForPlanet(planetIndex);
        if (ts != null && ts.isReady)
        {
            ts.SetResource(tileIndex, resource);
            // Persist to generator/state as well
            if (ts.planetRef != null)
            {
                ts.planetRef.suppressOwnershipGuards = true;
                try { ts.planetRef.SetHexTileData(tileIndex, ts.GetTileData(tileIndex)); }
                finally { ts.planetRef.suppressOwnershipGuards = false; }
            }
            return;
        }

        // Fallback: if TileSystem not ready, attempt to set via generator directly
        var gen = GetPlanetGeneratorForIndex(planetIndex);
        if (gen != null)
        {
            var td = gen.GetHexTileData(tileIndex);
            if (td != null)
            {
                var old = td.resource;
                td.resource = resource;
                gen.SetHexTileData(tileIndex, td);
            }
        }
    }

    // Instance-side resource setter that raises the OnTileResourceChanged event.
    public void SetResource(int tile, ResourceData resource)
    {
        if (!isReady || tiles == null) return;
        if (tile < 0 || tile >= tiles.Length) return;

        var prev = tiles[tile]?.resource;
        if (ReferenceEquals(prev, resource)) return;

        if (tiles[tile] == null) tiles[tile] = new HexTileData();
        tiles[tile].resource = resource;
        _dirtyOverlayTiles.Add(tile);

        try
        {
            OnTileResourceChanged?.Invoke(tile, prev, resource);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TileSystem] OnTileResourceChanged handler threw: {ex.Message}");
        }
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
    private static PlanetGenerator GetPlanetGeneratorForIndex(int planetIndex)
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
                var e = list[i]; e.pressure += amount; list[i] = e;
                OnReligionPressureChanged?.Invoke(planetIndex, new[] { tile }); return;
            }
        }
        list.Add(new ReligionPressureEntry { religion = religion, pressure = amount });
        OnReligionPressureChanged?.Invoke(planetIndex, new[] { tile });
    }

    /// <summary>Canonical notification for a controlling-city or governor change.</summary>
    public void NotifyAdministrationChanged(IEnumerable<int> changedTiles)
    {
        if (changedTiles == null) return;
        var snapshot = changedTiles as IReadOnlyList<int> ?? new List<int>(changedTiles);
        OnAdministrationChanged?.Invoke(planetIndex, snapshot);
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
        if (!isHolySite && holySiteReligions != null) holySiteReligions[tile] = null;
    }

    public void SetHolySiteReligion(int tile, ReligionData religion)
    { if (isReady && tile >= 0 && holySiteReligions != null && tile < holySiteReligions.Length && HasHolySite(tile)) holySiteReligions[tile] = religion; }

    public ReligionData GetHolySiteReligion(int tile)
    { return !isReady || tile < 0 || holySiteReligions == null || tile >= holySiteReligions.Length ? null : holySiteReligions[tile]; }

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
    /// Get the minimum hex-step distance on the wrapped flat grid.
    /// Falls back to BFS when grid dimensions are unavailable.
    /// </summary>
    public int GetWrappedHexDistance(int a, int b)
    {
        if (!isReady || a < 0 || b < 0 || a >= neighbors.Length || b >= neighbors.Length) return -1;
        if (a == b) return 0;

        var grid = planetRef != null ? planetRef.Grid : null;
        if (grid == null || grid.Width <= 0 || grid.Height <= 0 || grid.TileCount != neighbors.Length)
            return GetTileDistance(a, b);

        int width = grid.Width;
        int rowA = a / width;
        int colA = a % width;
        int rowB = b / width;
        int colB = b % width;

        Vector3Int cubeA = OddRToCube(rowA, colA);
        int bestDistance = int.MaxValue;

        // Horizontal wrap turns the map into a cylinder, so check the target column
        // in the local copy and one wrapped copy on each side.
        for (int wrapOffset = -1; wrapOffset <= 1; wrapOffset++)
        {
            Vector3Int cubeB = OddRToCube(rowB, colB + wrapOffset * width);
            int distance = CubeDistance(cubeA, cubeB);
            if (distance < bestDistance)
                bestDistance = distance;
        }

        return bestDistance;
    }
    
    /// <summary>
    /// Get Euclidean distance for continuous movement/physics (not pathfinding).
    /// </summary>
    public float GetTileDistanceFlat(int a, int b) => Vector3.Distance(GetTileCenterFlat(a), GetTileCenterFlat(b));

    private static Vector3Int OddRToCube(int row, int col)
    {
        int x = col - ((row - (row & 1)) / 2);
        int z = row;
        int y = -x - z;
        return new Vector3Int(x, y, z);
    }

    private static int CubeDistance(Vector3Int a, Vector3Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        int dz = Mathf.Abs(a.z - b.z);
        return Mathf.Max(dx, Mathf.Max(dy, dz));
    }

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
        holySiteReligions = null;
        holySiteDistrict = null;
    }
}
