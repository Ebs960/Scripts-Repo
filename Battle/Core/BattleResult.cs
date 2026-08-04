using System.Collections.Generic;

public sealed class BattleResult
{
    public int BattleId;
    public BattleSide WinningSide;
    public BattleResolutionType ResolutionType;

    public readonly List<BattleUnitOutcome> UnitOutcomes = new();

    public int FinalRound;
    public bool WasAutoResolved;
    public bool WasPlayerInvolved;
    public bool CampaignApplied;
    public readonly List<BattlePlacementFailure> PlacementFailures = new();
    public readonly List<BattleCommanderOutcome> CommanderOutcomes = new();
}

public sealed class BattleCommanderOutcome
{
    public string AssignmentId;
    public string FormationId;
    public CommandRole Role;
    public CommanderCharacterKind CharacterKind;
    public int CharacterId;
    public int ExperienceGained;
    public bool Participated;
    public bool FormationDestroyed;
    public bool FormationRetreated;
    public BattleCommanderStatus StatusBefore;
    public BattleCommanderStatus StatusAfter;
}

public sealed class BattlePlacementFailure
{
    public int CampaignRuntimeId;
    public BattleSide Side;
    public string Reason;
    public int OriginalTile = -1;
    public int RequestedTile = -1;
    public bool IsDeepSpace;
}

public sealed class BattleUnitOutcome
{
    public int CampaignRuntimeId;
    public BattleSide Side;

    public int FinalHealth;
    public bool Died;
    public bool Retreated;
    public bool Participated;
    public int WithdrawalCampaignTile = -1;
    public int WithdrawalTacticalExit = -1;
    public List<int> RetreatPath = new();
    public string RetreatFailureReason;

    public int ExperienceGained;

    public bool IsEmbarked;
    public int CarrierOrTransportCampaignRuntimeId = -1;

    public int SuggestedCampaignTile;
    public int SuggestedStackSlot;
}
