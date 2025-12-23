using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages interplanetary unit travel using AU distances and turn-based progression.
/// Units traveling in space are temporarily removed from the tile grid and tracked here.
/// </summary>
public class SpaceRouteManager : MonoBehaviour
{
    public static SpaceRouteManager Instance { get; private set; }

    [Header("Space Travel Configuration")]
    [Tooltip("Base turns required to travel 1 AU distance")]
    [Range(1, 20)]
    public int baseTurnsPerAU = 5;

    [Tooltip("Minimum turns for any space travel (even short distances)")]
    [Range(1, 5)]
    public int minimumTravelTurns = 2;

    [Header("Advanced Travel Settings")]
    [Tooltip("Technology modifier - reduces travel time as tech advances")]
    [Range(0.1f, 1.0f)]
    public float technologyModifier = 1.0f;

    [Tooltip("Unit type modifiers for different movement speeds")]
    public List<UnitTravelModifier> unitTravelModifiers = new List<UnitTravelModifier>();

    // Active space travel tasks
    private List<SpaceTravelTask> activeTravels = new List<SpaceTravelTask>();

    // Events for UI updates
    public event Action<SpaceTravelTask> OnTravelStarted;
    public event Action<SpaceTravelTask> OnTravelProgressed;
    public event Action<SpaceTravelTask> OnTravelCompleted;

    [System.Serializable]
    public struct UnitTravelModifier
    {
        [Tooltip("Name/type of unit (e.g., 'Spaceship', 'Colony Ship')")]
        public string unitTypeName;
        
        [Tooltip("Speed multiplier (higher = faster travel)")]
        [Range(0.1f, 3.0f)]
        public float speedMultiplier;
    }

    [System.Serializable]
    public struct SpaceTravelTask
    {
        public int taskId;                    // Unique identifier
        public GameObject travelingUnit;      // The unit GameObject
        public int originPlanetIndex;        // Starting planet
        public int destinationPlanetIndex;   // Target planet
        public float totalDistance;          // Distance in AU
        public int totalTurns;              // Total turns needed
        public int turnsRemaining;          // Turns left
        public DateTime departureTime;       // When travel started (for save/load)
        public string unitName;             // Unit display name
        public string unitType;             // Unit type for modifiers

        public float Progress => totalTurns > 0 ? 1.0f - (float)turnsRemaining / totalTurns : 1.0f;
        public bool IsComplete => turnsRemaining <= 0;
    }

    private int nextTaskId = 1;
    
    // Cached manager references to avoid repeated FindAnyObjectByType calls
    private TurnManager _cachedTurnManager;
    private SpaceTravelStatusUI _cachedStatusUI;

