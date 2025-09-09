using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Centralized helper class for accessing tile data from planet and moon.
/// Now decoupled from Hexasphere.
/// </summary>
public class TileDataHelper : MonoBehaviour
{
    public static TileDataHelper Instance { get; private set; }

    [Header("Single Planet References (Legacy)")]
    private PlanetGenerator planet;
    private MoonGenerator moon;
    
    [Header("Multi-Planet System")]
    [Tooltip("Dictionary of planet generators for multi-planet system")]
    private Dictionary<int, PlanetGenerator> planets = new Dictionary<int, PlanetGenerator>();
    [Tooltip("Dictionary of moon generators for multi-planet system")]
    private Dictionary<int, MoonGenerator> moons = new Dictionary<int, MoonGenerator>();

    private Dictionary<int, CachedTileData> tileDataCache = new();
    // Planet-aware cache to avoid cross-planet collisions when the same tileIndex exists on multiple planets
    private Dictionary<(int planetIndex, int tileIndex), CachedTileData> planetTileDataCache = new();
    private Dictionary<int, int[]> adjacencyCache = new();
    private Dictionary<int, Vector3> tileCenterCache = new();

    private const int CACHE_MAX_AGE = 10;

    private struct CachedTileData
    {
        public HexTileData tileData;
        public bool isMoonTile;
        public int lastUpdateFrame;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    void Start() => UpdateReferences();

    // PERFORMANCE FIX: Removed Update() method - now uses event-driven system
    // References are updated when GameManager calls RegisterPlanet/RegisterMoon
    // or when explicitly called via UpdateReferences()

    public void UpdateReferences()
    {
        planet = GameManager.Instance?.planetGenerator ?? FindAnyObjectByType<PlanetGenerator>();
        moon = GameManager.Instance?.moonGenerator ?? FindAnyObjectByType<MoonGenerator>();
        
        // Update multi-planet references
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            UpdateMultiPlanetReferences();
        }
        
        ClearAllCaches();
    }
    
    /// <summary>
    /// Register a planet generator for multi-planet support
    /// </summary>
    public void RegisterPlanet(int planetIndex, PlanetGenerator planetGen)
    {
        planets[planetIndex] = planetGen;
        Debug.Log($"[TileDataHelper] Registered planet {planetIndex}");
    }
    
    /// <summary>
    /// Register a moon generator for multi-planet support
    /// </summary>
    public void RegisterMoon(int planetIndex, MoonGenerator moonGen)
    {
        moons[planetIndex] = moonGen;
        Debug.Log($"[TileDataHelper] Registered moon for planet {planetIndex}");
    }
    
    /// <summary>
    /// Update references for multi-planet system
    /// </summary>
    private void UpdateMultiPlanetReferences()
    {
        // Get all planet generators from GameManager
        for (int i = 0; i < GameManager.Instance.maxPlanets; i++)
        {
            var planetGen = GameManager.Instance.GetPlanetGenerator(i);
            if (planetGen != null && !planets.ContainsKey(i))
            {
                RegisterPlanet(i, planetGen);
            }
        }
    }

    private void CacheTileData(int tileIndex, HexTileData data, bool isMoon)
    {
        tileDataCache[tileIndex] = new CachedTileData
        {
            tileData = data,
            isMoonTile = isMoon,
            lastUpdateFrame = Time.frameCount
        };
    }

    public (HexTileData tileData, bool isMoonTile) GetTileData(int tileIndex)
    {
        if (tileDataCache.TryGetValue(tileIndex, out var cached) &&
            Time.frameCount - cached.lastUpdateFrame < CACHE_MAX_AGE)
            return (cached.tileData, cached.isMoonTile);

        // Multi-planet system support - check current active planet first
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            int currentPlanetIndex = GameManager.Instance.currentPlanetIndex;
            
            // Check current planet
            if (planets.TryGetValue(currentPlanetIndex, out var currentPlanet))
            {
                HexTileData data = currentPlanet?.GetHexTileData(tileIndex);
                if (data != null)
                {
                    CacheTileData(tileIndex, data, false);
                    return (data, false);
                }
            }
            
            // Check current moon
            if (moons.TryGetValue(currentPlanetIndex, out var currentMoon))
            {
                HexTileData data = currentMoon?.GetHexTileData(tileIndex);
                if (data != null)
                {
                    CacheTileData(tileIndex, data, true);
                    return (data, true);
                }
            }
            
            return (null, false);
        }

