// Assets/Scripts/UI/CityCitizenAssignmentPanel.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CityCitizenAssignmentPanel : MonoBehaviour
{
    [Header("Summary")]
    [SerializeField] private TextMeshProUGUI cityNameText;
    [SerializeField] private TextMeshProUGUI jobSummaryText;
    [SerializeField] private TextMeshProUGUI orderCrimeText;

    [Header("Selected Tile")]
    [SerializeField] private GameObject selectedTilePanel;
    [SerializeField] private TextMeshProUGUI selectedTileTitleText;
    [SerializeField] private TextMeshProUGUI selectedTileYieldText;
    [SerializeField] private TextMeshProUGUI selectedTileAssignmentText;

    [Header("Buttons")]
    [SerializeField] private Button assignTileWorkerButton;
    [SerializeField] private Button unassignTileButton;
    [SerializeField] private Button lockTileButton;

    [Header("Rural Specialist Buttons")]
    [SerializeField] private Transform ruralSpecialistButtonContainer;
    [SerializeField] private GameObject specialistButtonPrefab;

    [Header("Urban Specialists")]
    [SerializeField] private Transform urbanSpecialistContainer;
    [SerializeField] private GameObject urbanSpecialistRowPrefab;

    private City currentCity;
    private int selectedTileIndex = -1;

    public void ShowForCity(City city) { currentCity = city; selectedTileIndex = -1; gameObject.SetActive(true); Refresh(); }
    public void ShowForTile(City city, int tileIndex) { currentCity = city; selectedTileIndex = tileIndex; gameObject.SetActive(true); Refresh(); }
    public void Hide() { gameObject.SetActive(false); currentCity = null; selectedTileIndex = -1; }

    public void Refresh()
    {
        if (currentCity == null) return;
        currentCity.RecalculateCitizenAssignmentCaches();
        if (cityNameText != null) cityNameText.text = currentCity.cityName;
        RefreshJobSummary();
        RefreshOrderCrime();
        RefreshSelectedTile();
        RefreshUrbanSpecialistRows();
    }

    private void RefreshJobSummary()
    {
        int tileWorkers = currentCity.GetAssignedCount(CityCitizenJobType.TileWorker);
        int rural = currentCity.GetAssignedCount(CityCitizenJobType.RuralSpecialist);
        int urban = currentCity.GetAssignedCount(CityCitizenJobType.UrbanSpecialist);
        int unemployed = currentCity.GetUnemployedCount();
        if (jobSummaryText != null)
            jobSummaryText.text = $"Tile Workers: {tileWorkers}\nRural Specialists: {rural}\nUrban Specialists: {urban}\nUnemployed: {unemployed}";
    }

    private void RefreshOrderCrime()
    {
        if (orderCrimeText == null) return;
        orderCrimeText.text = $"Order: {currentCity.orderRating}/{currentCity.maxOrder}\nUnemployment Penalty: -{currentCity.GetUnemploymentOrderPenaltyPerTurn()}/turn\nBandit Risk: +{currentCity.CachedBanditRiskFromUnemployment}";
    }

    private void RefreshSelectedTile()
    {
        if (selectedTilePanel != null) selectedTilePanel.SetActive(selectedTileIndex >= 0);
        ClearChildren(ruralSpecialistButtonContainer);
        if (selectedTileIndex < 0) return;
        var ts = TileSystem.GetForPlanet(currentCity.planetIndex) ?? TileSystem.Instance;
        var td = ts != null ? ts.GetTileData(selectedTileIndex) : null;
        if (td == null) return;
        if (selectedTileTitleText != null) selectedTileTitleText.text = td.improvement != null ? td.improvement.improvementName : td.biome.ToString();
        if (selectedTileYieldText != null) selectedTileYieldText.text = BuildSelectedTileYieldText(td);
        var assignment = currentCity.GetTileAssignment(selectedTileIndex);
        if (selectedTileAssignmentText != null) selectedTileAssignmentText.text = assignment != null ? assignment.GetDebugLabel() : "Unassigned";
        WireSelectedTileButtons();
        RefreshRuralSpecialistButtons(td);
    }

    private string BuildSelectedTileYieldText(HexTileData td)
    {
        int food = td.food, production = td.production, gold = td.gold, science = td.science, culture = td.culture, faith = td.faithYield;
        if (td.improvement != null)
        {
            food += td.improvement.foodPerTurn;
            production += td.improvement.productionPerTurn;
            gold += td.improvement.goldPerTurn;
            science += td.improvement.sciencePerTurn;
            culture += td.improvement.culturePerTurn;
            faith += td.improvement.faithPerTurn;
        }
        return $"Food {food}, Prod {production}, Gold {gold}, Sci {science}, Cul {culture}, Faith {faith}";
    }

    private void WireSelectedTileButtons()
    {
        if (assignTileWorkerButton != null)
        {
            assignTileWorkerButton.onClick.RemoveAllListeners();
            assignTileWorkerButton.onClick.AddListener(() => { string reason; if (!currentCity.AssignTileWorker(selectedTileIndex, out reason)) UIManager.Instance?.ShowNotification(reason); RefreshAll(); });
        }
        if (unassignTileButton != null)
        {
            unassignTileButton.onClick.RemoveAllListeners();
            unassignTileButton.onClick.AddListener(() => { currentCity.RemoveAssignmentFromTile(selectedTileIndex); RefreshAll(); });
        }
        if (lockTileButton != null)
        {
            lockTileButton.onClick.RemoveAllListeners();
            lockTileButton.onClick.AddListener(() => { var assignment = currentCity.GetTileAssignment(selectedTileIndex); if (assignment != null) currentCity.SetTileAssignmentLocked(selectedTileIndex, !assignment.locked); RefreshAll(); });
        }
    }

    private void RefreshRuralSpecialistButtons(HexTileData td)
    {
        if (ruralSpecialistButtonContainer == null || specialistButtonPrefab == null) return;
        if (td == null || td.improvementInstanceObject == null) return;
        var instance = td.improvementInstanceObject.GetComponent<ImprovementInstance>();
        if (instance == null) return;
        foreach (var slot in instance.GetActiveRuralSpecialistSlots())
        {
            if (slot == null) continue;
            var go = Instantiate(specialistButtonPrefab, ruralSpecialistButtonContainer);
            var text = go.GetComponentInChildren<TextMeshProUGUI>();
            var button = go.GetComponent<Button>();
            if (text != null) text.text = $"{slot.displayName}\n{FormatSpecialistYield(slot)}";
            if (button != null) button.onClick.AddListener(() => { string reason; if (!currentCity.AssignRuralSpecialist(selectedTileIndex, slot.slotId, out reason)) UIManager.Instance?.ShowNotification(reason); RefreshAll(); });
        }
    }

    private void RefreshUrbanSpecialistRows()
    {
        if (urbanSpecialistContainer == null || urbanSpecialistRowPrefab == null) return;
        ClearChildren(urbanSpecialistContainer);

        foreach (var building in currentCity.GetBuildings())
        {
            if (building == null || building.urbanSpecialistSlots == null) continue;
            foreach (var slot in building.urbanSpecialistSlots)
                CreateUrbanSpecialistRow(building, null, slot);
        }

        foreach (var district in currentCity.GetDistricts())
        {
            if (district == null || district.urbanSpecialistSlots == null) continue;
            foreach (var slot in district.urbanSpecialistSlots)
                CreateUrbanSpecialistRow(null, district, slot);
        }
    }

    private void CreateUrbanSpecialistRow(BuildingData building, DistrictData district, SpecialistSlotDefinition slot)
    {
        if (slot == null) return;

        var go = Instantiate(urbanSpecialistRowPrefab, urbanSpecialistContainer);
        var text = go.GetComponentInChildren<TextMeshProUGUI>();
        var button = go.GetComponent<Button>();
        bool assigned = building != null
            ? IsUrbanSlotAssigned(building, slot.slotId)
            : IsUrbanSlotAssigned(district, slot.slotId);
        string sourceName = building != null ? building.buildingName : district.districtName;

        if (text != null)
            text.text = $"{sourceName}: {slot.displayName}\n{FormatSpecialistYield(slot)}\n{(assigned ? "Assigned - click to unassign" : "Empty - click to assign")}";

        if (button != null)
        {
            button.interactable = true;
            button.onClick.AddListener(() =>
            {
                if (assigned)
                {
                    if (building != null) currentCity.RemoveUrbanSpecialist(building, slot.slotId);
                    else currentCity.RemoveUrbanSpecialist(district, slot.slotId);
                }
                else
                {
                    string reason;
                    bool success = building != null
                        ? currentCity.AssignUrbanSpecialist(building, slot.slotId, out reason)
                        : currentCity.AssignUrbanSpecialist(district, slot.slotId, out reason);
                    if (!success) UIManager.Instance?.ShowNotification(reason);
                }
                RefreshAll();
            });
        }
    }

    private bool IsUrbanSlotAssigned(BuildingData building, string slotId)
    {
        foreach (var assignment in currentCity.CitizenAssignments)
            if (assignment != null && assignment.jobType == CityCitizenJobType.UrbanSpecialist && assignment.building == building && assignment.specialistSlotId == slotId) return true;
        return false;
    }

    private bool IsUrbanSlotAssigned(DistrictData district, string slotId)
    {
        foreach (var assignment in currentCity.CitizenAssignments)
            if (assignment != null && assignment.jobType == CityCitizenJobType.UrbanSpecialist && assignment.district == district && assignment.specialistSlotId == slotId) return true;
        return false;
    }

    private static string FormatSpecialistYield(SpecialistSlotDefinition slot)
    {
        if (slot == null) return "";
        return $"F{slot.food} P{slot.production} G{slot.gold} S{slot.science} C{slot.culture} Faith{slot.faith} Policy{slot.policyPoints} Order{slot.order}";
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent) Destroy(child.gameObject);
    }

    private void RefreshAll()
    {
        CityTileOverlayController.Instance?.RefreshOverlay();
        Refresh();
    }
}
