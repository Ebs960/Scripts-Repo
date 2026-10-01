using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>One line of a government/policy effect summary. Colors are a UI decision; only meaning is stored here.</summary>
public struct PoliticalEffectLine
{
    public string label;
    public string value;
    public bool beneficial;
    public bool harmful;
}

/// <summary>One requirement row with its current met state.</summary>
public struct PoliticalRequirementLine
{
    public string label;
    public bool met;
}

/// <summary>Builds structured, readable summaries of governments and policies so UI tabs stay free of effect logic.</summary>
public static class PoliticalEffectSummaryBuilder
{
    public static List<PoliticalEffectLine> BuildGovernmentEffects(GovernmentData g)
    {
        var lines = new List<PoliticalEffectLine>();
        if (g == null) return lines;

        AddCombat(lines, g.attackBonus, g.meleeAttackBonus, g.rangedAttackBonus, g.cityAttackBonus, g.defenseBonus, g.movementBonus);
        AddYields(lines, g.foodModifier, g.productionModifier, g.goldModifier, g.scienceModifier, g.cultureModifier, g.faithModifier);
        AddFlat(lines, "City cap", g.cityCapModifier, false);
        if (g.institutions != null)
        {
            var m = g.institutions;
            AddPercent(lines, "Administrative efficiency", m.administrativeEfficiencyModifier, false);
            AddPercent(lines, "Distance loyalty penalty", m.distanceLoyaltyPenaltyModifier, true);
            AddPercent(lines, "Policy point generation", m.policyPointGenerationModifier, false);
            AddPercent(lines, "Domestic trade", m.domesticTradeModifier, false);
            AddPercent(lines, "Foreign trade", m.foreignTradeModifier, false);
            AddFlat(lines, "Trade route capacity", m.tradeRouteCapacityBonus, false);
            AddPercent(lines, "Labor productivity", m.laborProductivityModifier, false);
            AddPercent(lines, "Unemployment unhappiness", m.unemploymentUnhappinessModifier, true);
            AddPercent(lines, "Reinforcement speed", m.reinforcementSpeedModifier, false);
            AddPercent(lines, "Military upkeep", m.militaryUpkeepModifier, true);
            AddPercent(lines, "War weariness", m.warWearinessModifier, true);
            AddPercent(lines, "Corruption", m.corruptionModifier, true);
            AddPercent(lines, "Unrest", m.unrestModifier, true);
            AddPercent(lines, "Migration attraction", m.migrationAttractionModifier, false);
            AddPercent(lines, "Planetary loyalty", m.planetaryLoyaltyModifier, false);
            AddPercent(lines, "Cyber defense", m.cyberDefenseModifier, false);
        }
        AddPercent(lines, "Herd starvation losses", -g.herdStarvationPercentReduction, true);
        AddCount(lines, "Targeted bonuses", CountTargeted(g));
        if (g.suppressConventionalPolitics)
            lines.Add(new PoliticalEffectLine { label = "Conventional faction politics", value = "Suppressed", harmful = false, beneficial = false });
        return lines;
    }

