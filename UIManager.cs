using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject notificationPanel;
    public GameObject cityPanel;
    public GameObject techPanel;
    public GameObject culturePanel;
    public GameObject religionPanel;
    public GameObject tradePanel;
    [Header("Trade UI")]
    public UnityEngine.UI.Button tradeButton; // Optional: main trade button on player UI
    public GameObject diplomacyPanel;
    public GameObject equipmentPanel;
    public GameObject unitInfoPanel;
    public GameObject pauseMenuPanel;
    public GameObject playerUI;
    public SpaceMapUI spaceMapUI;

    [Header("UI Audio")]
    [Tooltip("Click sound played for all UI Buttons.")]
    public AudioClip uiClickClip;
    [Range(0f,1f)] public float uiClickVolume = 1f;
    private AudioSource uiAudioSource;
    private readonly HashSet<Button> wiredButtons = new HashSet<Button>();
    private readonly HashSet<Toggle> wiredToggles = new HashSet<Toggle>();
    private readonly HashSet<Dropdown> wiredDropdowns = new HashSet<Dropdown>();
    private readonly HashSet<TMPro.TMP_Dropdown> wiredTMPDropdowns = new HashSet<TMPro.TMP_Dropdown>();
    private readonly HashSet<Slider> wiredSliders = new HashSet<Slider>();
    private readonly HashSet<Scrollbar> wiredScrollbars = new HashSet<Scrollbar>();
    private readonly HashSet<ScrollRect> wiredScrollRects = new HashSet<ScrollRect>();

    [Header("Notification Settings")]
    public float notificationDuration = 3f;
    private Coroutine notificationCoroutine;

    private Dictionary<string, GameObject> panelDict;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        UIManager.Instance = this;
        DontDestroyOnLoad(gameObject);
        panelDict = new Dictionary<string, GameObject>
        {
            { "NotificationPanel", notificationPanel },
            { "notificationPanel", notificationPanel },
            { "CityPanel", cityPanel },
            { "cityPanel", cityPanel },
            { "TechPanel", techPanel },
            { "techPanel", techPanel },
            { "CulturePanel", culturePanel },
            { "culturePanel", culturePanel },
            { "ReligionPanel", religionPanel },
            { "religionPanel", religionPanel },
            { "TradePanel", tradePanel },
            { "tradePanel", tradePanel },
            { "DiplomacyPanel", diplomacyPanel },
            { "diplomacyPanel", diplomacyPanel },
            { "EquipmentPanel", equipmentPanel },
            { "equipmentPanel", equipmentPanel },
            { "UnitInfoPanel", unitInfoPanel },
            { "unitInfoPanel", unitInfoPanel },
            { "PauseMenuPanel", pauseMenuPanel },
            { "pauseMenuPanel", pauseMenuPanel },
            { "PlayerUI", playerUI },
            { "playerUI", playerUI }
        };
        HideAllPanels();
        
        // Keep PlayerUI active - it should be visible at game start (unless loading is active)
        if (playerUI != null && !IsLoadingActive()) 
            playerUI.SetActive(true);

        // Ensure we have an AudioSource for UI sounds
        uiAudioSource = GetComponent<AudioSource>();
        if (uiAudioSource == null)
            uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;
        uiAudioSource.spatialBlend = 0f;

        // Wire click sounds for all known panels/buttons
    WireAllPanelsForClickSound();

        // Subscribe to TradeManager events if available
        if (TradeManager.Instance != null)
        {
            TradeManager.Instance.OnGlobalTradeEnabled += HandleGlobalTradeEnabled;
            TradeManager.Instance.OnCivilizationTradeEnabled += HandleCivilizationTradeEnabled;
        }
    }

    void OnDestroy()
    {
        if (TradeManager.Instance != null)
        {
            TradeManager.Instance.OnGlobalTradeEnabled -= HandleGlobalTradeEnabled;
            TradeManager.Instance.OnCivilizationTradeEnabled -= HandleCivilizationTradeEnabled;
        }
    }
    
    /// <summary>
    /// Check if any loading panel is currently active or minimap generation is in progress
    /// </summary>
    private bool IsLoadingActive()
    {
        if (LoadingPanelController.Instance != null)
        {
            // Check if the loading panel is active
            if (LoadingPanelController.Instance.gameObject.activeSelf)
                return true;
        }
        
        // Also check if minimap generation is still in progress
        var minimapUI = FindFirstObjectByType<MinimapUI>();
        if (minimapUI != null && !minimapUI.MinimapsPreGenerated)
        {
            Debug.Log("[UIManager] Minimap generation still in progress, treating as loading active");
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Show a panel by name (e.g. "CityPanel"). Hides all others first.
    /// </summary>
    public void ShowPanel(string name)
    {
        // Don't show panels while loading is active
        if (IsLoadingActive()) return;
        
        HideAllPanels();
        if (!panelDict.TryGetValue(name, out var panel))
            panelDict.TryGetValue(name.ToLowerInvariant(), out panel);
        if (panel != null)
        {
            panel.SetActive(true);
            WireUIInteractions(panel);
        }
    }

    /// <summary>
    /// Hide a panel by name.
    /// </summary>
    public void HidePanel(string name)
    {
        if (!panelDict.TryGetValue(name, out var panel))
            panelDict.TryGetValue(name.ToLowerInvariant(), out panel);
        if (panel != null)
            panel.SetActive(false);
    }

    /// <summary>
    /// Hide all panels managed by the UIManager.
    /// </summary>
    public void HideAllPanels()
    {
        foreach (var panel in panelDict.Values)
        {
            if (panel != null && panel != playerUI)
                panel.SetActive(false);
        }

        // Always keep the main PlayerUI visible (unless loading is active)
        if (playerUI != null && !IsLoadingActive())
            playerUI.SetActive(true);
    }

    /// <summary>
    /// Get a panel GameObject by name.
    /// </summary>
    public GameObject GetPanel(string name)
    {
        if (!panelDict.TryGetValue(name, out var panel))
            panelDict.TryGetValue(name.ToLowerInvariant(), out panel);
        return panel;
    }

    /// <summary>
    /// Show a notification message to the player. Displays the notificationPanel and auto-hides after duration.
    /// </summary>
    public void ShowNotification(string message)
    {
        if (notificationPanel == null)
        {
            Debug.LogWarning("UIManager: notificationPanel is not assigned!");
            return;
        }
        // Try to find a TextMeshProUGUI or Text component on the panel
        var tmpText = notificationPanel.GetComponentInChildren<TextMeshProUGUI>();
        if (tmpText != null)
            tmpText.text = message;
        else
        {
            var uiText = notificationPanel.GetComponentInChildren<Text>();
            if (uiText != null)
                uiText.text = message;
        }
        notificationPanel.SetActive(true);
        if (notificationCoroutine != null)
            StopCoroutine(notificationCoroutine);
        notificationCoroutine = StartCoroutine(HideNotificationAfterDelay());
    }

    private System.Collections.IEnumerator HideNotificationAfterDelay()
    {
        yield return new WaitForSeconds(notificationDuration);
        if (notificationPanel != null)
            notificationPanel.SetActive(false);
    }

    // --- Additional helper methods previously handled by GameManager ---

    public void ShowTechPanel(Civilization civ)
    {
        if (techPanel == null) return;
        var techUI = techPanel.GetComponent<TechUI>();
        if (techUI != null)
            techUI.Show(civ);
        else
            ShowPanel("TechPanel");
    }

    public void HideTechPanel()
    {
        HidePanel("TechPanel");
    }

    public void ShowCulturePanel(Civilization civ)
    {
        if (culturePanel == null) return;
        var cultureUI = culturePanel.GetComponent<CultureUI>();
        if (cultureUI != null)
            cultureUI.Show(civ);
        else
            ShowPanel("CulturePanel");
    }

    public void HideCulturePanel()
    {
        HidePanel("CulturePanel");
    }

    public void ShowTradePanel(Civilization civ)
    {
        if (tradePanel == null) return;
        if (civ == null)
        {
            ShowNotification("No civilization selected for trade.");
            return;
        }
        // Check via TradeManager so global unlocks are respected
        if (TradeManager.Instance != null)
        {
            if (!TradeManager.Instance.IsTradeEnabledForCivilization(civ))
            {
                ShowNotification($"{civ.civData.civName} has not unlocked trade yet.");
                return;
            }
        }
        else
        {
            if (!civ.tradeEnabled)
            {
                ShowNotification($"{civ.civData.civName} has not unlocked trade yet.");
                return;
            }
        }
        var tradeUI = tradePanel.GetComponent<TradePanel>();
        if (tradeUI != null)
            tradeUI.Show(civ);
        ShowPanel("TradePanel");
    }

    /// <summary>
    /// Update the trade button interactable state for a given civilization.
    /// Call this when the selected civ changes or after unlock events.
    /// </summary>
    public void UpdateTradeButtonState(Civilization civ)
    {
        if (tradeButton == null) return;
        bool enabled = false;
        if (TradeManager.Instance != null)
            enabled = TradeManager.Instance.IsTradeEnabledForCivilization(civ);
        else if (civ != null)
            enabled = civ.tradeEnabled;

        tradeButton.interactable = enabled;
    }

    private void HandleGlobalTradeEnabled()
    {
        // Enable the button for the local player UI
        if (tradeButton != null)
            tradeButton.interactable = true;
    }

    private void HandleCivilizationTradeEnabled(Civilization civ)
    {
        // If the enabled civ is the player's civ, enable the button
        if (CivilizationManager.Instance != null && CivilizationManager.Instance.playerCiv == civ)
        {
            if (tradeButton != null)
                tradeButton.interactable = true;
        }
    }

    public void ShowEquipmentPanel(Civilization civ)
    {
    if (equipmentPanel == null)
        {
            Debug.LogWarning("UIManager: equipmentPanel is not assigned.");
            return;
        }
        // Prefer passing civ via SendMessage to avoid hard type dependency
        if (civ != null)
            equipmentPanel.SendMessage("Show", civ, SendMessageOptions.DontRequireReceiver);
        else
            equipmentPanel.SendMessage("ShowDefault", SendMessageOptions.DontRequireReceiver);
    ShowPanel("EquipmentPanel");
    WireUIInteractions(equipmentPanel);
    }

    public void HideEquipmentPanel()
    {
        if (equipmentPanel == null) return;
    equipmentPanel.SendMessage("Hide", SendMessageOptions.DontRequireReceiver);
        HidePanel("EquipmentPanel");
    }

    // --- UI Audio helpers ---
    private void WireAllPanelsForClickSound()
    {
        foreach (var kvp in panelDict)
        {
            var panel = kvp.Value;
            if (panel != null)
                WireUIInteractions(panel);
        }
    }

    // Public in case dynamic UIs want to call it after populating lists
    public void WireUIInteractions(GameObject root)
    {
        if (root == null) return;

        // Buttons
        var buttons = root.GetComponentsInChildren<Button>(true);
        foreach (var btn in buttons)
        {
            if (btn == null || wiredButtons.Contains(btn)) continue;
            btn.onClick.AddListener(PlayUIClick);
            wiredButtons.Add(btn);
        }

        // Toggles
        var toggles = root.GetComponentsInChildren<Toggle>(true);
        foreach (var t in toggles)
        {
            if (t == null || wiredToggles.Contains(t)) continue;
            t.onValueChanged.AddListener(_ => PlayUIClick());
            wiredToggles.Add(t);
        }

        // Unity Dropdown
        var dropdowns = root.GetComponentsInChildren<Dropdown>(true);
        foreach (var d in dropdowns)
        {
            if (d == null || wiredDropdowns.Contains(d)) continue;
            d.onValueChanged.AddListener(_ => PlayUIClick());
            wiredDropdowns.Add(d);
        }

        // TMP Dropdown
        var tmpDropdowns = root.GetComponentsInChildren<TMPro.TMP_Dropdown>(true);
        foreach (var d in tmpDropdowns)
        {
            if (d == null || wiredTMPDropdowns.Contains(d)) continue;
            d.onValueChanged.AddListener(_ => PlayUIClick());
            wiredTMPDropdowns.Add(d);
        }

        // Sliders
        var sliders = root.GetComponentsInChildren<Slider>(true);
        foreach (var s in sliders)
        {
            if (s == null || wiredSliders.Contains(s)) continue;
            s.onValueChanged.AddListener(_ => PlayUIClick());
            wiredSliders.Add(s);
        }

        // Scrollbars
        var scrollbars = root.GetComponentsInChildren<Scrollbar>(true);
        foreach (var sb in scrollbars)
        {
            if (sb == null || wiredScrollbars.Contains(sb)) continue;
            sb.onValueChanged.AddListener(_ => PlayUIClick());
            wiredScrollbars.Add(sb);
        }

        // ScrollRects (Scroll View) — play a click on scroll interactions
        var scrollRects = root.GetComponentsInChildren<ScrollRect>(true);
        foreach (var sr in scrollRects)
        {
            if (sr == null || wiredScrollRects.Contains(sr)) continue;
            sr.onValueChanged.AddListener(_ => PlayUIClick());
            wiredScrollRects.Add(sr);
        }
    }

    private void PlayUIClick()
    {
        // Prefer the global GameManager audio so all UI sounds are consistent.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayUIClick();
            return;
        }
        // Fallback to local AudioSource if global is unavailable.
        if (uiClickClip != null && uiAudioSource != null)
            uiAudioSource.PlayOneShot(uiClickClip, uiClickVolume);
    }

    public void ShowDiplomacyPanel(Civilization civ)
    {
        Debug.Log("[UIManager] ShowDiplomacyPanel called");
        if (diplomacyPanel == null) 
        {
            Debug.LogError("[UIManager] diplomacyPanel is null! Cannot show diplomacy UI.");
            return;
        }
        
        // First activate the diplomacy panel GameObject
        diplomacyPanel.SetActive(true);
        Debug.Log("[UIManager] Diplomacy panel GameObject activated");
    // Wire interactions for click sounds (buttons, toggles, dropdowns, sliders, scrollbars, scrollrects)
    WireUIInteractions(diplomacyPanel);
        
        // Then find and call the DiplomacyUI component
        var diplomacyUI = diplomacyPanel.GetComponent<DiplomacyUI>();
        if (diplomacyUI != null)
        {
            Debug.Log("[UIManager] Found DiplomacyUI component, calling Show()");
            diplomacyUI.Show(civ);
        }
        else
        {
            // Try to find DiplomacyUI in children
            diplomacyUI = diplomacyPanel.GetComponentInChildren<DiplomacyUI>();
            if (diplomacyUI != null)
            {
                Debug.Log("[UIManager] Found DiplomacyUI component in children, calling Show()");
                diplomacyUI.Show(civ);
            }
            else
            {
                Debug.LogError("[UIManager] DiplomacyUI component not found on diplomacy panel or its children!");
            }
        }
    }

    public void ShowUnitInfoPanelForUnit(object unit)
    {
        if (unitInfoPanel == null || unit == null) return;
        var infoUI = unitInfoPanel.GetComponent<UnitInfoPanel>();
        if (infoUI != null)
            infoUI.ShowPanel(unit);
        ShowPanel("UnitInfoPanel");
    }

    public void HideUnitInfoPanel()
    {
        if (unitInfoPanel == null) return;
        var infoUI = unitInfoPanel.GetComponent<UnitInfoPanel>();
        if (infoUI != null)
            infoUI.HidePanel();
        HidePanel("UnitInfoPanel");
    }

    /// <summary>
    /// Deselect any currently selected unit and hide the unit info panel.
    /// Convenience wrapper so UI buttons can deselect via UIManager.
    /// </summary>
    public void DeselectUnit()
    {
        if (UnitSelectionManager.Instance != null)
            UnitSelectionManager.Instance.DeselectUnit();
        HideUnitInfoPanel();
    }

    /// <summary>
    /// Show the space map UI for interplanetary travel and visualization
    /// </summary>
    public void ShowSpaceMap()
    {
        if (spaceMapUI != null)
        {
            Debug.Log("[UIManager] Opening Space Map UI");
            spaceMapUI.Show();
        }
        else
        {
            Debug.LogWarning("[UIManager] SpaceMapUI is not assigned! Please assign it in the Inspector.");
        }
    }

    /// <summary>
    /// Hide the space map UI
    /// </summary>
    public void HideSpaceMap()
    {
        if (spaceMapUI != null)
        {
            spaceMapUI.Hide();
        }
    }

    /// <summary>
    /// Show the pause menu
    /// </summary>
    public void ShowPauseMenu()
    {
        Debug.Log("[UIManager] ShowPauseMenu called");
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
            WireUIInteractions(pauseMenuPanel);
            Debug.Log("[UIManager] Pause menu panel activated");
        }
        else
        {
            Debug.LogError("[UIManager] pauseMenuPanel is null! Cannot show pause menu.");
        }
    }

    /// <summary>
    /// Hide the pause menu
    /// </summary>
    public void HidePauseMenu()
    {
        Debug.Log("[UIManager] HidePauseMenu called");
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
            Debug.Log("[UIManager] Pause menu panel deactivated");
        }
        else
        {
            Debug.LogError("[UIManager] pauseMenuPanel is null! Cannot hide pause menu.");
        }
    }

    /// <summary>
    /// UI hook: Switch camera view to Earth's moon (Luna).
    /// Wire your Moon button OnClick to this method.
    /// </summary>
    public void GoToEarthMoon()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.GoToEarthMoon();
        }
        else
        {
            Debug.LogWarning("[UIManager] GameManager not found; cannot switch to Earth's moon.");
        }
    }
}