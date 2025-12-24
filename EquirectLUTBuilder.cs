using UnityEngine;

/// <summary>
/// Builds an equirectangular LUT (pixel -> tileIndex) for a spherical hex grid.
/// This LUT is the shared authority for:
/// - flat map picking (UV -> pixel -> tileIndex)
/// - globe picking (sphere hit UV -> pixel -> tileIndex)
/// - texture baking (pixel -> tileIndex -> tileColor)
/// </summary>
public static class EquirectLUTBuilder
{
    /// <summary>
    /// Build a LUT where each pixel stores the nearest tile index for that direction.
    /// Convention:
    /// - u in [0..1] maps to lon in [-PI..PI]
    /// - v in [0..1] maps to lat in [-PI/2..PI/2]
    /// </summary>
    public static int[] BuildLUT(SphericalHexGrid grid, int width, int height)
    {
        if (grid == null || !grid.IsBuilt || width <= 0 || height <= 0)
            return null;

        var lut = new int[width * height];

        // Precompute trig tables
        float[] sinLat = new float[height];
        float[] cosLat = new float[height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float lat = (v * Mathf.PI) - (Mathf.PI * 0.5f);
            sinLat[y] = Mathf.Sin(lat);
            cosLat[y] = Mathf.Cos(lat);
        }

        float[] sinLon = new float[width];
        float[] cosLon = new float[width];
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float lon = (u * 2f * Mathf.PI) - Mathf.PI;
            sinLon[x] = Mathf.Sin(lon);
            cosLon[x] = Mathf.Cos(lon);
        }

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            float slat = sinLat[y];
            float clat = cosLat[y];

            for (int x = 0; x < width; x++)
            {
                float slon = sinLon[x];
                float clon = cosLon[x];

                // Standard spherical direction for lon/lat
                Vector3 dir = new Vector3(
                    clat * clon,
                    slat,
                    clat * slon
                );

                lut[row + x] = grid.GetTileAtPosition(dir);
            }
        }

        return lut;
    }
}

