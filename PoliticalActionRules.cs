using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public struct GovernorDismissalRiskPreview
{
    public float riskPercent, baseRisk, loyaltyRisk, ambitionRisk, powerRisk, grievanceRisk,
        personalityRisk, localMilitaryRisk, stateDeterrence, pressureReduction;
}

/// <summary>UI-facing rules for political actions, so enabled/disabled states and reasons come from one place.</summary>
public static class PoliticalActionRules
{
    public const int GovernorGiftCost = 50;
    public const int GovernorPressurePolicyCost = 30;
    public const int GovernorPardonFaithCost = 35;
    public const int GovernorDismissPolicyCost = 100;
    public const int GovernorPressureDuration = 10;
    public const int GovernorPardonCooldown = 10;
    public const float GovernorPressureBaseOpinionChange = -10f;
    public const float GovernorPardonOpinionBonus = 5f;
    public const string GovernorPressureModifierReason = "Under Pressure";
    public const string GovernorPardonModifierReason = "Recently Pardoned";

    public static int CurrentTurn
        => TurnManager.Instance != null ? TurnManager.Instance.round : GameManager.Instance != null ? GameManager.Instance.currentTurn : 0;

    public static Civilization FindPlayerCivilization()
    {
        var player = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
        if (player != null) return player;
        return CivilizationManager.Instance?.GetAllCivs()?.FirstOrDefault(c => c != null && c.isPlayerControlled);
    }

    public static bool CanGrantCouncilSeat(Civilization civ, Governor governor, out string reason)
    {
        reason = null;
        if (civ == null || governor == null) { reason = "No governor selected."; return false; }
        string institution = GovernmentPresentation.GetInstitutionName(civ);
        if (!civ.HasRoyalCouncil) { reason = $"This government has no {institution.ToLowerInvariant()}."; return false; }
        if (governor.IsOnCouncil) { reason = $"Already seated on the {institution}."; return false; }
        if (governor.IsInRebellion) { reason = "In open rebellion."; return false; }
        if (!governor.IsCouncilEligible) { reason = $"Not powerful enough for a seat on the {institution}."; return false; }
        if (civ.royalCouncil.Count >= civ.MaxCouncilSeats) { reason = $"The {institution} has no free seat."; return false; }
        return true;
    }

    public static bool CanRemoveFromCouncil(Civilization civ, Governor governor, out string reason)
    {
        reason = null;
        if (civ == null || governor == null) { reason = "No governor selected."; return false; }
        if (!civ.HasRoyalCouncil || !governor.IsOnCouncil) { reason = "Not currently seated."; return false; }
        return true;
    }

    public static bool CanCreateGovernor(Civilization civ, out string reason)
    {
        reason = null;
        if (civ == null) { reason = "No civilization."; return false; }
        string plural = GovernmentPresentation.GetGovernorTitlePlural(civ).ToLowerInvariant();
        if (!civ.governorsEnabled) { reason = $"{plural} have not been unlocked."; return false; }
        if (civ.governors.Count >= civ.governorCount) { reason = $"No free slot ({civ.governors.Count}/{civ.governorCount})."; return false; }
        return true;
    }

    public static bool CanSendGovernorGift(Civilization civ, Governor governor, out string reason)
    {
        reason = null;
        if (civ == null || governor == null) { reason = "No governor selected."; return false; }
        if (governor.IsInRebellion) { reason = "Cannot send gifts during open rebellion."; return false; }
        if (civ.gold < GovernorGiftCost) { reason = $"Requires {GovernorGiftCost} gold."; return false; }
        if (governor.OpinionModifiers.Any(m => m.reason == "Received Gift" && m.turnsRemaining != 0))
        { reason = "A recent gift is still influencing this governor."; return false; }
        return true;
    }

    public static bool CanRequestGovernorConversion(Civilization civ, Governor governor, out string reason)
    {
        reason = null;
        if (civ == null || governor == null) { reason = "No governor selected."; return false; }
        if (civ.StateReligion == null) { reason = "There is no state religion."; return false; }
        if (governor.PersonalReligion == civ.StateReligion) { reason = "Already follows the state religion."; return false; }
        if (governor.IsInRebellion) { reason = "Cannot negotiate during open rebellion."; return false; }
        return true;
    }

