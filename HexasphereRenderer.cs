// Assets/Scripts/Hexasphere/HexasphereRenderer.cs
using UnityEngine;

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

    // ───────────────────────────────────── Helpers ─────────────────────────────────────
    private MeshFilter   MF => meshFilter   != null ? meshFilter   : (meshFilter   = GetComponent<MeshFilter>());
    private MeshRenderer MR => meshRenderer != null ? meshRenderer : (meshRenderer = GetComponent<MeshRenderer>());

    private IHexasphereGenerator Generator => generatorSource as IHexasphereGenerator;

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
    public void BuildMesh(IcoSphereGrid grid)
    {
        Report(0.05f, "Building mesh…");
        MF.sharedMesh = HexTileMeshBuilder.Build(grid, out _);   // uv array unused here
    }

    /// <summary>Radially displace vertices (add elevation later if needed).</summary>
    public void ApplyHeightDisplacement(float radius)
    {
        Report(0.35f, "Applying elevation…");

        Mesh m = MF.sharedMesh;
        var v  = m.vertices;
        for (int i = 0; i < v.Length; i++)
            v[i] = v[i].normalized * radius;     // TODO add per‑vertex height
        m.vertices = v;
        m.RecalculateNormals();
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

        Report(1f, "Planet ready!");
    }

    // ───────────────────────────────────── Helpers ─────────────────────────────────────
    void Report(float pct, string msg)
    {
        if (loadingPanel == null) return;
        loadingPanel.SetProgress(pct);
        loadingPanel.SetStatus(msg);
    }
}
