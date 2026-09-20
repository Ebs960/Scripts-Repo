using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Player-facing management panel for the selected campaign Band.</summary>
public sealed class BandPanel : MonoBehaviour
{
    [Header("Overview")]
    [SerializeField] private TMP_Text titleText, ownerText, populationText, foodText, starvationText;
    [SerializeField] private TMP_Text stateText, movementText, yieldsText, structuresText, productionText, actionReasonText;
    [Header("Actions")]
    [SerializeField] private Button packButton, encampButton, forageButton, foundSettlementButton, closeButton;
    [Header("Data-driven Production")]
    [SerializeField] private RectTransform productionButtonsRoot;
    [Header("Garrison")]
    [SerializeField] private TMP_Text garrisonText, garrisonMessageText;
    [SerializeField] private RectTransform garrisonEntriesRoot;
    [SerializeField] private Button selectAllButton, deselectAllButton, formArmyButton, garrisonArmyButton;
    [SerializeField] private TMP_Dropdown nearbyArmyDropdown;

    private sealed class ProductionEntry
    {
        public GameObject root;
        public Button button;
        public TMP_Text status;
        public BandStructureData structure;
        public CombatUnitData unit;
    }

    private readonly List<ProductionEntry> productionEntries = new List<ProductionEntry>();
    private readonly HashSet<CombatUnit> selectedGarrison = new HashSet<CombatUnit>();
    private readonly List<CombatUnit> nearbyArmies = new List<CombatUnit>();
    private Band band;
    private BandData builtForData;
    private Band builtForBand;

    private void Awake()
    {
        Wire(packButton, Pack); Wire(encampButton, Encamp); Wire(forageButton, Forage);
        Wire(foundSettlementButton, FoundSettlement); Wire(closeButton, Close);
        Wire(selectAllButton, SelectAll); Wire(deselectAllButton, DeselectAll);
        Wire(formArmyButton, FormArmy); Wire(garrisonArmyButton, GarrisonArmy);
    }

    private void OnEnable()
    {
        Band.BandChanged += OnBandChanged;
        Band.BandDestroyed += OnBandDestroyed;
    }

    private void OnDisable()
    {
        Band.BandChanged -= OnBandChanged;
        Band.BandDestroyed -= OnBandDestroyed;
    }

    public void Show(Band value)
    {
        band = value;
        selectedGarrison.Clear();
        gameObject.SetActive(value != null);
        if (value == null) return;
        if (builtForData != value.Data || builtForBand != value) BuildProductionButtons();
        Refresh();
    }

    public void Hide()
    {
        band = null;
        selectedGarrison.Clear();
        gameObject.SetActive(false);
    }

    public void Pack() { Act(() => band.Pack(), "Not enough movement to pack."); }
    public void Encamp() { Act(() => band.Encamp(), "Not enough movement to encamp."); }
    public void Forage()
    {
        if (band == null) return;
        if (band.Data == null || band.CurrentMovePoints < band.Data.forageMovementCost) ShowMessage("Not enough movement to forage.");
        else if (band.FoodReserve >= band.FoodCapacity) ShowMessage("Food reserve full.");
        else band.Forage();
        Refresh();
    }
    public void Close() { UnitSelectionManager.Instance?.DeselectBand(); Hide(); }

    public void Refresh()
    {
        if (band == null) return;
        var data = band.Data;
        var yields = band.GetCurrentYields();
        Set(titleText, data != null ? data.displayName : "Band");
        Set(ownerText, $"Owner: {band.Owner?.civData?.civName ?? "None"}");
        Set(populationText, $"Population: {band.Population}");
        Set(foodText, $"Food: {band.FoodReserve}/{band.FoodCapacity}  •  Uses {band.FoodRequiredPerTurn}/turn");
        int collapse = data != null ? data.collapseAfterStarvationTurns : 0;
        int loss = Mathf.Max(1, Mathf.CeilToInt(band.Population * (data != null ? data.populationLossPctPerStarvingTurn : .1f)));
        Set(starvationText, band.IsStarving ? $"STARVING {band.ConsecutiveStarvationTurns}/{collapse} • next loss: {loss} population" : "Starvation: none");
        Set(stateText, $"State: {band.State}");
        Set(movementText, $"Movement: {band.CurrentMovePoints}/{data?.movementPoints ?? 0}");
        Set(yieldsText, $"Yields  Food {yields.food}  Production {yields.production}  Gold {yields.gold}  Science {yields.science}  Culture {yields.culture}  Faith {yields.faith}  Policy {yields.policyPoints}");
        Set(structuresText, "Structures: " + (band.BuiltStructures.Count == 0 ? "None" : string.Join(", ", band.BuiltStructures.Where(x => x != null).Select(x => x.structureName))));
        Set(productionText, GetProductionText());

        SetAction(packButton, band.State == BandState.Encamped && band.CurrentMovePoints >= (data?.packMovementCost ?? int.MaxValue));
        SetAction(encampButton, band.State == BandState.Packed && band.CurrentMovePoints >= (data?.encampMovementCost ?? int.MaxValue));
        SetAction(forageButton, data != null && band.CurrentMovePoints >= data.forageMovementCost && band.FoodReserve < band.FoodCapacity);
        bool canFound = band.CanFoundSettlement(out string foundReason);
        SetAction(foundSettlementButton, canFound);
        Set(actionReasonText, GetActionReason(foundReason));

        RefreshProductionButtons();
        RefreshGarrison();
    }

