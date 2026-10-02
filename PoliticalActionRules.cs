using System.Linq;

/// <summary>UI-facing rules for political actions, so enabled/disabled states and reasons come from one place.</summary>
public static class PoliticalActionRules
{
    public const int GovernorGiftCost = 50;

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
}