    void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SpaceRouteManager] Duplicate instance detected. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Cache manager references to avoid repeated FindAnyObjectByType calls
        _cachedTurnManager = FindAnyObjectByType<TurnManager>();
        _cachedStatusUI = FindAnyObjectByType<SpaceTravelStatusUI>();
        
        // Subscribe to turn progression
        var turnManager = _cachedTurnManager;
        if (turnManager != null)
        {
            // Hook into turn end event (you'll need to expose this in TurnManager)
            Debug.Log("[SpaceRouteManager] Connected to TurnManager for space travel progression");
        }
        else
        {
            Debug.LogWarning("[SpaceRouteManager] TurnManager not found! Space travel won't progress automatically.");
        }
    }

    /// <summary>
    /// Start interplanetary travel for a unit (spaceships only)
    /// </summary>
    public bool StartSpaceTravel(GameObject unit, int fromPlanetIndex, int toPlanetIndex)
    {
        if (unit == null)
        {
            Debug.LogError("[SpaceRouteManager] Cannot start travel - unit is null");
            return false;
        }

        // CRITICAL: Only spaceships can travel through space
        var combatUnit = unit.GetComponent<CombatUnit>();
    if (combatUnit == null || combatUnit.data.unitType != CombatCategory.Spaceship)
        {
            Debug.LogWarning($"[SpaceRouteManager] Only spaceships can travel through space! Unit {unit.name} is not a spaceship.");
            return false;
        }

        bool isPlanetMoonRoute = IsPlanetMoonPair(fromPlanetIndex, toPlanetIndex);

        // Travel capability gating (stubs for future systems)
        switch (combatUnit.data.travelCapability)
        {
            case TravelCapability.OrbitOnly:
                Debug.LogWarning("[SpaceRouteManager] This ship can only enter orbit. Interplanetary travel is not permitted (stub).");
                return false;
            case TravelCapability.PlanetAndMoon:
                // Allow planet <-> moon only in current system
                if (!isPlanetMoonRoute)
                {
                    Debug.LogWarning("[SpaceRouteManager] This ship can only travel between a planet and its moon (stub).");
                    return false;
                }
                break;
            case TravelCapability.Interplanetary:
                // Supported: within same solar system
                break;
            case TravelCapability.Interstellar:
                Debug.LogWarning("[SpaceRouteManager] Interstellar travel not implemented yet (stub).");
                return false;
            case TravelCapability.Intergalactic:
                Debug.LogWarning("[SpaceRouteManager] Intergalactic travel not implemented yet (stub).");
                return false;
        }

        if (fromPlanetIndex == toPlanetIndex && !isPlanetMoonRoute)
        {
            Debug.LogWarning("[SpaceRouteManager] Cannot travel to the same planet");
            return false;
        }

        // Validate speed > 0 (engines online)
        var cu = unit.GetComponent<CombatUnit>();
        float unitBaseSpeed = (cu != null && cu.data != null) ? cu.data.spaceAUPerTurn : 0f;
        if (unitBaseSpeed <= 0f)
        {
            Debug.LogWarning("[SpaceRouteManager] Cannot start space travel - ship speed is 0 AU/turn (engines offline).");
            return false;
        }

        // Get planet data
        var planetData = GameManager.Instance?.GetPlanetData();
        if (planetData == null || !planetData.ContainsKey(fromPlanetIndex) || !planetData.ContainsKey(toPlanetIndex))
        {
            Debug.LogError($"[SpaceRouteManager] Invalid planet indices: {fromPlanetIndex} -> {toPlanetIndex}");
            return false;
        }

        // Calculate distance and travel time
        float distance = CalculateDistanceBetweenPlanets(fromPlanetIndex, toPlanetIndex);
        int travelTurns = CalculateTravelTime(distance, unit);

        // Create travel task
        var task = new SpaceTravelTask
        {
            taskId = nextTaskId++,
            travelingUnit = unit,
            originPlanetIndex = fromPlanetIndex,
            destinationPlanetIndex = toPlanetIndex,
            totalDistance = distance,
            totalTurns = travelTurns,
            turnsRemaining = travelTurns,
            departureTime = DateTime.Now,
            unitName = unit.name,
            unitType = GetUnitType(unit)
        };

        // Remove unit from current planet's tile grid and start visual space travel
        StartVisualSpaceTravel(unit, fromPlanetIndex, toPlanetIndex);

        // Add to active travels
        activeTravels.Add(task);

        Debug.Log($"[SpaceRouteManager] Started space travel: {task.unitName} from Planet {fromPlanetIndex} to Planet {toPlanetIndex} ({distance:F2} AU, {travelTurns} turns)");

        // Notify listeners
        OnTravelStarted?.Invoke(task);

        // Also prompt UI status panel to update now (on launch)
        // Use cached reference to avoid expensive FindAnyObjectByType call
        if (_cachedStatusUI == null)
            _cachedStatusUI = FindAnyObjectByType<SpaceTravelStatusUI>();
        if (_cachedStatusUI != null)
        {
            _cachedStatusUI.Refresh();
        }

        return true;
    }

    /// <summary>
    /// Call this every turn to progress all active space travels
    /// </summary>
    public void ProgressAllTravels()
    {
        var completedTravels = new List<SpaceTravelTask>();

        for (int i = 0; i < activeTravels.Count; i++)
        {
            var travel = activeTravels[i];
            travel.turnsRemaining--;
            activeTravels[i] = travel; // Update the struct

            Debug.Log($"[SpaceRouteManager] {travel.unitName} space travel: {travel.turnsRemaining} turns remaining");

            // Update visual position in space
            UpdateVisualTravelProgress(travel);

            // Notify progress
            OnTravelProgressed?.Invoke(travel);

            // Check if travel is complete
            if (travel.IsComplete)
            {
                completedTravels.Add(travel);
            }
        }

        // Complete finished travels
        foreach (var completedTravel in completedTravels)
        {
            CompleteTravelTask(completedTravel);
        }

        // Ensure status UI updates each turn to reflect new ETAs
        // Use cached reference to avoid expensive FindAnyObjectByType call
        if (_cachedStatusUI == null)
            _cachedStatusUI = FindAnyObjectByType<SpaceTravelStatusUI>();
        if (_cachedStatusUI != null)
        {
            _cachedStatusUI.Refresh();
        }
    }

    private void CompleteTravelTask(SpaceTravelTask task)
    {
        Debug.Log($"[SpaceRouteManager] Completing travel: {task.unitName} arriving at Planet {task.destinationPlanetIndex}");

        // Remove from active travels
        activeTravels.Remove(task);

        // Place unit on destination planet
        PlaceUnitOnPlanet(task.travelingUnit, task.destinationPlanetIndex);

        // Notify completion
        OnTravelCompleted?.Invoke(task);

        Debug.Log($"[SpaceRouteManager] {task.unitName} successfully arrived at Planet {task.destinationPlanetIndex}");
    }

    private float CalculateDistanceBetweenPlanets(int planetA, int planetB)
    {
        var planetData = GameManager.Instance.GetPlanetData();
        
        Vector3 posA = planetData[planetA].worldPosition;
        Vector3 posB = planetData[planetB].worldPosition;

        // Calculate 3D distance and convert to AU
        float distanceUnits = Vector3.Distance(posA, posB);
        
        // Your world positions might already be in AU scale, but if not, adjust here
        // For now, assume world positions are already scaled appropriately
        return distanceUnits;
    }

    // Helper to recognize planet <-> moon travel within current system
    private bool IsPlanetMoonPair(int fromPlanet, int toPlanet)
    {
        var gameManager = GameManager.Instance;
        if (gameManager == null)
            return false;

        var planetData = gameManager.GetPlanetData();
        if (planetData == null || planetData.Count == 0)
            return false;

        bool MatchesMapping(int planetIndex, int possibleMoonIndex)
        {
            if (!planetData.TryGetValue(planetIndex, out var planet) || planet.moonNames == null || planet.moonNames.Count == 0)
                return false;

            if (!planetData.TryGetValue(possibleMoonIndex, out var body))
                return false;

            for (int i = 0; i < planet.moonNames.Count; i++)
            {
                string moonName = planet.moonNames[i];
                if (!string.IsNullOrEmpty(moonName) &&
                    string.Equals(moonName, body.planetName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        if (MatchesMapping(fromPlanet, toPlanet) || MatchesMapping(toPlanet, fromPlanet))
            return true;

        return false;
    }

    private int CalculateTravelTime(float distanceAU, GameObject unit)
    {
        // SPEED POLICY: Determined ONLY by unit's CombatUnitData (no global tech or route multipliers)
        var cu = unit.GetComponent<CombatUnit>();
        float speedAUPerTurn = (cu != null && cu.data != null) ? cu.data.spaceAUPerTurn : 0f;
        // If speed is 0, engines are disabled/offline: ship should not move (no auto fallback)
        if (speedAUPerTurn <= 0f)
        {
            return int.MaxValue / 2; // effectively infinite; caller prevents start with 0 speed
        }

        float turns = distanceAU / speedAUPerTurn;
        int finalTurns = Mathf.Max(minimumTravelTurns, Mathf.RoundToInt(turns));
        return finalTurns;
    }

    private string GetUnitType(GameObject unit)
    {
        // Check for common unit types
        if (unit.GetComponent<CombatUnit>() != null)
            return "Combat Unit";
        if (unit.GetComponent<WorkerUnit>() != null)
            return "Worker Unit";
        
        // You can expand this to check for specific unit types like:
        // if (unit.name.Contains("Spaceship")) return "Spaceship";
        // if (unit.name.Contains("Colony")) return "Colony Ship";
        
        return "Standard Unit";
    }

    private float GetSpeedMultiplierForUnit(string unitType)
    {
        foreach (var modifier in unitTravelModifiers)
        {
            if (string.Equals(modifier.unitTypeName, unitType, StringComparison.OrdinalIgnoreCase))
            {
                return modifier.speedMultiplier;
            }
        }
        return 1.0f; // Default speed
    }

    private void StartVisualSpaceTravel(GameObject unit, int fromPlanetIndex, int toPlanetIndex)
    {
        // Get planet positions
        var planetData = GameManager.Instance.GetPlanetData();
        Vector3 fromPos = planetData[fromPlanetIndex].worldPosition;
        Vector3 toPos = planetData[toPlanetIndex].worldPosition;

        // Position unit in space between planets (closer to origin planet initially)
        Vector3 spacePosition = Vector3.Lerp(fromPos, toPos, 0.1f);
        unit.transform.position = spacePosition;
        unit.transform.SetParent(null); // Remove from planet parent

        // Clear tile occupancy on origin planet
        var combatUnit = unit.GetComponent<CombatUnit>();
        if (combatUnit != null && combatUnit.currentTileIndex >= 0)
        {
            if (TileSystem.Instance != null) TileSystem.Instance.ClearTileOccupant(combatUnit.currentTileIndex);
        }

        // Unit remains visible but is now "in space"
        Debug.Log($"[SpaceRouteManager] {unit.name} is now traveling through space from Planet {fromPlanetIndex} to Planet {toPlanetIndex}");
    }

    private void UpdateVisualTravelProgress(SpaceTravelTask travel)
    {
        if (travel.travelingUnit == null) return;

        // Get planet positions
        var planetData = GameManager.Instance.GetPlanetData();
        if (!planetData.ContainsKey(travel.originPlanetIndex) || !planetData.ContainsKey(travel.destinationPlanetIndex))
            return;

        Vector3 fromPos = planetData[travel.originPlanetIndex].worldPosition;
        Vector3 toPos = planetData[travel.destinationPlanetIndex].worldPosition;

        // Calculate current progress (0 = at origin, 1 = at destination)
        float progress = travel.Progress;

        // Move unit along the space route
        Vector3 currentSpacePosition = Vector3.Lerp(fromPos, toPos, progress);
        travel.travelingUnit.transform.position = currentSpacePosition;

        // Orient unit to face destination
        Vector3 travelDirection = (toPos - fromPos).normalized;
        if (travelDirection != Vector3.zero)
        {
            travel.travelingUnit.transform.rotation = Quaternion.LookRotation(travelDirection);
        }
    }

    private void PlaceUnitOnPlanet(GameObject unit, int planetIndex)
    {
        // Get destination planet generator
        var planetGen = GameManager.Instance?.GetPlanetGenerator(planetIndex);
        if (planetGen == null)
        {
            Debug.LogError($"[SpaceRouteManager] Cannot place unit - Planet {planetIndex} generator not found!");
            return;
        }

        // Find a suitable landing tile (you might want to make this more sophisticated)
        var landingTile = FindLandingTile(planetGen);
        if (landingTile == -1)
        {
            Debug.LogWarning($"[SpaceRouteManager] No suitable landing tile found on Planet {planetIndex}!");
            landingTile = 0; // Default to first tile
        }

        // Position unit on the landing tile
        var tileData = planetGen.GetHexTileData(landingTile);
        if (tileData != null)
        {
            // Get tile world position and place unit
            Vector3 tileCenter = TileSystem.Instance != null ? TileSystem.Instance.GetTileSurfacePosition(landingTile, 0f, planetIndex) : planetGen.Grid.tileCenters[landingTile];
            unit.transform.position = tileCenter;
            unit.transform.SetParent(planetGen.transform);
        }

        // Reactivate the unit
        unit.SetActive(true);

        Debug.Log($"[SpaceRouteManager] Placed {unit.name} on Planet {planetIndex} at tile {landingTile}");
    }

    private int FindLandingTile(PlanetGenerator planetGen)
    {
        // Find first available land tile (not ocean)
        // You can make this more sophisticated later
        for (int i = 0; i < planetGen.data.Count; i++)
        {
            var tileData = planetGen.GetHexTileData(i);
            if (tileData != null && tileData.biome != Biome.Ocean && tileData.biome != Biome.Seas)
            {
                return i;
            }
        }
        return 0; // Fallback to first tile
    }

    // Public accessors for UI
    public List<SpaceTravelTask> GetActiveTravels() => new List<SpaceTravelTask>(activeTravels);
    
    public bool IsUnitTraveling(GameObject unit) => activeTravels.Exists(t => t.travelingUnit == unit);

    public SpaceTravelTask? GetTravelTaskForUnit(GameObject unit)
    {
        var task = activeTravels.Find(t => t.travelingUnit == unit);
        return task.travelingUnit != null ? task : null;
    }

    /// <summary>
    /// Cancel an active travel and return unit to origin planet
    /// </summary>
    public bool CancelTravel(int taskId)
    {
        var task = activeTravels.Find(t => t.taskId == taskId);
        if (task.travelingUnit == null)
        {
            Debug.LogWarning($"[SpaceRouteManager] Cannot cancel travel - task {taskId} not found");
            return false;
        }

        // Return unit to origin planet
        PlaceUnitOnPlanet(task.travelingUnit, task.originPlanetIndex);
        
        // Remove from active travels
        activeTravels.Remove(task);

        Debug.Log($"[SpaceRouteManager] Cancelled travel for {task.unitName}, returned to Planet {task.originPlanetIndex}");
        return true;
    }

    /// <summary>
    /// Get visual space position for a traveling unit (useful for camera tracking)
    /// </summary>
    public Vector3? GetUnitSpacePosition(GameObject unit)
    {
        var task = activeTravels.Find(t => t.travelingUnit == unit);
        if (task.travelingUnit == null) return null;

        return task.travelingUnit.transform.position;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
