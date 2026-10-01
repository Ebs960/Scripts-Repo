using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Prefab-facing card binder. Visual styling belongs in the authored prefab.</summary>
public sealed class ArmyRosterCardView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text unitName;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject selectedIndicator;
    [SerializeField] private GameObject workerIndicator;

    public void Bind(BaseUnit unit, bool worker, bool selected, Action clicked)
    {
        if (unitName != null) unitName.text = unit != null ? unit.UnitName : string.Empty;
        if (statusText != null && unit != null) statusText.text = $"HP {unit.currentHealth}/{unit.MaxHealth}  MP {unit.currentMovePoints}";
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
}
