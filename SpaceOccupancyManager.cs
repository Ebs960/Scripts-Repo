using UnityEngine;

public class SpaceOccupancyManager : MonoBehaviour
{
    public static SpaceOccupancyManager Instance { get; private set; }
    public SpaceHexGrid spaceGrid;
    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; if (spaceGrid == null) spaceGrid = SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : new SpaceHexGrid(); }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public bool RegisterUnit(CombatUnit unit, int tileIndex)
    {
        var tile = spaceGrid != null ? spaceGrid.GetTile(tileIndex) : null; if (unit == null || tile == null || tile.blocksMovement) return false;
        UnregisterUnit(unit); int id = unit.gameObject.GetRuntimeId(); if (!tile.spacecraftIds.Contains(id)) tile.spacecraftIds.Add(id);
        unit.currentSpaceTileIndex = tileIndex; unit.spaceLocation = SpaceLocation.InSpace(tileIndex);
        unit.planetIndex = -1; unit.currentTileIndex = -1;
        unit.transform.position = spaceGrid.GetWorldPosition(tileIndex) + Vector3.up * 1.2f;
        SpaceWorldManager.Instance?.Entities.Register(unit);
        return true;
    }
    public void UnregisterUnit(CombatUnit unit)
    {
        if (unit == null || spaceGrid == null) return; int id = unit.gameObject.GetRuntimeId(); foreach (var tile in spaceGrid.tiles) tile.spacecraftIds.Remove(id);
    }
}
