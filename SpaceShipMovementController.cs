using System;
using System.Collections.Generic;
using UnityEngine;

public class SpaceShipMovementController : MonoBehaviour, IUnitMovementDomain
{
    public static SpaceShipMovementController Instance { get; private set; }
    public SpaceHexGrid Grid { get; private set; } = new SpaceHexGrid(12, 5f);
    public SpaceHexPathfinder Pathfinder { get; private set; }

    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; Pathfinder = new SpaceHexPathfinder(Grid); }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public void SetGrid(SpaceHexGrid grid) { Grid = grid ?? new SpaceHexGrid(12, 5f); Pathfinder = new SpaceHexPathfinder(Grid); }

    public bool PlaceOnSpaceTile(BaseUnit unit, int tileIndex)
    {
        var tile = Grid.GetTile(tileIndex); if (unit == null || tile == null || tile.blocksMovement) return false;
        RemoveFromCurrentSpaceTile(unit);
        unit.spaceLocation = SpaceLocation.InSpace(tileIndex); unit.currentSpaceTileIndex = tileIndex; unit.planetIndex = -1; unit.currentTileIndex = -1;
        if (!tile.spacecraftIds.Contains(unit.gameObject.GetRuntimeId())) tile.spacecraftIds.Add(unit.gameObject.GetRuntimeId());
        SpaceWorldManager.Instance?.Entities.Register(unit);
        unit.transform.position = Grid.GetWorldPosition(tileIndex) + Vector3.up * 1.2f;
        if (unit is CombatUnit combat && combat.data != null && CombatUnitData.IsSpaceCategory(combat.data.unitType) && unit.owner != null)
        {
            if (unit.spaceFleetId < 0)
                EnsureSingletonFleet(combat);
            else
            {
                var fleet = SpaceFleetManager.Instance != null ? SpaceFleetManager.Instance.GetFleet(unit.spaceFleetId) : null;
                if (fleet != null && !fleet.isPacked && fleet.memberUnitIds != null &&
                    fleet.memberUnitIds.Count == 1 && fleet.memberUnitIds[0] == unit.gameObject.GetRuntimeId())
                { fleet.currentSpaceTileIndex = tileIndex; fleet.location = unit.spaceLocation; }
            }
        }
        return true;
    }

    public void EnsureSingletonFleet(CombatUnit unit)
    {
        if (unit == null || unit.currentHealth <= 0 || unit.owner == null || unit.data == null ||
            !CombatUnitData.IsSpaceCategory(unit.data.unitType) || unit.spaceFleetId >= 0 ||
            !unit.spaceLocation.IsInSolarSystemSpace) return;
        if (SpaceFleetManager.Instance == null) new GameObject("SpaceFleetManager").AddComponent<SpaceFleetManager>();
        if (SpaceFleetManager.Instance.CreateFleet(unit.owner.gameObject.GetRuntimeId(), unit.UnitName, -1, new[] { unit }) == null)
            Debug.LogError($"[SpaceMovement] Could not create a fleet for {unit.name}.", unit);
    }

    public bool EnterPlanetOrbit(BaseUnit unit, int planetIndex)
    {
        if (unit == null || unit.isPackedInSpaceFleet) return false;
        if (unit.spaceLocation.IsInSolarSystemSpace)
        {
            var planet = SpaceMapSelectionInfo.FindPlanetByIndex(planetIndex);
            var tile = Grid.GetTile(unit.currentSpaceTileIndex);
            int bodyId = planet != null && planet.celestialBodyId >= 0 ? planet.celestialBodyId : planetIndex;
            if (planet == null || tile == null || !tile.isPlanetOrbitSector || tile.associatedPlanetId != bodyId) return false;
            if (unit is CombatUnit combat && unit.spaceFleetId >= 0 &&
                (SpaceFleetManager.Instance == null || !SpaceFleetManager.Instance.DetachForShipOrder(combat))) return false;
        }
        RemoveFromCurrentSpaceTile(unit);
        unit.currentSpaceTileIndex = -1;
        unit.spaceLocation = SpaceLocation.InOrbit(planetIndex); unit.planetIndex = planetIndex; unit.currentLayer = TileLayer.Orbit;
        SpaceWorldManager.Instance?.Entities.Register(unit);
        var fleet = SpaceFleetManager.Instance != null ? SpaceFleetManager.Instance.GetFleet(unit.spaceFleetId) : null;
        if (fleet != null && fleet.memberUnitIds.Count == 1) { fleet.currentSpaceTileIndex = -1; fleet.location = unit.spaceLocation; }
        return true;
    }
    public bool LeavePlanetOrbitForSpace(BaseUnit unit, int tileIndex)
    {
        if (unit == null || unit.isPackedInSpaceFleet || unit.spaceLocation.locationType != SpaceLocationType.PlanetOrbit) return false;
        var planet = SpaceMapSelectionInfo.FindPlanetByIndex(unit.spaceLocation.planetIndex);
        var tile = Grid.GetTile(tileIndex);
        int bodyId = planet != null && planet.celestialBodyId >= 0 ? planet.celestialBodyId : unit.spaceLocation.planetIndex;
        return planet != null && tile != null && tile.isPlanetOrbitSector && tile.associatedPlanetId == bodyId &&
            CanPlaceShipOnTile(unit, tileIndex) &&
            PlaceOnSpaceTile(unit, tileIndex);
    }
    public bool EnterPlanetOrbitFromSpace(BaseUnit unit, int planetIndex) => EnterPlanetOrbit(unit, planetIndex);
    public bool LandOnPlanet(BaseUnit unit, int planetIndex, int tileIndex)
    {
        if (unit == null || unit.isPackedInSpaceFleet || unit.spaceLocation.locationType != SpaceLocationType.PlanetOrbit ||
            unit.spaceLocation.planetIndex != planetIndex) return false; RemoveFromCurrentSpaceTile(unit);
        unit.currentSpaceTileIndex = -1;
        unit.spaceLocation = SpaceLocation.OnSurface(planetIndex, tileIndex); unit.planetIndex = planetIndex; unit.currentTileIndex = tileIndex; unit.currentLayer = TileLayer.Surface;
        var fleet = SpaceFleetManager.Instance != null ? SpaceFleetManager.Instance.GetFleet(unit.spaceFleetId) : null;
        if (fleet != null && fleet.memberUnitIds.Count == 1) { fleet.currentSpaceTileIndex = -1; fleet.location = unit.spaceLocation; }
        return true;
    }

    public bool QueueMove(BaseUnit unit, int destinationTile)
    {
        var path = FindShipPath(unit, destinationTile);
        if (path.Count < 2) return false;
        if (unit is CombatUnit combat && unit.spaceFleetId >= 0 &&
            (SpaceFleetManager.Instance == null || !SpaceFleetManager.Instance.DetachForShipOrder(combat))) return false;
        unit.queuedSpacePath = path; unit.queuedSpacePathCursor = 0; MoveAlongQueuedPath(unit); return true;
    }

    public List<int> FindShipPath(BaseUnit unit, int destinationTile)
    {
        if (unit == null || !unit.spaceLocation.IsInSolarSystemSpace) return new List<int>();
        return FindShipPathFrom(unit, unit.currentSpaceTileIndex, destinationTile);
    }

    public List<int> FindShipPathFrom(BaseUnit unit, int startTile, int destinationTile)
    {
        if (unit == null || unit.currentHealth <= 0 || unit.isPackedInSpaceFleet ||
            Grid.GetTile(startTile) == null || destinationTile == startTile || unit.spaceMovementPointsPerTurn <= 0)
            return new List<int>();
        return Pathfinder.FindPath(startTile, destinationTile,
            tile => IsAvailableTile(tile, new[] { unit }) &&
                GetAbilityModifiedMovementCost(unit, tile) <= unit.spaceMovementPointsPerTurn);
    }

    public bool CanPlaceShipOnTile(BaseUnit unit, int tileIndex)
    {
        var tile = Grid.GetTile(tileIndex);
        return unit != null && tile != null && !tile.blocksMovement && IsAvailableTile(tile, new[] { unit });
    }

    public bool TryGetPackedMembers(SpaceFleet fleet, out List<CombatUnit> members, out string reason)
    {
        members = new List<CombatUnit>();
        reason = null;
        if (fleet == null || !fleet.isPacked || fleet.isDeployed || fleet.memberUnitIds == null ||
            fleet.memberUnitIds.Count == 0 || !fleet.location.IsInSolarSystemSpace ||
            fleet.location.spaceTileIndex != fleet.currentSpaceTileIndex || Grid.GetTile(fleet.currentSpaceTileIndex) == null)
        { reason = "fleet is not a valid packed formation in space"; return false; }
        var seen = new HashSet<int>();
        foreach (int id in fleet.memberUnitIds)
        {
            var unit = UnitRegistry.GetObject(id)?.GetComponent<CombatUnit>();
            if (!seen.Add(id) || unit == null || unit.currentHealth <= 0 ||
                unit.spaceFleetId != fleet.fleetId || !unit.isPackedInSpaceFleet ||
                unit.owner == null || unit.owner.gameObject.GetRuntimeId() != fleet.ownerCivilizationId ||
                !unit.spaceLocation.IsInSolarSystemSpace || unit.currentSpaceTileIndex != fleet.currentSpaceTileIndex ||
                unit.spaceLocation.spaceTileIndex != fleet.currentSpaceTileIndex)
            { reason = "fleet membership or location is inconsistent"; return false; }
            members.Add(unit);
        }
        return true;
    }

    public List<int> FindFleetPath(SpaceFleet fleet, int destinationTile)
    {
        if (!TryGetPackedMembers(fleet, out var members, out _) ||
            destinationTile == fleet.currentSpaceTileIndex) return new List<int>();
        return Pathfinder.FindPath(fleet.currentSpaceTileIndex, destinationTile,
            tile => IsAvailableTile(tile, members) && members.TrueForAll(unit => unit.spaceMovementPointsPerTurn > 0 &&
                GetAbilityModifiedMovementCost(unit, tile) <= unit.spaceMovementPointsPerTurn));
    }

    public bool QueueFleetMove(SpaceFleet fleet, int destinationTile)
    {
        var path = FindFleetPath(fleet, destinationTile);
        if (path.Count < 2) return false;
        fleet.queuedPath = path;
        fleet.queuedPathCursor = 0;
        MoveFleetAlongQueuedPath(fleet);
        return true;
    }

    private sealed class ReachableState
    {
        public int tile;
        public int[] remaining;
    }

    public HashSet<int> GetReachableTiles(IReadOnlyList<BaseUnit> members, int start)
    {
        var result = new HashSet<int>();
        if (members == null || members.Count == 0 || Grid.GetTile(start) == null) return result;
        var initial = new int[members.Count];
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] == null) return result;
            initial[i] = Mathf.Max(0, members[i].currentSpaceMovementPoints);
        }
        var states = new Dictionary<int, List<int[]>>();
        var queue = new Queue<ReachableState>();
        states[start] = new List<int[]> { initial };
        queue.Enqueue(new ReachableState { tile = start, remaining = initial });
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            result.Add(state.tile);
            foreach (int next in Grid.GetNeighbors(state.tile))
            {
                var tile = Grid.GetTile(next);
                if (tile == null || tile.blocksMovement || !IsAvailableTile(tile, members)) continue;
                var remaining = new int[members.Count];
                bool affordable = true;
                for (int i = 0; i < members.Count; i++)
                {
                    int cost = GetAbilityModifiedMovementCost(members[i], tile);
                    if (cost > members[i].spaceMovementPointsPerTurn) { affordable = false; break; }
                    remaining[i] = state.remaining[i] - cost;
                    if (remaining[i] < 0) { affordable = false; break; }
                }
                if (!affordable) continue;
                if (!states.TryGetValue(next, out var previous)) states[next] = previous = new List<int[]>();
                bool dominated = false;
                foreach (var existing in previous)
                {
                    bool better = true;
                    for (int i = 0; i < remaining.Length; i++)
                        if (existing[i] < remaining[i]) { better = false; break; }
                    if (better) { dominated = true; break; }
                }
                if (dominated) continue;
                previous.RemoveAll(existing =>
                {
                    for (int i = 0; i < remaining.Length; i++)
                        if (remaining[i] < existing[i]) return false;
                    return true;
                });
                previous.Add(remaining);
                queue.Enqueue(new ReachableState { tile = next, remaining = remaining });
            }
        }
        return result;
    }

    public void MoveFleetAlongQueuedPath(SpaceFleet fleet)
    {
        if (fleet == null || fleet.queuedPath == null || fleet.queuedPath.Count == 0) return;
        if (!TryGetPackedMembers(fleet, out var members, out string reason))
        {
            Debug.LogWarning($"[SpaceMovement] Cannot move fleet {fleet.fleetId}: {reason}.");
            return;
        }
        if (fleet.queuedPathCursor < 0 || fleet.queuedPathCursor >= fleet.queuedPath.Count ||
            fleet.queuedPath[fleet.queuedPathCursor] != fleet.currentSpaceTileIndex)
        {
            Debug.LogWarning($"[SpaceMovement] Cancelling an invalid route for fleet {fleet.fleetId}.");
            fleet.queuedPath.Clear();
            fleet.queuedPathCursor = 0;
            return;
        }
        while (fleet.queuedPathCursor + 1 < fleet.queuedPath.Count)
        {
            int next = fleet.queuedPath[fleet.queuedPathCursor + 1];
            var tile = Grid.GetTile(next);
            if (tile == null || tile.blocksMovement || !IsAvailableTile(tile, members) || Grid.GetDistance(fleet.currentSpaceTileIndex, next) != 1 ||
                members.Exists(unit => GetAbilityModifiedMovementCost(unit, tile) > unit.spaceMovementPointsPerTurn))
            {
                Debug.LogWarning($"[SpaceMovement] Cancelling a blocked or invalid route for fleet {fleet.fleetId}.");
                fleet.queuedPath.Clear();
                fleet.queuedPathCursor = 0;
                return;
            }
            if (members.Exists(unit => GetAbilityModifiedMovementCost(unit, tile) > unit.currentSpaceMovementPoints)) break;
            foreach (var unit in members)
            {
                int cost = GetAbilityModifiedMovementCost(unit, tile);
                PlaceOnSpaceTile(unit, next);
                unit.currentSpaceMovementPoints -= cost;
            }
            fleet.currentSpaceTileIndex = next;
            fleet.location = SpaceLocation.InSpace(next);
            fleet.queuedPathCursor++;
        }
    }

    public void MoveAlongQueuedPath(BaseUnit unit)
    {
        if (unit == null || unit.currentHealth <= 0 || unit.isPackedInSpaceFleet ||
            !unit.spaceLocation.IsInSolarSystemSpace || unit.queuedSpacePath == null || unit.queuedSpacePath.Count == 0) return;
        if (unit.queuedSpacePathCursor < 0 || unit.queuedSpacePathCursor >= unit.queuedSpacePath.Count ||
            unit.queuedSpacePath[unit.queuedSpacePathCursor] != unit.currentSpaceTileIndex)
        {
            Debug.LogWarning($"[SpaceMovement] Cancelling an invalid queued route for {unit.name}.", unit);
            unit.queuedSpacePath.Clear();
            unit.queuedSpacePathCursor = 0;
            return;
        }
        while (unit.queuedSpacePathCursor + 1 < unit.queuedSpacePath.Count && unit.currentSpaceMovementPoints > 0)
        {
            int next = unit.queuedSpacePath[unit.queuedSpacePathCursor + 1]; var tile = Grid.GetTile(next);
            if (tile == null || tile.blocksMovement || !IsAvailableTile(tile, new[] { unit }) ||
                Grid.GetDistance(unit.currentSpaceTileIndex, next) != 1)
            {
                Debug.LogWarning($"[SpaceMovement] Cancelling a blocked or invalid queued route for {unit.name}.", unit);
                unit.queuedSpacePath.Clear();
                unit.queuedSpacePathCursor = 0;
                break;
            }
            int cost = GetAbilityModifiedMovementCost(unit, tile);
            if (cost > unit.spaceMovementPointsPerTurn)
            {
                Debug.LogWarning($"[SpaceMovement] Cancelling a route that {unit.name} can no longer traverse.", unit);
                unit.queuedSpacePath.Clear();
                unit.queuedSpacePathCursor = 0;
                break;
            }
            if (cost > unit.currentSpaceMovementPoints) break;
            if (!PlaceOnSpaceTile(unit, next))
            {
                Debug.LogWarning($"[SpaceMovement] Could not place {unit.name} on its next route tile.", unit);
                break;
            }
            unit.currentSpaceMovementPoints -= cost; unit.queuedSpacePathCursor++;
        }
    }

    public static void ProcessCivilizationTurn(Civilization civilization)
    {
        if (civilization == null || civilization.combatUnits == null) return;
        SpaceShipMovementController movement = Instance;
        SpaceHexGrid worldGrid = SpaceWorldManager.Instance != null ? SpaceWorldManager.Instance.Grid : null;
        foreach (var unit in civilization.combatUnits.ToArray())
        {
            if (unit == null || unit.owner != civilization || unit.currentHealth <= 0 ||
                !unit.spaceLocation.IsInSolarSystemSpace) continue;
            unit.currentSpaceMovementPoints = Mathf.Max(0, unit.spaceMovementPointsPerTurn);
            if (unit.isPackedInSpaceFleet || unit.queuedSpacePath == null || unit.queuedSpacePath.Count == 0) continue;
            if (unit.queuedSpacePathCursor == unit.queuedSpacePath.Count - 1 &&
                unit.queuedSpacePath[unit.queuedSpacePathCursor] == unit.currentSpaceTileIndex) continue;
            if (worldGrid == null)
            {
                Debug.LogError($"[SpaceMovement] Cannot resume {unit.name}'s route without the space world grid.", unit);
                continue;
            }
            if (movement == null)
                movement = new GameObject("SpaceShipMovementController").AddComponent<SpaceShipMovementController>();
            if (movement.Grid != worldGrid) movement.SetGrid(worldGrid);
            movement.MoveAlongQueuedPath(unit);
        }
        var fleets = new List<SpaceFleet>();
        SpaceMapSelectionInfo.CollectFleets(fleets);
        foreach (var fleet in fleets)
        {
            if (fleet.ownerCivilizationId != civilization.gameObject.GetRuntimeId() || !fleet.isPacked ||
                fleet.queuedPath == null || fleet.queuedPath.Count == 0) continue;
            if (worldGrid == null)
            {
                Debug.LogError($"[SpaceMovement] Cannot resume fleet {fleet.fleetId}'s route without the space world grid.");
                continue;
            }
            if (movement == null)
                movement = new GameObject("SpaceShipMovementController").AddComponent<SpaceShipMovementController>();
            if (movement.Grid != worldGrid) movement.SetGrid(worldGrid);
            movement.MoveFleetAlongQueuedPath(fleet);
        }
        var map = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Exclude);
        if (map != null) map.RefreshTravelVisuals();
    }

    public int GetRouteCost(BaseUnit unit, IReadOnlyList<int> path, int cursor)
    {
        int cost = 0;
        for (int i = cursor + 1; i < path.Count; i++) cost += GetAbilityModifiedMovementCost(unit, Grid.GetTile(path[i]));
        return cost;
    }

    private int GetAbilityModifiedMovementCost(BaseUnit unit, SpaceHexTile tile)
    {
        int cost = Mathf.Max(1, tile != null ? tile.movementCost : 1);
        if (tile != null && SpaceFeatureManager.Instance != null)
            cost += SpaceFeatureManager.Instance.GetMovementCost(tile.tileIndex, unit);
        float efficiency = unit != null ? unit.GetAbilitySpaceMovementEfficiencyModifier() : 0f;
        return Mathf.Max(1, Mathf.CeilToInt(cost * Mathf.Max(0.25f, 1f - efficiency)));
    }

    private static bool IsAvailableTile(SpaceHexTile tile, IReadOnlyList<BaseUnit> movingUnits)
    {
        if (tile == null || tile.spacecraftIds == null) return false;
        foreach (int id in tile.spacecraftIds)
        {
            bool moving = false;
            foreach (var unit in movingUnits)
                if (unit != null && unit.gameObject.GetRuntimeId() == id) { moving = true; break; }
            if (moving) continue;
            var occupant = UnitRegistry.GetObject(id);
            if (occupant == null) return false;
            var occupantUnit = occupant.GetComponent<BaseUnit>();
            if (occupantUnit == null || occupantUnit.currentHealth > 0) return false;
        }
        return true;
    }

    IReadOnlyList<int> IUnitMovementDomain.FindPath(BaseUnit unit, int destination)
    {
        return unit == null || Pathfinder == null ? null : Pathfinder.FindPath(unit.currentSpaceTileIndex, destination);
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
    }

    public void RemoveDestroyedUnit(BaseUnit unit)
    {
        if (unit == null || !unit.spaceLocation.IsInSolarSystemSpace) return;
        RemoveFromCurrentSpaceTile(unit);
    }
}
