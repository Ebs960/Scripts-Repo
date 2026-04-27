// Assets/Scripts/UI/HudRightPanel.cs
using UnityEngine;

/// <summary>
/// Right-side HUD panel shell.
/// Layer dropdown ownership stays in HudPanelRouter; this panel only validates references.
/// </summary>
public class HudRightPanel : MonoBehaviour
{
    public void Bind(Civilization civ)
    {
        if (civ == null)
            Debug.LogWarning("HudRightPanel.Bind: Civilization is null");
    }
}
