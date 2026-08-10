using UnityEngine;

/// <summary>
/// Difficulty-scaling "intelligence budget" for AI.
/// Instead of giving harder AI more resources (which feels like cheating),
/// harder difficulty gives the AI more planning budget: more candidates scored,
/// deeper searches, less decision noise. The AI plays by the same rules — it just
/// thinks harder.
///
/// Easy AI: shallow searches, noisy scoring (makes mistakes), no army groups.
/// Expert AI: deep searches, zero noise, full army coordination.
///
/// Assigned per-civ or per-difficulty at game start and read by AIPlanner,
/// AIContext, and TacticalEvaluator.
/// </summary>
public class AiBudget
{
    // ──── Search depths ────
    public int ApproachSearchRange  = 8;   // max hex distance for approach targets
    public int ForageSearchRange    = 5;   // BFS range for forage/resource
    public int ExploreSearchRange   = 6;   // BFS range for exploration
    public int CitySiteScanLimit    = 300; // max tiles scanned for city sites in AIContext
    public int CitySiteSearchRange  = 8;   // BFS range for settler city-site search

    // ──── Candidate limits ────
    // NOTE: MaxCandidatesPerUnit must always be a finite, positive cap. Even Expert
    // difficulty needs a hard ceiling — otherwise late-game turns with hundreds of
    // units become an unbounded CPU sink. Expert gets a bigger budget, not an infinite one.
    public int MaxCandidatesPerUnit = 64;

    // Time-boxed thinking: once this many milliseconds have been spent generating/scoring
    // candidates for a unit, remaining low-priority scans (exploration, resource search)
    // are skipped and the AI commits to whatever it has already found. 0 = no time cap.
    public float MaxCandidateEvalMilliseconds = 0f;

    // ──── Coordination ────
    public bool EnableArmyGroups    = true;
    public int  ArmyGroupRange      = 6;

    // ──── Decision noise (higher = more "mistakes") ────
    public float ScoreNoise         = 0f;  // ± random noise added to final command scores

    // ──── Danger map ────
    public bool EnableDangerMap     = true;

    // ──── HTN planning depth ────
    public bool EnableStrategicPlanning = true;  // EmpireAI + OperationalPlanner
    public int  StrategicReevalInterval = 1;     // how often EmpireAI re-evaluates (turns)

    // ════════════════════════════════════════════════════════
    //  Factory: create budget from difficulty level
    // ════════════════════════════════════════════════════════

    public static AiBudget ForDifficulty(AIDifficulty difficulty)
    {
        return difficulty switch
        {
            AIDifficulty.Easy => new AiBudget
            {
                ApproachSearchRange = 4,
                ForageSearchRange = 3,
                ExploreSearchRange = 3,
                CitySiteScanLimit = 100,
                CitySiteSearchRange = 5,
                MaxCandidatesPerUnit = 12,
                MaxCandidateEvalMilliseconds = 2f,
                EnableArmyGroups = false,
                ArmyGroupRange = 4,
                ScoreNoise = 4f,
                EnableDangerMap = true,
                EnableStrategicPlanning = false,
                StrategicReevalInterval = 3
            },
            AIDifficulty.Normal => new AiBudget
            {
                ApproachSearchRange = 6,
                ForageSearchRange = 4,
                ExploreSearchRange = 5,
                CitySiteScanLimit = 200,
                CitySiteSearchRange = 6,
                MaxCandidatesPerUnit = 24,
                MaxCandidateEvalMilliseconds = 3f,
                EnableArmyGroups = true,
                ArmyGroupRange = 5,
                ScoreNoise = 1.5f,
                EnableDangerMap = true,
                EnableStrategicPlanning = true,
                StrategicReevalInterval = 2
            },
            AIDifficulty.Hard => new AiBudget
            {
                ApproachSearchRange = 8,
                ForageSearchRange = 6,
                ExploreSearchRange = 7,
                CitySiteScanLimit = 400,
                CitySiteSearchRange = 10,
                MaxCandidatesPerUnit = 48,
                MaxCandidateEvalMilliseconds = 5f,
                EnableArmyGroups = true,
                ArmyGroupRange = 8,
                ScoreNoise = 0.5f,
                EnableDangerMap = true,
                EnableStrategicPlanning = true,
                StrategicReevalInterval = 1
            },
            AIDifficulty.Expert => new AiBudget
            {
                ApproachSearchRange = 10,
                ForageSearchRange = 8,
                ExploreSearchRange = 8,
                CitySiteScanLimit = 500,
                CitySiteSearchRange = 12,
                // Expert gets a much bigger budget than lower difficulties, but it is
                // never unbounded — a hard candidate cap plus a time cap keep per-unit
                // planning cost predictable even with hundreds of late-game units.
                MaxCandidatesPerUnit = 96,
                MaxCandidateEvalMilliseconds = 8f,
                EnableArmyGroups = true,
                ArmyGroupRange = 10,
                ScoreNoise = 0f,
                EnableDangerMap = true,
                EnableStrategicPlanning = true,
                StrategicReevalInterval = 1
            },
            _ => new AiBudget() // default = Hard
        };
    }

