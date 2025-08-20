using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;
using TMPro;

/// <summary>
/// Visual tech tree builder that allows drag-and-drop positioning of technologies
/// </summary>
public class TechTreeBuilder : MonoBehaviour
{
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
    public GameObject techPaletteItemPrefab;
    public Button addAllTechsButton;
    
    [Header("Node Prefabs")]
    public GameObject techNodePrefab;
    
    [Header("Grid Settings")]
    public float gridSize = 50f;
    public bool showGrid = true;
    public Color gridColor = Color.gray;
    
    [Header("Connection Settings")]
    public Color validConnectionColor = Color.green;
    public Color invalidConnectionColor = Color.red;
    public bool showConnectionPreview = true;
    [Tooltip("Thickness of connection lines in UI pixels")] public float connectionThickness = 3f;
    
    [Header("Status")]
    public TextMeshProUGUI statusText;
    
    private Dictionary<TechData, TechBuilderNode> techNodes = new Dictionary<TechData, TechBuilderNode>();
    private List<GameObject> connectionLines = new List<GameObject>();
    private TechBuilderNode selectedNode;
    private TechBuilderNode draggedNode;
    private bool isConnecting = false;
    private GameObject connectionPreviewLine;
    private Image connectionPreviewImage;
    
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
        if (showGrid && builderContent != null)
        {
            CreateGridBackground();
        }
    }
    
    private void CreateGridBackground()
    {
        // Create a grid background using UI elements
        GameObject gridObj = new GameObject("Grid Background");
        gridObj.transform.SetParent(builderContent, false);
        gridObj.transform.SetAsFirstSibling();
        
        Image gridImage = gridObj.AddComponent<Image>();
        gridImage.color = new Color(gridColor.r, gridColor.g, gridColor.b, 0.1f);
        
        RectTransform gridRect = gridObj.GetComponent<RectTransform>();
        gridRect.anchorMin = Vector2.zero;
        gridRect.anchorMax = Vector2.one;
        gridRect.sizeDelta = Vector2.zero;
        gridRect.anchoredPosition = Vector2.zero;
    }
    
    public void LoadAllTechs()
    {
        if (TechManager.Instance == null) 
        {
            UpdateStatus("TechManager not found! Please ensure TechManager is in the scene.");
            return;
        }
        
        if (TechManager.Instance.allTechs == null || TechManager.Instance.allTechs.Count == 0)
        {
            UpdateStatus("No techs found in TechManager!");
            return;
        }
        
        foreach (var tech in TechManager.Instance.allTechs)
        {
            CreateTechPaletteItem(tech);
        }
        
        UpdateStatus($"Loaded {TechManager.Instance.allTechs.Count} techs into palette.");
    }
    
    public void AddAllTechsToBuilder()
    {
        if (TechManager.Instance == null) return;
        
        foreach (var tech in TechManager.Instance.allTechs)
        {
            if (!techNodes.ContainsKey(tech))
            {
                AddTechToBuilder(tech, GetRandomPosition());
            }
        }
        
        RefreshConnections();
        UpdateStatus($"Added all {TechManager.Instance.allTechs.Count} techs to builder.");
    }
    
    private void CreateTechPaletteItem(TechData tech)
    {
        if (techPaletteItemPrefab == null || techPalette == null)
        {
            Debug.LogError("Tech palette item prefab or palette container not assigned!");
            return;
        }
        
        GameObject paletteItem = Instantiate(techPaletteItemPrefab, techPalette);
        TechPaletteItem item = paletteItem.GetComponent<TechPaletteItem>();
        
        if (item == null)
        {
            item = paletteItem.AddComponent<TechPaletteItem>();
        }
        
        item.Initialize(tech, this);
    }
    
    public void AddTechToBuilder(TechData tech, Vector2 position)
    {
        if (techNodes.ContainsKey(tech)) 
        {
            UpdateStatus($"{tech.techName} is already in the builder.");
            return;
        }
        
        if (techNodePrefab == null || builderContent == null)
        {
            UpdateStatus("Tech node prefab or builder content not assigned!");
            return;
        }
        
        GameObject nodeObj = Instantiate(techNodePrefab, builderContent);
        TechBuilderNode node = nodeObj.GetComponent<TechBuilderNode>();
        
        if (node == null)
        {
            node = nodeObj.AddComponent<TechBuilderNode>();
        }
        
        node.Initialize(tech, this);
        node.SetPosition(position);
        
        techNodes[tech] = node;
        RefreshConnections();
        UpdateStatus($"Added {tech.techName} to builder.");
    }
    
    public void RemoveTechFromBuilder(TechData tech)
    {
        if (techNodes.TryGetValue(tech, out TechBuilderNode node))
        {
            if (selectedNode == node)
                selectedNode = null;
            
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
        // Add dependency (to requires from)
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
    
    public void RemoveConnection(TechData from, TechData to)
    {
        if (to.requiredTechnologies != null)
        {
            var dependencies = to.requiredTechnologies.ToList();
            if (dependencies.Remove(from))
            {
                to.requiredTechnologies = dependencies.ToArray();
                
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(to);
#endif
                
                RefreshConnections();
                UpdateStatus($"Removed connection: {from.techName} → {to.techName}");
            }
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
        
        // Create new connection lines
        foreach (var techNode in techNodes)
        {
            var tech = techNode.Key;
            var node = techNode.Value;
            
            if (tech.requiredTechnologies != null)
            {
                foreach (var dependency in tech.requiredTechnologies)
                {
                    if (dependency != null && techNodes.ContainsKey(dependency))
                    {
                        CreateConnectionLine(techNodes[dependency], node);
                    }
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

        // Keep lines behind nodes but above grid background (sibling 1 if grid is at 0)
        lineObj.transform.SetAsFirstSibling();
        // Try to place just above grid if present
        (lineObj.transform as RectTransform)?.SetSiblingIndex(1);

        var img = lineObj.GetComponent<Image>();
        img.raycastTarget = false;
        img.color = validConnectionColor;

        var rect = lineObj.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        // Layout between node positions
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
        
        if (Input.GetKeyDown(KeyCode.Escape))
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
                connectionPreviewLine.transform.SetAsFirstSibling();
                (connectionPreviewLine.transform as RectTransform)?.SetSiblingIndex(1);
                connectionPreviewImage = connectionPreviewLine.GetComponent<Image>();
                connectionPreviewImage.raycastTarget = false;
                connectionPreviewImage.color = new Color(validConnectionColor.r, validConnectionColor.g, validConnectionColor.b, 0.6f);
                var r = connectionPreviewLine.GetComponent<RectTransform>();
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
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
        UpdateStatus("Save functionality only available in editor.");
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
        UpdateStatus("Load functionality only available in editor.");
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
        if (TechManager.Instance == null) 
        {
            UpdateStatus("TechManager not found for auto layout!");
            return;
        }
        
        // Use dependency-based auto layout
        var layoutResult = DependencyLayoutManager.CalculateDependencyLayout(
            TechManager.Instance.allTechs.ToList(), false);
        
        // Clear existing layout
        ClearLayout();
        
        // Apply auto layout
        foreach (var kvp in layoutResult.nodePositions)
        {
            AddTechToBuilder(kvp.Key, kvp.Value);
        }
        
        // Adjust content size
        builderContent.sizeDelta = new Vector2(
            layoutResult.bounds.width + 200f, 
            layoutResult.bounds.height + 200f);
        
        UpdateStatus($"Auto layout applied with {layoutResult.layers.Count} dependency layers.");
    }
    
    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        
        Debug.Log($"[TechTreeBuilder] {message}");
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
        
        if (TechManager.Instance == null) return;
        
        foreach (var techPos in layout.techPositions)
        {
            var tech = TechManager.Instance.allTechs.Find(t => t.name == techPos.techName);
            if (tech != null)
            {
                AddTechToBuilder(tech, techPos.position);
            }
        }
    }
    
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
}
