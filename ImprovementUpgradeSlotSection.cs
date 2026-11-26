using TMPro;
using UnityEngine;

/// <summary>
/// Prefab-facing container for all improvement options assigned to one upgrade slot. The heading and the
/// option list are saved in the section prefab; its layout group sizes the section to its rows.
/// </summary>
public class ImprovementUpgradeSlotSection : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI slotNameText;
    [SerializeField] private TextMeshProUGUI slotStatusText;
    [SerializeField] private Transform optionContainer;

    public Transform OptionContainer => optionContainer != null ? optionContainer : transform;

    private void Awake()
    {
        if (optionContainer == null)
            Debug.LogError("[ImprovementUpgradeSlotSection] The section prefab has no option container assigned; rows will be added to the section itself.", this);
    }

    public void Bind(string slotName, int installedCount, int optionCount)
    {
        if (slotNameText != null) slotNameText.text = string.IsNullOrEmpty(slotName) ? string.Empty : slotName.ToUpperInvariant();
        if (slotStatusText != null)
            slotStatusText.text = installedCount > 0
                ? $"{installedCount} installed • {optionCount} option{(optionCount == 1 ? "" : "s")}"
                : $"Empty • {optionCount} option{(optionCount == 1 ? "" : "s")}";
    }
}