using UnityEngine;

/// <summary>
/// Example integration of Trade and Ruins systems with your civilization game
/// This shows how to connect the space features with your existing game mechanics
/// </summary>
public class SpaceSystemsIntegration : MonoBehaviour
{
    [Header("System References")]
    public InterplanetaryTradeManager tradeManager;
    public AncientRuinsManager ruinsManager;
    public SolarSystemManager solarSystemManager;
    
    [Header("Integration Settings")]
    public bool enableTradeSystem = true;
    public bool enableRuinsSystem = true;
    public bool autoInitialize = true;

    [Header("Example Data")]
    public TradeConfiguration tradeConfig;
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

        // Initialize Trade System
        if (enableTradeSystem)
        {
            InitializeTradeSystem();
        }

        // Initialize Ruins System  
        if (enableRuinsSystem)
        {
            InitializeRuinsSystem();
        }

        Debug.Log("[SpaceIntegration] Space systems initialized successfully!");
    }

    /// <summary>
    /// Initialize the interplanetary trade system
    /// </summary>
    private void InitializeTradeSystem()
    {
        if (tradeManager == null)
        {
            tradeManager = InterplanetaryTradeManager.Instance;
            if (tradeManager == null)
            {
                GameObject tradeGO = new GameObject("InterplanetaryTradeManager");
                tradeManager = tradeGO.AddComponent<InterplanetaryTradeManager>();
            }
        }

        // Configure trade settings from ScriptableObject if available
        if (tradeConfig != null)
        {
            ApplyTradeConfiguration();
        }

        // Subscribe to trade events
        tradeManager.OnTradeRouteEstablished += OnTradeRouteEstablished;
        tradeManager.OnTradeDelivery += OnTradeDelivery;

        Debug.Log("[SpaceIntegration] Trade system initialized");
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
    /// Apply trade configuration from ScriptableObject
    /// </summary>
    private void ApplyTradeConfiguration()
    {
        // This would set up the trade manager with configuration data
        // Implementation depends on how you structure your trade system
        Debug.Log($"[SpaceIntegration] Applied trade configuration: {tradeConfig.tradableResources.Length} resources available");
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
    /// Handle trade route establishment
    /// </summary>
    private void OnTradeRouteEstablished(InterplanetaryTradeManager.TradeRoute route)
    {
        Debug.Log($"[SpaceIntegration] Trade route established by {route.ownerCivilization?.name}: Profit +{route.profitPerTurn}/turn");
        
        // You can trigger UI updates, notifications, or other game effects here
        // For example:
        // UIManager.Instance?.ShowNotification($"New trade route established! +{route.profitPerTurn} gold per turn");
        // SoundManager.Instance?.PlaySound("TradeRouteEstablished");
    }

    /// <summary>
    /// Handle trade delivery completion
    /// </summary>
    private void OnTradeDelivery(InterplanetaryTradeManager.TradeRoute route)
    {
        Debug.Log($"[SpaceIntegration] Trade delivery completed: {route.ownerCivilization?.name} earned {route.profitPerTurn} gold");
        
        // You can add visual effects, update economy displays, etc.
        // For example:
        // EffectsManager.Instance?.ShowGoldGain(route.profitPerTurn);
    }

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

    #region Example Usage Methods

    /// <summary>
    /// Example: Establish a trade route for the current player
    /// Call this from your UI or game logic
    /// </summary>
    public void ExampleEstablishTradeRoute(Civilization playerCiv, int originPlanet, int destPlanet, ResourceData resource)
    {
        if (tradeManager != null && enableTradeSystem)
        {
            bool success = tradeManager.EstablishTradeRoute(playerCiv, originPlanet, destPlanet, resource);
            
            if (success)
            {
                Debug.Log("[SpaceIntegration] Trade route established successfully!");
            }
            else
            {
                Debug.LogWarning("[SpaceIntegration] Failed to establish trade route");
            }
        }
    }

    /// <summary>
    /// Example: Check for ruin discovery when a unit moves
    /// Call this from your unit movement system
    /// </summary>
    public void ExampleCheckRuinDiscovery(int planetIndex, Vector3 unitPosition, Civilization civilization)
    {
        if (ruinsManager != null && enableRuinsSystem)
        {
            ruinsManager.CheckForRuinDiscovery(planetIndex, unitPosition, civilization);
        }
    }

    /// <summary>
    /// Example: Start exploring a discovered ruin
    /// Call this from your exploration UI
    /// </summary>
    public void ExampleStartRuinExploration(AncientRuinsManager.RuinSite ruin, Civilization civilization)
    {
        if (ruinsManager != null && enableRuinsSystem)
        {
            bool success = ruinsManager.StartRuinExploration(ruin, civilization);
            
            if (success)
            {
                Debug.Log("[SpaceIntegration] Ruin exploration started!");
            }
            else
            {
                Debug.LogWarning("[SpaceIntegration] Cannot start ruin exploration");
            }
        }
    }

    #endregion

    void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (tradeManager != null)
        {
            tradeManager.OnTradeRouteEstablished -= OnTradeRouteEstablished;
            tradeManager.OnTradeDelivery -= OnTradeDelivery;
        }

        if (ruinsManager != null)
        {
            ruinsManager.OnRuinDiscovered -= OnRuinDiscovered;
            ruinsManager.OnRuinExplorationCompleted -= OnRuinExplorationCompleted;
        }
    }
}

/// <summary>
/// Quick setup component to automatically add space systems to your game
/// Just attach this to any GameObject in your scene
/// </summary>
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
