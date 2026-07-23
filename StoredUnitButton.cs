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

        if (unitNameText != null)
        {
            string name = "Unit";
            var cu = unit as CombatUnit;
            var wu = unit as WorkerUnit;
            if (cu != null && cu.data != null) name = cu.data.unitName;
            else if (wu != null && wu.data != null) name = wu.data.unitName;
            unitNameText.text = name;
        }

        if (unitIconImage != null)
        {
            Sprite s = null;
            var cu = unit as CombatUnit;
            var wu = unit as WorkerUnit;
            if (cu != null && cu.data != null) s = cu.data.GetIcon(cu.owner);
            else if (wu != null && wu.data != null) s = wu.data.GetIcon(wu.owner);
            unitIconImage.sprite = s;
            unitIconImage.gameObject.SetActive(s != null);
        }

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
