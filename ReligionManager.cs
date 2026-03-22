using System.Collections.Generic;
using UnityEngine;

public class ReligionManager : MonoBehaviour
{
    public static ReligionManager Instance { get; private set; }

    [Header("Religion Data")]
    [Tooltip("All available pantheons in the game")]
    public PantheonData[] availablePantheons;
    [Tooltip("All available religions in the game")]
    public ReligionData[] availableReligions;
    
    [Header("Religion Limits")]
    [Tooltip("Maximum number of religions that can be founded in a game")]
    public int maxReligionsPerGame = 5;
    
    [Header("Pressure Settings")]
    [Tooltip("Base pressure a Holy Site adds to its tile per turn")]
    public float holySitePressurePerTurn = 10f;
    [Tooltip("Pressure decay per tile distance")]
    public float pressureDecayPerTile = 2f;
    [Tooltip("Maximum tile distance that pressure spreads")]
    public int maxPressureSpreadDistance = 6;
    
    // Track founded religions in the game
    private List<(ReligionData religion, Civilization founder)> foundedReligions = new List<(ReligionData, Civilization)>();
    
    // Reusable BFS structures to avoid per-call allocations
    private readonly Queue<(int index, int dist)> _bfsQueue = new Queue<(int, int)>();
    private readonly HashSet<int> _bfsVisited = new HashSet<int>();
    private readonly List<int> _bfsResult = new List<int>();
    // Separate BFS structures for SpreadPressure (since it runs nested inside radius iteration)
    private readonly Queue<(int index, int dist)> _spreadQueue = new Queue<(int, int)>();
    private readonly HashSet<int> _spreadVisited = new HashSet<int>();

    // References (kept for now, but neighbors/data are via TileSystem)
    private PlanetGenerator planetGenerator;
    private HexGrid grid;
    private CivilizationManager cachedCivManager; // Cached reference to avoid repeated FindAnyObjectByType calls
    
    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
    
    void Start()
    {
        // Get references
        planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
        if(planetGenerator != null)
            grid = planetGenerator.Grid;
        
        // Register for turn changes
        if(TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
    }
    
    void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
    }
    
    /// <summary>
    /// Process religious pressure during each civilization's turn
    /// </summary>
    private void HandleTurnChanged(Civilization civ, int turn)
    {
        if (civ == null || !civ.hasFoundedReligion || civ.foundedReligion == null)
            return;
            
        // Process Holy Site pressure for this civ's religion
        UpdateReligiousPressure(civ);
    }
    
    /// <summary>
    /// Updates religious pressure for all Holy Sites of a civilization
    /// </summary>
    private void UpdateReligiousPressure(Civilization civ)
    {
        // Find all Holy Sites belonging to this civilization
        foreach (City city in civ.cities)
        {
            if (city == null) continue;
            int pIndex = city.planetIndex;
            var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
            if (ts == null || !ts.IsReady()) continue;

            // Get tiles within city radius — snapshot the list since SpreadPressure
            // will reuse shared BFS structures
            var centerTileIndex = city.centerTileIndex;
            GetTilesInRadius(ts, centerTileIndex, city.TerritoryRadius);
            // Copy holy site indices before calling SpreadPressure
            var holySiteTiles = new List<int>();
            foreach (int tileIndex in _bfsResult)
            {
                if (ts.HasHolySite(tileIndex))
                    holySiteTiles.Add(tileIndex);
            }

            foreach (int tileIndex in holySiteTiles)
            {
                ts.AddReligionPressure(tileIndex, civ.foundedReligion, holySitePressurePerTurn);
                SpreadPressure(ts, tileIndex, civ.foundedReligion);
            }
        }
    }
    
    /// <summary>
    /// Gets all tile indices within a certain radius of the center tile using a breadth-first search
    /// </summary>
    private List<int> GetTilesInRadius(TileSystem ts, int centerTileIndex, int radius)
    {
        _bfsResult.Clear();
        if (ts == null || !ts.IsReady() || radius <= 0)
            return _bfsResult;
            
        _bfsQueue.Clear();
        _bfsVisited.Clear();

        _bfsQueue.Enqueue((centerTileIndex, 0));
        _bfsVisited.Add(centerTileIndex);

        while (_bfsQueue.Count > 0)
        {
            var (currentIndex, currentDist) = _bfsQueue.Dequeue();
            _bfsResult.Add(currentIndex);

            if (currentDist < radius)
            {
                var neighbors = ts.GetNeighbors(currentIndex);
                foreach (int neighbor in neighbors)
                {
                    if (!_bfsVisited.Contains(neighbor))
                    {
                        _bfsVisited.Add(neighbor);
                        _bfsQueue.Enqueue((neighbor, currentDist + 1));
                    }
                }
            }
        }
        
        return _bfsResult;
    }
    
    /// <summary>
    /// Adds religious pressure to a specific tile (handled via TileSystem)
    /// </summary>
    
