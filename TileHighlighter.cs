using UnityEngine;

/// <summary>
/// Highlights the currently hovered tile via shader uniform.
/// Works with FlatMapDisplacement_URP shader.
/// </summary>
public class TileHighlighter : MonoBehaviour
{
    public static TileHighlighter Instance { get; private set; }
    
    [Header("Highlight Settings")]
    [SerializeField] private bool enableHighlight = true;
    [SerializeField] private Color highlightColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private float highlightWidth = 0.08f;
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseMin = 0.2f;
    [SerializeField] private float pulseMax = 0.4f;
    [SerializeField] private bool enablePulse = true;
    
    // Shader property IDs
    private static readonly int HighlightTileIndexID = Shader.PropertyToID("_HighlightTileIndex");
    private static readonly int HighlightColorID = Shader.PropertyToID("_HighlightColor");
    private static readonly int HighlightWidthID = Shader.PropertyToID("_HighlightWidth");
    private static readonly int EnableHighlightID = Shader.PropertyToID("_EnableTileHighlight");
    
    // State
    private int currentHighlightedTile = -1;
    private Material terrainMaterial;
    
    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void OnEnable()
    {
        // Subscribe to hover events
        if (TileHoverSystem.Instance != null)
        {
            TileHoverSystem.Instance.OnTileHoverEnter += OnTileHoverEnter;
            TileHoverSystem.Instance.OnTileHoverExit += OnTileHoverExit;
        }
    }
    
    private void OnDisable()
    {
        if (TileHoverSystem.Instance != null)
        {
            TileHoverSystem.Instance.OnTileHoverEnter -= OnTileHoverEnter;
            TileHoverSystem.Instance.OnTileHoverExit -= OnTileHoverExit;
        }
        
        ClearHighlight();
    }
    
    private void Start()
    {
        // Try to subscribe if TileHoverSystem wasn't ready in OnEnable
        if (TileHoverSystem.Instance != null)
        {
            TileHoverSystem.Instance.OnTileHoverEnter -= OnTileHoverEnter;
            TileHoverSystem.Instance.OnTileHoverExit -= OnTileHoverExit;
            TileHoverSystem.Instance.OnTileHoverEnter += OnTileHoverEnter;
            TileHoverSystem.Instance.OnTileHoverExit += OnTileHoverExit;
        }
        
        FindTerrainMaterial();
    }
    
    private void Update()
    {
        if (!enableHighlight || currentHighlightedTile < 0) return;
        
        // Pulse effect
        if (enablePulse && terrainMaterial != null)
        {
            float pulse = Mathf.Lerp(pulseMin, pulseMax, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            Color pulsedColor = highlightColor;
            pulsedColor.a = pulse;
            terrainMaterial.SetColor(HighlightColorID, pulsedColor);
        }
    }
    
    private void FindTerrainMaterial()
    {
        // Try HexMapChunkManager first
        var chunkManager = FindAnyObjectByType<HexMapChunkManager>();
        if (chunkManager != null && chunkManager.SharedMaterial != null)
        {
            terrainMaterial = chunkManager.SharedMaterial;
        }
        
        // Fallback to FlatMapTextureRenderer
        if (terrainMaterial == null)
        {
            var flatMap = FindAnyObjectByType<FlatMapTextureRenderer>();
            if (flatMap != null)
            {
                var renderer = flatMap.GetComponent<MeshRenderer>();
                if (renderer != null)
                    terrainMaterial = renderer.sharedMaterial;
            }
        }
        
        if (terrainMaterial != null)
        {
}
    }
    
    private void OnTileHoverEnter(int tileIndex, HexTileData tileData, Vector3 hitPoint)
    {
        if (!enableHighlight) return;
        
        SetHighlightedTile(tileIndex);
    }
    
    private void OnTileHoverExit(int tileIndex)
    {
        ClearHighlight();
    }
    
    public void SetHighlightedTile(int tileIndex)
    {
        currentHighlightedTile = tileIndex;
        
        if (terrainMaterial == null)
            FindTerrainMaterial();
        
        if (terrainMaterial != null)
        {
            terrainMaterial.SetFloat(EnableHighlightID, 1f);
            terrainMaterial.SetInt(HighlightTileIndexID, tileIndex);
            terrainMaterial.SetColor(HighlightColorID, highlightColor);
            terrainMaterial.SetFloat(HighlightWidthID, highlightWidth);
        }
        
        // Also set global for any shader that uses it
        Shader.SetGlobalInt(HighlightTileIndexID, tileIndex);
        Shader.SetGlobalColor(HighlightColorID, highlightColor);
        Shader.SetGlobalFloat(EnableHighlightID, 1f);
    }
    
    public void ClearHighlight()
    {
        currentHighlightedTile = -1;
        
        if (terrainMaterial != null)
        {
            terrainMaterial.SetFloat(EnableHighlightID, 0f);
            terrainMaterial.SetInt(HighlightTileIndexID, -1);
        }
        
        Shader.SetGlobalFloat(EnableHighlightID, 0f);
        Shader.SetGlobalInt(HighlightTileIndexID, -1);
    }
    
    /// <summary>
    /// Set highlight color at runtime.
    /// </summary>
    public void SetHighlightColor(Color color)
    {
        highlightColor = color;
        if (terrainMaterial != null && currentHighlightedTile >= 0)
        {
            terrainMaterial.SetColor(HighlightColorID, color);
        }
    }
}
