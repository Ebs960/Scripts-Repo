using System.Collections.Generic;

public sealed class BattleReinforcementGroup
{
    public BattleSide Side;
    public int OriginCampaignTile;
    public int EntryCellIndex;
    public int AvailableFromRound;

    public readonly List<BattleUnitSnapshot> Units = new();
}
