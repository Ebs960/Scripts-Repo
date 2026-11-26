using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// References into the Options Menu prefab. The layout is saved there;
/// FirstDraftOptionsMenu supplies the values and behaviour.
/// </summary>
public sealed class OptionsMenuView : MonoBehaviour
{
    [Serializable]
    public sealed class Tab
    {
        public Button button;
        public Image image;
        public TMP_Text label;
        public RectTransform page;
    }

    [Serializable]
    public sealed class ToggleRow
    {
        public Toggle toggle;
        public TMP_Text state;
    }

    [Serializable]
    public sealed class SliderRow
    {
        public Slider slider;
        public TMP_Text value;
    }

    [Header("Frame")]
    public RectTransform content;
    public ScrollRect scroll;
    public TMP_Text contextText;
    public TMP_Text footerStatus;
    public Button backButton;
    public Tab[] tabs;

    [Header("Graphics")]
    public TMP_Dropdown displayMode;
    public TMP_Dropdown resolution;
    public TMP_Text displayHint;
    public Button applyDisplay;
    public Button keepDisplay;
    public Button revertDisplay;
    public GameObject qualityRow;
    public TMP_Dropdown quality;
    public ToggleRow vSync;
    public TMP_Dropdown frameLimit;

    [Header("Audio")]
    public ToggleRow menuMusic;
    public SliderRow menuMusicVolume;
    public ToggleRow gameMusic;
    public SliderRow gameMusicVolume;

    [Header("Gameplay")]
    public ToggleRow autosave;
    public SliderRow autosaveInterval;
    public SliderRow cameraPan;
    public SliderRow cameraZoom;

    [Header("Interface")]
    public TMP_Dropdown tooltipDelay;
}