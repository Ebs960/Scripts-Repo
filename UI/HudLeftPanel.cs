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
    [SerializeField] private HudScienceProgress scienceProgress;
    [SerializeField] private HudCultureProgress cultureProgress;

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

        if (scienceProgress != null)
            scienceProgress.Bind(civ);

        if (cultureProgress != null)
            cultureProgress.Bind(civ);
    }

    private void Awake()
    {
        if (scienceProgress == null)
            scienceProgress = GetComponentInChildren<HudScienceProgress>(true);

        if (cultureProgress == null)
            cultureProgress = GetComponentInChildren<HudCultureProgress>(true);
    }
}
