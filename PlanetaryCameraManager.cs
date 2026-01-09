using UnityEngine;
using UnityEngine.InputSystem;

public class PlanetaryCameraManager : MonoBehaviour
{
    [Header("Skybox Settings")]
    public Material earthSkybox;
    private Skybox cameraSkybox;

    [Header("Sun Billboard")]
    private SunBillboard _sunBB;
    private Light sceneSun;

    [Header("Flat Camera Settings")]
    public float panSpeed = 60f;
    public float zoomSpeed = 40f;
    public float minHeight = 20f;
    public float maxHeight = 200f;
    public float pitchAngle = 60f;
    public bool allowMouseDrag = true;
    public float mouseSensitivity = 0.2f;

    private Vector3 _focusPoint = Vector3.zero;
    private float _cameraHeight = 80f;
    private Vector3? _lastMousePos = null;

    void Awake()
    {
        cameraSkybox = GetComponent<Skybox>();
        if (cameraSkybox == null)
            cameraSkybox = gameObject.AddComponent<Skybox>();

        _cameraHeight = Mathf.Clamp(_cameraHeight, minHeight, maxHeight);

        // Find the SunBillboard and Directional Light in the scene
        _sunBB = FindAnyObjectByType<SunBillboard>();
        sceneSun = FindAnyObjectByType<Light>();
        if (_sunBB != null && sceneSun != null)
        {
            _sunBB.sun = sceneSun;
        }

        if (_sunBB != null)
        {
            _sunBB.AssignCamera(GetComponent<Camera>());
        }
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
                if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
                    _lastMousePos = Mouse.current.position.ReadValue();
                else if (Mouse.current != null && Mouse.current.middleButton.isPressed && _lastMousePos.HasValue)
                {
                    Vector3 delta = (Vector3)Mouse.current.position.ReadValue() - _lastMousePos.Value;
                    _focusPoint += new Vector3(-delta.x, 0f, -delta.y) * mouseSensitivity;
                    _lastMousePos = Mouse.current.position.ReadValue();
                }
                else if (Mouse.current != null && Mouse.current.middleButton.wasReleasedThisFrame)
                    _lastMousePos = null;
            }
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            _cameraHeight = Mathf.Clamp(_cameraHeight - scroll * zoomSpeed, minHeight, maxHeight);
    }

    void UpdateCameraPosition()
    {
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
        UpdateCameraPosition();
        UpdateSkybox();
        if (_sunBB != null && _sunBB.targetCam == null)
        {
            _sunBB.AssignCamera(GetComponent<Camera>());
        }
    }

    void UpdateSkybox()
    {
        if (earthSkybox == null || cameraSkybox == null)
            return;

        cameraSkybox.material = earthSkybox;
        float blendValue = Mathf.InverseLerp(minHeight, maxHeight, _cameraHeight);

        if (cameraSkybox.material.HasProperty("_Blend"))
            cameraSkybox.material.SetFloat("_Blend", blendValue);
    }

    // Public method to assign the camera to the SunBillboard after planet creation
    public void AssignSunBillboardCamera()
    {
        if (_sunBB != null)
        {
            _sunBB.AssignCamera(GetComponent<Camera>());
        }
    }

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
