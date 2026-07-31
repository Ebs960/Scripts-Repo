public sealed class EngagementContext
{
    public bool IsAntiSubmarineEngagement;
    public bool IsSubmarineSearch;
    public bool IsAssignedNavalAircraft;
    public bool IsScriptedInstantEffect;
    public int SpaceRegionId = -1;
}

public sealed class BattleTheaterDecision
{
    public bool IsValid;
    public string RejectionReason;
    public BattleTheater Theater;
    public CombatUnit Attacker;
    public CombatUnit Defender;
    public int PlanetIndex = -1;
    public int SpaceRegionId = -1;
    public bool AllowsManualBattle;
    public bool AllowsRetreat;
    public bool AllowsCancel;
}

/// <summary>Authoritative, location-first theater routing for campaign engagements.</summary>
public static class BattleTheaterResolver
{
    public static BattleTheaterDecision ResolveBattleTheater(CombatUnit attacker, CombatUnit defender, EngagementContext context = null)
    {
        var result = new BattleTheaterDecision { Attacker = attacker, Defender = defender, AllowsCancel = true };
        if (attacker == null || defender == null) return Reject(result, "missing attacker or defender");
        if (attacker.currentHealth <= 0 || defender.currentHealth <= 0) return Reject(result, "inactive combatant");
        if (!AircraftMissionManager.IsHostile(attacker.owner, defender.owner)) return Reject(result, "combatants are not hostile");

        bool attackerInSpace = IsOnSpaceMap(attacker);
        bool defenderInSpace = IsOnSpaceMap(defender);
        if (attackerInSpace || defenderInSpace)
        {
            if (!attackerInSpace || !defenderInSpace) return Reject(result, "space-map and planetary units cannot engage directly");
            result.Theater = BattleTheater.DeepSpace;
            result.SpaceRegionId = context != null && context.SpaceRegionId >= 0 ? context.SpaceRegionId : 0;
        }
        else
        {
            if (attacker.planetIndex != defender.planetIndex) return Reject(result, "combatants are on different planets");
            result.PlanetIndex = attacker.planetIndex;
            BattleDomain a = BattleDomainResolver.Resolve(attacker);
            BattleDomain d = BattleDomainResolver.Resolve(defender);
            bool underwater = a == BattleDomain.Underwater || d == BattleDomain.Underwater
                || (context != null && (context.IsAntiSubmarineEngagement || context.IsSubmarineSearch));
            if (underwater)
            {
                if (!IsUnderwaterParticipant(attacker, a, context) || !IsUnderwaterParticipant(defender, d, context))
                    return Reject(result, "unit domain cannot participate in an underwater battle");
                result.Theater = BattleTheater.Underwater;
            }
            else
            {
                if (!IsPlanetaryParticipant(a) || !IsPlanetaryParticipant(d))
                    return Reject(result, "unit domain cannot participate in a planetary battle");
                result.Theater = BattleTheater.PlanetaryJoint;
            }
        }

        result.IsValid = true;
        result.AllowsManualBattle = true;
        result.AllowsRetreat = true;
        return result;
    }

    public static bool IsOnSpaceMap(CombatUnit unit) => unit != null
        && unit.currentSpaceTileIndex >= 0
        && unit.spaceLocation.locationType == SpaceLocationType.SolarSystemSpace;

    public static bool AllowsDomain(BattleTheater theater, BattleDomain domain, bool assignedNavalAircraft = false) => theater switch
    {
        BattleTheater.PlanetaryJoint => IsPlanetaryParticipant(domain),
        BattleTheater.Underwater => domain == BattleDomain.Underwater || domain == BattleDomain.NavalSurface
            || (domain == BattleDomain.Air && assignedNavalAircraft),
        BattleTheater.DeepSpace => domain == BattleDomain.Space,
        _ => false,
    };

    private static bool IsPlanetaryParticipant(BattleDomain domain) => domain == BattleDomain.Land
        || domain == BattleDomain.NavalSurface || domain == BattleDomain.Air || domain == BattleDomain.Orbit;
    private static bool IsUnderwaterParticipant(CombatUnit unit, BattleDomain domain, EngagementContext context) => domain == BattleDomain.Underwater
        || domain == BattleDomain.NavalSurface || (domain == BattleDomain.Air && ((context != null && context.IsAssignedNavalAircraft)
            || (unit != null && unit.IsTransported && unit.TransportingUnit != null
                && BattleDomainResolver.Resolve(unit.TransportingUnit) == BattleDomain.NavalSurface)));
    private static BattleTheaterDecision Reject(BattleTheaterDecision result, string reason)
    { result.IsValid = false; result.RejectionReason = reason; return result; }
}
