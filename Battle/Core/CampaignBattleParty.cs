using System.Collections.Generic;
using System.Linq;

public enum CampaignBattlePartyKind { Army, BandGarrison }

/// <summary>Campaign source abstraction; tactical participants remain CombatUnits only.</summary>
public sealed class CampaignBattleParty
{
    public CampaignBattlePartyKind Kind { get; private set; }
    public Civilization Owner { get; private set; }
    public int PlanetIndex { get; private set; }
    public int CampaignTileIndex { get; private set; }
    public IReadOnlyList<CombatUnit> CombatUnits { get; private set; }
    public CombatUnit ArmyRepresentative { get; private set; }
    public Band BandHost { get; private set; }

    public static CampaignBattleParty FromArmy(CombatUnit unit)
    {
        var representative = CampaignArmyService.GetRepresentative(unit);
        return new CampaignBattleParty { Kind = CampaignBattlePartyKind.Army, Owner = unit?.owner,
            PlanetIndex = unit != null ? unit.planetIndex : -1, CampaignTileIndex = unit != null ? unit.currentTileIndex : -1,
            CombatUnits = CampaignArmyService.GetMembers(unit).Where(x => x != null && !x.IsBandGarrisoned).ToList(), ArmyRepresentative = representative };
    }

    public static CampaignBattleParty FromBand(Band band)
    {
        return new CampaignBattleParty { Kind = CampaignBattlePartyKind.BandGarrison, Owner = band?.Owner,
            PlanetIndex = band != null ? band.PlanetIndex : -1, CampaignTileIndex = band != null ? band.CurrentTileIndex : -1,
            CombatUnits = band != null ? band.Garrison.Where(x => x != null).ToList() : new List<CombatUnit>(), BandHost = band };
    }
}
