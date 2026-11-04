using System.Collections.Generic;
using UnityEngine;

public readonly struct ReligionFoundingInfo
{
    public ReligionData Religion { get; }
    public string Name => Religion.religionName;
    public Sprite Icon => Religion.icon;
    public int FaithCost { get; }
    public string Requirements { get; }
    public bool IsFounded { get; }
    public ReligionAvailabilityResult Availability { get; }

    public ReligionFoundingInfo(Civilization civ, ReligionData religion, City city, ReligionManager manager)
    {
        Religion = religion;
        FaithCost = civ.GetReligionCost(religion);
        Requirements = ReligionPresentation.ReligionRequirements(religion);
        IsFounded = manager != null && manager.GetFoundedReligions().Contains(religion);
        Availability = manager != null ? manager.CanFoundReligion(civ, religion, city, true)
            : ReligionAvailabilityResult.Fail("Religion manager is unavailable.");
    }
}

public readonly struct CityReligionInfo
{
    public City City { get; }
    public string Name => City.cityName;
    public ReligionData MajorityReligion { get; }
    public bool HasHolySite { get; }
    public bool MatchesStateReligion { get; }

    public CityReligionInfo(City city, ReligionData majorityReligion, ReligionData stateReligion)
    {
        City = city;
        MajorityReligion = majorityReligion;
        HasHolySite = city.HasHolySite();
        MatchesStateReligion = majorityReligion != null && majorityReligion == stateReligion;
    }
}

public static class ReligionPresentation
{
    public static string ReligionName(ReligionData religion) => religion != null ? religion.religionName : "None";

    public static string ReligionRequirements(ReligionData religion)
    {
        var requirements = new List<string>();
        switch (religion.pantheonRequirementMode)
        {
            case PantheonRequirementMode.None:
                requirements.Add("Pantheon: not required");
                break;
            case PantheonRequirementMode.Any:
                requirements.Add("Pantheon: any");
                break;
            case PantheonRequirementMode.MinimumTier:
                requirements.Add($"Pantheon tier: {religion.minimumPantheonTier} or higher");
                break;
            case PantheonRequirementMode.Specific:
                var names = new List<string>();
                if (religion.compatiblePantheons != null)
                    foreach (var pantheon in religion.compatiblePantheons)
                        if (pantheon != null) names.Add(pantheon.pantheonName);
                requirements.Add("Pantheon: " + (names.Count > 0 ? string.Join(", ", names) : "none configured"));
                break;
        }
        var cultures = new List<string>();
        if (religion.requiredCultures != null)
            foreach (var culture in religion.requiredCultures)
                if (culture != null) cultures.Add(culture.name);
        requirements.Add("Cultures: " + (cultures.Count > 0 ? string.Join(", ", cultures) : "none required"));
        requirements.Add(religion.useMinimumAge ? $"Minimum age: {religion.minimumAge}" : "Minimum age: none");
        requirements.Add("Holy Site: required in the selected owned city");
        return string.Join("\n", requirements);
    }

    public static List<CityReligionInfo> BuildCityOverview(Civilization civ, ReligionManager manager)
    {
        var rows = new List<CityReligionInfo>();
        if (civ?.cities != null)
            foreach (var city in civ.cities)
                if (city != null) rows.Add(new CityReligionInfo(city, manager != null ? manager.GetCityMajorityReligion(city) : null, civ.StateReligion));
        return rows;
    }

    public static string CityOverviewText(IReadOnlyList<CityReligionInfo> cities)
    {
        var rows = new List<string>();
        foreach (var city in cities)
            rows.Add($"{city.Name}: {ReligionName(city.MajorityReligion)} | Holy Site: {(city.HasHolySite ? "Yes" : "No")} | State religion: {(city.MatchesStateReligion ? "Matches" : "No match")}");
        return rows.Count > 0 ? string.Join("\n", rows) : "No cities.";
    }
}

public static class ReligionEffectDescriptions
{
    public static string BuildBeliefEffectSummary(BeliefData belief, bool compact = false)
    {
        if (belief == null) return compact ? string.Empty : "No belief assigned.";
        var parts = new List<string>();
        if (belief.faithCost > 0) parts.Add($"{belief.faithCost} Faith");
        AppendSharedYieldModifiers(parts, belief.extraFaithInHolySite, belief.extraFoodInHolySite, belief.extraProductionInHolySite,
            belief.goldPerCity, belief.culturePerCity, belief.happinessBonus, belief.combatStrengthNearHolySite,
            belief.growthRateModifier, belief.productionRateModifier,
            belief.foodModifier, belief.productionModifier, belief.goldModifier, belief.scienceModifier, belief.cultureModifier, belief.faithModifier);
        AppendImprovementBonuses(parts, belief.improvementBonuses, compact);
        AppendHerdYieldBonuses(parts, belief.herdYieldBonuses, compact);
        AppendAttritionBonuses(parts, belief.attritionBonuses);
        if (belief.herdStarvationPercentReduction > 0f) parts.Add($"-{belief.herdStarvationPercentReduction:P0} herd starvation");
        if (!compact && !string.IsNullOrWhiteSpace(belief.description)) parts.Insert(0, belief.description.Trim());
        if (parts.Count == 0)
            return string.IsNullOrWhiteSpace(belief.description) ? "No immediate effect listed." : belief.description.Trim();
        string summary = string.Join(compact ? " \u2022 " : "\n", parts);
        if (compact && summary.Length > 180) summary = summary.Substring(0, 177) + "...";
        return summary;
    }

