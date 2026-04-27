// Assets/Scripts/UI/HudRightPanel.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static GameManager;

/// <summary>
/// Right-side HUD panel hosting layer selection dropdown.
/// Routes layer changes to planet's LayerManager.SetOnlyLayerVisible().
/// </summary>
public class HudRightPanel : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown layerDropdown;

    /// <summary>
    /// Bind this panel to a civilization and populate displays.
    /// </summary>
    public void Bind(Civilization civ)
    {
        if (civ == null)
        {
            Debug.LogWarning("HudRightPanel.Bind: Civilization is null");
            return;
        }

        WireLayerDropdown();
    }

    private void WireLayerDropdown()
    {
        if (layerDropdown == null)
        {
            Debug.LogWarning("HudRightPanel: layerDropdown not assigned");
            return;
        }

        layerDropdown.onValueChanged.RemoveAllListeners();
        layerDropdown.onValueChanged.AddListener(value =>
        {
            var lm = UnityEngine.Object.FindFirstObjectByType<LayerManager>();
            if (lm != null)
                lm.SetOnlyLayerVisible((PlanetLayerType)value);
        });
    }
}
