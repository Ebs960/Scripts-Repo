using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents a single chunk of the hex map.
/// Each chunk is a subdivided plane that samples from the shared baked texture.
/// Chunks use the same FlatMapDisplacement_URP shader as FlatMapTextureRenderer.
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
    
    // Reference to manager
    private HexMapChunkManager manager;
    
    // Chunk bounds in world space (local coordinates)
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
        
        // Add collider for raycasting
        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        
        mesh = new Mesh();
        mesh.name = $"Chunk_{chunkX}_{chunkZ}";
        meshFilter.mesh = mesh;
        
        // Set layer for terrain raycasting
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        gameObject.layer = terrainLayer >= 0 ? terrainLayer : 0;
    }
    
    /// <summary>
    /// Set the material for this chunk. Should be an instance of FlatMapDisplacement_URP.
    /// </summary>
    public void SetMaterial(Material mat)
    {
        material = mat;
        if (meshRenderer != null)
        {
            meshRenderer.material = material;
        }
    }
    
    /// <summary>
    /// Set the world-space bounds this chunk covers (in local chunk coordinates).
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
    /// </summary>
    public void SetUVRegion(Vector2 uvMin, Vector2 uvMax)
    {
        this.uvMin = uvMin;
        this.uvMax = uvMax;
        isDirty = true;
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
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        
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
