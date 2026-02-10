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
        int createdLakes = 0;
        int createdOceans = 0;
        bool[] visited = new bool[tileCount];

        // Tile padding for ocean AABB quads (kept for ocean/seas regions)
        float tileWidth = grid.MapWidth / Mathf.Max(1, grid.Width);
        float tileHeight = grid.MapHeight / Mathf.Max(1, grid.Height);
        float padX = tileWidth * 0.5f;
        float padZ = tileHeight * 0.5f;

        // Map wraps horizontally (X axis). Ghost copies are created at +/-MapWidth.
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

            bool regionIsLake = RegionIsLake(planetGen, region);

            float height;
            if (regionIsLake)
            {
                // Lake water sits at the level of its lowest shore tile (the natural outlet).
                height = ComputeLakeWaterHeight(planetGen, grid, region);
            }
            else
            {
                // Ocean/seas use the planet's authoritative sea level.
                height = planetGen.SeaLevelWorldY;
            }

            if (regionIsLake)
            {
                // Build a combined hex mesh from all lake tiles in this body.
                // Each tile contributes a hexagon, so the water surface exactly matches
                // the lake's tile footprint — no rectangular overshoot onto land.
                Vector3 centroid = ComputeRegionCentroid(grid, region);
                Mesh mesh = BuildCombinedHexMesh(grid, region, centroid);
                CreateLakeWaterSurface(centroid, mesh, height, mapWidthWorld, spawnedSurfaces.Count);
                createdLakes++;
            }
            else
            {
                // Ocean/seas: axis-aligned bounding box quad (appropriate for large bodies
                // that fill map edges). The AABB naturally covers the rectangular border area.
                var bounds = CalculateRegionBounds(grid, region, padX, padZ);
                CreateOceanWaterSurface(bounds, height, mapWidthWorld, spawnedSurfaces.Count);
                createdOceans++;
            }
            createdRegions++;
        }

        Debug.Log($"[WaterSurfaceGenerator] Water generation complete. Regions={createdRegions} (lakes={createdLakes}, oceans={createdOceans}) TotalSurfaces={spawnedSurfaces.Count}");
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

    #region Tile Classification

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

    #endregion

    #region Lake Water Surface (Per-Tile Hex Mesh)

    /// <summary>
    /// Compute the hex circumradius (center-to-corner distance) matching HexGrid.GenerateFlatGrid.
    /// Used as fallback when the grid's precomputed tileCorners are unavailable.
    /// </summary>
    private static float ComputeHexSize(HexGrid grid)
    {
        float sX = grid.MapWidth / (grid.Width * Mathf.Sqrt(3f));
        float sZ = grid.MapHeight / (1.5f * (grid.Height + 0.5f));
        return Mathf.Max(0.001f, Mathf.Min(sX, sZ));
    }

    /// <summary>
    /// Compute the centroid (average tile center) of a water region.
    /// Used as the mesh origin for lake water surfaces so that the mesh
    /// vertices are in a local space centered on the lake body.
    /// </summary>
    private static Vector3 ComputeRegionCentroid(HexGrid grid, List<int> region)
    {
        Vector3 sum = Vector3.zero;
        foreach (int idx in region)
        {
            sum += grid.tileCenters[idx];
        }
        return region.Count > 0 ? sum / region.Count : Vector3.zero;
    }

    /// <summary>
    /// Build a single combined mesh from all hex tiles in a lake region.
    /// Each tile contributes a hexagon (center + 6 corners = 7 verts, 6 triangles).
    /// Vertices are relative to the given centroid (the mesh origin).
    /// Uses the grid's precomputed tileCorners when available for exact alignment
    /// with the terrain hex geometry; falls back to recomputed corners otherwise.
    /// </summary>
    private static Mesh BuildCombinedHexMesh(HexGrid grid, List<int> region, Vector3 centroid)
    {
        bool useGridCorners = grid.tileCorners != null
                              && grid.CornerVertices != null
                              && grid.CornerVertices.Count > 0;

        float hexSize = 0f;
        if (!useGridCorners)
        {
            hexSize = ComputeHexSize(grid);
        }

        int vertsPerHex = 7; // center + 6 corners
        int trisPerHex = 6;
        int totalVerts = region.Count * vertsPerHex;
        int totalIndices = region.Count * trisPerHex * 3;

        var vertices = new Vector3[totalVerts];
        var uvs = new Vector2[totalVerts];
        var triangles = new int[totalIndices];

        for (int h = 0; h < region.Count; h++)
        {
            int tileIdx = region[h];
            Vector3 tileCenter = grid.tileCenters[tileIdx];

            // Mesh-local position (relative to centroid, flat on XZ plane)
            Vector3 localCenter = tileCenter - centroid;
            localCenter.y = 0f;

            int vBase = h * vertsPerHex;
            int tBase = h * trisPerHex * 3;

            // Center vertex
            vertices[vBase] = localCenter;
            uvs[vBase] = new Vector2(0.5f, 0.5f);

            // 6 corner vertices
            if (useGridCorners
                && tileIdx < grid.tileCorners.Length
                && grid.tileCorners[tileIdx] != null
                && grid.tileCorners[tileIdx].Count == 6)
            {
                // Use precomputed corners from the grid (exact match with terrain)
                var cornerIndices = grid.tileCorners[tileIdx];
                for (int k = 0; k < 6; k++)
                {
                    Vector3 cornerWorld = grid.CornerVertices[cornerIndices[k]];
                    Vector3 localCorner = cornerWorld - centroid;
                    localCorner.y = 0f;
                    vertices[vBase + 1 + k] = localCorner;

                    // UV: unit circle mapping based on hex angle (pointy-top: -30 + 60k degrees)
                    float angle = Mathf.Deg2Rad * (60f * k - 30f);
                    uvs[vBase + 1 + k] = new Vector2(
                        0.5f + 0.5f * Mathf.Cos(angle),
                        0.5f + 0.5f * Mathf.Sin(angle));
                }
            }
            else
            {
                // Fallback: recompute corners (pointy-top, same angles as HexGrid)
                for (int k = 0; k < 6; k++)
                {
                    float angle = Mathf.Deg2Rad * (60f * k - 30f);
                    Vector3 corner = localCenter + new Vector3(
                        hexSize * Mathf.Cos(angle), 0f, hexSize * Mathf.Sin(angle));
                    vertices[vBase + 1 + k] = corner;

                    uvs[vBase + 1 + k] = new Vector2(
                        0.5f + 0.5f * Mathf.Cos(angle),
                        0.5f + 0.5f * Mathf.Sin(angle));
                }
            }

            // 6 triangles (fan from center, counter-clockwise winding viewed from Y+)
            for (int k = 0; k < 6; k++)
            {
                int next = (k + 1) % 6;
                triangles[tBase + k * 3 + 0] = vBase;             // center
                triangles[tBase + k * 3 + 1] = vBase + 1 + k;     // current corner
                triangles[tBase + k * 3 + 2] = vBase + 1 + next;  // next corner
            }
        }

        var mesh = new Mesh { name = "LakeHexMesh" };
        if (totalVerts > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Create a lake water surface using a combined hex mesh positioned at the lake centroid.
    /// The mesh exactly matches the hex tile footprint of the lake body.
    /// </summary>
    private void CreateLakeWaterSurface(Vector3 centroid, Mesh mesh, float height, float mapWidthWorld, int regionIndex)
    {
        if (lakeSurfacePrefab == null)
        {
            Debug.LogWarning("[WaterSurfaceGenerator] Missing lake water surface prefab.");
            return;
        }

        var instance = Instantiate(lakeSurfacePrefab.gameObject, transform, false);
        instance.name = $"WaterSurface_Lake_{regionIndex}";

        // Position at the centroid XZ with the computed water height Y.
        // Convert from world space to this transform's local space so the
        // surface is correct even if the map hierarchy is offset or rotated.
        Vector3 worldPos = new Vector3(centroid.x, height, centroid.z);
        Vector3 localPos = transform.InverseTransformPoint(worldPos);
        instance.transform.localPosition = localPos;

        Debug.Log($"[WaterSurfaceGenerator] Created lake surface idx={regionIndex} tiles={mesh.vertexCount / 7} height={height:F3} centroid=({centroid.x:F1}, {centroid.z:F1}) localPos={localPos}");

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

        meshFilter.sharedMesh = mesh;
        spawnedSurfaces.Add(instance);

        // Ghost copies for horizontal world wrap (+/-MapWidth).
        // Convert the world-space wrap offset to local space for correct placement
        // even if WaterSurfaceGenerator is rotated or scaled.
        if (mapWidthWorld > 0.001f)
        {
            Vector3 localWrapOffset = transform.InverseTransformVector(new Vector3(mapWidthWorld, 0f, 0f));

            var left = Instantiate(instance, transform, false);
            left.name = $"WaterSurface_Lake_{regionIndex}_GhostLeft";
            left.transform.localPosition = localPos - localWrapOffset;
            spawnedSurfaces.Add(left);

            var right = Instantiate(instance, transform, false);
            right.name = $"WaterSurface_Lake_{regionIndex}_GhostRight";
            right.transform.localPosition = localPos + localWrapOffset;
            spawnedSurfaces.Add(right);
        }
    }

    #endregion

    #region Ocean Water Surface (AABB Quad)

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

    /// <summary>
    /// Create an ocean/seas water surface using an AABB quad.
    /// Oceans are large bodies that fill map edges, so a rectangular quad is appropriate.
    /// </summary>
    private void CreateOceanWaterSurface(Bounds bounds, float height, float mapWidthWorld, int regionIndex)
    {
        if (oceanSurfacePrefab == null)
        {
            Debug.LogWarning("[WaterSurfaceGenerator] Missing ocean water surface prefab.");
            return;
        }

        var instance = Instantiate(oceanSurfacePrefab.gameObject, transform, false);
        instance.name = $"WaterSurface_Ocean_{regionIndex}";

        // Convert world-space height to this transform's local Y.
        float localY = transform.InverseTransformPoint(new Vector3(transform.position.x, height, transform.position.z)).y;
        instance.transform.localPosition = new Vector3(0f, localY, 0f);

        Debug.Log($"[WaterSurfaceGenerator] Created ocean surface idx={regionIndex} bounds={bounds.size} height={height}");

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
        if (mapWidthWorld > 0.001f)
        {
            var left = Instantiate(instance, transform, false);
            left.name = $"WaterSurface_Ocean_{regionIndex}_GhostLeft";
            left.transform.localPosition = new Vector3(-mapWidthWorld, localY, 0f);
            spawnedSurfaces.Add(left);

            var right = Instantiate(instance, transform, false);
            right.name = $"WaterSurface_Ocean_{regionIndex}_GhostRight";
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

    #endregion

    #region Lake Water Height

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
                // HexTileData is a class — null means missing data
                if (td == null || !td.isLand) continue;
                // Only consider true land shore tiles (not coast/ocean/seas)
                if (td.biome == Biome.Coast || td.biome == Biome.Ocean || td.biome == Biome.Seas) continue;

                if (td.renderElevation < minShoreRenderElev)
                {
                    minShoreRenderElev = td.renderElevation;
                }
            }
        }

        // Get the ACTUAL displacement strength from HexMapChunkManager (what the shader uses).
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
            if (td == null) continue;
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

    #endregion
}
