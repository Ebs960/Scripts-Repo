using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Compact, prefab-authored management panel for the selected campaign Band.</summary>
public sealed class BandPanel : MonoBehaviour
{
    [Serializable]
    private sealed class ProductionButtonBinding
    {
        public Button button;
        public Image icon;
        public TMP_Text nameText;
        public TMP_Text costStatusText;
        public GameObject completedMark;
        [NonSerialized] public BandStructureData structure;
        [NonSerialized] public CombatUnitData unit;
    }

    [Header("Overview")]
    [SerializeField] private TMP_Text titleText, ownerText, populationText, foodText, starvationText;
    [SerializeField] private TMP_Text stateText, movementText, yieldsText, structuresText, actionReasonText;
    [Header("Current Production")]
    [SerializeField] private TMP_Text productionText, productionTurnsText;
    [SerializeField] private Image productionProgressFill;
    [Header("Authored Structure Buttons (5)")]
    [SerializeField] private BandStructureData foragingTentData, storyCircleData, stonePileData, toolMakerData, fishingTentData;
    [SerializeField] private ProductionButtonBinding foragingTentButton, storyCircleButton, stonePileButton, toolMakerButton, fishingTentButton;
    [Header("Authored Recruitment Buttons (4)")]
    [SerializeField] private CombatUnitData hunterData, clubmanData, spearThrowerData, raftData;
    [SerializeField] private ProductionButtonBinding hunterButton, clubmanButton, spearThrowerButton, raftButton;
    [Header("Actions")]
    [SerializeField] private Button packEncampButton, forageButton, foundSettlementButton, splinterButton, closeButton;
    [SerializeField] private TMP_Text packEncampButtonText;
    [Header("Collapsible Garrison")]
    [SerializeField] private Button garrisonHeaderButton;
    [SerializeField] private TMP_Text garrisonHeaderText, garrisonMessageText;
    [SerializeField] private GameObject garrisonExpandedRoot;
    [SerializeField] private RectTransform garrisonEntriesRoot;
    [SerializeField] private Button selectAllButton, deselectAllButton, formArmyButton, garrisonArmyButton;
    [SerializeField] private TMP_Dropdown nearbyArmyDropdown;

    private readonly HashSet<CombatUnit> selectedGarrison = new HashSet<CombatUnit>();
    private readonly List<CombatUnit> nearbyArmies = new List<CombatUnit>();
    private Band band;
    private bool garrisonExpanded;

    private ProductionButtonBinding[] StructureBindings => new[] { foragingTentButton, storyCircleButton, stonePileButton, toolMakerButton, fishingTentButton };
    private ProductionButtonBinding[] RecruitmentBindings => new[] { hunterButton, clubmanButton, spearThrowerButton, raftButton };

    private void Awake()
    {
        Bind(foragingTentButton, foragingTentData); Bind(storyCircleButton, storyCircleData); Bind(stonePileButton, stonePileData);
        Bind(toolMakerButton, toolMakerData); Bind(fishingTentButton, fishingTentData);
        Bind(hunterButton, hunterData); Bind(clubmanButton, clubmanData); Bind(spearThrowerButton, spearThrowerData); Bind(raftButton, raftData);
        Wire(packEncampButton, TogglePackedState); Wire(forageButton, Forage); Wire(foundSettlementButton, FoundSettlement);
        Wire(splinterButton, Splinter); Wire(closeButton, Close); Wire(garrisonHeaderButton, ToggleGarrison);
        Wire(selectAllButton, SelectAll); Wire(deselectAllButton, DeselectAll); Wire(formArmyButton, FormArmy); Wire(garrisonArmyButton, GarrisonArmy);
        if (garrisonExpandedRoot != null) garrisonExpandedRoot.SetActive(garrisonExpanded);
    }

    private void OnEnable() { Band.BandChanged += OnBandChanged; Band.BandDestroyed += OnBandDestroyed; }
    private void OnDisable() { Band.BandChanged -= OnBandChanged; Band.BandDestroyed -= OnBandDestroyed; }

    public void Show(Band value)
    {
        band = value; selectedGarrison.Clear(); gameObject.SetActive(value != null);
        if (value != null) Refresh();
    }
    public void Hide() { band = null; selectedGarrison.Clear(); gameObject.SetActive(false); }
    public void Close() { UnitSelectionManager.Instance?.DeselectBand(); Hide(); }

