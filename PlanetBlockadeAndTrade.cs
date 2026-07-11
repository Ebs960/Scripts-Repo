using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PlanetBlockadeState
{
    public int planetId;
    public bool isBlockaded;
    public List<int> hostileControlledSectorDirections = new List<int>();
    public Dictionary<int, int> controllingCivilizationIdByDirection = new Dictionary<int, int>();
}

public class PlanetBlockadeManager : MonoBehaviour
{
    public SpaceHexGrid spaceGrid;
    public List<PlanetBlockadeState> blockadeStates = new List<PlanetBlockadeState>();
    private void Awake() { if (spaceGrid == null) spaceGrid = SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : new SpaceHexGrid(); }
    public PlanetBlockadeState RecalculatePlanet(int planetId, Civilization planetOwner)
    {
        var state = blockadeStates.Find(s => s.planetId == planetId); if (state == null) { state = new PlanetBlockadeState { planetId = planetId }; blockadeStates.Add(state); }
        state.hostileControlledSectorDirections.Clear(); state.controllingCivilizationIdByDirection.Clear();
        foreach (var tile in spaceGrid.tiles)
        {
            if (!tile.isPlanetOrbitSector || tile.associatedPlanetId != planetId) continue;
            var strengths = new Dictionary<Civilization, int>();
            foreach (int id in tile.spacecraftIds)
            {
                var go = UnitRegistry.GetObject(id); var unit = go != null ? go.GetComponent<CombatUnit>() : null;
                if (unit == null || unit.currentHealth <= 0 || unit.CurrentSpaceAttack <= 0) continue;
                if (!AircraftMissionManager.IsHostile(unit.owner, planetOwner)) continue;
                strengths[unit.owner] = (strengths.TryGetValue(unit.owner, out int s) ? s : 0) + unit.CurrentSpaceAttack + unit.CurrentDefense;
            }
            Civilization controller = null; int best = 0; foreach (var kv in strengths) if (kv.Value > best) { controller = kv.Key; best = kv.Value; }
            if (controller != null) { state.hostileControlledSectorDirections.Add(tile.orbitSectorDirection); state.controllingCivilizationIdByDirection[tile.orbitSectorDirection] = controller.gameObject.GetRuntimeId(); }
        }
        state.isBlockaded = state.hostileControlledSectorDirections.Count >= 3;
        return state;
    }
}

[Serializable] public class ResourceAmount { public string resourceId; public int amount; }
[Serializable] public class SpaceCargoManifest { public List<int> carriedUnitIds = new List<int>(); public List<ResourceAmount> resources = new List<ResourceAmount>(); public int civilianPopulation; }
[Serializable] public class SpaceTradeRoute { public int routeId; public int originPlanetId; public int destinationPlanetId; public List<int> path = new List<int>(); public int pathCursor; public bool suspendedByBlockade; public SpaceCargoManifest cargo = new SpaceCargoManifest(); }
public class SpaceTradeManager : MonoBehaviour { public List<SpaceTradeRoute> routes = new List<SpaceTradeRoute>(); public void SetPlanetBlockaded(int planetId, bool blockaded) { foreach (var r in routes) if (r.originPlanetId == planetId || r.destinationPlanetId == planetId) r.suspendedByBlockade = blockaded; } }
public class CargoShipAI : MonoBehaviour { public SpaceTradeRoute assignedRoute; public void TickRoute() { if (assignedRoute == null || assignedRoute.suspendedByBlockade || assignedRoute.path == null || assignedRoute.path.Count == 0) return; assignedRoute.pathCursor = Mathf.Min(assignedRoute.pathCursor + 1, assignedRoute.path.Count - 1); } }
