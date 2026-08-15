using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Button prefab behavior for stored-unit buttons in the ImprovementUpgradeUI.
/// Shows icon + optional name and invokes unstoring when clicked.
/// </summary>
public class StoredUnitButton : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI unitNameText;
    [SerializeField] private Image unitIconImage;
    [SerializeField] private Button buttonComponent;

    private BaseUnit representedUnit;
    private ImprovementInstance ownerInstance;

    public void Setup(BaseUnit unit, ImprovementInstance instance)
    {
        representedUnit = unit;
        ownerInstance = instance;

        UnitUIPresenter.Bind(unit, unitNameText, unitIconImage);

        if (buttonComponent != null)
        {
            buttonComponent.onClick.RemoveAllListeners();
            buttonComponent.onClick.AddListener(() => {
                if (ownerInstance != null && representedUnit != null)
                {
                    bool ok = ownerInstance.TryUnstoreUnit(representedUnit);
                    if (ok)
                    {
                        // Refresh parent UI if present
                        var ui = GetComponentInParent<ImprovementUpgradeUI>();
                        if (ui != null)
                        {
                            ui.RefreshStoredUnits(ownerInstance);
                        }
                        else
                        {
                            gameObject.SetActive(false);
                        }
                    }
                }
            });
        }
    }
}

/// <summary>Shared name and icon binding for UI controls that represent a unit.</summary>
public static class UnitUIPresenter
{
    public static void Bind(BaseUnit unit, TextMeshProUGUI nameText, Image iconImage)
    {
        string displayName = "Unit";
        Sprite icon = null;
        if (unit is CombatUnit combat && combat.data != null)
        {
            displayName = combat.data.unitName;
            icon = combat.data.GetIcon(combat.owner);
        }
        else if (unit is WorkerUnit worker && worker.data != null)
        {
            displayName = worker.data.unitName;
            icon = worker.data.GetIcon(worker.owner);
        }

        if (nameText != null) nameText.text = displayName;
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }
    }
}
