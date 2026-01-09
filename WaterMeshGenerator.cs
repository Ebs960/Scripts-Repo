using UnityEngine;
using System.Collections.Generic;

public class WaterMeshGenerator : MonoBehaviour
{
    public Material waterMaterial;
    [Tooltip("Height of the water surface above the flat map plane.")]
    public float waterSurfaceElevation = 0.12f;
    [Tooltip("Inset per tile to avoid overlap artifacts between water and land.")]
    public float tileInset = 0.02f;

    GameObject waterObject;

    public void Generate(PlanetGenerator planetGen)
    {
        if (planetGen == null || planetGen.Grid == null || !planetGen.Grid.IsBuilt)
        {
            Debug.LogWarning("[WaterMeshGenerator] Missing planet generator grid.");
            return;
        }

        if (waterObject != null)
        {
            Destroy(waterObject);
        }

        waterObject = new GameObject("WaterMesh");
        waterObject.name = "WaterMesh";
        waterObject.transform.SetParent(transform, false);
        waterObject.transform.localPosition = new Vector3(0f, waterSurfaceElevation, 0f);

        var meshFilter = waterObject.AddComponent<MeshFilter>();
        var meshRenderer = waterObject.AddComponent<MeshRenderer>();
        if (waterMaterial != null)
            meshRenderer.material = waterMaterial;

        BuildWaterMesh(planetGen, meshFilter);
    }

    private void BuildWaterMesh(PlanetGenerator planetGen, MeshFilter meshFilter)
    {
        var grid = planetGen.Grid;
        float tileWidth = grid.MapWidth / grid.Width;
        float tileHeight = grid.MapHeight / grid.Height;
        float insetX = tileWidth * tileInset;
        float insetZ = tileHeight * tileInset;

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        for (int i = 0; i < grid.TileCount; i++)
        {
            var tileData = planetGen.GetHexTileData(i);
            if (tileData == null) continue;

            if (!IsWaterBiome(tileData.biome)) continue;

            Vector3 center = grid.tileCenters[i];
            float halfW = tileWidth * 0.5f - insetX;
            float halfH = tileHeight * 0.5f - insetZ;

            int baseIndex = vertices.Count;
            vertices.Add(new Vector3(center.x - halfW, 0f, center.z - halfH));
            vertices.Add(new Vector3(center.x + halfW, 0f, center.z - halfH));
            vertices.Add(new Vector3(center.x + halfW, 0f, center.z + halfH));
            vertices.Add(new Vector3(center.x - halfW, 0f, center.z + halfH));

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }

        var mesh = new Mesh
        {
            name = "WaterMesh_Flat"
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshFilter.sharedMesh = mesh;
    }

    private bool IsWaterBiome(Biome biome)
    {
        return biome == Biome.Coast ||
               biome == Biome.Seas ||
               biome == Biome.Ocean ||
               biome == Biome.River;
    }
}
