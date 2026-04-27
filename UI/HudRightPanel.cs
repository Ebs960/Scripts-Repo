// Assets/Scripts/UI/HudRightPanel.cs
using TMPro;
using UnityEngine;

/// <summary>
/// Right-side HUD shell.
/// Layer dropdown interaction is owned by HudPanelRouter.
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
