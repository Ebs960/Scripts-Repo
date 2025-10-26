using UnityEngine;
using TMPro;
using System.Text;

/// <summary>
/// IMPROVED tile info display that uses mathematical sphere intersection instead of raycasting.
/// Much more robust - works even without colliders on tiles!
/// </summary>
public class TileInfoDisplayImproved : MonoBehaviour
{
    public static TileInfoDisplayImproved Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Text element that displays tile information")]
    public TextMeshProUGUI infoText;
    
    [Header("UI Settings")]
    [Tooltip("Font size for the info text")]
    public int fontSize = 18;
    [Tooltip("Text color")]
    public Color textColor = Color.white;
    [Tooltip("Background panel (optional)")]
    public UnityEngine.UI.Image backgroundPanel;
    [Tooltip("Background color with alpha")]
    public Color backgroundColor = new Color(0, 0, 0, 0.7f);
    
    [Header("Display Options")]
    [Tooltip("Show tile coordinates")]
    public bool showCoordinates = true;
    [Tooltip("Show movement cost")]
    public bool showMovementCost = true;
    [Tooltip("Show defense bonus")]
    public bool showDefenseBonus = true;
    [Tooltip("Show improvement info")]
    public bool showImprovements = true;
    [Tooltip("Show owner/civilization")]
    public bool showOwner = true;
    [Tooltip("Show resource info")]
    public bool showResources = true;

    [Header("Highlight Marker")]
    [Tooltip("Prefab or GameObject for highlighting hovered tile")]
    public GameObject highlightMarkerPrefab;
    private GameObject highlightMarker;
    
    [Header("Performance")]
    [Tooltip("Update frequency (lower = better performance)")]
    [Range(0.01f, 0.2f)]
    public float updateInterval = 0.05f;
    
    // Private state
    private int currentHoveredTile = -1;
    private bool isHoveringMoon = false;
    private float updateTimer = 0f;
    private StringBuilder sb = new StringBuilder(256);
    private Camera mainCam;
    private bool isReady = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-create UI if not assigned
        if (infoText == null)
        {
            CreateDefaultUI();
        }
        
        // Setup background
        if (backgroundPanel != null)
        {
            backgroundPanel.color = backgroundColor;
        }
        
        // Setup text
        if (infoText != null)
        {
            infoText.fontSize = fontSize;
            infoText.color = textColor;
            infoText.text = "";
        }

        // Create highlight marker
        if (highlightMarker == null)
        {
            if (highlightMarkerPrefab != null)
            {
                highlightMarker = Instantiate(highlightMarkerPrefab);
            }
            else
            {
                CreateDefaultHighlight();
            }
        }
        
        if (highlightMarker != null)
        {
            highlightMarker.SetActive(false);
        }

