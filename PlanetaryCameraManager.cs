using UnityEngine;
using UnityEngine.InputSystem;

public class PlanetaryCameraManager : MonoBehaviour
{
    [Header("Camera Basics")]
    // Globe-related skybox and sun billboard removed

    [Header("Flat Camera Settings")]
    public float panSpeed = 60f;
    public float zoomSpeed = 40f;
    public float minHeight = 20f;
    public float maxHeight = 200f;
    
    [Header("Rotation (Q/E)")]
    [Tooltip("Yaw rotation speed in degrees per second when pressing Q or E.")]
    public float rotateSpeed = 60f;

    [Header("Pitch Settings")]
    [Tooltip("Pitch angle when fully zoomed in (looking more forward).")]
    public float minPitchAngle = 30f;
    [Tooltip("Pitch angle when fully zoomed out (looking more down).")]
    public float maxPitchAngle = 75f;
    
    public bool allowMouseDrag = true;
    public float mouseSensitivity = 0.2f;
    [Tooltip("Which mouse button to use for dragging: 0=Left, 1=Right, 2=Middle")]
    public int dragMouseButton = 1; // Default to right mouse button

    [Header("Horizontal Wrap")]
    [Tooltip("Enable horizontal wrap (Civ-style infinite scroll).")]
    public bool wrapEnabled = true;
    [Tooltip("Reference to chunk-based map manager.")]
    public HexMapChunkManager chunkManager;
    [Tooltip("Center X for wrap bounds. 0 means map centered at world X=0.")]
    public float wrapCenterX = 0f;

    private Vector3 _focusPoint = Vector3.zero;
    private float _cameraHeight = 80f;

    /// <summary>
    /// Current camera height (read-only). Used by OrbitSkyboxController for space transition.
    /// </summary>
    public float CameraHeight => _cameraHeight;
    private Vector3? _lastMousePos = null;

    [Header("Underwater Transition")]
    [Tooltip("Target camera height when fully in underwater mode.")]
    public float underwaterTargetHeight = 30f;
    [Tooltip("Target pitch angle (degrees) when underwater.")]
    public float underwaterTargetPitchAngle = 40f;
    [Tooltip("Pitch angle when fully zoomed in while underwater.")]
    public float underwaterMinPitchAngle = 35f;
    [Tooltip("Pitch angle when fully zoomed out while underwater.")]
    public float underwaterMaxPitchAngle = 55f;
    [Tooltip("Speed (units/sec) the camera moves when swooping to the ocean.")]
    public float underwaterMoveSpeed = 40f;
    [Tooltip("Minimum allowed camera height while underwater.")]
    public float underwaterMinHeight = 10f;
    [Tooltip("Maximum allowed camera height while underwater.")]
    public float underwaterMaxHeight = 80f;

    [Header("Orbit Transition")]
    [Tooltip("Target pitch angle (degrees) when viewing orbit layer (looking more downward).")]
    public float orbitTargetPitchAngle = 60f;
    [Tooltip("Pitch angle when fully zoomed in while in orbit.")]
    public float orbitMinPitchAngle = 55f;
    [Tooltip("Pitch angle when fully zoomed out while in orbit.")]
    public float orbitMaxPitchAngle = 75f;
    [Tooltip("Speed (units/sec) the camera moves when swooping up to orbit.")]
    public float orbitMoveSpeed = 50f;
    [Tooltip("Extra height above orbit altitude for a good overview.")]
    public float orbitHeightPadding = 30f;
    [Tooltip("Minimum allowed camera height while in orbit view.")]
    public float orbitMinHeight = 40f;
    [Tooltip("Maximum allowed camera height while in orbit view.")]
    public float orbitMaxHeight = 300f;

    // Runtime state
    private Coroutine _transitionCoroutine = null;
    private bool _isInUnderwaterMode = false;
    private bool _isInOrbitMode = false;
    private float _savedSurfaceHeight;
    private float _savedSurfacePitchMin;
    private float _savedSurfacePitchMax;
    private float _savedSurfaceMinHeight;
    private float _savedSurfaceMaxHeight;

    void Awake()
    {
        if (transform.position.y > 0.001f)
            _cameraHeight = transform.position.y;
        _cameraHeight = Mathf.Clamp(_cameraHeight, minHeight, maxHeight);
        _focusPoint = new Vector3(transform.position.x, 0f, transform.position.z);
    }

    public bool IsInUnderwaterMode => _isInUnderwaterMode;
    public bool IsInOrbitMode => _isInOrbitMode;

