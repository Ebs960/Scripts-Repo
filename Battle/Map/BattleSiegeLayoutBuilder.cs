using System.Collections.Generic;
using UnityEngine;

/// <summary>Augments the connected campaign-derived map; it never replaces terrain generation.</summary>
public static class BattleSiegeLayoutBuilder
{
    public static List<BattleFortificationState> Apply(BattleMap map, BattleSiegeType type, BattleFortificationProfile profile, ref BattleObjective objective)
    {
        var result = new List<BattleFortificationState>();
        if (map == null || type == BattleSiegeType.None) return result;
        int center = FindCenter(map, objective.CellIndex);
        if (center < 0) return result;
        objective.CellIndex = center;
        objective.Type = type == BattleSiegeType.Fort ? BattleObjectiveType.StrongholdCapture : BattleObjectiveType.SettlementCapture;
        map.Cells[center].IsObjective = true;
        map.Cells[center].IsFortifiedInterior = profile != null;
        map.Cells[center].HasHardCover |= profile != null;
        if (profile == null) return result; // A settlement is an objective, not an implicit wall.

        var perimeter = new List<int>();
        foreach (int neighbor in map.Cells[center].NeighborIndices ?? System.Array.Empty<int>())
            if (map.GetCell(neighbor)?.SupportsLand == true) perimeter.Add(neighbor);
        perimeter.Sort();
        int limit = type == BattleSiegeType.Fort ? Mathf.Min(4, perimeter.Count) : perimeter.Count;
        for (int i = 0; i < limit; i++)
        {
            bool gate = i == 0;
            var cell = map.Cells[perimeter[i]];
            cell.HasHardCover = true;
            result.Add(New(i + 1, gate ? BattleFortificationKind.Gate : BattleFortificationKind.Wall,
                cell.BattleIndex, gate ? profile.gateHitPoints : profile.wallHitPoints, profile.defense));
        }
        result.Add(New(limit + 1, BattleFortificationKind.Strongpoint, center, profile.strongpointHitPoints, profile.defense));
        return result;
    }

    private static BattleFortificationState New(int id, BattleFortificationKind kind, int cell, int hp, int defense)
        => new() { StructureId = id, Kind = kind, CellIndex = cell, MaxHitPoints = hp, CurrentHitPoints = hp, Defense = defense };

    private static int FindCenter(BattleMap map, int preferred)
    {
        if (map.GetCell(preferred)?.SupportsLand == true) return preferred;
        int best = -1, degree = -1;
        foreach (var cell in map.Cells)
            if (cell.SupportsLand && (cell.NeighborIndices?.Length ?? 0) > degree)
            { best = cell.BattleIndex; degree = cell.NeighborIndices?.Length ?? 0; }
        return best;
    }
}
