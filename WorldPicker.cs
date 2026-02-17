using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GPU-based UV/LUT picker for texture-planet mode.
/// Replaces per-tile colliders with a single collider + LUT lookup.
/// </summary>
public class WorldPicker : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public Collider flatMapCollider;

    [Header("LUT")]
    [Tooltip("Width of the LUT texture (matches texture resolution)")]
    public int lutWidth = 2048;
    [Tooltip("Height of the LUT texture (matches texture resolution)")]
    public int lutHeight = 2048;
    [Tooltip("Pixel → Tile Index Lookup Table (from EquirectLUTBuilder)")]
    public int[] lut;
    
    [Header("Map Bounds (auto-set by HexMapChunkManager)")]
    public float mapWidth = 360f;
    public float mapHeight = 180f;
    
    [Header("Debug")]
    public bool debugLog = false;

    // GPU picker runtime objects (GPU picking is the single path)
    private RenderTexture pickerRT;
    private Texture2D lutTexture;
    private Shader pickerReplacementShader;

    // Async readback state (one-frame latency)
    private int lastReadTileIndex = -1; // most recent completed result
    private bool hasPendingRequest = false;

    /// <summary>
    /// Pick a tile index from screen position using UV-based LUT lookup.
    /// This replaces per-tile collider picking with GPU-accelerated LUT lookup.
    /// </summary>
    /// <param name="screenPos">Screen position (e.g., Input.mousePosition)</param>
    /// <param name="tileIndex">Output tile index (-1 if not found)</param>
    /// <param name="hitWorldPos">Output world position of hit point</param>
    /// <returns>True if a tile was picked, false otherwise</returns>
    public bool TryPickTileIndex(Vector2 screenPos, out int tileIndex, out Vector3 hitWorldPos)
    {
        tileIndex = -1;
        hitWorldPos = Vector3.zero;

        if (targetCamera == null) targetCamera = Camera.main;
        // HDRP / some scenes may not tag a camera as MainCamera. Fall back to any camera so picking still works.
        if (targetCamera == null) targetCamera = FindAnyObjectByType<Camera>();
        if (targetCamera == null) return false;
        if (lut == null || lut.Length == 0)
        {
            if (debugLog) Debug.LogWarning("[WorldPicker] LUT is null or empty");
            return false;
        }
        // GPU picking (pixel-perfect). We render the scene into an offscreen RT using a replacement
        // shader that samples the LUT. We then request an async readback of the pixel at the
        // cursor position and return the last completed readback result (one-frame latency).
        EnsureGPUPickerSetup();
        if (pickerReplacementShader == null || lutTexture == null)
        {
            if (debugLog) Debug.LogError("[WorldPicker] GPU picker not set up (missing shader or LUT texture).");
            return false;
        }

        int rtW = Mathf.Max(1, targetCamera.pixelWidth);
        int rtH = Mathf.Max(1, targetCamera.pixelHeight);
        if (pickerRT == null || pickerRT.width != rtW || pickerRT.height != rtH)
        {
            if (pickerRT != null) pickerRT.Release();
            pickerRT = new RenderTexture(rtW, rtH, 16, RenderTextureFormat.ARGB32);
            pickerRT.Create();
        }

        // Temporarily render the target camera into the picker RT using the replacement shader.
        RenderTexture prevRT = targetCamera.targetTexture;
        targetCamera.targetTexture = pickerRT;

        try
        {
            targetCamera.RenderWithShader(pickerReplacementShader, "");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldPicker] Exception while rendering replacement shader: {ex}");
            targetCamera.targetTexture = prevRT;
            return false;
        }

        targetCamera.targetTexture = prevRT;

        // Pixel coordinates inside RT
        int px = Mathf.Clamp(Mathf.FloorToInt(screenPos.x * (rtW / (float)targetCamera.pixelWidth)), 0, rtW - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(screenPos.y * (rtH / (float)targetCamera.pixelHeight)), 0, rtH - 1);

        // Issue an async readback for the RT. Capture px,py and rt dims for decoding in callback.
        int capturePx = px;
        int capturePy = py;
        int captureW = rtW;
        int captureH = rtH;
        hasPendingRequest = true;
        AsyncGPUReadback.Request(pickerRT, 0, request =>
        {
            hasPendingRequest = false;
            if (request.hasError)
            {
                if (debugLog) Debug.LogError("[WorldPicker] AsyncGPUReadback request error.");
                return;
            }

            var data = request.GetData<Color32>();
            int idxPos = capturePy * captureW + capturePx;
            if (idxPos < 0 || idxPos >= data.Length)
            {
                if (debugLog) Debug.LogWarning($"[WorldPicker] Readback index out of range: {idxPos} (len={data.Length})");
                return;
            }

            Color32 rc = data[idxPos];
            Color rcf = new Color(rc.r / 255f, rc.g / 255f, rc.b / 255f, rc.a / 255f);
            int decoded = DecodeRGBA32ToInt(rcf);
            lastReadTileIndex = decoded;
            if (debugLog) Debug.Log($"[WorldPicker][Async] completed px={capturePx} py={capturePy} color={rc} tileIndex={decoded}");
        });

        // Return the last completed readback result (one-frame latency). hitWorldPos is not available from GPU pass.
        tileIndex = lastReadTileIndex;
        if (debugLog) Debug.Log($"[WorldPicker] Returning lastReadTileIndex={lastReadTileIndex} (one-frame latency)");
        return tileIndex >= 0;
    }

    private void EnsureGPUPickerSetup()
    {
        if (pickerReplacementShader == null)
        {
            pickerReplacementShader = Shader.Find("Hidden/TileIndexPicker");
            if (pickerReplacementShader == null)
            {
                Debug.LogError("[WorldPicker] Replacement shader 'Hidden/TileIndexPicker' not found. GPU picking disabled.");
                return;
            }
        }

        // Build or update the LUT texture on GPU
        if (lutTexture == null || lutTexture.width != lutWidth || lutTexture.height != lutHeight)
        {
            if (lut == null || lut.Length == 0)
            {
                Debug.LogError("[WorldPicker] LUT data missing when building LUT texture.");
                return;
            }

            lutTexture = new Texture2D(lutWidth, lutHeight, TextureFormat.RGBA32, false);
            lutTexture.filterMode = FilterMode.Point;
            lutTexture.wrapMode = TextureWrapMode.Repeat;
            var colors = new Color32[lutWidth * lutHeight];
            for (int i = 0; i < lut.Length && i < colors.Length; i++)
            {
                int v = lut[i];
                byte r = (byte)(v & 0xFF);
                byte g = (byte)((v >> 8) & 0xFF);
                byte b = (byte)((v >> 16) & 0xFF);
                byte a = (byte)((v >> 24) & 0xFF);
                colors[i] = new Color32(r, g, b, a);
            }
            lutTexture.SetPixels32(colors);
            lutTexture.Apply(false, false);

            // expose to shader as global
            Shader.SetGlobalTexture("_TileIndexLUT", lutTexture);
        }
    }

    private static int DecodeRGBA32ToInt(Color c)
    {
        int r = Mathf.RoundToInt(c.r * 255f) & 0xFF;
        int g = Mathf.RoundToInt(c.g * 255f) & 0xFF;
        int b = Mathf.RoundToInt(c.b * 255f) & 0xFF;
        int a = Mathf.RoundToInt(c.a * 255f) & 0xFF;
        int val = r | (g << 8) | (b << 16) | (a << 24);
        // treat 0 as invalid
        if (val == 0) return -1;
        return val;
    }
}
