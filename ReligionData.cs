using UnityEngine;
using UnityEngine.Serialization;

public enum PantheonRequirementMode { None, Any, MinimumTier, Specific }

[CreateAssetMenu(menuName="CivGame/Religion/Religion")]
public class ReligionData : ScriptableObject
{
    [Header("Identity")]
    public string religionName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Foundation")]
    public PantheonRequirementMode pantheonRequirementMode;
    public PantheonTier minimumPantheonTier = PantheonTier.God;
    public PantheonData[] compatiblePantheons;
    [FormerlySerializedAs("requiredPantheon")]
    [SerializeField, HideInInspector] private PantheonData legacyRequiredPantheon;
    [Tooltip("Cultures that must be adopted before this religion becomes available.")]
    public CultureData[] requiredCultures;
    [Tooltip("Faith cost to found this Religion (in a Holy Site)")]
    public int faithCost;
    public bool useMinimumAge;
    public TechAge minimumAge;

    public bool HasLegacyPantheon => legacyRequiredPantheon != null;
    public void ClearLegacyPantheon() => legacyRequiredPantheon = null;

    [Header("Beliefs")]
    // Optional additional beliefs that can be added later
    [Tooltip("Additional beliefs that can be unlocked later in the game")]
    public BeliefData[] enhancerBeliefs;

    [Header("Targeted Yield Bonuses")]
    [Tooltip("Per-tile yield bonuses granted by civilizations that have founded this religion.")]
    public TileYieldBonus[] tileYieldBonuses;
    [Tooltip("Per-building yield/stat bonuses granted by civilizations that have founded this religion. Can target exact buildings or building categories.")]
    public BuildingYieldBonus[] buildingYieldBonuses;
    [Tooltip("Per-unit stat bonuses granted by civilizations that have founded this religion.")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-worker stat bonuses granted by civilizations that have founded this religion.")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Per-city yield, defense, and happiness bonuses granted by civilizations that have founded this religion.")]
    public CityYieldBonus[] cityBonuses;
    [Tooltip("State-religion modifiers to per-turn city unhappiness caused by citizens following other religions.")]
    public NonStateReligionUnhappinessModifier[] nonStateReligionUnhappinessModifiers;

    // REMOVED: Unlocked Content arrays
    // Availability is now controlled solely by requiredTechs/requiredCultures in the respective data classes
    // Religious units should have ReligionData in their requiredTechs or requiredCultures
}
