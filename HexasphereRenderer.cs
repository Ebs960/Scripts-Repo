using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexasphereRenderer : MonoBehaviour
{
    public PlanetGenerator generator;               // assign at runtime
    public Material planetMaterial;                 // assign a material using HexasphereShader
    MeshFilter mf; Vector2[] tileUV;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
    }

    public void BuildMesh(IcoSphereGrid grid)
    {
        mf.sharedMesh = HexTileMeshBuilder.Build(grid, out tileUV);
        var mr = GetComponent<MeshRenderer>();
        mr.sharedMaterial = planetMaterial;
    }

    /// <summary>Push a lookup texture & tile count into the shader.</summary>
    public void PushBiomeLookups(Texture2D biomeIndex, Texture2D biomeAlbedoArray)
    {
        planetMaterial.SetTexture("_BiomeIndexTex", biomeIndex);
        planetMaterial.SetTexture("_BiomeAlbedoArray", biomeAlbedoArray);
        planetMaterial.SetInt("_BiomeCount", generator.biomeSettings.Count);
    }

    public void ApplyHeightDisplacement(float radius)
    {
        Mesh m = mf.sharedMesh;
        var verts = m.vertices;
        for (int i = 0; i < verts.Length; i++)
            verts[i] = verts[i].normalized * radius;      // simple sphere (later: add elevation)
        m.vertices = verts;
        m.RecalculateNormals();
        mf.sharedMesh = m;
    }
}
