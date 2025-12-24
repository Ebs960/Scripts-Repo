using UnityEngine;

/// <summary>
/// UV/LUT-based picker for texture-planet mode.
/// - Flat: raycast against ONE quad collider, use hit.textureCoord -> LUT
/// - Globe: raycast against ONE sphere collider, use hit.textureCoord -> LUT
/// 
/// This replaces per-tile colliders and TileIndexHolder picking when UseTexturePlanet is enabled.
/// </summary>
public class WorldPicker : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public Collider flatMapCollider;
    public Collider globeCollider;

    [Header("LUT")]
    public int lutWidth = 2048;
    public int lutHeight = 1024;
    public int[] lut;

    [Header("Mode")]
    [Range(0f, 1f)]
    public float morph; // 0 = flat, 1 = globe

    public bool TryPickTileIndex(Vector2 screenPos, out int tileIndex, out Vector3 hitWorldPos)
    {
        tileIndex = -1;
        hitWorldPos = Vector3.zero;

        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return false;
        if (lut == null || lut.Length != lutWidth * lutHeight) return false;

        var ray = targetCamera.ScreenPointToRay(screenPos);
        Collider col = (morph < 0.5f) ? flatMapCollider : globeCollider;
        if (col == null) return false;

        if (!col.Raycast(ray, out var hit, 50000f))
            return false;

        Vector2 uv = hit.textureCoord;
        // Wrap U for equirect repeat; clamp V
        uv.x = Mathf.Repeat(uv.x, 1f);
        uv.y = Mathf.Clamp01(uv.y);

        int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * lutWidth), 0, lutWidth - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * lutHeight), 0, lutHeight - 1);
        int idx = y * lutWidth + x;
        if (idx < 0 || idx >= lut.Length) return false;

        tileIndex = lut[idx];
        hitWorldPos = hit.point;
        return tileIndex >= 0;
    }
}