    public void Refresh()
    {
        if (band == null) return;
        var data = band.Data; var yields = band.GetCurrentYields();
        Set(titleText, data?.displayName ?? "Band"); Set(ownerText, $"Owner  {band.Owner?.civData?.civName ?? "None"}");
        Set(populationText, $"Population  {band.Population}"); Set(foodText, $"Food  {band.FoodReserve}/{band.FoodCapacity}  •  {band.FoodRequiredPerTurn}/turn");
        if (starvationText != null) { starvationText.gameObject.SetActive(band.IsStarving); if (band.IsStarving) { int loss = Mathf.Max(1, Mathf.CeilToInt(band.Population * (data?.populationLossPctPerStarvingTurn ?? .1f))); starvationText.text = $"STARVING {band.ConsecutiveStarvationTurns}/{data?.collapseAfterStarvationTurns ?? 0} • next loss {loss}"; } }
        Set(stateText, band.State.ToString()); Set(movementText, $"Movement  {band.CurrentMovePoints}/{data?.movementPoints ?? 0}");
        Set(yieldsText, $"Yields  F {yields.food}  P {yields.production}  G {yields.gold}  S {yields.science}  C {yields.culture}  Faith {yields.faith}  PP {yields.policyPoints}");
        Set(structuresText, "Built  " + (band.BuiltStructures.Count == 0 ? "None" : string.Join(", ", band.BuiltStructures.Where(x => x != null).Select(x => x.structureName))));
        RefreshProduction(yields.production); RefreshProductionButtons(); RefreshActions(); RefreshGarrison();
    }

    private void RefreshProduction(int productionPerTurn)
    {
        string name; int required;
        if (band.QueuedStructure != null) { name = band.QueuedStructure.structureName; required = Mathf.Max(1, band.QueuedStructure.productionCost); }
        else if (band.QueuedUnit != null) { name = band.QueuedUnit.unitName; required = Mathf.Max(1, band.QueuedUnit.bandProductionCost); }
        else { Set(productionText, "No Active Production"); Set(productionTurnsText, string.Empty); if (productionProgressFill != null) productionProgressFill.fillAmount = 0f; return; }
        Set(productionText, $"{name}  {band.ProductionProgress}/{required}");
        int remaining = Mathf.Max(0, required - band.ProductionProgress);
        Set(productionTurnsText, productionPerTurn > 0 ? $"{Mathf.CeilToInt((float)remaining / productionPerTurn)} turns" : "No production income");
        if (productionProgressFill != null) productionProgressFill.fillAmount = Mathf.Clamp01((float)band.ProductionProgress / required);
    }

    private void RefreshProductionButtons()
    {
        foreach (var binding in StructureBindings) RefreshBinding(binding);
        foreach (var binding in RecruitmentBindings) RefreshBinding(binding);
    }

    private void RefreshBinding(ProductionButtonBinding binding)
    {
        if (binding == null || binding.button == null) return;
        bool available; string reason = string.Empty; bool completed = false; bool inProgress = false;
        if (binding.structure != null)
        {
            bool offered = band.Data?.allowedStructures?.Contains(binding.structure) == true;
            completed = band.BuiltStructures.Contains(binding.structure); inProgress = band.QueuedStructure == binding.structure;
            available = offered && band.CanQueueStructure(binding.structure, out reason);
            if (!offered) reason = "Not available to this Band.";
            if (binding.icon != null) binding.icon.sprite = binding.structure.icon; Set(binding.nameText, binding.structure.structureName);
        }
        else
        {
            bool offered = binding.unit != null && band.Data?.allowedMilitaryRecruitment?.Contains(binding.unit) == true;
            inProgress = band.QueuedUnit == binding.unit;
            available = offered && band.CanQueueMilitaryUnit(binding.unit, out reason);
            if (!offered) reason = "Not available to this Band.";
            if (binding.icon != null) binding.icon.sprite = binding.unit?.GetIcon(band.Owner); Set(binding.nameText, binding.unit?.unitName ?? "Unassigned");
        }
        binding.button.interactable = available; if (binding.icon != null) { binding.icon.enabled = binding.icon.sprite != null; binding.icon.preserveAspect = true; }
        if (binding.completedMark != null) binding.completedMark.SetActive(completed);
        string status = completed ? "COMPLETED" : inProgress ? "IN PROGRESS" : available ? FormatCost(binding) : reason;
        Set(binding.costStatusText, status);
        var tooltip = binding.button.GetComponent<SimpleTooltipTarget>(); if (tooltip != null) tooltip.Bind(binding.nameText?.text, available ? FormatCost(binding) : reason);
    }

