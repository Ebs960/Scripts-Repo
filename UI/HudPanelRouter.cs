// Assets/Scripts/UI/HudPanelRouter.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static GameManager;

/// <summary>
/// Centralized panel routing system replacing scattered ShowPanel calls throughout PlayerUI and other scripts.
/// Routes all HUD dropdown button clicks to appropriate full-screen panels via UIManager.
/// </summary>
public class HudPanelRouter : MonoBehaviour
{
    [SerializeField] private Button techButton;
    [SerializeField] private Button cultureButton;
    [SerializeField] private Button religionButton;
    [SerializeField] private Button policyButton;
    [SerializeField] private Button diplomacyButton;
    [SerializeField] private Button politicalAffairsButton;
    [SerializeField] private Button equipmentButton;
    [SerializeField] private TMP_Dropdown layerDropdown;

    private Civilization currentCiv;

    private void Start()
    {
        WireButtonListeners();
    }

    private void OnDestroy()
    {
        UnwireButtonListeners();
    }

    private void WireButtonListeners()
    {
        if (techButton != null)
        {
            techButton.onClick.RemoveAllListeners();
            techButton.onClick.AddListener(() => RouteTechPanel());
        }

        if (cultureButton != null)
        {
            cultureButton.onClick.RemoveAllListeners();
            cultureButton.onClick.AddListener(() => RouteCulturePanel());
        }

        if (religionButton != null)
        {
            religionButton.onClick.RemoveAllListeners();
            religionButton.onClick.AddListener(() => RouteReligionPanel());
        }

        if (policyButton != null)
        {
            policyButton.onClick.RemoveAllListeners();
            policyButton.onClick.AddListener(() => RouteGovernmentPanel());
        }

        if (diplomacyButton != null)
        {
            diplomacyButton.onClick.RemoveAllListeners();
            diplomacyButton.onClick.AddListener(() => RouteDiplomacyPanel());
        }

        if (politicalAffairsButton != null)
        {
            politicalAffairsButton.onClick.RemoveAllListeners();
            politicalAffairsButton.onClick.AddListener(() => RoutePoliticalAffairsPanel());
        }

        if (equipmentButton != null)
        {
            equipmentButton.onClick.RemoveAllListeners();
            equipmentButton.onClick.AddListener(() => RouteEquipmentPanel());
        }

        if (layerDropdown != null)
        {
            layerDropdown.onValueChanged.RemoveAllListeners();
            layerDropdown.onValueChanged.AddListener(value => RouteLayerSelection(value));
        }
    }

    private void UnwireButtonListeners()
    {
        if (techButton != null)
            techButton.onClick.RemoveAllListeners();
        if (cultureButton != null)
            cultureButton.onClick.RemoveAllListeners();
        if (religionButton != null)
            religionButton.onClick.RemoveAllListeners();
        if (policyButton != null)
            policyButton.onClick.RemoveAllListeners();
        if (diplomacyButton != null)
            diplomacyButton.onClick.RemoveAllListeners();
        if (politicalAffairsButton != null)
            politicalAffairsButton.onClick.RemoveAllListeners();
        if (equipmentButton != null)
            equipmentButton.onClick.RemoveAllListeners();
        if (layerDropdown != null)
            layerDropdown.onValueChanged.RemoveAllListeners();
    }

    /// <summary>
    /// Set the current civilization for routing (called by HudTopBar or HudController).
    /// </summary>
    public void SetCurrentCivilization(Civilization civ)
    {
        currentCiv = civ;
    }

    // ===== Routing Methods =====

    private void RouteTechPanel()
    {
        if (currentCiv == null)
            return;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowTechPanel(currentCiv);
    }

    private void RouteCulturePanel()
    {
        if (currentCiv == null)
            return;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowCulturePanel(currentCiv);
    }

    private void RouteReligionPanel()
    {
        if (currentCiv == null)
            return;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowReligionPanel(currentCiv);
    }

    private void RouteGovernmentPanel()
    {
        if (currentCiv == null)
            return;

        // Government panel logic (open policy/government UI)
        if (UIManager.Instance != null)
        {
            // Assuming government panel is managed via UIManager.ShowPanel or similar
            UIManager.Instance.ShowPanel("GovernmentPanel");
        }
    }

    private void RouteDiplomacyPanel()
    {
        if (currentCiv == null)
            return;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowDiplomacyPanel(currentCiv);
    }

    private void RoutePoliticalAffairsPanel()
    {
        if (currentCiv == null)
            return;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowPoliticalAffairsPanel(currentCiv);
    }

    private void RouteEquipmentPanel()
    {
        if (currentCiv == null)
            return;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowEquipmentPanel(currentCiv);
    }

    private void RouteLayerSelection(int layerIndex)
    {
        // Route to planet's LayerManager (LayerManager is a component on planet objects)
        var lm = UnityEngine.Object.FindFirstObjectByType<LayerManager>();
        if (lm != null)
            lm.SetOnlyLayerVisible((PlanetLayerType)layerIndex);
    }
}
