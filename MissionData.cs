using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Mission is a multi-step event with objectives, narrative, world overrides, and legacy rewards.
/// All supporting types are nested inside to keep the file count low.
/// </summary>
[CreateAssetMenu(fileName = "New Mission", menuName = "Data/Mission Data")]
public class MissionData : ScriptableObject
{
    [Header("Identity")]
    public string missionName;
    public Sprite icon;
    [TextArea(3, 6)]
    public string description;
    [TextArea(4, 10)]
    public string flavorText;
    public Sprite splashImage;

    [Header("Activation")]
    [Tooltip("Earliest turn this mission can begin. 0 = available immediately.")]
    public int earliestTurn;
    [Tooltip("Last turn this mission can begin. 0 = no limit.")]
    public int latestTurn;
    public TechData[] requiredTechs;
    public CultureData[] requiredCultures;

    [Header("Objectives (completed sequentially)")]
    public List<Objective> objectives = new List<Objective>();

    [Header("Narrative Beats")]
    [Tooltip("Flavor text shown at each stage transition")]
    public NarrativeBeat[] narrativeBeats;

    [Header("World Overrides (active while mission runs)")]
    public WorldOverride[] worldOverrides;

    [Header("Rewards")]
    public RewardTier[] rewardTiers;

    // ─────────────────────────────────────────────
    //  Objective
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class Objective
    {
        public string objectiveName;
        [TextArea(1, 3)]
        public string description;
        public Sprite icon;

        public ObjectiveType type;
        [Tooltip("Target count / duration for this objective")]
        public int targetValue;

        [Header("Optional Filters")]
        [Tooltip("If set, only counts toward objective on these biomes")]
        public Biome[] biomeFilter;
        [Tooltip("If set, only this specific tech counts")]
        public TechData specificTech;
        [Tooltip("If set, only this specific culture counts")]
        public CultureData specificCulture;
        [Tooltip("If set, only kills of this unit type count")]
        public CombatUnitData specificUnit;
        [Tooltip("If set, only this improvement type counts")]
        public ImprovementData specificImprovement;
    }

    public enum ObjectiveType
    {
        SurviveTurns,
        BuildImprovements,
        DefeatAnimals,
        DefeatUnits,
        ReachPopulation,
        ResearchTech,
        ResearchCulture,
        FoundCity,
        OwnTiles,
        AccumulateGold,
        AccumulateFood,
        AccumulateFaith,
        TrainUnits,
        BuildBuilding,
        AdoptPolicy,
        ChangeGovernment,
        FormAlliance,
        FoundPantheon,
    }

    // ─────────────────────────────────────────────
    //  Narrative Beat
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class NarrativeBeat
    {
        [Tooltip("Shown when this objective index becomes active")]
        public int objectiveIndex;
        public string headline;
        [TextArea(3, 8)]
        public string narrativeText;
        public Sprite stageImage;
    }

    // ─────────────────────────────────────────────
    //  World Override
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class WorldOverride
    {
        public WorldOverrideType type;
        public float value;
    }

    public enum WorldOverrideType
    {
        WinterDurationTurns,
        DroughtChance,
        DroughtSeverity,
        AnimalSpawnMultiplier,
        WinterAttritionDamage,
        FoodYieldMultiplier,
    }

    // ─────────────────────────────────────────────
    //  Reward Tier
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class RewardTier
    {
        public string tierName;
        [TextArea(2, 4)]
        public string completionFlavorText;
        public Sprite tierBadge;
        [Tooltip("Minimum objectives completed to earn this tier")]
        public int requiredObjectivesCompleted;
        public LegacyData rewardLegacy;
    }
}
