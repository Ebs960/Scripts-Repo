using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HudGovernorSummaryRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI citiesText;
    [SerializeField] private TextMeshProUGUI loyaltyText;
    [SerializeField] private TextMeshProUGUI traitsText;
    [SerializeField] private Image iconImage;

    public void Populate(Governor governor)
    {
        Populate(governor, null);
    }

    public void Populate(Governor governor, Civilization civ)
    {
        if (governor == null)
        {
            if (nameText != null) nameText.text = "Governor";
            if (citiesText != null) citiesText.text = "Cities: —";
            if (loyaltyText != null) loyaltyText.text = "Loyalty: —";
            if (traitsText != null) traitsText.text = string.Empty;
            if (iconImage != null) iconImage.enabled = false;
            return;
        }

        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(governor.Name) ? "Governor" : governor.Name;

        if (citiesText != null)
            citiesText.text = BuildCitySummary(governor, civ);

        if (loyaltyText != null)
            loyaltyText.text = $"Loyalty: {governor.Opinion:0}";

        if (traitsText != null)
            traitsText.text = BuildTraitSummary(governor);

        if (iconImage != null)
            iconImage.enabled = false;
    }

    private static string BuildCitySummary(Governor governor, Civilization civ)
    {
        var names = new List<string>();

        if (governor.Cities != null)
        {
            foreach (var city in governor.Cities)
            {
                if (city == null) continue;
                names.Add(!string.IsNullOrWhiteSpace(city.cityName) ? city.cityName : city.name);
            }
        }

        if (names.Count == 0 && civ != null && civ.cities != null)
        {
            foreach (var city in civ.cities)
            {
                if (city == null || city.governor != governor)
                    continue;

                names.Add(!string.IsNullOrWhiteSpace(city.cityName) ? city.cityName : city.name);
            }
        }

        return names.Count > 0
            ? $"Cities: {string.Join(", ", names)}"
            : "Cities: None";
    }

    private static string BuildTraitSummary(Governor governor)
    {
        var names = new List<string>();

        if (governor.Traits != null)
        {
            foreach (var trait in governor.Traits)
            {
                if (trait == null) continue;
                names.Add(!string.IsNullOrWhiteSpace(trait.traitName) ? trait.traitName : trait.name);
            }
        }

        return names.Count > 0
            ? $"Traits: {string.Join(", ", names)}"
            : string.Empty;
    }
}
