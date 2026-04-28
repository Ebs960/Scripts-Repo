using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Reusable HUD dropdown control with split interactions:
/// - Main button click opens a full panel.
/// - Arrow button click expands/collapses inline body content.
/// </summary>
public class HudDropdownButton : MonoBehaviour
{
    [Header("Button References")]
    [SerializeField] private Button mainButton;
    [SerializeField] private Button arrowButton;

    [Header("Body")]
    [SerializeField] private GameObject bodyRoot;
    [SerializeField] private Image bodyImage;
    [SerializeField] private VerticalLayoutGroup bodyLayout;
    [SerializeField] private bool startCollapsed = true;

    [Header("Display")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private Image iconImage;

    public event Action<HudDropdownButton, bool> OnExpandedChanged;

    private bool isBodyExpanded;
    private Action onMainButtonClick;

    private bool listenersWired;
    private UnityEngine.Events.UnityAction mainButtonListener;
    private UnityEngine.Events.UnityAction arrowButtonListener;

    public bool IsBodyExpanded => isBodyExpanded;
    public Transform BodyRootTransform => bodyRoot != null ? bodyRoot.transform : null;

    private void Awake()
    {
        TryAutoAssignReferences();
    }

    private void OnEnable()
    {
        TryAutoAssignReferences();
        WireButtonListeners();
        ApplyInitialBodyState();
    }

    private void OnDisable()
    {
        UnwireButtonListeners();
    }

    private void Reset()
    {
        TryAutoAssignReferences();
    }

    private void OnValidate()
    {
        TryAutoAssignReferences();
    }

    public void Bind(string label, Sprite icon, Action onMainClick)
    {
        SetLabel(label);
        SetIcon(icon);
        SetMainClick(onMainClick);
    }

    public void SetLabel(string label)
    {
        if (labelText != null)
            labelText.text = label;
    }

    public void SetIcon(Sprite icon)
    {
        if (iconImage == null)
            return;

        iconImage.sprite = icon;
        iconImage.enabled = icon != null;
    }

    public void SetMainClick(Action callback)
    {
        onMainButtonClick = callback;
    }

    public void ToggleBody()
    {
        SetExpandedState(!isBodyExpanded, notify: true);
    }

    public void ExpandBody()
    {
        SetExpandedState(true, notify: true);
    }

    public void CollapseBody()
    {
        SetExpandedState(false, notify: true);
    }

    public void SetBodyContent(GameObject contentPrefab)
    {
        ClearBody();

        if (bodyRoot == null || contentPrefab == null)
            return;

        var instance = Instantiate(contentPrefab, bodyRoot.transform);
        instance.name = "BodyContent_Instance";
        RebuildParentLayouts();
    }

    public void SetBodyContentFromInstance(GameObject contentInstance)
    {
        if (bodyRoot == null)
            return;

        ClearBody();

        if (contentInstance != null)
        {
            contentInstance.transform.SetParent(bodyRoot.transform, false);
            contentInstance.name = "BodyContent_Instance";
            RebuildParentLayouts();
        }
    }

    public void ClearBodyContent()
    {
        ClearBody();
    }

    public void ClearBody()
    {
        if (bodyRoot == null)
            return;

        for (int i = bodyRoot.transform.childCount - 1; i >= 0; i--)
            Destroy(bodyRoot.transform.GetChild(i).gameObject);

        RebuildParentLayouts();
    }

    public void RebuildParentLayouts()
    {
        var current = transform as RectTransform;
        while (current != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(current);
            current = current.parent as RectTransform;
        }
    }

    public void SetBodyHeight(float height)
    {
        if (bodyRoot == null)
            return;

        var bodyRect = bodyRoot.GetComponent<RectTransform>();
        if (bodyRect != null)
        {
            bodyRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            RebuildParentLayouts();
        }
    }

    private void ApplyInitialBodyState()
    {
        SetExpandedState(!startCollapsed, notify: false);
        RebuildParentLayouts();
    }

    private void SetExpandedState(bool expanded, bool notify)
    {
        isBodyExpanded = expanded;

        if (bodyRoot != null)
            bodyRoot.SetActive(isBodyExpanded);

        UpdateArrowVisuals();

        if (notify)
            OnExpandedChanged?.Invoke(this, isBodyExpanded);

        RebuildParentLayouts();
    }

    private void UpdateArrowVisuals()
    {
        if (arrowButton == null)
            return;

        var arrowImage = arrowButton.GetComponentInChildren<Image>(true);
        if (arrowImage != null)
            arrowImage.transform.localRotation = Quaternion.Euler(0f, 0f, isBodyExpanded ? -90f : 0f);
    }

    private void WireButtonListeners()
    {
        if (listenersWired)
            return;

        mainButtonListener = () => onMainButtonClick?.Invoke();
        arrowButtonListener = ToggleBody;

        if (mainButton != null)
            mainButton.onClick.AddListener(mainButtonListener);

        if (arrowButton != null)
            arrowButton.onClick.AddListener(arrowButtonListener);

        listenersWired = true;
    }

    private void UnwireButtonListeners()
    {
        if (!listenersWired)
            return;

        if (mainButton != null && mainButtonListener != null)
            mainButton.onClick.RemoveListener(mainButtonListener);

        if (arrowButton != null && arrowButtonListener != null)
            arrowButton.onClick.RemoveListener(arrowButtonListener);

        listenersWired = false;
    }

    private void TryAutoAssignReferences()
    {
        if (mainButton == null)
            mainButton = FindButtonByNames("Main Button", "Main Button Area") ?? GetComponent<Button>();

        if (arrowButton == null)
            arrowButton = FindButtonByNames("Arrow Button");

        if (bodyRoot == null)
            bodyRoot = FindChildByNames("Body Root")?.gameObject;

        if (labelText == null)
            labelText = FindTmpByNames("Label Text", "Text (TMP)");

        if (iconImage == null)
        {
            var iconTransform = FindChildByNames("Icon");
            if (iconTransform != null)
                iconImage = iconTransform.GetComponent<Image>();
        }

        if (bodyRoot != null)
        {
            if (bodyImage == null)
                bodyImage = bodyRoot.GetComponent<Image>();

            if (bodyLayout == null)
                bodyLayout = bodyRoot.GetComponent<VerticalLayoutGroup>();
        }
    }

    private Button FindButtonByNames(params string[] names)
    {
        var child = FindChildByNames(names);
        return child != null ? child.GetComponent<Button>() : null;
    }

    private TextMeshProUGUI FindTmpByNames(params string[] names)
    {
        var child = FindChildByNames(names);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private Transform FindChildByNames(params string[] names)
    {
        foreach (var candidate in GetComponentsInChildren<Transform>(true))
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (candidate.name == names[i])
                    return candidate;
            }
        }

        return null;
    }
}
