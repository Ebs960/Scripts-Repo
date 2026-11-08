using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class ConfirmationDialogRequest
{
    public string title;
    public string description;
    public string confirmLabel = "CONFIRM";
    public string cancelLabel = "CANCEL";
    public bool destructive;
    public bool messageOnly;
    public UnityEngine.Object owner;
    public MenuUiSkin skin;
    public Action onConfirm;
    public Action onCancel;
}

public static class ConfirmationDialogService
{
    private static ConfirmationDialogUI activeDialog;

    public static bool IsVisible => activeDialog != null && activeDialog.IsVisible;

    public static bool ShowConfirmation(string title, string message, Action onConfirm, Action onCancel = null,
        string confirmLabel = "CONFIRM", string cancelLabel = "CANCEL", bool destructive = false,
        UnityEngine.Object owner = null, MenuUiSkin skin = null)
    {
        if (IsVisible)
            return false;

        var host = new GameObject("Confirmation Dialog");
        activeDialog = host.AddComponent<ConfirmationDialogUI>();
        activeDialog.Show(new ConfirmationDialogRequest
        {
            title = title,
            description = message,
            confirmLabel = confirmLabel,
            cancelLabel = cancelLabel,
            destructive = destructive,
            owner = owner,
            skin = skin,
            onConfirm = onConfirm,
            onCancel = onCancel
        });
        return true;
    }

    public static bool ShowMessage(string title, string message, UnityEngine.Object owner = null, MenuUiSkin skin = null)
    {
        if (IsVisible)
            return false;

        var host = new GameObject("Information Dialog");
        activeDialog = host.AddComponent<ConfirmationDialogUI>();
        activeDialog.Show(new ConfirmationDialogRequest
        {
            title = title,
            description = message,
            confirmLabel = "OK",
            messageOnly = true,
            owner = owner,
            skin = skin
        });
        return true;
    }

    public static void DismissActive()
    {
        if (activeDialog != null)
            activeDialog.Dismiss();
    }

    internal static void Clear(ConfirmationDialogUI dialog)
    {
        if (activeDialog == dialog)
            activeDialog = null;
    }
}

public sealed class ConfirmationDialogUI : MonoBehaviour
{
    private const int SortingOrder = 1100;
    private static readonly Color BackdropColor = new Color(0.015f, 0.023f, 0.035f, 0.82f);
    private static readonly Color ShellColor = new Color(0.051f, 0.086f, 0.125f, 1f);
    private static readonly Color Gold = new Color(0.72f, 0.52f, 0.21f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.91f, 0.86f, 1f);
    private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.79f, 1f);

    private ConfirmationDialogRequest request;
    private GameObject root;
    private bool resolved;
    public bool IsVisible => root != null && root.activeSelf;

    public void Show(ConfirmationDialogRequest dialogRequest)
    {
        if (dialogRequest == null)
        {
            Destroy(gameObject);
            return;
        }

        request = dialogRequest;
        Build();
        root.SetActive(true);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(request.messageOnly ? confirmButton.gameObject : cancelButton.gameObject);
    }

    private Button confirmButton;
    private Button cancelButton;

    private void Update()
    {
        if (resolved) return;
        if (OwnerWasDestroyed())
        {
            CloseWithoutCallback();
            return;
        }

        if (Keyboard.current != null && Keyboard.current[Key.Escape].wasPressedThisFrame)
            Cancel();
    }

    public void Confirm()
    {
        if (resolved) return;
        resolved = true;
        Action callback = OwnerWasDestroyed() ? null : request?.onConfirm;
        Close();
        callback?.Invoke();
    }

    public void Cancel()
    {
        if (resolved) return;
        resolved = true;
        Action callback = OwnerWasDestroyed() ? null : request?.onCancel;
        Close();
        callback?.Invoke();
    }

    private void CloseWithoutCallback()
    {
        if (resolved) return;
        resolved = true;
        Close();
    }

    public void Dismiss()
    {
        CloseWithoutCallback();
    }

    private bool OwnerWasDestroyed()
    {
        return request != null && !ReferenceEquals(request.owner, null) && request.owner == null;
    }

    private void Close()
    {
        request = null;
        ConfirmationDialogService.Clear(this);
        Destroy(gameObject);
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;
        gameObject.AddComponent<GraphicRaycaster>();
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        root = Rect("Backdrop", transform, Vector2.zero, Vector2.one);
        var backdrop = root.AddComponent<Image>();
        backdrop.color = BackdropColor;
        backdrop.raycastTarget = true;

        var shell = Rect("Dialog", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        shell.GetComponent<RectTransform>().sizeDelta = new Vector2(740, 410);
        var shellImage = shell.AddComponent<Image>();
        shellImage.color = ShellColor;
        if (request.skin != null && request.skin.confirmationDialogShell != null)
        {
            shellImage.sprite = request.skin.confirmationDialogShell;
            shellImage.type = Image.Type.Sliced;
        }

        var outline = shell.AddComponent<Outline>();
        outline.effectColor = Gold;
        outline.effectDistance = new Vector2(2, 2);
        outline.useGraphicAlpha = false;

        Text("Title", shell.transform, request.title ?? "CONFIRM", 38, TextColor,
            new Vector2(0.08f, 0.68f), new Vector2(0.92f, 0.9f), FontStyles.Bold, TextAlignmentOptions.Center);
        Text("Description", shell.transform, request.description ?? "", 24, MutedText,
            new Vector2(0.09f, 0.38f), new Vector2(0.91f, 0.66f), FontStyles.Normal, TextAlignmentOptions.Center);

        confirmButton = Button("Confirm", shell.transform, request.confirmLabel, request.destructive
            ? new Color(0.48f, 0.31f, 0.19f, 1f)
            : Gold, request.skin != null ? request.skin.confirmButton : null, Confirm);
        var confirmRect = confirmButton.GetComponent<RectTransform>();
        confirmRect.anchorMin = request.messageOnly ? new Vector2(0.31f, 0.12f) : new Vector2(0.53f, 0.12f);
        confirmRect.anchorMax = request.messageOnly ? new Vector2(0.69f, 0.29f) : new Vector2(0.91f, 0.29f);
        confirmRect.offsetMin = Vector2.zero;
        confirmRect.offsetMax = Vector2.zero;
        if (!request.messageOnly)
        {
            cancelButton = Button("Cancel", shell.transform, request.cancelLabel, new Color(0.08f, 0.13f, 0.19f, 1f),
                request.skin != null ? request.skin.cancelButton : null, Cancel);
            var cancelRect = cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = new Vector2(0.09f, 0.12f);
            cancelRect.anchorMax = new Vector2(0.47f, 0.29f);
            cancelRect.offsetMin = Vector2.zero;
            cancelRect.offsetMax = Vector2.zero;
        }
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

    private static TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color,
        Vector2 anchorMin, Vector2 anchorMax, FontStyles style, TextAlignmentOptions alignment)
    {
        var go = Rect(name, parent, anchorMin, anchorMax);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = name == "Title" ? MenuUiTypography.Heading() : MenuUiTypography.Body();
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private static Button Button(string name, Transform parent, string label, Color color, Sprite sprite, Action action)
    {
        var go = Rect(name, parent, Vector2.zero, Vector2.zero);
        var image = go.AddComponent<Image>();
        image.color = color;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        button.onClick.AddListener(() => action());
        Text("Label", go.transform, label, 23, TextColor, Vector2.zero, Vector2.one, FontStyles.Bold,
            TextAlignmentOptions.Center);
        return button;
    }
}
