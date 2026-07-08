using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Camera input for the true world-space space map. Supports arrow/WASD panning,
/// mouse-drag panning against a flat XZ plane, and optional orthographic zoom.
/// </summary>
public class SpaceMapCameraController : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float keyboardPanSpeed = 80f;
    [SerializeField] private float mouseDragSpeed = 1f;
    [SerializeField] private bool enableWasd = true;
    [SerializeField] private bool enableMouseDrag = true;
    [SerializeField] private bool enableScrollZoom = true;
    [SerializeField] private float zoomSpeed = 12f;
    [SerializeField] private float minOrthographicSize = 20f;
    [SerializeField] private float maxOrthographicSize = 260f;
    [SerializeField] private Vector2 panBounds = new Vector2(600f, 600f);

    private Plane mapPlane = new Plane(Vector3.up, Vector3.zero);
    private bool dragging;
    private Vector3 lastDragWorldPoint;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = GetComponent<Camera>();
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        if (targetCamera == null) return;
        HandleKeyboardPan();
        HandleMouseDrag();
        HandleZoom();
        ClampCamera();
    }

    public void CenterOn(Vector3 worldPosition)
    {
        if (targetCamera == null) return;
        Vector3 pos = targetCamera.transform.position;
        pos.x = worldPosition.x;
        pos.z = worldPosition.z;
        targetCamera.transform.position = pos;
        ClampCamera();
    }

    private void HandleKeyboardPan()
    {
        Vector3 pan = Vector3.zero;
        if (Input.GetKey(KeyCode.LeftArrow) || (enableWasd && Input.GetKey(KeyCode.A))) pan.x -= 1f;
        if (Input.GetKey(KeyCode.RightArrow) || (enableWasd && Input.GetKey(KeyCode.D))) pan.x += 1f;
        if (Input.GetKey(KeyCode.UpArrow) || (enableWasd && Input.GetKey(KeyCode.W))) pan.z += 1f;
        if (Input.GetKey(KeyCode.DownArrow) || (enableWasd && Input.GetKey(KeyCode.S))) pan.z -= 1f;
        if (pan.sqrMagnitude > 0.001f)
            targetCamera.transform.position += pan.normalized * keyboardPanSpeed * Time.unscaledDeltaTime;
    }

    private void HandleMouseDrag()
    {
        if (!enableMouseDrag) return;

        if (Input.GetMouseButtonDown(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()) && TryGetMouseWorldPoint(out lastDragWorldPoint))
        {
            dragging = true;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            dragging = false;
        }

        if (!dragging || !Input.GetMouseButton(0) || !TryGetMouseWorldPoint(out Vector3 currentPoint))
            return;

        Vector3 delta = (lastDragWorldPoint - currentPoint) * mouseDragSpeed;
        targetCamera.transform.position += new Vector3(delta.x, 0f, delta.z);
        TryGetMouseWorldPoint(out lastDragWorldPoint);
    }

    private void HandleZoom()
    {
        if (!enableScrollZoom || !targetCamera.orthographic) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) <= 0.001f) return;
        targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize - scroll * zoomSpeed, minOrthographicSize, maxOrthographicSize);
    }

    private bool TryGetMouseWorldPoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        if (!mapPlane.Raycast(ray, out float enter)) return false;
        worldPoint = ray.GetPoint(enter);
        return true;
    }

    private void ClampCamera()
    {
        Vector3 pos = targetCamera.transform.position;
        pos.x = Mathf.Clamp(pos.x, -panBounds.x, panBounds.x);
        pos.z = Mathf.Clamp(pos.z, -panBounds.y, panBounds.y);
        targetCamera.transform.position = pos;
    }
}
