using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Tech palette item that can be dragged into the builder
/// </summary>
public class TechPaletteItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI Components")]
    public Image techIcon;
    public TextMeshProUGUI techNameText;
    public Image backgroundImage;
    
    [Header("Visual States")]
    public Color normalColor = Color.white;
    public Color hoverColor = Color.gray;
    
    public TechData RepresentedTech { get; private set; }
    public TechTreeBuilder Builder { get; private set; }
    
    private GameObject draggedObject;
    
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
            techNameText.fontSizeMax = 10f;
        }
        
        if (backgroundImage != null)
            backgroundImage.color = normalColor;
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
            var paletteItem = draggedObject.GetComponent<TechPaletteItem>();
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
                    Builder.AddTechToBuilder(RepresentedTech, localPoint);
                }
            }
        }
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (backgroundImage != null)
            backgroundImage.color = hoverColor;
        
        // Show tooltip with tech info
        if (TooltipSystem.Instance != null)
        {
            TooltipSystem.Instance.ShowTechTooltip(RepresentedTech, null);
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
