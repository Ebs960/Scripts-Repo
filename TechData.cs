// Assets/Scripts/Data/TechData.cs
using UnityEngine;

public enum TechAge
{
    PrehistoricAge,
    TribalAge,
    IceAge,
    VillageAge,
    MonumentAge,
    CopperAge,
    BronzeAge,
    IronAge,
    ClassicalAge,
    AxialAge,
    MigrationAge,
    DarkAge,
    FeudalAge,
    CastleAge,
    RenaissanceAge,
    ColonialAge,
    ReformationAge,
    EnlightenmentAge,
    RevolutionAge,
    SteamAge,
    RailroadAge,
    ImperialAge,
    ModernAge,
    AtomicAge,
    InformationAge,
    NanoAge,
    MutantAge,
    SolarAge,
    AquarianAge,
    PlasmaAge,
    InterstellarAge,
    GalacticAge
}

[CreateAssetMenu(fileName = "NewTechData", menuName = "Data/Technology Data")]
public class TechData : ScriptableObject
{
    [Header("Identification")]
    public string techName;
    public TechAge techAge;

    [TextArea]
    public string description;

    [Header("Cost & Requirements")]
    public int scienceCost;
    public TechData[] requiredTechnologies;
    public CultureData[] requiredCultures;      // ← new: culture prereqs
    public int requiredCityCount;
    public Biome[] requiredControlledBiomes;

    [Header("Unlocks & Bonuses")]
    public CombatUnitData[] unlockedUnits;
    public WorkerUnitData[] unlockedWorkerUnits;
    public ImprovementData[] unlocksImprovements;
    public BuildingData[] unlockedBuildings;
    public EquipmentData[] unlockedEquipment;
    public PolicyData[] unlockedPolicies;
    public GovernmentData[] unlockedGovernments;
    public ReligionData[] unlockedReligions;
    public LeaderData[] unlockedLeaders;
    
    [Tooltip("Whether this technology unlocks pantheon founding and religion mechanics")]
    public bool unlocksReligion;
    public float attackBonus;                   // e.g. +10% attack
    public float defenseBonus;                  // e.g. +10% defense
    public float movementBonus;                 // e.g. +1 move point
    public float foodModifier;                  // New
    public float productionModifier;            // New
    public float goldModifier;                  // New
    public float scienceModifier;               // New
    public float cultureModifier;               // New
    public float faithModifier;                 // New

    [Header("Governor Bonuses")]
    public int additionalGovernorSlots;
    public GovernorTrait[] unlockedGovernorTraits;
}