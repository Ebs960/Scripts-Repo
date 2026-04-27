// Assets/Scripts/UI/HudRightPanel.cs
using TMPro;
using UnityEngine;

/// <summary>
/// Right-side HUD panel shell.
/// Layer dropdown ownership stays in HudPanelRouter; this panel only validates references.
/// </summary>
public class HudRightPanel : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown layerDropdown;

    public void Bind(Civilization civ)
    {
        if (civ == null)
        {
            Debug.LogWarning("HudRightPanel.Bind: Civilization is null");
            return;
        }

        if (layerDropdown == null)
            layerDropdown = GetComponentInChildren<TMP_Dropdown>(true);
    }
}
