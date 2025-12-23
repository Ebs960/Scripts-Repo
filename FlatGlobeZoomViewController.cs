using UnityEngine;

/// <summary>
/// Controls flat-vs-globe presentation switching using a zoom parameter derived from camera distance.
/// 
/// - Does NOT change spherical tile logic.
/// - Flat map is a DUPLICATE set of visuals - globe tiles are not touched.
/// - Globe root is COMPLETELY DISABLED in flat mode for clean separation.
/// 
/// Camera behavior:
/// - In flat mode: orbit/pivot camera similar to PlanetaryCameraManager but over flat plane.
/// - In globe mode: leaves control to PlanetaryCameraManager (orbital camera).
/// </summary>
public class FlatGlobeZoomViewController : MonoBehaviour
{
    [Header("View Roots")]
    [SerializeField] private FlatMapRenderer flatMapRenderer;
    [Tooltip("Root object for the globe presentation (PlanetGenerator). Will be COMPLETELY DISABLED in flat mode.")]
    [SerializeField] private GameObject globeVisualRoot;
    [Tooltip("When true, automatically binds globeVisualRoot to GameManager.GetCurrentPlanetGenerator().gameObject.")]
    [SerializeField] private bool autoBindGlobeRootFromGameManager = true;

    [Header("Zoom Source")]
    [Tooltip("If assigned, uses orbitRadius as cameraDistance and enables/disables this component for globe mode.")]
    [SerializeField] private PlanetaryCameraManager planetaryCamera;

    [Tooltip("Camera to control for flat mode (defaults to Camera.main).")]
    [SerializeField] private Camera targetCamera;

    [Header("Zoom Thresholds")]
    [Tooltip("CameraDistance at which zoomT becomes 0 (flat end).")]
    [SerializeField] private float flatZoomDistance = 2f;

    [Tooltip("CameraDistance at which zoomT becomes 1 (globe end).")]
    [SerializeField] private float globeZoomDistance = 30f;

    [Header("Blend Thresholds")]
    [SerializeField, Range(0f, 1f)] private float flatOnlyMaxT = 0.3f;
    [SerializeField, Range(0f, 1f)] private float globeOnlyMinT = 0.7f;

    [Header("Startup")]
    [Tooltip("When true, forces the game to start in flat mode (surface view).")]
    [SerializeField] private bool startInFlatMode = true;

    [Header("Flat Camera - Position")]
    [SerializeField] private bool controlCameraInFlatMode = true;
    [SerializeField] private float flatCameraMinHeight = 20f;
    [SerializeField] private float flatCameraMaxHeight = 200f;
    [SerializeField] private float flatCameraStartHeight = 80f;

    [Header("Flat Camera - Pan (WASD / Arrow Keys)")]
    [SerializeField] private float flatPanSpeed = 60f;

    [Header("Flat Camera - Orbit/Pivot (like PlanetaryCameraManager)")]
    [Tooltip("Enable mouse drag rotation (right mouse button).")]
    [SerializeField] private bool enableOrbitRotation = true;
    [SerializeField] private float orbitSensitivity = 0.3f;
    [Tooltip("Minimum pitch angle (looking down). 10 = nearly top-down, 89 = horizon.")]
    [SerializeField] private float minPitch = 20f;
    [Tooltip("Maximum pitch angle. 90 = straight down.")]
    [SerializeField] private float maxPitch = 90f;

    [Header("Flat Camera - Zoom")]
    [SerializeField] private float flatZoomScrollSpeed = 20f;

    [Header("Flat Camera - Mouse Drag Pan (Middle Mouse)")]
    [SerializeField] private bool enableMouseDragPan = true;
    [SerializeField] private float mouseDragPanSpeed = 0.5f;

    [Header("Runtime (Read-only)")]
    [SerializeField] private float zoomT;
    [SerializeField] private bool isFlatActive;
    [SerializeField] private float flatDistanceDriver;

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

