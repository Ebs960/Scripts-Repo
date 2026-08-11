using System.Collections.Generic;
using UnityEngine;

public class ReligionManager : MonoBehaviour
{
    public static ReligionManager Instance { get; private set; }

    [Header("Religion Data")]
    [Tooltip("Optional database that supplies all pantheons, religions, and beliefs without relying on Resources folders.")]
    public ReligionDatabase religionDatabase;
    
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

        ApplyReligionDatabase();
    }

    private void OnValidate()
    {
        ApplyReligionDatabase();
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

    private void ApplyReligionDatabase()
    {
        ResourceCache.SetReligionDatabase(religionDatabase);
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
        if (civ == null)
            return;
            
        // Process Holy Site pressure for this civ's religion
        UpdateReligiousPressure(civ);
    }
    
    /// <summary>
    /// Updates religious pressure for all Holy Sites of a civilization
    /// </summary>
    private void UpdateReligiousPressure(Civilization civ)
    {
        if (civ == null || civ.cities == null) return;
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
                // Holy sites carry their own faith: capture never silently converts them.
                var sourceReligion = ts.GetHolySiteReligion(tileIndex);
                if (sourceReligion == null) continue;
                ts.AddReligionPressure(tileIndex, sourceReligion, holySitePressurePerTurn);
                SpreadPressure(ts, tileIndex, sourceReligion);
            }

            // A stable city majority is a weaker bounded source, allowing adopted foreign faiths to spread.
            var majority = GetCityMajorityReligion(city);
            if (majority != null)
            {
                ts.AddReligionPressure(centerTileIndex, majority, holySitePressurePerTurn * .2f);
                SpreadPressure(ts, centerTileIndex, majority, .2f);
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
    private void SpreadPressure(TileSystem ts, int sourceTileIndex, ReligionData religion, float strength = 1f)
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
                float pressure = (holySitePressurePerTurn - (currentDist * pressureDecayPerTile)) * strength;
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
        var allPantheons = ResourceCache.GetAllPantheonData();
        
        // Get all civilizations in the game - use cached reference to avoid expensive FindAnyObjectByType call
        if (cachedCivManager == null)
            cachedCivManager = FindAnyObjectByType<CivilizationManager>();
        var civManager = cachedCivManager;
        if (civManager == null)
            return result;
            
        // Add all available pantheons
        if (allPantheons != null)
        {
            foreach (var pantheon in allPantheons)
                if (pantheon != null && pantheon.IsSpirit) result.Add(pantheon);
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
    /// Get all religions that have not yet been founded and are currently available to the given civilization.
    /// If no civilization is supplied, the method falls back to the default availability rules.
    /// </summary>
    public List<ReligionData> GetAvailableReligions(Civilization civ = null)
    {
        List<ReligionData> result = new List<ReligionData>();
        var allReligions = ResourceCache.GetAllReligionData();
        
        if (allReligions != null)
        {
            foreach (var religion in allReligions)
            {
                if (!CanFoundReligion(civ, religion, null, false).CanFound) continue;
                result.Add(religion);
            }
        }
        
        return result;
    }

    public bool IsReligionAvailableToCivilization(ReligionData religion, Civilization civ)
    {
        return CanFoundReligion(civ, religion, null, false).CanFound;
    }

    public ReligionAvailabilityResult CanFoundReligion(Civilization civ, ReligionData religion, City holySiteCity = null, bool requireHolySite = false)
    {
        if (religion == null) return ReligionAvailabilityResult.Fail("Religion is null.");
        if (IsReligionFounded(religion)) return ReligionAvailabilityResult.Fail("Religion is already founded.");
        if (foundedReligions.Count >= maxReligionsPerGame) return ReligionAvailabilityResult.Fail("The global religion limit has been reached.");
        if (civ == null) return ReligionAvailabilityResult.Success;
        if (civ.hasFoundedReligion || civ.foundedReligion != null) return ReligionAvailabilityResult.Fail("Civilization already founded a religion.");

        if (religion.requiredCultures != null && religion.requiredCultures.Length > 0)
        {
            if (civ.researchedCultures == null)
                return ReligionAvailabilityResult.Fail("Required cultures are missing.");

            foreach (var requiredCulture in religion.requiredCultures)
            {
                if (requiredCulture == null) continue;
                if (!civ.researchedCultures.Contains(requiredCulture))
                    return ReligionAvailabilityResult.Fail($"Missing culture: {requiredCulture.name}.");
            }
        }
        if (religion.useMinimumAge && civ.GetCurrentAge() < religion.minimumAge)
            return ReligionAvailabilityResult.Fail($"Requires {religion.minimumAge}.");
        var pantheons = civ.foundedPantheons;
        bool pantheonMet = religion.pantheonRequirementMode == PantheonRequirementMode.None
            || (pantheons != null && religion.pantheonRequirementMode == PantheonRequirementMode.Any && pantheons.Count > 0)
            || (pantheons != null && religion.pantheonRequirementMode == PantheonRequirementMode.MinimumTier && pantheons.Exists(p => p != null && p.tier >= religion.minimumPantheonTier))
            || (pantheons != null && religion.pantheonRequirementMode == PantheonRequirementMode.Specific && religion.compatiblePantheons != null && pantheons.Exists(p => System.Array.IndexOf(religion.compatiblePantheons, p) >= 0));
        if (!pantheonMet) return ReligionAvailabilityResult.Fail("Pantheon requirement is not met.");
        if (civ.faith < religion.faithCost) return ReligionAvailabilityResult.Fail("Insufficient Faith.");
        if (requireHolySite)
        {
            if (holySiteCity == null) return ReligionAvailabilityResult.Fail("A Holy Site city is required.");
            var ts = TileSystem.GetForPlanet(holySiteCity.planetIndex) ?? TileSystem.Instance;
            if (ts?.GetTileData(holySiteCity.centerTileIndex)?.HasHolySite != true)
                return ReligionAvailabilityResult.Fail("The selected city has no Holy Site.");
        }
        return ReligionAvailabilityResult.Success;
    }

    private bool IsReligionFounded(ReligionData religion)
    {
        foreach (var (existingReligion, _) in foundedReligions)
        {
            if (existingReligion == religion)
                return true;
        }

        return false;
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

public readonly struct ReligionAvailabilityResult
{
    public bool CanFound { get; }
    public string FailureReason { get; }
    private ReligionAvailabilityResult(bool canFound, string reason) { CanFound = canFound; FailureReason = reason; }
    public static ReligionAvailabilityResult Success => new ReligionAvailabilityResult(true, null);
    public static ReligionAvailabilityResult Fail(string reason) => new ReligionAvailabilityResult(false, reason);
}
