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
public class SpaceTradeManager : MonoBehaviour { public void SetPlanetBlockaded(int planetId, bool blockaded) { TradeNetworkManager.Instance?.NotifyPlanetBlockadeChanged(planetId); } }
public class CargoShipAI : MonoBehaviour { public int assignedUnifiedRouteId; public int pathCursor; public void TickRoute() { var route = TradeNetworkManager.Instance?.activeRoutes.Find(r => r.routeId == assignedUnifiedRouteId); if (route == null || route.suspended || route.segments == null || route.segments.Count == 0) return; pathCursor = Mathf.Min(pathCursor + 1, route.segments.Count - 1); } }
