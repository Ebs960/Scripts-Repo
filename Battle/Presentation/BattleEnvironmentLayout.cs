using System.Collections.Generic;
using UnityEngine;

public enum BattleDecorationKind { Tree=1,Grass=2,Bush=3,Rock=4,Prop=5,SoftCover=6,HardCover=7,Port=8 }
public readonly struct BattleDecorationPlacement
{
    public readonly BattleDecorationKind Kind;public readonly int PrefabIndex;public readonly Vector3 LocalPosition;public readonly float Yaw;public readonly float Scale;
    public BattleDecorationPlacement(BattleDecorationKind kind,int prefabIndex,Vector3 position,float yaw,float scale){Kind=kind;PrefabIndex=prefabIndex;LocalPosition=position;Yaw=yaw;Scale=scale;}
}

/// <summary>Pure deterministic decoration planning; never reads or changes simulation state.</summary>
public static class BattleEnvironmentLayout
{
    public static int Hash(int a,int b,int c,int d){unchecked{uint h=2166136261;h=(h^(uint)a)*16777619;h=(h^(uint)b)*16777619;h=(h^(uint)c)*16777619;h=(h^(uint)d)*16777619;return (int)h;}}
    public static List<BattleDecorationPlacement> Generate(BattleCell cell,BattleBiomeVisualProfile profile,int battleSeed,float radius)
    {
        var result=new List<BattleDecorationPlacement>();if(cell==null||profile==null||cell.IsWater)return result;
        bool reserved=cell.IsObjective||cell.HasPort||cell.DeploymentOwner.HasValue||cell.RetreatExitForSide.HasValue||cell.IsReinforcementEntry||cell.HasHardCover;
        float vegetation=Mathf.Max(0f,profile.vegetationByElevation?.Evaluate(cell.ElevationLevel)??1f);if(cell.ElevationLevel>=2)vegetation*=profile.mountainVegetationMultiplier;
        float rocks=Mathf.Max(0f,profile.rockByElevation?.Evaluate(cell.ElevationLevel)??1f);if(cell.ElevationLevel>=2)rocks*=profile.mountainRockMultiplier;
        Add(result,cell,profile,battleSeed,radius,BattleDecorationKind.Tree,profile.treePrefabs,reserved||cell.HasBeach?0:Count(profile.treeDensity*vegetation*(cell.IsForest?profile.forestTreeMultiplier:1f),profile.maximumTrees),profile.treeScaleRange,true);
        Add(result,cell,profile,battleSeed,radius,BattleDecorationKind.Grass,profile.grassPrefabs,Count(profile.grassDensity*vegetation,profile.maximumGrassClumps),profile.grassScaleRange,false);
        Add(result,cell,profile,battleSeed,radius,BattleDecorationKind.Bush,profile.bushPrefabs,reserved?0:Count(profile.bushDensity*vegetation*(cell.IsForest?profile.forestBushMultiplier:1f),profile.maximumBushes),profile.bushScaleRange,true);
        Add(result,cell,profile,battleSeed,radius,BattleDecorationKind.Rock,profile.rockPrefabs,reserved?0:Count(profile.rockDensity*rocks,profile.maximumRocks),profile.rockScaleRange,true);
        Add(result,cell,profile,battleSeed,radius,BattleDecorationKind.Prop,profile.environmentalPropPrefabs,reserved?0:Count(profile.propDensity,profile.maximumProps),profile.propScaleRange,true);
        if(cell.HasSoftCover)Add(result,cell,profile,battleSeed,radius,BattleDecorationKind.SoftCover,profile.softCoverPrefabs,1,profile.bushScaleRange,true);
        if(cell.HasHardCover)Add(result,cell,profile,battleSeed,radius,BattleDecorationKind.HardCover,profile.hardCoverPrefabs,1,profile.rockScaleRange,true);
        if(cell.HasPort)Add(result,cell,profile,battleSeed,radius,BattleDecorationKind.Port,profile.portPrefabs,1,profile.propScaleRange,true);
        return result;
    }
    private static int Count(float density,int maximum)=>Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f,density)*maximum),0,maximum);
    private static void Add(List<BattleDecorationPlacement> output,BattleCell cell,BattleBiomeVisualProfile profile,int seed,float radius,BattleDecorationKind kind,GameObject[] prefabs,int count,Vector2 scaleRange,bool clearCenter)
    {
        int valid=ValidPrefabCount(prefabs);if(valid==0)return;float outer=radius*(1f-profile.edgePadding);
        for(int i=0;i<count;i++)for(int attempt=0;attempt<12;attempt++)
        {
            var rng=new LocalRng(Hash(seed,cell.CampaignTileIndex,(int)kind,i*17+attempt));float angle=rng.Value()*Mathf.PI*2f;float minimum=clearCenter?profile.unitClearRadius:0f;float distance=Mathf.Sqrt(Mathf.Lerp(minimum*minimum,outer*outer,rng.Value()));Vector3 p=new(Mathf.Cos(angle)*distance,.03f,Mathf.Sin(angle)*distance);
            if(cell.HasRiver&&Mathf.Abs(p.x)<profile.riverClearHalfWidth)continue;if(cell.HasBeach&&p.z<0f&&kind!=BattleDecorationKind.Grass)continue;
            int selected=SelectValidPrefab(prefabs,rng.Range(valid));float scale=Mathf.Lerp(scaleRange.x,scaleRange.y,rng.Value());output.Add(new BattleDecorationPlacement(kind,selected,p,rng.Value()*360f,scale));break;
        }
    }
    private static int ValidPrefabCount(GameObject[] prefabs){int count=0;if(prefabs!=null)foreach(var p in prefabs)if(p!=null)count++;return count;}
    private static int SelectValidPrefab(GameObject[] prefabs,int ordinal){for(int i=0;i<prefabs.Length;i++)if(prefabs[i]!=null&&ordinal--==0)return i;return -1;}
    private struct LocalRng{private uint state;public LocalRng(int seed){state=(uint)seed;if(state==0)state=0x9E3779B9;}public float Value(){state^=state<<13;state^=state>>17;state^=state<<5;return (state&0x00FFFFFF)/16777216f;}public int Range(int max)=>Mathf.Min(max-1,Mathf.FloorToInt(Value()*max));}
}
