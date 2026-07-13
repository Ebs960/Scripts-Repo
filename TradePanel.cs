using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class TradePanel : MonoBehaviour
{
    [Header("Panel References")]
    [Tooltip("Main trade panel container")]
    public GameObject tradePanel;
    
    [Header("Trade Route UI")]
    [Tooltip("Panel for creating new trade routes")]
    public GameObject newTradeRoutePanel;
    [Tooltip("Toggle between city and interplanetary trade")]
    public Toggle interplanetaryToggle;
    [Tooltip("Dropdown to select source city")]
    public TMP_Dropdown sourceCityDropdown;
    [Tooltip("Dropdown to select destination city")]
    public TMP_Dropdown destinationCityDropdown;
    [Tooltip("Dropdown to select origin planet (interplanetary mode)")]
    public TMP_Dropdown originPlanetDropdown;
    [Tooltip("Dropdown to select destination planet (interplanetary mode)")]
    public TMP_Dropdown destinationPlanetDropdown;
    [Tooltip("Button to establish trade route")]
    public Button establishTradeRouteButton;
    [Tooltip("Text showing estimated trade route benefits")]
    public TextMeshProUGUI routeBenefitsText;
    
    [Header("Active Routes UI")]
    [Tooltip("Container for listing active trade routes")]
    public GameObject activeRoutesPanel;
    [Tooltip("Prefab for trade route list items")]
    public GameObject tradeRouteItemPrefab;
    [Tooltip("Parent transform for trade route items")]
    public Transform tradeRouteListContent;
    
    [Header("Trade Details")]
    [Tooltip("Text showing total gold from trade routes")]
    public TextMeshProUGUI totalTradeGoldText;
    [Tooltip("Text showing total food from trade routes")]
    public TextMeshProUGUI totalTradeFoodText;
    [Tooltip("Text showing total production from trade routes")]
    public TextMeshProUGUI totalTradeProductionText;
    
    // Current data
    private Civilization playerCiv;
    private List<City> availableSourceCities = new List<City>();
    private List<City> availableDestinationCities = new List<City>();
    private List<int> availableSourceNodeIds = new List<int>();
    private List<int> availableDestinationNodeIds = new List<int>();
    private bool isInterplanetaryMode = false;
    
    void Start()
    {
        // Set up event listeners
        establishTradeRouteButton.onClick.AddListener(OnEstablishTradeRouteClicked);
        
        // Set up dropdown change listeners
        sourceCityDropdown.onValueChanged.AddListener(OnSourceCitySelected);
        destinationCityDropdown.onValueChanged.AddListener(OnDestinationCitySelected);
        
        // Unified node workflow replaces the old city/interplanetary toggle.
        if (interplanetaryToggle != null)
        {
            interplanetaryToggle.gameObject.SetActive(false);
        }
        if (originPlanetDropdown != null) originPlanetDropdown.gameObject.SetActive(false);
        if (destinationPlanetDropdown != null) destinationPlanetDropdown.gameObject.SetActive(false);
        
        // Hide panel initially
        if (tradePanel == null)
            tradePanel = this.gameObject;
            
        tradePanel.SetActive(false);
    }
    
    /// <summary>
    /// Show the trade panel for the given civilization
    /// </summary>
    public void Show(Civilization playerCiv)
    {
        this.playerCiv = playerCiv;
        UpdateUIState();
        if (tradePanel == null)
            tradePanel = this.gameObject;
        tradePanel.SetActive(true);
    }
    
    /// <summary>
    /// Hide the trade panel
    /// </summary>
    public void Hide()
    {
        if (tradePanel == null)
            tradePanel = this.gameObject;
        tradePanel.SetActive(false);
    }
    
    /// <summary>
    /// Update all UI elements based on current trade state
    /// </summary>
    private void UpdateUIState()
    {
        if (playerCiv == null) return;
        
        // Update available source cities (cities with trading capacity)
        UpdateAvailableSourceCities();
        
        // Update available destination cities
        UpdateAvailableDestinationCities();
        
        // Update active trade routes display
        UpdateActiveTradeRoutes();
        
        // Update trade totals
        UpdateTradeTotals();
    }
    
    /// <summary>
    /// Update the list of cities that can initiate trade routes
    /// </summary>
    private void UpdateAvailableSourceCities()
    {
        var manager = TradeNetworkManager.EnsureInstance();
        manager.RebuildRegistry();
        availableSourceCities.Clear();
        availableSourceNodeIds.Clear();
        sourceCityDropdown.ClearOptions();

        var labels = new List<string>();
        bool civHasCapacity = manager.HasCivilizationRouteCapacity(playerCiv);
        foreach (var node in manager.allTradeNodes)
        {
            if (!civHasCapacity || node.ownerCivilizationId != playerCiv.GetRuntimeId() || !node.canOriginateRoutes) continue;
            availableSourceNodeIds.Add(node.nodeId);
            if (node.city != null) availableSourceCities.Add(node.city);
            labels.Add($"{node.displayName} ({node.nodeType}, P{node.location.planetId})");
        }

        if (labels.Count > 0) { sourceCityDropdown.AddOptions(labels); OnSourceCitySelected(0); }
        else { sourceCityDropdown.AddOptions(new List<string> { "No Eligible Trade Nodes" }); establishTradeRouteButton.interactable = false; }
    }

    private void UpdateAvailableDestinationCities()
    {
        var manager = TradeNetworkManager.EnsureInstance();
        if (sourceCityDropdown.value < 0 || sourceCityDropdown.value >= availableSourceNodeIds.Count) return;
        int sourceNodeId = availableSourceNodeIds[sourceCityDropdown.value];
        availableDestinationCities.Clear();
        availableDestinationNodeIds.Clear();
        destinationCityDropdown.ClearOptions();

        var labels = new List<string>();
        foreach (var node in manager.allTradeNodes)
        {
            if (node.nodeId == sourceNodeId || !node.canReceiveRoutes) continue;
            var preview = manager.PreviewRoute(sourceNodeId, node.nodeId, playerCiv);
            if (preview == null || preview.suspended) continue;
            availableDestinationNodeIds.Add(node.nodeId);
            if (node.city != null) availableDestinationCities.Add(node.city);
            labels.Add($"{node.displayName} ({node.nodeType}, P{node.location.planetId})");
        }
        if (labels.Count > 0) { destinationCityDropdown.AddOptions(labels); OnDestinationCitySelected(0); }
        else { destinationCityDropdown.AddOptions(new List<string> { "No Reachable Destinations" }); establishTradeRouteButton.interactable = false; }
    }

    /// <summary>
    /// Update the display of active trade routes
    /// </summary>
    private void UpdateActiveTradeRoutes()
    {
        // Clear existing trade route items
        if (tradeRouteListContent != null)
        {
            foreach (Transform child in tradeRouteListContent)
            {
                Destroy(child.gameObject);
            }
            
            // Create items for every unified trade-network route.
            if (TradeNetworkManager.Instance != null)
            {
                foreach (TradeRoute route in TradeNetworkManager.Instance.GetRoutesForCivilization(playerCiv))
                {
                    GameObject item = Instantiate(tradeRouteItemPrefab, tradeRouteListContent);
                    UpdateTradeRouteItem(item, route);
                }
            }
        }
    }
    
    
    /// <summary>
    /// Update the display of a trade route list item
    /// </summary>
    private void UpdateTradeRouteItem(GameObject item, TradeRoute route)
    {
        // Assuming the prefab has these components
        var sourceText = item.transform.Find("SourceText")?.GetComponent<TextMeshProUGUI>();
        var destText = item.transform.Find("DestinationText")?.GetComponent<TextMeshProUGUI>();
        var benefitsText = item.transform.Find("BenefitsText")?.GetComponent<TextMeshProUGUI>();
        var cancelButton = item.transform.Find("CancelButton")?.GetComponent<Button>();
        
        string routeName = TradeNetworkManager.Instance != null ? TradeNetworkManager.Instance.GetRouteDisplayName(route) : $"Route {route.routeId}";
        if (sourceText != null)
            sourceText.text = routeName;
        if (destText != null)
            destText.text = route.suspended ? $"Suspended: {route.suspensionReason}" : "Active";
        if (benefitsText != null)
            benefitsText.text = $"+{route.goldPerTurn}g, Raid {Mathf.RoundToInt(route.raidChance * 100f)}%, Range {GetCityTradeRangeLabel(route)}";
        
        // Setup cancel button
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.interactable = true;
            cancelButton.onClick.AddListener(() => {
                if (TradeNetworkManager.Instance != null && TradeNetworkManager.Instance.activeRoutes.Remove(route))
                    UpdateUIState();
            });
        }
    }
    
    /// <summary>
    /// Update the display of total benefits from all trade routes
    /// </summary>
    private void UpdateTradeTotals()
    {
        int totalGold = 0;
        int totalFood = 0;
        int totalProduction = 0;
        if (TradeNetworkManager.Instance != null)
        {
            foreach (TradeRoute route in TradeNetworkManager.Instance.GetRoutesForCivilization(playerCiv))
            {
                if (route == null || route.suspended) continue;
                totalGold += route.goldPerTurn;
                totalFood += route.foodPerTurn;
                totalProduction += route.productionPerTurn;
            }
        }
        if (totalTradeGoldText != null) totalTradeGoldText.text = $"Total Gold: +{totalGold}";
        if (totalTradeFoodText != null) totalTradeFoodText.text = $"Total Food: +{totalFood}";
        if (totalTradeProductionText != null) totalTradeProductionText.text = $"Total Production: +{totalProduction}";
    }

    /// <summary>
    /// Called when a source city is selected
    /// </summary>
    private void OnSourceCitySelected(int index)
    {
        UpdateAvailableDestinationCities();
    }
    
    /// <summary>
    /// Called when a destination city is selected
    /// </summary>
    private void OnDestinationCitySelected(int index)
    {
        if (index < 0 || index >= availableDestinationNodeIds.Count) return;
        var manager = TradeNetworkManager.EnsureInstance();
        var source = manager.GetNode(availableSourceNodeIds[sourceCityDropdown.value]);
        var dest = manager.GetNode(availableDestinationNodeIds[index]);
        if (routeBenefitsText != null)
        {
            routeBenefitsText.text = $"{source?.displayName} → {dest?.displayName}\nPreview uses real node path, capacity, range, gateway, blockade, and risk checks.";
        }
        if (establishTradeRouteButton != null) establishTradeRouteButton.interactable = true;
    }

    /// <summary>
    /// Called when the establish trade route button is clicked
    /// </summary>
    private void OnEstablishTradeRouteClicked()
    {
        var manager = TradeNetworkManager.EnsureInstance();
        if (sourceCityDropdown.value < 0 || sourceCityDropdown.value >= availableSourceNodeIds.Count ||
            destinationCityDropdown.value < 0 || destinationCityDropdown.value >= availableDestinationNodeIds.Count)
            return;

        int sourceNodeId = availableSourceNodeIds[sourceCityDropdown.value];
        int destinationNodeId = availableDestinationNodeIds[destinationCityDropdown.value];
        if (manager.TryCreateRoute(sourceNodeId, destinationNodeId, playerCiv, out var route))
        {
            UpdateUIState();
            UIManager.Instance?.ShowNotification($"Trade route established: {manager.GetRouteDisplayName(route)}");
        }
    }

    /// <summary>
    /// Called when interplanetary toggle is changed
    /// </summary>
    private void OnInterplanetaryToggleChanged(bool isInterplanetary)
    {
        isInterplanetaryMode = isInterplanetary;
        
        // Show/hide appropriate UI elements
        if (sourceCityDropdown != null)
            sourceCityDropdown.gameObject.SetActive(!isInterplanetary);
        if (destinationCityDropdown != null)
            destinationCityDropdown.gameObject.SetActive(!isInterplanetary);
        if (originPlanetDropdown != null)
            originPlanetDropdown.gameObject.SetActive(isInterplanetary);
        if (destinationPlanetDropdown != null)
            destinationPlanetDropdown.gameObject.SetActive(isInterplanetary);
            
        UpdateUIState();
    }
    
    /// <summary>
    /// Update available planets for interplanetary trade
    /// </summary>
    private void UpdateAvailablePlanets()
    {
        if (originPlanetDropdown == null || destinationPlanetDropdown == null)
            return;
            
        originPlanetDropdown.ClearOptions();
        destinationPlanetDropdown.ClearOptions();
        
        List<string> planetNames = new List<string>();
        
        // Populate planet list from GameManager (multi-planet is the default)
        if (GameManager.Instance != null)
        {
            var planetData = GameManager.Instance.GetPlanetData();
            if (planetData != null)
            {
                foreach (var planet in planetData.Values)
                {
                    planetNames.Add(planet.planetName);
                }
            }
        }
        
        // Fallback planet names if no multi-planet system or no planets
        if (planetNames.Count == 0)
        {
            planetNames.AddRange(new[] { "Planet 1", "Planet 2", "Planet 3", "Planet 4" });
        }
        
        originPlanetDropdown.AddOptions(planetNames);
        destinationPlanetDropdown.AddOptions(planetNames);
        
        // Update benefits when planet selection changes
        UpdateInterplanetaryBenefits();
    }
    
    /// <summary>
    /// Called when origin planet is selected
    /// </summary>
    private void OnOriginPlanetSelected(int index)
    {
        UpdateInterplanetaryBenefits();
    }
    
    /// <summary>
    /// Called when destination planet is selected
    /// </summary>
    private void OnDestinationPlanetSelected(int index)
    {
        UpdateInterplanetaryBenefits();
    }
    
    /// <summary>
    /// Update the benefits display for interplanetary trade
    /// </summary>
    private void UpdateInterplanetaryBenefits()
    {
        if (routeBenefitsText == null || originPlanetDropdown == null || destinationPlanetDropdown == null)
            return;
            
        int originIndex = originPlanetDropdown.value;
        int destIndex = destinationPlanetDropdown.value;
        
        if (originIndex == destIndex)
        {
            routeBenefitsText.text = "Cannot trade with same planet";
            establishTradeRouteButton.interactable = false;
            return;
        }
        if (!TradeManager.CanSpaceTradeBetweenPlanets(originIndex, destIndex))
        {
            routeBenefitsText.text = "No valid trade gateway route exists in the trade network";
            establishTradeRouteButton.interactable = false;
            return;
        }
        
        // Calculate benefits for interplanetary trade
        TradeRoute simulatedRoute = new TradeRoute(playerCiv, originIndex, destIndex);
        routeBenefitsText.text = $"Gold: +{simulatedRoute.goldPerTurn}/turn";
        establishTradeRouteButton.interactable = true;
    }

    private string GetCityTradeConnectionLabel(TradeRoute route)
    {
        if (route == null) return "Invalid";
        if (route.usesRoadConnection) return "Road";
        if (route.usesHarborConnection) return "Harbor";
        if (route.usesAirportConnection) return "Airport";
        if (route.usesSpaceportConnection) return "Spaceport";
        return "Invalid";
    }

    private string GetCityTradeRangeLabel(TradeRoute route)
    {
        if (route == null) return "Invalid";
        return $"{route.routeDistance} cost";
    }

    private string FormatTradeResources(List<ResourceCost> resources)
    {
        if (resources == null || resources.Count == 0)
            return "None";

        List<string> names = new List<string>();
        foreach (var resource in resources)
        {
            if (resource == null || resource.resource == null || resource.amount <= 0) continue;
            names.Add($"{resource.amount} {resource.resource.resourceName}");
        }

        return names.Count > 0 ? string.Join(", ", names) : "None";
    }
}