    public static List<PoliticalEffectLine> BuildPolicyEffects(PolicyData p)
    {
        var lines = new List<PoliticalEffectLine>();
        if (p == null) return lines;

        AddCombat(lines, p.attackBonus, p.meleeAttackBonus, p.rangedAttackBonus, p.cityAttackBonus, p.defenseBonus, p.movementBonus);
        AddYields(lines, p.foodModifier, p.productionModifier, p.goldModifier, p.scienceModifier, p.cultureModifier, p.faithModifier);
        AddPercent(lines, "Population growth", p.populationGrowthModifier, false);
        AddPercent(lines, "Migration attraction", p.migrationAttractionModifier, false);
        AddPercent(lines, "War weariness", p.warWearinessModifier, true);
        AddPercent(lines, "Corruption", p.corruptionModifier, true);
        AddPercent(lines, "Unrest", p.unrestModifier, true);
        AddPercent(lines, "Administrative efficiency", p.administrativeEfficiencyModifier, false);
        AddPercent(lines, "Distance loyalty penalty", p.distanceLoyaltyPenaltyModifier, true);
        AddPercent(lines, "Policy point generation", p.policyPointGenerationModifier, false);
        AddPercent(lines, "Domestic trade", p.domesticTradeModifier, false);
        AddPercent(lines, "Foreign trade", p.foreignTradeModifier, false);
        AddFlat(lines, "Trade route capacity", p.tradeRouteCapacityBonus, false);
        AddPercent(lines, "Labor productivity", p.laborProductivityModifier, false);
        AddPercent(lines, "Unemployment unhappiness", p.unemploymentUnhappinessModifier, true);
        AddPercent(lines, "Reinforcement speed", p.reinforcementSpeedModifier, false);
        AddPercent(lines, "Military upkeep", p.militaryUpkeepModifier, true);
        AddPercent(lines, "Cyber defense", p.cyberDefenseModifier, false);
        AddPercent(lines, "Cyber offense", p.cyberOffenseModifier, false);
        AddPercent(lines, "Espionage defense", p.espionageDefenseModifier, false);
        AddPercent(lines, "Orbital production", p.orbitalProductionModifier, false);
        AddPercent(lines, "Interplanetary trade", p.interplanetaryTradeModifier, false);
        AddPercent(lines, "Planetary loyalty", p.planetaryLoyaltyModifier, false);
        AddPercent(lines, "Planetary defense", p.planetaryDefenseModifier, false);
        AddPercent(lines, "Herd starvation losses", -p.herdStarvationPercentReduction, true);
        AddFlat(lines, "Governor slots", p.additionalGovernorSlots, false);
        if (p.unlockedGovernorTraits != null)
        {
            var traits = p.unlockedGovernorTraits.Where(t => t != null)
                .Select(t => string.IsNullOrWhiteSpace(t.traitName) ? t.name : t.traitName).ToList();
            if (traits.Count > 0)
                lines.Add(new PoliticalEffectLine { label = "Unlocks governor traits", value = string.Join(", ", traits), beneficial = true });
        }
        AddCount(lines, "Targeted bonuses", CountTargeted(p));
        return lines;
    }

    /// <summary>Opinion reactions governors will have when the government/policy is adopted.</summary>
    public static List<PoliticalEffectLine> BuildGovernorReactionLines(GovernorOpinionEffect[] effects)
    {
        var lines = new List<PoliticalEffectLine>();
        if (effects == null) return lines;
        foreach (var effect in effects)
        {
            if (effect == null || Mathf.Approximately(effect.value, 0f)) continue;
            var filters = new List<string>();
            if (effect.requiresAnyPersonality != null && effect.requiresAnyPersonality.Length > 0)
                filters.Add(string.Join("/", effect.requiresAnyPersonality));
            if (effect.onlyIfReligionMismatch) filters.Add("different faith");
            if (effect.onlyIfCultureMismatch) filters.Add("different culture");
            if (effect.onlyIfNotOnCouncil) filters.Add("unseated");
            string scope = filters.Count > 0 ? string.Join(", ", filters) : "all governors";
            string duration = effect.durationTurns < 0 ? "permanent" : $"{effect.durationTurns} turns";
            lines.Add(new PoliticalEffectLine
            {
                label = $"{effect.reason} ({scope})",
                value = $"{effect.value:+0.#;-0.#;0} opinion, {duration}",
                beneficial = effect.value > 0f,
                harmful = effect.value < 0f,
            });
        }
        return lines;
    }

