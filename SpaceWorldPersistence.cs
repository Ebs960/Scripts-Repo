using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SpaceWorldSaveData
{
    [Serializable] public sealed class CivilizationId { public int runtimeId; public int index; }
    [Serializable] public sealed class UnitId { public int runtimeId; public string persistentId; }

    public int starSystemId;
    public int radius;
    public float tileSize;
    public List<SpaceHexTile> tiles;
    public List<SpaceFeatureInstance> features;
    public List<SpaceImprovementInstance> improvements;
    public List<SpaceFleet> fleets;
    public List<SpaceConstructionOrder> constructionOrders;
    public int nextFeatureId;
    public int nextImprovementId;
    public int nextConstructionOrderId;
    public List<CivilizationId> civilizations = new List<CivilizationId>();
    public List<UnitId> units = new List<UnitId>();
}

public static class SpaceWorldPersistence
{
    public static SpaceWorldSaveData Capture()
    {
        var world = SpaceWorldManager.Instance?.CurrentSystem;
        var fleets = new List<SpaceFleet>();
        SpaceMapSelectionInfo.CollectFleets(fleets);
        if (world == null)
        {
            if (fleets.Count > 0) throw new InvalidOperationException("Cannot save fleets without a space world.");
            return null;
        }
        if (world.grid == null) throw new InvalidOperationException("Cannot save a space world without its grid.");
        var civs = CivilizationManager.Instance?.GetAllCivs();
        if (civs == null) throw new InvalidOperationException("Cannot save space ownership without civilizations.");
        var data = new SpaceWorldSaveData
        {
            starSystemId = world.starSystemId, radius = world.grid.radius, tileSize = world.grid.tileSize,
            tiles = world.grid.tiles, features = world.features, improvements = world.improvements,
            fleets = fleets, constructionOrders = world.constructionOrders,
            nextFeatureId = world.nextFeatureId, nextImprovementId = world.nextImprovementId,
            nextConstructionOrderId = world.nextConstructionOrderId
        };
        for (int i = 0; i < civs.Count; i++)
            if (civs[i] != null) data.civilizations.Add(new SpaceWorldSaveData.CivilizationId { runtimeId = civs[i].gameObject.GetRuntimeId(), index = i });
        foreach (var unit in UnitRegistry.GetCombatUnits())
            if (unit != null) data.units.Add(new SpaceWorldSaveData.UnitId { runtimeId = unit.gameObject.GetRuntimeId(), persistentId = unit.PersistentId });

        var copy = JsonUtility.FromJson<SpaceWorldSaveData>(JsonUtility.ToJson(data));
        if (world.features != null)
            for (int i = 0; i < world.features.Count; i++)
                if (world.features[i] != null && copy.features[i] != null)
                    copy.features[i].civilizationDiscoverySaveIds = world.features[i].civilizationsThatDiscoveredIt != null
                        ? new List<int>(world.features[i].civilizationsThatDiscoveredIt) : new List<int>();
        return copy;
    }

