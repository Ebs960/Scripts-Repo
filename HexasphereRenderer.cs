using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class HexasphereRenderer : MonoBehaviour
{
    public Material planetMaterial;                 // assign a material using HexasphereShader
    MeshFilter mf; Vector2[] tileUV;
    MeshCollider mc;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mc = GetComponent<MeshCollider>();
    }

    public void BuildMesh(IcoSphereGrid grid)
    {
        mf.sharedMesh = HexTileMeshBuilder.Build(grid, out tileUV);
        var mr = GetComponent<MeshRenderer>();
        mr.sharedMaterial = planetMaterial;
        if (mc != null)
            mc.sharedMesh = mf.sharedMesh;
    }

    /// <summary>Push lookup textures into the shader.</summary>
    public void PushBiomeLookups(Texture2D biomeIndex, Texture2DArray biomeAlbedoArray, int biomeCount)
    {
        planetMaterial.SetTexture("_BiomeIndexTex", biomeIndex);
        planetMaterial.SetTexture("_BiomeAlbedoArray", biomeAlbedoArray);
        planetMaterial.SetInt("_BiomeCount", biomeCount);
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
        if (mc != null)
            mc.sharedMesh = m;
    }
}
