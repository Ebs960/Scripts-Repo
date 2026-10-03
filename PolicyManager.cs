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
        return PolicyTechsMet(civ, p, null)
            && PolicyCulturesMet(civ, p, null)
            && PolicyGovernmentMet(civ, p, null)
            && PolicyCitiesMet(civ, p, null)
            && PolicyReligionMet(civ, p, null)
            && PolicyRequiredPoliciesMet(civ, p, null, ongoing: true);
    }

    public bool MeetsPolicyPrerequisites(Civilization civ, PolicyData p)
        => civ != null && p != null
           && PolicyTechsMet(civ, p, null)
           && PolicyCulturesMet(civ, p, null)
           && PolicyGovernmentMet(civ, p, null)
           && PolicyCitiesMet(civ, p, null)
           && PolicyReligionMet(civ, p, null)
           && PolicyRequiredPoliciesMet(civ, p, null, ongoing: false)
           && (civ.activePolicies == null || !civ.activePolicies.Contains(p))
           && !HasActiveConflict(civ, p);

    public bool HasActiveConflict(Civilization civ, PolicyData candidate)
        => !PolicyConflictFree(civ, candidate, GetActivePolicyInArea(civ, candidate != null ? candidate.policyArea : PolicyArea.Unassigned, candidate), null, null);

    /// <summary>
    /// Full adoption evaluation with exact failure reasons. Built from the same rule helpers as
    /// MeetsPolicyPrerequisites/GetAvailablePolicies/AdoptPolicy, so UI and gameplay cannot drift.
    /// </summary>
    public PolicyAdoptionEvaluation EvaluatePolicy(Civilization civ, PolicyData policy)
    {
        var e = new PolicyAdoptionEvaluation { policy = policy, policyArea = policy != null ? policy.policyArea : PolicyArea.Unassigned };
        if (civ == null || policy == null)
        {
            e.failureReasons.Add("No policy selected.");
            return e;
        }

        var reasons = e.failureReasons;
        e.policyPointCost = policy.policyPointCost;
        e.currentPolicyPoints = civ.policyPoints;
        e.alreadyActive = civ.activePolicies != null && civ.activePolicies.Contains(policy);
        e.replacedAreaPolicy = GetActivePolicyInArea(civ, policy.policyArea, policy);
        if (e.alreadyActive) reasons.Add("This policy is already active.");

        e.meetsTechRequirements = PolicyTechsMet(civ, policy, reasons);
        e.meetsCultureRequirements = PolicyCulturesMet(civ, policy, reasons);
        e.meetsGovernmentRequirement = PolicyGovernmentMet(civ, policy, reasons);
        e.meetsCityRequirement = PolicyCitiesMet(civ, policy, reasons);
        e.meetsReligionRequirement = PolicyReligionMet(civ, policy, reasons);
        e.meetsRequiredPolicies = PolicyRequiredPoliciesMet(civ, policy, reasons, ongoing: false);
        e.hasConflict = !PolicyConflictFree(civ, policy, e.replacedAreaPolicy, e.conflictingActivePolicies, reasons);
        e.affordable = civ.policyPoints >= policy.policyPointCost;
        if (!e.affordable)
            reasons.Add($"Costs {policy.policyPointCost} policy points ({civ.policyPoints} available).");

        e.canAdopt = !e.alreadyActive && e.MeetsStructuralRequirements && !e.hasConflict && e.affordable;
        return e;
    }

    // Rule helpers: a null `reasons` list means boolean-only (short-circuits, builds no strings).

    private static bool PolicyTechsMet(Civilization civ, PolicyData p, List<string> reasons)
    {
        bool ok = true;
        if (p.requiredTechs != null)
            foreach (var req in p.requiredTechs)
            {
                if (req == null || civ.researchedTechs.Contains(req)) continue;
                if (reasons == null) return false;
                ok = false;
                reasons.Add($"Requires technology: {GovernmentPresentation.NameOf(req)}.");
            }
        return ok;
    }

    private static bool PolicyCulturesMet(Civilization civ, PolicyData p, List<string> reasons)
    {
        bool ok = true;
        if (p.requiredCultures != null)
            foreach (var req in p.requiredCultures)
            {
                if (req == null || civ.researchedCultures.Contains(req)) continue;
                if (reasons == null) return false;
                ok = false;
                reasons.Add($"Requires culture: {GovernmentPresentation.NameOf(req)}.");
            }
        return ok;
    }

    private static bool PolicyGovernmentMet(Civilization civ, PolicyData p, List<string> reasons)
    {
        if (p.requiredGovernments == null) return true;
        bool hasRequirement = false, matched = false;
        List<string> names = reasons != null ? new List<string>() : null;
        foreach (var req in p.requiredGovernments)
        {
            if (req == null) continue;
            hasRequirement = true;
            names?.Add(GovernmentPresentation.NameOf(req));
            if (civ.currentGovernment == req) matched = true;
        }
        if (!hasRequirement || matched) return true;
        reasons?.Add($"Requires one of these governments: {string.Join(", ", names)}.");
        return false;
    }

    private static bool PolicyCitiesMet(Civilization civ, PolicyData p, List<string> reasons)
    {
        if (civ.cities != null && civ.cities.Count >= p.requiredCityCount) return true;
        reasons?.Add($"Requires {p.requiredCityCount} cities ({civ.cities?.Count ?? 0} owned).");
        return false;
    }

    private static bool PolicyReligionMet(Civilization civ, PolicyData p, List<string> reasons)
    {
        if (SatisfiesReligiousRequirements(civ, p.religiousRequirementGroups)) return true;
        reasons?.Add($"Requires a religious condition: {DescribeReligiousRequirements(p.religiousRequirementGroups)}.");
        return false;
    }

    private static bool PolicyRequiredPoliciesMet(Civilization civ, PolicyData p, List<string> reasons, bool ongoing)
    {
        bool ok = true;
        if (p.requiredPolicies != null)
            foreach (var required in p.requiredPolicies)
            {
                if (ongoing && required != null && required.policyArea == p.policyArea && Contains(p.supersedesPolicies, required))
                    continue;
                if (required == null || (civ.activePolicies != null && civ.activePolicies.Contains(required))) continue;
                if (reasons == null) return false;
                ok = false;
                reasons.Add($"Requires active policy: {GovernmentPresentation.NameOf(required)}.");
            }
        return ok;
    }

    private static bool PolicyConflictFree(Civilization civ, PolicyData candidate, PolicyData replacing, List<PolicyData> conflicts, List<string> reasons)
    {
        if (civ?.activePolicies == null || candidate == null) return true;
        bool ok = true;
        foreach (var active in civ.activePolicies)
        {
            if (active == null || active == candidate || active == replacing) continue;
            if (!Contains(candidate.incompatiblePolicies, active) && !Contains(active.incompatiblePolicies, candidate)) continue;
            if (reasons == null && conflicts == null) return false;
            ok = false;
            conflicts?.Add(active);
            reasons?.Add($"Conflicts with active policy: {GovernmentPresentation.NameOf(active)}.");
        }
        return ok;
    }

    private static string DescribeReligiousRequirements(PolicyReligiousRequirementGroup[] groups)
    {
        if (groups == null || groups.Length == 0) return "none";
        var routes = new List<string>();
        foreach (var group in groups)
        {
            if (group == null) continue;
            var parts = new List<string>();
            if (group.requiresStateReligion) parts.Add("a state religion");
            if (HasNonNull(group.anyStateReligions))
                parts.Add("state religion " + string.Join("/", System.Array.ConvertAll(
                    System.Array.FindAll(group.anyStateReligions, r => r != null), r => string.IsNullOrWhiteSpace(r.religionName) ? r.name : r.religionName)));
            if (HasNonNull(group.anyPantheons))
                parts.Add("pantheon " + string.Join("/", System.Array.ConvertAll(
                    System.Array.FindAll(group.anyPantheons, x => x != null), x => string.IsNullOrWhiteSpace(x.pantheonName) ? x.name : x.pantheonName)));
            if (group.useMinimumPantheonTier) parts.Add($"a pantheon of tier {group.minimumPantheonTier} or higher");
            if (HasNonNull(group.anyBeliefs))
                parts.Add("belief " + string.Join("/", System.Array.ConvertAll(
                    System.Array.FindAll(group.anyBeliefs, x => x != null), x => string.IsNullOrWhiteSpace(x.beliefName) ? x.name : x.beliefName)));
            if (group.anyBeliefCategories != null && group.anyBeliefCategories.Length > 0)
                parts.Add("a belief of category " + string.Join("/", group.anyBeliefCategories));
            if (parts.Count > 0) routes.Add(string.Join(" and ", parts));
        }
        return routes.Count == 0 ? "none" : string.Join(" or ", routes);
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
        NormalizeLegacyPolicyAreas(civ);
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

    private void NormalizeLegacyPolicyAreas(Civilization civ)
    {
        var newestByArea = new Dictionary<PolicyArea, PolicyData>();
        for (int i = civ.activePolicies.Count - 1; i >= 0; i--)
        {
            var policy = civ.activePolicies[i];
            if (policy == null || policy.policyArea == PolicyArea.Unassigned) continue;
            if (!newestByArea.TryGetValue(policy.policyArea, out var newest))
            {
                newestByArea.Add(policy.policyArea, policy);
                continue;
            }
            Debug.Log($"[PolicyManager] Legacy save contained multiple policies in {GovernmentPresentation.PolicyAreaDisplayName(policy.policyArea)}; keeping {GovernmentPresentation.NameOf(newest)} and removing {GovernmentPresentation.NameOf(policy)}.");
            civ.RevokePolicy(policy);
        }
    }

    public PolicyData GetActivePolicyInArea(Civilization civ, PolicyArea area)
        => GetActivePolicyInArea(civ, area, null);

    private static PolicyData GetActivePolicyInArea(Civilization civ, PolicyArea area, PolicyData excluding)
    {
        if (civ?.activePolicies == null || area == PolicyArea.Unassigned) return null;
        for (int i = civ.activePolicies.Count - 1; i >= 0; i--)
        {
            var policy = civ.activePolicies[i];
            if (policy != null && policy != excluding && policy.policyArea == area) return policy;
        }
        return null;
    }

    public List<PolicyData> GetPoliciesInArea(PolicyArea area)
        => allPolicies == null ? new List<PolicyData>() : allPolicies.Where(p => p != null && p.policyArea == area).Distinct().ToList();

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
        var evaluation = EvaluatePolicy(civ, p);
        if (!evaluation.canAdopt) return false;

        var voteResult = RunPolicyCouncilVote(civ, p, revocation: false);
        if (!voteResult.passed)
        {
            Debug.Log($"[PolicyManager] Policy '{p.policyName}' rejected by council vote " +
                      $"({voteResult.yesVotes}\u2013{voteResult.noVotes}).");
            return false;
        }

        // The approved switch is one atomic political action. Automatic removals neither vote nor refund.
        if (evaluation.replacedAreaPolicy != null)
            civ.RevokePolicy(evaluation.replacedAreaPolicy);
        if (p.supersedesPolicies != null)
            foreach (var superseded in p.supersedesPolicies)
                if (superseded != null && civ.activePolicies != null && civ.activePolicies.Contains(superseded))
                    civ.RevokePolicy(superseded);

        // Adopt first, then charge: Civilization.AdoptPolicy re-validates availability,
        // so deducting points up-front could silently charge without adopting.
        if (!civ.ActivatePolicyAfterValidation(p)) return false;

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
        var result = CouncilVoteService.EvaluateAndRecord(civ, BuildPolicyCouncilProposal(p, revocation));
        CouncilVoteService.NotifyPlayer(civ, result);
        return result;
    }

    /// <summary>The exact proposal a real policy adoption/repeal puts to the council; previews must use it too.</summary>
    public static CouncilProposalContext BuildPolicyCouncilProposal(PolicyData p, bool revocation)
    {
        return new CouncilProposalContext
        {
            // Domains implicated by this policy's mechanics.
            domains = VetoDomain.PolicyChange | p.additionalVetoDomains,
            targetPolicy = p,
            policyIsRevocation = revocation,
            numericContext = -p.goldModifier,
            description = revocation ? $"Repeal {p.policyName}" : $"Adopt {p.policyName}",
        };
    }

    /// <summary>The exact proposal a real government change puts to the council; previews must use it too.</summary>
    public static CouncilProposalContext BuildGovernmentCouncilProposal(GovernmentData g)
    {
        return new CouncilProposalContext
        {
            domains = VetoDomain.Succession | VetoDomain.GovernmentChange,
            targetGovernment = g,
            description = $"Adopt {g.governmentName}",
        };
    }

    /// <summary>Side-effect-free council outlook for adopting or repealing a policy.</summary>
    public CouncilVoteResult PreviewPolicyVote(Civilization civ, PolicyData p, bool revocation)
        => p == null ? null : CouncilVoteService.Preview(civ, BuildPolicyCouncilProposal(p, revocation));

    /// <summary>Side-effect-free council outlook for changing government.</summary>
    public CouncilVoteResult PreviewGovernmentVote(Civilization civ, GovernmentData g)
        => g == null ? null : CouncilVoteService.Preview(civ, BuildGovernmentCouncilProposal(g));

    /// <summary>
    /// Structural prerequisites for a government (unlocked, techs, cultures, city count,
    /// state religion, vassal count) excluding policy-point affordability.
    /// </summary>
    public bool MeetsGovernmentPrerequisites(Civilization civ, GovernmentData g)
    {
        if (civ == null || g == null) return false;
        if (!IsGovernmentUnlocked(civ, g)) return false;
        if (civ.currentGovernment == g) return false;
        return GovernmentTechsMet(civ, g, null)
            && GovernmentCulturesMet(civ, g, null)
            && GovernmentCitiesMet(civ, g, null)
            && GovernmentReligionMet(civ, g, null)
            && GovernmentVassalsMet(civ, g, null);
    }

    /// <summary>
    /// Full adoption evaluation with exact failure reasons. Built from the same rule helpers as
    /// MeetsGovernmentPrerequisites/GetAvailableGovernments/ChangeGovernment, so UI and gameplay cannot drift.
    /// </summary>
    public GovernmentAdoptionEvaluation EvaluateGovernment(Civilization civ, GovernmentData government)
    {
        var e = new GovernmentAdoptionEvaluation { government = government };
        if (civ == null || government == null)
        {
            e.failureReasons.Add("No government selected.");
            return e;
        }

        var reasons = e.failureReasons;
        e.policyPointCost = government.policyPointCost;
        e.currentPolicyPoints = civ.policyPoints;
        e.isCurrentGovernment = civ.currentGovernment == government;
        e.unlocked = IsGovernmentUnlocked(civ, government);
        if (e.isCurrentGovernment) reasons.Add("This is your current government.");

        e.meetsTechRequirements = GovernmentTechsMet(civ, government, reasons);
        e.meetsCultureRequirements = GovernmentCulturesMet(civ, government, reasons);
        e.meetsCityRequirement = GovernmentCitiesMet(civ, government, reasons);
        e.meetsReligionRequirement = GovernmentReligionMet(civ, government, reasons);
        e.meetsVassalRequirement = GovernmentVassalsMet(civ, government, reasons);
        e.affordable = civ.policyPoints >= government.policyPointCost;
        if (!e.affordable)
            reasons.Add($"Costs {government.policyPointCost} policy points ({civ.policyPoints} available).");

        e.canAdopt = e.MeetsStructuralPrerequisites && e.affordable;
        return e;
    }

    /// <summary>A government is unlocked once its own requiredTechs and requiredCultures are all met.</summary>
    public bool IsGovernmentUnlocked(Civilization civ, GovernmentData g)
        => civ != null && g != null && GovernmentTechsMet(civ, g, null) && GovernmentCulturesMet(civ, g, null);

    public List<GovernmentData> GetUnlockedGovernments(Civilization civ)
    {
        var unlocked = new List<GovernmentData>();
        if (civ == null || allGovernments == null) return unlocked;
        foreach (var g in allGovernments)
            if (IsGovernmentUnlocked(civ, g)) unlocked.Add(g);
        return unlocked;
    }

    private static bool GovernmentTechsMet(Civilization civ, GovernmentData g, List<string> reasons)
    {
        bool ok = true;
        if (g.requiredTechs != null)
            foreach (var req in g.requiredTechs)
            {
                if (req == null || civ.researchedTechs.Contains(req)) continue;
                if (reasons == null) return false;
                ok = false;
                reasons.Add($"Requires technology: {GovernmentPresentation.NameOf(req)}.");
            }
        return ok;
    }

    private static bool GovernmentCulturesMet(Civilization civ, GovernmentData g, List<string> reasons)
    {
        bool ok = true;
        if (g.requiredCultures != null)
            foreach (var req in g.requiredCultures)
            {
                if (req == null || civ.researchedCultures.Contains(req)) continue;
                if (reasons == null) return false;
                ok = false;
                reasons.Add($"Requires culture: {GovernmentPresentation.NameOf(req)}.");
            }
        return ok;
    }

    private static bool GovernmentCitiesMet(Civilization civ, GovernmentData g, List<string> reasons)
    {
        if (civ.cities != null && civ.cities.Count >= g.requiredCityCount) return true;
        reasons?.Add($"Requires {g.requiredCityCount} cities ({civ.cities?.Count ?? 0} owned).");
        return false;
    }

    private static bool GovernmentReligionMet(Civilization civ, GovernmentData g, List<string> reasons)
    {
        if (!g.requiresStateReligion || civ.StateReligion != null) return true;
        reasons?.Add("Requires a state religion.");
        return false;
    }

    private static bool GovernmentVassalsMet(Civilization civ, GovernmentData g, List<string> reasons)
    {
        if (g.requiredVassalCount <= 0 || civ.ActiveVassalCount >= g.requiredVassalCount) return true;
        reasons?.Add($"Requires {g.requiredVassalCount} vassals ({civ.ActiveVassalCount} held).");
        return false;
    }

    /// <summary>All governments whose structural prerequisites are met, regardless of current policy points.</summary>
    public List<GovernmentData> GetStructurallyAvailableGovernments(Civilization civ)
    {
        var avail = new List<GovernmentData>();
        if (civ == null) return avail;
        foreach (var g in GetUnlockedGovernments(civ))
            if (MeetsGovernmentPrerequisites(civ, g)) avail.Add(g);
        return avail;
    }

    /// <summary>
    /// Which governments can the civ switch to right now? (prerequisites + affordability)
    /// </summary>
    public List<GovernmentData> GetAvailableGovernments(Civilization civ)
    {
        var avail = new List<GovernmentData>();
        if (civ == null) return avail;

        foreach (var g in GetUnlockedGovernments(civ))
        {
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

        var voteResult = CouncilVoteService.EvaluateAndRecord(civ, BuildGovernmentCouncilProposal(g));
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
