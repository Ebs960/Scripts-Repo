using TMPro;
using UnityEngine;

/// <summary>
/// Displays the authoritative generated world as an interactable 3D globe.
///
/// This component reuses HexMapChunkManager's PlanetTextureBaker.BakeResult so the
/// globe shows the same continents, biomes, lakes, and rivers as the generated map.
/// It also supports UV/LUT-based picking: raycast the sphere, convert the hit UV
/// to the baked equirectangular texture pixel, then resolve that pixel to a real
/// generated tile index.
/// </summary>
public class GeneratedWorldPlanetPreview : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HexMapChunkManager chunkManager;
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private Camera pickingCamera;
    [SerializeField] private MeshRenderer globeRenderer;
    [SerializeField] private MeshFilter globeMeshFilter;
    [SerializeField] private MeshCollider globeCollider;

    [Header("Globe Material")]
    [Tooltip("Optional material to clone for the globe. If omitted, a Standard material is created.")]
    [SerializeField] private Material globeMaterialTemplate;
    [Tooltip("Texture property used by the globe shader for the baked planet color map.")]
    [SerializeField] private string colorTextureProperty = "_BaseMap";
    [Tooltip("HDRP Lit texture property used when the generated default material is HDRP/Lit.")]
    [SerializeField] private string hdrpColorTextureProperty = "_BaseColorMap";
    [Tooltip("Fallback texture property used by built-in/legacy shaders.")]
    [SerializeField] private string fallbackColorTextureProperty = "_MainTex";
    [Tooltip("Texture property used by the globe shader for the baked heightmap, if available.")]
    [SerializeField] private string heightTextureProperty = "_HeightMap";

    [Header("Generated Mesh")]
    [SerializeField, Range(16, 256)] private int longitudeSegments = 128;
    [SerializeField, Range(8, 128)] private int latitudeSegments = 64;
    [SerializeField, Min(0.01f)] private float radius = 1f;
    [SerializeField] private bool generateMeshOnAwake = true;

    [Header("Interaction")]
    [SerializeField] private bool enablePicking = true;
    [SerializeField] private LayerMask pickingMask = ~0;
    [SerializeField] private bool logPickedTiles = false;
    [SerializeField] private bool pickOnMouseClick = true;
    [SerializeField] private int mouseButton = 0;

    [Header("Rotation")]
    [SerializeField] private bool rotate = true;
    [SerializeField] private float rotationSpeedDegrees = 5f;

    [Header("Planet Name Label")]
    [Tooltip("Prefab created in Unity. The first TMP_Text found in this prefab is set to the current planet name.")]
    [SerializeField] private GameObject planetNameLabelPrefab;
    [SerializeField] private Transform planetNameLabelParent;
    [SerializeField] private Vector3 planetNameLabelLocalOffset = new Vector3(0f, 1.35f, 0f);
    [SerializeField] private bool instantiateLabelOnAwake = true;

    private Material globeMaterialInstance;
    private PlanetTextureBaker.BakeResult currentBakeResult;
    private GameObject planetNameLabelInstance;
    private TMP_Text planetNameText;
    private int currentPlanetIndex = int.MinValue;

    public int LastPickedTileIndex { get; private set; } = -1;
    public HexTileData LastPickedTileData { get; private set; }

    private void Awake()
    {
        ResolveReferences();

        if (generateMeshOnAwake)
            EnsureGlobeMesh();

        EnsureMaterial();

        if (instantiateLabelOnAwake)
            EnsurePlanetNameLabel();
    }

    private void OnEnable()
    {
        RefreshFromGeneratedWorld(true);
    }

    private void Update()
    {
        if (rotate)
            transform.Rotate(Vector3.up, rotationSpeedDegrees * Time.deltaTime, Space.Self);

        RefreshFromGeneratedWorld(false);

        if (enablePicking && pickOnMouseClick && Input.GetMouseButtonDown(mouseButton))
            TryPickTile(Input.mousePosition, out _, out _);
    }

    /// <summary>
    /// Refreshes texture and label state from the active generated world.
    /// Safe to call after world generation, planet switching, or rebaking textures.
    /// </summary>
    public void RefreshFromGeneratedWorld(bool force)
    {
        ResolveReferences();
        if (chunkManager == null)
            return;

        PlanetTextureBaker.BakeResult bake = chunkManager.GetBakeResult();
        int planetIndex = planetGenerator != null ? planetGenerator.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        bool planetChanged = planetIndex != currentPlanetIndex;
        bool textureChanged = bake.texture != currentBakeResult.texture || bake.heightmap != currentBakeResult.heightmap;

        if (force || planetChanged || textureChanged)
        {
            currentPlanetIndex = planetIndex;
            currentBakeResult = bake;
            ApplyBakeResultToMaterial();
            UpdatePlanetNameLabel();
        }
    }

    /// <summary>
    /// Raycasts this preview globe and resolves the hit to the authoritative generated tile index.
    /// </summary>
    public bool TryPickTile(Vector2 screenPosition, out int tileIndex, out HexTileData tileData)
    {
        tileIndex = -1;
        tileData = null;

        if (!enablePicking)
            return false;

        ResolveReferences();
        if (pickingCamera == null || globeCollider == null || currentBakeResult.lut == null || currentBakeResult.lut.Length == 0)
            return false;

        Ray ray = pickingCamera.ScreenPointToRay(screenPosition);
        if (!TryRaycastGlobe(ray, out RaycastHit hit))
            return false;

        float u = Mathf.Repeat(hit.textureCoord.x, 1f);
        float v = Mathf.Clamp01(hit.textureCoord.y);
        int width = Mathf.Max(1, currentBakeResult.width);
        int height = Mathf.Max(1, currentBakeResult.height);
        int px = Mathf.Clamp(Mathf.FloorToInt(u * width), 0, width - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(v * height), 0, height - 1);
        int pixelIndex = py * width + px;

        if (pixelIndex < 0 || pixelIndex >= currentBakeResult.lut.Length)
            return false;

        tileIndex = currentBakeResult.lut[pixelIndex];
        if (tileIndex < 0)
            return false;

        if (planetGenerator != null && planetGenerator.data != null)
            planetGenerator.data.TryGetValue(tileIndex, out tileData);

        LastPickedTileIndex = tileIndex;
        LastPickedTileData = tileData;

        if (logPickedTiles)
        {
            string biome = tileData != null ? tileData.biome.ToString() : "unknown biome";
            Debug.Log($"[GeneratedWorldPlanetPreview] Picked tile {tileIndex} ({biome}) at uv=({u:F3},{v:F3}).");
        }

        return true;
    }

    private bool TryRaycastGlobe(Ray ray, out RaycastHit globeHit)
    {
        globeHit = default;
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, pickingMask, QueryTriggerInteraction.Ignore);
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider != globeCollider || hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            globeHit = hit;
        }

        return bestDistance < float.PositiveInfinity;
    }

    private void ResolveReferences()
    {
        if (chunkManager == null)
            chunkManager = FindAnyObjectByType<HexMapChunkManager>();

        if (GameManager.Instance != null)
        {
            PlanetGenerator currentGenerator = GameManager.Instance.GetCurrentPlanetGenerator();
            if (currentGenerator != null && (planetGenerator == null || planetGenerator.planetIndex != GameManager.Instance.currentPlanetIndex))
                planetGenerator = currentGenerator;
        }
        else if (planetGenerator == null)
        {
            planetGenerator = FindAnyObjectByType<PlanetGenerator>();
        }

        if (pickingCamera == null)
            pickingCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

        if (globeRenderer == null)
            globeRenderer = GetComponentInChildren<MeshRenderer>();

        if (globeMeshFilter == null)
            globeMeshFilter = GetComponentInChildren<MeshFilter>();

        if (globeCollider == null)
            globeCollider = GetComponentInChildren<MeshCollider>();
    }

    private void EnsureGlobeMesh()
    {
        if (globeMeshFilter == null)
        {
            GameObject sphere = new GameObject("GeneratedWorldGlobeSphere");
            sphere.transform.SetParent(transform, false);
            globeMeshFilter = sphere.AddComponent<MeshFilter>();
            globeRenderer = sphere.AddComponent<MeshRenderer>();
            globeCollider = sphere.AddComponent<MeshCollider>();
        }

        Mesh mesh = BuildUvSphere(longitudeSegments, latitudeSegments, radius);
        globeMeshFilter.sharedMesh = mesh;

        if (globeCollider == null)
            globeCollider = globeMeshFilter.gameObject.AddComponent<MeshCollider>();
        globeCollider.sharedMesh = mesh;
    }

    private void EnsureMaterial()
    {
        if (globeRenderer == null)
            return;

        if (globeMaterialInstance != null)
            return;

        if (globeMaterialTemplate != null)
        {
            globeMaterialInstance = new Material(globeMaterialTemplate);
        }
        else if (globeRenderer.sharedMaterial != null)
        {
            globeMaterialInstance = new Material(globeRenderer.sharedMaterial);
        }
        else
        {
            Shader shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogWarning("[GeneratedWorldPlanetPreview] No compatible globe shader found.");
                return;
            }
            globeMaterialInstance = new Material(shader);
        }

        globeMaterialInstance.name = "GeneratedWorldPlanetPreview_Material";
        globeRenderer.material = globeMaterialInstance;
    }

    private void ApplyBakeResultToMaterial()
    {
        EnsureMaterial();
        if (globeMaterialInstance == null || currentBakeResult.texture == null)
            return;

        SetTextureIfPresent(colorTextureProperty, currentBakeResult.texture);
        SetTextureIfPresent(hdrpColorTextureProperty, currentBakeResult.texture);
        SetTextureIfPresent(fallbackColorTextureProperty, currentBakeResult.texture);

        if (currentBakeResult.heightmap != null)
            SetTextureIfPresent(heightTextureProperty, currentBakeResult.heightmap);
    }

    private void SetTextureIfPresent(string propertyName, Texture texture)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || texture == null || globeMaterialInstance == null)
            return;

        if (globeMaterialInstance.HasProperty(propertyName))
            globeMaterialInstance.SetTexture(propertyName, texture);
    }

    private void EnsurePlanetNameLabel()
    {
        if (planetNameLabelPrefab == null || planetNameLabelInstance != null)
            return;

        Transform parent = planetNameLabelParent != null ? planetNameLabelParent : transform;
        planetNameLabelInstance = Instantiate(planetNameLabelPrefab, parent);
        planetNameLabelInstance.name = $"{planetNameLabelPrefab.name}_PlanetName";
        planetNameLabelInstance.transform.localPosition = planetNameLabelLocalOffset;
        planetNameText = planetNameLabelInstance.GetComponentInChildren<TMP_Text>(true);
        UpdatePlanetNameLabel();
    }

    private void UpdatePlanetNameLabel()
    {
        EnsurePlanetNameLabel();
        if (planetNameText == null)
            return;

        planetNameText.text = GetPlanetDisplayName();
    }

    private string GetPlanetDisplayName()
    {
        int planetIndex = planetGenerator != null ? planetGenerator.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);

        if (GameManager.Instance != null)
        {
            var planetData = GameManager.Instance.GetPlanetData();
            if (planetData != null && planetData.TryGetValue(planetIndex, out var data) && data != null && !string.IsNullOrWhiteSpace(data.planetName))
                return data.planetName.Trim();
        }

        if (planetGenerator != null && !string.IsNullOrWhiteSpace(planetGenerator.name))
            return planetGenerator.name;

        return $"Planet {planetIndex}";
    }

    private static Mesh BuildUvSphere(int longitudeSegments, int latitudeSegments, float radius)
    {
        longitudeSegments = Mathf.Max(3, longitudeSegments);
        latitudeSegments = Mathf.Max(2, latitudeSegments);
        radius = Mathf.Max(0.01f, radius);

        int vertexCount = (longitudeSegments + 1) * (latitudeSegments + 1);
        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[longitudeSegments * latitudeSegments * 6];

        int vertex = 0;
        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = lat / (float)latitudeSegments;
            float theta = (v - 0.5f) * Mathf.PI;
            float y = Mathf.Sin(theta);
            float ringRadius = Mathf.Cos(theta);

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = lon / (float)longitudeSegments;
                float phi = u * Mathf.PI * 2f;
                Vector3 normal = new Vector3(Mathf.Sin(phi) * ringRadius, y, Mathf.Cos(phi) * ringRadius).normalized;
                vertices[vertex] = normal * radius;
                normals[vertex] = normal;
                uvs[vertex] = new Vector2(u, v);
                vertex++;
            }
        }

        int tri = 0;
        int stride = longitudeSegments + 1;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int a = lat * stride + lon;
                int b = a + stride;
                int c = a + 1;
                int d = b + 1;

                triangles[tri++] = a;
                triangles[tri++] = b;
                triangles[tri++] = c;
                triangles[tri++] = c;
                triangles[tri++] = b;
                triangles[tri++] = d;
            }
        }

        Mesh mesh = new Mesh { name = "GeneratedWorldPlanetPreview_UVSphere" };
        if (vertexCount > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
