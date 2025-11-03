using UnityEngine;
using TMPro;
using System.Text;
using UnityEngine.UI;

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
    [Tooltip("Optional background panel behind the text.")]
    public Image backgroundPanel;

    [Header("UI Prefab")]
    [Tooltip("Prefab that contains the TextMeshProUGUI. Instantiated if one isn't assigned in the scene.")]
    public GameObject tileInfoUIPrefab;
    [Tooltip("Optional parent for the prefab (e.g. your Canvas).")]
    public Transform uiParent;

    [Header("UI Settings")]
    [Tooltip("Font size used for the primary tile information text.")]
    public int fontSize = 18;
    [Tooltip("Color for the information text.")]
    public Color textColor = Color.white;
    [Tooltip("Background color when a panel is available.")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.7f);

    [Header("Display Options")]
    [Tooltip("Show tile coordinates in the footer.")]
    public bool showCoordinates = true;
    [Tooltip("Show the biome movement cost.")]
    public bool showMovementCost = true;
    [Tooltip("Show the biome defence bonus.")]
    public bool showDefenseBonus = true;
    [Tooltip("Include improvement information.")]
    public bool showImprovements = true;
    [Tooltip("Include owner / civilisation details.")]
    public bool showOwner = true;
    [Tooltip("Include resource information.")]
    public bool showResources = true;

    [Header("Highlight Marker")]
    [Tooltip("Prefab for a ring / disc that marks the hovered tile.")]
    public GameObject highlightMarkerPrefab;

    // ─────────────────────────────────────────────────────────────
    // Private fields
    // ─────────────────────────────────────────────────────────────
    GameObject uiRoot;              // top-level object that holds the text
    GameObject highlightMarker;     // ring / disc instance
    readonly StringBuilder sb = new StringBuilder(256);
    bool isReady = false;           // stays false until map is finished

    // State for current hover
    int lastHoveredTileIndex = -1;
    bool lastHoverWasMoon = false;

    // ─────────────────────────────────────────────────────────────
    // Unity - Awake
    // ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        BuildUI();
        CreateHighlightMarker();
        ClearDisplay();
        SetUIVisibility(false);
    }

    void OnEnable()
    {
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileHovered += OnTileHoveredEvent;
            TileSystem.Instance.OnTileHoverExited += OnTileExitedEvent;
        }
    }

    void OnDisable()
    {
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileHovered -= OnTileHoveredEvent;
            TileSystem.Instance.OnTileHoverExited -= OnTileExitedEvent;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Public API: called by GameSceneInitializer when loading ends
    // ─────────────────────────────────────────────────────────────
    public void SetReady(bool value = true)
    {
        isReady = value;
        SetUIVisibility(value);

        if (!value)
        {
            highlightMarker?.SetActive(false);
            lastHoveredTileIndex = -1;
            lastHoverWasMoon = false;
            ClearDisplay();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────
    void OnTileHoveredEvent(int tileIndex, Vector3 worldPos)
    {
        if (!isReady)
            return;

        bool isMoon = TileSystem.Instance != null && TileSystem.Instance.IsCurrentHoverOnMoon;
        var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileDataForBody(tileIndex, isMoon) : null;
        if (tileData == null)
            return;

        Vector3 tileSurfacePosition = TileSystem.Instance != null
            ? TileSystem.Instance.GetTileSurfacePositionForBody(tileIndex, isMoon, 0.1f)
            : worldPos;

        UpdateHighlight(tileSurfacePosition, isMoon);
        UpdateText(tileData, tileIndex, isMoon);

        lastHoveredTileIndex = tileIndex;
        lastHoverWasMoon = isMoon;
    }

    void OnTileExitedEvent()
    {
        highlightMarker?.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    void BuildUI()
    {
        if (infoText == null && tileInfoUIPrefab != null)
        {
            GameObject uiInstance = Instantiate(tileInfoUIPrefab, uiParent ?? transform);
            infoText = uiInstance.GetComponentInChildren<TextMeshProUGUI>();
            if (backgroundPanel == null)
                backgroundPanel = uiInstance.GetComponentInChildren<Image>();
        }

        if (infoText == null)
        {
            CreateDefaultUI();
        }

        if (backgroundPanel != null)
        {
            backgroundPanel.color = backgroundColor;
        }

        if (infoText != null)
        {
            infoText.fontSize = fontSize;
            infoText.color = textColor;
        }

        uiRoot = DetermineUIRoot();
    }

    void CreateHighlightMarker()
    {
        if (highlightMarker != null)
            return;

        if (highlightMarkerPrefab != null)
        {
            highlightMarker = Instantiate(highlightMarkerPrefab);
        }
        else
        {
            highlightMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            highlightMarker.name = "FallbackHighlightMarker";
            highlightMarker.transform.localScale = new Vector3(1.2f, 0.05f, 1.2f);
            var renderer = highlightMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Unlit/Color"))
                {
                    color = new Color(1f, 0.92f, 0.2f, 0.6f)
                };
            }
            Destroy(highlightMarker.GetComponent<Collider>());
        }

        if (highlightMarker != null)
        {
            highlightMarker.SetActive(false);
        }
    }

    GameObject DetermineUIRoot()
    {
        if (backgroundPanel != null)
            return backgroundPanel.gameObject;
        if (infoText != null && infoText.transform.parent != null)
            return infoText.transform.parent.gameObject;
        return infoText != null ? infoText.gameObject : gameObject;
    }

    void SetUIVisibility(bool visible)
    {
        if (uiRoot != null)
            uiRoot.SetActive(visible);
        else if (infoText != null)
            infoText.gameObject.SetActive(visible);

        if (backgroundPanel != null)
            backgroundPanel.gameObject.SetActive(visible);
    }

    void UpdateHighlight(Vector3 tileSurfacePosition, bool isMoon)
    {
        if (highlightMarker == null)
            return;

        Vector3 normal = tileSurfacePosition.normalized;

        if (GameManager.Instance != null)
        {
            var generator = isMoon
                ? GameManager.Instance.GetCurrentMoonGenerator() as IHexasphereGenerator
                : GameManager.Instance.GetCurrentPlanetGenerator() as IHexasphereGenerator;

            if (generator != null)
            {
                // Cast to MonoBehaviour to access transform (since PlanetGenerator/MoonGenerator are MonoBehaviours)
                MonoBehaviour generatorMono = generator as MonoBehaviour;
                if (generatorMono != null)
                {
                    Vector3 center = generatorMono.transform.position;
                    normal = (tileSurfacePosition - center).normalized;
                }
            }
        }

        highlightMarker.transform.position = tileSurfacePosition;
        highlightMarker.transform.up = normal;
        highlightMarker.SetActive(true);
    }

    void UpdateText(HexTileData tileData, int tileIndex, bool isMoon)
    {
        if (infoText == null)
            return;

        sb.Clear();

        string biomeName = tileData.biome.ToString();
        sb.Append($"<size={fontSize + 4}><b>{biomeName}</b></size>");

        if (tileData.isHill)
            sb.Append(" <color=#8B7355>(Hill)</color>");
        else if (tileData.elevationTier == ElevationTier.Mountain)
            sb.Append(" <color=#A0A0A0>(Mountain)</color>");

        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("<b>Yields:</b>");
        sb.AppendLine($"  🌾 Food: <color=#90EE90>{tileData.food}</color>  " +
                     $"⚙️ Production: <color=#FFA500>{tileData.production}</color>");
        sb.AppendLine($"  💰 Gold: <color=#FFD700>{tileData.gold}</color>  " +
                     $"🔬 Science: <color=#00CED1>{tileData.science}</color>");
        sb.AppendLine($"  🎭 Culture: <color=#FF69B4>{tileData.culture}</color>  " +
                     $"✨ Faith: <color=#DDA0DD>{tileData.faithYield}</color>");
        sb.AppendLine();

        sb.AppendLine("<b>Terrain:</b>");
        sb.AppendLine($"  Elevation: {tileData.elevation:F2}");

        if (showMovementCost)
        {
            int moveCost = BiomeHelper.GetMovementCost(tileData.biome);
            sb.AppendLine($"  Movement Cost: {moveCost}");
        }

        if (showDefenseBonus)
        {
            int defBonus = BiomeHelper.GetDefenseBonus(tileData.biome);
            if (tileData.isHill) defBonus += 2;
            if (defBonus > 0)
                sb.AppendLine($"  <color=#87CEEB>Defense: +{defBonus}</color>");
        }
        sb.AppendLine();

        if (showImprovements && tileData.improvement != null)
        {
            sb.AppendLine($"<b>Improvement:</b> {tileData.improvement.improvementName}");
        }

        if (tileData.district != null)
        {
            sb.AppendLine($"<b>District:</b> {tileData.district.districtName}");
        }

        if (tileData.HasHolySite)
        {
            sb.AppendLine("<b><color=#FFD700>⛪ Holy Site</color></b>");
        }

        if (showOwner && tileData.owner != null)
        {
            string ownerName = tileData.owner.civData?.civName ?? "Unknown";
            sb.AppendLine($"<b>Owner:</b> <color=#87CEEB>{ownerName}</color>");
        }

        if (showResources && tileData.resource != null)
        {
            sb.AppendLine($"<b>Resource:</b> <color=#FFD700>{tileData.resource.resourceName}</color>");
        }

        if (showCoordinates)
        {
            sb.AppendLine();
            string bodyName = isMoon ? "Moon" : "Planet";
            sb.AppendLine($"<i><size={fontSize - 4}><color=#888888>{bodyName} Tile #{tileIndex}</color></size></i>");
        }

        infoText.text = sb.ToString();
    }

    void ClearDisplay()
    {
        if (infoText != null)
            infoText.text = string.Empty;
    }

    void CreateDefaultUI()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("TileInfo_Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        GameObject panelGO = new GameObject("TileInfo_Panel");
        panelGO.transform.SetParent(canvas.transform, false);
        backgroundPanel = panelGO.AddComponent<Image>();
        backgroundPanel.color = backgroundColor;

        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot = new Vector2(0, 1);
        panelRect.anchoredPosition = new Vector2(10, -10);
        panelRect.sizeDelta = new Vector2(300, 200);

        GameObject textGO = new GameObject("TileInfo_Text");
        textGO.transform.SetParent(panelGO.transform, false);
        infoText = textGO.AddComponent<TextMeshProUGUI>();
        infoText.fontSize = fontSize;
        infoText.color = textColor;
        infoText.alignment = TextAlignmentOptions.TopLeft;
        infoText.margin = new Vector4(10, 10, 10, 10);

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Toggle display on/off.
    /// </summary>
    public void ToggleDisplay(bool show)
    {
        SetUIVisibility(show);
        if (!show)
        {
            highlightMarker?.SetActive(false);
        }
        else if (lastHoveredTileIndex >= 0)
        {
            // Re-apply last highlight when re-enabled.
            Vector3 tileSurfacePosition = TileSystem.Instance != null
                ? TileSystem.Instance.GetTileSurfacePositionForBody(lastHoveredTileIndex, lastHoverWasMoon, 0.1f)
                : Vector3.zero;
            if (tileSurfacePosition != Vector3.zero)
                UpdateHighlight(tileSurfacePosition, lastHoverWasMoon);
        }
    }
}
