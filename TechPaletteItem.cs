using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Unified tech tree item used both as a palette drag-source and as a placed builder node.
/// When isBuilderNode is false (default), dragging creates a ghost copy that drops into the builder.
/// When isBuilderNode is true, dragging repositions the node on the canvas.
/// </summary>
public class TechPaletteItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI Components")]
    public Image techIcon;
    public TextMeshProUGUI techNameText;
    public Image backgroundImage;
    
    [Header("Visual States")]
    public Color normalColor = Color.white;
    public Color hoverColor = Color.gray;
    public Color selectedColor = Color.yellow;
    public Color draggingColor = Color.cyan;
    
    public TechData RepresentedTech { get; private set; }
    public TechTreeBuilder Builder { get; private set; }
    public Vector2Int GridPosition { get; private set; }

    /// <summary>When true this item lives on the builder canvas and can be repositioned.</summary>
    public bool isBuilderNode;

    private RectTransform rectTransform;
    private GameObject draggedObject;   // ghost copy used in palette mode
    private bool isDragging;
    private bool isSelected;
    
    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
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
            techNameText.fontSizeMin = 6f;
            techNameText.fontSizeMax = 12f;
        }

        if (backgroundImage != null)
            backgroundImage.color = normalColor;
    }

    // ───────────────────── Position helpers (builder node mode) ─────────────────────

    public void SetPosition(Vector2 position)
    {
        if (Builder != null && Builder.snapToGridToggle != null && Builder.snapToGridToggle.isOn)
            position = SnapToGrid(position);
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        rectTransform.anchoredPosition = position;
    }

    public Vector2 GetPosition()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        return rectTransform.anchoredPosition;
    }

    public void SetGridPosition(Vector2Int gridPos) => GridPosition = gridPos;

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        if (backgroundImage == null) return;
        if (isDragging) backgroundImage.color = draggingColor;
        else if (isSelected) backgroundImage.color = selectedColor;
        else backgroundImage.color = normalColor;
    }

    private Vector2 SnapToGrid(Vector2 position)
    {
        float gs = Builder != null ? Builder.gridSize : 50f;
        return new Vector2(Mathf.Round(position.x / gs) * gs, Mathf.Round(position.y / gs) * gs);
    }

    // ───────────────────── Drag handlers ─────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isBuilderNode)
        {
            // Builder mode: reposition this node on the canvas
            isDragging = true;
            UpdateVisualState();
            transform.SetAsLastSibling();
        }
        else
        {
            // Palette mode: create a ghost copy
            draggedObject = Instantiate(gameObject);
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                draggedObject.transform.SetParent(canvas.transform, false);
                var cg = draggedObject.AddComponent<CanvasGroup>();
                cg.alpha = 0.7f;
                cg.blocksRaycasts = false;
                var dup = draggedObject.GetComponent<TechPaletteItem>();
                if (dup != null) Destroy(dup);
            }
        }
    }
    
    public void OnDrag(PointerEventData eventData)
    {
        if (isBuilderNode)
        {
            if (Builder != null && Builder.builderContent != null)
            {
                Vector2 localPoint;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Builder.builderContent, eventData.position, eventData.pressEventCamera, out localPoint);
                SetPosition(localPoint);
            }
        }
        else
        {
            if (draggedObject != null)
                draggedObject.transform.position = eventData.position;
        }
    }
    
    public void OnEndDrag(PointerEventData eventData)
    {
        if (isBuilderNode)
        {
            isDragging = false;
            UpdateVisualState();
            if (Builder != null && Builder.snapToGridToggle != null && Builder.snapToGridToggle.isOn)
                SetPosition(SnapToGrid(rectTransform.anchoredPosition));
            if (Builder != null) Builder.RefreshConnections();
        }
        else
        {
            if (draggedObject != null) Destroy(draggedObject);
            if (Builder != null && Builder.builderContent != null)
            {
                Vector2 localPoint;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Builder.builderContent, eventData.position, eventData.pressEventCamera, out localPoint))
                {
                    if (Builder.builderContent.rect.Contains(localPoint))
                        Builder.AddTechToBuilder(RepresentedTech, localPoint);
                }
            }
        }
    }

    // ───────────────────── Click / selection (builder node mode) ─────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isBuilderNode) return;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (Keyboard.current != null &&
                (Keyboard.current[Key.LeftCtrl].isPressed || Keyboard.current[Key.RightCtrl].isPressed))
            {
                if (Builder != null) Builder.StartConnection(this);
            }
            else
            {
                if (Builder != null) Builder.SelectNode(this);
            }
        }
    }

    // ───────────────────── Hover / tooltip ─────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isDragging && !isSelected && backgroundImage != null)
            backgroundImage.color = hoverColor;
        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.ShowTechTooltip(RepresentedTech, null);
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isDragging && !isSelected)
            UpdateVisualState();
        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.HideTooltip();
    }
}