    /// <summary>
    /// Spreads religious pressure from a Holy Site to nearby tiles
    /// </summary>
    private void SpreadPressure(TileSystem ts, int sourceTileIndex, ReligionData religion)
    {
        if (ts == null || !ts.IsReady())
            return;
            
        _spreadQueue.Clear();
        _spreadVisited.Clear();

        _spreadQueue.Enqueue((sourceTileIndex, 0));
        _spreadVisited.Add(sourceTileIndex);

        while (_spreadQueue.Count > 0)
        {
            var (currentIndex, currentDist) = _spreadQueue.Dequeue();

            if (currentDist > 0) // Don't apply pressure to the source tile itself
            {
                float pressure = holySitePressurePerTurn - (currentDist * pressureDecayPerTile);
                if (pressure > 0)
                {
                    ts.AddReligionPressure(currentIndex, religion, pressure);
                }
            }

            if (currentDist < maxPressureSpreadDistance)
            {
                var neighbors = ts.GetNeighbors(currentIndex);
                foreach (int neighbor in neighbors)
                {
                    if (!_spreadVisited.Contains(neighbor))
                    {
                        _spreadVisited.Add(neighbor);
                        _spreadQueue.Enqueue((neighbor, currentDist + 1));
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Register a newly founded religion
    /// </summary>
    public void RegisterFoundedReligion(ReligionData religion, Civilization founder)
    {
        if (religion == null || founder == null)
            return;
            
        // Check if max religions has been reached
        if (foundedReligions.Count >= maxReligionsPerGame)
        {
            Debug.LogWarning("Maximum number of religions already founded!");
            return;
        }
        
        // Check if this religion is already founded
        foreach (var (existingReligion, _) in foundedReligions)
        {
            if (existingReligion == religion)
            {
                Debug.LogWarning($"Religion {religion.religionName} is already founded!");
                return;
            }
        }
        
        // Register the new religion
        foundedReligions.Add((religion, founder));
}
    
    /// <summary>
    /// Get all religions that have been founded in the game
    /// </summary>
    public List<ReligionData> GetFoundedReligions()
    {
        List<ReligionData> result = new List<ReligionData>();
        foreach (var (religion, _) in foundedReligions)
        {
            result.Add(religion);
        }
        return result;
    }
    
    /// <summary>
    /// Get all pantheons that have not yet been chosen
    /// </summary>
    public List<PantheonData> GetAvailablePantheons()
    {
        List<PantheonData> result = new List<PantheonData>();
        
        // Get all civilizations in the game - use cached reference to avoid expensive FindAnyObjectByType call
        if (cachedCivManager == null)
            cachedCivManager = FindAnyObjectByType<CivilizationManager>();
        var civManager = cachedCivManager;
        if (civManager == null)
            return result;
            
        // Add all available pantheons
        if (availablePantheons != null)
        {
            result.AddRange(availablePantheons);
        }
        
        // Remove pantheons that have already been chosen by any civilization
        foreach (var civ in civManager.civilizations)
        {
            if (civ == null || civ.foundedPantheons == null) continue;
            foreach (var p in civ.foundedPantheons)
            {
                if (p != null) result.Remove(p);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Get all religions that have not yet been founded
    /// </summary>
    public List<ReligionData> GetAvailableReligions()
    {
        List<ReligionData> result = new List<ReligionData>();
        
        // Add all available religions
        if (availableReligions != null)
        {
            result.AddRange(availableReligions);
        }
        
        // Remove already founded religions
        foreach (var (religion, _) in foundedReligions)
        {
            result.Remove(religion);
        }
        
        return result;
    }
    
    /// <summary>
    /// Calculate the majority religion for a city
    /// </summary>
    public ReligionData GetCityMajorityReligion(City city)
    {
        if (city == null)
            return null;

        var ts = TileSystem.GetForPlanet(city.planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return null;
            
        // Get all tiles within city radius
        var tiles = GetTilesInRadius(ts, city.centerTileIndex, city.TerritoryRadius);
        
        // Count total pressure for each religion (using serializable pressure list)
        Dictionary<ReligionData, float> religionPressures = new Dictionary<ReligionData, float>();
        
        foreach (int tileIndex in tiles)
        {
            var list = ts.GetReligionPressures(tileIndex);
            if (list == null) continue;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.religion == null || e.pressure <= 0f) continue;
                if (!religionPressures.TryGetValue(e.religion, out var acc)) acc = 0f;
                religionPressures[e.religion] = acc + e.pressure;
            }
        }
        
        // Find the religion with the highest pressure
        ReligionData majorityReligion = null;
        float highestPressure = 0f;
        
        foreach (var kvp in religionPressures)
        {
            if (kvp.Value > highestPressure)
            {
                highestPressure = kvp.Value;
                majorityReligion = kvp.Key;
            }
        }
        
        return majorityReligion;
    }
} 