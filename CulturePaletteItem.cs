using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Culture palette item that can be dragged into the builder
/// </summary>
public class CulturePaletteItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("UI Components")]
    public Image cultureIcon;
    public TextMeshProUGUI cultureNameText;
    public Image backgroundImage;
    
    [Header("Visual States")]
    public Color normalColor = Color.white;
    public Color hoverColor = Color.gray;
    public Color selectedColor = Color.yellow;
    public Color draggingColor = Color.cyan;
    
    public CultureData RepresentedCulture { get; private set; }
    public CultureTreeBuilder Builder { get; private set; }
    public Vector2Int GridPosition { get; private set; }

    /// <summary>If true this item lives on the builder canvas and can be repositioned.</summary>
    public bool isBuilderNode;
    
    private GameObject draggedObject;
    private RectTransform rectTransform;
    private bool isDragging = false;
    private bool isSelected = false;
    
    public void Initialize(CultureData culture, CultureTreeBuilder builder)
    {
        RepresentedCulture = culture;
        Builder = builder;

        if (culture.cultureIcon != null && cultureIcon != null)
            cultureIcon.sprite = culture.cultureIcon;

        if (cultureNameText != null)
        {
            cultureNameText.text = culture.cultureName;
            cultureNameText.enableAutoSizing = true;
            cultureNameText.fontSizeMin = 6f;
            cultureNameText.fontSizeMax = 10f;
        }

        if (backgroundImage != null)
            backgroundImage.color = normalColor;

        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
    }
    
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isBuilderNode)
        {
            isDragging = true;
            UpdateVisualState();
            transform.SetAsLastSibling();
        }
        else
        {
            // Create a dragged copy for palette mode
            draggedObject = Instantiate(gameObject);
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                draggedObject.transform.SetParent(canvas.transform, false);

                var canvasGroup = draggedObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0.7f;
                canvasGroup.blocksRaycasts = false;

                var paletteItem = draggedObject.GetComponent<CulturePaletteItem>();
                if (paletteItem != null)
                    Destroy(paletteItem);
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
            {
                SetPosition(SnapToGrid(rectTransform.anchoredPosition));
            }
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
                        Builder.AddCultureToBuilder(RepresentedCulture, localPoint);
                }
            }
        }
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isBuilderNode)
        {
            if (backgroundImage != null)
                backgroundImage.color = hoverColor;
        }
        else
        {
            if (!isDragging && !isSelected && backgroundImage != null)
                backgroundImage.color = hoverColor;
        }

        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.ShowCultureTooltip(RepresentedCulture, null);
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isBuilderNode)
        {
            if (backgroundImage != null)
                backgroundImage.color = normalColor;
        }
        else
        {
            if (!isDragging && !isSelected)
                UpdateVisualState();
        }

        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.RequestHideTooltip();
    }

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

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isBuilderNode) return;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (Keyboard.current != null && (Keyboard.current[Key.LeftCtrl].isPressed || Keyboard.current[Key.RightCtrl].isPressed))
            {
                if (Builder != null) Builder.StartConnection(this);
            }
            else
            {
                if (Builder != null) Builder.SelectNode(this);
            }
        }
    }
}
