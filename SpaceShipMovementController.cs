using System;
using System.Collections.Generic;
using UnityEngine;

public class SpaceShipMovementController : MonoBehaviour
{
    public static SpaceShipMovementController Instance { get; private set; }
    public SpaceHexGrid Grid { get; private set; } = new SpaceHexGrid(12, 5f);
    public SpaceHexPathfinder Pathfinder { get; private set; }

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
        if (tile != null && SpaceFeatureManager.Instance != null)
            cost += SpaceFeatureManager.Instance.GetMovementCost(tile.tileIndex, unit);
        float efficiency = unit != null ? unit.GetAbilitySpaceMovementEfficiencyModifier() : 0f;
        return Mathf.Max(1, Mathf.CeilToInt(cost * Mathf.Max(0.25f, 1f - efficiency)));
    }

    public SpaceFleet CreateFleet(IEnumerable<BaseUnit> units, string fleetName = "Fleet")
    {
        if (SpaceFleetManager.Instance == null) new GameObject("SpaceFleetManager").AddComponent<SpaceFleetManager>();
        var members = new List<CombatUnit>();
        foreach (var u in units) { var c = u as CombatUnit; if (c != null && c.currentSpaceTileIndex >= 0) members.Add(c); }
        return SpaceFleetManager.Instance.CreateFleet(-1, fleetName, -1, members);
    }
    public void RemoveFromFleet(BaseUnit unit) { var f = SpaceFleetManager.Instance != null && unit != null ? SpaceFleetManager.Instance.GetFleet(unit.spaceFleetId) : null; if (unit == null || f == null) return; f.memberUnitIds.Remove(unit.gameObject.GetRuntimeId()); unit.spaceFleetId = -1; }
    public void MergeFleets(int targetFleetId, int sourceFleetId) { var mgr = SpaceFleetManager.Instance; var t = mgr != null ? mgr.GetFleet(targetFleetId) : null; var s = mgr != null ? mgr.GetFleet(sourceFleetId) : null; if (t == null || s == null) return; foreach (int id in s.memberUnitIds) if (!t.memberUnitIds.Contains(id)) t.memberUnitIds.Add(id); mgr.fleets.Remove(s); }
    private void RemoveFromCurrentSpaceTile(BaseUnit unit) { if (unit.currentSpaceTileIndex < 0) return; Grid.GetTile(unit.currentSpaceTileIndex)?.spacecraftIds.Remove(unit.gameObject.GetRuntimeId()); }
}
