using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlanetaryCameraManager : MonoBehaviour
{
    [Header("Skybox Settings")]
    public Material earthSkybox;
    private Skybox cameraSkybox;

    [Header("Sun Billboard")]
    private SunBillboard _sunBB;
    private Light sceneSun;

    [Header("Quaternion Orbit Camera")]
    public Vector3 planetCenter = Vector3.zero;
    private Vector3 currentOrbitCenter;
    public float orbitRadius = 8f;
    public float minOrbitRadius = 2f;
    public float maxOrbitRadius = 30f;
    public float orbitSpeed = 1.0f;
    public float pitchSpeed = 1.0f;
    public float yawSpeed = 1.0f;
    private Quaternion cameraRotation = Quaternion.identity;
    public float mouseSensitivity = 2.0f;
    public bool allowMouseDrag = true;
    private Vector3? lastMousePos = null;
    // Moons are now treated as regular planets (separate body indices). This camera always orbits the current planet.
    public bool IsOnMoon => false;

    [Header("Swooping Camera Settings")]
    public bool enableSwooping = true;
    public float maxZoomPivot = -10f;
    public float minZoomPivot = -60f;
    public float cameraPivot = 0f;
    public float minPivot = -80f;
    public float maxPivot = 80f;

    private float yaw = 0f;
    private float pitch = 0f;
    private float basePivot = 0f;

    void Awake()
    {
        cameraSkybox = GetComponent<Skybox>();
        if (cameraSkybox == null)
            cameraSkybox = gameObject.AddComponent<Skybox>();

        cameraRotation = Quaternion.identity;

        // Initialize center positions
        UpdateCenterPositions();
        currentOrbitCenter = planetCenter;

        // Find the SunBillboard and Directional Light in the scene
        _sunBB = FindAnyObjectByType<SunBillboard>();
        sceneSun = FindAnyObjectByType<Light>();
        if (_sunBB != null && sceneSun != null)
        {
            _sunBB.sun = sceneSun;
            // Camera assignment will be handled by SunBillboard itself or after planet creation
        }

        // Optionally, assign the camera if SunBillboard exists
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
        float deltaYaw = 0f, deltaPitch = 0f, pivot = 0f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) deltaYaw += yawSpeed * dt;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) deltaYaw -= yawSpeed * dt;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) deltaPitch += pitchSpeed * dt;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) deltaPitch -= pitchSpeed * dt;
        if (Input.GetKey(KeyCode.Z)) pivot += pitchSpeed * dt;
        if (Input.GetKey(KeyCode.X)) pivot -= pitchSpeed * dt;

        yaw += deltaYaw;
        pitch += deltaPitch;
        pitch = Mathf.Clamp(pitch, -89f, 89f);
        basePivot += pivot;
        basePivot = Mathf.Clamp(basePivot, minPivot, maxPivot);

        float swoopPivot = 0f;
        if (enableSwooping)
        {
            float zoomNormalized = Mathf.InverseLerp(minOrbitRadius, maxOrbitRadius, orbitRadius);
            swoopPivot = Mathf.Lerp(minZoomPivot, maxZoomPivot, zoomNormalized);
        }

        cameraPivot = basePivot + swoopPivot;
        cameraPivot = Mathf.Clamp(cameraPivot, minPivot, maxPivot);

        if (allowMouseDrag)
        {
            // MIGRATED: Check UI blocking before mouse drag
            if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
            {
                lastMousePos = null; // Cancel drag if over UI
                return;
            }

            if (Input.GetMouseButtonDown(0))
                lastMousePos = Input.mousePosition;
            else if (Input.GetMouseButton(0) && lastMousePos.HasValue)
            {
                Vector3 delta = (Vector3)Input.mousePosition - lastMousePos.Value;
                float dragYaw = -delta.x * mouseSensitivity * 0.01f;
                float dragPitch = -delta.y * mouseSensitivity * 0.01f;
                yaw += dragYaw;
                pitch += dragPitch;
                pitch = Mathf.Clamp(pitch, -89f, 89f);
                lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(0))
                lastMousePos = null;
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            orbitRadius = Mathf.Clamp(orbitRadius - scroll * orbitSpeed, minOrbitRadius, maxOrbitRadius);

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // Legacy keybind: keep for now, but moons are separate bodies.
            // Tab will attempt to switch to Luna if available, otherwise stays on current planet.
            if (GameManager.Instance != null)
                GameManager.Instance.GoToEarthMoon();
            UpdateCenterPositions();
            currentOrbitCenter = planetCenter;
        }
    }

    void UpdateCameraPosition()
    {
        cameraRotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 camOffset = cameraRotation * (Vector3.back * orbitRadius);
        Vector3 camPos = currentOrbitCenter + camOffset;
        transform.position = camPos;
        transform.LookAt(currentOrbitCenter, cameraRotation * Vector3.up);
        transform.rotation = Quaternion.LookRotation((currentOrbitCenter - camPos).normalized, cameraRotation * Vector3.up);
        transform.Rotate(Vector3.right, cameraPivot, Space.Self);
    }

    void UpdateCenterPositions()
    {
        // If GameManager knows about multiple planets, get the current planet centre.
        if (GameManager.Instance != null)
        {
            var currentPlanet = GameManager.Instance.GetCurrentPlanetGenerator();
            if (currentPlanet != null)
                planetCenter = currentPlanet.transform.position;
        }
        else
        {
            // Fallback: keep existing serialized defaults
        }
    }



    /// <summary>
    /// Public method to update camera centers after world generation
    /// </summary>
    public void RefreshCenterPositions()
    {
        UpdateCenterPositions();
        // Update current orbit center if we're already set
        currentOrbitCenter = planetCenter;
        Debug.Log($"[PlanetaryCameraManager] Refreshed center - Planet: {planetCenter}");
    }

    void HandleClickDetection()
    {
        // Respect input priority and UI blocking
        if (InputManager.Instance != null)
        {
            if (!InputManager.Instance.CanProcessInput(InputManager.InputPriority.Background)) return;
            if (InputManager.Instance.IsPointerOverUI()) return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

            int tileIndex = -1;

            // Click detection for current planet (moons are separate planets now)
            var planet = GameManager.Instance?.GetCurrentPlanetGenerator();
            if (planet != null && planet.Grid != null &&
                RaySphereIntersection(ray, planet.transform.position, planet.transform.localScale.x * 0.5f, out Vector3 hitPoint))
            {
                Vector3 localDir = (hitPoint - planet.transform.position).normalized;
                tileIndex = planet.Grid.GetTileAtPosition(localDir);

                if (tileIndex >= 0)
                {
                    Debug.Log($"Clicked on tile index: {tileIndex}");
                    var tile = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(tileIndex) : null;
                    if (tile != null)
                    {
                        Debug.Log($"Biome: {tile.biome}, Elevation: {tile.elevation}, Food: {tile.food}");
                    }
                }
            }
        }
    }

    void LateUpdate()
    {
        // Refresh positions periodically in case generators move
        if (Time.frameCount % 60 == 0) // Every 60 frames (about once per second at 60fps)
        {
            UpdateCenterPositions();
            currentOrbitCenter = planetCenter;
        }

        HandleInput();
        UpdateCameraPosition();
        HandleClickDetection();
        UpdateSkybox();
        // Optionally, re-assign camera if needed (e.g., after planet creation)
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
        float blendValue = Mathf.InverseLerp(minOrbitRadius, maxOrbitRadius, orbitRadius);

        if (cameraSkybox.material.HasProperty("_Blend"))
            cameraSkybox.material.SetFloat("_Blend", blendValue);
    }

    bool RaySphereIntersection(Ray ray, Vector3 center, float radius, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        Vector3 oc = ray.origin - center;
        float b = Vector3.Dot(oc, ray.direction);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float discriminant = b * b - c;
        if (discriminant < 0f) return false;
        float t = -b - Mathf.Sqrt(discriminant);
        if (t < 0f) t = -b + Mathf.Sqrt(discriminant);
        if (t < 0f) return false;
        hitPoint = ray.origin + t * ray.direction;
        return true;
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
        orbitRadius = Mathf.Clamp(orbitRadius + delta, minOrbitRadius, maxOrbitRadius);
    }

    /// <summary>
    /// Instantly reorient the orbital camera to face a given direction on the active body.
    /// 'dir' is a unit vector in world/planet-local space where:
    /// +Z forward, +X right (east), +Y up (north). Set 'toMoon' true to orbit the moon.
    /// </summary>
    public void JumpToDirection(Vector3 dir, bool toMoon)
    {
        dir = dir.normalized;

        // Convert direction into yaw (around Y) and pitch (around X)
        float newYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float newPitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;

        // Refresh centers (multi-planet aware) and pick orbit target (always planet now)
        UpdateCenterPositions();
        currentOrbitCenter = planetCenter;

        // Apply angles and reset any added pivot tilt so we're not off-kilter
        yaw = newYaw;
        pitch = Mathf.Clamp(newPitch, -89f, 89f);
        basePivot = 0f; // reset user tilt; swoop will still be applied in Update

        // Move camera immediately this frame
        cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 camOffset = cameraRotation * (Vector3.back * orbitRadius);
        Vector3 camPos = currentOrbitCenter + camOffset;
        transform.position = camPos;
        transform.LookAt(currentOrbitCenter, cameraRotation * Vector3.up);
    }

    /// <summary>
    /// Switch orbit target between planet and moon, keeping current orientation.
    /// </summary>
    public void SwitchToMoon(bool toMoon)
    {
        // Legacy API: switch bodies by planet index (Luna) instead of toggling a moon orbit mode.
        if (toMoon)
        {
            if (GameManager.Instance != null) GameManager.Instance.GoToEarthMoon();
        }
        else
        {
            if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
                GameManager.Instance.SetCurrentPlanet(0);
        }
        UpdateCenterPositions();
        currentOrbitCenter = planetCenter;

        // Keep orientation; just reposition camera around the new center
        cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 camOffset = cameraRotation * (Vector3.back * orbitRadius);
        Vector3 camPos = currentOrbitCenter + camOffset;
        transform.position = camPos;
        transform.LookAt(currentOrbitCenter, cameraRotation * Vector3.up);
        Debug.Log($"[PlanetaryCameraManager] Switched target to current planet at {currentOrbitCenter}");
    }
}

