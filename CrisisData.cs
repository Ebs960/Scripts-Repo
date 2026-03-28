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

    [Header("Activation Requirements")]
    public TechData[] requiredTechs;
    public CultureData[] requiredCultures;

    [Header("Crisis Missions")]
    [Tooltip("Missions offered directly by this crisis while it is active.")]
    public List<MissionData> crisisMissions = new List<MissionData>();

    [Header("World Overrides (active while crisis runs)")]
    public WorldOverride[] worldOverrides;

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
