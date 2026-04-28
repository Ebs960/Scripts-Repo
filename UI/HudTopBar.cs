// Assets/Scripts/UI/HudTopBar.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static GameManager;

/// <summary>
/// Top bar HUD widget showing:
/// - Civilization name and round
/// - Yield displays (food, gold, policy points with per-turn deltas)
/// - Resource category displays (manually placed widgets)
/// </summary>
public class HudTopBar : MonoBehaviour
{
    [Header("Info Display")]
    [SerializeField] private TextMeshProUGUI civNameText;
    [SerializeField] private TextMeshProUGUI roundText;
    [SerializeField] private TextMeshProUGUI seasonText;

    [Header("Yield Widgets")]
    [SerializeField] private HudYieldWidget foodYieldWidget;
    [SerializeField] private HudYieldWidget goldYieldWidget;
    [SerializeField] private HudYieldWidget policyYieldWidget;
    [SerializeField] private HudYieldWidget faithYieldWidget;

    [Header("Resource Categories (Manually Placed)")]
    [SerializeField] private HudResourceCategoryWidget[] resourceCategoryWidgets = new HudResourceCategoryWidget[6];
    [SerializeField] private ResourceCategory[] allResourceCategories = new ResourceCategory[6];

    [Header("Panel Buttons (optional)")]
    [SerializeField] private Button religionButton;
    [SerializeField] private Button policyButton;
    [SerializeField] private Button diplomacyButton;
    [SerializeField] private Button equipmentButton;
    [SerializeField] private Button endTurnButton;

    [Header("Layer Dropdown (optional)")]
    [SerializeField] private TMP_Dropdown layerDropdown;

    private Civilization currentCiv;
    private readonly List<PlanetLayerType> layerDropdownMapping = new();

    private bool listenersWired;
    private UnityEngine.Events.UnityAction religionAction;
    private UnityEngine.Events.UnityAction policyAction;
    private UnityEngine.Events.UnityAction diplomacyAction;
    private UnityEngine.Events.UnityAction equipmentAction;
    private UnityEngine.Events.UnityAction endTurnAction;
    private UnityEngine.Events.UnityAction<int> layerChangedAction;

    /// <summary>
    /// Bind this widget to a civilization and populate displays.
    /// </summary>
    public void Bind(Civilization civ)
    {
        currentCiv = civ;
        if (currentCiv == null)
        {
            Debug.LogWarning("HudTopBar.Bind: Civilization is null");
            return;
        }

        if (civNameText != null)
            civNameText.text = currentCiv.civData != null && !string.IsNullOrEmpty(currentCiv.civData.civName) ? currentCiv.civData.civName : (currentCiv.name ?? "Unknown");

        if (roundText != null)
        {
            var round = TurnManager.Instance?.round ?? 0;
            roundText.text = $"Turn {round}";
        }

        if (seasonText != null)
        {
            if (ClimateManager.Instance != null)
            {
                var season = ClimateManager.Instance.GetSeasonForPlanet(0);
                seasonText.text = season.ToString();
            }
            else
            {
                seasonText.text = "---";
            }
        }

        if (foodYieldWidget != null)
        {
            int foodDelta = currentCiv.cachedFoodPerTurn - currentCiv.cachedFoodConsumption;
            foodYieldWidget.Bind("Food", currentCiv.food, foodDelta, null);
        }

        if (goldYieldWidget != null)
            goldYieldWidget.Bind("Gold", currentCiv.gold, currentCiv.cachedGoldPerTurn, null);

        if (policyYieldWidget != null)
            policyYieldWidget.Bind("Policy Points", currentCiv.policyPoints, currentCiv.cachedPolicyPerTurn, null);

        if (faithYieldWidget != null)
            faithYieldWidget.Bind("Faith", currentCiv.faith, currentCiv.cachedFaithPerTurn, null);

        BindResourceCategories();
        WireButtonListeners();
        RefreshLayerDropdown();
        UpdateEndTurnButtonState();
    }

