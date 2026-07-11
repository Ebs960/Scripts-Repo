using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CivilizationSpaceVisionState
{
    public int civilizationId;
    public HashSet<int> exploredSpaceTiles = new HashSet<int>();
    public HashSet<int> currentlyVisibleSpaceTiles = new HashSet<int>();
}

public class SpaceReconFogOfWar : MonoBehaviour
{
    public static SpaceReconFogOfWar Instance { get; private set; }
    private readonly Dictionary<int, CivilizationSpaceVisionState> states = new Dictionary<int, CivilizationSpaceVisionState>();
    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; }
    public CivilizationSpaceVisionState GetState(int civId) { if (!states.TryGetValue(civId, out var s)) states[civId] = s = new CivilizationSpaceVisionState { civilizationId = civId }; return s; }
    public void RevealRadius(int civId, SpaceHexGrid grid, int centerTile, int radius, bool currentVisibility)
    {
        var state = GetState(civId); foreach (var tile in grid.tiles) if (grid.GetDistance(centerTile, tile.tileIndex) <= radius) { state.exploredSpaceTiles.Add(tile.tileIndex); if (currentVisibility) state.currentlyVisibleSpaceTiles.Add(tile.tileIndex); }
    }
    public bool PerformRecon(BaseUnit unit, SpaceHexGrid grid, int targetTile)
    {
        if (unit == null || !unit.canPerformSpaceRecon || grid == null) return false;
        RevealRadius(unit.owner != null ? unit.owner.GetInstanceID() : -1, grid, targetTile, unit.spaceReconRange, true); return true;
    }
}
