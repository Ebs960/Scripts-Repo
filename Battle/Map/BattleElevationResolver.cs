using UnityEngine;

public static class BattleElevationResolver
{
    public static void GenerateLocalElevations(BattleMap map, HexTileData anchor, int seed)
    {
        if(map==null||anchor==null)return;
        foreach(var cell in map.Cells)
        {
            int radial=Mathf.Max(Mathf.Abs(cell.LocalQ),Mathf.Max(Mathf.Abs(cell.LocalR),Mathf.Abs(cell.LocalS)));
            int noise=Positive(seed*31+cell.LocalQ*73856093+cell.LocalR*19349663)%7;
            int level=1;
            if(anchor.isMountain||anchor.elevationTier==ElevationTier.Mountain)
                level=radial<=1?3:(radial<=3?2:1);
            else if(anchor.isHill||anchor.elevationTier==ElevationTier.Hill)
                level=(radial<=2||noise==0)?2:1;
            else if(noise==0) level=0; // flat terrain can contain depressions, never peaks
            if(cell.HasRiver||cell.IsWater)level=0;
            cell.ElevationLevel=Mathf.Clamp(level,0,3);
        }
        ApplyCliffEdges(map);
    }

    private static int Positive(int value)=>value==int.MinValue?0:Mathf.Abs(value);
    private static void ApplyCliffEdges(BattleMap map)
    {
        foreach(var cell in map.Cells) foreach(int n in cell.NeighborIndices??System.Array.Empty<int>())
            cell.SetCliffTowardNeighbor(n,Mathf.Abs(cell.ElevationLevel-map.Cells[n].ElevationLevel)>=2);
    }
}
