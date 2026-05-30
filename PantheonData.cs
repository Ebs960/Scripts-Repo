using UnityEngine;

public enum PantheonTier
{
    Spirit,
    God
}

[System.Serializable]
public class PantheonBonuses
{
    [Header("Unlocked Content")]
    [Tooltip("Combat units this spirit/god unlocks for the founding civilization.")]
    public CombatUnitData[] unlockedCombatUnits;
    [Tooltip("Worker units this spirit/god unlocks for the founding civilization.")]
    public WorkerUnitData[] unlockedWorkerUnits;
    [Tooltip("Buildings this spirit/god unlocks for the founding civilization.")]
    public BuildingData[] unlockedBuildings;

    [Header("Civilization Modifiers")]
    public float attackBonus;
    public float meleeAttackBonus;
    public float rangedAttackBonus;
    public float cityAttackBonus;
    public float defenseBonus;
    public float movementBonus;
    public float foodModifier;
    public float productionModifier;
    public float goldModifier;
    public float scienceModifier;
    public float cultureModifier;
    public float faithModifier;

    [Header("Targeted Yield Bonuses")]
    [Tooltip("Per-turn yield modifiers for specific combat units owned by the civilization.")]
    public UnitYieldBonus[] unitYieldBonuses;
    [Tooltip("Per-turn yield modifiers for specific worker units owned by the civilization.")]
    public WorkerUnitYieldBonus[] workerYieldBonuses;
    [Tooltip("Per-turn yield modifiers for specific buildings in owned cities.")]
    public BuildingYieldBonus[] buildingYieldBonuses;
    [Tooltip("Per-turn yield modifiers for tiles matching terrain filters in owned territory.")]
    public TileYieldBonus[] tileYieldBonuses;
    [Tooltip("Per-turn yield modifiers for specific improvements in owned territory.")]
    public ImprovementYieldBonus[] improvementBonuses;
    [Tooltip("Per-unit stat bonuses granted by this pantheon (e.g., healing speed).")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-worker stat bonuses granted by this pantheon (e.g., healing speed).")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Disease modifiers granted by this pantheon.")]
    public DiseaseModifierBonus[] diseaseBonuses;
    public AttritionModifierBonus[] attritionBonuses;
    [Tooltip("Reduces the percent of herd animals lost to starvation (e.g. 0.05 = -5 percentage points)")]
    public float herdStarvationPercentReduction = 0f;
    [Tooltip("Per-herd per-turn yield bonuses granted by this pantheon (can filter by animal species).")]
    public HerdYieldBonus[] herdYieldBonuses;
    [Tooltip("Per-turn yield modifiers applied to all cities or just the capital.")]
    public CityYieldBonus[] cityYieldBonuses;
}

[CreateAssetMenu(menuName="CivGame/Religion/Pantheon")]
public class PantheonData : ScriptableObject
{
    [Header("Identity")]
    public string pantheonName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Cost")]
    [Tooltip("Faith required to found this Pantheon")]
    public int faithCost;

    [Header("Type & Upgrades")]
    [Tooltip("Explicit pantheon tier. Spirits are early pantheons, while Gods are their stronger form.")]
    public PantheonTier tier = PantheonTier.Spirit;
    [Tooltip("Whether this pantheon (if a spirit) can be upgraded into a God-level pantheon")]
    public bool canUpgradeToGod = false;
    [Tooltip("Optional reference to the upgraded pantheon (God) this spirit becomes when upgraded")]
    public PantheonData upgradedPantheon;

    [Header("Bonuses")]
    [Tooltip("Unique units, buildings, and stat boosts granted while this spirit/god is active.")]
    public PantheonBonuses bonuses;

    public bool IsSpirit => tier == PantheonTier.Spirit;
    public bool IsGod => tier == PantheonTier.God;
    private void OnValidate()
    {
        if (!IsSpirit)
        {
            canUpgradeToGod = false;
            upgradedPantheon = null;
        }
    }
} 