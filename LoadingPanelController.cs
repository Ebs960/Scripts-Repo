using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoadingPanelController : MonoBehaviour
{
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject loadingPanel;

    [Header("Space Loading Integration")]
    [Tooltip("Prefab of the space loading panel to instantiate when needed")]
    public GameObject spaceLoadingPanelPrefab;
    [Tooltip("Reference to instantiated space loading panel (auto-assigned)")]
    public SpaceLoadingPanelController spaceLoadingPanel;
    [Tooltip("Should this panel automatically switch to space loading for space scenarios?")]
    public bool enableSpaceLoadingIntegration = true;
    [Tooltip("Should the space loading panel persist between scenes?")]
    public bool persistSpaceLoadingPanel = true;

    void Awake()
    {
        // Auto-find loading panel if not assigned
        if (loadingPanel == null)
            loadingPanel = gameObject;
            
        // Initialize space loading panel if prefab is assigned
        InitializeSpaceLoadingPanel();
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
    }

    /// <summary>
    /// Hide the regular loading panel
    /// </summary>
    public void HideLoading()
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(false);
    }

    /// <summary>
    /// Show space-specific loading if applicable, otherwise show regular loading
    /// </summary>
    public void ShowLoading(string status, bool isSpaceTravel = false)
    {
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
        ShowLoading(status);
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
}