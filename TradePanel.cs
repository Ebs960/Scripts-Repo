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
    [Tooltip("Dropdown to select source city")]
    public TMP_Dropdown sourceCityDropdown;
    [Tooltip("Dropdown to select destination city")]
    public TMP_Dropdown destinationCityDropdown;
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
    
    void Start()
    {
        // Set up event listeners
        establishTradeRouteButton.onClick.AddListener(OnEstablishTradeRouteClicked);
        
        // Set up dropdown change listeners
        sourceCityDropdown.onValueChanged.AddListener(OnSourceCitySelected);
        destinationCityDropdown.onValueChanged.AddListener(OnDestinationCitySelected);
        
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
        availableSourceCities.Clear();
        sourceCityDropdown.ClearOptions();
        
        List<string> cityNames = new List<string>();
        
        foreach (City city in playerCiv.cities)
        {
            if (city.CanInitiateTradeRoute())
            {
                availableSourceCities.Add(city);
                cityNames.Add(city.cityName);
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
            
            // Create items for each active trade route
            foreach (City city in playerCiv.cities)
            {
                foreach (TradeRoute route in city.GetActiveTradeRoutes())
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
                    {
                        UIManager.Instance.ShowNotification($"Trade route cancelled between {route.sourceCity.cityName} and {route.destinationCity.cityName}");
                    }
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
        
        foreach (City city in playerCiv.cities)
        {
            foreach (TradeRoute route in city.GetActiveTradeRoutes())
            {
                totalGold += route.goldPerTurn;
                totalFood += route.foodPerTurn;
                totalProduction += route.productionPerTurn;
            }
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
            {
                UIManager.Instance.ShowNotification($"Trade route established from {sourceCity.cityName} to {destCity.cityName}");
            }
        }
    }
}
