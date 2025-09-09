using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Visual culture tree builder that allows drag-and-drop positioning of cultures
/// </summary>
public class CultureTreeBuilder : MonoBehaviour
{
    [Header("Background Settings")]
    [Tooltip("ScriptableObject containing age-based background images")]
    public CultureTreeBackgroundData backgroundData;
    
    [Header("Builder UI")]
    public ScrollRect builderScrollRect;
    public RectTransform builderContent;
    public Button saveLayoutButton;
    public Button loadLayoutButton;
    public Button clearLayoutButton;
    public Button autoLayoutButton;
    public Toggle snapToGridToggle;
    
    [Header("Culture Palette")]
    public Transform culturePalette;
    public Button addAllCulturesButton;
    
    [Header("Grid Settings")]
    public float gridSize = 50f;
    public bool showGrid = true;
    public Color gridColor = Color.gray;
    [Header("Grid Layout")]
    public int gridColumns = 15;  // Reduced columns since cells are wider now
    public int gridRows = 10;     // Reduced rows for better proportions
    public Vector2 cellSize = new Vector2(200f, 100f);  // Rectangular: 2:1 ratio
    public Color emptyCellColor = new Color(0.1f, 0.1f, 0.1f, 0.3f);
    public Color occupiedCellColor = new Color(0.4f, 0.2f, 0.4f, 0.5f); // Purple tint for culture
    
    [Header("Connection Settings")]
    public Color validConnectionColor = Color.magenta;
    public Color invalidConnectionColor = Color.red;
    public bool showConnectionPreview = true;
    [Tooltip("Thickness of connection lines in UI pixels")] public float connectionThickness = 3f;
    
    [Header("Status")]
    public TextMeshProUGUI statusText;
    
    // Discovered CultureData assets available to the builder (no CultureManager needed)
    private readonly List<CultureData> availableCultures = new List<CultureData>();
    private readonly Dictionary<string, CultureData> cultureByName = new Dictionary<string, CultureData>();

    private Dictionary<CultureData, CultureBuilderNode> cultureNodes = new Dictionary<CultureData, CultureBuilderNode>();
    private List<GameObject> connectionLines = new List<GameObject>();
    private CultureBuilderNode selectedNode;
    private CultureBuilderNode draggedNode;
    private bool isConnecting = false;
    private GameObject connectionPreviewLine;
    private Image connectionPreviewImage;
    
    // Grid system
    private GameObject[,] gridCells;
    private Dictionary<Vector2Int, CultureBuilderNode> gridOccupancy = new Dictionary<Vector2Int, CultureBuilderNode>();
    
    public static CultureTreeBuilder Instance { get; private set; }
    
    void Awake()
    {
        Instance = this;
    }
    
    void Start()
    {
        SetupUI();
        SetupGrid();
        LoadAllCultures();
        UpdateStatus("Culture Tree Builder loaded. Drag cultures from palette to build your tree!");
    }
    
    void Update()
    {
        HandleInput();
        UpdateConnectionPreview();
    }
    
    private void SetupUI()
    {
        if (saveLayoutButton != null)
            saveLayoutButton.onClick.AddListener(SaveLayout);
        
        if (loadLayoutButton != null)
            loadLayoutButton.onClick.AddListener(LoadLayout);
        
        if (clearLayoutButton != null)
            clearLayoutButton.onClick.AddListener(ClearLayout);
        
        if (autoLayoutButton != null)
            autoLayoutButton.onClick.AddListener(AutoLayout);
        
        if (addAllCulturesButton != null)
            addAllCulturesButton.onClick.AddListener(AddAllCulturesToBuilder);
    }
    
