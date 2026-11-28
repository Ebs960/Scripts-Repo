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

    [Tooltip("Unit class, current planet, and location, shown under the unit name")]
    [SerializeField] private TextMeshProUGUI unitDetailText;

    [Header("Configuration")]
    [Tooltip("Only show planets that are explored/colonized")]
    public bool onlyShowKnownPlanets = true;

    private readonly List<int> destinations = new List<int>();
    private GameObject selectedUnit;
    private int currentPlanetIndex;
    private float nextRefresh;
    private bool initialized;
    private bool showing;

    private void Awake()
    {
        Initialize();
        // This component sits on the panel it hides, so Awake can run while the panel is being opened.
        if (!showing) HideEmbarkUI();
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        if (embarkButton != null) embarkButton.onClick.AddListener(OnEmbarkButtonClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(HideEmbarkUI);
        if (destinationDropdown != null) destinationDropdown.onValueChanged.AddListener(OnDestinationChanged);
    }

    public void ShowEmbarkUI(GameObject unit, int fromPlanetIndex)
    {
        showing = true;
        Initialize();
        var ship = unit != null ? unit.GetComponent<CombatUnit>() : null;
        selectedUnit = ship != null && ship.data != null && ship.data.unitType == CombatCategory.Spaceship ? unit : null;
        currentPlanetIndex = fromPlanetIndex;
        if (selectedUnitText != null)
        {
            selectedUnitText.text = selectedUnit != null ? SpaceMapSelectionInfo.UnitName(ship) : "Select a spaceship to embark.";
            selectedUnitText.color = selectedUnit != null ? Color.white : Color.red;
        }
        RefreshUnitDetail();

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
        // The canvas may be switched off in the scene until the modal is first needed.
        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            if (!parent.gameObject.activeSelf) parent.gameObject.SetActive(true);
        if (embarkPanel != null) embarkPanel.SetActive(true);
        OnDestinationChanged(0);
    }

    public void HideEmbarkUI()
    {
        showing = false;
        selectedUnit = null;
        if (embarkPanel != null) embarkPanel.SetActive(false);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh || selectedUnit == null || destinationDropdown == null ||
            (embarkPanel != null && !embarkPanel.activeInHierarchy)) return;
        RefreshUnitDetail();
        OnDestinationChanged(destinationDropdown.value);
    }

    private void RefreshUnitDetail()
    {
        if (unitDetailText == null) return;
        var ship = selectedUnit != null ? selectedUnit.GetComponent<CombatUnit>() : null;
        if (ship == null) { unitDetailText.text = string.Empty; return; }
        var planet = SpaceMapSelectionInfo.FindPlanetByIndex(currentPlanetIndex);
        unitDetailText.text =
            SpaceMapSelectionInfo.Label("Class", SpaceMapSelectionInfo.UnitClass(ship)) + "\n" +
            SpaceMapSelectionInfo.Label("Planet", SpaceMapSelectionInfo.IsPlanetDiscovered(planet) ? planet.planetName : "Unknown") + "\n" +
            SpaceMapSelectionInfo.Label("Status", SpaceMapSelectionInfo.UnitStatus(ship));
    }

    // The route service only answers yes or no; these are the same conditions it checks, put into words.
    private string DescribeBlockedRoute(CombatUnit ship)
    {
        if (ship.isPackedInSpaceFleet) return "The ship is packed in a fleet.";
        bool fromOrbit = ship.spaceLocation.locationType == SpaceLocationType.PlanetOrbit && ship.spaceLocation.planetIndex == currentPlanetIndex;
        if (!fromOrbit && !ship.spaceLocation.IsInSolarSystemSpace)
            return ship.IsInOrbit ? "This orbit is not registered for interplanetary travel." : "Enter orbit before departing.";
        if (ship.spaceMovementPointsPerTurn <= 0) return "The ship has no space movement.";
        return "No open path to the destination's orbit.";
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
        string destinationLine = SpaceMapSelectionInfo.Label("Destination", name);
        if (manager == null)
        {
            if (travelInfoText != null) travelInfoText.text = $"{destinationLine}\n{SpaceMapSelectionInfo.Label("Route", "Route service unavailable")}";
            return;
        }

        bool routeAvailable = manager.TryGetRoute(selectedUnit, currentPlanetIndex, destinationIndex, out var path, out int destination);
        var ship = selectedUnit.GetComponent<CombatUnit>();
        bool arrived = destination >= 0 && ship.currentSpaceTileIndex == destination;
        var queuedPath = ship.queuedSpacePath;
        bool queued = destination >= 0 && !arrived && queuedPath != null && queuedPath.Count > 0 &&
            queuedPath[queuedPath.Count - 1] == destination && ship.queuedSpacePathCursor + 1 < queuedPath.Count;
        string available = SpaceMapSelectionInfo.Label("Movement available", $"{ship.currentSpaceMovementPoints}/{ship.spaceMovementPointsPerTurn}");
        if (!routeAvailable && !queued)
        {
            if (travelInfoText != null)
                travelInfoText.text = $"{destinationLine}\n{SpaceMapSelectionInfo.Label("Route", "No valid route")}\n{DescribeBlockedRoute(ship)}\n{available}";
            return;
        }

        int cursor = queued ? ship.queuedSpacePathCursor : 0;
        if (queued) path = queuedPath;
        int hexes = path.Count - cursor - 1;
        int cost = (movement ?? SpaceShipMovementController.Instance).GetRouteCost(ship, path, cursor);
        string status = arrived ? "At destination approach sector" : queued ? (routeAvailable ? "Movement queued" : "Queued route blocked") : "Route available";
        if (travelInfoText != null)
            travelInfoText.text = $"{destinationLine}\n{SpaceMapSelectionInfo.Label("Route", status)}\n" +
                $"{SpaceMapSelectionInfo.Label("Distance", $"{hexes} hex{(hexes == 1 ? string.Empty : "es")} remaining")}\n" +
                $"{SpaceMapSelectionInfo.Label("Movement required", cost.ToString())}\n{available}";
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
        var embarkUI = FindAnyObjectByType<SpaceEmbarkUI>(FindObjectsInactive.Include);
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
