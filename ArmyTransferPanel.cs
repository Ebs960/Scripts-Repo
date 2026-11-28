using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored popup that edits a temporary ArmyTransferService plan until Confirm.</summary>
public sealed class ArmyTransferPanel : MonoBehaviour
{
    [SerializeField] private GameObject rootPanel;
    [SerializeField] private Transform leftRosterContent;
    [SerializeField] private Transform rightRosterContent;
    [SerializeField] private TMP_Text leftSummary;
    [SerializeField] private TMP_Text rightSummary;
    [SerializeField] private TMP_Text validationMessage;
    [SerializeField] private ArmyRosterCardView combatUnitCardPrefab;
    [SerializeField] private ArmyRosterCardView workerUnitCardPrefab;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private readonly List<GameObject> cards = new();
    private ArmyTransferService.TransferPlan plan;
    private bool runtimePanel;

    private void Awake()
    {
        confirmButton?.onClick.AddListener(Confirm);
        cancelButton?.onClick.AddListener(Cancel);
        if (rootPanel != null) rootPanel.SetActive(false);
    }
    private void OnEnable() => ArmyTransferService.TransferRequested += Open;
    private void OnDisable()
    {
        ArmyTransferService.TransferRequested -= Open;
        Cancel();
    }

    private void OnDestroy()
    {
        if (runtimePanel && rootPanel != null) Destroy(rootPanel);
    }

    public void Open(CombatUnit left, CombatUnit right)
    {
        if (!ArmyTransferService.CanTransfer(left, right, out string reason))
        { UIManager.Instance?.ShowNotification(reason); return; }
        if (rootPanel == null && !BuildRuntimePanel())
        {
            UIManager.Instance?.ShowNotification("Army transfer panel could not be opened; see the console.");
            return;
        }
        plan = ArmyTransferService.TransferPlan.Capture(left, right);
        rootPanel.SetActive(true);
        rootPanel.transform.SetAsLastSibling();
        Refresh();
    }

