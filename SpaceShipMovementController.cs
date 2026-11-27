using System;
using System.Collections.Generic;
using UnityEngine;

public class SpaceShipMovementController : MonoBehaviour, IUnitMovementDomain
{
    public static event Action ShipMoved;
    public static SpaceShipMovementController Instance { get; private set; }
    public SpaceHexGrid Grid { get; private set; } = new SpaceHexGrid(12, 5f);
    public SpaceHexPathfinder Pathfinder { get; private set; }

    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; Pathfinder = new SpaceHexPathfinder(Grid); }
    public void SetGrid(SpaceHexGrid grid) { Grid = grid ?? new SpaceHexGrid(12, 5f); Pathfinder = new SpaceHexPathfinder(Grid); }

    public bool PlaceOnSpaceTile(BaseUnit unit, int tileIndex)
    {
        var tile = Grid.GetTile(tileIndex); if (unit == null || tile == null || tile.blocksMovement) return false;
        if (unit.currentSpaceTileIndex < 0 && unit.currentTileIndex >= 0 && unit.planetIndex >= 0)
        {
            TileOccupancyManager.GetForPlanet(unit.planetIndex)?.ClearOccupantById(
                unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId());
        }
        RemoveFromCurrentSpaceTile(unit);
        unit.spaceLocation = SpaceLocation.InSpace(tileIndex); unit.currentSpaceTileIndex = tileIndex; unit.planetIndex = -1; unit.currentTileIndex = -1;
        if (!tile.spacecraftIds.Contains(unit.gameObject.GetRuntimeId())) tile.spacecraftIds.Add(unit.gameObject.GetRuntimeId());
        SpaceWorldManager.Instance?.Entities.Register(unit);
        unit.transform.position = Grid.GetWorldPosition(tileIndex) + Vector3.up * 1.2f;
        ShipMoved?.Invoke();
        return true;
    }

    public bool EnterPlanetOrbit(BaseUnit unit, int planetIndex)
    {
        if (unit == null) return false; RemoveFromCurrentSpaceTile(unit);
        unit.spaceLocation = SpaceLocation.InOrbit(planetIndex); unit.planetIndex = planetIndex; unit.currentLayer = TileLayer.Orbit;
        SpaceWorldManager.Instance?.Entities.Register(unit);
        return true;
    }
    public bool LeavePlanetOrbitForSpace(BaseUnit unit, int tileIndex) => PlaceOnSpaceTile(unit, tileIndex);
    public bool EnterPlanetOrbitFromSpace(BaseUnit unit, int planetIndex) => EnterPlanetOrbit(unit, planetIndex);
    public bool LandOnPlanet(BaseUnit unit, int planetIndex, int tileIndex)
    {
        if (unit == null) return false; RemoveFromCurrentSpaceTile(unit);
        unit.spaceLocation = SpaceLocation.OnSurface(planetIndex, tileIndex); unit.planetIndex = planetIndex; unit.currentTileIndex = tileIndex; unit.currentLayer = TileLayer.Surface; return true;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public List<int> FindRoute(BaseUnit unit, int start, int destination)
    {
        if (unit == null || Grid.GetTile(start) == null || Grid.GetTile(destination) == null)
            return new List<int>();

        return Pathfinder.FindPath(start, destination, tile =>
            GetAbilityModifiedMovementCost(unit, tile) <= unit.spaceMovementPointsPerTurn);
    }

    public int GetRouteCost(BaseUnit unit, IReadOnlyList<int> path, int cursor)
    {
        int cost = 0;
        for (int i = cursor + 1; i < path.Count; i++)
            cost += GetAbilityModifiedMovementCost(unit, Grid.GetTile(path[i]));
        return cost;
    }

    public void AdvanceTurn(BaseUnit unit)
    {
        if (unit == null || unit.currentHealth <= 0 || unit.currentSpaceTileIndex < 0 || unit.isPackedInSpaceFleet) return;
        unit.currentSpaceMovementPoints = unit.spaceMovementPointsPerTurn;
        MoveAlongQueuedPath(unit);
    }

    public bool QueueMove(BaseUnit unit, int destinationTile)
    {
        if (unit == null || unit.currentHealth <= 0 || unit.currentSpaceTileIndex < 0 || unit.isPackedInSpaceFleet) return false;
        var path = FindRoute(unit, unit.currentSpaceTileIndex, destinationTile);
        if (path.Count == 0) return false;
        unit.queuedSpacePath = path; unit.queuedSpacePathCursor = 0; MoveAlongQueuedPath(unit); return true;
    }

    public void MoveAlongQueuedPath(BaseUnit unit)
    {
        if (unit == null || unit.queuedSpacePath == null) return;

        while (unit.queuedSpacePathCursor + 1 < unit.queuedSpacePath.Count && unit.currentSpaceMovementPoints > 0)
        {
            int next = unit.queuedSpacePath[unit.queuedSpacePathCursor + 1]; var tile = Grid.GetTile(next); if (tile == null || tile.blocksMovement) break;
            int cost = GetAbilityModifiedMovementCost(unit, tile); if (cost > unit.currentSpaceMovementPoints) break;
            unit.currentSpaceMovementPoints -= cost; unit.queuedSpacePathCursor++; PlaceOnSpaceTile(unit, next);
        }
        SpaceRouteManager.Instance?.UpdateTravel(unit);
    }

    private int GetAbilityModifiedMovementCost(BaseUnit unit, SpaceHexTile tile)
    {
        int cost = Mathf.Max(1, tile != null ? tile.movementCost : 1);
        if (tile != null && SpaceFeatureManager.Instance != null)
            cost += SpaceFeatureManager.Instance.GetMovementCost(tile.tileIndex, unit);
        float efficiency = unit != null ? unit.GetAbilitySpaceMovementEfficiencyModifier() : 0f;
        return Mathf.Max(1, Mathf.CeilToInt(cost * Mathf.Max(0.25f, 1f - efficiency)));
    }

    IReadOnlyList<int> IUnitMovementDomain.FindPath(BaseUnit unit, int destination)
    {
        return unit == null || Pathfinder == null ? null : FindRoute(unit, unit.currentSpaceTileIndex, destination);
    }

    bool IUnitMovementDomain.CanEnter(BaseUnit unit, int location)
    {
        var tile = Grid?.GetTile(location);
        return unit != null && tile != null && !tile.blocksMovement;
    }

    int IUnitMovementDomain.GetMovementCost(BaseUnit unit, int location)
    {
        var tile = Grid?.GetTile(location);
        return tile != null ? GetAbilityModifiedMovementCost(unit, tile) : int.MaxValue;
    }
    private void RemoveFromCurrentSpaceTile(BaseUnit unit)
    {
        if (unit.currentSpaceTileIndex < 0) return;
        Grid.GetTile(unit.currentSpaceTileIndex)?.spacecraftIds.Remove(unit.gameObject.GetRuntimeId());
        SpaceWorldManager.Instance?.Entities.RemoveShip(unit.gameObject.GetRuntimeId());
        unit.currentSpaceTileIndex = -1;
    }
}
