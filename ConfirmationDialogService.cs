using System;
using UnityEngine;

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
    private const string PrefabPath = "MenuUI/Confirmation Dialog";
    private static ConfirmationDialogUI activeDialog;

    public static bool IsVisible => activeDialog != null && activeDialog.IsVisible;

    public static bool ShowConfirmation(string title, string message, Action onConfirm, Action onCancel = null,
        string confirmLabel = "CONFIRM", string cancelLabel = "CANCEL", bool destructive = false,
        UnityEngine.Object owner = null, MenuUiSkin skin = null)
    {
        return Open(new ConfirmationDialogRequest
        {
            title = title, description = message,
            confirmLabel = confirmLabel, cancelLabel = cancelLabel, destructive = destructive,
            owner = owner, skin = skin, onConfirm = onConfirm, onCancel = onCancel
        });
    }

    public static bool ShowMessage(string title, string message, UnityEngine.Object owner = null, MenuUiSkin skin = null)
    {
        return Open(new ConfirmationDialogRequest
        {
            title = title, description = message,
            confirmLabel = "OK", messageOnly = true, owner = owner, skin = skin
        });
    }

    private static bool Open(ConfirmationDialogRequest request)
    {
        if (IsVisible) return false;

        var prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null || !prefab.TryGetComponent<ConfirmationDialogUI>(out var view) || !view.IsConfigured)
        {
            Debug.LogError($"Missing or misconfigured confirmation dialog prefab at Resources/{PrefabPath}.");
            return false;
        }

        activeDialog = UnityEngine.Object.Instantiate(view);
        activeDialog.Show(request);
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
