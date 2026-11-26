using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class ConfirmationDialogUI : MonoBehaviour
{
    [SerializeField] private Image shell;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private TMP_Text cancelText;
    [SerializeField] private RectTransform messageButtonPosition;

    private ConfirmationDialogRequest request;
    private bool resolved;
    public bool IsVisible => !resolved && gameObject.activeInHierarchy;
    internal bool IsConfigured => shell != null && titleText != null && descriptionText != null &&
        confirmButton != null && cancelButton != null && confirmText != null && cancelText != null &&
        messageButtonPosition != null;

    internal void Show(ConfirmationDialogRequest dialogRequest)
    {
        request = dialogRequest;
        titleText.text = request.title ?? "CONFIRM";
        descriptionText.text = request.description ?? string.Empty;
        confirmText.text = request.confirmLabel;
        cancelText.text = request.cancelLabel;
        cancelButton.gameObject.SetActive(!request.messageOnly);
        if (request.messageOnly && confirmButton.transform is RectTransform rect)
            rect.anchoredPosition = messageButtonPosition.anchoredPosition;

        ApplySkin(request.skin);
        if (request.destructive)
            confirmButton.targetGraphic.color = new Color(1f, 0.68f, 0.52f);

        confirmButton.onClick.AddListener(Confirm);
        cancelButton.onClick.AddListener(Cancel);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(request.messageOnly ? confirmButton.gameObject : cancelButton.gameObject);
    }

    private void ApplySkin(MenuUiSkin skin)
    {
        if (skin == null) return;
        ApplySprite(shell, skin.confirmationDialogShell);
        ApplySprite(confirmButton.targetGraphic as Image, skin.confirmButton);
        ApplySprite(cancelButton.targetGraphic as Image, skin.cancelButton);
        if (skin.headingFont != null) titleText.font = skin.headingFont;
        if (skin.bodyFont != null)
        {
            descriptionText.font = skin.bodyFont;
            confirmText.font = skin.bodyFont;
            cancelText.font = skin.bodyFont;
        }
    }

    private static void ApplySprite(Image image, Sprite sprite)
    {
        if (image == null || sprite == null) return;
        image.sprite = sprite;
        image.type = sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
    }

    private void Update()
    {
        if (resolved) return;
        if (OwnerWasDestroyed())
        {
            Dismiss();
            return;
        }

        if (Keyboard.current != null && Keyboard.current[Key.Escape].wasPressedThisFrame)
            Cancel();
    }

    private void Confirm() => Resolve(request?.onConfirm);
    private void Cancel() => Resolve(request?.onCancel);
    public void Dismiss() => Resolve(null);

    private bool OwnerWasDestroyed()
    {
        return request != null && !ReferenceEquals(request.owner, null) && request.owner == null;
    }

    private void Resolve(Action callback)
    {
        if (resolved) return;
        resolved = true;
        if (OwnerWasDestroyed()) callback = null;
        request = null;
        ConfirmationDialogService.Clear(this);
        gameObject.SetActive(false);
        Destroy(gameObject);
        callback?.Invoke();
    }

    private void OnDestroy() => ConfirmationDialogService.Clear(this);
}
