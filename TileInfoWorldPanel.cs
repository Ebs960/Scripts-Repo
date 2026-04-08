using UnityEngine;
using UnityEngine.InputSystem;
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
    [SerializeField] private TextMeshProUGUI moistureText;
    [SerializeField] private TextMeshProUGUI temperatureText;
    [SerializeField] private TextMeshProUGUI mosquitoText;
    [SerializeField] private TextMeshProUGUI landText;
    [SerializeField] private TextMeshProUGUI resourceText;

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
    private GameManager.PlanetLayerType lastHoveredLayer = GameManager.PlanetLayerType.Surface;
    private LayerManager cachedLayerManager;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
    }

    private TileSystem _subscribedTS;

    private void Start()
    {
        if (worldPicker == null)
            worldPicker = FindAnyObjectByType<WorldPicker>();

        if (panelRect == null || biomeText == null || yieldsText == null)
        {
            Debug.LogWarning("[TileInfoWorldPanel] UI references are not fully assigned (need at least panelRect, biomeText, yieldsText). Disabling.");
            enabled = false;
            return;
        }

        // Start hidden
        panelRect.gameObject.SetActive(false);
        SubscribeToTileSystem();
    }

    private void OnDisable()
    {
        UnsubscribeFromTileSystem();
    }

    private void SubscribeToTileSystem()
    {
        if (worldPicker == null)
            worldPicker = FindAnyObjectByType<WorldPicker>();
        if (worldPicker == null) return;

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
        if (IsBlockedByOverlay())
        {
            Hide();
            return;
        }

        if (tileIndex < 0) return;

        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;

        if (tileData != null)
        {
            var layer = GetActiveViewLayer();
            if (tileIndex != lastHoveredTile || lastHoveredLayer != layer)
            {
                lastHoveredTile = tileIndex;
                lastHoveredLayer = layer;
                UpdateContent(tileData, tileIndex, layer);
            }
            Show();
        }
    }

    private void OnTileHoverExited()
    {
        if (lastHoveredTile >= 0)
        {
            lastHoveredTile = -1;
            Hide();
        }
    }

    private void Update()
    {
        // Re-subscribe if TileSystem was recreated (planet switch)
        if (_subscribedTS == null || _subscribedTS != TileSystem.Instance)
            SubscribeToTileSystem();

        if (IsBlockedByOverlay())
        {
            if (lastHoveredTile >= 0)
                lastHoveredTile = -1;
            Hide();
            return;
        }

        // Position tooltip near cursor while hovering (lightweight — no raycast)
        if (followMouse && uiCanvas != null && lastHoveredTile >= 0)
        {
            PositionNearCursor();
        }
    }

    private void PositionNearCursor()
    {
        if (panelRect == null || uiCanvas == null) return;

        Vector2 screenPos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        
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

    private void UpdateContent(HexTileData tileData, int tileIndex, GameManager.PlanetLayerType layer)
    {
        switch (layer)
        {
            case GameManager.PlanetLayerType.Underwater:
                UpdateContentUnderwater(tileData);
                break;
            case GameManager.PlanetLayerType.Orbit:
                UpdateContentOrbit(tileData, tileIndex);
                break;
            default:
                UpdateContentSurface(tileData);
                break;
        }
    }

    private void UpdateContentSurface(HexTileData tileData)
    {
        if (biomeText != null)
        {
            string biomeName = FormatBiomeName(tileData.biome);
            biomeText.text = biomeName;
        }
        if (yieldsText != null) yieldsText.text = FormatYields(tileData);

        if (resourceText != null)
        {
            if (tileData.HasResource && tileData.resource != null)
                resourceText.text = $"Resource: {tileData.resource.resourceName}";
            else
                resourceText.text = "Resource: None";
        }

        string elevInfo = $"Elev: {tileData.elevation:F2}m\nHill: {(tileData.isHill ? "Yes" : "No")}\nMountain: {(tileData.isMountain ? "Yes" : "No")}";
        if (elevationText != null)
            elevationText.text = elevInfo;
        else if (yieldsText != null)
            yieldsText.text += "\n" + elevInfo;

        // Land display: use dedicated field if present, otherwise append to elevation text for compatibility
        if (landText != null)
            landText.text = tileData.isLand ? "Land: Yes" : "Land: No";
        else if (elevationText != null)
            elevationText.text = elevInfo + $"\nLand: {(tileData.isLand ? "Yes" : "No")}";

        if (moistureText != null)
            moistureText.text = $"Moisture: {tileData.moisture:F2}";
        else if (yieldsText != null)
            yieldsText.text += $"\nMoisture: {tileData.moisture:F2}";

        if (temperatureText != null)
            temperatureText.text = $"Temperature: {tileData.temperature:F1}°C";
        else if (yieldsText != null)
            yieldsText.text += $"\nTemperature: {tileData.temperature:F1}°C";

        if (mosquitoText != null)
            mosquitoText.text = FormatMosquitoStatus(tileData);
        else if (yieldsText != null)
            yieldsText.text += $"\n{FormatMosquitoStatus(tileData)}";
    }

    private void UpdateContentUnderwater(HexTileData tileData)
    {
        if (biomeText != null)
        {
            // Show the underwater floor biome as primary
            Biome displayBiome = tileData.underwaterBiome != Biome.Ocean
                ? tileData.underwaterBiome
                : tileData.biome;
            biomeText.text = $"{FormatBiomeName(displayBiome)} (Seafloor)";
        }
        if (yieldsText != null) yieldsText.text = FormatYields(tileData);

        if (resourceText != null)
        {
            if (tileData.HasResource && tileData.resource != null)
                resourceText.text = $"Resource: {tileData.resource.resourceName}";
            else
                resourceText.text = "Resource: None";
        }

        // Underwater-specific info: trench depth, underwater improvements
        var sb = new System.Text.StringBuilder();
        sb.Append($"Depth: {Mathf.Abs(tileData.elevation):F2}m");
        if (tileData.trenchDepth > 0f)
            sb.Append($"\nTrench: {tileData.trenchDepth:F1}m");
        if (tileData.HasUnderwaterImprovement)
            sb.Append($"\nImprovement: {tileData.improvement.improvementName}");
        if (tileData.HasUnderwaterDistrict)
            sb.Append($"\nDistrict: {tileData.district.districtName}");

        // Always show whether the underlying tile is considered land (useful for debugging frozen/coastal cases)
        sb.Append($"\nLand: {(tileData.isLand ? "Yes" : "No")}");

        if (elevationText != null)
            elevationText.text = sb.ToString();
        else if (yieldsText != null)
            yieldsText.text += "\n" + sb.ToString();

        // Land display for underwater view
        if (landText != null)
            landText.text = tileData.isLand ? "Land: Yes" : "Land: No";
        else if (elevationText != null)
            elevationText.text = sb.ToString() + $"\nLand: {(tileData.isLand ? "Yes" : "No")}";

        if (moistureText != null)
            moistureText.text = "";
        if (temperatureText != null)
            temperatureText.text = $"Temperature: {tileData.temperature:F1}°C";
        if (mosquitoText != null)
            mosquitoText.text = FormatMosquitoStatus(tileData);
    }

    private void UpdateContentOrbit(HexTileData tileData, int tileIndex)
    {
        if (biomeText != null)
            biomeText.text = "Orbit";

        // Orbital yields / info
        var sb = new System.Text.StringBuilder();
        if (tileData.HasOrbitalImprovement)
            sb.Append($"Improvement: {tileData.improvement.improvementName}");
        else
            sb.Append("No orbital structures");

        // Check for orbital occupant
        var occMgr = TileOccupancyManager.Instance;
        if (occMgr != null)
        {
            GameObject occupant = occMgr.GetOccupantObject(tileIndex, TileLayer.Orbit);
            if (occupant != null)
            {
                var unit = occupant.GetComponent<BaseUnit>();
                string unitName = unit != null ? unit.UnitName : occupant.name;
                sb.Append($"\nUnit: {unitName}");
            }
        }

        if (yieldsText != null) yieldsText.text = sb.ToString();
        if (resourceText != null) resourceText.text = "";
        if (elevationText != null) elevationText.text = $"Surface below: {FormatBiomeName(tileData.biome)}";
        // Show land state in dedicated field or append to elevation
        if (landText != null)
            landText.text = tileData.isLand ? "Land: Yes" : "Land: No";
        else if (elevationText != null)
            elevationText.text = elevationText.text + $"\nLand: {(tileData.isLand ? "Yes" : "No")}";
        if (moistureText != null) moistureText.text = "";
        if (temperatureText != null) temperatureText.text = "";
        if (mosquitoText != null) mosquitoText.text = FormatMosquitoStatus(tileData);
    }

    private GameManager.PlanetLayerType GetActiveViewLayer()
    {
        if (cachedLayerManager == null)
            cachedLayerManager = FindAnyObjectByType<LayerManager>();
        if (cachedLayerManager != null)
            return cachedLayerManager.ActiveViewLayer;
        return GameManager.PlanetLayerType.Surface;
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

    private string FormatMosquitoStatus(HexTileData tileData)
    {
        return $"Mosquitoes: {(tileData != null && tileData.hasMosquitoes ? "Yes" : "No")}";
    }

    #region Public API
    public void ShowForTile(int tileIndex)
    {
        if (IsBlockedByOverlay())
        {
            Hide();
            return;
        }

        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tileData == null) return;
        UpdateContent(tileData, tileIndex, GetActiveViewLayer());
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

    private bool IsBlockedByOverlay()
    {
        if (LoadingPanelController.Instance != null && LoadingPanelController.Instance.IsUiBlocked)
            return true;

        if (UIManager.Instance != null && UIManager.Instance.IsBlockingModalVisible)
            return true;

        return false;
    }
    #endregion
}
