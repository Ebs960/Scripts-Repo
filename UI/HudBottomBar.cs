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
    [SerializeField] private GameObject unitInfoPanelRoot;
    [SerializeField] private GameObject minimapRoot;

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

        if (unitInfoPanelRoot != null)
            unitInfoPanelRoot.SetActive(true);

        if (minimapRoot != null)
            minimapRoot.SetActive(true);
    }

    private void Awake()
    {
        if (unitInfoPanelRoot == null && UIManager.Instance != null)
            unitInfoPanelRoot = UIManager.Instance.unitInfoPanel;

        if (minimapRoot == null)
        {
            var minimap = GetComponentInChildren<MinimapUI>(true);
            if (minimap != null)
                minimapRoot = minimap.gameObject;
        }
    }
}