    private bool BuildRuntimePanel()
    {
        var assets = ManagementUIAssets.Load();
        var hud = UIManager.Instance != null ? UIManager.Instance.gameplayHudRoot : null;
        var canvas = hud != null ? hud.GetComponentInParent<Canvas>() : null;
        if (canvas == null || assets == null || assets.armyTransferShell == null || assets.armyRosterCard == null)
        {
            Debug.LogError("[ArmyTransferPanel] A gameplay canvas, transfer shell, and roster card are required.", this);
            return false;
        }

        combatUnitCardPrefab = assets.armyRosterCard;
        workerUnitCardPrefab = assets.armyRosterCard;
        rootPanel = new GameObject("ArmyTransferBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        runtimePanel = true;
        rootPanel.transform.SetParent(canvas.transform, false);
        Stretch(rootPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        rootPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

        var shell = new GameObject("ArmyTransferShell", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        shell.transform.SetParent(rootPanel.transform, false);
        var shellRect = shell.GetComponent<RectTransform>();
        shellRect.anchorMin = shellRect.anchorMax = new Vector2(0.5f, 0.5f);
        shellRect.sizeDelta = new Vector2(900f, 560f);
        var shellImage = shell.GetComponent<Image>();
        shellImage.sprite = assets.armyTransferShell;
        shellImage.color = Color.white;

        CreateText("Title", shell.transform, "ARMY TRANSFER", 22f,
            new Vector2(0.25f, 0.86f), new Vector2(0.75f, 0.97f));
        leftSummary = CreateText("LeftSummary", shell.transform, "", 15f,
            new Vector2(0.08f, 0.75f), new Vector2(0.47f, 0.84f));
        rightSummary = CreateText("RightSummary", shell.transform, "", 15f,
            new Vector2(0.53f, 0.75f), new Vector2(0.92f, 0.84f));
        leftRosterContent = CreateRoster("LeftRoster", shell.transform,
            new Vector2(0.07f, 0.23f), new Vector2(0.48f, 0.72f));
        rightRosterContent = CreateRoster("RightRoster", shell.transform,
            new Vector2(0.52f, 0.23f), new Vector2(0.93f, 0.72f));
        validationMessage = CreateText("Validation", shell.transform, "", 14f,
            new Vector2(0.08f, 0.07f), new Vector2(0.60f, 0.19f));
        confirmButton = CreateButton("Confirm", shell.transform, "CONFIRM",
            new Vector2(0.64f, 0.085f), new Vector2(0.77f, 0.17f));
        cancelButton = CreateButton("Cancel", shell.transform, "CANCEL",
            new Vector2(0.80f, 0.085f), new Vector2(0.93f, 0.17f));
        confirmButton.onClick.AddListener(Confirm);
        cancelButton.onClick.AddListener(Cancel);
        rootPanel.SetActive(false);
        return true;
    }

    private static Transform CreateRoster(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var viewport = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        viewport.transform.SetParent(parent, false);
        Stretch(viewport.GetComponent<RectTransform>(), min, max);
        viewport.GetComponent<Image>().color = Color.clear;
        var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var rect = content.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        var grid = content.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(116f, 70f);
        grid.spacing = new Vector2(5f, 5f);
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.GetComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = rect;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30f;
        return content.transform;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, float size, Vector2 min, Vector2 max)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        Stretch(obj.GetComponent<RectTransform>(), min, max);
        var text = obj.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(string name, Transform parent, string label, Vector2 min, Vector2 max)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        Stretch(obj.GetComponent<RectTransform>(), min, max);
        obj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        CreateText("Label", obj.transform, label, 15f, Vector2.zero, Vector2.one);
        return obj.GetComponent<Button>();
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public void MoveCombatToLeft(CombatUnit unit) { if (plan == null || unit == null) return; plan.RightCombat.Remove(unit); if (!plan.LeftCombat.Contains(unit)) plan.LeftCombat.Add(unit); Refresh(); }
    public void MoveCombatToRight(CombatUnit unit) { if (plan == null || unit == null) return; plan.LeftCombat.Remove(unit); if (!plan.RightCombat.Contains(unit)) plan.RightCombat.Add(unit); Refresh(); }
    public void MoveWorkerToLeft(WorkerUnit unit) { if (plan == null || unit == null) return; plan.RightWorkers.Remove(unit); if (!plan.LeftWorkers.Contains(unit)) plan.LeftWorkers.Add(unit); Refresh(); }
    public void MoveWorkerToRight(WorkerUnit unit) { if (plan == null || unit == null) return; plan.LeftWorkers.Remove(unit); if (!plan.RightWorkers.Contains(unit)) plan.RightWorkers.Add(unit); Refresh(); }

    private void Refresh()
    {
        foreach (var card in cards)
        {
            if (card == null) continue;
            card.SetActive(false);
            Destroy(card);
        }
        cards.Clear();
        if (plan == null) return;
        int capacity = plan.LeftArmy.owner.GetMaxArmySize();
        if (leftSummary != null) leftSummary.text = $"Combat: {plan.LeftCombat.Count} / {capacity}\nWorkers: {plan.LeftWorkers.Count}";
        if (rightSummary != null) rightSummary.text = $"Combat: {plan.RightCombat.Count} / {capacity}\nWorkers: {plan.RightWorkers.Count}";
        AddSide(plan.LeftCombat, plan.LeftWorkers, leftRosterContent, true);
        AddSide(plan.RightCombat, plan.RightWorkers, rightRosterContent, false);
        bool valid = ArmyTransferService.ValidateTransferPlan(plan, out string reason);
        if (confirmButton != null) confirmButton.interactable = valid;
        if (validationMessage != null) validationMessage.text = valid ? string.Empty : reason;
    }

    private void AddSide(IReadOnlyList<CombatUnit> combat, IReadOnlyList<WorkerUnit> workers, Transform parent, bool left)
    {
        if (parent == null) return;
        foreach (var unit in combat)
        {
            if (combatUnitCardPrefab == null || unit == null) continue;
            CombatUnit captured = unit;
            var card = Instantiate(combatUnitCardPrefab, parent); cards.Add(card.gameObject);
            card.Bind(unit, false, false, () => { if (left) MoveCombatToRight(captured); else MoveCombatToLeft(captured); });
        }
        foreach (var worker in workers)
        {
            if (workerUnitCardPrefab == null || worker == null) continue;
            WorkerUnit captured = worker;
            var card = Instantiate(workerUnitCardPrefab, parent); cards.Add(card.gameObject);
            card.Bind(worker, true, false, () => { if (left) MoveWorkerToRight(captured); else MoveWorkerToLeft(captured); });
        }
    }

    private void Confirm()
    {
        if (!ArmyTransferService.ApplyTransferPlan(plan, out string reason))
        { if (validationMessage != null) validationMessage.text = reason; UIManager.Instance?.ShowNotification(reason); return; }
        foreach (var hud in FindObjectsByType<CampaignArmyPanel>()) hud.Refresh();
        Cancel();
    }

    private void Cancel()
    {
        plan = null;
        if (rootPanel != null) rootPanel.SetActive(false);
    }
}
