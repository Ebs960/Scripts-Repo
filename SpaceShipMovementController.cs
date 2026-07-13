using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SpaceFleet
{
    public int fleetId;
    public int ownerCivilizationId;
    public string fleetName;
    public List<int> memberUnitIds = new List<int>();
    public SpaceLocation location;
    public List<int> queuedPath = new List<int>();
}

public class SpaceShipMovementController : MonoBehaviour
{
    public static SpaceShipMovementController Instance { get; private set; }
    public SpaceHexGrid Grid { get; private set; } = new SpaceHexGrid(12, 5f);
    public SpaceHexPathfinder Pathfinder { get; private set; }
    public readonly Dictionary<int, SpaceFleet> fleets = new Dictionary<int, SpaceFleet>();
    private int nextFleetId = 1;

    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; Pathfinder = new SpaceHexPathfinder(Grid); }
    public void SetGrid(SpaceHexGrid grid) { Grid = grid ?? new SpaceHexGrid(12, 5f); Pathfinder = new SpaceHexPathfinder(Grid); }

    public bool PlaceOnSpaceTile(BaseUnit unit, int tileIndex)
    {
        var tile = Grid.GetTile(tileIndex); if (unit == null || tile == null || tile.blocksMovement) return false;
        RemoveFromCurrentSpaceTile(unit);
        unit.spaceLocation = SpaceLocation.InSpace(tileIndex); unit.currentSpaceTileIndex = tileIndex; unit.planetIndex = -1; unit.currentTileIndex = -1;
        if (!tile.spacecraftIds.Contains(unit.gameObject.GetRuntimeId())) tile.spacecraftIds.Add(unit.gameObject.GetRuntimeId());
        unit.transform.position = Grid.GetWorldPosition(tileIndex) + Vector3.up * 1.2f;
        return true;
    }

    public bool EnterPlanetOrbit(BaseUnit unit, int planetIndex)
    {
        if (unit == null) return false; RemoveFromCurrentSpaceTile(unit);
        unit.spaceLocation = SpaceLocation.InOrbit(planetIndex); unit.planetIndex = planetIndex; unit.currentLayer = TileLayer.Orbit; return true;
    }
    public bool LeavePlanetOrbitForSpace(BaseUnit unit, int tileIndex) => PlaceOnSpaceTile(unit, tileIndex);
    public bool EnterPlanetOrbitFromSpace(BaseUnit unit, int planetIndex) => EnterPlanetOrbit(unit, planetIndex);
    public bool LandOnPlanet(BaseUnit unit, int planetIndex, int tileIndex)
    {
        if (unit == null) return false; RemoveFromCurrentSpaceTile(unit);
        unit.spaceLocation = SpaceLocation.OnSurface(planetIndex, tileIndex); unit.planetIndex = planetIndex; unit.currentTileIndex = tileIndex; unit.currentLayer = TileLayer.Surface; return true;
    }

    public bool QueueMove(BaseUnit unit, int destinationTile)
    {
        if (unit == null || unit.currentSpaceTileIndex < 0) return false;
        var path = Pathfinder.FindPath(unit.currentSpaceTileIndex, destinationTile);
        if (path.Count == 0) return false;
        unit.queuedSpacePath = path; unit.queuedSpacePathCursor = 0; MoveAlongQueuedPath(unit); return true;
    }

    public void MoveAlongQueuedPath(BaseUnit unit)
    {
        if (unit == null || unit.queuedSpacePath == null) return;
        if (unit.currentSpaceMovementPoints <= 0) unit.currentSpaceMovementPoints = unit.spaceMovementPointsPerTurn;
        while (unit.queuedSpacePathCursor + 1 < unit.queuedSpacePath.Count && unit.currentSpaceMovementPoints > 0)
        {
            int next = unit.queuedSpacePath[unit.queuedSpacePathCursor + 1]; var tile = Grid.GetTile(next); if (tile == null || tile.blocksMovement) break;
            int cost = GetAbilityModifiedMovementCost(unit, tile); if (cost > unit.currentSpaceMovementPoints) break;
            unit.currentSpaceMovementPoints -= cost; unit.queuedSpacePathCursor++; PlaceOnSpaceTile(unit, next);
        }
    }

    private int GetAbilityModifiedMovementCost(BaseUnit unit, SpaceHexTile tile)
    {
        int cost = Mathf.Max(1, tile != null ? tile.movementCost : 1);
        float efficiency = unit != null ? unit.GetAbilitySpaceMovementEfficiencyModifier() : 0f;
        return Mathf.Max(1, Mathf.CeilToInt(cost * Mathf.Max(0.25f, 1f - efficiency)));
    }

    public SpaceFleet CreateFleet(IEnumerable<BaseUnit> units, string fleetName = "Fleet")
    {
        var fleet = new SpaceFleet { fleetId = nextFleetId++, fleetName = fleetName, ownerCivilizationId = -1 };
        foreach (var u in units) if (u != null && u.currentSpaceTileIndex >= 0) { if (fleet.memberUnitIds.Count == 0) fleet.location = u.spaceLocation; fleet.memberUnitIds.Add(u.gameObject.GetRuntimeId()); u.spaceFleetId = fleet.fleetId; }
        fleets[fleet.fleetId] = fleet; return fleet;
    }
    public void RemoveFromFleet(BaseUnit unit) { if (unit == null || !fleets.TryGetValue(unit.spaceFleetId, out var f)) return; f.memberUnitIds.Remove(unit.gameObject.GetRuntimeId()); unit.spaceFleetId = -1; }
    public void MergeFleets(int targetFleetId, int sourceFleetId) { if (!fleets.TryGetValue(targetFleetId, out var t) || !fleets.TryGetValue(sourceFleetId, out var s)) return; foreach (int id in s.memberUnitIds) if (!t.memberUnitIds.Contains(id)) t.memberUnitIds.Add(id); fleets.Remove(sourceFleetId); }
    private void RemoveFromCurrentSpaceTile(BaseUnit unit) { if (unit.currentSpaceTileIndex < 0) return; Grid.GetTile(unit.currentSpaceTileIndex)?.spacecraftIds.Remove(unit.gameObject.GetRuntimeId()); }
}
