// Assets/Scripts/Data/ResourceData.cs
using UnityEngine;

public enum ResourceCategory
{
    Metals,
    Livestock,
    Fuel,
    Materials,
    Luxuries,
    Equipment
}

[System.Serializable]
public class ResourceSurplusYield
{
    [Tooltip("Yield granted per surplus unit of this resource each turn.")]
    public int food;
    public int production;
    public int gold;
    public int science;
    public int culture;
    public int policyPoints;
    public int faith;

    [Tooltip("If true, production is added to every city production queue. If false, the production value is ignored until another production target is implemented.")]
    public bool applyProductionToAllCities;
}

/// <summary>
/// Defines a resource type: where it can spawn, what it looks like, and what yields it provides.
/// </summary>
[CreateAssetMenu(fileName = "NewResourceData", menuName = "Data/Resource Data")]
public class ResourceData : ScriptableObject
{
    [Header("Identity")]
    public string resourceName;
    public Sprite icon;
    public GameObject prefab;
    [Tooltip("Category for inventory/breakdown display (Metals, Livestock, Fuel, etc.)")]
    public ResourceCategory category = ResourceCategory.Materials;

    [Header("Audio")]
    [Tooltip("Sound played when this resource is clicked on the map. Leave empty for no sound.")]
    public AudioClip selectSound;
    [Tooltip("Random pitch variation range (±) applied to select sound for variety.")]
    [Range(0f, 0.3f)]
    public float selectPitchVariation = 0.1f;

    [Header("Spawn Rules")]
    [Tooltip("Which biomes this resource can appear on (surface biome check)")]
    public Biome[] allowedBiomes;
    [Tooltip("Which underwater floor biomes this resource can appear on (checked against HexTileData.underwaterBiome). Leave empty to skip underwater spawning.")]
    public Biome[] allowedUnderwaterBiomes;
    [Tooltip("Chance (0–1) that this resource spawns on a valid tile")]
    public float spawnChance;
    [Header("Orbital")]
    [Tooltip("If true, this resource spawns in the orbit layer above a tile instead of on the surface. Uses allowedBiomes to check the surface biome below.")]
    public bool isOrbitalResource = false;
    [Header("Map Visibility Requirements")]
    [Tooltip("If set, this resource is hidden on the map until the viewing civilization has at least one of these technologies.")]
    public TechData[] requiredTechsToReveal;
    [Tooltip("If set, this resource is hidden on the map until the viewing civilization has at least one of these cultures.")]
    public CultureData[] requiredCulturesToReveal;

    [Header("Per-Turn Yields (per owned node)")]
    public int foodPerTurn;
    public int productionPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;

    [Header("Surplus Bonuses (per unused produced resource)")]
    public ResourceSurplusYield surplusYieldPerResource;
    [Tooltip("Maximum happiness/morale added to each city per surplus unit of this resource.")]
    public int happinessPerSurplusResource;

    [Header("Forage (one-off) Yields")]
    public int forageFood;
    public int forageGold;
    public int forageScience;
    public int forageCulture;
    public int foragePolicyPoints;
    public int forageFaith;
    
    [Header("Forage Requirements")]
    [Tooltip("Can this resource be foraged by workers?")]
    public bool canBeForaged = true;
    [Tooltip("Research needed to harvest this resource")]
    public TechData requiredTech;
    [Tooltip("Requires special harvesting equipment or skills")]
    public bool requiresSpecialHarvester = false;
    [Tooltip("How many turns until this resource respawns after being foraged")]
    public int regrowthTime = 5;
}
