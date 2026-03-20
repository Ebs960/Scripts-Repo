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
    /// Called once per turn. Decays temporary modifiers, recalculates opinion.
    /// Returns the computed opinion value (also stored in Opinion).
    /// </summary>
    public float TickOpinion()
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

        // Ambitious governors slowly drift negative if not appeased
        if (HasPersonality(PersonalityTrait.Ambitious))
            total -= 1f; // -1 per turn passive drift

        Opinion = Mathf.Clamp(total, -100f, 100f);
        return Opinion;
    }

    /// <summary>
    /// Opinion mapped to loyalty contribution for city: high opinion = big loyalty bonus, negative = loyalty drain.
    /// Range: roughly -15 to +20.
    /// </summary>
    public float GetLoyaltyContribution()
    {
        // Map -100..100 opinion to -15..+20 loyalty per turn
        return Mathf.Lerp(-15f, 20f, (Opinion + 100f) / 200f);
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