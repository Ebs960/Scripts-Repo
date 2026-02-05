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
    
    [Tooltip("Height offset above terrain to avoid z-fighting")]
    [SerializeField] private float heightOffset = 0.1f;
    
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
    private Camera mainCamera;
    
    // Line renderer pool
    private List<LineRenderer> lineRendererPool = new List<LineRenderer>();
    private int activeLineRenderers = 0;
    private Transform lineRendererParent;
    
    // Timing
    private float lastUpdateTime;
    
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
        
        // Create parent for line renderers
        lineRendererParent = new GameObject("HexGridLines").transform;
        lineRendererParent.SetParent(transform);
        lineRendererParent.localPosition = Vector3.zero;
        lineRendererParent.localRotation = Quaternion.identity;
        
        // Create default material if not assigned
        if (lineMaterial == null)
        {
            lineMaterial = CreateDefaultLineMaterial();
        }
        
        // Try to find references
        FindReferences();
        
        // Initial visibility
        lineRendererParent.gameObject.SetActive(showGrid);
    }
    
    void Update()
    {
        if (!showGrid || grid == null) return;
        
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
        showGrid = visible;
        if (lineRendererParent != null)
        {
            lineRendererParent.gameObject.SetActive(visible);
        }
        
        if (visible && grid == null)
        {
            FindReferences();
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
    
    private void FindReferences()
    {
        // Find HexMapChunkManager
        var chunkManager = GetComponent<HexMapChunkManager>();
        if (chunkManager == null)
        {
            chunkManager = GetComponentInParent<HexMapChunkManager>();
        }
        
        if (chunkManager != null)
        {
            grid = chunkManager.Grid;
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
        }
    }
    
    private void UpdateGridLines()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null || grid == null) return;
        
        Vector3 camPos = mainCamera.transform.position;
        float renderDistSq = renderDistance * renderDistance;
        
        // Reset active count
        activeLineRenderers = 0;
        
        // Get tile count
        int tileCount = Mathf.Min(grid.TileCount, maxHexesToRender);
        
        // Calculate hex radius from grid
        float hexRadius = CalculateHexRadius();
        
        for (int i = 0; i < tileCount; i++)
        {
            Vector3 tileCenter = grid.tileCenters[i];
            
            // Distance culling
            float distSq = (tileCenter - camPos).sqrMagnitude;
            if (distSq > renderDistSq) continue;
            
            // Draw hex outline
            DrawHexOutline(tileCenter, hexRadius);
        }
        
        // Disable unused line renderers
        for (int i = activeLineRenderers; i < lineRendererPool.Count; i++)
        {
            lineRendererPool[i].enabled = false;
        }
    }
    
    private void DrawHexOutline(Vector3 center, float radius)
    {
        LineRenderer lr = GetOrCreateLineRenderer();
        
        // Set positions for hex outline (7 points to close the loop)
        Vector3[] positions = new Vector3[7];
        for (int i = 0; i < 6; i++)
        {
            positions[i] = center + new Vector3(
                HEX_CORNERS[i].x * radius,
                heightOffset,
                HEX_CORNERS[i].y * radius
            );
        }
        positions[6] = positions[0]; // Close the loop
        
        lr.positionCount = 7;
        lr.SetPositions(positions);
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
        
        Material mat = new Material(shader);
        mat.color = gridColor;
        mat.SetFloat("_ZWrite", 0);
        mat.renderQueue = 3000; // Transparent queue
        
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
    }
}
