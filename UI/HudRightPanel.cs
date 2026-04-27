// Assets/Scripts/UI/HudRightPanel.cs
using UnityEngine;

/// <summary>
/// Right-side HUD panel hosting layer selection dropdown.
/// Routes layer changes to planet's LayerManager.SetOnlyLayerVisible().
/// </summary>
public class HudRightPanel : MonoBehaviour
{
    public void Bind(Civilization civ)
    {
        if (civ == null)
            Debug.LogWarning("HudRightPanel.Bind: Civilization is null");
    }
}