    public static string BuildPantheonEffectSummary(PantheonData pantheon, bool compact = false)
    {
        if (pantheon == null) return compact ? string.Empty : "No pantheon assigned.";
        var parts = new List<string>();
        var bonuses = pantheon.bonuses;
        if (!compact && !string.IsNullOrWhiteSpace(pantheon.description)) parts.Add(pantheon.description.Trim());
        if (bonuses != null)
        {
            AppendPantheonModifiers(parts, bonuses);
            AppendImprovementBonuses(parts, bonuses.improvementBonuses, compact);
            AppendHerdYieldBonuses(parts, bonuses.herdYieldBonuses, compact);
            AppendAttritionBonuses(parts, bonuses.attritionBonuses);
            if (bonuses.herdStarvationPercentReduction > 0f) parts.Add($"-{bonuses.herdStarvationPercentReduction:P0} herd starvation");
        }
        if (parts.Count == 0)
            return string.IsNullOrWhiteSpace(pantheon.description) ? "No pantheon bonuses listed." : pantheon.description.Trim();
        string summary = string.Join(compact ? " \u2022 " : "\n", parts);
        if (compact && summary.Length > 180) summary = summary.Substring(0, 177) + "...";
        return summary;
    }

    private static void AppendPantheonModifiers(List<string> parts, PantheonBonuses bonuses)
    {
        if (bonuses == null) return;
        AppendSignedInt(parts, bonuses.attackBonus, "Attack");
        AppendSignedInt(parts, bonuses.defenseBonus, "Defense");
        AppendSignedInt(parts, bonuses.movementBonus, "Movement");
        AppendSignedPercent(parts, bonuses.foodModifier, "Food");
        AppendSignedPercent(parts, bonuses.productionModifier, "Production");
        AppendSignedPercent(parts, bonuses.goldModifier, "Gold");
        AppendSignedPercent(parts, bonuses.scienceModifier, "Science");
        AppendSignedPercent(parts, bonuses.cultureModifier, "Culture");
        AppendSignedPercent(parts, bonuses.faithModifier, "Faith");
    }

    private static void AppendSharedYieldModifiers(List<string> parts,
        int extraFaithInHolySite, int extraFoodInHolySite, int extraProductionInHolySite,
        int goldPerCity, int culturePerCity, int happinessBonus, int combatStrengthNearHolySite,
        float growthRateModifier, float productionRateModifier,
        float foodModifier, float productionModifier, float goldModifier, float scienceModifier, float cultureModifier, float faithModifier)
    {
        AppendSignedInt(parts, extraFaithInHolySite, "Faith in Holy Site");
        AppendSignedInt(parts, extraFoodInHolySite, "Food in Holy Site");
        AppendSignedInt(parts, extraProductionInHolySite, "Production in Holy Site");
        AppendSignedInt(parts, goldPerCity, "Gold per city");
        AppendSignedInt(parts, culturePerCity, "Culture per city");
        AppendSignedInt(parts, happinessBonus, "Happiness");
        AppendSignedInt(parts, combatStrengthNearHolySite, "Combat strength near Holy Site");
        AppendSignedPercent(parts, growthRateModifier, "Growth");
        AppendSignedPercent(parts, productionRateModifier, "Production rate");
        AppendSignedPercent(parts, foodModifier, "Food");
        AppendSignedPercent(parts, productionModifier, "Production");
        AppendSignedPercent(parts, goldModifier, "Gold");
        AppendSignedPercent(parts, scienceModifier, "Science");
        AppendSignedPercent(parts, cultureModifier, "Culture");
        AppendSignedPercent(parts, faithModifier, "Faith");
    }

    private static void AppendImprovementBonuses(List<string> parts, ImprovementYieldBonus[] bonuses, bool compact)
    {
        if (bonuses == null || bonuses.Length == 0) return;
        int added = 0;
        foreach (var bonus in bonuses)
        {
            if (bonus == null) continue;
            string summary = FormatImprovementBonus(bonus);
            if (string.IsNullOrEmpty(summary)) continue;
            parts.Add(summary);
            added++;
            if (compact && added >= 2)
            {
                if (bonuses.Length > added) parts.Add($"+{bonuses.Length - added} more improvement bonus(es)");
                break;
            }
        }
    }

