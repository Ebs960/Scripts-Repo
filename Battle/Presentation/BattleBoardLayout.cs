using System.Collections.Generic;
using UnityEngine;

/// <summary>Immutable, presentation-only mapping from battle cells to local tactical space.</summary>
public sealed class BattleBoardLayout
{
    public const float HexRadius = 1.35f;
    public const float ElevationStep = 0.42f;
    private readonly Vector3[] centers;
    public Bounds Bounds { get; private set; }

    private BattleBoardLayout(int count) { centers = new Vector3[count]; }
    public Vector3 GetCellCenter(int index) => index >= 0 && index < centers.Length ? centers[index] : Vector3.zero;

    public Vector3 GetUnitPosition(BattleSession session, BattleUnitState unit)
    {
        if (session == null || unit == null) return Vector3.zero;
        Vector3 p = GetCellCenter(unit.CellIndex);
        p.y += unit.Domain switch
        {
            BattleDomain.Underwater => unit.DepthBand == BattleDepthBand.Deep ? -0.8f : -0.35f,
            BattleDomain.Air => 2.2f,
            BattleDomain.Orbit => 4.2f,
            BattleDomain.Space => 0.45f,
            _ => 0.12f,
        };
        return p;
    }

    public static BattleBoardLayout Build(BattleSession session)
    {
        var map = session.Map;
        var result = new BattleBoardLayout(map.CellCount);
        for (int i = 0; i < map.CellCount; i++)
        {
            var cell = map.Cells[i];
            // Pointy-top axial layout. Campaign coordinates deliberately play no part.
            float x = HexRadius * Mathf.Sqrt(3f) * (cell.LocalQ + cell.LocalR * .5f);
            float z = HexRadius * 1.5f * cell.LocalR;
            result.centers[i] = new Vector3(x, cell.ElevationLevel * ElevationStep, z);
        }
        result.CalculateBounds();
        return result;
    }

    private static void EmbedTopology(BattleMap map, Vector3[] output)
    {
        var placed = new bool[map.CellCount]; var queue = new Queue<int>();
        if (map.CellCount == 0) return; placed[0] = true; queue.Enqueue(0);
        Vector3[] directions = { new(2.34f,0,0), new(1.17f,0,2.03f), new(-1.17f,0,2.03f), new(-2.34f,0,0), new(-1.17f,0,-2.03f), new(1.17f,0,-2.03f) };
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue(); var neighbors = map.Cells[cell].NeighborIndices ?? System.Array.Empty<int>();
            for (int i = 0; i < neighbors.Length; i++) if (!placed[neighbors[i]])
            { placed[neighbors[i]] = true; output[neighbors[i]] = output[cell] + directions[i % 6]; queue.Enqueue(neighbors[i]); }
        }
    }

    private void CalculateBounds()
    {
        Bounds = centers.Length == 0 ? new Bounds(Vector3.zero, Vector3.one) : new Bounds(centers[0], Vector3.zero);
        for (int i = 0; i < centers.Length; i++) Bounds.Encapsulate(centers[i]);
        Bounds.Expand(new Vector3(HexRadius * 2f, ElevationStep * 2f, HexRadius * 2f));
    }
}
