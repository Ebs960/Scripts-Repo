using UnityEngine;
using System.Collections.Generic;

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
    [Tooltip("Enable GPU morph shader (smooth transition between flat and globe). If false, uses visibility switching.")]
    public bool useMorphShader = true;
    [Tooltip("Custom morph shader. If null, auto-finds Custom/FlatGlobeMorph.")]
    public Shader morphShader;

    [Header("Camera")]
    public Camera targetCamera;
    public PlanetaryCameraManager planetaryCameraManager; // optional

    // Runtime objects
    private GameObject _flatQuad;
    private GameObject _globeSphere;
    private GameObject _morphMesh; // Single mesh that morphs between flat and globe
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

        ApplyMorph(morph);
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

    private void ApplyMorph(float morph)
    {
        if (useMorphShader && _morphMesh != null)
        {
            // Use single morphing mesh - always visible
            if (_flatQuad != null) _flatQuad.SetActive(false);
            if (_globeSphere != null) _globeSphere.SetActive(false);
            if (_morphMesh != null) _morphMesh.SetActive(true);
            
            // Update morph parameter in shader
            if (_worldMaterial != null)
            {
                _worldMaterial.SetFloat("_Morph", morph);
            }
            
            // Enable orbital camera when morphing toward globe (morph > 0.5)
            if (planetaryCameraManager != null)
                planetaryCameraManager.enabled = morph > 0.5f;
        }
        else
        {
            // Fallback: visibility switching (original behavior)
            bool showFlat = morph < 0.5f;
            if (_flatQuad != null) _flatQuad.SetActive(showFlat);
            if (_globeSphere != null) _globeSphere.SetActive(!showFlat);
            if (_morphMesh != null) _morphMesh.SetActive(false);
            
            // When globe is visible, allow orbital camera manager; when flat is visible, disable it.
            if (planetaryCameraManager != null)
                planetaryCameraManager.enabled = !showFlat;
        }
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
        if (_morphMesh != null) Destroy(_morphMesh);

        // Create material with morph shader if enabled
        if (useMorphShader)
        {
            Shader shaderToUse = morphShader;
            if (shaderToUse == null)
            {
                shaderToUse = Shader.Find("Custom/FlatGlobeMorph");
                if (shaderToUse == null)
                {
                    Debug.LogWarning("[WorldViewController] Morph shader not found, falling back to visibility switching.");
                    useMorphShader = false;
                }
            }
            
            if (useMorphShader && shaderToUse != null)
            {
                _worldMaterial = new Material(shaderToUse) { name = "WorldMorphMaterial_Runtime" };
                _worldMaterial.mainTexture = _bake.texture;
                _worldMaterial.SetTexture("_MainTex", _bake.texture);
                if (_bake.heightmap != null)
                {
                    _worldMaterial.SetTexture("_Heightmap", _bake.heightmap);
                }
                _worldMaterial.SetFloat("_FlatHeightScale", 0.1f);
                _worldMaterial.SetFloat("_GlobeHeightScale", 0.1f);
                _worldMaterial.SetFloat("_MapHeight", Mathf.PI * planetGen.radius);
                _worldMaterial.SetFloat("_PlanetRadius", planetGen.radius);
                _worldMaterial.SetFloat("_Morph", 0f); // Start at flat
                
                // Create single morphing mesh (subdivided plane that morphs to sphere)
                CreateMorphMesh(planetGen);
            }
        }
        
        // Fallback: Create separate flat and globe objects (original behavior)
        if (!useMorphShader || _morphMesh == null)
        {
            if (_worldMaterial == null)
            {
                var shader = Shader.Find("Unlit/Texture");
                if (shader == null) shader = Shader.Find("Standard");
                _worldMaterial = new Material(shader) { name = "WorldTextureMaterial_Runtime" };
            }
            _worldMaterial.mainTexture = _bake.texture;

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
        }

        // Ensure colliders are present and assigned for GPU-based picking
        // Priority: morph mesh (if using morph shader), then flat/globe colliders
        if (useMorphShader && _morphMesh != null)
        {
            // Morph mesh handles all morph values - single collider for all states
            _picker.morphMeshCollider = _morphMesh.GetComponent<Collider>();
            _picker.flatMapCollider = null; // Not needed when using morph mesh
            _picker.globeCollider = null;   // Not needed when using morph mesh
        }
        else
        {
            // Separate colliders for flat and globe (fallback mode)
            _picker.morphMeshCollider = null;
            _picker.flatMapCollider = _flatQuad != null ? _flatQuad.GetComponent<Collider>() : null;
            _picker.globeCollider = _globeSphere != null ? _globeSphere.GetComponent<Collider>() : null;
        }
        
        // Assign LUT for GPU-based picking (same LUT used by GPU texture baking)
        _picker.lutWidth = _bake.width;
        _picker.lutHeight = _bake.height;
        _picker.lut = _bake.lut;
        
        // Initialize TerrainOverlayGPU (Phase 6: GPU overlays)
        InitializeTerrainOverlays(planetGen);
    }
    
    /// <summary>
    /// Initialize TerrainOverlayGPU system for fog and ownership overlays.
    /// </summary>
    private void InitializeTerrainOverlays(PlanetGenerator planetGen)
    {
        var overlayGPU = FindAnyObjectByType<TerrainOverlayGPU>();
        if (overlayGPU != null && _bake.lut != null)
        {
            overlayGPU.Initialize(_bake.lut, _bake.width, _bake.height, textureWidth, textureHeight);
            
            // Subscribe to TileSystem events for overlay updates
            SubscribeToTileSystemEvents(overlayGPU);
            
            // Apply overlay textures to material
            ApplyOverlayTexturesToMaterial(overlayGPU);
        }
    }
    
    private TerrainOverlayGPU _cachedOverlayGPU;
    
    /// <summary>
    /// Subscribe to TileSystem events to update overlays when fog/ownership changes.
    /// </summary>
    private void SubscribeToTileSystemEvents(TerrainOverlayGPU overlayGPU)
    {
        if (TileSystem.Instance == null) return;
        
        _cachedOverlayGPU = overlayGPU;
        
        // Unsubscribe first to avoid duplicates
        TileSystem.Instance.OnTileOwnerChanged -= HandleTileOwnerChanged;
        TileSystem.Instance.OnFogChanged -= HandleFogChanged;
        
        // Subscribe to events
        TileSystem.Instance.OnTileOwnerChanged += HandleTileOwnerChanged;
        TileSystem.Instance.OnFogChanged += HandleFogChanged;
    }
    
    private void HandleTileOwnerChanged(int tile, int oldOwner, int newOwner)
    {
        if (_cachedOverlayGPU != null)
        {
            _cachedOverlayGPU.MarkTilesDirty(new[] { tile });
            _cachedOverlayGPU.UpdateOverlays();
        }
    }
    
    private void HandleFogChanged(int civId, List<int> changedTiles)
    {
        if (_cachedOverlayGPU != null)
        {
            _cachedOverlayGPU.MarkTilesDirty(changedTiles);
            _cachedOverlayGPU.UpdateOverlays();
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileOwnerChanged -= HandleTileOwnerChanged;
            TileSystem.Instance.OnFogChanged -= HandleFogChanged;
        }
    }
    
    /// <summary>
    /// Apply overlay textures (fog mask and ownership) to the material.
    /// </summary>
    private void ApplyOverlayTexturesToMaterial(TerrainOverlayGPU overlayGPU)
    {
        if (_worldMaterial == null) return;
        
        // Apply fog mask texture
        var fogMask = overlayGPU.GetFogMaskTexture();
        if (fogMask != null)
        {
            _worldMaterial.SetTexture("_FogMask", fogMask);
            _worldMaterial.SetFloat("_EnableFog", overlayGPU.EnableFogOverlay ? 1f : 0f);
        }
        
        // Apply ownership overlay texture
        var ownershipTex = overlayGPU.GetOwnershipTexture();
        if (ownershipTex != null)
        {
            _worldMaterial.SetTexture("_OwnershipOverlay", ownershipTex);
            _worldMaterial.SetFloat("_EnableOwnership", overlayGPU.EnableOwnershipOverlay ? 1f : 0f);
        }
    }
    
    /// <summary>
    /// Create a subdivided plane mesh that can morph between flat and globe positions.
    /// </summary>
    private void CreateMorphMesh(PlanetGenerator planetGen)
    {
        _morphMesh = new GameObject("MorphMesh");
        _morphMesh.transform.SetParent(worldRoot, false);
        _morphMesh.transform.position = planetGen.transform.position;
        _morphMesh.transform.localRotation = Quaternion.identity;
        _morphMesh.transform.localScale = Vector3.one;

        // Create subdivided plane mesh (similar to FlatMapTextureRenderer but for morphing)
        Mesh mesh = new Mesh();
        mesh.name = "MorphPlane";

        int segmentsX = 256; // Good detail for morphing
        int segmentsY = 128; // Maintain aspect ratio (2:1 for equirectangular)
        int vertexCount = (segmentsX + 1) * (segmentsY + 1);
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];

        float mapWidth = 2f * Mathf.PI * planetGen.radius * flatMapMarginMultiplier;
        float mapHeight = Mathf.PI * planetGen.radius * flatMapMarginMultiplier;

        // Generate flat plane vertices (morph shader will transform them)
        for (int y = 0; y <= segmentsY; y++)
        {
            for (int x = 0; x <= segmentsX; x++)
            {
                int index = y * (segmentsX + 1) + x;

                // UV coordinates (0-1) for equirectangular mapping
                float u = (float)x / segmentsX;
                float v = (float)y / segmentsY;
                uvs[index] = new Vector2(u, v);

                // Flat plane position (shader will morph this to sphere)
                float worldX = (u - 0.5f) * mapWidth;
                float worldZ = (v - 0.5f) * mapHeight;
                vertices[index] = new Vector3(worldX, flatMapY, worldZ);
                normals[index] = Vector3.up; // Will be morphed by shader
            }
        }

        // Generate triangles
        int[] triangles = new int[segmentsX * segmentsY * 6];
        int triIndex = 0;

        for (int y = 0; y < segmentsY; y++)
        {
            for (int x = 0; x < segmentsX; x++)
            {
                int current = y * (segmentsX + 1) + x;
                int next = current + segmentsX + 1;

                // First triangle
                triangles[triIndex++] = current;
                triangles[triIndex++] = next;
                triangles[triIndex++] = current + 1;

                // Second triangle
                triangles[triIndex++] = current + 1;
                triangles[triIndex++] = next;
                triangles[triIndex++] = next + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.normals = normals;
        mesh.RecalculateBounds();

        // Add components
        MeshFilter meshFilter = _morphMesh.AddComponent<MeshFilter>();
        meshFilter.mesh = mesh;

        MeshRenderer meshRenderer = _morphMesh.AddComponent<MeshRenderer>();
        meshRenderer.material = _worldMaterial;

        // Add collider for picking
        MeshCollider meshCollider = _morphMesh.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
    }
}

