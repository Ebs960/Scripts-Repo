using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component that displays ongoing space travel status.
/// Shows all active interplanetary travels with progress bars and ETA.
/// </summary>
public class SpaceTravelStatusUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Content area where travel entries are instantiated")]
    public Transform travelListContent;
    
    [Tooltip("Prefab for individual travel status entries")]
    public GameObject travelEntryPrefab;
    
    [Tooltip("Panel containing the space travel status UI")]
    public GameObject statusPanel;
    
    [Tooltip("Button to toggle the status panel visibility")]
    public Button toggleButton;
    
    [Tooltip("Text on the toggle button showing travel count")]
    public TextMeshProUGUI toggleButtonText;

    [Header("Configuration")]
    [Tooltip("Auto-hide when no travels are active")]
    public bool autoHideWhenEmpty = true;
    
    [Tooltip("Update frequency in seconds")]
    public float updateInterval = 1.0f;

    // Active travel UI entries
    private Dictionary<int, GameObject> travelEntries = new Dictionary<int, GameObject>();
    private float lastUpdateTime;

    void Start()
    {
        // Setup toggle button
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleStatusPanel);
        }

        // Subscribe to space travel events
        if (SpaceRouteManager.Instance != null)
        {
            SpaceRouteManager.Instance.OnTravelStarted += OnTravelStarted;
            SpaceRouteManager.Instance.OnTravelProgressed += OnTravelProgressed;
            SpaceRouteManager.Instance.OnTravelCompleted += OnTravelCompleted;
        }

        // Initially hide if no travels
        UpdateDisplay();
    }

    void Update()
    {
        // Periodic update of travel progress
        if (Time.time - lastUpdateTime > updateInterval)
        {
            UpdateDisplay();
            lastUpdateTime = Time.time;
        }
    }

    /// <summary>
    /// Public refresh method so other systems can force an immediate UI update
    /// </summary>
    public void Refresh()
    {
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (SpaceRouteManager.Instance == null)
            return;

        var activeTravels = SpaceRouteManager.Instance.GetActiveTravels();

        // Update toggle button text
        if (toggleButtonText != null)
        {
            toggleButtonText.text = activeTravels.Count > 0 ? $"Space Travel ({activeTravels.Count})" : "Space Travel";
        }

        // Auto-hide/show based on travel count
        if (autoHideWhenEmpty && statusPanel != null)
        {
            bool shouldShow = activeTravels.Count > 0;
            if (statusPanel.activeSelf != shouldShow)
            {
                statusPanel.SetActive(shouldShow);
            }
        }

        // Update existing entries and remove completed ones
        var completedTasks = new List<int>();
        foreach (var kvp in travelEntries)
        {
            int taskId = kvp.Key;
            bool stillActive = activeTravels.Exists(t => t.taskId == taskId);
            
            if (!stillActive)
            {
                completedTasks.Add(taskId);
            }
        }

        // Remove completed entries
        foreach (int taskId in completedTasks)
        {
            if (travelEntries.TryGetValue(taskId, out GameObject entry))
            {
                Destroy(entry);
                travelEntries.Remove(taskId);
            }
        }

        // Update progress for existing entries
        foreach (var travel in activeTravels)
        {
            if (travelEntries.ContainsKey(travel.taskId))
            {
                UpdateTravelEntry(travel);
            }
        }
    }

    private void OnTravelStarted(SpaceRouteManager.SpaceTravelTask travel)
    {
        CreateTravelEntry(travel);
}

    private void OnTravelProgressed(SpaceRouteManager.SpaceTravelTask travel)
    {
        UpdateTravelEntry(travel);
    }

    private void OnTravelCompleted(SpaceRouteManager.SpaceTravelTask travel)
    {
        // Entry will be removed in UpdateDisplay()
}

    private void CreateTravelEntry(SpaceRouteManager.SpaceTravelTask travel)
    {
        if (travelEntryPrefab == null || travelListContent == null)
        {
            Debug.LogWarning("[SpaceTravelStatusUI] Cannot create travel entry - missing prefab or content area");
            return;
        }

        // Instantiate entry
        GameObject entryGO = Instantiate(travelEntryPrefab, travelListContent);
        travelEntries[travel.taskId] = entryGO;

        // Setup the entry with travel data
        SetupTravelEntry(entryGO, travel);
    }

    private void SetupTravelEntry(GameObject entryGO, SpaceRouteManager.SpaceTravelTask travel)
    {
        // Get planet names
        var planetData = GameManager.Instance?.GetPlanetData();
        string originName = planetData?.ContainsKey(travel.originPlanetIndex) == true 
            ? planetData[travel.originPlanetIndex].planetName 
            : $"Planet {travel.originPlanetIndex}";
        
        string destName = planetData?.ContainsKey(travel.destinationPlanetIndex) == true 
            ? planetData[travel.destinationPlanetIndex].planetName 
            : $"Planet {travel.destinationPlanetIndex}";

        // Setup UI elements in the entry
        var entryComponent = entryGO.GetComponent<SpaceTravelEntry>();
        if (entryComponent != null)
        {
            entryComponent.Setup(travel, originName, destName);
        }
        else
        {
            // Fallback: setup using child components directly
            SetupEntryFallback(entryGO, travel, originName, destName);
        }
    }

    private void SetupEntryFallback(GameObject entryGO, SpaceRouteManager.SpaceTravelTask travel, string originName, string destName)
    {
        // Find child UI components and set them up
        var unitNameText = entryGO.transform.Find("UnitName")?.GetComponent<TextMeshProUGUI>();
        var routeText = entryGO.transform.Find("Route")?.GetComponent<TextMeshProUGUI>();
        var progressBar = entryGO.transform.Find("ProgressBar")?.GetComponent<Slider>();
        var etaText = entryGO.transform.Find("ETA")?.GetComponent<TextMeshProUGUI>();

        if (unitNameText != null)
            unitNameText.text = travel.unitName;

        if (routeText != null)
            routeText.text = $"{originName} → {destName}";

        if (progressBar != null)
        {
            progressBar.minValue = 0f;
            progressBar.maxValue = 1f;
            progressBar.value = travel.Progress;
        }

        if (etaText != null)
            etaText.text = $"ETA: {travel.turnsRemaining} turns";
    }

    private void UpdateTravelEntry(SpaceRouteManager.SpaceTravelTask travel)
    {
        if (!travelEntries.TryGetValue(travel.taskId, out GameObject entryGO))
            return;

        // Update via component if available
        var entryComponent = entryGO.GetComponent<SpaceTravelEntry>();
        if (entryComponent != null)
        {
            entryComponent.UpdateProgress(travel);
        }
        else
        {
            // Fallback: update child components directly
            var progressBar = entryGO.transform.Find("ProgressBar")?.GetComponent<Slider>();
            var etaText = entryGO.transform.Find("ETA")?.GetComponent<TextMeshProUGUI>();

            if (progressBar != null)
                progressBar.value = travel.Progress;

            if (etaText != null)
                etaText.text = $"ETA: {travel.turnsRemaining} turns";
        }
    }

    private void ToggleStatusPanel()
    {
        if (statusPanel != null)
        {
            statusPanel.SetActive(!statusPanel.activeSelf);
        }
    }

    public void ShowStatusPanel()
    {
        if (statusPanel != null)
            statusPanel.SetActive(true);
    }

    public void HideStatusPanel()
    {
        if (statusPanel != null)
            statusPanel.SetActive(false);
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (SpaceRouteManager.Instance != null)
        {
            SpaceRouteManager.Instance.OnTravelStarted -= OnTravelStarted;
            SpaceRouteManager.Instance.OnTravelProgressed -= OnTravelProgressed;
            SpaceRouteManager.Instance.OnTravelCompleted -= OnTravelCompleted;
        }

        // Clean up button listener
        if (toggleButton != null)
            toggleButton.onClick.RemoveAllListeners();
    }
}

