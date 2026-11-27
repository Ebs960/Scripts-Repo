using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Compatibility facade for old callers. It no longer stores turn countdowns,
/// AU distances, DateTime gameplay progress, or hidden travelling units; orders are
/// forwarded to SpaceShipMovementController as queued hex movement.
/// </summary>
public class SpaceRouteManager : MonoBehaviour
{
    public static SpaceRouteManager Instance { get; private set; }
    public event Action<SpaceTravelTask> OnTravelStarted;
    public event Action<SpaceTravelTask> OnTravelProgressed;
    public event Action<SpaceTravelTask> OnTravelCompleted;

    [Serializable]
    public struct SpaceTravelTask
    {
        public int taskId;
        public GameObject travelingUnit;
        public int originPlanetIndex;
        public int destinationPlanetIndex;
        public int originSpaceTileIndex;
        public int destinationSpaceTileIndex;
        public string unitName;
        public string unitType;
        public int totalHexes;
        public int remainingHexes;
        internal bool arrived;
        public float Progress => totalHexes > 0 ? Mathf.Clamp01(1f - (float)remainingHexes / totalHexes) : (IsComplete ? 1f : 0f);
        public bool IsComplete => arrived;
    }

    private readonly Dictionary<int, SpaceTravelTask> active = new Dictionary<int, SpaceTravelTask>();
    private int nextTaskId = 1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public bool TryGetRoute(GameObject unit, int fromPlanetIndex, int toPlanetIndex, out List<int> path, out int destination)
    {
        path = null;
        destination = -1;
        var ship = unit != null ? unit.GetComponent<CombatUnit>() : null;
        if (ship == null || ship.data == null || ship.data.unitType != CombatCategory.Spaceship ||
            ship.currentHealth <= 0 || ship.isPackedInSpaceFleet) return false;

        var world = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Include);
        if (world == null) return false;
        if (world.Grid == null || world.Grid != SpaceWorldManager.Instance?.Grid) world.RebuildSpaceMap();

        int GetOrbitSector(int planetIndex)
        {
            int anchor = world.GetPlanetAnchorTile(planetIndex);
            int body = world.Grid.GetTile(anchor)?.planetId ?? -1;
            if (body < 0) return -1;
            foreach (int index in world.Grid.GetNeighbors(anchor))
            {
                var sector = world.Grid.GetTile(index);
                if (sector.isPlanetOrbitSector && sector.associatedPlanetId == body && !sector.blocksMovement)
                    return index;
            }
            return -1;
        }

        int originPlanet = ship.planetIndex >= 0 ? ship.planetIndex : fromPlanetIndex;
        int start = ship.currentSpaceTileIndex >= 0 ? ship.currentSpaceTileIndex : GetOrbitSector(originPlanet);
        destination = GetOrbitSector(toPlanetIndex);
        var movement = SpaceShipMovementController.Instance;
        if (start < 0 || destination < 0 || movement == null) return false;
        path = movement.FindRoute(ship, start, destination);
        return path.Count > 0;
    }

    public bool StartSpaceTravel(GameObject unit, int fromPlanetIndex, int toPlanetIndex)
    {
        if (!TryGetRoute(unit, fromPlanetIndex, toPlanetIndex, out var path, out int destination)) return false;
        var ship = unit.GetComponent<CombatUnit>();
        var movement = SpaceShipMovementController.Instance;
        int originPlanet = ship.planetIndex >= 0 ? ship.planetIndex : fromPlanetIndex;
        if (ship.currentSpaceTileIndex < 0 && !movement.PlaceOnSpaceTile(ship, path[0])) return false;

        var task = new SpaceTravelTask
        {
            taskId = nextTaskId++, travelingUnit = unit, originPlanetIndex = originPlanet,
            destinationPlanetIndex = toPlanetIndex, originSpaceTileIndex = path[0],
            destinationSpaceTileIndex = destination, unitName = unit.name, unitType = ship.data.unitName,
            totalHexes = path.Count - 1, remainingHexes = path.Count - 1
        };
        ship.queuedSpacePath = path;
        ship.queuedSpacePathCursor = 0;
        active[unit.GetRuntimeId()] = task;
        OnTravelStarted?.Invoke(task);
        movement.MoveAlongQueuedPath(ship);
        return true;
    }

    internal void ClearTravelTracking() => active.Clear();

    internal void UpdateTravel(BaseUnit ship)
    {
        int unitId = ship.gameObject.GetRuntimeId();
        if (!active.TryGetValue(unitId, out var task)) return;

        if (ship.currentSpaceTileIndex == task.destinationSpaceTileIndex)
        {
            task.remainingHexes = 0;
            task.arrived = true;
            active.Remove(unitId);
            OnTravelCompleted?.Invoke(task);
            return;
        }

        var path = ship.queuedSpacePath;
        if (path == null || path.Count == 0 || path[path.Count - 1] != task.destinationSpaceTileIndex)
        {
            active.Remove(unitId);
            return;
        }

        int remaining = Mathf.Max(0, path.Count - ship.queuedSpacePathCursor - 1);
        if (remaining == task.remainingHexes) return;
        task.remainingHexes = remaining;
        active[unitId] = task;
        OnTravelProgressed?.Invoke(task);
    }

    public List<SpaceTravelTask> GetActiveTravels()
    {
        var tasks = new List<SpaceTravelTask>();
        foreach (var task in active.Values)
            if (task.travelingUnit != null) tasks.Add(task);
        return tasks;
    }

    public SpaceTravelTask? GetTravelTaskForUnit(GameObject unit) =>
        unit != null && active.TryGetValue(unit.GetRuntimeId(), out var task) ? task : (SpaceTravelTask?)null;

    public bool CancelTravel(int taskId)
    {
        foreach (var task in GetActiveTravels())
        {
            if (task.taskId != taskId || !task.travelingUnit.TryGetComponent<BaseUnit>(out var ship)) continue;
            active.Remove(task.travelingUnit.GetRuntimeId());
            ship.queuedSpacePath?.Clear();
            ship.queuedSpacePathCursor = 0;
            return true;
        }
        return false;
    }
}
