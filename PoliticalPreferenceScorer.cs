using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized, deterministic scorer for how much a governor (or a whole faction)
/// likes a given policy or government. All preference weights live here so that
/// FactionBloc demand generation and council voting share one source of truth.
/// Positive scores mean "wants this adopted", negative means "opposes it".
/// </summary>
public static class PoliticalPreferenceScorer
{
    // ── Centralized weights ───────────────────────────────────────────────────
    /// <summary>Multiplier for GovernorOpinionEffect values that match the governor. Strongest signal.</summary>
    public const float OpinionEffectWeight = 1.5f;
    /// <summary>Multiplier converting fractional yield modifiers (e.g. 0.10) into preference points.</summary>
    public const float YieldModifierWeight = 100f;
    /// <summary>Weight applied to a specialization's favored yield/military modifiers.</summary>
    public const float SpecializationWeight = 1.0f;
    /// <summary>Extra weight for Greedy governors on gold effects and Zealous governors on faith effects.</summary>
    public const float PersonalityYieldWeight = 0.5f;
    /// <summary>Preference points per additional governor slot for Ambitious governors.</summary>
    public const float AmbitiousGovernorSlotWeight = 15f;
    /// <summary>Flat status-quo bias subtracted from any change for Content/Loyal governors.</summary>
    public const float StatusQuoBias = 5f;
    /// <summary>Points for religious-tolerance mechanics when the governor's personal religion mismatches the state religion.</summary>
    public const float ReligionToleranceWeight = 40f;
    /// <summary>Points an on-council governor loses for a government that abolishes the council.</summary>
    public const float LosesCouncilSeatPenalty = 40f;
    /// <summary>Points a council-eligible governor gains for a government that introduces a council.</summary>
    public const float GainsCouncilAccessBonus = 15f;

    // ── Policy scoring ────────────────────────────────────────────────────────

    /// <summary>
    /// How much this governor wants the given policy to be active.
    /// Deterministic; derived from opinion effects, policy mechanics,
    /// specialization, personality, and religious identity.
    /// </summary>
    public static float ScorePolicyForGovernor(PolicyData policy, Governor governor, Civilization civ)
    {
        if (policy == null || governor == null) return 0f;

        float score = 0f;

        // Strongest signal: authored opinion effects that would hit this governor.
        score += ScoreOpinionEffects(policy.governorOpinionEffects, governor, civ);

        // Specialization-aligned mechanics.
        score += ScoreYieldMechanics(
            governor,
            policy.attackBonus + policy.meleeAttackBonus + policy.rangedAttackBonus + policy.cityAttackBonus,
            policy.defenseBonus,
            policy.movementBonus,
            policy.goldModifier,
            policy.productionModifier,
            policy.scienceModifier,
            policy.cultureModifier,
            policy.faithModifier);

        // Religious tolerance mechanics matter strongly to religiously mismatched governors.
        score += ScoreReligiousTolerance(policy.nonStateReligionUnhappinessModifiers, governor, civ);

        // Ambitious governors favor anything that expands governor influence.
        if (governor.HasPersonality(PersonalityTrait.Ambitious) && policy.additionalGovernorSlots > 0)
            score += policy.additionalGovernorSlots * AmbitiousGovernorSlotWeight;

        // Content/Loyal governors carry a mild status-quo bias against any change.
        if (governor.HasPersonality(PersonalityTrait.Content) || governor.HasPersonality(PersonalityTrait.Loyal))
            score -= StatusQuoBias;

        return score;
    }

    /// <summary>
    /// How much this governor wants the given government to be the active government.
    /// </summary>
    public static float ScoreGovernmentForGovernor(GovernmentData government, Governor governor, Civilization civ)
    {
        if (government == null || governor == null) return 0f;

        float score = 0f;

        score += ScoreOpinionEffects(government.governorOpinionEffects, governor, civ);

        score += ScoreYieldMechanics(
            governor,
            government.attackBonus + government.meleeAttackBonus + government.rangedAttackBonus + government.cityAttackBonus,
            government.defenseBonus,
            government.movementBonus,
            government.goldModifier,
            government.productionModifier,
            government.scienceModifier,
            government.cultureModifier,
            government.faithModifier);

        score += ScoreReligiousTolerance(government.nonStateReligionUnhappinessModifiers, governor, civ);

        // Council structure: seated governors defend their institution; eligible
        // governors (especially Ambitious ones) favor governments that seat them.
        if (governor.IsOnCouncil && !government.usesRoyalCouncil)
            score -= LosesCouncilSeatPenalty;
        else if (!governor.IsOnCouncil && government.usesRoyalCouncil && governor.IsCouncilEligible)
        {
            score += GainsCouncilAccessBonus;
            if (governor.HasPersonality(PersonalityTrait.Ambitious))
                score += GainsCouncilAccessBonus;
        }

        if (governor.HasPersonality(PersonalityTrait.Content) || governor.HasPersonality(PersonalityTrait.Loyal))
            score -= StatusQuoBias;

        return score;
    }

