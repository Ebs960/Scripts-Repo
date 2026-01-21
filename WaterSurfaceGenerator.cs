using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public class WaterSurfaceGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BiomeVisualDatabase biomeVisualDatabase;
    [SerializeField] private WaterSurface oceanSurfacePrefab;
    [SerializeField] private WaterSurface lakeSurfacePrefab;

    [Header("Water Levels")]
    [Tooltip("Surface height for ocean/sea regions.")]
    [SerializeField] private float seaLevel = 0.12f;
    [Tooltip("Surface height for inland lakes/rivers.")]
    [SerializeField] private float lakeLevel = 0.12f;

    private readonly List<GameObject> spawnedSurfaces = new List<GameObject>();

    public void Generate(PlanetGenerator planetGen)
    {
        if (planetGen == null || planetGen.Grid == null || !planetGen.Grid.IsBuilt)
        {
            Debug.LogWarning("[WaterSurfaceGenerator] Missing planet generator grid.");
            return;
        }

        if (biomeVisualDatabase == null)
        {
            Debug.LogWarning("[WaterSurfaceGenerator] Missing biome visual database.");
            return;
        }

        ClearSurfaces();

        var grid = planetGen.Grid;
        int tileCount = grid.TileCount;
        bool[] visited = new bool[tileCount];

        float tileWidth = grid.MapWidth / Mathf.Max(1, grid.Width);
        float tileHeight = grid.MapHeight / Mathf.Max(1, grid.Height);
        float padX = tileWidth * 0.5f;
        float padZ = tileHeight * 0.5f;

        for (int i = 0; i < tileCount; i++)
        {
            if (visited[i]) continue;

            if (!IsWaterTile(planetGen, i))
            {
                visited[i] = true;
                continue;
            }

            var region = FloodFillRegion(planetGen, grid, i, visited);
            if (region.Count == 0) continue;

            var bounds = CalculateRegionBounds(grid, region, padX, padZ);
            bool regionIsLake = RegionIsLake(planetGen, region);

            float height;
            if (regionIsLake)
            {
                // Compute average renderElevation for the lake region and convert
                // to world Y using GameManager's flat plane Y + displacement strength.
                float sumRender = 0f;
                int renderCount = 0;
                foreach (var ti in region)
                {
                    var td = planetGen.GetHexTileData(ti);
                    if (td != null)
                    {
                        sumRender += td.renderElevation;
                        renderCount++;
                    }
                }
                float avgRender = (renderCount > 0) ? (sumRender / renderCount) : 0f;

                float flatY = 0f;
                float disp = 0f;
                if (GameManager.Instance != null)
                {
                    flatY = GameManager.Instance.GetFlatPlaneY();
                    disp = GameManager.Instance.GetTerrainDisplacementStrength();
                }

                height = flatY + avgRender * disp;
            }
            else
            {
                height = seaLevel;
            }

            CreateWaterSurface(regionIsLake, bounds, height, spawnedSurfaces.Count);
        }
    }

    private void ClearSurfaces()
    {
        foreach (var surface in spawnedSurfaces)
        {
            if (surface != null)
            {
                Destroy(surface);
            }
        }

        spawnedSurfaces.Clear();
    }

    private bool IsWaterTile(PlanetGenerator planetGen, int tileIndex)
    {
        var tile = planetGen.GetHexTileData(tileIndex);
        if (tile == null) return false;

        var visual = biomeVisualDatabase.Get(tile.biome);
        return visual != null && visual.isWaterBiome;
    }

    private List<int> FloodFillRegion(PlanetGenerator planetGen, SphericalHexGrid grid, int startIndex, bool[] visited)
    {
        var region = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(startIndex);
        visited[startIndex] = true;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            region.Add(current);

            var neighbors = grid.neighbors[current];
            if (neighbors == null) continue;

            foreach (var neighbor in neighbors)
            {
                if (neighbor < 0 || neighbor >= visited.Length) continue;
                if (visited[neighbor]) continue;

                if (IsWaterTile(planetGen, neighbor))
                {
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
                else
                {
                    visited[neighbor] = true;
                }
            }
        }

        return region;
    }

    private static Bounds CalculateRegionBounds(SphericalHexGrid grid, List<int> region, float padX, float padZ)
    {
        Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, 0f, float.MinValue);

        foreach (var index in region)
        {
            Vector3 tileCenter = grid.tileCenters[index];
            min.x = Mathf.Min(min.x, tileCenter.x);
            min.z = Mathf.Min(min.z, tileCenter.z);
            max.x = Mathf.Max(max.x, tileCenter.x);
            max.z = Mathf.Max(max.z, tileCenter.z);
        }

        min.x -= padX;
        min.z -= padZ;
        max.x += padX;
        max.z += padZ;

        Vector3 size = new Vector3(Mathf.Max(0.01f, max.x - min.x), 0.1f, Mathf.Max(0.01f, max.z - min.z));
        Vector3 center = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
        return new Bounds(center, size);
    }

    private static bool RegionIsLake(PlanetGenerator planetGen, List<int> region)
    {
        foreach (var index in region)
        {
            var tile = planetGen.GetHexTileData(index);
            if (tile == null) continue;
            if (tile.isLake || tile.isRiver)
            {
                return true;
            }
        }

        return false;
    }

    private void CreateWaterSurface(bool isLake, Bounds bounds, float height, int regionIndex)
    {
        var prefab = isLake ? lakeSurfacePrefab : oceanSurfacePrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[WaterSurfaceGenerator] Missing water surface prefab.");
            return;
        }

        var instance = Instantiate(prefab.gameObject, transform, false);
        instance.name = $"WaterSurface_{regionIndex}";
        instance.transform.localPosition = new Vector3(0f, height, 0f);

        var meshFilter = instance.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = instance.AddComponent<MeshFilter>();
        }

        var meshRenderer = instance.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = instance.AddComponent<MeshRenderer>();
        }

        meshFilter.sharedMesh = BuildQuadMesh(bounds);
        spawnedSurfaces.Add(instance);
    }

    private static Mesh BuildQuadMesh(Bounds bounds)
    {
        var mesh = new Mesh
        {
            name = "WaterSurfaceRegion"
        };

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        var vertices = new[]
        {
            new Vector3(min.x, 0f, min.z),
            new Vector3(max.x, 0f, min.z),
            new Vector3(max.x, 0f, max.z),
            new Vector3(min.x, 0f, max.z)
        };

        var uvs = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };

        var triangles = new[]
        {
            0, 2, 1,
            0, 3, 2
        };

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}