    private string FormatCost(ProductionButtonBinding binding)
    {
        int production = binding.structure != null ? binding.structure.productionCost : Mathf.Max(1, binding.unit?.bandProductionCost ?? 1);
        int gold = binding.structure != null ? binding.structure.goldCost : binding.unit?.goldCost ?? 0;
        ResourceCost[] resources = binding.structure != null ? binding.structure.resourceCosts : binding.unit?.requiredResourceCosts;
        var parts = new List<string> { $"{production} Production" }; if (gold > 0) parts.Add($"{gold} Gold");
        if (resources != null) parts.AddRange(resources.Where(x => x?.resource != null && x.amount > 0).Select(x => $"{x.amount} {x.resource.resourceName}"));
        return string.Join(" • ", parts);
    }

    private void RefreshActions()
    {
        var data = band.Data; bool packed = band.State == BandState.Packed;
        Set(packEncampButtonText, packed ? "Encamp" : "Pack Camp");
        SetAction(packEncampButton, data != null && band.CurrentMovePoints >= (packed ? data.encampMovementCost : data.packMovementCost));
        SetAction(forageButton, data != null && band.CurrentMovePoints >= data.forageMovementCost && band.FoodReserve < band.FoodCapacity);
        bool canFound = band.CanFoundSettlement(out string foundReason); SetAction(foundSettlementButton, canFound);
        bool canSplinter = band.CanSplinterNewBand(out string splinterReason); SetAction(splinterButton, canSplinter);
        string reason = string.Empty;
        int stateCost = packed ? data?.encampMovementCost ?? int.MaxValue : data?.packMovementCost ?? int.MaxValue;
        if (band.CurrentMovePoints < stateCost) reason = $"{(packed ? "Encamp" : "Pack")}: not enough movement.";
        else if (data != null && band.CurrentMovePoints < data.forageMovementCost) reason = "Forage: not enough movement.";
        else if (band.FoodReserve >= band.FoodCapacity) reason = "Forage: food reserve full.";
        else if (!canFound) reason = "Settlement: " + foundReason;
        else if (!canSplinter) reason = "Splinter: " + splinterReason;
        Set(actionReasonText, reason);
    }

