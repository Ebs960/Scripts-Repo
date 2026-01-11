using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// World-space UI panel that displays tile information when hovering.
/// Shows biome, terrain features, yields, and resource icon.
/// </summary>
public class TileInfoWorldPanel : MonoBehaviour
{
    public static TileInfoWorldPanel Instance { get; private set; }
    
    [Header("Panel Settings")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 2f, 0f);
    [SerializeField] private float fadeSpeed = 8f;
    [SerializeField] private float showDelay = 0.1f;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private float maxDistance = 100f; // Hide if camera too far
    
    [Header("UI References (Auto-created if null)")]
    [SerializeField] private Canvas worldCanvas;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TextMeshProUGUI biomeText;
    [SerializeField] private TextMeshProUGUI featuresText;
    [SerializeField] private TextMeshProUGUI yieldsText;
    [SerializeField] private Image resourceIcon;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image borderImage;
    [SerializeField] private Image shadowImage;
    
    [Header("Styling")]
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
    [SerializeField] private Color borderColor = new Color(0.4f, 0.4f, 0.5f, 0.8f);
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
    [SerializeField] private Color biomeTextColor = Color.white;
    [SerializeField] private Color featuresTextColor = Color.white;
    [SerializeField] private Color yieldsTextColor = Color.white;
    [SerializeField] private int biomeFontSize = 20;
    [SerializeField] private int featuresFontSize = 14;
    [SerializeField] private int yieldsFontSize = 18;
    [SerializeField] private float borderWidth = 2f;
    [SerializeField] private float shadowOffset = 4f;
    
    [Header("Yield Icons (Unicode)")]
    [SerializeField] private string foodIcon = "🌾";
    [SerializeField] private string productionIcon = "⚙";
    [SerializeField] private string goldIcon = "💰";
    [SerializeField] private string scienceIcon = "🔬";
    [SerializeField] private string cultureIcon = "🎭";
    [SerializeField] private string faithIcon = "✨";
    
    // State
    private int currentTileIndex = -1;
    private float targetAlpha = 0f;
    private float currentAlpha = 0f;
    private float showTimer = 0f;
    private bool pendingShow = false;
    private Vector3 targetPosition;
    private Camera mainCamera;
    
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
    
    private void Start()
    {
        mainCamera = Camera.main;
        
        if (worldCanvas == null)
            CreateUI();
        
        // Start hidden
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
        
        // Subscribe to hover events
        StartCoroutine(SubscribeWhenReady());
    }
    
