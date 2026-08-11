using UnityEngine;

/// <summary>
/// Defines a political opinion reaction pushed to governors when a policy or government
/// is adopted/changed. Attach arrays of these to GovernmentData and PolicyData.
/// The reaction engine in Civilization filters by personality and mismatch conditions,
/// then calls Governor.AddOpinionModifier on each matching governor.
/// </summary>
[System.Serializable]
public class GovernorOpinionEffect
{
    [Tooltip("Human-readable reason shown in opinion log (e.g. 'Centralizing Government Adopted')")]
    public string reason = "Political Reaction";

    [Tooltip("Opinion delta applied to each matching governor (negative = anger)")]
    public float value = -10f;

    [Tooltip("Duration in turns. Use -1 for a permanent shift.")]
    public int durationTurns = 20;

    [Tooltip("If non-empty, only governors who have AT LEAST ONE of these personalities are affected. " +
             "Leave empty to affect all governors.")]
    public PersonalityTrait[] requiresAnyPersonality;

    [Tooltip("If true, only applies to governors whose personal religion differs from the civ's state religion.")]
    public bool onlyIfReligionMismatch;

    [Tooltip("If true, only applies to governors whose personal culture differs from the civ's primary culture.")]
    public bool onlyIfCultureMismatch;

    [Tooltip("If true, only applies to governors who are NOT currently seated on the royal council.")]
    public bool onlyIfNotOnCouncil;

    /// <summary>
    /// Returns true if this effect should apply to the given governor given the owning civilization's context.
    /// </summary>
    public bool Matches(Governor gov, Civilization civ)
    {
        // Personality filter
        if (requiresAnyPersonality != null && requiresAnyPersonality.Length > 0)
        {
            bool hasAny = false;
            foreach (var p in requiresAnyPersonality)
            {
                if (gov.HasPersonality(p)) { hasAny = true; break; }
            }
            if (!hasAny) return false;
        }

        // Religion mismatch filter
        if (onlyIfReligionMismatch)
        {
            bool mismatched = (civ.StateReligion != null && gov.PersonalReligion != null &&
                               civ.StateReligion != gov.PersonalReligion);
            if (!mismatched) return false;
        }

        // Culture mismatch filter
        if (onlyIfCultureMismatch)
        {
            bool mismatched = (gov.PersonalCulture != null &&
                               !civ.researchedCultures.Contains(gov.PersonalCulture));
            if (!mismatched) return false;
        }

        // Council seat filter
        if (onlyIfNotOnCouncil && gov.IsOnCouncil) return false;

        return true;
    }
}
