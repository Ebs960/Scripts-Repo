// Assets/Units/CombatUnitData.cs
using UnityEngine;

public enum CombatCategory
{
    Spearman, Swordsman, Clubman, Artillery,
    Archer, Crossbowman, Spaceship, Aircraft,
    Submarine, Ship, Boat, SeaCrawler,
    Gunman, Robot, Mutant, Cyborg,
    Driller, LavaSwimmer, Tank,
    Cavalry, HeavyCavalry, RangedCavalry,
    Dragoon, Animal
}

public enum FormationShape { Square, Circle, Wedge }

[CreateAssetMenu(fileName = "NewCombatUnitData", menuName = "Data/Combat Unit Data")]
public class CombatUnitData : ScriptableObject
{
    [Header("Basic Info")]
    public string unitName;
    public CombatCategory unitType;
    public Sprite icon;
    public GameObject prefab;
    public GameObject[] modelVariants;

    [Header("Formation")]
    public GameObject formationMemberPrefab;
    [Range(1, 100)] public int formationSize = 9;
    public FormationShape formationShape = FormationShape.Square;
    [Range(0.5f, 5f)] public float formationSpacing = 1.5f;

    [Header("Category & Deployment")]
    public CombatCategory category;
    public bool requiresAirport;
    public bool requiresSpaceport;
    
    [Header("Transport Capabilities")]
    [Tooltip("Whether this unit can transport other units")]
    public bool isTransport = false;
    [Tooltip("Maximum number of units this transport can carry")]
    [Range(1, 10)]
    public int transportCapacity = 3;
    [Tooltip("Whether this transport can travel to the moon (only spaceships)")]
    public bool canTravelToMoon = false;
    
    [Header("Naval Requirements")]
    [Tooltip("Must control at least one coastal tile (coast, seas, ocean)")]
    public bool requiresCoastalCity = false;
    [Tooltip("Must have a Harbor building in the city")]
    public bool requiresHarbor = false;

    [Header("Combat System")]
    [Tooltip("Whether this unit can attack air units (Aircraft)")]
    public bool canAttackAir = false;
    [Tooltip("Whether this unit can attack space units (Spaceship)")]
    public bool canAttackSpace = false;
    [Tooltip("Whether this unit can attack underwater units (Submarine/SeaCrawler)")]
    public bool canAttackUnderwater = false;
    [Tooltip("Whether this unit can perform a counter-attack when attacked")]
    public bool canCounterAttack = false;
    [Tooltip("Base morale for the unit")]
    public int baseMorale = 100;
    [Tooltip("Morale lost per HP lost")]
    public int moraleLostPerHealth = 1;
    [Tooltip("Morale gained when killing an enemy unit")]
    public int moraleGainOnKill = 10;

    [Header("Production & Purchase")]
    public int productionCost;
    public int goldCost;
    public ResourceData[] requiredResources;
    public Biome[] requiredTerrains;

    [Header("Base Stats")]
    public int baseAttack;
    public int baseDefense;
    public int baseHealth;
    public int baseRange;
    public int baseMovePoints;
    public int baseAttackPoints;

    [Header("Progression")]
    public int[] xpToNextLevel;
    public AbilityData[] abilitiesByLevel;

    [Header("Requirements")]
    [Tooltip("All these techs must be researched to unlock this unit")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to unlock this unit")]
    public CultureData[] requiredCultures;

    [Header("Equipment")]
    public EquipmentData defaultEquipment;

    [Header("Yield")]
    public int foodOnKill;
    
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
        
        return true;
    }
}
