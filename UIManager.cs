using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Serialization;

public class UIManager : MonoBehaviour
{
    private enum ModalKind
    {
        Narrative,
        Selection,
    }

    private sealed class ModalRequest
    {
        public ModalKind kind;
        public string title;
        public string body;
        public Sprite image;
        public string rewardTitle;
        public string rewardBody;
        public Sprite rewardImage;
        public bool allowClose = true;
        public Action onConfirm;
        public CrisisData crisis;
        public List<MissionData> missions;
    }

    public static UIManager Instance { get; private set; }
    public bool IsBlockingModalVisible => modalVisible;

    [Header("UI Panels")]
    public GameObject notificationPanel;
    public GameObject cityPanel;
    public GameObject techPanel;
    public GameObject culturePanel;
    public GameObject herdPanel;
    public GameObject governmentPanel;
    public GameObject religionPanel;
    public GameObject tradePanel;
    [Header("Trade UI")]
    public UnityEngine.UI.Button tradeButton; // Optional: main trade button on player UI
    public GameObject diplomacyPanel;
    public GameObject equipmentPanel;
    public GameObject unitInfoPanel;
    public GameObject pauseMenuPanel;
    [FormerlySerializedAs("playerUI")]
    public GameObject gameplayHudRoot;
    public SpaceMapUI spaceMapUI;

    [Header("Mission and Crisis UI")]
    public MissionNarrativePopupUI missionNarrativePopupPrefab;
    public MissionSelectionPopupUI missionSelectionPopupPrefab;

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
    private System.Collections.Generic.Queue<string> _pendingNotifications = new System.Collections.Generic.Queue<string>();
    private bool _wasLoadingLastFrame = false;

    private Dictionary<string, GameObject> panelDict;
    private CrisisManager subscribedCrisisManager;
    private TurnManager subscribedTurnManager;
    private readonly Queue<ModalRequest> modalQueue = new Queue<ModalRequest>();
    private bool modalVisible;
    private bool restoreGameplayHudAfterModal;
    private bool restoreUnitInfoPanelAfterModal;
    private bool restoreMinimapAfterModal;
    private MinimapUI cachedMinimapUI;
    private bool handlingSelectionReminder;
    private CrisisData pendingSelectionCrisis;
    private MissionNarrativePopupUI narrativePopupInstance;
    private MissionSelectionPopupUI selectionPopupInstance;
    private Coroutine modalWatchdog;
    private Canvas rootCanvas;
    private GameObject rootObject;
    private GameObject backdropObject;
    private GameObject narrativePanel;
    private Image narrativeImage;
    private TextMeshProUGUI narrativeTitle;
    private TextMeshProUGUI narrativeBody;
    private Button narrativeCloseButton;
    private TextMeshProUGUI narrativeCloseText;
    private Action narrativeCloseAction;
    private GameObject selectionPanel;
    private TextMeshProUGUI selectionTitle;
    private TextMeshProUGUI selectionSubtitle;
    private Transform selectionGrid;
    private Button selectionCloseButton;
    private TextMeshProUGUI selectionCloseText;
    private TMP_FontAsset defaultFont;
    private CrisisMissionTrackerUI crisisMissionTrackerUI;
    private LegacyTrackerUI legacyTrackerUI;
    private PoliticalAffairsPanelUI politicalAffairsPanelUI;
    private bool startupMissionCrisisViewsHidden;

