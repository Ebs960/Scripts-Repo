using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TreeNodeVisual : MonoBehaviour
{
    [Tooltip("Tech/Culture Node Button: one shared blue/gold sprite for all states.")]
    public Sprite backgroundSprite;
    public Color locked = new Color(0.025f, 0.04f, 0.07f);
    public Color available = new Color(0.035f, 0.10f, 0.20f);
    public Color active = new Color(0.055f, 0.17f, 0.28f);
    public Color completed = new Color(0.055f, 0.095f, 0.14f);
    public Color border = new Color(0.48f, 0.43f, 0.30f);
    public Color selectedBorder = new Color(0.85f, 0.76f, 0.49f);
    public Sprite lockedSprite;
    public Sprite availableSprite;
    public Sprite activeSprite;
    public Sprite completedSprite;
    public void Apply(Image background, Button button, TMP_Text label, int state, bool selected, Image icon = null)
    {
        if (background == null) return;
        Color fill = state == 1 ? completed : state == 2 ? active : state == 0 ? available : locked;
        Sprite sprite = backgroundSprite != null ? backgroundSprite : state == 1 ? completedSprite : state == 2 ? activeSprite : state == 0 ? availableSprite : lockedSprite;
        background.sprite = sprite;
        background.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        background.preserveAspect = sprite != null && background.type == Image.Type.Simple;
        background.color = sprite != null ? state == 3 ? new Color(0.4f, 0.43f, 0.48f) : state == 1 ? new Color(0.65f, 0.7f, 0.76f) : Color.white : fill;
        var outline = background.GetComponent<Outline>();
        if (outline == null) outline = background.gameObject.AddComponent<Outline>();
        outline.effectColor = selected || state == 2 ? selectedBorder : state == 3 ? Color.Lerp(border, locked, 0.65f) : border;
        outline.effectDistance = new Vector2(1, -1);
        outline.useGraphicAlpha = false;
        if (label != null)
        {
            label.color = state == 3 ? new Color(0.65f, 0.68f, 0.70f) : new Color(0.94f, 0.95f, 0.97f);
            label.raycastTarget = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10;
            label.fontSizeMax = 16;
            label.characterSpacing = 0;
            label.textWrappingMode = TextWrappingModes.Normal;
            if (label.transform.parent == background.transform)
            {
                label.rectTransform.anchorMin = new Vector2(icon != null && icon.sprite != null ? 0.32f : 0, 0);
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(8, 8);
                label.rectTransform.offsetMax = new Vector2(-10, -8);
                label.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }
        if (icon != null)
        {
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.color = state == 3 ? new Color(0.45f, 0.48f, 0.53f) : state == 1 ? new Color(0.7f, 0.74f, 0.8f) : Color.white;
            icon.gameObject.SetActive(icon.sprite != null);
            if (icon.transform.parent == background.transform)
            {
                float size = Mathf.Max(1, Mathf.Min(background.rectTransform.rect.height - 16, background.rectTransform.rect.width * 0.25f));
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, 0.5f);
                icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                icon.rectTransform.sizeDelta = new Vector2(size, size);
                icon.rectTransform.anchoredPosition = new Vector2(8 + size * 0.5f, 0);
            }
        }
        if (button != null)
        {
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = colors.disabledColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(0.88f, 0.88f, 0.88f);
            colors.colorMultiplier = 1;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }
    }
}