    public static void Restore(SpaceWorldSaveData saved)
    {
        if (saved == null) return;
        saved = JsonUtility.FromJson<SpaceWorldSaveData>(JsonUtility.ToJson(saved));
        var civs = CivilizationManager.Instance?.GetAllCivs();
        if (civs == null || saved.tiles == null || saved.radius < 1 || saved.tileSize <= 0f)
            throw new InvalidOperationException("Saved space world is incomplete.");
        var civIds = new Dictionary<int, int>();
        foreach (var entry in saved.civilizations ?? new List<SpaceWorldSaveData.CivilizationId>())
        {
            if (entry == null || entry.index < 0 || entry.index >= civs.Count || civs[entry.index] == null)
                throw new InvalidOperationException("A saved space civilization could not be restored.");
            civIds.Add(entry.runtimeId, civs[entry.index].gameObject.GetRuntimeId());
        }
        var unitIds = new Dictionary<int, CombatUnit>();
        foreach (var entry in saved.units ?? new List<SpaceWorldSaveData.UnitId>())
        {
            if (entry == null || string.IsNullOrEmpty(entry.persistentId)) continue;
            var unit = UnitRegistry.GetByPersistentId(entry.persistentId)?.GetComponent<CombatUnit>();
            if (unit != null) unitIds.Add(entry.runtimeId, unit);
        }
        var grid = new SpaceHexGrid(saved.radius, saved.tileSize);
        if (grid.tiles.Count != saved.tiles.Count) throw new InvalidOperationException("Saved space grid size is invalid.");
        for (int i = 0; i < saved.tiles.Count; i++)
        {
            var tile = saved.tiles[i];
            var generated = grid.GetTile(i);
            if (tile == null || tile.tileIndex != i || tile.q != generated.q || tile.r != generated.r)
                throw new InvalidOperationException("Saved space grid topology is invalid.");
            tile.controllingCivilizationId = RemapCivilization(tile.controllingCivilizationId, civIds);
            tile.spacecraftIds = new List<int>();
            grid.tiles[i] = tile;
        }
        var state = new SpaceWorldState
        {
            starSystemId = saved.starSystemId, grid = grid,
            features = saved.features ?? new List<SpaceFeatureInstance>(),
            improvements = saved.improvements ?? new List<SpaceImprovementInstance>(),
            fleets = saved.fleets ?? new List<SpaceFleet>(),
            constructionOrders = saved.constructionOrders ?? new List<SpaceConstructionOrder>(),
            nextFeatureId = saved.nextFeatureId, nextImprovementId = saved.nextImprovementId,
            nextConstructionOrderId = saved.nextConstructionOrderId
        };
        foreach (var feature in state.features)
        {
            if (feature == null) continue;
            var discoveries = new List<int>();
            foreach (int id in feature.civilizationDiscoverySaveIds ?? new List<int>())
                discoveries.Add(RemapCivilization(id, civIds));
            feature.civilizationDiscoverySaveIds = discoveries;
            feature.AfterDeserialize();
        }
        foreach (var improvement in state.improvements)
            if (improvement != null) improvement.ownerCivilizationId = RemapCivilization(improvement.ownerCivilizationId, civIds);
        foreach (var order in state.constructionOrders)
        {
            if (order == null) continue;
            order.ownerCivilizationId = RemapCivilization(order.ownerCivilizationId, civIds);
            var contributors = new List<int>();
            foreach (int id in order.contributingUnitIds ?? new List<int>())
            {
                if (!unitIds.TryGetValue(id, out var unit)) throw new InvalidOperationException("A saved construction contributor is missing.");
                contributors.Add(unit.gameObject.GetRuntimeId());
            }
            order.contributingUnitIds = contributors;
        }
        var fleetIds = new HashSet<int>();
        var assignedUnits = new HashSet<CombatUnit>();
        foreach (var fleet in state.fleets)
        {
            if (fleet == null || !fleetIds.Add(fleet.fleetId) || fleet.memberUnitIds == null || fleet.memberUnitIds.Count == 0)
                throw new InvalidOperationException("Saved fleet membership is invalid.");
            fleet.ownerCivilizationId = RemapCivilization(fleet.ownerCivilizationId, civIds);
            var members = new List<int>();
            foreach (int id in fleet.memberUnitIds)
            {
                if (!unitIds.TryGetValue(id, out var unit) || !assignedUnits.Add(unit) ||
                    unit.owner == null || unit.owner.gameObject.GetRuntimeId() != fleet.ownerCivilizationId ||
                    unit.spaceFleetId != fleet.fleetId)
                    throw new InvalidOperationException($"A member of saved fleet {fleet.fleetId} could not be restored.");
                if (fleet.isPacked && (!unit.spaceLocation.IsInSolarSystemSpace ||
                    unit.currentSpaceTileIndex != fleet.currentSpaceTileIndex))
                    throw new InvalidOperationException($"Saved fleet {fleet.fleetId} has inconsistent locations.");
                members.Add(unit.gameObject.GetRuntimeId());
            }
            fleet.memberUnitIds = members;
        }
        foreach (var unit in unitIds.Values)
        {
            if (unit.spaceFleetId >= 0 && !assignedUnits.Contains(unit))
                throw new InvalidOperationException($"Saved spacecraft {unit.name} refers to a missing fleet.");
            if (!unit.spaceLocation.IsInSolarSystemSpace) continue;
            var tile = grid.GetTile(unit.currentSpaceTileIndex);
            if (tile == null || unit.spaceLocation.spaceTileIndex != unit.currentSpaceTileIndex)
                throw new InvalidOperationException($"Saved spacecraft {unit.name} has an invalid space location.");
            tile.spacecraftIds.Add(unit.gameObject.GetRuntimeId());
        }

        var remappedAdmirals = new List<(AdmiralInstance admiral, int owner, int captor)>();
        if (AdmiralManager.Instance != null)
            foreach (var admiral in AdmiralManager.Instance.admirals)
                if (admiral != null)
                    remappedAdmirals.Add((admiral, RemapCivilization(admiral.ownerCivilizationId, civIds),
                        RemapCivilization(admiral.capturedByCivilizationId, civIds)));
        foreach (var fleet in state.fleets)
            if (fleet.admiralId >= 0 && (AdmiralManager.Instance == null || AdmiralManager.Instance.GetAdmiral(fleet.admiralId) == null))
                throw new InvalidOperationException($"The admiral of saved fleet {fleet.fleetId} could not be restored.");

        if (SpaceWorldManager.Instance == null) new GameObject("SpaceWorldManager").AddComponent<SpaceWorldManager>();
        if (SpaceFleetManager.Instance == null) new GameObject("SpaceFleetManager").AddComponent<SpaceFleetManager>();
        if (SpaceShipMovementController.Instance == null) new GameObject("SpaceShipMovementController").AddComponent<SpaceShipMovementController>();
        if (SpaceOccupancyManager.Instance == null) new GameObject("SpaceOccupancyManager").AddComponent<SpaceOccupancyManager>();
        SpaceWorldManager.Instance.LoadSystem(state);
        SpaceFleetManager.Instance.RestoreFleets(state.fleets);
        SpaceShipMovementController.Instance.SetGrid(grid);
        SpaceOccupancyManager.Instance.spaceGrid = grid;
        if (SpaceCombatManager.Instance != null) SpaceCombatManager.Instance.spaceGrid = grid;
        foreach (var unit in unitIds.Values)
        {
            if (unit.spaceLocation.IsInSolarSystemSpace)
            {
                SpaceWorldManager.Instance.Entities.Register(unit);
                unit.transform.position = grid.GetWorldPosition(unit.currentSpaceTileIndex) + Vector3.up * 1.2f;
            }
            var fleet = unit.spaceFleetId >= 0 ? SpaceFleetManager.Instance.GetFleet(unit.spaceFleetId) : null;
            unit.isPackedInSpaceFleet = fleet != null && fleet.isPacked;
            if (unit.isPackedInSpaceFleet) unit.gameObject.SetActive(false);
            else if (fleet != null && fleet.isDeployed) unit.gameObject.SetActive(true);
        }
        foreach (var entry in remappedAdmirals)
        {
            entry.admiral.ownerCivilizationId = entry.owner;
            entry.admiral.capturedByCivilizationId = entry.captor;
        }
        foreach (var fleet in state.fleets)
            if (fleet.admiralId >= 0) AdmiralManager.Instance.GetAdmiral(fleet.admiralId).assignedFleetId = fleet.fleetId;
    }

    private static int RemapCivilization(int oldId, Dictionary<int, int> ids)
    {
        if (oldId == -1) return oldId;
        if (!ids.TryGetValue(oldId, out int result))
            throw new InvalidOperationException($"Saved space civilization id {oldId} could not be resolved.");
        return result;
    }
}
