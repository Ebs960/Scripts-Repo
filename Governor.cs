using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public struct GovernorBonuses {
    public int gold, production, food, science, culture, faith, combat, cityDefense;
}

public class Governor
{
    public enum Specialization
    {
        Military,
        Economic,
        Scientific,
        Cultural,
        Religious,
        Industrial
    }

    public int Id { get; private set; } // Unique identifier
    public string Name { get; private set; }
    public Specialization specialization { get; private set; }
    public int Level { get; private set; } = 1;
    public int Experience { get; private set; } = 0;
    private const int XP_PER_LEVEL = 100;

    // List of assigned cities (or, each city references its governor)
    public List<City> Cities { get; private set; } = new List<City>();
    // List of assigned herds (new)
    public List<Herd> Herds { get; private set; } = new List<Herd>();

    // Traits (ScriptableObjects)
    public List<GovernorTrait> Traits { get; private set; } = new List<GovernorTrait>();

    // Stat tracking for trait unlocking
    private Dictionary<TraitTrigger, int> stats = new Dictionary<TraitTrigger, int>();

    // --- CK-Lite Personality & Opinion System ---
    public List<PersonalityTrait> PersonalityTraits { get; private set; } = new List<PersonalityTrait>();
    /// <summary>Opinion of the ruling player, -100 (hostile) to +100 (devoted). Starts at 50.</summary>
    public float Opinion { get; set; } = 50f;
    public List<OpinionModifier> OpinionModifiers { get; private set; } = new List<OpinionModifier>();

    // --- Political Identity ---
    /// <summary>Governor's personal religion. May differ from civ's state religion.</summary>
    public ReligionData PersonalReligion { get; set; }
    /// <summary>Governor's personal culture. May differ from the civ's dominant culture.</summary>
    public CultureData PersonalCulture { get; set; }
    /// <summary>
    /// Derived ambition score (0-100). High score = this governor actively schemes.
    /// Computed from personality (Ambitious trait) and power rank.
    /// </summary>
    public int AmbitionScore => ComputeAmbitionScore();

    // --- Loyalty Floor / Ceiling (political limits on opinion range) ---
    /// <summary>Opinion can never fall below this floor, even from stacking negatives.</summary>
    public float LoyaltyFloor { get; set; } = -100f;
    /// <summary>Opinion can never rise above this ceiling.</summary>
    public float LoyaltyCeiling { get; set; } = 100f;

    // --- Power Rank (based on governed population) ---
    /// <summary>
    /// A rough measure of this governor's political weight.
    /// PowerRank = 2 x (sum of governed city levels) + 1 x (sum of governed herd levels).
    /// Null holdings are ignored and negative levels clamp to zero.
    /// Used for council eligibility and faction power calculation.
    /// </summary>
    public int PowerRank
    {
        get
        {
            int rank = 0;
            if (Cities != null)
            {
                foreach (var city in Cities)
                    if (city != null) rank += 2 * Mathf.Max(0, city.level);
            }
            if (Herds != null)
            {
                foreach (var herd in Herds)
                    if (herd != null) rank += Mathf.Max(0, herd.level);
            }
            return rank;
        }
    }

    // --- Council State ---
    public bool IsCouncilEligible { get; set; }
    public bool IsOnCouncil { get; set; }

    // --- Grievance Stacks ---
    /// <summary>Accumulated grievances by source. Each source stacks independently.</summary>
    public Dictionary<GrievanceSource, int> Grievances { get; private set; } = new Dictionary<GrievanceSource, int>();

    // --- Faction Membership ---
    /// <summary>The noble bloc this governor belongs to, or null if unaffiliated.</summary>
    public FactionBloc Faction { get; set; }

    // --- Rebellion State ---
    public bool IsInRebellion { get; set; }

    // --- Once-per-turn opinion tick guard ---
    /// <summary>The last civilization round in which this governor's opinion was ticked. -1 = never.</summary>
    public int LastOpinionTickRound { get; private set; } = -1;

