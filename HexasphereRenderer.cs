// HexasphereRenderer.cs
using UnityEngine;

/// <summary>
/// Generates the visible planet/moon mesh and pushes biome textures to its material.
/// Works with any component that implements <see cref="IHexasphereGenerator"/>
/// (e.g. PlanetGenerator, MoonGenerator, …).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexasphereRenderer : MonoBehaviour
{
    [Header("Material")]
    [Tooltip("Material that uses HexasphereShader. If left null, the existing material on the MeshRenderer is used.")]
    public Material planetMaterial;

    [Header("Generator Source")]
    [Tooltip("Drag a PlanetGenerator, MoonGenerator, or any MonoBehaviour that implements IHexasphereGenerator.")]
    [SerializeField] public MonoBehaviour generatorSource;   // <-- Unity shows this slot

    // helper cast – returns null if the dragged component doesn't implement the interface
    private IHexasphereGenerator Generator => generatorSource as IHexasphereGenerator;

    // cached components
    MeshFilter   mf;
    MeshRenderer mr;

    public void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();

        // Assign material if provided
        if (planetMaterial != null)
            mr.sharedMaterial = planetMaterial;

        // Auto‑detect a generator on the same GameObject if the field is still empty
       if (generatorSource == null)
        {
     // Try to find a component on this GO that implements the interface
     var gen = GetComponent<IHexasphereGenerator>();
    generatorSource = gen as MonoBehaviour;   // cast back to MonoBehaviour
    }
    }

    /// <summary>Builds the planetary mesh and stores it in the MeshFilter.</summary>
    public void BuildMesh(IcoSphereGrid grid)
    {
        mf.sharedMesh = HexTileMeshBuilder.Build(grid, out _);
    }

    /// <summary>
    /// Pushes the per‑tile biome index map and the biome albedo Texture2DArray to the material.
    /// </summary>
    public void PushBiomeLookups(Texture2D indexTex, Texture2DArray albedoArray)
    {
        var mat = mr.sharedMaterial;

        if (indexTex   != null) mat.SetTexture("_BiomeIndexTex",    indexTex);
        if (albedoArray != null) mat.SetTexture("_BiomeAlbedoArray", albedoArray);

        int biomeCount =
            Generator != null ? Generator.GetBiomeSettings().Count :
            albedoArray != null ? albedoArray.depth :
            0;

        mat.SetInt("_BiomeCount", biomeCount);
    }

    /// <summary>Simple radial displacement – call after you know each tile's height.</summary>
    public void ApplyHeightDisplacement(float radius)
    {
        Mesh mesh = mf.sharedMesh;
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
            verts[i] = verts[i].normalized * radius;     // TODO add per‑vertex elevation here
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mf.sharedMesh = mesh;
    }
}
