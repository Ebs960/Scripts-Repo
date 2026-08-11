using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A formal demand issued by a noble faction to the player.
/// The player must Accept, Refuse, or attempt to Negotiate.
/// Refusal adds grievances and can escalate to rebellion.
/// </summary>
[System.Serializable]
public class FactionDemand
{
    public FactionDemandType type;
    public string description;
    /// <summary>Policy to revoke or adopt, when relevant.</summary>
    public PolicyData targetPolicy;
    /// <summary>Government type to switch to, when relevant.</summary>
    public GovernmentData targetGovernment;
    /// <summary>Governor whose city should be returned, when relevant.</summary>
    public Governor targetGovernor;
    /// <summary>Concrete faith targeted by religious demands; never null for adoption.</summary>
    public ReligionData targetReligion;
    /// <summary>Turn on which this demand was issued (for expiry/escalation).</summary>
    public int issuedTurn;
    /// <summary>After this many turns of no response the faction escalates.</summary>
    public int expiryTurns = 5;
}

/// <summary>
/// A coalition of governors united around a shared political grievance.
/// Factions form when multiple governors share an alignment and have high enough combined anger.
/// They issue coordinated demands and can trigger multi-city rebellions if refused.
/// </summary>
public class FactionBloc
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string FactionName { get; private set; }
    public FactionAlignment Alignment { get; private set; }
    public ReligionData ReligiousIdentity { get; private set; }
    public ReligiousFactionGoal ReligiousGoal { get; private set; }

    // ── Membership ────────────────────────────────────────────────────────────
    public Governor Leader { get; private set; }
    public List<Governor> Members { get; private set; } = new List<Governor>();

    // ── State ─────────────────────────────────────────────────────────────────
    public bool IsInRebellion { get; private set; }
    public List<FactionDemand> ActiveDemands { get; private set; } = new List<FactionDemand>();

    // ─────────────────────────────────────────────────────────────────────────

    public FactionBloc(string name, FactionAlignment alignment, Governor founder,
        ReligionData religiousIdentity = null, ReligiousFactionGoal religiousGoal = ReligiousFactionGoal.DemandTolerance)
    {
        FactionName = name;
        Alignment = alignment;
        ReligiousIdentity = alignment == FactionAlignment.Religious ? (religiousIdentity ?? founder?.PersonalReligion) : null;
        ReligiousGoal = religiousGoal;
        Leader = founder;
        Members.Add(founder);
        founder.Faction = this;
    }

    // ── Membership ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if this governor's alignment and opinion makes them a compatible candidate.
    /// A governor can join if they share the alignment (or are Independent) and Opinion < 20.
    /// </summary>
    public bool CanJoin(Governor gov)
    {
        if (gov == null || gov.Faction != null) return false;
        if (gov.IsOnCouncil) return false;           // Seated governors rarely join opposition factions
        if (gov.Opinion > 20f) return false;         // Content governors don't scheme
        // Alignment compatibility
        return Alignment switch
        {
            FactionAlignment.Religious  => gov.PersonalReligion != null && gov.PersonalReligion == ReligiousIdentity
                && !gov.HasPersonality(PersonalityTrait.Cynical)
                && (gov.HasPersonality(PersonalityTrait.Zealous) || gov.Grievances.ContainsKey(GrievanceSource.ReligionForced)),
            FactionAlignment.Separatist => gov.Opinion < -30f && gov.AmbitionScore > 50,
            FactionAlignment.Mercantile => gov.specialization == Governor.Specialization.Economic,
            _                           => gov.AmbitionScore > 30,
        };
    }

    /// <summary>Add a governor to this faction.</summary>
    public void AddMember(Governor gov)
    {
        if (gov == null || Members.Contains(gov)) return;
        Members.Add(gov);
        gov.Faction = this;
        ElectLeader();
    }

    /// <summary>Remove a governor (e.g. they died, were bribed, or got a council seat).</summary>
    public void RemoveMember(Governor gov)
    {
        if (gov == null) return;
        Members.Remove(gov);
        if (gov.Faction == this) gov.Faction = null;
        ElectLeader();
    }

    // ── Power ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Faction power = sum of member PowerRank scores, modified by average anger.
    /// Higher power means greater pressure on the player.
    /// </summary>
    public float ComputePower()
    {
        if (Members.Count == 0) return 0f;
        float rankSum = Members.Sum(m => m.PowerRank);
        float avgOpinion = Members.Average(m => m.Opinion);
        // Angry governors punch above their weight; content governors do nothing
        float angerMult = Mathf.Lerp(2f, 0.1f, (avgOpinion + 100f) / 200f);
        return rankSum * angerMult;
    }

    // ── Demands ───────────────────────────────────────────────────────────────

    /// <summary>Minimum weighted faction preference before a policy is worth demanding.</summary>
    private const float MinPolicyDemandScore = 5f;
    /// <summary>How much better an alternative government must score before demanding a change.</summary>
    private const float GovernmentChangeScoreMargin = 30f;

    /// <summary>
    /// Generate a contextually appropriate, CONCRETE demand. Candidate targets are
    /// selected with PoliticalPreferenceScorer (member preferences weighted by
    /// PowerRank) and validated against PolicyManager's structural prerequisites,
    /// so a demand never carries a null or illegal target. Returns null when no
    /// legal meaningful demand exists this turn.
    /// </summary>
    public FactionDemand GenerateDemand(Civilization civ, int currentTurn)
    {
        if (civ == null) return null;

        var demand = BuildDemandForAlignment(civ, currentTurn);
        if (demand == null) return null;

        ActiveDemands.Add(demand);
        return demand;
    }

    private FactionDemand BuildDemandForAlignment(Civilization civ, int currentTurn)
    {
        switch (Alignment)
        {
            case FactionAlignment.Reformist:
            {
                // Adopt the legal policy the faction most favors.
                var best = FindBestAdoptablePolicy(civ, out float bestScore);
                if (best != null && bestScore >= MinPolicyDemandScore)
                    return MakeAdoptPolicyDemand(best, currentTurn);

                // Or push a government the faction dramatically prefers.
                var (government, margin) = FindPreferredGovernment(civ);
                if (government != null && margin >= GovernmentChangeScoreMargin)
                    return MakeChangeGovernmentDemand(government, currentTurn);

                return null;
            }

            case FactionAlignment.Conservative:
            {
                // Repeal the ACTIVE policy the faction most dislikes.
                var worst = FindWorstActivePolicy(civ, out float worstScore);
                if (worst != null && worstScore <= -MinPolicyDemandScore)
                    return MakeRevokePolicyDemand(worst, currentTurn);

                // Status-quo faction: only presses a government change when the current
                // government itself strongly conflicts with member preferences.
                var (government, margin) = FindPreferredGovernment(civ);
                if (government != null && margin >= GovernmentChangeScoreMargin * 2f)
                    return MakeChangeGovernmentDemand(government, currentTurn);

                return null;
            }

            case FactionAlignment.Mercantile:
            {
                // Favor the economically preferred legal policy.
                var best = FindBestAdoptablePolicy(civ, out float bestScore, p => p.goldModifier > 0f);
                if (best != null && bestScore >= MinPolicyDemandScore)
                    return MakeAdoptPolicyDemand(best, currentTurn);

                // Concrete fallback: lower the tribute/tax burden.
                return new FactionDemand
                {
                    type = FactionDemandType.ReduceTaxation,
                    description = $"The {FactionName} demands a reduction in taxation.",
                    issuedTurn = currentTurn,
                    expiryTurns = 5,
                    targetGovernor = Leader,
                };
            }

            case FactionAlignment.Religious:
            {
                if (ReligiousIdentity != null && ReligiousGoal == ReligiousFactionGoal.EstablishOurReligion
                    && civ.StateReligion != ReligiousIdentity)
                    return new FactionDemand { type = FactionDemandType.AdoptStateReligion,
                        targetReligion = ReligiousIdentity, issuedTurn = currentTurn, expiryTurns = 5,
                        description = $"The {FactionName} demands that {ReligiousIdentity.religionName} become the state religion." };
                // Prefer a concrete faith-aligned policy or government.
                var best = FindBestAdoptablePolicy(civ, out float bestScore, p => p.faithModifier > 0f || HasReligiousOpinionEffects(p));
                if (best != null && bestScore >= MinPolicyDemandScore)
                    return MakeAdoptPolicyDemand(best, currentTurn);

                var (government, margin) = FindPreferredGovernment(civ, g => g.requiresStateReligion || g.faithModifier > 0f);
                if (government != null && margin >= GovernmentChangeScoreMargin)
                    return MakeChangeGovernmentDemand(government, currentTurn);

                // Fallback when members suffer religious oppression.
                if (Members.Any(m => m != null &&
                        (m.Grievances.ContainsKey(GrievanceSource.ReligionForced) || HasReligionMismatch(m, civ))))
                {
                    return new FactionDemand
                    {
                        type = FactionDemandType.GrantReligiousFreedom,
                        description = $"The {FactionName} demands religious freedom for its governors.",
                        issuedTurn = currentTurn,
                        expiryTurns = 5,
                        targetGovernor = Leader,
                        targetReligion = ReligiousIdentity,
                    };
                }

                return null;
            }

            case FactionAlignment.Independent:
            {
                // A council seat can only be demanded when the institution exists
                // and a faction governor could legally be seated.
                var seatCandidate = FindSeatableGovernor(civ);
                if (seatCandidate != null)
                {
                    return new FactionDemand
                    {
                        type = FactionDemandType.GrantCouncilSeat,
                        description = $"The {FactionName} demands that {seatCandidate.Name} receive a seat on the royal council.",
                        issuedTurn = currentTurn,
                        expiryTurns = 5,
                        targetGovernor = seatCandidate,
                    };
                }

                // Otherwise fall back to a concrete policy demand the members favor.
                var best = FindBestAdoptablePolicy(civ, out float bestScore);
                if (best != null && bestScore >= MinPolicyDemandScore)
                    return MakeAdoptPolicyDemand(best, currentTurn);

                return new FactionDemand
                {
                    type = FactionDemandType.ReduceTaxation,
                    description = $"The {FactionName} demands a reduction in taxation to preserve local autonomy.",
                    issuedTurn = currentTurn,
                    expiryTurns = 5,
                    targetGovernor = Leader,
                };
            }

            case FactionAlignment.Separatist:
            {
                // Severe separatists issue the independence ultimatum.
                bool severe = Members.All(m => m == null || m.IsRebellionReady())
                           || (Leader != null && Leader.Opinion < -60f);
                if (severe)
                {
                    return new FactionDemand
                    {
                        type = FactionDemandType.DeclareIndependence,
                        description = $"The {FactionName} demands independence and threatens open rebellion.",
                        issuedTurn = currentTurn,
                        expiryTurns = 5,
                        targetGovernor = Leader,
                    };
                }

                // Less extreme: press a concrete grievance first.
                var wronged = Members.FirstOrDefault(m => m != null && m.Grievances.ContainsKey(GrievanceSource.CityReassigned));
                if (wronged != null)
                {
                    return new FactionDemand
                    {
                        type = FactionDemandType.ReturnTerritory,
                        description = $"The {FactionName} demands that territory taken from {wronged.Name} be restored.",
                        issuedTurn = currentTurn,
                        expiryTurns = 5,
                        targetGovernor = wronged,
                    };
                }

                var worst = FindWorstActivePolicy(civ, out float worstScore);
                if (worst != null && worstScore <= -MinPolicyDemandScore)
                    return MakeRevokePolicyDemand(worst, currentTurn);

                return new FactionDemand
                {
                    type = FactionDemandType.DeclareIndependence,
                    description = $"The {FactionName} demands independence and threatens open rebellion.",
                    issuedTurn = currentTurn,
                    expiryTurns = 5,
                    targetGovernor = Leader,
                };
            }

            default:
                return null;
        }
    }

    private FactionDemand MakeAdoptPolicyDemand(PolicyData policy, int currentTurn) => new FactionDemand
    {
        type = FactionDemandType.AdoptPolicy,
        description = $"The {FactionName} demands adoption of {policy.policyName}.",
        targetPolicy = policy,
        issuedTurn = currentTurn,
        expiryTurns = 5,
        targetGovernor = Leader,
    };

    private FactionDemand MakeRevokePolicyDemand(PolicyData policy, int currentTurn) => new FactionDemand
    {
        type = FactionDemandType.RevokePolicy,
        description = $"The {FactionName} demands repeal of {policy.policyName}.",
        targetPolicy = policy,
        issuedTurn = currentTurn,
        expiryTurns = 5,
        targetGovernor = Leader,
    };

    private FactionDemand MakeChangeGovernmentDemand(GovernmentData government, int currentTurn) => new FactionDemand
    {
        type = FactionDemandType.ChangeGovernment,
        description = $"The {FactionName} demands adoption of {government.governmentName}.",
        targetGovernment = government,
        issuedTurn = currentTurn,
        expiryTurns = 5,
        targetGovernor = Leader,
    };

    /// <summary>Best structurally-legal, not-yet-active policy by weighted faction preference.</summary>
    private PolicyData FindBestAdoptablePolicy(Civilization civ, out float bestScore, System.Func<PolicyData, bool> filter = null)
    {
        bestScore = float.MinValue;
        PolicyData best = null;
        if (PolicyManager.Instance == null) return null;

        foreach (var policy in PolicyManager.Instance.GetStructurallyAvailablePolicies(civ))
        {
            if (policy == null) continue;
            if (filter != null && !filter(policy)) continue;
            float score = PoliticalPreferenceScorer.ScorePolicyForFaction(policy, this, civ);
            if (score > bestScore)
            {
                bestScore = score;
                best = policy;
            }
        }
        return best;
    }

    /// <summary>ACTIVE policy the faction most dislikes (lowest weighted preference).</summary>
    private PolicyData FindWorstActivePolicy(Civilization civ, out float worstScore)
    {
        worstScore = float.MaxValue;
        PolicyData worst = null;
        if (civ.activePolicies == null) return null;

        foreach (var policy in civ.activePolicies)
        {
            if (policy == null) continue;
            float score = PoliticalPreferenceScorer.ScorePolicyForFaction(policy, this, civ);
            if (score < worstScore)
            {
                worstScore = score;
                worst = policy;
            }
        }
        return worst;
    }

    /// <summary>
    /// Structurally-legal government the faction prefers most, with the score margin
    /// over the current government. Returns (null, 0) when nothing scores higher.
    /// </summary>
    private (GovernmentData government, float margin) FindPreferredGovernment(Civilization civ, System.Func<GovernmentData, bool> filter = null)
    {
        if (PolicyManager.Instance == null) return (null, 0f);

        float currentScore = civ.currentGovernment != null
            ? PoliticalPreferenceScorer.ScoreGovernmentForFaction(civ.currentGovernment, this, civ)
            : 0f;

        GovernmentData best = null;
        float bestScore = float.MinValue;
        foreach (var government in PolicyManager.Instance.GetStructurallyAvailableGovernments(civ))
        {
            if (government == null) continue;
            if (filter != null && !filter(government)) continue;
            float score = PoliticalPreferenceScorer.ScoreGovernmentForFaction(government, this, civ);
            if (score > bestScore)
            {
                bestScore = score;
                best = government;
            }
        }

        if (best == null || bestScore <= currentScore) return (null, 0f);
        return (best, bestScore - currentScore);
    }

    /// <summary>A faction governor who could legally take a council seat right now, or null.</summary>
    private Governor FindSeatableGovernor(Civilization civ)
    {
        if (!civ.HasRoyalCouncil) return null;
        if (civ.royalCouncil.Count >= civ.MaxCouncilSeats) return null;

        return Members
            .Where(m => m != null && !m.IsOnCouncil)
            .OrderByDescending(m => m.PowerRank)
            .FirstOrDefault(m =>
            {
                m.RefreshCouncilEligibility();
                return m.IsCouncilEligible;
            });
    }

    private static bool HasReligiousOpinionEffects(PolicyData policy)
        => policy.governorOpinionEffects != null
           && policy.governorOpinionEffects.Any(e => e != null && e.onlyIfReligionMismatch);

    private static bool HasReligionMismatch(Governor gov, Civilization civ)
        => civ.StateReligion != null && gov.PersonalReligion != null && gov.PersonalReligion != civ.StateReligion;

    /// <summary>
    /// Resolve a pending demand. If refused, adds grievances and may trigger rebellion.
    /// Returns true if rebellion was triggered.
    /// </summary>
    public bool ResolveDemand(FactionDemand demand, bool accepted, Civilization civ, int currentTurn)
    {
        ActiveDemands.Remove(demand);

        if (accepted)
        {
            // Acceptance improves opinion for all members
            foreach (var m in Members)
                m.AddOpinionModifier("Demand Accepted", 15f, 20);
            return false;
        }

        // Refusal: grievances + escalation check
        foreach (var m in Members)
        {
            m.AddGrievance(GrievanceSource.OverruledDecision);
            m.AddOpinionModifier("Demand Refused", -20f, 30);
        }

        // Separatist or very powerful factions trigger rebellion
        bool rebelsNow = Alignment == FactionAlignment.Separatist ||
                         (ComputePower() > 15f && Members.All(m => m.IsRebellionReady()));

        if (rebelsNow)
        {
            TriggerRebellion(civ);
            return true;
        }

        // Generate a harder follow-up demand
        if (Alignment == FactionAlignment.Separatist && ActiveDemands.Count == 0)
            GenerateDemand(civ, currentTurn);

        return false;
    }

    // ── Rebellion ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Trigger revolt on all cities governed by faction members.
    /// Cities call their own TriggerRevolt logic; this is the coordinated version.
    /// </summary>
    public void TriggerRebellion(Civilization civ)
    {
        if (IsInRebellion) return;
        IsInRebellion = true;

        // Name the rebel civ after the largest city (by level) in the bloc
        var allCities = Members.SelectMany(m => m.Cities).Where(c => c != null && c.owner == civ).ToList();
        var largestCity = allCities.OrderByDescending(c => c.level).FirstOrDefault();
        string rebelName = largestCity != null
            ? $"{largestCity.cityName} Rebels"
            : $"{FactionName} Rebels";

        foreach (var member in Members)
        {
            member.IsInRebellion = true;
            foreach (var city in member.Cities)
            {
                if (city == null || city.owner != civ) continue;
                city.TriggerRevolt(rebelName);
            }
        }

        Debug.Log($"[FactionBloc] '{FactionName}' has risen in rebellion against {civ.civData?.civName ?? civ.name}! " +
                  $"Rebel civ named '{rebelName}'. {Members.Count} governors, {allCities.Count} cities affected.");
    }

    public void RestoreRebellionState(bool isInRebellion)
    {
        IsInRebellion = isInRebellion;
        foreach (var member in Members)
        {
            if (member != null)
                member.IsInRebellion = isInRebellion;
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    /// <summary>Elect the member with highest PowerRank as leader.</summary>
    private void ElectLeader()
    {
        if (Members.Count == 0) { Leader = null; return; }
        Leader = Members.OrderByDescending(m => m.PowerRank).First();
    }
}
