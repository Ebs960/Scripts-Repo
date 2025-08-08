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
    
    [Tooltip("Text showing travel time and distance")]
    public TextMeshProUGUI travelInfoText;
    
    [Tooltip("Panel containing this embark UI (for show/hide)")]
    public GameObject embarkPanel;

    [Header("Configuration")]
    [Tooltip("Only show planets that are explored/colonized")]
    public bool onlyShowKnownPlanets = true;

    // Current state
    private GameObject selectedUnit;
    private int currentPlanetIndex;

    void Start()
    {
        // Setup button listeners
        if (embarkButton != null)
            embarkButton.onClick.AddListener(OnEmbarkButtonClicked);
        
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelButtonClicked);
        
        if (destinationDropdown != null)
            destinationDropdown.onValueChanged.AddListener(OnDestinationChanged);

        // Hide UI initially
        HideEmbarkUI();
    }

    /// <summary>
    /// Show the embark UI for the selected unit (spaceships only)
    /// </summary>
    public void ShowEmbarkUI(GameObject unit, int fromPlanetIndex)
    {
        if (unit == null)
        {
            Debug.LogWarning("[SpaceEmbarkUI] Cannot show embark UI - unit is null");
            return;
        }

        // Check if unit is a spaceship
        var combatUnit = unit.GetComponent<CombatUnit>();
        if (combatUnit == null || combatUnit.data.category != CombatCategory.Spaceship)
        {
            Debug.LogWarning($"[SpaceEmbarkUI] Only spaceships can travel through space! Unit {unit.name} is not a spaceship.");
            
            // Show user-friendly message
            if (selectedUnitText != null)
            {
                selectedUnitText.text = $"❌ Only spaceships can travel between planets!";
                selectedUnitText.color = Color.red;
            }
            
            if (embarkPanel != null)
                embarkPanel.SetActive(true);
                
            return;
        }

        selectedUnit = unit;
        currentPlanetIndex = fromPlanetIndex;

        // Update unit info display
        if (selectedUnitText != null)
        {
            selectedUnitText.text = $"Selected Unit: {unit.name}";
            selectedUnitText.color = Color.white; // Reset color for valid spaceships
        }

        // Populate destination dropdown
        PopulateDestinationDropdown();

        // Show the UI panel
        if (embarkPanel != null)
            embarkPanel.SetActive(true);

        Debug.Log($"[SpaceEmbarkUI] Showing embark UI for {unit.name} from Planet {fromPlanetIndex}");
    }

    /// <summary>
    /// Hide the embark UI
    /// </summary>
    public void HideEmbarkUI()
    {
        if (embarkPanel != null)
            embarkPanel.SetActive(false);
        
        selectedUnit = null;
        currentPlanetIndex = -1;
    }

    private void PopulateDestinationDropdown()
    {
        if (destinationDropdown == null)
            return;

        destinationDropdown.ClearOptions();
        
        var planetData = GameManager.Instance?.GetPlanetData();
        if (planetData == null)
        {
            Debug.LogError("[SpaceEmbarkUI] Cannot populate dropdown - planet data is null");
            return;
        }

        var options = new List<TMP_Dropdown.OptionData>();

        foreach (var kvp in planetData)
        {
            int planetIndex = kvp.Key;
            var planet = kvp.Value;

            // Skip current planet
            if (planetIndex == currentPlanetIndex)
                continue;

            // Filter by exploration status if enabled
            if (onlyShowKnownPlanets && !planet.isExplored)
                continue;

            // Create option text with planet info
            string optionText = $"{planet.planetName} ({planet.planetType})";
            
            // Add distance info if available
            float distance = CalculateDistance(currentPlanetIndex, planetIndex);
            if (distance > 0)
            {
                optionText += $" - {distance:F1} AU";
            }

            options.Add(new TMP_Dropdown.OptionData(optionText));
        }

        destinationDropdown.AddOptions(options);

        // Update travel info for first option
        if (options.Count > 0)
        {
            OnDestinationChanged(0);
        }
        else
        {
            if (travelInfoText != null)
                travelInfoText.text = "No destinations available";
            
            if (embarkButton != null)
                embarkButton.interactable = false;
        }
    }

    private void OnDestinationChanged(int selectedIndex)
    {
        if (selectedUnit == null || destinationDropdown == null)
            return;

        // Get the actual planet index for the selected dropdown option
        int destinationPlanetIndex = GetPlanetIndexFromDropdownSelection(selectedIndex);
        if (destinationPlanetIndex == -1)
            return;

        // Calculate and display travel info
        UpdateTravelInfo(destinationPlanetIndex);
    }

    private int GetPlanetIndexFromDropdownSelection(int dropdownIndex)
    {
        // This is a bit complex because we filtered the dropdown options
        // We need to map back to the actual planet indices
        
        var planetData = GameManager.Instance?.GetPlanetData();
        if (planetData == null)
            return -1;

        int filteredIndex = 0;
        foreach (var kvp in planetData)
        {
            int planetIndex = kvp.Key;
            var planet = kvp.Value;

            // Skip current planet
            if (planetIndex == currentPlanetIndex)
                continue;

            // Filter by exploration status if enabled
            if (onlyShowKnownPlanets && !planet.isExplored)
                continue;

            if (filteredIndex == dropdownIndex)
                return planetIndex;

            filteredIndex++;
        }

        return -1;
    }

    private void UpdateTravelInfo(int destinationPlanetIndex)
    {
        if (travelInfoText == null || selectedUnit == null)
            return;

        // Calculate travel distance and time
        float distance = CalculateDistance(currentPlanetIndex, destinationPlanetIndex);
        int travelTurns = CalculateTravelTime(distance, selectedUnit);

        var planetData = GameManager.Instance.GetPlanetData();
        string destinationName = planetData.ContainsKey(destinationPlanetIndex) 
            ? planetData[destinationPlanetIndex].planetName 
            : $"Planet {destinationPlanetIndex}";

        travelInfoText.text = $"Travel to {destinationName}:\n" +
                             $"Distance: {distance:F1} AU\n" +
                             $"Travel Time: {travelTurns} turns";

        // Enable embark button
        if (embarkButton != null)
            embarkButton.interactable = true;
    }

    private float CalculateDistance(int fromPlanet, int toPlanet)
    {
        var planetData = GameManager.Instance?.GetPlanetData();
        if (planetData == null || !planetData.ContainsKey(fromPlanet) || !planetData.ContainsKey(toPlanet))
            return 0f;

        Vector3 fromPos = planetData[fromPlanet].worldPosition;
        Vector3 toPos = planetData[toPlanet].worldPosition;

        return Vector3.Distance(fromPos, toPos);
    }

    private int CalculateTravelTime(float distance, GameObject unit)
    {
        // Use the same calculation as SpaceRouteManager
        var spaceManager = SpaceRouteManager.Instance;
        if (spaceManager == null)
            return Mathf.RoundToInt(distance * 5); // Fallback: 5 turns per AU

        // Access the calculation logic (you might need to make this public in SpaceRouteManager)
        float baseTurns = distance * spaceManager.baseTurnsPerAU;
        baseTurns *= spaceManager.technologyModifier;
        
        return Mathf.Max(spaceManager.minimumTravelTurns, Mathf.RoundToInt(baseTurns));
    }

    private void OnEmbarkButtonClicked()
    {
        if (selectedUnit == null)
        {
            Debug.LogWarning("[SpaceEmbarkUI] Cannot embark - no unit selected");
            return;
        }

        if (destinationDropdown == null || destinationDropdown.options.Count == 0)
        {
            Debug.LogWarning("[SpaceEmbarkUI] Cannot embark - no destination selected");
            return;
        }

        // Get destination planet index
        int destinationIndex = GetPlanetIndexFromDropdownSelection(destinationDropdown.value);
        if (destinationIndex == -1)
        {
            Debug.LogError("[SpaceEmbarkUI] Cannot embark - invalid destination");
            return;
        }

        // Start space travel via SpaceRouteManager
        bool success = SpaceRouteManager.Instance?.StartSpaceTravel(selectedUnit, currentPlanetIndex, destinationIndex) ?? false;

        if (success)
        {
            Debug.Log($"[SpaceEmbarkUI] Successfully started space travel for {selectedUnit.name}");
            HideEmbarkUI();
            
            // You might want to show a confirmation message or update the UI
            // ShowTravelConfirmation(selectedUnit.name, destinationIndex);
        }
        else
        {
            Debug.LogError("[SpaceEmbarkUI] Failed to start space travel");
            // Show error message to player
        }
    }

    private void OnCancelButtonClicked()
    {
        HideEmbarkUI();
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
            embarkButton.onClick.RemoveAllListeners();
        
        if (cancelButton != null)
            cancelButton.onClick.RemoveAllListeners();
        
        if (destinationDropdown != null)
            destinationDropdown.onValueChanged.RemoveAllListeners();
    }
}
