using UnityEngine;
using TMPro;
using System.Text;

/// <summary>
/// Shows per-tile yields when the mouse hovers the planet mesh.
/// It now stays dormant until GameSceneInitializer calls SetReady().
/// </summary>
public class TileInfoDisplay : MonoBehaviour
{
    public static TileInfoDisplay Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Text element that prints the tile's data.")]
    public TextMeshProUGUI infoText;

    [Header("UI Prefab")]
    [Tooltip("Prefab that contains the TextMeshProUGUI. Instantiated if one isn't assigned in the scene.")]
    public GameObject tileInfoUIPrefab;
    [Tooltip("Optional parent for the prefab (e.g. your Canvas).")]
    public Transform uiParent;

    [Header("Highlight Marker")]
    [Tooltip("Prefab for a ring / disc that marks the hovered tile.")]
    public GameObject highlightMarkerPrefab;

    // ─────────────────────────────────────────────────────────────
    // Private fields
    // ─────────────────────────────────────────────────────────────
    GameObject uiRoot;              // top-level object that holds the text
    GameObject highlightMarker;     // ring / disc instance
    readonly StringBuilder sb = new StringBuilder();
    bool isReady = false;           // stays false until map is finished
    
    // State for current hover
    int lastHoveredTileIndex = -1;


    // ─────────────────────────────────────────────────────────────
    // Unity - Awake
    // ─────────────────────────────────────────────────────────────
    void Awake()
    {
        // Singleton
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // (1) Build or fetch the UI object
        if (infoText == null && tileInfoUIPrefab != null)
        {
            GameObject uiInstance = Instantiate(tileInfoUIPrefab, uiParent ?? transform);
            infoText = uiInstance.GetComponentInChildren<TextMeshProUGUI>();
        }
        uiRoot = infoText != null ? infoText.transform.parent.gameObject : gameObject;
        uiRoot.SetActive(false);                // ← HIDDEN UNTIL READY
        ClearDisplay();

        // (2) Build highlight marker (or fallback)
        if (highlightMarkerPrefab != null)
            highlightMarker = Instantiate(highlightMarkerPrefab);
        else
        {
            // simple yellow ring
            highlightMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            highlightMarker.name = "FallbackHighlightMarker";
            highlightMarker.transform.localScale = new Vector3(1.1f, 0.05f, 1.1f);
            var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, .92f, .2f, .6f) };
            highlightMarker.GetComponent<Renderer>().material = mat;
            Destroy(highlightMarker.GetComponent<Collider>());
        }
        highlightMarker.SetActive(false);       
    }

    // ─────────────────────────────────────────────────────────────
    // Public API: called by GameSceneInitializer when loading ends
    // ─────────────────────────────────────────────────────────────
    public void SetReady(bool value = true)
    {
        isReady = value;
        uiRoot.SetActive(value);
        if (!value) 
        {
            highlightMarker.SetActive(false);
            lastHoveredTileIndex = -1;
            lastHoveredTileIndex = -1;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Unity - Update
    // ─────────────────────────────────────────────────────────────
    void Update() { }

    private void OnEnable()
    {
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileHovered += OnTileHoveredEvent;
            TileSystem.Instance.OnTileHoverExited += OnTileExitedEvent;
        }
    }

    private void OnDisable()
    {
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileHovered -= OnTileHoveredEvent;
            TileSystem.Instance.OnTileHoverExited -= OnTileExitedEvent;
        }
    }

    private void OnTileHoveredEvent(int tileIndex, Vector3 worldPos)
    {
        if (!isReady) return;

    var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(tileIndex) : null;
        if (tileData == null) return;
    Vector3 tileSurfacePosition = TileSystem.Instance != null ? TileSystem.Instance.GetTileSurfacePosition(tileIndex, 0.1f) : worldPos;
        highlightMarker.transform.position = tileSurfacePosition;
        highlightMarker.transform.up = tileSurfacePosition.normalized;
        highlightMarker.SetActive(true);

        sb.Clear();
        string bodyName = "Planet";
        string biomeName = System.Enum.GetName(typeof(Biome), tileData.biome);
        sb.AppendLine($"  <b>{biomeName}</b> {(tileData.isHill ? "(Hill)" : "")}");
        sb.AppendLine($"  Elevation: {tileData.elevation:F2}");
        sb.AppendLine($"  Food: {tileData.food}   Prod: {tileData.production}");
        sb.AppendLine($"  Gold: {tileData.gold}   Sci: {tileData.science}");
        sb.AppendLine($"  Culture: {tileData.culture}");
        sb.AppendLine($"  <i><color=#888888>{bodyName} Tile #{tileIndex}</color></i>");

        infoText.text = sb.ToString();
        lastHoveredTileIndex = tileIndex;
        // tracked via lastHoveredTileIndex only
    }

    private void OnTileExitedEvent()
    {
        // keep UI visible as before; hide marker
        highlightMarker.SetActive(false);
    }
    
    // Event-driven: no manual refresh required

    void ClearDisplay() => infoText.text = "";
}
