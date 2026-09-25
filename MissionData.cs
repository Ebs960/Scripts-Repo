using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Mission is a player-driven goal (oath, achievement, era goal, strategic objective)
/// with multi-step objectives, narrative beats, and legacy rewards.
///
/// Missions can exist standalone or be injected by CrisisManager during a crisis.
/// World-scale overrides and crisis narratives live on CrisisData, NOT here.
/// </summary>
[CreateAssetMenu(fileName = "New Mission", menuName = "Data/Mission Data")]
public class MissionData : ScriptableObject
{
    [System.Flags]
    public enum MissionStrategyTag { None=0, Military=1, Economic=2, Scientific=4, Diplomatic=8, Stability=16, Survival=32, Reform=64, Repression=128 }
    public enum ParticipantRole { Any, Overlord, Subject }
    public enum ObjectiveTargetMode { Fixed, PerCity, PerAffectedCity, PerStartingPopulation, PerStartingMilitaryUnit, PerAffectedImprovement, PerStartingTradeRoute, PercentOfBaseline, PerStartingRobotUnit, PerCrisisActorAtActivation, PerAffectedBuilding }
    public enum ObjectiveComparison { AtLeast, AtMost }
    public enum ObjectiveCompletionTiming { Immediate, CrisisEnd }
    public enum BaselineMetric { None, Population, Cities, Infrastructure, Treasury, FoodReserve, TradeIncome, MilitaryUnits, RobotUnits }
    [Header("Identity")]
    public string missionName;
    public Sprite icon;
    [TextArea(3, 6)]
    public string description;
    [TextArea(4, 10)]
    public string flavorText;
    public Sprite splashImage;

    [Header("Victory")]
    public Sprite victorySplashImage;
    [TextArea(4, 10)]
    public string victoryFlavorText;

    [Header("Failure")]
    public Sprite failureSplashImage;
    [TextArea(4, 10)]
    public string failureFlavorText;

    [Header("Activation")]
    [Tooltip("Earliest turn this mission can begin. 0 = available immediately.")]
    public int earliestTurn;
    [Tooltip("Last turn this mission can begin. 0 = no limit.")]
    public int latestTurn;
    public TechData[] requiredTechs;
    public CultureData[] requiredCultures;
    public ParticipantRole participantRole;
    public MissionStrategyTag strategyTags;
    [Min(0), Tooltip("0 uses the crisis duration; otherwise the mission fails this many turns after it starts.")]
    public int completionDeadlineTurns;

    [Header("Objectives (completed sequentially)")]
    public List<Objective> objectives = new List<Objective>();

    [Header("Mission Constraints")]
    [Tooltip("Failure conditions monitored while this mission is active.")]
    public MissionConstraint[] constraints;

    [Header("Narrative Beats")]
    [Tooltip("Flavor text shown at each stage transition")]
    public NarrativeBeat[] narrativeBeats;

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
        public ObjectiveTargetMode targetMode;
        public float targetMultiplier = 1f;
        public int minimumTarget;
        public int maximumTarget;
        public ObjectiveComparison comparison = ObjectiveComparison.AtLeast;
        [Min(0)] public int requiredConsecutiveTurns;
        public ObjectiveCompletionTiming completionTiming = ObjectiveCompletionTiming.Immediate;
        public BaselineMetric baselineMetric;

        [Header("Optional Filters")]
        [Tooltip("If set, only counts toward objective on these biomes")]
        public Biome[] biomeFilter;
        [Tooltip("If set, only this specific tech counts")]
        public TechData specificTech;
        [Tooltip("If set, only this specific culture counts")]
        public CultureData specificCulture;
        [Tooltip("If set, only kills of this unit type count")]
        public CombatUnitData specificUnit;
        [Tooltip("If set, only kills of these specific unit types count")]
        public CombatUnitData[] specificUnits;
        [Tooltip("If set, only kills of these specific worker unit types count")]
        public WorkerUnitData[] specificWorkerUnits;
        [Tooltip("If set, only kills of these combat categories count (e.g. Animal, Spearman)")]
        public CombatCategory[] specificCategories;
        [Tooltip("If set, only this improvement type counts")]
        public ImprovementData specificImprovement;
        [Tooltip("If set, only these improvement types count")]
        public ImprovementData[] specificImprovements;
        [Tooltip("If set, only this specific building counts")]
        public BuildingData specificBuilding;
        [Tooltip("If set, only these specific buildings count")]
        public BuildingData[] specificBuildings;
        [Tooltip("If set, only this crisis project counts")]
        public CrisisProjectData specificProject;
        public bool useBuildingCategoryFilter;
        public BuildingCategory buildingCategory;
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
        DeclareWar,
        MakePeace,
        EstablishTrade,
        DefeatCrisisUnits, DestroyCrisisSpawners, RepairImprovements, RestoreDisabledBuildings,
        ReclaimCity, DefendCityBattles, MaintainNetGold, MaintainNetFood,
        MaintainPercentOfBaseline, MaintainArmySize, PreservePopulation, PreserveCities,
        PreserveInfrastructure, MaintainTradeIncome, RaiseAverageHappiness, RaiseAverageOrder,
        ResolveFactionDemand, CureInfectedCities, CompleteCrisisProject, ReintegrateCrisisUnits,
        RaidSettlements, HuntOrForage, BuildImprovementsInUnaffectedArea,
        AchieveIndependence, RestoreSubjectControl, NegotiateAutonomy,
        CurrentInfectedCities,
    }

    [System.Serializable]
    public class MissionConstraint
    {
        public ConstraintType type;
        [Tooltip("Constraint becomes active after this objective index is completed. -1 = active immediately.")]
        public int activatesAfterObjectiveIndex = -1;
        [Tooltip("Target count used by count-based constraints.")]
        public int targetValue = 1;
        public CountComparison comparison = CountComparison.EqualTo;
        [TextArea(2, 4)]
        public string failureFlavorText;

        [Header("Optional Filters")]
        public CombatUnitData specificUnit;
        public CombatUnitData[] specificUnits;
        public WorkerUnitData[] specificWorkerUnits;
        public CombatCategory[] specificCategories;
        public ImprovementData specificImprovement;
        public ImprovementData[] specificImprovements;
        public BuildingData specificBuilding;
        public BuildingData[] specificBuildings;
    }

    public enum ConstraintType
    {
        NoUnitLosses,
        MaintainImprovementCount,
        MaintainBuildingCount,
        NoCityLosses, NoCrisisInfrastructureLosses, NoPopulationLossFromCrisis,
        MaximumPopulationLossPercent, MaximumCrisisDamage, NoCrisisUnitLosses,
    }

    public enum CountComparison
    {
        EqualTo,
        AtLeast,
        AtMost,
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
