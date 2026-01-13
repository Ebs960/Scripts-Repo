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
    private Vector3? _lastMousePos = null;

    void Awake()
    {
        _cameraHeight = Mathf.Clamp(_cameraHeight, minHeight, maxHeight);
    }

    // Expose focus point for helpers like FlatMapWrapCamera
    public Vector3 FocusPoint
    {
        get => _focusPoint;
        set => _focusPoint = new Vector3(value.x, 0f, value.z);
    }

    void HandleInput()
    {
        // MIGRATED: Check InputManager priority (Background priority for camera)
        if (InputManager.Instance != null && !InputManager.Instance.CanProcessInput(InputManager.InputPriority.Background))
            return;

        float dt = Time.deltaTime;
        Vector3 panDirection = Vector3.zero;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) panDirection.x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) panDirection.x += 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) panDirection.z += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) panDirection.z -= 1f;

        if (panDirection.sqrMagnitude > 0f)
        {
            panDirection.Normalize();
            _focusPoint += new Vector3(panDirection.x, 0f, panDirection.z) * panSpeed * dt;
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
                    _focusPoint += new Vector3(-delta.x, 0f, -delta.y) * mouseSensitivity;
                    _lastMousePos = Mouse.current.position.ReadValue();
                }
                else if (buttonReleased)
                    _lastMousePos = null;
            }
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            _cameraHeight = Mathf.Clamp(_cameraHeight - scroll * zoomSpeed, minHeight, maxHeight);
    }

    void UpdateCameraPosition()
    {
        // Interpolate pitch based on zoom level (zoomed in = lower pitch, zoomed out = higher pitch)
        float zoomT = Mathf.InverseLerp(minHeight, maxHeight, _cameraHeight);
        float pitchAngle = Mathf.Lerp(minPitchAngle, maxPitchAngle, zoomT);
        
        float pitchRad = pitchAngle * Mathf.Deg2Rad;
        float horizontalDist = _cameraHeight / Mathf.Tan(pitchRad);
        Vector3 camPos = new Vector3(
            _focusPoint.x,
            _cameraHeight,
            _focusPoint.z - horizontalDist
        );
        transform.position = camPos;
        transform.rotation = Quaternion.LookRotation(_focusPoint - camPos, Vector3.up);
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