    private string GetProductionText()
    {
        if (band.QueuedStructure != null) return $"Building {band.QueuedStructure.structureName}: {band.ProductionProgress}/{band.QueuedStructure.productionCost}";
        if (band.QueuedUnit != null) return $"Recruiting {band.QueuedUnit.unitName}: {band.ProductionProgress}/{Mathf.Max(1, band.QueuedUnit.bandProductionCost)}";
        return "Production: idle";
    }

    private string GetActionReason(string foundReason)
    {
        if (band.State == BandState.Packed && band.CurrentMovePoints < band.Data.encampMovementCost) return "Not enough movement to encamp.";
        if (band.State == BandState.Encamped && band.CurrentMovePoints < band.Data.packMovementCost) return "Not enough movement to pack.";
        if (band.FoodReserve >= band.FoodCapacity) return "Food reserve full.";
        return string.IsNullOrEmpty(foundReason) ? string.Empty : "Settlement: " + foundReason;
    }

    private void BuildProductionButtons()
    {
        ClearProductionEntries();
        builtForData = band != null ? band.Data : null;
        builtForBand = band;
        if (builtForData == null) return;
        EnsureProductionRoot();
        CreateSection("BAND STRUCTURES");
        foreach (var structure in builtForData.allowedStructures.Where(x => x != null)) CreateProductionEntry(structure, null);
        CreateSection("MILITARY UNITS");
        foreach (var unit in builtForData.allowedMilitaryRecruitment.Where(x => x != null)) CreateProductionEntry(null, unit);
        LayoutRebuilder.ForceRebuildLayoutImmediate(productionButtonsRoot);
    }

    private void EnsureProductionRoot()
    {
        if (productionButtonsRoot == null) return;
        var layout = productionButtonsRoot.GetComponent<GridLayoutGroup>() ?? productionButtonsRoot.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(154f, 66f); layout.spacing = new Vector2(10f, 8f); layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 3;
        var fitter = productionButtonsRoot.GetComponent<ContentSizeFitter>() ?? productionButtonsRoot.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void CreateSection(string text)
    {
        // Three cells keep the following section aligned without fixed row coordinates.
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject(i == 0 ? text : text + " Spacer", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(productionButtonsRoot, false);
            var label = go.GetComponent<TextMeshProUGUI>(); label.text = i == 0 ? text : string.Empty; label.fontSize = 16; label.fontStyle = FontStyles.Bold;
            productionEntries.Add(new ProductionEntry { root = go });
        }
    }

    private void CreateProductionEntry(BandStructureData structure, CombatUnitData unit)
    {
        string display = structure != null ? structure.structureName : unit.unitName;
        Sprite icon = structure != null ? structure.icon : unit.GetIcon(band.Owner);
        int cost = structure != null ? structure.productionCost : Mathf.Max(1, unit.bandProductionCost);
        var go = new GameObject("Band Production Option", typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup));
        go.transform.SetParent(productionButtonsRoot, false); go.GetComponent<Image>().color = new Color(.18f, .14f, .09f, .95f);
        var layout = go.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(5, 5, 3, 3); layout.spacing = 1;
        var button = go.GetComponent<Button>();
        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        iconObject.transform.SetParent(go.transform, false);
        var iconLayout = iconObject.GetComponent<LayoutElement>(); iconLayout.preferredHeight = 24f; iconLayout.preferredWidth = 24f;
        var iconImage = iconObject.GetComponent<Image>(); iconImage.sprite = icon; iconImage.enabled = icon != null; iconImage.preserveAspect = true; iconImage.raycastTarget = false;
        CreateText(go.transform, display, 13, FontStyles.Bold);
        CreateText(go.transform, FormatCost(cost, structure?.goldCost ?? unit.goldCost, structure?.resourceCosts ?? unit.requiredResourceCosts), 9, FontStyles.Normal);
        var status = CreateText(go.transform, string.Empty, 9, FontStyles.Normal);
        var entry = new ProductionEntry { root = go, button = button, status = status, structure = structure, unit = unit };
        button.onClick.AddListener(() => Queue(entry)); productionEntries.Add(entry);
    }

    private void RefreshProductionButtons()
    {
        foreach (var entry in productionEntries)
        {
            if (entry.button == null) continue;
            bool available; string reason;
            if (entry.structure != null) available = band.CanQueueStructure(entry.structure, out reason);
            else available = band.CanQueueMilitaryUnit(entry.unit, out reason);
            entry.button.interactable = available;
            entry.status.text = available ? "AVAILABLE" : reason.ToUpperInvariant();
        }
    }

