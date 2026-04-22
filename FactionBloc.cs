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
    /// <summary>Turn on which this demand was issued (for expiry/escalation).</summary>
    public int issuedTurn;
    /// <summary>After this many turns of no response the faction escalates.</summary>
    public int expiryTurns = 5;
}

/// <summary>
/// A coalition of noble governors united around a shared political grievance.
/// Factions form when multiple lords share an alignment and have high enough combined anger.
/// They issue coordinated demands and can trigger multi-city rebellions if refused.
/// </summary>
public class FactionBloc
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string FactionName { get; private set; }
    public FactionAlignment Alignment { get; private set; }

    // ── Membership ────────────────────────────────────────────────────────────
    public Governor Leader { get; private set; }
    public List<Governor> Members { get; private set; } = new List<Governor>();

    // ── State ─────────────────────────────────────────────────────────────────
    public bool IsInRebellion { get; private set; }
    public List<FactionDemand> ActiveDemands { get; private set; } = new List<FactionDemand>();

    // ─────────────────────────────────────────────────────────────────────────

    public FactionBloc(string name, FactionAlignment alignment, Governor founder)
    {
        FactionName = name;
        Alignment = alignment;
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
        if (gov.IsOnCouncil) return false;           // Seated lords rarely join opposition factions
        if (gov.Opinion > 20f) return false;         // Content lords don't scheme
        // Alignment compatibility
        return Alignment switch
        {
            FactionAlignment.Religious  => gov.HasPersonality(PersonalityTrait.Zealous),
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
        // Angry lords punch above their weight; content lords do nothing
        float angerMult = Mathf.Lerp(2f, 0.1f, (avgOpinion + 100f) / 200f);
        return rankSum * angerMult;
    }

    // ── Demands ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a contextually appropriate demand based on alignment.
    /// Should be called when faction power crosses a meaningful threshold.
    /// </summary>
    public FactionDemand GenerateDemand(int currentTurn)
    {
        FactionDemandType type = Alignment switch
        {
            FactionAlignment.Independent  => FactionDemandType.GrantCouncilSeat,
            FactionAlignment.Reformist    => FactionDemandType.AdoptPolicy,
            FactionAlignment.Conservative => FactionDemandType.RevokePolicy,
            FactionAlignment.Religious    => FactionDemandType.GrantReligiousFreedom,
            FactionAlignment.Separatist   => FactionDemandType.DeclareIndependence,
            FactionAlignment.Mercantile   => FactionDemandType.ReduceTaxation,
            _                             => FactionDemandType.GrantCouncilSeat,
        };

        string description = type switch
        {
            FactionDemandType.GrantCouncilSeat      => $"The {FactionName} demands a seat on the royal council.",
            FactionDemandType.AdoptPolicy           => $"The {FactionName} demands adoption of favorable policy.",
            FactionDemandType.RevokePolicy          => $"The {FactionName} demands revocation of an unpopular policy.",
            FactionDemandType.GrantReligiousFreedom => $"The {FactionName} demands religious freedom for their lords.",
            FactionDemandType.DeclareIndependence   => $"The {FactionName} threatens open rebellion if autonomy is not granted.",
            FactionDemandType.ReduceTaxation        => $"The {FactionName} demands a reduction in taxation.",
            _                                       => $"The {FactionName} makes a political demand.",
        };

        var demand = new FactionDemand
        {
            type = type,
            description = description,
            issuedTurn = currentTurn,
            expiryTurns = 5,
            targetGovernor = Leader,
        };

        ActiveDemands.Add(demand);
        return demand;
    }

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
            GenerateDemand(currentTurn);

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
                  $"Rebel civ named '{rebelName}'. {Members.Count} lords, {allCities.Count} cities affected.");
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
