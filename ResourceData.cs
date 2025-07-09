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
    [Tooltip("Which biomes this resource can appear on")]
    public Biome[] allowedBiomes;
    [Tooltip("Chance (0–1) that this resource spawns on a valid tile")]
    public float spawnChance;

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