using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TreePanelView : MonoBehaviour
{
    [Header("Shared Shell")]
    public RectTransform panelShell;
    public RectTransform header;
    public RectTransform treeViewport;
    public RectTransform selectedNodeArea;
    public Image shellSurface;
    public Image headerSurface;
    public Image infoSurface;
    public Sprite surfaceSprite;
    [Tooltip("Tech/Culture Main Shell: transparent opening, blue header, gold frame. Slice the full header band into the top border.")]
    public Sprite mainShellSprite;
    [Tooltip("Research Progress Bar Background: shared blue/gold channel artwork, not the legacy parchment HUD bar.")]
    public Sprite researchProgressSprite;
    public RectTransform researchProgress;
    public Image progressBackgroundImage;
    public Image progressFillImage;
    public TMP_Text turnsRemainingText;
    [Tooltip("Normalized left, bottom, right, top insets of the fill channel inside the progress artwork.")]
    public Vector4 progressChannelInsets = new Vector4(0.025f, 0.2f, 0.025f, 0.2f);
    public Color progressFillColor = new Color(0.25f, 0.65f, 0.78f);
    public bool arrangeAssignedRegions = true;
    public bool fullScreen = true;
    public bool hidePermanentDetails = true;
    [Min(0)] public float frameInset = 3;
    public GameObject[] legacyInformationPanels;
    public RectTransform navigationGroup;
    public Button closeButton;
    [Min(36)] public float headerHeight = 52;
    [Min(160)] public float informationHeight = 224;
    public TMP_Text titleText;
    public TMP_Text focusText;
    public TMP_Text progressText;
    public Slider progressBar;
    public Slider selectedProgressBar;
    public Button researchButton;
    public TMP_Text researchButtonText;
    public Button zoomInButton;
    public Button zoomOutButton;
    public Button resetViewButton;
    public Button focusResearchButton;
    public Color shellColor = Color.white;
    public Color regionColor = new Color(0.035f, 0.085f, 0.16f, 0.98f);
    private UnityAction researchAction;
    private UnityAction focusAction;
    private TreeNavigation navigation;
    private UnityAction closeAction;
    private bool generatedClose;
    private RectTransform progressChannel;
    private bool arranging;
    private float normalizedProgress;

    public static TreePanelView GetOrCreateShell(MonoBehaviour owner, GameObject panel, ScrollRect scroll, Button existingClose)
    {
        if (owner == null) return null;
        if (panel == null) panel = owner.gameObject;
        var view = panel.GetComponent<TreePanelView>();
        if (view == null) view = panel.AddComponent<TreePanelView>();
        view.PrepareShell(owner, panel, scroll, existingClose);
        return view;
    }

    public void PrepareShell(MonoBehaviour owner, GameObject panel, ScrollRect scroll, Button existingClose)
    {
        if (panel == null) panel = owner.gameObject;
        if (panelShell == null) panelShell = panel.transform as RectTransform;
        if (treeViewport == null && scroll != null) treeViewport = scroll.transform as RectTransform;
        var previousSurface = panel.GetComponent<Image>();
        if (shellSurface == null || shellSurface == previousSurface)
        {
            var artwork = panel.transform.Find("ShellArtwork") as RectTransform;
            if (artwork == null) artwork = CreateRect("ShellArtwork", panel.transform);
            PlaceChild(artwork, Vector2.zero, Vector2.one);
            shellSurface = artwork.GetComponent<Image>();
            if (shellSurface == null) shellSurface = artwork.gameObject.AddComponent<Image>();
        }
        if (previousSurface != null && previousSurface != shellSurface)
        {
            previousSurface.material = null;
            previousSurface.sprite = null;
            previousSurface.color = Color.clear;
            previousSurface.raycastTarget = false;
            var previousOutline = previousSurface.GetComponent<Outline>();
            if (previousOutline != null) previousOutline.enabled = false;
        }
        if (closeButton == null) closeButton = existingClose;
        CreateFallbackControls(owner);
        if (shellSurface.transform.parent == panelShell) shellSurface.transform.SetAsLastSibling();
        if (header != null && header.parent == panelShell) header.SetAsLastSibling();
    }

    public void HideLegacyDetails(params Component[] references)
    {
        if (!hidePermanentDetails) return;
        if (selectedNodeArea != null) selectedNodeArea.gameObject.SetActive(false);
        if (infoSurface != null && infoSurface != shellSurface && infoSurface != headerSurface &&
            infoSurface.transform != panelShell && infoSurface.transform != treeViewport)
            infoSurface.gameObject.SetActive(false);
        if (legacyInformationPanels != null)
            foreach (var panel in legacyInformationPanels)
                if (panel != null && panel != gameObject && panel.transform != panelShell && panel.transform != treeViewport)
                    panel.SetActive(false);
        Transform commonParent = null;
        foreach (var reference in references)
            if (reference != null) { commonParent = reference.transform.parent; break; }
        while (commonParent != null && commonParent != panelShell && commonParent != treeViewport && commonParent != transform)
        {
            bool containsAll = true;
            int count = 0;
            foreach (var reference in references)
                if (reference != null) { count++; if (!reference.transform.IsChildOf(commonParent)) containsAll = false; }
            if (count > 1 && containsAll) { commonParent.gameObject.SetActive(false); break; }
            commonParent = commonParent.parent;
        }
        foreach (var reference in references)
            if (reference != null && reference.transform != panelShell && reference.transform != treeViewport)
                reference.gameObject.SetActive(false);
    }

    public void Configure(string title, TreeNavigation treeNavigation, UnityAction research, UnityAction focus, UnityAction close = null)
    {
        Unbind();
        navigation = treeNavigation;
        researchAction = research;
        focusAction = focus;
        closeAction = close;
        SetText(titleText, title);
        if (researchButton != null && researchAction != null) researchButton.onClick.AddListener(researchAction);
        if (generatedClose && closeButton != null && closeAction != null) closeButton.onClick.AddListener(closeAction);
        if (navigationGroup != null) navigationGroup.gameObject.SetActive(false);
        foreach (var control in new[] { zoomInButton, zoomOutButton, resetViewButton, focusResearchButton })
            if (control != null) control.gameObject.SetActive(false);
        if (shellSurface != null)
        {
            shellSurface.material = null;
            shellSurface.sprite = mainShellSprite;
            shellSurface.color = mainShellSprite != null ? Color.white : Color.clear;
            shellSurface.type = mainShellSprite != null && mainShellSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            shellSurface.preserveAspect = shellSurface.type != Image.Type.Sliced;
            shellSurface.raycastTarget = false;
            var outline = shellSurface.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }
        if (headerSurface != null)
        {
            headerSurface.sprite = null;
            headerSurface.color = mainShellSprite != null ? Color.clear : regionColor;
            headerSurface.raycastTarget = false;
        }
        if (progressBackgroundImage != null)
        {
            progressBackgroundImage.sprite = researchProgressSprite;
            progressBackgroundImage.color = researchProgressSprite != null ? Color.white : regionColor;
            progressBackgroundImage.type = researchProgressSprite != null && researchProgressSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            progressBackgroundImage.preserveAspect = researchProgressSprite != null && progressBackgroundImage.type == Image.Type.Simple;
            progressBackgroundImage.raycastTarget = false;
        }
        if (treeViewport != null)
        {
            var surface = treeViewport.GetComponent<Image>();
            if (surface != null) { surface.material = null; surface.sprite = null; surface.color = Color.clear; }
            var scroll = treeViewport.GetComponent<ScrollRect>();
            if (scroll != null && scroll.viewport != null)
            {
                var viewportSurface = scroll.viewport.GetComponent<Image>();
                if (viewportSurface != null) { viewportSurface.material = null; viewportSurface.color = Color.clear; }
                var mask = scroll.viewport.GetComponent<Mask>();
                if (mask != null) mask.showMaskGraphic = false;
            }
        }
        if (progressFillImage != null) { progressFillImage.color = progressFillColor; progressFillImage.raycastTarget = false; }
        if (progressBar != null && progressFillImage != null) progressBar.gameObject.SetActive(false);
        if (hidePermanentDetails && researchButton != null) researchButton.gameObject.SetActive(false);
        if (hidePermanentDetails && selectedProgressBar != null) selectedProgressBar.gameObject.SetActive(false);
        if (header != null) foreach (var label in header.GetComponentsInChildren<TMP_Text>(true))
        {
            label.color = new Color(0.94f, 0.94f, 0.91f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 12;
            label.fontSizeMax = Mathf.Min(24, Mathf.Max(12, label.fontSize));
        }
        if (hidePermanentDetails) HideLegacyDetails();
        Arrange();
    }

    private void OnRectTransformDimensionsChange() { Arrange(); }
    private void Arrange()
    {
        if (!arrangeAssignedRegions || panelShell == null || arranging) return;
        arranging = true;
        try
        {
        if (fullScreen && panelShell.parent is RectTransform && panelShell.GetComponent<Canvas>() == null)
        {
            panelShell.anchorMin = Vector2.zero;
            panelShell.anchorMax = Vector2.one;
            panelShell.offsetMin = panelShell.offsetMax = Vector2.zero;
        }
        Vector2 size = panelShell.rect.size;
        Vector2 artworkSize = mainShellSprite != null ? FitArtworkSize(size, mainShellSprite.rect.width / mainShellSprite.rect.height,
            mainShellSprite.border != Vector4.zero) : size;
        Vector2 letterbox = (size - artworkSize) * 0.5f;
        Vector4 border = mainShellSprite != null && shellSurface != null ? mainShellSprite.border / Mathf.Max(0.001f, shellSurface.pixelsPerUnit) : Vector4.zero;
        float left = letterbox.x + Mathf.Max(frameInset, border.x);
        float right = letterbox.x + Mathf.Max(frameInset, border.z);
        float width = Mathf.Max(1, size.x - left - right);
        float desiredHeader = width < 480 ? 112 : width < 900 ? 80 : headerHeight;
        float top = header != null ? Mathf.Max(desiredHeader, border.w) : 0;
        float bottom = !hidePermanentDetails && selectedNodeArea != null ? Mathf.Min(informationHeight, panelShell.rect.height * 0.4f) : 0;
        float topInset = letterbox.y + frameInset;
        float bottomInset = letterbox.y + Mathf.Max(frameInset, border.y);
        Place(header, new Vector2(0, 1), Vector2.one, new Vector2(left, -top - topInset), new Vector2(-right, -topInset));
        if (!hidePermanentDetails) Place(selectedNodeArea, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, bottom));
        Place(treeViewport, Vector2.zero, Vector2.one, new Vector2(left, bottom + bottomInset), new Vector2(-right, -top - topInset));
        ArrangeHeader(width, top);
        if (progressChannel != null)
        {
            float insetLeft = Mathf.Clamp(progressChannelInsets.x, 0, 0.45f);
            float insetBottom = Mathf.Clamp(progressChannelInsets.y, 0, 0.45f);
            float insetRight = Mathf.Clamp(progressChannelInsets.z, 0, 0.45f);
            float insetTop = Mathf.Clamp(progressChannelInsets.w, 0, 0.45f);
            Vector2 channelSize = progressBackgroundImage.rectTransform.rect.size;
            Vector2 drawingScale = Vector2.one;
            if (researchProgressSprite != null)
            {
                if (researchProgressSprite.border == Vector4.zero)
                {
                    Vector2 fitted = FitArtworkSize(channelSize, researchProgressSprite.rect.width / researchProgressSprite.rect.height, false);
                    drawingScale = new Vector2(fitted.x / Mathf.Max(1, channelSize.x), fitted.y / Mathf.Max(1, channelSize.y));
                }
                else
                {
                    Vector4 channelBorder = researchProgressSprite.border / Mathf.Max(0.001f, progressBackgroundImage.pixelsPerUnit);
                    insetLeft = Mathf.Max(insetLeft, channelBorder.x / Mathf.Max(1, channelSize.x));
                    insetRight = Mathf.Max(insetRight, channelBorder.z / Mathf.Max(1, channelSize.x));
                    insetBottom = Mathf.Max(insetBottom, channelBorder.y / Mathf.Max(1, channelSize.y));
                    insetTop = Mathf.Max(insetTop, channelBorder.w / Mathf.Max(1, channelSize.y));
                }
            }
            Vector2 drawingInset = (Vector2.one - drawingScale) * 0.5f;
            progressChannel.anchorMin = drawingInset + Vector2.Scale(drawingScale, new Vector2(Mathf.Min(0.45f, insetLeft), Mathf.Min(0.45f, insetBottom)));
            progressChannel.anchorMax = Vector2.one - drawingInset - Vector2.Scale(drawingScale, new Vector2(Mathf.Min(0.45f, insetRight), Mathf.Min(0.45f, insetTop)));
            progressChannel.offsetMin = progressChannel.offsetMax = Vector2.zero;
        }
        }
        finally { arranging = false; }
    }

    public static Vector2 FitArtworkSize(Vector2 available, float aspect, bool sliced)
    {
        if (sliced || aspect <= 0 || float.IsNaN(aspect) || float.IsInfinity(aspect)) return available;
        float width = Mathf.Min(available.x, available.y * aspect);
        return new Vector2(width, width / aspect);
    }

    private void ArrangeHeader(float width, float height)
    {
        if (header == null) return;
        var layout = header.GetComponent<LayoutGroup>();
        if (layout != null) layout.enabled = false;
        if (closeButton != null) HeaderRect(closeButton.transform as RectTransform, width - 42, 8, 34, 34);
        if (width < 480)
        {
            HeaderRect(titleText != null ? titleText.rectTransform : null, 10, 8, width - 62, 28);
            HeaderRect(focusText != null ? focusText.rectTransform : null, 10, 40, width - 20, 24);
            HeaderRect(researchProgress, 10, 72, width - 20, 26);
        }
        else if (width < 900)
        {
            HeaderRect(titleText != null ? titleText.rectTransform : null, 12, 6, width - 72, 28);
            HeaderRect(focusText != null ? focusText.rectTransform : null, 12, 42, width * 0.4f - 18, 26);
            HeaderRect(researchProgress, width * 0.4f, 42, width * 0.6f - 12, 26);
        }
        else
        {
            HeaderRect(titleText != null ? titleText.rectTransform : null, 12, (height - 30) * 0.5f, 228, 30);
            HeaderRect(focusText != null ? focusText.rectTransform : null, 252, (height - 30) * 0.5f, width - 670, 30);
            HeaderRect(researchProgress, width - 404, (height - 26) * 0.5f, 344, 26);
        }
        if (progressBackgroundImage != null) PlaceChild(progressBackgroundImage.rectTransform, Vector2.zero, new Vector2(0.76f, 1));
        if (progressText != null && progressBackgroundImage != null) PlaceChild(progressText.rectTransform, Vector2.zero, Vector2.one);
        if (turnsRemainingText != null) PlaceChild(turnsRemainingText.rectTransform, new Vector2(0.78f, 0), Vector2.one);
    }

    private void HeaderRect(RectTransform rect, float left, float top, float width, float height)
    {
        if (rect == null || rect.parent != header) return;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(Mathf.Max(1, width), height);
    }

    private static void PlaceChild(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void CreateFallbackControls(MonoBehaviour owner)
    {
        if (panelShell == null) return;
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        if (font == null)
            foreach (var label in owner.GetComponentsInChildren<TMP_Text>(true))
                if (label.font != null) { font = label.font; break; }
        if (header == null)
        {
            header = CreateRect("Tree Header", panelShell);
            headerSurface = header.gameObject.AddComponent<Image>();
        }
        if (titleText == null)
        {
            titleText = CreateLabel("Title", header, font, 18);
        }
        if (focusText == null) focusText = CreateLabel("Current Focus", header, font, 14);
        if (researchProgress == null) researchProgress = CreateRect("ResearchProgress", header);
        if (progressBackgroundImage == null) progressBackgroundImage = CreateRect("ProgressBackground", researchProgress).gameObject.AddComponent<Image>();
        if (progressFillImage == null)
        {
            progressChannel = CreateRect("FillChannel", progressBackgroundImage.transform);
            progressChannel.gameObject.AddComponent<RectMask2D>();
            progressFillImage = CreateRect("ProgressFill", progressChannel).gameObject.AddComponent<Image>();
        }
        else
        {
            progressChannel = progressFillImage.transform.parent as RectTransform;
            if (progressChannel == progressBackgroundImage.rectTransform || progressChannel == researchProgress)
            {
                progressChannel = CreateRect("FillChannel", progressBackgroundImage.transform);
                progressChannel.gameObject.AddComponent<RectMask2D>();
                progressFillImage.transform.SetParent(progressChannel, false);
                progressChannel.SetAsFirstSibling();
            }
        }
        if (progressText == null) progressText = CreateLabel("ProgressLabel", progressBackgroundImage.transform, font, 12);
        progressText.alignment = TextAlignmentOptions.Center;
        if (turnsRemainingText == null) turnsRemainingText = CreateLabel("TurnsRemainingLabel", researchProgress, font, 12);
        turnsRemainingText.alignment = TextAlignmentOptions.MidlineRight;
        if (closeButton == null)
        {
            closeButton = CreateButton("Close", header, font, "x");
            generatedClose = true;
        }
        else if (closeButton.transform.parent != header) closeButton.transform.SetParent(header, false);
        closeButton.transform.SetAsLastSibling();
        SetFill(normalizedProgress);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static TMP_Text CreateLabel(string name, Transform parent, TMP_FontAsset font, float size)
    {
        var label = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = size;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10;
        label.fontSizeMax = size;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = 0;
        label.color = new Color(0.94f, 0.95f, 0.97f);
        return label;
    }

    private static Button CreateButton(string name, Transform parent, TMP_FontAsset font, string text)
    {
        var rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.035f, 0.085f, 0.16f, 0.94f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = 36;
        var label = CreateLabel("Label", rect, font, 16);
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    private void Place(RectTransform region, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax)
    {
        if (region == null || region == panelShell || region.parent != panelShell) return;
        region.anchorMin = min;
        region.anchorMax = max;
        region.offsetMin = insetMin;
        region.offsetMax = insetMax;
    }

    public void UpdateFocus(string name, float progress, float cost, int perTurn, bool hasFocus)
    {
        SetText(focusText, name);
        float remaining = Mathf.Max(0, cost - progress);
        SetText(progressText, hasFocus ? $"{Mathf.Clamp(progress, 0, Mathf.Max(0, cost)):0}/{Mathf.Max(0, cost):0}" : string.Empty);
        SetText(turnsRemainingText, hasFocus ? (perTurn > 0 ? $"{Mathf.CeilToInt(remaining / perTurn)} turns" : "No income") : string.Empty);
        normalizedProgress = hasFocus ? NormalizeProgress(progress, cost) : 0;
        SetFill(normalizedProgress);
        SetProgress(progressBar, progress, cost);
        if (focusResearchButton != null) focusResearchButton.interactable = hasFocus;
    }

    public void UpdateSelection(float progress, float cost, bool canStart, string actionLabel)
    {
        SetProgress(selectedProgressBar, progress, cost);
        if (researchButton != null) researchButton.interactable = canStart;
        SetText(researchButtonText, actionLabel);
    }

    public static void SetText(TMP_Text target, string text) { if (target != null) target.text = text; }
    public static float NormalizeProgress(float progress, float cost)
    {
        return cost > 0 && !float.IsNaN(cost) && !float.IsInfinity(cost) && !float.IsNaN(progress)
            ? Mathf.Clamp01(progress / cost) : 0;
    }

    private void SetFill(float value)
    {
        if (progressFillImage == null) return;
        var rect = progressFillImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(value, 1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void SetProgress(Slider slider, float progress, float cost)
    {
        if (slider == null) return;
        slider.interactable = false;
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.SetValueWithoutNotify(NormalizeProgress(progress, cost));
    }

    private void OnDestroy() { Unbind(); }
    private void Unbind()
    {
        if (generatedClose && closeButton != null && closeAction != null) closeButton.onClick.RemoveListener(closeAction);
        if (researchButton != null && researchAction != null) researchButton.onClick.RemoveListener(researchAction);
        if (focusResearchButton != null && focusAction != null) focusResearchButton.onClick.RemoveListener(focusAction);
        if (navigation != null)
        {
            if (zoomInButton != null) zoomInButton.onClick.RemoveListener(navigation.ZoomIn);
            if (zoomOutButton != null) zoomOutButton.onClick.RemoveListener(navigation.ZoomOut);
            if (resetViewButton != null) resetViewButton.onClick.RemoveListener(navigation.ResetView);
        }
    }
}