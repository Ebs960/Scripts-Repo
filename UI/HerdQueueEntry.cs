using UnityEngine;
using UnityEngine.EventSystems;

public class HerdQueueEntry : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    public HerdPanel owner;
    public int queueIndex;

    private RectTransform rect;
    private CanvasGroup cg;
    private Transform originalParent;
    private int originalSibling;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (owner != null) owner.OnQueueEntryClicked(queueIndex);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalParent = rect.parent;
        originalSibling = rect.GetSiblingIndex();
        // move to top-level canvas so it follows pointer
        var canvas = owner?.GetComponentInParent<Canvas>();
        if (canvas != null) rect.SetParent(canvas.transform, true);
        cg.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector3 worldPoint;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out worldPoint))
            rect.position = worldPoint;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        cg.blocksRaycasts = true;
        if (owner == null) return;

        // find closest sibling in the queue container to drop into
        var container = owner.queueContainer as RectTransform;
        if (container == null) { rect.SetParent(originalParent, true); rect.SetSiblingIndex(originalSibling); owner.Refresh(); return; }

        float bestDist = float.MaxValue;
        int bestIndex = 0;
        for (int i = 0; i < container.childCount; i++)
        {
            var child = container.GetChild(i) as RectTransform;
            if (child == null) continue;
            float d = Vector2.Distance(rect.position, child.position);
            if (d < bestDist) { bestDist = d; bestIndex = i; }
        }

        // If container is empty, place at 0
        if (container.childCount == 0) bestIndex = 0;

        // restore parent and notify owner to reorder
        rect.SetParent(container, true);
        owner.OnQueueEntryReordered(originalSibling, bestIndex);
    }
}