        // Legacy single planet/moon support
        HexTileData legacyData = planet?.GetHexTileData(tileIndex);
        if (legacyData != null)
        {
            CacheTileData(tileIndex, legacyData, false);
            return (legacyData, false);
        }

        legacyData = moon?.GetHexTileData(tileIndex);
        if (legacyData != null)
        {
            CacheTileData(tileIndex, legacyData, true);
            return (legacyData, true);
        }

        return (null, false);
    }

    public void SetTileData(int tileIndex, HexTileData tileData)
    {
        bool isMoon = false;
        
        // Multi-planet system support
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            int currentPlanetIndex = GameManager.Instance.currentPlanetIndex;
            
            // Check if tile exists on current moon first
            if (moons.TryGetValue(currentPlanetIndex, out var currentMoon) && 
                currentMoon?.GetHexTileData(tileIndex) != null)
            {
                currentMoon.SetHexTileData(tileIndex, tileData);
                isMoon = true;
            }
            // Otherwise set on current planet
            else if (planets.TryGetValue(currentPlanetIndex, out var currentPlanet))
            {
                currentPlanet?.SetHexTileData(tileIndex, tileData);
                isMoon = false;
            }
        }
        else
        {
            // Legacy single planet/moon support
            isMoon = moon?.GetHexTileData(tileIndex) != null;
            if (isMoon) moon?.SetHexTileData(tileIndex, tileData);
            else planet?.SetHexTileData(tileIndex, tileData);
        }

        tileDataCache[tileIndex] = new CachedTileData
        {
            tileData = tileData,
            isMoonTile = isMoon,
            lastUpdateFrame = Time.frameCount
        };
    }

    public void SetTileOccupant(int tileIndex, GameObject occupant)
    {
        var (data, isMoon) = GetTileData(tileIndex);
        if (data == null) return;

        // If setting to null, allow clearing occupant unconditionally
        if (occupant == null)
        {
            data.occupantId = 0;
            SetTileData(tileIndex, data);
            return;
        }

        // If this tile has an improvement owner (e.g., a fort), prevent other civs from occupying it
        if (data.improvementOwner != null)
        {
            Civilization unitOwner = null;
            var cu = occupant.GetComponent<CombatUnit>();
            if (cu != null) unitOwner = cu.owner;
            else
            {
                var wu = occupant.GetComponent<WorkerUnit>();
                if (wu != null) unitOwner = wu.owner;
            }

            if (unitOwner != null && unitOwner != data.improvementOwner)
            {
                Debug.LogWarning($"Prevented {occupant.name} (owner={unitOwner?.civData?.civName}) from occupying tile {tileIndex} owned by {data.improvementOwner.civData?.civName}.");
                return;
            }
        }

        data.occupantId = occupant ? occupant.GetInstanceID() : 0;
        SetTileData(tileIndex, data);
    }

    public void ClearTileOccupant(int tileIndex) => SetTileOccupant(tileIndex, null);

    public int[] GetTileNeighbors(int tileIndex)
    {
        if (adjacencyCache.TryGetValue(tileIndex, out var cached)) return cached;

        // Multi-planet system support
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            int currentPlanetIndex = GameManager.Instance.currentPlanetIndex;
            
            // Try current planet first
            if (planets.TryGetValue(currentPlanetIndex, out var currentPlanet))
            {
                var planetGrid = currentPlanet?.Grid;
                if (planetGrid != null && tileIndex >= 0 && tileIndex < planetGrid.neighbors.Length)
                {
                    var neighborsList = planetGrid.neighbors[tileIndex];
                    var neighbors = neighborsList?.ToArray() ?? new int[0];
                    adjacencyCache[tileIndex] = neighbors;
                    return neighbors;
                }
            }
            
            // Try current moon if planet failed
            if (moons.TryGetValue(currentPlanetIndex, out var currentMoon))
            {
                var moonGrid = currentMoon?.Grid;
                if (moonGrid != null && tileIndex >= 0 && tileIndex < moonGrid.neighbors.Length)
                {
                    var neighborsList = moonGrid.neighbors[tileIndex];
                    var neighbors = neighborsList?.ToArray() ?? new int[0];
                    adjacencyCache[tileIndex] = neighbors;
                    return neighbors;
                }
            }
            
            return new int[0];
        }

        // Legacy single planet support
        var legacyGrid = planet?.Grid;
        if (legacyGrid != null && tileIndex >= 0 && tileIndex < legacyGrid.neighbors.Length)
        {
            var neighborsList = legacyGrid.neighbors[tileIndex];
            var neighbors = neighborsList?.ToArray() ?? new int[0];
            adjacencyCache[tileIndex] = neighbors;
            return neighbors;
        }

        return new int[0];
    }

    public Vector3 GetTileCenter(int tileIndex)
    {
        if (tileCenterCache.TryGetValue(tileIndex, out var cached)) return cached;

        Vector3 pos = Vector3.zero;
        
        // Multi-planet system support
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            int currentPlanetIndex = GameManager.Instance.currentPlanetIndex;
            
            // Try current planet first
            if (planets.TryGetValue(currentPlanetIndex, out var currentPlanet))
            {
                var planetGrid = currentPlanet?.Grid;
                if (planetGrid != null && tileIndex >= 0 && tileIndex < planetGrid.tileCenters.Length)
                {
                    pos = planetGrid.tileCenters[tileIndex];
                    tileCenterCache[tileIndex] = pos;
                    return pos;
                }
            }
            
            // Try current moon if planet failed
            if (moons.TryGetValue(currentPlanetIndex, out var currentMoon))
            {
                var moonGrid = currentMoon?.Grid;
                if (moonGrid != null && tileIndex >= 0 && tileIndex < moonGrid.tileCenters.Length)
                {
                    pos = moonGrid.tileCenters[tileIndex];
                    tileCenterCache[tileIndex] = pos;
                    return pos;
                }
            }
        }
        else
        {
            // Legacy single planet support
            var legacyGrid = planet?.Grid;
            if (legacyGrid != null && tileIndex >= 0 && tileIndex < legacyGrid.tileCenters.Length)
            {
                pos = legacyGrid.tileCenters[tileIndex];
            }
        }
        
        tileCenterCache[tileIndex] = pos;
        return pos;
    }

    /// <summary>
    /// Returns the world position of a tile taking height displacement into account.
    /// An optional offset can be supplied to place objects slightly above the surface.
    /// </summary>
    public Vector3 GetTileSurfacePosition(int tileIndex, float unitOffset = 0f)
    {
        var (tileData, isMoon) = GetTileData(tileIndex);

        if (isMoon && moon != null)
        {
            var tileDir = moon.Grid.tileCenters[tileIndex].normalized;
            float radius = moon.Grid.Radius;
            
            // Get elevation from moon's tile elevation data
            float elevation = moon.GetTileElevation(tileIndex);
            float elevationScale = radius * 0.1f; // Same scale as used in generation
            
            return moon.transform.TransformPoint(tileDir * (radius + elevation * elevationScale + unitOffset));
        }
        else if (planet != null)
        {
            var tileDir = planet.Grid.tileCenters[tileIndex].normalized;
            float radius = planet.Grid.Radius;
            
            // Get elevation from planet's tile elevation data
            float elevation = planet.GetTileElevation(tileIndex);
            if (tileData != null && tileData.isHill)
            {
                elevation += planet.hillElevationBoost; // Add hill boost if it's a hill tile
            }
            float elevationScale = radius * 0.1f; // Same scale as used in generation
            
            return planet.transform.TransformPoint(tileDir * (radius + elevation * elevationScale + unitOffset));
        }

        // Fallback to default center if no generator references exist
        return GetTileCenter(tileIndex);
    }

    /// <summary>
    /// Gets the surface position of a tile with optional height offset and specific planet
    /// </summary>
    public Vector3 GetTileSurfacePosition(int tileIndex, float heightOffset = 0f, int planetIndex = -1)
    {
        // If no planet specified, use current planet
        if (planetIndex < 0)
            planetIndex = GameManager.Instance?.currentPlanetIndex ?? 0;
        
        // Get the appropriate planet generator
        var planetGen = GameManager.Instance?.GetPlanetGenerator(planetIndex);
        if (planetGen == null || planetGen.Grid == null)
        {
            Debug.LogError($"Could not find planet generator for planet {planetIndex}");
            return Vector3.zero;
        }
        
        // Get tile center and calculate surface position
        Vector3 tileCenter = planetGen.Grid.tileCenters[tileIndex];
        Vector3 planetCenter = planetGen.transform.position;
        Vector3 surfaceNormal = (tileCenter - planetCenter).normalized;
        
        // Get elevation from planet's tile elevation data
        float elevation = planetGen.GetTileElevation(tileIndex);
        var tileData = planetGen.GetHexTileData(tileIndex);
        if (tileData != null && tileData.isHill)
        {
            elevation += planetGen.hillElevationBoost; // Add hill boost if it's a hill tile
        }
        float elevationScale = planetGen.Grid.Radius * 0.1f; // Same scale as used in generation
        
        // Apply height offset along the surface normal
        return planetGen.transform.TransformPoint(surfaceNormal * (planetGen.Grid.Radius + elevation * elevationScale + heightOffset));
    }

    public bool IsTileAccessible(int tileIndex, bool mustBeLand, int movePoints, int unitID, bool allowMoon = false)
    {
        var (data, isMoon) = GetTileData(tileIndex);
        if (data == null) return false;

        if (isMoon && !allowMoon) return false;
        if (mustBeLand && !data.isLand) return false;

        int cost = BiomeHelper.GetMovementCost(data.biome);
        if (movePoints < cost) return false;

        return data.occupantId == 0 || data.occupantId == unitID;
    }

    public float GetTileDistance(int index1, int index2)
    {
        return Vector3.Distance(GetTileCenter(index1), GetTileCenter(index2));
    }

    /// <summary>
    /// Returns all tile indices within the specified number of steps from the
    /// start tile using breadth-first traversal of neighbor links.
    /// </summary>
    public List<int> GetTilesWithinSteps(int startIndex, int steps)
    {
        var result = new List<int>();
        if (steps <= 0)
            return result;

        HashSet<int> visited = new() { startIndex };
        Queue<(int idx, int depth)> queue = new();
        queue.Enqueue((startIndex, 0));

        while (queue.Count > 0)
        {
            var (idx, depth) = queue.Dequeue();
            if (depth >= steps) continue;

            foreach (int neigh in GetTileNeighbors(idx))
            {
                if (visited.Add(neigh))
                {
                    result.Add(neigh);
                    queue.Enqueue((neigh, depth + 1));
                }
            }
        }

        return result;
    }

    public void ClearAllCaches()
    {
        tileDataCache.Clear();
        adjacencyCache.Clear();
        tileCenterCache.Clear();
        planetTileDataCache.Clear();
    }
    
    /// <summary>
    /// Clear caches when switching between planets - called by MinimapController
    /// </summary>
    public void OnPlanetSwitch()
    {
        ClearAllCaches();
        Debug.Log("[TileDataHelper] Cleared caches for planet switch");
    }
    
    /// <summary>
    /// Get tile data from a specific planet (for multi-planet operations)
    /// </summary>
    public (HexTileData tileData, bool isMoonTile) GetTileDataFromPlanet(int tileIndex, int planetIndex)
    {
        // Check planet-aware cache first
        var key = (planetIndex, tileIndex);
        if (planetTileDataCache.TryGetValue(key, out var cached) &&
            Time.frameCount - cached.lastUpdateFrame < CACHE_MAX_AGE)
        {
            return (cached.tileData, cached.isMoonTile);
        }
        
        // Check planet first
        if (planets.TryGetValue(planetIndex, out var targetPlanet))
        {
            HexTileData data = targetPlanet?.GetHexTileData(tileIndex);
            if (data != null)
            {
                planetTileDataCache[key] = new CachedTileData { tileData = data, isMoonTile = false, lastUpdateFrame = Time.frameCount };
                return (data, false);
            }
        }
        
        // Check moon
        if (moons.TryGetValue(planetIndex, out var targetMoon))
        {
            HexTileData data = targetMoon?.GetHexTileData(tileIndex);
            if (data != null)
            {
                planetTileDataCache[key] = new CachedTileData { tileData = data, isMoonTile = true, lastUpdateFrame = Time.frameCount };
                return (data, true);
            }
        }
        
        return (null, false);
    }
    
    /// <summary>
    /// Get tile center from a specific planet (for multi-planet operations)
    /// </summary>
    public Vector3 GetTileCenterFromPlanet(int tileIndex, int planetIndex)
    {
        // Check planet first
        if (planets.TryGetValue(planetIndex, out var targetPlanet))
        {
            var planetGrid = targetPlanet?.Grid;
            if (planetGrid != null && tileIndex >= 0 && tileIndex < planetGrid.tileCenters.Length)
            {
                return planetGrid.tileCenters[tileIndex];
            }
        }
        
        // Check moon
        if (moons.TryGetValue(planetIndex, out var targetMoon))
        {
            var moonGrid = targetMoon?.Grid;
            if (moonGrid != null && tileIndex >= 0 && tileIndex < moonGrid.tileCenters.Length)
            {
                return moonGrid.tileCenters[tileIndex];
            }
        }
        
        return Vector3.zero;
    }
    
    /// <summary>
    /// Set tile data on a specific planet (for multi-planet operations)
    /// </summary>
    public void SetTileDataOnPlanet(int tileIndex, HexTileData tileData, int planetIndex)
    {
        // Check if tile exists on moon first
        if (moons.TryGetValue(planetIndex, out var targetMoon) && 
            targetMoon?.GetHexTileData(tileIndex) != null)
        {
            targetMoon.SetHexTileData(tileIndex, tileData);
        }
        // Otherwise set on planet
        else if (planets.TryGetValue(planetIndex, out var targetPlanet))
        {
            targetPlanet?.SetHexTileData(tileIndex, tileData);
        }
    }

    // --- Religion helpers ---
    /// <summary>
    /// Add religious pressure to a tile and persist the change.
    /// Centralizes religion writes so callers don't need to manipulate HexTileData directly.
    /// </summary>
    public void AddReligionPressure(int tileIndex, ReligionData religion, float amount)
    {
        if (religion == null || amount == 0f) return;
        var (tileData, isMoon) = GetTileData(tileIndex);
        if (tileData == null) return;

        if (tileData.religionStatus.pressures == null)
            tileData.religionStatus.Initialize();

        tileData.religionStatus.AddPressure(religion, amount);
        SetTileData(tileIndex, tileData);
    }

    /// <summary>
    /// Returns the dominant religion for the tile, or null if none.
    /// </summary>
    public ReligionData GetDominantReligion(int tileIndex)
    {
        var (tileData, isMoon) = GetTileData(tileIndex);
        if (tileData == null) return null;
        return tileData.religionStatus.GetDominantReligion();
    }

    /// <summary>
    /// Checks whether the supplied civilization may occupy the tile.
    /// This enforces improvement ownership rules and requires the tile to be empty.
    /// Note: callers that will actually set the occupant should use SetTileOccupant to perform
    /// the final write (it performs owner checks against the specific GameObject).
    /// </summary>
    public bool CanOccupyTile(int tileIndex, Civilization civ)
    {
        var (data, isMoon) = GetTileData(tileIndex);
        if (data == null) return false;

        // If an improvement owns this tile, only that civ may occupy it
        if (data.improvementOwner != null && civ != data.improvementOwner)
            return false;

        // Require there to be no existing occupant
        return data.occupantId == 0;
    }
}