    private void RefreshGarrison()
    {
        selectedGarrison.RemoveWhere(x => x == null || !band.Garrison.Contains(x));
        Set(garrisonHeaderText, $"Garrison Management — {band.Garrison.Count}/{band.GarrisonCapacity} {(garrisonExpanded ? "▾" : "▸")}");
        if (garrisonExpandedRoot != null) garrisonExpandedRoot.SetActive(garrisonExpanded);
        if (!garrisonExpanded) return;
        ClearChildren(garrisonEntriesRoot);
        foreach (var unit in band.Garrison.Where(x => x != null))
        {
            var go = new GameObject("Garrison Unit", typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(garrisonEntriesRoot, false);
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>(); label.transform.SetParent(go.transform, false); label.text = unit.UnitName; label.fontSize = 12; label.raycastTarget = false;
            go.GetComponent<Image>().color = selectedGarrison.Contains(unit) ? new Color(.72f,.42f,.14f) : new Color(.12f,.14f,.18f);
            go.GetComponent<Button>().onClick.AddListener(() => { if (!selectedGarrison.Add(unit)) selectedGarrison.Remove(unit); RefreshGarrison(); });
        }
        nearbyArmies.Clear();
        if (band.Owner?.combatUnits != null) nearbyArmies.AddRange(band.Owner.combatUnits.Where(x => x != null && !x.isStored && CampaignArmyService.IsRepresentative(x) && x.planetIndex == band.PlanetIndex && x.currentLayer == TileLayer.Surface && x.currentTileIndex == band.CurrentTileIndex));
        if (nearbyArmyDropdown != null) { nearbyArmyDropdown.ClearOptions(); nearbyArmyDropdown.AddOptions(nearbyArmies.Select(x => x.MilitaryFormationName ?? x.UnitName).ToList()); nearbyArmyDropdown.interactable = nearbyArmies.Count > 1; }
        SetAction(formArmyButton, selectedGarrison.Count > 0 && selectedGarrison.Count <= (band.Owner?.GetMaxArmySize() ?? CampaignArmyService.DefaultArmySize)); SetAction(garrisonArmyButton, nearbyArmies.Count > 0);
    }

    private void Bind(ProductionButtonBinding binding, BandStructureData data) { if (binding == null) return; binding.structure = data; binding.unit = null; Wire(binding.button, () => Queue(binding)); }
    private void Bind(ProductionButtonBinding binding, CombatUnitData data) { if (binding == null) return; binding.unit = data; binding.structure = null; Wire(binding.button, () => Queue(binding)); }
    private void Queue(ProductionButtonBinding binding) { if (band == null) return; bool ok = binding.structure != null ? band.QueueStructure(binding.structure) : band.QueueMilitaryUnit(binding.unit); if (!ok) ShowMessage("Production could not be started."); Refresh(); }
    private void TogglePackedState() { if (band == null) return; bool ok = band.State == BandState.Packed ? band.Encamp() : band.Pack(); if (!ok) ShowMessage("Not enough movement."); Refresh(); }
    private void Forage() { if (band == null) return; if (band.Data == null || band.CurrentMovePoints < band.Data.forageMovementCost) ShowMessage("Not enough movement to forage."); else if (band.FoodReserve >= band.FoodCapacity) ShowMessage("Food reserve full."); else band.Forage(); Refresh(); }
    private void ToggleGarrison() { garrisonExpanded = !garrisonExpanded; RefreshGarrison(); }
    private void SelectAll() { selectedGarrison.Clear(); foreach (var unit in band.Garrison.Where(x => x != null)) selectedGarrison.Add(unit); RefreshGarrison(); }
    private void DeselectAll() { selectedGarrison.Clear(); RefreshGarrison(); }
    private void FormArmy() { if (!band.FormArmy(selectedGarrison.ToList(), out _)) ShowMessage("Selected units cannot form an army."); else selectedGarrison.Clear(); Refresh(); }
    private void GarrisonArmy() { int i = nearbyArmyDropdown?.value ?? 0; if (i < 0 || i >= nearbyArmies.Count) { ShowMessage("No eligible army is present."); return; } if (!band.TryGarrisonArmy(nearbyArmies[i], out string reason)) ShowMessage(reason); Refresh(); }
    private void FoundSettlement() { City city = band.FoundSettlement(out string reason); if (city == null) { ShowMessage(reason); return; } Hide(); UIManager.Instance?.ShowPanel("CityPanel"); UIManager.Instance?.GetPanel("CityPanel")?.GetComponent<CityUI>()?.ShowForCity(city); }
    private void Splinter() { if (!band.CanSplinterNewBand(out string reason)) { ShowMessage(reason); return; } UnitSelectionManager.Instance?.BeginSplinterTargeting(); }
    private void OnBandChanged(Band changed) { if (changed == band) Refresh(); }
    private void OnBandDestroyed(Band destroyed, BandLossReason reason) { if (destroyed != band) return; UnitSelectionManager.Instance?.DeselectBand(); Hide(); }
    private void ShowMessage(string message) { Set(garrisonMessageText, message); if (!string.IsNullOrEmpty(message)) UIManager.Instance?.ShowNotification(message); }
    private static void ClearChildren(Transform root) { if (root == null) return; for (int i=root.childCount-1;i>=0;i--) Destroy(root.GetChild(i).gameObject); }
    private static void Wire(Button button, UnityEngine.Events.UnityAction action) { if (button == null) return; button.onClick.RemoveAllListeners(); button.onClick.AddListener(action); }
    private static void SetAction(Button button, bool enabled) { if (button != null) button.interactable = enabled; }
    private static void Set(TMP_Text text, string value) { if (text != null) text.text = value; }
}
