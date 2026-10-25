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
    public string proposalId;
    public FactionProposalCategory category = FactionProposalCategory.HostileDemand;
    public FactionProposalState state = FactionProposalState.Offered;
    public FactionProposalObjective objective;
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
    public int acceptedTurn = -1;
    public int deadlineTurn = -1;
    public int progress;
    public int requiredProgress = 1;
    public float satisfactionReward;
    public float satisfactionFailurePenalty;
    public BuildingData targetBuilding;
}

/// <summary>
/// A coalition of governors united around a shared political grievance.
/// Factions form when multiple governors share an alignment and have high enough combined anger.
/// They issue coordinated demands and can trigger multi-city rebellions if refused.
/// </summary>
public class FactionBloc
{
    public const float SupportiveThreshold = 70f;
    public const float NeutralThreshold = 40f;
    public const float DissatisfiedThreshold = 20f;
    public const float MinimumMemberRelevance = 2f;
    public const int CooperativeRequestDeadlineTurns = 10;
    public const int HostileDemandDeadlineTurns = 5;
    public const int ProposalCooldownTurns = 8;
    public const int FormationIntervalTurns = 3;
    public const int MaximumFactionsPerCivilization = 4;
    public const float IndependentFounderPower = 8f;
    public const float MinimumProposalPower = 4f;
    public const float AiProposalInfluence = 6f;
    private const float SatisfactionTrendPerTurn = 2f;

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
    public float Satisfaction { get; private set; }
    public int NextProposalTurn { get; private set; }
    public FactionStance Stance => Satisfaction >= SupportiveThreshold ? FactionStance.Supportive
        : Satisfaction >= NeutralThreshold ? FactionStance.Neutral
        : Satisfaction >= DissatisfiedThreshold ? FactionStance.Dissatisfied
        : FactionStance.Hostile;

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
        Satisfaction = InitialSatisfaction(founder);
    }

    // ── Membership ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when this governor has sufficient relevance and shares the faction's interests.
    /// </summary>
    public bool CanJoin(Governor gov)
    {
        if (gov == null || gov.Faction != null) return false;
        if (gov.PowerRank < MinimumMemberRelevance && !gov.HasPersonality(PersonalityTrait.Ambitious)) return false;
        return Alignment switch
        {
            FactionAlignment.Religious  => gov.PersonalReligion != null && gov.PersonalReligion == ReligiousIdentity
                && !gov.HasPersonality(PersonalityTrait.Cynical),
            FactionAlignment.Separatist => gov.Opinion < -20f && (gov.AmbitionScore > 50 || gov.TotalGrievances() >= 2),
            FactionAlignment.Mercantile => gov.specialization == Governor.Specialization.Economic || gov.HasPersonality(PersonalityTrait.Greedy),
            FactionAlignment.Reformist => gov.specialization == Governor.Specialization.Scientific
                || gov.specialization == Governor.Specialization.Cultural || gov.specialization == Governor.Specialization.Industrial,
            FactionAlignment.Conservative => gov.HasPersonality(PersonalityTrait.Content)
                || gov.HasPersonality(PersonalityTrait.Loyal) || gov.HasPersonality(PersonalityTrait.Honest),
            FactionAlignment.Independent => gov.HasPersonality(PersonalityTrait.Ambitious)
                || gov.specialization == Governor.Specialization.Military,
            _ => false,
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
    /// Faction power is institutional influence and does not depend on satisfaction.
    /// </summary>
    public float ComputePower()
    {
        return Members.Where(member => member != null).Sum(member => Mathf.Max(1, member.PowerRank));
    }

    public float ComputePoliticalThreat()
    {
        if (Members.Count == 0) return 0f;
        float dissatisfaction = (100f - Satisfaction) / 100f;
        float grievances = Members.Where(member => member != null).Sum(member => member.TotalGrievances());
        float readiness = Members.Count(member => member != null && member.IsRebellionReady());
        float separatist = Alignment == FactionAlignment.Separatist ? ComputePower() * 0.25f : 0f;
        return ComputePower() * dissatisfaction + grievances * 0.75f + readiness * 3f + separatist;
    }

    public float GetSupportLoyaltyBonus()
        => Stance == FactionStance.Supportive ? Mathf.Clamp(ComputePower() * 0.15f, 0f, 5f) : 0f;

    public void AdjustSatisfaction(float amount)
        => Satisfaction = Mathf.Clamp(Satisfaction + amount, 0f, 100f);

    public void UpdateSatisfaction(Civilization civ)
    {
        if (civ == null || Members.Count == 0) return;
        float averageOpinion = Members.Where(member => member != null).Select(member => member.Opinion).DefaultIfEmpty(50f).Average();
        float grievances = Members.Where(member => member != null).Sum(member => member.TotalGrievances());
        float target = 50f + Mathf.Clamp((averageOpinion - 25f) * 0.15f, -10f, 10f) - Mathf.Clamp(grievances, 0f, 15f);
        if (civ.currentGovernment != null)
            target += Mathf.Clamp(PoliticalPreferenceScorer.ScoreGovernmentForFaction(civ.currentGovernment, this, civ) * 0.1f, -10f, 10f);
        if (civ.activePolicies != null && civ.activePolicies.Count > 0)
            target += Mathf.Clamp(civ.activePolicies.Where(policy => policy != null)
                .Select(policy => PoliticalPreferenceScorer.ScorePolicyForFaction(policy, this, civ)).DefaultIfEmpty().Average() * 0.05f, -8f, 8f);
        target += LegacyAffinity(civ);
        Satisfaction = Mathf.MoveTowards(Satisfaction, Mathf.Clamp(target, 0f, 100f), SatisfactionTrendPerTurn);
    }

    public void RestorePoliticalState(float satisfaction, int nextProposalTurn)
    {
        Satisfaction = Mathf.Clamp(satisfaction, 0f, 100f);
        NextProposalTurn = Mathf.Max(0, nextProposalTurn);
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
        if (civ == null || ActiveDemands.Count > 0 || currentTurn < NextProposalTurn) return null;

        var demand = Stance == FactionStance.Supportive
            ? BuildCooperativeRequest(civ, currentTurn)
            : BuildDemandForAlignment(civ, currentTurn);
        if (demand == null) return null;

        demand.proposalId = string.IsNullOrWhiteSpace(demand.proposalId)
            ? $"{FactionName}|{currentTurn}|{demand.type}" : demand.proposalId;
        if (demand.category == FactionProposalCategory.HostileDemand && Stance != FactionStance.Hostile)
            demand.category = FactionProposalCategory.Petition;
        demand.deadlineTurn = demand.deadlineTurn >= 0 ? demand.deadlineTurn : currentTurn + demand.expiryTurns;

        ActiveDemands.Add(demand);
        return demand;
    }

    private FactionDemand BuildCooperativeRequest(Civilization civ, int currentTurn)
    {
        if (Alignment == FactionAlignment.Reformist || Alignment == FactionAlignment.Mercantile)
        {
            var policy = FindBestAdoptablePolicy(civ, out float score,
                Alignment == FactionAlignment.Mercantile ? p => p.goldModifier > 0f || p.domesticTradeModifier > 0f || p.foreignTradeModifier > 0f : null);
            if (policy == null || score < MinPolicyDemandScore) return null;
            return new FactionDemand
            {
                category = FactionProposalCategory.CooperativeRequest,
                state = FactionProposalState.Offered,
                objective = FactionProposalObjective.AdoptPolicy,
                type = FactionDemandType.AdoptPolicy,
                description = $"Adopt {policy.policyName}, a reform supported by our members.",
                issuedTurn = currentTurn,
                expiryTurns = CooperativeRequestDeadlineTurns,
                deadlineTurn = currentTurn + CooperativeRequestDeadlineTurns,
                targetPolicy = policy,
                satisfactionReward = 12f,
                satisfactionFailurePenalty = 8f,
            };
        }
        if (Alignment != FactionAlignment.Religious) return null;
        var governedCities = Members.Where(member => member != null).SelectMany(member => member.Cities)
            .Where(city => city != null && city.owner == civ).Distinct().ToList();
        if (governedCities.Count == 0) return null;
        var building = ResourceCache.GetAllBuildings()
            .Where(candidate => candidate != null && candidate.isFaithBuilding && candidate.AreRequirementsMet(civ))
            .Where(candidate => governedCities.Any(city => city.builtBuildings == null
                || !city.builtBuildings.Any(entry => entry.data == candidate)))
            .Where(candidate => governedCities.All(city => city.productionQueue == null
                || !city.productionQueue.Any(entry => entry?.data == candidate)))
            .OrderBy(candidate => candidate.productionCost).ThenBy(candidate => candidate.buildingName)
            .FirstOrDefault();
        if (building == null) return null;
        return new FactionDemand
        {
            category = FactionProposalCategory.CooperativeRequest,
            state = FactionProposalState.Offered,
            objective = FactionProposalObjective.ConstructBuilding,
            type = FactionDemandType.ConstructFaithBuilding,
            description = $"Construct {building.buildingName} in a city governed by one of our members.",
            issuedTurn = currentTurn,
            expiryTurns = CooperativeRequestDeadlineTurns,
            deadlineTurn = currentTurn + CooperativeRequestDeadlineTurns,
            targetBuilding = building,
            targetReligion = ReligiousIdentity,
            satisfactionReward = 15f,
            satisfactionFailurePenalty = 10f,
        };
    }

    private float LegacyAffinity(Civilization civ)
    {
        if (civ.activeLegacies == null) return 0f;
        float affinity = 0f;
        foreach (var legacy in civ.activeLegacies)
        {
            if (legacy?.factionAffinities == null) continue;
            foreach (var entry in legacy.factionAffinities)
                if (entry != null && entry.alignment == Alignment) affinity += entry.satisfactionModifier * 0.5f;
        }
        return Mathf.Clamp(affinity, -10f, 10f);
    }

    private static float InitialSatisfaction(Governor founder)
        => Mathf.Clamp(50f + (founder?.Opinion ?? 0f) * 0.15f - (founder?.TotalGrievances() ?? 0) * 2f, 25f, 75f);

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
        if (demand == null || !ActiveDemands.Contains(demand)) return false;
        if (demand.category == FactionProposalCategory.CooperativeRequest)
        {
            if (accepted)
            {
                demand.state = FactionProposalState.Accepted;
                demand.acceptedTurn = currentTurn;
                demand.deadlineTurn = currentTurn + Mathf.Max(1, demand.expiryTurns);
                AdjustSatisfaction(1f);
            }
            else
            {
                demand.state = FactionProposalState.Declined;
                ActiveDemands.Remove(demand);
                AdjustSatisfaction(-1f);
                NextProposalTurn = currentTurn + ProposalCooldownTurns;
            }
            return false;
        }

        ActiveDemands.Remove(demand);
        demand.state = accepted ? FactionProposalState.Completed : FactionProposalState.Declined;
        NextProposalTurn = currentTurn + ProposalCooldownTurns;

        if (accepted)
        {
            foreach (var m in Members)
                m.SetOpinionModifier("Faction Proposal Fulfilled", demand.category == FactionProposalCategory.Petition ? 8f : 15f, 20);
            AdjustSatisfaction(demand.category == FactionProposalCategory.Petition ? 6f : 10f);
            return false;
        }

        if (demand.category == FactionProposalCategory.Petition)
        {
            foreach (var member in Members)
                member.SetOpinionModifier("Faction Petition Declined", -3f, 12);
            AdjustSatisfaction(-4f);
            return false;
        }

        foreach (var m in Members)
        {
            m.AddGrievance(GrievanceSource.OverruledDecision);
            m.SetOpinionModifier("Demand Refused", -20f, 30);
        }
        AdjustSatisfaction(-15f);

        bool rebellionReady = Members.Any(member => member != null && member.IsRebellionReady());
        bool rebelsNow = Stance == FactionStance.Hostile && rebellionReady
            && (ComputePoliticalThreat() >= 15f || Alignment == FactionAlignment.Separatist);

        if (rebelsNow)
        {
            TriggerRebellion(civ);
            return true;
        }

        return false;
    }

    public bool TryCompleteBuildingRequest(Civilization civ, City city, BuildingData building, int currentTurn)
    {
        var proposal = ActiveDemands.FirstOrDefault(demand => demand != null
            && demand.category == FactionProposalCategory.CooperativeRequest
            && demand.state == FactionProposalState.Accepted
            && demand.objective == FactionProposalObjective.ConstructBuilding);
        if (proposal == null || city == null || building == null || city.owner != civ) return false;
        if (proposal.targetBuilding != building || !building.isFaithBuilding) return false;
        if (!Members.Any(member => member != null && member.Cities.Contains(city))) return false;

        proposal.progress = Mathf.Min(proposal.requiredProgress, proposal.progress + 1);
        if (proposal.progress < proposal.requiredProgress) return false;
        proposal.state = FactionProposalState.Completed;
        ActiveDemands.Remove(proposal);
        AdjustSatisfaction(proposal.satisfactionReward);
        foreach (var member in Members)
            member.SetOpinionModifier("Faction Request Fulfilled", 5f, 15);
        NextProposalTurn = currentTurn + ProposalCooldownTurns;
        return true;
    }

    public bool TryCompletePolicyRequest(Civilization civ, PolicyData policy, int currentTurn)
    {
        var proposal = ActiveDemands.FirstOrDefault(demand => demand != null
            && demand.category == FactionProposalCategory.CooperativeRequest
            && demand.state == FactionProposalState.Accepted
            && demand.objective == FactionProposalObjective.AdoptPolicy);
        if (proposal == null || policy == null || proposal.targetPolicy != policy) return false;
        proposal.progress = proposal.requiredProgress;
        proposal.state = FactionProposalState.Completed;
        ActiveDemands.Remove(proposal);
        AdjustSatisfaction(proposal.satisfactionReward);
        foreach (var member in Members)
            member.SetOpinionModifier("Faction Request Fulfilled", 5f, 15);
        NextProposalTurn = currentTurn + ProposalCooldownTurns;
        return true;
    }

    public bool ProcessProposalDeadline(int currentTurn)
    {
        var proposal = ActiveDemands.FirstOrDefault();
        if (proposal == null || proposal.category != FactionProposalCategory.CooperativeRequest
            || currentTurn <= proposal.deadlineTurn) return false;
        if (proposal.state == FactionProposalState.Offered)
        {
            proposal.state = FactionProposalState.Declined;
            ActiveDemands.Remove(proposal);
            NextProposalTurn = currentTurn + ProposalCooldownTurns;
            return false;
        }
        if (proposal.state != FactionProposalState.Accepted) return false;
        proposal.state = FactionProposalState.Failed;
        ActiveDemands.Remove(proposal);
        AdjustSatisfaction(-Mathf.Abs(proposal.satisfactionFailurePenalty));
        foreach (var member in Members)
            member.SetOpinionModifier("Faction Promise Broken", -5f, 20);
        NextProposalTurn = currentTurn + ProposalCooldownTurns;
        return true;
    }

    // ── Rebellion ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Trigger revolt on all cities governed by faction members.
    /// Cities call their own TriggerRevolt logic; this is the coordinated version.
    /// </summary>
    public void TriggerRebellion(Civilization civ)
        => TriggerRebellion(civ, int.MaxValue);

    public void TriggerRebellion(Civilization civ, int maximumCityRevolts)
    {
        if (IsInRebellion) return;
        IsInRebellion = true;

        // Name the rebel civ after the largest city (by level) in the bloc
        var allCities = Members.SelectMany(m => m.Cities).Where(c => c != null && c.owner == civ).ToList();
        var largestCity = allCities.OrderByDescending(c => c.level).FirstOrDefault();
        string rebelName = largestCity != null
            ? $"{largestCity.cityName} Rebels"
            : $"{FactionName} Rebels";

        int revoltsRemaining = Mathf.Max(0, maximumCityRevolts);
        foreach (var member in Members)
        {
            member.IsInRebellion = true;
            foreach (var city in member.Cities.OrderBy(c => c != null ? c.loyalty : float.MaxValue))
            {
                if (city == null || city.owner != civ || revoltsRemaining <= 0) continue;
                city.TriggerRevolt(rebelName);
                revoltsRemaining--;
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
