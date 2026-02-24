// Assets/Scripts/Data/ResourceData.cs
using UnityEngine;

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
    [Header("Per-Turn Yields (per owned node)")]
    public int foodPerTurn;
    public int productionPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;

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