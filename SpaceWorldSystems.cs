using System;
using System.Collections.Generic;
using System.Linq;
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
    [SerializeField] private SpaceImprovementData[] improvementCatalog = Array.Empty<SpaceImprovementData>();
    public SpaceConstructionManager Construction { get; private set; }
    public SpaceHexGrid Grid => CurrentSystem != null ? CurrentSystem.grid : null;
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Construction = FindAnyObjectByType<SpaceConstructionManager>(FindObjectsInactive.Include);
        if (Construction == null) Construction = gameObject.AddComponent<SpaceConstructionManager>();
        foreach (var data in improvementCatalog ?? Array.Empty<SpaceImprovementData>()) SpaceImprovementRegistry.Register(data);
        foreach (var data in Resources.LoadAll<SpaceImprovementData>("")) SpaceImprovementRegistry.Register(data);
        if (CurrentSystem == null) CreateSystem(0, 12, 5f);
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public void CreateSystem(int starSystemId, int radius = 12, float tileSize = 5f)
    {
        LoadSystem(new SpaceWorldState { starSystemId = starSystemId, grid = new SpaceHexGrid(radius, tileSize) });
    }
    public void LoadSystem(SpaceWorldState state)
    {
        CurrentSystem = state ?? new SpaceWorldState { grid = new SpaceHexGrid() };
        Entities.Rebuild(CurrentSystem);
        SpaceShipMovementController.Instance?.SetGrid(Grid);
        if (SpaceCombatManager.Instance != null) SpaceCombatManager.Instance.spaceGrid = Grid;
        var fleetDeployment = FindAnyObjectByType<FleetDeploymentManager>(FindObjectsInactive.Include);
        if (fleetDeployment != null) fleetDeployment.spaceGrid = Grid;
    }
    public SpaceWorldState SaveSystem() => CurrentSystem;

    [Serializable]
    public sealed class InfrastructureSave
    {
        public int systemId;
        public int radius;
        public float tileSize;
        public List<SpaceHexTile> tiles;
        public List<SpaceFeatureInstance> features;
        public List<SpaceImprovementInstance> improvements;
        public List<SpaceConstructionOrder> orders;
        public List<int> civilizationRuntimeIds;
        public int nextFeatureId;
        public int nextImprovementId;
        public int nextOrderId;
    }

    public InfrastructureSave CaptureInfrastructure()
    {
        var state = CurrentSystem;
        if (state?.grid == null) return null;
        foreach (var feature in state.features) feature?.BeforeSerialize();
        return new InfrastructureSave
        {
            systemId = state.starSystemId,
            radius = state.grid.radius,
            tileSize = state.grid.tileSize,
            tiles = state.grid.tiles,
            features = state.features,
            improvements = state.improvements,
            orders = state.constructionOrders,
            civilizationRuntimeIds = CivilizationManager.Instance?.GetAllCivs()
                ?.Select(civ => civ != null ? civ.gameObject.GetRuntimeId() : -1).ToList(),
            nextFeatureId = state.nextFeatureId,
            nextImprovementId = state.nextImprovementId,
            nextOrderId = state.nextConstructionOrderId
        };
    }

    public void RestoreInfrastructure(InfrastructureSave save)
    {
        if (save == null)
        {
            CreateSystem(CurrentSystem?.starSystemId ?? 0, Grid?.radius ?? 12, Grid?.tileSize ?? 5f);
            return;
        }

        // Restore from a copy so reopening the same save never remaps or mutates the input twice.
        save = JsonUtility.FromJson<InfrastructureSave>(JsonUtility.ToJson(save));
        var grid = new SpaceHexGrid(save.radius, save.tileSize);
        var civilizations = CivilizationManager.Instance?.GetAllCivs();
        int RemapOwner(int oldId)
        {
            if (oldId == -1 || civilizations == null) return -1;
            int index = save.civilizationRuntimeIds?.IndexOf(oldId) ?? -1;
            return index >= 0 && index < civilizations.Count && civilizations[index] != null
                ? civilizations[index].gameObject.GetRuntimeId() : -1;
        }

        foreach (var tile in save.tiles ?? new List<SpaceHexTile>())
        {
            if (tile == null || !grid.TryGetIndex(tile.q, tile.r, out int index) || index != tile.tileIndex) continue;
            tile.controllingCivilizationId = RemapOwner(tile.controllingCivilizationId);
            tile.spacecraftIds = new List<int>();
            tile.improvementInstanceIds = new List<int>();
            grid.tiles[index] = tile;
        }

        var state = new SpaceWorldState
        {
            starSystemId = save.systemId,
            grid = grid,
            features = save.features ?? new List<SpaceFeatureInstance>(),
            improvements = save.improvements ?? new List<SpaceImprovementInstance>(),
            constructionOrders = save.orders ?? new List<SpaceConstructionOrder>(),
            nextFeatureId = Mathf.Max(1, save.nextFeatureId),
            nextImprovementId = Mathf.Max(1, save.nextImprovementId),
            nextConstructionOrderId = Mathf.Max(1, save.nextOrderId)
        };
        foreach (var improvement in state.improvements)
        {
            if (improvement == null) continue;
            state.nextImprovementId = Mathf.Max(state.nextImprovementId, improvement.instanceId + 1);
            improvement.ownerCivilizationId = RemapOwner(improvement.ownerCivilizationId);
            grid.GetTile(improvement.spaceTileIndex)?.improvementInstanceIds.Add(improvement.instanceId);
        }
        foreach (var order in state.constructionOrders)
        {
            if (order == null) continue;
            state.nextConstructionOrderId = Mathf.Max(state.nextConstructionOrderId, order.orderId + 1);
            order.ownerCivilizationId = RemapOwner(order.ownerCivilizationId);
            order.contributingUnitIds = new List<int>();
        }
        foreach (var feature in state.features)
        {
            if (feature == null) continue;
            state.nextFeatureId = Mathf.Max(state.nextFeatureId, feature.instanceId + 1);
            feature.civilizationDiscoverySaveIds = feature.civilizationDiscoverySaveIds?.Select(RemapOwner).Where(id => id != -1).ToList();
            feature.AfterDeserialize();
        }

        LoadSystem(state);
    }
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
/// <summary>Map marker for a SpaceFleet. Identifies the fleet by id so packed fleets stay selectable.</summary>
public class SpaceFleetView : MonoBehaviour { public int fleetId = -1; }
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