    private void BindResourceCategories()
    {
        if (currentCiv == null || resourceCategoryWidgets == null || allResourceCategories == null)
            return;

        for (int i = 0; i < resourceCategoryWidgets.Length; i++)
        {
            if (resourceCategoryWidgets[i] == null || i >= allResourceCategories.Length)
                continue;

            var widget = resourceCategoryWidgets[i];
            var category = allResourceCategories[i];

            int count = ResourceCategoryProviderUtility.GetTotalCount(currentCiv, category);
            int yieldPerTurn = 0;

            widget.Bind(currentCiv, category, count, yieldPerTurn);
        }
    }

    private void WireButtonListeners()
    {
        if (listenersWired)
            return;

        religionAction = OpenReligionPanel;
        policyAction = OpenGovernmentPanel;
        diplomacyAction = OpenDiplomacyPanel;
        equipmentAction = OpenEquipmentPanel;
        endTurnAction = OnEndTurnButtonClicked;
        layerChangedAction = HandleLayerChanged;

        if (religionButton != null)
            religionButton.onClick.AddListener(religionAction);

        if (policyButton != null)
            policyButton.onClick.AddListener(policyAction);

        if (diplomacyButton != null)
            diplomacyButton.onClick.AddListener(diplomacyAction);

        if (equipmentButton != null)
            equipmentButton.onClick.AddListener(equipmentAction);

        if (endTurnButton != null)
            endTurnButton.onClick.AddListener(endTurnAction);

        if (layerDropdown != null)
            layerDropdown.onValueChanged.AddListener(layerChangedAction);

        listenersWired = true;
    }

    private void RefreshLayerDropdown()
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

    private void OpenReligionPanel()
    {
        if (UIManager.Instance != null && currentCiv != null)
            UIManager.Instance.ShowReligionPanel(currentCiv);
    }

    private void OpenGovernmentPanel()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ShowPanel("GovernmentPanel");
    }

    private void OpenDiplomacyPanel()
    {
        if (UIManager.Instance != null && currentCiv != null)
            UIManager.Instance.ShowDiplomacyPanel(currentCiv);
    }

    private void OpenEquipmentPanel()
    {
        if (UIManager.Instance != null && currentCiv != null)
            UIManager.Instance.ShowEquipmentPanel(currentCiv);
    }


    public void OnEndTurnButtonClicked()
    {
        if (TurnManager.Instance == null)
        {
            Debug.LogWarning("HudTopBar: TurnManager missing; cannot end turn.");
            return;
        }

        if (currentCiv == null || !currentCiv.isPlayerControlled)
            return;

        var activeCiv = TurnManager.Instance.GetCurrentCivilization();
        if (activeCiv != null && activeCiv != currentCiv)
            return;

        TurnManager.Instance.EndPlayerTurn();
        UpdateEndTurnButtonState();
    }

    private void UpdateEndTurnButtonState()
    {
        if (endTurnButton == null)
            return;

        bool canEndTurn = currentCiv != null && currentCiv.isPlayerControlled;
        var activeCiv = TurnManager.Instance != null ? TurnManager.Instance.GetCurrentCivilization() : null;
        if (activeCiv != null)
            canEndTurn &= activeCiv == currentCiv;

        endTurnButton.interactable = canEndTurn;
    }

    private void HandleLayerChanged(int dropdownIndex)
    {
        if (dropdownIndex < 0 || dropdownIndex >= layerDropdownMapping.Count)
            return;

        var lm = GetActiveLayerManager();
        if (lm == null)
        {
            Debug.LogWarning("HudTopBar: No LayerManager found on active planet.");
            return;
        }

        lm.SetOnlyLayerVisible(layerDropdownMapping[dropdownIndex]);
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

    private void OnDestroy()
    {
        if (!listenersWired)
            return;

        if (religionButton != null && religionAction != null)
            religionButton.onClick.RemoveListener(religionAction);
        if (policyButton != null && policyAction != null)
            policyButton.onClick.RemoveListener(policyAction);
        if (diplomacyButton != null && diplomacyAction != null)
            diplomacyButton.onClick.RemoveListener(diplomacyAction);
        if (equipmentButton != null && equipmentAction != null)
            equipmentButton.onClick.RemoveListener(equipmentAction);
        if (endTurnButton != null && endTurnAction != null)
            endTurnButton.onClick.RemoveListener(endTurnAction);
        if (layerDropdown != null && layerChangedAction != null)
            layerDropdown.onValueChanged.RemoveListener(layerChangedAction);

        listenersWired = false;
    }
}
