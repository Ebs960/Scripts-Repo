using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Centralized helper class for accessing tile data from planet and moon.
/// Now decoupled from Hexasphere.
/// </summary>
public class TileDataHelper : MonoBehaviour
{
    public static TileDataHelper Instance { get; private set; }

    private PlanetGenerator planet;
    private MoonGenerator moon;

    private Dictionary<int, CachedTileData> tileDataCache = new();
    private Dictionary<int, int[]> adjacencyCache = new();
    private Dictionary<int, Vector3> tileCenterCache = new();

    private const int CACHE_MAX_AGE = 10;

    private struct CachedTileData
    {
        public HexTileData tileData;
        public bool isMoonTile;
        public int lastUpdateFrame;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    void Start() => UpdateReferences();

    void Update()
    {
        if (planet == null) UpdateReferences();
    }

    public void UpdateReferences()
    {
        planet = GameManager.Instance?.planetGenerator ?? FindAnyObjectByType<PlanetGenerator>();
        moon = GameManager.Instance?.moonGenerator ?? FindAnyObjectByType<MoonGenerator>();
        ClearAllCaches();
    }

    private void CacheTileData(int tileIndex, HexTileData data, bool isMoon)
    {
        tileDataCache[tileIndex] = new CachedTileData
        {
            tileData = data,
            isMoonTile = isMoon,
            lastUpdateFrame = Time.frameCount
        };
    }

    public (HexTileData tileData, bool isMoonTile) GetTileData(int tileIndex)
    {
        if (tileDataCache.TryGetValue(tileIndex, out var cached) &&
            Time.frameCount - cached.lastUpdateFrame < CACHE_MAX_AGE)
            return (cached.tileData, cached.isMoonTile);

        HexTileData data = planet?.GetHexTileData(tileIndex);
        if (data != null)
        {
            CacheTileData(tileIndex, data, false);
            return (data, false);
        }

        data = moon?.GetHexTileData(tileIndex);
        if (data != null)
        {
            CacheTileData(tileIndex, data, true);
            return (data, true);
        }

        return (null, false);
    }

    public void SetTileData(int tileIndex, HexTileData tileData)
    {
        bool isMoon = moon?.GetHexTileData(tileIndex) != null;
        if (isMoon) moon?.SetHexTileData(tileIndex, tileData);
        else planet?.SetHexTileData(tileIndex, tileData);

        tileDataCache[tileIndex] = new CachedTileData
        {
            tileData = tileData,
            isMoonTile = isMoon,
            lastUpdateFrame = Time.frameCount
        };
    }

    public void SetTileOccupant(int tileIndex, GameObject occupant)
    {
        var (data, isMoon) = GetTileData(tileIndex);
        if (data == null) return;

        data.occupantId = occupant ? occupant.GetInstanceID() : 0;
        SetTileData(tileIndex, data);
    }

    public void ClearTileOccupant(int tileIndex) => SetTileOccupant(tileIndex, null);

    public int[] GetTileNeighbors(int tileIndex)
    {
        if (adjacencyCache.TryGetValue(tileIndex, out var cached)) return cached;

        // Get the grid from the planet generator and access neighbors directly
        var grid = planet?.Grid;
        if (grid != null && tileIndex >= 0 && tileIndex < grid.neighbors.Length)
        {
            var neighborsList = grid.neighbors[tileIndex];
            var neighbors = neighborsList?.ToArray() ?? new int[0];
            adjacencyCache[tileIndex] = neighbors;
            return neighbors;
        }

        return new int[0];
    }

    public Vector3 GetTileCenter(int tileIndex)
    {
        if (tileCenterCache.TryGetValue(tileIndex, out var cached)) return cached;

        // Get the grid from the planet generator and access tile centers directly
        var grid = planet?.Grid;
        Vector3 pos = Vector3.zero;
        if (grid != null && tileIndex >= 0 && tileIndex < grid.tileCenters.Length)
        {
            pos = grid.tileCenters[tileIndex];
        }
        
        tileCenterCache[tileIndex] = pos;
        return pos;
    }

    public bool IsTileAccessible(int tileIndex, bool mustBeLand, int movePoints, int unitID, bool allowMoon = false)
    {
        var (data, isMoon) = GetTileData(tileIndex);
        if (data == null) return false;

        if (isMoon && !allowMoon) return false;
        if (mustBeLand && !data.isLand) return false;

        int cost = BiomeHelper.GetMovementCost(data.biome);
        if (movePoints < cost) return false;

        return data.occupantId == 0 || data.occupantId == unitID;
    }

    public float GetTileDistance(int index1, int index2)
    {
        return Vector3.Distance(GetTileCenter(index1), GetTileCenter(index2));
    }

    public void ClearAllCaches()
    {
        tileDataCache.Clear();
        adjacencyCache.Clear();
        tileCenterCache.Clear();
    }
}