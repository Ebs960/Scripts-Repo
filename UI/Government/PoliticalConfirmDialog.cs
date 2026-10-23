using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Everything the shared political confirmation dialog needs to show; actions run only on Confirm.</summary>
public class PoliticalConfirmRequest
{
    public string title;
    public string description;
    public string confirmLabel = "Confirm";
    public Sprite icon;
    public List<PoliticalEffectLine> lines = new List<PoliticalEffectLine>();
    public Action onConfirm;
    public Action onCancel;
}

/// <summary>The shared Yes/No political confirmation; the prompt is authored artwork. Actions run only on Confirm.</summary>
public class PoliticalConfirmDialog : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private PoliticalConfirmRequest pending;

    private GameObject Root => root != null ? root : gameObject;
    public bool IsVisible => Root.activeSelf;

    public void Show(PoliticalConfirmRequest request)
    {
        if (request == null) return;
        pending = request;

        GovernmentUiUtil.SetClick(confirmButton, Confirm);
        GovernmentUiUtil.SetClick(cancelButton, Cancel);
        Root.SetActive(true);
    }

    public void Confirm()
    {
        var request = pending;
        Hide();
        request?.onConfirm?.Invoke();
    }

    public void Cancel()
    {
        var request = pending;
        Hide();
        request?.onCancel?.Invoke();
    }

    public void Hide()
    {
        pending = null;
        if (Root != null) Root.SetActive(false);
    }
}
