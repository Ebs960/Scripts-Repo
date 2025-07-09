// Assets/Scripts/Data/CultureData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "NewCultureData", menuName = "Data/Culture Data")]
public class CultureData : ScriptableObject
{
    [Header("Identification")]
    public string cultureName;

    [TextArea]
    public string description;

    [Header("Cost & Requirements")]
    public int cultureCost;
    public CultureData[] requiredCultures;
    public int requiredCityCount;
    public Biome[] requiredControlledBiomes;

    [Header("Unlocks & Bonuses")]
    public PolicyData[] unlocksPolicies;
    public CombatUnitData[] unlockedUnits;
    public WorkerUnitData[] unlockedWorkerUnits;
    public BuildingData[] unlockedBuildings;
    public AbilityData[] unlockedAbilities;
    public GovernmentData[] unlockedGovernments;
    public ReligionData[] unlockedReligions;
    public float attackBonus;
    public float defenseBonus;
    public float movementBonus;
    public float foodModifier;
    public float productionModifier;
    public float goldModifier;
    public float scienceModifier;
    public float cultureModifier;
    public float faithModifier;

    [Header("Governor Bonuses")]
    public int additionalGovernorSlots;
    public GovernorTrait[] unlockedGovernorTraits;
}