using UnityEngine;
using UnityEngine.Serialization;

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
    public float defenseBonus;
    public float movementBonus;
    public float foodModifier;
    public float productionModifier;
    public float goldModifier;
    public float scienceModifier;
    public float cultureModifier;
    public float faithModifier;
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

    [Header("Choices")]
    [Tooltip("Which founder beliefs you can pick as your Pantheon bonus")]
    public BeliefData[] possibleFounderBeliefs;

    [Header("Type & Upgrades")]
    [Tooltip("Explicit pantheon tier. Spirits are early pantheons, while Gods are their stronger form.")]
    public PantheonTier tier = PantheonTier.Spirit;
    [FormerlySerializedAs("isSpirit")]
    [SerializeField, HideInInspector] private bool legacyIsSpirit = true;
    [SerializeField, HideInInspector] private bool tierInitialized;
    [Tooltip("Whether this pantheon (if a spirit) can be upgraded into a God-level pantheon")]
    public bool canUpgradeToGod = false;
    [Tooltip("Optional reference to the upgraded pantheon (God) this spirit becomes when upgraded")]
    public PantheonData upgradedPantheon;

    [Header("Bonuses")]
    [Tooltip("Unique units, buildings, and stat boosts granted while this spirit/god is active.")]
    public PantheonBonuses bonuses;

    public bool IsSpirit => tier == PantheonTier.Spirit;
    public bool IsGod => tier == PantheonTier.God;

    private void OnEnable()
    {
        SyncTierFromLegacyData();
    }

    private void OnValidate()
    {
        SyncTierFromLegacyData();

        if (!IsSpirit)
        {
            canUpgradeToGod = false;
            upgradedPantheon = null;
        }
    }

    private void SyncTierFromLegacyData()
    {
        if (!tierInitialized)
        {
            tier = legacyIsSpirit ? PantheonTier.Spirit : PantheonTier.God;
            tierInitialized = true;
        }

        legacyIsSpirit = IsSpirit;
    }
} 