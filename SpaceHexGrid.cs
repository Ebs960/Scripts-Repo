using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpaceLocationType { PlanetSurface, PlanetOrbit, SolarSystemSpace, Destroyed }
public enum SpaceTerrainType { EmptySpace, Planet, Moon, AsteroidField, DebrisField, Nebula, RadiationZone }

[Serializable]
public struct SpaceLocation
{
    public SpaceLocationType locationType;
    public int planetIndex;
    public int planetaryTileIndex;
    public int spaceTileIndex;
    public bool IsOnPlanet => locationType == SpaceLocationType.PlanetSurface || locationType == SpaceLocationType.PlanetOrbit;
    public bool IsInSolarSystemSpace => locationType == SpaceLocationType.SolarSystemSpace;

    public static SpaceLocation OnSurface(int planetIndex, int tileIndex) => new SpaceLocation { locationType = SpaceLocationType.PlanetSurface, planetIndex = planetIndex, planetaryTileIndex = tileIndex, spaceTileIndex = -1 };
    public static SpaceLocation InOrbit(int planetIndex, int orbitTileIndex = -1) => new SpaceLocation { locationType = SpaceLocationType.PlanetOrbit, planetIndex = planetIndex, planetaryTileIndex = orbitTileIndex, spaceTileIndex = -1 };
    public static SpaceLocation InSpace(int spaceTileIndex) => new SpaceLocation { locationType = SpaceLocationType.SolarSystemSpace, planetIndex = -1, planetaryTileIndex = -1, spaceTileIndex = spaceTileIndex };
}

[Serializable] public class SpaceHazardData { public string hazardId; public int damagePerTurn; public bool hidden; }
[Serializable] public class SpaceResourceData { public string resourceId; public int quantity; public bool hidden; }

[Serializable]
public class SpaceHexTile
{
    public int tileIndex;
    public int q;
    public int r;
    public SpaceTerrainType terrainType = SpaceTerrainType.EmptySpace;
    public int movementCost = 1;
    public bool blocksMovement;
    public int controllingCivilizationId = -1;
    public int planetId = -1;
    public int stationId = -1;
    public List<int> spacecraftIds = new List<int>();
    public SpaceHazardData hazard;
    public SpaceResourceData resource;
}

public class SpaceHexGrid
{
    public int radius { get; private set; }
    public float tileSize { get; private set; }
    public readonly List<SpaceHexTile> tiles = new List<SpaceHexTile>();
    private readonly Dictionary<(int q, int r), int> indexByCoord = new Dictionary<(int q, int r), int>();

    public SpaceHexGrid(int radius = 12, float tileSize = 5f) { Generate(radius, tileSize); }

    public void Generate(int newRadius, float newTileSize)
    {
        radius = Mathf.Max(1, newRadius); tileSize = Mathf.Max(0.1f, newTileSize);
        tiles.Clear(); indexByCoord.Clear();
        for (int q = -radius; q <= radius; q++)
        for (int r = Mathf.Max(-radius, -q - radius); r <= Mathf.Min(radius, -q + radius); r++)
        {
            var tile = new SpaceHexTile { tileIndex = tiles.Count, q = q, r = r };
            indexByCoord[(q, r)] = tile.tileIndex; tiles.Add(tile);
        }
    }

    public SpaceHexTile GetTile(int index) => index >= 0 && index < tiles.Count ? tiles[index] : null;
    public bool TryGetIndex(int q, int r, out int index) => indexByCoord.TryGetValue((q, r), out index);
    public Vector3 GetWorldPosition(int index)
    {
        var t = GetTile(index); if (t == null) return Vector3.zero;
        return new Vector3(tileSize * Mathf.Sqrt(3f) * (t.q + t.r * 0.5f), 0f, tileSize * 1.5f * t.r);
    }
    public int GetNearestTileIndex(Vector3 world)
    {
        int best = -1; float bestSqr = float.MaxValue;
        foreach (var t in tiles) { float d = (GetWorldPosition(t.tileIndex) - world).sqrMagnitude; if (d < bestSqr) { bestSqr = d; best = t.tileIndex; } }
        return best;
    }
    public IEnumerable<int> GetNeighbors(int index)
    {
        var t = GetTile(index); if (t == null) yield break;
        int[,] dirs = { {1,0},{1,-1},{0,-1},{-1,0},{-1,1},{0,1} };
        for (int i=0;i<6;i++) if (TryGetIndex(t.q + dirs[i,0], t.r + dirs[i,1], out int n)) yield return n;
    }
    public int GetDistance(int a, int b)
    {
        var x = GetTile(a); var y = GetTile(b); if (x == null || y == null) return int.MaxValue;
        return (Mathf.Abs(x.q - y.q) + Mathf.Abs(x.q + x.r - y.q - y.r) + Mathf.Abs(x.r - y.r)) / 2;
    }
    public static int GetDistance(SpaceHexTile a, SpaceHexTile b) => a == null || b == null ? int.MaxValue : (Mathf.Abs(a.q-b.q)+Mathf.Abs(a.q+a.r-b.q-b.r)+Mathf.Abs(a.r-b.r))/2;
}

public class SpaceHexPathfinder
{
    private readonly SpaceHexGrid grid;
    public SpaceHexPathfinder(SpaceHexGrid grid) { this.grid = grid; }
    public List<int> FindPath(int start, int goal, Func<SpaceHexTile, bool> passable = null)
    {
        var frontier = new Queue<int>(); var came = new Dictionary<int,int>(); frontier.Enqueue(start); came[start] = -1;
        while (frontier.Count > 0)
        {
            int cur = frontier.Dequeue(); if (cur == goal) break;
            foreach (int next in grid.GetNeighbors(cur))
            {
                var tile = grid.GetTile(next); if (tile == null || tile.blocksMovement || (passable != null && !passable(tile)) || came.ContainsKey(next)) continue;
                frontier.Enqueue(next); came[next] = cur;
            }
        }
        if (!came.ContainsKey(goal)) return new List<int>();
        var path = new List<int>(); for (int c = goal; c >= 0; c = came[c]) path.Add(c); path.Reverse(); return path;
    }
}
