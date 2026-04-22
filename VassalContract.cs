using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Runtime subject relationship between an overlord civilization and a subject civilization.
/// This is the single source of truth for vassal mechanics — tribute, liberty, restrictions.
/// Owned and processed by SubjectManager.
/// </summary>
[System.Serializable]
public class VassalContract
{
    // ── Participants ──────────────────────────────────────────────────────────
    [System.NonSerialized] public Civilization overlord;
    [System.NonSerialized] public Civilization subject;

    /// <summary>civData.civName of the overlord — used for save/load resolution.</summary>
    public string overlordCivName;
    /// <summary>civData.civName of the subject — used for save/load resolution.</summary>
    public string subjectCivName;

    // ── Tribute Terms ─────────────────────────────────────────────────────────
    /// <summary>Fraction of gold yield transferred to overlord each turn (0.0 – 1.0).</summary>
    [Range(0f, 1f)] public float goldTributePct = 0.10f;
    /// <summary>Fraction of science yield transferred each turn.</summary>
    [Range(0f, 1f)] public float scienceTributePct = 0f;
    /// <summary>Fraction of food yield transferred each turn.</summary>
    [Range(0f, 1f)] public float foodTributePct = 0f;

    // ── Military Obligation ───────────────────────────────────────────────────
    /// <summary>Number of combat units the subject is expected to provide during declared wars.</summary>
    public int militaryObligationCount = 2;

    // ── Autonomy & Interference ───────────────────────────────────────────────
    /// <summary>Subject autonomy level 0–100. Higher = more self-governing; less tribute scrutiny.</summary>
    [Range(0, 100)] public int autonomyLevel = 50;
    /// <summary>How the overlord treats the subject's local religion.</summary>
    public ReligionToleranceRule religionRule = ReligionToleranceRule.FullTolerance;
    /// <summary>The last game turn on which the overlord interfered. Cooldown prevents stacking.</summary>
    public int lastInterferenceTurn = -999;
    /// <summary>Minimum turns between interference actions to prevent spam.</summary>
    public int interferenceCooldown = 5;

    // ── Subject Pressure ──────────────────────────────────────────────────────
    /// <summary>
    /// Liberty desire 0–100. Rises from religious oppression, tribute exhaustion, culture
    /// mismatch, and interference. When it reaches breakawayThreshold the subject may attempt
    /// independence.
    /// </summary>
    [Range(0f, 100f)] public float libertyDesire = 0f;
    /// <summary>At this liberty desire level the subject will attempt to break away.</summary>
    public float breakawayThreshold = 85f;
    /// <summary>Subject's opinion of the overlord (-100 to +100). Separate from general diplomacy.</summary>
    [Range(-100f, 100f)] public float subjectOpinion = 0f;
    /// <summary>Accumulated resentment from interference actions. Decays slowly.</summary>
    [Range(0f, 100f)] public float resentment = 0f;
    /// <summary>
    /// How exhausted the subject is from paying tribute each turn.
    /// Rises when tribute takes a meaningful share of their income.
    /// </summary>
    [Range(0f, 100f)] public float tributeExhaustion = 0f;
    /// <summary>
    /// Subject's assessment of their own military strength vs. overlord's.
    /// High confidence accelerates independence attempts.
    /// </summary>
    [Range(0f, 100f)] public float militaryConfidence = 30f;

    // ── State ─────────────────────────────────────────────────────────────────
    /// <summary>True if the subject capitulated (was beaten into vassalage, not freely agreed).</summary>
    public bool isCapitulated = false;
    /// <summary>Turn the contract was established.</summary>
    public int contractStartTurn = 0;

    // ── Derived ───────────────────────────────────────────────────────────────

    /// <summary>Is an interference action on cooldown?</summary>
    public bool IsInterferenceOnCooldown(int currentTurn)
        => (currentTurn - lastInterferenceTurn) < interferenceCooldown;

    /// <summary>
    /// Effective breakaway threshold. Capitulated subjects need more built-up liberty desire
    /// before daring to break away (they know they were beaten once already).
    /// </summary>
    public float EffectiveBreakawayThreshold =>
        isCapitulated ? breakawayThreshold + 15f : breakawayThreshold;

    /// <summary>Has the subject's liberty desire crossed the breakaway threshold?</summary>
    public bool WantsIndependence() => libertyDesire >= EffectiveBreakawayThreshold;

    /// <summary>
    /// Apply one turn of liberty desire growth from all pressure sources.
    /// Call this from SubjectManager.ProcessLibertyTick().
    /// </summary>
    public void TickLibertyDesire(int currentTurn)
    {
        float delta = 0f;

        // Base decay toward stability for content subjects
        // Capitulated subjects are more suppressed — stronger passive decay
        float decayRate = isCapitulated ? 1.0f : 0.5f;
        if (libertyDesire > 20f && subjectOpinion > 20f)
            delta -= decayRate;

        // Religious pressure
        if (religionRule == ReligionToleranceRule.ForcedConversion)       delta += 3f;
        else if (religionRule == ReligionToleranceRule.StateReligionRequired) delta += 1.5f;
        else if (religionRule == ReligionToleranceRule.LimitedTolerance)   delta += 0.5f;

        // Tribute exhaustion feeds into desire
        delta += tributeExhaustion * 0.05f;

        // Military confidence: confident subjects push harder
        delta += (militaryConfidence - 30f) * 0.02f;

        // Resentment from interference
        delta += resentment * 0.03f;

        // Distance from the overlord's capital weakens direct control.
        delta += PoliticalDistanceUtility.GetVassalDistanceLibertyPressure(this);

        // Autonomy level slows growth
        delta *= Mathf.Lerp(1.5f, 0.5f, autonomyLevel / 100f);

        libertyDesire = Mathf.Clamp(libertyDesire + delta, 0f, 100f);

        // Slowly decay resentment
        resentment = Mathf.Max(0f, resentment - 1f);
        // Slowly decay tribute exhaustion
        tributeExhaustion = Mathf.Max(0f, tributeExhaustion - 0.5f);
    }
}
