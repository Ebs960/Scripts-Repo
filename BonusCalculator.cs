using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Utility class for calculating combined bonuses from technologies and cultures
/// </summary>
public static class BonusCalculator
{
    [System.Serializable]
    public struct CombinedBonuses
    {
        [Header("Percentage Modifiers")]
        public float foodModifier;
        public float productionModifier;
        public float goldModifier;
        public float scienceModifier;
        public float cultureModifier;
        public float faithModifier;
        public float attackBonus;
        public float defenseBonus;
        public float movementBonus;
        
        [Header("Flat Bonuses")]
        public int flatFoodBonus;
        public int flatProductionBonus;
        public int flatGoldBonus;
        public int flatScienceBonus;
        public int flatCultureBonus;
        public int flatFaithBonus;
        
        [Header("Unlocked Content")]
        public int additionalGovernorSlots;
        
        [Header("Limit Modifiers")]
        public List<UnitLimitModifier> unitLimitModifiers;
        public List<BuildingLimitModifier> buildingLimitModifiers;
    }
    
    /// <summary>
    /// Calculate combined bonuses from a list of technologies
    /// </summary>
    public static CombinedBonuses CalculateTechBonuses(List<TechData> technologies)
    {
        CombinedBonuses result = new CombinedBonuses();
        
        if (technologies == null || technologies.Count == 0)
            return result;
        
        foreach (var tech in technologies)
        {
            if (tech == null) continue;
            
            // Add percentage modifiers
            result.foodModifier += tech.foodModifier;
            result.productionModifier += tech.productionModifier;
            result.goldModifier += tech.goldModifier;
            result.scienceModifier += tech.scienceModifier;
            result.cultureModifier += tech.cultureModifier;
            result.faithModifier += tech.faithModifier;
            result.attackBonus += tech.attackBonus;
            result.defenseBonus += tech.defenseBonus;
            result.movementBonus += tech.movementBonus;
            
            // Add flat bonuses
            result.flatFoodBonus += tech.flatFoodBonus;
            result.flatProductionBonus += tech.flatProductionBonus;
            result.flatGoldBonus += tech.flatGoldBonus;
            result.flatScienceBonus += tech.flatScienceBonus;
            result.flatCultureBonus += tech.flatCultureBonus;
            result.flatFaithBonus += tech.flatFaithBonus;
            
            // Add other bonuses
            result.additionalGovernorSlots += tech.additionalGovernorSlots;
            
            // Collect limit modifiers
            if (result.unitLimitModifiers == null)
                result.unitLimitModifiers = new List<UnitLimitModifier>();
            if (result.buildingLimitModifiers == null)
                result.buildingLimitModifiers = new List<BuildingLimitModifier>();
            
            if (tech.unitLimitModifiers != null)
                result.unitLimitModifiers.AddRange(tech.unitLimitModifiers);
            if (tech.buildingLimitModifiers != null)
                result.buildingLimitModifiers.AddRange(tech.buildingLimitModifiers);
        }
        
        return result;
    }
    
    /// <summary>
    /// Calculate combined bonuses from a list of cultures
    /// </summary>
    public static CombinedBonuses CalculateCultureBonuses(List<CultureData> cultures)
    {
        CombinedBonuses result = new CombinedBonuses();
        
        if (cultures == null || cultures.Count == 0)
            return result;
        
        foreach (var culture in cultures)
        {
            if (culture == null) continue;
            
            // Add percentage modifiers
            result.foodModifier += culture.foodModifier;
            result.productionModifier += culture.productionModifier;
            result.goldModifier += culture.goldModifier;
            result.scienceModifier += culture.scienceModifier;
            result.cultureModifier += culture.cultureModifier;
            result.faithModifier += culture.faithModifier;
            result.attackBonus += culture.attackBonus;
            result.defenseBonus += culture.defenseBonus;
            result.movementBonus += culture.movementBonus;
            
            // Add flat bonuses
            result.flatFoodBonus += culture.flatFoodBonus;
            result.flatProductionBonus += culture.flatProductionBonus;
            result.flatGoldBonus += culture.flatGoldBonus;
            result.flatScienceBonus += culture.flatScienceBonus;
            result.flatCultureBonus += culture.flatCultureBonus;
            result.flatFaithBonus += culture.flatFaithBonus;
            
            // Add other bonuses
            result.additionalGovernorSlots += culture.additionalGovernorSlots;
            
            // Collect limit modifiers
            if (result.unitLimitModifiers == null)
                result.unitLimitModifiers = new List<UnitLimitModifier>();
            if (result.buildingLimitModifiers == null)
                result.buildingLimitModifiers = new List<BuildingLimitModifier>();
            
            if (culture.unitLimitModifiers != null)
                result.unitLimitModifiers.AddRange(culture.unitLimitModifiers);
            if (culture.buildingLimitModifiers != null)
                result.buildingLimitModifiers.AddRange(culture.buildingLimitModifiers);
        }
        
        return result;
    }
    
