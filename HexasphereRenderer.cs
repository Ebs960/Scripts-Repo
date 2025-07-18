// Assets/Scripts/Hexasphere/HexasphereRenderer.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Renders a procedurally generated hex‑sphere. Works with any component that
/// implements <see cref="IHexasphereGenerator"/> (PlanetGenerator, MoonGenerator, etc.),
/// OR with a plain float[] supplied through <see cref="SetCustomElevations"/>.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexasphereRenderer : MonoBehaviour
{
    // ─────────────────────────── Inspector Slots ───────────────────────────
    [Header("Render Components  (drag‑assign or leave blank)")]
    [SerializeField] private MeshFilter   meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;

    [Header("Material")]
    [Tooltip("If set, this material will replace the MeshRenderer's material in Awake().")]
    public Material planetMaterial;

    [Header("Generator Source")]
    [Tooltip("Drag a PlanetGenerator, MoonGenerator, or any MonoBehaviour that implements IHexasphereGenerator.")]
    [SerializeField] public  MonoBehaviour generatorSource;

    [Header("Optional UI")]
    public LoadingPanelController loadingPanel;

    [Header("Mesh Quality")]
    [Tooltip("Use separate vertices for each tile to ensure clear boundaries. Trades memory for visual clarity.")]
    public bool useSeparateVertices = false;
    
    [Header("Biome Data")]
    [Tooltip("Use per‑tile biome data instead of UV‑based texture sampling for more accurate biome mapping.")]
    public bool usePerTileBiomeData = true;

    // ─────────────────────────── Helpers ───────────────────────────
    private MeshFilter   MF => meshFilter   != null ? meshFilter   : (meshFilter   = GetComponent<MeshFilter>());
    private MeshRenderer MR => meshRenderer != null ? meshRenderer : (meshRenderer = GetComponent<MeshRenderer>());

    private IHexasphereGenerator Generator => generatorSource as IHexasphereGenerator;

    // Fallback array when no IHexasphereGenerator is present
    private float[] customElevations;

    // Maps a mesh‑vertex index to all tiles that share it (set by HexTileMeshBuilder)
    private Dictionary<int, List<int>> vertexToTiles;

    // ─────────────────────────── Unity ───────────────────────────
    void Awake()
    {
        _ = MF; _ = MR;                           // ensure refs

        // Material setup
        if (planetMaterial != null)
            MR.sharedMaterial = planetMaterial;
        else if (MR.sharedMaterial == null)
        {
            var shader = Shader.Find("Custom/HexasphereURP");
            if (shader != null)
            {
                planetMaterial  = new Material(shader);
                MR.sharedMaterial = planetMaterial;
                Debug.Log("[HexasphereRenderer] Created default material with HexasphereURP shader");
            }
            else
                Debug.LogError("[HexasphereRenderer] Could not find Custom/HexasphereURP shader!");
        }

        // Auto‑detect generator on same GO if slot empty
        if (generatorSource == null)
            generatorSource = GetComponent<IHexasphereGenerator>() as MonoBehaviour;
    }

    // ─────────────────────────── Public API ───────────────────────────
    /// <summary>
    /// Inject a plain per‑tile elevation array (0‑1).  Call *before* ApplyHeightDisplacement.
    /// </summary>
    public void SetCustomElevations(float[] elevations)
    {
        customElevations = elevations;
        Debug.Log($"[HexasphereRenderer] Set custom elevations for {customElevations?.Length ?? 0} tiles");
    }

    /// <summary>Build and assign the planet/moon mesh.</summary>
    public void BuildMesh(SphericalHexGrid grid)
    {
        Report(0.05f, "Building mesh…");

        if (usePerTileBiomeData)
        {
            // Build mesh with per‑tile biome data (uses Generator if present)
            var tileBiomeIndices = new Dictionary<int, int>();

            if (Generator != null)
            {
                for (int i = 0; i < grid.TileCount; i++)
                {
                    var tileData = Generator.GetHexTileData(i);
                    if (tileData != null) tileBiomeIndices[i] = (int)tileData.biome;
                }
            }

            int biomeCount = Generator != null ? Generator.GetBiomeSettings().Count : 0;
            MF.sharedMesh = HexTileMeshBuilder.BuildWithPerTileBiomeData(
                grid, tileBiomeIndices, biomeCount, out vertexToTiles);
            Debug.Log("[HexasphereRenderer] Built mesh with per‑tile biome data");
        }
        else if (useSeparateVertices)
        {
            MF.sharedMesh = HexTileMeshBuilder.BuildWithSeparateVertices(
                grid, out _, out vertexToTiles);
            Debug.Log("[HexasphereRenderer] Using separate vertex mesh for clear tile boundaries");
        }
        else
        {
            MF.sharedMesh = HexTileMeshBuilder.Build(
                grid, out _, out vertexToTiles);
            Debug.Log("[HexasphereRenderer] Using shared vertex mesh for memory efficiency");
        }
    }

    /// <summary>Radially displace vertices based on per‑tile elevation.</summary>
    public void ApplyHeightDisplacement(float radius)
    {
        Report(0.35f, "Applying elevation…");

        Mesh m = MF.sharedMesh;
        var  v = m.vertices;

        if (Generator == null && customElevations == null)
        {
            Debug.LogError("[HexasphereRenderer] No generator *or* custom elevation array found for height displacement!");
            return;
        }

        for (int i = 0; i < v.Length; i++)
        {
            Vector3 original = v[i];
            float   elevationOffset = 0f;

            if (vertexToTiles != null && vertexToTiles.TryGetValue(i, out var tileList))
            {
                float total = 0f;
                foreach (int tileIndex in tileList)
                {
                    float tileElev = 0f;

                    if (Generator != null)
                        tileElev = Generator.GetTileElevation(tileIndex);
                    else if (customElevations != null && tileIndex < customElevations.Length)
                        tileElev = customElevations[tileIndex];

                    total += tileElev;
                }
                elevationOffset = (total / tileList.Count) * radius;
            }

            v[i] = original.normalized * (radius + elevationOffset);
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

        if (indexTex != null)  mat.SetTexture("_BiomeIndexTex",  indexTex);
        if (albedoArray != null) mat.SetTexture("_BiomeAlbedoArray", albedoArray);

        int count = Generator != null ? Generator.GetBiomeSettings().Count
                                      : albedoArray != null ? albedoArray.depth : 0;
        mat.SetInt("_BiomeCount", count);
        mat.SetFloat("_UsePerTileBiomeData", usePerTileBiomeData ? 1f : 0f);
        mat.SetFloat("_SharpBoundaries",      useSeparateVertices ? 1f : 0f);

        Report(1f, "Planet ready!");
    }

    // ─────────────────────────── Helpers ───────────────────────────
    private void Report(float pct, string msg)
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetProgress(pct);
            loadingPanel.SetStatus(msg);
        }
    }
}
