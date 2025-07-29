using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RawImage))]
public class MinimapController : MonoBehaviour, IPointerClickHandler
{
    [Header("References")]
    public MinimapGenerator generator;
    public RawImage minimapImage; // auto-filled in Awake
    public RectTransform marker;  // small UI Image/RectTransform child used as camera marker
    public Transform planetRoot;  // center of the planet
    public Camera mainCamera;
    public PlanetaryCameraManager cameraManager; // your existing manager; hook via Inspector

    [Header("Options")]
    public bool buildOnStart = true;
    public float zoomStep = 0.2f; // passed to camera manager
    public float clickLerpSeconds = 0.35f; // smooth camera retarget

    private RectTransform _rt;

    void Awake()
    {
        if (!minimapImage) minimapImage = GetComponent<RawImage>();
        _rt = minimapImage.rectTransform;
    }

    void Start()
    {
        if (buildOnStart && generator) {
            generator.Build();
            if (generator.IsReady)
                minimapImage.texture = generator.minimapTexture;
        }
    }

    void Update()
    {
        if (generator && generator.IsReady && minimapImage.texture == null)
            minimapImage.texture = generator.minimapTexture;

        UpdateMarker();
    }

    // --- UI Hooks (wire to your buttons) ---
    public void OnZoomInButton()  { if (cameraManager) cameraManager.ZoomBy(-zoomStep); }
    public void OnZoomOutButton() { if (cameraManager) cameraManager.ZoomBy(+zoomStep); }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!generator || !generator.IsReady) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, eventData.position, eventData.pressEventCamera, out var local))
        {
            var uv = LocalToUV(local);
            Vector3 dir = UVToDirection(uv);
            FocusCamera(dir);
        }
    }

    // --- Marker shows camera sub-observer point ---
    private void UpdateMarker()
    {
        if (!marker || !mainCamera || !planetRoot || !generator || !generator.IsReady) return;

        // For an orbital camera looking at planet center: the sub-observer point is the direction from planet center toward camera
        Vector3 center = planetRoot.position;
        Vector3 camDirFromCenter = (mainCamera.transform.position - center).normalized;
        // The point on the surface facing the camera (nadir under the camera):
        Vector3 surfacePoint = center + camDirFromCenter; // unit sphere assumption for UV calc

        Vector2 uv = WorldDirToUV(camDirFromCenter);
        Vector2 anchored = UVToLocal(uv);
        marker.anchoredPosition = anchored;
    }

    // --- Camera focus ---
    private void FocusCamera(Vector3 dir)
    {
        if (cameraManager != null) {
            cameraManager.FocusOnDirection(dir, clickLerpSeconds);
        }
        else {
            // Fallback: rotate main camera to look at point on sphere
            if (!planetRoot || !mainCamera) return;
            Vector3 center = planetRoot.position;
            Vector3 newPos = center - dir * (mainCamera.transform.position - center).magnitude;
            StartCoroutine(LerpCamera(mainCamera.transform.position, newPos, clickLerpSeconds));
        }
    }

    private System.Collections.IEnumerator LerpCamera(Vector3 from, Vector3 to, float seconds)
    {
        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, seconds);
            mainCamera.transform.position = Vector3.Slerp(from, to, t);
            mainCamera.transform.LookAt(planetRoot ? planetRoot.position : Vector3.zero);
            yield return null;
        }
    }

    // --- Coordinate conversions ---

    private Vector2 LocalToUV(Vector2 local)
    {
        var size = _rt.rect.size;
        float u = Mathf.Clamp01((local.x / size.x) + 0.5f);
        float v = Mathf.Clamp01((local.y / size.y) + 0.5f);
        return new Vector2(u, v);
    }

    private Vector2 UVToLocal(Vector2 uv)
    {
        var size = _rt.rect.size;
        float x = (uv.x - 0.5f) * size.x;
        float y = (uv.y - 0.5f) * size.y;
        return new Vector2(x, y);
    }

    private Vector3 UVToDirection(Vector2 uv)
    {
        float lon = (uv.x * 2f - 1f) * Mathf.PI;      // -pi..pi
        float lat = (uv.y - 0.5f) * Mathf.PI;         // -pi/2..pi/2
        float clat = Mathf.Cos(lat);
        return new Vector3(clat * Mathf.Cos(lon), Mathf.Sin(lat), clat * Mathf.Sin(lon)).normalized;
    }

    private Vector2 WorldDirToUV(Vector3 dir)
    {
        float lat = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
        float lon = Mathf.Atan2(dir.z, dir.x);
        float u = (lon + Mathf.PI) / (2f * Mathf.PI);
        float v = (lat + Mathf.PI * 0.5f) / Mathf.PI;
        return new Vector2(u, v);
    }
}
