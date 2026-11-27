using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpaceFleetRole { Recon, Patrol, Escort, Strike, Siege, Carrier, Invasion, ConstructionSupport, TradeProtection, Blockade }

[CreateAssetMenu(fileName = "Space Fleet Role Template", menuName = "Data/AI/Space Fleet Role Template")]
public class SpaceFleetRoleTemplate : ScriptableObject
{
    public SpaceFleetRole role;
    [Range(0f, 1f)] public float scouts;
    [Range(0f, 1f)] public float directCombatShips;
    [Range(0f, 1f)] public float areaAttackShips;
    [Range(0f, 1f)] public float carriers;
    [Range(0f, 1f)] public float repairShips;
    [Range(0f, 1f)] public float transports;
    [Range(0f, 1f)] public float constructionShips;
}

[Serializable]
public class SpaceFleet
{
    public int fleetId;
    public int ownerCivilizationId;
    public string fleetName;
    public int admiralId = -1;
    public List<int> memberUnitIds = new List<int>();
    public int currentSpaceTileIndex = -1;
    public SpaceLocation location;
    public List<int> queuedPath = new List<int>();
    public int queuedPathCursor;
    public bool isPacked;
    public bool isDeployed;
}

public class SpaceFleetManager : MonoBehaviour
{
    public static SpaceFleetManager Instance { get; private set; }
    public List<SpaceFleet> fleets = new List<SpaceFleet>();
    private int nextFleetId = 1;
    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public void RestoreFleets(List<SpaceFleet> restored)
    {
        fleets = restored ?? new List<SpaceFleet>();
        nextFleetId = 1;
        foreach (var fleet in fleets)
            if (fleet != null) nextFleetId = Mathf.Max(nextFleetId, fleet.fleetId + 1);
    }
    public SpaceFleet CreateFleet(int ownerCivilizationId, string fleetName, int admiralId, IEnumerable<CombatUnit> members)
    {
        if (members == null) return null;
        var units = new List<CombatUnit>();
        var seen = new HashSet<int>();
        foreach (var unit in members)
        {
            if (unit == null || unit.currentHealth <= 0 || unit.owner == null ||
                unit.owner.gameObject.GetRuntimeId() != ownerCivilizationId || unit.spaceFleetId >= 0 ||
                !unit.spaceLocation.IsInSolarSystemSpace || unit.currentSpaceTileIndex != unit.spaceLocation.spaceTileIndex ||
                !seen.Add(unit.gameObject.GetRuntimeId())) return null;
            if (units.Count > 0 && unit.currentSpaceTileIndex != units[0].currentSpaceTileIndex) return null;
            units.Add(unit);
        }
        if (units.Count == 0) return null;
        var admiral = admiralId >= 0 && AdmiralManager.Instance != null ? AdmiralManager.Instance.GetAdmiral(admiralId) : null;
        if (admiralId >= 0 && (admiral == null || admiral.ownerCivilizationId != ownerCivilizationId ||
            admiral.assignedFleetId >= 0 || admiral.status == AdmiralStatus.Captured || admiral.status == AdmiralStatus.Killed)) return null;
        while (GetFleet(nextFleetId) != null) nextFleetId++;
        var fleet = new SpaceFleet { fleetId = nextFleetId++, ownerCivilizationId = ownerCivilizationId,
            fleetName = fleetName, admiralId = admiralId, isDeployed = true,
            currentSpaceTileIndex = units[0].currentSpaceTileIndex, location = units[0].spaceLocation };
        foreach (var unit in units)
        {
            UnitRegistry.Register(unit.gameObject);
            fleet.memberUnitIds.Add(unit.gameObject.GetRuntimeId());
            unit.spaceFleetId = fleet.fleetId;
        }
        if (admiral != null) admiral.assignedFleetId = fleet.fleetId;
        fleets.Add(fleet);
        var worldFleets = SpaceWorldManager.Instance?.CurrentSystem?.fleets;
        if (worldFleets != null && !worldFleets.Contains(fleet)) worldFleets.Add(fleet);
        SpaceWorldManager.Instance?.Entities.Register(fleet);
        return fleet;
    }

