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

    private void Awake()
    {
        confirmButton?.onClick.AddListener(Confirm);
        cancelButton?.onClick.AddListener(Cancel);
        if (rootPanel != null) rootPanel.SetActive(false);
    }
    private void OnEnable() => ArmyTransferService.TransferRequested += Open;
    private void OnDisable() => ArmyTransferService.TransferRequested -= Open;

    public void Open(CombatUnit left, CombatUnit right)
    {
        if (!ArmyTransferService.CanTransfer(left, right, out string reason))
        { UIManager.Instance?.ShowNotification(reason); return; }
        plan = ArmyTransferService.TransferPlan.Capture(left, right);
        if (rootPanel == null)
        {
            Debug.LogWarning("[ArmyTransferPanel] Assign the authored transfer popup references in the Inspector.");
            UIManager.Instance?.ShowNotification("Army transfer panel has not been assigned.");
            return;
        }
        rootPanel.SetActive(true);
        Refresh();
    }

    public void MoveCombatToLeft(CombatUnit unit) { if (plan == null || unit == null) return; plan.RightCombat.Remove(unit); if (!plan.LeftCombat.Contains(unit)) plan.LeftCombat.Add(unit); Refresh(); }
    public void MoveCombatToRight(CombatUnit unit) { if (plan == null || unit == null) return; plan.LeftCombat.Remove(unit); if (!plan.RightCombat.Contains(unit)) plan.RightCombat.Add(unit); Refresh(); }
    public void MoveWorkerToLeft(WorkerUnit unit) { if (plan == null || unit == null) return; plan.RightWorkers.Remove(unit); if (!plan.LeftWorkers.Contains(unit)) plan.LeftWorkers.Add(unit); Refresh(); }
    public void MoveWorkerToRight(WorkerUnit unit) { if (plan == null || unit == null) return; plan.LeftWorkers.Remove(unit); if (!plan.RightWorkers.Contains(unit)) plan.RightWorkers.Add(unit); Refresh(); }

    private void Refresh()
    {
        foreach (var card in cards) if (card != null) Destroy(card);
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
        foreach (var hud in FindObjectsByType<CampaignArmyPanel>(FindObjectsSortMode.None)) hud.Refresh();
        Cancel();
    }

    private void Cancel()
    {
        plan = null;
        if (rootPanel != null) rootPanel.SetActive(false);
    }
}
