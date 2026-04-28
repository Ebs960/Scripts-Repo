using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HudVassalSummaryRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI civNameText;
    [SerializeField] private TextMeshProUGUI relationText;
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private Image iconImage;

    public void Populate(Civilization civ, DiplomaticState relation)
    {
        if (civNameText != null)
            civNameText.text = civ?.civData?.civName ?? civ?.name ?? "Unknown Civilization";

        if (relationText != null)
            relationText.text = relation.ToString();

        if (detailText != null)
            detailText.text = relation == DiplomaticState.Protected ? "Protected Subject" : "Vassal State";

        if (iconImage != null)
        {
            var icon = civ?.civData?.icon;
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }
    }
}
