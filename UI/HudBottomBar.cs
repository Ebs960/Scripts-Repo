// Assets/Scripts/UI/HudBottomBar.cs
using UnityEngine;

/// <summary>
/// Bottom bar HUD shell for already-placed UnitInfoPanel and MinimapUI roots.
/// </summary>
public class HudBottomBar : MonoBehaviour
{
    [SerializeField] private GameObject unitInfoPanelRoot;
    [SerializeField] private GameObject minimapRoot;

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
