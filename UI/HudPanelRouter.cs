// Assets/Scripts/UI/HudPanelRouter.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static GameManager;

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
    private readonly List<PlanetLayerType> layerDropdownMapping = new();

    private void Start()
    {
        WireButtonListeners();
        RefreshLayerDropdown();
    }

    private void OnDestroy()
    {
        UnwireButtonListeners();
    }

    public void SetCurrentCivilization(Civilization civ)
    {
        currentCiv = civ;
    }

    public void RefreshLayerDropdown()
    {
        if (layerDropdown == null)
            return;

        var lm = GetActiveLayerManager();

        layerDropdownMapping.Clear();
        layerDropdown.ClearOptions();

        var options = new List<TMP_Dropdown.OptionData>();

        PlanetLayerType[] layersToCheck =
        {
            PlanetLayerType.Surface,
            PlanetLayerType.Underwater,
            PlanetLayerType.Mantle,
            PlanetLayerType.Atmosphere,
            PlanetLayerType.Orbit
        };

        foreach (var layer in layersToCheck)
        {
            if (lm != null && lm.IsLayerSupported(layer))
            {
                layerDropdownMapping.Add(layer);
                options.Add(new TMP_Dropdown.OptionData(layer.ToString()));
            }
        }

        if (options.Count == 0)
        {
            layerDropdownMapping.Add(PlanetLayerType.Surface);
            options.Add(new TMP_Dropdown.OptionData("Surface"));
        }

        layerDropdown.SetValueWithoutNotify(0);
        layerDropdown.AddOptions(options);
        layerDropdown.RefreshShownValue();
    }

    private void WireButtonListeners()
    {
        if (techButton != null)
        {
            techButton.onClick.RemoveAllListeners();
            techButton.onClick.AddListener(RouteTechPanel);
        }

        if (cultureButton != null)
        {
            cultureButton.onClick.RemoveAllListeners();
            cultureButton.onClick.AddListener(RouteCulturePanel);
        }

        if (religionButton != null)
        {
            religionButton.onClick.RemoveAllListeners();
            religionButton.onClick.AddListener(RouteReligionPanel);
        }

        if (policyButton != null)
        {
            policyButton.onClick.RemoveAllListeners();
            policyButton.onClick.AddListener(RouteGovernmentPanel);
        }

        if (diplomacyButton != null)
        {
            diplomacyButton.onClick.RemoveAllListeners();
            diplomacyButton.onClick.AddListener(RouteDiplomacyPanel);
        }

        if (politicalAffairsButton != null)
        {
            politicalAffairsButton.onClick.RemoveAllListeners();
            politicalAffairsButton.onClick.AddListener(RoutePoliticalAffairsPanel);
        }

        if (equipmentButton != null)
        {
            equipmentButton.onClick.RemoveAllListeners();
            equipmentButton.onClick.AddListener(RouteEquipmentPanel);
        }

        if (layerDropdown != null)
        {
            layerDropdown.onValueChanged.RemoveAllListeners();
            layerDropdown.onValueChanged.AddListener(RouteLayerSelection);
        }
    }

    private void UnwireButtonListeners()
    {
        if (techButton != null) techButton.onClick.RemoveAllListeners();
        if (cultureButton != null) cultureButton.onClick.RemoveAllListeners();
        if (religionButton != null) religionButton.onClick.RemoveAllListeners();
        if (policyButton != null) policyButton.onClick.RemoveAllListeners();
        if (diplomacyButton != null) diplomacyButton.onClick.RemoveAllListeners();
        if (politicalAffairsButton != null) politicalAffairsButton.onClick.RemoveAllListeners();
        if (equipmentButton != null) equipmentButton.onClick.RemoveAllListeners();
        if (layerDropdown != null) layerDropdown.onValueChanged.RemoveAllListeners();
    }

    private void RouteTechPanel()
    {
        if (UIManager.Instance != null && currentCiv != null)
            UIManager.Instance.ShowTechPanel(currentCiv);
    }

    private void RouteCulturePanel()
    {
        if (UIManager.Instance != null && currentCiv != null)
            UIManager.Instance.ShowCulturePanel(currentCiv);
    }

    private void RouteReligionPanel()
    {
        if (UIManager.Instance != null && currentCiv != null)
            UIManager.Instance.ShowReligionPanel(currentCiv);
    }

    private void RouteGovernmentPanel()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ShowPanel("GovernmentPanel");
    }

    private void RouteDiplomacyPanel()
    {
        var civ = currentCiv ?? ResolvePlayerCivilization();
        if (UIManager.Instance != null && civ != null)
            UIManager.Instance.ShowDiplomacyPanel(civ);
    }

    private void RoutePoliticalAffairsPanel()
    {
        var civ = currentCiv ?? ResolvePlayerCivilization();
        if (UIManager.Instance != null)
            UIManager.Instance.ShowPoliticalAffairsPanel(civ);
    }

    private void RouteEquipmentPanel()
    {
        var civ = currentCiv ?? ResolvePlayerCivilization();
        if (UIManager.Instance != null && civ != null)
            UIManager.Instance.ShowEquipmentPanel(civ);
    }

    private void RouteLayerSelection(int dropdownIndex)
    {
        if (dropdownIndex < 0 || dropdownIndex >= layerDropdownMapping.Count)
            return;

        var lm = GetActiveLayerManager();
        if (lm == null)
        {
            Debug.LogWarning("HudPanelRouter: No LayerManager found on active planet.");
            return;
        }

        var selectedLayer = layerDropdownMapping[dropdownIndex];
        lm.SetOnlyLayerVisible(selectedLayer);
    }

    private LayerManager GetActiveLayerManager()
    {
        var gen = GameManager.Instance != null
            ? GameManager.Instance.GetCurrentPlanetGenerator()
            : Object.FindAnyObjectByType<PlanetGenerator>();

        if (gen == null)
            return Object.FindAnyObjectByType<LayerManager>();

        var lm = gen.GetComponent<LayerManager>();
        if (lm != null)
            return lm;

        return Object.FindAnyObjectByType<LayerManager>();
    }

    private Civilization ResolvePlayerCivilization()
    {
        if (currentCiv != null)
            return currentCiv;

        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs != null)
        {
            foreach (var civ in allCivs)
            {
                if (civ != null && civ.isPlayerControlled)
                    return civ;
            }
        }

        return TurnManager.Instance?.GetCurrentCivilization();
    }
}
