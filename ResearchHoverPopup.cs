using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ResearchHoverPopup : MonoBehaviour
{
    [Header("Optional Prefab References (created at runtime when unassigned)")]
    [SerializeField] private RectTransform popupRect;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private ScrollRect detailsScroll;
    [SerializeField] private CanvasGroup popupGroup;
    public TMP_FontAsset font;
    [Tooltip("Technology/Culture Hover Popup Background. For nine-slicing, include the entire title band and divider in the top border.")]
    public Sprite backgroundSprite;
    public Color backgroundColor = new Color(0.035f, 0.085f, 0.16f, 0.98f);
    public Color borderColor = new Color(0.48f, 0.43f, 0.30f);
    public Color headingColor = new Color(0.94f, 0.94f, 0.91f);
    public Color secondaryColor = new Color(0.68f, 0.71f, 0.73f);
    [Min(0)] public float showDelay = 0.15f;
    [Min(0)] public float hideDelay = 0.15f;
    [Min(120)] public float preferredWidth = 360f;
    [Range(0.2f, 0.95f)] public float maximumHeightFraction = 0.7f;
    [Min(0)] public float screenMargin = 8f;
    [Min(0)] public float nodeGap = 12f;
    [Min(72)] public float titleSectionHeight = 80f;
    [Range(0.05f, 0.5f)] public float unslicedTitleFraction = 0.2f;
    public bool pinOnNavigationSelection = true;
    private static ResearchHoverPopup activePopup;
    private MonoBehaviour owner;
    private RectTransform node;
    private Func<ResearchPopupInfo> infoProvider;
    private Canvas canvas;
    private bool pinned;
    private bool visible;
    private float showAt;
    private float hideAt = -1;
    private Vector2 previousAvailableSize;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private RectTransform CanvasRect => canvas != null ? canvas.transform as RectTransform : null;
    private Camera EventCamera => canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
    private Camera NodeEventCamera
    {
        get
        {
            var parent = node != null ? node.GetComponentInParent<Canvas>() : null;
            var root = parent != null ? parent.rootCanvas : null;
            return root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        }
    }

    public static ResearchHoverPopup GetOrCreate(MonoBehaviour owner, ResearchHoverPopup assigned)
    {
        if (assigned != null && assigned.gameObject.scene.IsValid()) return NormalizeHost(assigned);
        if (owner == null) return null;
        var parentCanvas = owner.GetComponentInParent<Canvas>();
        if (parentCanvas == null) return null;
        var root = parentCanvas.rootCanvas;
        var existing = root.GetComponentInChildren<ResearchHoverPopup>(true);
        if (existing != null) return NormalizeHost(existing);
        if (assigned != null) return NormalizeHost(Instantiate(assigned, root.transform));
        var host = new GameObject("ResearchHoverPopup", typeof(RectTransform), typeof(ResearchHoverPopup));
        var rect = host.GetComponent<RectTransform>();
        rect.SetParent(root.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return host.GetComponent<ResearchHoverPopup>();
    }

    private static ResearchHoverPopup NormalizeHost(ResearchHoverPopup popup)
    {
        var parent = popup.GetComponentInParent<Canvas>();
        if (parent != null && popup.transform.parent != parent.rootCanvas.transform)
            popup.transform.SetParent(parent.rootCanvas.transform, false);
        popup.transform.localScale = Vector3.one;
        popup.gameObject.SetActive(true);
        popup.enabled = true;
        return popup;
    }

    private void Awake()
    {
        if (popupRect == transform && popupGroup == null)
        {
            popupGroup = GetComponent<CanvasGroup>();
            if (popupGroup == null) popupGroup = gameObject.AddComponent<CanvasGroup>();
        }
        HideView();
    }

    public void Request(MonoBehaviour requestOwner, RectTransform anchor, Func<ResearchPopupInfo> provider, bool pin = false)
    {
        if (requestOwner == null || anchor == null || provider == null) return;
        if (activePopup != null && activePopup != this) activePopup.HideImmediately();
        activePopup = this;
        if (owner == requestOwner && node == anchor && infoProvider != null)
        {
            hideAt = -1;
            if (pin) { pinned = true; if (!visible) Show(); }
            return;
        }
        HideView();
        owner = requestOwner;
        node = anchor;
        infoProvider = provider;
        pinned = pin;
        hideAt = -1;
        showAt = Time.unscaledTime + Mathf.Max(0, showDelay);
        if (pin) Show();
    }

    public void Leave(MonoBehaviour requestOwner, RectTransform anchor)
    {
        if (owner == requestOwner && node == anchor && !pinned) hideAt = Time.unscaledTime + Mathf.Max(0, hideDelay);
    }

    public void HideForOwner(MonoBehaviour requestOwner)
    {
        if (owner == requestOwner) HideImmediately();
    }

    public void RefreshForOwner(MonoBehaviour requestOwner)
    {
        if (owner == requestOwner && visible) Show(false);
    }

    public void HideImmediately()
    {
        HideView();
        owner = null;
        node = null;
        infoProvider = null;
        pinned = false;
        hideAt = -1;
        if (activePopup == this) activePopup = null;
    }

    private void HideView()
    {
        visible = false;
        if (popupGroup != null) { popupGroup.alpha = 0; popupGroup.blocksRaycasts = false; }
        if (popupRect != null && popupRect != transform) popupRect.gameObject.SetActive(false);
    }

    private void OnDisable() { HideImmediately(); }

    private void LateUpdate()
    {
        if (infoProvider == null) return;
        if (owner == null || !owner.isActiveAndEnabled || node == null || !node.gameObject.activeInHierarchy)
        {
            HideImmediately();
            return;
        }
        if (pinned && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HideImmediately();
            return;
        }
        var pointer = Pointer.current;
        if (!pinned)
        {
            bool over = pointer != null && PointerOverNodeOrPopup(pointer.position.ReadValue());
            if (over) hideAt = -1;
            else if (hideAt < 0) hideAt = Time.unscaledTime + Mathf.Max(0, hideDelay);
            if (hideAt >= 0 && Time.unscaledTime >= hideAt) { HideImmediately(); return; }
            if (!visible && over && Time.unscaledTime >= showAt) Show();
        }
        if (!visible || CanvasRect == null) return;
        if (previousAvailableSize != AvailableRect().size) Resize();
        Position();
        if (popupGroup != null)
            popupGroup.blocksRaycasts = pointer == null ||
                !(RectTransformUtility.RectangleContainsScreenPoint(node, pointer.position.ReadValue(), NodeEventCamera) &&
                  RectTransformUtility.RectangleContainsScreenPoint(popupRect, pointer.position.ReadValue(), EventCamera));
    }

    private bool PointerOverNodeOrPopup(Vector2 screenPoint)
    {
        if (EventSystem.current == null) return false;
        hits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPoint }, hits);
        if (hits.Count == 0) return false;
        var target = hits[0].gameObject.transform;
        return target.IsChildOf(node) || (visible && popupRect != null && target.IsChildOf(popupRect));
    }

    private void Show(bool resetScroll = true)
    {
        var info = infoProvider != null ? infoProvider() : null;
        if (info == null || !EnsureView()) { HideImmediately(); return; }
        float scrollPosition = resetScroll ? 1 : detailsScroll.verticalNormalizedPosition;
        popupRect.gameObject.SetActive(true);
        popupGroup.alpha = 1;
        popupGroup.blocksRaycasts = true;
        visible = true;
        transform.SetAsLastSibling();
        nameText.text = info.name;
        summaryText.text = info.age + "  |  " + (info.status == ResearchPopupStatus.Active && !string.IsNullOrEmpty(info.actionLabel) ? info.actionLabel : info.status.ToString());
        bodyText.text = info.BuildBody();
        iconImage.gameObject.SetActive(info.icon != null);
        iconImage.sprite = info.icon;
        nameText.rectTransform.offsetMin = new Vector2(info.icon != null ? 64 : 12, -48);
        Resize();
        detailsScroll.StopMovement();
        detailsScroll.verticalNormalizedPosition = scrollPosition;
        Position();
    }

    private bool EnsureView()
    {
        if (canvas == null)
        {
            var parent = GetComponentInParent<Canvas>();
            if (parent == null) return false;
            canvas = parent.rootCanvas;
        }
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (font == null)
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
                if (text.font != null) { font = text.font; break; }
        if (font == null) return false;
        if (popupRect == null)
        {
            popupRect = CreateRect("Floating Details", transform);
            popupRect.pivot = new Vector2(0, 1);
            popupRect.anchorMin = popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        }
        popupRect.anchorMin = popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.localScale = Vector3.one;
        if (popupGroup == null) popupGroup = popupRect.GetComponent<CanvasGroup>();
        if (popupGroup == null) popupGroup = popupRect.gameObject.AddComponent<CanvasGroup>();
        if (backgroundImage == null) backgroundImage = popupRect.GetComponent<Image>();
        if (backgroundImage == null) backgroundImage = popupRect.gameObject.AddComponent<Image>();
        backgroundImage.color = backgroundSprite != null ? Color.white : backgroundColor;
        backgroundImage.raycastTarget = true;
        backgroundImage.sprite = backgroundSprite;
        backgroundImage.type = backgroundSprite != null && backgroundSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        backgroundImage.preserveAspect = backgroundSprite != null && backgroundImage.type == Image.Type.Simple;
        var outline = backgroundImage.GetComponent<Outline>();
        if (outline == null && backgroundSprite == null) outline = backgroundImage.gameObject.AddComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = backgroundSprite == null;
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(1, -1);
            outline.useGraphicAlpha = false;
        }
        if (iconImage == null)
        {
            iconImage = CreateRect("Icon", popupRect).gameObject.AddComponent<Image>();
            SetTopRect(iconImage.rectTransform, new Vector2(12, -12), new Vector2(40, 40));
        }
        iconImage.raycastTarget = false;
        if (nameText == null)
        {
            nameText = CreateText("Name", popupRect, 18, headingColor);
            nameText.rectTransform.anchorMin = nameText.rectTransform.anchorMax = new Vector2(0, 1);
            nameText.rectTransform.anchorMax = Vector2.one;
            nameText.rectTransform.offsetMin = new Vector2(64, -48);
            nameText.rectTransform.offsetMax = new Vector2(-12, -10);
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 12;
            nameText.fontSizeMax = 18;
        }
        if (summaryText == null)
        {
            summaryText = CreateText("Age and Status", popupRect, 12, secondaryColor);
            summaryText.rectTransform.anchorMin = new Vector2(0, 1);
            summaryText.rectTransform.anchorMax = Vector2.one;
            summaryText.rectTransform.offsetMin = new Vector2(12, -72);
            summaryText.rectTransform.offsetMax = new Vector2(-12, -50);
            summaryText.enableAutoSizing = true;
            summaryText.fontSizeMin = 10;
            summaryText.fontSizeMax = 12;
        }
        if (detailsScroll == null)
        {
            var viewport = CreateRect("Scrollable Details", popupRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(12, 12);
            viewport.offsetMax = new Vector2(-12, -80);
            var image = viewport.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            detailsScroll = viewport.gameObject.AddComponent<ScrollRect>();
            detailsScroll.viewport = viewport;
        }
        if (bodyText == null)
        {
            bodyText = CreateText("Details", detailsScroll.viewport, 14, headingColor);
            bodyText.rectTransform.anchorMin = new Vector2(0, 1);
            bodyText.rectTransform.anchorMax = Vector2.one;
            bodyText.rectTransform.pivot = new Vector2(0, 1);
            bodyText.rectTransform.anchoredPosition = Vector2.zero;
        }
        detailsScroll.content = bodyText.rectTransform;
        bodyText.enableAutoSizing = false;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Overflow;
        detailsScroll.horizontal = false;
        detailsScroll.vertical = true;
        detailsScroll.movementType = ScrollRect.MovementType.Clamped;
        detailsScroll.scrollSensitivity = 28;
        return true;
    }

    private TMP_Text CreateText(string name, Transform parent, float size, Color color)
    {
        var text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.characterSpacing = 0;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void SetTopRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private Rect AvailableRect()
    {
        Rect available = CanvasRect.rect;
        if (canvas.targetDisplay == 0 && canvas.renderMode != RenderMode.WorldSpace &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, Screen.safeArea.min, EventCamera, out var min) &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, Screen.safeArea.max, EventCamera, out var max))
            available = Rect.MinMaxRect(Mathf.Max(available.xMin, min.x), Mathf.Max(available.yMin, min.y),
                Mathf.Min(available.xMax, max.x), Mathf.Min(available.yMax, max.y));
        float margin = Mathf.Min(Mathf.Max(0, screenMargin), Mathf.Min(available.width, available.height) * 0.1f);
        return Rect.MinMaxRect(available.xMin + margin, available.yMin + margin, available.xMax - margin, available.yMax - margin);
    }

    private void Resize()
    {
        if (CanvasRect == null || bodyText == null) return;
        Rect available = AvailableRect();
        float width = Mathf.Min(Mathf.Max(120, preferredWidth), Mathf.Max(1, available.width));
        float maximumHeight = Mathf.Max(1, available.height * Mathf.Clamp(maximumHeightFraction, 0.2f, 0.95f));
        Vector4 border = backgroundSprite != null && backgroundImage != null ? backgroundSprite.border / Mathf.Max(0.001f, backgroundImage.pixelsPerUnit) : Vector4.zero;
        float titleHeight = Mathf.Max(titleSectionHeight, border.w);
        float bottom = Mathf.Max(12, border.y);
        float left = Mathf.Max(12, border.x);
        float right = Mathf.Max(12, border.z);
        float bodyHeight = bodyText.GetPreferredValues(bodyText.text, Mathf.Max(1, width - left - right), Mathf.Infinity).y;
        float height = Mathf.Min(bodyHeight + titleHeight + bottom, maximumHeight);
        if (backgroundSprite != null && backgroundSprite.border == Vector4.zero)
        {
            float aspect = backgroundSprite.rect.width / backgroundSprite.rect.height;
            height = Mathf.Min(width / aspect, maximumHeight);
            width = Mathf.Min(width, height * aspect);
            titleHeight = Mathf.Max(72, height * unslicedTitleFraction);
            bodyHeight = bodyText.GetPreferredValues(bodyText.text, Mathf.Max(1, width - left - right), Mathf.Infinity).y;
        }
        popupRect.sizeDelta = new Vector2(width, height);
        detailsScroll.viewport.offsetMin = new Vector2(left, bottom);
        detailsScroll.viewport.offsetMax = new Vector2(-right, -titleHeight);
        bodyText.rectTransform.sizeDelta = new Vector2(0, bodyHeight);
        previousAvailableSize = available.size;
    }

    private void Position()
    {
        if (node == null || popupRect == null || CanvasRect == null) return;
        node.GetWorldCorners(corners);
        Vector2 min = Vector2.positiveInfinity;
        Vector2 max = Vector2.negativeInfinity;
        foreach (var corner in corners)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(NodeEventCamera, corner);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screenPoint, EventCamera, out var point)) continue;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        if (float.IsInfinity(min.x) || float.IsInfinity(max.x)) return;
        Vector2 topLeft = CalculatePosition(AvailableRect(), Rect.MinMaxRect(min.x, min.y, max.x, max.y), popupRect.rect.size, nodeGap);
        popupRect.position = CanvasRect.TransformPoint(topLeft + new Vector2(popupRect.pivot.x * popupRect.rect.width,
            (popupRect.pivot.y - 1) * popupRect.rect.height));
    }

    public static Vector2 CalculatePosition(Rect available, Rect anchor, Vector2 size, float gap)
    {
        float left = anchor.xMax + gap;
        float top = anchor.yMax;
        if (left + size.x > available.xMax) left = anchor.xMin - gap - size.x;
        if (left < available.xMin)
        {
            left = anchor.center.x - size.x * 0.5f;
            top = anchor.yMin - gap - size.y >= available.yMin ? anchor.yMin - gap : anchor.yMax + gap + size.y;
        }
        return new Vector2(Mathf.Clamp(left, available.xMin, Mathf.Max(available.xMin, available.xMax - size.x)),
            Mathf.Clamp(top, Mathf.Min(available.yMax, available.yMin + size.y), available.yMax));
    }
}