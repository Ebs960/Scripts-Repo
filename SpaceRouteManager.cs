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
        public float Progress
        {
            get
            {
                var unit = travelingUnit != null ? travelingUnit.GetComponent<BaseUnit>() : null;
                if (unit == null) return 0f;
                if (unit.currentSpaceTileIndex == destinationSpaceTileIndex) return 1f;
                return unit.queuedSpacePath != null && unit.queuedSpacePath.Count > 1
                    ? Mathf.Clamp01((float)unit.queuedSpacePathCursor / (unit.queuedSpacePath.Count - 1)) : 0f;
            }
        }
        public bool IsComplete => travelingUnit != null && travelingUnit.TryGetComponent<BaseUnit>(out var unit)
            && unit.spaceLocation.IsInSolarSystemSpace && unit.currentSpaceTileIndex == destinationSpaceTileIndex;
    }

    private int nextTaskId = 1;
    private readonly Dictionary<int, SpaceTravelTask> activeTravels = new Dictionary<int, SpaceTravelTask>();
    private readonly Dictionary<int, int> lastCursors = new Dictionary<int, int>();
    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; DontDestroyOnLoad(gameObject); }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        foreach (var task in new List<SpaceTravelTask>(activeTravels.Values))
        {
            var unit = task.travelingUnit != null ? task.travelingUnit.GetComponent<BaseUnit>() : null;
            if (unit == null || unit.currentHealth <= 0 || unit.queuedSpacePath == null ||
                unit.queuedSpacePath.Count == 0 || unit.queuedSpacePath[unit.queuedSpacePath.Count - 1] != task.destinationSpaceTileIndex)
            {
                Debug.LogWarning($"[SpaceRouteManager] Travel task {task.taskId} was cancelled or its spacecraft was lost.");
                activeTravels.Remove(task.taskId); lastCursors.Remove(task.taskId);
                continue;
            }
            if (!lastCursors.TryGetValue(task.taskId, out int cursor) || cursor != unit.queuedSpacePathCursor)
            {
                lastCursors[task.taskId] = unit.queuedSpacePathCursor;
                OnTravelProgressed?.Invoke(task);
            }
            if (!task.IsComplete) continue;
            activeTravels.Remove(task.taskId); lastCursors.Remove(task.taskId);
            OnTravelCompleted?.Invoke(task);
        }
    }

    internal void ClearTravelTracking()
    {
        activeTravels.Clear();
        lastCursors.Clear();
    }

    public bool TryGetRoute(GameObject unit, int fromPlanetIndex, int toPlanetIndex, out List<int> route, out int destination)
    {
        route = null;
        destination = -1;
        var ship = unit != null ? unit.GetComponent<CombatUnit>() : null;
        if (ship == null || ship.data == null || ship.data.unitType != CombatCategory.Spaceship ||
            ship.currentHealth <= 0 || ship.isPackedInSpaceFleet) return false;
        var world = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Include);
        if (world == null) return false;
        if (world.Grid == null || world.Grid != SpaceWorldManager.Instance?.Grid || world.GetPlanetAnchorTile(toPlanetIndex) < 0)
            world.RebuildSpaceMap();
        var movement = SpaceShipMovementController.Instance;
        if (movement == null) return false;
        bool fromOrbit = ship.spaceLocation.locationType == SpaceLocationType.PlanetOrbit && ship.spaceLocation.planetIndex == fromPlanetIndex;
        if (!fromOrbit && !ship.spaceLocation.IsInSolarSystemSpace) return false;
        var starts = fromOrbit ? world.GetPlanetOrbitSectors(fromPlanetIndex) : new List<int> { ship.currentSpaceTileIndex };
        var destinations = world.GetPlanetOrbitSectors(toPlanetIndex);
        var tracked = GetTravelTaskForUnit(unit);
        if (tracked.HasValue && tracked.Value.destinationPlanetIndex == toPlanetIndex)
        {
            destination = tracked.Value.destinationSpaceTileIndex;
            return TryGetTrackedRoute(ship, destination, out route);
        }
        if (destinations.Contains(ship.currentSpaceTileIndex))
        {
            destination = ship.currentSpaceTileIndex;
            route = new List<int> { destination };
            return true;
        }
        foreach (int start in starts)
        {
            if (fromOrbit && !movement.CanPlaceShipOnTile(ship, start)) continue;
            foreach (int candidate in destinations)
            {
                var path = movement.FindShipPathFrom(ship, start, candidate);
                if (path.Count < 2 || (route != null && path.Count >= route.Count)) continue;
                route = path;
                destination = candidate;
            }
        }
        return route != null;
    }

    private static bool TryGetTrackedRoute(BaseUnit ship, int destination, out List<int> route)
    {
        route = null;
        if (ship.currentSpaceTileIndex == destination)
            route = new List<int> { destination };
        else if (ship.queuedSpacePath != null && ship.queuedSpacePath.Count > 0 &&
            ship.queuedSpacePath[ship.queuedSpacePath.Count - 1] == destination)
            route = ship.queuedSpacePath;
        return route != null;
    }

    public bool StartSpaceTravel(GameObject unit, int fromPlanetIndex, int toPlanetIndex)
    {
        if (!TryGetRoute(unit, fromPlanetIndex, toPlanetIndex, out var route, out int dest) || route.Count < 2) return false;
        var combat = unit.GetComponent<CombatUnit>();
        BaseUnit baseUnit = combat;
        var movement = SpaceShipMovementController.Instance;
        bool fromOrbit = baseUnit.spaceLocation.locationType == SpaceLocationType.PlanetOrbit;
        int start = route[0];
        if (fromOrbit && !movement.LeavePlanetOrbitForSpace(baseUnit, start)) return false;
        bool ok = movement.QueueMove(baseUnit, dest);
        if (!ok && fromOrbit && !movement.EnterPlanetOrbitFromSpace(baseUnit, fromPlanetIndex))
            Debug.LogError($"[SpaceRouteManager] Could not restore {baseUnit.name} to its original orbit after a failed order.", baseUnit);
        if (ok)
        {
            var task = new SpaceTravelTask { taskId = nextTaskId++, travelingUnit = unit, originPlanetIndex = fromPlanetIndex, destinationPlanetIndex = toPlanetIndex, originSpaceTileIndex = start, destinationSpaceTileIndex = dest, unitName = unit.name, unitType = combat.data.unitName };
            activeTravels.Add(task.taskId, task);
            lastCursors.Add(task.taskId, -1);
            OnTravelStarted?.Invoke(task);
        }
        return ok;
    }

    public List<SpaceTravelTask> GetActiveTravels() => new List<SpaceTravelTask>(activeTravels.Values);
    public SpaceTravelTask? GetTravelTaskForUnit(GameObject unit)
    {
        foreach (var task in activeTravels.Values) if (task.travelingUnit == unit) return task;
        return null;
    }
    public bool CancelTravel(int taskId)
    {
        if (!activeTravels.TryGetValue(taskId, out var task)) return false;
        var unit = task.travelingUnit != null ? task.travelingUnit.GetComponent<BaseUnit>() : null;
        if (unit == null || unit.queuedSpacePath == null || unit.queuedSpacePath.Count == 0 ||
            unit.queuedSpacePath[unit.queuedSpacePath.Count - 1] != task.destinationSpaceTileIndex) return false;
        unit.queuedSpacePath.Clear(); unit.queuedSpacePathCursor = 0;
        activeTravels.Remove(taskId); lastCursors.Remove(taskId);
        return true;
    }
}
