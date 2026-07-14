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
    public SpaceFleet CreateFleet(int ownerCivilizationId, string fleetName, int admiralId, IEnumerable<CombatUnit> members)
    {
        var fleet = new SpaceFleet { fleetId = nextFleetId++, ownerCivilizationId = ownerCivilizationId, fleetName = fleetName, admiralId = admiralId };
        foreach (var unit in members) if (unit != null) { fleet.memberUnitIds.Add(unit.gameObject.GetRuntimeId()); unit.spaceFleetId = fleet.fleetId; fleet.currentSpaceTileIndex = unit.currentSpaceTileIndex; fleet.location = unit.spaceLocation; }
        fleets.Add(fleet); return fleet;
    }
    public SpaceFleet GetFleet(int fleetId) => fleets.Find(f => f.fleetId == fleetId);
    public bool CanPack(SpaceFleet fleet, out string reason) { reason = null; if (fleet == null) { reason = "missing fleet"; return false; } if (fleet.admiralId < 0) { reason = "fleet requires an admiral before packing"; return false; } var admiral = AdmiralManager.Instance != null ? AdmiralManager.Instance.GetAdmiral(fleet.admiralId) : null; if (admiral != null && (admiral.status == AdmiralStatus.Captured || admiral.status == AdmiralStatus.Killed)) { reason = "fleet admiral is unavailable"; return false; } return true; }
    public AdmiralFleetLossOutcome ResolveFleetDestroyed(SpaceFleet fleet, int enemyCivilizationId = -1) { if (fleet == null || fleet.admiralId < 0 || AdmiralManager.Instance == null) return AdmiralFleetLossOutcome.Killed; var outcome = AdmiralManager.Instance.ResolveFleetDestroyed(fleet.admiralId, enemyCivilizationId); fleet.admiralId = -1; fleet.isPacked = false; fleet.isDeployed = false; return outcome; }
}

public class FleetDeploymentManager : MonoBehaviour
{
    public SpaceHexGrid spaceGrid;
    private void Awake() { if (spaceGrid == null) spaceGrid = SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : new SpaceHexGrid(); }
    public bool UnpackFleet(SpaceFleet fleet, out string reason)
    {
        reason = null; if (fleet == null) { reason = "missing fleet"; return false; }
        var placements = FindPlacements(fleet, out reason); if (placements == null) return false;
        foreach (var pair in placements)
        {
            var unit = pair.Key; int tile = pair.Value;
            unit.isPackedInSpaceFleet = false; unit.currentSpaceTileIndex = tile; unit.spaceLocation = SpaceLocation.InSpace(tile); if (unit.gameObject != null) unit.gameObject.SetActive(true);
        }
        fleet.isPacked = false; fleet.isDeployed = true; return true;
    }
    public bool PackFleet(SpaceFleet fleet, int gatherRadius, out string reason)
    {
        reason = null; if (fleet == null) { reason = "missing fleet"; return false; }
        if (fleet.admiralId < 0) { reason = "fleet requires an assigned admiral"; return false; }
        var members = ResolveMembers(fleet); if (members.Count != fleet.memberUnitIds.Count) { reason = "a fleet member is missing or destroyed"; return false; }
        int center = ChooseCenter(members); foreach (var u in members) if (spaceGrid.GetDistance(center, u.currentSpaceTileIndex) > gatherRadius) { reason = "fleet members are too widely separated"; return false; }
        foreach (var u in members) { u.isPackedInSpaceFleet = true; u.currentSpaceMovementPoints = 0; if (u.gameObject != null) u.gameObject.SetActive(false); }
        fleet.currentSpaceTileIndex = center; fleet.location = SpaceLocation.InSpace(center); fleet.isPacked = true; fleet.isDeployed = false; return true;
    }
    private Dictionary<CombatUnit,int> FindPlacements(SpaceFleet fleet, out string reason)
    {
        reason = null; var members = ResolveMembers(fleet); var result = new Dictionary<CombatUnit,int>(); var used = new HashSet<int>(); var spiral = BuildSpiral(fleet.currentSpaceTileIndex, 3);
        foreach (var unit in members)
        {
            bool placed = false; foreach (int tile in spiral) if (!used.Contains(tile) && IsLegalDeploymentTile(tile)) { result[unit] = tile; used.Add(tile); placed = true; break; }
            if (!placed) { reason = "not enough legal deployment spaces"; return null; }
        }
        return result;
    }
    private List<int> BuildSpiral(int center, int maxRing) { var list = new List<int>(); for (int ring=0; ring<=maxRing; ring++) foreach (var tile in spaceGrid.tiles) if (spaceGrid.GetDistance(center, tile.tileIndex) == ring) list.Add(tile.tileIndex); return list; }
    private bool IsLegalDeploymentTile(int tileIndex) { var t = spaceGrid.GetTile(tileIndex); return t != null && !t.blocksMovement && t.terrainType != SpaceTerrainType.Planet; }
    private List<CombatUnit> ResolveMembers(SpaceFleet fleet) { var list = new List<CombatUnit>(); foreach (int id in fleet.memberUnitIds) { var go = UnitRegistry.GetObject(id); var u = go != null ? go.GetComponent<CombatUnit>() : null; if (u != null && u.currentHealth > 0) list.Add(u); } return list; }
    private int ChooseCenter(List<CombatUnit> members) => members.Count == 0 ? -1 : members[0].currentSpaceTileIndex;
}
