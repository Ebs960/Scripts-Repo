using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Compact contextual damage preview placed beside the hovered target, clear of the command dock.</summary>
public sealed class BattleCombatPreviewView : MonoBehaviour
{
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform panel;
    [SerializeField] private RectTransform dock;
    [SerializeField] private TextMeshProUGUI title;
    [SerializeField] private TextMeshProUGUI body;
    private readonly Vector3[] corners = new Vector3[4];

    private const float TargetGap = 44f;
    private const float Margin = 8f;

    public bool IsVisible => panel != null && panel.gameObject.activeSelf;

    public void Show(string titleText, string bodyText, Vector2 screenPoint)
    {
        if (panel == null) return;
        title.text = titleText;
        body.text = bodyText;
        panel.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 target);
        Rect bounds = canvasRect.rect;
        Vector2 size = panel.rect.size;

        // Prefer the upper right of the target so neither the hex nor the dock is covered.
        bool left = target.x + TargetGap + size.x > bounds.xMax - Margin;
        float x = left ? target.x - TargetGap - size.x : target.x + TargetGap;
        float y = target.y + TargetGap * 0.5f;

        dock.GetWorldCorners(corners);
        float dockTop = canvasRect.InverseTransformPoint(corners[1]).y;
        float minY = dockTop + Margin;
        float maxY = bounds.yMax - 54f - Margin - size.y;
        y = Mathf.Clamp(y, minY, Mathf.Max(minY, maxY));

        panel.pivot = Vector2.zero;
        panel.anchoredPosition = new Vector2(Mathf.Clamp(x, bounds.xMin + Margin, bounds.xMax - Margin - size.x), y);
    }

    public void Hide()
    {
        if (panel != null && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
    }
}
