using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored shared tooltip panel used for button hints, disabled reasons and status breakdowns.</summary>
public sealed class BattleTooltipView : MonoBehaviour
{
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI label;
    private readonly Vector3[] corners = new Vector3[4];

    public BattleTooltipView CreateOverlay(RectTransform canvasRoot)
    {
        if (canvasRoot == null) return null;
        var overlay = Instantiate(gameObject, canvasRoot, false);
        overlay.name = "TacticalHoverTooltipArtwork";
        var view = overlay.GetComponent<BattleTooltipView>();
        view.canvasRect = canvasRoot;
        view.panel = (RectTransform)overlay.transform;
        view.label = overlay.GetComponentInChildren<TextMeshProUGUI>(true);
        view.Hide();
        return view;
    }

    public void Show(string text, RectTransform anchor, bool below)
    {
        if (panel == null || label == null || anchor == null || string.IsNullOrEmpty(text)) return;
        int separator = text.IndexOf(':');
        label.text = separator > 0
            ? $"<b><color=#F0D36A>{text.Substring(0, separator).Trim()}</color></b>\n{text.Substring(separator + 1).Trim()}"
            : text;
        panel.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        anchor.GetWorldCorners(corners);
        Vector3 edge = below ? (corners[0] + corners[3]) * 0.5f : (corners[1] + corners[2]) * 0.5f;
        Vector2 local = canvasRect.InverseTransformPoint(edge);
        panel.pivot = new Vector2(0.5f, below ? 1f : 0f);
        float limit = Mathf.Max(0f, canvasRect.rect.width * 0.5f - panel.rect.width * 0.5f - 4f);
        local.x = Mathf.Clamp(local.x, -limit, limit);
        local.y += below ? -6f : 6f;
        float minY = canvasRect.rect.yMin + panel.rect.height * panel.pivot.y + 4f;
        float maxY = canvasRect.rect.yMax - panel.rect.height * (1f - panel.pivot.y) - 4f;
        local.y = minY <= maxY ? Mathf.Clamp(local.y, minY, maxY) : 0f;
        panel.anchoredPosition = local;
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }
}
