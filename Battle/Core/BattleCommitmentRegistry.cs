using System.Collections.Generic;

public sealed class BattleCommitment
{
    public int CampaignRuntimeId;
    public string FormationId;
    public int BattleId;
    public BattleTheater Theater;
    public int CarrierOrTransportRuntimeId = -1;
    public string CommanderAssignmentId;
    public int CampaignTurn;
}

public sealed class BattleCommitmentRegistry
{
    private readonly Dictionary<int, BattleCommitment> commitments = new();
    public bool IsCommitted(int runtimeId) => runtimeId != 0 && commitments.ContainsKey(runtimeId);
    public bool TryCommit(BattleCommitment commitment)
    {
        if (commitment == null || commitment.CampaignRuntimeId == 0 || commitments.ContainsKey(commitment.CampaignRuntimeId))
            return false;

        int relationshipId = commitment.CarrierOrTransportRuntimeId;
        if (relationshipId > 0
            && relationshipId != commitment.CampaignRuntimeId
            && commitments.ContainsKey(relationshipId))
            return false;

        commitments.Add(commitment.CampaignRuntimeId, commitment);
        if (relationshipId > 0 && relationshipId != commitment.CampaignRuntimeId)
            commitments.Add(relationshipId, commitment);
        return true;
    }
    public void ReleaseBattle(int battleId)
    {
        var remove = new List<int>();
        foreach (var pair in commitments) if (pair.Value.BattleId == battleId) remove.Add(pair.Key);
        for (int i = 0; i < remove.Count; i++) commitments.Remove(remove[i]);
    }
    public void Clear() => commitments.Clear();
}
