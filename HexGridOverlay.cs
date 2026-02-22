using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Renders a hex grid overlay on top of the terrain using line renderers.
/// This is a standalone system that doesn't rely on shader properties.
/// Attach to the same GameObject as HexMapChunkManager or a child of it.
/// </summary>
public class HexGridOverlay : MonoBehaviour
{
    [Header("Grid Settings")]
    [Tooltip("Show or hide the hex grid overlay")]
    [SerializeField] private bool showGrid = false;
    
    [Tooltip("Color of the hex grid lines")]
    [SerializeField] private Color gridColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
    
    [Tooltip("Width of the hex grid lines")]
    [SerializeField, Range(0.01f, 0.5f)] private float lineWidth = 0.05f;
    
    [Tooltip("Height offset above terrain surface to avoid z-fighting")]
    [SerializeField] private float heightOffset = 0.3f;
    
    [Tooltip("Material for line renderers (optional - will create default if null)")]
    [SerializeField] private Material lineMaterial;
    
    [Header("Performance")]
    [Tooltip("Maximum number of hex cells to render (for performance)")]
    [SerializeField] private int maxHexesToRender = 10000;
    
    [Tooltip("Only render hexes within this distance from camera")]
    [SerializeField] private float renderDistance = 500f;
    
    [Tooltip("Update interval in seconds (0 = every frame)")]
    [SerializeField] private float updateInterval = 0.1f;
    
    // References
    private HexGrid grid;
    private PlanetGenerator planetGenerator;
    private HexMapChunkManager chunkManager;
    private Camera mainCamera;
    
    // Terrain height sampling
    private float displacementStrength = 1f;
    private float flatY = 0f;
    private Texture2D heightmapTexture;
    
    // Line renderer pool
    private List<LineRenderer> lineRendererPool = new List<LineRenderer>();
    private int activeLineRenderers = 0;
    private Transform lineRendererParent;
    // Track last-known show state so inspector/runtime changes update the parent active state
    private bool _lastShowGridState = false;
    
    // Timing
    private float lastUpdateTime;

    // Event subscription
    private bool _subscribedToPlanetReady = false;
    
    // Hex geometry constants (pointy-top hex)
    private static readonly float SQRT3 = Mathf.Sqrt(3f);
    private static readonly Vector2[] HEX_CORNERS = new Vector2[6]
    {
        new Vector2(0f, 1f),                          // Top
        new Vector2(SQRT3 / 2f, 0.5f),               // Top-right
        new Vector2(SQRT3 / 2f, -0.5f),              // Bottom-right
        new Vector2(0f, -1f),                         // Bottom
        new Vector2(-SQRT3 / 2f, -0.5f),             // Bottom-left
        new Vector2(-SQRT3 / 2f, 0.5f)               // Top-left
    };
    
    void Start()
    {
        mainCamera = Camera.main;
        Debug.Log($"[HexGridOverlay] Start() — showGrid={showGrid}, mainCamera={(mainCamera != null ? mainCamera.name : "NULL")}, gameObject={gameObject.name}, active={gameObject.activeInHierarchy}");
        
        // Create parent for line renderers
        lineRendererParent = new GameObject("HexGridLines").transform;
        lineRendererParent.SetParent(transform);
        lineRendererParent.localPosition = Vector3.zero;
        lineRendererParent.localRotation = Quaternion.identity;
        
        // Create default material if not assigned
        if (lineMaterial == null)
        {
            lineMaterial = CreateDefaultLineMaterial();
            Debug.Log($"[HexGridOverlay] Created default line material: shader={lineMaterial?.shader?.name ?? "NULL"}, color={lineMaterial?.color}");
        }
        else
        {
            Debug.Log($"[HexGridOverlay] Using assigned material: {lineMaterial.name}, shader={lineMaterial.shader?.name ?? "NULL"}");
        }
        
        // Try to find references silently at startup (avoid noisy warnings while systems initialize)
        FindReferences(silent: true);

        Debug.Log($"[HexGridOverlay] After FindReferences — grid={(grid != null ? $"found (TileCount={grid.TileCount}, Width={grid.Width}, MapWidth={grid.MapWidth})" : "NULL")}, planetGenerator={(planetGenerator != null ? planetGenerator.name : "NULL")}");

        // Subscribe to GameManager planet-ready event so we can acquire the grid when it's available.
        if (GameManager.Instance != null && !_subscribedToPlanetReady)
        {
            GameManager.Instance.OnPlanetReady += HandlePlanetReady;
            _subscribedToPlanetReady = true;
        }
        
        // Initial visibility
        lineRendererParent.gameObject.SetActive(showGrid);
        _lastShowGridState = showGrid;
        
        if (!showGrid)
        {
            Debug.LogWarning("[HexGridOverlay] showGrid is FALSE — grid lines are hidden by default. Call SetGridVisible(true) or enable 'Show Grid' in the Inspector to display them.");
        }
    }
    
