using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Pointer hover hook. Pointer events reach this component even when its Button is not interactable.</summary>
public sealed class BattleHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private const float DefaultHoverDelay = 0.4f;
    [SerializeField] private BattleTooltipView tooltip;
    [SerializeField] private bool below;
    private string text;
    private bool hovered;
    private bool visible;
    private Coroutine pendingShow;

    public void Initialize(BattleTooltipView view) => tooltip = view;

    public void SetText(string value)
    {
        if (text == value) return;
        text = value;
        if (!hovered || tooltip == null) return;
        if (string.IsNullOrEmpty(text)) Hide();
        else if (visible) tooltip.Show(text, (RectTransform)transform, below);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        if (tooltip != null && !string.IsNullOrEmpty(text)) pendingShow = StartCoroutine(ShowAfterDelay());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        hovered = false;
        Hide();
    }

    private IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSecondsRealtime(DefaultHoverDelay);
        pendingShow = null;
        if (!hovered || tooltip == null || string.IsNullOrEmpty(text)) yield break;
        tooltip.Show(text, (RectTransform)transform, below);
        visible = true;
    }

    private void Hide()
    {
        if (pendingShow != null)
        {
            StopCoroutine(pendingShow);
            pendingShow = null;
        }
        visible = false;
        if (tooltip != null) tooltip.Hide();
    }

    private void OnDisable()
    {
        hovered = false;
        Hide();
    }
}
