using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Culture palette item that can be dragged into the builder
/// </summary>
public class CulturePaletteItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI Components")]
    public Image cultureIcon;
    public TextMeshProUGUI cultureNameText;
    public Image backgroundImage;
    
    [Header("Visual States")]
    public Color normalColor = Color.white;
    public Color hoverColor = Color.gray;
    
    public CultureData RepresentedCulture { get; private set; }
    public CultureTreeBuilder Builder { get; private set; }
    
    private GameObject draggedObject;
    
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

        // Debug logs for diagnosis
var rect = GetComponent<RectTransform>();
        if (rect != null)
        {
}
    }
    
    public void OnBeginDrag(PointerEventData eventData)
    {
        // Create a dragged copy
        draggedObject = Instantiate(gameObject);
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            draggedObject.transform.SetParent(canvas.transform, false);
            
            // Make it semi-transparent
            var canvasGroup = draggedObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0.7f;
            canvasGroup.blocksRaycasts = false;
            
            // Remove any existing components that might interfere
            var paletteItem = draggedObject.GetComponent<CulturePaletteItem>();
            if (paletteItem != null)
                Destroy(paletteItem);
        }
    }
    
    public void OnDrag(PointerEventData eventData)
    {
        if (draggedObject != null)
        {
            draggedObject.transform.position = eventData.position;
        }
    }
    
    public void OnEndDrag(PointerEventData eventData)
    {
        if (draggedObject != null)
        {
            Destroy(draggedObject);
        }
        
        // Check if dropped in builder area
        if (Builder != null && Builder.builderContent != null)
        {
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Builder.builderContent, eventData.position, eventData.pressEventCamera, out localPoint))
            {
                // Check if we're within the builder content bounds
                Rect contentRect = Builder.builderContent.rect;
                if (contentRect.Contains(localPoint))
                {
                    Builder.AddCultureToBuilder(RepresentedCulture, localPoint);
                }
            }
        }
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (backgroundImage != null)
            backgroundImage.color = hoverColor;
        
        // Show tooltip with culture info
        if (TooltipSystem.Instance != null)
        {
            TooltipSystem.Instance.ShowCultureTooltip(RepresentedCulture, null);
        }
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        if (backgroundImage != null)
            backgroundImage.color = normalColor;
        
        if (TooltipSystem.Instance != null)
        {
            TooltipSystem.Instance.HideTooltip();
        }
    }
}
