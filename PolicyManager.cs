using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PolicyManager : MonoBehaviour
{
    public static PolicyManager Instance { get; private set; }

    [Tooltip("All policies in the game")]
    public List<PolicyData> allPolicies = new List<PolicyData>();
    [Tooltip("All governments in the game")]
    public List<GovernmentData> allGovernments = new List<GovernmentData>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (allPolicies == null || !allPolicies.Exists(p => p != null))
            allPolicies = new List<PolicyData>(ResourceCache.GetAllPolicyData().Where(p => p != null));
        if (allGovernments == null || !allGovernments.Exists(g => g != null))
            allGovernments = new List<GovernmentData>(ResourceCache.GetAllGovernmentData().Where(g => g != null));
    }

    /// <summary>
    /// Structural prerequisites only (techs, cultures, government, city count, religion, and active policies).
    /// Deliberately excludes policy-point affordability so faction demand generation
    /// can target legal-but-unaffordable-right-now policies without duplicating requirement logic.
    /// </summary>
    public bool SatisfiesPolicyStructuralRequirements(Civilization civ, PolicyData p)
    {
        if (civ == null || p == null) return false;
        if (p.requiredTechs != null)
            foreach (var req in p.requiredTechs)
                if (req != null && !civ.researchedTechs.Contains(req)) return false;
        if (p.requiredCultures != null)
            foreach (var req in p.requiredCultures)
                if (req != null && !civ.researchedCultures.Contains(req)) return false;
        if (p.requiredGovernments != null)
        {
            bool hasRequirement = false, matched = false;
            foreach (var req in p.requiredGovernments)
            {
                if (req == null) continue;
                hasRequirement = true;
                if (civ.currentGovernment == req) matched = true;
            }
            if (hasRequirement && !matched) return false;
        }
        if (civ.cities == null || civ.cities.Count < p.requiredCityCount) return false;
        if (!SatisfiesReligiousRequirements(civ, p.religiousRequirementGroups)) return false;
        if (p.requiredPolicies != null)
            foreach (var required in p.requiredPolicies)
                if (required != null && (civ.activePolicies == null || !civ.activePolicies.Contains(required))) return false;
        return true;
    }

    public bool MeetsPolicyPrerequisites(Civilization civ, PolicyData p)
        => SatisfiesPolicyStructuralRequirements(civ, p)
           && (civ.activePolicies == null || !civ.activePolicies.Contains(p))
           && !HasActiveConflict(civ, p);

    public bool HasActiveConflict(Civilization civ, PolicyData candidate)
    {
        if (civ?.activePolicies == null || candidate == null) return false;
        foreach (var active in civ.activePolicies)
            if (active != null && (Contains(candidate.incompatiblePolicies, active)
                || Contains(active.incompatiblePolicies, candidate))) return true;
        return false;
    }

    private static bool SatisfiesReligiousRequirements(Civilization civ, PolicyReligiousRequirementGroup[] groups)
    {
        if (groups == null || groups.Length == 0) return true;
        foreach (var group in groups)
            if (group != null && SatisfiesReligiousGroup(civ, group)) return true;
        return false;
    }

    private static bool SatisfiesReligiousGroup(Civilization civ, PolicyReligiousRequirementGroup group)
    {
        if (group.requiresStateReligion && civ.StateReligion == null) return false;
        if (HasNonNull(group.anyStateReligions) && !Contains(group.anyStateReligions, civ.StateReligion)) return false;
        if (HasNonNull(group.anyPantheons))
        {
            bool matched = false;
            if (civ.foundedPantheons != null)
                foreach (var owned in civ.foundedPantheons)
                    foreach (var required in group.anyPantheons)
                        if (required != null && PantheonMatches(required, owned, group.allowPantheonUpgradeDescendants)) matched = true;
            if (!matched) return false;
        }
        if (group.useMinimumPantheonTier)
        {
            bool matched = civ.foundedPantheons != null && civ.foundedPantheons.Exists(
                pantheon => pantheon != null && pantheon.tier >= group.minimumPantheonTier);
            if (!matched) return false;
        }
        if (HasNonNull(group.anyBeliefs) || (group.anyBeliefCategories != null && group.anyBeliefCategories.Length > 0))
        {
            bool specificMatched = !HasNonNull(group.anyBeliefs);
            bool categoryMatched = group.anyBeliefCategories == null || group.anyBeliefCategories.Length == 0;
            foreach (var belief in civ.EnumerateActiveBeliefs())
            {
                if (Contains(group.anyBeliefs, belief)) specificMatched = true;
                if (belief != null && group.anyBeliefCategories != null
                    && System.Array.IndexOf(group.anyBeliefCategories, belief.category) >= 0) categoryMatched = true;
            }
            if (!specificMatched || !categoryMatched) return false;
        }
        return true;
    }

    private static bool PantheonMatches(PantheonData required, PantheonData owned, bool descendants)
    {
        if (required == null || owned == null) return false;
        // The content model currently has two tiers. The guard also makes malformed
        // cyclic upgrade data harmless instead of hanging prerequisite evaluation.
        int remainingUpgradeLinks = 32;
        for (var current = required; current != null && remainingUpgradeLinks-- > 0;
             current = descendants ? current.upgradedPantheon : null)
        {
            if (current == owned) return true;
            if (!descendants) break;
        }
        return false;
    }

    private static bool HasNonNull<T>(T[] values) where T : Object
    { if (values == null) return false; foreach (var value in values) if (value != null) return true; return false; }
    private static bool Contains<T>(T[] values, T target) where T : Object
    { if (target == null || values == null) return false; foreach (var value in values) if (value == target) return true; return false; }

    public void RevalidateActivePolicies(Civilization civ)
    {
        if (civ?.activePolicies == null) return;
        // Repeat because removing one prerequisite can invalidate a policy that was
        // visited earlier in the list. This is event-driven, never a per-frame search.
        bool removed;
        do
        {
            removed = false;
            for (int i = civ.activePolicies.Count - 1; i >= 0; i--)
            {
                var policy = civ.activePolicies[i];
                if (SatisfiesPolicyStructuralRequirements(civ, policy)) continue;
                Debug.Log($"[PolicyManager] Automatically revoking '{policy?.policyName ?? "<missing policy>"}': structural prerequisites are no longer met.");
                civ.RevokePolicy(policy); // Structural revocation deliberately bypasses council and refunds.
                removed = true;
            }
        } while (removed);
    }

    /// <summary>All policies whose structural prerequisites are met, regardless of current policy points.</summary>
    public List<PolicyData> GetStructurallyAvailablePolicies(Civilization civ)
    {
        var avail = new List<PolicyData>();
        if (civ == null) return avail;
        foreach (var p in allPolicies)
            if (p != null && MeetsPolicyPrerequisites(civ, p)) avail.Add(p);
        return avail;
    }

    /// <summary>
    /// Which policies can the civ adopt right now? (prerequisites + affordability)
    /// </summary>
    public List<PolicyData> GetAvailablePolicies(Civilization civ)
    {
        var avail = new List<PolicyData>();
        foreach (var p in allPolicies)
        {
            if (p == null) continue;
            if (civ.policyPoints < p.policyPointCost) continue;
            if (MeetsPolicyPrerequisites(civ, p)) avail.Add(p);
        }
        return avail;
    }

    /// <summary>
    /// Pay policy points and adopt a policy. Runs a Royal Council vote when the
    /// active government grants the council a vote on implicated domains.
    /// </summary>
    public bool AdoptPolicy(Civilization civ, PolicyData p)
    {
        if (!GetAvailablePolicies(civ).Contains(p)) return false;

        var voteResult = RunPolicyCouncilVote(civ, p, revocation: false);
        if (!voteResult.passed)
        {
            Debug.Log($"[PolicyManager] Policy '{p.policyName}' rejected by council vote " +
                      $"({voteResult.yesVotes}\u2013{voteResult.noVotes}).");
            return false;
        }

        // Supersession occurs only after approval, without another vote or refund.
        if (p.supersedesPolicies != null)
            foreach (var superseded in p.supersedesPolicies)
                if (superseded != null && civ.activePolicies != null && civ.activePolicies.Contains(superseded))
                    civ.RevokePolicy(superseded);

        // Adopt first, then charge: Civilization.AdoptPolicy re-validates availability,
        // so deducting points up-front could silently charge without adopting.
        civ.AdoptPolicy(p);
        if (civ.activePolicies == null || !civ.activePolicies.Contains(p)) return false;

        civ.policyPoints -= p.policyPointCost;
        ApplyGovernorPoliticalReactions(civ, p.governorOpinionEffects);
        if (civ.isPlayerControlled)
            UIManager.Instance?.ShowNotification($"Policy adopted: {p.policyName}");
        return true;
    }

    /// <summary>
    /// Revoke an active policy, reversing its effects. Runs a Royal Council vote
    /// when the government grants the council a vote on policy changes.
    /// </summary>
    public bool RevokePolicy(Civilization civ, PolicyData p)
    {
        if (civ == null || p == null) return false;
        if (civ.activePolicies == null || !civ.activePolicies.Contains(p)) return false;

        var voteResult = RunPolicyCouncilVote(civ, p, revocation: true);
        if (!voteResult.passed)
        {
            Debug.Log($"[PolicyManager] Revocation of '{p.policyName}' rejected by council vote " +
                      $"({voteResult.yesVotes}\u2013{voteResult.noVotes}).");
            return false;
        }

        return civ.RevokePolicy(p);
    }

    private static CouncilVoteResult RunPolicyCouncilVote(Civilization civ, PolicyData p, bool revocation)
    {
        // Domains implicated by this policy's mechanics.
        var domains = VetoDomain.PolicyChange | p.additionalVetoDomains;

        var result = CouncilVoteService.Evaluate(civ, new CouncilProposalContext
        {
            domains = domains,
            targetPolicy = p,
            policyIsRevocation = revocation,
            numericContext = -p.goldModifier,
            description = revocation ? $"Repeal {p.policyName}" : $"Adopt {p.policyName}",
        });
        CouncilVoteService.NotifyPlayer(civ, result);
        return result;
    }

    /// <summary>
    /// Structural prerequisites for a government (unlocked, techs, cultures, city count,
    /// state religion, vassal count) excluding policy-point affordability.
    /// </summary>
    public bool MeetsGovernmentPrerequisites(Civilization civ, GovernmentData g)
    {
        if (civ == null || g == null) return false;
        if (civ.unlockedGovernments == null || !civ.unlockedGovernments.Contains(g)) return false;
        if (civ.currentGovernment == g) return false;
        if (g.requiredTechs != null)
            foreach (var req in g.requiredTechs)
                if (req != null && !civ.researchedTechs.Contains(req)) return false;
        if (g.requiredCultures != null)
            foreach (var req in g.requiredCultures)
                if (req != null && !civ.researchedCultures.Contains(req)) return false;
        if (civ.cities == null || civ.cities.Count < g.requiredCityCount) return false;
        if (g.requiresStateReligion && civ.StateReligion == null) return false;
        if (g.requiredVassalCount > 0 && civ.ActiveVassalCount < g.requiredVassalCount) return false;
        return true;
    }

    /// <summary>All governments whose structural prerequisites are met, regardless of current policy points.</summary>
    public List<GovernmentData> GetStructurallyAvailableGovernments(Civilization civ)
    {
        var avail = new List<GovernmentData>();
        if (civ == null || civ.unlockedGovernments == null) return avail;
        foreach (var g in civ.unlockedGovernments)
            if (g != null && MeetsGovernmentPrerequisites(civ, g)) avail.Add(g);
        return avail;
    }

    /// <summary>
    /// Which governments can the civ switch to right now? (prerequisites + affordability)
    /// </summary>
    public List<GovernmentData> GetAvailableGovernments(Civilization civ)
    {
        var avail = new List<GovernmentData>();
        if (civ == null || civ.unlockedGovernments == null) return avail;

        foreach (var g in civ.unlockedGovernments)
        {
            if (g == null) continue;
            if (civ.policyPoints < g.policyPointCost) continue;
            if (MeetsGovernmentPrerequisites(civ, g)) avail.Add(g);
        }
        return avail;
    }

    /// <summary>
    /// Switch government, unlocking new policies. Runs a Royal Council vote when
    /// the current government's council may vote on succession/government change.
    /// </summary>
    public bool ChangeGovernment(Civilization civ, GovernmentData g)
    {
        if (!GetAvailableGovernments(civ).Contains(g)) return false;

        var voteResult = CouncilVoteService.Evaluate(civ, new CouncilProposalContext
        {
            domains = VetoDomain.Succession | VetoDomain.GovernmentChange,
            targetGovernment = g,
            description = $"Adopt {g.governmentName}",
        });
        CouncilVoteService.NotifyPlayer(civ, voteResult);
        if (!voteResult.passed)
        {
            Debug.Log($"[PolicyManager] Government change to '{g.governmentName}' rejected by council vote " +
                      $"({voteResult.yesVotes}\u2013{voteResult.noVotes}).");
            return false;
        }

        // Change first, then charge: Civilization.ChangeGovernment re-validates availability.
        civ.ChangeGovernment(g);
        if (civ.currentGovernment != g) return false;

        civ.policyPoints -= g.policyPointCost;
        ApplyGovernorPoliticalReactions(civ, g.governorOpinionEffects);
        if (civ.isPlayerControlled)
            UIManager.Instance?.ShowNotification($"Government changed to {g.governmentName}");
        return true;
    }

    /// <summary>
    /// Push governor opinion reactions for every effect in the array.
    /// Filters by personality, religion mismatch, culture mismatch, and council state.
    /// Call this after a government change or policy adoption.
    /// </summary>
    public void ApplyGovernorPoliticalReactions(Civilization civ, GovernorOpinionEffect[] effects)
    {
        if (effects == null || effects.Length == 0 || civ?.governors == null) return;
        foreach (var effect in effects)
        {
            if (effect == null) continue;
            foreach (var gov in civ.governors)
            {
                if (gov == null) continue;
                if (effect.Matches(gov, civ))
                    gov.AddOpinionModifier(effect.reason, effect.value, effect.durationTurns);
            }
        }
    }
}
