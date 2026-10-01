using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Navigates a continuous city-management sheet without changing section visibility.
/// The panel frame, header, and navigation buttons should live outside the ScrollRect.
/// </summary>
public class CityUIScrollNavigator : MonoBehaviour
{
    [Header("Scroll Sheet")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform contentRoot;

    [Header("Sections")]
    [SerializeField] private RectTransform citizensAndTilesSection;
    [SerializeField] private RectTransform productionSection;
    [SerializeField] private RectTransform buildingsAndSpecialistsSection;
    [SerializeField] private RectTransform crimeAndDiseaseSection;
    [SerializeField] private RectTransform unitStorageSection;

    [Header("Optional Navigation Buttons")]
    [SerializeField] private Button citizensButton;
    [SerializeField] private Button productionButton;
    [SerializeField] private Button buildingsButton;
    [SerializeField] private Button crimeDiseaseButton;
    [SerializeField] private Button unitStorageButton;

    public CityUITab CurrentSection { get; private set; } = CityUITab.Overview;
    public event Action<CityUITab> SectionChanged;

    /// <summary>
    /// Raised only for fixed navigation-button clicks. This lets CityUI distinguish an
    /// explicit request for tile assignment from ordinary scrolling/programmatic navigation.
    /// </summary>
    public event Action<CityUITab> NavigationButtonClicked;

    private void Awake()
    {
        AddButtonListeners();
    }

    private void OnEnable()
    {
        if (scrollRect != null)
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
    }

    private void OnDisable()
    {
        if (scrollRect != null)
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
    }

    private void OnDestroy()
    {
        RemoveButtonListeners();
    }

    public void NavigateTo(CityUITab tab)
    {
        RectTransform section = GetSection(tab);
        if (scrollRect == null || contentRoot == null || section == null)
            return;

        RebuildLayout();

        RectTransform viewport = scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.transform as RectTransform;
        if (viewport == null)
            return;

        Bounds sectionBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(contentRoot, section);
        float scrollableHeight = Mathf.Max(0f, contentRoot.rect.height - viewport.rect.height);
        float distanceFromContentTop = contentRoot.rect.yMax - sectionBounds.max.y;

        scrollRect.StopMovement();
        scrollRect.verticalNormalizedPosition = scrollableHeight <= Mathf.Epsilon
            ? 1f
            : 1f - Mathf.Clamp01(distanceFromContentTop / scrollableHeight);
        SetCurrentSection(tab);
    }

    public void ResetToDefault()
    {
        NavigateTo(CityUITab.Overview);
    }

    public void RefreshLayout()
    {
        if (contentRoot == null)
            return;

        RebuildLayout();
        UpdateCurrentSectionFromScroll();
    }

    private void RebuildLayout()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        Canvas.ForceUpdateCanvases();
    }

    private RectTransform GetSection(CityUITab tab)
    {
        switch (tab)
        {
            case CityUITab.Overview: return citizensAndTilesSection;
            case CityUITab.Production: return productionSection;
            case CityUITab.BuildingsAndSpecialists: return buildingsAndSpecialistsSection;
            case CityUITab.CrimeAndDisease: return crimeAndDiseaseSection;
            case CityUITab.UnitStorage: return unitStorageSection;
            default: return null;
        }
    }

    private void OnScrollValueChanged(Vector2 _)
    {
        UpdateCurrentSectionFromScroll();
    }

    private void UpdateCurrentSectionFromScroll()
    {
        if (scrollRect == null || contentRoot == null)
            return;

        RectTransform viewport = scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.transform as RectTransform;
        if (viewport == null)
            return;

        float viewportTop = RectTransformUtility.CalculateRelativeRectTransformBounds(
            contentRoot, viewport).max.y;
        CityUITab nearestTab = CurrentSection;
        float nearestDistance = float.MaxValue;

        foreach (CityUITab tab in Enum.GetValues(typeof(CityUITab)))
        {
            RectTransform section = GetSection(tab);
            if (section == null || !section.gameObject.activeInHierarchy)
                continue;

            float sectionTop = RectTransformUtility.CalculateRelativeRectTransformBounds(
                contentRoot, section).max.y;
            float distance = Mathf.Abs(viewportTop - sectionTop);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTab = tab;
            }
        }

        SetCurrentSection(nearestTab);
    }

    private void SetCurrentSection(CityUITab tab)
    {
        if (CurrentSection == tab)
            return;

        CurrentSection = tab;
        SectionChanged?.Invoke(tab);
    }

    private void NavigateFromButton(CityUITab tab)
    {
        NavigateTo(tab);
        NavigationButtonClicked?.Invoke(tab);
    }

    private void AddButtonListeners()
    {
        citizensButton?.onClick.AddListener(NavigateToCitizens);
        productionButton?.onClick.AddListener(NavigateToProduction);
        buildingsButton?.onClick.AddListener(NavigateToBuildings);
        crimeDiseaseButton?.onClick.AddListener(NavigateToCrimeDisease);
        unitStorageButton?.onClick.AddListener(NavigateToUnitStorage);
    }

    private void RemoveButtonListeners()
    {
        citizensButton?.onClick.RemoveListener(NavigateToCitizens);
        productionButton?.onClick.RemoveListener(NavigateToProduction);
        buildingsButton?.onClick.RemoveListener(NavigateToBuildings);
        crimeDiseaseButton?.onClick.RemoveListener(NavigateToCrimeDisease);
        unitStorageButton?.onClick.RemoveListener(NavigateToUnitStorage);
    }

    private void NavigateToCitizens() => NavigateFromButton(CityUITab.Overview);
    private void NavigateToProduction() => NavigateFromButton(CityUITab.Production);
    private void NavigateToBuildings() => NavigateFromButton(CityUITab.BuildingsAndSpecialists);
    private void NavigateToCrimeDisease() => NavigateFromButton(CityUITab.CrimeAndDisease);
    private void NavigateToUnitStorage() => NavigateFromButton(CityUITab.UnitStorage);
}
