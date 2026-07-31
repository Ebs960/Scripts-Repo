using System.Collections.Generic;

public sealed class BattleReinforcementGroup
{
    public int ReinforcementGroupId;
    public string FormationId;
    public BattleSide Side;
    public BattleTheater Theater;
    public int OriginCampaignTile;
    public int EntryCellIndex = -1;
    public int AvailableFromRound;
    public int EarliestEntryRound { get => AvailableFromRound; set => AvailableFromRound = value; }
    public int OriginSpaceRegion = -1;
    public BattleDomain Domain;
    public BattleEntryMethod EntryMethod;

    public readonly List<BattleUnitSnapshot> Units = new();
}
