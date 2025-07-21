using System.Collections;
using UnityEngine;

public static class TextureUtils
{
    /// <summary>
    /// Read pixels from a RenderTexture into a Texture2D in small vertical slices
    /// yielding between slices to keep the main thread responsive.
    /// </summary>
    public static IEnumerator ReadPixelsAsync(RenderTexture src, Texture2D dest, int rowsPerStep = 4)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = src;
        int width = dest.width;
        int height = dest.height;
        for (int y = 0; y < height; y += rowsPerStep)
        {
            int stepHeight = Mathf.Min(rowsPerStep, height - y);
            dest.ReadPixels(new Rect(0, y, width, stepHeight), 0, y);
            if (y % (rowsPerStep * 1) == 0)
                yield return null;
        }
        dest.Apply();
        RenderTexture.active = prev;
    }

    /// <summary>
    /// Immediately read the entire RenderTexture into the destination Texture2D.
    /// Ensures the correct render target is active while reading pixels.
    /// </summary>
    public static void ReadPixelsImmediate(RenderTexture src, Texture2D dest)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = src;
        dest.ReadPixels(new Rect(0, 0, dest.width, dest.height), 0, 0);
        dest.Apply();
        RenderTexture.active = prev;
    }
}