    public bool DetachForShipOrder(CombatUnit unit)
    {
        if (unit == null || unit.isPackedInSpaceFleet) return false;
        if (unit.spaceFleetId < 0) return true;
        var source = GetFleet(unit.spaceFleetId);
        int id = unit.gameObject.GetRuntimeId();
        if (source == null || source.isPacked || source.memberUnitIds == null || !source.memberUnitIds.Contains(id)) return false;
        if (source.memberUnitIds.Count == 1) return true;
        int index = source.memberUnitIds.IndexOf(id);
        source.memberUnitIds.RemoveAt(index);
        unit.spaceFleetId = -1;
        var detached = CreateFleet(source.ownerCivilizationId, unit.UnitName, -1, new[] { unit });
        if (detached == null)
        {
            unit.spaceFleetId = source.fleetId;
            source.memberUnitIds.Insert(index, id);
            return false;
        }
        var leader = UnitRegistry.GetObject(source.memberUnitIds[0])?.GetComponent<CombatUnit>();
        if (leader != null)
        {
            source.currentSpaceTileIndex = leader.currentSpaceTileIndex;
            source.location = leader.spaceLocation;
        }
        return true;
    }
    public SpaceFleet GetFleet(int fleetId)
    {
        if (SpaceWorldManager.Instance != null && SpaceWorldManager.Instance.Entities.TryGetFleet(fleetId, out var indexed)) return indexed;
        return fleets.Find(f => f.fleetId == fleetId);
    }
    public static bool TryGetPackedBattleMembers(CombatUnit unit, out List<CombatUnit> members)
    {
        members = null;
        if (unit == null || !unit.isPackedInSpaceFleet || Instance == null || SpaceShipMovementController.Instance == null) return false;
        var fleet = Instance.GetFleet(unit.spaceFleetId);
        return fleet != null && fleet.memberUnitIds != null && fleet.memberUnitIds.Contains(unit.gameObject.GetRuntimeId()) &&
            SpaceShipMovementController.Instance.TryGetPackedMembers(fleet, out members, out _);
    }
    public bool CanPack(SpaceFleet fleet, out string reason) { reason = null; if (fleet == null) { reason = "missing fleet"; return false; } var admiral = AdmiralManager.Instance != null && fleet.admiralId >= 0 ? AdmiralManager.Instance.GetAdmiral(fleet.admiralId) : null; if (admiral != null && (admiral.status == AdmiralStatus.Captured || admiral.status == AdmiralStatus.Killed)) { reason = "fleet admiral is unavailable"; return false; } return true; }
    public AdmiralFleetLossOutcome ResolveFleetDestroyed(SpaceFleet fleet, int enemyCivilizationId = -1) { if (fleet == null || fleet.admiralId < 0 || AdmiralManager.Instance == null) return AdmiralFleetLossOutcome.Killed; var outcome = AdmiralManager.Instance.ResolveFleetDestroyed(fleet.admiralId, enemyCivilizationId); fleet.admiralId = -1; fleet.isPacked = false; fleet.isDeployed = false; return outcome; }

    public void RemoveUnit(BaseUnit unit)
    {
        var fleet = unit != null ? GetFleet(unit.spaceFleetId) : null;
        if (fleet == null || fleet.memberUnitIds == null || !fleet.memberUnitIds.Contains(unit.gameObject.GetRuntimeId()))
        {
            if (unit != null) Debug.LogWarning($"[SpaceFleet] Cannot remove {unit.name}: its fleet membership is invalid.", unit);
            return;
        }
        if (unit.currentHealth <= 0)
        {
            fleet.memberUnitIds.Remove(unit.gameObject.GetRuntimeId());
            unit.spaceFleetId = -1;
            return;
        }
        if (fleet.memberUnitIds.Count == 1) return;
        if (!fleet.isPacked)
        {
            if (!(unit is CombatUnit deployed) || !DetachForShipOrder(deployed))
                Debug.LogWarning($"[SpaceFleet] Cannot detach {unit.name} from fleet {fleet.fleetId}.", unit);
            return;
        }
        if (!(unit is CombatUnit packed) || !unit.isPackedInSpaceFleet)
        {
            Debug.LogWarning($"[SpaceFleet] Cannot detach {unit.name}: its packed state is inconsistent.", unit);
            return;
        }
        int index = fleet.memberUnitIds.IndexOf(unit.gameObject.GetRuntimeId());
        fleet.memberUnitIds.RemoveAt(index);
        unit.spaceFleetId = -1;
        var detached = CreateFleet(fleet.ownerCivilizationId, unit.UnitName, -1, new[] { packed });
        if (detached == null)
        {
            unit.spaceFleetId = fleet.fleetId;
            fleet.memberUnitIds.Insert(index, unit.gameObject.GetRuntimeId());
            Debug.LogWarning($"[SpaceFleet] Could not create a detached fleet for {unit.name}.", unit);
            return;
        }
        detached.isPacked = true;
        detached.isDeployed = false;
    }

