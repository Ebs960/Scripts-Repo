#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class BattleBoardLayoutTests
{
    [Test]
    public void LayoutPreservesTopologyAndElevation()
    {
        var map=new BattleMap();
        map.AddCell(new BattleCell{BattleIndex=0,CampaignTileIndex=100,NeighborIndices=new[]{1,2},ElevationLevel=0});
        map.AddCell(new BattleCell{BattleIndex=1,CampaignTileIndex=101,NeighborIndices=new[]{0,2},ElevationLevel=1});
        map.AddCell(new BattleCell{BattleIndex=2,CampaignTileIndex=102,NeighborIndices=new[]{0,1},ElevationLevel=3});
        var session=new BattleSession(1,BattleTheater.PlanetaryJoint,-999,-1,100,10,4,map,new List<BattleUnitState>(),default,new List<BattleReinforcementGroup>());
        var layout=BattleBoardLayout.Build(session);
        Assert.That(layout.GetCellCenter(1),Is.Not.EqualTo(layout.GetCellCenter(2)));
        Assert.That(layout.GetCellCenter(2).y-layout.GetCellCenter(0).y,Is.EqualTo(3f*BattleBoardLayout.ElevationStep).Within(.001f));
        Assert.That(layout.Bounds.Contains(layout.GetCellCenter(0)),Is.True);
        Assert.That(layout.Bounds.Contains(layout.GetCellCenter(2)),Is.True);
    }
}
#endif
