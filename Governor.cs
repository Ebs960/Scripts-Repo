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

    // Traits (ScriptableObjects)
    public List<GovernorTrait> Traits { get; private set; } = new List<GovernorTrait>();

    // Stat tracking for trait unlocking
    private Dictionary<TraitTrigger, int> stats = new Dictionary<TraitTrigger, int>();

    public Governor(int id, string name, Specialization spec)
    {
        Id = id;
        Name = name;
        specialization = spec;
        Level = 1;
        Experience = 0;
        Cities = new List<City>();
        Traits = new List<GovernorTrait>();
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
        var civ = Cities.FirstOrDefault()?.owner;
        if (civ == null) return;

        foreach (var trait in civ.unlockedGovernorTraits)
        {
            // Skip if we already have this trait
            if (Traits.Contains(trait)) continue;

            // Check if we meet the requirement
            if (GetStat(trait.triggerType) >= trait.requiredValue)
            {
                Traits.Add(trait);
                Debug.Log($"Governor {Name} unlocked trait: {trait.traitName}");
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
        Debug.Log($"Governor {Name} reached level {Level}!");
        
        // Notify any cities this governor is assigned to
        foreach (var city in Cities)
        {
            // You might want to refresh city UI or apply new bonuses here
            city.RefreshGovernorBonuses();
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