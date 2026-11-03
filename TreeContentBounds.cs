using UnityEngine;
using UnityEngine.UI;

public class TreeContentBounds : MonoBehaviour
{
    private Vector2 originOffset;
    private readonly Vector3[] corners = new Vector3[4];

    public static void Fit(RectTransform content, Vector2 minimumSize, float padding = 80)
    {
        if (content == null) return;
        var layout = content.GetComponent<LayoutGroup>();
        if (layout != null) layout.enabled = false;
        var fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;
        var helper = content.GetComponent<TreeContentBounds>();
        if (helper == null) helper = content.gameObject.AddComponent<TreeContentBounds>();
        helper.Resize(content, minimumSize, Mathf.Max(0, padding));
    }

    public static Vector2 ToLayoutPosition(RectTransform content, Vector2 localPoint)
    {
        var helper = content.GetComponent<TreeContentBounds>();
        Vector2 offset = helper != null ? helper.originOffset : Vector2.zero;
        return localPoint - new Vector2(content.rect.xMin, content.rect.yMax) - offset;
    }

    public static Vector2 ToContentPosition(RectTransform content, Vector2 layoutPoint)
    {
        return layoutPoint - ToLayoutPosition(content, Vector2.zero);
    }

    public static void AlignChild(RectTransform content, RectTransform child)
    {
        if (content == null || child == null) return;
        var helper = content.GetComponent<TreeContentBounds>();
        if (helper == null) return;
        Vector2 position = child.anchoredPosition;
        child.anchorMin = child.anchorMax = new Vector2(helper.originOffset.x / content.rect.width,
            1 + helper.originOffset.y / content.rect.height);
        child.anchoredPosition = position;
    }

    private void Resize(RectTransform content, Vector2 minimumSize, float padding)
    {
        Vector2 origin = new Vector2(content.rect.xMin, content.rect.yMax) + originOffset;
        Vector2 min = Vector2.zero;
        Vector2 max = new Vector2(Mathf.Max(1, minimumSize.x), 0);
        min.y = -Mathf.Max(1, minimumSize.y);
        foreach (Transform child in content)
        {
            var rect = child as RectTransform;
            if (rect == null || !child.gameObject.activeSelf) continue;
            rect.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector2 point = (Vector2)content.InverseTransformPoint(corner) - origin;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        Vector2 size = max - min + Vector2.one * (padding * 2);
        originOffset = new Vector2(padding - min.x, -padding - max.y);
        content.anchorMin = content.anchorMax = new Vector2(0, 1);
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
        Vector2 anchor = new Vector2(originOffset.x / size.x, 1 + originOffset.y / size.y);
        foreach (Transform child in content)
        {
            var rect = child as RectTransform;
            if (rect == null) continue;
            Vector2 position = rect.anchoredPosition;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
        }
    }

    public static void ResetOrigin(RectTransform content)
    {
        if (content == null) return;
        var helper = content.GetComponent<TreeContentBounds>();
        if (helper != null) helper.originOffset = Vector2.zero;
    }
}