    public void HandleUnitDestroyed(BaseUnit unit)
    {
        var fleet = unit != null ? GetFleet(unit.spaceFleetId) : null;
        if (fleet == null) return;
        fleet.memberUnitIds?.Remove(unit.gameObject.GetRuntimeId());
        unit.spaceFleetId = -1;
        if (fleet.memberUnitIds == null) return;
        if (fleet.memberUnitIds.Count != 0)
        {
            if (!fleet.isPacked)
            {
                var leader = UnitRegistry.GetObject(fleet.memberUnitIds[0])?.GetComponent<CombatUnit>();
                if (leader != null) { fleet.currentSpaceTileIndex = leader.currentSpaceTileIndex; fleet.location = leader.spaceLocation; }
            }
            return;
        }
        ResolveFleetDestroyed(fleet);
        fleets.Remove(fleet);
        SpaceWorldManager.Instance?.CurrentSystem?.fleets?.Remove(fleet);
        SpaceWorldManager.Instance?.Entities.RemoveFleet(fleet.fleetId);
    }

    public bool MergeFleets(int targetFleetId, int sourceFleetId)
    {
        var target = GetFleet(targetFleetId); var source = GetFleet(sourceFleetId);
        if (target == null || source == null || target == source) return false;
        if (target.ownerCivilizationId != source.ownerCivilizationId || target.isPacked != source.isPacked ||
            target.isDeployed != source.isDeployed || target.memberUnitIds == null || source.memberUnitIds == null ||
            !target.location.IsInSolarSystemSpace || !source.location.IsInSolarSystemSpace ||
            target.currentSpaceTileIndex != source.currentSpaceTileIndex ||
            (target.admiralId >= 0 && source.admiralId >= 0)) return false;

        var ids = new HashSet<int>();
        foreach (int id in target.memberUnitIds)
        {
            var unit = UnitRegistry.GetObject(id)?.GetComponent<CombatUnit>();
            if (!ids.Add(id) || unit == null || unit.currentHealth <= 0 || unit.spaceFleetId != target.fleetId ||
                unit.owner == null || unit.owner.gameObject.GetRuntimeId() != target.ownerCivilizationId ||
                !unit.spaceLocation.IsInSolarSystemSpace || unit.currentSpaceTileIndex != target.currentSpaceTileIndex) return false;
        }
        var transferred = new List<CombatUnit>();
        foreach (int id in source.memberUnitIds)
        {
            var unit = UnitRegistry.GetObject(id)?.GetComponent<CombatUnit>();
            if (!ids.Add(id) || unit == null || unit.currentHealth <= 0 || unit.spaceFleetId != source.fleetId ||
                unit.owner == null || unit.owner.gameObject.GetRuntimeId() != target.ownerCivilizationId ||
                !unit.spaceLocation.IsInSolarSystemSpace || unit.currentSpaceTileIndex != target.currentSpaceTileIndex) return false;
            transferred.Add(unit);
        }
        foreach (var unit in transferred)
        {
            target.memberUnitIds.Add(unit.gameObject.GetRuntimeId());
            unit.spaceFleetId = target.fleetId;
        }
        if (target.admiralId < 0) target.admiralId = source.admiralId;
        var admiral = AdmiralManager.Instance != null && target.admiralId >= 0 ? AdmiralManager.Instance.GetAdmiral(target.admiralId) : null;
        if (admiral != null) admiral.assignedFleetId = target.fleetId;
        source.memberUnitIds.Clear();
        fleets.Remove(source);
        SpaceWorldManager.Instance?.CurrentSystem?.fleets?.Remove(source);
        SpaceWorldManager.Instance?.Entities.RemoveFleet(source.fleetId);
        return true;
    }
}

