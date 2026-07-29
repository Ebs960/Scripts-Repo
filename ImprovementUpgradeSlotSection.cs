using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Prefab-facing container for all improvement options assigned to one upgrade slot.</summary>
public class ImprovementUpgradeSlotSection : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI slotNameText;
    [SerializeField] private TextMeshProUGUI slotStatusText;
    [SerializeField] private Transform optionContainer;
    private LayoutElement layoutElement;

    public Transform OptionContainer => optionContainer != null ? optionContainer : transform;

    private void Awake()
    {
        EnsureVisualTree();
    }

    private void EnsureVisualTree()
    {
        layoutElement ??= gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        if (slotNameText == null) slotNameText = CreateText("Slot Name", "GENERAL", 18f, new Vector2(8f, -6f), new Vector2(300f, 28f));
        if (slotStatusText == null) slotStatusText = CreateText("Slot Status", "Empty", 13f, new Vector2(320f, -8f), new Vector2(270f, 24f));
        if (optionContainer == null)
        {
            var child = new GameObject("Options", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            child.transform.SetParent(transform, false);
            var rect = (RectTransform)child.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -40f);
            rect.sizeDelta = new Vector2(0f, 0f);
            var layout = child.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            child.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            optionContainer = child.transform;
        }
    }

    private TextMeshProUGUI CreateText(string objectName, string value, float fontSize, Vector2 position, Vector2 size)
    {
        var child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        child.transform.SetParent(transform, false);
        var rect = (RectTransform)child.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = child.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.raycastTarget = false;
        return text;
    }

    public void Bind(string slotName, int installedCount, int optionCount)
    {
        EnsureVisualTree();
        layoutElement.preferredHeight = 40f + Mathf.Max(1, optionCount) * 102f;
        if (slotNameText != null) slotNameText.text = slotName;
        if (slotStatusText != null)
            slotStatusText.text = installedCount > 0
                ? $"{installedCount} installed • {optionCount} options"
                : $"Empty • {optionCount} options";
    }
}
