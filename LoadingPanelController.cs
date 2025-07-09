using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoadingPanelController : MonoBehaviour
{
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI statusText;

    public void SetProgress(float value)
    {
        if (progressSlider != null)
            progressSlider.value = value;
    }

    public void SetStatus(string text)
    {
        if (statusText != null)
            statusText.text = text;
    }
} 