public class FleetDeploymentManager : MonoBehaviour
{
    public SpaceHexGrid spaceGrid;
    private void Awake() { if (spaceGrid == null) spaceGrid = SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : new SpaceHexGrid(); }
    public bool UnpackFleet(SpaceFleet fleet, out string reason)
    {
        reason = null;
        var movement = SpaceShipMovementController.Instance;
        if (movement == null || movement.Grid != spaceGrid) { reason = "space movement grid is unavailable"; return false; }
        if (!movement.TryGetPackedMembers(fleet, out _, out reason)) return false;
        var placements = FindPlacements(fleet, out reason); if (placements == null) return false;
        foreach (var pair in placements)
        {
            var unit = pair.Key; int tile = pair.Value;
            movement.PlaceOnSpaceTile(unit, tile);
            unit.isPackedInSpaceFleet = false;
            unit.gameObject.SetActive(true);
        }
        if (fleet.queuedPath == null) fleet.queuedPath = new List<int>();
        else fleet.queuedPath.Clear();
        fleet.queuedPathCursor = 0;
        fleet.isPacked = false; fleet.isDeployed = true; return true;
    }
    public bool PackFleet(SpaceFleet fleet, int gatherRadius, out string reason)
    {
        reason = null;
        if (fleet == null || fleet.isPacked || fleet.memberUnitIds == null || fleet.memberUnitIds.Count == 0) { reason = "fleet is missing, empty, or already packed"; return false; }
        if (SpaceFleetManager.Instance == null) { reason = "fleet manager is unavailable"; return false; }
        if (!SpaceFleetManager.Instance.CanPack(fleet, out reason)) return false;
        var movement = SpaceShipMovementController.Instance;
        if (movement == null || movement.Grid != spaceGrid) { reason = "space movement grid is unavailable"; return false; }
        var members = ResolveMembers(fleet);
        if (members.Count != fleet.memberUnitIds.Count) { reason = "a fleet member is missing or destroyed"; return false; }
        int center = ChooseCenter(members);
        var centerTile = spaceGrid.GetTile(center);
        if (centerTile == null || centerTile.blocksMovement) { reason = "fleet center is not a legal space tile"; return false; }
        var seen = new HashSet<int>();
        foreach (var u in members)
        {
            if (!seen.Add(u.gameObject.GetRuntimeId()) || u.spaceFleetId != fleet.fleetId || u.isPackedInSpaceFleet ||
                u.owner == null || u.owner.gameObject.GetRuntimeId() != fleet.ownerCivilizationId ||
                !u.spaceLocation.IsInSolarSystemSpace || u.spaceLocation.spaceTileIndex != u.currentSpaceTileIndex ||
                spaceGrid.GetTile(u.currentSpaceTileIndex) == null)
            { reason = "fleet membership or location is inconsistent"; return false; }
            if (spaceGrid.GetDistance(center, u.currentSpaceTileIndex) > gatherRadius) { reason = "fleet members are too widely separated"; return false; }
        }
        foreach (var u in members)
        {
            movement.PlaceOnSpaceTile(u, center);
            if (u.queuedSpacePath == null) u.queuedSpacePath = new List<int>();
            else u.queuedSpacePath.Clear();
            u.queuedSpacePathCursor = 0;
            u.isPackedInSpaceFleet = true; u.currentSpaceMovementPoints = 0;
            u.gameObject.SetActive(false);
        }
        if (fleet.queuedPath == null) fleet.queuedPath = new List<int>();
        else fleet.queuedPath.Clear();
        fleet.queuedPathCursor = 0;
        fleet.currentSpaceTileIndex = center; fleet.location = SpaceLocation.InSpace(center); fleet.isPacked = true; fleet.isDeployed = false; return true;
    }
    private Dictionary<CombatUnit,int> FindPlacements(SpaceFleet fleet, out string reason)
    {
        reason = null; var members = ResolveMembers(fleet); var result = new Dictionary<CombatUnit,int>(); var used = new HashSet<int>(); var spiral = BuildSpiral(fleet.currentSpaceTileIndex, 3);
        var fleetIds = new HashSet<int>(fleet.memberUnitIds);
        foreach (var unit in members)
        {
            bool placed = false; foreach (int tile in spiral) if (!used.Contains(tile) && IsLegalDeploymentTile(tile, fleetIds)) { result[unit] = tile; used.Add(tile); placed = true; break; }
            if (!placed) { reason = "not enough legal deployment spaces"; return null; }
        }
        return result;
    }
    private List<int> BuildSpiral(int center, int maxRing) { var list = new List<int>(); for (int ring=0; ring<=maxRing; ring++) foreach (var tile in spaceGrid.tiles) if (spaceGrid.GetDistance(center, tile.tileIndex) == ring) list.Add(tile.tileIndex); return list; }
    private bool IsLegalDeploymentTile(int tileIndex, HashSet<int> fleetIds)
    {
        var tile = spaceGrid.GetTile(tileIndex);
        if (tile == null || tile.blocksMovement || tile.terrainType == SpaceTerrainType.Planet) return false;
        foreach (int id in tile.spacecraftIds)
            if (!fleetIds.Contains(id) && UnitRegistry.GetObject(id) != null) return false;
        return true;
    }
    private List<CombatUnit> ResolveMembers(SpaceFleet fleet) { var list = new List<CombatUnit>(); foreach (int id in fleet.memberUnitIds) { var go = UnitRegistry.GetObject(id); var u = go != null ? go.GetComponent<CombatUnit>() : null; if (u != null && u.currentHealth > 0) list.Add(u); } return list; }
    private int ChooseCenter(List<CombatUnit> members) => members.Count == 0 ? -1 : members[0].currentSpaceTileIndex;
}
