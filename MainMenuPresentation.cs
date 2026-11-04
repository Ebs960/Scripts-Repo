using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class MainMenuPresentation : MonoBehaviour
{
    [SerializeField] private MainMenuManager menuManager;
    [Header("Artwork")]
    public Sprite MainMenuShellSprite;
    public Sprite GenericMenuButtonSprite;
    [SerializeField] private MainMenuBackgroundRotator backgroundRotator;
    [Header("Typography")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private string gameTitle = "CAVEMAN\nTO COSMOS";
    [SerializeField] private bool showVersion = true;

    private RectTransform root;
    private RectTransform shell;
    private RectTransform content;
    private VerticalLayoutGroup column;
    private TextMeshProUGUI title;
    private TextMeshProUGUI version;
    private readonly List<Button> buttons = new List<Button>();
    private GameObject lastSelected;
    private bool initialized;
    private bool building;
    private bool focusPending;

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (initialized || building) return;
        if (menuManager == null) menuManager = GetComponentInParent<MainMenuManager>();
        if (menuManager == null || menuManager.mainMenuPanel == null) return;
        building = true;
        root = menuManager.mainMenuPanel.transform as RectTransform;
        if (root == null) { building = false; return; }
        root.name = "MainMenuRoot";
        Stretch(root);
        ConfigureCanvas();

        var oldBackground = root.GetComponent<Image>();
        if (oldBackground != null)
        {
            oldBackground.sprite = null;
            oldBackground.overrideSprite = null;
            oldBackground.color = new Color(.025f, .04f, .065f, 1f);
            oldBackground.raycastTarget = false;
        }

        var background = CreateImage("BackgroundImage", root);
        Stretch(background.rectTransform);
        background.transform.SetAsFirstSibling();
        if (backgroundRotator == null) backgroundRotator = root.GetComponent<MainMenuBackgroundRotator>();
        if (backgroundRotator == null) backgroundRotator = root.gameObject.AddComponent<MainMenuBackgroundRotator>();
        backgroundRotator.Initialize(background);

        var shellImage = CreateImage("LeftMenuShell", root);
        shell = shellImage.rectTransform;
        shell.anchorMin = Vector2.zero;
        shell.anchorMax = new Vector2(.35f, 1f);
        shell.offsetMin = Vector2.zero;
        shell.offsetMax = Vector2.zero;
        ApplySprite(shellImage, MainMenuShellSprite, new Color(.035f, .055f, .09f, .96f));

        content = CreateRect("MenuContent", shell);
        content.anchorMin = new Vector2(.5f, .5f);
        content.anchorMax = content.anchorMin;
        content.pivot = content.anchorMin;
        content.anchoredPosition = Vector2.zero;
        column = content.gameObject.AddComponent<VerticalLayoutGroup>();
        column.childAlignment = TextAnchor.MiddleLeft;
        column.childControlWidth = true;
        column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;

        TMP_FontAsset labelFont = null;
        if (menuManager.newGameButton != null)
        {
            var oldLabel = menuManager.newGameButton.GetComponentInChildren<TMP_Text>(true);
            if (oldLabel != null) labelFont = oldLabel.font;
        }
        title = CreateText("GameTitle", content, titleFont != null ? titleFont : labelFont);
        title.text = gameTitle;
        title.fontSize = 48f;
        title.fontSizeMax = 48f;
        title.fontSizeMin = 22f;
        title.enableAutoSizing = true;
        title.color = new Color(.78f, .7f, .49f, 1f);
        SetHeight(title.gameObject, 144f);

        ConfigureButton(menuManager.newGameButton, "NewGameButton", "New Game", labelFont);
        ConfigureButton(menuManager.loadGameButton, "LoadGameButton", "Load Game", labelFont);
        ConfigureButton(menuManager.optionsButton, "OptionsButton", "Options", labelFont);
        ConfigureButton(menuManager.quitGameButton, "QuitGameButton", "Quit Game", labelFont);
        ConfigureNavigation();

        if (showVersion)
        {
            version = CreateText("VersionText", content, labelFont);
            version.text = "v" + Application.version;
            version.fontSize = 14f;
            version.color = new Color(.55f, .59f, .65f, 1f);
            SetHeight(version.gameObject, 24f);
        }
        initialized = true;
        building = false;
        ApplyResponsiveLayout();
        focusPending = true;
    }

    private void ConfigureCanvas()
    {
        var canvas = root.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;
    }

    private void ConfigureButton(Button button, string objectName, string labelText, TMP_FontAsset font)
    {
        if (button == null) return;
        buttons.Add(button);
        button.name = objectName;
        button.transform.SetParent(content, false);
        button.transform.localScale = Vector3.one;
        button.transform.localRotation = Quaternion.identity;
        var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        ApplySprite(image, GenericMenuButtonSprite, new Color(.065f, .09f, .135f, 1f));
        image.raycastTarget = true;
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = new Color(.85f, .87f, .9f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(.62f, .67f, .73f, 1f);
        colors.selectedColor = new Color(.95f, .92f, .84f, 1f);
        colors.disabledColor = new Color(.38f, .42f, .48f, .65f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = .12f;
        button.colors = colors;
        foreach (var effect in button.GetComponents<BaseMeshEffect>()) effect.enabled = false;
        foreach (var legacy in button.GetComponentsInChildren<Text>(true)) legacy.enabled = false;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null) label = CreateText("Label", button.transform, font);
        label.text = labelText;
        label.color = new Color(.89f, .87f, .8f, 1f);
        label.fontSize = 26f;
        label.fontSizeMax = 26f;
        label.fontSizeMin = 16f;
        label.enableAutoSizing = true;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = 0f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(24f, 6f);
        label.rectTransform.offsetMax = new Vector2(-24f, -6f);
        SetHeight(button.gameObject, 64f);
    }

    private void ConfigureNavigation()
    {
        for (int index = 0; index < buttons.Count; index++)
            buttons[index].navigation = new Navigation {
                mode = Navigation.Mode.Explicit,
                selectOnUp = buttons[(index + buttons.Count - 1) % buttons.Count],
                selectOnDown = buttons[(index + 1) % buttons.Count]
            };
    }

    private void OnEnable()
    {
        focusPending = true;
        ApplyResponsiveLayout();
    }

    private void OnDisable()
    {
        var events = EventSystem.current;
        if (events == null) return;
        foreach (var button in buttons)
            if (events.currentSelectedGameObject == button.gameObject) lastSelected = button.gameObject;
    }

    private void LateUpdate()
    {
        if (EventSystem.current != null)
            foreach (var button in buttons)
                if (EventSystem.current.currentSelectedGameObject == button.gameObject) lastSelected = button.gameObject;
        if (!focusPending || EventSystem.current == null || EventSystem.current.alreadySelecting) return;
        focusPending = false;
        foreach (var button in buttons)
            if (button.gameObject == lastSelected && button.IsActive() && button.IsInteractable())
            {
                EventSystem.current.SetSelectedGameObject(lastSelected);
                return;
            }
        foreach (var button in buttons)
            if (button.IsActive() && button.IsInteractable())
            {
                EventSystem.current.SetSelectedGameObject(button.gameObject);
                return;
            }
    }

    private void OnRectTransformDimensionsChange()
    {
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (!initialized || building || root == null) return;
        Vector2 size = root.rect.size;
        if (size.x <= 0f || size.y <= 0f) return;
        float shellWidth = Mathf.Min(size.x, Mathf.Max(320f, size.x * .35f));
        shell.anchorMax = new Vector2(shellWidth / size.x, 1f);
        float height = Mathf.Min(640f, Mathf.Max(0f, size.y - 64f));
        content.sizeDelta = new Vector2(Mathf.Min(480f, Mathf.Max(0f, shellWidth - 80f)), height);
        column.spacing = Mathf.Clamp(height * .025f, 8f, 18f);
        float titleHeight = Mathf.Clamp(height * .25f, 84f, 144f);
        SetHeight(title.gameObject, titleHeight);
        float versionHeight = version != null ? 24f : 0f;
        int gaps = buttons.Count + (version != null ? 1 : 0);
        float buttonHeight = Mathf.Clamp((height - titleHeight - versionHeight - gaps * column.spacing) / Mathf.Max(1, buttons.Count), 40f, 64f);
        foreach (var button in buttons) SetHeight(button.gameObject, buttonHeight);
    }

    private static void ApplySprite(Image image, Sprite sprite, Color emptyColor)
    {
        image.sprite = sprite;
        image.overrideSprite = null;
        image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        image.color = sprite != null ? Color.white : emptyColor;
        image.raycastTarget = false;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(string name, Transform parent)
    {
        var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset font)
    {
        var label = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = 0f;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private static void SetHeight(GameObject target, float height)
    {
        var element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
        element.ignoreLayout = false;
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}
