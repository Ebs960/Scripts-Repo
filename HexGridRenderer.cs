using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexGridRenderer : MonoBehaviour
{
    public Material lineMaterial;
    public float lineWidth = 0.02f;

    private LineRenderer[] lineRenderers;

    public void RenderGrid(IcoSphereGrid grid, float radius)
    {
        ClearGrid();

        if (grid == null || grid.tileCenters == null || grid.neighbors == null)
        {
            Debug.LogWarning("[HexGridRenderer] Invalid grid.");
            return;
        }

        lineRenderers = new LineRenderer[grid.TileCount];
        for (int i = 0; i < grid.TileCount; i++)
        {
            Vector3 center = grid.tileCenters[i];
            foreach (int neighborIndex in grid.neighbors[i])
            {
                Vector3 neighborCenter = grid.tileCenters[neighborIndex];
                Vector3[] points = new Vector3[] { center.normalized * radius, neighborCenter.normalized * radius };
                GameObject lineObj = new GameObject("HexEdge_" + i + "_" + neighborIndex);
                lineObj.transform.SetParent(transform);
                LineRenderer lr = lineObj.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.SetPositions(points);
                lr.material = lineMaterial;
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth;
                lr.useWorldSpace = false;
                lr.loop = false;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.widthMultiplier = 1f;
            }
        }
    }

    public void ClearGrid()
    {
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
    }
}