    private static readonly HashSet<string> fullscreenHudHidingPanels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "TechPanel",
        "CulturePanel",
        "GovernmentPanel",
        "ReligionPanel",
        "TradePanel",
        "DiplomacyPanel",
        "EquipmentPanel",
        "PoliticalAffairsPanel"
    };


    private void LogMissionSelectionBlocked(string context, CrisisData crisis, List<MissionData> missions = null)
    {
        // Diagnostic logging removed — this fires frequently during normal gameplay.
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        UIManager.Instance = this;
        DontDestroyOnLoad(gameObject);
        defaultFont = TMP_Settings.defaultFontAsset;
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
            { "HerdPanel", herdPanel },
            { "herdPanel", herdPanel },
            { "GovernmentPanel", governmentPanel },
            { "governmentPanel", governmentPanel },
            { "PauseMenuPanel", pauseMenuPanel },
            { "pauseMenuPanel", pauseMenuPanel },
            { "gameplayHudRoot", gameplayHudRoot }
        };
        HideAllPanels();

        // Keep Gameplay HUD active - it should be visible at game start (unless loading is active)
        if (gameplayHudRoot != null && !IsLoadingActive())
            gameplayHudRoot.SetActive(true);

        // Ensure the Unit Info panel is visible at startup by default (it will be hidden
        // automatically when other top-level panels are shown via ShowPanel()). Only
        // enable it if loading is not active.
        if (unitInfoPanel != null && !IsLoadingActive())
        {
            unitInfoPanel.SetActive(true);
            WireUIInteractions(unitInfoPanel);
        }

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

        TrySubscribeMissionCrisisUi();
        EnsureCrisisMissionTrackerUi();
        EnsureLegacyTrackerUi();
    }

    void OnDestroy()
    {
        if (TradeManager.Instance != null)
        {
            TradeManager.Instance.OnGlobalTradeEnabled -= HandleGlobalTradeEnabled;
            TradeManager.Instance.OnCivilizationTradeEnabled -= HandleCivilizationTradeEnabled;
        }

        UnsubscribeMissionCrisisUi();
    }
    
    /// <summary>
    /// Check if any loading panel is currently active or minimap generation is in progress
    /// </summary>
    private bool IsLoadingActive()
    {
        if (LoadingPanelController.Instance != null)
        {
            if (LoadingPanelController.Instance.IsUiBlocked)
                return true;
        }
        
        // NOTE: Minimap pre-generation check was removed here.
        // In deferred/event-driven minimap mode, MinimapsPreGenerated stays false
        // indefinitely, which was blocking ALL gameplay UI (unit panels, city panels,
        // notifications, etc.) even after the game was fully playable.
        // The loading panel check above is sufficient.
        
        return false;
    }

    /// <summary>
    /// Show a panel by name (e.g. "CityPanel"). Hides all others first.
    /// </summary>
    public void ShowPanel(string name)
    {
        // Don't show panels while loading is active
        if (IsLoadingActive()) return;
        if (modalVisible)
        {
            Debug.Log($"[UIManager] Ignoring ShowPanel('{name}') because a mission/crisis modal is open.");
            return;
        }
        
        HideAllPanels();
        if (!panelDict.TryGetValue(name, out var panel))
            panelDict.TryGetValue(name.ToLowerInvariant(), out panel);
        if (panel != null)
        {
            panel.SetActive(true);
            WireUIInteractions(panel);
            if (ShouldHideHudForPanel(name))
                SetGameplayHudVisible(false);
            else
                SyncGameplayHudVisibilityForOpenPanels();
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

        SyncGameplayHudVisibilityForOpenPanels();
    }

    /// <summary>
    /// Hide all panels managed by the UIManager.
    /// </summary>
    public void HideAllPanels()
    {
        foreach (var panel in panelDict.Values)
        {
            if (panel != null && panel != gameplayHudRoot)
                panel.SetActive(false);
        }

        // Always keep the main Gameplay HUD visible (unless loading is active)
        if (gameplayHudRoot != null && !IsLoadingActive())
            gameplayHudRoot.SetActive(true);
    }


    private bool ShouldHideHudForPanel(string panelName)
    {
        return !string.IsNullOrWhiteSpace(panelName) && fullscreenHudHidingPanels.Contains(panelName);
    }

    private void SetGameplayHudVisible(bool visible)
    {
        if (gameplayHudRoot == null)
            return;

        if (IsLoadingActive())
        {
            gameplayHudRoot.SetActive(false);
            return;
        }

        if (gameplayHudRoot.activeSelf != visible)
            gameplayHudRoot.SetActive(visible);
    }

    private bool IsAnyHudHidingPanelOpen()
    {
        foreach (var kv in panelDict)
        {
            if (!ShouldHideHudForPanel(kv.Key))
                continue;

            if (kv.Value != null && kv.Value.activeInHierarchy)
                return true;
        }

        if (politicalAffairsPanelUI != null && politicalAffairsPanelUI.gameObject.activeInHierarchy)
            return true;

        return false;
    }

    private void SyncGameplayHudVisibilityForOpenPanels()
    {
        if (modalVisible)
            return;

        SetGameplayHudVisible(!IsAnyHudHidingPanelOpen());
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

        // If loading is active, enqueue the notification to be displayed later
        if (IsLoadingActive())
        {
            _pendingNotifications.Enqueue(message);
            return;
        }

        // Display immediately
        DisplayNotification(message);
    }

    public bool ShowPoliticalSelection(string title, string subtitle, IReadOnlyList<MissionSelectionPopupUI.OptionData> options, Action<int> onSelected)
    {
        if (modalVisible) return false;

        EnsureMissionCrisisPrefabViews();
        if (selectionPopupInstance == null) return false;

        if (!selectionPopupInstance.gameObject.activeSelf)
            selectionPopupInstance.gameObject.SetActive(true);

        selectionPopupInstance.Show(
            title,
            subtitle,
            options,
            index =>
            {
                onSelected?.Invoke(index);
                CloseCurrentMissionCrisisModal();
            });

        SuppressGameplayHudForMissionCrisisModal();
        modalVisible = true;
        StartModalWatchdog();
        return true;
    }

    public void ShowPoliticalAffairsPanel(Civilization civ = null)
    {
        civ ??= CivilizationManager.Instance?.GetAllCivs()?.FirstOrDefault(c => c != null && c.isPlayerControlled);
        if (civ == null) return;

        EnsurePoliticalAffairsPanelUi();
        politicalAffairsPanelUI?.Show(civ);
        SyncGameplayHudVisibilityForOpenPanels();
    }

    private void EnsurePoliticalAffairsPanelUi()
    {
        if (politicalAffairsPanelUI != null) return;
        if (gameplayHudRoot == null) return;

        // IMPORTANT: Parent this panel to the root canvas (not gameplayHudRoot).
        // The political affairs panel is considered HUD-hiding, so if it is parented to
        // gameplayHudRoot it will deactivate itself when HUD visibility is synced,
        // causing rapid active/inactive flicker.
        var parentRect = gameplayHudRoot.GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
        if (parentRect == null)
            parentRect = gameplayHudRoot.GetComponent<RectTransform>();
        if (parentRect == null) return;

        var go = new GameObject("PoliticalAffairsPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(PoliticalAffairsPanelUI));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parentRect, false);
        rect.anchorMin = new Vector2(0.15f, 0.12f);
        rect.anchorMax = new Vector2(0.85f, 0.88f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.13f, 0.97f);

        politicalAffairsPanelUI = go.GetComponent<PoliticalAffairsPanelUI>();
        politicalAffairsPanelUI.BuildRuntimeUi(defaultFont, PlayUIClick);
        politicalAffairsPanelUI.Hide();
    }

    private void DisplayNotification(string message)
    {
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
        notificationCoroutine = null;
    }

    void Update()
    {
        TrySubscribeMissionCrisisUi();
        EnsureCrisisMissionTrackerUi();
        EnsureLegacyTrackerUi();

        bool loadingNow = IsLoadingActive();
        // If we transitioned from loading->not loading, try to flush queued notifications
        if (_wasLoadingLastFrame && !loadingNow)
        {
            TryFlushPendingNotifications();
        }
        _wasLoadingLastFrame = loadingNow;

        // If not loading and no current notification displayed, and pending queue exists, show next
        if (!loadingNow && notificationCoroutine == null && _pendingNotifications.Count > 0)
        {
            var next = _pendingNotifications.Dequeue();
            DisplayNotification(next);
        }

        SyncGameplayHudVisibilityForOpenPanels();

        if (!startupMissionCrisisViewsHidden)
        {
            HideStartupMissionCrisisViews();
            startupMissionCrisisViewsHidden = true;
        }
    }

    private void TryFlushPendingNotifications()
    {
        if (_pendingNotifications.Count == 0) return;
        if (notificationCoroutine == null)
        {
            var next = _pendingNotifications.Dequeue();
            DisplayNotification(next);
        }
    }

    private void EnsureCrisisMissionTrackerUi()
    {
        if (crisisMissionTrackerUI != null) return;
        if (gameplayHudRoot == null) return;

        var parentRect = gameplayHudRoot.GetComponent<RectTransform>();
        if (parentRect == null)
            parentRect = gameplayHudRoot.GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
        if (parentRect == null) return;

        var trackerObject = new GameObject("CrisisMissionTracker", typeof(RectTransform), typeof(CanvasRenderer), typeof(CrisisMissionTrackerUI));
        var trackerRect = trackerObject.GetComponent<RectTransform>();
        trackerRect.SetParent(parentRect, false);
        trackerRect.anchorMin = new Vector2(1f, 1f);
        trackerRect.anchorMax = new Vector2(1f, 1f);
        trackerRect.pivot = new Vector2(1f, 1f);
        trackerRect.anchoredPosition = new Vector2(-16f, -96f);
        trackerRect.sizeDelta = new Vector2(360f, 0f);

        crisisMissionTrackerUI = trackerObject.GetComponent<CrisisMissionTrackerUI>();
    }

    private void EnsureLegacyTrackerUi()
    {
        if (legacyTrackerUI != null) return;
        if (gameplayHudRoot == null) return;

        var parentRect = gameplayHudRoot.GetComponent<RectTransform>();
        if (parentRect == null)
            parentRect = gameplayHudRoot.GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
        if (parentRect == null) return;

        var legacyObject = new GameObject("LegacyTracker", typeof(RectTransform), typeof(CanvasRenderer), typeof(LegacyTrackerUI));
        var legacyRect = legacyObject.GetComponent<RectTransform>();
        legacyRect.SetParent(parentRect, false);
        legacyRect.anchorMin = new Vector2(0f, 1f);
        legacyRect.anchorMax = new Vector2(0f, 1f);
        legacyRect.pivot = new Vector2(0f, 1f);
        legacyRect.anchoredPosition = new Vector2(16f, -96f);
        legacyRect.sizeDelta = new Vector2(320f, 0f);

        legacyTrackerUI = legacyObject.GetComponent<LegacyTrackerUI>();
    }

    private void HideStartupMissionCrisisViews()
    {
        foreach (var popup in Resources.FindObjectsOfTypeAll<MissionNarrativePopupUI>())
        {
            if (popup == null || !popup.gameObject.scene.IsValid())
                continue;

            popup.Hide();
        }

        foreach (var popup in Resources.FindObjectsOfTypeAll<MissionSelectionPopupUI>())
        {
            if (popup == null || !popup.gameObject.scene.IsValid())
                continue;

            popup.Hide();
        }
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

    public void ShowReligionPanel(Civilization civ)
    {
        if (religionPanel == null) return;

        ShowPanel("ReligionPanel");

        var religionUI = religionPanel.GetComponent<ReligionUI>();
        if (religionUI != null)
        {
            religionUI.Show(civ);
        }
    }

    public void HideReligionPanel()
    {
        if (religionPanel == null) return;

        var religionUI = religionPanel.GetComponent<ReligionUI>();
        if (religionUI != null)
            religionUI.Hide();
        else
            HidePanel("ReligionPanel");
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
        
        if (diplomacyPanel == null) 
        {
            Debug.LogError("[UIManager] diplomacyPanel is null! Cannot show diplomacy UI.");
            return;
        }
        
        ShowPanel("DiplomacyPanel");
        
        // Then find and call the DiplomacyUI component
        var diplomacyUI = diplomacyPanel.GetComponent<DiplomacyUI>();
        if (diplomacyUI != null)
        {
            
            diplomacyUI.Show(civ);
        }
        else
        {
            // Try to find DiplomacyUI in children
            diplomacyUI = diplomacyPanel.GetComponentInChildren<DiplomacyUI>();
            if (diplomacyUI != null)
            {
                
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
        if (modalVisible)
        {
            Debug.Log("[UIManager] Ignoring ShowUnitInfoPanelForUnit because a mission/crisis modal is open.");
            return;
        }
        // Show the panel container FIRST (which calls HideAllPanels + SetActive),
        // then populate it. This avoids populating into a hidden panel that gets
        // immediately wiped by HideAllPanels, and eliminates a visual flicker.
        ShowPanel("UnitInfoPanel");
        var infoUI = unitInfoPanel.GetComponent<UnitInfoPanel>();
        if (infoUI != null)
            infoUI.ShowPanel(unit);
    }

    public void ShowHerdPanelForHerd(Herd herd)
    {
        if (modalVisible)
        {
            Debug.Log("[UIManager] Ignoring ShowHerdPanelForHerd because a mission/crisis modal is open.");
            return;
        }

        if (herd == null)
        {
            Debug.LogWarning("UIManager.ShowHerdPanelForHerd: herd is null");
            return;
        }

        if (herdPanel == null)
        {
            Debug.LogWarning("UIManager.ShowHerdPanelForHerd: herdPanel is not assigned in Inspector. Attempting to locate in scene.");
            var found = FindFirstObjectByType<HerdPanel>();
            if (found != null)
                herdPanel = found.gameObject;
            else
            {
                Debug.LogError("UIManager.ShowHerdPanelForHerd: No HerdPanel found in scene. Cannot show herd UI.");
                return;
            }
        }

        // Make the Herd panel modal: hide all managed panels (including gameplayHudRoot),
        // then activate the herd panel and populate it.
        foreach (var kv in panelDict)
        {
            if (kv.Value != null)
                kv.Value.SetActive(false);
        }

        herdPanel.SetActive(true);
        WireUIInteractions(herdPanel);

        var hp = herdPanel.GetComponent<HerdPanel>();
        if (hp != null)
            hp.ShowPanel(herd);
        else
            Debug.LogWarning("UIManager.ShowHerdPanelForHerd: herdPanel GameObject has no HerdPanel component.");

        Debug.Log($"UIManager: Opened HerdPanel for herd={herd.name}");
    }

    public void HideHerdPanel()
    {
        if (herdPanel == null) return;
        var hp = herdPanel.GetComponent<HerdPanel>();
        if (hp != null) hp.HidePanel();
    }

    public void HideUnitInfoPanel()
    {
        if (unitInfoPanel == null) return;
        var infoUI = unitInfoPanel.GetComponent<UnitInfoPanel>();
        if (infoUI != null)
            infoUI.HidePanel();
        // Do not immediately deactivate the panel here; the UnitInfoPanel will animate out
        // and deactivate itself when the slide-out completes.
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
        
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
            WireUIInteractions(pauseMenuPanel);
            
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
        
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
            
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

    private void TrySubscribeMissionCrisisUi()
    {
        if (subscribedCrisisManager == null && CrisisManager.Instance != null)
        {
            subscribedCrisisManager = CrisisManager.Instance;
            subscribedCrisisManager.OnCrisisOminousWarning += HandleCrisisOminousWarning;
            subscribedCrisisManager.OnCrisisObviousWarning += HandleCrisisObviousWarning;
            subscribedCrisisManager.OnCrisisStarted += HandleCrisisStarted;
            subscribedCrisisManager.OnCrisisPhaseChanged += HandleCrisisPhaseChanged;
            subscribedCrisisManager.OnCrisisEnded += HandleCrisisEnded;
            subscribedCrisisManager.OnMissionStarted += HandleMissionStarted;
            subscribedCrisisManager.OnObjectiveCompleted += HandleObjectiveCompleted;
            subscribedCrisisManager.OnMissionCompleted += HandleMissionCompleted;
            subscribedCrisisManager.OnMissionFailed += HandleMissionFailed;
            Debug.Log("[UIManager] Subscribed to CrisisManager mission events");
        }

        if (subscribedTurnManager == null && TurnManager.Instance != null)
        {
            subscribedTurnManager = TurnManager.Instance;
            subscribedTurnManager.OnTurnChanged += HandleMissionCrisisTurnChanged;
        }

        // If we subscribed late, re-evaluate any active crisis so we don't miss the initial selection window.
        // Only queue selection when the crisis is actually Active (not during warning phases).
        if (subscribedCrisisManager != null)
        {
            var active = subscribedCrisisManager.ActiveCrisis;
            var phase = subscribedCrisisManager.CurrentPhase;
            bool crisisIsActive = phase == CrisisData.CrisisPhase.Active
                || phase == CrisisData.CrisisPhase.Escalation
                || phase == CrisisData.CrisisPhase.Climax;
            if (active != null && crisisIsActive && pendingSelectionCrisis == null)
            {
                var available = GetAvailableCrisisMissions(active);
                if (available != null && available.Count > 0 && subscribedCrisisManager.GetActiveMission(GetPlayerCivilization()) == null)
                {
                    Debug.Log($"[UIManager] Showing mission selection directly for crisis={active.crisisName} availableCount={available.Count}");
                    pendingSelectionCrisis = active;
                    ShowMissionSelectionDirectly(active, available);
                }
                else
                {
                    LogMissionSelectionBlocked("late crisis UI subscription", active, available);
                }
            }
        }
    }

    private void UnsubscribeMissionCrisisUi()
    {
        if (subscribedCrisisManager != null)
        {
            subscribedCrisisManager.OnCrisisOminousWarning -= HandleCrisisOminousWarning;
            subscribedCrisisManager.OnCrisisObviousWarning -= HandleCrisisObviousWarning;
            subscribedCrisisManager.OnCrisisStarted -= HandleCrisisStarted;
            subscribedCrisisManager.OnCrisisPhaseChanged -= HandleCrisisPhaseChanged;
            subscribedCrisisManager.OnCrisisEnded -= HandleCrisisEnded;
            subscribedCrisisManager.OnMissionStarted -= HandleMissionStarted;
            subscribedCrisisManager.OnObjectiveCompleted -= HandleObjectiveCompleted;
            subscribedCrisisManager.OnMissionCompleted -= HandleMissionCompleted;
            subscribedCrisisManager.OnMissionFailed -= HandleMissionFailed;
            subscribedCrisisManager = null;
        }

        if (subscribedTurnManager != null)
        {
            subscribedTurnManager.OnTurnChanged -= HandleMissionCrisisTurnChanged;
            subscribedTurnManager = null;
        }
    }

    private void HandleCrisisOminousWarning(CrisisData crisis)
    {
        QueueNarrative(crisis?.crisisName, crisis?.ominousWarningText, crisis?.ominousWarningSplash);
    }

    private void HandleCrisisObviousWarning(CrisisData crisis)
    {
        QueueNarrative(crisis?.crisisName, crisis?.obviousWarningText, crisis?.obviousWarningSplash);
    }

    private void HandleCrisisStarted(CrisisData crisis)
    {
        // Show the crisis announcement narrative. When it closes, if missions are
        // available the selection screen will appear automatically (no queue).
        var missions = GetAvailableCrisisMissions(crisis);
        if (missions.Count > 0)
            pendingSelectionCrisis = crisis;
        else
            LogMissionSelectionBlocked("HandleCrisisStarted", crisis, missions);

        QueueNarrative(crisis?.crisisName, crisis?.crisisStartText, crisis?.crisisStartSplash);
    }

    private void HandleCrisisPhaseChanged(CrisisData crisis, CrisisData.CrisisPhase phase)
    {
        if (crisis == null) return;

        switch (phase)
        {
            case CrisisData.CrisisPhase.Escalation:
                QueueNarrative(crisis.crisisName, crisis.escalationText, crisis.escalationSplash);
                break;
            case CrisisData.CrisisPhase.Climax:
                QueueNarrative(crisis.crisisName, crisis.climaxText, crisis.climaxSplash);
                break;
            case CrisisData.CrisisPhase.Resolution:
                QueueNarrative(crisis.crisisName, crisis.resolutionText, crisis.resolutionSplash);
                break;
        }
    }

    private void HandleCrisisEnded(CrisisData crisis)
    {
        if (pendingSelectionCrisis == crisis)
            pendingSelectionCrisis = null;
    }

    private void HandleObjectiveCompleted(Civilization civ, MissionData mission, int objectiveIndex)
    {
        Debug.Log($"[UIManager] HandleObjectiveCompleted civ={civ?.civData?.civName ?? "null"} mission={mission?.missionName ?? "null"} index={objectiveIndex} isPlayer={IsPlayerCivilization(civ)}");
        if (!IsPlayerCivilization(civ) || mission == null) return;
        if (objectiveIndex < 0 || objectiveIndex >= mission.objectives.Count) return;
        var objective = mission.objectives[objectiveIndex];
        string objName = !string.IsNullOrWhiteSpace(objective.objectiveName) ? objective.objectiveName : $"Objective {objectiveIndex + 1}";
        ShowNotification($"Objective Complete: {objName}");
    }

    private void HandleMissionStarted(Civilization civ, MissionData mission)
    {
        Debug.Log($"[UIManager] HandleMissionStarted civ={civ?.civData?.civName ?? "null"} mission={mission?.missionName ?? "null"} isPlayer={IsPlayerCivilization(civ)}");
        if (!IsPlayerCivilization(civ) || mission == null) return;

        pendingSelectionCrisis = null;
        QueueNarrative(mission.missionName, ResolveMissionStartBody(mission), mission.splashImage);
    }

    private void HandleMissionCompleted(Civilization civ, MissionData mission, CrisisManager.MissionState state)
    {
        Debug.Log($"[UIManager] HandleMissionCompleted civ={civ?.civData?.civName ?? "null"} mission={mission?.missionName ?? "null"} isPlayer={IsPlayerCivilization(civ)} completedObjectives={state?.CompletedObjectiveCount}");
        if (!IsPlayerCivilization(civ) || mission == null) return;

        string body = !string.IsNullOrWhiteSpace(mission.victoryFlavorText)
            ? mission.victoryFlavorText
            : mission.description;

        TryGetRewardDisplay(state, out string rewardTitle, out string rewardBody, out Sprite rewardImage);

        QueueNarrative(
            $"Mission Complete: {mission.missionName}",
            body,
            mission.victorySplashImage != null ? mission.victorySplashImage : mission.splashImage,
            rewardTitle,
            rewardBody,
            rewardImage);
    }

    private void HandleMissionFailed(Civilization civ, MissionData mission, string reason)
    {
        Debug.Log($"[UIManager] HandleMissionFailed civ={civ?.civData?.civName ?? "null"} mission={mission?.missionName ?? "null"} isPlayer={IsPlayerCivilization(civ)} reason={reason}");
        if (!IsPlayerCivilization(civ) || mission == null) return;

        string body = string.IsNullOrWhiteSpace(reason)
            ? mission.failureFlavorText
            : reason;

        if (!string.IsNullOrWhiteSpace(mission.failureFlavorText) && !string.Equals(body, mission.failureFlavorText, StringComparison.Ordinal))
            body = mission.failureFlavorText + "\n\n" + body;

        QueueNarrative($"Mission Failed: {mission.missionName}", body, mission.failureSplashImage != null ? mission.failureSplashImage : mission.splashImage);
    }

    private void HandleMissionCrisisTurnChanged(Civilization civ, int round)
    {
        if (handlingSelectionReminder || !IsPlayerCivilization(civ) || pendingSelectionCrisis == null) return;

        if (subscribedCrisisManager == null || subscribedCrisisManager.GetActiveMission(civ) != null) return;

        var available = GetAvailableCrisisMissions(pendingSelectionCrisis);
        if (available.Count == 0)
        {
            LogMissionSelectionBlocked($"mission selection reminder on round {round}", pendingSelectionCrisis, available);
            pendingSelectionCrisis = null;
            return;
        }

        handlingSelectionReminder = true;
        try
        {
            // Show selection directly — don't go through the modal queue.
            ShowMissionSelectionDirectly(pendingSelectionCrisis, available);
        }
        finally
        {
            handlingSelectionReminder = false;
        }
    }

    private void QueueNarrative(
        string title,
        string body,
        Sprite image,
        string rewardTitle = null,
        string rewardBody = null,
        Sprite rewardImage = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body) && image == null) return;

        EnqueueModal(new ModalRequest
        {
            kind = ModalKind.Narrative,
            title = string.IsNullOrWhiteSpace(title) ? "Notification" : title,
            body = string.IsNullOrWhiteSpace(body) ? string.Empty : body,
            image = image,
            rewardTitle = rewardTitle,
            rewardBody = rewardBody,
            rewardImage = rewardImage,
        });
    }

    private void QueueSelection(CrisisData crisis, List<MissionData> missions)
    {
        if (crisis == null || missions == null || missions.Count == 0)
        {
            LogMissionSelectionBlocked("QueueSelection", crisis, missions);
            return;
        }

        // Don't queue a duplicate Selection if one is already pending.
        foreach (var pending in modalQueue)
        {
            if (pending.kind == ModalKind.Selection)
            {
                Debug.Log("[UIManager] QueueSelection: skipping duplicate — a Selection modal is already queued.");
                return;
            }
        }

        EnqueueModal(new ModalRequest
        {
            kind = ModalKind.Selection,
            title = string.IsNullOrWhiteSpace(crisis.crisisName) ? "Choose a Mission" : crisis.crisisName,
            body = "Choose one mission to pursue during this crisis.",
            crisis = crisis,
            missions = missions,
            allowClose = false,
        });
    }

    private void EnqueueModal(ModalRequest request)
    {
        if (request == null) return;
        modalQueue.Enqueue(request);
        TryShowNextModal();
    }

    private void TryShowNextModal()
    {
        if (modalVisible || modalQueue.Count == 0) return;

        Debug.Log($"[UIManager] TryShowNextModal: queue={modalQueue.Count} nextKind={modalQueue.Peek().kind}");
        var request = modalQueue.Peek();
        if (TryShowPrefabModal(request))
        {
            modalQueue.Dequeue();
            SuppressGameplayHudForMissionCrisisModal();
            modalVisible = true;
            StartModalWatchdog();
            Debug.Log($"[UIManager] Prefab modal shown for kind={request.kind}");
            return;
        }
        Debug.Log($"[UIManager] TryShowPrefabModal returned false for kind={request.kind}, falling back");

        EnsureMissionCrisisFallbackUi();
        if (rootObject == null || backdropObject == null)
        {
            // Fallback UI could not be created — discard the modal so the queue is not permanently blocked.
            Debug.LogWarning($"[UIManager] Could not create fallback mission/crisis UI; discarding queued modal. kind={request.kind} title={request.title}");
            modalQueue.Dequeue();
            return;
        }

        request = modalQueue.Dequeue();
    SuppressGameplayHudForMissionCrisisModal();
        backdropObject.SetActive(true);
        modalVisible = true;
        StartModalWatchdog();

        if (request.kind == ModalKind.Selection)
            ShowMissionCrisisFallbackSelection(request);
        else
            ShowMissionCrisisFallbackNarrative(request);
    }

    private void CloseCurrentMissionCrisisModal()
    {
        StopModalWatchdog();
        narrativeCloseAction = null;

        if (narrativePopupInstance != null)
            narrativePopupInstance.Hide();
        if (selectionPopupInstance != null)
            selectionPopupInstance.Hide();

        if (narrativePanel != null) narrativePanel.SetActive(false);
        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (backdropObject != null) backdropObject.SetActive(false);
        if (rootObject != null) rootObject.SetActive(false);

        RestoreGameplayHudAfterMissionCrisisModal();

        modalVisible = false;
        Debug.Log("[UIManager] CloseCurrentMissionCrisisModal: modal closed and UI roots deactivated.");

        // If there are more queued narratives, show them first.
        if (modalQueue.Count > 0)
        {
            TryShowNextModal();
            return;
        }

        // Queue is empty. If there's a pending mission selection, show it now.
        if (pendingSelectionCrisis != null && subscribedCrisisManager != null
            && subscribedCrisisManager.GetActiveMission(GetPlayerCivilization()) == null)
        {
            var available = GetAvailableCrisisMissions(pendingSelectionCrisis);
            if (available.Count > 0)
            {
                ShowMissionSelectionDirectly(pendingSelectionCrisis, available);
                return;
            }
        }
    }

    /// <summary>
    /// Show the mission selection screen immediately, bypassing the modal queue.
    /// This is the single entry point for showing mission selection — never use QueueSelection.
    /// </summary>
    private void ShowMissionSelectionDirectly(CrisisData crisis, List<MissionData> missions)
    {
        if (crisis == null || missions == null || missions.Count == 0) return;
        if (modalVisible)
        {
            Debug.Log("[UIManager] ShowMissionSelectionDirectly: a modal is already visible, deferring.");
            return;
        }

        EnsureMissionCrisisPrefabViews();

        if (selectionPopupInstance != null)
        {
            if (!selectionPopupInstance.gameObject.activeSelf)
                selectionPopupInstance.gameObject.SetActive(true);

            var request = new ModalRequest
            {
                kind = ModalKind.Selection,
                title = string.IsNullOrWhiteSpace(crisis.crisisName) ? "Choose a Mission" : crisis.crisisName,
                body = "Choose one mission to pursue during this crisis.",
                crisis = crisis,
                missions = missions,
                allowClose = false,
            };

            var options = BuildSelectionOptions(request);
            Debug.Log($"[UIManager] ShowMissionSelectionDirectly: title={request.title} optionCount={options?.Count}");
            selectionPopupInstance.Show(
                request.title,
                request.body,
                options,
                index => OnSelectionOptionChosen(request, index));

            SuppressGameplayHudForMissionCrisisModal();
            modalVisible = true;
            StartModalWatchdog();
            return;
        }

        // Fallback to queue if prefab not available
        Debug.Log("[UIManager] ShowMissionSelectionDirectly: no prefab instance, falling back to queue.");
        QueueSelection(crisis, missions);
    }

    private void PurgeSelectionModalsFromQueue()
    {
        if (modalQueue.Count == 0) return;
        var kept = new Queue<ModalRequest>();
        while (modalQueue.Count > 0)
        {
            var item = modalQueue.Dequeue();
            if (item.kind != ModalKind.Selection)
                kept.Enqueue(item);
        }
        while (kept.Count > 0)
            modalQueue.Enqueue(kept.Dequeue());
        Debug.Log($"[UIManager] PurgeSelectionModalsFromQueue: queue now has {modalQueue.Count} items.");
    }

    private void StartModalWatchdog()
    {
        StopModalWatchdog();
        modalWatchdog = StartCoroutine(ModalWatchdogCoroutine(30f));
    }

    private void StopModalWatchdog()
    {
        if (modalWatchdog != null)
        {
            StopCoroutine(modalWatchdog);
            modalWatchdog = null;
        }
    }

    private IEnumerator ModalWatchdogCoroutine(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (modalVisible)
        {
            Debug.LogWarning($"[UIManager] Modal watchdog fired after {timeout}s — force-closing stuck modal.");
            CloseCurrentMissionCrisisModal();
        }
        modalWatchdog = null;
    }

    private void SuppressGameplayHudForMissionCrisisModal()
    {
        restoreGameplayHudAfterModal = gameplayHudRoot != null && gameplayHudRoot.activeSelf;
        restoreUnitInfoPanelAfterModal = unitInfoPanel != null && unitInfoPanel.activeSelf;

        if (cachedMinimapUI == null)
            cachedMinimapUI = FindAnyObjectByType<MinimapUI>(FindObjectsInactive.Include);
        restoreMinimapAfterModal = cachedMinimapUI != null && cachedMinimapUI.gameObject.activeSelf;

        if (unitInfoPanel != null)
            unitInfoPanel.SetActive(false);
        if (gameplayHudRoot != null)
            gameplayHudRoot.SetActive(false);
        if (cachedMinimapUI != null)
            cachedMinimapUI.gameObject.SetActive(false);
    }

    private void RestoreGameplayHudAfterMissionCrisisModal()
    {
        if (gameplayHudRoot != null)
            gameplayHudRoot.SetActive(restoreGameplayHudAfterModal && !IsLoadingActive());

        if (unitInfoPanel != null)
            unitInfoPanel.SetActive(restoreUnitInfoPanelAfterModal && !IsLoadingActive());

        if (cachedMinimapUI != null)
            cachedMinimapUI.gameObject.SetActive(restoreMinimapAfterModal && !IsLoadingActive());

        restoreGameplayHudAfterModal = false;
        restoreUnitInfoPanelAfterModal = false;
        restoreMinimapAfterModal = false;
    }

    private bool TryShowPrefabModal(ModalRequest request)
    {
        EnsureMissionCrisisPrefabViews();
        Debug.Log($"[UIManager] TryShowPrefabModal: narrativePopup={narrativePopupInstance != null} selectionPopup={selectionPopupInstance != null} kind={request?.kind}");
        if (request == null) return false;

        if (request.kind == ModalKind.Narrative && narrativePopupInstance != null)
        {
            if (!narrativePopupInstance.gameObject.activeSelf)
                narrativePopupInstance.gameObject.SetActive(true);
            narrativePopupInstance.Show(
                request.title,
                request.body,
                request.image,
                request.rewardTitle,
                request.rewardBody,
                request.rewardImage,
                CloseCurrentMissionCrisisModal);
            return true;
        }

        if (request.kind == ModalKind.Selection && selectionPopupInstance != null)
        {
            if (!selectionPopupInstance.gameObject.activeSelf)
                selectionPopupInstance.gameObject.SetActive(true);
            var options = BuildSelectionOptions(request);
            if (options == null || options.Count == 0)
            {
                LogMissionSelectionBlocked("TryShowPrefabModal selection with no built options", request.crisis, request.missions);
            }
            Debug.Log($"[UIManager] Showing selection popup: title={request.title} optionCount={options?.Count} popupRoot={selectionPopupInstance.gameObject.name} active={selectionPopupInstance.gameObject.activeSelf}");
            selectionPopupInstance.Show(
                request.title,
                request.body,
                options,
                index => OnSelectionOptionChosen(request, index));
            Debug.Log($"[UIManager] Selection popup Show() called. IsVisible={selectionPopupInstance.IsVisible} go.activeSelf={selectionPopupInstance.gameObject.activeSelf}");
            return true;
        }

        Debug.Log($"[UIManager] TryShowPrefabModal: no matching handler for kind={request.kind}");
        return false;
    }

    private void EnsureMissionCrisisPrefabViews()
    {
        if (narrativePopupInstance == null)
        {
            // Prefer an existing scene instance over instantiating a duplicate.
            narrativePopupInstance = FindAnyObjectByType<MissionNarrativePopupUI>(FindObjectsInactive.Include);
            Debug.Log($"[UIManager] EnsurePrefabViews: FindAnyObjectByType<NarrativePopup> = {(narrativePopupInstance != null ? narrativePopupInstance.gameObject.name : "null")}");
            if (narrativePopupInstance == null && missionNarrativePopupPrefab != null)
            {
                narrativePopupInstance = Instantiate(missionNarrativePopupPrefab);
                narrativePopupInstance.gameObject.name = missionNarrativePopupPrefab.gameObject.name;
                Debug.Log($"[UIManager] Instantiated narrative popup: {narrativePopupInstance.gameObject.name}");
            }
            if (narrativePopupInstance != null)
                narrativePopupInstance.Hide();
        }

        if (selectionPopupInstance == null)
        {
            selectionPopupInstance = FindAnyObjectByType<MissionSelectionPopupUI>(FindObjectsInactive.Include);
            Debug.Log($"[UIManager] EnsurePrefabViews: FindAnyObjectByType<SelectionPopup> = {(selectionPopupInstance != null ? selectionPopupInstance.gameObject.name : "null")} prefab={(missionSelectionPopupPrefab != null ? missionSelectionPopupPrefab.gameObject.name : "null")}");
            if (selectionPopupInstance == null && missionSelectionPopupPrefab != null)
            {
                selectionPopupInstance = Instantiate(missionSelectionPopupPrefab);
                selectionPopupInstance.gameObject.name = missionSelectionPopupPrefab.gameObject.name;
                Debug.Log($"[UIManager] Instantiated selection popup: {selectionPopupInstance.gameObject.name}");
            }
            if (selectionPopupInstance != null)
                selectionPopupInstance.Hide();
        }

        // If the selection popup was still not found but the narrative popup contains
        // a MissionSelectionPopupUI child, use it instead of falling back to runtime UI.
        if (selectionPopupInstance == null && narrativePopupInstance != null)
        {
            selectionPopupInstance = narrativePopupInstance.GetComponentInChildren<MissionSelectionPopupUI>(true);
            Debug.Log($"[UIManager] EnsurePrefabViews: child fallback selectionPopup = {(selectionPopupInstance != null ? selectionPopupInstance.gameObject.name : "null")}");
        }
    }

    // Prefabs are standalone and contain their own Canvas; no popup parent resolution required.

    private void EnsureMissionCrisisFallbackUi()
    {
        if (rootObject != null) return;

        rootCanvas = ResolveMissionCrisisFallbackCanvas();
        if (rootCanvas == null)
        {
            Debug.LogWarning("UIManager: No Canvas found for mission/crisis fallback UI.");
            return;
        }

        rootObject = new GameObject("MissionCrisisRuntimeUI", typeof(RectTransform), typeof(CanvasRenderer));
        var rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.SetParent(rootCanvas.transform, false);
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.SetAsLastSibling();

        backdropObject = CreateMissionCrisisFallbackPanel("Backdrop", rootRect, new Color(0f, 0f, 0f, 0.78f));
        var backdropRect = backdropObject.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        backdropObject.SetActive(false);

        narrativePanel = BuildMissionCrisisFallbackNarrativePanel(rootRect);
        selectionPanel = BuildMissionCrisisFallbackSelectionPanel(rootRect);
    }

    private GameObject BuildMissionCrisisFallbackNarrativePanel(RectTransform parent)
    {
        var panel = CreateMissionCrisisFallbackPanel("NarrativePanel", parent, new Color(0.1f, 0.11f, 0.13f, 0.98f));
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.2f, 0.12f);
        rect.anchorMax = new Vector2(0.8f, 0.88f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 16;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        narrativeTitle = CreateMissionCrisisFallbackText("Title", panel.transform, 30, FontStyles.Bold, TextAlignmentOptions.Center);
        narrativeTitle.color = Color.white;

        narrativeImage = CreateMissionCrisisFallbackImage("Image", panel.transform, true);
        var imageLayout = narrativeImage.gameObject.AddComponent<LayoutElement>();
        imageLayout.preferredHeight = 260f;
        imageLayout.minHeight = 180f;

        var scrollObject = new GameObject("BodyScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
        scrollObject.transform.SetParent(panel.transform, false);
        var scrollRect = scrollObject.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 1f);
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;
        scrollObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        scrollObject.GetComponent<Mask>().showMaskGraphic = false;
        scrollObject.GetComponent<LayoutElement>().flexibleHeight = 1f;
        scrollObject.GetComponent<LayoutElement>().minHeight = 180f;

        var viewport = scrollObject.GetComponent<ScrollRect>();
        viewport.horizontal = false;
        viewport.movementType = ScrollRect.MovementType.Clamped;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(scrollObject.transform, false);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(16f, 16f);
        contentRect.offsetMax = new Vector2(-16f, -16f);
        content.GetComponent<VerticalLayoutGroup>().childControlHeight = true;
        content.GetComponent<VerticalLayoutGroup>().childControlWidth = true;
        content.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;
        content.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        narrativeBody = CreateMissionCrisisFallbackText("Body", content.transform, 22, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        narrativeBody.textWrappingMode = TextWrappingModes.Normal;
        narrativeBody.color = new Color(0.92f, 0.92f, 0.92f, 1f);

        viewport.content = contentRect;
        viewport.viewport = scrollObject.GetComponent<RectTransform>();

        narrativeCloseButton = CreateMissionCrisisFallbackButton("CloseButton", panel.transform, out narrativeCloseText);
        narrativeCloseText.text = "Continue";
        narrativeCloseButton.onClick.AddListener(OnMissionCrisisFallbackNarrativeCloseClicked);

        panel.SetActive(false);
        return panel;
    }

    private GameObject BuildMissionCrisisFallbackSelectionPanel(RectTransform parent)
    {
        var panel = CreateMissionCrisisFallbackPanel("SelectionPanel", parent, new Color(0.1f, 0.11f, 0.13f, 0.98f));
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.08f, 0.08f);
        rect.anchorMax = new Vector2(0.92f, 0.92f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 16;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        selectionTitle = CreateMissionCrisisFallbackText("SelectionTitle", panel.transform, 30, FontStyles.Bold, TextAlignmentOptions.Center);
        selectionTitle.color = Color.white;

        selectionSubtitle = CreateMissionCrisisFallbackText("SelectionSubtitle", panel.transform, 22, FontStyles.Normal, TextAlignmentOptions.Center);
        selectionSubtitle.color = new Color(0.9f, 0.9f, 0.9f, 1f);

        var gridObject = new GameObject("MissionGrid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
        gridObject.transform.SetParent(panel.transform, false);
        selectionGrid = gridObject.transform;

        var gridRect = gridObject.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0f, 0f);
        gridRect.anchorMax = new Vector2(1f, 1f);
        gridRect.offsetMin = Vector2.zero;
        gridRect.offsetMax = Vector2.zero;

        var grid = gridObject.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(360f, 300f);
        grid.spacing = new Vector2(16f, 16f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;

        gridObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        gridObject.GetComponent<LayoutElement>().flexibleHeight = 1f;

        selectionCloseButton = CreateMissionCrisisFallbackButton("SelectionCloseButton", panel.transform, out selectionCloseText);
        selectionCloseText.text = "Decide Later";
        selectionCloseButton.onClick.AddListener(CloseCurrentMissionCrisisModal);

        panel.SetActive(false);
        return panel;
    }

    private void ShowMissionCrisisFallbackNarrative(ModalRequest request)
    {
        selectionPanel.SetActive(false);
        narrativePanel.SetActive(true);

        narrativeTitle.text = request.title;
        narrativeBody.text = BuildRuntimeNarrativeBody(request);
        narrativeCloseAction = request.onConfirm;
        narrativeCloseText.text = request.onConfirm != null ? "Accept" : "Continue";

        if (request.image != null)
        {
            narrativeImage.sprite = request.image;
            narrativeImage.preserveAspect = true;
            narrativeImage.gameObject.SetActive(true);
        }
        else
        {
            narrativeImage.sprite = null;
            narrativeImage.gameObject.SetActive(false);
        }
    }

    private void ShowMissionCrisisFallbackSelection(ModalRequest request)
    {
        narrativePanel.SetActive(false);
        selectionPanel.SetActive(true);

        selectionTitle.text = string.IsNullOrWhiteSpace(request.title)
            ? "Choose a Mission"
            : request.title;
        selectionSubtitle.text = string.IsNullOrWhiteSpace(request.body)
            ? string.Empty
            : request.body;

        foreach (Transform child in selectionGrid)
            Destroy(child.gameObject);

        var missions = request.missions ?? new List<MissionData>();
        foreach (var mission in missions)
            BuildMissionCrisisFallbackMissionCard(selectionGrid, mission, request.crisis);

        // Selection must be chosen; hide the close/defer button.
        selectionCloseButton.gameObject.SetActive(false);
    }

    private string BuildRuntimeNarrativeBody(ModalRequest request)
    {
        if (request == null) return string.Empty;

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.body))
            builder.Append(request.body.Trim());

        if (!string.IsNullOrWhiteSpace(request.rewardTitle) || !string.IsNullOrWhiteSpace(request.rewardBody))
        {
            if (builder.Length > 0) builder.Append("\n\n");
            if (!string.IsNullOrWhiteSpace(request.rewardTitle))
                builder.AppendLine(request.rewardTitle.Trim());
            if (!string.IsNullOrWhiteSpace(request.rewardBody))
                builder.Append(request.rewardBody.Trim());
        }

        return builder.ToString();
    }

    private List<MissionSelectionPopupUI.OptionData> BuildSelectionOptions(ModalRequest request)
    {
        var options = new List<MissionSelectionPopupUI.OptionData>(4);
        if (request?.missions == null) return options;

        int count = Mathf.Min(4, request.missions.Count);
        for (int i = 0; i < count; i++)
        {
            var mission = request.missions[i];
            options.Add(new MissionSelectionPopupUI.OptionData
            {
                title = mission != null ? mission.missionName : string.Empty,
                body = BuildMissionCardText(mission),
                splash = mission != null && mission.splashImage != null ? mission.splashImage : mission != null ? mission.icon : null,
                interactable = mission != null,
            });
        }

        return options;
    }

    private void OnSelectionOptionChosen(ModalRequest request, int index)
    {
        if (request?.missions == null || index < 0 || index >= request.missions.Count)
        {
            ShowNotification("That mission option is not available.");
            return;
        }

        OnChooseMissionClicked(request.crisis, request.missions[index]);
    }

    private void BuildMissionCrisisFallbackMissionCard(Transform parent, MissionData mission, CrisisData crisis)
    {
        var card = CreateMissionCrisisFallbackPanel(mission != null ? mission.missionName : "MissionCard", parent as RectTransform, new Color(0.15f, 0.18f, 0.2f, 1f));
        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 16, 16);
        layout.spacing = 10;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var image = CreateMissionCrisisFallbackImage("Splash", card.transform, true);
        image.sprite = mission != null && mission.splashImage != null ? mission.splashImage : mission != null ? mission.icon : null;
        image.gameObject.SetActive(image.sprite != null);
        var imageLayout = image.gameObject.AddComponent<LayoutElement>();
        imageLayout.preferredHeight = 110f;

        var title = CreateMissionCrisisFallbackText("Title", card.transform, 24, FontStyles.Bold, TextAlignmentOptions.Center);
        title.text = mission != null && !string.IsNullOrWhiteSpace(mission.missionName) ? mission.missionName : "Unnamed Mission";
        title.color = Color.white;

        var body = CreateMissionCrisisFallbackText("Body", card.transform, 18, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        body.text = BuildMissionCardText(mission);
        body.color = new Color(0.92f, 0.92f, 0.92f, 1f);

        var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(card.transform, false);
        spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;

        var chooseButton = CreateMissionCrisisFallbackButton("ChooseButton", card.transform, out var chooseText);
        chooseText.text = "Choose Mission";
        chooseButton.onClick.AddListener(() => OnChooseMissionClicked(crisis, mission));
    }

    private void OnChooseMissionClicked(CrisisData crisis, MissionData mission)
    {
        var civ = GetPlayerCivilization();
        if (civ == null || mission == null || subscribedCrisisManager == null)
        {
            ShowNotification("Mission selection is not ready yet.");
            return;
        }

        if (subscribedCrisisManager.StartMission(civ, mission))
        {
            pendingSelectionCrisis = null;
            PurgeSelectionModalsFromQueue();
            CloseCurrentMissionCrisisModal();
            return;
        }

        ShowNotification($"Could not start mission '{mission.missionName}'.");
    }

    private void OnMissionCrisisFallbackNarrativeCloseClicked()
    {
        var action = narrativeCloseAction;
        CloseCurrentMissionCrisisModal();
        action?.Invoke();
    }

    private List<MissionData> GetAvailableCrisisMissions(CrisisData crisis)
    {
        var result = new List<MissionData>();
        if (crisis == null || subscribedCrisisManager == null) return result;

        var civ = GetPlayerCivilization();
        if (civ == null) return result;

        return subscribedCrisisManager.GetAvailableMissions(civ, crisis);
    }

    private Civilization GetPlayerCivilization()
    {
        if (CivilizationManager.Instance != null && CivilizationManager.Instance.playerCiv != null)
            return CivilizationManager.Instance.playerCiv;

        if (TurnManager.Instance != null)
            return TurnManager.Instance.playerCiv;

        return null;
    }

    private bool IsPlayerCivilization(Civilization civ)
    {
        var player = GetPlayerCivilization();
        return civ != null && player != null && civ == player;
    }

    private string ResolveMissionStartBody(MissionData mission)
    {
        if (mission == null) return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mission.flavorText)) parts.Add(mission.flavorText.Trim());
        if (!string.IsNullOrWhiteSpace(mission.description)) parts.Add(mission.description.Trim());

        return string.Join("\n\n", parts);
    }

    private string BuildMissionCardText(MissionData mission)
    {
        if (mission == null) return string.Empty;

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(mission.description))
            sb.AppendLine(mission.description.Trim());

        if (mission.objectives != null && mission.objectives.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Objectives:");
            int count = Mathf.Min(3, mission.objectives.Count);
            for (int i = 0; i < count; i++)
            {
                var objective = mission.objectives[i];
                if (objective == null) continue;

                string label = !string.IsNullOrWhiteSpace(objective.objectiveName)
                    ? objective.objectiveName.Trim()
                    : objective.type.ToString();
                sb.Append("• ").AppendLine(label);
            }
        }

        if (mission.constraints != null && mission.constraints.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Constraints:");
            foreach (var constraint in mission.constraints)
            {
                string line = FormatMissionConstraint(constraint, mission);
                if (!string.IsNullOrWhiteSpace(line))
                    sb.Append("• ").AppendLine(line);
            }
        }

        return sb.ToString().Trim();
    }

    private string FormatMissionConstraint(MissionData.MissionConstraint constraint, MissionData mission)
    {
        if (constraint == null) return string.Empty;

        string activationText = string.Empty;
        if (constraint.activatesAfterObjectiveIndex >= 0)
        {
            int objectiveNumber = constraint.activatesAfterObjectiveIndex + 1;
            activationText = $" after objective {objectiveNumber}";
        }

        return constraint.type switch
        {
            MissionData.ConstraintType.NoUnitLosses => BuildNoUnitLossesConstraintText(constraint, activationText),
            MissionData.ConstraintType.MaintainImprovementCount => BuildMaintainCountConstraintText(
                constraint,
                activationText,
                "improvement",
                GetImprovementConstraintTargetLabel(constraint),
                mission),
            MissionData.ConstraintType.MaintainBuildingCount => BuildMaintainCountConstraintText(
                constraint,
                activationText,
                "building",
                GetBuildingConstraintTargetLabel(constraint),
                mission),
            _ => string.IsNullOrWhiteSpace(constraint.failureFlavorText)
                ? $"Must satisfy mission constraint{activationText}."
                : constraint.failureFlavorText.Trim(),
        };
    }

    private string BuildNoUnitLossesConstraintText(MissionData.MissionConstraint constraint, string activationText)
    {
        string target = GetUnitConstraintTargetLabel(constraint);
        if (string.IsNullOrWhiteSpace(target))
            return $"Do not lose any units{activationText}.";

        return $"Do not lose {target}{activationText}.";
    }

    private string BuildMaintainCountConstraintText(
        MissionData.MissionConstraint constraint,
        string activationText,
        string noun,
        string targetLabel,
        MissionData mission)
    {
        string comparison = constraint.comparison switch
        {
            MissionData.CountComparison.AtLeast => $"Maintain at least {constraint.targetValue}",
            MissionData.CountComparison.AtMost => $"Maintain at most {constraint.targetValue}",
            _ => $"Maintain exactly {constraint.targetValue}",
        };

        string suffix = string.IsNullOrWhiteSpace(targetLabel) ? $" {noun}" : $" {targetLabel}";
        string sentence = comparison + suffix + activationText + ".";

        if (!string.IsNullOrWhiteSpace(constraint.failureFlavorText))
            sentence += $" Fails if {constraint.failureFlavorText.Trim()}";

        return sentence;
    }

    private string GetUnitConstraintTargetLabel(MissionData.MissionConstraint constraint)
    {
        if (constraint == null) return string.Empty;

        if (constraint.specificUnit != null)
            return $"the {constraint.specificUnit.unitName}";

        var parts = new List<string>();

        if (constraint.specificUnits != null)
        {
            foreach (var unit in constraint.specificUnits)
            {
                if (unit != null)
                    parts.Add(unit.unitName);
            }
        }

        if (constraint.specificWorkerUnits != null)
        {
            foreach (var worker in constraint.specificWorkerUnits)
            {
                if (worker != null)
                    parts.Add(worker.unitName);
            }
        }

        if (parts.Count > 0)
            return $"any of these units: {string.Join(", ", parts)}";

        if (constraint.specificCategories != null && constraint.specificCategories.Length > 0)
            return $"any {string.Join(" or ", constraint.specificCategories)} units";

        return string.Empty;
    }

    private string GetImprovementConstraintTargetLabel(MissionData.MissionConstraint constraint)
    {
        if (constraint == null) return string.Empty;
        if (constraint.specificImprovement != null)
            return $"{constraint.specificImprovement.improvementName} improvements";

        if (constraint.specificImprovements != null)
        {
            var names = new List<string>();
            foreach (var improvement in constraint.specificImprovements)
            {
                if (improvement != null)
                    names.Add(improvement.improvementName);
            }

            if (names.Count > 0)
                return $"improvements of these types: {string.Join(", ", names)}";
        }

        return "improvements";
    }

    private string GetBuildingConstraintTargetLabel(MissionData.MissionConstraint constraint)
    {
        if (constraint == null) return string.Empty;
        if (constraint.specificBuilding != null)
            return $"{constraint.specificBuilding.buildingName} buildings";

        if (constraint.specificBuildings != null)
        {
            var names = new List<string>();
            foreach (var building in constraint.specificBuildings)
            {
                if (building != null)
                    names.Add(building.buildingName);
            }

            if (names.Count > 0)
                return $"buildings of these types: {string.Join(", ", names)}";
        }

        return "buildings";
    }

    private bool TryGetRewardDisplay(CrisisManager.MissionState state, out string rewardTitle, out string rewardBody, out Sprite rewardSprite)
    {
        rewardTitle = null;
        rewardBody = null;
        rewardSprite = null;

        var mission = state?.mission;
        var tiers = mission?.rewardTiers;
        if (tiers == null || tiers.Length == 0) return false;

        MissionData.RewardTier chosenTier = null;
        foreach (var tier in tiers)
        {
            if (tier == null || tier.rewardLegacy == null) continue;
            if (state.CompletedObjectiveCount < tier.requiredObjectivesCompleted) continue;

            if (chosenTier == null || tier.requiredObjectivesCompleted > chosenTier.requiredObjectivesCompleted)
                chosenTier = tier;
        }

        if (chosenTier == null || chosenTier.rewardLegacy == null) return false;

        var legacy = chosenTier.rewardLegacy;
        rewardTitle = string.IsNullOrWhiteSpace(chosenTier.tierName)
            ? legacy.legacyName
            : $"{chosenTier.tierName}: {legacy.legacyName}";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(chosenTier.completionFlavorText))
            parts.Add(chosenTier.completionFlavorText.Trim());
        if (!string.IsNullOrWhiteSpace(legacy.flavorText))
            parts.Add(legacy.flavorText.Trim());
        else if (!string.IsNullOrWhiteSpace(legacy.description))
            parts.Add(legacy.description.Trim());

        rewardBody = string.Join("\n\n", parts);
        rewardSprite = legacy.bannerImage != null ? legacy.bannerImage : legacy.icon;
        return true;
    }

    private Canvas ResolveMissionCrisisFallbackCanvas()
    {
        if (gameplayHudRoot != null)
        {
            var canvas = gameplayHudRoot.GetComponentInParent<Canvas>();
            if (canvas != null) return canvas;
        }

        if (notificationPanel != null)
        {
            var canvas = notificationPanel.GetComponentInParent<Canvas>();
            if (canvas != null) return canvas;
        }

        return FindFirstObjectByType<Canvas>();
    }

    private GameObject CreateMissionCrisisFallbackPanel(string name, RectTransform parent, Color color)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    private Image CreateMissionCrisisFallbackImage(string name, Transform parent, bool preserveAspect)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        var image = imageObject.GetComponent<Image>();
        image.color = Color.white;
        image.preserveAspect = preserveAspect;
        return image;
    }

    private TextMeshProUGUI CreateMissionCrisisFallbackText(string name, Transform parent, float fontSize, FontStyles style, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = defaultFont;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.text = string.Empty;
        return text;
    }

    private Button CreateMissionCrisisFallbackButton(string name, Transform parent, out TextMeshProUGUI buttonText)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.GetComponent<Image>().color = new Color(0.66f, 0.48f, 0.23f, 1f);

        var layout = buttonObject.GetComponent<LayoutElement>();
        layout.preferredHeight = 44f;

        var button = buttonObject.GetComponent<Button>();

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        buttonText = textObject.GetComponent<TextMeshProUGUI>();
        buttonText.font = defaultFont;
        buttonText.fontSize = 20f;
        buttonText.fontStyle = FontStyles.Bold;
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.color = new Color(0.08f, 0.08f, 0.08f, 1f);

        return button;
    }
}