    // Expose focus point for helpers like FlatMapWrapCamera
    public Vector3 FocusPoint
    {
        get => _focusPoint;
        set => _focusPoint = new Vector3(value.x, 0f, value.z);
    }

    /// <summary>
    /// Smoothly move the camera to focus on a world point (XZ) and adjust height/pitch to underwater presets.
    /// This physically moves the single camera; there is no camera switching.
    /// </summary>
    public void TransitionToUnderwater(Vector3 oceanWorldPoint, float maxMoveSpeed = -1f)
    {
        if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
        _savedSurfaceHeight = _cameraHeight;
        _savedSurfacePitchMin = minPitchAngle;
        _savedSurfacePitchMax = maxPitchAngle;
        _savedSurfaceMinHeight = minHeight;
        _savedSurfaceMaxHeight = maxHeight;

        if (maxMoveSpeed <= 0f) maxMoveSpeed = underwaterMoveSpeed;
        _transitionCoroutine = StartCoroutine(UnderwaterTransitionCoroutine(oceanWorldPoint, maxMoveSpeed));
    }

    /// <summary>
    /// Return camera to saved surface presets.
    /// </summary>
    public void TransitionToSurface(float duration = 0.6f)
    {
        if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
        _transitionCoroutine = StartCoroutine(SurfaceTransitionCoroutine(duration));
    }

    /// <summary>
    /// Smoothly move the camera up to orbit altitude for a birds-eye overview of orbit-layer objects.
    /// </summary>
    public void TransitionToOrbit(Vector3 focusPoint, float orbitLayerHeight)
    {
        if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);

        // Only save surface settings if we aren't already in a special mode
        if (!_isInUnderwaterMode && !_isInOrbitMode)
        {
            _savedSurfaceHeight = _cameraHeight;
            _savedSurfacePitchMin = minPitchAngle;
            _savedSurfacePitchMax = maxPitchAngle;
            _savedSurfaceMinHeight = minHeight;
            _savedSurfaceMaxHeight = maxHeight;
        }

