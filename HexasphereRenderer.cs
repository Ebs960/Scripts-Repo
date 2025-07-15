using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class HexasphereRenderer : MonoBehaviour
{

    public IHexasphereGenerator generator;   // Drag a PlanetGenerator OR MoonGenerator
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
        if (mc != null)
            mc.sharedMesh = mf.sharedMesh;
    }

    /// <summary>Push lookup textures into the shader.</summary>

    public void PushBiomeLookups(Texture2D idx, Texture2DArray arr)
    {
        var mat = GetComponent<MeshRenderer>().sharedMaterial;

        mat.SetTexture("_BiomeIndexTex",    idx);
        mat.SetTexture("_BiomeAlbedoArray", arr);

        int biomeCnt = generator != null
                       ? generator.GetBiomeSettings().Count
                       : arr.depth;                 // fallback
        mat.SetInt("_BiomeCount", biomeCnt);
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
