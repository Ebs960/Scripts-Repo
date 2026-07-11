using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PlanetVisualSnapshot
{
    public int planetId;
    public int subdivisions;
    public Vector3[] tileDirections;
    public int[] biomeIds;
    public float[] elevations;
    public bool[] waterTiles;
    public Color atmosphereColor = Color.clear;
    public bool hasClouds;
    public bool hasRings;
}

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexaspherePlanetPreview : MonoBehaviour
{
    public PlanetVisualSnapshot snapshot;
    public float rotationDegreesPerSecond = 3f;
    public int[] previewPolygonToSourceTile;
    private void Update() { transform.Rotate(Vector3.up, rotationDegreesPerSecond * Time.deltaTime, Space.Self); }
    public void ApplySnapshot(PlanetVisualSnapshot visualSnapshot, int previewSubdivisions)
    {
        snapshot = visualSnapshot;
        var builder = new HexaspherePlanetPreviewBuilder();
        var mesh = builder.Build(snapshot, previewSubdivisions, out previewPolygonToSourceTile);
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}

public class HexaspherePlanetPreviewBuilder
{
    private readonly Dictionary<string, int[]> mappingCache = new Dictionary<string, int[]>();
    public Mesh Build(PlanetVisualSnapshot snapshot, int previewSubdivisions, out int[] previewToSource)
    {
        Mesh mesh = new Mesh { name = "Hexasphere Planet Preview" };
        if (snapshot == null || snapshot.tileDirections == null || snapshot.tileDirections.Length == 0) { previewToSource = Array.Empty<int>(); return mesh; }
        Vector3[] vertices = new Vector3[snapshot.tileDirections.Length]; Color[] colors = new Color[vertices.Length]; int[] indices = new int[Mathf.Max(0, vertices.Length - 2) * 3];
        for (int i = 0; i < vertices.Length; i++) { float elevation = snapshot.elevations != null && i < snapshot.elevations.Length ? snapshot.elevations[i] : 0f; vertices[i] = snapshot.tileDirections[i].normalized * (1f + elevation * 0.04f); colors[i] = GetBiomeColor(snapshot, i); }
        for (int i = 0, tri = 0; i < vertices.Length - 2; i++) { indices[tri++] = 0; indices[tri++] = i + 1; indices[tri++] = i + 2; }
        mesh.vertices = vertices; mesh.triangles = indices; mesh.colors = colors; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        previewToSource = BuildPreviewMapping(snapshot, vertices, previewSubdivisions); return mesh;
    }
    private int[] BuildPreviewMapping(PlanetVisualSnapshot snapshot, Vector3[] previewDirections, int previewSubdivisions)
    {
        string key = snapshot.planetId + ":" + snapshot.subdivisions + ":" + previewSubdivisions + ":" + previewDirections.Length; if (mappingCache.TryGetValue(key, out var cached)) return cached;
        int[] map = new int[previewDirections.Length]; for (int i = 0; i < map.Length; i++) map[i] = FindNearest(snapshot.tileDirections, previewDirections[i].normalized); mappingCache[key] = map; return map;
    }
    private int FindNearest(Vector3[] source, Vector3 direction) { int best = 0; float bestDot = -2f; for (int i = 0; i < source.Length; i++) { float dot = Vector3.Dot(direction, source[i].normalized); if (dot > bestDot) { bestDot = dot; best = i; } } return best; }
    private Color GetBiomeColor(PlanetVisualSnapshot snapshot, int i) { if (snapshot.waterTiles != null && i < snapshot.waterTiles.Length && snapshot.waterTiles[i]) return new Color(0.05f, 0.22f, 0.65f); int biome = snapshot.biomeIds != null && i < snapshot.biomeIds.Length ? snapshot.biomeIds[i] : 0; return Color.HSVToRGB(Mathf.Repeat(biome * 0.137f, 1f), 0.55f, 0.8f); }
}
