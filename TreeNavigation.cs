using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TreeNavigation : MonoBehaviour
{
    [SerializeField] private ScrollRect scrollRect;
    [Min(0.05f)] public float minimumZoom = 0.35f;
    [Min(0.05f)] public float maximumZoom = 2f;
    [Min(0.001f)] public float wheelZoomSpeed = 0.12f;
    public bool wheelZoom = true;
    private float originalScrollSensitivity;
    private bool sensitivityCaptured;
    private bool middleDragging;
    private Vector2 previousPointer;
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private RectTransform Viewport => scrollRect != null
        ? (scrollRect.viewport != null ? scrollRect.viewport : scrollRect.transform as RectTransform) : null;
    private Camera EventCamera
    {
        get
        {
            var canvas = GetComponentInParent<Canvas>();
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }
    }

    public void Configure(ScrollRect target)
    {
        if (scrollRect != target) RestoreSensitivity();
        scrollRect = target;
        if (target == null) return;
        target.horizontal = target.vertical = true;
        if (!sensitivityCaptured)
        {
            originalScrollSensitivity = target.scrollSensitivity;
            sensitivityCaptured = true;
        }
        target.scrollSensitivity = wheelZoom && isActiveAndEnabled ? 0 : originalScrollSensitivity;
    }

    private void OnEnable() { Configure(scrollRect != null ? scrollRect : GetComponent<ScrollRect>()); }
    private void OnDisable() { RestoreSensitivity(); middleDragging = false; }
    private void RestoreSensitivity()
    {
        if (scrollRect != null && sensitivityCaptured) scrollRect.scrollSensitivity = originalScrollSensitivity;
        sensitivityCaptured = false;
    }

    private bool PointerOverTree(Vector2 screenPoint)
    {
        if (Viewport == null || !RectTransformUtility.RectangleContainsScreenPoint(Viewport, screenPoint, EventCamera))
            return false;
        if (EventSystem.current == null) return false;
        hits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPoint }, hits);
        return hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(Viewport);
    }

    private void Update()
    {
        if (Mouse.current == null || scrollRect == null || scrollRect.content == null) return;
        scrollRect.scrollSensitivity = wheelZoom ? 0 : originalScrollSensitivity;
        var mouse = Mouse.current;
        Vector2 pointer = mouse.position.ReadValue();
        bool overTree = PointerOverTree(pointer);
        if (wheelZoom && overTree && !mouse.leftButton.isPressed && !mouse.middleButton.isPressed)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
                SetZoom(scrollRect.content.localScale.x * Mathf.Exp(Mathf.Sign(wheel) * wheelZoomSpeed), pointer);
        }
        if (mouse.middleButton.wasPressedThisFrame && overTree)
        {
            middleDragging = true;
            previousPointer = pointer;
            scrollRect.StopMovement();
        }
        if (!mouse.middleButton.isPressed) middleDragging = false;
        if (middleDragging)
        {
            var parent = scrollRect.content.parent as RectTransform;
            if (parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pointer, EventCamera, out var current) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, previousPointer, EventCamera, out var previous))
            {
                scrollRect.content.anchoredPosition += current - previous;
                ClampToViewport();
            }
            previousPointer = pointer;
        }
    }

    public void SetZoom(float value, Vector2 screenPoint)
    {
        if (scrollRect == null || scrollRect.content == null) return;
        var content = scrollRect.content;
        var parent = content.parent as RectTransform;
        if (parent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPoint, EventCamera, out var point)) return;
        Vector3 before = parent.InverseTransformPoint(content.TransformPoint(point));
        float zoom = Mathf.Clamp(value, Mathf.Max(0.05f, minimumZoom), Mathf.Max(minimumZoom, maximumZoom));
        content.localScale = new Vector3(zoom, zoom, 1);
        Vector3 after = parent.InverseTransformPoint(content.TransformPoint(point));
        scrollRect.StopMovement();
        content.anchoredPosition += (Vector2)(before - after);
        ClampToViewport();
    }

    public void ZoomIn() { ZoomFromCenter(1.2f); }
    public void ZoomOut() { ZoomFromCenter(1 / 1.2f); }
    private void ZoomFromCenter(float factor)
    {
        if (Viewport != null && scrollRect.content != null)
            SetZoom(scrollRect.content.localScale.x * factor,
                RectTransformUtility.WorldToScreenPoint(EventCamera, Viewport.TransformPoint(Viewport.rect.center)));
    }

    public void ResetView()
    {
        if (scrollRect == null || scrollRect.content == null) return;
        ZoomFromCenter(Mathf.Clamp(1, minimumZoom, maximumZoom) / scrollRect.content.localScale.x);
        scrollRect.StopMovement();
        scrollRect.horizontalNormalizedPosition = 0;
        scrollRect.verticalNormalizedPosition = 1;
        ClampToViewport();
    }

    public void Focus(RectTransform node)
    {
        if (node == null || Viewport == null || scrollRect.content == null) return;
        FocusPoint(node.TransformPoint(node.rect.center));
    }

    public void FocusPoint(Vector3 worldPoint)
    {
        if (Viewport == null || scrollRect.content == null) return;
        var parent = scrollRect.content.parent as RectTransform;
        if (parent == null) return;
        scrollRect.StopMovement();
        Vector3 center = parent.InverseTransformPoint(Viewport.TransformPoint(Viewport.rect.center));
        Vector3 target = parent.InverseTransformPoint(worldPoint);
        scrollRect.content.anchoredPosition += (Vector2)(center - target);
        ClampToViewport();
    }

    private void ClampToViewport()
    {
        if (Viewport == null || scrollRect.content == null) return;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(Viewport, scrollRect.content);
        Rect view = Viewport.rect;
        Vector2 delta = Vector2.zero;
        delta.x = bounds.size.x <= view.width ? view.center.x - bounds.center.x
            : bounds.min.x > view.xMin ? view.xMin - bounds.min.x
            : bounds.max.x < view.xMax ? view.xMax - bounds.max.x : 0;
        delta.y = bounds.size.y <= view.height ? view.center.y - bounds.center.y
            : bounds.min.y > view.yMin ? view.yMin - bounds.min.y
            : bounds.max.y < view.yMax ? view.yMax - bounds.max.y : 0;
        var parent = scrollRect.content.parent;
        scrollRect.content.anchoredPosition += (Vector2)parent.InverseTransformVector(Viewport.TransformVector(delta));
    }
}