    private void RefreshGarrison()
    {
        selectedGarrison.RemoveWhere(x => x == null || !band.Garrison.Contains(x));
        Set(garrisonText, $"Garrison: {band.Garrison.Count}/{band.GarrisonCapacity}");
        ClearChildren(garrisonEntriesRoot);
        foreach (var unit in band.Garrison.Where(x => x != null))
        {
            var go = new GameObject("Garrison Unit", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(garrisonEntriesRoot, false);
            CreateText(go.transform, unit.UnitName, 12, FontStyles.Normal);
            bool selected = selectedGarrison.Contains(unit); go.GetComponent<Image>().color = selected ? new Color(.55f, .42f, .12f) : new Color(.16f, .16f, .16f);
            go.GetComponent<Button>().onClick.AddListener(() => { if (!selectedGarrison.Add(unit)) selectedGarrison.Remove(unit); RefreshGarrison(); });
        }
        nearbyArmies.Clear();
        if (band.Owner?.combatUnits != null) nearbyArmies.AddRange(band.Owner.combatUnits.Where(x => x != null && !x.isStored && CampaignArmyService.IsRepresentative(x) && x.planetIndex == band.PlanetIndex && x.currentLayer == TileLayer.Surface && x.currentTileIndex == band.CurrentTileIndex));
        if (nearbyArmyDropdown != null) { nearbyArmyDropdown.ClearOptions(); nearbyArmyDropdown.AddOptions(nearbyArmies.Select(x => x.MilitaryFormationName ?? x.UnitName).ToList()); nearbyArmyDropdown.interactable = nearbyArmies.Count > 1; }
        SetAction(formArmyButton, selectedGarrison.Count > 0 && selectedGarrison.Count <= (band.Owner?.GetMaxArmySize() ?? CampaignArmyService.DefaultArmySize));
        SetAction(garrisonArmyButton, nearbyArmies.Count > 0);
    }

    private void Queue(ProductionEntry entry)
    {
        bool success = entry.structure != null ? band.QueueStructure(entry.structure) : band.QueueMilitaryUnit(entry.unit);
        if (!success) ShowMessage("Production could not be started.");
        Refresh();
    }

    private void SelectAll() { selectedGarrison.Clear(); foreach (var unit in band.Garrison.Where(x => x != null)) selectedGarrison.Add(unit); RefreshGarrison(); }
    private void DeselectAll() { selectedGarrison.Clear(); RefreshGarrison(); }
    private void FormArmy() { if (!band.FormArmy(selectedGarrison.ToList(), out _)) ShowMessage("Selected units cannot form an army."); else { selectedGarrison.Clear(); ShowMessage(string.Empty); } Refresh(); }
    private void GarrisonArmy() { int index = nearbyArmyDropdown != null ? nearbyArmyDropdown.value : 0; if (index < 0 || index >= nearbyArmies.Count) { ShowMessage("No eligible army is present."); return; } if (!band.TryGarrisonArmy(nearbyArmies[index], out string reason)) ShowMessage(reason); else ShowMessage(string.Empty); Refresh(); }
    private void FoundSettlement() { City city = band.FoundSettlement(out string reason); if (city == null) { ShowMessage(reason); return; } Hide(); UIManager.Instance?.ShowPanel("CityPanel"); var panel = UIManager.Instance?.GetPanel("CityPanel"); panel?.GetComponent<CityUI>()?.ShowForCity(city); }

    private void Act(System.Func<bool> action, string failure) { if (band == null) return; if (!action()) ShowMessage(failure); Refresh(); }
    private void OnBandChanged(Band changed) { if (changed == band) Refresh(); }
    private void OnBandDestroyed(Band destroyed, BandLossReason reason) { if (destroyed != band) return; UnitSelectionManager.Instance?.DeselectBand(); Hide(); }
    private void ShowMessage(string message) { Set(garrisonMessageText, message); if (!string.IsNullOrEmpty(message)) UIManager.Instance?.ShowNotification(message); }
    private void ClearProductionEntries() { foreach (var entry in productionEntries) if (entry.root != null) Destroy(entry.root); productionEntries.Clear(); }
    private static void ClearChildren(Transform root) { if (root == null) return; for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject); }
    private TMP_Text CreateText(Transform parent, string value, float size, FontStyles style) { var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); var text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = size; text.fontStyle = style; text.color = Color.white; text.raycastTarget = false; if (titleText != null) text.font = titleText.font; return text; }
    private static string FormatCost(int production, int gold, ResourceCost[] resources) { var parts = new List<string> { $"{production} Production" }; if (gold > 0) parts.Add($"{gold} Gold"); if (resources != null) parts.AddRange(resources.Where(x => x?.resource != null && x.amount > 0).Select(x => $"{x.amount} {x.resource.resourceName}")); return string.Join(" • ", parts); }
    private static void Wire(Button button, UnityEngine.Events.UnityAction action) { if (button == null) return; button.onClick.RemoveAllListeners(); button.onClick.AddListener(action); }
    private static void SetAction(Button button, bool enabled) { if (button != null) button.interactable = enabled; }
    private static void Set(TMP_Text target, string value) { if (target != null) target.text = value; }
}
