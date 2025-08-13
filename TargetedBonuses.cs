using UnityEngine;

// Shared serializable types for targeted bonuses applied by Techs and Cultures.
// These are data-only containers; game systems should read them and apply the effects at runtime.

[System.Serializable]
public class UnitStatBonus
{
    public CombatUnitData unit;

    [Header("Additive (flat)")]
    public int attackAdd;
    public int defenseAdd;
    public int healthAdd;
    public int movePointsAdd;
    public int rangeAdd;
    public int attackPointsAdd;
    public int moraleAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float defensePct;
    public float healthPct;
    public float movePointsPct;
    public float rangePct;
    public float attackPointsPct;
    public float moralePct;
}

[System.Serializable]
public class WorkerUnitStatBonus
{
    public WorkerUnitData worker;

    [Header("Additive (flat)")]
    public int workPointsAdd;
    public int movePointsAdd;
    public int healthAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float workPointsPct;
    public float movePointsPct;
    public float healthPct;
}

[System.Serializable]
public class EquipmentStatBonus
{
    public EquipmentData equipment;

    [Header("Additive (flat)")]
    public int attackAdd;
    public int defenseAdd;
    public int healthAdd;
    public int movePointsAdd;
    public int rangeAdd;
    public int attackPointsAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float defensePct;
    public float healthPct;
    public float movePointsPct;
    public float rangePct;
    public float attackPointsPct;
}

[System.Serializable]
public class ImprovementYieldBonus
{
    public ImprovementData improvement;

    [Header("Yield Add (flat per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class BuildingYieldBonus
{
    public BuildingData building;

    [Header("Yield Add (flat per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class GenericYieldBonus
{
    [Tooltip("Assign any asset that represents a district or other yield-bearing entity (e.g., DistrictData if present).")]
    public ScriptableObject target;

    [Header("Yield Add (flat per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;

    [Header("Yield % (per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
}
