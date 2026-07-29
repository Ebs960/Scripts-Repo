using System.Collections.Generic;

public sealed class BattleCell
{
    public int BattleIndex;
    public int CampaignTileIndex;

    public int[] NeighborIndices;

    public Biome Biome;
    public int ElevationLevel;

    public bool IsPassable;
    public bool IsWater;
    public bool IsForest;
    public bool HasRiver;
    public bool HasHardCover;
    public bool HasSoftCover;

    public BattleSide? DeploymentOwner;
    public bool IsObjective;
    public bool IsReinforcementEntry;

    private readonly HashSet<int> cliffNeighbors = new();

    public bool IsCliffTowardNeighbor(int neighborIndex) => cliffNeighbors.Contains(neighborIndex);

    public void SetCliffTowardNeighbor(int neighborIndex, bool isCliff)
    {
        if (isCliff)
            cliffNeighbors.Add(neighborIndex);
        else
            cliffNeighbors.Remove(neighborIndex);
    }
}
