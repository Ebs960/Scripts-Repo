// Assets/Scripts/Hexasphere/HexasphereRenderer.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Renders a procedurally generated hex‑sphere.  Works with any component
/// that implements <see cref="IHexasphereGenerator"/> (PlanetGenerator,
/// MoonGenerator, etc.).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexasphereRenderer : MonoBehaviour
{
    // ───────────────────────────────────── Inspector Slots ─────────────────────────────────────
    [Header("Render Components  (drag‑assign or leave blank)")]
    [SerializeField] private MeshFilter   meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;

    [Header("Material")]
    [Tooltip("If set, this material will replace the MeshRenderer's material in Awake().")]
    public Material planetMaterial;

    [Header("Generator Source")]
    [Tooltip("Drag a PlanetGenerator, MoonGenerator, or any MonoBehaviour that implements IHexasphereGenerator.")]
    [SerializeField] public  MonoBehaviour generatorSource;   // Unity‑serialised, editable

    [Header("Optional UI")]
    public LoadingPanelController loadingPanel;

    [Header("Mesh Quality")]
    [Tooltip("Use separate vertices for each tile to ensure clear boundaries. Trades memory for visual clarity.")]
    public bool useSeparateVertices = false;
    
    [Header("Biome Data")]
    [Tooltip("Use per-tile biome data instead of UV-based texture sampling for more accurate biome mapping.")]
    public bool usePerTileBiomeData = true;

    // ───────────────────────────────────── Helpers ─────────────────────────────────────
    private MeshFilter   MF => meshFilter   != null ? meshFilter   : (meshFilter   = GetComponent<MeshFilter>());
    private MeshRenderer MR => meshRenderer != null ? meshRenderer : (meshRenderer = GetComponent<MeshRenderer>());

    private IHexasphereGenerator Generator => generatorSource as IHexasphereGenerator;
    
    // Store vertex-to-tile mapping for height displacement
    private Dictionary<int, List<int>> vertexToTiles;

    // ───────────────────────────────────── Unity ───────────────────────────────────────
    void Awake()
    {
        // Ensure references exist even if not drag‑assigned
        _ = MF; _ = MR;

        // Apply override material if provided, or create a default material
        if (planetMaterial != null)
        {
            MR.sharedMaterial = planetMaterial;
        }
        else if (MR.sharedMaterial == null)
        {
            // Create a default material with the HexasphereURP shader
            var shader = Shader.Find("Custom/HexasphereURP");
            if (shader != null)
            {
                planetMaterial = new Material(shader);
                MR.sharedMaterial = planetMaterial;
                Debug.Log("[HexasphereRenderer] Created default material with HexasphereURP shader");
            }
            else
            {
                Debug.LogError("[HexasphereRenderer] Could not find Custom/HexasphereURP shader!");
            }
        }

        // Auto‑detect a generator on same GO if slot empty
        if (generatorSource == null)
        {
            var gen = GetComponent<IHexasphereGenerator>();
            generatorSource = gen as MonoBehaviour;
        }
    }

    // ───────────────────────────────────── Public API ───────────────────────────────────
    /// <summary>Build and assign the planet/moon mesh.</summary>
    public void BuildMesh(SphericalHexGrid grid)
    {
        Report(0.05f, "Building mesh…");
        
        if (usePerTileBiomeData)
        {
            // Build mesh with per-tile biome data
            Dictionary<int, int> tileBiomeIndices = new Dictionary<int, int>();
            
            // Get biome indices from the generator if available
            if (Generator != null)
            {
                for (int i = 0; i < grid.TileCount; i++)
                {
                    var tileData = Generator.GetHexTileData(i);
                    if (tileData != null)
                    {
                        // Convert biome enum to index
                        int biomeIndex = (int)tileData.biome;
                        tileBiomeIndices[i] = biomeIndex;
                    }
                }
            }
            
            MF.sharedMesh = HexTileMeshBuilder.BuildWithPerTileBiomeData(grid, tileBiomeIndices, out vertexToTiles);
            Debug.Log("[HexasphereRenderer] Built mesh with per-tile biome data");
        }
        else if (useSeparateVertices)
        {
            MF.sharedMesh = HexTileMeshBuilder.BuildWithSeparateVertices(grid, out _, out vertexToTiles);
            Debug.Log("[HexasphereRenderer] Using separate vertex mesh for clear tile boundaries");
        }
        else
        {
            MF.sharedMesh = HexTileMeshBuilder.Build(grid, out _, out vertexToTiles);
            Debug.Log("[HexasphereRenderer] Using shared vertex mesh for memory efficiency");
        }
    }

    /// <summary>Radially displace vertices with smooth per-vertex elevation based on tile data.</summary>
    public void ApplyHeightDisplacement(float radius)
    {
        Report(0.35f, "Applying elevation…");

        Mesh m = MF.sharedMesh;
        var v = m.vertices;
        
        // Get the generator to access tile elevation data
        if (Generator == null)
        {
            Debug.LogError("[HexasphereRenderer] No generator found for height displacement!");
            return;
        }
        
        // Apply smooth per-vertex elevation displacement
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 originalVertex = v[i];
            float elevationOffset = 0f;
            
            // Calculate average elevation from all tiles that share this vertex
            if (vertexToTiles != null && vertexToTiles.ContainsKey(i))
            {
                float totalElevation = 0f;
                int tileCount = 0;
                
                foreach (int tileIndex in vertexToTiles[i])
                {
                    float tileElevation = Generator.GetTileElevation(tileIndex);
                    totalElevation += tileElevation;
                    tileCount++;
                }
                
                if (tileCount > 0)
                {
                    elevationOffset = (totalElevation / tileCount) * radius;
                }
            }
            
            // Apply the elevation offset along the vertex normal (radial direction)
            v[i] = originalVertex.normalized * (radius + elevationOffset);
        }
        
        m.vertices = v;
        m.RecalculateNormals();
        
        Debug.Log($"[HexasphereRenderer] Applied height displacement with radius {radius}, vertex count: {v.Length}");
    }

    /// <summary>Bind biome lookup textures to the material.</summary>
    public void PushBiomeLookups(Texture2D indexTex, Texture2DArray albedoArray)
    {
        Report(0.65f, "Uploading textures…");

        var mat = MR.sharedMaterial;
        if (mat == null)
        {
            Debug.LogError("[HexasphereRenderer] No material assigned to MeshRenderer!");
            return;
        }

        Debug.Log($"[HexasphereRenderer] Pushing textures to material: indexTex={indexTex?.width}x{indexTex?.height}, albedoArray={albedoArray?.width}x{albedoArray?.height}x{albedoArray?.depth}");
        
        if (indexTex != null) 
        {
            mat.SetTexture("_BiomeIndexTex", indexTex);
            Debug.Log("[HexasphereRenderer] Set _BiomeIndexTex");
            
            // Debug: Sample a few pixels from the index texture
            if (indexTex.width > 0 && indexTex.height > 0)
            {
                Color sample1 = indexTex.GetPixel(0, 0);
                Color sample2 = indexTex.GetPixel(indexTex.width/2, indexTex.height/2);
                Color sample3 = indexTex.GetPixel(indexTex.width-1, indexTex.height-1);
                Debug.Log($"[HexasphereRenderer] Index texture samples - Center: {sample1.r:F3}, Middle: {sample2.r:F3}, Corner: {sample3.r:F3}");
            }
        }
        else
        {
            Debug.LogWarning("[HexasphereRenderer] indexTex is null!");
        }
        
        if (albedoArray != null) 
        {
            mat.SetTexture("_BiomeAlbedoArray", albedoArray);
            Debug.Log("[HexasphereRenderer] Set _BiomeAlbedoArray");
        }
        else
        {
            Debug.LogWarning("[HexasphereRenderer] albedoArray is null!");
        }

        int count = Generator != null ? Generator.GetBiomeSettings().Count
                                      : albedoArray != null ? albedoArray.depth : 0;
        mat.SetInt("_BiomeCount", count);
        Debug.Log($"[HexasphereRenderer] Set _BiomeCount = {count}");

        // Set biome data mode
        mat.SetFloat("_UsePerTileBiomeData", usePerTileBiomeData ? 1.0f : 0.0f);
        Debug.Log($"[HexasphereRenderer] Set _UsePerTileBiomeData = {usePerTileBiomeData}");

        // Enable sharp boundaries if using separate vertices
        if (useSeparateVertices)
        {
            mat.SetFloat("_SharpBoundaries", 1.0f);
            Debug.Log("[HexasphereRenderer] Enabled sharp biome boundaries");
        }
        else
        {
            mat.SetFloat("_SharpBoundaries", 0.0f);
        }

        Report(1f, "Planet ready!");
    }

    /// <summary>Enable or disable sharp biome boundaries in the material.</summary>
    public void SetSharpBoundaries(bool enabled)
    {
        var mat = MR.sharedMaterial;
        if (mat != null)
        {
            mat.SetFloat("_SharpBoundaries", enabled ? 1.0f : 0.0f);
            Debug.Log($"[HexasphereRenderer] Sharp boundaries {(enabled ? "enabled" : "disabled")}");
        }
    }

    /// <summary>Enable or disable per-tile biome data mode.</summary>
    public void SetPerTileBiomeData(bool enabled)
    {
        usePerTileBiomeData = enabled;
        var mat = MR.sharedMaterial;
        if (mat != null)
        {
            mat.SetFloat("_UsePerTileBiomeData", enabled ? 1.0f : 0.0f);
            Debug.Log($"[HexasphereRenderer] Per-tile biome data {(enabled ? "enabled" : "disabled")}");
        }
    }

    // ───────────────────────────────────── Helpers ─────────────────────────────────────
    void Report(float pct, string msg)
    {
        if (loadingPanel == null) return;
        loadingPanel.SetProgress(pct);
        loadingPanel.SetStatus(msg);
    }
}
