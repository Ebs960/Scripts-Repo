// Assets/Scripts/UI/HudMissionSummaryItem.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Single mission summary item in the dropdown.
/// Displays: mission name, progress, objectives.
/// </summary>
public class HudMissionSummaryItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI missionNameText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private Image progressBar;
    [SerializeField] private Image missionIcon;

    public void Populate(MissionData mission, Civilization civ)
    {
        if (mission == null) return;

        if (missionNameText != null)
            missionNameText.text = mission.missionName;

        if (missionIcon != null && mission.icon != null)
            missionIcon.sprite = mission.icon;

        // Get mission state for this civilization via CrisisManager
        var missionState = CrisisManager.Instance?.GetActiveMission(civ);
        if (missionState != null && missionState.mission == mission)
        {
            int completed = missionState.CompletedObjectiveCount;
            int total = mission.objectives != null ? mission.objectives.Count : 0;

            if (progressText != null)
                progressText.text = $"{completed}/{total} Objectives";

            if (progressBar != null)
                progressBar.fillAmount = total > 0 ? (float)completed / total : 0;
        }
    }
}