    public Governor(int id, string name, Specialization spec)
    {
        Id = id;
        Name = name;
        specialization = spec;
        Level = 1;
        Experience = 0;
        Cities = new List<City>();
        Herds = new List<Herd>();
        Traits = new List<GovernorTrait>();
        PersonalityTraits = new List<PersonalityTrait>();
        OpinionModifiers = new List<OpinionModifier>();
        Opinion = 50f;
        stats = new Dictionary<TraitTrigger, int>();
        Grievances = new Dictionary<GrievanceSource, int>();
        LoyaltyFloor = -100f;
        LoyaltyCeiling = 100f;
        IsCouncilEligible = false;
        IsOnCouncil = false;
        Faction = null;
        IsInRebellion = false;
        LastOpinionTickRound = -1;
        
        // Initialize all stats to 0
        foreach (TraitTrigger trigger in System.Enum.GetValues(typeof(TraitTrigger)))
        {
            stats[trigger] = 0;
        }
    }

    // Record a stat increase for trait unlocking and gain XP
    public void RecordStat(TraitTrigger trigger, int amount = 1)
    {
        if (!stats.ContainsKey(trigger))
            stats[trigger] = 0;
        
        stats[trigger] += amount;
        GainExperience(amount * 10); // Each stat point gives some XP

        // Check for trait unlocks
        CheckTraitUnlocks();
    }

    // Get the current value of a stat
    public int GetStat(TraitTrigger trigger)
    {
        return stats.GetValueOrDefault(trigger, 0);
    }

    private void CheckTraitUnlocks()
    {
        // Get all unlockable traits from the civilization
        var civ = Cities.FirstOrDefault()?.owner ?? Herds.FirstOrDefault()?.owner;
        if (civ == null) return;

        foreach (var trait in civ.unlockedGovernorTraits)
        {
            // Skip if we already have this trait
            if (Traits.Contains(trait)) continue;

            // Check if we meet the requirement
            if (GetStat(trait.triggerType) >= trait.requiredValue)
            {
                Traits.Add(trait);
}
        }
    }

    public void GainExperience(int amount)
    {
        Experience += amount;
        while (Experience >= XP_PER_LEVEL * Level)
        {
            Experience -= XP_PER_LEVEL * Level;
            LevelUp();
        }
    }

    private void LevelUp()
    {
        Level++;
// Notify any cities this governor is assigned to
        foreach (var city in Cities)
        {
            // You might want to refresh city UI or apply new bonuses here
            city.RefreshGovernorBonuses();
        }
        // Also notify any herds this governor is assigned to
        foreach (var herd in Herds)
        {
            if (herd != null) herd.RefreshGovernorBonuses();
        }
    }

    // Returns the sum of all bonuses from traits and specialization
    public GovernorBonuses GetTotalBonuses()
    {
        var spec = GetSpecializationBonuses(specialization);
        var trait = new GovernorBonuses();
        foreach (var t in Traits)
        {
            trait.gold        += t.goldBonusModifier;
            trait.production  += t.productionBonusModifier;
            trait.food        += t.foodBonusModifier;
            trait.science     += t.scienceBonusModifier;
            trait.culture     += t.cultureBonusModifier;
            trait.faith       += t.faithBonusModifier;
            trait.combat      += t.combatBonusModifier;
            trait.cityDefense += t.cityDefenseBonusModifier;
        }

        // Apply level bonuses (2% per level)
        float levelMultiplier = 1f + (Level - 1) * 0.02f;
        
        return new GovernorBonuses {
            gold        = Mathf.RoundToInt((spec.gold        + trait.gold)        * levelMultiplier),
            production  = Mathf.RoundToInt((spec.production  + trait.production)  * levelMultiplier),
            food        = Mathf.RoundToInt((spec.food        + trait.food)        * levelMultiplier),
            science     = Mathf.RoundToInt((spec.science     + trait.science)     * levelMultiplier),
            culture     = Mathf.RoundToInt((spec.culture     + trait.culture)     * levelMultiplier),
            faith       = Mathf.RoundToInt((spec.faith       + trait.faith)       * levelMultiplier),
            combat      = Mathf.RoundToInt((spec.combat      + trait.combat)      * levelMultiplier),
            cityDefense = Mathf.RoundToInt((spec.cityDefense + trait.cityDefense) * levelMultiplier)
        };
    }

