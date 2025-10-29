using UnityEngine;

namespace GameCombat
{
    public enum ProjectileArcType { Straight, Parabolic, Homing }
    
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
    public string projectileName;
    public Sprite icon;
    [Tooltip("Category of projectile - determines which weapons can use it")]
    public ProjectileCategory category = ProjectileCategory.Arrow;
    
    [Header("Production & Requirements")]
    [Tooltip("Production cost for cities to produce this projectile type")]
    public int productionCost = 10;
    [Tooltip("Gold cost to purchase this projectile type")]
    public int goldCost = 50;
    [Tooltip("Resources required to produce this projectile")]
    public ResourceData[] requiredResources;
    [Tooltip("Technologies required to unlock this projectile")]
    public TechData[] requiredTechs;
    [Tooltip("Cultures required to unlock this projectile")]
    public CultureData[] requiredCultures;
    
    [Header("Visuals")]
    public GameObject projectilePrefab;
    public float scale = 1f;
    public TrailRenderer trailEffect;
    public ParticleSystem impactEffect;

    [Header("Trajectory")]
    public ProjectileArcType arcType = ProjectileArcType.Parabolic;
    public float speed = 10f;
    public float gravity = 9.81f; // Used for parabolic
    public bool useGravity = true;
    public float homingStrength = 0f; // Used for homing

    [Header("Damage & Effects")]
    public float damage = 10f;
    public float areaOfEffectRadius = 0f; // 0 = single target
    public bool explodeOnImpact = false;
    public float explosionForce = 0f;
    public float statusEffectDuration = 0f;
    public string statusEffectName;

    [Header("Audio")]
    public AudioClip launchSound;
    public AudioClip impactSound;
    
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
        if (requiredResources != null)
        {
            foreach (var resource in requiredResources)
            {
                if (resource != null && civ.GetResourceCount(resource) <= 0)
                    return false;
            }
        }
        
        return true;
    }
    }
}
