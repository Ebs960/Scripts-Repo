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
        var source = new Vector3[map.CellCount];
        bool sourced = false;
        if (session.Theater == BattleTheater.DeepSpace)
        {
            var grid = SpaceWorldManager.Instance != null ? SpaceWorldManager.Instance.Grid :
                (SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : null);
            if (grid != null)
            {
                for (int i = 0; i < map.CellCount; i++) source[i] = grid.GetWorldPosition(map.Cells[i].CampaignTileIndex);
                sourced = true;
            }
        }
        else
        {
            var tiles = TileSystem.GetForPlanet(session.PlanetIndex) ?? TileSystem.Instance;
            if (tiles != null)
            {
                for (int i = 0; i < map.CellCount; i++) source[i] = tiles.GetTileCenterFromPlanet(map.Cells[i].CampaignTileIndex, session.PlanetIndex);
                sourced = true;
            }
        }

        if (!sourced) EmbedTopology(map, source);
        Vector3 anchor = source.Length > 0 ? source[0] : Vector3.zero;
        float nearest = float.MaxValue;
        for (int i = 0; i < map.CellCount; i++)
            foreach (int n in map.Cells[i].NeighborIndices ?? System.Array.Empty<int>())
                nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(source[i].x, source[i].z), new Vector2(source[n].x, source[n].z)));
        float scale = nearest < float.MaxValue && nearest > .001f ? HexRadius * 1.75f / nearest : 1f;
        for (int i = 0; i < map.CellCount; i++)
        {
            Vector3 flat = (source[i] - anchor) * scale;
            result.centers[i] = new Vector3(flat.x, map.Cells[i].ElevationLevel * ElevationStep, flat.z);
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
