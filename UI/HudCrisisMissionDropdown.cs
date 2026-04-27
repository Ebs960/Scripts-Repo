// Assets/Scripts/UI/HudCrisisMissionDropdown.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Crisis and mission tracking dropdown for the HUD.
/// Displays active crises and missions in a collapsible dropdown.
/// 
/// Uses prefab-driven content to avoid hardcoding UI structure.
/// References existing CrisisMissionTrackerUI for data/events.
/// </summary>
public class HudCrisisMissionDropdown : MonoBehaviour
{
    [SerializeField] private HudDropdownButton dropdownButton;

    [Header("Content Prefabs")]
    [SerializeField] private GameObject missionSummaryItemPrefab;
    [SerializeField] private GameObject crisisSummaryItemPrefab;
    [SerializeField] private GameObject notificationItemPrefab;
    [SerializeField] private GameObject detailPopupPrefab;

    private CrisisMissionTrackerUI trackerUI;
    private Civilization currentCiv;
    private GameObject contentRoot;

    private void Start()
    {
        if (dropdownButton == null)
        {
            Debug.LogWarning("HudCrisisMissionDropdown: dropdownButton not assigned");
            return;
        }

        // Find existing tracker
        trackerUI = UnityEngine.Object.FindFirstObjectByType<CrisisMissionTrackerUI>();

        // Configure dropdown
        dropdownButton.Bind("Crisis & Missions", null, OpenCrisisMissionPanel);

        // Subscribe to crisis/mission events
        if (CrisisManager.Instance != null)
        {
            CrisisManager.Instance.OnCrisisStarted += HandleCrisisChanged;
            CrisisManager.Instance.OnCrisisEnded += HandleCrisisChanged;
            CrisisManager.Instance.OnMissionStarted += HandleMissionStarted;
            CrisisManager.Instance.OnMissionCompleted += HandleMissionCompleted;
            CrisisManager.Instance.OnObjectiveCompleted += HandleObjectiveCompleted;
        }

        // Initial populate
        RefreshDropdownContent();
    }

    private void OnDestroy()
    {
        if (CrisisManager.Instance != null)
        {
            CrisisManager.Instance.OnCrisisStarted -= HandleCrisisChanged;
            CrisisManager.Instance.OnCrisisEnded -= HandleCrisisChanged;
            CrisisManager.Instance.OnMissionStarted -= HandleMissionStarted;
            CrisisManager.Instance.OnMissionCompleted -= HandleMissionCompleted;
            CrisisManager.Instance.OnObjectiveCompleted -= HandleObjectiveCompleted;
        }

        if (contentRoot != null)
            Destroy(contentRoot);
    }

    /// <summary>
    /// Refresh dropdown body content with current crises and missions.
    /// </summary>
    private void RefreshDropdownContent()
    {
        if (dropdownButton == null || currentCiv == null) return;

        // Get active crises and missions
        var crises = GetActiveCrises();
        var missions = GetActiveMissions();

        if (contentRoot != null)
            Destroy(contentRoot);

        contentRoot = new GameObject("CrisisMissionContent");
        var layout = contentRoot.AddComponent<VerticalLayoutGroup>();
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        // Add crisis items
        foreach (var crisis in crises)
        {
            if (crisisSummaryItemPrefab != null)
            {
                var instance = Instantiate(crisisSummaryItemPrefab, contentRoot.transform, false);
                var itemWidget = instance.GetComponent<HudCrisisSummaryItem>();
                if (itemWidget != null)
                    itemWidget.Populate(crisis);
            }
        }

        // Add mission items
        foreach (var mission in missions)
        {
            if (missionSummaryItemPrefab != null)
            {
                var instance = Instantiate(missionSummaryItemPrefab, contentRoot.transform, false);
                var itemWidget = instance.GetComponent<HudMissionSummaryItem>();
                if (itemWidget != null)
                    itemWidget.Populate(mission, currentCiv);
            }
        }

        dropdownButton.SetBodyContentFromInstance(contentRoot);
    }

    private void HandleCrisisChanged(CrisisData _)
    {
        RefreshDropdownContent();
    }

    private void HandleMissionStarted(Civilization civ, MissionData mission)
    {
        RefreshDropdownContent();
    }

    private void HandleMissionCompleted(Civilization civ, MissionData mission, MissionState state)
    {
        RefreshDropdownContent();
    }

    private void HandleObjectiveCompleted(Civilization civ, MissionData mission, int index)
    {
        RefreshDropdownContent();
    }

    /// <summary>
    /// Get all active crises for the current civilization.
    /// </summary>
    private List<CrisisData> GetActiveCrises()
    {
        var crises = new List<CrisisData>();

        if (currentCiv == null || CrisisManager.Instance == null)
            return crises;

        // CrisisManager tracks a single active crisis; return it if present
        if (CrisisManager.Instance.ActiveCrisis != null)
            crises.Add(CrisisManager.Instance.ActiveCrisis);

        return crises;
    }

    /// <summary>
    /// Get all active missions for the current civilization.
    /// </summary>
    private List<MissionData> GetActiveMissions()
    {
        var missions = new List<MissionData>();

        if (currentCiv == null || CrisisManager.Instance == null)
            return missions;

        // Get player's current active crisis mission via CrisisManager
        var state = CrisisManager.Instance?.GetActiveMission(currentCiv);
        if (state != null && state.mission != null)
            missions.Add(state.mission);

        return missions;
    }

    /// <summary>
    /// Open full crisis/mission panel via UIManager.
    /// </summary>
    private void OpenCrisisMissionPanel()
    {
        if (UIManager.Instance != null)
        {
            // Assuming there's a full panel for detailed crisis/mission view
            // If not, this is a placeholder for future expansion
            Debug.Log("HudCrisisMissionDropdown: Opening full crisis/mission panel");
        }
    }

    /// <summary>
    /// Set the civilization for which to display crises and missions.
    /// </summary>
    public void SetCurrentCivilization(Civilization civ)
    {
        currentCiv = civ;
        RefreshDropdownContent();
    }
}
