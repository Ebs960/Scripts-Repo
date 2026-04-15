using UnityEngine;

/// <summary>
/// Runtime instance of an active disease on a city or herd.
/// Tracks remaining duration, accumulated fractional damage, and immunity cooldown.
/// </summary>
[System.Serializable]
public class DiseaseInstance
{
    [Tooltip("The disease template this instance is based on.")]
    public DiseaseData data;

    [Tooltip("Turns remaining before the disease resolves naturally. -1 = permanent until cured.")]
    public int turnsRemaining;

    [Tooltip("Accumulated fractional population loss (cities) — triggers level loss when >= 1.")]
    public float accumulatedPopulationLoss;

    [Tooltip("Turns of immunity remaining after recovery (prevents re-infection by same disease).")]
    public int immunityTurnsRemaining;

    public DiseaseInstance(DiseaseData disease)
        : this(disease, disease != null ? (disease.baseDuration > 0 ? disease.baseDuration : -1) : -1)
    {
    }

    public DiseaseInstance(DiseaseData disease, int turnsRemainingOverride)
    {
        data = disease;
        turnsRemaining = turnsRemainingOverride;
        accumulatedPopulationLoss = 0f;
        immunityTurnsRemaining = 0;
    }

    /// <summary>
    /// Returns true if the disease has expired (duration elapsed).
    /// Permanent diseases (turnsRemaining == -1) never expire on their own.
    /// </summary>
    public bool HasExpired => turnsRemaining == 0;

    /// <summary>
    /// Tick one turn of duration. Returns true if still active after tick.
    /// </summary>
    public bool TickDuration()
    {
        if (turnsRemaining > 0)
            turnsRemaining--;
        return turnsRemaining != 0;
    }
}