    private static string FormatImprovementBonus(ImprovementYieldBonus bonus)
    {
        if (bonus == null) return string.Empty;
        var yieldParts = new List<string>();
        AppendYieldValue(yieldParts, bonus.foodAdd, "Food");
        AppendYieldValue(yieldParts, bonus.productionAdd, "Production");
        AppendYieldValue(yieldParts, bonus.goldAdd, "Gold");
        AppendYieldValue(yieldParts, bonus.scienceAdd, "Science");
        AppendYieldValue(yieldParts, bonus.cultureAdd, "Culture");
        AppendYieldValue(yieldParts, bonus.faithAdd, "Faith");
        AppendYieldValue(yieldParts, bonus.policyPointsAdd, "Policy");
        AppendYieldPercent(yieldParts, bonus.foodPct, "Food");
        AppendYieldPercent(yieldParts, bonus.productionPct, "Production");
        AppendYieldPercent(yieldParts, bonus.goldPct, "Gold");
        AppendYieldPercent(yieldParts, bonus.sciencePct, "Science");
        AppendYieldPercent(yieldParts, bonus.culturePct, "Culture");
        AppendYieldPercent(yieldParts, bonus.faithPct, "Faith");
        AppendYieldPercent(yieldParts, bonus.policyPointsPct, "Policy");
        if (yieldParts.Count == 0) return string.Empty;
        string improvementName = bonus.improvement != null && !string.IsNullOrWhiteSpace(bonus.improvement.improvementName)
            ? bonus.improvement.improvementName : "Improvements";
        return $"{improvementName}: {string.Join(", ", yieldParts)}";
    }

    private static void AppendAttritionBonuses(List<string> parts, AttritionModifierBonus[] bonuses)
    {
        if (bonuses == null) return;
        foreach (var bonus in bonuses)
        {
            if (bonus == null) continue;
            AppendReduction(parts, bonus.winterDamageReductionPct, "winter attrition");
            AppendReduction(parts, bonus.famineDamageReductionPct, "famine attrition");
            AppendReduction(parts, bonus.biomeDamageReductionPct, "biome damage");
        }
    }

    private static void AppendHerdYieldBonuses(List<string> parts, HerdYieldBonus[] bonuses, bool compact)
    {
        if (bonuses == null || bonuses.Length == 0) return;
        int added = 0;
        foreach (var bonus in bonuses)
        {
            if (bonus == null) continue;
            string summary = FormatHerdYieldBonus(bonus);
            if (string.IsNullOrEmpty(summary)) continue;
            parts.Add(summary);
            added++;
            if (compact && added >= 2)
            {
                if (bonuses.Length > added) parts.Add($"+{bonuses.Length - added} more herd bonus(es)");
                break;
            }
        }
    }

    private static string FormatHerdYieldBonus(HerdYieldBonus bonus)
    {
        if (bonus == null) return string.Empty;
        var yieldParts = new List<string>();
        AppendYieldValue(yieldParts, bonus.foodAdd, "Food");
        AppendYieldValue(yieldParts, bonus.productionAdd, "Production");
        AppendYieldValue(yieldParts, bonus.goldAdd, "Gold");
        AppendYieldValue(yieldParts, bonus.scienceAdd, "Science");
        AppendYieldValue(yieldParts, bonus.cultureAdd, "Culture");
        AppendYieldValue(yieldParts, bonus.faithAdd, "Faith");
        AppendYieldValue(yieldParts, bonus.policyPointsAdd, "Policy");
        AppendYieldPercent(yieldParts, bonus.foodPct, "Food");
        AppendYieldPercent(yieldParts, bonus.productionPct, "Production");
        AppendYieldPercent(yieldParts, bonus.goldPct, "Gold");
        AppendYieldPercent(yieldParts, bonus.sciencePct, "Science");
        AppendYieldPercent(yieldParts, bonus.culturePct, "Culture");
        AppendYieldPercent(yieldParts, bonus.faithPct, "Faith");
        AppendYieldPercent(yieldParts, bonus.policyPointsPct, "Policy");
        if (yieldParts.Count == 0) return string.Empty;
        string speciesLabel = bonus.useSpeciesFilter ? $"Herds ({bonus.species})" : "All Herds";
        return $"{speciesLabel}: {string.Join(", ", yieldParts)}";
    }

    private static void AppendSignedInt(List<string> parts, float value, string label)
    {
        if (Mathf.Abs(value) <= 0.0001f) return;
        parts.Add($"{(value > 0 ? "+" : string.Empty)}{value:0.##} {label}");
    }

    private static void AppendSignedPercent(List<string> parts, float value, string label)
    {
        if (Mathf.Abs(value) <= 0.0001f) return;
        parts.Add($"{(value > 0 ? "+" : string.Empty)}{value:P0} {label}");
    }

    private static void AppendReduction(List<string> parts, float value, string label)
    {
        if (Mathf.Abs(value) <= 0.0001f) return;
        string prefix = value >= 0f ? "-" : "+";
        parts.Add($"{prefix}{Mathf.Abs(value):P0} {label}");
    }

    private static void AppendYieldValue(List<string> parts, int value, string label)
    {
        if (value == 0) return;
        parts.Add($"{(value > 0 ? "+" : string.Empty)}{value} {label}");
    }

    private static void AppendYieldPercent(List<string> parts, float value, string label)
    {
        if (Mathf.Abs(value) <= 0.0001f) return;
        parts.Add($"{(value > 0 ? "+" : string.Empty)}{value:P0} {label}");
    }
}
