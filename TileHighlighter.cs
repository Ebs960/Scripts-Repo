using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Highlights the currently hovered tile via shader uniform.
/// Directly uses WorldPicker for tile detection — no dependency on TileHoverSystem.
/// Works with the shared terrain shader (uses the HexMapChunkManager shared material).
/// </summary>
public class TileHighlighter : MonoBehaviour
{
    public static TileHighlighter Instance { get; private set; }

    [Header("Tile Picking")]
    [Tooltip("Assign the WorldPicker from the scene. If empty, auto-finds one.")]
    [SerializeField] private WorldPicker worldPicker;

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

    private TileSystem _subscribedTS;

    private void Start()
    {
        FindTerrainMaterial();
        SubscribeToTileSystem();
    }

    private void OnDisable()
    {
        ClearHighlight();
        UnsubscribeFromTileSystem();
    }

    private void SubscribeToTileSystem()
    {
        var ts = TileSystem.Instance;
        if (ts == null) return;
        if (_subscribedTS == ts) return;
        UnsubscribeFromTileSystem();
        _subscribedTS = ts;
        ts.OnTileHovered += OnTileHovered;
        ts.OnTileHoverExited += OnTileHoverExited;
    }

    private void UnsubscribeFromTileSystem()
    {
        if (_subscribedTS != null)
        {
            _subscribedTS.OnTileHovered -= OnTileHovered;
            _subscribedTS.OnTileHoverExited -= OnTileHoverExited;
            _subscribedTS = null;
        }
    }

    private void OnTileHovered(int tileIndex, Vector3 worldPos)
    {
        if (!enableHighlight) return;
        if (tileIndex >= 0 && tileIndex != currentHighlightedTile)
            SetHighlightedTile(tileIndex);
    }

    private void OnTileHoverExited()
    {
        if (currentHighlightedTile >= 0)
            ClearHighlight();
    }

    private void Update()
    {
        // Re-subscribe if TileSystem was recreated (planet switch)
        if (_subscribedTS == null || _subscribedTS != TileSystem.Instance)
            SubscribeToTileSystem();

        // Pulse effect — only material color write, no raycast
        if (enablePulse && currentHighlightedTile >= 0 && terrainMaterial != null)
        {
            float pulse = Mathf.Lerp(pulseMin, pulseMax, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            Color pulsedColor = highlightColor;
            pulsedColor.a = pulse;
            terrainMaterial.SetColor(HighlightColorID, pulsedColor);
        }
    }

    private void FindTerrainMaterial()
    {
        var chunkManager = FindAnyObjectByType<HexMapChunkManager>();
        if (chunkManager != null && chunkManager.SharedMaterial != null)
        {
            terrainMaterial = chunkManager.SharedMaterial;
        }
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
