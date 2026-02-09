using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI panel showing biome name and yields when hovering over tiles.
/// Directly uses WorldPicker for tile detection — no dependency on
/// TileHoverSystem. Assign the WorldPicker and UI references in the inspector.
/// </summary>
public class TileInfoWorldPanel : MonoBehaviour
{
    public static TileInfoWorldPanel Instance { get; private set; }

    [Header("Tile Picking")]
    [Tooltip("Assign the WorldPicker from the scene. Used to detect which tile the mouse is over.")]
    [SerializeField] private WorldPicker worldPicker;

    [Header("UI References")]
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TextMeshProUGUI biomeText;
    [SerializeField] private TextMeshProUGUI yieldsText;
    [SerializeField] private TextMeshProUGUI elevationText;

    [Header("Styling")]
    [SerializeField] private TMP_FontAsset overrideFontAsset;
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
    [SerializeField] private Color textColor = Color.white;

    [Header("Behaviour")]
    [Tooltip("Follow the mouse cursor position on screen.")]
    [SerializeField] private bool followMouse = true;
    [Tooltip("Offset from the cursor when followMouse is enabled.")]
    [SerializeField] private Vector2 cursorOffset = new Vector2(20f, -20f);

    // State
    private int lastHoveredTile = -1;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
    }

    private void Start()
    {
        if (panelRect == null || biomeText == null || yieldsText == null)
        {
            Debug.LogWarning("[TileInfoWorldPanel] UI references are not fully assigned (need at least panelRect, biomeText, yieldsText). Disabling.");
            enabled = false;
            return;
        }

        // Auto-find WorldPicker if not assigned
        if (worldPicker == null)
        {
            worldPicker = FindAnyObjectByType<WorldPicker>();
            if (worldPicker != null)
                Debug.Log("[TileInfoWorldPanel] Auto-found WorldPicker in scene.");
            else
                Debug.LogWarning("[TileInfoWorldPanel] No WorldPicker assigned or found. Tile hover will not work.");
        }

        // Start hidden
        panelRect.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (worldPicker == null) return;

        // Pick the tile under the mouse
        if (worldPicker.TryPickTileIndex(Input.mousePosition, out int tileIndex, out Vector3 worldPos))
        {
            if (tileIndex >= 0)
            {
                // Get tile data from the current planet's TileSystem
                int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
                var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
                var tileData = ts != null ? ts.GetTileData(tileIndex) : null;

                if (tileData != null)
                {
                    // Only update content when hovering a new tile (avoid per-frame text rebuilding)
                    if (tileIndex != lastHoveredTile)
                    {
                        lastHoveredTile = tileIndex;
                        UpdateContent(tileData);
                    }
                    Show();

                    // Position panel near cursor
                    if (followMouse && uiCanvas != null)
                    {
                        PositionNearCursor();
                    }
                    return;
                }
            }
        }

        // No valid tile under cursor — hide
        if (lastHoveredTile >= 0)
        {
            lastHoveredTile = -1;
            Hide();
        }
    }

    private void PositionNearCursor()
    {
        if (panelRect == null || uiCanvas == null) return;

        Vector2 screenPos = Input.mousePosition;
        
        // Convert screen position to canvas space
        if (uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            panelRect.position = screenPos + cursorOffset;
        }
        else
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                uiCanvas.transform as RectTransform,
                screenPos + cursorOffset,
                uiCanvas.worldCamera,
                out Vector2 localPoint);
            panelRect.localPosition = localPoint;
        }
    }

    private void UpdateContent(HexTileData tileData)
    {
        if (biomeText != null) biomeText.text = FormatBiomeName(tileData.biome);
        if (yieldsText != null) yieldsText.text = FormatYields(tileData);

        string elevInfo = $"Elev: {tileData.elevation:F3} (render {tileData.renderElevation:F3})\nHill: {(tileData.isHill ? "Yes" : "No")}";
        if (elevationText != null)
        {
            elevationText.text = elevInfo;
        }
        else if (yieldsText != null)
        {
            yieldsText.text += "\n" + elevInfo;
        }
    }

    private string FormatBiomeName(Biome biome)
    {
        string name = biome.ToString();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    private string FormatYields(HexTileData tileData)
    {
        var yields = new System.Collections.Generic.List<string>();
        if (tileData.food > 0) yields.Add($"F{tileData.food}");
        if (tileData.production > 0) yields.Add($"P{tileData.production}");
        if (tileData.gold > 0) yields.Add($"G{tileData.gold}");
        if (tileData.science > 0) yields.Add($"S{tileData.science}");
        if (tileData.culture > 0) yields.Add($"C{tileData.culture}");
        if (tileData.faithYield > 0) yields.Add($"*{tileData.faithYield}");
        if (yields.Count == 0) return "No yields";
        return string.Join("  ", yields);
    }

    #region Public API
    public void ShowForTile(int tileIndex)
    {
        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tileData == null) return;
        UpdateContent(tileData);
        Show();
    }

    public void Hide()
    {
        if (panelRect != null) panelRect.gameObject.SetActive(false);
    }

    public void Show()
    {
        if (panelRect != null) panelRect.gameObject.SetActive(true);
    }
    #endregion
}