    // ── Faction aggregation ───────────────────────────────────────────────────

    /// <summary>
    /// Faction preference = member preferences weighted by PowerRank, so governors
    /// ruling larger populations matter more. Returns 0 for empty factions.
    /// </summary>
    public static float ScorePolicyForFaction(PolicyData policy, FactionBloc faction, Civilization civ)
        => AggregateWeighted(faction, gov => ScorePolicyForGovernor(policy, gov, civ));

    /// <summary>
    /// Faction preference for a government, weighted by member PowerRank.
    /// </summary>
    public static float ScoreGovernmentForFaction(GovernmentData government, FactionBloc faction, Civilization civ)
        => AggregateWeighted(faction, gov => ScoreGovernmentForGovernor(government, gov, civ));

    private static float AggregateWeighted(FactionBloc faction, System.Func<Governor, float> scorer)
    {
        if (faction?.Members == null || faction.Members.Count == 0) return 0f;

        float weightedSum = 0f;
        float weightTotal = 0f;
        foreach (var member in faction.Members)
        {
            if (member == null) continue;
            float weight = Mathf.Max(1, member.PowerRank);
            weightedSum += scorer(member) * weight;
            weightTotal += weight;
        }

        return weightTotal > 0f ? weightedSum / weightTotal : 0f;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static float ScoreOpinionEffects(GovernorOpinionEffect[] effects, Governor governor, Civilization civ)
    {
        if (effects == null || effects.Length == 0 || civ == null) return 0f;

        float score = 0f;
        foreach (var effect in effects)
        {
            if (effect == null) continue;
            if (effect.Matches(governor, civ))
                score += effect.value * OpinionEffectWeight;
        }
        return score;
    }

    private static float ScoreYieldMechanics(
        Governor governor,
        float attack, float defense, float movement,
        float gold, float production, float science, float culture, float faith)
    {
        float score = 0f;

        switch (governor.specialization)
        {
            case Governor.Specialization.Military:
                score += (attack + defense + movement) * YieldModifierWeight * SpecializationWeight;
                break;
            case Governor.Specialization.Economic:
                score += gold * YieldModifierWeight * SpecializationWeight;
                break;
            case Governor.Specialization.Scientific:
                score += science * YieldModifierWeight * SpecializationWeight;
                break;
            case Governor.Specialization.Cultural:
                score += culture * YieldModifierWeight * SpecializationWeight;
                break;
            case Governor.Specialization.Religious:
                score += faith * YieldModifierWeight * SpecializationWeight;
                break;
            case Governor.Specialization.Industrial:
                score += production * YieldModifierWeight * SpecializationWeight;
                break;
        }

        if (governor.HasPersonality(PersonalityTrait.Greedy))
            score += gold * YieldModifierWeight * PersonalityYieldWeight;
        if (governor.HasPersonality(PersonalityTrait.Zealous))
            score += faith * YieldModifierWeight * PersonalityYieldWeight;
        if (governor.HasPersonality(PersonalityTrait.Brave))
            score += attack * YieldModifierWeight * PersonalityYieldWeight;

        return score;
    }

    private static float ScoreReligiousTolerance(NonStateReligionUnhappinessModifier[] modifiers, Governor governor, Civilization civ)
    {
        if (modifiers == null || modifiers.Length == 0 || governor == null || civ == null) return 0f;

        bool mismatched = civ.foundedReligion != null
            && governor.PersonalReligion != null
            && civ.foundedReligion != governor.PersonalReligion;
        if (!mismatched) return 0f;

        // Negative modifiers reduce the penalty on non-state believers = more tolerant.
        float tolerance = 0f;
        foreach (var mod in modifiers)
        {
            if (mod == null) continue;
            tolerance -= mod.unhappinessPerFollowerAdd;
            tolerance -= mod.unhappinessPct;
        }

        return Mathf.Clamp(tolerance, -1f, 1f) * ReligionToleranceWeight;
    }
}
