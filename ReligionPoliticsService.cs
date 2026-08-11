using System;
using UnityEngine;

/// <summary>Single authority and balance surface for religion's political effects.</summary>
public static class ReligionPoliticsService
{
    public static event Action<Civilization, ReligionData, ReligionData, StateReligionChangeReason> OnStateReligionChanged;

    public static bool TrySetStateReligion(Civilization civ, ReligionData religion,
        StateReligionChangeReason reason, bool requirePoliticalApproval, out string failureReason)
    {
        failureReason = null;
        if (civ == null) { failureReason = "No civilization was supplied."; return false; }
        if (civ.StateReligion == religion) return true;
        if (requirePoliticalApproval)
        {
            var vote = CouncilVoteService.Evaluate(civ, new CouncilProposalContext {
                domains = VetoDomain.Religion,
                targetReligion = religion,
                religionProposalType = religion == null ? ReligionProposalType.RemoveStateReligion : ReligionProposalType.AdoptStateReligion,
                description = religion == null ? "Remove the state religion" : $"Adopt {religion.religionName} as the state religion"
            });
            CouncilVoteService.NotifyPlayer(civ, vote);
            if (!vote.passed) { failureReason = "The Royal Council rejected the religious change."; return false; }
        }
        var old = civ.StateReligion;
        civ.SetStateReligionFromAuthority(religion);
        PolicyManager.Instance?.RevalidateActivePolicies(civ);
        OnStateReligionChanged?.Invoke(civ, old, religion, reason);
        return true;
    }

    public static float GetTolerance01(Civilization civ) => civ == null ? 1f : civ.religiousTolerance switch {
        ReligionToleranceRule.FullTolerance => 1f, ReligionToleranceRule.LimitedTolerance => .65f,
        ReligionToleranceRule.StateReligionRequired => .25f, ReligionToleranceRule.ForcedConversion => 0f, _ => 1f };

    public static float GetGovernorReligionOpinionModifier(Governor governor, Civilization civ)
    {
        if (governor == null || civ == null || civ.StateReligion == null || governor.PersonalReligion == null) return 0f;
        float concern = governor.HasPersonality(PersonalityTrait.Cynical) ? .25f : 1f;
        if (governor.specialization == Governor.Specialization.Religious) concern *= 1.35f;
        if (governor.HasPersonality(PersonalityTrait.Zealous)) concern *= 1.75f;
        return governor.PersonalReligion == civ.StateReligion ? 6f * concern : -18f * (1f - GetTolerance01(civ)) * concern;
    }

    public static float GetCityReligionLoyaltyModifier(City city)
    {
        var civ = city?.owner;
        if (civ == null || civ.StateReligion == null || ReligionManager.Instance == null) return 0f;
        var majority = ReligionManager.Instance.GetCityMajorityReligion(city);
        if (majority == null || majority == civ.StateReligion) return 0f;
        // This modest political pressure is tolerance-derived; follower morale remains the detailed population cost.
        return -6f * (1f - GetTolerance01(civ));
    }
}
