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
/// Visual tech tree builder that allows drag-and-drop positioning of technologies
/// </summary>
public class TechTreeBuilder : MonoBehaviour
{
    [Header("Background Settings")]
    [Tooltip("ScriptableObject containing age-based background images")]
    public TechTreeBackgroundData backgroundData;
    
    [Header("Builder UI")]
    public ScrollRect builderScrollRect;
    public RectTransform builderContent;
    public Button saveLayoutButton;
    public Button loadLayoutButton;
    public Button clearLayoutButton;
    public Button autoLayoutButton;
    public Toggle snapToGridToggle;
    
    [Header("Tech Palette")]
    public Transform techPalette;
    public Button addAllTechsButton;
    
    [Header("Grid Settings")]
    public float gridSize = 50f;
    public bool showGrid = true;
    public Color gridColor = Color.gray;
    [Header("Grid Layout")]
    public int gridColumns = 15;  // Reduced columns since cells are wider now
    public int gridRows = 10;     // Reduced rows for better proportions
    public Vector2 cellSize = new Vector2(200f, 100f);  // Rectangular: 2:1 ratio
    public Color emptyCellColor = Color.clear; // Transparent cells
    public Color occupiedCellColor = Color.clear; // Transparent cells
    
    [Header("Connection Settings")]
    public Color validConnectionColor = Color.green;
    public Color invalidConnectionColor = Color.red;
    public bool showConnectionPreview = true;
    [Tooltip("Thickness of connection lines in UI pixels")] public float connectionThickness = 3f;
    
    [Header("Status")]
    public TextMeshProUGUI statusText;
    
    // Discovered TechData assets available to the builder (no TechManager needed)
    private readonly List<TechData> availableTechs = new List<TechData>();
    private readonly Dictionary<string, TechData> techByName = new Dictionary<string, TechData>();

    private Dictionary<TechData, TechBuilderNode> techNodes = new Dictionary<TechData, TechBuilderNode>();
    private List<GameObject> connectionLines = new List<GameObject>();
    private TechBuilderNode selectedNode;
    private TechBuilderNode draggedNode;
    private bool isConnecting = false;
    private GameObject connectionPreviewLine;
    private Image connectionPreviewImage;
    
    // Grid system
    private GameObject[,] gridCells;
    private Dictionary<Vector2Int, TechBuilderNode> gridOccupancy = new Dictionary<Vector2Int, TechBuilderNode>();
    
    public static TechTreeBuilder Instance { get; private set; }
    
    void Awake()
    {
        Instance = this;
    }
    
    void Start()
    {
        SetupUI();
        SetupGrid();
        LoadAllTechs();
        UpdateStatus("Tech Tree Builder loaded. Drag techs from palette to build your tree!");
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
        
        if (addAllTechsButton != null)
            addAllTechsButton.onClick.AddListener(AddAllTechsToBuilder);
    }
    
    private void SetupGrid()
    {
        if (builderContent != null)
        {
            // CRITICAL: Disable any Grid Layout Group that might be overriding our manual positioning
            GridLayoutGroup gridLayout = builderContent.GetComponent<GridLayoutGroup>();
            if (gridLayout != null)
            {
gridLayout.enabled = false;
            }
            
            // Also disable any other layout components that might interfere
            VerticalLayoutGroup vertLayout = builderContent.GetComponent<VerticalLayoutGroup>();
            if (vertLayout != null)
            {
vertLayout.enabled = false;
            }
            
            HorizontalLayoutGroup horizLayout = builderContent.GetComponent<HorizontalLayoutGroup>();
            if (horizLayout != null)
            {
horizLayout.enabled = false;
            }
            
            // Disable Content Size Fitter that can resize elements automatically
            ContentSizeFitter sizeFitter = builderContent.GetComponent<ContentSizeFitter>();
            if (sizeFitter != null)
            {
sizeFitter.enabled = false;
            }
            
            // Disable Layout Element that can override positioning
            LayoutElement layoutElement = builderContent.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
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
return;
        }
        
        // Get all backgrounds in age order
        Sprite[] backgroundImages = backgroundData.GetAllBackgroundsInOrder();
        
        if (backgroundImages.Length == 0)
        {
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
                
                // Add visual background (invisible)
                Image cellImage = cellObj.AddComponent<Image>();
                cellImage.color = Color.clear; // Make cells invisible
                cellImage.raycastTarget = false; // Don't block interactions
                
                // Store reference
                gridCells[x, y] = cellObj;
            }
        }
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
    
