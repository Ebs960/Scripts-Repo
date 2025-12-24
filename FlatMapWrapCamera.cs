using UnityEngine;

/// <summary>
/// Presentation-only camera wrap for the flat equirectangular map.
/// Teleports the camera in X by +/- mapWidth to create infinite-scroll wrapping (Civ-style).
/// 
/// IMPORTANT: This does NOT move/teleport units or change gameplay state.
/// </summary>
public class FlatMapWrapCamera : MonoBehaviour
{
    [Tooltip("Flat map texture renderer that defines mapWidth.")]
    [SerializeField] private FlatMapTextureRenderer flatMap;

    [Tooltip("Enable wrap behavior. Typically enabled only in flat mode.")]
    [SerializeField] private bool wrapEnabled = true;

    [Tooltip("Optional center X for wrap bounds. 0 means map centered at world X=0.")]
    [SerializeField] private float centerX = 0f;

    private void LateUpdate()
    {
        if (!wrapEnabled || flatMap == null || !flatMap.IsBuilt) return;

        float mapWidth = flatMap.MapWidth;
        if (mapWidth <= 0.0001f) return;

        float half = mapWidth * 0.5f;
        float minX = centerX - half;
        float maxX = centerX + half;

        var p = transform.position;
        if (p.x > maxX) p.x -= mapWidth;
        else if (p.x < minX) p.x += mapWidth;
        transform.position = p;
    }

    public void SetWrapEnabled(bool enabled) => wrapEnabled = enabled;
}

