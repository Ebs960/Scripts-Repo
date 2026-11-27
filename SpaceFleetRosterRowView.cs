using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One row of the fleet roster. Displays a live unit; it never stores a copy of its data.</summary>
public class SpaceFleetRosterRowView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI iconFallbackText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI classText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject selectedHighlight;

    public int UnitId { get; private set; }
    public Button Button => button;

    public void Bind(int unitId, BaseUnit unit, bool selected)
    {
        UnitId = unitId;
        bool available = SpaceMapSelectionInfo.IsAlive(unit);
        string unitName = SpaceMapSelectionInfo.UnitName(unit);

        Sprite sprite = SpaceMapSelectionInfo.UnitIcon(unit);
        if (icon != null) { icon.sprite = sprite; icon.enabled = sprite != null; }
        if (iconFallbackText != null)
        {
            iconFallbackText.gameObject.SetActive(sprite == null);
            iconFallbackText.text = unit != null && unitName.Length > 0 ? unitName.Substring(0, 1).ToUpperInvariant() : "?";
        }
        if (nameText != null) nameText.text = unitName;
        if (classText != null) classText.text = unit != null ? SpaceMapSelectionInfo.UnitClass(unit) : string.Empty;
        if (healthText != null) healthText.text = unit != null ? $"HP {Mathf.Max(0, unit.currentHealth)}/{unit.MaxHealth}" : string.Empty;
        if (statusText != null) statusText.text = SpaceMapSelectionInfo.UnitStatus(unit);
        if (selectedHighlight != null) selectedHighlight.SetActive(selected && available);
        if (button != null) button.interactable = available;
    }
}
