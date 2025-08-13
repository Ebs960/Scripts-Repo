using UnityEngine;

public enum RouteType { Road, Railroad }

[CreateAssetMenu(fileName = "NewWorkerUnitData", menuName = "Data/Worker Unit Data")]
public class WorkerUnitData : ScriptableObject
{
    public string unitName;
    public Sprite icon;
    public GameObject prefab;

    [Header("Stats")] public int baseWorkPoints;
    public int baseMovePoints;
    public int baseHealth;
    public int baseAttack = 0;
    public int baseDefense = 0;
    public bool canFoundCity;
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

    [Header("Build Options")] public ImprovementData[] buildableImprovements;
    public RouteType[] buildableRoutes;

    [Header("Requirements")]
    [Tooltip("All these techs must be researched to unlock this unit")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to unlock this unit")]
    public CultureData[] requiredCultures;

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