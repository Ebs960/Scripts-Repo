using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class FactionSelectionPresentation : MonoBehaviour
{
    private MainMenuManager menu;
    private RectTransform root;
    private RectTransform roster;
    private RectTransform detailArea;
    private RectTransform heroSlot;
    private RectTransform heroViewport;
    private RectTransform header;
    private RectTransform footer;
    private ScrollRect rosterScroll;
    private ScrollRect detailsScroll;
    private AspectRatioFitter artworkFitter;
    private TextMeshProUGUI artworkPlaceholder;
    private Vector2 lastSize;
    private GameObject lastFocusedRow;

    public void Initialize(MainMenuManager manager)
    {
        if (menu != null) return;
        menu = manager;
        var panel = (RectTransform)menu.civSelectionPanel.transform;
        SetRect(panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var background = CreateImage("FactionSelectionShell", panel, new Color(.025f, .04f, .065f, 1f));
        background.raycastTarget = true;
        root = background.rectTransform;
        SetRect(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        header = CreateRect("CivilizationHeader", root);
        if (menu.selectedCivIcon != null)
        {
            Move(menu.selectedCivIcon.rectTransform, header);
            menu.selectedCivIcon.preserveAspect = true;
            menu.selectedCivIcon.raycastTarget = false;
            SetRect(menu.selectedCivIcon.rectTransform, Vector2.zero, Vector2.up, Vector2.zero, new Vector2(64f, 0f));
        }
        if (menu.selectedCivName != null)
        {
            Move(menu.selectedCivName.rectTransform, header);
            ConfigureText(menu.selectedCivName, 36f);
            menu.selectedCivName.enableAutoSizing = true;
            menu.selectedCivName.fontSizeMin = 20f;
            menu.selectedCivName.fontSizeMax = 36f;
            SetRect(menu.selectedCivName.rectTransform, Vector2.zero, Vector2.one, new Vector2(84f, 0f), Vector2.zero);
        }

        rosterScroll = CreateScroll("CivilizationRoster", root);
        roster = rosterScroll.content;
        menu.civButtonContainer = roster;
        detailArea = CreateRect("CivilizationDetails", root);
        heroSlot = CreateRect("HeroArtworkSlot", detailArea);
        var viewportImage = CreateImage("HeroArtworkViewport", heroSlot, new Color(.045f, .07f, .11f, 1f));
        heroViewport = viewportImage.rectTransform;
        heroViewport.gameObject.AddComponent<RectMask2D>();
        var viewportFitter = heroViewport.gameObject.AddComponent<AspectRatioFitter>();
        viewportFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        viewportFitter.aspectRatio = 16f / 9f;
        artworkPlaceholder = CreateText("ArtworkPlaceholder", heroViewport, "Choose a civilization");
        artworkPlaceholder.alignment = TextAlignmentOptions.Center;
        SetRect(artworkPlaceholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -16f));

        if (menu.selectedCivArtwork == menu.selectedCivIcon && menu.selectedCivArtwork != null)
        {
            Debug.LogWarning("[Faction Selection] Hero artwork must be separate from the civilization emblem. Creating a dedicated Image.", menu);
            menu.selectedCivArtwork = null;
        }
        if (menu.selectedCivArtwork == null)
            menu.selectedCivArtwork = CreateImage("HeroArtwork", heroViewport, Color.white);
        else
            Move(menu.selectedCivArtwork.rectTransform, heroViewport);
        menu.selectedCivArtwork.transform.SetAsLastSibling();
        menu.selectedCivArtwork.raycastTarget = false;
        menu.selectedCivArtwork.maskable = true;
        menu.selectedCivArtwork.type = Image.Type.Simple;
        menu.selectedCivArtwork.preserveAspect = true;
        menu.selectedCivArtwork.color = Color.white;
        artworkFitter = menu.selectedCivArtwork.GetComponent<AspectRatioFitter>()
            ?? menu.selectedCivArtwork.gameObject.AddComponent<AspectRatioFitter>();
        artworkFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

        detailsScroll = CreateScroll("DescriptionAndBonuses", detailArea);
        AddDetailText(menu.selectedCivDescription);
        CreateText("BonusesHeading", detailsScroll.content, "Civilization Bonuses");
        AddDetailText(menu.selectedCivBonuses);

        footer = CreateRect("FactionNavigation", root);
        ConfigureNavigationButton(menu.backFromCivButton, false);
        ConfigureNavigationButton(menu.selectCivButton, true);

        foreach (Transform child in panel)
            if (child != root) child.gameObject.SetActive(false);
        ApplyLayout();
        ShowArtwork(null, false);
    }

    public Button CreateCivilizationButton()
    {
        var image = CreateImage("CivilizationButton", roster, Color.white);
        return image.gameObject.AddComponent<Button>();
    }

    public void ConfigureCivilizationButton(Button button, CivData civ, Sprite placeholder)
    {
        Move((RectTransform)button.transform, roster);
        button.transform.SetAsLastSibling();
        var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        image.color = Color.white;
        image.sprite = null;
        image.overrideSprite = null;
        image.raycastTarget = true;
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.interactable = true;
        var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(.65f, .56f, .36f, 1f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.enabled = false;
        var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = false;
        layout.minHeight = 64f;
        layout.preferredHeight = 64f;

        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null) label = CreateText("CivilizationName", button.transform, civ.civName);
        Move(label.rectTransform, button.transform);
        label.gameObject.SetActive(true);
        label.text = civ.civName;
        ConfigureText(label, 24f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = 24f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(70f, 6f), new Vector2(-12f, -6f));
        var existing = button.transform.Find("FactionEmblem");
        var emblem = existing != null ? existing.GetComponent<Image>() : CreateImage("FactionEmblem", button.transform, Color.white);
        Move(emblem.rectTransform, button.transform);
        emblem.sprite = civ.icon != null ? civ.icon : placeholder;
        emblem.overrideSprite = null;
        emblem.preserveAspect = true;
        emblem.raycastTarget = false;
        emblem.gameObject.SetActive(emblem.sprite != null);
        SetRect(emblem.rectTransform, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(12f, -24f), new Vector2(60f, 24f));
    }

    public void ShowArtwork(Sprite artwork, bool hasSelection)
    {
        menu.selectedCivArtwork.sprite = null;
        menu.selectedCivArtwork.overrideSprite = null;
        menu.selectedCivArtwork.enabled = artwork != null;
        artworkPlaceholder.gameObject.SetActive(artwork == null);
        artworkPlaceholder.text = hasSelection ? "Artwork not yet assigned" : "Choose a civilization";
        if (artwork == null) return;
        artworkFitter.aspectRatio = artwork.rect.width / artwork.rect.height;
        menu.selectedCivArtwork.sprite = artwork;
    }

    public void ResetScroll()
    {
        Canvas.ForceUpdateCanvases();
        rosterScroll.StopMovement();
        rosterScroll.verticalNormalizedPosition = 1f;
        ResetDetailsScroll();
    }

    public void ResetDetailsScroll()
    {
        detailsScroll.StopMovement();
        detailsScroll.verticalNormalizedPosition = 1f;
    }

    private void LateUpdate()
    {
        if (root == null) return;
        if (lastSize != root.rect.size) ApplyLayout();
        var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (focused == lastFocusedRow) return;
        lastFocusedRow = focused;
        if (focused == null || !focused.transform.IsChildOf(roster)) return;
        Canvas.ForceUpdateCanvases();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(rosterScroll.viewport, focused.transform);
        var viewport = rosterScroll.viewport.rect;
        float offset = bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y
            : bounds.min.y < viewport.yMin ? viewport.yMin - bounds.min.y : 0f;
        roster.anchoredPosition += new Vector2(0f, offset);
    }

    private void ApplyLayout()
    {
        lastSize = root.rect.size;
        float width = lastSize.x;
        float height = lastSize.y;
        const float margin = 24f;
        SetRect(header, Vector2.up, Vector2.one, new Vector2(margin, -88f), new Vector2(-margin, -margin));
        SetRect(footer, Vector2.zero, Vector2.right, new Vector2(margin, margin), new Vector2(-margin, 88f));
        float bodyHeight = Mathf.Max(0f, height - 224f);
        float bodyWidth = Mathf.Max(0f, width - margin * 2f);
        if (width < 900f)
        {
            float rosterHeight = Mathf.Min(280f, bodyHeight * .28f);
            SetRect((RectTransform)rosterScroll.transform, Vector2.up, Vector2.one,
                new Vector2(margin, -112f - rosterHeight), new Vector2(-margin, -112f));
            SetRect(detailArea, Vector2.zero, Vector2.one, new Vector2(margin, 112f), new Vector2(-margin, -136f - rosterHeight));
            bodyHeight = Mathf.Max(0f, bodyHeight - rosterHeight - margin);
        }
        else
        {
            float rosterWidth = Mathf.Clamp(bodyWidth * .25f, 240f, 400f);
            SetRect((RectTransform)rosterScroll.transform, Vector2.zero, Vector2.up,
                new Vector2(margin, 112f), new Vector2(margin + rosterWidth, -112f));
            SetRect(detailArea, Vector2.zero, Vector2.one,
                new Vector2(margin * 2f + rosterWidth, 112f), new Vector2(-margin, -112f));
            bodyWidth -= rosterWidth + margin;
        }
        float artHeight = Mathf.Min(bodyWidth * 9f / 16f, bodyHeight * .6f);
        SetRect(heroSlot, Vector2.up, Vector2.one, new Vector2(0f, -artHeight), Vector2.zero);
        SetRect((RectTransform)detailsScroll.transform, Vector2.zero, Vector2.one,
            Vector2.zero, new Vector2(0f, -artHeight - 16f));
    }

    private void ConfigureNavigationButton(Button button, bool next)
    {
        if (button == null) return;
        Move((RectTransform)button.transform, footer);
        SetRect((RectTransform)button.transform, new Vector2(next ? .52f : 0f, 0f),
            new Vector2(next ? 1f : .48f, 1f), Vector2.zero, Vector2.zero);
        var colors = button.colors;
        colors.normalColor = new Color(.18f, .24f, .33f, 1f);
        colors.highlightedColor = new Color(.35f, .37f, .36f, 1f);
        colors.selectedColor = new Color(.35f, .37f, .36f, 1f);
        colors.pressedColor = new Color(.12f, .18f, .27f, 1f);
        colors.disabledColor = new Color(.12f, .15f, .19f, .6f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.color = new Color(.85f, .78f, .60f, 1f);
    }

    private void AddDetailText(TextMeshProUGUI text)
    {
        if (text == null) return;
        Move(text.rectTransform, detailsScroll.content);
        ConfigureText(text, 24f);
        text.enableAutoSizing = false;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string value)
    {
        var text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (menu.selectedCivDescription != null) text.font = menu.selectedCivDescription.font;
        text.text = value;
        ConfigureText(text, 24f);
        return text;
    }

    private static void ConfigureText(TextMeshProUGUI text, float size)
    {
        text.fontSize = size;
        text.color = new Color(.87f, .87f, .84f, 1f);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    private static ScrollRect CreateScroll(string name, Transform parent)
    {
        var image = CreateImage(name, parent, new Color(.035f, .055f, .09f, 1f));
        image.raycastTarget = true;
        image.gameObject.AddComponent<RectMask2D>();
        var scroll = image.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = image.rectTransform;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 36f;
        scroll.content = CreateRect("Content", image.transform);
        SetRect(scroll.content, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
        scroll.content.pivot = Vector2.up;
        var layout = scroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = scroll.content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return scroll;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Move(RectTransform rect, Transform parent)
    {
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.gameObject.SetActive(true);
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
