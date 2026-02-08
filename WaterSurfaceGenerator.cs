using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public class WaterSurfaceGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BiomeVisualDatabase biomeVisualDatabase;
    [SerializeField] private WaterSurface oceanSurfacePrefab;
    [SerializeField] private WaterSurface lakeSurfacePrefab;

    // Note: Sea level for oceans is authoritative from PlanetGenerator.SeaLevelWorldY.
    // Lake water surfaces compute their own height from surrounding shore terrain.

    private readonly List<GameObject> spawnedSurfaces = new List<GameObject>();

    // Planet we're attached to (set via Initialize)
    private PlanetGenerator attachedPlanet;

    /// <summary>
    /// Initialize the water generator for a specific planet generator.
    /// Subscribes to the planet's OnSurfaceGenerated event and will create
    /// HDRP water surfaces when that callback occurs. If the surface is already
    /// generated, the callback is invoked immediately to produce water.
    /// </summary>
    public void Initialize(PlanetGenerator planet)
    {
        if (planet == null) return;
        // Unsubscribe from previous if any
        if (attachedPlanet != null)
        {
            attachedPlanet.OnSurfaceGenerated -= HandleSurfaceGenerated;
        }

        attachedPlanet = planet;
        attachedPlanet.OnSurfaceGenerated += HandleSurfaceGenerated;

        Debug.Log($"[WaterSurfaceGenerator] Initialized for planet '{planet.name}' SeaLevel={planet.SeaLevelWorldY} HasGeneratedSurface={attachedPlanet.HasGeneratedSurface}");

        // If the planet is already ready, run generation now via the same handler
        if (attachedPlanet.HasGeneratedSurface)
        {
            Debug.Log("[WaterSurfaceGenerator] Planet already generated - running HandleSurfaceGenerated immediately.");
            HandleSurfaceGenerated();
        }
    }

    private void OnDestroy()
    {
        if (attachedPlanet != null)
        {
            attachedPlanet.OnSurfaceGenerated -= HandleSurfaceGenerated;
            attachedPlanet = null;
        }
        ClearSurfaces();
    }

    private void HandleSurfaceGenerated()
    {
        var gen = attachedPlanet;
        if (gen == null) return;

        Debug.Log($"[WaterSurfaceGenerator] HandleSurfaceGenerated invoked for planet '{gen.name}' (SeaLevel={gen.SeaLevelWorldY})");

        // Clear any stale surfaces and generate fresh ones
        ClearSurfaces();
        // Gate water generation to planets that explicitly support the Underwater layer
        var layerManager = gen.GetComponent<LayerManager>();
        bool hasUnderwater = (layerManager != null)
            ? layerManager.IsLayerSupported(GameManager.PlanetLayerType.Underwater)
            : gen.HasLayer(GameManager.PlanetLayerType.Underwater); // fallback for legacy scenes
        if (!hasUnderwater)
        {
            Debug.Log("[WaterSurfaceGenerator] Planet does not support Underwater layer; skipping water surface generation.");
            return;
        }

        GenerateInternal(gen);
    }

    // Internal generate method used only from the event handler above
    private void GenerateInternal(PlanetGenerator planetGen)
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

        var grid = planetGen.Grid;
        int tileCount = grid.TileCount;
        Debug.Log($"[WaterSurfaceGenerator] Generating water surfaces: TileCount={tileCount} Resolution={grid.Width}x{grid.Height} SeaLevel={planetGen.SeaLevelWorldY}");

        int createdRegions = 0;
        bool[] visited = new bool[tileCount];

        float tileWidth = grid.MapWidth / Mathf.Max(1, grid.Width);
        float tileHeight = grid.MapHeight / Mathf.Max(1, grid.Height);
        float padX = tileWidth * 0.5f;
        float padZ = tileHeight * 0.5f;

        // Map wraps horizontally (X axis). Terrain chunks are teleported/ghosted by ±MapWidth.
        // To keep water present across the wrap seam, we mirror each generated water surface
        // to the left and right by ±MapWidth.
        float mapWidthWorld = grid.MapWidth;

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
                // Lake water sits at the level of its lowest shore tile (the natural outlet).
                // This ensures lake water is flat and at the correct terrain-relative height,
                // rather than using the global ocean sea level.
                height = ComputeLakeWaterHeight(planetGen, grid, region);
            }
            else
            {
                // Ocean/seas use the planet's authoritative sea level.
                height = planetGen.SeaLevelWorldY;
            }

            CreateWaterSurface(regionIsLake, bounds, height, mapWidthWorld, spawnedSurfaces.Count);
            createdRegions++;
        }

        Debug.Log($"[WaterSurfaceGenerator] Water generation complete. Regions created={createdRegions} TotalSurfaces={spawnedSurfaces.Count}");
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
        if (visual == null) return false;
        // Only treat Ocean and Lake as HDRP Water Surface candidates.
        return visual.waterType == BiomeVisualData.WaterType.Ocean || visual.waterType == BiomeVisualData.WaterType.Lake;
    }

    private List<int> FloodFillRegion(PlanetGenerator planetGen, HexGrid grid, int startIndex, bool[] visited)
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

    private static Bounds CalculateRegionBounds(HexGrid grid, List<int> region, float padX, float padZ)
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
            // Only consider a region a lake if tiles are marked as lake. Rivers
            // must not be treated as flat HDRP water surfaces.
            if (tile.isLake)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Compute the world-space Y height for a lake's water surface.
    /// The water level equals the lowest shore tile's world Y (the natural outlet).
    /// This makes each lake sit at the correct terrain-relative height — a mountain
    /// lake is high, a valley lake is low.
    /// </summary>
    private float ComputeLakeWaterHeight(PlanetGenerator planetGen, HexGrid grid, List<int> region)
    {
        int tileCount = grid.TileCount;
        float minShoreRenderElev = float.MaxValue;

        // Build a set for fast membership checks
        var regionSet = new HashSet<int>(region);

        foreach (int lakeIdx in region)
        {
            var neighbors = grid.neighbors[lakeIdx];
            if (neighbors == null) continue;

            foreach (int n in neighbors)
            {
                if (n < 0 || n >= tileCount) continue;
                if (regionSet.Contains(n)) continue; // skip other lake tiles in this body

                var td = planetGen.GetHexTileData(n);
                // HexTileData is a struct — check isLand instead of null
                if (!td.isLand) continue;
                // Only consider true land shore tiles (not coast/ocean/seas)
                if (td.biome == Biome.Coast || td.biome == Biome.Ocean || td.biome == Biome.Seas) continue;

                if (td.renderElevation < minShoreRenderElev)
                {
                    minShoreRenderElev = td.renderElevation;
                }
            }
        }

        // Get the ACTUAL displacement strength from HexMapChunkManager (what the shader uses).
        // GameManager.terrainDisplacementStrength is a separate value that may not match.
        float flatY = GameManager.Instance != null ? GameManager.Instance.GetFlatPlaneY() : 0f;
        float dispStrength = GetActualDisplacementStrength();

        if (minShoreRenderElev < float.MaxValue)
        {
            float waterY = flatY + minShoreRenderElev * dispStrength;
            Debug.Log($"[WaterSurfaceGenerator] Lake water height from shore: renderElev={minShoreRenderElev:F4} worldY={waterY:F3} (flatY={flatY:F3} disp={dispStrength:F1})");
            return waterY;
        }

        // Fallback: no shore tiles found (isolated lake, shouldn't normally happen).
        // Use the average renderElevation of the lake tiles themselves + a small offset
        // so the water is slightly above the terrain.
        float sumElev = 0f;
        int count = 0;
        foreach (int idx in region)
        {
            var td = planetGen.GetHexTileData(idx);
            sumElev += td.renderElevation;
            count++;
        }

        if (count > 0)
        {
            float avgRender = sumElev / count;
            // Add a small offset so water sits above the lake bed, not at it
            float waterRender = avgRender + 0.02f;
            float waterY = flatY + waterRender * dispStrength;
            Debug.Log($"[WaterSurfaceGenerator] Lake water height from tile average (no shore): renderElev={avgRender:F4}+0.02 worldY={waterY:F3}");
            return waterY;
        }

        // Last resort: use global sea level
        Debug.LogWarning("[WaterSurfaceGenerator] Could not compute lake water height, falling back to SeaLevelWorldY");
        return planetGen.SeaLevelWorldY;
    }

    /// <summary>
    /// Get the actual displacement strength from HexMapChunkManager (matches _ElevationScale in shader).
    /// Falls back to GameManager.terrainDisplacementStrength if the chunk manager isn't found.
    /// </summary>
    private float GetActualDisplacementStrength()
    {
        // Try to get it from the planet's terrain renderer first
        if (attachedPlanet != null && attachedPlanet.terrainRenderer != null)
        {
            return attachedPlanet.terrainRenderer.DisplacementStrength;
        }

        // Try to find any HexMapChunkManager in the scene
        var chunkManager = FindAnyObjectByType<HexMapChunkManager>(FindObjectsInactive.Include);
        if (chunkManager != null)
        {
            return chunkManager.DisplacementStrength;
        }

        // Final fallback: GameManager's value (may not match shader)
        if (GameManager.Instance != null)
        {
            Debug.LogWarning("[WaterSurfaceGenerator] Using GameManager.terrainDisplacementStrength as fallback — may not match shader _ElevationScale");
            return GameManager.Instance.GetTerrainDisplacementStrength();
        }

        return 5f;
    }

    private void CreateWaterSurface(bool isLake, Bounds bounds, float height, float mapWidthWorld, int regionIndex)
    {
        var prefab = isLake ? lakeSurfacePrefab : oceanSurfacePrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[WaterSurfaceGenerator] Missing water surface prefab.");
            return;
        }

        var instance = Instantiate(prefab.gameObject, transform, false);
        instance.name = $"WaterSurface_{regionIndex}";
        // `height` is world-space (PlanetGenerator.SeaLevelWorldY). Convert to this transform's local space
        // so we don't accidentally treat a world Y as a local Y when the map/planet hierarchy is offset/rotated.
        float localY = transform.InverseTransformPoint(new Vector3(transform.position.x, height, transform.position.z)).y;
        instance.transform.localPosition = new Vector3(0f, localY, 0f);

        Debug.Log($"[WaterSurfaceGenerator] Created WaterSurface idx={regionIndex} isLake={isLake} bounds={bounds.size} height={height}");

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

        // Mirror to left/right so water exists across horizontal wrap seams.
        // This matches the chunk manager's world-wrap model (periodic repeat in X by MapWidth).
        if (mapWidthWorld > 0.001f)
        {
            var left = Instantiate(instance, transform, false);
            left.name = $"WaterSurface_{regionIndex}_GhostLeft";
            left.transform.localPosition = new Vector3(-mapWidthWorld, localY, 0f);
            spawnedSurfaces.Add(left);

            var right = Instantiate(instance, transform, false);
            right.name = $"WaterSurface_{regionIndex}_GhostRight";
            right.transform.localPosition = new Vector3(mapWidthWorld, localY, 0f);
            spawnedSurfaces.Add(right);
        }
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
