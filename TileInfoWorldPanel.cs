using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Developer-authored UI panel showing biome name and yields.
/// NOTE: This script no longer creates UI at runtime — assign all
/// UI references in the inspector (Canvas, panel Rect, Text fields,
/// and a CanvasGroup on the panel) just like a MainMenu-style UI.
/// </summary>
public class TileInfoWorldPanel : MonoBehaviour
{
    public static TileInfoWorldPanel Instance { get; private set; }

    [Header("UI References (optional)")]
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TextMeshProUGUI biomeText;
    [SerializeField] private TextMeshProUGUI yieldsText;
    [SerializeField] private TextMeshProUGUI elevationText;

    [Header("Styling")]
    [SerializeField] private TMP_FontAsset overrideFontAsset;
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
    [SerializeField] private Color textColor = Color.white;

    // No CanvasGroup/fade: panel visibility is handled by activating/deactivating the panel GameObject.

    private void Awake()
    {
        if (Instance == null) Instance = this; else if (Instance != this) Destroy(gameObject);
    }

    private void Start()
    {
        if (uiCanvas == null || panelRect == null || biomeText == null || yieldsText == null)
        {
            Debug.LogWarning("TileInfoWorldPanel: UI references are not fully assigned. Please create the UI in the scene or a prefab and assign `uiCanvas`, `panelRect`, `biomeText` and `yieldsText` in the inspector. Disabling the component.");
            enabled = false;
            return;
        }

        // Start hidden
        panelRect.gameObject.SetActive(false);
        StartCoroutine(SubscribeWhenReady());
    }

    private System.Collections.IEnumerator SubscribeWhenReady()
    {
        while (TileHoverSystem.Instance == null) yield return null;
        TileHoverSystem.Instance.OnTileHoverEnter += OnTileHoverEnter;
        TileHoverSystem.Instance.OnTileHoverExit += OnTileHoverExit;
    }

    private void OnDestroy()
    {
        if (TileHoverSystem.Instance != null)
        {
            TileHoverSystem.Instance.OnTileHoverEnter -= OnTileHoverEnter;
            TileHoverSystem.Instance.OnTileHoverExit -= OnTileHoverExit;
        }
    }

    // No Update required — visibility is toggled immediately by SetActive

    // Runtime UI creation removed. UI should be authored by the developer (scene or prefab).

    private void OnTileHoverEnter(int tileIndex, HexTileData tileData, Vector3 _)
    {
        if (tileData == null) return;
        UpdateContent(tileData);
        Show();
    }

    private void OnTileHoverExit(int tileIndex)
    {
        Hide();
    }

    private void UpdateContent(HexTileData tileData)
    {
        if (biomeText != null) biomeText.text = FormatBiomeName(tileData.biome);
        if (yieldsText != null) yieldsText.text = FormatYields(tileData);

        // Elevation & hill status display. If a dedicated `elevationText` field
        // is assigned in the inspector, write there. Otherwise append to yieldsText.
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
        for (int i = 0; i < name.Length; i++) { if (i > 0 && char.IsUpper(name[i])) sb.Append(' '); sb.Append(name[i]); }
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
        var tileData = TileSystem.Instance?.GetTileData(tileIndex);
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
