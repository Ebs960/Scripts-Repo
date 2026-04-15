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

        if (label != null) label.text = belief != null ? belief.beliefName : "-";
        if (iconImage != null) iconImage.sprite = belief != null ? belief.icon : null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (rect == null) rect = GetComponent<RectTransform>();
        canvasGroup.alpha = 0.85f;
        canvasGroup.blocksRaycasts = false;
        originalParent = transform.parent;
        originalSibling = transform.GetSiblingIndex();
        if (dragLayer != null)
            transform.SetParent(dragLayer, true);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (rect == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, eventData.position, eventData.pressEventCamera, out var localPoint);
        rect.anchoredPosition = localPoint;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;

        // If not dropped onto a slot, return to original parent
        if (transform.parent == dragLayer)
        {
            transform.SetParent(originalParent, false);
            transform.SetSiblingIndex(originalSibling);
        }
    }
}
