using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Fog of War overlay using HDRP Decal Projector.
/// Projects a fog mask texture onto the terrain from above.
/// Uses a procedurally created material - no Shader Graph required.
/// 
/// Setup:
/// 1. Attach this script to a GameObject
/// 2. Assign the TerrainOverlayGPU reference (or it will auto-find)
/// 3. The script creates its own decal material automatically
/// </summary>
public class FogOfWarDecal : MonoBehaviour
{
    [Header("References")]
    [Tooltip("TerrainOverlayGPU that provides the fog mask RenderTexture")]
    [SerializeField] private TerrainOverlayGPU terrainOverlayGPU;
    
    [Header("Decal Settings")]
    [Tooltip("Automatically size the decal to cover the map")]
    [SerializeField] private bool autoSizeToMap = true;
    
    [Tooltip("Height above the terrain for the decal projector")]
    [SerializeField] private float projectorHeight = 100f;
    
    [Tooltip("Projection depth (how far down the decal projects)")]
    [SerializeField] private float projectionDepth = 200f;
    
    [Tooltip("Padding around map edges")]
    [SerializeField] private float mapPadding = 10f;
    
    [Header("Fog Appearance")]
    [Tooltip("Fog color for unexplored areas")]
    [SerializeField] private Color unexploredColor = new Color(0f, 0f, 0f, 1f);
    
    [Tooltip("Fog color for explored but not visible areas")]
    [SerializeField] private Color exploredColor = new Color(0.1f, 0.1f, 0.1f, 0.6f);
    
    [Tooltip("Enable fog of war rendering")]
    [SerializeField] private bool enableFog = true;
    
    // Component references
    private DecalProjector decalProjector;
    private Material decalMaterial;
    
    // Shader property IDs
    private static readonly int BaseColorMapID = Shader.PropertyToID("_BaseColorMap");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    
    void Awake()
    {
        // Add DecalProjector if not present
        decalProjector = GetComponent<DecalProjector>();
        if (decalProjector == null)
        {
            decalProjector = gameObject.AddComponent<DecalProjector>();
        }
        
        // Find TerrainOverlayGPU if not assigned
        if (terrainOverlayGPU == null)
        {
            terrainOverlayGPU = FindAnyObjectByType<TerrainOverlayGPU>();
        }
        
        // Create procedural decal material
        CreateDecalMaterial();
    }
    
    void Start()
    {
        if (autoSizeToMap)
        {
            SizeDecalToMap();
        }
        
        SetupDecalProjector();
        UpdateFogTexture();
    }
    
    private RenderTexture _lastFogMask;

    void LateUpdate()
    {
        // Only re-assign the texture when the RenderTexture reference actually changes
        if (!enableFog || terrainOverlayGPU == null) return;
        RenderTexture current = terrainOverlayGPU.GetFogMaskTexture();
        if (current != _lastFogMask)
        {
            _lastFogMask = current;
            if (current != null && decalMaterial != null)
                decalMaterial.SetTexture(BaseColorMapID, current);
        }
    }
    
    /// <summary>
    /// Create a procedural decal material using HDRP's decal shader
    /// </summary>
    private void CreateDecalMaterial()
    {
        // Try to find HDRP Decal shader
        Shader decalShader = Shader.Find("HDRP/Decal");
        if (decalShader == null)
        {
            decalShader = Shader.Find("Shader Graphs/Decal");
        }
        if (decalShader == null)
        {
            // Fallback to any available decal-like shader
            decalShader = Shader.Find("Decal");
        }
        
        if (decalShader != null)
        {
            decalMaterial = new Material(decalShader);
            decalMaterial.name = "FogOfWar_ProceduralDecal";
            // HDRP decals are rendered with instancing (DrawMeshInstanced). If instancing is not enabled
            // on the material, HDRP will throw every frame during the decal pass and can break startup/UI.
            decalMaterial.enableInstancing = true;
            decalMaterial.SetColor(BaseColorID, unexploredColor);
            
            // Configure for alpha blending
            decalMaterial.SetFloat("_DecalBlend", 1f);
            
            Debug.Log($"[FogOfWarDecal] Created procedural decal material using shader: {decalShader.name}");
        }
        else
        {
            Debug.LogError("[FogOfWarDecal] Could not find HDRP Decal shader. Fog of war decal will not work.");
        }
    }
    
    /// <summary>
    /// Enable or disable the fog of war overlay
    /// </summary>
    public void SetFogEnabled(bool enabled)
    {
        enableFog = enabled;
        
        if (decalProjector != null)
        {
            decalProjector.enabled = enabled;
        }
    }
    
    /// <summary>
    /// Set the fog colors
    /// </summary>
    public void SetFogColors(Color unexplored, Color explored)
    {
        unexploredColor = unexplored;
        exploredColor = explored;
        
        if (decalMaterial != null)
        {
            decalMaterial.SetColor(BaseColorID, unexploredColor);
        }
    }
    
    /// <summary>
    /// Check if fog is currently enabled
    /// </summary>
    public bool IsFogEnabled => enableFog;
    
    private void SetupDecalProjector()
    {
        if (decalProjector == null) return;
        
        // Configure the decal projector for top-down projection
        decalProjector.pivot = new Vector3(0f, 0.5f, 0f);
        decalProjector.fadeFactor = 1f;
        decalProjector.enabled = enableFog;
        decalProjector.material = decalMaterial;
        
        // Point straight down
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
    
    private void SizeDecalToMap()
    {
        if (decalProjector == null) return;
        
        float mapWidth = 100f;
        float mapHeight = 100f;
        
        // Try to get map dimensions from various sources
        var planetGen = FindAnyObjectByType<PlanetGenerator>();
        if (planetGen != null && planetGen.Grid != null)
        {
            mapWidth = planetGen.Grid.MapWidth;
            mapHeight = planetGen.Grid.MapHeight;
        }
        else if (GameManager.Instance != null)
        {
            mapWidth = GameManager.Instance.GetFlatMapWidth();
            mapHeight = GameManager.Instance.GetFlatMapHeight();
        }
        
        // Set decal size with padding
        decalProjector.size = new Vector3(
            mapWidth + mapPadding * 2f,
            mapHeight + mapPadding * 2f,
            projectionDepth
        );
        
        // Position at map center, above the terrain
        Vector3 mapCenter = new Vector3(mapWidth / 2f, projectorHeight, mapHeight / 2f);
        transform.position = mapCenter;
        
        Debug.Log($"[FogOfWarDecal] Sized to map: {mapWidth}x{mapHeight}, position: {mapCenter}");
    }
    
    private void UpdateFogTexture()
    {
        if (terrainOverlayGPU == null || decalMaterial == null) return;
        
        RenderTexture fogMask = terrainOverlayGPU.GetFogMaskTexture();
        if (fogMask != null)
        {
            decalMaterial.SetTexture(BaseColorMapID, fogMask);
        }
    }
    
    /// <summary>
    /// Manually refresh the decal size to match current map dimensions
    /// </summary>
    public void RefreshSize()
    {
        SizeDecalToMap();
    }
    
    void OnDestroy()
    {
        // Clean up procedural material
        if (decalMaterial != null)
        {
            Destroy(decalMaterial);
        }
    }
    
    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Visualize the decal projection area in editor
        Gizmos.color = new Color(0.2f, 0.2f, 0.8f, 0.3f);
        
        var projector = GetComponent<DecalProjector>();
        if (projector != null)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, projector.size);
        }
    }
    #endif
}
