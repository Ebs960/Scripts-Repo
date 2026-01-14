using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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
    private bool isInterplanetaryMode = false;
    
    void Start()
    {
        // Set up event listeners
        establishTradeRouteButton.onClick.AddListener(OnEstablishTradeRouteClicked);
        
        // Set up dropdown change listeners
        sourceCityDropdown.onValueChanged.AddListener(OnSourceCitySelected);
        destinationCityDropdown.onValueChanged.AddListener(OnDestinationCitySelected);
        
        // Set up interplanetary trade listeners
        if (interplanetaryToggle != null)
        {
            interplanetaryToggle.onValueChanged.AddListener(OnInterplanetaryToggleChanged);
        }
        if (originPlanetDropdown != null)
        {
            originPlanetDropdown.onValueChanged.AddListener(OnOriginPlanetSelected);
        }
        if (destinationPlanetDropdown != null)
        {
            destinationPlanetDropdown.onValueChanged.AddListener(OnDestinationPlanetSelected);
        }
        
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
        if (isInterplanetaryMode)
        {
            // For interplanetary trade, populate planet dropdowns
            UpdateAvailablePlanets();
            return;
        }
        
        availableSourceCities.Clear();
        sourceCityDropdown.ClearOptions();
        
        List<string> cityNames = new List<string>();
        
        foreach (City city in playerCiv.cities)
        {
            if (city.CanInitiateTradeRoute())
            {
                availableSourceCities.Add(city);
                cityNames.Add($"{city.cityName}");
            }
        }
        
        if (cityNames.Count > 0)
        {
            sourceCityDropdown.AddOptions(cityNames);
            OnSourceCitySelected(0);
        }
        else
        {
            sourceCityDropdown.AddOptions(new List<string> { "No Trading Cities" });
            establishTradeRouteButton.interactable = false;
        }
    }
    
    /// <summary>
    /// Update the list of possible trade destinations based on selected source city
    /// </summary>
    private void UpdateAvailableDestinationCities()
    {
        if (sourceCityDropdown.value < 0 || sourceCityDropdown.value >= availableSourceCities.Count)
            return;
            
        City sourceCity = availableSourceCities[sourceCityDropdown.value];
        
        availableDestinationCities.Clear();
        destinationCityDropdown.ClearOptions();
        
        List<string> cityNames = new List<string>();
        
        // Get all cities within trade range (including other civilizations' cities)
        var citiesInRange = sourceCity.GetCitiesInTradeRange();
        
        foreach (City city in citiesInRange)
        {
            if (city != sourceCity && !sourceCity.HasTradeRouteWith(city))
            {
                availableDestinationCities.Add(city);
                cityNames.Add($"{city.cityName} ({city.owner.civData.civName})");
            }
        }
        
        if (cityNames.Count > 0)
        {
            destinationCityDropdown.AddOptions(cityNames);
            OnDestinationCitySelected(0);
        }
        else
        {
            destinationCityDropdown.AddOptions(new List<string> { "No Available Destinations" });
            establishTradeRouteButton.interactable = false;
        }
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
            
            // Create items for city trade routes
            foreach (City city in playerCiv.cities)
            {
                foreach (TradeRoute route in city.GetActiveTradeRoutes())
                {
                    GameObject item = Instantiate(tradeRouteItemPrefab, tradeRouteListContent);
                    UpdateTradeRouteItem(item, route);
                }
            }
            
            // Create items for interplanetary trade routes
            foreach (TradeRoute route in playerCiv.GetInterplanetaryTradeRoutes())
            {
                GameObject item = Instantiate(tradeRouteItemPrefab, tradeRouteListContent);
                UpdateTradeRouteItem(item, route);
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
        
        if (sourceText != null)
            sourceText.text = route.sourceCity.cityName;
        if (destText != null)
            destText.text = route.destinationCity.cityName;
        if (benefitsText != null)
            benefitsText.text = $"+{route.goldPerTurn}g, +{route.foodPerTurn}f, +{route.productionPerTurn}p";
        
        // Setup cancel button
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() => {
                if (route.sourceCity.CancelTradeRoute(route.destinationCity))
                {
                    UpdateUIState();
                    if (UIManager.Instance != null)
                        UIManager.Instance.ShowNotification($"Trade route cancelled between {route.sourceCity.cityName} and {route.destinationCity.cityName}");
                }
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
        
        // Add city trade routes
        foreach (City city in playerCiv.cities)
        {
            foreach (TradeRoute route in city.GetActiveTradeRoutes())
            {
                totalGold += route.goldPerTurn;
                totalFood += route.foodPerTurn;
                totalProduction += route.productionPerTurn;
            }
        }
        
        // Add interplanetary trade routes
        foreach (TradeRoute route in playerCiv.GetInterplanetaryTradeRoutes())
        {
            totalGold += route.goldPerTurn;
            totalFood += route.foodPerTurn;
            totalProduction += route.productionPerTurn;
        }
        
        if (totalTradeGoldText != null)
            totalTradeGoldText.text = $"Total Gold: +{totalGold}";
        if (totalTradeFoodText != null)
            totalTradeFoodText.text = $"Total Food: +{totalFood}";
        if (totalTradeProductionText != null)
            totalTradeProductionText.text = $"Total Production: +{totalProduction}";
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
        if (index < 0 || index >= availableDestinationCities.Count)
            return;
            
        City sourceCity = availableSourceCities[sourceCityDropdown.value];
        City destCity = availableDestinationCities[index];
        
        // Calculate and display estimated trade route benefits
        var benefits = TradeRoute.CalculateTradeRouteBenefits(sourceCity, destCity);
        
        if (routeBenefitsText != null)
        {
            routeBenefitsText.text = $"Estimated Benefits:\n" +
                                    $"Gold: +{benefits.goldPerTurn}\n" +
                                    $"Food: +{benefits.foodPerTurn}\n" +
                                    $"Production: +{benefits.productionPerTurn}";
        }
        
        if (establishTradeRouteButton != null)
            establishTradeRouteButton.interactable = true;
    }
    
    /// <summary>
    /// Called when the establish trade route button is clicked
    /// </summary>
    private void OnEstablishTradeRouteClicked()
    {
        if (isInterplanetaryMode)
        {
            // Handle interplanetary trade route creation
            if (originPlanetDropdown.value < 0 || destinationPlanetDropdown.value < 0)
                return;
                
            int originPlanet = originPlanetDropdown.value;
            int destPlanet = destinationPlanetDropdown.value;
            
            if (originPlanet == destPlanet)
            {
return;
            }
            
            // Create interplanetary trade route
            TradeRoute newRoute = new TradeRoute(playerCiv, originPlanet, destPlanet);
            playerCiv.AddTradeRoute(newRoute);
UpdateUIState();
        }
        else
        {
            // Handle city trade route creation (original code)
            if (sourceCityDropdown.value < 0 || sourceCityDropdown.value >= availableSourceCities.Count ||
                destinationCityDropdown.value < 0 || destinationCityDropdown.value >= availableDestinationCities.Count)
                return;
                
            City sourceCity = availableSourceCities[sourceCityDropdown.value];
            City destCity = availableDestinationCities[destinationCityDropdown.value];
            
            // Attempt to establish the trade route
            if (sourceCity.EstablishTradeRoute(destCity))
            {
                // Update UI if successful
                UpdateUIState();
                
                // Show notification
                if (UIManager.Instance != null)
                    UIManager.Instance.ShowNotification($"Trade route established from {sourceCity.cityName} to {destCity.cityName}");
            }
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
        
        // Calculate benefits for interplanetary trade
        TradeRoute simulatedRoute = new TradeRoute(playerCiv, originIndex, destIndex);
        routeBenefitsText.text = $"Gold: +{simulatedRoute.goldPerTurn}/turn";
        establishTradeRouteButton.interactable = true;
    }
}
