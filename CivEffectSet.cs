using UnityEngine;

/// <summary>
/// Shared gameplay effects that can be attached to civilization identities.
/// Mirrors the major effect families used by technologies, cultures, and buildings
/// so civ assets can participate in the same data-driven bonus systems.
/// </summary>
[System.Serializable]
public class CivEffectSet
{
    [Header("Unit Bonuses")]
    public UnitStatBonus[] unitBonuses;
    public WorkerUnitStatBonus[] workerBonuses;
    public UnitAuraBonus[] auraBonuses;
    public UnitProductionModifier[] unitProductionModifiers;
    public UnitYieldBonus[] unitYieldBonuses;
    public WorkerUnitYieldBonus[] workerYieldBonuses;
    public int allWorkersWorkPoints = 0;

    [Header("Equipment Bonuses")]
    public EquipmentStatBonus[] equipmentBonuses;
    public EquipmentYieldBonus[] equipmentYieldBonuses;

    [Header("Map, City, and Building Bonuses")]
    public TileYieldBonus[] tileYieldBonuses;
    public ImprovementYieldBonus[] improvementBonuses;
    public BuildingYieldBonus[] buildingBonuses;
    public CityYieldBonus[] cityBonuses;
    public GenericYieldBonus[] genericYieldBonuses;

    [Header("Limits")]
    public UnitLimitModifier[] unitLimitModifiers;
    public BuildingLimitModifier[] buildingLimitModifiers;
    public CitySlotModifier[] citySlotModifiers;

    [Header("Religion, Health, and Environment")]
    public NonStateReligionUnhappinessModifier[] nonStateReligionUnhappinessModifiers;
    public DiseaseModifierBonus[] diseaseBonuses;
    public AttritionModifierBonus[] attritionBonuses;

    [Header("Herd Modifiers")]
    public float herdStarvationPercentReduction = 0f;
    public HerdYieldBonus[] herdYieldBonuses;
}
