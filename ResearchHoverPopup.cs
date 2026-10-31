using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Reusable, non-interactive hover card for the technology and culture trees.</summary>
public sealed class ResearchHoverPopup : MonoBehaviour
{
    private RectTransform panel;
    private RectTransform canvasRect;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI bodyText;
    private Canvas canvas;
    private const float PopupWidth = 380f;
    private const float PopupHeight = 340f;
    private const float Margin = 12f;

    public static ResearchHoverPopup For(Transform context)
    {
        Canvas root = context.GetComponentInParent<Canvas>();
        if (root == null) return null;
        Canvas top = root.rootCanvas;
        ResearchHoverPopup popup = top.GetComponent<ResearchHoverPopup>();
        if (popup == null) popup = top.gameObject.AddComponent<ResearchHoverPopup>();
        popup.EnsureCreated();
        return popup;
    }

    private void EnsureCreated()
    {
        if (panel != null) return;
        canvas = GetComponent<Canvas>();
        canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;

        GameObject box = new GameObject("Research Hover Card", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(canvasRect, false);
        panel = (RectTransform)box.transform;
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0, 1);
        panel.sizeDelta = new Vector2(PopupWidth, PopupHeight);
        Image image = box.GetComponent<Image>();
        image.color = new Color(0.055f, 0.095f, 0.15f, 0.97f);
        image.raycastTarget = false;

        titleText = CreateText("Heading", panel, new Vector2(12, -12), new Vector2(PopupWidth - 24, 38), 21, new Color(0.89f, 0.76f, 0.53f));
        bodyText = CreateText("Information", panel, new Vector2(12, -58), new Vector2(PopupWidth - 24, PopupHeight - 72), 16, new Color(0.90f, 0.93f, 0.96f));
        bodyText.overflowMode = TextOverflowModes.Ellipsis;
        panel.gameObject.SetActive(false);
    }

    private static TextMeshProUGUI CreateText(string name, RectTransform parent, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI label = obj.GetComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.color = color;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        return label;
    }

    public void Show(string heading, string information, Vector2 pointerPosition)
    {
        EnsureCreated();
        if (panel == null) return;
        titleText.text = heading ?? "";
        bodyText.text = information ?? "";
        panel.gameObject.SetActive(true);
        panel.SetAsLastSibling();
        Reposition(pointerPosition);
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void Reposition(Vector2 pointerPosition)
    {
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, pointerPosition, uiCamera, out Vector2 point))
            return;

        Rect bounds = canvasRect.rect;
        float x = point.x + 18f;
        float y = point.y - 12f;
        if (x + PopupWidth > bounds.xMax - Margin) x = point.x - PopupWidth - 18f;
        if (y - PopupHeight < bounds.yMin + Margin) y = bounds.yMin + PopupHeight + Margin;
        panel.anchoredPosition = new Vector2(
            Mathf.Clamp(x, bounds.xMin + Margin, bounds.xMax - PopupWidth - Margin),
            Mathf.Clamp(y, bounds.yMin + PopupHeight + Margin, bounds.yMax - Margin));
    }
}

/// <summary>Attach to each instantiated tree node; does not change click behavior.</summary>
public sealed class ResearchHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Action<Vector2> onEnter;
    private Action onExit;

    public void Configure(Action<Vector2> enter, Action exit)
    {
        onEnter = enter;
        onExit = exit;
    }

    public void OnPointerEnter(PointerEventData eventData) => onEnter?.Invoke(eventData.position);
    public void OnPointerExit(PointerEventData eventData) => onExit?.Invoke();
    private void OnDisable() => onExit?.Invoke();
}
