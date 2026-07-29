using System;
using UnityEngine;
using UnityEngine.UI;

public enum CityUITab
{
    Overview,
    Production,
    BuildingsAndSpecialists,
    CrimeAndDisease,
    UnitStorage
}

/// <summary>
/// Controls the feature panels in the city screen. Visual assets and final tab
/// styling are intentionally left to the prefab; this component only owns tab
/// selection, panel visibility, and optional selected-state graphics.
/// </summary>
public class CityUITabController : MonoBehaviour
{
    [Serializable]
    private class TabBinding
    {
        public CityUITab tab;
        public Button button;
        public GameObject panel;
        [Tooltip("Optional graphic tinted to indicate the active tab.")]
        public Graphic selectedGraphic;
    }

    [SerializeField] private CityUITab defaultTab = CityUITab.Overview;
    [SerializeField] private TabBinding[] tabs = Array.Empty<TabBinding>();
    [SerializeField] private Color selectedColor = new Color(0.9f, 0.8f, 0.1f, 1f);
    [SerializeField] private Color normalColor = Color.white;

    public CityUITab CurrentTab { get; private set; }
    public event Action<CityUITab> TabChanged;

    private void Awake()
    {
        WireButtons();
        SelectTab(defaultTab, false);
    }

    public void SelectTab(CityUITab tab)
    {
        SelectTab(tab, true);
    }

    public void ResetToDefault()
    {
        SelectTab(defaultTab);
    }

    private void WireButtons()
    {
        if (tabs == null) return;
        foreach (var binding in tabs)
        {
            if (binding == null || binding.button == null) continue;
            CityUITab capturedTab = binding.tab;
            binding.button.onClick.AddListener(() => SelectTab(capturedTab));
        }
    }

    private void SelectTab(CityUITab tab, bool notify)
    {
        CurrentTab = tab;
        if (tabs != null)
        {
            foreach (var binding in tabs)
            {
                if (binding == null) continue;
                bool selected = binding.tab == tab;
                if (binding.panel != null) binding.panel.SetActive(selected);
                if (binding.selectedGraphic != null)
                    binding.selectedGraphic.color = selected ? selectedColor : normalColor;
            }
        }

        if (notify) TabChanged?.Invoke(tab);
    }
}
