using UnityEngine;

[CreateAssetMenu(menuName="CivGame/Religion/Belief")]
public class BeliefData : ScriptableObject
{
    [Header("Identity")]
    public string beliefName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Effects")]
    [Tooltip("Flat bonus faith in Holy Site per turn")]
    public int extraFaithInHolySite;
    [Tooltip("Flat bonus food in Holy Site per turn")]
    public int extraFoodInHolySite;
    [Tooltip("Flat bonus production in Holy Site per turn")]
    public int extraProductionInHolySite;
    [Tooltip("Bonus gold in cities with this religion")]
    public int goldPerCity;
    [Tooltip("Bonus culture in cities with this religion")]
    public int culturePerCity;
    [Tooltip("Happiness bonus in cities with this religion")]
    public int happinessBonus;
    [Tooltip("Combat strength bonus for units near Holy Site")]
    public int combatStrengthNearHolySite;
    [Tooltip("Growth rate bonus in cities with this religion")]
    public float growthRateModifier;
    [Tooltip("Production rate bonus in cities with this religion")]
    public float productionRateModifier;

    // Adding percentage-based yield modifiers for consistency
    [Header("Percentage Yield Modifiers")]
    public float foodModifier;          // New
    public float productionModifier;    // New
    public float goldModifier;          // New
    public float scienceModifier;       // New
    public float cultureModifier;       // New
    public float faithModifier;         // New

    [Header("Category")]
    [Tooltip("Category of this belief. Civilization may only hold one belief per category at a time.")]
    public BeliefCategory category = BeliefCategory.Survival;

    [Header("Pantheon Availability")]
    [Tooltip("Optional pantheon restriction. Leave empty to make this belief available to all civilizations and all pantheons by default.")]
    public PantheonData[] exclusiveToPantheons;

    [Header("Season Availability")]
    [Tooltip("Optional global season restriction for this belief. Leave disabled to make it active in all seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Targeted Yield Bonuses")]
    [Tooltip("Per-turn yield modifiers for specific combat units owned by the civilization.")]
    public UnitYieldBonus[] unitYieldBonuses;
    [Tooltip("Per-turn yield modifiers for specific worker units owned by the civilization.")]
    public WorkerUnitYieldBonus[] workerYieldBonuses;
    [Tooltip("Per-turn yield modifiers for specific buildings in owned cities.")]
    public BuildingYieldBonus[] buildingYieldBonuses;
    [Tooltip("Per-turn yield modifiers for tiles matching terrain filters in owned territory.")]
    public TileYieldBonus[] tileYieldBonuses;
    [Tooltip("Per-unit stat bonuses granted by this belief (e.g., healing speed).")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-worker stat bonuses granted by this belief (e.g., healing speed).")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Disease modifiers granted by this belief.")]
    public DiseaseModifierBonus[] diseaseBonuses;
    public AttritionModifierBonus[] attritionBonuses;
    [Tooltip("Reduces the percent of herd animals lost to starvation (e.g. 0.05 = -5 percentage points)")]
    public float herdStarvationPercentReduction = 0f;
    [Tooltip("Per-turn yield modifiers applied to all cities or just the capital.")]
    public CityYieldBonus[] cityYieldBonuses;
} 

public enum BeliefCategory
{
    Survival,
    Harvest,
    Ritual,
    Warfare,
    Knowledge
}