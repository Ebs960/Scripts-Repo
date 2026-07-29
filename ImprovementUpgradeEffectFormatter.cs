using System.Collections.Generic;

public static class ImprovementUpgradeEffectFormatter
{
    public static string Format(ImprovementUpgradeData upgrade)
    {
        if (upgrade == null) return "No effects";
        var parts = new List<string>();
        Add(parts, upgrade.additionalFood, "Food");
        Add(parts, upgrade.additionalProduction, "Production");
        Add(parts, upgrade.additionalGold, "Gold");
        Add(parts, upgrade.additionalScience, "Science");
        Add(parts, upgrade.additionalCulture, "Culture");
        Add(parts, upgrade.additionalFaith, "Faith");
        Add(parts, upgrade.additionalPolicyPoints, "Policy");
        Add(parts, upgrade.additionalShelterCapacity, "Storage");
        AddPercent(parts, upgrade.empireFoodModifier, "Empire Food");
        AddPercent(parts, upgrade.empireProductionModifier, "Empire Production");
        AddPercent(parts, upgrade.empireGoldModifier, "Empire Gold");
        AddPercent(parts, upgrade.empireScienceModifier, "Empire Science");
        AddPercent(parts, upgrade.empireCultureModifier, "Empire Culture");
        AddPercent(parts, upgrade.empireFaithModifier, "Empire Faith");
        AddPercent(parts, upgrade.empirePolicyPointsModifier, "Empire Policy");
        Add(parts, upgrade.tradeRangeModifier, "Trade Range");
        Add(parts, upgrade.tradeRouteCapacityModifier, "Trade Capacity");
        AddPercent(parts, upgrade.tradeRouteGoldModifier, "Trade Gold");
        AddPercent(parts, upgrade.tradeRaidChanceReduction, "Raid Protection");
        Add(parts, upgrade.defenseAdd, "Unit Defense");
        AddPercent(parts, upgrade.defensePct, "Unit Defense");
        Add(parts, upgrade.fortAttackAdd, "Fort Attack");
        AddPercent(parts, upgrade.fortAttackPct, "Fort Attack");
        Add(parts, upgrade.fortDefenseAdd, "Fort Defense");
        AddPercent(parts, upgrade.fortDefensePct, "Fort Defense");
        Add(parts, upgrade.additionalFortHitPoints, "Fort HP");
        if (upgrade.grantsTradeRelay) parts.Add("Grants Trade Relay");
        if (upgrade.grantsZoneOfControl) parts.Add("Grants Zone of Control");
        if (upgrade.blocksZoneOfControl) parts.Add("Blocks Enemy Zone of Control");
        if (upgrade.addedRuralSpecialistSlots != null && upgrade.addedRuralSpecialistSlots.Length > 0)
            parts.Add($"+{upgrade.addedRuralSpecialistSlots.Length} Rural Specialist Slot{(upgrade.addedRuralSpecialistSlots.Length == 1 ? "" : "s")}");
        if (upgrade.resourceProductionPerTurn != null && upgrade.resourceProductionPerTurn.Length > 0)
            parts.Add($"Produces {ResourceCost.FormatCosts(upgrade.resourceProductionPerTurn)} / turn");
        if (upgrade.auraBonuses != null && upgrade.auraBonuses.Length > 0)
            parts.Add($"{upgrade.auraBonuses.Length} Aura Effect{(upgrade.auraBonuses.Length == 1 ? "" : "s")}");
        return parts.Count == 0 ? "No direct effects" : string.Join(" • ", parts);
    }

    private static void Add(List<string> parts, int value, string label)
    {
        if (value != 0) parts.Add($"{value:+#;-#} {label}");
    }

    private static void AddPercent(List<string> parts, float value, string label)
    {
        if (value != 0f) parts.Add($"{value:+0%;-0%} {label}");
    }
}
