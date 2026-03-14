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
    public int MaxCandidatesPerUnit = 64;  // cap on commands scored per unit (0 = unlimited)

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
                MaxCandidatesPerUnit = 0, // unlimited
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
}

public enum AIDifficulty
{
    Easy,
    Normal,
    Hard,
    Expert
}
