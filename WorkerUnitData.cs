using UnityEngine;

public enum RouteType { Road, Railroad }

 [CreateAssetMenu(fileName = "NewWorkerUnitData", menuName = "Data/Worker Unit Data")]
public class WorkerUnitData : ScriptableObject
{
    [Header("Default Equipment")]
    [Tooltip("Default weapon equipped by this worker (optional)")]
    public EquipmentData defaultWeapon;
    [Tooltip("Default projectile/ranged weapon equipped by this worker (used when firing)")]
    public EquipmentData defaultProjectileWeapon;
    // meleeEngageDuration removed (deprecated)
    [Tooltip("Default shield equipped by this worker (optional)")]
    public EquipmentData defaultShield;
    [Tooltip("Default armor equipped by this worker (optional)")]
    public EquipmentData defaultArmor;
    [Tooltip("Default miscellaneous equipment equipped by this worker (optional)")]
    public EquipmentData defaultMiscellaneous;
    public string unitName;
    public Sprite icon;
    public GameObject prefab;

    [Header("Audio")]
    [Tooltip("Sound played when this worker is selected/clicked on the map. Leave empty for no sound.")]
    public AudioClip selectSound;
    [Tooltip("Random pitch variation range (±) applied to select sound for variety.")]
    [Range(0f, 0.3f)]
    public float selectPitchVariation = 0.08f;

    [Header("Stats")] public int baseWorkPoints;
    [Tooltip("Whether this worker unit can enter orbit (landing/launch).")]
    public bool canEnterOrbit = false;
    public int baseMovePoints;
    public int baseHealth;
    public int baseAttack = 0;
    public int baseDefense = 0;
    public bool canFoundCity;

    [Header("Action Points")]
    [Tooltip("How many attacks/actions this unit can perform per turn.")]
    [Range(0, 10)]
    public int attackPointsPerTurn = 1;
    
    [Header("Vision")]
    [Tooltip("How many tiles this unit can see (reveals fog of war). Default is 2 tiles.")]
    [Range(1, 10)]
    public int sightRange = 2;
    [Tooltip("Can harvest resources that require special skills/equipment")]
    public bool canHarvestSpecialResources = false;
    [Tooltip("Can forage resources from unimproved tiles")]
    public bool canForage = false;

    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons (e.g., winter)")]
    public bool takesWeatherDamage = true;

    [Header("Production & Purchase")] public int productionCost;
    public int goldCost;
    public ResourceData[] requiredResources;
    public Biome[] requiredTerrains;
    [Tooltip("Coastal city required for production")]
    public bool requiresCoastalCity = false;
    [Tooltip("Harbor building required for production")]
    public bool requiresHarbor = false;

    [Header("Worker Construction")]
    [Tooltip("If true, workers can construct this worker type on the map using work points.")]
    public bool buildableByWorker = false;
    [Tooltip("Total work points required by workers to construct this worker unit on a tile.")]
    public int workerWorkCost = 30;

    [Header("Per-Turn Yields")]
    [Tooltip("Flat yields this worker provides each turn while alive (added to owning civilization)")]
    public int foodPerTurn;
    [Header("Yield")]
    [Tooltip("Food awarded to attacker when this worker is killed (0 = none)")]
    public int foodOnKill;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;
    
    [Header("Per-Turn Consumption")]
    [Tooltip("Food this worker consumes each turn (subtracted from civilization stockpile)")]
    public int foodConsumptionPerTurn = 1;

    [Header("Capture")]
    [Tooltip("If true this worker/animal can be captured into a herd")]
    public bool captureable = false;
    [Tooltip("If >0, number of herd 'animals' added to a herd when this worker is captured/killed and converted")]
    public int captureHerdCount = 0;
    [Tooltip("If set, explicit species this capture converts to (overrides name-matching).")]
    public Herd.HerdSpecies captureSpecies = Herd.HerdSpecies.Other;

    [Header("Build Options")]
    public RouteType[] buildableRoutes;

    [Header("Requirements")]
    [Tooltip("All these techs must be researched to unlock this unit")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to unlock this unit")]
    public CultureData[] requiredCultures;
    [Tooltip("At least one of these governments must be active to allow this worker (optional)")]
    public GovernmentData[] requiredGovernments;
    [Tooltip("All of these policies must be active to allow this worker (optional)")]
    public PolicyData[] requiredPolicies;

    [Header("Unit Limits")]
    [Tooltip("Maximum number of this unit type a civilization can have (-1 = unlimited)")]
    public int unitLimit = -1;
    [Tooltip("Unique identifier for units that share the same limit (leave empty for individual limits)")]
    public string limitCategory = "";

    /// <summary>
    /// Checks if all requirements (techs, cultures) are met for this unit
    /// </summary>
    public bool AreRequirementsMet(Civilization civ)
    {
        if (civ == null) return false;
        
        // Check tech requirements
        if (requiredTechs != null && requiredTechs.Length > 0)
        {
            foreach (var tech in requiredTechs)
            {
                if (tech == null) continue;
                
                // Check if this tech has been researched
                if (!civ.researchedTechs.Contains(tech))
                    return false;
            }
        }
        
        // Check culture requirements
        if (requiredCultures != null && requiredCultures.Length > 0)
        {
            foreach (var culture in requiredCultures)
            {
                if (culture == null) continue;
                
                // Check if this culture has been adopted
                if (!civ.researchedCultures.Contains(culture))
                    return false;
            }
        }
        // Government requirement (any-of)
        if (requiredGovernments != null && requiredGovernments.Length > 0)
        {
            bool govOk = false;
            foreach (var gov in requiredGovernments)
            {
                if (gov == null) continue;
                if (civ.currentGovernment == gov) { govOk = true; break; }
            }
            if (!govOk) return false;
        }

        // Policy requirements (all-of)
        if (requiredPolicies != null && requiredPolicies.Length > 0)
        {
            foreach (var pol in requiredPolicies)
            {
                if (pol == null) continue;
                if (!civ.activePolicies.Contains(pol)) return false;
            }
        }
        
        return true;
    }
}