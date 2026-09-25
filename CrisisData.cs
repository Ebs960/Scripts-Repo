using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Crisis is a world-scale event (Ice Age, Black Death, Bronze Age Collapse, etc.)
/// that modifies global mechanics, drives narrative phases, and injects missions into the game.
///
/// Crises create missions.  Missions create legacies.
/// </summary>
[CreateAssetMenu(fileName = "New Crisis", menuName = "Data/Crisis Data")]
public class CrisisData : ScriptableObject
{
    public enum CrisisScope { Global, Civilization, City, Continent, SubjectRelationship }
    public enum CrisisRepeatMode { OneTime, Repeatable }
    public enum CrisisMechanic
    {
        None, PredatorSurge, HostileRaiders, Drought, DiseaseOutbreak, IndustrialDamage,
        FinancialShock, PoliticalCoup, ConstitutionalConflict, PopularRevolution,
        IndependenceWar, LocustInfestation, TerroristCells, RobotDefection,
        AsteroidCountdown, GeneticMutation, AlienLanding
    }
    [Header("Identity")]
    public string crisisName;
    public Sprite icon;

    [Header("Narrative — Ominous Warning")]
    public Sprite ominousWarningSplash;
    [TextArea(5, 10)]
    public string ominousWarningText;

    [Header("Narrative — Obvious Warning")]
    public Sprite obviousWarningSplash;
    [TextArea(5, 10)]
    public string obviousWarningText;

    [Header("Narrative — Crisis Start")]
    public Sprite crisisStartSplash;
    [TextArea(5, 10)]
    public string crisisStartText;

    [Header("Narrative — Escalation")]
    public Sprite escalationSplash;
    [TextArea(5, 10)]
    public string escalationText;

    [Header("Narrative — Climax")]
    public Sprite climaxSplash;
    [TextArea(5, 10)]
    public string climaxText;

    [Header("Narrative — Resolution")]
    public Sprite resolutionSplash;
    [TextArea(5, 10)]
    public string resolutionText;

    [Header("Timing")]
    [Tooltip("Turns of ominous foreshadowing (subtle hints). 0 = skip ominous warning.")]
    public int ominousWarningTurns;
    [Tooltip("Turns of obvious warning (clear danger signals) after ominous phase. 0 = skip obvious warning.")]
    public int obviousWarningTurns;
    [Tooltip("Total active turns once the crisis starts (excludes warnings). 0 = indefinite (must be ended manually).")]
    public int durationTurns;
    [Tooltip("Turns after crisis start when escalation phase begins. 0 = no escalation phase.")]
    public int escalationAtTurn;
    [Tooltip("Turns after crisis start when climax phase begins. 0 = no climax phase.")]
    public int climaxAtTurn;

    [Header("Activation Window")]
    [Tooltip("Earliest game turn this crisis can trigger. 0 = any turn.")]
    public int earliestTurn;
    [Tooltip("Latest game turn this crisis can trigger. 0 = no limit.")]
    public int latestTurn;

    [Header("Age, Season, Scope & Repetition")]
    public bool useAgeWindow;
    public TechAge minimumAge = TechAge.PaleolithicAge;
    public TechAge maximumAge = TechAge.GalacticAge;
    public CrisisScope scope = CrisisScope.Global;
    public CrisisMechanic mechanic;
    public CrisisRepeatMode repeatMode = CrisisRepeatMode.OneTime;
    [Min(0)] public int cooldownTurns;
    [Tooltip("0 means unlimited for a repeatable crisis.")]
    [Min(0)] public int maximumOccurrences;
    public bool useSeasonFilter;
    public Season[] allowedSeasons;
    public bool mustStartAtSeasonBoundary;

    [Header("Runtime Integration")]
    [Min(0f)] public float minimumRiskScore;
    [Tooltip("Optional disease used by DiseaseOutbreak. Population loss remains owned by DiseaseManager/City disease processing.")]
    public DiseaseData crisisDisease;
    public CrisisProjectData[] crisisProjects;

    [Header("Repeat Completion")]
    public RepeatCompletionReward repeatCompletionReward;

    [Header("Activation Requirements")]
    public TechData[] requiredTechs;
    public CultureData[] requiredCultures;

    [Header("Crisis Missions")]
    [Tooltip("Missions offered directly by this crisis while it is active.")]
    public List<MissionData> crisisMissions = new List<MissionData>();

    [Header("World Overrides (active while crisis runs)")]
    public WorldOverride[] worldOverrides;

    [Header("Escalation World Overrides")]
    [Tooltip("Overrides layered over the active-crisis values when escalation begins. Later values replace earlier values of the same type.")]
    public WorldOverride[] escalationWorldOverrides;

    [Header("Climax World Overrides")]
    [Tooltip("Overrides layered over the escalation values when the climax begins. Later values replace earlier values of the same type.")]
    public WorldOverride[] climaxWorldOverrides;

    // ─────────────────────────────────────────────
    //  Phase enum
    // ─────────────────────────────────────────────

    public enum CrisisPhase
    {
        Dormant,
        OminousWarning,
        ObviousWarning,
        Active,
        Escalation,
        Climax,
        Resolution,
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
        PreySpawnMultiplier,
        PredatorSpawnMultiplier,
        WinterAttritionDamage,
        FoodYieldMultiplier,
        ForceWinter,
    }
}

[System.Serializable]
public class RepeatCompletionReward
{
    [Min(0)] public int gold;
    [Min(0)] public int food;
    [Min(0)] public int policyPoints;
}