    // Debug: track whether we've logged the "first frame" info yet
    private bool _loggedFirstUpdate = false;
    private int _updateCallCount = 0;
    
    void Update()
    {
        _updateCallCount++;
        // React to runtime/inspector changes when toggled in Play mode or inspector
        if (_lastShowGridState != showGrid)
        {
            SetGridVisible(showGrid);
        }

        if (!showGrid)
        {
            if (!_loggedFirstUpdate)
            {
                Debug.LogWarning("[HexGridOverlay] Update skipped: showGrid is FALSE");
                _loggedFirstUpdate = true;
            }
            return;
        }
        
        if (grid == null)
        {
            // Retry finding references periodically
            if (_updateCallCount % 60 == 0)
            {
                Debug.LogWarning("[HexGridOverlay] Update skipped: grid is NULL — retrying FindReferences...");
                FindReferences();
                if (grid != null)
                {
                    Debug.Log($"[HexGridOverlay] Grid found on retry! TileCount={grid.TileCount}, Width={grid.Width}");
                }
            }
            return;
        }
        
        // Throttle updates
        if (updateInterval > 0 && Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;
        
        UpdateGridLines();
    }
    
    /// <summary>
    /// Toggle grid visibility
    /// </summary>
    public void SetGridVisible(bool visible)
    {
        Debug.Log($"[HexGridOverlay] SetGridVisible({visible}) called — was showGrid={showGrid}, grid={(grid != null ? "OK" : "NULL")}");
        showGrid = visible;
        if (lineRendererParent != null)
        {
            lineRendererParent.gameObject.SetActive(visible);
        }
        
        if (visible && grid == null)
        {
            FindReferences(silent: false);
        }
        
        // Reset first-update logging so we get fresh diagnostics when toggled on
        if (visible)
        {
            _loggedFirstGridUpdate = false;
            _loggedFirstUpdate = false;
        }
        _lastShowGridState = showGrid;
    }
    
    void OnValidate()
    {
        // Editor inspector toggle: update parent GameObject active state immediately when possible.
        if (lineRendererParent == null)
        {
            var child = transform.Find("HexGridLines");
            if (child != null) lineRendererParent = child;
        }

        if (lineRendererParent != null)
        {
            lineRendererParent.gameObject.SetActive(showGrid);
            _lastShowGridState = showGrid;
        }
    }
    
    /// <summary>
    /// Configure grid appearance
    /// </summary>
    public void ConfigureGrid(Color? color = null, float? width = null)
    {
        if (color.HasValue) gridColor = color.Value;
        if (width.HasValue) lineWidth = Mathf.Clamp(width.Value, 0.01f, 0.5f);
        
        // Update existing line renderers
        UpdateLineRendererAppearance();
    }
    
    /// <summary>
    /// Check if grid is currently visible
    /// </summary>
    public bool IsGridVisible => showGrid;
    
    private void FindReferences(bool silent = false)
    {
        // Find HexMapChunkManager
        if (chunkManager == null)
        {
            chunkManager = GetComponent<HexMapChunkManager>();
            if (chunkManager == null)
            {
                chunkManager = GetComponentInParent<HexMapChunkManager>();
            }
        }
        
        if (chunkManager != null)
        {
            grid = chunkManager.Grid;
            displacementStrength = chunkManager.DisplacementStrength;
            // Try to pull the runtime heightmap directly from the shared terrain material.
            // This lets the grid follow the *same bilinear-smoothed heightmap* that the shader uses.
            // Without this, edges look "weird" because the terrain slopes at tile boundaries while
            // the overlay lines sit at a constant height per tile.
            if (chunkManager.SharedMaterial != null && chunkManager.SharedMaterial.HasProperty("_Heightmap"))
            {
                heightmapTexture = chunkManager.SharedMaterial.GetTexture("_Heightmap") as Texture2D;
            }
            if (!silent)
                Debug.Log($"[HexGridOverlay] FindReferences — Found HexMapChunkManager on '{chunkManager.gameObject.name}', " +
                    $"Grid={(grid != null ? $"OK (TileCount={grid.TileCount})" : "NULL (chunk manager has no grid yet)")}, " +
                    $"displacementStrength={displacementStrength}");
        }
        else
        {
            if (!silent)
                Debug.LogWarning($"[HexGridOverlay] FindReferences — HexMapChunkManager NOT found on '{gameObject.name}' or parents. " +
                    $"Hierarchy: {GetHierarchyPath(transform)}");
        }
        
        // Find PlanetGenerator
        if (planetGenerator == null)
        {
            planetGenerator = GetComponentInParent<PlanetGenerator>();
            if (planetGenerator == null && GameManager.Instance != null)
            {
                planetGenerator = GameManager.Instance.GetCurrentPlanetGenerator();
            }
        }
        
        if (planetGenerator != null && grid == null)
        {
            grid = planetGenerator.Grid;
            Debug.Log($"[HexGridOverlay] FindReferences — Using PlanetGenerator '{planetGenerator.name}', Grid={(grid != null ? $"OK (TileCount={grid.TileCount})" : "NULL")}");
        }
        
        if (grid == null && !silent)
        {
            Debug.LogWarning("[HexGridOverlay] FindReferences — Could NOT find a valid HexGrid from either HexMapChunkManager or PlanetGenerator. Grid lines will not render until grid is available.");
        }
    }

    private void HandlePlanetReady(int planetIndex)
    {
        // When a planet becomes ready, try to find the grid (this will pick up HexMapChunkManager or PlanetGenerator)
        FindReferences(silent: false);
        if (grid != null)
        {
            Debug.Log($"[HexGridOverlay] HandlePlanetReady — acquired grid (TileCount={grid.TileCount}).");
            // Reset logging flags so first-update diagnostics run again when grid becomes available
            _loggedFirstGridUpdate = false;
            _loggedFirstUpdate = false;
        }
    }
    
    /// <summary>
    /// Helper to print the transform hierarchy for debugging
    /// </summary>
    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + " > " + path;
        }
        return path;
    }
    
    // Debug: only log detailed update info once
    private bool _loggedFirstGridUpdate = false;
    
    private void UpdateGridLines()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null || grid == null)
        {
            if (!_loggedFirstGridUpdate)
            {
                Debug.LogWarning($"[HexGridOverlay] UpdateGridLines bail — mainCamera={(mainCamera != null ? "OK" : "NULL")}, grid={(grid != null ? "OK" : "NULL")}");
            }
            return;
        }
        
        Vector3 camPos = mainCamera.transform.position;
        float renderDistSq = renderDistance * renderDistance;
        
        // Reset active count
        activeLineRenderers = 0;
        
        // Get tile count
        int tileCount = Mathf.Min(grid.TileCount, maxHexesToRender);
        
        // Calculate hex radius from grid
        float hexRadius = CalculateHexRadius();
        
        int tilesInRange = 0;
        int edgesDrawn = 0;
        
        // Pre-fetch neighbor data availability
        bool hasNeighborData = grid.neighbors != null && grid.neighbors.Length == grid.TileCount;
        
        // Cache transform for UV conversion
        Transform mapTransform = (chunkManager != null) ? chunkManager.transform : transform;

        for (int i = 0; i < tileCount; i++)
        {
            Vector3 tileCenter = grid.tileCenters[i];
            
            // Distance culling
            float distSq = (tileCenter - camPos).sqrMagnitude;
            if (distSq > renderDistSq) continue;
            
            tilesInRange++;
            
            // Base height for this tile.
            // If we can sample the runtime heightmap (preferred), do it per-point (corners).
            // Otherwise fall back to the tile's stored elevation (center) which can mismatch at edges.
            float tileElevation = 0f;
            if (planetGenerator != null)
            {
                HexTileData tileData = planetGenerator.GetHexTileData(i);
                if (tileData != null) tileElevation = tileData.elevation;
            }
            float visualYCenter = flatY + tileElevation * displacementStrength + heightOffset;
            
            // (old per-corner height sampling removed) We'll render simple flat hexagons per-tile below.
            
            // Simpler rendering: draw a closed hexagon per tile using shared corner positions when available.
            LineRenderer lr = GetOrCreateLineRenderer();
            lr.loop = true;

            if (grid.tileCorners != null && grid.tileCorners.Length == grid.TileCount &&
                grid.CornerVertices != null && grid.CornerVertices.Count > 0 && grid.tileCorners[i] != null && grid.tileCorners[i].Count >= 6)
            {
                lr.positionCount = 6;
                for (int c = 0; c < 6; c++)
                {
                    int cornerIdx = grid.tileCorners[i][c];
                    Vector3 cornerPos = grid.CornerVertices[cornerIdx];
                    cornerPos.y = visualYCenter; // flat Y across tile for consistent outlines
                    lr.SetPosition(c, cornerPos);
                }
                lr.enabled = true;
                edgesDrawn += 6;
            }
            else
            {
                // Fallback: compute around center (flat Y)
                lr.positionCount = 6;
                for (int c = 0; c < 6; c++)
                {
                    Vector3 cornerWorld = new Vector3(
                        tileCenter.x + HEX_CORNERS[c].x * hexRadius,
                        visualYCenter,
                        tileCenter.z + HEX_CORNERS[c].y * hexRadius
                    );
                    lr.SetPosition(c, cornerWorld);
                }
                lr.enabled = true;
                edgesDrawn += 6;
            }
        }
        
        // Disable unused line renderers
        for (int i = activeLineRenderers; i < lineRendererPool.Count; i++)
        {
            lineRendererPool[i].enabled = false;
        }
        
        // Log first successful update with detailed info
        if (!_loggedFirstGridUpdate)
        {
            _loggedFirstGridUpdate = true;
            
            // Sample a tile center to check positions
            Vector3 sampleCenter = tileCount > 0 ? grid.tileCenters[0] : Vector3.zero;
            
            Debug.Log($"[HexGridOverlay] === FIRST GRID UPDATE ===\n" +
                $"  Total tiles: {grid.TileCount}, Cap: {maxHexesToRender}, Checked: {tileCount}\n" +
                $"  Tiles in range: {tilesInRange}, Edges drawn: {edgesDrawn}\n" +
                $"  Active LineRenderers: {activeLineRenderers}, Pool size: {lineRendererPool.Count}\n" +
                $"  Hex radius: {hexRadius:F4}, hasNeighborData: {hasNeighborData}\n" +
                $"  Camera pos: {camPos}, Render distance: {renderDistance}\n" +
                $"  Sample tile[0] center: {sampleCenter}\n" +
                $"  Distance from cam to tile[0]: {Vector3.Distance(camPos, sampleCenter):F1}\n" +
                $"  lineWidth: {lineWidth}, gridColor: {gridColor}\n" +
                $"  Material: {(lineMaterial != null ? lineMaterial.shader.name : "NULL")}\n" +
                $"  displacementStrength: {displacementStrength}, flatY: {flatY}\n" +
                $"  Parent active: {lineRendererParent?.gameObject.activeInHierarchy}");
            
            if (tilesInRange == 0)
            {
                Debug.LogWarning($"[HexGridOverlay] NO TILES IN RANGE! Camera is at {camPos} but tiles are around {sampleCenter}. " +
                    $"Distance={Vector3.Distance(camPos, sampleCenter):F1} > renderDistance={renderDistance}. " +
                    $"Try increasing renderDistance or check if camera and grid are in the same coordinate space.");
            }
            
            if (hexRadius < 0.01f)
            {
                Debug.LogWarning($"[HexGridOverlay] Hex radius is extremely small ({hexRadius:F6}). Grid may not be visible. " +
                    $"MapWidth={grid.MapWidth}, gridWidth={grid.Width}");
            }
        }
    }
    
    /// <summary>
    /// Draw a single edge segment between two corner positions.
    /// Uses one LineRenderer per edge to avoid doubled lines on shared hex borders.
    /// </summary>
    private void DrawEdge(Vector3 from, Vector3 to)
    {
        LineRenderer lr = GetOrCreateLineRenderer();
        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.enabled = true;
    }
    
    private LineRenderer GetOrCreateLineRenderer()
    {
        if (activeLineRenderers < lineRendererPool.Count)
        {
            return lineRendererPool[activeLineRenderers++];
        }
        
        // Create new line renderer
        GameObject go = new GameObject($"HexLine_{lineRendererPool.Count}");
        go.transform.SetParent(lineRendererParent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.material = lineMaterial;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.startColor = gridColor;
        lr.endColor = gridColor;
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.allowOcclusionWhenDynamic = false;
        
        lineRendererPool.Add(lr);
        activeLineRenderers++;
        
        return lr;
    }
    
    private void UpdateLineRendererAppearance()
    {
        foreach (var lr in lineRendererPool)
        {
            lr.material = lineMaterial;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.startColor = gridColor;
            lr.endColor = gridColor;
        }
    }
    
    private float CalculateHexRadius()
    {
        if (grid == null) return 1f;
        
        // Estimate hex radius from map dimensions and tile count
        float mapWidth = grid.MapWidth;
        int tilesX = grid.Width;
        
        if (tilesX > 0 && mapWidth > 0)
        {
            // For pointy-top hexes: horizontal spacing = sqrt(3) * radius
            return mapWidth / (tilesX * SQRT3);
        }
        
        return 1f;
    }
    
    private Material CreateDefaultLineMaterial()
    {
        // Try to use URP/HDRP unlit shader, fall back to standard
        Shader shader = Shader.Find("HDRP/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        
        if (shader == null)
        {
            Debug.LogError("[HexGridOverlay] Could not find ANY shader for line material! Grid lines will not render.");
            return null;
        }
        
        Debug.Log($"[HexGridOverlay] Creating default material with shader: {shader.name}");
        
        Material mat = new Material(shader);
        
        // Set color using the correct property for each shader pipeline
        // HDRP/Unlit uses _UnlitColor, URP/Unlit uses _BaseColor, Unlit/Color uses _Color
        if (shader.name.Contains("HDRP"))
        {
            mat.SetColor("_UnlitColor", gridColor);
            // HDRP needs surface type set to Transparent for alpha
            if (mat.HasProperty("_SurfaceType"))
                mat.SetFloat("_SurfaceType", 1); // 1 = Transparent
            if (mat.HasProperty("_BlendMode"))
                mat.SetFloat("_BlendMode", 0); // 0 = Alpha
            // Render on top to avoid z-fighting with terrain
            mat.renderQueue = 3100;
            Debug.Log($"[HexGridOverlay] Configured HDRP/Unlit material — _UnlitColor={gridColor}");
        }
        else if (shader.name.Contains("Universal"))
        {
            mat.SetColor("_BaseColor", gridColor);
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_ZWrite", 0);
            mat.renderQueue = 3100;
            Debug.Log($"[HexGridOverlay] Configured URP/Unlit material — _BaseColor={gridColor}");
        }
        else
        {
            // Unlit/Color or Sprites/Default
            mat.color = gridColor;
            mat.renderQueue = 3100;
            Debug.Log($"[HexGridOverlay] Configured fallback material — color={gridColor}");
        }
        
        return mat;
    }
    
    void OnDestroy()
    {
        // Cleanup
        foreach (var lr in lineRendererPool)
        {
            if (lr != null && lr.gameObject != null)
            {
                Destroy(lr.gameObject);
            }
        }
        lineRendererPool.Clear();
        
        if (lineMaterial != null && lineMaterial.name.Contains("Instance"))
        {
            Destroy(lineMaterial);
        }
        // Unsubscribe from GameManager event
        if (GameManager.Instance != null && _subscribedToPlanetReady)
        {
            GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
            _subscribedToPlanetReady = false;
        }
    }

    void OnDisable()
    {
        if (GameManager.Instance != null && _subscribedToPlanetReady)
        {
            GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
            _subscribedToPlanetReady = false;
        }
    }
}