    public void LoadAllTechs()
    {
        availableTechs.Clear();
        techByName.Clear();

#if UNITY_EDITOR
        // Editor: find all TechData assets anywhere in the project
        string[] guids = AssetDatabase.FindAssets("t:TechData");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var tech = AssetDatabase.LoadAssetAtPath<TechData>(path);
            if (tech != null && !availableTechs.Contains(tech))
            {
                availableTechs.Add(tech);
                if (!techByName.ContainsKey(tech.name)) techByName.Add(tech.name, tech);
            }
        }
#else
        // Runtime: look in Resources (place TechData assets under a Resources folder)
        var found = ResourceCache.GetAllTechData();
        foreach (var tech in found)
        {
            if (tech != null && !availableTechs.Contains(tech))
            {
                availableTechs.Add(tech);
                if (!techByName.ContainsKey(tech.name)) techByName.Add(tech.name, tech);
            }
        }
#endif

        // Populate palette
        if (availableTechs.Count == 0)
        {
            UpdateStatus("No technologies found. In editor, ensure TechData assets exist; at runtime, place them under a Resources folder.");
            return;
        }

        foreach (var tech in availableTechs.OrderBy(t => t.scienceCost))
        {
            CreateTechPaletteItem(tech);
        }

        UpdateStatus($"Loaded {availableTechs.Count} techs into palette.");
    }
    
    public void AddAllTechsToBuilder()
    {
        int currentIndex = 0;
        foreach (var tech in availableTechs)
        {
            if (!techNodes.ContainsKey(tech))
            {
                // Calculate sequential grid position (left-to-right, top-to-bottom)
                int gridX = currentIndex % gridColumns;
                int gridY = currentIndex / gridColumns;
                
                // Make sure we don't exceed grid bounds
                if (gridY >= gridRows)
                {
                    UpdateStatus($"Grid full! Can only place {gridColumns * gridRows} techs.");
                    break;
                }
                
                Vector2Int gridPos = new Vector2Int(gridX, gridY);
                Vector2 worldPos = GetCellWorldPosition(gridX, gridY);
                
                AddTechToBuilder(tech, worldPos);
                currentIndex++;
            }
        }
        
        RefreshConnections();
        UpdateStatus($"Added {currentIndex} techs to builder in grid layout.");
    }
    
    private void CreateTechPaletteItem(TechData tech)
    {
        if (techPalette == null)
        {
            Debug.LogError("Tech palette container not assigned!");
            return;
        }

        // Create the palette item completely in code - no prefab needed!
        GameObject paletteItem = new GameObject($"PaletteItem_{tech.techName}");
        paletteItem.transform.SetParent(techPalette, false);

        // Add RectTransform and set it up
        RectTransform rect = paletteItem.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(180, 50);
        rect.anchorMin = new Vector2(0, 1); // top-left
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.localScale = Vector3.one;

        // Add background image
        Image backgroundImage = paletteItem.AddComponent<Image>();
        backgroundImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        // Create icon child object
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(paletteItem.transform, false);
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0, 0);
        iconRect.anchorMax = new Vector2(0, 1);
        iconRect.sizeDelta = new Vector2(42, 0);
        iconRect.offsetMin = new Vector2(4, 4);
        iconRect.offsetMax = new Vector2(46, -4);

        Image iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = tech.techIcon; // Use the tech's icon directly!
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;

        // Create text child object
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(paletteItem.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = new Vector2(50, 4);
        textRect.offsetMax = new Vector2(-4, -4);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = tech.techName; // Use the tech's name directly!
        text.fontSize = 11;
        text.fontSizeMin = 8;
        text.fontSizeMax = 14;
        text.enableAutoSizing = true;
        text.color = Color.white;
        text.alignment = TMPro.TextAlignmentOptions.MidlineLeft;

        // Add layout element for proper sizing
        LayoutElement layoutElement = paletteItem.AddComponent<LayoutElement>();
        layoutElement.minWidth = 140;
        layoutElement.minHeight = 40;
        layoutElement.preferredWidth = 180;
        layoutElement.preferredHeight = 50;

        // Add the TechPaletteItem component and set it up
        TechPaletteItem item = paletteItem.AddComponent<TechPaletteItem>();
        item.techIcon = iconImage;
        item.techNameText = text;
        item.backgroundImage = backgroundImage;
        item.Initialize(tech, this);
}
    
    public void AddTechToBuilder(TechData tech, Vector2 position)
    {
        if (techNodes.ContainsKey(tech)) 
        {
            UpdateStatus($"{tech.techName} is already in the builder.");
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
// Get the exact world position for this grid cell
        Vector2 snapPosition = GetCellWorldPosition(finalGridPos.x, finalGridPos.y);
// Create the tech node completely in code - no prefab needed!
        GameObject nodeObj = new GameObject($"TechNode_{tech.techName}");
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
        backgroundImage.color = new Color(0.1f, 0.3f, 0.6f, 0.9f); // Blue background

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
        iconImage.sprite = tech.techIcon; // Use the tech's icon directly!
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
        text.text = tech.techName; // Use the tech's name directly!
        text.fontSize = 12;
        text.fontSizeMin = 8;
        text.fontSizeMax = 16;
        text.enableAutoSizing = true;
        text.color = Color.white;
        text.alignment = TMPro.TextAlignmentOptions.Center;

        // Add the TechBuilderNode component and set it up
        TechBuilderNode node = nodeObj.AddComponent<TechBuilderNode>();
        node.techIcon = iconImage;
        node.techNameText = text;
        node.backgroundImage = backgroundImage;
        node.Initialize(tech, this);
        node.SetGridPosition(finalGridPos); // Set grid position
        
        // Register in our systems
        techNodes[tech] = node;
        gridOccupancy[finalGridPos] = node;
        UpdateCellVisual(finalGridPos, true);
        
        RefreshConnections();
        UpdateStatus($"Added {tech.techName} to builder at grid ({finalGridPos.x}, {finalGridPos.y}).");
    }
    
    public void RemoveTechFromBuilder(TechData tech)
    {
        if (techNodes.TryGetValue(tech, out TechBuilderNode node))
        {
            if (selectedNode == node)
                selectedNode = null;
            
            // Clear grid occupancy
            Vector2Int gridPos = node.GridPosition;
            if (gridOccupancy.ContainsKey(gridPos))
            {
                gridOccupancy.Remove(gridPos);
                UpdateCellVisual(gridPos, false);
            }
            
            Destroy(node.gameObject);
            techNodes.Remove(tech);
            RefreshConnections();
            UpdateStatus($"Removed {tech.techName} from builder.");
        }
    }
    
    public void SelectNode(TechBuilderNode node)
    {
        if (selectedNode != null)
            selectedNode.SetSelected(false);
        
        selectedNode = node;
        if (selectedNode != null)
        {
            selectedNode.SetSelected(true);
            UpdateStatus($"Selected {node.RepresentedTech.techName}. Ctrl+Click another tech to create connection.");
        }
    }
    
    public void StartConnection(TechBuilderNode fromNode)
    {
        if (selectedNode != null && selectedNode != fromNode)
        {
            // Create connection between selected node and this node
            CreateConnection(selectedNode.RepresentedTech, fromNode.RepresentedTech);
            selectedNode.SetSelected(false);
            selectedNode = null;
        }
        else
        {
            isConnecting = true;
            SelectNode(fromNode);
            UpdateStatus($"Click another tech to connect from {fromNode.RepresentedTech.techName}.");
        }
    }
    
    public void CreateConnection(TechData from, TechData to)
    {
        if (from == null || to == null) return;

        var dependencies = to.requiredTechnologies?.ToList() ?? new List<TechData>();
        if (!dependencies.Contains(from))
        {
            dependencies.Add(from);
            to.requiredTechnologies = dependencies.ToArray();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(to);
#endif
            RefreshConnections();
            UpdateStatus($"Created connection: {from.techName} → {to.techName}");
        }
        else
        {
            UpdateStatus($"Connection already exists: {from.techName} → {to.techName}");
        }
    }
    
    public void RefreshConnections()
    {
        // Clear existing connection lines
        foreach (var line in connectionLines)
        {
            if (line != null)
                Destroy(line);
        }
        connectionLines.Clear();

        // Create new connection lines based on current node dependencies
        foreach (var kvp in techNodes)
        {
            TechData tech = kvp.Key;
            TechBuilderNode node = kvp.Value;
            if (tech?.requiredTechnologies == null) continue;

            foreach (var dependency in tech.requiredTechnologies)
            {
                if (dependency != null && techNodes.TryGetValue(dependency, out var depNode))
                {
                    CreateConnectionLine(depNode, node);
                }
            }
        }
    }
    
    private void CreateConnectionLine(TechBuilderNode fromNode, TechBuilderNode toNode)
    {
        if (builderContent == null || fromNode == null || toNode == null) return;

        // Create a lightweight UI Image line
        GameObject lineObj = new GameObject("ConnectionLine", typeof(RectTransform), typeof(Image));
        lineObj.transform.SetParent(builderContent, false);

        // CRITICAL: Place connection lines ABOVE all grid cells but BELOW tech nodes
        // Grid cells are at indices 0 to (gridColumns * gridRows - 1)
        // So place connection lines after all grid cells
        int gridCellCount = gridColumns * gridRows;
        lineObj.transform.SetSiblingIndex(gridCellCount);

        var img = lineObj.GetComponent<Image>();
        img.raycastTarget = false;
        img.color = validConnectionColor;

        var rect = lineObj.GetComponent<RectTransform>();
        // CRITICAL: Use same anchor system as tech nodes (top-left)
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
            RemoveTechFromBuilder(selectedNode.RepresentedTech);
        }
        
        // Only handle Escape if we have an active state to clear
        if (Input.GetKeyDown(KeyCode.Escape) && (isConnecting || selectedNode != null))
        {
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
                // CRITICAL: Place connection preview ABOVE all grid cells but BELOW tech nodes
                int gridCellCount = gridColumns * gridRows;
                connectionPreviewLine.transform.SetSiblingIndex(gridCellCount);
                connectionPreviewImage = connectionPreviewLine.GetComponent<Image>();
                connectionPreviewImage.raycastTarget = false;
                connectionPreviewImage.color = new Color(validConnectionColor.r, validConnectionColor.g, validConnectionColor.b, 0.6f);
                var r = connectionPreviewLine.GetComponent<RectTransform>();
                // CRITICAL: Use same anchor system as tech nodes (top-left)
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
        TechTreeLayout layout = CreateLayoutData();
        string json = JsonUtility.ToJson(layout, true);
        
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.SaveFilePanel(
            "Save Tech Tree Layout", Application.dataPath, "TechTreeLayout", "json");
        
        if (!string.IsNullOrEmpty(path))
        {
            System.IO.File.WriteAllText(path, json);
            UpdateStatus($"Tech tree layout saved to: {path}");
        }
#else
        string path = Application.persistentDataPath + "/TechTreeLayout.json";
        System.IO.File.WriteAllText(path, json);
        UpdateStatus($"Layout saved to {path}");
#endif
    }
    
    public void LoadLayout()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "Load Tech Tree Layout", Application.dataPath, "json");
        
        if (!string.IsNullOrEmpty(path))
        {
            string json = System.IO.File.ReadAllText(path);
            TechTreeLayout layout = JsonUtility.FromJson<TechTreeLayout>(json);
            ApplyLayoutData(layout);
            UpdateStatus($"Tech tree layout loaded from: {path}");
        }
