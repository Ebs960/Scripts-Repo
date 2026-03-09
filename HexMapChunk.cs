using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents a single chunk of the hex map.
/// Each chunk is a subdivided plane that samples from the shared baked texture.
/// Chunks use the shared terrain shader provided by `HexMapChunkManager`.
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
                pixels[rowBase + x] = new Color(response.snow, response.wet, response.dry, 0f);
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
    /// Assign which tile indices belong to this chunk (for dirty tracking).
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
        
        GenerateSubdividedMesh();
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
    /// Generate a subdivided plane mesh that maps to the baked texture region.
    /// Uses the same approach as FlatMapTextureRenderer.CreateSubdividedPlane().
    /// GPU vertex shader does elevation displacement via heightmap.
    /// </summary>
    private void GenerateSubdividedMesh()
    {
        if (manager == null) return;
        
        int subdivisionsX = manager.MeshSubdivisionsPerChunk;
        int subdivisionsZ = Mathf.Max(1, Mathf.RoundToInt(subdivisionsX * ((localMaxZ - localMinZ) / (localMaxX - localMinX))));
        
        int vertsX = subdivisionsX + 1;
        int vertsZ = subdivisionsZ + 1;
        int vertCount = vertsX * vertsZ;
        
        var vertices = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var normals = new Vector3[vertCount];
        var tangents = new Vector4[vertCount];
        
        float width = localMaxX - localMinX;
        float height = localMaxZ - localMinZ;
        
        // Generate vertices
        for (int z = 0; z < vertsZ; z++)
        {
            for (int x = 0; x < vertsX; x++)
            {
                int idx = z * vertsX + x;
                
                float tx = (float)x / subdivisionsX;
                float tz = (float)z / subdivisionsZ;
                
                // Local position (Y will be displaced by GPU shader via heightmap)
                vertices[idx] = new Vector3(
                    localMinX + tx * width,
                    0f,
                    localMinZ + tz * height
                );
                
                // UV for main texture (interpolate within our region of the baked texture)
                uvs[idx] = new Vector2(
                    Mathf.Lerp(uvMin.x, uvMax.x, tx),
                    Mathf.Lerp(uvMin.y, uvMax.y, tz)
                );
                
                normals[idx] = Vector3.up;
                tangents[idx] = new Vector4(1f, 0f, 0f, 1f);
            }
        }
        
        // Generate triangles
        int triCount = subdivisionsX * subdivisionsZ * 6;
        var triangles = new int[triCount];
        int triIdx = 0;
        
        for (int z = 0; z < subdivisionsZ; z++)
        {
            for (int x = 0; x < subdivisionsX; x++)
            {
                int bl = z * vertsX + x;
                int br = bl + 1;
                int tl = bl + vertsX;
                int tr = tl + 1;
                
                // First triangle
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tl;
                triangles[triIdx++] = tr;
                
                // Second triangle
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tr;
                triangles[triIdx++] = br;
            }
        }
        
        // Apply to mesh
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        // IMPORTANT (HDRP / GPU displacement):
        // This mesh is a flat plane in CPU vertex data; actual terrain relief is applied in the shader via heightmap displacement.
        // Unity's frustum culling uses the mesh bounds. If bounds have ~0 thickness in Y, chunks can be culled incorrectly
        // at low camera angles (looks like "terrain disappearing / see-through at the horizon").
        //
        // Fix: expand the bounds vertically based on the displacement strength.
            try
            {
                float displacement = 1f; // Default: world-space elevation scale
                if (manager != null && manager.SharedMaterial != null && manager.SharedMaterial.HasProperty("_ElevationScale"))
                {
                    displacement = manager.SharedMaterial.GetFloat("_ElevationScale");
                }
                else if (manager != null)
                {
                    displacement = manager.DisplacementStrength;
                }

                // Expand bounds significantly to prevent frustum culling at shallow camera angles.
                // At low pitch angles (30-40°), chunks can be incorrectly culled if bounds are too thin in Y.
                // Use: displacement + generous padding, enforced with robust minimum.
                float halfY = Mathf.Max(displacement + 10f, 40f); // At least 80 units tall
                var b = mesh.bounds;
                b.center = new Vector3(b.center.x, 0f, b.center.z);
                b.size = new Vector3(Mathf.Max(b.size.x, 0.0001f), halfY * 2f, Mathf.Max(b.size.z, 0.0001f));
                mesh.bounds = b;
            }
            catch
            {
                // Bounds expansion is a robustness improvement; ignore failures to avoid breaking chunk generation.
            }
        
        // Update collider
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
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