    // Returns the default bonuses for a specialization
    public static GovernorBonuses GetSpecializationBonuses(Specialization spec)
    {
        switch (spec)
        {
            case Specialization.Military:
                return new GovernorBonuses { combat = 5, cityDefense = 5 };
            case Specialization.Economic:
                return new GovernorBonuses { gold = 5, production = 2 };
            case Specialization.Scientific:
                return new GovernorBonuses { science = 5 };
            case Specialization.Cultural:
                return new GovernorBonuses { culture = 5 };
            case Specialization.Religious:
                return new GovernorBonuses { faith = 5 };
            case Specialization.Industrial:
                return new GovernorBonuses { production = 5 };
            default:
                return new GovernorBonuses();
        }
    }

    // ===================== CK-Lite Personality & Opinion =====================

    public bool HasPersonality(PersonalityTrait trait) => PersonalityTraits.Contains(trait);

    /// <summary>
    /// Assign 2-3 random personality traits. Called once at governor creation.
    /// Avoids contradictory pairs (Loyal/Ambitious, Brave/Craven, etc.).
    /// </summary>
    public void AssignRandomPersonality()
    {
        var pool = new List<PersonalityTrait>((PersonalityTrait[])System.Enum.GetValues(typeof(PersonalityTrait)));
        int count = Random.Range(2, 4); // 2 or 3 traits
        PersonalityTraits.Clear();

        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            var picked = pool[idx];
            PersonalityTraits.Add(picked);
            pool.RemoveAt(idx);

            // Remove contradictions
            switch (picked)
            {
                case PersonalityTrait.Loyal:     pool.Remove(PersonalityTrait.Ambitious); break;
                case PersonalityTrait.Ambitious:  pool.Remove(PersonalityTrait.Loyal); pool.Remove(PersonalityTrait.Content); break;
                case PersonalityTrait.Generous:   pool.Remove(PersonalityTrait.Greedy); break;
                case PersonalityTrait.Greedy:     pool.Remove(PersonalityTrait.Generous); break;
                case PersonalityTrait.Brave:      pool.Remove(PersonalityTrait.Craven); break;
                case PersonalityTrait.Craven:     pool.Remove(PersonalityTrait.Brave); break;
                case PersonalityTrait.Honest:     pool.Remove(PersonalityTrait.Deceitful); break;
                case PersonalityTrait.Deceitful:  pool.Remove(PersonalityTrait.Honest); break;
                case PersonalityTrait.Zealous:    pool.Remove(PersonalityTrait.Cynical); break;
                case PersonalityTrait.Cynical:    pool.Remove(PersonalityTrait.Zealous); break;
                case PersonalityTrait.Content:    pool.Remove(PersonalityTrait.Ambitious); break;
            }
        }

