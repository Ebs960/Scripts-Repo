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

    public bool StartSpaceTravel(GameObject unit, int fromPlanetIndex, int toPlanetIndex)
    {
        var baseUnit = unit != null ? unit.GetComponent<BaseUnit>() : null; var combat = unit != null ? unit.GetComponent<CombatUnit>() : null;
        if (baseUnit == null || combat == null || combat.data == null || combat.data.unitType != CombatCategory.Spaceship) return false;
        var world = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Include); if (world == null) return false;
        if (world.Grid == null || world.GetPlanetAnchorTile(toPlanetIndex) < 0) world.RebuildSpaceMap();
        if (SpaceShipMovementController.Instance == null) new GameObject("SpaceShipMovementController").AddComponent<SpaceShipMovementController>();
        var movement = SpaceShipMovementController.Instance;
        bool fromOrbit = baseUnit.spaceLocation.locationType == SpaceLocationType.PlanetOrbit && baseUnit.spaceLocation.planetIndex == fromPlanetIndex;
        if (!fromOrbit && !baseUnit.spaceLocation.IsInSolarSystemSpace) return false;
        var starts = fromOrbit ? world.GetPlanetOrbitSectors(fromPlanetIndex) : new List<int> { baseUnit.currentSpaceTileIndex };
        var destinations = world.GetPlanetOrbitSectors(toPlanetIndex);
        int start = -1, dest = -1, shortest = int.MaxValue;
        foreach (int candidateStart in starts)
        {
            if (fromOrbit && !movement.CanPlaceShipOnTile(baseUnit, candidateStart)) continue;
            foreach (int candidateDestination in destinations)
            {
                var path = movement.FindShipPathFrom(baseUnit, candidateStart, candidateDestination);
                if (path.Count < 2 || path.Count >= shortest) continue;
                start = candidateStart; dest = candidateDestination; shortest = path.Count;
            }
        }
        if (start < 0 || dest < 0) return false;
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
