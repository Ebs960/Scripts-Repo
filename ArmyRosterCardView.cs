using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Prefab-facing card binder. Visual styling belongs in the authored prefab.</summary>
public sealed class ArmyRosterCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text unitName;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject selectedIndicator;
    [SerializeField] private GameObject workerIndicator;
    private BaseUnit boundUnit;

    public void Bind(BaseUnit unit, bool worker, bool selected, Action clicked)
    {
        boundUnit = unit;
        if (unitName != null) unitName.text = unit != null ? unit.UnitName : string.Empty;
        if (statusText != null && unit != null)
            statusText.text = $"{unit.currentHealth}/{unit.MaxHealth} HP  {unit.currentMovePoints}/{unit.GetTurnMovementBudget()} MP";
        if (selectedIndicator != null) selectedIndicator.SetActive(selected);
        if (workerIndicator != null) workerIndicator.SetActive(worker);
        if (icon != null)
        {
            icon.sprite = unit is CombatUnit combat && combat.data != null ? combat.data.GetIcon(combat.owner)
                : unit is WorkerUnit civilian && civilian.data != null ? civilian.data.GetIcon(civilian.owner) : null;
            icon.enabled = icon.sprite != null;
        }
        if (button != null) { button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => clicked?.Invoke()); }
    }

    public void Clear()
    {
        boundUnit = null;
        if (button != null) button.onClick.RemoveAllListeners();
        if (icon != null) { icon.sprite = null; icon.enabled = false; }
        if (unitName != null) unitName.text = string.Empty;
        if (statusText != null) statusText.text = string.Empty;
        if (selectedIndicator != null) selectedIndicator.SetActive(false);
        if (workerIndicator != null) workerIndicator.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (boundUnit == null || TooltipSystem.Instance == null) return;
        string role = boundUnit is CombatUnit combat
            ? combat.data != null ? combat.data.unitType.ToString() : "Combat unit"
            : boundUnit is WorkerUnit worker && worker.data != null ? worker.data.unitName : "Support unit";
        int level = boundUnit is CombatUnit combatUnit ? combatUnit.level
            : boundUnit is WorkerUnit workerUnit ? workerUnit.level : 0;
        int experience = boundUnit is CombatUnit experiencedCombat ? experiencedCombat.experience
            : boundUnit is WorkerUnit experiencedWorker ? experiencedWorker.experience : 0;
        TooltipSystem.Instance.ShowSimpleTooltip(boundUnit.UnitName,
            $"{role}\nHealth: {boundUnit.currentHealth}/{boundUnit.MaxHealth}\n" +
            $"Movement: {boundUnit.currentMovePoints}/{boundUnit.GetTurnMovementBudget()}\nLevel: {level}  XP: {experience}");
    }

    public void OnPointerExit(PointerEventData eventData)
        => TooltipSystem.Instance?.RequestHideTooltip();

    private void OnDisable() => TooltipSystem.Instance?.RequestHideTooltip();
}
