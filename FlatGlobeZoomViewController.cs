using UnityEngine;

/// <summary>
/// Handles flat map camera controls (pan/zoom/orbit) with no mode transitions.
/// </summary>
public class FlatGlobeZoomViewController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;

    [Header("Flat Camera - Position")]
    [SerializeField] private bool controlCameraInFlatMode = true;
    [SerializeField] private float flatCameraMinHeight = 20f;
    [SerializeField] private float flatCameraMaxHeight = 200f;
    [SerializeField] private float flatCameraStartHeight = 80f;

    [Header("Flat Camera - Pan (WASD / Arrow Keys)")]
    [SerializeField] private float flatPanSpeed = 60f;

    [Header("Flat Camera - Orbit/Pivot (Right Mouse)")]
    [SerializeField] private bool enableOrbitRotation = true;
    [SerializeField] private float orbitSensitivity = 0.3f;
    [SerializeField] private float minPitch = 20f;
    [SerializeField] private float maxPitch = 90f;

    [Header("Flat Camera - Zoom")]
    [SerializeField] private float flatZoomScrollSpeed = 20f;

    [Header("Flat Camera - Mouse Drag Pan (Middle Mouse)")]
    [SerializeField] private bool enableMouseDragPan = true;
    [SerializeField] private float mouseDragPanSpeed = 0.5f;

    [Header("Runtime (Read-only)")]
    [SerializeField] private float currentZoomDistance;

    // Flat camera state
    private Vector3 _flatLookAtPoint;
    private float _flatYaw;
    private float _flatPitch = 60f;
    private float _flatHeight;
    private Vector3 _lastMousePos;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        _flatHeight = flatCameraStartHeight;
        _flatPitch = 60f;
    }

    private void Start()
    {
        _flatLookAtPoint = Vector3.zero;
        currentZoomDistance = _flatHeight;
        PositionFlatCamera();
    }

    private void Update()
    {
        if (!controlCameraInFlatMode || targetCamera == null) return;

        UpdateFlatCamera();
    }

    /// <summary>
    /// Immediately position the camera for flat map view based on current flat camera state.
    /// </summary>
    private void PositionFlatCamera()
    {
        if (targetCamera == null) return;

        float pitchRad = _flatPitch * Mathf.Deg2Rad;
        float yawRadFinal = _flatYaw * Mathf.Deg2Rad;

        float horizontalDist = _flatHeight / Mathf.Tan(pitchRad);
        float offsetX = -Mathf.Sin(yawRadFinal) * horizontalDist;
        float offsetZ = -Mathf.Cos(yawRadFinal) * horizontalDist;

        Vector3 camPos = new Vector3(
            _flatLookAtPoint.x + offsetX,
            _flatHeight,
            _flatLookAtPoint.z + offsetZ
        );

        targetCamera.transform.position = camPos;
        targetCamera.transform.LookAt(_flatLookAtPoint);
    }

    private void UpdateFlatCamera()
    {
        float dt = Time.deltaTime;

        // Keyboard pan
        Vector3 pan = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) pan.z += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) pan.z -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) pan.x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) pan.x += 1f;

        if (pan.sqrMagnitude > 0f)
        {
            pan.Normalize();
            _flatLookAtPoint += pan * flatPanSpeed * dt;
        }

        // Mouse drag pan
        if (enableMouseDragPan)
        {
            if (Input.GetMouseButtonDown(2))
                _lastMousePos = Input.mousePosition;
            else if (Input.GetMouseButton(2))
            {
                Vector3 delta = Input.mousePosition - _lastMousePos;
                _flatLookAtPoint += new Vector3(-delta.x, 0f, -delta.y) * mouseDragPanSpeed;
                _lastMousePos = Input.mousePosition;
            }
        }

        // Orbit rotation
        if (enableOrbitRotation && Input.GetMouseButton(1))
        {
            float yawDelta = Input.GetAxis("Mouse X") * orbitSensitivity;
            float pitchDelta = -Input.GetAxis("Mouse Y") * orbitSensitivity;

            _flatYaw += yawDelta;
            _flatPitch = Mathf.Clamp(_flatPitch + pitchDelta, minPitch, maxPitch);
        }

        // Zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _flatHeight = Mathf.Clamp(_flatHeight - scroll * flatZoomScrollSpeed, flatCameraMinHeight, flatCameraMaxHeight);
            currentZoomDistance = _flatHeight;
        }

        PositionFlatCamera();
    }
}
