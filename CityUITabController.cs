using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public enum CityUITab
{
    Overview,
    Production,
    BuildingsAndSpecialists,
    CrimeAndDisease,
    UnitStorage
}

/// <summary>
/// Navigation controller for the city interface.
///
/// Legacy mode (no ScrollRect assigned):
///     Behaves like the old tab controller and shows one feature panel at a time.
///
/// Scroll-sheet mode (ScrollRect assigned):
///     Keeps every city section visible in one continuous vertical sheet.
///     The navigation buttons jump the ScrollRect to their section, while manual
///     scrolling updates the highlighted navigation button.
///
/// This dual-mode behavior lets the existing City UI keep working while the new
/// vertical scrolling prefab is assembled in the editor.
/// </summary>
public class CityUITabController : MonoBehaviour
{
    [Serializable]
    private class TabBinding
    {
        public CityUITab tab;
        public Button button;

        [Tooltip("Legacy panel reference. In scroll-sheet mode this may also be used as the section anchor when Section is not assigned.")]
        public GameObject panel;

        [Tooltip("Optional explicit section anchor inside the ScrollRect content.")]
        public RectTransform section;

        [Tooltip("Optional graphic tinted to indicate the active section.")]
        public Graphic selectedGraphic;
    }

    [Header("Navigation")]
    [SerializeField] private CityUITab defaultTab = CityUITab.Overview;
    [SerializeField] private TabBinding[] tabs = Array.Empty<TabBinding>();
    [SerializeField] private Color selectedColor = new Color(0.9f, 0.8f, 0.1f, 1f);
    [SerializeField] private Color normalColor = Color.white;

    [Header("Continuous Scroll Sheet")]
    [Tooltip("Assign this to enable the new continuous scrolling city layout. Leave empty to preserve legacy tab-panel behavior.")]
    [SerializeField] private ScrollRect scrollRect;

    [Tooltip("Usually the ScrollRect Content object. If left empty, ScrollRect.content is used.")]
    [SerializeField] private RectTransform contentRoot;

    [Tooltip("When enabled, manually scrolling the sheet updates the highlighted navigation button.")]
    [SerializeField] private bool trackSelectionFromScroll = true;

    [Tooltip("Small offset in pixels used when deciding which section owns the current scroll position.")]
    [SerializeField] private float sectionSelectionBias = 36f;

    public CityUITab CurrentTab { get; private set; }
    public bool UsesScrollNavigation => ResolveScrollContent() != null && ResolveViewport() != null;

    public event Action<CityUITab> TabChanged;

    private bool suppressScrollTracking;
    private Coroutine scrollRoutine;

    private void Awake()
    {
        ResolveScrollContent();
        WireButtons();

        if (scrollRect != null)
            scrollRect.onValueChanged.AddListener(HandleScrollValueChanged);

        SelectTab(defaultTab, false);
    }

    private void OnEnable()
    {
        if (!UsesScrollNavigation)
            return;

        EnsureScrollSectionsVisible();
        ScheduleScrollToSection(CurrentTab);
    }

    private void OnDestroy()
    {
        if (scrollRect != null)
            scrollRect.onValueChanged.RemoveListener(HandleScrollValueChanged);
    }

    public void SelectTab(CityUITab tab)
    {
        SelectTab(tab, true);
    }

    public void ResetToDefault()
    {
        SelectTab(defaultTab);
    }

