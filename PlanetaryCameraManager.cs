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
    public Vector3 moonCenter = new Vector3(20f, 0f, 0f);
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
    private bool onMoon = false;

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
            onMoon = !onMoon;
            currentOrbitCenter = onMoon ? moonCenter : planetCenter;
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

    void HandleClickDetection()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

            var planet = GameManager.Instance?.planetGenerator ?? FindAnyObjectByType<PlanetGenerator>();
            int tileIndex = -1;

            if (planet != null && planet.Grid != null &&
                RaySphereIntersection(ray, planet.transform.position, planet.transform.localScale.x * 0.5f, out Vector3 hitPoint))
            {
                Vector3 localDir = (hitPoint - planet.transform.position).normalized;
                tileIndex = planet.Grid.GetTileAtPosition(localDir);
            }

            if (tileIndex >= 0)
            {
                Debug.Log($"Clicked on tile index: {tileIndex}");
                var (tile, _) = TileDataHelper.Instance.GetTileData(tileIndex);
                if (tile != null)
                {
                    Debug.Log($"Biome: {tile.biome}, Elevation: {tile.elevation}, Food: {tile.food}");
                }
            }
        }
    }

    void LateUpdate()
    {
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

    // Add this to your PlanetaryCameraManager

public void ZoomBy(float delta)
{
    orbitRadius = Mathf.Clamp(orbitRadius + delta, minOrbitRadius, maxOrbitRadius);
}

public void FocusOnDirection(Vector3 worldDir, float seconds = 0.35f)
{
    // Convert worldDir (from planet center) to yaw/pitch for the camera
    // Assume the planet is at planetCenter (not offset); adjust if not.
    Vector3 dir = worldDir.normalized;
    // Yaw: atan2(z, x); Pitch: asin(y)
    float yawNew = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
    float pitchNew = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
    // Start a coroutine for a smooth lerp (if needed), else snap
    StartCoroutine(LerpToAngles(yaw, pitch, yawNew, pitchNew, seconds));
}

private System.Collections.IEnumerator LerpToAngles(float startYaw, float startPitch, float endYaw, float endPitch, float seconds)
{
    float t = 0f;
    float normEndYaw = endYaw;
    // Ensure minimal angular interpolation (wrap-around)
    if (Mathf.Abs(endYaw - startYaw) > 180f)
        normEndYaw += (endYaw < startYaw) ? 360f : -360f;

    while (t < 1f)
    {
        t += Time.deltaTime / Mathf.Max(0.001f, seconds);
        yaw = Mathf.LerpAngle(startYaw, normEndYaw, t);
        pitch = Mathf.Lerp(startPitch, endPitch, t);
        yield return null;
    }
    yaw = endYaw;
    pitch = endPitch;
}

}
