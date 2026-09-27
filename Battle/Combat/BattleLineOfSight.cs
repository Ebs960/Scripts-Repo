using System.Collections.Generic;
using UnityEngine;

public sealed class BattleLineOfSight
{
    public bool HasLineOfSight(BattleSession session, BattleUnitState attacker, BattleUnitState defender, out BattleLosBlockReason reason)
    {
        reason=BattleLosBlockReason.None;
        if(session==null||attacker==null||defender==null){reason=BattleLosBlockReason.InvalidTarget;return false;}
        int distance=session.MapDistance(attacker.CellIndex,defender.CellIndex);
        if(distance>Mathf.FloorToInt(Mathf.Max(3f,attacker.Snapshot.Range))){reason=BattleLosBlockReason.OutOfRange;return false;}
        var line=BuildLine(session.Map,attacker.CellIndex,defender.CellIndex);
        for(int i=1;i<line.Count-1;i++)
        {
            var cell=session.Map.GetCell(line[i]);
            if(cell==null)continue;
            if(cell.IsForest){reason=BattleLosBlockReason.BlockedByForest;return false;}
            if(cell.HasHardCover){reason=BattleLosBlockReason.BlockedByStructure;return false;}
            if(cell.ElevationLevel>=3){reason=BattleLosBlockReason.BlockedByElevation;return false;}
        }
        return true;
    }

    internal static List<int> BuildLine(BattleMap map,int start,int end)
    {
        var result=new List<int>(); var a=map?.GetCell(start);var b=map?.GetCell(end);
        if(a==null||b==null)return result;
        int count=BattleMap.AxialDistance(a,b);
        if(count==0){result.Add(start);return result;}
        for(int i=0;i<=count;i++)
        {
            float t=(float)i/count;
            CubeRound(Mathf.Lerp(a.LocalQ,b.LocalQ,t),Mathf.Lerp(a.LocalS,b.LocalS,t),Mathf.Lerp(a.LocalR,b.LocalR,t),out int q,out int r);
            if(map.TryGetBattleIndex(q,r,out int index)&&(result.Count==0||result[result.Count-1]!=index))result.Add(index);
        }
        return result;
    }

    private static void CubeRound(float x,float y,float z,out int q,out int r)
    {
        int rx=Mathf.RoundToInt(x),ry=Mathf.RoundToInt(y),rz=Mathf.RoundToInt(z);
        float dx=Mathf.Abs(rx-x),dy=Mathf.Abs(ry-y),dz=Mathf.Abs(rz-z);
        if(dx>dy&&dx>dz)rx=-ry-rz;else if(dy>dz)ry=-rx-rz;else rz=-rx-ry;
        q=rx;r=rz;
    }
}