#else
        string path = Application.persistentDataPath + "/TechTreeLayout.json";
        if (System.IO.File.Exists(path))
        {
            string json = System.IO.File.ReadAllText(path);
            TechTreeLayout layout = JsonUtility.FromJson<TechTreeLayout>(json);
            ApplyLayoutData(layout);
            UpdateStatus($"Layout loaded from {path}");
        }
#endif
    }
    
    public void ClearLayout()
    {
        foreach (var node in techNodes.Values.ToList())
        {
            Destroy(node.gameObject);
        }
        techNodes.Clear();
        
        foreach (var line in connectionLines)
        {
            if (line != null)
                Destroy(line);
        }
        connectionLines.Clear();
        
        selectedNode = null;
        UpdateStatus("Layout cleared.");
    }
    
    public void AutoLayout()
    {
        if (availableTechs == null || availableTechs.Count == 0)
        {
            UpdateStatus("No techs available for auto-layout.");
            return;
        }

        // Clear existing layout
        ClearLayout();

        // Build dependency graph and calculate tech levels
        Dictionary<TechData, int> techLevels = CalculateTechLevels();
        Dictionary<int, List<TechData>> techsByLevel = GroupTechsByLevel(techLevels);

        // Position techs level by level
        int currentX = 0;
        foreach (var levelPair in techsByLevel.OrderBy(kvp => kvp.Key))
        {
            int level = levelPair.Key;
            List<TechData> techsInLevel = levelPair.Value;

            // Calculate vertical spacing for this level
            int startY = Mathf.Max(0, (gridRows - techsInLevel.Count) / 2);

            for (int i = 0; i < techsInLevel.Count && startY + i < gridRows; i++)
            {
                TechData tech = techsInLevel[i];
                Vector2Int gridPos = new Vector2Int(currentX, startY + i);

                // Make sure we don't exceed grid bounds
                if (gridPos.x >= gridColumns)
                {
                    UpdateStatus($"Auto-layout stopped: not enough horizontal space. Consider expanding grid or reducing tech count.");
                    break;
                }

                Vector2 worldPos = GetCellWorldPosition(gridPos.x, gridPos.y);
                AddTechToBuilder(tech, worldPos);
            }

            currentX++;
            if (currentX >= gridColumns) break;
        }

        RefreshConnections();
        UpdateStatus($"Auto-arranged {techNodes.Count} techs by dependency levels.");
    }

    private Dictionary<TechData, int> CalculateTechLevels()
    {
        Dictionary<TechData, int> levels = new Dictionary<TechData, int>();
        Dictionary<TechData, HashSet<TechData>> dependencies = new Dictionary<TechData, HashSet<TechData>>();

        // Initialize all techs and their dependencies
        foreach (var tech in availableTechs)
        {
            levels[tech] = 0;
            dependencies[tech] = new HashSet<TechData>();

            if (tech.requiredTechnologies != null)
            {
                foreach (var prereq in tech.requiredTechnologies)
                {
                    if (prereq != null && availableTechs.Contains(prereq))
                    {
                        dependencies[tech].Add(prereq);
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

            foreach (var tech in availableTechs)
            {
                if (dependencies[tech].Count == 0)
                {
                    // No dependencies, stays at level 0
                    continue;
                }

                // Calculate level based on highest prerequisite level + 1
                int maxPrereqLevel = 0;
                foreach (var prereq in dependencies[tech])
                {
                    maxPrereqLevel = Mathf.Max(maxPrereqLevel, levels[prereq]);
                }

                int newLevel = maxPrereqLevel + 1;
                if (newLevel != levels[tech])
                {
                    levels[tech] = newLevel;
                    changed = true;
                }
            }
        }

        // Debug output
foreach (var levelPair in levels.OrderBy(kvp => kvp.Value))
        {
}

        return levels;
    }

    private Dictionary<int, List<TechData>> GroupTechsByLevel(Dictionary<TechData, int> techLevels)
    {
        Dictionary<int, List<TechData>> techsByLevel = new Dictionary<int, List<TechData>>();

        foreach (var pair in techLevels)
        {
            int level = pair.Value;
            TechData tech = pair.Key;

            if (!techsByLevel.ContainsKey(level))
            {
                techsByLevel[level] = new List<TechData>();
            }

            techsByLevel[level].Add(tech);
        }

        // Sort techs within each level by science cost or name for consistent ordering
        foreach (var levelGroup in techsByLevel.Values)
        {
            levelGroup.Sort((a, b) => 
            {
                int costCompare = a.scienceCost.CompareTo(b.scienceCost);
                return costCompare != 0 ? costCompare : string.Compare(a.techName, b.techName);
            });
        }

        return techsByLevel;
    }
    
    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
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
    
    private TechTreeLayout CreateLayoutData()
    {
        TechTreeLayout layout = new TechTreeLayout();
        layout.techPositions = new List<TechPosition>();
        
        foreach (var kvp in techNodes)
        {
            TechPosition techPos = new TechPosition
            {
                techName = kvp.Key.name,
                position = kvp.Value.GetPosition()
            };
            layout.techPositions.Add(techPos);
        }
        
        return layout;
    }
    
    private void ApplyLayoutData(TechTreeLayout layout)
    {
        ClearLayout();
        
        foreach (var techPos in layout.techPositions)
        {
            techByName.TryGetValue(techPos.techName, out var tech);
            if (tech != null)
            {
                AddTechToBuilder(tech, techPos.position);
            }
        }
    }
}

// Layout data structure for saving/loading tech tree layouts
[System.Serializable]
public class TechTreeLayout
{
    public List<TechPosition> techPositions;
}

[System.Serializable]
public class TechPosition
{
    public string techName;
    public Vector2 position;
}