        // Set initial opinion from personality baseline
        Opinion = GetPersonalityBaselineOpinion();
    }

    /// <summary>
    /// Permanent opinion baseline from personality traits.
    /// </summary>
    public float GetPersonalityBaselineOpinion()
    {
        float baseline = 50f;
        foreach (var t in PersonalityTraits)
        {
            switch (t)
            {
                case PersonalityTrait.Loyal:     baseline += 20f; break;
                case PersonalityTrait.Ambitious:  baseline -= 10f; break;
                case PersonalityTrait.Content:    baseline += 15f; break;
                case PersonalityTrait.Greedy:     baseline -= 5f;  break;
                case PersonalityTrait.Cruel:      baseline -= 8f;  break;
            }
        }
        return Mathf.Clamp(baseline, -100f, 100f);
    }

    /// <summary>
    /// Add a temporary or permanent opinion modifier (e.g. "Received Gift" +15, 10 turns).
    /// </summary>
    public void AddOpinionModifier(string reason, float value, int duration = -1)
    {
        // Personality modifies gift effectiveness
        if (reason.Contains("Gift") || reason.Contains("gift"))
        {
            if (HasPersonality(PersonalityTrait.Generous)) value *= 1.5f;
            if (HasPersonality(PersonalityTrait.Greedy))   value *= 0.5f;
        }
        if (reason.Contains("Threat") || reason.Contains("threat"))
        {
            if (HasPersonality(PersonalityTrait.Craven))  value = Mathf.Abs(value); // threats work on cowards
            if (HasPersonality(PersonalityTrait.Brave))   value *= 0.3f; // brave ones shrug off threats
        }

        OpinionModifiers.Add(new OpinionModifier(reason, value, duration));
    }

    /// <summary>
    /// Round-aware opinion tick. Call once per civilization turn from
    /// Civilization.TickGovernorPolitics. A repeated call in the same round is a
    /// no-op, so temporary modifiers can never decay twice in one turn.
    /// Returns the computed opinion value (also stored in Opinion).
    /// </summary>
    public float TickOpinionForTurn(int round)
    {
        if (round == LastOpinionTickRound)
            return Opinion;
        LastOpinionTickRound = round;
        return TickOpinion();
    }

    /// <summary>
    /// Unguarded opinion tick. Decays temporary modifiers, recalculates opinion.
    /// Prefer TickOpinionForTurn(round) so the tick cannot run twice per round.
    /// </summary>
    private float TickOpinion()
    {
        // Decay temporary modifiers
        for (int i = OpinionModifiers.Count - 1; i >= 0; i--)
        {
            var mod = OpinionModifiers[i];
            if (mod.turnsRemaining == 0)
            {
                OpinionModifiers.RemoveAt(i);
                continue;
            }
            if (mod.turnsRemaining > 0)
            {
                mod.turnsRemaining--;
                OpinionModifiers[i] = mod;
            }
        }

        // Recalculate: baseline + sum of active modifiers
        float total = GetPersonalityBaselineOpinion();
        foreach (var mod in OpinionModifiers)
            total += mod.value;

        // Ambitious governors carry a flat -1 opinion malus while unappeased
        // (recomputed from baseline each tick, so it does not accumulate).
        if (HasPersonality(PersonalityTrait.Ambitious))
            total -= 1f;

        // Far-flung governors are harder to keep loyal to the capital.
        total -= PoliticalDistanceUtility.GetGovernorDistancePenalty(this);

        Opinion = Mathf.Clamp(total, LoyaltyFloor, LoyaltyCeiling);
        return Opinion;
    }

    /// <summary>
    /// Opinion mapped to loyalty contribution for city: high opinion = big loyalty bonus, negative = loyalty drain.
    /// Range: roughly -15 to +20. Clamped by LoyaltyFloor/Ceiling.
    /// </summary>
    public float GetLoyaltyContribution()
    {
        float clamped = Mathf.Clamp(Opinion, LoyaltyFloor, LoyaltyCeiling);
        // Map -100..100 opinion to -15..+20 loyalty per turn
        return Mathf.Lerp(-15f, 20f, (clamped + 100f) / 200f);
    }

    // ===================== Political State =====================

    /// <summary>
    /// Add a grievance stack for a specific source. Automatically fires a matching opinion penalty.
    /// </summary>
    public void AddGrievance(GrievanceSource source, int stacks = 1)
    {
        if (!Grievances.ContainsKey(source)) Grievances[source] = 0;
        Grievances[source] += stacks;

        // Each grievance source maps to a canonical opinion hit
        float opinionHit = GetGrievanceOpinionHit(source) * stacks;
        string reason = $"Grievance: {source}";
        AddOpinionModifier(reason, opinionHit, 30);
    }

    /// <summary>
    /// Clear all grievance stacks for a source (e.g. after a concession is made).
    /// Does NOT retroactively undo opinion modifiers; those decay naturally.
    /// </summary>
    public void ClearGrievance(GrievanceSource source)
    {
        Grievances.Remove(source);
    }

    /// <summary>Returns total grievance stack count across all sources.</summary>
    public int TotalGrievances()
    {
        int total = 0;
        foreach (var kv in Grievances) total += kv.Value;
        return total;
    }

    /// <summary>
    /// How many total grievance stacks does this governor have that could justify rebellion?
    /// Rebellion is plausible when score exceeds PowerRank * 2.
    /// </summary>
    public bool IsRebellionReady() => TotalGrievances() >= PowerRank * 2 && Opinion < -20f;

    private static float GetGrievanceOpinionHit(GrievanceSource source)
    {
        switch (source)
        {
            case GrievanceSource.CityReassigned:         return -15f;
            case GrievanceSource.OverruledDecision:      return -8f;
            case GrievanceSource.TaxIncreased:           return -5f;
            case GrievanceSource.TitleRevoked:           return -20f;
            case GrievanceSource.CouncilSeatDenied:      return -12f;
            case GrievanceSource.ReligionForced:         return -18f;
            case GrievanceSource.PrivilegeRevoked:       return -10f;
            case GrievanceSource.PublicInsult:           return -15f;
            case GrievanceSource.AllianceBrokenWithAlly: return -6f;
            case GrievanceSource.WarLosses:              return -10f;
            default:                                      return -5f;
        }
    }

    private int ComputeAmbitionScore()
    {
        int score = HasPersonality(PersonalityTrait.Ambitious) ? 40 : 0;
        score += PowerRank * 3;                         // bigger domain = more ambitious
        score += Mathf.Max(0, (int)(50f - Opinion));    // unhappier = schemes more
        return Mathf.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Re-evaluate whether this governor should be eligible for a council seat.
    /// Call this whenever city/herd assignments change or government changes.
    /// Eligibility threshold: PowerRank >= 4 (e.g. a level-2 city, two level-1 cities, or equivalent herds).
    /// </summary>
    public void RefreshCouncilEligibility()
    {
        IsCouncilEligible = PowerRank >= 4;
    }

    // ===================== Save/Load =====================

    /// <summary>
    /// Restore intrinsic governor character/political state from save data.
    /// Overwrites the randomly rolled personality assigned at creation.
    /// </summary>
    public void RestorePoliticalState(
        int id,
        int level,
        int experience,
        List<PersonalityTrait> personalityTraits,
        float opinion,
        List<OpinionModifier> opinionModifiers,
        ReligionData personalReligion,
        CultureData personalCulture,
        float loyaltyFloor,
        float loyaltyCeiling,
        Dictionary<GrievanceSource, int> grievances,
        bool isCouncilEligible,
        bool isInRebellion,
        int lastOpinionTickRound)
    {
        Id = id;
        Level = Mathf.Max(1, level);
        Experience = Mathf.Max(0, experience);

        PersonalityTraits = personalityTraits != null
            ? new List<PersonalityTrait>(personalityTraits)
            : new List<PersonalityTrait>();

        OpinionModifiers = opinionModifiers != null
            ? new List<OpinionModifier>(opinionModifiers)
            : new List<OpinionModifier>();

        PersonalReligion = personalReligion;
        PersonalCulture = personalCulture;
        LoyaltyFloor = loyaltyFloor;
        LoyaltyCeiling = loyaltyCeiling;

        Grievances = grievances != null
            ? new Dictionary<GrievanceSource, int>(grievances)
            : new Dictionary<GrievanceSource, int>();

        IsCouncilEligible = isCouncilEligible;
        IsInRebellion = isInRebellion;
        LastOpinionTickRound = lastOpinionTickRound;
        Opinion = Mathf.Clamp(opinion, LoyaltyFloor, LoyaltyCeiling);
    }
}

[System.Serializable]
public class PromotionBonus
{
    public string name;
    [TextArea(2, 4)]
    public string description;
    public int requiredLevel;
    
    [Header("Bonus Values")]
    public int additionalGoldBonus;
    public int additionalProductionBonus;
    public int additionalFoodBonus;
    public int additionalScienceBonus;
    public int additionalCultureBonus;
    public int additionalFaithBonus;
    public int additionalCombatBonus;
    public int additionalCityDefenseBonus;
} 