using TMPro;
using UnityEngine;

/// <summary>One objective line in the mission details window. Layout is authored in the Mission Objective Row prefab.</summary>
public class MissionObjectiveRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI markerText;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private RectTransform progressFill;
    [SerializeField] private Color completedColor = new Color(0.55f, 0.82f, 0.52f, 1f);
    [SerializeField] private Color currentColor = new Color(0.88f, 0.76f, 0.48f, 1f);
    [SerializeField] private Color upcomingColor = new Color(0.72f, 0.76f, 0.79f, 1f);

    public void Bind(int number, string title, string description, string progress, float progress01, bool completed, bool current)
    {
        if (markerText != null)
        {
            markerText.text = number.ToString();
            markerText.color = completed ? completedColor : current ? currentColor : upcomingColor;
        }

        if (titleText != null) titleText.text = title ?? string.Empty;
        if (descriptionText != null) descriptionText.text = description ?? string.Empty;
        if (progressText != null) progressText.text = progress ?? string.Empty;

        if (progressFill != null)
        {
            progressFill.anchorMin = new Vector2(0f, 0f);
            progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress01), 1f);
            progressFill.offsetMin = Vector2.zero;
            progressFill.offsetMax = Vector2.zero;
        }
    }
}
