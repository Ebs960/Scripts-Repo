using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

/// <summary>
/// Represents one CPU-generated stepped-hex campaign terrain chunk.
/// Chunks share the SurfaceFamily terrain material provided by `HexMapChunkManager`.
/// Chunks can be teleported for seamless world wrapping.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexMapChunk : MonoBehaviour
{
    [Header("Chunk Info (Read-Only)")]
    [SerializeField] private int chunkX;
    [SerializeField] private int chunkZ;
    [SerializeField] private int columnIndex;
    
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh mesh;
    private Material material;
    private Texture2D seasonMaskTexture;
    private MaterialPropertyBlock propertyBlock;
    private int seasonMaskWidth;
    private int seasonMaskHeight;

    // Per-chunk freeze target mask — baked once per season (not animated).
    // Stores tile.freezeTarget in the R channel; the shader multiplies by _FreezeProgress.
    private Texture2D freezeTargetMaskTexture;
    private int freezeMaskWidth;
    private int freezeMaskHeight;
    
    // Reference to manager
    private HexMapChunkManager manager;
    
    // Chunk mesh bounds in local mesh space (the chunk transform places it in the map)
    private float localMinX, localMaxX, localMinZ, localMaxZ;
    
    // UV region this chunk samples from the baked texture
    private Vector2 uvMin;
    private Vector2 uvMax;
    
    // Tile indices contained in this chunk (for dirty tracking)
    private List<int> tileIndices = new List<int>();
    private bool isDirty = true;
    
    public int ChunkX => chunkX;
    public int ChunkZ => chunkZ;
    public int ColumnIndex => columnIndex;
    public List<int> TileIndices => tileIndices;
    public bool IsDirty => isDirty;
    public Vector2 UVMin => uvMin;
    public Vector2 UVMax => uvMax;
    internal Mesh GeneratedMesh => mesh;
    
    public void Initialize(HexMapChunkManager manager, int chunkX, int chunkZ, int columnIndex)
    {
        this.manager = manager;
        this.chunkX = chunkX;
        this.chunkZ = chunkZ;
        this.columnIndex = columnIndex;
        
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        // Terrain chunks should never be occlusion-culled. Occlusion data is typically baked for static level geometry,
        // and can incorrectly cull large/dynamic surfaces at grazing angles, which looks like "terrain disappearing".
        // This is a targeted fix that avoids relying on camera-wide Occlusion Culling toggles.
        if (meshRenderer != null)
        {
            meshRenderer.allowOcclusionWhenDynamic = false;
        }
        
        // Add collider for raycasting
        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        
        mesh = new Mesh();
        mesh.name = $"Chunk_{chunkX}_{chunkZ}";
        meshFilter.mesh = mesh;
        
        // IMPORTANT:
        // These chunks are the *visible* terrain renderers.
        // Do NOT force them onto a "Terrain" layer, because many cameras exclude that layer (common setup),
        // which makes the entire map invisible.
        //
        // Raycasting is handled by `HexMapChunkManager`'s dedicated `ChunkMapCollider` instead.
        gameObject.layer = manager != null ? manager.gameObject.layer : 0;
    }
    
    /// <summary>
    /// Set the material for this chunk. Should be the shared terrain material created by `HexMapChunkManager`.
    /// </summary>
    public void SetMaterial(Material mat)
    {
        material = mat;
        if (meshRenderer != null)
        {
            // IMPORTANT: Use sharedMaterial so all chunks truly share the same instance.
            // Using .material would silently instantiate a per-renderer copy, which breaks
            // later runtime updates and causes main vs ghost columns to diverge.
            meshRenderer.sharedMaterial = material;

            // Ensure this remains disabled even if renderer got recreated.
            meshRenderer.allowOcclusionWhenDynamic = false;
        }
    }

    /// <summary>
    /// Controls only the visible terrain surface. The chunk object and its collider,
    /// data, children, and wrapping references remain available.
    /// </summary>
    public void SetTerrainVisible(bool visible)
    {
        if (meshRenderer != null)
            meshRenderer.enabled = visible;
    }

    /// <summary>
    /// Controls the existing per-chunk liquid-water renderer without rebuilding it.
    /// </summary>
    public void SetWaterVisible(bool visible)
    {
        Transform water = transform.Find("Water");
        if (water == null) return;

        var renderer = water.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.enabled = visible;
    }
    
    /// <summary>
    /// Set the mesh-local bounds this chunk covers.
    /// The chunk's transform controls where this mesh sits in the overall map.
    /// </summary>
    public void SetBounds(float minX, float maxX, float minZ, float maxZ)
    {
        this.localMinX = minX;
        this.localMaxX = maxX;
        this.localMinZ = minZ;
        this.localMaxZ = maxZ;
        isDirty = true;
    }
    
    /// <summary>
    /// Set the UV region this chunk samples from the main baked texture.
    /// Applies small epsilon inset to prevent UV seam artifacts at chunk boundaries from texture interpolation.
    /// </summary>
    public void SetUVRegion(Vector2 uvMin, Vector2 uvMax)
    {
        // Tiny epsilon inset prevents texture sampling from bleeding across chunk boundaries when
        // bilinear/trilinear filtering interpolates across seams. Typical atlas padding is 1-2 texels.
        const float uvEpsilon = 0.0005f; // ~1 texel for 2048x2048 texture
        this.uvMin = new Vector2(
            Mathf.Lerp(uvMin.x, uvMax.x, uvEpsilon),
            Mathf.Lerp(uvMin.y, uvMax.y, uvEpsilon)
        );
        this.uvMax = new Vector2(
            Mathf.Lerp(uvMin.x, uvMax.x, 1f - uvEpsilon),
            Mathf.Lerp(uvMin.y, uvMax.y, 1f - uvEpsilon)
        );
        isDirty = true;
    }

    public void UpdateSeasonMask(
        int lutWidth,
        int lutHeight,
        int chunkPixelWidth,
        int chunkPixelHeight,
        int[] lut,
        PlanetGenerator planetGenerator,
        ClimateManager climateManager,
        Season season)
    {
        if (meshRenderer == null || lut == null || planetGenerator == null || climateManager == null)
        {
            return;
        }

        if (chunkPixelWidth <= 0 || chunkPixelHeight <= 0 || lutWidth <= 0 || lutHeight <= 0)
        {
            return;
        }

        if (seasonMaskTexture == null || seasonMaskWidth != chunkPixelWidth || seasonMaskHeight != chunkPixelHeight)
        {
            seasonMaskTexture = new Texture2D(chunkPixelWidth, chunkPixelHeight, TextureFormat.RGBA32, false);
            seasonMaskTexture.filterMode = FilterMode.Point;
            seasonMaskTexture.wrapMode = TextureWrapMode.Clamp;
            seasonMaskTexture.name = $"SeasonMask_{chunkX}_{chunkZ}";
            seasonMaskWidth = chunkPixelWidth;
            seasonMaskHeight = chunkPixelHeight;
        }

        int pixelCount = chunkPixelWidth * chunkPixelHeight;
        var pixels = ArrayPoolUtils.Rent<Color>(pixelCount, true);

        int chunkOffsetX = chunkX * chunkPixelWidth;
        int chunkOffsetY = chunkZ * chunkPixelHeight;

        for (int y = 0; y < chunkPixelHeight; y++)
        {
            int globalY = chunkOffsetY + y;
            if (globalY < 0 || globalY >= lutHeight) continue;

            int rowBase = y * chunkPixelWidth;
            int lutRowBase = globalY * lutWidth;
            for (int x = 0; x < chunkPixelWidth; x++)
            {
                int globalX = chunkOffsetX + x;
                if (globalX < 0 || globalX >= lutWidth) continue;

                int lutIndex = lutRowBase + globalX;
                if (lutIndex < 0 || lutIndex >= lut.Length) continue;

                int tileIndex = lut[lutIndex];
                if (tileIndex < 0) continue;

                if (!planetGenerator.data.TryGetValue(tileIndex, out var tile))
                {
                    continue;
                }

                var response = climateManager.GetSeasonResponse(tile.biome, season);
                pixels[rowBase + x] = new Color(response.snow, 0f, response.dry, 0f);
            }
        }

        seasonMaskTexture.SetPixels(pixels);
        seasonMaskTexture.Apply();

        ArrayPoolUtils.Return<Color>(pixels);

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        Vector2 uvScale = new Vector2(
            1f / Mathf.Max(uvMax.x - uvMin.x, 0.0001f),
            1f / Mathf.Max(uvMax.y - uvMin.y, 0.0001f));
        Vector2 uvOffset = new Vector2(-uvMin.x * uvScale.x, -uvMin.y * uvScale.y);

        propertyBlock.SetTexture("_TileSeasonMask", seasonMaskTexture);
        propertyBlock.SetVector("_TileSeasonMask_TexSize", new Vector2(chunkPixelWidth, chunkPixelHeight));
        propertyBlock.SetVector("_TileSeasonMask_ST", new Vector4(uvScale.x, uvScale.y, uvOffset.x, uvOffset.y));
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>
    /// Bake the per-chunk freeze target mask texture.
    /// Must be called once after <see cref="ClimateManager"/> fires
    /// <c>OnPlanetFreezeTargetsReady</c> (i.e. after <c>tile.freezeTarget</c> values have
    /// been written for the season).
    /// <para>
    /// The texture stores <c>tile.freezeTarget</c> in the <b>R channel</b>, a lake flag in
    /// <b>G</b>, and a river flag in <b>B</b> for every LUT pixel. Non-water tiles write 0.
    /// The shader computes actual freeze as <c>R * _FreezeProgress</c> and uses G/B to pick
    /// the correct ice texture arrays without needing a second lookup texture.
    /// </para>
    /// </summary>
    public void UpdateFreezeTargetMask(
        int lutWidth,
        int lutHeight,
        int chunkPixelWidth,
        int chunkPixelHeight,
        int[] lut,
        PlanetGenerator planetGenerator)
    {
        if (meshRenderer == null || lut == null || planetGenerator == null)
            return;

        if (chunkPixelWidth <= 0 || chunkPixelHeight <= 0 || lutWidth <= 0 || lutHeight <= 0)
            return;

        // Create or recreate the texture if dimensions changed
        if (freezeTargetMaskTexture == null
            || freezeMaskWidth  != chunkPixelWidth
            || freezeMaskHeight != chunkPixelHeight)
        {
            freezeTargetMaskTexture = new Texture2D(chunkPixelWidth, chunkPixelHeight, TextureFormat.RGBA32, false);
            freezeTargetMaskTexture.filterMode = FilterMode.Point;
            freezeTargetMaskTexture.wrapMode   = TextureWrapMode.Clamp;
            freezeTargetMaskTexture.name       = $"FreezeMask_{chunkX}_{chunkZ}";
            freezeMaskWidth  = chunkPixelWidth;
            freezeMaskHeight = chunkPixelHeight;
        }

        int pixelCount = chunkPixelWidth * chunkPixelHeight;
        var pixels = ArrayPoolUtils.Rent<Color>(pixelCount, true);

        int chunkOffsetX = chunkX * chunkPixelWidth;
        int chunkOffsetY = chunkZ * chunkPixelHeight;
        int freezePixelCount = 0;

        for (int y = 0; y < chunkPixelHeight; y++)
        {
            int globalY = chunkOffsetY + y;
            if (globalY < 0 || globalY >= lutHeight) continue;

            int rowBase    = y * chunkPixelWidth;
            int lutRowBase = globalY * lutWidth;

            for (int x = 0; x < chunkPixelWidth; x++)
            {
                int globalX = chunkOffsetX + x;
                if (globalX < 0 || globalX >= lutWidth) continue;

                int lutIndex = lutRowBase + globalX;
                if (lutIndex < 0 || lutIndex >= lut.Length) continue;

                int tileIndex = lut[lutIndex];
                if (tileIndex < 0) continue;

                float freezeTarget = 0f;
                float isLake = 0f;
                float isRiver = 0f;
                if (planetGenerator.data.TryGetValue(tileIndex, out var tile)
                    && tile.waterType != TileWaterType.None)
                {
                    freezeTarget = tile.freezeTarget;
                    isLake = tile.waterType == TileWaterType.Lake ? 1f : 0f;
                    isRiver = tile.waterType == TileWaterType.River ? 1f : 0f;
                    if (freezeTarget > 0f) freezePixelCount++;
                }

                pixels[rowBase + x] = new Color(freezeTarget, isLake, isRiver, 0f);
            }
        }

        if (freezePixelCount > 0)
            Debug.Log($"[HexMapChunk] Chunk({chunkX},{chunkZ}) freeze mask: {freezePixelCount} pixels with non-zero freezeTarget out of {pixelCount} total");

        freezeTargetMaskTexture.SetPixels(pixels);
        freezeTargetMaskTexture.Apply();
        ArrayPoolUtils.Return<Color>(pixels);

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        // Re-read the existing property block so we don't overwrite season mask values
        meshRenderer.GetPropertyBlock(propertyBlock);

        Vector2 uvScale  = new Vector2(
            1f / Mathf.Max(uvMax.x - uvMin.x, 0.0001f),
            1f / Mathf.Max(uvMax.y - uvMin.y, 0.0001f));
        Vector2 uvOffset = new Vector2(-uvMin.x * uvScale.x, -uvMin.y * uvScale.y);

        propertyBlock.SetTexture("_FreezeMaskTex", freezeTargetMaskTexture);
        propertyBlock.SetVector("_FreezeMask_ST", new Vector4(uvScale.x, uvScale.y, uvOffset.x, uvOffset.y));
        meshRenderer.SetPropertyBlock(propertyBlock);
    }


    /// </summary>
    public void SetTileIndices(List<int> indices)
    {
        tileIndices.Clear();
        tileIndices.AddRange(indices);
    }
    
    /// <summary>
    /// Mark a specific tile as needing update.
    /// </summary>
    public void MarkTileDirty(int tileIndex)
    {
        if (tileIndices.Contains(tileIndex))
        {
            isDirty = true;
        }
    }
    
    /// <summary>
    /// Mark the chunk as needing mesh rebuild.
    /// </summary>
    public void MarkDirty()
    {
        isDirty = true;
    }
    
    /// <summary>
    /// Rebuild the chunk mesh if dirty.
    /// </summary>
    public void Refresh()
    {
        if (!isDirty) return;

        GenerateTerrainMesh();
        isDirty = false;
    }
    
    /// <summary>
    /// Force immediate mesh regeneration.
    /// </summary>
    public void ForceRefresh()
    {
        isDirty = true;
        Refresh();
    }
    
    /// <summary>
    /// Builds the production faceted terrain mesh for all stepped hexes assigned to this chunk.
    /// Terrain elevation is authored directly into the CPU mesh and is never shader-displaced.
    /// </summary>
    private void GenerateTerrainMesh()
    {
        if (manager == null || manager.Grid == null || !manager.Grid.IsBuilt || mesh == null)
            return;

        HexGrid grid = manager.Grid;
        float fullRadius = grid.GetLookupData().s;
        float outerRadius = fullRadius * manager.HexTopScale;
        float bevelWidthWorld = Mathf.Clamp(fullRadius * manager.BevelWidth, 0f, outerRadius);
        float innerRadius = outerRadius - bevelWidthWorld;
        // A zero-width bevel keeps the top and wall meeting at the same height.
        float bevelDrop = bevelWidthWorld > 0.0001f ? Mathf.Max(0f, manager.BevelDrop) : 0f;
        float seamDepth = manager.SeamDepth;
        float chunkOriginX = -manager.MapWidth * 0.5f + chunkX * (manager.MapWidth / Mathf.Max(1, manager.GridChunkCountX));
        float chunkOriginZ = -manager.MapHeight * 0.5f + chunkZ * (manager.MapHeight / Mathf.Max(1, manager.GridChunkCountZ));

        var vertices = new List<Vector3>(tileIndices.Count * 55);
        var uvs = new List<Vector2>(tileIndices.Count * 55);
        var normals = new List<Vector3>(tileIndices.Count * 55);
        var tangents = new List<Vector4>(tileIndices.Count * 55);
        var triangles = new List<int>(tileIndices.Count * 90);

        foreach (int tileIndex in tileIndices)
        {
            if (tileIndex < 0 || tileIndex >= grid.TileCount)
                continue;

            Vector3 mapCenter = grid.tileCenters[tileIndex];
            // Chunk parents already contribute flatY. Convert the authoritative world Y
            // exactly once to local mesh space rather than adding either baseline twice.
            float topY = manager.GetRenderedTerrainWorldY(tileIndex) - manager.FlatY;
            float shoulderY = topY - bevelDrop;
            Vector3 localCenter = new Vector3(mapCenter.x - chunkOriginX, topY, mapCenter.z - chunkOriginZ);
            Vector2 centerUV = MapPositionToUV(mapCenter);

            int topStart = vertices.Count;
            AddVertex(localCenter, centerUV, Vector3.up, Vector3.right, vertices, uvs, normals, tangents);
            for (int corner = 0; corner < 6; corner++)
            {
                float angle = Mathf.Deg2Rad * (60f * corner - 30f);
                Vector3 cornerOffset = new Vector3(innerRadius * Mathf.Cos(angle), 0f, innerRadius * Mathf.Sin(angle));
                AddVertex(localCenter + cornerOffset, MapPositionToUV(mapCenter + cornerOffset),
                    Vector3.up, Vector3.right, vertices, uvs, normals, tangents);
            }

            for (int corner = 0; corner < 6; corner++)
            {
                int next = (corner + 1) % 6;
                // Reverse the angular order so the top face points upward in Unity's XZ plane.
                triangles.Add(topStart);
                triangles.Add(topStart + 1 + next);
                triangles.Add(topStart + 1 + corner);
            }

            // Give each bevel edge its own vertices so its angled normal remains faceted.
            if (bevelWidthWorld > 0.0001f)
            {
                for (int edge = 0; edge < 6; edge++)
                {
                    float angleA = Mathf.Deg2Rad * (60f * edge - 30f);
                    float angleB = Mathf.Deg2Rad * (60f * ((edge + 1) % 6) - 30f);
                    Vector3 innerA = localCenter + new Vector3(innerRadius * Mathf.Cos(angleA), 0f, innerRadius * Mathf.Sin(angleA));
                    Vector3 innerB = localCenter + new Vector3(innerRadius * Mathf.Cos(angleB), 0f, innerRadius * Mathf.Sin(angleB));
                    Vector3 outerA = new Vector3(localCenter.x + outerRadius * Mathf.Cos(angleA), shoulderY, localCenter.z + outerRadius * Mathf.Sin(angleA));
                    Vector3 outerB = new Vector3(localCenter.x + outerRadius * Mathf.Cos(angleB), shoulderY, localCenter.z + outerRadius * Mathf.Sin(angleB));
                    Vector3 outward = new Vector3(
                        outerA.x + outerB.x - localCenter.x * 2f, 0f,
                        outerA.z + outerB.z - localCenter.z * 2f).normalized;
                    Vector3 bevelNormal = (Vector3.up * bevelWidthWorld + outward * bevelDrop).normalized;
                    Vector3 edgeTangent = (innerB - innerA).normalized;
                    int bevelStart = vertices.Count;

                    // Center UV ownership prevents the rim from sampling an adjacent biome.
                    AddVertex(innerA, centerUV, bevelNormal, edgeTangent, vertices, uvs, normals, tangents);
                    AddVertex(innerB, centerUV, bevelNormal, edgeTangent, vertices, uvs, normals, tangents);
                    AddVertex(outerA, centerUV, bevelNormal, edgeTangent, vertices, uvs, normals, tangents);
                    AddVertex(outerB, centerUV, bevelNormal, edgeTangent, vertices, uvs, normals, tangents);

                    triangles.Add(bevelStart);
                    triangles.Add(bevelStart + 3);
                    triangles.Add(bevelStart + 2);
                    triangles.Add(bevelStart);
                    triangles.Add(bevelStart + 1);
                    triangles.Add(bevelStart + 3);
                }
            }

            for (int edge = 0; edge < 6; edge++)
            {
                float angleA = Mathf.Deg2Rad * (60f * edge - 30f);
                float angleB = Mathf.Deg2Rad * (60f * ((edge + 1) % 6) - 30f);
                Vector3 offsetA = new Vector3(outerRadius * Mathf.Cos(angleA), 0f, outerRadius * Mathf.Sin(angleA));
                Vector3 offsetB = new Vector3(outerRadius * Mathf.Cos(angleB), 0f, outerRadius * Mathf.Sin(angleB));
                Vector3 outward = new Vector3(offsetA.x + offsetB.x, 0f, offsetA.z + offsetB.z).normalized;

                int neighborIndex = grid.GetTileAtPosition(mapCenter + outward * (fullRadius * 1.05f));
                bool hasNeighbor = neighborIndex >= 0 && neighborIndex != tileIndex;
                float bottomY;
                if (!hasNeighbor)
                {
                    bottomY = topY - Mathf.Max(10f, seamDepth);
                }
                else
                {
                    float neighborY = manager.GetRenderedTerrainWorldY(neighborIndex) - manager.FlatY;
                    if (topY > neighborY + 0.0001f)
                        bottomY = neighborY - bevelDrop;
                    else if (manager.HexTopScale < 0.9999f && Mathf.Abs(topY - neighborY) <= 0.0001f && seamDepth > 0f)
                        bottomY = topY - Mathf.Max(seamDepth, bevelDrop);
                    else
                        continue;
                }

                Vector3 upperA = new Vector3(localCenter.x + offsetA.x, shoulderY, localCenter.z + offsetA.z);
                Vector3 upperB = new Vector3(localCenter.x + offsetB.x, shoulderY, localCenter.z + offsetB.z);
                Vector3 lowerA = new Vector3(upperA.x, bottomY, upperA.z);
                Vector3 lowerB = new Vector3(upperB.x, bottomY, upperB.z);
                Vector3 edgeTangent = (upperB - upperA).normalized;
                int wallStart = vertices.Count;

                // Wall UVs deliberately use the owning (upper) tile center for stable biome selection.
                AddVertex(upperA, centerUV, outward, edgeTangent, vertices, uvs, normals, tangents);
                AddVertex(upperB, centerUV, outward, edgeTangent, vertices, uvs, normals, tangents);
                AddVertex(lowerA, centerUV, outward, edgeTangent, vertices, uvs, normals, tangents);
                AddVertex(lowerB, centerUV, outward, edgeTangent, vertices, uvs, normals, tangents);

                triangles.Add(wallStart);
                triangles.Add(wallStart + 3);
                triangles.Add(wallStart + 2);
                triangles.Add(wallStart);
                triangles.Add(wallStart + 1);
                triangles.Add(wallStart + 3);
            }
        }

        mesh.Clear();
        mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.SetTangents(tangents);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }

    private Vector2 MapPositionToUV(Vector3 mapPosition)
    {
        return new Vector2(
            (mapPosition.x + manager.MapWidth * 0.5f) / manager.MapWidth,
            Mathf.Clamp01((mapPosition.z + manager.MapHeight * 0.5f) / manager.MapHeight));
    }

    private static void AddVertex(
        Vector3 position, Vector2 uv, Vector3 normal, Vector3 tangent,
        List<Vector3> vertices, List<Vector2> uvs, List<Vector3> normals, List<Vector4> tangents)
    {
        vertices.Add(position);
        uvs.Add(uv);
        normals.Add(normal);
        tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
    }
    
    /// <summary>
    /// Get the world-space bounds of this chunk.
    /// </summary>
    public Bounds GetBounds()
    {
        if (mesh != null && meshRenderer != null)
        {
            return meshRenderer.bounds;
        }
        Vector3 center = transform.TransformPoint(new Vector3(
            (localMinX + localMaxX) * 0.5f, 
            0f, 
            (localMinZ + localMaxZ) * 0.5f));
        Vector3 size = new Vector3(localMaxX - localMinX, 10f, localMaxZ - localMinZ);
        return new Bounds(center, size);
    }
    
    private void OnDestroy()
    {
        if (mesh != null)
        {
            Destroy(mesh);
        }
        // Don't destroy material - it's shared/managed by ChunkManager
    }
}