    private void SetupGrid()
    {
        if (builderContent != null)
        {
            // CRITICAL: Disable any Grid Layout Group that might be overriding our manual positioning
            GridLayoutGroup gridLayout = builderContent.GetComponent<GridLayoutGroup>();
            if (gridLayout != null)
            {
                Debug.Log("Found GridLayoutGroup on builderContent - disabling it for manual positioning");
                gridLayout.enabled = false;
            }
            
            // Also disable any other layout components that might interfere
            VerticalLayoutGroup vertLayout = builderContent.GetComponent<VerticalLayoutGroup>();
            if (vertLayout != null)
            {
                Debug.Log("Found VerticalLayoutGroup on builderContent - disabling it for manual positioning");
                vertLayout.enabled = false;
            }
            
            HorizontalLayoutGroup horizLayout = builderContent.GetComponent<HorizontalLayoutGroup>();
            if (horizLayout != null)
            {
                Debug.Log("Found HorizontalLayoutGroup on builderContent - disabling it for manual positioning");
                horizLayout.enabled = false;
            }
            
            // Disable Content Size Fitter that can resize elements automatically
            ContentSizeFitter sizeFitter = builderContent.GetComponent<ContentSizeFitter>();
            if (sizeFitter != null)
            {
                Debug.Log("Found ContentSizeFitter on builderContent - disabling it for manual positioning");
                sizeFitter.enabled = false;
            }
            
            // Disable Layout Element that can override positioning
            LayoutElement layoutElement = builderContent.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                Debug.Log("Found LayoutElement on builderContent - disabling it for manual positioning");
                layoutElement.enabled = false;
            }
            
            CreateBackgroundImages();
            CreateGridCells();
        }
    }
    
    private void CreateBackgroundImages()
    {
        if (backgroundData == null)
        {
            Debug.Log("No background data assigned to CultureTreeBuilder");
            return;
        }
        
        // Get all backgrounds in age order
        Sprite[] backgroundImages = backgroundData.GetAllBackgroundsInOrder();
        
        if (backgroundImages.Length == 0)
        {
            Debug.Log("No background images found in background data");
            return;
        }
        
        // Calculate total background width using the ScriptableObject
        float totalBackgroundWidth = backgroundData.GetTotalWidth();
        float imageHeight = 1024f * backgroundData.backgroundScale;
        
        // Adjust content size to accommodate both grid and background
        float contentWidth = Mathf.Max(gridColumns * cellSize.x, totalBackgroundWidth);
        float contentHeight = Mathf.Max(gridRows * cellSize.y, imageHeight);
        builderContent.sizeDelta = new Vector2(contentWidth, contentHeight);
        
        // Create background container
        GameObject backgroundContainer = new GameObject("BackgroundContainer");
        backgroundContainer.transform.SetParent(builderContent, false);
        
        RectTransform bgRect = backgroundContainer.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 1); // Top-left origin
        bgRect.anchorMax = new Vector2(0, 1);
        bgRect.pivot = new Vector2(0, 1);
        bgRect.sizeDelta = new Vector2(totalBackgroundWidth, imageHeight);
        bgRect.anchoredPosition = Vector2.zero;
        
        // Set background to render behind everything else
        backgroundContainer.transform.SetAsFirstSibling();
        
        // Create individual background images for each age
        var allAges = System.Enum.GetValues(typeof(TechAge));
        float currentX = 0f;
        int imageIndex = 0;
        
        foreach (TechAge age in allAges)
        {
            Sprite ageBackground = backgroundData.GetBackgroundForAge(age);
            if (ageBackground == null) continue;
            
            GameObject bgImageObj = new GameObject($"Background_{age}");
            bgImageObj.transform.SetParent(backgroundContainer.transform, false);
            
            RectTransform imageRect = bgImageObj.AddComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0, 1); // Top-left origin
            imageRect.anchorMax = new Vector2(0, 1);
            imageRect.pivot = new Vector2(0, 1);
            
            // Get width for this age
            float ageWidth = backgroundData.GetWidthForAge(age);
            imageRect.sizeDelta = new Vector2(ageWidth, imageHeight);
            
            // Position based on current X
            imageRect.anchoredPosition = new Vector2(currentX, 0);
            
            // Add Image component
            Image bgImage = bgImageObj.AddComponent<Image>();
            bgImage.sprite = ageBackground;
            bgImage.type = Image.Type.Simple;
            bgImage.raycastTarget = false; // Don't block interactions
            
            // Ensure proper aspect ratio
            bgImage.preserveAspect = true;
            
            // Move to next position
            currentX += ageWidth + backgroundData.imageSpacing;
            imageIndex++;
        }
        
        Debug.Log($"Created {imageIndex} age-based background images with total width: {totalBackgroundWidth}px");
    }
    
    private void CreateGridCells()
    {
        // Content size is already set in CreateBackgroundImages()
        
        // Initialize grid array
        gridCells = new GameObject[gridColumns, gridRows];
        
        // Create visual grid cells
        for (int x = 0; x < gridColumns; x++)
        {
            for (int y = 0; y < gridRows; y++)
            {
                GameObject cellObj = new GameObject($"GridCell_{x}_{y}");
                cellObj.transform.SetParent(builderContent, false);
                
                RectTransform cellRect = cellObj.AddComponent<RectTransform>();
                cellRect.sizeDelta = new Vector2(cellSize.x - 2, cellSize.y - 2); // Small gap between cells
                cellRect.anchorMin = new Vector2(0, 1); // Top-left origin
                cellRect.anchorMax = new Vector2(0, 1);
                cellRect.pivot = new Vector2(0.5f, 0.5f);
                
                // Position the cell
                Vector2 cellPosition = GetCellWorldPosition(x, y);
                cellRect.anchoredPosition = cellPosition;
                
                // Add visual background
                Image cellImage = cellObj.AddComponent<Image>();
                cellImage.color = emptyCellColor;
                cellImage.raycastTarget = false; // Don't block interactions
                
                // Store reference
                gridCells[x, y] = cellObj;
            }
        }
        
        Debug.Log($"Created {gridColumns}x{gridRows} grid with {cellSize.x}x{cellSize.y}px cells");
    }
    
    private Vector2 GetCellWorldPosition(int gridX, int gridY)
    {
        // Convert grid coordinates to world position
        float worldX = (gridX * cellSize.x) + (cellSize.x * 0.5f);
        float worldY = -(gridY * cellSize.y) - (cellSize.y * 0.5f); // Negative because UI Y goes down
        return new Vector2(worldX, worldY);
    }
    
    private Vector2Int GetNearestGridCell(Vector2 worldPosition)
    {
        // Convert world position to grid coordinates
        int gridX = Mathf.RoundToInt((worldPosition.x - cellSize.x * 0.5f) / cellSize.x);
        int gridY = Mathf.RoundToInt((-worldPosition.y - cellSize.y * 0.5f) / cellSize.y);
        
        // Clamp to grid bounds
        gridX = Mathf.Clamp(gridX, 0, gridColumns - 1);
        gridY = Mathf.Clamp(gridY, 0, gridRows - 1);
        
        return new Vector2Int(gridX, gridY);
    }
    
    private bool IsGridCellOccupied(Vector2Int gridPos)
    {
        return gridOccupancy.ContainsKey(gridPos);
    }
    
    private Vector2Int FindNearestEmptyCell(Vector2Int preferredPos)
    {
        // Start from preferred position and spiral outward
        for (int radius = 0; radius < Mathf.Max(gridColumns, gridRows); radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius) continue; // Only check border of current radius
                    
                    Vector2Int testPos = preferredPos + new Vector2Int(dx, dy);
                    if (testPos.x >= 0 && testPos.x < gridColumns && 
                        testPos.y >= 0 && testPos.y < gridRows && 
                        !IsGridCellOccupied(testPos))
                    {
                        return testPos;
                    }
                }
            }
        }
        
        // Fallback to first available cell
        for (int x = 0; x < gridColumns; x++)
        {
            for (int y = 0; y < gridRows; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
                if (!IsGridCellOccupied(pos))
                    return pos;
            }
        }
        
        return preferredPos; // Return original if no empty cells (shouldn't happen normally)
    }
    
    private void UpdateCellVisual(Vector2Int gridPos, bool occupied)
    {
        if (gridPos.x >= 0 && gridPos.x < gridColumns && gridPos.y >= 0 && gridPos.y < gridRows)
        {
            GameObject cell = gridCells[gridPos.x, gridPos.y];
            if (cell != null)
            {
                Image cellImage = cell.GetComponent<Image>();
                if (cellImage != null)
                {
                    cellImage.color = occupied ? occupiedCellColor : emptyCellColor;
                }
            }
        }
    }
    
    public void LoadAllCultures()
    {
        availableCultures.Clear();
        cultureByName.Clear();

#if UNITY_EDITOR
        // Editor: find all CultureData assets anywhere in the project
        string[] guids = AssetDatabase.FindAssets("t:CultureData");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var culture = AssetDatabase.LoadAssetAtPath<CultureData>(path);
            if (culture != null && !availableCultures.Contains(culture))
            {
                availableCultures.Add(culture);
                if (!cultureByName.ContainsKey(culture.name)) cultureByName.Add(culture.name, culture);
            }
        }
