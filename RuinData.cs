using UnityEngine;

/// <summary>
/// ScriptableObject defining a type of ancient ruin — what it looks like, how often it spawns,
/// and exactly what rewards the exploring civilization receives.
/// Create via Assets > Create > Data > Ruin Data.
/// </summary>
[CreateAssetMenu(fileName = "NewRuinData", menuName = "Data/Ruin Data")]
public class RuinData : ScriptableObject
{
    [Header("Identity")]
    public string ruinName;
    [TextArea]
    public string description;
    public Sprite icon;

    [Header("Visual")]
    [Tooltip("Prefab instantiated at the ruin's world position. If left empty, the AncientRuinsManager's default ruinPrefab is used instead.")]
    public GameObject ruinPrefab;

    [Header("Ruin Type")]
    [Tooltip("Categorizes this ruin for filtering and display purposes.")]
    public AncientRuinsManager.RuinType ruinType;

    [Header("Planet Filtering")]
    [Tooltip("If non-empty, this ruin will ONLY spawn on planets whose PlanetType is in this list. "
           + "Leave empty to allow spawning on any planet.")]
    public PlanetType[] allowedPlanetTypes;

    [Header("Water Spawning")]
    [Tooltip("When true, this ruin is allowed to spawn on water tiles (lakes/rivers). "
           + "Water ruins will be placed on the underwater layer, not the surface.")]
    public bool canSpawnInWater = false;

    [Header("Spawn Weight")]
    [Tooltip("Relative chance this ruin type is selected when ruins are placed on a planet. "
           + "Higher values = more common. E.g. a weight of 2 is twice as likely as a weight of 1.")]
    [Range(0.1f, 10f)]
    public float spawnWeight = 1f;

    [Header("Gold Reward")]
    [Tooltip("If true, exploring this ruin awards a random amount of gold to the civilization.")]
    public bool grantsGold = false;
    [Tooltip("Minimum gold awarded (inclusive).")]
    public int goldMin = 50;
    [Tooltip("Maximum gold awarded (inclusive).")]
    public int goldMax = 200;

    [Header("Culture Reward")]
    [Tooltip("If true, exploring this ruin awards culture. Applied toward current culture adoption if one is active.")]
    public bool grantsCulture = false;
    [Tooltip("Minimum culture awarded (inclusive).")]
    public int cultureMin = 20;
    [Tooltip("Maximum culture awarded (inclusive).")]
    public int cultureMax = 100;

    [Header("Faith Reward")]
    [Tooltip("If true, exploring this ruin awards faith to the civilization.")]
    public bool grantsFaith = false;
    [Tooltip("Minimum faith awarded (inclusive).")]
    public int faithMin = 15;
    [Tooltip("Maximum faith awarded (inclusive).")]
    public int faithMax = 75;

    [Header("Map Reveal")]
    [Tooltip("If true, exploring this ruin reveals fog of war around the tile.")]
    public bool revealsMap = false;
    [Tooltip("Radius (in hex tiles) around the ruin to reveal when explored.")]
    [Range(1, 15)]
    public int revealRadius = 5;

    [Header("Population Reward")]
    [Tooltip("If true, exploring this ruin increases population in the civilization's nearest city.")]
    public bool grantsPopulation = false;
    [Tooltip("Number of population levels granted to the nearest city.")]
    [Range(1, 5)]
    public int populationBonus = 1;

    [Header("Technology Reward")]
    [Tooltip("Technologies immediately granted to the exploring civilization when this ruin is explored. "
           + "Each tech is skipped if the civilization already knows it.")]
    public TechData[] guaranteedTechs;
}
