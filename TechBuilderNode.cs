using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Draggable tech node for the tree builder
/// </summary>
public class TechBuilderNode : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, 
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI Components")]
    public Image techIcon;
    public Image backgroundImage;
    public TextMeshProUGUI techNameText;
    public Button deleteButton;
    public Button connectButton;
    
    [Header("Visual States")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color draggingColor = Color.cyan;
    public Color hoverColor = Color.gray;
    
    public TechData RepresentedTech { get; private set; }
    public TechTreeBuilder Builder { get; private set; }
    public Vector2Int GridPosition { get; private set; }
    
    private RectTransform rectTransform;
    private bool isDragging = false;
    private bool isSelected = false;
    private Vector2 originalPosition;
    
    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        
        if (deleteButton != null)
            deleteButton.onClick.AddListener(DeleteNode);
        
        if (connectButton != null)
            connectButton.onClick.AddListener(StartConnection);
    }
    
    public void Initialize(TechData tech, TechTreeBuilder builder)
    {
        RepresentedTech = tech;
        Builder = builder;
        
        if (tech.techIcon != null && techIcon != null)
            techIcon.sprite = tech.techIcon;
        
        if (techNameText != null)
        {
            techNameText.text = tech.techName;
            techNameText.enableAutoSizing = true;
            techNameText.fontSizeMin = 8f;
            techNameText.fontSizeMax = 12f;
        }
        
        UpdateVisualState();
    }
    
    public void SetPosition(Vector2 position)
    {
        if (Builder != null && Builder.snapToGridToggle != null && Builder.snapToGridToggle.isOn)
        {
            position = SnapToGrid(position);
        }
        
        rectTransform.anchoredPosition = position;
    }
    
    public void SetGridPosition(Vector2Int gridPos)
    {
        GridPosition = gridPos;
        // Position will be set by the builder when creating the node
    }
    
    public Vector2 GetPosition()
    {
        return rectTransform.anchoredPosition;
    }
    
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateVisualState();
    }
    
    private void UpdateVisualState()
    {
        if (backgroundImage == null) return;
        
        Color targetColor = normalColor;
        
        if (isDragging)
            targetColor = draggingColor;
        else if (isSelected)
            targetColor = selectedColor;
        else
            targetColor = normalColor;
        
        backgroundImage.color = targetColor;
    }
    
    private Vector2 SnapToGrid(Vector2 position)
    {
        float gridSize = Builder != null ? Builder.gridSize : 50f;
        float snappedX = Mathf.Round(position.x / gridSize) * gridSize;
        float snappedY = Mathf.Round(position.y / gridSize) * gridSize;
        return new Vector2(snappedX, snappedY);
    }
    
    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true;
        originalPosition = rectTransform.anchoredPosition;
        UpdateVisualState();
        
        // Bring to front
        transform.SetAsLastSibling();
    }
    
    public void OnDrag(PointerEventData eventData)
    {
        Vector2 localPoint;
        if (Builder != null && Builder.builderContent != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Builder.builderContent, eventData.position, eventData.pressEventCamera, out localPoint);
            
            SetPosition(localPoint);
        }
    }
    
    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        UpdateVisualState();
        
        // Snap to grid if enabled
        if (Builder != null && Builder.snapToGridToggle != null && Builder.snapToGridToggle.isOn)
        {
            SetPosition(SnapToGrid(rectTransform.anchoredPosition));
        }
        
        // Refresh connections after moving
        if (Builder != null)
        {
            Builder.RefreshConnections();
        }
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                // Ctrl+Click for connections
                StartConnection();
            }
            else
            {
                // Regular click for selection
                if (Builder != null)
                    Builder.SelectNode(this);
            }
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            // Right click for context menu
            ShowContextMenu(eventData.position);
        }
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isDragging && !isSelected && backgroundImage != null)
        {
            backgroundImage.color = hoverColor;
        }
        
        // Show tooltip with tech info
        if (TooltipSystem.Instance != null)
        {
            TooltipSystem.Instance.ShowTechTooltip(RepresentedTech, null);
        }
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isDragging && !isSelected)
        {
            UpdateVisualState();
        }
        
        if (TooltipSystem.Instance != null)
        {
            TooltipSystem.Instance.HideTooltip();
        }
    }
    
    private void DeleteNode()
    {
        if (Builder != null)
            Builder.RemoveTechFromBuilder(RepresentedTech);
    }
    
    private void StartConnection()
    {
        if (Builder != null)
            Builder.StartConnection(this);
    }
    
    private void ShowContextMenu(Vector2 screenPosition)
    {
        // Context menu placeholder - implement UI popup as needed
    }
}
