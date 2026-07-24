// Assets/Scripts/Data/NaturalDisasterData.cs
using UnityEngine;

public enum NaturalDisasterType
{
    Earthquake,
    Flood,
    Storm
}

[CreateAssetMenu(fileName = "NewNaturalDisasterData", menuName = "Data/Natural Disaster Data")]
public class NaturalDisasterData : ScriptableObject
{
    [Header("Identity")]
    public string disasterName;
    public NaturalDisasterType disasterType;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Trigger")]
    [Tooltip("Base chance (0-1) this disaster attempts to strike a given planet each round.")]
    [Range(0f, 1f)] public float baseChancePerTurn = 0.02f;
    [Tooltip("Number of hex rings around the primary struck tile that are also affected. 0 = single tile only.")]
    [Range(0, 5)] public int areaRadius = 0;
    [Tooltip("If enabled, this disaster can only trigger during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Unit Damage")]
    [Tooltip("Flat HP damage applied to units on affected tiles, before resistance.")]
    public int unitDamage = 10;

    [Header("Improvement Damage")]
    [Tooltip("Chance (0-1) a non-fort improvement on an affected tile becomes damaged, before resistance. Damaged improvements produce no yield until repaired by a worker (free, takes 1 turn).")]
    [Range(0f, 1f)] public float improvementDamageChance = 0.5f;
    [Tooltip("Flat HP damage applied to fort improvements on affected tiles, before resistance/defense.")]
    public int fortDamageAmount = 20;

    [Header("Building Damage")]
    [Tooltip("Chance (0-1) an operational building in a struck city becomes damaged, before resistance. Damaged buildings stop functioning until repaired by a worker (free, takes 1 turn).")]
    [Range(0f, 1f)] public float buildingDamageChance = 0.25f;

    [Header("City-Wide Effects")]
    [Tooltip("Percent penalty applied to all yields of a struck city while this effect is active.")]
    [Range(0f, 1f)] public float cityYieldPenaltyPct = 0.15f;
    [Tooltip("How many turns the city-wide yield penalty lasts.")]
    public int cityEffectDuration = 3;
    [Tooltip("Population (city level) lost per turn while this effect is active, accumulated fractionally. 0 = no population loss.")]
    public float cityPopulationLossPerTurn = 0f;
}
