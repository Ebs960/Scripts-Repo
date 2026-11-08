using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class PauseMenuPresentation : MonoBehaviour
{
    private static readonly Color BackdropColor = new Color(0.01f, 0.018f, 0.027f, 0.78f);
    private static readonly Color ShellColor = new Color(0.043f, 0.075f, 0.11f, 1f);
    private static readonly Color Gold = new Color(0.72f, 0.52f, 0.21f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.91f, 0.86f, 1f);
    private static readonly Color DisabledColor = new Color(0.22f, 0.24f, 0.25f, 1f);

    private GameObject root;
    private TextMeshProUGUI status;
    private Button saveButton;
    private Button resumeButton;
    private MenuUiSkin skin;
    private PauseMenuManager manager;
    private bool pausePanelWasActive;

    public static PauseMenuPresentation Create(Transform parent, PauseMenuManager pauseManager, MenuUiSkin uiSkin)
    {
        var host = new GameObject("Pause Menu Presentation");
        host.transform.SetParent(parent, false);
        var presentation = host.AddComponent<PauseMenuPresentation>();
        presentation.manager = pauseManager;
        presentation.skin = uiSkin;
        presentation.Build();
        host.SetActive(false);
        return presentation;
    }

    public void Show(bool canSave)
    {
        if (root == null) return;
        if (manager != null && manager.pauseMenuPanel != null)
        {
            pausePanelWasActive = manager.pauseMenuPanel.activeSelf;
            manager.pauseMenuPanel.SetActive(false);
        }
        root.SetActive(true);
        saveButton.interactable = canSave;
        status.text = canSave ? string.Empty : "Saving is unavailable during tactical encounters.";
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(resumeButton != null ? resumeButton.gameObject : null);
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
        if (manager != null && manager.pauseMenuPanel != null && pausePanelWasActive)
            manager.pauseMenuPanel.SetActive(true);
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 850;
        gameObject.AddComponent<GraphicRaycaster>();
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        root = Rect("Backdrop", transform, Vector2.zero, Vector2.one);
        var backdrop = root.AddComponent<Image>();
        backdrop.color = BackdropColor;
        backdrop.raycastTarget = true;

        var shell = Rect("Pause Shell", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        shell.GetComponent<RectTransform>().sizeDelta = new Vector2(590, 790);
        var shellImage = shell.AddComponent<Image>();
        shellImage.color = ShellColor;
        ApplySprite(shellImage, skin != null ? skin.pauseMenuShell : null);
        var outline = shell.AddComponent<Outline>();
        outline.effectColor = Gold;
        outline.effectDistance = new Vector2(2, 2);
        outline.useGraphicAlpha = false;

        var header = Text("Header", shell.transform, "PAUSED", 42, TextColor, FontStyles.Bold,
            TextAlignmentOptions.Center);
        var headerRect = header.rectTransform;
        headerRect.anchorMin = new Vector2(0.05f, 0.82f);
        headerRect.anchorMax = new Vector2(0.95f, 0.96f);
        headerRect.offsetMin = Vector2.zero;
        headerRect.offsetMax = Vector2.zero;

        var list = Rect("Menu Buttons", shell.transform, new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.8f));
        var layout = list.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        resumeButton = AddButton(list.transform, "Resume Game", Gold, skin != null ? skin.genericButton : null, manager.ResumeGame);
        saveButton = AddButton(list.transform, "Save Game", new Color(0.12f, 0.18f, 0.23f, 1f),
            skin != null ? skin.genericButton : null, manager.ShowSaveMenu);
        AddButton(list.transform, "Load Game", new Color(0.12f, 0.18f, 0.23f, 1f),
            skin != null ? skin.genericButton : null, manager.ShowLoadMenu);
        AddButton(list.transform, "Options", new Color(0.12f, 0.18f, 0.23f, 1f),
            skin != null ? skin.genericButton : null, manager.ShowOptions);
        AddButton(list.transform, "Exit to Main Menu", new Color(0.12f, 0.18f, 0.23f, 1f),
            skin != null ? skin.genericButton : null, manager.ExitToMainMenu);
        AddButton(list.transform, "Exit to Desktop", new Color(0.12f, 0.18f, 0.23f, 1f),
            skin != null ? skin.genericButton : null, manager.ExitGame);

        status = Text("Save Restriction", shell.transform, "", 18, DisabledColor, FontStyles.Normal,
            TextAlignmentOptions.Center);
        status.rectTransform.anchorMin = new Vector2(0.07f, 0.035f);
        status.rectTransform.anchorMax = new Vector2(0.93f, 0.1f);
        status.rectTransform.offsetMin = Vector2.zero;
        status.rectTransform.offsetMax = Vector2.zero;
    }

    private Button AddButton(Transform parent, string label, Color color, Sprite sprite, UnityEngine.Events.UnityAction action)
    {
        var go = Rect(label, parent, Vector2.zero, Vector2.one);
        go.AddComponent<LayoutElement>().preferredHeight = 70;
        var image = go.AddComponent<Image>();
        image.color = color;
        ApplySprite(image, sprite);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        button.onClick.AddListener(action);
        var text = Text("Label", go.transform, label, 23, TextColor, FontStyles.Bold, TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    private static GameObject Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    private TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color,
        FontStyles style, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = name == "Header" ? MenuUiTypography.Heading(skin) : MenuUiTypography.Body(skin);
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private static void ApplySprite(Image image, Sprite sprite)
    {
        if (image == null || sprite == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
    }
}
