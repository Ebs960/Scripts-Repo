using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SpaceWorldState
{
    public int starSystemId;
    public SpaceHexGrid grid;
    public List<SpaceFeatureInstance> features = new List<SpaceFeatureInstance>();
    public List<SpaceImprovementInstance> improvements = new List<SpaceImprovementInstance>();
    public List<SpaceFleet> fleets = new List<SpaceFleet>();
    public List<SpaceConstructionOrder> constructionOrders = new List<SpaceConstructionOrder>();
    public int nextFeatureId = 1;
    public int nextImprovementId = 1;
    public int nextConstructionOrderId = 1;
}

public class SpaceWorldManager : MonoBehaviour
{
    public static SpaceWorldManager Instance { get; private set; }
    public SpaceWorldState CurrentSystem { get; private set; }
    public SpaceEntityIndex Entities { get; } = new SpaceEntityIndex();
    public SpaceHexGrid Grid => CurrentSystem != null ? CurrentSystem.grid : null;
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (CurrentSystem == null) CreateSystem(0, 12, 5f);
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public void CreateSystem(int starSystemId, int radius = 12, float tileSize = 5f)
    {
        CurrentSystem = new SpaceWorldState { starSystemId = starSystemId, grid = new SpaceHexGrid(radius, tileSize) };
        Entities.Rebuild(CurrentSystem);
    }
    public void LoadSystem(SpaceWorldState state) { CurrentSystem = state ?? new SpaceWorldState { grid = new SpaceHexGrid() }; Entities.Rebuild(CurrentSystem); }
    public SpaceWorldState SaveSystem() => CurrentSystem;
}

public class SpaceShipView : MonoBehaviour
{
    public int entityId;
    public BaseUnit Unit { get; private set; }

    public void Initialize(BaseUnit unit)
    {
        Unit = unit;
        entityId = unit != null ? unit.gameObject.GetRuntimeId() : -1;
    }
}
public class SpaceFeatureView : MonoBehaviour { public int entityId; }
public class SpaceImprovementView : MonoBehaviour { public int entityId; }
public class SpacePlanetView : MonoBehaviour { public int entityId; }

/// <summary>Authoritative runtime lookup for non-Unity space entities owned by the current world.</summary>
public sealed class SpaceEntityIndex
{
    private readonly Dictionary<int, SpaceFeatureInstance> features = new Dictionary<int, SpaceFeatureInstance>();
    private readonly Dictionary<int, SpaceImprovementInstance> improvements = new Dictionary<int, SpaceImprovementInstance>();
    private readonly Dictionary<int, SpaceFleet> fleets = new Dictionary<int, SpaceFleet>();
    private readonly Dictionary<int, BaseUnit> ships = new Dictionary<int, BaseUnit>();

    public IReadOnlyDictionary<int, SpaceFeatureInstance> Features => features;
    public IReadOnlyDictionary<int, SpaceImprovementInstance> Improvements => improvements;
    public IReadOnlyDictionary<int, SpaceFleet> Fleets => fleets;
    public IReadOnlyDictionary<int, BaseUnit> Ships => ships;

    public void Rebuild(SpaceWorldState state)
    {
        features.Clear(); improvements.Clear(); fleets.Clear(); ships.Clear();
        if (state == null) return;
        if (state.features != null) foreach (var value in state.features) if (value != null) features[value.instanceId] = value;
        if (state.improvements != null) foreach (var value in state.improvements) if (value != null) improvements[value.instanceId] = value;
        if (state.fleets != null) foreach (var value in state.fleets) if (value != null) fleets[value.fleetId] = value;
    }

    public void Register(SpaceFeatureInstance value) { if (value != null) features[value.instanceId] = value; }
    public void Register(SpaceImprovementInstance value) { if (value != null) improvements[value.instanceId] = value; }
    public void Register(SpaceFleet value) { if (value != null) fleets[value.fleetId] = value; }
    public void Register(BaseUnit value) { if (value != null) ships[value.gameObject.GetRuntimeId()] = value; }
    public void RemoveShip(int id) { ships.Remove(id); }
    public void RemoveFleet(int id) { fleets.Remove(id); }
    public bool TryGetImprovement(int id, out SpaceImprovementInstance value) => improvements.TryGetValue(id, out value);
    public bool TryGetFeature(int id, out SpaceFeatureInstance value) => features.TryGetValue(id, out value);
    public bool TryGetFleet(int id, out SpaceFleet value) => fleets.TryGetValue(id, out value);
    public bool TryGetShip(int id, out BaseUnit value) => ships.TryGetValue(id, out value);
}

/// <summary>Composition root for space runtime services and their initialization order.</summary>
public class SpaceSystems : MonoBehaviour
{
    public static SpaceSystems Instance { get; private set; }

    [SerializeField] private SpaceWorldManager world;
    [SerializeField] private SpaceShipMovementController movement;
    [SerializeField] private SpaceCombatManager combat;
    [SerializeField] private SpaceFleetManager fleets;
    [SerializeField] private SpaceMissionManager missions;
    [SerializeField] private SpaceFeatureManager features;
    [SerializeField] private SpaceVisionManager vision;
    [SerializeField] private SpaceRepairManager repairs;
    [SerializeField] private SpaceOccupancyManager occupancy;
    [SerializeField] private SpaceRouteManager routes;

    public SpaceWorldManager World => world;
    public SpaceShipMovementController Movement => movement;
    public SpaceCombatManager Combat => combat;
    public SpaceFleetManager Fleets => fleets;
    public SpaceMissionManager Missions => missions;
    public SpaceFeatureManager Features => features;
    public SpaceVisionManager Vision => vision;
    public SpaceRepairManager Repairs => repairs;
    public SpaceOccupancyManager Occupancy => occupancy;
    public SpaceRouteManager Routes => routes;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        world = Resolve(world);
        movement = Resolve(movement);
        combat = Resolve(combat);
        fleets = Resolve(fleets);
        missions = Resolve(missions);
        features = Resolve(features);
        vision = Resolve(vision);
        repairs = Resolve(repairs);
        occupancy = Resolve(occupancy);
        routes = Resolve(routes);
        if (world == null) Debug.LogError("[SpaceSystems] A SpaceWorldManager reference is required.", this);
    }

    private T Resolve<T>(T assigned) where T : Component
    {
        if (assigned != null) return assigned;
        var child = GetComponentInChildren<T>(true);
        return child != null ? child : FindAnyObjectByType<T>(FindObjectsInactive.Include);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
