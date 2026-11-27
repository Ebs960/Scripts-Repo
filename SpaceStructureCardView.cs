using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One space structure in the infrastructure panel, read from a live SpaceImprovementInstance.</summary>
public class SpaceStructureCardView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI iconFallbackText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private TextMeshProUGUI statusText;

    public void Bind(SpaceImprovementInstance instance)
    {
        var data = instance != null ? SpaceImprovementRegistry.GetData(instance.improvementDataId) : null;
        string structureName = SpaceMapSelectionInfo.ImprovementName(instance, data);

        Sprite sprite = data != null ? data.icon : null;
        if (icon != null) { icon.sprite = sprite; icon.enabled = sprite != null; }
        if (iconFallbackText != null)
        {
            iconFallbackText.gameObject.SetActive(sprite == null);
            iconFallbackText.text = structureName.Substring(0, 1).ToUpperInvariant();
        }
        if (nameText != null) nameText.text = structureName;
        if (statusText != null) statusText.text = SpaceMapSelectionInfo.ImprovementStatus(instance);
        if (detailText == null || instance == null) return;

        string owner = SpaceMapSelectionInfo.CivilizationName(SpaceMapSelectionInfo.ResolveCivilization(instance.ownerCivilizationId));
        string health = data != null ? $"{Mathf.Max(0, instance.currentHealth)}/{data.maximumHealth}" : Mathf.Max(0, instance.currentHealth).ToString();
        string detail = $"{(data != null ? SpaceMapSelectionInfo.Nicify(data.improvementType) : "Structure")}, {owner}\nHP {health}";
        if (data != null && data.shipStorageCapacity > 0) detail += $", ships {instance.storedShips}/{data.shipStorageCapacity}";
        if (data != null && data.cargoStorageCapacity > 0) detail += $", cargo {instance.storedCargo}/{data.cargoStorageCapacity}";
        detailText.text = detail;
    }
}
