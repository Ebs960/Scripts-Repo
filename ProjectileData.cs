using UnityEngine;

namespace GameCombat
{
    public enum ProjectileCategory
    {
        Arrow,      // Used by bows
        Bolt,       // Used by crossbows
        Bullet,     // Used by guns
        Shell,      // Used by artillery
        Rocket,     // Used by launchers
        Javelin,    // Used by spear throwers
        Stone,      // Used by slings
        Laser,      // Used by energy weapons
        Plasma,     // Used by plasma weapons
        Magic       // Used by magical weapons
    }

    [CreateAssetMenu(fileName = "NewProjectileData", menuName = "Data/Projectile Data")]
    public class ProjectileData : ScriptableObject
    {
    [Header("Identity")]
    public string stableId;
    public string projectileName;
    [TextArea(2, 5)] public string description;
    public TechAge projectileAge;
    public Sprite icon;
    [Tooltip("Category of projectile - determines which weapons can use it")]
    public ProjectileCategory category = ProjectileCategory.Arrow;

    public ProjectileCategory projectileCategory => category;
    
    [Header("Production & Requirements")]
    [Tooltip("Production cost for cities to produce this projectile type")]
    public int productionCost = 10;
    [Tooltip("Gold cost to purchase this projectile type")]
    public int goldCost = 50;
    [Tooltip("Material quantities consumed when this ammunition is produced.")]
    public ResourceCost[] resourceCosts;
    [Tooltip("Technologies required to unlock this projectile")]
    public TechData[] requiredTechs;
    [Tooltip("Cultures required to unlock this projectile")]
    public CultureData[] requiredCultures;
    
    [Header("Damage & Effects")]
    [Tooltip("Flat ammunition damage added after the unit and weapon ranged-damage calculation.")]
    public float damage = 10f;

    [Tooltip("Status effect applied on hit (replaces legacy string-based statusEffectName)")]
    public StatusEffectData statusEffect;
    [Tooltip("Chance-based and filtered hit effects. The legacy statusEffect remains a guaranteed application.")]
    public StatusEffectApplication[] onHitEffects;

    [System.Obsolete("Use statusEffect (StatusEffectData) instead")]
    [HideInInspector]
    public float statusEffectDuration = 0f;
    [System.Obsolete("Use statusEffect (StatusEffectData) instead")]
    [HideInInspector]
    public string statusEffectName;

    [Header("Runtime Visuals")]
    public GameObject projectilePrefab;
    public GameObject heldProjectilePrefab;
    public GameObject impactVfxPrefab;

    [Header("Audio")]
    public AudioClip launchSound;
    public AudioClip impactSound;

    [Header("Flight")]
    public float launchSpeed = 18f;
    public float flightArcHeight = 0.75f;
    public float maxFlightDuration = 1.25f;
    public bool rotateAlongVelocity = true;

    [Header("Held/Nocked Offsets")]
    public Vector3 heldLocalPosition;
    public Vector3 heldLocalEulerAngles;
    public Vector3 heldLocalScale = Vector3.one;

    public Vector3 nockedLocalPosition;
    public Vector3 nockedLocalEulerAngles;
    public Vector3 nockedLocalScale = Vector3.one;
    
    /// <summary>
    /// Checks if this projectile's requirements are met by the civilization
    /// </summary>
    public bool CanBeProducedBy(Civilization civ)
    {
        if (civ == null) return false;
        
        // Check tech requirements
        if (requiredTechs != null)
        {
            foreach (var tech in requiredTechs)
            {
                if (tech != null && !civ.researchedTechs.Contains(tech))
                    return false;
            }
        }
        
        // Check culture requirements
        if (requiredCultures != null)
        {
            foreach (var culture in requiredCultures)
            {
                if (culture != null && !civ.researchedCultures.Contains(culture))
                    return false;
            }
        }
        
        // Check resource requirements
        if (!ResourceCost.CanAfford(civ, resourceCosts, false))
            return false;
        return true;
    }
    }
}
