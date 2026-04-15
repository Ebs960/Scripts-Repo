using UnityEngine;

/// <summary>
/// ScriptableObject defining a disease type that can infect cities and herds.
/// </summary>
[CreateAssetMenu(fileName = "NewDiseaseData", menuName = "Data/Disease Data")]
public class DiseaseData : ScriptableObject
{
    [Header("Identity")]
    public string diseaseName;
    [TextArea(2, 4)]
    public string description;
    public Sprite icon;

    [Header("Duration")]
    [Tooltip("How many turns the disease lasts before it naturally resolves. 0 = permanent until cured.")]
    public int baseDuration = 10;

    [Header("Infection")]
    [Tooltip("Base chance (0-1) per turn for a city/herd to spontaneously contract this disease.")]
    [Range(0f, 1f)]
    public float baseInfectionChance = 0.02f;

    [Tooltip("Additional infection chance per population level in a city (overcrowding).")]
    public float infectionChancePerPopulation = 0.005f;

    [Tooltip("Additional infection chance per 100 animals in a herd (overcrowding).")]
    public float infectionChancePer100Animals = 0.01f;

    [Header("Spread")]
    [Tooltip("Chance (0-1) per turn to spread to an adjacent city or herd within spread radius.")]
    [Range(0f, 1f)]
    public float spreadChance = 0.15f;

    [Tooltip("Tile radius within which the disease can spread to other cities/herds.")]
    public int spreadRadius = 3;

    [Tooltip("Can this disease spread along trade routes (ignoring distance)?")]
    public bool spreadsAlongTradeRoutes = true;

    [Header("City Effects")]
    [Tooltip("Population levels lost per turn (can be fractional, accumulated).")]
    public float cityPopulationLossPerTurn = 0.1f;

    [Tooltip("Percentage penalty to food yield (0-1). E.g. 0.2 = -20% food.")]
    [Range(0f, 1f)]
    public float cityFoodPenaltyPct = 0.15f;

    [Tooltip("Percentage penalty to production yield (0-1).")]
    [Range(0f, 1f)]
    public float cityProductionPenaltyPct = 0.1f;

    [Tooltip("Percentage penalty to gold yield (0-1).")]
    [Range(0f, 1f)]
    public float cityGoldPenaltyPct = 0.1f;

    [Tooltip("Percentage penalty to science yield (0-1).")]
    [Range(0f, 1f)]
    public float citySciencePenaltyPct = 0.05f;

    [Tooltip("Percentage penalty to culture yield (0-1).")]
    [Range(0f, 1f)]
    public float cityCulturePenaltyPct = 0.05f;

    [Tooltip("Percentage penalty to faith yield (0-1).")]
    [Range(0f, 1f)]
    public float cityFaithPenaltyPct = 0f;

    [Tooltip("Flat morale drop per turn while infected.")]
    public int cityMoralePenaltyPerTurn = 2;

    [Tooltip("Flat loyalty penalty per turn while infected.")]
    public float cityLoyaltyPenaltyPerTurn = 1f;

    [Header("Herd Effects")]
    [Tooltip("Percentage of animals that die per turn (0-1). E.g. 0.05 = 5% mortality.")]
    [Range(0f, 1f)]
    public float herdMortalityRatePerTurn = 0.05f;

    [Tooltip("Percentage penalty to forage/grazing yield (0-1).")]
    [Range(0f, 1f)]
    public float herdForagePenaltyPct = 0.2f;

    [Header("Biome & Season Affinity")]
    [Tooltip("If true, this disease only appears in the specified biomes.")]
    public bool useBiomeFilter;
    [Tooltip("Biomes where this disease can spontaneously appear.")]
    public Biome[] affectedBiomes;

    [Tooltip("If true, this disease only appears during the specified seasons.")]
    public bool useSeasonFilter;
    [Tooltip("Seasons during which this disease can spontaneously appear.")]
    public Season[] affectedSeasons;

    [Header("Resistance & Immunity")]
    [Tooltip("TechData that grant immunity to this disease once researched.")]
    public TechData[] immunityTechs;

    [Tooltip("BuildingData that reduce infection chance when built in a city. Each grants resistancePctPerBuilding.")]
    public BuildingData[] resistanceBuildings;

    [Tooltip("Percentage resistance granted per resistance building (0-1).")]
    [Range(0f, 1f)]
    public float resistancePctPerBuilding = 0.25f;

    [Tooltip("Turns of immunity after recovering from this disease.")]
    public int immunityTurnsAfterRecovery = 5;
}
