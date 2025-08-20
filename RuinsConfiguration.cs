using UnityEngine;

/// <summary>
/// Scriptable object to configure ancient ruins for the ruins exploration system
/// Create instances of this to define ruins across your solar system
/// </summary>
[CreateAssetMenu(fileName = "RuinsConfiguration", menuName = "Data/Ruins Configuration")]
public class RuinsConfiguration : ScriptableObject
{
    [Header("Ruins Settings")]
    [Tooltip("Maximum ruins per planet")]
    public int maxRuinsPerPlanet = 3;
    
    [Tooltip("Range within which units can discover ruins")]
    public float discoveryRange = 2f;

    [Header("Ruin Descriptions")]
    [Tooltip("Description templates for different ruin types")]
    public RuinDescriptionData[] ruinDescriptions;

    [Header("Ancient Technologies")]
    [Tooltip("Unique technologies only available from ruins")]
    public TechData[] ancientTechnologies;

    [Header("Pre-configured Ruins")]
    [Tooltip("Specific ruins placed on known planets")]
    public PredefinedRuin[] predefinedRuins;

    [System.Serializable]
    public class RuinDescriptionData
    {
        public AncientRuinsManager.RuinType ruinType;
        public string[] possibleNames;
        [TextArea(2, 4)]
        public string[] possibleDescriptions;
    }

    [System.Serializable]
    public class PredefinedRuin
    {
        [Header("Location")]
        public int planetIndex;
        public Vector3 position;
        
        [Header("Ruin Details")]
        public AncientRuinsManager.RuinType ruinType;
        public string ruinName;
        [TextArea(2, 3)]
        public string description;
        
        [Header("Exploration")]
        [Range(1, 5)]
        public int explorationDifficulty = 2;
        
        [Header("Rewards")]
        public TechData[] guaranteedTechs;
        public ResourceRewardData[] guaranteedResources;
    }

    [System.Serializable]
    public class ResourceRewardData
    {
        public ResourceData resource;
        public int amount;
    }

