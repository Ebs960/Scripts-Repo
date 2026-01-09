using UnityEngine;

/// <summary>
/// Flat world view controller.
/// - Bakes a shared world texture (PlanetTextureBaker)
/// - Creates a flat map quad for visualization
/// - Routes hover/click to TileSystem via UV/LUT picking (WorldPicker)
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

    [Header("Camera")]
    public Camera targetCamera;

    // Runtime objects
    private GameObject _flatQuad;
    private Material _worldMaterial;
    private PlanetGenerator _boundPlanet;
    private PlanetTextureBaker.BakeResult _bake;
    private WorldPicker _picker;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    void Start()
    {
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

        HandleInput();
    }

    private void EnsurePickerComponent()
    {
        _picker = GetComponent<WorldPicker>();
        if (_picker == null) _picker = gameObject.AddComponent<WorldPicker>();
        _picker.targetCamera = targetCamera;
    }

    private void HandleInput()
    {
        if (targetCamera == null) return;

        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
        {
            return;
        }

        if (_picker != null && _picker.TryPickTileIndex(Input.mousePosition, out int tileIndex, out var worldPos))
        {
            if (Input.GetMouseButtonDown(0))
            {
                // Hook for click events if needed.
            }
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
        _bake = PlanetTextureBaker.Bake(planetGen, colorProvider, textureWidth, textureHeight);

        if (_worldMaterial == null)
        {
            _worldMaterial = new Material(Shader.Find("Unlit/Texture"));
        }

        if (_worldMaterial != null)
        {
            _worldMaterial.mainTexture = _bake.texture;
        }

        CreateOrUpdateFlatQuad(planetGen);
        ConfigurePicker();
    }

    private void CreateOrUpdateFlatQuad(PlanetGenerator planetGen)
    {
        if (_flatQuad == null)
        {
            _flatQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _flatQuad.name = "FlatMapQuad_Texture";
            _flatQuad.transform.SetParent(worldRoot, false);
            _flatQuad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // XY quad -> XZ plane
        }

        _flatQuad.transform.position = new Vector3(planetGen.transform.position.x, flatMapY, planetGen.transform.position.z);

        float mapWidth = planetGen.Grid.MapWidth * flatMapMarginMultiplier;
        float mapHeight = planetGen.Grid.MapHeight * flatMapMarginMultiplier;
        _flatQuad.transform.localScale = new Vector3(mapWidth, mapHeight, 1f);

        var flatRenderer = _flatQuad.GetComponent<Renderer>();
        if (flatRenderer != null) flatRenderer.sharedMaterial = _worldMaterial;
    }

    private void ConfigurePicker()
    {
        if (_picker == null) return;
        _picker.lutWidth = _bake.width;
        _picker.lutHeight = _bake.height;
        _picker.lut = _bake.lut;

        if (_flatQuad != null)
        {
            var col = _flatQuad.GetComponent<Collider>();
            _picker.flatMapCollider = col;
        }
    }
}