        HideDisplay();
    }

    void Start()
    {
        mainCam = Camera.main;
        
        // Auto-enable when game starts
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted += OnGameReady;
        }
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted -= OnGameReady;
        }
    }

    private void OnGameReady()
    {
        SetReady(true);
    }

    public void SetReady(bool ready)
    {
        isReady = ready;
        if (!ready)
        {
            HideDisplay();
        }
    }

    void Update()
    {
        if (!isReady) return;
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        // Throttle updates for performance
        updateTimer += Time.deltaTime;
        if (updateTimer < updateInterval) return;
        updateTimer = 0f;

        // Check if mouse is over UI
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
        {
            HideDisplay();
            return;
        }

        // Use mathematical sphere intersection instead of raycasting!
        var result = GetTileUnderMouse();
        
        if (result.found)
        {
            if (result.tileIndex != currentHoveredTile || result.isMoon != isHoveringMoon)
            {
                currentHoveredTile = result.tileIndex;
                isHoveringMoon = result.isMoon;
                UpdateDisplay(result.tileIndex, result.isMoon, result.worldPosition);
            }
        }
        else
        {
            if (currentHoveredTile >= 0)
            {
                currentHoveredTile = -1;
                isHoveringMoon = false;
                HideDisplay();
            }
        }
    }

    /// <summary>
    /// ROBUST METHOD: Uses mathematical sphere intersection instead of raycasting.
    /// Works even without colliders on tiles!
    /// </summary>
    private (bool found, int tileIndex, bool isMoon, Vector3 worldPosition) GetTileUnderMouse()
    {
        if (mainCam == null) return (false, -1, false, Vector3.zero);

        Ray mouseRay = mainCam.ScreenPointToRay(Input.mousePosition);

        // Try planet first
        var planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planet != null && planet.Grid != null && planet.Grid.IsBuilt)
        {
            var hit = IntersectSphere(mouseRay, planet.transform.position, planet.radius * 1.1f);
            if (hit.intersects)
            {
                int tileIndex = FindClosestTile(hit.point, planet.Grid);
                if (tileIndex >= 0)
                {
                    return (true, tileIndex, false, hit.point);
                }
            }
        }

        // Try moon if available
        var moon = GameManager.Instance?.GetCurrentMoonGenerator();
        if (moon != null && moon.Grid != null && moon.Grid.IsBuilt)
        {
            var hit = IntersectSphere(mouseRay, moon.transform.position, moon.Grid.Radius * 1.1f);
            if (hit.intersects)
            {
                int tileIndex = FindClosestTile(hit.point, moon.Grid);
                if (tileIndex >= 0)
                {
                    return (true, tileIndex, true, hit.point);
                }
            }
        }

        return (false, -1, false, Vector3.zero);
    }

    /// <summary>
    /// Mathematical ray-sphere intersection (no raycasting needed!)
    /// </summary>
    private (bool intersects, Vector3 point) IntersectSphere(Ray ray, Vector3 sphereCenter, float sphereRadius)
    {
        Vector3 oc = ray.origin - sphereCenter;
        float a = Vector3.Dot(ray.direction, ray.direction);
        float b = 2.0f * Vector3.Dot(oc, ray.direction);
        float c = Vector3.Dot(oc, oc) - sphereRadius * sphereRadius;
        float discriminant = b * b - 4 * a * c;

        if (discriminant < 0)
        {
            return (false, Vector3.zero);
        }

        float t = (-b - Mathf.Sqrt(discriminant)) / (2.0f * a);
        if (t < 0)
        {
            // Ray origin is inside sphere, use the other intersection
            t = (-b + Mathf.Sqrt(discriminant)) / (2.0f * a);
        }

        if (t < 0)
        {
            return (false, Vector3.zero);
        }

        Vector3 hitPoint = ray.origin + ray.direction * t;
        return (true, hitPoint);
    }

    /// <summary>
    /// Find the closest tile to a world position (simple distance check)
    /// </summary>
    private int FindClosestTile(Vector3 worldPos, SphericalHexGrid grid)
    {
        if (grid == null || grid.tileCenters == null || grid.tileCenters.Length == 0)
            return -1;

        int closestIndex = -1;
        float closestDistance = float.MaxValue;

        for (int i = 0; i < grid.TileCount; i++)
        {
            float distance = Vector3.Distance(worldPos, grid.tileCenters[i]);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    /// <summary>
    /// Update the display with tile information
    /// </summary>
    private void UpdateDisplay(int tileIndex, bool isMoon, Vector3 worldPosition)
    {
        var tileData = TileSystem.Instance?.GetTileDataForBody(tileIndex, isMoon);
        if (tileData == null)
        {
            HideDisplay();
            return;
        }

        // Build info text
        sb.Clear();
        
        // Title: Biome name
        string biomeName = tileData.biome.ToString();
        sb.Append($"<size={fontSize + 4}><b>{biomeName}</b></size>");
        
        // Hill/Mountain indicator
        if (tileData.isHill)
            sb.Append(" <color=#8B7355>(Hill)</color>");
        else if (tileData.elevationTier == ElevationTier.Mountain)
            sb.Append(" <color=#A0A0A0>(Mountain)</color>");
        
        sb.AppendLine();
        sb.AppendLine();

        // Yields
        sb.AppendLine("<b>Yields:</b>");
        sb.AppendLine($"  🌾 Food: <color=#90EE90>{tileData.food}</color>  " +
                     $"⚙️ Production: <color=#FFA500>{tileData.production}</color>");
        sb.AppendLine($"  💰 Gold: <color=#FFD700>{tileData.gold}</color>  " +
                     $"🔬 Science: <color=#00CED1>{tileData.science}</color>");
        sb.AppendLine($"  🎭 Culture: <color=#FF69B4>{tileData.culture}</color>  " +
                     $"✨ Faith: <color=#DDA0DD>{tileData.faithYield}</color>");
        sb.AppendLine();

        // Terrain properties
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

        // Improvement
        if (showImprovements && tileData.improvement != null)
        {
            sb.AppendLine($"<b>Improvement:</b> {tileData.improvement.improvementName}");
        }
        
        // District
        if (tileData.district != null)
        {
            sb.AppendLine($"<b>District:</b> {tileData.district.districtName}");
        }
        
        // Holy Site
        if (tileData.HasHolySite)
        {
            sb.AppendLine("<b><color=#FFD700>⛪ Holy Site</color></b>");
        }

        // Owner
        if (showOwner && tileData.owner != null)
        {
            string ownerName = tileData.owner.civData?.civName ?? "Unknown";
            sb.AppendLine($"<b>Owner:</b> <color=#87CEEB>{ownerName}</color>");
        }

        // Resources
        if (showResources && tileData.resource != null)
        {
            sb.AppendLine($"<b>Resource:</b> <color=#FFD700>{tileData.resource.resourceName}</color>");
        }

        // Coordinates
        if (showCoordinates)
        {
            sb.AppendLine();
            string bodyName = isMoon ? "Moon" : "Planet";
            sb.AppendLine($"<i><size={fontSize - 4}><color=#888888>{bodyName} Tile #{tileIndex}</color></size></i>");
        }

        // Update text
        if (infoText != null)
        {
            infoText.text = sb.ToString();
            infoText.gameObject.SetActive(true);
        }

        // Update highlight marker
        if (highlightMarker != null)
        {
            Vector3 tilePos = TileSystem.Instance?.GetTileSurfacePositionForBody(tileIndex, isMoon, 0.1f) ?? worldPosition;
            
            // Calculate surface normal
            var generator = isMoon ? 
                (IHexasphereGenerator)GameManager.Instance?.GetCurrentMoonGenerator() : 
                (IHexasphereGenerator)GameManager.Instance?.GetCurrentPlanetGenerator();
            
            if (generator != null)
            {
                Vector3 planetCenter = isMoon ? 
                    GameManager.Instance.GetCurrentMoonGenerator().transform.position :
                    GameManager.Instance.GetCurrentPlanetGenerator().transform.position;
                
                Vector3 normal = (tilePos - planetCenter).normalized;
                
                highlightMarker.transform.position = tilePos;
                highlightMarker.transform.up = normal;
                highlightMarker.SetActive(true);
            }
        }
    }

    private void HideDisplay()
    {
        if (infoText != null)
        {
            infoText.text = "";
            infoText.gameObject.SetActive(false);
        }
        
        if (highlightMarker != null)
        {
            highlightMarker.SetActive(false);
        }
        
        currentHoveredTile = -1;
        isHoveringMoon = false;
    }

    private void CreateDefaultUI()
    {
        // Create a canvas if one doesn't exist
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("TileInfo_Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        // Create background panel
        GameObject panelGO = new GameObject("TileInfo_Panel");
        panelGO.transform.SetParent(canvas.transform, false);
        backgroundPanel = panelGO.AddComponent<UnityEngine.UI.Image>();
        backgroundPanel.color = backgroundColor;

        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1); // Top-left anchor
        panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot = new Vector2(0, 1);
        panelRect.anchoredPosition = new Vector2(10, -10); // 10px from top-left
        panelRect.sizeDelta = new Vector2(300, 200);

        // Create text element
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

        Debug.Log("[TileInfoDisplayImproved] Created default UI");
    }

    private void CreateDefaultHighlight()
    {
        // Create a simple ring/disc to highlight tiles
        highlightMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        highlightMarker.name = "TileHighlight";
        
        // Remove collider (we don't want it blocking raycasts)
        Destroy(highlightMarker.GetComponent<Collider>());
        
        // Scale to be a flat disc
        highlightMarker.transform.localScale = new Vector3(1.2f, 0.05f, 1.2f);
        
        // Create glowing material
        Material highlightMat = new Material(Shader.Find("Unlit/Color"));
        highlightMat.color = new Color(1f, 0.92f, 0.2f, 0.6f); // Yellow-ish glow
        highlightMarker.GetComponent<Renderer>().material = highlightMat;
        
        highlightMarker.SetActive(false);
        
        Debug.Log("[TileInfoDisplayImproved] Created default highlight marker");
    }

    /// <summary>
    /// Toggle display on/off
    /// </summary>
    public void ToggleDisplay(bool show)
    {
        if (infoText != null)
        {
            infoText.gameObject.SetActive(show);
        }
        
        if (backgroundPanel != null)
        {
            backgroundPanel.gameObject.SetActive(show);
        }
    }

    /// <summary>
    /// Public method to get tile under cursor (useful for other systems)
    /// </summary>
    public (bool found, int tileIndex, bool isMoon) GetTileAtCursor()
    {
        var result = GetTileUnderMouse();
        return (result.found, result.tileIndex, result.isMoon);
    }
}

