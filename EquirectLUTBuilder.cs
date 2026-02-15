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
    public static int[] BuildLUT(HexGrid grid, int width, int height)
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

    /// <summary>
    /// Batched version of BuildLUT that yields between row batches to avoid blocking the main thread.
    /// Processes <paramref name="rowsPerBatch"/> rows per frame, then yields.
    /// Caller must run this via StartCoroutine().
    /// </summary>
    /// <param name="grid">Hex grid to query tile positions from</param>
    /// <param name="width">LUT width in pixels</param>
    /// <param name="height">LUT height in pixels</param>
    /// <param name="rowsPerBatch">Number of rows to process per frame (higher = faster but chunkier frames)</param>
    /// <param name="onComplete">Callback invoked with the completed LUT (or null on failure)</param>
    public static System.Collections.IEnumerator BuildLUTBatched(
        HexGrid grid, int width, int height, int rowsPerBatch, System.Action<int[]> onComplete)
    {
        if (grid == null || !grid.IsBuilt || width <= 0 || height <= 0)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        var lut = new int[width * height];

        // Pre-compute constants outside the loop for efficiency
        float invWidth = 1f / width;
        float invHeight = 1f / height;
        float mapW = grid.MapWidth;
        float mapH = grid.MapHeight;

        int rowsProcessed = 0;
        while (rowsProcessed < height)
        {
            int rowsThisBatch = Mathf.Min(rowsPerBatch, height - rowsProcessed);

            for (int y = rowsProcessed; y < rowsProcessed + rowsThisBatch; y++)
            {
                int row = y * width;
                float v = (y + 0.5f) * invHeight;
                float worldZ = (v - 0.5f) * mapH;

                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) * invWidth;
                    float worldX = (u - 0.5f) * mapW;
                    lut[row + x] = grid.GetTileAtPosition(new Vector3(worldX, 0f, worldZ));
                }
            }

            rowsProcessed += rowsThisBatch;
            yield return null;
        }

        onComplete?.Invoke(lut);
    }
}