    /// <summary>
    /// Forces the ScrollRect to rebuild its content and re-evaluate the active
    /// navigation section. Call this after a section adds/removes dynamic rows.
    /// </summary>
    public void RefreshScrollLayout()
    {
        if (!UsesScrollNavigation)
            return;

        var content = ResolveScrollContent();
        if (content == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();

        if (trackSelectionFromScroll)
            UpdateSelectionFromScroll();
    }

    private void WireButtons()
    {
        if (tabs == null)
            return;

        foreach (var binding in tabs)
        {
            if (binding == null || binding.button == null)
                continue;

            CityUITab capturedTab = binding.tab;
            binding.button.onClick.AddListener(() => SelectTab(capturedTab));
        }
    }

    private void SelectTab(CityUITab tab, bool notify)
    {
        CurrentTab = tab;

        if (UsesScrollNavigation)
        {
            EnsureScrollSectionsVisible();
            ApplySelectedVisual(tab);
            ScheduleScrollToSection(tab);
        }
        else
        {
            ApplyLegacyPanelSelection(tab);
        }

        if (notify)
            TabChanged?.Invoke(tab);
    }

    private void ApplyLegacyPanelSelection(CityUITab tab)
    {
        if (tabs == null)
            return;

        foreach (var binding in tabs)
        {
            if (binding == null)
                continue;

            bool selected = binding.tab == tab;

            if (binding.panel != null)
                binding.panel.SetActive(selected);

            if (binding.selectedGraphic != null)
                binding.selectedGraphic.color = selected ? selectedColor : normalColor;
        }
    }

    private void ApplySelectedVisual(CityUITab tab)
    {
        if (tabs == null)
            return;

        foreach (var binding in tabs)
        {
            if (binding?.selectedGraphic == null)
                continue;

            binding.selectedGraphic.color =
                binding.tab == tab ? selectedColor : normalColor;
        }
    }

    private void EnsureScrollSectionsVisible()
    {
        if (tabs == null)
            return;

        foreach (var binding in tabs)
        {
            if (binding == null)
                continue;

            if (binding.panel != null && !binding.panel.activeSelf)
                binding.panel.SetActive(true);

            if (binding.section != null && !binding.section.gameObject.activeSelf)
                binding.section.gameObject.SetActive(true);
        }
    }

    private RectTransform ResolveScrollContent()
    {
        if (contentRoot == null && scrollRect != null)
            contentRoot = scrollRect.content;

        return contentRoot;
    }

    private RectTransform ResolveViewport()
    {
        if (scrollRect == null)
            return null;

        return scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.transform as RectTransform;
    }

    private RectTransform ResolveSection(TabBinding binding)
    {
        if (binding == null)
            return null;

        if (binding.section != null)
            return binding.section;

        return binding.panel != null
            ? binding.panel.transform as RectTransform
            : null;
    }

    private void ScheduleScrollToSection(CityUITab tab)
    {
        if (!UsesScrollNavigation || !isActiveAndEnabled)
            return;

        if (scrollRoutine != null)
            StopCoroutine(scrollRoutine);

        scrollRoutine = StartCoroutine(ScrollToSectionAtEndOfFrame(tab));
    }

    private IEnumerator ScrollToSectionAtEndOfFrame(CityUITab tab)
    {
        suppressScrollTracking = true;

        // Wait for dynamic production/building rows and ContentSizeFitters to
        // finish changing the sheet before measuring the target section.
        yield return null;

        var content = ResolveScrollContent();
        var viewport = ResolveViewport();
        var binding = FindBinding(tab);
        var section = ResolveSection(binding);

        if (content != null && viewport != null && section != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();

            float scrollableHeight = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            float targetOffset = GetSectionOffsetFromTop(content, section);

            float normalized = scrollableHeight <= 0.01f
                ? 1f
                : 1f - Mathf.Clamp01(targetOffset / scrollableHeight);

            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = normalized;
        }

        suppressScrollTracking = false;
        scrollRoutine = null;
    }

    private void HandleScrollValueChanged(Vector2 _)
    {
        if (!trackSelectionFromScroll || suppressScrollTracking || !UsesScrollNavigation)
            return;

        UpdateSelectionFromScroll();
    }

    private void UpdateSelectionFromScroll()
    {
        var content = ResolveScrollContent();
        var viewport = ResolveViewport();

        if (content == null || viewport == null || tabs == null || tabs.Length == 0)
            return;

        float scrollableHeight = Mathf.Max(0f, content.rect.height - viewport.rect.height);
        float currentOffset =
            (1f - Mathf.Clamp01(scrollRect.verticalNormalizedPosition)) * scrollableHeight;

        TabBinding closest = null;
        float closestDistance = float.MaxValue;

        foreach (var binding in tabs)
        {
            var section = ResolveSection(binding);
            if (section == null || !section.gameObject.activeInHierarchy)
                continue;

            float sectionOffset = GetSectionOffsetFromTop(content, section);
            float distance = Mathf.Abs(sectionOffset - (currentOffset + sectionSelectionBias));

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = binding;
            }
        }

        if (closest == null || closest.tab == CurrentTab)
            return;

        CurrentTab = closest.tab;
        ApplySelectedVisual(CurrentTab);
        TabChanged?.Invoke(CurrentTab);
    }

    private static float GetSectionOffsetFromTop(RectTransform content, RectTransform section)
    {
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(content, section);

        // RectTransform local coordinates depend on pivot. This calculates the
        // content's actual top edge and measures the section from that edge.
        float contentTop = (1f - content.pivot.y) * content.rect.height;
        float sectionTop = bounds.max.y;

        return Mathf.Max(0f, contentTop - sectionTop);
    }

    private TabBinding FindBinding(CityUITab tab)
    {
        if (tabs == null)
            return null;

        foreach (var binding in tabs)
            if (binding != null && binding.tab == tab)
                return binding;

        return null;
    }
}
