using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component for embarking units on interplanetary travel.
/// Attach this to a UI panel that shows when the player wants to send units to other planets.
/// </summary>
public class SpaceEmbarkUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Dropdown to select destination planet")]
    public TMP_Dropdown destinationDropdown;
    
    [Tooltip("Button to confirm space travel")]
    public Button embarkButton;
    
    [Tooltip("Button to cancel/close this UI")]
    public Button cancelButton;
    
    [Tooltip("Text showing selected unit info")]
    public TextMeshProUGUI selectedUnitText;
    
    [Tooltip("Text showing route and movement status")]
    public TextMeshProUGUI travelInfoText;
    
    [Tooltip("Panel containing this embark UI (for show/hide)")]
    public GameObject embarkPanel;

    [Header("Configuration")]
    [Tooltip("Only show planets that are explored/colonized")]
    public bool onlyShowKnownPlanets = true;

    private readonly List<int> destinations = new List<int>();
    private GameObject selectedUnit;
    private int currentPlanetIndex;
    private float nextRefresh;

    private void Awake()
    {
        if (embarkButton != null) embarkButton.onClick.AddListener(OnEmbarkButtonClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(HideEmbarkUI);
        if (destinationDropdown != null) destinationDropdown.onValueChanged.AddListener(OnDestinationChanged);
        HideEmbarkUI();
    }

    public void ShowEmbarkUI(GameObject unit, int fromPlanetIndex)
    {
        var ship = unit != null ? unit.GetComponent<CombatUnit>() : null;
        selectedUnit = ship != null && ship.data != null && ship.data.unitType == CombatCategory.Spaceship ? unit : null;
        currentPlanetIndex = fromPlanetIndex;
        if (selectedUnitText != null)
        {
            selectedUnitText.text = selectedUnit != null ? $"Selected unit: {unit.name}" : "Select a spaceship to embark.";
            selectedUnitText.color = selectedUnit != null ? Color.white : Color.red;
        }

        destinations.Clear();
        var options = new List<TMP_Dropdown.OptionData>();
        var planets = GameManager.Instance?.GetPlanetData();
        if (selectedUnit != null && planets != null)
        {
            foreach (var pair in planets)
            {
                if (pair.Key == fromPlanetIndex || pair.Value == null) continue;
                bool known = SpaceMapSelectionInfo.IsPlanetDiscovered(pair.Value);
                if (onlyShowKnownPlanets && !known) continue;
                destinations.Add(pair.Key);
                options.Add(new TMP_Dropdown.OptionData(known ? pair.Value.planetName : $"Unexplored body {pair.Key}"));
            }
        }

        if (destinationDropdown != null)
        {
            destinationDropdown.ClearOptions();
            destinationDropdown.AddOptions(options);
            destinationDropdown.SetValueWithoutNotify(0);
            destinationDropdown.RefreshShownValue();
        }
        if (embarkPanel != null) embarkPanel.SetActive(true);
        OnDestinationChanged(0);
    }

    public void HideEmbarkUI()
    {
        selectedUnit = null;
        if (embarkPanel != null) embarkPanel.SetActive(false);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh || selectedUnit == null || destinationDropdown == null ||
            (embarkPanel != null && !embarkPanel.activeInHierarchy)) return;
        OnDestinationChanged(destinationDropdown.value);
    }

    private void OnDestinationChanged(int index)
    {
        nextRefresh = Time.unscaledTime + .25f;
        if (embarkButton != null) embarkButton.interactable = false;
        if (selectedUnit == null || destinationDropdown == null || index < 0 || index >= destinations.Count ||
            index >= destinationDropdown.options.Count)
        {
            if (travelInfoText != null) travelInfoText.text = "No destinations available";
            return;
        }

        int destinationIndex = destinations[index];
        var manager = SpaceRouteManager.Instance;
        var movement = SpaceShipMovementController.Instance;
        string name = destinationDropdown.options[index].text;
        if (manager == null)
        {
            if (travelInfoText != null) travelInfoText.text = $"Destination: {name}\nRoute service unavailable";
            return;
        }

        bool routeAvailable = manager.TryGetRoute(selectedUnit, currentPlanetIndex, destinationIndex, out var path, out int destination);
        var ship = selectedUnit.GetComponent<BaseUnit>();
        bool arrived = destination >= 0 && ship.currentSpaceTileIndex == destination;
        var queuedPath = ship.queuedSpacePath;
        bool queued = destination >= 0 && !arrived && queuedPath != null && queuedPath.Count > 0 &&
            queuedPath[queuedPath.Count - 1] == destination && ship.queuedSpacePathCursor + 1 < queuedPath.Count;
        if (!routeAvailable && !queued)
        {
            if (travelInfoText != null) travelInfoText.text = $"Destination: {name}\nNo valid route";
            return;
        }

        int cursor = queued ? ship.queuedSpacePathCursor : 0;
        if (queued) path = queuedPath;
        int hexes = path.Count - cursor - 1;
        int cost = (movement ?? SpaceShipMovementController.Instance).GetRouteCost(ship, path, cursor);
        string status = arrived ? "At destination approach sector" : queued ? (routeAvailable ? "Movement queued" : "Queued route blocked") : "Route available";
        if (travelInfoText != null)
            travelInfoText.text = $"Destination: {name}\n{status}\n{hexes} hexes remaining · {cost} movement points\nAvailable this turn: {ship.currentSpaceMovementPoints}/{ship.spaceMovementPointsPerTurn}";
        if (embarkButton != null) embarkButton.interactable = !arrived && !queued;
    }

    private void OnEmbarkButtonClicked()
    {
        if (selectedUnit == null || destinationDropdown == null ||
            destinationDropdown.value < 0 || destinationDropdown.value >= destinations.Count) return;
        int index = destinationDropdown.value;
        bool queued = SpaceRouteManager.Instance?.StartSpaceTravel(selectedUnit, currentPlanetIndex, destinations[index]) == true;
        OnDestinationChanged(index);
        if (!queued && travelInfoText != null) travelInfoText.text += "\nCould not queue movement.";
    }

    /// <summary>
    /// Static method to show embark UI from anywhere in the codebase
    /// </summary>
    public static void ShowEmbarkUIForUnit(GameObject unit, int fromPlanetIndex)
    {
        var embarkUI = FindAnyObjectByType<SpaceEmbarkUI>();
        if (embarkUI != null)
        {
            embarkUI.ShowEmbarkUI(unit, fromPlanetIndex);
        }
        else
        {
            Debug.LogWarning("[SpaceEmbarkUI] No SpaceEmbarkUI found in scene!");
        }
    }

    void OnDestroy()
    {
        // Clean up button listeners
        if (embarkButton != null)
            embarkButton.onClick.RemoveListener(OnEmbarkButtonClicked);
        
        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(HideEmbarkUI);
        
        if (destinationDropdown != null)
            destinationDropdown.onValueChanged.RemoveListener(OnDestinationChanged);
    }
}
