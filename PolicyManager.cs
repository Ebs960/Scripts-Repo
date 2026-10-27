using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PolicyManager : MonoBehaviour
{
    public static PolicyManager Instance { get; private set; }

    public const int MaxActivePoliciesPerArea = 2;

    /// <summary>The 11 selectable policy areas in display order.</summary>
    public static readonly PolicyArea[] PolicyAreaOrder =
    {
        PolicyArea.Administration, PolicyArea.MilitarySecurity, PolicyArea.LawJustice, PolicyArea.CivilRights,
        PolicyArea.LaborSocialOrder, PolicyArea.EconomyTrade, PolicyArea.InfrastructureLand, PolicyArea.Education,
        PolicyArea.Religion, PolicyArea.WelfareHealth, PolicyArea.AdvancedTechnology,
    };

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
        => !PolicyConflictFree(civ, candidate, SupersededActive(civ, candidate), null, null);

    /// <summary>
    /// Full adoption evaluation with exact failure reasons, judged against the active set AFTER the planned
    /// supersessions and the optional explicit replacement. Side-effect free; AdoptPolicy enforces the same result.
    /// </summary>
    public PolicyAdoptionEvaluation EvaluatePolicy(Civilization civ, PolicyData policy, PolicyData replacement = null)
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
        if (e.alreadyActive) reasons.Add("This policy is already active.");

        e.activeAreaPolicies = GetActivePoliciesInArea(civ, policy.policyArea).Where(p => p != policy).ToList();
        e.supersededActivePolicies = SupersededActive(civ, policy);
        var remaining = e.activeAreaPolicies.Where(p => !e.supersededActivePolicies.Contains(p)).ToList();
        e.remainingCapacity = Mathf.Max(0, MaxActivePoliciesPerArea - remaining.Count);

        bool replacementValid = true;
        if (remaining.Count >= MaxActivePoliciesPerArea && !e.alreadyActive)
        {
            if (replacement == null) e.requiresReplacementSelection = true;
            else if (remaining.Contains(replacement)) e.selectedReplacement = replacement;
            else
            {
                replacementValid = false;
                reasons.Add($"{GovernmentPresentation.NameOf(replacement)} is not an active {GovernmentPresentation.PolicyAreaDisplayName(policy.policyArea)} policy that can be replaced.");
            }
        }

        var removals = new HashSet<PolicyData>(e.supersededActivePolicies);
        if (e.selectedReplacement != null) removals.Add(e.selectedReplacement);

        e.meetsTechRequirements = PolicyTechsMet(civ, policy, reasons);
        e.meetsCultureRequirements = PolicyCulturesMet(civ, policy, reasons);
        e.meetsGovernmentRequirement = PolicyGovernmentMet(civ, policy, reasons);
        e.meetsCityRequirement = PolicyCitiesMet(civ, policy, reasons);
        e.meetsReligionRequirement = PolicyReligionMet(civ, policy, reasons);
        e.meetsRequiredPolicies = RequiredPoliciesMetAfter(civ, policy, removals, reasons);
        e.hasConflict = !PolicyConflictFree(civ, policy, removals, e.conflictingActivePolicies, reasons);
        e.affordable = civ.policyPoints >= policy.policyPointCost;
        if (!e.affordable)
            reasons.Add($"Costs {policy.policyPointCost} policy points ({civ.policyPoints} available).");

        if (e.requiresReplacementSelection)
        {
            foreach (var choice in remaining)
                if (EvaluatePolicy(civ, policy, choice).canAdopt) e.replacementChoices.Add(choice);
            string area = GovernmentPresentation.PolicyAreaDisplayName(policy.policyArea);
            reasons.Add(e.replacementChoices.Count > 0
                ? $"{area} is full ({MaxActivePoliciesPerArea}/{MaxActivePoliciesPerArea}); choose a policy to replace."
                : $"{area} is full and no active policy there can be replaced without breaking requirements.");
        }

        e.canAdopt = !e.alreadyActive && e.MeetsStructuralRequirements && !e.hasConflict && e.affordable
            && !e.requiresReplacementSelection && replacementValid;
        return e;
    }

    /// <summary>
    /// Lowest-preference structurally valid replacement for a faction-requested policy (affordability is
    /// ignored, matching demand generation). Null when no replacement is needed or possible.
    /// </summary>
    public PolicyData ChooseReplacementForFaction(Civilization civ, PolicyData policy, FactionBloc faction, out bool possible)
    {
        var evaluation = EvaluatePolicy(civ, policy);
        possible = true;
        if (!evaluation.requiresReplacementSelection) return null;
        var choices = evaluation.activeAreaPolicies
            .Where(choice => !evaluation.supersededActivePolicies.Contains(choice))
            .Where(choice =>
            {
                var withChoice = EvaluatePolicy(civ, policy, choice);
                return withChoice.MeetsStructuralRequirements && !withChoice.hasConflict;
            })
            .OrderBy(choice => faction != null ? PoliticalPreferenceScorer.ScorePolicyForFaction(choice, faction, civ) : 0f)
            .ThenBy(GovernmentPresentation.NameOf)
            .ToList();
        possible = choices.Count > 0;
        return choices.FirstOrDefault();
    }

    /// <summary>Factions whose members favor a policy, i.e. those likely to be upset if it is replaced or repealed.</summary>
    public static List<FactionBloc> GetFactionsFavoringPolicy(Civilization civ, PolicyData policy, float minimumScore = 5f)
    {
        var result = new List<FactionBloc>();
        if (civ?.nobleFactions == null || policy == null) return result;
        foreach (var faction in civ.nobleFactions)
            if (faction != null && PoliticalPreferenceScorer.ScorePolicyForFaction(policy, faction, civ) >= minimumScore)
                result.Add(faction);
        return result;
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
                if (ongoing && required != null && civ.activePolicies != null
                    && civ.activePolicies.Exists(active => active != null && Contains(active.supersedesPolicies, required)))
                    continue;
                if (required == null || (civ.activePolicies != null && civ.activePolicies.Contains(required))) continue;
                if (reasons == null) return false;
                ok = false;
                reasons.Add($"Requires active policy: {GovernmentPresentation.NameOf(required)}.");
            }
        return ok;
    }

    private static List<PolicyData> SupersededActive(Civilization civ, PolicyData candidate)
    {
        var result = new List<PolicyData>();
        if (civ?.activePolicies == null || candidate?.supersedesPolicies == null) return result;
        foreach (var superseded in candidate.supersedesPolicies)
            if (superseded != null && superseded != candidate && civ.activePolicies.Contains(superseded) && !result.Contains(superseded))
                result.Add(superseded);
        return result;
    }

    /// <summary>Required-policy rules judged on the active set after the planned removals and the candidate's activation.</summary>
    private static bool RequiredPoliciesMetAfter(Civilization civ, PolicyData candidate, ICollection<PolicyData> removals, List<string> reasons)
    {
        var before = civ.activePolicies ?? new List<PolicyData>();
        var after = new HashSet<PolicyData>(before.Where(p => p != null && !removals.Contains(p))) { candidate };
        bool ok = true;

        if (candidate.requiredPolicies != null)
            foreach (var required in candidate.requiredPolicies)
            {
                if (required == null) continue;
                bool activeNow = before.Contains(required);
                // Upgrading out of a superseded prerequisite is the existing, explicitly permitted path.
                if (activeNow && (after.Contains(required) || Contains(candidate.supersedesPolicies, required))) continue;
                if (reasons == null) return false;
                ok = false;
                reasons.Add(activeNow
                    ? $"Requires {GovernmentPresentation.NameOf(required)} to remain active."
                    : $"Requires active policy: {GovernmentPresentation.NameOf(required)}.");
            }

        foreach (var removed in removals)
            foreach (var dependent in after)
            {
                if (dependent == null || dependent == candidate || !Contains(dependent.requiredPolicies, removed)) continue;
                // An active policy that supersedes the removed one is its explicit upgrade and keeps dependents valid.
                if (after.Any(active => active != null && Contains(active.supersedesPolicies, removed))) continue;
                if (reasons == null) return false;
                ok = false;
                reasons.Add($"Removing {GovernmentPresentation.NameOf(removed)} would break {GovernmentPresentation.NameOf(dependent)}, which requires it.");
            }
        return ok;
    }

    private static bool PolicyConflictFree(Civilization civ, PolicyData candidate, ICollection<PolicyData> removals, List<PolicyData> conflicts, List<string> reasons)
    {
        if (civ?.activePolicies == null || candidate == null) return true;
        bool ok = true;
        foreach (var active in civ.activePolicies)
        {
            if (active == null || active == candidate || (removals != null && removals.Contains(active))) continue;
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

    /// <summary>Entries from the most recent normalization, for inspection and tests.</summary>
    public readonly List<string> LastNormalizationReport = new List<string>();

    private void NormalizeLegacyPolicyAreas(Civilization civ)
    {
        LastNormalizationReport.Clear();

        // Revoking one duplicate entry reverses exactly one application of its bonuses.
        foreach (var duplicate in civ.activePolicies.Where(p => p != null).GroupBy(p => p)
                     .Where(g => g.Count() > 1).Select(g => g.Key).ToList())
            while (civ.activePolicies.Count(p => p == duplicate) > 1)
            {
                ReportNormalization(civ, $"Removed duplicate active entry for {GovernmentPresentation.NameOf(duplicate)}.");
                civ.RevokePolicy(duplicate);
            }

        foreach (var group in civ.activePolicies.Where(p => p != null && p.policyArea != PolicyArea.Unassigned)
                     .GroupBy(p => p.policyArea).Where(g => g.Count() > MaxActivePoliciesPerArea).ToList())
        {
            // Deterministic: keep policies other active policies depend on, then the most recently adopted.
            var ordered = group
                .OrderByDescending(p => civ.activePolicies.Any(other => other != null && other != p && Contains(other.requiredPolicies, p)))
                .ThenByDescending(p => civ.activePolicies.LastIndexOf(p))
                .ToList();
            var kept = ordered.Take(MaxActivePoliciesPerArea).ToList();
            foreach (var removed in ordered.Skip(MaxActivePoliciesPerArea))
            {
                ReportNormalization(civ, $"{GovernmentPresentation.PolicyAreaDisplayName(group.Key)} exceeded {MaxActivePoliciesPerArea} active policies; " +
                    $"kept {string.Join(" and ", kept.Select(GovernmentPresentation.NameOf))}, removed {GovernmentPresentation.NameOf(removed)}.");
                civ.RevokePolicy(removed);
            }
        }
    }

    private void ReportNormalization(Civilization civ, string message)
    {
        LastNormalizationReport.Add(message);
        Debug.LogWarning($"[PolicyManager] {civ.civData?.civName ?? civ.name}: {message}");
        if (civ.isPlayerControlled) UIManager.Instance?.ShowNotification(message);
    }

    /// <summary>All active policies in an area, in adoption order, without duplicates.</summary>
    public List<PolicyData> GetActivePoliciesInArea(Civilization civ, PolicyArea area)
    {
        var result = new List<PolicyData>();
        if (civ?.activePolicies == null || area == PolicyArea.Unassigned) return result;
        foreach (var policy in civ.activePolicies)
            if (policy != null && policy.policyArea == area && !result.Contains(policy)) result.Add(policy);
        return result;
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
    /// Pay policy points and adopt a policy, replacing <paramref name="replacement"/> when the area is full.
    /// Runs a Royal Council vote when the active government grants the council a vote on implicated domains.
    /// </summary>
    public bool AdoptPolicy(Civilization civ, PolicyData p, PolicyData replacement = null)
    {
        var evaluation = EvaluatePolicy(civ, p, replacement);
        if (!evaluation.canAdopt) return false;

        var voteResult = RunPolicyCouncilVote(civ, p, revocation: false);
        if (!voteResult.passed)
        {
            Debug.Log($"[PolicyManager] Policy '{p.policyName}' rejected by council vote " +
                      $"({voteResult.yesVotes}\u2013{voteResult.noVotes}).");
            return false;
        }

        // The approved switch is one atomic political action. Automatic removals neither vote nor refund.
        var planned = new List<PolicyData>(evaluation.supersededActivePolicies);
        if (evaluation.selectedReplacement != null && !planned.Contains(evaluation.selectedReplacement))
            planned.Add(evaluation.selectedReplacement);
        var removed = new List<PolicyData>();
        foreach (var policy in planned)
            if (civ.RevokePolicy(policy)) removed.Add(policy);

        // Adopt first, then charge, so a failed activation can never cost points; restore removals on failure.
        if (!civ.ActivatePolicyAfterValidation(p))
        {
            foreach (var policy in removed) civ.ActivatePolicyAfterValidation(policy);
            return false;
        }

        civ.policyPoints -= p.policyPointCost;
        ApplyGovernorPoliticalReactions(civ, p.governorOpinionEffects);
        if (civ.isPlayerControlled)
            UIManager.Instance?.ShowNotification(evaluation.selectedReplacement != null
                ? $"Policy adopted: {p.policyName} (replaced {evaluation.selectedReplacement.policyName})"
                : $"Policy adopted: {p.policyName}");
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
