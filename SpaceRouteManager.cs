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
        public float Progress => 1f;
        public bool IsComplete => true;
    }

    private int nextTaskId = 1;
    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; DontDestroyOnLoad(gameObject); }

    public bool StartSpaceTravel(GameObject unit, int fromPlanetIndex, int toPlanetIndex)
    {
        var baseUnit = unit != null ? unit.GetComponent<BaseUnit>() : null; var combat = unit != null ? unit.GetComponent<CombatUnit>() : null;
        if (baseUnit == null || combat == null || combat.data == null || combat.data.unitType != CombatCategory.Spaceship) return false;
        var world = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Include); if (world == null) return false;
        if (world.Grid == null) world.RebuildSpaceMap();
        int start = baseUnit.currentSpaceTileIndex >= 0 ? baseUnit.currentSpaceTileIndex : world.GetPlanetAnchorTile(fromPlanetIndex);
        int dest = world.GetPlanetAnchorTile(toPlanetIndex); if (start < 0 || dest < 0) return false;
        if (SpaceShipMovementController.Instance == null) new GameObject("SpaceShipMovementController").AddComponent<SpaceShipMovementController>();
        if (baseUnit.currentSpaceTileIndex < 0) SpaceShipMovementController.Instance.PlaceOnSpaceTile(baseUnit, start);
        bool ok = SpaceShipMovementController.Instance.QueueMove(baseUnit, dest);
        if (ok)
        {
            var task = new SpaceTravelTask { taskId = nextTaskId++, travelingUnit = unit, originPlanetIndex = fromPlanetIndex, destinationPlanetIndex = toPlanetIndex, originSpaceTileIndex = start, destinationSpaceTileIndex = dest, unitName = unit.name, unitType = combat.data.unitName };
            OnTravelStarted?.Invoke(task); OnTravelProgressed?.Invoke(task); OnTravelCompleted?.Invoke(task);
        }
        return ok;
    }

    public List<SpaceTravelTask> GetActiveTravels() => new List<SpaceTravelTask>();
    public SpaceTravelTask? GetTravelTaskForUnit(GameObject unit) => null;
    public bool CancelTravel(int taskId) => false;
}
