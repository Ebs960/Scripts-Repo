// Assets/Scripts/UI/HudBreakdownItem.cs
using UnityEngine;
using TMPro;

/// <summary>
/// Single row in a breakdown popover, showing:
/// - Source name (e.g., "City: Capital", "Units Consumption")
/// - Amount
/// - Category label
/// </summary>
public class HudBreakdownItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI sourceText;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private TextMeshProUGUI categoryText;

    [SerializeField] private Color positiveColor = Color.green;
    [SerializeField] private Color negativeColor = Color.red;
    [SerializeField] private Color neutralColor = Color.white;

    public void Populate(HudBreakdownService.BreakdownItem item)
    {
        if (sourceText != null)
            sourceText.text = item.source;

        if (amountText != null)
        {
            amountText.text = (item.amount >= 0 ? "+" : "") + item.amount.ToString("N0");
            amountText.color = item.amount > 0 ? positiveColor : (item.amount < 0 ? negativeColor : neutralColor);
        }

        if (categoryText != null)
            categoryText.gameObject.SetActive(false);
    }
}
