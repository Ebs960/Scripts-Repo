// Assets/Scripts/UI/HudBottomBar.cs
using UnityEngine;

/// <summary>
/// Bottom bar HUD widget consolidating:
/// - UnitInfoPanel (selected unit stats, build actions, fortify, stacking)
/// - MinimapUI (world minimap with fog of war)
/// 
/// Preserves all existing logic from original components.
/// </summary>
public class HudBottomBar : MonoBehaviour
{
    [SerializeField] private GameObject unitInfoPanelPrefab;
    [SerializeField] private GameObject minimapPrefab;

    private GameObject unitInfoInstance;
    private GameObject minimapInstance;

    /// <summary>
    /// Bind this panel to a civilization and populate displays.
    /// </summary>
    public void Bind(Civilization civ)
    {
        if (civ == null)
        {
            Debug.LogWarning("HudBottomBar.Bind: Civilization is null");
            return;
        }

        // Instantiate unit info panel
        if (unitInfoPanelPrefab != null)
        {
            if (unitInfoInstance != null)
                Destroy(unitInfoInstance);

            unitInfoInstance = Instantiate(unitInfoPanelPrefab, transform);
            unitInfoInstance.name = "UnitInfoPanel_Instance";
            // UnitInfoPanel will self-initialize via its own Start/Awake
        }

        // Instantiate minimap
        if (minimapPrefab != null)
        {
            if (minimapInstance != null)
                Destroy(minimapInstance);

            minimapInstance = Instantiate(minimapPrefab, transform);
            minimapInstance.name = "Minimap_Instance";
            // MinimapUI will self-initialize
        }
    }
}
