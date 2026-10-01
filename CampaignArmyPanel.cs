using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Prefab-driven bottom HUD for a selected campaign army.</summary>
public sealed class CampaignArmyPanel : MonoBehaviour
{
    [Header("Authored Army HUD")]
    [SerializeField] private GameObject rootPanel;
    [SerializeField] private TMP_Text armyNameText;
    [SerializeField] private TMP_InputField renameInput;
    [SerializeField] private Button renamePenButton;
    [SerializeField] private Button renameConfirmButton;
    [SerializeField] private TMP_Text combatCapacityText;
    [SerializeField] private TMP_Text workerCountText;
    [SerializeField] private Transform commanderArea;
    [SerializeField] private Transform rosterContent;
    [SerializeField] private ArmyRosterCardView combatUnitCardPrefab;
    [SerializeField] private ArmyRosterCardView workerUnitCardPrefab;
    [SerializeField] private Button defenseModeButton;
    [SerializeField] private GameObject defenseModeActiveIndicator;
    [SerializeField] private Button buildButton;
    [SerializeField] private ArmyRosterSelection rosterSelection;

    private readonly List<GameObject> cards = new();
    private CombatUnit selectedArmy;
    private string nameBeforeEdit;

    public static CampaignArmyPanel GetOrCreate(Component host)
    {
        if (host == null) return null;
        var existing = host.GetComponent<CampaignArmyPanel>();
        return existing != null ? existing : host.gameObject.AddComponent<CampaignArmyPanel>();
    }

    private void Awake()
    {
        if (rosterSelection == null) rosterSelection = GetComponent<ArmyRosterSelection>();
        if (rosterSelection == null) rosterSelection = gameObject.AddComponent<ArmyRosterSelection>();
        renamePenButton?.onClick.AddListener(BeginRename);
        renameConfirmButton?.onClick.AddListener(CommitRename);
        renameInput?.onSubmit.AddListener(_ => CommitRename());
        defenseModeButton?.onClick.AddListener(ToggleDefenseMode);
        buildButton?.onClick.AddListener(OpenArmyBuildMenu);
        SetRenameMode(false);
    }

    private void Update()
    {
        if (renameInput == null || !renameInput.gameObject.activeSelf || Keyboard.current == null) return;
        if (Keyboard.current.escapeKey.wasPressedThisFrame) CancelRename();
    }

    private void OnDisable() { rosterSelection?.Clear(); }

    public void Show(CombatUnit unit)
    {
        selectedArmy = CampaignArmyService.GetRepresentative(unit);
        rosterSelection.SetFormation(selectedArmy);
        if (rootPanel == null)
        {
            Debug.LogWarning("[CampaignArmyPanel] Assign the authored Army HUD root and card prefabs in the Inspector.");
            return;
        }
        rootPanel.SetActive(selectedArmy != null);
        Refresh();
    }

    public void Hide()
    {
        selectedArmy = null;
        rosterSelection?.SetFormation(null);
        if (rootPanel != null) rootPanel.SetActive(false);
    }

    public void ClearRosterSelection()
    {
        rosterSelection?.Clear();
        Refresh();
    }

    public void Refresh()
    {
        selectedArmy = CampaignArmyService.GetRepresentative(selectedArmy);
        if (selectedArmy == null) { Hide(); return; }
        var combat = CampaignArmyService.GetMembers(selectedArmy);
        var workers = CampaignArmyService.GetArmyWorkers(selectedArmy);
        string armyName = string.IsNullOrWhiteSpace(selectedArmy.MilitaryFormationName)
            ? selectedArmy.MilitaryFormationType.ToString() : selectedArmy.MilitaryFormationName;
        if (armyNameText != null) armyNameText.text = armyName;
        if (combatCapacityText != null) combatCapacityText.text = $"Combat: {combat.Count} / {selectedArmy.owner.GetMaxArmySize()}";
        if (workerCountText != null) workerCountText.text = $"Workers: {workers.Count} support units";
        if (buildButton != null) buildButton.interactable = workers.Count > 0;
        bool defended = CampaignArmyService.IsInDefenseMode(selectedArmy);
        if (defenseModeButton != null) defenseModeButton.interactable = combat.Count > 0;
        if (defenseModeActiveIndicator != null) defenseModeActiveIndicator.SetActive(defended);
        RebuildCards(combat, workers);
    }

    private void RebuildCards(IReadOnlyList<CombatUnit> combat, IReadOnlyList<WorkerUnit> workers)
    {
        foreach (var card in cards) if (card != null) Destroy(card);
        cards.Clear();
        if (rosterContent == null) return;
        foreach (var unit in combat)
        {
            if (unit == null || combatUnitCardPrefab == null) continue;
            CombatUnit captured = unit;
            var view = Instantiate(combatUnitCardPrefab, rosterContent);
            cards.Add(view.gameObject);
            view.Bind(unit, false, rosterSelection.IsSelected(unit), () => OnCombatCardClicked(captured));
        }
        foreach (var worker in workers)
        {
            if (worker == null || workerUnitCardPrefab == null) continue;
            WorkerUnit captured = worker;
            var view = Instantiate(workerUnitCardPrefab, rosterContent);
            cards.Add(view.gameObject);
            view.Bind(worker, true, rosterSelection.IsSelected(worker), () => OnWorkerCardClicked(captured));
        }
    }

    private static bool ShiftHeld => Keyboard.current != null
        && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    private void OnCombatCardClicked(CombatUnit unit) { if (ShiftHeld) rosterSelection.Toggle(unit); else rosterSelection.SelectExclusive(unit); Refresh(); }
    private void OnWorkerCardClicked(WorkerUnit unit) { if (ShiftHeld) rosterSelection.Toggle(unit); else rosterSelection.SelectExclusive(unit); Refresh(); }

    private void ToggleDefenseMode()
    {
        if (CampaignArmyService.IsInDefenseMode(selectedArmy)) CampaignArmyService.ExitDefenseMode(selectedArmy);
        else CampaignArmyService.EnterDefenseMode(selectedArmy);
        Refresh();
    }

    private void BeginRename()
    {
        if (selectedArmy == null || renameInput == null) return;
        nameBeforeEdit = armyNameText != null ? armyNameText.text : selectedArmy.MilitaryFormationName;
        renameInput.characterLimit = 32;
        renameInput.SetTextWithoutNotify(nameBeforeEdit);
        SetRenameMode(true);
        renameInput.Select(); renameInput.ActivateInputField();
    }

    private void CommitRename()
    {
        if (selectedArmy == null || renameInput == null) return;
        CampaignArmyService.RenameArmy(selectedArmy, renameInput.text);
        SetRenameMode(false); Refresh();
    }

    private void CancelRename()
    {
        if (renameInput != null) renameInput.SetTextWithoutNotify(nameBeforeEdit);
        SetRenameMode(false); Refresh();
    }

    private void SetRenameMode(bool editing)
    {
        if (renameInput != null) renameInput.gameObject.SetActive(editing);
        if (renameConfirmButton != null) renameConfirmButton.gameObject.SetActive(editing);
        if (armyNameText != null) armyNameText.gameObject.SetActive(!editing);
        if (renamePenButton != null) renamePenButton.gameObject.SetActive(!editing);
    }

    public void OpenArmyBuildMenu()
    {
        if (!CampaignArmyService.HasAttachedWorker(selectedArmy)) return;
        UIManager.Instance?.ShowNotification("Army construction menu is not implemented yet.");
    }
}