#else
        // Runtime: look in Resources (place CultureData assets under a Resources folder)
        var found = Resources.LoadAll<CultureData>(string.Empty);
        foreach (var culture in found)
        {
            if (culture != null && !availableCultures.Contains(culture))
            {
                availableCultures.Add(culture);
                if (!cultureByName.ContainsKey(culture.name)) cultureByName.Add(culture.name, culture);
            }
        }
#endif

        // Populate palette
        if (availableCultures.Count == 0)
        {
            UpdateStatus("No cultures found. In editor, ensure CultureData assets exist; at runtime, place them under a Resources folder.");
            return;
        }

        foreach (var culture in availableCultures.OrderBy(c => c.cultureCost))
        {
            CreateCulturePaletteItem(culture);
        }

        UpdateStatus($"Loaded {availableCultures.Count} cultures into palette.");
    }
    
    public void AddAllCulturesToBuilder()
    {
        int currentIndex = 0;
        foreach (var culture in availableCultures)
        {
            if (!cultureNodes.ContainsKey(culture))
            {
                // Calculate sequential grid position (left-to-right, top-to-bottom)
                int gridX = currentIndex % gridColumns;
                int gridY = currentIndex / gridColumns;
                
                // Make sure we don't exceed grid bounds
                if (gridY >= gridRows)
                {
                    UpdateStatus($"Grid full! Can only place {gridColumns * gridRows} cultures.");
                    break;
                }
                
                Vector2Int gridPos = new Vector2Int(gridX, gridY);
                Vector2 worldPos = GetCellWorldPosition(gridX, gridY);
                
                AddCultureToBuilder(culture, worldPos);
                currentIndex++;
            }
        }
        
        RefreshConnections();
        UpdateStatus($"Added {currentIndex} cultures to builder in grid layout.");
    }
    
    private void CreateCulturePaletteItem(CultureData culture)
    {
        if (culturePalette == null)
        {
            Debug.LogError("Culture palette container not assigned!");
            return;
        }

        // Create the palette item completely in code - no prefab needed!
        GameObject paletteItem = new GameObject($"PaletteItem_{culture.cultureName}");
        paletteItem.transform.SetParent(culturePalette, false);

        // Add RectTransform and set it up
        RectTransform rect = paletteItem.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(180, 50);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;

        // Add background image
        Image backgroundImage = paletteItem.AddComponent<Image>();
        backgroundImage.color = new Color(0.4f, 0.2f, 0.4f, 0.9f); // Purple background for culture

        // Create icon child object
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(paletteItem.transform, false);
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0, 0.2f);
        iconRect.anchorMax = new Vector2(0.3f, 0.8f);
        iconRect.offsetMin = new Vector2(5, 0);
        iconRect.offsetMax = new Vector2(-5, 0);

        Image iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = culture.cultureIcon; // Use the culture's icon directly!
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;

        // Create text child object
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(paletteItem.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.35f, 0.1f);
        textRect.anchorMax = new Vector2(1f, 0.9f);
        textRect.offsetMin = new Vector2(0, 0);
        textRect.offsetMax = new Vector2(-5, 0);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = culture.cultureName; // Use the culture's name directly!
        text.fontSize = 10;
        text.fontSizeMin = 6;
        text.fontSizeMax = 12;
        text.enableAutoSizing = true;
        text.color = Color.white;
        text.alignment = TMPro.TextAlignmentOptions.Left;

        // Add the CulturePaletteItem component and set it up
        CulturePaletteItem paletteComponent = paletteItem.AddComponent<CulturePaletteItem>();
        paletteComponent.cultureIcon = iconImage;
        paletteComponent.cultureNameText = text;
        paletteComponent.backgroundImage = backgroundImage;
        paletteComponent.Initialize(culture, this);

        // Wire UI interactions for dynamic palette item (click sounds, focus, etc.)
        if (UIManager.Instance != null)
            UIManager.Instance.WireUIInteractions(paletteItem);

        Debug.Log($"[CreateCulturePaletteItem] Created palette item for {culture.cultureName} with icon: {(culture.cultureIcon != null ? "YES" : "NO")}");
    }
    
    public void AddCultureToBuilder(CultureData culture, Vector2 position)
    {
        if (cultureNodes.ContainsKey(culture)) 
        {
            UpdateStatus($"{culture.cultureName} is already in the builder.");
            return;
        }
        
        if (builderContent == null)
        {
            UpdateStatus("Builder content not assigned!");
            return;
        }
        
        // Find the best grid cell for this position
        Vector2Int preferredGridPos = GetNearestGridCell(position);
        Vector2Int finalGridPos = IsGridCellOccupied(preferredGridPos) ? 
            FindNearestEmptyCell(preferredGridPos) : preferredGridPos;
        
        // Debug logging for positioning
        Debug.Log($"[AddCultureToBuilder] {culture.cultureName}: input position={position}, preferred grid=({preferredGridPos.x},{preferredGridPos.y}), final grid=({finalGridPos.x},{finalGridPos.y})");
        
        // Get the exact world position for this grid cell
        Vector2 snapPosition = GetCellWorldPosition(finalGridPos.x, finalGridPos.y);
        Debug.Log($"[AddCultureToBuilder] {culture.cultureName}: snap position={snapPosition}");
        
        // Create the culture node completely in code - no prefab needed!
        GameObject nodeObj = new GameObject($"CultureNode_{culture.cultureName}");
        nodeObj.transform.SetParent(builderContent, false);

        // Add RectTransform and set it up
        RectTransform rect = nodeObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(cellSize.x - 10, cellSize.y - 10); // Slightly smaller than cell
        rect.anchorMin = new Vector2(0, 1); // Top-left origin to match grid
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.anchoredPosition = snapPosition;

        // Add background image
        Image backgroundImage = nodeObj.AddComponent<Image>();
        backgroundImage.color = new Color(0.4f, 0.1f, 0.4f, 0.9f); // Purple background for culture

        // Create icon child object
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(nodeObj.transform, false);
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        // For wider cells, put icon on the left side
        iconRect.anchorMin = new Vector2(0.1f, 0.2f);
        iconRect.anchorMax = new Vector2(0.4f, 0.8f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;

        Image iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = culture.cultureIcon; // Use the culture's icon directly!
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;

        // Create text child object
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(nodeObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        // For wider cells, put text on the right side
        textRect.anchorMin = new Vector2(0.45f, 0.1f);
        textRect.anchorMax = new Vector2(0.95f, 0.9f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = culture.cultureName; // Use the culture's name directly!
        text.fontSize = 12;
        text.fontSizeMin = 8;
        text.fontSizeMax = 16;
        text.enableAutoSizing = true;
        text.color = Color.white;
        text.alignment = TMPro.TextAlignmentOptions.Left;

        // Add the CultureBuilderNode component and set it up
        CultureBuilderNode node = nodeObj.AddComponent<CultureBuilderNode>();
        node.cultureIcon = iconImage;
        node.cultureNameText = text;
        node.backgroundImage = backgroundImage;
        node.Initialize(culture, this);
        node.SetGridPosition(finalGridPos); // Set grid position
        
        // Register in our systems
        cultureNodes[culture] = node;
        gridOccupancy[finalGridPos] = node;
        UpdateCellVisual(finalGridPos, true);
        
        // Wire UI interactions for the dynamic culture node
        if (UIManager.Instance != null)
            UIManager.Instance.WireUIInteractions(nodeObj);

        RefreshConnections();
        UpdateStatus($"Added {culture.cultureName} to builder at grid ({finalGridPos.x}, {finalGridPos.y}).");
    }
    
    public void RemoveCultureFromBuilder(CultureData culture)
    {
        if (cultureNodes.TryGetValue(culture, out CultureBuilderNode node))
        {
            if (selectedNode == node)
                selectedNode = null;

            Vector2Int gridPos = node.GridPosition;
            if (gridOccupancy.ContainsKey(gridPos))
            {
                gridOccupancy.Remove(gridPos);
                UpdateCellVisual(gridPos, false);
            }

            cultureNodes.Remove(culture);
            Destroy(node.gameObject);
            RefreshConnections();
            UpdateStatus($"Removed {culture.cultureName} from builder.");
        }
    }
    
    public void SelectNode(CultureBuilderNode node)
    {
        if (selectedNode != null)
            selectedNode.SetSelected(false);

        selectedNode = node;
        if (selectedNode != null)
        {
            selectedNode.SetSelected(true);
            UpdateStatus($"Selected {selectedNode.RepresentedCulture.cultureName}. Ctrl+Click another culture to create connection.");
        }
    }
    
    public void StartConnection(CultureBuilderNode fromNode)
    {
        if (selectedNode != null && selectedNode != fromNode)
        {
            // Connect these two nodes
            CreateConnection(selectedNode.RepresentedCulture, fromNode.RepresentedCulture);
            isConnecting = false;
        }
        else
        {
            selectedNode = fromNode;
            fromNode.SetSelected(true);
            isConnecting = true;
            UpdateStatus($"Connecting from {fromNode.RepresentedCulture.cultureName}. Click another culture to connect.");
        }
    }
    
    public void CreateConnection(CultureData from, CultureData to)
    {
        if (from == null || to == null) return;

        // Add dependency to the target culture
        var dependencies = to.requiredCultures?.ToList() ?? new List<CultureData>();
        if (!dependencies.Contains(from))
        {
#if UNITY_EDITOR
            // In editor, we can modify the ScriptableObject
            var newDeps = new CultureData[dependencies.Count + 1];
            dependencies.CopyTo(newDeps);
            newDeps[dependencies.Count] = from;
            to.requiredCultures = newDeps;
            EditorUtility.SetDirty(to);
#endif
            UpdateStatus($"Connected {from.cultureName} → {to.cultureName}");
        }
        else
        {
            UpdateStatus($"{from.cultureName} is already a prerequisite for {to.cultureName}");
        }

        RefreshConnections();
    }
    
    public void RefreshConnections()
    {
        // Clear existing connection lines
        foreach (var line in connectionLines)
        {
            if (line != null) Destroy(line);
        }
        connectionLines.Clear();

        // Create connection lines for all dependencies
        foreach (var nodePair in cultureNodes)
        {
            var culture = nodePair.Key;
            var node = nodePair.Value;

            if (culture.requiredCultures != null)
            {
                foreach (var prerequisite in culture.requiredCultures)
                {
                    if (prerequisite != null && cultureNodes.TryGetValue(prerequisite, out CultureBuilderNode depNode))
                    {
                        CreateConnectionLine(depNode, node);
                    }
                }
            }
        }
    }
    
    private void CreateConnectionLine(CultureBuilderNode fromNode, CultureBuilderNode toNode)
    {
        if (builderContent == null || fromNode == null || toNode == null) return;

        // Create a lightweight UI Image line
        GameObject lineObj = new GameObject("ConnectionLine", typeof(RectTransform), typeof(Image));
        lineObj.transform.SetParent(builderContent, false);

        // CRITICAL: Place connection lines ABOVE all grid cells but BELOW culture nodes
        // Grid cells are at indices 0 to (gridColumns * gridRows - 1)
        // So place connection lines after all grid cells
        int gridCellCount = gridColumns * gridRows;
        lineObj.transform.SetSiblingIndex(gridCellCount);

        var img = lineObj.GetComponent<Image>();
        img.raycastTarget = false;
        img.color = validConnectionColor;

        var rect = lineObj.GetComponent<RectTransform>();
        // CRITICAL: Use same anchor system as culture nodes (top-left)
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);

        // Layout between node positions (they already use the correct coordinate system)
        Vector2 a = fromNode.GetPosition();
        Vector2 b = toNode.GetPosition();
        LayoutLineBetween(rect, a, b, connectionThickness);

        connectionLines.Add(lineObj);
    }
    
    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Delete) && selectedNode != null)
        {
            RemoveCultureFromBuilder(selectedNode.RepresentedCulture);
        }
        
        // Only handle Escape if we have an active state to clear
        if (Input.GetKeyDown(KeyCode.Escape) && (isConnecting || selectedNode != null))
        {
            Debug.Log("[CultureTreeBuilder] Escape pressed - clearing selection/connection state");
            isConnecting = false;
            if (selectedNode != null)
                selectedNode.SetSelected(false);
            selectedNode = null;
            UpdateStatus("Selection cleared.");
        }
    }
    
    private void UpdateConnectionPreview()
    {
        if (isConnecting && selectedNode != null && showConnectionPreview)
        {
            if (connectionPreviewLine == null)
            {
                connectionPreviewLine = new GameObject("ConnectionPreview", typeof(RectTransform), typeof(Image));
                connectionPreviewLine.transform.SetParent(builderContent, false);
                // CRITICAL: Place connection preview ABOVE all grid cells but BELOW culture nodes
                int gridCellCount = gridColumns * gridRows;
                connectionPreviewLine.transform.SetSiblingIndex(gridCellCount);
                connectionPreviewImage = connectionPreviewLine.GetComponent<Image>();
                connectionPreviewImage.raycastTarget = false;
                connectionPreviewImage.color = new Color(validConnectionColor.r, validConnectionColor.g, validConnectionColor.b, 0.6f);
                var r = connectionPreviewLine.GetComponent<RectTransform>();
                // CRITICAL: Use same anchor system as culture nodes (top-left)
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0.5f, 0.5f);
            }

            Vector2 mousePos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                builderContent, Input.mousePosition, null, out mousePos);

            Vector2 from = selectedNode.GetPosition();
            LayoutLineBetween(connectionPreviewLine.GetComponent<RectTransform>(), from, mousePos, connectionThickness);
        }
        else
        {
            if (connectionPreviewLine != null)
            {
                Destroy(connectionPreviewLine);
                connectionPreviewLine = null;
                connectionPreviewImage = null;
            }
        }
    }

    // Helper to position a UI Image RectTransform as a line between two points in local space
    private void LayoutLineBetween(RectTransform rect, Vector2 a, Vector2 b, float thickness)
    {
        Vector2 dir = b - a;
        float length = dir.magnitude;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        rect.anchoredPosition = (a + b) * 0.5f;
        rect.sizeDelta = new Vector2(length, Mathf.Max(1f, thickness));
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
    
    private Vector2 GetRandomPosition()
    {
        return new Vector2(
            Random.Range(100f, builderContent.sizeDelta.x - 100f),
            Random.Range(-builderContent.sizeDelta.y + 100f, -100f)
        );
    }
    
    public void SaveLayout()
    {
        CultureTreeLayout layout = CreateLayoutData();
        string json = JsonUtility.ToJson(layout, true);
        
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.SaveFilePanel(
            "Save Culture Tree Layout", Application.dataPath, "CultureTreeLayout", "json");
        
        if (!string.IsNullOrEmpty(path))
        {
            System.IO.File.WriteAllText(path, json);
            UpdateStatus($"Culture tree layout saved to: {path}");
        }
#else
        string path = Application.persistentDataPath + "/CultureTreeLayout.json";
        System.IO.File.WriteAllText(path, json);
        UpdateStatus($"Layout saved to {path}");
#endif
    }
    
    public void LoadLayout()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "Load Culture Tree Layout", Application.dataPath, "json");
        
        if (!string.IsNullOrEmpty(path))
        {
            string json = System.IO.File.ReadAllText(path);
            CultureTreeLayout layout = JsonUtility.FromJson<CultureTreeLayout>(json);
            ApplyLayoutData(layout);
            UpdateStatus($"Layout loaded from {path}");
        }
