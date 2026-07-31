using System.Collections.Generic;

public sealed class BattleResult
{
    public int BattleId;
    public BattleSide WinningSide;
    public BattleResolutionType ResolutionType;

    public readonly List<BattleUnitOutcome> UnitOutcomes = new();

    public int FinalRound;
    public bool WasAutoResolved;
}

public sealed class BattleUnitOutcome
{
    public int CampaignRuntimeId;
    public BattleSide Side;

    public int FinalHealth;
    public bool Died;
    public bool Retreated;

    public int ExperienceGained;

    public bool IsEmbarked;
    public int CarrierOrTransportCampaignRuntimeId = -1;

    public int SuggestedCampaignTile;
    public int SuggestedStackSlot;
}