    private bool _pendingForceStartFlat;
    private int _cachedPlanetIndex = -1;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
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
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetReady += HandlePlanetReady;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
    }

    private void Start()
    {
        _pendingForceStartFlat = startInFlatMode;

        TryAutoBindGlobeRoot();
        RebuildFlatForCurrentPlanet();

        if (targetCamera != null)
        {
            _cachedGlobeFov = targetCamera.fieldOfView;
            _cachedGlobeOrtho = targetCamera.orthographic;
            _cachedGlobeOrthoSize = targetCamera.orthographicSize;
        }

        _flatLookAtPoint = Vector3.zero;

        if (planetaryCamera != null)
            flatDistanceDriver = Mathf.Clamp(planetaryCamera.orbitRadius, flatZoomDistance, globeZoomDistance);
        else
            flatDistanceDriver = flatZoomDistance;

        if (startInFlatMode)
        {
            SetFlatActive(true);
            SetGlobeActive(false);
            zoomT = 0f;
        }
    }

    private void HandlePlanetReady(int planetIndex)
    {
        if (GameManager.Instance != null && planetIndex == GameManager.Instance.currentPlanetIndex)
        {
            TryAutoBindGlobeRoot();
            RebuildFlatForCurrentPlanet();
            SetGlobeActive(!isFlatActive);

            if (_pendingForceStartFlat)
            {
                _pendingForceStartFlat = false;
                SetFlatActive(true);
                SetGlobeActive(false);
                zoomT = 0f;
            }
        }
    }

    private void RebuildFlatForCurrentPlanet()
    {
        if (flatMapRenderer == null) return;
        var gen = GameManager.Instance != null ? GameManager.Instance.GetCurrentPlanetGenerator() : null;
        if (gen != null) flatMapRenderer.Rebuild(gen);
    }

    private void Update()
    {
        // CRITICAL: If we're supposed to be in flat mode but globe is still visible, keep trying to hide it
        if (isFlatActive && globeVisualRoot == null)
        {
            TryAutoBindGlobeRoot();
            if (globeVisualRoot != null)
            {
                globeVisualRoot.SetActive(false);
                Debug.Log($"[FlatGlobeZoomViewController] Late-bound and disabled globe: {globeVisualRoot.name}");
            }
        }

        // Periodically check for planet changes (handles single-planet mode and planet switching)
        bool checkPlanet = autoBindGlobeRootFromGameManager && (Time.frameCount % 10 == 0);
        // Also check every frame if we're missing the globe root in flat mode
        checkPlanet |= (isFlatActive && globeVisualRoot == null);
        
        if (checkPlanet)
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                int currentIndex = gm.currentPlanetIndex;
                var gen = gm.GetCurrentPlanetGenerator();

                bool planetChanged = currentIndex != _cachedPlanetIndex;
                bool rootMissing = globeVisualRoot == null;
                bool rootChanged = gen != null && globeVisualRoot != gen.gameObject;

                if (planetChanged || rootMissing || rootChanged)
                {
                    _cachedPlanetIndex = currentIndex;
                    TryAutoBindGlobeRoot();
                    RebuildFlatForCurrentPlanet();
                    
                    // Force globe off if we're in flat mode
                    if (isFlatActive && globeVisualRoot != null)
                    {
                        globeVisualRoot.SetActive(false);
                    }
                    else
                    {
                        SetGlobeActive(!isFlatActive);
                    }

                    if (_pendingForceStartFlat && gen != null)
                    {
                        _pendingForceStartFlat = false;
                        SetFlatActive(true);
                        SetGlobeActive(false);
                        zoomT = 0f;
                    }
                }
            }
        }

        float cameraDistance = GetCameraDistance();
        zoomT = Mathf.InverseLerp(flatZoomDistance, globeZoomDistance, cameraDistance);

        // View switching rules
        bool flatOn = zoomT < flatOnlyMaxT;
        bool globeOn = zoomT > globeOnlyMinT;

        // Middle band: allow overlap (for future fading)
        if (!flatOn && !globeOn)
        {
            flatOn = zoomT < globeOnlyMinT;
            globeOn = zoomT > flatOnlyMaxT;
        }

        SetFlatActive(flatOn);
        SetGlobeActive(globeOn);

        if (controlCameraInFlatMode && isFlatActive)
            UpdateFlatCamera();
    }

    private float GetCameraDistance()
    {
        if (isFlatActive)
            return flatDistanceDriver;

        if (planetaryCamera != null)
            return planetaryCamera.orbitRadius;

        if (targetCamera == null) return 0f;
        return targetCamera.transform.position.magnitude;
    }

    private void SetFlatActive(bool active)
    {
        if (isFlatActive == active) return;
        isFlatActive = active;

        // Show/hide flat map
        if (flatMapRenderer != null)
            flatMapRenderer.gameObject.SetActive(active);

        // Disable orbital camera in flat mode
        if (planetaryCamera != null)
        {
            if (active)
            {
                flatDistanceDriver = Mathf.Clamp(planetaryCamera.orbitRadius, flatZoomDistance, globeZoomDistance);
                planetaryCamera.enabled = false;
            }
            else
            {
                planetaryCamera.orbitRadius = Mathf.Clamp(flatDistanceDriver, planetaryCamera.minOrbitRadius, planetaryCamera.maxOrbitRadius);
                planetaryCamera.enabled = true;
            }
        }

        // Enable camera wrap in flat mode
        if (targetCamera != null)
        {
            var wrap = targetCamera.GetComponent<FlatMapWrapCamera>();
            if (wrap != null) wrap.SetWrapEnabled(active);
        }

        if (!controlCameraInFlatMode || targetCamera == null) return;

        if (active)
        {
            // Cache globe camera settings
            _cachedGlobeFov = targetCamera.fieldOfView;
            _cachedGlobeOrtho = targetCamera.orthographic;
            _cachedGlobeOrthoSize = targetCamera.orthographicSize;

            // Initialize flat camera over center
            _flatLookAtPoint = Vector3.zero;
            targetCamera.orthographic = false; // Perspective for orbit feel
        }
        else
        {
            // Restore globe camera settings
            targetCamera.orthographic = _cachedGlobeOrtho;
            targetCamera.orthographicSize = _cachedGlobeOrthoSize;
            targetCamera.fieldOfView = _cachedGlobeFov;
        }
    }

    private void SetGlobeActive(bool active)
    {
        // Try to bind if we don't have a reference yet
        if (globeVisualRoot == null)
            TryAutoBindGlobeRoot();

        // COMPLETELY disable globe root in flat mode - simple and clean
        if (globeVisualRoot != null)
        {
            globeVisualRoot.SetActive(active);
        }
        else if (!active)
        {
            // Fallback: if we still don't have the root but need to hide the globe,
            // try to find and disable any PlanetGenerator in the scene
            var planetGen = GameManager.Instance != null ? GameManager.Instance.GetCurrentPlanetGenerator() : null;
            if (planetGen != null)
            {
                globeVisualRoot = planetGen.gameObject;
                globeVisualRoot.SetActive(false);
                Debug.Log($"[FlatGlobeZoomViewController] Bound and disabled globe: {globeVisualRoot.name}");
            }
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
                flatDistanceDriver = Mathf.Clamp(flatDistanceDriver - scroll * flatZoomScrollSpeed * 0.5f, flatZoomDistance, globeZoomDistance);
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
            // Pan relative to camera yaw
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

                // Pan relative to camera orientation
                float yawRad = _flatYaw * Mathf.Deg2Rad;
                float cosYaw = Mathf.Cos(yawRad);
                float sinYaw = Mathf.Sin(yawRad);

                Vector3 right = new Vector3(cosYaw, 0f, -sinYaw);
                Vector3 forward = new Vector3(sinYaw, 0f, cosYaw);

                _flatLookAtPoint -= (right * delta.x + forward * delta.y) * mouseDragPanSpeed;
            }
        }

        // === APPLY CAMERA TRANSFORM ===
        // Calculate camera position orbiting around _flatLookAtPoint
        float pitchRad = _flatPitch * Mathf.Deg2Rad;
        float yawRadFinal = _flatYaw * Mathf.Deg2Rad;

        // Spherical to Cartesian offset from look-at point
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

    private void LateUpdate()
    {
        // SAFETY NET: Ensure globe stays disabled when in flat mode
        // This catches cases where something else might re-enable it
        if (isFlatActive && globeVisualRoot != null && globeVisualRoot.activeSelf)
        {
            globeVisualRoot.SetActive(false);
        }
    }

    private void TryAutoBindGlobeRoot()
    {
        if (!autoBindGlobeRootFromGameManager) return;
        if (GameManager.Instance == null) return;
        var gen = GameManager.Instance.GetCurrentPlanetGenerator();
        if (gen == null) return;
        globeVisualRoot = gen.gameObject;
    }
}