    public static List<PoliticalRequirementLine> BuildGovernmentRequirements(Civilization civ, GovernmentData g)
    {
        var lines = new List<PoliticalRequirementLine>();
        if (g == null) return lines;
        if (g.requiredTechs != null)
            foreach (var t in g.requiredTechs.Where(t => t != null))
                lines.Add(new PoliticalRequirementLine { label = $"Technology: {GovernmentPresentation.NameOf(t)}", met = civ != null && civ.researchedTechs.Contains(t) });
        if (g.requiredCultures != null)
            foreach (var c in g.requiredCultures.Where(c => c != null))
                lines.Add(new PoliticalRequirementLine { label = $"Culture: {GovernmentPresentation.NameOf(c)}", met = civ != null && civ.researchedCultures.Contains(c) });
        if (g.requiredCityCount > 0)
            lines.Add(new PoliticalRequirementLine { label = $"{g.requiredCityCount} cities", met = civ?.cities != null && civ.cities.Count >= g.requiredCityCount });
        if (g.requiresStateReligion)
            lines.Add(new PoliticalRequirementLine { label = "A state religion", met = civ != null && civ.StateReligion != null });
        if (g.requiredVassalCount > 0)
            lines.Add(new PoliticalRequirementLine { label = $"{g.requiredVassalCount} vassals", met = civ != null && civ.ActiveVassalCount >= g.requiredVassalCount });
        lines.Add(new PoliticalRequirementLine { label = "Government unlocked", met = civ?.unlockedGovernments != null && civ.unlockedGovernments.Contains(g) });
        return lines;
    }

    public static List<PoliticalRequirementLine> BuildPolicyRequirements(Civilization civ, PolicyData p)
    {
        var lines = new List<PoliticalRequirementLine>();
        if (p == null) return lines;
        if (p.requiredTechs != null)
            foreach (var t in p.requiredTechs.Where(t => t != null))
                lines.Add(new PoliticalRequirementLine { label = $"Technology: {GovernmentPresentation.NameOf(t)}", met = civ != null && civ.researchedTechs.Contains(t) });
        if (p.requiredCultures != null)
            foreach (var c in p.requiredCultures.Where(c => c != null))
                lines.Add(new PoliticalRequirementLine { label = $"Culture: {GovernmentPresentation.NameOf(c)}", met = civ != null && civ.researchedCultures.Contains(c) });
        var governments = p.requiredGovernments?.Where(x => x != null).ToList();
        if (governments != null && governments.Count > 0)
            lines.Add(new PoliticalRequirementLine
            {
                label = "Government: " + string.Join(" or ", governments.Select(GovernmentPresentation.NameOf)),
                met = civ != null && governments.Contains(civ.currentGovernment),
            });
        if (p.requiredCityCount > 0)
            lines.Add(new PoliticalRequirementLine { label = $"{p.requiredCityCount} cities", met = civ?.cities != null && civ.cities.Count >= p.requiredCityCount });
        if (p.requiredPolicies != null)
            foreach (var required in p.requiredPolicies.Where(x => x != null))
                lines.Add(new PoliticalRequirementLine { label = $"Active policy: {GovernmentPresentation.NameOf(required)}", met = civ?.activePolicies != null && civ.activePolicies.Contains(required) });
        if (p.religiousRequirementGroups != null && p.religiousRequirementGroups.Length > 0)
        {
            // The evaluator owns religion rules; show the final verdict from the shared manager.
            bool met = PolicyManager.Instance != null && civ != null && PolicyManager.Instance.EvaluatePolicy(civ, p).meetsReligionRequirement;
            lines.Add(new PoliticalRequirementLine { label = "Religious condition", met = met });
        }
        return lines;
    }

    public static string DescribeCouncilStructure(GovernmentData g)
    {
        if (g == null) return string.Empty;
        string institution = string.IsNullOrWhiteSpace(g.institutionDisplayName) ? GovernmentPresentation.DefaultInstitution : g.institutionDisplayName;
        if (!g.usesRoyalCouncil) return $"No {institution.ToLowerInvariant()} votes on decisions.";
        return $"{institution}: {g.councilSeatCount} seats. Votes on: {FormatVetoDomains(g.councilVetoDomains)}.";
    }

    public static string DescribeElectionStructure(GovernmentData g)
    {
        var rules = g?.electionRules;
        if (rules == null || !rules.enabled) return string.Empty;
        return $"{rules.electorateModel} electorate, {rules.termLengthTurns}-turn term, {rules.candidateCount} candidates, office: {rules.executiveTitle}.";
    }