/// <summary>
/// Component for individual travel entry UI elements.
/// Attach this to your travel entry prefab for easier management.
/// </summary>
public class SpaceTravelEntry : MonoBehaviour
{
    [Header("UI Components")]
    public TextMeshProUGUI unitNameText;
    public TextMeshProUGUI routeText;
    public Slider progressBar;
    public TextMeshProUGUI etaText;
    public Button cancelButton;

    private int travelTaskId;

    public void Setup(SpaceRouteManager.SpaceTravelTask travel, string originName, string destName)
    {
        travelTaskId = travel.taskId;

        if (unitNameText != null)
            unitNameText.text = travel.unitName;

        if (routeText != null)
            routeText.text = $"{originName} → {destName}";

        if (progressBar != null)
        {
            progressBar.minValue = 0f;
            progressBar.maxValue = 1f;
            progressBar.value = travel.Progress;
        }

        if (etaText != null)
            etaText.text = $"ETA: {travel.turnsRemaining} turns";

        // Setup cancel button
        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(() => CancelTravel());
        }
    }

    public void UpdateProgress(SpaceRouteManager.SpaceTravelTask travel)
    {
        if (progressBar != null)
            progressBar.value = travel.Progress;

        if (etaText != null)
            etaText.text = $"ETA: {travel.turnsRemaining} turns";
    }

    private void CancelTravel()
    {
        if (SpaceRouteManager.Instance != null)
        {
            bool cancelled = SpaceRouteManager.Instance.CancelTravel(travelTaskId);
            if (cancelled)
            {
}
        }
    }

    void OnDestroy()
    {
        if (cancelButton != null)
            cancelButton.onClick.RemoveAllListeners();
    }
}
