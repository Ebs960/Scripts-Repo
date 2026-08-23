using NUnit.Framework;
using UnityEngine;

public sealed class BattleSiegeTests
{
    [Test]
    public void UnfortifiedCityIsSettlementAndCreatesNoWalls()
    {
        Assert.AreEqual(BattleSiegeType.Settlement, BattleSiegeResolver.ClassifyCity(System.Array.Empty<BuildingData>()));
        var map=Map(); var objective=new BattleObjective{CellIndex=0};
        Assert.That(BattleSiegeLayoutBuilder.Apply(map,BattleSiegeType.Settlement,null,ref objective),Is.Empty);
        Assert.AreEqual(BattleObjectiveType.SettlementCapture,objective.Type);
    }

    [Test]
    public void ExplicitPalisadeProfileIsSelected()
    {
        var wood=Profile("wood",1,BattleFortificationMaterial.Wood,60,Color.yellow);
        var building=ScriptableObject.CreateInstance<BuildingData>(); building.grantsCityFortifications=true; building.tacticalFortificationProfile=wood;
        Assert.AreSame(wood,BattleSiegeResolver.SelectCityProfile(new[]{building}));
    }

    [Test]
    public void HigherStoneTierReplacesPalisadeRatherThanStacking()
    {
        var wood=Profile("wood",1,BattleFortificationMaterial.Wood,60,Color.yellow);
        var stone=Profile("stone",2,BattleFortificationMaterial.Stone,140,Color.gray);
        var a=ScriptableObject.CreateInstance<BuildingData>();a.grantsCityFortifications=true;a.tacticalFortificationProfile=wood;
        var b=ScriptableObject.CreateInstance<BuildingData>();b.grantsCityFortifications=true;b.tacticalFortificationProfile=stone;
        Assert.AreSame(stone,BattleSiegeResolver.SelectCityProfile(new[]{a,b}));
        Assert.AreNotEqual(wood.wallHitPoints,stone.wallHitPoints);
        Assert.AreNotEqual(wood.visualProfile.fallbackColor,stone.visualProfile.fallbackColor);
    }

    [Test]
    public void FortUsesExplicitImprovementProfile()
    {
        var profile=Profile("fort",3,BattleFortificationMaterial.Earthwork,100,Color.green);
        var improvement=ScriptableObject.CreateInstance<ImprovementData>(); improvement.grantsFortifications=true; improvement.tacticalFortificationProfile=profile;
        Assert.AreSame(profile,BattleSiegeResolver.SelectFortProfile(improvement));
    }

    [Test]
    public void BreachStateIsPassableAndSurvivesRoundTripData()
    {
        var wall=new BattleFortificationState{StructureId=4,Kind=BattleFortificationKind.Wall,CurrentHitPoints=5,MaxHitPoints=5};
        Assert.IsTrue(wall.BlocksMovement); Assert.AreEqual(5,wall.ApplyDamage(99)); Assert.IsFalse(wall.BlocksMovement);
        var save=new BattleFortificationSaveData{id=wall.StructureId,kind=(int)wall.Kind,currentHitPoints=wall.CurrentHitPoints,maxHitPoints=wall.MaxHitPoints,breached=wall.IsBreached};
        var restored=new BattleFortificationState{StructureId=save.id,Kind=(BattleFortificationKind)save.kind,CurrentHitPoints=save.currentHitPoints,MaxHitPoints=save.maxHitPoints,IsBreached=save.breached};
        Assert.IsTrue(restored.IsBreached); Assert.Zero(restored.CurrentHitPoints);
    }

    [Test]
    public void FortifiedDeploymentKeepsAttackersOutsideAndDefendersInside()
    {
        var map=Map(); map.Cells[0].IsFortifiedInterior=true;
        var preview=new EngagementPreview{Map=map,FortificationProfile=Profile("x",1,BattleFortificationMaterial.Wood,10,Color.red)};
        BattleDeploymentBuilder.BuildDeploymentZones(map,preview,1);
        Assert.AreEqual(BattleSide.Defender,map.Cells[0].DeploymentOwner);
        Assert.AreEqual(BattleSide.Attacker,map.Cells[2].DeploymentOwner);
    }

    private static BattleFortificationProfile Profile(string id,int tier,BattleFortificationMaterial material,int hp,Color color)
    {
        var visual=ScriptableObject.CreateInstance<BattleFortificationVisualProfile>();visual.fallbackColor=color;
        var p=ScriptableObject.CreateInstance<BattleFortificationProfile>();p.profileId=id;p.fortificationTier=tier;p.material=material;p.wallHitPoints=hp;p.gateHitPoints=hp;p.strongpointHitPoints=hp;p.visualProfile=visual;return p;
    }

    private static BattleMap Map()
    {
        var map=new BattleMap();
        map.AddCell(new BattleCell{BattleIndex=0,SupportsLand=true,NeighborIndices=new[]{1,2}});
        map.AddCell(new BattleCell{BattleIndex=1,SupportsLand=true,NeighborIndices=new[]{0}});
        map.AddCell(new BattleCell{BattleIndex=2,SupportsLand=true,NeighborIndices=new[]{0}});
        return map;
    }
}
