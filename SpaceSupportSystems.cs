using System;
using System.Collections.Generic;
using UnityEngine;

public class SpaceVisionManager : MonoBehaviour
{
    public readonly Dictionary<int, HashSet<int>> visibleTilesByCivilization = new Dictionary<int, HashSet<int>>();
    public readonly Dictionary<int, HashSet<int>> exploredTilesByCivilization = new Dictionary<int, HashSet<int>>();
    public void RecalculateVisibility(Civilization civilization)
    {
        if (civilization == null || SpaceWorldManager.Instance?.Grid == null) return;
        int civId = civilization.gameObject.GetRuntimeId();
        if (!visibleTilesByCivilization.TryGetValue(civId, out var visible)) visibleTilesByCivilization[civId] = visible = new HashSet<int>();
        if (!exploredTilesByCivilization.TryGetValue(civId, out var explored)) exploredTilesByCivilization[civId] = explored = new HashSet<int>();
        visible.Clear();
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (unit == null || unit.owner != civilization || unit.currentSpaceTileIndex < 0) continue;
            int range = Mathf.Max(1, unit.data != null ? unit.data.spaceVisionRange : unit.spaceVisionRange);
            AddVisible(civId, unit.currentSpaceTileIndex, range, visible, explored);
        }
        foreach (var improvement in SpaceWorldManager.Instance.CurrentSystem.improvements)
        {
            if (improvement.ownerCivilizationId != civId || !improvement.operational || improvement.disabled) continue;
            var data = SpaceImprovementRegistry.GetData(improvement.improvementDataId);
            if (data != null && data.visionRange > 0) AddVisible(civId, improvement.spaceTileIndex, data.visionRange, visible, explored);
        }
    }
    private void AddVisible(int civId, int origin, int range, HashSet<int> visible, HashSet<int> explored)
    {
        var grid = SpaceWorldManager.Instance.Grid;
        foreach (var tile in grid.tiles) if (grid.GetDistance(origin, tile.tileIndex) <= range) { visible.Add(tile.tileIndex); explored.Add(tile.tileIndex); }
    }
}

public class SpaceRepairManager : MonoBehaviour
{
    public int EstimateRepairAtTile(Civilization civilization, int tileIndex)
    {
        if (civilization == null || SpaceWorldManager.Instance?.CurrentSystem == null) return 0;
        int civId = civilization.gameObject.GetRuntimeId(); int best = 0;
        foreach (var improvement in SpaceWorldManager.Instance.CurrentSystem.improvements)
        {
            if (improvement.ownerCivilizationId != civId || !improvement.operational || improvement.disabled) continue;
            var data = SpaceImprovementRegistry.GetData(improvement.improvementDataId);
            if (data != null && data.repairPerTurn > 0 && SpaceWorldManager.Instance.Grid.GetDistance(improvement.spaceTileIndex, tileIndex) <= data.repairRange)
                best = Mathf.Max(best, data.repairPerTurn);
        }
        return best;
    }
}

public class SpaceFeatureGenerator
{
    public void Generate(SpaceWorldState state, IList<SpaceFeatureData> featureData, int seed)
    {
        if (state?.grid == null || featureData == null || featureData.Count == 0) return;
        var rng = new System.Random(seed);
        foreach (var data in featureData)
        {
            if (data == null) continue;
            int clusterSize = Math.Max(1, rng.Next(Math.Max(1, data.minimumClusterSize), Math.Max(data.minimumClusterSize, data.maximumClusterSize) + 1));
            int start = rng.Next(0, state.grid.tiles.Count);
            var instance = new SpaceFeatureInstance { instanceId = state.nextFeatureId++, featureDataId = data.featureId, remainingResourceQuantity = data.resourceDeposit != null ? data.resourceDeposit.quantity : 0 };
            foreach (var tile in BuildCluster(state.grid, start, clusterSize)) { instance.occupiedTileIndices.Add(tile); state.grid.GetTile(tile)?.featureInstanceIds.Add(instance.instanceId); }
            state.features.Add(instance);
            SpaceWorldManager.Instance?.Entities.Register(instance);
        }
    }
    private IEnumerable<int> BuildCluster(SpaceHexGrid grid, int start, int count)
    {
        var result = new List<int> { start }; var frontier = new Queue<int>(); frontier.Enqueue(start);
        while (frontier.Count > 0 && result.Count < count)
        {
            int current = frontier.Dequeue();
            foreach (int n in grid.GetNeighbors(current)) if (!result.Contains(n)) { result.Add(n); frontier.Enqueue(n); if (result.Count >= count) break; }
        }
        return result;
    }
}

public class SpaceResourceGenerator { public void Generate(SpaceWorldState state, int seed) { } }
public class SpaceImprovementSpawner { public SpaceImprovementInstance Spawn(SpaceWorldState state, SpaceImprovementData data, int ownerCivilizationId, int tileIndex) { if (state == null || data == null) return null; SpaceImprovementRegistry.Register(data); var inst = new SpaceImprovementInstance { instanceId = state.nextImprovementId++, improvementDataId = data.improvementId, ownerCivilizationId = ownerCivilizationId, spaceTileIndex = tileIndex, currentHealth = data.maximumHealth, operational = true }; state.improvements.Add(inst); SpaceWorldManager.Instance?.Entities.Register(inst); state.grid.GetTile(tileIndex)?.improvementInstanceIds.Add(inst.instanceId); return inst; } }
