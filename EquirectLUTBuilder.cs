using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Builds a flat LUT (pixel -> tileIndex) for a rectangular grid.
/// This LUT is the shared authority for:
/// - flat map picking (UV -> pixel -> tileIndex)
/// - texture baking (pixel -> tileIndex -> tileColor)
/// </summary>
public static class EquirectLUTBuilder
{
    /// <summary>
    /// Burst-compiled parallel job that builds the entire LUT across all CPU cores.
    /// Each Execute(y) processes one full row of pixels.
    /// </summary>
    [BurstCompile]
    private struct BuildLUTJob : IJobParallelFor
    {
        public HexGridLookupData grid;
        public int lutWidth;
        public float invLutWidth;
        public float invLutHeight;
        public float mapWidth;
        public float mapHeight;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> lut;

        public void Execute(int y)
        {
            int rowStart = y * lutWidth;
            float v = (y + 0.5f) * invLutHeight;
            float worldZ = (v - 0.5f) * mapHeight;

            for (int x = 0; x < lutWidth; x++)
            {
                float u = (x + 0.5f) * invLutWidth;
                float worldX = (u - 0.5f) * mapWidth;
                lut[rowStart + x] = TileAtPosition(worldX, worldZ);
            }
        }

        private int TileAtPosition(float posX, float posZ)
        {
            if (posZ < grid.minZCorner || posZ > grid.maxZCorner)
                return -1;

            float lx = posX - grid.offsetX;
            float lz = posZ - grid.offsetZ;

            float qf = grid.sqrt3over3_div_s * lx - grid.inv3_div_s * lz;
            float rf = grid.twothirds_div_s * lz;

            float xf = qf;
            float zf = rf;
            float yf = -xf - zf;
            int xi = (int)math.round(xf);
            int yi = (int)math.round(yf);
            int zi = (int)math.round(zf);

            float xDiff = math.abs(xi - xf);
            float yDiff = math.abs(yi - yf);
            float zDiff = math.abs(zi - zf);

            if (xDiff > yDiff && xDiff > zDiff)
                xi = -yi - zi;
            else if (yDiff > zDiff)
                yi = -xi - zi;
            else
                zi = -xi - yi;

            int row = zi;
            int col = xi + ((row & 1) == 0 ? (row / 2) : ((row + 1) / 2));
            col = ((col % grid.gridWidth) + grid.gridWidth) % grid.gridWidth;

            if (row < 0 || row >= grid.gridHeight)
                return -1;

            int idx = row * grid.gridWidth + col;
            return (idx >= 0 && idx < grid.tileCount) ? idx : -1;
        }
    }

    /// <summary>
    /// Build the LUT using Burst-compiled parallel jobs across all CPU cores.
    /// Typically 10-20x faster than the coroutine/batched path.
    /// </summary>
    public static int[] BuildLUTBurst(HexGrid grid, int width, int height)
    {
        if (grid == null || !grid.IsBuilt || width <= 0 || height <= 0)
            return null;

        var gridData = grid.GetLookupData();
        var lutNative = new NativeArray<int>(width * height, Allocator.TempJob);

        var job = new BuildLUTJob
        {
            grid = gridData,
            lutWidth = width,
            invLutWidth = 1f / width,
            invLutHeight = 1f / height,
            mapWidth = grid.MapWidth,
            mapHeight = grid.MapHeight,
            lut = lutNative,
        };

        job.Schedule(height, 4).Complete();

        var result = lutNative.ToArray();
        lutNative.Dispose();
        return result;
    }

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

        var lut = ArrayPoolUtils.RentInt(width * height);

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
    public static System.Collections.IEnumerator BuildLUTBatched(
        HexGrid grid, int width, int height, int rowsPerBatch, System.Action<int[]> onComplete)
    {
        if (grid == null || !grid.IsBuilt || width <= 0 || height <= 0)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        var lut = ArrayPoolUtils.RentInt(width * height);

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