    private System.Collections.IEnumerator SubscribeWhenReady()
    {
        while (TileHoverSystem.Instance == null)
            yield return null;
        
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
    
    private void Update()
    {
        // Handle show delay
        if (pendingShow)
        {
            showTimer += Time.deltaTime;
            if (showTimer >= showDelay)
            {
                targetAlpha = 1f;
                pendingShow = false;
            }
        }
        
        // Fade animation
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, fadeSpeed * Time.deltaTime);
        if (canvasGroup != null)
            canvasGroup.alpha = currentAlpha;
        
        // Position and rotation
        if (currentAlpha > 0.01f)
        {
            transform.position = targetPosition + offset;
            
            if (faceCamera && mainCamera != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - mainCamera.transform.position);
            }
            
            // Hide if too far from camera
            if (mainCamera != null)
            {
                float dist = Vector3.Distance(mainCamera.transform.position, transform.position);
                if (dist > maxDistance)
                {
                    targetAlpha = 0f;
                }
            }
        }
    }
    
    private void OnTileHoverEnter(int tileIndex, HexTileData tileData, Vector3 hitPoint)
    {
        if (tileData == null) return;
        
        currentTileIndex = tileIndex;
        targetPosition = GetTileCenter(tileIndex, hitPoint);
        
        UpdateContent(tileData);
        
        // Start show delay
        pendingShow = true;
        showTimer = 0f;
    }
    
    private void OnTileHoverExit(int tileIndex)
    {
        targetAlpha = 0f;
        pendingShow = false;
        currentTileIndex = -1;
    }
    
    private Vector3 GetTileCenter(int tileIndex, Vector3 fallback)
    {
        var gen = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (gen?.Grid?.tileCenters != null && tileIndex < gen.Grid.tileCenters.Length)
        {
            Vector3 center = gen.Grid.tileCenters[tileIndex];
            // Adjust Y based on flat map renderer position
            var chunkManager = FindAnyObjectByType<HexMapChunkManager>();
            if (chunkManager != null)
                center.y = chunkManager.transform.position.y;
            else
            {
                var flatMap = FindAnyObjectByType<FlatMapTextureRenderer>();
                if (flatMap != null)
                    center.y = flatMap.transform.position.y;
            }
            return center;
        }
        return fallback;
    }
    
    private void UpdateContent(HexTileData tileData)
    {
        // Biome name
        if (biomeText != null)
        {
            string biomeName = FormatBiomeName(tileData.biome);
            biomeText.text = biomeName;
        }
        
        // Terrain features
        if (featuresText != null)
        {
            string features = GetTerrainFeatures(tileData);
            featuresText.text = features;
            featuresText.gameObject.SetActive(!string.IsNullOrEmpty(features));
        }
        
        // Yields
        if (yieldsText != null)
        {
            yieldsText.text = FormatYields(tileData);
        }
        
        // Resource icon
        if (resourceIcon != null)
        {
            if (tileData.HasResource && tileData.resource.icon != null)
            {
                resourceIcon.sprite = tileData.resource.icon;
                resourceIcon.gameObject.SetActive(true);
            }
            else
            {
                resourceIcon.gameObject.SetActive(false);
            }
        }
    }
    
    private string FormatBiomeName(Biome biome)
    {
        // Convert enum to readable name (e.g., "PineForest" -> "Pine Forest")
        string name = biome.ToString();
        
        // Insert spaces before capitals
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        
        return sb.ToString();
    }
    
    private string GetTerrainFeatures(HexTileData tileData)
    {
        var features = new System.Collections.Generic.List<string>();
        
        // Elevation tier
        if (tileData.elevationTier == ElevationTier.Hill || tileData.isHill)
            features.Add("Hills");
        else if (tileData.elevationTier == ElevationTier.Mountain)
            features.Add("Mountain");
        
        // Other features
        if (!tileData.isLand && tileData.biome != Biome.Ocean && tileData.biome != Biome.Coast)
            features.Add("Water");
        
        if (!tileData.isPassable)
            features.Add("Impassable");
        
        return string.Join(" • ", features);
    }
    
    private string FormatYields(HexTileData tileData)
    {
        var yields = new System.Collections.Generic.List<string>();
        
        if (tileData.food > 0)
            yields.Add($"{foodIcon}{tileData.food}");
        if (tileData.production > 0)
            yields.Add($"{productionIcon}{tileData.production}");
        if (tileData.gold > 0)
            yields.Add($"{goldIcon}{tileData.gold}");
        if (tileData.science > 0)
            yields.Add($"{scienceIcon}{tileData.science}");
        if (tileData.culture > 0)
            yields.Add($"{cultureIcon}{tileData.culture}");
        if (tileData.faithYield > 0)
            yields.Add($"{faithIcon}{tileData.faithYield}");
        
        if (yields.Count == 0)
            return "No yields";
        
        return string.Join("  ", yields);
    }
    
    #region UI Creation
    
    private void CreateUI()
    {
        // Create world space canvas
        GameObject canvasObj = new GameObject("TileInfoCanvas");
        canvasObj.transform.SetParent(transform);
        canvasObj.transform.localPosition = Vector3.zero;
        
        worldCanvas = canvasObj.AddComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;
        worldCanvas.sortingOrder = 100;
        
        canvasGroup = canvasObj.AddComponent<CanvasGroup>();
        
        // Set canvas size
        RectTransform canvasRect = worldCanvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(240f, 140f);
        canvasRect.localScale = Vector3.one * 0.012f; // Scale down for world space
        
        // Add CanvasScaler for consistent sizing
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100f;
        
        // === SHADOW (behind everything) ===
        GameObject shadowObj = new GameObject("Shadow");
        shadowObj.transform.SetParent(canvasObj.transform);
        var shadowRect = shadowObj.AddComponent<RectTransform>();
        shadowRect.anchorMin = Vector2.zero;
        shadowRect.anchorMax = Vector2.one;
        shadowRect.offsetMin = new Vector2(shadowOffset, -shadowOffset);
        shadowRect.offsetMax = new Vector2(shadowOffset, -shadowOffset);
        
        shadowImage = shadowObj.AddComponent<Image>();
        shadowImage.color = shadowColor;
        
        // === BORDER (behind panel) ===
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(canvasObj.transform);
        var borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = new Vector2(-borderWidth, -borderWidth);
        borderRect.offsetMax = new Vector2(borderWidth, borderWidth);
        
        borderImage = borderObj.AddComponent<Image>();
        borderImage.color = borderColor;
        
        // === MAIN PANEL ===
        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(canvasObj.transform);
        panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        backgroundImage = panelObj.AddComponent<Image>();
        backgroundImage.color = backgroundColor;
        
        // Add vertical layout with better padding
        var layout = panelObj.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 12);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        
        // Add content size fitter
        var fitter = panelObj.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // === BIOME TEXT (Title) ===
        biomeText = CreateTextElement(panelObj.transform, "BiomeText", biomeFontSize, biomeTextColor, FontStyles.Bold);
        
        // === SEPARATOR LINE ===
        GameObject separator = new GameObject("Separator");
        separator.transform.SetParent(panelObj.transform);
        var sepRect = separator.AddComponent<RectTransform>();
        var sepLayout = separator.AddComponent<LayoutElement>();
        sepLayout.preferredHeight = 1f;
        sepLayout.flexibleWidth = 1f;
        var sepImage = separator.AddComponent<Image>();
        sepImage.color = new Color(1f, 1f, 1f, 0.2f);
        
        // === FEATURES TEXT ===
        featuresText = CreateTextElement(panelObj.transform, "FeaturesText", featuresFontSize, featuresTextColor, FontStyles.Italic);
        
        // === YIELDS ROW ===
        GameObject yieldsRow = new GameObject("YieldsRow");
        yieldsRow.transform.SetParent(panelObj.transform);
        var yieldsRowRect = yieldsRow.AddComponent<RectTransform>();
        
        var rowLayout = yieldsRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.padding = new RectOffset(0, 0, 4, 0);
        
        // Create yields text
        yieldsText = CreateTextElement(yieldsRow.transform, "YieldsText", yieldsFontSize, yieldsTextColor, FontStyles.Normal);
        var yieldsLayoutElem = yieldsText.gameObject.AddComponent<LayoutElement>();
        yieldsLayoutElem.flexibleWidth = 1f;
        
        // Create resource icon
        GameObject iconObj = new GameObject("ResourceIcon");
        iconObj.transform.SetParent(yieldsRow.transform);
        resourceIcon = iconObj.AddComponent<Image>();
        resourceIcon.preserveAspect = true;
        var iconLayout = iconObj.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = 28f;
        iconLayout.preferredHeight = 28f;
        resourceIcon.gameObject.SetActive(false);
}
    
    private TextMeshProUGUI CreateTextElement(Transform parent, string name, int fontSize, Color color, FontStyles style)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent);
        
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        
        // Add slight shadow/outline for readability
        tmp.fontMaterial.EnableKeyword("UNDERLAY_ON");
        tmp.fontMaterial.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.8f));
        tmp.fontMaterial.SetFloat("_UnderlayOffsetX", 0.5f);
        tmp.fontMaterial.SetFloat("_UnderlayOffsetY", -0.5f);
        
        return tmp;
    }
    
    #endregion
    
    #region Public API
    
    /// <summary>
    /// Force show info for a specific tile (useful for selection, not just hover).
    /// </summary>
    public void ShowForTile(int tileIndex)
    {
        var tileData = TileSystem.Instance?.GetTileData(tileIndex);
        if (tileData == null) return;
        
        currentTileIndex = tileIndex;
        targetPosition = GetTileCenter(tileIndex, Vector3.zero);
        UpdateContent(tileData);
        targetAlpha = 1f;
        pendingShow = false;
    }
    
    /// <summary>
    /// Force hide the panel.
    /// </summary>
    public void Hide()
    {
        targetAlpha = 0f;
        pendingShow = false;
    }
    
    /// <summary>
    /// Set the offset from tile center.
    /// </summary>
    public void SetOffset(Vector3 newOffset)
    {
        offset = newOffset;
    }
    
    #endregion
}
