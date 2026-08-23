using System.Collections.Generic;
using UnityEngine;

/// <summary>One combined line mesh for all active national and thematic boundaries.</summary>
[DisallowMultipleComponent]
public class CampaignMapBorderRenderer : MonoBehaviour
{
    [SerializeField] private Material borderMaterial;
    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    public void Rebuild(TileSystem tiles, CampaignMapMode mode, int[] categories, Color[] colors,
        CampaignMapModePresentationData presentation)
    {
        EnsureComponents();
        if (mode == CampaignMapMode.Normal || tiles == null || categories == null)
        { mesh.Clear(); meshRenderer.enabled = false; return; }

        var vertices = new List<Vector3>();
        var vertexColors = new List<Color>();
        int count = categories.Length;
        for (int tile = 0; tile < count; tile++)
        {
            int[] neighbours = tiles.GetNeighbors(tile);
            for (int n = 0; n < neighbours.Length; n++)
            {
                int other = neighbours[n];
                if (other <= tile || other < 0 || other >= count) continue; // shared edge exactly once
                var aData = tiles.GetTileData(tile); var bData = tiles.GetTileData(other);
                bool national = SupportsNational(mode) && aData?.owner != bData?.owner;
                bool thematic = SupportsThematic(mode) && categories[tile] != categories[other];
                if (!national && !thematic) continue;

                Vector3 a = tiles.GetTileCenter(tile), b = tiles.GetTileCenter(other);
                Vector3 midpoint = (a + b) * .5f;
                Vector3 normal = midpoint.sqrMagnitude > .001f ? midpoint.normalized : Vector3.up;
                Vector3 tangent = Vector3.Cross(normal, b - a).normalized;
                float halfLength = (b - a).magnitude * .29f;
                vertices.Add(midpoint - tangent * halfLength + normal * .025f);
                vertices.Add(midpoint + tangent * halfLength + normal * .025f);
                Color c = national ? presentation.nationalBorderColor : presentation.thematicBorderColor;
                vertexColors.Add(c); vertexColors.Add(c);
            }
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(vertexColors);
        mesh.SetIndices(BuildSequential(vertices.Count), MeshTopology.Lines, 0, true);
        meshRenderer.enabled = vertices.Count > 0;
    }

    private void EnsureComponents()
    {
        if (mesh != null) return;
        meshFilter = GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
        mesh = new Mesh { name = "Campaign Map Borders" }; mesh.MarkDynamic(); meshFilter.sharedMesh = mesh;
        if (borderMaterial != null) meshRenderer.sharedMaterial = borderMaterial;
    }
    private static int[] BuildSequential(int count) { var indices = new int[count]; for (int i = 0; i < count; i++) indices[i] = i; return indices; }
    private static bool SupportsNational(CampaignMapMode mode) => mode == CampaignMapMode.PoliticalOwnership || mode == CampaignMapMode.GovernmentType || mode == CampaignMapMode.Administration || mode == CampaignMapMode.Diplomacy;
    private static bool SupportsThematic(CampaignMapMode mode) => mode == CampaignMapMode.GovernmentType || mode == CampaignMapMode.Religion || mode == CampaignMapMode.Continents || mode == CampaignMapMode.Administration;

    public static bool ShouldCreateNationalEdge(int tile, int neighbour, Civilization owner, Civilization neighbourOwner)
        => neighbour > tile && owner != neighbourOwner;
    public static bool ShouldCreateThematicEdge(int tile, int neighbour, int category, int neighbourCategory)
        => neighbour > tile && category != neighbourCategory;
}