    public static string FormatVetoDomains(VetoDomain domains)
    {
        if (domains == VetoDomain.None) return "nothing";
        var values = System.Enum.GetValues(typeof(VetoDomain)).Cast<VetoDomain>()
            .Where(v => v != VetoDomain.None && v != VetoDomain.All && domains.HasFlag(v))
            .Select(v => v.ToString());
        return string.Join(", ", values);
    }

    public static string FormatPolicyTags(PolicyData p)
        => p?.policyTags == null || p.policyTags.Length == 0 ? string.Empty : string.Join(", ", p.policyTags);

    private static int CountTargeted(GovernmentData g)
        => Len(g.tileYieldBonuses) + Len(g.buildingBonuses) + Len(g.unitYieldBonuses) + Len(g.unitBonuses)
         + Len(g.equipmentYieldBonuses) + Len(g.workerYieldBonuses) + Len(g.workerBonuses) + Len(g.diseaseBonuses)
         + Len(g.attritionBonuses) + Len(g.cityBonuses) + Len(g.herdYieldBonuses) + Len(g.citySlotModifiers)
         + Len(g.nonStateReligionUnhappinessModifiers);

    private static int CountTargeted(PolicyData p)
        => Len(p.tileYieldBonuses) + Len(p.buildingBonuses) + Len(p.unitYieldBonuses) + Len(p.unitBonuses)
         + Len(p.equipmentYieldBonuses) + Len(p.workerYieldBonuses) + Len(p.workerBonuses) + Len(p.diseaseBonuses)
         + Len(p.attritionBonuses) + Len(p.cityBonuses) + Len(p.herdYieldBonuses)
         + Len(p.nonStateReligionUnhappinessModifiers);

    private static int Len(System.Array a) => a == null ? 0 : a.Length;

    private static void AddCombat(List<PoliticalEffectLine> lines, float attack, float melee, float ranged, float cityAttack, float defense, float movement)
    {
        AddPercent(lines, "Attack", attack, false);
        AddPercent(lines, "Melee attack", melee, false);
        AddPercent(lines, "Ranged attack", ranged, false);
        AddPercent(lines, "City attack", cityAttack, false);
        AddPercent(lines, "Defense", defense, false);
        AddFlatFloat(lines, "Movement", movement);
    }

    private static void AddYields(List<PoliticalEffectLine> lines, float food, float production, float gold, float science, float culture, float faith)
    {
        AddPercent(lines, "Food", food, false);
        AddPercent(lines, "Production", production, false);
        AddPercent(lines, "Gold", gold, false);
        AddPercent(lines, "Science", science, false);
        AddPercent(lines, "Culture", culture, false);
        AddPercent(lines, "Faith", faith, false);
    }

    private static void AddPercent(List<PoliticalEffectLine> lines, string label, float value, bool lowerIsBetter)
    {
        if (Mathf.Approximately(value, 0f)) return;
        bool good = lowerIsBetter ? value < 0f : value > 0f;
        lines.Add(new PoliticalEffectLine { label = label, value = $"{value * 100f:+0.##;-0.##}%", beneficial = good, harmful = !good });
    }

    private static void AddFlat(List<PoliticalEffectLine> lines, string label, int value, bool lowerIsBetter)
    {
        if (value == 0) return;
        bool good = lowerIsBetter ? value < 0 : value > 0;
        lines.Add(new PoliticalEffectLine { label = label, value = $"{value:+0;-0}", beneficial = good, harmful = !good });
    }

    private static void AddFlatFloat(List<PoliticalEffectLine> lines, string label, float value)
    {
        if (Mathf.Approximately(value, 0f)) return;
        lines.Add(new PoliticalEffectLine { label = label, value = $"{value:+0.##;-0.##}", beneficial = value > 0f, harmful = value < 0f });
    }

    private static void AddCount(List<PoliticalEffectLine> lines, string label, int count)
    {
        if (count > 0) lines.Add(new PoliticalEffectLine { label = label, value = count.ToString() });
    }
}