    /// <summary>
    /// Calculate combined bonuses from both technologies and cultures
    /// </summary>
    public static CombinedBonuses CalculateTotalBonuses(List<TechData> technologies, List<CultureData> cultures)
    {
        var techBonuses = CalculateTechBonuses(technologies);
        var cultureBonuses = CalculateCultureBonuses(cultures);
        
        return CombineBonuses(techBonuses, cultureBonuses);
    }
    
    /// <summary>
    /// Combine two bonus structures
    /// </summary>
    public static CombinedBonuses CombineBonuses(CombinedBonuses bonuses1, CombinedBonuses bonuses2)
    {
        CombinedBonuses result = new CombinedBonuses();
        
        // Combine percentage modifiers
        result.foodModifier = bonuses1.foodModifier + bonuses2.foodModifier;
        result.productionModifier = bonuses1.productionModifier + bonuses2.productionModifier;
        result.goldModifier = bonuses1.goldModifier + bonuses2.goldModifier;
        result.scienceModifier = bonuses1.scienceModifier + bonuses2.scienceModifier;
        result.cultureModifier = bonuses1.cultureModifier + bonuses2.cultureModifier;
        result.faithModifier = bonuses1.faithModifier + bonuses2.faithModifier;
        result.attackBonus = bonuses1.attackBonus + bonuses2.attackBonus;
        result.defenseBonus = bonuses1.defenseBonus + bonuses2.defenseBonus;
        result.movementBonus = bonuses1.movementBonus + bonuses2.movementBonus;
        
        // Combine flat bonuses
        result.flatFoodBonus = bonuses1.flatFoodBonus + bonuses2.flatFoodBonus;
        result.flatProductionBonus = bonuses1.flatProductionBonus + bonuses2.flatProductionBonus;
        result.flatGoldBonus = bonuses1.flatGoldBonus + bonuses2.flatGoldBonus;
        result.flatScienceBonus = bonuses1.flatScienceBonus + bonuses2.flatScienceBonus;
        result.flatCultureBonus = bonuses1.flatCultureBonus + bonuses2.flatCultureBonus;
        result.flatFaithBonus = bonuses1.flatFaithBonus + bonuses2.flatFaithBonus;
        
        // Combine other bonuses
        result.additionalGovernorSlots = bonuses1.additionalGovernorSlots + bonuses2.additionalGovernorSlots;
        
        // Combine limit modifiers
        result.unitLimitModifiers = new List<UnitLimitModifier>();
        result.buildingLimitModifiers = new List<BuildingLimitModifier>();
        
        if (bonuses1.unitLimitModifiers != null)
            result.unitLimitModifiers.AddRange(bonuses1.unitLimitModifiers);
        if (bonuses2.unitLimitModifiers != null)
            result.unitLimitModifiers.AddRange(bonuses2.unitLimitModifiers);
        
        if (bonuses1.buildingLimitModifiers != null)
            result.buildingLimitModifiers.AddRange(bonuses1.buildingLimitModifiers);
        if (bonuses2.buildingLimitModifiers != null)
            result.buildingLimitModifiers.AddRange(bonuses2.buildingLimitModifiers);
        
        return result;
    }
    
    /// <summary>
    /// Apply bonuses to a base yield amount
    /// </summary>
    public static int ApplyBonuses(int baseYield, float percentageModifier, int flatBonus)
    {
        float modifiedYield = baseYield * (1f + percentageModifier);
        return Mathf.RoundToInt(modifiedYield) + flatBonus;
    }
    
