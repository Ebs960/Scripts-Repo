using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual connection line between tech nodes in the builder
/// </summary>
public class ConnectionLine : MonoBehaviour
{
    [Header("Line Settings")]
    public Image lineImage;
    public Image arrowHead;
    public Color lineColor = Color.white;
    public float lineWidth = 3f;
    
    private TechBuilderNode fromNode;
    private TechBuilderNode toNode;
    private RectTransform rectTransform;
    
    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        
        if (lineImage == null)
            lineImage = GetComponent<Image>();
    }
    
    public void Setup(TechBuilderNode from, TechBuilderNode to)
    {
        fromNode = from;
        toNode = to;
        
        if (lineImage != null)
            lineImage.color = lineColor;
        
        UpdateLine();
    }
    
    void Update()
    {
        if (fromNode != null && toNode != null)
        {
            UpdateLine();
        }
    }
    
    private void UpdateLine()
    {
        if (fromNode == null || toNode == null) return;
        
        Vector2 fromPos = fromNode.GetPosition();
        Vector2 toPos = toNode.GetPosition();
        
        // Calculate line properties
        Vector2 direction = toPos - fromPos;
        float distance = direction.magnitude;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        
        // Position and rotate the line
        rectTransform.anchoredPosition = fromPos + direction * 0.5f;
        rectTransform.sizeDelta = new Vector2(distance, lineWidth);
        rectTransform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        
        // Update arrow head if present
        if (arrowHead != null)
        {
            Vector2 arrowPos = toPos - direction.normalized * 30f; // Offset from node
            arrowHead.rectTransform.anchoredPosition = arrowPos;
            arrowHead.rectTransform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
    
    public void SetColor(Color color)
    {
        lineColor = color;
        if (lineImage != null)
            lineImage.color = color;
        if (arrowHead != null)
            arrowHead.color = color;
    }
    
    public void OnDestroy()
    {
        // Clean up references
        fromNode = null;
        toNode = null;
    }
}
