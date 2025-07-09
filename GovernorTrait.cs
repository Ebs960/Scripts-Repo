using UnityEngine;

[CreateAssetMenu(fileName = "New Governor Trait", menuName = "Data/Governor Trait")]
public class GovernorTrait : ScriptableObject
{
    [Header("Basic Info")]
    public string traitName;
    [TextArea(2, 4)]
    public string description;
    public Sprite icon;

    [Header("Trigger Conditions")]
    [Tooltip("How this trait is acquired")]
    public TraitTrigger triggerType;
    public int requiredValue; // e.g., number of sieges survived, trade routes established

    [Header("Bonuses")]
    public int goldBonusModifier;
    public int productionBonusModifier;
    public int foodBonusModifier;
    public int scienceBonusModifier;
    public int cultureBonusModifier;
    public int faithBonusModifier;
    public int combatBonusModifier;
    public int cityDefenseBonusModifier;
    
    [Header("Special Effects")]
    [Tooltip("Special abilities this trait provides")]
    public TraitEffect[] specialEffects;
}

public enum TraitTrigger
{
    SiegesDefended,
    TradeRoutesEstablished,
    UnitsProduced,
    BuildingsConstructed,
    TechsResearched,
    CulturesAdopted,
    EnemiesDefeated,
    CitiesConquered,
    ReligionsConverted,
    YearsInOffice,
    PopulationGrowth
}

[System.Serializable]
public class TraitEffect
{
    public string effectName;
    public TraitEffectType effectType;
    public float value;
}

public enum TraitEffectType
{
    ReduceUnitCost,
    ReduceBuildingCost,
    IncreaseTradeRange,
    BonusResourceGeneration,
    ImmuneToSiege,
    FasterUnitProduction,
    BonusExperienceGain
} 