    /// <summary>
    /// Apply yield bonuses to calculate final yields
    /// </summary>
    public static YieldCollection ApplyYieldBonuses(YieldCollection baseYields, CombinedBonuses bonuses)
    {
        YieldCollection finalYields = new YieldCollection();
        
        finalYields.food = ApplyBonuses(baseYields.food, bonuses.foodModifier, bonuses.flatFoodBonus);
        finalYields.production = ApplyBonuses(baseYields.production, bonuses.productionModifier, bonuses.flatProductionBonus);
        finalYields.gold = ApplyBonuses(baseYields.gold, bonuses.goldModifier, bonuses.flatGoldBonus);
        finalYields.science = ApplyBonuses(baseYields.science, bonuses.scienceModifier, bonuses.flatScienceBonus);
        finalYields.culture = ApplyBonuses(baseYields.culture, bonuses.cultureModifier, bonuses.flatCultureBonus);
        finalYields.faith = ApplyBonuses(baseYields.faith, bonuses.faithModifier, bonuses.flatFaithBonus);
        
        return finalYields;
    }
    
    /// <summary>
    /// Get a formatted string describing the bonuses
    /// </summary>
    public static string GetBonusDescription(CombinedBonuses bonuses)
    {
        var description = new System.Text.StringBuilder();
        
        // Percentage modifiers
        if (bonuses.foodModifier != 0)
            description.AppendLine($"Food: {bonuses.foodModifier:+0.0%;-0.0%;+0%}");
        if (bonuses.productionModifier != 0)
            description.AppendLine($"Production: {bonuses.productionModifier:+0.0%;-0.0%;+0%}");
        if (bonuses.goldModifier != 0)
            description.AppendLine($"Gold: {bonuses.goldModifier:+0.0%;-0.0%;+0%}");
        if (bonuses.scienceModifier != 0)
            description.AppendLine($"Science: {bonuses.scienceModifier:+0.0%;-0.0%;+0%}");
        if (bonuses.cultureModifier != 0)
            description.AppendLine($"Culture: {bonuses.cultureModifier:+0.0%;-0.0%;+0%}");
        if (bonuses.faithModifier != 0)
            description.AppendLine($"Faith: {bonuses.faithModifier:+0.0%;-0.0%;+0%}");
        
        // Flat bonuses
        if (bonuses.flatFoodBonus != 0)
            description.AppendLine($"Food: {bonuses.flatFoodBonus:+0;-0;+0} per turn");
        if (bonuses.flatProductionBonus != 0)
            description.AppendLine($"Production: {bonuses.flatProductionBonus:+0;-0;+0} per turn");
        if (bonuses.flatGoldBonus != 0)
            description.AppendLine($"Gold: {bonuses.flatGoldBonus:+0;-0;+0} per turn");
        if (bonuses.flatScienceBonus != 0)
            description.AppendLine($"Science: {bonuses.flatScienceBonus:+0;-0;+0} per turn");
        if (bonuses.flatCultureBonus != 0)
            description.AppendLine($"Culture: {bonuses.flatCultureBonus:+0;-0;+0} per turn");
        if (bonuses.flatFaithBonus != 0)
            description.AppendLine($"Faith: {bonuses.flatFaithBonus:+0;-0;+0} per turn");
        
        // Combat bonuses
        if (bonuses.attackBonus != 0)
            description.AppendLine($"Attack: {bonuses.attackBonus:+0.0%;-0.0%;+0%}");
        if (bonuses.defenseBonus != 0)
            description.AppendLine($"Defense: {bonuses.defenseBonus:+0.0%;-0.0%;+0%}");
        if (bonuses.movementBonus != 0)
            description.AppendLine($"Movement: {bonuses.movementBonus:+0.#;-0.#;+0}");
        
        // Other bonuses
        if (bonuses.additionalGovernorSlots > 0)
            description.AppendLine($"Governor Slots: +{bonuses.additionalGovernorSlots}");
        
        return description.ToString().Trim();
    }
}

/// <summary>
/// Simple yield collection for bonus calculations
/// </summary>
[System.Serializable]
public struct YieldCollection
{
    public int food;
    public int production;
    public int gold;
    public int science;
    public int culture;
    public int faith;
    
    public YieldCollection(int food = 0, int production = 0, int gold = 0, int science = 0, int culture = 0, int faith = 0)
    {
        this.food = food;
        this.production = production;
        this.gold = gold;
        this.science = science;
        this.culture = culture;
        this.faith = faith;
    }
    
    public static YieldCollection operator +(YieldCollection a, YieldCollection b)
    {
        return new YieldCollection(
            a.food + b.food,
            a.production + b.production,
            a.gold + b.gold,
            a.science + b.science,
            a.culture + b.culture,
            a.faith + b.faith
        );
    }
    
    public override string ToString()
    {
        return $"Food: {food}, Production: {production}, Gold: {gold}, Science: {science}, Culture: {culture}, Faith: {faith}";
    }
}
