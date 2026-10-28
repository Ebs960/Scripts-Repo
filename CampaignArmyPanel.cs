using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Prefab-driven bottom HUD for a selected campaign army.</summary>
public sealed class CampaignArmyPanel : MonoBehaviour
{
    public static CampaignArmyPanel Active { get; private set; }

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
    [SerializeField] private TMP_Text selectedCountText;

    private readonly List<ArmyRosterCardView> cards = new();
    private CombatUnit selectedArmy;
    private Action<BaseUnit> onInspectedUnitChanged;
    private string nameBeforeEdit;
    private bool isEditingName;

    public static CampaignArmyPanel GetOrCreate(Component host)
    {
        if (host == null) return null;
        return host.GetComponent<CampaignArmyPanel>()
            ?? host.GetComponentInChildren<CampaignArmyPanel>(true);
    }

    private void Awake()
    {
        Active = this;
        if (rosterSelection == null) rosterSelection = GetComponent<ArmyRosterSelection>();
        if (combatCapacityText == null)
            combatCapacityText = GetComponentInParent<UnitInfoPanel>()?.StackInfoText;
        renamePenButton?.onClick.AddListener(ToggleRename);
        renameConfirmButton?.onClick.AddListener(CommitRename);
        renameInput?.onSubmit.AddListener(_ => CommitRename());
        defenseModeButton?.onClick.AddListener(ToggleDefenseMode);
        buildButton?.onClick.AddListener(OpenArmyBuildMenu);
        SetRenameMode(false);
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    private void Update()
    {
        if (renameInput == null || !isEditingName || Keyboard.current == null) return;
        if (Keyboard.current.escapeKey.wasPressedThisFrame) CancelRename();
    }


    public void Show(CombatUnit unit)
    {
        selectedArmy = CampaignArmyService.GetRepresentative(unit);
        if (selectedArmy == null) { Hide(); return; }
        rosterSelection?.SetFormation(selectedArmy);
        if (rootPanel == null)
        {
            Debug.LogWarning("[CampaignArmyPanel] Assign the authored Army HUD root and card prefabs in the Inspector.");
            return;
        }
        rootPanel.SetActive(selectedArmy != null);
        if (renamePenButton != null && !isEditingName) renamePenButton.gameObject.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        selectedArmy = null;
        onInspectedUnitChanged = null;
        rosterSelection?.SetFormation(null);
        if (isEditingName)
        {
            renameInput?.SetTextWithoutNotify(nameBeforeEdit);
            SetRenameMode(false);
        }
        if (renamePenButton != null) renamePenButton.gameObject.SetActive(false);
        if (renameConfirmButton != null) renameConfirmButton.gameObject.SetActive(false);
        if (rootPanel != null) rootPanel.SetActive(false);
        ClearCards();
    }

    public void SetInspector(Action<BaseUnit> inspectedUnitChanged)
        => onInspectedUnitChanged = inspectedUnitChanged;

    public void Inspect(BaseUnit unit)
    {
        if (unit == null || selectedArmy == null) return;
        onInspectedUnitChanged?.Invoke(unit);
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
        rosterSelection?.Prune(combat, workers);
        string armyName = string.IsNullOrWhiteSpace(selectedArmy.MilitaryFormationName)
            ? selectedArmy.MilitaryFormationType.ToString() : selectedArmy.MilitaryFormationName;
        if (armyNameText != null && !isEditingName)
        {
            string formationType = selectedArmy.MilitaryFormationType.ToString();
            armyNameText.text = string.IsNullOrWhiteSpace(selectedArmy.MilitaryFormationName)
                ? formationType : $"{armyName}  |  {formationType}";
        }
        int selectedCount = rosterSelection != null
            ? rosterSelection.SelectedCombatUnits.Count + rosterSelection.SelectedWorkers.Count : 0;
        if (combatCapacityText != null)
        {
            combatCapacityText.gameObject.SetActive(true);
            combatCapacityText.text = $"Army {combat.Count}/{selectedArmy.owner.GetMaxArmySize()}" +
                (workers.Count > 0 ? $"  |  W {workers.Count}" : string.Empty) +
                (selectedCount > 0 ? $"  |  {selectedCount} selected" : string.Empty);
        }
        if (workerCountText != null) workerCountText.text = $"Workers: {workers.Count} support units";
        if (buildButton != null) buildButton.gameObject.SetActive(false);
        bool defended = CampaignArmyService.IsInDefenseMode(selectedArmy);
        if (defenseModeButton != null) defenseModeButton.interactable = combat.Count > 0;
        if (defenseModeActiveIndicator != null) defenseModeActiveIndicator.SetActive(defended);
        if (selectedCountText != null)
        {
            selectedCountText.text = selectedCount > 0 ? $"{selectedCount} selected" : string.Empty;
            selectedCountText.gameObject.SetActive(selectedCount > 0);
        }
        RebuildCards(combat, workers);
    }

    private void RebuildCards(IReadOnlyList<CombatUnit> combat, IReadOnlyList<WorkerUnit> workers)
    {
        if (rosterContent == null) return;
        int index = 0;
        foreach (var unit in combat)
        {
            if (unit == null || combatUnitCardPrefab == null) continue;
            CombatUnit captured = unit;
            var view = GetCard(index++, combatUnitCardPrefab);
            view.Bind(unit, false, rosterSelection.IsSelected(unit), () => OnCombatCardClicked(captured));
        }
        foreach (var worker in workers)
        {
            if (worker == null || workerUnitCardPrefab == null) continue;
            WorkerUnit captured = worker;
            var view = GetCard(index++, workerUnitCardPrefab);
            view.Bind(worker, true, rosterSelection.IsSelected(worker), () => OnWorkerCardClicked(captured));
        }
        for (int i = index; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            cards[i].Clear();
            cards[i].gameObject.SetActive(false);
        }
    }

    private ArmyRosterCardView GetCard(int index, ArmyRosterCardView prefab)
    {
        while (cards.Count <= index)
            cards.Add(Instantiate(prefab, rosterContent));
        var card = cards[index];
        if (card == null) card = cards[index] = Instantiate(prefab, rosterContent);
        card.gameObject.SetActive(true);
        return card;
    }

    private void ClearCards()
    {
        foreach (var card in cards)
        {
            if (card == null) continue;
            card.Clear();
            card.gameObject.SetActive(false);
        }
    }

    private static bool ShiftHeld => Keyboard.current != null
        && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    private void OnCombatCardClicked(CombatUnit unit) { if (ShiftHeld) rosterSelection.Toggle(unit); else rosterSelection.SelectExclusive(unit); Inspect(unit); Refresh(); }
    private void OnWorkerCardClicked(WorkerUnit unit) { if (ShiftHeld) rosterSelection.Toggle(unit); else rosterSelection.SelectExclusive(unit); Inspect(unit); Refresh(); }

    private void ToggleDefenseMode()
    {
        if (CampaignArmyService.IsInDefenseMode(selectedArmy)) CampaignArmyService.ExitDefenseMode(selectedArmy);
        else CampaignArmyService.EnterDefenseMode(selectedArmy);
        Refresh();
    }

    public void ToggleDefenseFromUnitInfo() => ToggleDefenseMode();

    private void BeginRename()
    {
        if (selectedArmy == null || renameInput == null) return;
        nameBeforeEdit = string.IsNullOrWhiteSpace(selectedArmy.MilitaryFormationName)
            ? selectedArmy.MilitaryFormationType.ToString() : selectedArmy.MilitaryFormationName;
        renameInput.characterLimit = 32;
        renameInput.SetTextWithoutNotify(nameBeforeEdit);
        SetRenameMode(true);
        renameInput.Select(); renameInput.ActivateInputField();
    }

    private void ToggleRename()
    {
        if (isEditingName) CommitRename();
        else BeginRename();
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
        isEditingName = editing;
        bool sharedNameObject = renameInput != null && armyNameText != null
            && renameInput.gameObject == armyNameText.gameObject;
        if (sharedNameObject)
        {
            renameInput.enabled = editing;
            armyNameText.enabled = true;
        }
        else if (renameInput != null) renameInput.gameObject.SetActive(editing);
        if (renameConfirmButton != null) renameConfirmButton.gameObject.SetActive(editing);
        if (armyNameText != null && !sharedNameObject) armyNameText.gameObject.SetActive(!editing);
        if (renamePenButton != null)
        {
            renamePenButton.gameObject.SetActive(!editing || renameConfirmButton == null);
            var label = renamePenButton.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = editing ? "Save" : "Edit";
        }
    }

    public void OpenArmyBuildMenu()
    {
        if (!CampaignArmyService.HasAttachedWorker(selectedArmy)) return;
        UIManager.Instance?.ShowNotification("Army construction menu is not implemented yet.");
    }
}
