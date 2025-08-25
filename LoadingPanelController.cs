using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class LoadingPanelController : MonoBehaviour
{
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject loadingPanel;

    [Header("UI Blocking System")]
    [Tooltip("Hide all game UI while loading panel is active")]
    [SerializeField] private bool hideAllUIWhileLoading = true;

    [Header("Space Loading Integration")]
    [Tooltip("Prefab of the space loading panel to instantiate when needed")]
    public GameObject spaceLoadingPanelPrefab;
    [Tooltip("Reference to instantiated space loading panel (auto-assigned)")]
    public SpaceLoadingPanelController spaceLoadingPanel;
    [Tooltip("Should this panel automatically switch to space loading for space scenarios?")]
    public bool enableSpaceLoadingIntegration = true;
    [Tooltip("Should the space loading panel persist between scenes?")]
    public bool persistSpaceLoadingPanel = true;

    // UI Management for blocking system
    private static LoadingPanelController instance;
    private readonly List<GameObject> hiddenUIElements = new List<GameObject>();
    private readonly Dictionary<GameObject, bool> originalUIStates = new Dictionary<GameObject, bool>();
    
    public static LoadingPanelController Instance => instance;

    void Awake()
    {
        // Singleton pattern
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        
        // Auto-find loading panel if not assigned
        if (loadingPanel == null)
            loadingPanel = gameObject;
            
        // Initialize space loading panel if prefab is assigned
        InitializeSpaceLoadingPanel();
        
        // IMPORTANT: Hide all UI immediately on startup to prevent premature visibility
        if (hideAllUIWhileLoading)
        {
            HideAllGameUI();
        }
    }

    /// <summary>
    /// Initialize or find the space loading panel
    /// </summary>
    private void InitializeSpaceLoadingPanel()
    {
        // Try to find existing space loading panel first
        if (spaceLoadingPanel == null)
        {
            spaceLoadingPanel = FindFirstObjectByType<SpaceLoadingPanelController>();
        }
        
        // If none found and we have a prefab, instantiate it
        if (spaceLoadingPanel == null && spaceLoadingPanelPrefab != null)
        {
            GameObject spaceLoadingGO = Instantiate(spaceLoadingPanelPrefab);
            spaceLoadingPanel = spaceLoadingGO.GetComponent<SpaceLoadingPanelController>();
            
            if (spaceLoadingPanel != null)
            {
                // Make it persistent if requested
                if (persistSpaceLoadingPanel)
                {
                    DontDestroyOnLoad(spaceLoadingGO);
                }
                
                // CRITICAL: Hide immediately after creation
                spaceLoadingPanel.HideSpaceLoading();
                Debug.Log("[LoadingPanel] Space loading panel instantiated and hidden");
            }
            else
            {
                Debug.LogWarning("[LoadingPanel] Space loading prefab does not have SpaceLoadingPanelController component");
                Destroy(spaceLoadingGO);
            }
        }
        
        // Ensure any existing panel is hidden
        if (spaceLoadingPanel != null)
        {
            spaceLoadingPanel.HideSpaceLoading();
        }
    }

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

    /// <summary>
    /// Show the regular loading panel
    /// </summary>
    public void ShowLoading(string status)
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(true);
        
        SetStatus(status);
        SetProgress(0f);
        
        // Hide all game UI while loading
        if (hideAllUIWhileLoading)
            HideAllGameUI();
    }

    /// <summary>
    /// Hide the regular loading panel
    /// </summary>
    public void HideLoading()
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(false);
            
        // Check if we should wait for minimap generation before restoring UI
        if (hideAllUIWhileLoading)
        {
            var minimapUI = FindFirstObjectByType<MinimapUI>();
            if (minimapUI != null && !minimapUI.MinimapsPreGenerated)
            {
                Debug.Log("[LoadingPanel] Minimap generation not complete, deferring UI restoration...");
                StartCoroutine(WaitForMinimapCompletionAndShowUI());
            }
            else
            {
                Debug.Log("[LoadingPanel] Minimap generation complete or no MinimapUI found, showing UI immediately");
                ShowAllGameUI();
            }
        }
    }

    /// <summary>
    /// Show space-specific loading if applicable, otherwise show regular loading
    /// </summary>
    public void ShowLoading(string status, bool isSpaceTravel = false)
    {
        // Hide all game UI first
        if (hideAllUIWhileLoading)
            HideAllGameUI();
            
        if (isSpaceTravel && enableSpaceLoadingIntegration)
        {
            // Ensure we have a space loading panel
            if (spaceLoadingPanel == null)
            {
                InitializeSpaceLoadingPanel();
            }
            
            if (spaceLoadingPanel != null)
            {
                // Use space loading panel for space travel
                spaceLoadingPanel.ShowSpaceLoading(status);
                return;
            }
            else
            {
                Debug.LogWarning("[LoadingPanel] Space travel requested but no space loading panel available, using regular loading");
            }
        }

        // Use regular loading panel
        if (loadingPanel != null)
            loadingPanel.SetActive(true);
        
        SetStatus(status);
        SetProgress(0f);
    }

    /// <summary>
    /// Hide both regular and space loading panels
    /// </summary>
    public void HideAllLoading()
    {
        HideLoading();

        if (spaceLoadingPanel != null)
        {
            spaceLoadingPanel.HideSpaceLoading();
        }
        
        // Ensure UI is restored when all loading is hidden
        if (hideAllUIWhileLoading)
            ShowAllGameUI();
    }
    
    /// <summary>
    /// Update space travel progress
    /// </summary>
    public void UpdateSpaceProgress(float progress, string status = "")
    {
        if (spaceLoadingPanel != null)
        {
            spaceLoadingPanel.SetProgress(progress);
            if (!string.IsNullOrEmpty(status))
            {
                spaceLoadingPanel.SetStatus(status);
            }
        }
    }

    /// <summary>
    /// Public method to manually assign space loading panel
    /// </summary>
    public void SetSpaceLoadingPanel(SpaceLoadingPanelController panel)
    {
        spaceLoadingPanel = panel;
    }
    
    /// <summary>
    /// Auto-detect if this is a space travel scenario based on status text
    /// </summary>
    public void ShowLoadingAuto(string status)
    {
        bool isSpaceTravel = status.ToLower().Contains("travel") || 
                           status.ToLower().Contains("planet") || 
                           status.ToLower().Contains("space") ||
                           status.ToLower().Contains("star chart") ||
                           status.ToLower().Contains("orbit");
        
        ShowLoading(status, isSpaceTravel);
    }
    
    /// <summary>
    /// Static helper method to show loading using singleton space loading panel
    /// </summary>
    public static void ShowSpaceLoadingStatic(string status)
    {
        if (SpaceLoadingPanelController.Instance != null)
        {
            SpaceLoadingPanelController.Instance.ShowSpaceLoading(status);
        }
        else
        {
            Debug.LogWarning("[LoadingPanel] No SpaceLoadingPanelController instance available for static call");
        }
    }
    
    /// <summary>
    /// Static helper method to hide space loading using singleton
    /// </summary>
    public static void HideSpaceLoadingStatic()
    {
        if (SpaceLoadingPanelController.Instance != null)
        {
            SpaceLoadingPanelController.Instance.HideSpaceLoading();
        }
    }
    
    /// <summary>
    /// Hide all game UI elements while loading
    /// </summary>
    private void HideAllGameUI()
    {
        hiddenUIElements.Clear();
        originalUIStates.Clear();
        
        // Hide UIManager and its panels
        if (UIManager.Instance != null)
        {
            StoreAndHideUIElement(UIManager.Instance.gameObject);
            
            // Hide specific panels managed by UIManager
            var panels = new GameObject[] {
                UIManager.Instance.playerUI,
                UIManager.Instance.notificationPanel,
                UIManager.Instance.cityPanel,
                UIManager.Instance.techPanel,
                UIManager.Instance.culturePanel,
                UIManager.Instance.religionPanel,
                UIManager.Instance.tradePanel,
                UIManager.Instance.diplomacyPanel,
                UIManager.Instance.equipmentPanel,
                UIManager.Instance.unitInfoPanel,
                UIManager.Instance.pauseMenuPanel
            };
            
            foreach (var panel in panels)
            {
                if (panel != null)
                    StoreAndHideUIElement(panel);
            }
            
            // Hide SpaceMapUI if it exists
            if (UIManager.Instance.spaceMapUI != null)
                StoreAndHideUIElement(UIManager.Instance.spaceMapUI.gameObject);
        }
        
        // Don't hide MinimapUI GameObject since it needs to stay active for coroutines
        // MinimapUI now handles its own UI element hiding internally
        var minimapUI = FindFirstObjectByType<MinimapUI>();
        if (minimapUI != null)
        {
            Debug.Log("[LoadingPanel] Found MinimapUI, but leaving GameObject active for coroutines");
            // Don't call StoreAndHideUIElement(minimapUI.gameObject) - let MinimapUI handle its own hiding
        }
        
        // Hide PlayerUI instances
        var playerUIs = FindObjectsByType<PlayerUI>(FindObjectsSortMode.None);
        foreach (var playerUI in playerUIs)
        {
            if (playerUI != null)
                StoreAndHideUIElement(playerUI.gameObject);
        }
        
        // Hide SpaceMapUI instances
        var spaceMapUIs = FindObjectsByType<SpaceMapUI>(FindObjectsSortMode.None);
        foreach (var spaceMapUI in spaceMapUIs)
        {
            if (spaceMapUI != null)
                StoreAndHideUIElement(spaceMapUI.gameObject);
        }
        
        // Hide other UI systems
        var cityUIs = FindObjectsByType<CityUI>(FindObjectsSortMode.None);
        foreach (var cityUI in cityUIs)
        {
            if (cityUI != null)
                StoreAndHideUIElement(cityUI.gameObject);
        }
        
        var techUIs = FindObjectsByType<TechUI>(FindObjectsSortMode.None);
        foreach (var techUI in techUIs)
        {
            if (techUI != null)
                StoreAndHideUIElement(techUI.gameObject);
        }
        
        var transportUIs = FindObjectsByType<TransportUIManager>(FindObjectsSortMode.None);
        foreach (var transportUI in transportUIs)
        {
            if (transportUI != null)
                StoreAndHideUIElement(transportUI.gameObject);
        }
        
        Debug.Log($"[LoadingPanel] Hidden {hiddenUIElements.Count} UI elements during loading");
    }
    
    /// <summary>
    /// Show all previously hidden game UI elements
    /// </summary>
    private void ShowAllGameUI()
    {
        // Double-check that minimap generation is complete before showing UI
        var minimapUI = FindFirstObjectByType<MinimapUI>();
        if (minimapUI != null && !minimapUI.MinimapsPreGenerated)
        {
            Debug.Log("[LoadingPanel] Minimap generation still in progress, not showing UI yet");
            return;
        }
        
        foreach (var uiElement in hiddenUIElements)
        {
            if (uiElement != null && originalUIStates.TryGetValue(uiElement, out bool originalState))
            {
                uiElement.SetActive(originalState);
            }
        }
        
        Debug.Log($"[LoadingPanel] Restored {hiddenUIElements.Count} UI elements after loading");
        
        hiddenUIElements.Clear();
        originalUIStates.Clear();
        
        // Trigger any UI systems that were waiting for loading to complete
        TriggerDeferredUIInitialization();
        
        // FIXED: Initialize music tracks when loading is complete and UI is shown
        if (MusicManager.Instance != null)
        {
            Debug.Log("[LoadingPanel] Initializing music tracks now that loading is complete...");
            MusicManager.Instance.InitializeMusicTracks();
        }
        else
        {
            Debug.LogWarning("[LoadingPanel] MusicManager.Instance is null, cannot initialize music tracks");
        }
    }
    
    /// <summary>
    /// Trigger UI systems that were waiting for loading to complete
    /// </summary>
    private void TriggerDeferredUIInitialization()
    {
        // Trigger MinimapUI initialization if it was deferred
        var minimapUI = FindFirstObjectByType<MinimapUI>();
        if (minimapUI != null)
        {
            minimapUI.TriggerDeferredInitialization();
            Debug.Log("[LoadingPanel] Triggered deferred MinimapUI initialization");
        }
        
        // Ensure UIManager panels are properly restored
        if (UIManager.Instance != null)
        {
            // PlayerUI should be active after loading
            if (UIManager.Instance.playerUI != null)
            {
                UIManager.Instance.playerUI.SetActive(true);
            }
        }
    }
    
    /// <summary>
    /// Wait for minimap generation to complete before showing UI
    /// </summary>
    private System.Collections.IEnumerator WaitForMinimapCompletionAndShowUI()
    {
        var minimapUI = FindFirstObjectByType<MinimapUI>();
        
        if (minimapUI != null)
        {
            Debug.Log("[LoadingPanel] Waiting for minimap pre-generation to complete...");
            
            // Wait until minimap generation is done
            while (!minimapUI.MinimapsPreGenerated)
            {
                yield return null;
            }
            
            Debug.Log("[LoadingPanel] Minimap pre-generation complete! Now showing UI...");
        }
        
        // Now show all the UI
        ShowAllGameUI();
    }
    
    /// <summary>
    /// Public method that can be called when minimap generation is complete
    /// to ensure UI is properly restored
    /// </summary>
    public void OnMinimapGenerationComplete()
    {
        if (hideAllUIWhileLoading && hiddenUIElements.Count > 0)
        {
            Debug.Log("[LoadingPanel] Minimap generation complete signal received, showing UI...");
            ShowAllGameUI();
        }
    }
    
    /// <summary>
    /// Store original state and hide a UI element
    /// </summary>
    private void StoreAndHideUIElement(GameObject uiElement)
    {
        if (uiElement != null)
        {
            originalUIStates[uiElement] = uiElement.activeSelf;
            if (uiElement.activeSelf)
            {
                uiElement.SetActive(false);
                hiddenUIElements.Add(uiElement);
            }
        }
    }
}