    public static float GetPressureOpinionChange(Governor governor)
    {
        float adjustment = 0f;
        if (governor != null)
        {
            if (governor.HasPersonality(PersonalityTrait.Craven)) adjustment += 5f;
            if (governor.HasPersonality(PersonalityTrait.Brave)) adjustment -= 5f;
            if (governor.HasPersonality(PersonalityTrait.Loyal)) adjustment += 2f;
            if (governor.HasPersonality(PersonalityTrait.Content)) adjustment += 2f;
            if (governor.HasPersonality(PersonalityTrait.Ambitious)) adjustment -= 5f;
            if (governor.HasPersonality(PersonalityTrait.Cruel)) adjustment -= 2f;
        }
        return Mathf.Clamp(GovernorPressureBaseOpinionChange + adjustment, -20f, -3f);
    }

    public static bool CanPressureGovernor(Civilization civ, Governor governor, out string reason)
    {
        reason = null;
        if (civ == null || governor == null) { reason = "No governor selected."; return false; }
        if (governor.IsInRebellion) { reason = "Cannot pressure a governor already in open rebellion."; return false; }
        if (governor.IsUnderPressure) { reason = "This governor is already under political pressure."; return false; }
        if (civ.policyPoints < GovernorPressurePolicyCost) { reason = $"Requires {GovernorPressurePolicyCost} Policy Points."; return false; }
        return true;
    }

    public static bool TryPressureGovernor(Civilization civ, Governor governor, out string reason)
    {
        if (!CanPressureGovernor(civ, governor, out reason)) return false;
        civ.AddPolicyPoints(-GovernorPressurePolicyCost);
        governor.SetOpinionModifier(GovernorPressureModifierReason, GetPressureOpinionChange(governor), GovernorPressureDuration);
        return true;
    }

    public static bool CanPardonGovernor(Civilization civ, Governor governor, out string reason)
    {
        reason = null;
        if (civ == null || governor == null) { reason = "No governor selected."; return false; }
        if (governor.IsInRebellion) { reason = "Cannot pardon a governor during open rebellion."; return false; }
        if (governor.TotalGrievances() <= 0) { reason = "This governor has no grievances to pardon."; return false; }
        if (governor.HasActiveOpinionModifier(GovernorPardonModifierReason)) { reason = "This governor was recently pardoned."; return false; }
        if (civ.faith < GovernorPardonFaithCost) { reason = $"Requires {GovernorPardonFaithCost} Faith."; return false; }
        return true;
    }

    public static bool TryPardonGovernor(Civilization civ, Governor governor, out string reason)
    {
        if (!CanPardonGovernor(civ, governor, out reason)) return false;
        civ.AddFaith(-GovernorPardonFaithCost);
        governor.ClearAllGrievances();
        governor.SetOpinionModifier(GovernorPardonModifierReason, GovernorPardonOpinionBonus, GovernorPardonCooldown);
        reason = null;
        return true;
    }

    public static bool CanDismissGovernor(Civilization civ, Governor governor, out string reason)
    {
        reason = null;
        if (civ == null || governor == null) { reason = "No governor selected."; return false; }
        if (governor.IsInRebellion) { reason = "This governor is already in open rebellion."; return false; }
        if (civ.governors == null || !civ.governors.Contains(governor)) { reason = "This governor is no longer in office."; return false; }
        if (civ.policyPoints < GovernorDismissPolicyCost) { reason = $"Requires {GovernorDismissPolicyCost} Policy Points."; return false; }
        return true;
    }

