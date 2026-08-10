using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Owns capture/restore of a governor's intrinsic character/political state for
/// save games (GovernorSaveData). City/herd assignment binding stays in
/// GameManager because it needs world context; PoliticalEventManager owns
/// council membership and faction composition by governor ID.
/// </summary>
public static class GovernorSaveUtility
{
    /// <summary>Capture a governor's full save record, including assignments.</summary>
    public static PauseMenuManager.GovernorSaveData Capture(Governor gov, Civilization civ)
    {
        if (gov == null) return null;

        var data = new PauseMenuManager.GovernorSaveData
        {
            id = gov.Id,
            name = gov.Name,
            specialization = gov.specialization,
            level = gov.Level,
            experience = gov.Experience,
            personalityTraits = new List<PersonalityTrait>(gov.PersonalityTraits ?? new List<PersonalityTrait>()),
            opinion = gov.Opinion,
            personalReligionName = gov.PersonalReligion != null ? gov.PersonalReligion.name : null,
            personalCultureName = gov.PersonalCulture != null ? gov.PersonalCulture.name : null,
            loyaltyFloor = gov.LoyaltyFloor,
            loyaltyCeiling = gov.LoyaltyCeiling,
            isCouncilEligible = gov.IsCouncilEligible,
            isInRebellion = gov.IsInRebellion,
            lastOpinionTickRound = gov.LastOpinionTickRound,
        };

        if (gov.OpinionModifiers != null)
        {
            foreach (var mod in gov.OpinionModifiers)
            {
                data.opinionModifiers.Add(new PauseMenuManager.GovernorOpinionModifierSaveData
                {
                    reason = mod.reason,
                    value = mod.value,
                    turnsRemaining = mod.turnsRemaining,
                });
            }
        }

        if (gov.Grievances != null)
        {
            foreach (var kv in gov.Grievances)
                data.grievances.Add(new PauseMenuManager.GovernorGrievanceSaveData { source = kv.Key, stacks = kv.Value });
        }

        if (gov.Traits != null)
        {
            foreach (var trait in gov.Traits)
                if (trait != null) data.traitNames.Add(trait.traitName);
        }

        if (gov.Cities != null && civ?.cities != null)
        {
            foreach (var city in gov.Cities)
            {
                if (city == null) continue;
                int idx = civ.cities.IndexOf(city);
                if (idx >= 0) data.assignedCityIndices.Add(idx);
            }
        }

        if (gov.Herds != null)
        {
            foreach (var herd in gov.Herds)
            {
                if (herd == null) continue;
                data.assignedHerdRefs.Add(new PauseMenuManager.HerdRef
                {
                    planetIndex = herd.planetIndex,
                    tileIndex = herd.currentTileIndex,
                });
            }
        }

        return data;
    }

    /// <summary>
    /// Apply saved intrinsic state (identity, progression, personality, opinion,
    /// modifiers, religion/culture, grievances, tick guard) plus traits onto an
    /// already-created governor. Does NOT bind city/herd assignments.
    /// </summary>
    public static void RestoreIntrinsicState(
        Governor gov,
        PauseMenuManager.GovernorSaveData data,
        IReadOnlyDictionary<string, GovernorTrait> traitLookup,
        IReadOnlyDictionary<string, ReligionData> religionLookup,
        IReadOnlyDictionary<string, CultureData> cultureLookup)
    {
        if (gov == null || data == null) return;

        var opinionModifiers = new List<OpinionModifier>();
        if (data.opinionModifiers != null)
        {
            foreach (var mod in data.opinionModifiers)
            {
                if (mod == null) continue;
                opinionModifiers.Add(new OpinionModifier(mod.reason, mod.value, mod.turnsRemaining));
            }
        }

        var grievances = new Dictionary<GrievanceSource, int>();
        if (data.grievances != null)
        {
            foreach (var grievance in data.grievances)
            {
                if (grievance == null) continue;
                grievances[grievance.source] = grievance.stacks;
            }
        }

        ReligionData religion = null;
        if (!string.IsNullOrEmpty(data.personalReligionName) && religionLookup != null)
            religionLookup.TryGetValue(data.personalReligionName, out religion);

        CultureData culture = null;
        if (!string.IsNullOrEmpty(data.personalCultureName) && cultureLookup != null)
            cultureLookup.TryGetValue(data.personalCultureName, out culture);

        gov.RestorePoliticalState(
            data.id,
            data.level,
            data.experience,
            data.personalityTraits,
            data.opinion,
            opinionModifiers,
            religion,
            culture,
            data.loyaltyFloor,
            data.loyaltyCeiling,
            grievances,
            data.isCouncilEligible,
            data.isInRebellion,
            data.lastOpinionTickRound);

        if (data.traitNames != null && traitLookup != null)
        {
            foreach (var traitName in data.traitNames)
            {
                if (string.IsNullOrWhiteSpace(traitName)) continue;
                if (traitLookup.TryGetValue(traitName, out var trait) && trait != null && !gov.Traits.Contains(trait))
                    gov.Traits.Add(trait);
            }
        }
    }

    /// <summary>Convenience: build a name-keyed lookup, ignoring null entries and duplicate keys.</summary>
    public static Dictionary<string, T> BuildLookup<T>(IEnumerable<T> assets, System.Func<T, string> keySelector) where T : UnityEngine.Object
    {
        var lookup = new Dictionary<string, T>();
        if (assets == null) return lookup;
        foreach (var asset in assets)
        {
            if (asset == null) continue;
            string key = keySelector(asset);
            if (string.IsNullOrEmpty(key) || lookup.ContainsKey(key)) continue;
            lookup[key] = asset;
        }
        return lookup;
    }
}
