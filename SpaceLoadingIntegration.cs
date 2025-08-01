using UnityEngine;

/// <summary>
/// Helper script to automatically set up space loading integration.
/// Attach this to any GameObject to automatically connect loading panels.
/// </summary>
public class SpaceLoadingIntegration : MonoBehaviour
{
    [Header("Auto-Setup Configuration")]
    [Tooltip("Space loading panel prefab to use")]
    public GameObject spaceLoadingPanelPrefab;
    
    [Tooltip("Should the space loading panel persist between scenes?")]
    public bool persistSpaceLoadingPanel = true;
    
    [Tooltip("Auto-setup on Start?")]
    public bool autoSetup = true;

    void Start()
    {
        if (autoSetup)
        {
            SetupSpaceLoadingIntegration();
        }
        
        // Ensure all space UI starts hidden
        HideAllSpaceUIOnStart();
    }

    /// <summary>
    /// Hide all space UI elements on game start
    /// </summary>
    private void HideAllSpaceUIOnStart()
    {
        // Hide space map UI
        var spaceMapUI = FindFirstObjectByType<SpaceMapUI>();
        if (spaceMapUI != null)
        {
            spaceMapUI.Hide();
        }
        
        // Hide space loading panels
        var spaceLoadingPanels = FindObjectsByType<SpaceLoadingPanelController>(FindObjectsSortMode.None);
        foreach (var panel in spaceLoadingPanels)
        {
            panel.HideSpaceLoading();
        }
        
        // Hide singleton instance
        if (SpaceLoadingPanelController.Instance != null)
        {
            SpaceLoadingPanelController.Instance.HideSpaceLoading();
        }
        
        Debug.Log("[SpaceLoadingIntegration] All space UI hidden on game start");
    }

    /// <summary>
    /// Sets up space loading integration automatically
    /// </summary>
    [ContextMenu("Setup Space Loading Integration")]
    public void SetupSpaceLoadingIntegration()
    {
        // Find all LoadingPanelControllers in the scene
        LoadingPanelController[] loadingPanels = FindObjectsByType<LoadingPanelController>(FindObjectsSortMode.None);
        
        int setupCount = 0;
        
        foreach (var loadingPanel in loadingPanels)
        {
            // Only setup if they don't already have a prefab assigned
            if (loadingPanel.spaceLoadingPanelPrefab == null && spaceLoadingPanelPrefab != null)
            {
                loadingPanel.spaceLoadingPanelPrefab = spaceLoadingPanelPrefab;
                loadingPanel.persistSpaceLoadingPanel = persistSpaceLoadingPanel;
                loadingPanel.enableSpaceLoadingIntegration = true;
                
                Debug.Log($"[SpaceLoadingIntegration] Configured LoadingPanelController on {loadingPanel.gameObject.name}");
                setupCount++;
            }
        }
        
        // If no loading panels found, create a basic one
        if (loadingPanels.Length == 0)
        {
            CreateBasicLoadingPanel();
            setupCount++;
        }
        
        // After setup, ensure all space loading panels are hidden
        StartCoroutine(HideAllSpaceLoadingPanels());
        
        Debug.Log($"[SpaceLoadingIntegration] Setup complete! Configured {setupCount} loading panel(s)");
    }
    
    /// <summary>
    /// Creates a basic loading panel if none exists
    /// </summary>
    private void CreateBasicLoadingPanel()
    {
        GameObject loadingGO = new GameObject("LoadingPanelController");
        LoadingPanelController loadingPanel = loadingGO.AddComponent<LoadingPanelController>();
        
        // Configure it
        loadingPanel.spaceLoadingPanelPrefab = spaceLoadingPanelPrefab;
        loadingPanel.persistSpaceLoadingPanel = persistSpaceLoadingPanel;
        loadingPanel.enableSpaceLoadingIntegration = true;
        
        // IMPORTANT: Hide any created space loading panels
        StartCoroutine(HideSpaceLoadingAfterFrame(loadingPanel));
        
        Debug.Log("[SpaceLoadingIntegration] Created basic LoadingPanelController");
    }
    
    /// <summary>
    /// Hide space loading panel after it's been created and initialized
    /// </summary>
    private System.Collections.IEnumerator HideSpaceLoadingAfterFrame(LoadingPanelController loadingPanel)
    {
        // Wait a frame for initialization
        yield return null;
        
        // Ensure space loading is hidden
        if (loadingPanel.spaceLoadingPanel != null)
        {
            loadingPanel.spaceLoadingPanel.HideSpaceLoading();
        }
    }
    
    /// <summary>
    /// Hide all space loading panels after setup
    /// </summary>
    private System.Collections.IEnumerator HideAllSpaceLoadingPanels()
    {
        yield return null; // Wait a frame
        
        // Hide singleton instance if it exists
        if (SpaceLoadingPanelController.Instance != null)
        {
            SpaceLoadingPanelController.Instance.HideSpaceLoading();
        }
        
        // Hide any other space loading panels
        SpaceLoadingPanelController[] spacePanels = FindObjectsByType<SpaceLoadingPanelController>(FindObjectsSortMode.None);
        foreach (var panel in spacePanels)
        {
            panel.HideSpaceLoading();
        }
        
        Debug.Log("[SpaceLoadingIntegration] All space loading panels hidden");
    }
    
    /// <summary>
    /// Test the space loading system
    /// </summary>
    [ContextMenu("Test Space Loading")]
    public void TestSpaceLoading()
    {
        LoadingPanelController loadingPanel = FindFirstObjectByType<LoadingPanelController>();
        if (loadingPanel != null)
        {
            StartCoroutine(TestSpaceLoadingCoroutine(loadingPanel));
        }
        else
        {
            Debug.LogWarning("[SpaceLoadingIntegration] No LoadingPanelController found for testing");
        }
    }
    
    private System.Collections.IEnumerator TestSpaceLoadingCoroutine(LoadingPanelController loadingPanel)
    {
        Debug.Log("[SpaceLoadingIntegration] Testing space loading...");
        
        // Test space loading
        loadingPanel.ShowLoading("Traveling to Mars...", true);
        yield return new UnityEngine.WaitForSeconds(3f);
        
        // Test regular loading
        loadingPanel.ShowLoading("Loading game data...", false);
        yield return new UnityEngine.WaitForSeconds(2f);
        
        // Hide all
        loadingPanel.HideAllLoading();
        
        Debug.Log("[SpaceLoadingIntegration] Test complete!");
    }
}