    /// <summary>
    /// Create a default ruins configuration with sample data
    /// </summary>
    public static RuinsConfiguration CreateDefault()
    {
        var config = CreateInstance<RuinsConfiguration>();
        config.name = "Default Ruins Configuration";
        
        // Create default ruin descriptions
        config.ruinDescriptions = new RuinDescriptionData[]
        {
            new RuinDescriptionData
            {
                ruinType = AncientRuinsManager.RuinType.Technology,
                possibleNames = new string[] 
                { 
                    "Temple of the Star Watchers", "Sacred Pyramid of Zephyria", "Shrine of the Ancient Ones",
                    "Cathedral of Cosmic Harmony", "Temple of Eternal Wisdom"
                },
                possibleDescriptions = new string[]
                {
                    "A magnificent temple with intricate carvings depicting unknown constellations and cosmic events.",
                    "Ancient religious structure where an advanced civilization once worshipped celestial bodies.",
                    "Sacred halls filled with mysterious symbols that seem to shift when viewed directly."
                }
            },
            new RuinDescriptionData
            {
                ruinType = AncientRuinsManager.RuinType.Gold,
                possibleNames = new string[] 
                { 
                    "Quantum Research Facility", "Xenobiology Lab Complex", "Advanced Physics Institute",
                    "Molecular Engineering Center", "Astroscience Laboratory"
                },
                possibleDescriptions = new string[]
                {
                    "A sophisticated research facility with equipment far beyond current understanding.",
                    "Scientific laboratory containing experimental apparatus of unknown purpose.",
                    "Research complex where breakthrough discoveries in physics and chemistry were made."
                }
            },
            new RuinDescriptionData
            {
                ruinType = AncientRuinsManager.RuinType.Unit,
                possibleNames = new string[] 
                { 
                    "Automated Production Facility", "Nanotech Manufacturing Plant", "Industrial Megacomplex",
                    "Resource Processing Center", "Advanced Assembly Matrix"
                },
                possibleDescriptions = new string[]
                {
                    "Massive automated factory that once produced technologies beyond imagination.",
                    "Industrial complex with self-repairing machinery that still hums with activity.",
                    "Manufacturing facility capable of precise molecular assembly and construction."
                }
            },
            new RuinDescriptionData
            {
                ruinType = AncientRuinsManager.RuinType.Gold,
                possibleNames = new string[] 
                { 
                    "Secure Resource Vault", "Treasury of the Ancients", "Protected Storage Complex",
                    "Quantum Secured Depot", "Emergency Resource Cache"
                },
                possibleDescriptions = new string[]
                {
                    "A heavily fortified vault containing precious materials and rare elements.",
                    "Secure storage facility designed to preserve valuable resources for millennia.",
                    "Emergency cache filled with rare materials needed for advanced technologies."
                }
            },
            new RuinDescriptionData
            {
                ruinType = AncientRuinsManager.RuinType.Map,
                possibleNames = new string[] 
                { 
                    "Galactic Observatory", "Deep Space Monitoring Station", "Cosmic Phenomena Tracker",
                    "Stellar Cartography Center", "Astronomical Research Outpost"
                },
                possibleDescriptions = new string[]
                {
                    "Advanced observatory capable of monitoring distant galaxies and cosmic phenomena.",
                    "Astronomical facility with instruments that map stellar formations with incredible precision.",
                    "Space monitoring station that tracked civilizations across the galaxy."
                }
            },
            new RuinDescriptionData
            {
                ruinType = AncientRuinsManager.RuinType.Culture,
                possibleNames = new string[] 
                { 
                    "Archive of Universal Knowledge", "Great Library of Cosmos", "Digital Consciousness Vault",
                    "Information Nexus Complex", "Repository of Ancient Wisdom"
                },
                possibleDescriptions = new string[]
                {
                    "Vast archive containing the accumulated knowledge of an entire civilization.",
                    "Digital library storing information in crystalline matrices that defy understanding.",
                    "Repository where the thoughts and discoveries of ancient minds are preserved."
                }
            },
            new RuinDescriptionData
            {
                ruinType = AncientRuinsManager.RuinType.Faith,
                possibleNames = new string[] 
                { 
                    "Quantum Defense Grid", "Military Command Bunker", "Strategic Defense Complex",
                    "Automated Defense Station", "Fortress of the Last Stand"
                },
                possibleDescriptions = new string[]
                {
                    "Formidable military installation with defensive systems still partially operational.",
                    "Strategic command center designed to coordinate planetary defense networks.",
                    "Fortress complex where an ancient civilization made their final stand."
                }
            },
            new RuinDescriptionData
            {
                ruinType = AncientRuinsManager.RuinType.Population,
                possibleNames = new string[] 
                { 
                    "Interdimensional Gateway", "Quantum Tunnel Hub", "Galactic Transit Node",
                    "Wormhole Generation Station", "Instant Travel Portal"
                },
                possibleDescriptions = new string[]
                {
                    "Mysterious portal structure that once enabled instant travel across vast distances.",
                    "Transportation hub connecting this world to distant parts of the galaxy.",
                    "Gateway device that bends space-time to create shortcuts through the cosmos."
                }
            }
        };

        return config;
    }
}

/// <summary>
/// Setup script to create default ruins configuration
/// </summary>
public class RuinsConfigurationSetup : MonoBehaviour
{
    [Header("Setup Options")]
    public bool createDefaultConfig = true;
    public string configFileName = "DefaultRuinsConfig";

    void Start()
    {
        if (createDefaultConfig)
        {
            CreateDefaultConfiguration();
        }
    }

    [ContextMenu("Create Default Ruins Configuration")]
    public void CreateDefaultConfiguration()
    {
        #if UNITY_EDITOR
        var config = RuinsConfiguration.CreateDefault();
        
        string path = $"Assets/{configFileName}.asset";
        UnityEditor.AssetDatabase.CreateAsset(config, path);
        UnityEditor.AssetDatabase.SaveAssets();
        
        Debug.Log($"[RuinsSetup] Created default ruins configuration at {path}");
        #else
        Debug.Log("[RuinsSetup] Ruins configuration creation is only available in the Unity Editor");
        #endif
    }
}