        _transitionCoroutine = StartCoroutine(OrbitTransitionCoroutine(focusPoint, orbitLayerHeight));
    }

    private System.Collections.IEnumerator UnderwaterTransitionCoroutine(Vector3 oceanWorldPoint, float moveSpeed)
    {
        _isInUnderwaterMode = true;

        Vector3 startFocus = _focusPoint;
        Vector3 targetFocus = new Vector3(oceanWorldPoint.x, 0f, oceanWorldPoint.z);

        float startHeight = _cameraHeight;
        // Constrain target height to underwater-specific limits and switch min/max constraints
        float targetHeight = Mathf.Clamp(underwaterTargetHeight, underwaterMinHeight, underwaterMaxHeight);
        // Temporarily apply underwater min/max so UpdateCameraPosition uses them
        minHeight = underwaterMinHeight;
        maxHeight = underwaterMaxHeight;

        float maxDist = Vector3.Distance(startFocus, targetFocus);
        // move until focus nearly at target and height close
        while (Vector3.Distance(_focusPoint, targetFocus) > 0.1f || Mathf.Abs(_cameraHeight - targetHeight) > 0.1f)
        {
            // Move focus toward target at constant speed
            Vector3 dir = (targetFocus - _focusPoint);
            float step = moveSpeed * Time.deltaTime;
            if (dir.sqrMagnitude <= step * step)
                _focusPoint = targetFocus;
            else
                _focusPoint += dir.normalized * step;

            // Smooth height lerp
            _cameraHeight = Mathf.MoveTowards(_cameraHeight, targetHeight, moveSpeed * Time.deltaTime);

            // Adjust pitch range to underwater presets gradually
            minPitchAngle = Mathf.MoveTowards(minPitchAngle, underwaterMinPitchAngle, 30f * Time.deltaTime);
            maxPitchAngle = Mathf.MoveTowards(maxPitchAngle, underwaterMaxPitchAngle, 30f * Time.deltaTime);

            UpdateCameraPosition();
            yield return null;
        }

        // ensure final values
        _focusPoint = targetFocus;
        _cameraHeight = targetHeight;
        minPitchAngle = underwaterMinPitchAngle;
        maxPitchAngle = underwaterMaxPitchAngle;
        UpdateCameraPosition();

        _transitionCoroutine = null;
    }

    private System.Collections.IEnumerator SurfaceTransitionCoroutine(float duration)
    {
        float startHeight = _cameraHeight;
        float targetHeight = Mathf.Clamp(_savedSurfaceHeight, _savedSurfaceMinHeight, _savedSurfaceMaxHeight);
        float startMinPitch = minPitchAngle;
        float startMaxPitch = maxPitchAngle;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _cameraHeight = Mathf.Lerp(startHeight, targetHeight, t);
            minPitchAngle = Mathf.Lerp(startMinPitch, _savedSurfacePitchMin, t);
            maxPitchAngle = Mathf.Lerp(startMaxPitch, _savedSurfacePitchMax, t);
            UpdateCameraPosition();
            yield return null;
        }

        _cameraHeight = targetHeight;
        minPitchAngle = _savedSurfacePitchMin;
        maxPitchAngle = _savedSurfacePitchMax;
        // Restore saved surface min/max height constraints
        minHeight = _savedSurfaceMinHeight;
        maxHeight = _savedSurfaceMaxHeight;
        _isInUnderwaterMode = false;
        _isInOrbitMode = false;
        _transitionCoroutine = null;
    }

    private System.Collections.IEnumerator OrbitTransitionCoroutine(Vector3 focusPoint, float orbitLayerHeight)
    {
        _isInOrbitMode = true;
        _isInUnderwaterMode = false;

        Vector3 targetFocus = new Vector3(focusPoint.x, 0f, focusPoint.z);
        float targetHeight = Mathf.Clamp(orbitLayerHeight + orbitHeightPadding, orbitMinHeight, orbitMaxHeight);

        // Apply orbit height constraints immediately
        minHeight = orbitMinHeight;
        maxHeight = orbitMaxHeight;

        float speed = orbitMoveSpeed;

        while (Mathf.Abs(_cameraHeight - targetHeight) > 0.2f ||
               Vector3.Distance(_focusPoint, targetFocus) > 0.1f)
        {
            // Move focus toward target
            Vector3 dir = targetFocus - _focusPoint;
            if (dir.sqrMagnitude > 0.01f)
            {
                float step = speed * Time.deltaTime;
                if (dir.sqrMagnitude <= step * step)
                    _focusPoint = targetFocus;
                else
                    _focusPoint += dir.normalized * step;
            }

            // Smooth height ramp
            _cameraHeight = Mathf.MoveTowards(_cameraHeight, targetHeight, speed * Time.deltaTime);

            // Gradually adjust pitch range to orbit presets
            minPitchAngle = Mathf.MoveTowards(minPitchAngle, orbitMinPitchAngle, 40f * Time.deltaTime);
            maxPitchAngle = Mathf.MoveTowards(maxPitchAngle, orbitMaxPitchAngle, 40f * Time.deltaTime);

            UpdateCameraPosition();
            yield return null;
        }

        // Snap final values
        _focusPoint = targetFocus;
        _cameraHeight = targetHeight;
        minPitchAngle = orbitMinPitchAngle;
        maxPitchAngle = orbitMaxPitchAngle;
        UpdateCameraPosition();

        _transitionCoroutine = null;
    }


    void HandleInput()
    {
        // MIGRATED: Check InputManager priority (Background priority for camera)
        if (InputManager.Instance != null && !InputManager.Instance.CanProcessInput(InputManager.InputPriority.Background))
            return;

        float dt = Mathf.Min(Time.deltaTime, 1f / 15f);
        Vector3 panDirection = Vector3.zero;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[Key.A].isPressed || kb[Key.LeftArrow].isPressed) panDirection.x -= 1f;
            if (kb[Key.D].isPressed || kb[Key.RightArrow].isPressed) panDirection.x += 1f;
            if (kb[Key.W].isPressed || kb[Key.UpArrow].isPressed) panDirection.z += 1f;
            if (kb[Key.S].isPressed || kb[Key.DownArrow].isPressed) panDirection.z -= 1f;

            // Q/E rotate camera in place (Y axis)
            if (kb[Key.Q].isPressed) transform.Rotate(Vector3.up, -rotateSpeed * dt, Space.World);
            if (kb[Key.E].isPressed) transform.Rotate(Vector3.up, rotateSpeed * dt, Space.World);
        }

        if (panDirection.sqrMagnitude > 0f)
        {
            panDirection.Normalize();
            // Move relative to the camera's current view direction (projected onto XZ).
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 right = transform.right;
            right.y = 0f;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;

            Vector3 worldMove = right * panDirection.x + fwd * panDirection.z;
            _focusPoint += new Vector3(worldMove.x, 0f, worldMove.z) * (panSpeed * dt);
        }

        if (allowMouseDrag)
        {
            // MIGRATED: Check UI blocking before mouse drag
            if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
            {
                _lastMousePos = null; // Cancel drag if over UI
            }
            else
            {
                // Get the configured mouse button state
                bool buttonPressed = false;
                bool buttonHeld = false;
                bool buttonReleased = false;
                
                if (Mouse.current != null)
                {
                    var button = dragMouseButton switch
                    {
                        0 => Mouse.current.leftButton,
                        1 => Mouse.current.rightButton,
                        _ => Mouse.current.middleButton
                    };
                    buttonPressed = button.wasPressedThisFrame;
                    buttonHeld = button.isPressed;
                    buttonReleased = button.wasReleasedThisFrame;
                }
                
                if (buttonPressed)
                    _lastMousePos = Mouse.current.position.ReadValue();
                else if (buttonHeld && _lastMousePos.HasValue)
                {
                    Vector3 delta = (Vector3)Mouse.current.position.ReadValue() - _lastMousePos.Value;
                    Vector3 camRelative = new Vector3(-delta.x, 0f, -delta.y) * mouseSensitivity;
                    Vector3 fwd = transform.forward;
                    fwd.y = 0f;
                    fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                    Vector3 right = transform.right;
                    right.y = 0f;
                    right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;

                    Vector3 worldMove = right * camRelative.x + fwd * camRelative.z;
                    _focusPoint += new Vector3(worldMove.x, 0f, worldMove.z);
                    _lastMousePos = Mouse.current.position.ReadValue();
                }
                else if (buttonReleased)
                    _lastMousePos = null;
            }
        }

        float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        if (Mathf.Abs(scroll) > 0.001f)
            _cameraHeight = Mathf.Clamp(_cameraHeight - scroll * zoomSpeed, minHeight, maxHeight);
    }

    void UpdateCameraPosition()
    {
        // Only update height (zoom) and pitch (look up/down), not yaw or orbit
        float zoomT = Mathf.InverseLerp(minHeight, maxHeight, _cameraHeight);
        float pitchAngle = Mathf.Lerp(minPitchAngle, maxPitchAngle, zoomT);
        pitchAngle = Mathf.Clamp(pitchAngle, 10f, 85f);
        Vector3 euler = transform.eulerAngles;
        euler.x = pitchAngle;
        transform.eulerAngles = euler;
        Vector3 pos = transform.position;
        pos.x = _focusPoint.x;
        pos.y = _cameraHeight;
        pos.z = _focusPoint.z;
        transform.position = pos;
    }

    void LateUpdate()
    {
        HandleInput();
        ApplyWrap();
        UpdateCameraPosition();
    }

    void ApplyWrap()
    {
        if (!wrapEnabled) return;
        
        // Get map width from chunk manager (preferred) or flat map (fallback)
        float mapWidth = 0f;
        float mapHeight = 0f;
        bool isBuilt = false;
        
        if (chunkManager != null && chunkManager.IsBuilt)
        {
            mapWidth = chunkManager.MapWidth;
            mapHeight = chunkManager.MapHeight;
            isBuilt = true;
        }
        // FlatMapTextureRenderer fallback removed - HexMapChunkManager is now the sole renderer
        
        if (!isBuilt || mapWidth <= 0.0001f) return;

        // Horizontal wrapping (X axis - infinite scroll)
        float halfW = mapWidth * 0.5f;
        float minX = wrapCenterX - halfW;
        float maxX = wrapCenterX + halfW;

        if (_focusPoint.x > maxX) _focusPoint.x -= mapWidth;
        else if (_focusPoint.x < minX) _focusPoint.x += mapWidth;
        
        // Vertical clamping (Z axis - no wrap, just clamp to map bounds)
        if (mapHeight > 0.0001f)
        {
            float halfH = mapHeight * 0.5f;
            // Add padding based on zoom level to prevent seeing past edges
            float viewPadding = _cameraHeight * 0.3f; // Adjust based on camera angle
            float minZ = -halfH + viewPadding;
            float maxZ = halfH - viewPadding;
            _focusPoint.z = Mathf.Clamp(_focusPoint.z, minZ, maxZ);
        }
    }

    // Globe/skybox features removed

    public void ZoomBy(float delta)
    {
        _cameraHeight = Mathf.Clamp(_cameraHeight + delta, minHeight, maxHeight);
    }

    public void JumpToWorldPoint(Vector3 worldPoint)
    {
        _focusPoint = new Vector3(worldPoint.x, 0f, worldPoint.z);
        UpdateCameraPosition();
    }
}
