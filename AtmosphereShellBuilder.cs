using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Utility for building an atmosphere shell mesh around a spherical grid.
/// </summary>
public static class AtmosphereShellBuilder
{
    /// <summary>
    /// Builds a spherical shell mesh for atmosphere rendering.
    /// </summary>
    /// <param name="grid">SphericalHexGrid containing vertices and triangles.</param>
    /// <param name="radius">Base radius of the planet.</param>
    /// <param name="thickness">Shell thickness (default: 0.02f).</param>
    /// <returns>Mesh representing the atmosphere shell.</returns>
    public static Mesh BuildAtmosphereShell(SphericalHexGrid grid, float radius, float thickness = 0.02f)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        verts.AddRange(grid.Vertices);     // deduped icosphere vertices
        tris.AddRange(grid.Triangles);     // original topology

        for (int i = 0; i < verts.Count; i++)
            verts[i] = verts[i].normalized * (radius + thickness);

        Mesh m = new Mesh { name = "AtmosphereShell",
                            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        return m;
    }
}
