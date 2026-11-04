using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class BeliefButtonUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public BeliefData belief;
    private ReligionUI owner;
    private RectTransform rect;
    private CanvasGroup canvasGroup;
    private Transform originalParent;
    private int originalSibling;
    private RectTransform dragLayer;
    private Vector2 originalPosition;
    private bool isDragging;

    public Image iconImage;
    public TextMeshProUGUI label;

    public void Initialize(BeliefData b, ReligionUI ownerUI, RectTransform dragLayerOverride = null)
    {
        belief = b;
        owner = ownerUI;
        rect = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        originalParent = transform.parent;
        originalSibling = transform.GetSiblingIndex();
        dragLayer = dragLayerOverride;

        if (label == null) label = GetComponentInChildren<TextMeshProUGUI>();
        if (iconImage == null) iconImage = GetComponentInChildren<Image>();

        if (label != null)
        {
            label.text = belief != null
                ? belief.faithCost > 0 ? $"{belief.beliefName} ({belief.faithCost} Faith)" : belief.beliefName
                : "-";
        }
        if (iconImage != null) iconImage.sprite = belief != null ? belief.icon : null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (rect == null) rect = GetComponent<RectTransform>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        if (rect == null) return;
        isDragging = true;
        canvasGroup.alpha = 0.85f;
        canvasGroup.blocksRaycasts = false;
        originalParent = transform.parent;
        originalSibling = transform.GetSiblingIndex();
        originalPosition = rect.anchoredPosition;
        if (dragLayer != null)
            transform.SetParent(dragLayer, true);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || rect == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, eventData.position, eventData.pressEventCamera, out var localPoint);
        rect.anchoredPosition = localPoint;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        RestoreAfterDrag();
    }

    private void OnDisable()
    {
        RestoreAfterDrag();
    }

    private void RestoreAfterDrag()
    {
        if (!isDragging) return;
        isDragging = false;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }
        if (originalParent != null)
        {
            transform.SetParent(originalParent, false);
            transform.SetSiblingIndex(originalSibling);
            if (rect != null) rect.anchoredPosition = originalPosition;
        }
    }
}
