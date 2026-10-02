using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// DEPRECATED: runtime-generated Government/Policy lists and confirmation dialog.
// Emergency fallback only, used while the Government prefab has no authored tab shell wired.
// The production path is the prefab-driven tab shell in GovernmentPanel.cs.
public partial class GovernmentPanel
{
    [Header("Legacy runtime UI (deprecated, used only when no tab shell is wired)")]
    public Transform governmentsContentRoot;
    public TextMeshProUGUI governmentsHeaderText;
    public Transform policiesContentRoot;
    public TextMeshProUGUI policiesHeaderText;
    public GameObject confirmDialogRoot;
    public TextMeshProUGUI confirmMessageText;
    public Button confirmOkButton;
    public Button confirmCancelButton;
    public Transform confirmEffectsContainer;
    public Image confirmIconImage;

    private readonly List<GameObject> legacySpawned = new List<GameObject>();
    private GameObject legacyConfirmRoot;
    private GameObject legacyCloseButton;

    private bool IsLegacyConfirmVisible => legacyConfirmRoot != null && legacyConfirmRoot.activeSelf;

    private void EnsureLegacyCloseButton()
    {
        if (closeButton != null || legacyCloseButton != null || panelRoot == null) return;
        legacyCloseButton = new GameObject("CloseButton", typeof(RectTransform), typeof(Button), typeof(TextMeshProUGUI));
        legacyCloseButton.transform.SetParent(panelRoot.transform, false);
        var text = legacyCloseButton.GetComponent<TextMeshProUGUI>();
        text.text = "X";
        text.fontSize = 20;
        text.color = Color.white;
        var rect = legacyCloseButton.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-10f, -10f);
        rect.sizeDelta = new Vector2(30f, 30f);
        legacyCloseButton.GetComponent<Button>().onClick.AddListener(Close);
        UIManager.Instance?.WireUIInteractions(legacyCloseButton);
    }

    private void RefreshLegacyLists()
    {
        ClearLegacySpawned();
        EnsureLegacyCloseButton();
        if (civ == null || PolicyManager.Instance == null) return;

        if (governmentsHeaderText != null)
        {
            governmentsHeaderText.text = "Available Governments";
            governmentsHeaderText.gameObject.SetActive(true);
        }
        if (policiesHeaderText != null)
        {
            policiesHeaderText.text = "Available Policies";
            policiesHeaderText.gameObject.SetActive(true);
        }

        if (governmentsContentRoot != null)
        {
            foreach (var government in PolicyManager.Instance.GetUnlockedGovernments(civ))
            {
                var g = government;
                var evaluation = PolicyManager.Instance.EvaluateGovernment(civ, g);
                var row = NewLegacyRow(governmentsContentRoot, "Government_" + GovernmentPresentation.NameOf(g));
                LegacyText(row.transform, GovernmentPresentation.NameOf(g) + "\n" + g.description);
                LegacyButton(row.transform, "Adopt", evaluation.canAdopt, () => RequestConfirmation(new PoliticalConfirmRequest
                {
                    title = $"Adopt {GovernmentPresentation.NameOf(g)}?",
                    description = $"Cost: {g.policyPointCost} policy points.",
                    icon = g.icon,
                    confirmLabel = "Adopt",
                    lines = PoliticalEffectSummaryBuilder.BuildGovernmentEffects(g),
                    onConfirm = () => { PolicyManager.Instance.ChangeGovernment(civ, g); RefreshAllVisible(); },
                }));
            }
        }

        if (policiesContentRoot != null)
        {
            foreach (var policy in PolicyManager.Instance.GetAvailablePolicies(civ))
            {
                var p = policy;
                var row = NewLegacyRow(policiesContentRoot, "Policy_" + GovernmentPresentation.NameOf(p));
                LegacyText(row.transform, GovernmentPresentation.NameOf(p) + "\n" + p.description);
                LegacyButton(row.transform, "Adopt", true, () => RequestConfirmation(new PoliticalConfirmRequest
                {
                    title = $"Adopt {GovernmentPresentation.NameOf(p)}?",
                    description = $"Cost: {p.policyPointCost} policy points.\n{p.description}",
                    confirmLabel = "Adopt",
                    lines = PoliticalEffectSummaryBuilder.BuildPolicyEffects(p),
                    onConfirm = () => { PolicyManager.Instance.AdoptPolicy(civ, p); RefreshAllVisible(); },
                }));
            }
        }
    }

    private GameObject NewLegacyRow(Transform parent, string rowName)
    {
        var row = new GameObject(rowName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        legacySpawned.Add(row);
        return row;
    }

    private static void LegacyText(Transform parent, string text)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 18;
        label.color = Color.white;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 60f);
    }

    private static void LegacyButton(Transform parent, string label, bool interactable, UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Button), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16;
        text.color = interactable ? Color.black : Color.gray;
        text.alignment = TextAlignmentOptions.Center;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 30f);
        var button = go.GetComponent<Button>();
        button.interactable = interactable;
        button.onClick.AddListener(onClick);
    }

    private void ClearLegacySpawned()
    {
        for (int i = legacySpawned.Count - 1; i >= 0; i--)
            if (legacySpawned[i] != null) Destroy(legacySpawned[i]);
        legacySpawned.Clear();
        if (governmentsHeaderText != null) governmentsHeaderText.gameObject.SetActive(false);
        if (policiesHeaderText != null) policiesHeaderText.gameObject.SetActive(false);
    }

    private void ShowLegacyConfirmation(PoliticalConfirmRequest request)
    {
        EnsureLegacyConfirmDialog();
        if (legacyConfirmRoot == null) return;

        if (confirmMessageText != null)
        {
            var lines = request.lines.Select(l => string.IsNullOrEmpty(l.value) ? l.label : $"{l.label}: {l.value}");
            confirmMessageText.text = request.title + "\n" + request.description + "\n" + string.Join("\n", lines);
        }
        if (confirmIconImage != null)
        {
            confirmIconImage.sprite = request.icon;
            confirmIconImage.gameObject.SetActive(request.icon != null);
        }
        if (confirmOkButton != null)
        {
            confirmOkButton.onClick.RemoveAllListeners();
            confirmOkButton.onClick.AddListener(() => { HideLegacyConfirmation(); request.onConfirm?.Invoke(); });
        }
        if (confirmCancelButton != null)
        {
            confirmCancelButton.onClick.RemoveAllListeners();
            confirmCancelButton.onClick.AddListener(() => { HideLegacyConfirmation(); request.onCancel?.Invoke(); });
        }
        legacyConfirmRoot.SetActive(true);
        UIManager.Instance?.WireUIInteractions(legacyConfirmRoot);
    }

    private void CancelLegacyConfirmation() => confirmCancelButton?.onClick.Invoke();

    private void HideLegacyConfirmation()
    {
        if (legacyConfirmRoot != null) legacyConfirmRoot.SetActive(false);
    }

    private void EnsureLegacyConfirmDialog()
    {
        if (legacyConfirmRoot != null) return;
        if (confirmDialogRoot != null) { legacyConfirmRoot = confirmDialogRoot; return; }

        // Prefer the dialog already authored in the prefab: the lowest ancestor shared by its message and OK button.
        if (confirmMessageText != null && confirmOkButton != null)
        {
            for (var t = confirmOkButton.transform.parent; t != null; t = t.parent)
            {
                if (t == transform || (panelRoot != null && t == panelRoot.transform)) break;
                if (confirmMessageText.transform.IsChildOf(t)) { legacyConfirmRoot = t.gameObject; return; }
            }
        }

        if (panelRoot == null) return;
        legacyConfirmRoot = new GameObject("ConfirmDialog", typeof(RectTransform), typeof(Image));
        legacyConfirmRoot.transform.SetParent(panelRoot.transform, false);
        var rect = legacyConfirmRoot.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420f, 260f);
        legacyConfirmRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        var message = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        message.transform.SetParent(legacyConfirmRoot.transform, false);
        confirmMessageText = message.GetComponent<TextMeshProUGUI>();
        confirmMessageText.fontSize = 16;
        confirmMessageText.alignment = TextAlignmentOptions.Center;
        var messageRect = confirmMessageText.rectTransform;
        messageRect.anchorMin = new Vector2(0f, 0.3f);
        messageRect.anchorMax = Vector2.one;
        messageRect.offsetMin = new Vector2(8f, 8f);
        messageRect.offsetMax = new Vector2(-8f, -8f);

        confirmOkButton = NewLegacyDialogButton(legacyConfirmRoot.transform, "Confirm", 0f);
        confirmCancelButton = NewLegacyDialogButton(legacyConfirmRoot.transform, "Cancel", 0.5f);
        legacyConfirmRoot.SetActive(false);
    }

    private static Button NewLegacyDialogButton(Transform parent, string label, float anchorX)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Button), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(anchorX, 0f);
        rect.anchorMax = new Vector2(anchorX + 0.5f, 0.28f);
        rect.offsetMin = new Vector2(8f, 8f);
        rect.offsetMax = new Vector2(-8f, -8f);
        return go.GetComponent<Button>();
    }
}