    // ════════════════════════════════════════════════════════
    //  Factory: scale a difficulty budget down by actor sophistication tier
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a budget for a given difficulty, then scales it down according to the actor's
    /// sophistication tier. Major civilizations get the full difficulty budget. City-states
    /// get a cheap regional budget with lightweight army coordination but no HTN strategic
    /// layer. Tribes get an even cheaper local-survival budget with small raiding/defense groups.
    /// This keeps the ~13 minor actors from costing as much
    /// CPU as a full civilization while still letting them act sensibly.
    /// </summary>
    public static AiBudget For(AIDifficulty difficulty, AIActorTier tier)
    {
        AiBudget b = ForDifficulty(difficulty);
        switch (tier)
        {
            case AIActorTier.CityState:
                b.ApproachSearchRange   = Mathf.Min(b.ApproachSearchRange, 5);
                b.ForageSearchRange     = Mathf.Min(b.ForageSearchRange, 3);
                b.ExploreSearchRange    = Mathf.Min(b.ExploreSearchRange, 4);
                b.CitySiteScanLimit     = Mathf.Min(b.CitySiteScanLimit, 80);
                b.CitySiteSearchRange   = Mathf.Min(b.CitySiteSearchRange, 4);
                b.MaxCandidatesPerUnit  = Mathf.Min(b.MaxCandidatesPerUnit, 16);
                b.MaxCandidateEvalMilliseconds = Mathf.Min(b.MaxCandidateEvalMilliseconds > 0f ? b.MaxCandidateEvalMilliseconds : 4f, 3f);
                b.EnableArmyGroups      = true;
                b.ArmyGroupRange        = Mathf.Min(b.ArmyGroupRange, 4);
                b.EnableStrategicPlanning = false;
                break;

            case AIActorTier.Tribe:
                b.ApproachSearchRange   = Mathf.Min(b.ApproachSearchRange, 3);
                b.ForageSearchRange     = Mathf.Min(b.ForageSearchRange, 3);
                b.ExploreSearchRange    = Mathf.Min(b.ExploreSearchRange, 3);
                b.CitySiteScanLimit     = Mathf.Min(b.CitySiteScanLimit, 40);
                b.CitySiteSearchRange   = Mathf.Min(b.CitySiteSearchRange, 4);
                b.MaxCandidatesPerUnit  = Mathf.Min(b.MaxCandidatesPerUnit, 8);
                b.MaxCandidateEvalMilliseconds = Mathf.Min(b.MaxCandidateEvalMilliseconds > 0f ? b.MaxCandidateEvalMilliseconds : 4f, 1.5f);
                b.EnableArmyGroups      = true;
                b.ArmyGroupRange        = Mathf.Min(b.ArmyGroupRange, 3);
                b.EnableStrategicPlanning = false;
                break;

            case AIActorTier.Major:
            default:
                break; // full difficulty budget, unchanged
        }
        return b;
    }

    /// <summary>
    /// Classifies a civilization's AI sophistication tier from its CivData flags.
    /// Major civilizations: full strategic/operational/tactical AI.
    /// City-states: regional diplomatic, military, economic, technological, and cultural AI.
    /// Tribes: local movement, survival, raid, hunt, settle, technology, and culture AI.
    /// </summary>
    public static AIActorTier ResolveTier(CivData data)
    {
        if (data == null) return AIActorTier.Major;
        if (data.isTribe) return AIActorTier.Tribe;
        if (data.isCityState) return AIActorTier.CityState;
        return AIActorTier.Major;
    }
}

public enum AIDifficulty
{
    Easy,
    Normal,
    Hard,
    Expert
}

/// <summary>
/// AI actor sophistication tier. Determines how much of the planning pipeline runs
/// and how expensive its per-unit search budget is. See AiBudget.For / ResolveTier.
/// </summary>
public enum AIActorTier
{
    Major,
    CityState,
    Tribe
}
