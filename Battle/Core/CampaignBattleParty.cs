using System.Collections.Generic;
using System.Linq;

public enum CampaignBattlePartyKind { Army, BandGarrison, HerdGarrison }

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
    public Herd HerdHost { get; private set; }
    public string ArmyFormationId { get; private set; }

    public static CampaignBattleParty FromArmy(CombatUnit unit)
    {
        var representative = CampaignArmyService.GetRepresentative(unit);
        return new CampaignBattleParty { Kind = CampaignBattlePartyKind.Army, Owner = unit?.owner,
            PlanetIndex = unit != null ? unit.planetIndex : -1, CampaignTileIndex = unit != null ? unit.currentTileIndex : -1,
            CombatUnits = CampaignArmyService.GetMembers(unit).Where(x => x != null && !x.IsBandGarrisoned && x.storedInHerd == null).ToList(), ArmyRepresentative = representative,
            ArmyFormationId = representative != null ? representative.MilitaryFormationId : string.Empty };
    }

    public static CampaignBattleParty FromHerd(Herd herd)
    {
        return new CampaignBattleParty { Kind = CampaignBattlePartyKind.HerdGarrison, Owner = herd?.owner,
            PlanetIndex = herd != null ? herd.planetIndex : -1, CampaignTileIndex = herd != null ? herd.currentTileIndex : -1,
            CombatUnits = herd != null ? herd.MilitaryGarrison.Where(x => x != null).ToList() : new List<CombatUnit>(), HerdHost = herd };
    }

    public static CampaignBattleParty FromBand(Band band)
    {
        return new CampaignBattleParty { Kind = CampaignBattlePartyKind.BandGarrison, Owner = band?.Owner,
            PlanetIndex = band != null ? band.PlanetIndex : -1, CampaignTileIndex = band != null ? band.CurrentTileIndex : -1,
            CombatUnits = band != null ? band.Garrison.Where(x => x != null).ToList() : new List<CombatUnit>(), BandHost = band };
    }
}