    public static GovernorDismissalRiskPreview PreviewGovernorDismissalRisk(Civilization civ, Governor governor)
    {
        var p = new GovernorDismissalRiskPreview { baseRisk = 5f };
        if (civ == null || governor == null) return p;
        p.loyaltyRisk = Mathf.Clamp01((50f - governor.Opinion) / 150f) * 25f;
        p.ambitionRisk = Mathf.Clamp01(governor.AmbitionScore / 100f) * 20f;
        float totalPower = Mathf.Max(1f, civ.governors.Where(g => g != null).Sum(g => g.PowerRank));
        p.powerRisk = governor.PowerRank / totalPower * 20f;
        p.grievanceRisk = Mathf.Clamp(governor.TotalGrievances() * 3f, 0f, 15f);
        float personality = 0f;
        foreach (var trait in governor.PersonalityTraits)
            switch (trait)
            {
                case PersonalityTrait.Loyal: personality -= 20f; break;
                case PersonalityTrait.Content: personality -= 15f; break;
                case PersonalityTrait.Craven: personality -= 10f; break;
                case PersonalityTrait.Honest: personality -= 5f; break;
                case PersonalityTrait.Greedy: personality += 5f; break;
                case PersonalityTrait.Deceitful: personality += 10f; break;
                case PersonalityTrait.Cruel: personality += 10f; break;
                case PersonalityTrait.Brave: personality += 15f; break;
                case PersonalityTrait.Ambitious: personality += 20f; break;
                case PersonalityTrait.Zealous:
                    if (governor.PersonalReligion != null && civ.StateReligion != null && governor.PersonalReligion != civ.StateReligion) personality += 10f;
                    break;
            }
        p.personalityRisk = Mathf.Clamp(personality, -25f, 30f);
        var local = LocalGarrisonUnits(civ, governor).Distinct().ToList();
        var all = civ.combatUnits.Where(IsHealthyUnit).Distinct().ToList();
        p.localMilitaryRisk = all.Count == 0 ? 0f : Mathf.Clamp01((float)local.Count / all.Count) * 20f;
        var localSet = new HashSet<CombatUnit>(local);
        var nearby = all.Where(u => !localSet.Contains(u) && !u.isStored && !u.isGarrisonedInCity
            && IsNearHolding(u, governor)).ToList();
        p.stateDeterrence = all.Count == 0 ? 0f : -Mathf.Clamp01((float)nearby.Count / all.Count) * 15f;
        p.pressureReduction = governor.IsUnderPressure ? -10f : 0f;
        p.riskPercent = Mathf.Clamp(p.baseRisk + p.loyaltyRisk + p.ambitionRisk + p.powerRisk + p.grievanceRisk
            + p.personalityRisk + p.localMilitaryRisk + p.stateDeterrence + p.pressureReduction, 0f, 95f);
        return p;
    }

    // No canonical aggregate combat-power API exists; healthy unit count is the deliberately conservative fallback.
    private static bool IsHealthyUnit(CombatUnit u) => u != null && u.currentHealth > 0 && u.owner != null;
    private static IEnumerable<CombatUnit> LocalGarrisonUnits(Civilization civ, Governor governor)
    {
        foreach (var u in civ.combatUnits.Where(IsHealthyUnit))
            if (u.owner == civ && u.isGarrisonedInCity && governor.Cities.Any(c => c != null && c.planetIndex == u.planetIndex && c.centerTileIndex == u.currentTileIndex)) yield return u;
        foreach (var herd in governor.Herds.Where(h => h != null))
            foreach (var u in herd.MilitaryGarrison)
                if (IsHealthyUnit(u) && u.owner == civ) yield return u;
    }

    private static bool IsNearHolding(CombatUnit unit, Governor governor)
    {
        foreach (var city in governor.Cities.Where(c => c != null))
            if (Near(unit, city.planetIndex, city.centerTileIndex)) return true;
        foreach (var herd in governor.Herds.Where(h => h != null))
            if (Near(unit, herd.planetIndex, herd.currentTileIndex)) return true;
        return false;
    }

    private static bool Near(CombatUnit unit, int planet, int tile)
    {
        if (unit.planetIndex != planet) return false;
        if (unit.currentTileIndex == tile) return true;
        var system = TileSystem.GetForPlanet(planet);
        var neighbors = system != null ? system.GetNeighbors(tile) : null;
        return neighbors != null && neighbors.Contains(unit.currentTileIndex);
    }
}
