// Assets/Scripts/UI/HudLeftPanel.cs
using UnityEngine;

/// <summary>
/// Left-side HUD panel housing science and culture progress widgets.
/// Displays:
/// - Tech progress (% toward next tech)
/// - Culture progress (% toward next culture/era)
/// - Breakdown popovers on hover
/// </summary>
public class HudLeftPanel : MonoBehaviour
{
    [SerializeField] private GameObject scienceProgressPrefab;
    [SerializeField] private GameObject cultureProgressPrefab;

    private GameObject scienceInstance;
    private GameObject cultureInstance;

    /// <summary>
    /// Bind this panel to a civilization and populate displays.
    /// </summary>
    public void Bind(Civilization civ)
    {
        if (civ == null)
        {
            Debug.LogWarning("HudLeftPanel.Bind: Civilization is null");
            return;
        }

        // Instantiate science widget
        if (scienceProgressPrefab != null)
        {
            if (scienceInstance != null)
                Destroy(scienceInstance);

            scienceInstance = Instantiate(scienceProgressPrefab, transform);
            scienceInstance.name = "ScienceProgress_Instance";

            var scienceWidget = scienceInstance.GetComponent<HudScienceProgress>();
            if (scienceWidget != null)
                scienceWidget.Bind(civ);
        }

        // Instantiate culture widget
        if (cultureProgressPrefab != null)
        {
            if (cultureInstance != null)
                Destroy(cultureInstance);

            cultureInstance = Instantiate(cultureProgressPrefab, transform);
            cultureInstance.name = "CultureProgress_Instance";

            var cultureWidget = cultureInstance.GetComponent<HudCultureProgress>();
            if (cultureWidget != null)
                cultureWidget.Bind(civ);
        }
    }
}
