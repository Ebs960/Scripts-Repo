using UnityEngine;

/// <summary>
/// Example integration of Trade and Ruins systems with your civilization game
/// This shows how to connect the space features with your existing game mechanics
/// </summary>
public class SpaceSystemsIntegration : MonoBehaviour
{
    [Header("System References")]

    public AncientRuinsManager ruinsManager;
    public SolarSystemManager solarSystemManager;

    [Header("Integration Settings")]
    public bool enableTradeSystem = true;
    public bool enableRuinsSystem = true;
    public bool autoInitialize = true;

    [Header("Example Data")]
    public RuinsConfiguration ruinsConfig;

    void Start()
    {
        if (autoInitialize)
        {
            InitializeSpaceSystems();
        }
    }

    /// <summary>
    /// Initialize both trade and ruins systems
    /// </summary>
    public void InitializeSpaceSystems()
    {
        Debug.Log("[SpaceIntegration] Initializing space systems...");


        // Initialize Ruins System  
        if (enableRuinsSystem)
        {
            InitializeRuinsSystem();
        }

        Debug.Log("[SpaceIntegration] Space systems initialized successfully!");
    }


    /// <summary>
    /// Initialize the ancient ruins system
    /// </summary>
    private void InitializeRuinsSystem()
    {
        if (ruinsManager == null)
        {
            ruinsManager = AncientRuinsManager.Instance;
            if (ruinsManager == null)
            {
                GameObject ruinsGO = new GameObject("AncientRuinsManager");
                ruinsManager = ruinsGO.AddComponent<AncientRuinsManager>();
            }
        }

        // Configure ruins settings from ScriptableObject if available
        if (ruinsConfig != null)
        {
            ApplyRuinsConfiguration();
        }

        // Subscribe to ruins events
        ruinsManager.OnRuinDiscovered += OnRuinDiscovered;
        ruinsManager.OnRuinExplorationCompleted += OnRuinExplorationCompleted;

        Debug.Log("[SpaceIntegration] Ruins system initialized");
    }



    /// <summary>
    /// Apply ruins configuration from ScriptableObject
    /// </summary>
    private void ApplyRuinsConfiguration()
    {
        // This would set up the ruins manager with configuration data
        Debug.Log($"[SpaceIntegration] Applied ruins configuration: {ruinsConfig.ruinDescriptions.Length} ruin types available");
    }

    #region Event Handlers


    /// <summary>
    /// Handle ruin discovery
    /// </summary>
    private void OnRuinDiscovered(AncientRuinsManager.RuinSite ruin, Civilization civilization)
    {
        Debug.Log($"[SpaceIntegration] {civilization?.name} discovered {ruin.ruinName}!");

        // You can show discovery notifications, update exploration UI, etc.
        // For example:
        // UIManager.Instance?.ShowDiscoveryNotification(ruin.ruinName, ruin.description);
        // ExplorationManager.Instance?.AddDiscoveredSite(ruin);
    }

    /// <summary>
    /// Handle ruin exploration completion
    /// </summary>
    private void OnRuinExplorationCompleted(AncientRuinsManager.RuinSite ruin, Civilization civilization, System.Collections.Generic.List<string> rewards)
    {
        Debug.Log($"[SpaceIntegration] {civilization?.name} completed exploration of {ruin.ruinName}!");

        foreach (var reward in rewards)
        {
            Debug.Log($"[SpaceIntegration] Reward: {reward}");
        }

        // You can show reward notifications, unlock new technologies, etc.
        // For example:
        // UIManager.Instance?.ShowRewardsNotification(rewards);
        // TechnologyTree.Instance?.UpdateAvailableTechs(civilization);
    }

    #endregion



    public class QuickSpaceSystemsSetup : MonoBehaviour
    {
        [Header("Quick Setup")]
        public bool setupOnStart = true;
        public bool createUIButtons = true;

        void Start()
        {
            if (setupOnStart)
            {
                SetupSpaceSystems();
            }
        }

        [ContextMenu("Setup Space Systems")]
        public void SetupSpaceSystems()
        {
            // Create the integration manager
            GameObject integrationGO = new GameObject("SpaceSystemsIntegration");
            var integration = integrationGO.AddComponent<SpaceSystemsIntegration>();

            // Create UI buttons if requested
            if (createUIButtons)
            {
                CreateSpaceUI();
            }

            Debug.Log("[QuickSetup] Space systems setup complete!");
        }

        private void CreateSpaceUI()
        {
            // Find or create a Canvas for UI
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGO = new GameObject("Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            // Create space map button
            GameObject buttonGO = new GameObject("SpaceMapButton");
            buttonGO.transform.SetParent(canvas.transform, false);

            var spaceMapButton = buttonGO.AddComponent<SpaceMapButton>();

            Debug.Log("[QuickSetup] Space UI created!");
        }
    }
}
