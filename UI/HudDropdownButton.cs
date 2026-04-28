// Assets/Scripts/UI/HudDropdownButton.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Reusable two-button dropdown control for HUD widgets.
/// 
/// Structure:
/// - Main Button: Opens full panel (e.g., TechPanel, CulturePanel)
/// - Arrow Button: Toggles inline body (collapsed/expanded)
/// - Body Root: 9-sliced background, vertically stretchable, hosts inline content
/// 
/// The body is dynamically populated with breakdown/detail content via Bind().
/// </summary>
public class HudDropdownButton : MonoBehaviour
{
    [Header("Button References")]
    [SerializeField] private Button mainButton;
    [SerializeField] private Button arrowButton;

    [Header("Body")]
    [SerializeField] private GameObject bodyRoot;
    [SerializeField] private Image bodyImage; // 9-sliced background
    [SerializeField] private VerticalLayoutGroup bodyLayout; // For content sizing

    [Header("Display")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private Image iconImage;

    private bool isBodyExpanded = false;
    private System.Action onMainButtonClick;
    private RectTransform bodyRectTransform;

    private void Awake()
    {
        bodyRectTransform = bodyRoot?.GetComponent<RectTransform>();
    }

    private void Start()
    {
        WireButtonListeners();
        
        // Start with body collapsed
        if (bodyRoot != null)
            bodyRoot.SetActive(false);
        isBodyExpanded = false;
    }

    private void OnDestroy()
    {
        UnwireButtonListeners();
    }

    private void WireButtonListeners()
    {
        if (mainButton != null)
        {
            mainButton.onClick.RemoveAllListeners();
            mainButton.onClick.AddListener(() =>
            {
                onMainButtonClick?.Invoke();
            });
        }

        if (arrowButton != null)
        {
            arrowButton.onClick.RemoveAllListeners();
            arrowButton.onClick.AddListener(() =>
            {
                ToggleBody();
            });
        }
    }

    private void UnwireButtonListeners()
    {
        if (mainButton != null)
            mainButton.onClick.RemoveAllListeners();
        if (arrowButton != null)
            arrowButton.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// Configure this dropdown button with label, icon, and main button callback.
    /// </summary>
    public void Bind(string label, Sprite icon, System.Action onMainClick)
    {
        if (labelText != null)
            labelText.text = label;

        if (iconImage != null)
            iconImage.sprite = icon;

        onMainButtonClick = onMainClick;
    }

    /// <summary>
    /// Populate the body with content (breakdown items, detail widgets, etc.).
    /// </summary>
    public void SetBodyContent(GameObject contentPrefab)
    {
        ClearBodyContent();

        if (bodyRoot == null || contentPrefab == null)
            return;

        var instance = Instantiate(contentPrefab, bodyRoot.transform);
        instance.name = "BodyContent_Instance";
    }

    public void ClearBodyContent()
    {
        if (bodyRoot == null) return;

        foreach (Transform child in bodyRoot.transform)
            Destroy(child.gameObject);
    }

    public void SetBodyContentFromInstance(GameObject contentInstance)
    {
        if (bodyRoot == null) return;

        ClearBodyContent();

        if (contentInstance != null)
        {
            contentInstance.transform.SetParent(bodyRoot.transform, false);
            contentInstance.name = "BodyContent_Instance";
        }
    }

    public Transform BodyRootTransform => bodyRoot != null ? bodyRoot.transform : null;

    /// <summary>
    /// Toggle body visibility and expand/collapse state.
    /// </summary>
    public void ToggleBody()
    {
        isBodyExpanded = !isBodyExpanded;

        if (bodyRoot != null)
            bodyRoot.SetActive(isBodyExpanded);

        // Optional: Animate arrow rotation or other visual feedback
        UpdateArrowVisuals();
    }

    /// <summary>
    /// Expand the body (show inline content).
    /// </summary>
    public void ExpandBody()
    {
        if (isBodyExpanded) return;

        isBodyExpanded = true;
        if (bodyRoot != null)
            bodyRoot.SetActive(true);
        UpdateArrowVisuals();
    }

    /// <summary>
    /// Collapse the body (hide inline content).
    /// </summary>
    public void CollapseBody()
    {
        if (!isBodyExpanded) return;

        isBodyExpanded = false;
        if (bodyRoot != null)
            bodyRoot.SetActive(false);
        UpdateArrowVisuals();
    }

    /// <summary>
    /// Update arrow visuals (e.g., rotation) based on expanded state.
    /// </summary>
    private void UpdateArrowVisuals()
    {
        if (arrowButton == null) return;

        // Optional: Rotate arrow image
        var arrowImage = arrowButton.GetComponentInChildren<Image>();
        if (arrowImage != null)
        {
            arrowImage.transform.localRotation = Quaternion.Euler(0, 0, isBodyExpanded ? -90 : 0);
        }
    }

    /// <summary>
    /// Force set the body content size (height).
    /// </summary>
    public void SetBodyHeight(float height)
    {
        if (bodyRectTransform != null)
        {
            bodyRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }

    /// <summary>
    /// Get body visibility state.
    /// </summary>
    public bool IsBodyExpanded => isBodyExpanded;
}
