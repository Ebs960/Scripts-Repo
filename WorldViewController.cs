using UnityEngine;

/// <summary>
/// Texture-planet world view controller.
/// 
/// Responsibilities (per MASTER AI INSTRUCTIONS):
/// - If GameManager.UseTexturePlanet == true:
///   - Disable per-tile collider picking (TileSystem already disables itself)
///   - Bake a shared equirectangular world texture (PlanetTextureBaker)
///   - Create:
///     - FlatMapQuad (gameplay surface visualization)
///     - GlobeSphere (context visualization)
///   - Drive zoom->morph (0=flat, 1=globe) and visibility switching
///   - Route hover/click to TileSystem via UV/LUT picking (WorldPicker)
/// 
/// Notes:
/// - Does not delete any old code; it is gated by GameManager.UseTexturePlanet.
/// - Does not instantiate per-tile meshes in texture mode.
/// </summary>
public class WorldViewController : MonoBehaviour
{
    [Header("Mode")]
    public bool UseTexturePlanet = true;

    [Header("Baking")]
    public MinimapColorProvider colorProvider;
    public int textureWidth = 2048;
    public int textureHeight = 1024;

    [Header("View Objects")]
    public Transform worldRoot;
    public float flatMapY = 0f;
    public float flatMapMarginMultiplier = 1.02f; // slight margin to avoid edge clipping

    [Header("Zoom -> Morph")]
    [Tooltip("Camera distance that corresponds to morph=0 (flat).")]
    public float zoomDistanceFlat = 150f;
    [Tooltip("Camera distance that corresponds to morph=1 (globe).")]
    public float zoomDistanceGlobe = 800f;

    [Header("Camera")]
    public Camera targetCamera;
    public PlanetaryCameraManager planetaryCameraManager; // optional

    // Runtime objects
    private GameObject _flatQuad;
    private GameObject _globeSphere;
    private Material _worldMaterial;
    private PlanetGenerator _boundPlanet;
    private PlanetTextureBaker.BakeResult _bake;
    private WorldPicker _picker;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (planetaryCameraManager == null) planetaryCameraManager = FindAnyObjectByType<PlanetaryCameraManager>();
    }

    void Start()
    {
        // GameManager.UseTexturePlanet no longer exists.
        // Use the inspector-configured value for this behaviour.
        if (!UseTexturePlanet)
        {
            enabled = false;
            return;
        }

        if (worldRoot == null)
        {
            var go = new GameObject("TextureWorldRoot");
            worldRoot = go.transform;
        }

        EnsurePickerComponent();
        TryBindToCurrentPlanet(force: true);
    }

    void Update()
    {
        if (!UseTexturePlanet) return;

        // Keep planet binding current (planet switching)
        TryBindToCurrentPlanet(force: false);

        float camDist = GetCameraDistance();
        float morph = Mathf.InverseLerp(zoomDistanceFlat, zoomDistanceGlobe, camDist);
        morph = Mathf.Clamp01(morph);

        if (_picker != null) _picker.morph = morph;

        ApplyVisibility(morph);
        HandleInput(morph);
    }

    private void EnsurePickerComponent()
    {
        _picker = GetComponent<WorldPicker>();
        if (_picker == null) _picker = gameObject.AddComponent<WorldPicker>();
        _picker.targetCamera = targetCamera;
    }

    private float GetCameraDistance()
    {
        if (planetaryCameraManager != null)
            return planetaryCameraManager.orbitRadius;
        if (targetCamera != null)
            return Vector3.Distance(targetCamera.transform.position, Vector3.zero);
        return 0f;
    }

    private void ApplyVisibility(float morph)
    {
        bool showFlat = morph < 0.5f;
        if (_flatQuad != null) _flatQuad.SetActive(showFlat);
        if (_globeSphere != null) _globeSphere.SetActive(!showFlat);

        // When globe is visible, allow orbital camera manager; when flat is visible, disable it.
        if (planetaryCameraManager != null)
            planetaryCameraManager.enabled = !showFlat;
    }

    private void HandleInput(float morph)
    {
        if (targetCamera == null) return;

        // Hover always (unless UI blocks)
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
        {
            // TODO: ExternalHoverExit removed from TileSystem
            // TileSystem.Instance?.ExternalHoverExit();
            return;
        }

        if (_picker != null && _picker.TryPickTileIndex(Input.mousePosition, out int tileIndex, out var worldPos))
        {
            // TODO: ExternalHover/ExternalClick removed from TileSystem
            // Use InputManager or direct tile system events instead
            // TileSystem.Instance?.ExternalHover(tileIndex, worldPos);

            if (Input.GetMouseButtonDown(0))
            {
                // TileSystem.Instance?.ExternalClick(tileIndex, worldPos);
            }
        }
        else
        {
            // TileSystem.Instance?.ExternalHoverExit();
        }
    }

    private void TryBindToCurrentPlanet(bool force)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        var gen = gm.GetCurrentPlanetGenerator();
        if (gen == null || gen.Grid == null || !gen.Grid.IsBuilt) return;

        if (!force && gen == _boundPlanet) return;

        _boundPlanet = gen;
        RebuildForPlanet(gen);
    }

    private void RebuildForPlanet(PlanetGenerator planetGen)
    {
        // Bake texture + LUT
        _bake = PlanetTextureBaker.Bake(planetGen, colorProvider, textureWidth, textureHeight);

        if (_worldMaterial == null)
        {
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Standard");
            _worldMaterial = new Material(shader) { name = "WorldTextureMaterial_Runtime" };
        }
        _worldMaterial.mainTexture = _bake.texture;

        // Destroy previous objects (runtime only)
        if (_flatQuad != null) Destroy(_flatQuad);
        if (_globeSphere != null) Destroy(_globeSphere);

        // Create globe
        _globeSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _globeSphere.name = "GlobeSphere_Texture";
        _globeSphere.transform.SetParent(worldRoot, false);
        _globeSphere.transform.position = planetGen.transform.position;
        _globeSphere.transform.localScale = Vector3.one * (planetGen.radius * 2f);
        var globeR = _globeSphere.GetComponent<Renderer>();
        if (globeR != null) globeR.sharedMaterial = _worldMaterial;

        // Create flat
        _flatQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _flatQuad.name = "FlatMapQuad_Texture";
        _flatQuad.transform.SetParent(worldRoot, false);
        _flatQuad.transform.position = new Vector3(planetGen.transform.position.x, flatMapY, planetGen.transform.position.z);
        _flatQuad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // XY quad -> XZ plane

        float mapWidth = 2f * Mathf.PI * planetGen.radius * flatMapMarginMultiplier;
        float mapHeight = Mathf.PI * planetGen.radius * flatMapMarginMultiplier;
        _flatQuad.transform.localScale = new Vector3(mapWidth, mapHeight, 1f);

        var flatR = _flatQuad.GetComponent<Renderer>();
        if (flatR != null) flatR.sharedMaterial = _worldMaterial;

        // Ensure colliders are present and assigned
        _picker.flatMapCollider = _flatQuad.GetComponent<Collider>();
        _picker.globeCollider = _globeSphere.GetComponent<Collider>();
        _picker.lutWidth = _bake.width;
        _picker.lutHeight = _bake.height;
        _picker.lut = _bake.lut;
    }
}

