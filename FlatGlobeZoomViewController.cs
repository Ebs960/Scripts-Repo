using UnityEngine;

/// <summary>
/// Handles zoom-based transitions between Flat and Globe modes, and flat camera controls.
/// 
/// IMPORTANT: This script does NOT control visibility directly.
/// It delegates to MapViewController (the single authority) for mode switching.
/// 
/// Responsibilities:
/// - Monitor zoom level and request mode changes via MapViewController
/// - Control flat camera (orbit/pan/zoom) when in flat mode
/// </summary>
public class FlatGlobeZoomViewController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FlatMapRenderer flatMapRenderer;
    [SerializeField] private PlanetaryCameraManager planetaryCamera;
    [SerializeField] private Camera targetCamera;

    [Header("Zoom Thresholds")]
    [Tooltip("Camera distance at which we switch TO flat mode (zooming in).")]
    [SerializeField] private float flatZoomDistance = 2f;

    [Tooltip("Camera distance at which we switch TO globe mode (zooming out).")]
    [SerializeField] private float globeZoomDistance = 30f;

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

    // Globe camera cache
    private float _cachedGlobeFov;
    private bool _cachedGlobeOrtho;
    private float _cachedGlobeOrthoSize;

    // Subscription tracking to prevent double subscription
    private bool _isSubscribedToModeChanged;

    private void Awake()
    {
        if (targetCamera == null) 
            targetCamera = Camera.main;

        if (planetaryCamera != null)
        {
            if (Mathf.Approximately(flatZoomDistance, 2f) && !Mathf.Approximately(planetaryCamera.minOrbitRadius, 0f))
                flatZoomDistance = planetaryCamera.minOrbitRadius;
            if (Mathf.Approximately(globeZoomDistance, 30f) && !Mathf.Approximately(planetaryCamera.maxOrbitRadius, 0f))
                globeZoomDistance = planetaryCamera.maxOrbitRadius;
        }

        _flatHeight = flatCameraStartHeight;
        _flatPitch = 60f;
    }

    private void OnEnable()
    {
        TrySubscribeToModeChanged();
    }

    private void OnDisable()
    {
        TryUnsubscribeFromModeChanged();
    }

    private void TrySubscribeToModeChanged()
    {
        if (_isSubscribedToModeChanged) return;
        if (MapViewController.Instance == null) return;

        MapViewController.Instance.OnModeChanged += HandleModeChanged;
        _isSubscribedToModeChanged = true;
    }

    private void TryUnsubscribeFromModeChanged()
    {
        if (!_isSubscribedToModeChanged) return;
        
        // Safe null check - Instance may already be destroyed
        if (MapViewController.Instance != null)
            MapViewController.Instance.OnModeChanged -= HandleModeChanged;
        
        _isSubscribedToModeChanged = false;
    }

    private void Start()
    {
        // Try subscribing again in case MapViewController was created after OnEnable
        TrySubscribeToModeChanged();

        if (targetCamera != null)
        {
            _cachedGlobeFov = targetCamera.fieldOfView;
            _cachedGlobeOrtho = targetCamera.orthographic;
            _cachedGlobeOrthoSize = targetCamera.orthographicSize;
        }

        _flatLookAtPoint = Vector3.zero;
        
        // Initialize currentZoomDistance from actual camera state
        InitializeZoomDistance();
    }

    private void InitializeZoomDistance()
    {
        // If we have a planetary camera, use its actual orbit radius
        if (planetaryCamera != null)
        {
            currentZoomDistance = planetaryCamera.orbitRadius;
        }
        else
        {
            // Default to flat zoom distance
            currentZoomDistance = flatZoomDistance;
        }
    }

    private void HandleModeChanged(MapViewController.MapViewMode newMode)
    {
        bool isFlat = newMode == MapViewController.MapViewMode.Flat;

        // Configure planetary camera
        if (planetaryCamera != null)
        {
            planetaryCamera.enabled = !isFlat;
        }

        // Configure camera wrap
        if (targetCamera != null)
        {
            var wrap = targetCamera.GetComponent<FlatMapWrapCamera>();
            if (wrap != null) wrap.SetWrapEnabled(isFlat);
        }

        if (!controlCameraInFlatMode || targetCamera == null) return;

        if (isFlat)
        {
            // Cache globe camera settings
            _cachedGlobeFov = targetCamera.fieldOfView;
            _cachedGlobeOrtho = targetCamera.orthographic;
            _cachedGlobeOrthoSize = targetCamera.orthographicSize;

            // Initialize flat camera state
            _flatLookAtPoint = Vector3.zero;
            _flatHeight = flatCameraStartHeight;
            _flatPitch = 60f;
            _flatYaw = 0f;
            
            // Calculate and set flat camera position immediately
            PositionFlatCamera();
            
            targetCamera.orthographic = false;
            
            Debug.Log($"[FlatGlobeZoomViewController] Switched to FLAT mode, camera repositioned to height {_flatHeight}");
        }
        else
        {
            // Restore globe camera settings
            targetCamera.orthographic = _cachedGlobeOrtho;
            targetCamera.orthographicSize = _cachedGlobeOrthoSize;
            targetCamera.fieldOfView = _cachedGlobeFov;
            
            // Planetary camera will handle repositioning when it's re-enabled
            Debug.Log("[FlatGlobeZoomViewController] Switched to GLOBE mode");
        }
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

    private void Update()
    {
        // Only process if authority exists
        if (MapViewController.Instance == null) return;

        // Late subscription if MapViewController was created after our Start()
        TrySubscribeToModeChanged();

        bool isFlat = MapViewController.Instance.IsFlatMode;

        // Handle zoom-based mode switching
        if (isFlat)
        {
            // In flat mode - check if we should switch to globe (zooming out)
            if (currentZoomDistance >= globeZoomDistance)
            {
                MapViewController.Instance.SetGlobeMode();
                if (planetaryCamera != null)
                    planetaryCamera.orbitRadius = globeZoomDistance;
            }
        }
        else
        {
            // In globe mode - check if we should switch to flat (zooming in)
            if (planetaryCamera != null && planetaryCamera.orbitRadius <= flatZoomDistance)
            {
                currentZoomDistance = flatZoomDistance;
                MapViewController.Instance.SetFlatMode();
            }
        }

        // Update flat camera if in flat mode
        if (isFlat && controlCameraInFlatMode)
        {
            UpdateFlatCamera();
        }
    }

    private void UpdateFlatCamera()
    {
        if (targetCamera == null) return;

        // Check input priority
        if (InputManager.Instance != null)
        {
            if (!InputManager.Instance.CanProcessInput(InputManager.InputPriority.Background)) return;
        }

        bool pointerOverUI = InputManager.Instance != null && InputManager.Instance.IsPointerOverUI();
        float dt = Time.deltaTime;

        // === ZOOM (scroll wheel) ===
        if (!pointerOverUI)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _flatHeight = Mathf.Clamp(_flatHeight - scroll * flatZoomScrollSpeed, flatCameraMinHeight, flatCameraMaxHeight);
                currentZoomDistance = Mathf.Clamp(currentZoomDistance - scroll * flatZoomScrollSpeed * 0.5f, flatZoomDistance, globeZoomDistance);
            }
        }

        // === PAN (WASD / Arrow Keys) ===
        float dx = 0f, dz = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dx -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dx += 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dz += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dz -= 1f;

        if (Mathf.Abs(dx) > 0.001f || Mathf.Abs(dz) > 0.001f)
        {
            float yawRad = _flatYaw * Mathf.Deg2Rad;
            float cosYaw = Mathf.Cos(yawRad);
            float sinYaw = Mathf.Sin(yawRad);

            Vector3 forward = new Vector3(sinYaw, 0f, cosYaw);
            Vector3 right = new Vector3(cosYaw, 0f, -sinYaw);

            Vector3 move = (right * dx + forward * dz).normalized * flatPanSpeed * dt;
            _flatLookAtPoint += move;
        }

        // === ORBIT ROTATION (Right Mouse Drag) ===
        if (enableOrbitRotation && !pointerOverUI)
        {
            if (Input.GetMouseButtonDown(1))
                _lastMousePos = Input.mousePosition;

            if (Input.GetMouseButton(1))
            {
                Vector3 delta = Input.mousePosition - _lastMousePos;
                _lastMousePos = Input.mousePosition;

                _flatYaw += delta.x * orbitSensitivity;
                _flatPitch -= delta.y * orbitSensitivity;
                _flatPitch = Mathf.Clamp(_flatPitch, minPitch, maxPitch);
            }
        }

        // === MOUSE DRAG PAN (Middle Mouse) ===
        if (enableMouseDragPan && !pointerOverUI)
        {
            if (Input.GetMouseButtonDown(2))
                _lastMousePos = Input.mousePosition;

            if (Input.GetMouseButton(2))
            {
                Vector3 delta = Input.mousePosition - _lastMousePos;
                _lastMousePos = Input.mousePosition;

                float yawRad = _flatYaw * Mathf.Deg2Rad;
                float cosYaw = Mathf.Cos(yawRad);
                float sinYaw = Mathf.Sin(yawRad);

                Vector3 right = new Vector3(cosYaw, 0f, -sinYaw);
                Vector3 forward = new Vector3(sinYaw, 0f, cosYaw);

                _flatLookAtPoint -= (right * delta.x + forward * delta.y) * mouseDragPanSpeed;
            }
        }

        // === APPLY CAMERA TRANSFORM ===
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
}