#else
        string path = Application.persistentDataPath + "/CultureTreeLayout.json";
        if (System.IO.File.Exists(path))
        {
            string json = System.IO.File.ReadAllText(path);
            CultureTreeLayout layout = JsonUtility.FromJson<CultureTreeLayout>(json);
            ApplyLayoutData(layout);
            UpdateStatus($"Layout loaded from {path}");
        }
#endif
    }
    
    public void ClearLayout()
    {
        foreach (var node in cultureNodes.Values.ToList())
        {
            RemoveCultureFromBuilder(node.RepresentedCulture);
        }
        
        selectedNode = null;
        UpdateStatus("Layout cleared.");
    }
    
    public void AutoLayout()
    {
        if (availableCultures == null || availableCultures.Count == 0)
        {
            UpdateStatus("No cultures available for auto-layout.");
            return;
        }

        // Clear existing layout
        ClearLayout();

        // Build dependency graph and calculate culture levels
        Dictionary<CultureData, int> cultureLevels = CalculateCultureLevels();
        Dictionary<int, List<CultureData>> culturesByLevel = GroupCulturesByLevel(cultureLevels);

        // Position cultures level by level
        int currentX = 0;
        foreach (var levelPair in culturesByLevel.OrderBy(kvp => kvp.Key))
        {
            int level = levelPair.Key;
            List<CultureData> culturesInLevel = levelPair.Value;

            // Calculate vertical spacing for this level
            int startY = Mathf.Max(0, (gridRows - culturesInLevel.Count) / 2);

            for (int i = 0; i < culturesInLevel.Count && startY + i < gridRows; i++)
            {
                CultureData culture = culturesInLevel[i];
                Vector2Int gridPos = new Vector2Int(currentX, startY + i);

                // Make sure we don't exceed grid bounds
                if (gridPos.x >= gridColumns)
                {
                    UpdateStatus($"Auto-layout stopped: not enough horizontal space. Consider expanding grid or reducing culture count.");
                    break;
                }

                Vector2 worldPos = GetCellWorldPosition(gridPos.x, gridPos.y);
                AddCultureToBuilder(culture, worldPos);
            }

            currentX++;
            if (currentX >= gridColumns) break;
        }

        RefreshConnections();
        UpdateStatus($"Auto-arranged {cultureNodes.Count} cultures by dependency levels.");
    }

    private Dictionary<CultureData, int> CalculateCultureLevels()
    {
        Dictionary<CultureData, int> levels = new Dictionary<CultureData, int>();
        Dictionary<CultureData, HashSet<CultureData>> dependencies = new Dictionary<CultureData, HashSet<CultureData>>();

        // Initialize all cultures and their dependencies
        foreach (var culture in availableCultures)
        {
            levels[culture] = 0;
            dependencies[culture] = new HashSet<CultureData>();

            if (culture.requiredCultures != null)
            {
                foreach (var prereq in culture.requiredCultures)
                {
                    if (prereq != null && availableCultures.Contains(prereq))
                    {
                        dependencies[culture].Add(prereq);
                    }
                }
            }
        }

        // Calculate levels using topological sort approach
        bool changed = true;
        int maxIterations = 100; // Prevent infinite loops
        int iteration = 0;

        while (changed && iteration < maxIterations)
        {
            changed = false;
            iteration++;

            foreach (var culture in availableCultures)
            {
                if (dependencies[culture].Count == 0)
                {
                    // No dependencies, stays at level 0
                    continue;
                }

                // Calculate level based on highest prerequisite level + 1
                int maxPrereqLevel = 0;
                foreach (var prereq in dependencies[culture])
                {
                    maxPrereqLevel = Mathf.Max(maxPrereqLevel, levels[prereq]);
                }

                int newLevel = maxPrereqLevel + 1;
                if (newLevel != levels[culture])
                {
                    levels[culture] = newLevel;
                    changed = true;
                }
            }
        }

        // Debug output
        Debug.Log($"Calculated culture levels in {iteration} iterations:");
        foreach (var levelPair in levels.OrderBy(kvp => kvp.Value))
        {
            Debug.Log($"  Level {levelPair.Value}: {levelPair.Key.cultureName}");
        }

        return levels;
    }

    private Dictionary<int, List<CultureData>> GroupCulturesByLevel(Dictionary<CultureData, int> cultureLevels)
    {
        Dictionary<int, List<CultureData>> culturesByLevel = new Dictionary<int, List<CultureData>>();

        foreach (var pair in cultureLevels)
        {
            int level = pair.Value;
            CultureData culture = pair.Key;

            if (!culturesByLevel.ContainsKey(level))
            {
                culturesByLevel[level] = new List<CultureData>();
            }

            culturesByLevel[level].Add(culture);
        }

        // Sort cultures within each level by culture cost or name for consistent ordering
        foreach (var levelGroup in culturesByLevel.Values)
        {
            levelGroup.Sort((a, b) => 
            {
                int costCompare = a.cultureCost.CompareTo(b.cultureCost);
                return costCompare != 0 ? costCompare : string.Compare(a.cultureName, b.cultureName);
            });
        }

        return culturesByLevel;
    }
    
    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        
        Debug.Log($"[CultureTreeBuilder] {message}");
    }
    
    // Public methods for grid access
    public Vector2 GetCellWorldPositionPublic(int gridX, int gridY)
    {
        return GetCellWorldPosition(gridX, gridY);
    }
    
    public Vector2Int GetNearestGridCellPublic(Vector2 worldPosition)
    {
        return GetNearestGridCell(worldPosition);
    }
    
    private CultureTreeLayout CreateLayoutData()
    {
        var layout = new CultureTreeLayout();
        layout.culturePositions = new List<CulturePosition>();
        
        foreach (var pair in cultureNodes)
        {
            layout.culturePositions.Add(new CulturePosition
            {
                cultureName = pair.Key.cultureName,
                position = pair.Value.GetPosition()
            });
        }
        
        return layout;
    }
    
    private void ApplyLayoutData(CultureTreeLayout layout)
    {
        ClearLayout();
        
        if (layout?.culturePositions == null) return;
        
        foreach (var culturePos in layout.culturePositions)
        {
            if (cultureByName.TryGetValue(culturePos.cultureName, out CultureData culture))
            {
                AddCultureToBuilder(culture, culturePos.position);
            }
        }
        
        RefreshConnections();
    }
    
    [System.Serializable]
    public class CultureTreeLayout
    {
        public List<CulturePosition> culturePositions;
    }
    
    [System.Serializable]
    public class CulturePosition
    {
        public string cultureName;
        public Vector2 position;
    }
}
