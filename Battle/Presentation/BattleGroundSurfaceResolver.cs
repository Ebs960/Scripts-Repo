using UnityEngine;

public readonly struct BattleGroundSurface
{
    public readonly BiomeVisualData Visual;
    public readonly SurfaceFamilyData Family;
    public readonly int Variant;
    public readonly bool Mountain;
    public BattleGroundSurface(BiomeVisualData visual,SurfaceFamilyData family,int variant,bool mountain){Visual=visual;Family=family;Variant=variant;Mountain=mountain;}
    public Texture2DArray Albedo=>Mountain&&Family?.mountainAlbedoArray!=null?Family.mountainAlbedoArray:Family?.albedoArray;
    public Texture2DArray Normal=>Mountain&&Family?.mountainNormalArray!=null?Family.mountainNormalArray:Family?.normalArray;
    public Texture2DArray Mask=>Mountain&&Family?.mountainMaskArray!=null?Family.mountainMaskArray:Family?.maskArray;
    public Texture2DArray Height=>Mountain&&Family?.mountainHeightArray!=null?Family.mountainHeightArray:Family?.heightArray;
    public Texture2DArray Emissive=>Mountain&&Family?.mountainEmissiveArray!=null?Family.mountainEmissiveArray:Family?.emissiveArray;
}

public static class BattleGroundSurfaceResolver
{
    public static BattleGroundSurface Resolve(BattleCell cell,int battleSeed,BiomeVisualDatabase campaignDatabase,BattleBiomeVisualProfile profile=null)
    {
        BiomeVisualData visual=profile?.biomeVisualOverride;
        if(visual?.surfaceFamily==null)visual=campaignDatabase?.Get(cell.Biome);
        SurfaceFamilyData family=visual?.surfaceFamily;
        bool mountain=cell.ElevationLevel>=3&&family!=null&&family.HasMountainVariants;
        int count=family==null?0:(mountain?family.MountainVariantCount:family.VariantCount);int variant=0;
        if(count>0)variant=visual.forcedVariant>=0?Mathf.Clamp(visual.forcedVariant,0,count-1):PositiveMod(BattleEnvironmentLayout.Hash(battleSeed,cell.CampaignTileIndex,(int)cell.Biome,cell.ElevationLevel),count);
        return new BattleGroundSurface(visual,family,variant,mountain);
    }
    private static int PositiveMod(int value,int count)=>(value&int.MaxValue)%count;
}
