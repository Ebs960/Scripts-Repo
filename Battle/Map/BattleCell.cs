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
    public bool SupportsLand;
    public bool SupportsNavalSurface;
    public bool SupportsUnderwater;
    public bool SupportsAir = true;
    public bool SupportsOrbit = true;
    public bool SupportsSpace;
    public int WaterDepthLevel;
    public bool HasPort;
    public bool HasBeach;
    public bool IsForest;
    public bool HasRiver;
    public bool HasHardCover;
    public bool HasSoftCover;

    public BattleSide? DeploymentOwner;
    public BattleSide? RetreatExitForSide;
    public bool IsObjective;
    public bool IsReinforcementEntry;

    private readonly HashSet<int> cliffNeighbors = new();

    public bool IsCliffTowardNeighbor(int neighborIndex) => cliffNeighbors.Contains(neighborIndex);
    public IEnumerable<int> CliffNeighbors => cliffNeighbors;

    public bool Supports(BattleDomain domain)
    {
        return domain switch
        {
            BattleDomain.Land => SupportsLand,
            BattleDomain.NavalSurface => SupportsNavalSurface,
            BattleDomain.Underwater => SupportsUnderwater,
            BattleDomain.Air => SupportsAir,
            BattleDomain.Orbit => SupportsOrbit,
            BattleDomain.Space => SupportsSpace,
            _ => false,
        };
    }

    public void SetCliffTowardNeighbor(int neighborIndex, bool isCliff)
    {
        if (isCliff)
            cliffNeighbors.Add(neighborIndex);
        else
            cliffNeighbors.Remove(neighborIndex);
    }
}
