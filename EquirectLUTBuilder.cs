using UnityEngine;

/// <summary>
/// Builds a flat LUT (pixel -> tileIndex) for a rectangular grid.
/// This LUT is the shared authority for:
/// - flat map picking (UV -> pixel -> tileIndex)
/// - texture baking (pixel -> tileIndex -> tileColor)
/// </summary>
public static class EquirectLUTBuilder
{
    /// <summary>
    /// Build a LUT where each pixel stores the nearest tile index for that map coordinate.
    /// Convention:
    /// - u in [0..1] maps to X across the flat map width
    /// - v in [0..1] maps to Z across the flat map height
    /// </summary>
    public static int[] BuildLUT(SphericalHexGrid grid, int width, int height)
    {
        if (grid == null || !grid.IsBuilt || width <= 0 || height <= 0)
            return null;

        var lut = new int[width * height];

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            float v = (y + 0.5f) / height;
            float worldZ = (v - 0.5f) * grid.MapHeight;

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float worldX = (u - 0.5f) * grid.MapWidth;
                lut[row + x] = grid.GetTileAtPosition(new Vector3(worldX, 0f, worldZ));
            }
        }

        return lut;
    }
}
