// Assets/Scripts Repo/MissileData.cs
using UnityEngine;

/// <summary>
/// Defines a missile type — a third entity category distinct from combat and worker units.
/// Missiles are stored in cities, missile-capable units, and missile silo improvements.
/// They are launched via MissilePanelUI and fly via MissileProjectileController.
/// </summary>
[CreateAssetMenu(fileName = "NewMissileData", menuName = "Data/Missile Data")]
public class MissileData : ScriptableObject
{
    [Header("Identity")]
    public string missileName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Production")]
    [Tooltip("Production points required to build one missile in a city.")]
    public int productionCost = 100;
    [Tooltip("Gold cost for instant purchase.")]
    public int goldCost = 500;
    [Tooltip("Resources consumed when this missile's construction begins.")]
    public ResourceCost[] buildResourceCosts;
    [Tooltip("Technologies that must be researched before this missile type is available.")]
    public TechData[] requiredTechs;

    [Header("Range")]
    [Tooltip("Maximum tile distance from the launch source to the target tile.")]
    public int range = 5;

    [Header("Blast Damage")]
    [Tooltip("HP damage dealt to each unit on every tile within the blast radius.")]
    public int blastUnitDamage = 50;
    [Tooltip("Damage applied directly to the city's defense rating if a city occupies a tile in the blast.")]
    public int cityDamage = 30;
    [Tooltip("Radius of the blast in tiles around the target (0 = target tile only, 1 = target + immediate neighbors, etc.).")]
    public int blastRadius = 0;
    [Tooltip("If true and the struck city's defense reaches 0, the city is completely destroyed and removed from the map.")]
    public bool canWipeCity = false;

    [Header("Nuclear")]
    [Tooltip("Mark this missile as nuclear. Nuclear missiles apply radiation pollution to all blast tiles.")]
    public bool isNuclear = false;
    [Tooltip("Pollution intensity deposited on each tile in the blast radius. Higher = more yield penalty and unit damage per turn.")]
    public int pollutionLevel = 5;
    [Tooltip("Number of turns the radiation pollution persists before naturally clearing.")]
    public int pollutionDuration = 10;
    [Tooltip("HP damage dealt to unsheltered units each turn while they occupy a polluted tile.")]
    public int pollutionUnitDamagePerTurn = 2;
    [Tooltip("Fraction by which tile yields are reduced while the tile is polluted (0 = no reduction, 1 = total output loss).")]
    [Range(0f, 1f)]
    public float pollutionYieldPenalty = 0.5f;


    [Header("Interception & Defense")]
    [Tooltip("If true, gated anti-air / missile-defense units can fire on this missile before impact.")]
    public bool canBeIntercepted = true;
    [Tooltip("Damage this missile can absorb before it is destroyed by defensive fire.")]
    [Min(1)] public int interceptionHitPoints = 50;
    [Tooltip("Flat penalty applied to defender hit chance. Higher values make this missile harder to hit.")]
    [Range(0f, 1f)] public float interceptionEvasion = 0f;
    [Tooltip("Minimum anti-air range needed to engage this missile. Use 0 to allow any gated anti-air defender in range.")]
    public int minimumInterceptorRange = 0;

    [Header("Animation")]
    [Tooltip("How long the missile takes to travel from launch point to impact (seconds).")]
    public float flightDuration = 3f;
    [Tooltip("Peak height of the parabolic flight arc in world units.")]
    public float arcHeight = 30f;
    [Tooltip("Prefab instantiated at the launch point and driven along the arc by MissileProjectileController.")]
    public GameObject flightPrefab;
    [Tooltip("Prefab spawned at the impact position when the missile detonates.")]
    public GameObject impactPrefab;
    [Tooltip("Optional nuclear mushroom cloud / flash prefab spawned at impact (nuclear missiles only).")]
    public GameObject nuclearFlashPrefab;
}
