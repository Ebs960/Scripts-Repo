using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds a local tactical board contained wholly within one strategic anchor.</summary>
public sealed class BattleMapBuilder
{
    private static readonly (int q, int r)[] Directions =
    { (1,0), (1,-1), (0,-1), (-1,0), (-1,1), (0,1) };
    private readonly BattleRuleset ruleset;

    public BattleMapBuilder(BattleRuleset ruleset) { this.ruleset = ruleset; }

    public BattleMap Build(EngagementPreview preview)
    {
        if (preview == null) return null;
        int radius = ruleset.GetBattleRadius(preview.TotalUnits);
        if (preview.Theater == BattleTheater.DeepSpace) return BuildLocalMap(preview, radius, null);
        var ts = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;
        var anchor = ts.GetTileData(preview.AnchorTile);
        if (anchor == null) return null;
        var map = BuildLocalMap(preview, radius, anchor);
        BattleElevationResolver.GenerateLocalElevations(map, anchor, preview.RandomSeed);
        preview.PlanetaryEnvironment = ClassifyPlanetaryEnvironment(map);
        return map;
    }

    private BattleMap BuildLocalMap(EngagementPreview preview, int radius, HexTileData anchor)
    {
        var map = new BattleMap();
        for (int q = -radius; q <= radius; q++)
        for (int r = Mathf.Max(-radius, -q-radius); r <= Mathf.Min(radius, -q+radius); r++)
        {
            int distance = (Mathf.Abs(q)+Mathf.Abs(r)+Mathf.Abs(-q-r))/2;
            var cell = new BattleCell {
                BattleIndex=map.CellCount, CampaignTileIndex=preview.AnchorTile, LocalQ=q, LocalR=r,
                IsBoundary=distance==radius, Biome=anchor != null ? anchor.biome : Biome.Plains,
                IsPassable=anchor == null || anchor.isPassable, SupportsAir=anchor != null, SupportsOrbit=anchor != null,
                SupportsSpace=anchor == null
            };
            if (cell.IsBoundary) cell.BoundaryDirection = ClosestBoundaryDirection(q, r);
            ConfigureBaseTerrain(cell, anchor);
            map.AddCell(cell);
        }
        foreach (var cell in map.Cells)
        {
            var neighbors = new List<int>(6);
            for (int d=0; d<Directions.Length; d++)
                if (map.TryGetBattleIndex(cell.LocalQ+Directions[d].q, cell.LocalR+Directions[d].r, out int index)) neighbors.Add(index);
            cell.NeighborIndices=neighbors.ToArray();
        }
        if (anchor != null) GenerateFeatures(map, anchor, preview.RandomSeed);
        return map;
    }

    private static void ConfigureBaseTerrain(BattleCell cell, HexTileData anchor)
    {
        if (anchor == null) { cell.IsPassable=true; cell.SupportsSpace=true; return; }
        bool openWater=anchor.IsWaterTile && !anchor.isRiver && anchor.biome != Biome.River && anchor.biome != Biome.Coast;
        cell.IsWater=openWater; cell.SupportsLand=!openWater; cell.SupportsNavalSurface=openWater;
        cell.SupportsUnderwater=openWater; cell.WaterDepthLevel=openWater?2:0;
        if (openWater) cell.Features|=BattleTerrainFeature.DeepWater;
        cell.HasPort=anchor.improvement != null && anchor.improvement.isPort;
    }

    private static void GenerateFeatures(BattleMap map, HexTileData anchor, int seed)
    {
        bool road=anchor.improvement != null && anchor.improvement.isRoad;
        bool river=anchor.isRiver || anchor.biome==Biome.River;
        foreach (var c in map.Cells)
        {
            int hash=StableHash(seed,c.LocalQ,c.LocalR);
            int roll=(hash&0x7fffffff)%100;
            bool forest=(anchor.biome==Biome.Tropical && roll<55) || (anchor.biome==Biome.Temperate && roll<30);
            bool marsh=anchor.biome==Biome.Swamp && roll<60;
            bool rough=(anchor.biome==Biome.Desert || anchor.isHill || anchor.isMountain) && roll<25;
            if (forest) { c.Features|=BattleTerrainFeature.Forest|BattleTerrainFeature.SoftCover; c.IsForest=true; c.HasSoftCover=true; }
            if (marsh) { c.Features|=BattleTerrainFeature.Marsh|BattleTerrainFeature.SoftCover; c.HasSoftCover=true; }
            if (rough) c.Features|=BattleTerrainFeature.RoughGround;
            // Straight axial corridors are coherent, deterministic, and always cross both board edges.
            if (road && c.LocalR==0) c.Features|=BattleTerrainFeature.Road;
            if (river && c.LocalQ==0)
            {
                c.Features|=BattleTerrainFeature.River|BattleTerrainFeature.ShallowWater; c.HasRiver=true;
                c.IsWater=true; c.WaterDepthLevel=1; c.SupportsNavalSurface=true; c.SupportsUnderwater=true;
                c.SupportsLand=false;
            }
            if (road && river && c.LocalQ==0 && c.LocalR==0)
            {
                c.Features|=BattleTerrainFeature.Bridge; c.SupportsLand=true; c.IsWater=false;
            }
            c.HasHardCover=c.HasFeature(BattleTerrainFeature.HardCover);
        }
    }

    private static int StableHash(int seed,int q,int r) { unchecked { int h=seed; h=h*397^q; return h*397^r; } }
    private static int ClosestBoundaryDirection(int q,int r)
    {
        int best=0, score=int.MinValue;
        for(int i=0;i<Directions.Length;i++) { int s=q*Directions[i].q+r*Directions[i].r; if(s>score){score=s;best=i;} }
        return best;
    }

    private static PlanetaryBattleEnvironment ClassifyPlanetaryEnvironment(BattleMap map)
    {
        int water=0,land=0; bool port=false,beach=false;
        foreach(var c in map.Cells){if(c.IsWater)water++;else land++;port|=c.HasPort;beach|=c.HasBeach;}
        if(port)return PlanetaryBattleEnvironment.Port;
        if(beach&&water>0&&land>0)return PlanetaryBattleEnvironment.Amphibious;
        if(water==0)return PlanetaryBattleEnvironment.Inland;
        if(land==0)return PlanetaryBattleEnvironment.OpenOcean;
        return water>land*2?PlanetaryBattleEnvironment.Archipelago:land>water*2?PlanetaryBattleEnvironment.Coastal:PlanetaryBattleEnvironment.Mixed;
    }
}
