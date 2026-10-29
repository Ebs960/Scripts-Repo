using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class DiplomacyConfirmationDialog : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text confirmLabelText;
    [SerializeField] private TMP_Text cancelLabelText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private Action onConfirm;
    private Action onCancel;
    private GameObject previousSelection;
    private bool isResolving;

    private GameObject Root => root != null ? root : gameObject;
    public bool IsVisible => Root.activeInHierarchy;

    private void Awake()
    {
        if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
        if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        Root.SetActive(false);
    }

    private void Update()
    {
        if (!IsVisible || Keyboard.current == null || !Keyboard.current[Key.Escape].wasPressedThisFrame) return;
        Cancel();
    }

    public void Show(string title, string description, string confirmLabel, string cancelLabel,
        Action confirm, Action cancel = null)
    {
        if (confirm == null || isResolving) return;
        onConfirm = confirm;
        onCancel = cancel;
        if (titleText != null) titleText.text = title ?? string.Empty;
        if (descriptionText != null) descriptionText.text = description ?? string.Empty;
        if (confirmLabelText != null) confirmLabelText.text = string.IsNullOrWhiteSpace(confirmLabel) ? "Confirm" : confirmLabel;
        if (cancelLabelText != null) cancelLabelText.text = string.IsNullOrWhiteSpace(cancelLabel) ? "Cancel" : cancelLabel;
        previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        Root.SetActive(true);
        if (EventSystem.current != null && confirmButton != null)
            EventSystem.current.SetSelectedGameObject(confirmButton.gameObject);
    }

    public void Confirm()
    {
        if (!IsVisible || isResolving || onConfirm == null) return;
        var callback = onConfirm;
        isResolving = true;
        ClearAndClose();
        callback.Invoke();
        isResolving = false;
    }

    public void Cancel()
    {
        if (!IsVisible || isResolving) return;
        var callback = onCancel;
        isResolving = true;
        ClearAndClose();
        callback?.Invoke();
        isResolving = false;
    }

    public void HideWithoutCallback()
    {
        if (!Root.activeSelf || isResolving) return;
        isResolving = true;
        ClearAndClose();
        isResolving = false;
    }

    private void ClearAndClose()
    {
        onConfirm = null;
        onCancel = null;
        Root.SetActive(false);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection = null;
    }
}