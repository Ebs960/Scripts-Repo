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

/// <summary>
/// The single authored confirmation dialog for adopt government/policy, repeal, remove councillor, release vassal and
/// politically significant vassal changes. If no line-row prefab is assigned, lines are appended to the description.
/// </summary>
public class PoliticalConfirmDialog : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Transform linesRoot;
    [SerializeField] private PoliticalLineRowUI lineRowPrefab;
    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text confirmLabel;
    [SerializeField] private Button cancelButton;

    private readonly List<PoliticalLineRowUI> pool = new List<PoliticalLineRowUI>();
    private PoliticalConfirmRequest pending;

    private GameObject Root => root != null ? root : gameObject;
    public bool IsVisible => Root.activeSelf;

    public void Show(PoliticalConfirmRequest request)
    {
        if (request == null) return;
        pending = request;

        string description = request.description;
        bool usesRows = linesRoot != null && lineRowPrefab != null;
        if (!usesRows && request.lines != null && request.lines.Count > 0)
        {
            var extra = new List<string>();
            foreach (var line in request.lines)
                extra.Add(string.IsNullOrEmpty(line.value) ? line.label : $"{line.label}: {line.value}");
            description = string.IsNullOrEmpty(description) ? string.Join("\n", extra) : description + "\n\n" + string.Join("\n", extra);
        }

        GovernmentUiUtil.SetText(titleText, request.title);
        GovernmentUiUtil.SetText(descriptionText, description);
        GovernmentUiUtil.SetText(confirmLabel, request.confirmLabel);
        GovernmentUiUtil.SetImage(iconImage, request.icon);
        GovernmentUiUtil.FillList(linesRoot, lineRowPrefab, pool,
            usesRows ? (IReadOnlyList<PoliticalEffectLine>)request.lines : Array.Empty<PoliticalEffectLine>(),
            (row, line) => row.BindEffect(line));
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
