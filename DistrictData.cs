using UnityEngine;

[CreateAssetMenu(menuName="Data/Districts")]
public class DistrictData : ScriptableObject
{
    [Header("Identity")]
    public string districtName;
    [TextArea] public string description;
    public Sprite icon;
    public GameObject prefab;
    
    [Header("Building Requirements")]
    public int productionCost;
    public int goldCost;
    public TechData requiredTech;
    public Biome[] allowedBiomes;
    public bool requiresRiver;
    public bool requiresCoastal;
    public bool requiresMountainAdjacent;
    
    [Header("District Specific")]
    public bool isHolySite;
    public float adjacencyBonusPerAdjacentTile = 0.5f;
    
    [Header("Yields")]
    public int baseFaith;
    public int baseFood;
    public int baseProduction;
    public int baseGold;
    public int baseScience;
    public int baseCulture;
    
    [Header("Buildings")]
    public BuildingData[] possibleBuildings;
} 