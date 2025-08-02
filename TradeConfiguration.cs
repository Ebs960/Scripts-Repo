using UnityEngine;

/// <summary>
/// Scriptable object to configure tradable resources for the interplanetary trade system
/// Create instances of this in your project to define what resources can be traded between planets
/// </summary>
[CreateAssetMenu(fileName = "TradeConfiguration", menuName = "Space Game/Trade Configuration")]
public class TradeConfiguration : ScriptableObject
{
    [Header("Trade Routes Settings")]
    [Tooltip("Maximum trade routes each civilization can maintain")]
    public int maxTradeRoutesPerCivilization = 5;
    
    [Tooltip("Base travel time for adjacent planets")]
    public float baseTradeDistance = 2f;
    
    [Tooltip("Additional time per planet distance")]
    public float distanceMultiplier = 1.5f;

    [Header("Tradable Resources")]
    [Tooltip("Resources that can be traded between planets")]
    public TradableResourceConfig[] tradableResources;

    [System.Serializable]
    public class TradableResourceConfig
    {
        [Header("Resource Info")]
        public ResourceData resource;
        public int baseValue = 10; // Base profit per turn
        
        [Header("Planet Production")]
        [Tooltip("Planets that naturally produce this resource")]
        public PlanetType[] producerPlanets;
        
        [Header("Planet Consumption")]
        [Tooltip("Planets that value/consume this resource")]
        public PlanetType[] consumerPlanets;
        
        [Header("Market Settings")]
        [Range(0.5f, 2f)]
        [Tooltip("Market demand multiplier")]
        public float demandMultiplier = 1f;
    }

    /// <summary>
    /// Get default trade configuration for common space resources
    /// </summary>
    public static TradeConfiguration CreateDefault()
    {
        var config = CreateInstance<TradeConfiguration>();
        config.name = "Default Trade Configuration";
        
        // You can expand this with more resources as needed
        config.tradableResources = new TradableResourceConfig[]
        {
            // Example configurations - you'll need to create actual ResourceData assets
            new TradableResourceConfig
            {
                // Luxury Goods: Earth exports to other colonies
                baseValue = 15,
                producerPlanets = new PlanetType[] { PlanetType.Terrestrial },
                consumerPlanets = new PlanetType[] { PlanetType.Desert, PlanetType.Ice, PlanetType.Volcanic },
                demandMultiplier = 1.2f
            },
            new TradableResourceConfig
            {
                // Raw Materials: Rocky/Desert planets export to developed worlds
                baseValue = 12,
                producerPlanets = new PlanetType[] { PlanetType.Desert, PlanetType.Volcanic },
                consumerPlanets = new PlanetType[] { PlanetType.Terrestrial, PlanetType.Ocean },
                demandMultiplier = 1.1f
            },
            new TradableResourceConfig
            {
                // Water/Ice: Ice planets export to dry worlds
                baseValue = 20,
                producerPlanets = new PlanetType[] { PlanetType.Ice },
                consumerPlanets = new PlanetType[] { PlanetType.Desert, PlanetType.Volcanic },
                demandMultiplier = 1.5f
            },
            new TradableResourceConfig
            {
                // Energy: Gas giants/volcanic worlds export energy
                baseValue = 18,
                producerPlanets = new PlanetType[] { PlanetType.GasGiant, PlanetType.Volcanic },
                consumerPlanets = new PlanetType[] { PlanetType.Terrestrial, PlanetType.Ice, PlanetType.Desert },
                demandMultiplier = 1.3f
            }
        };
        
        return config;
    }
}

/// <summary>
/// Simple script to create default trade configuration asset
/// Attach this to any GameObject and it will create the asset when the game starts
/// </summary>
public class TradeConfigurationSetup : MonoBehaviour
{
    [Header("Setup Options")]
    public bool createDefaultConfig = true;
    public string configFileName = "DefaultTradeConfig";

    void Start()
    {
        if (createDefaultConfig)
        {
            CreateDefaultConfiguration();
        }
    }

    [ContextMenu("Create Default Trade Configuration")]
    public void CreateDefaultConfiguration()
    {
        #if UNITY_EDITOR
        var config = TradeConfiguration.CreateDefault();
        
        string path = $"Assets/{configFileName}.asset";
        UnityEditor.AssetDatabase.CreateAsset(config, path);
        UnityEditor.AssetDatabase.SaveAssets();
        
        Debug.Log($"[TradeSetup] Created default trade configuration at {path}");
        #else
        Debug.Log("[TradeSetup] Trade configuration creation is only available in the Unity Editor");
        #endif
    }
}
