#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using GameCombat;

public sealed class BattleEnvironmentTests
{
    private GameObject prefab;
    private BattleBiomeVisualProfile profile;
    [SetUp] public void SetUp(){prefab=new GameObject("Decoration prefab");profile=ScriptableObject.CreateInstance<BattleBiomeVisualProfile>();profile.treePrefabs=new[]{prefab};profile.grassPrefabs=new[]{prefab};profile.rockPrefabs=new[]{prefab};profile.maximumTrees=8;profile.maximumGrassClumps=8;profile.maximumRocks=8;profile.treeDensity=.5f;profile.grassDensity=.5f;profile.rockDensity=.5f;profile.unitClearRadius=.5f;profile.edgePadding=.1f;profile.vegetationByElevation=AnimationCurve.Linear(0,1,3,.2f);profile.rockByElevation=AnimationCurve.Linear(0,.5f,3,2f);profile.forestTreeMultiplier=2f;}
    [TearDown] public void TearDown(){Object.DestroyImmediate(prefab);Object.DestroyImmediate(profile);}
    private static BattleCell Cell(int tile=17,int elevation=0)=>new(){BattleIndex=0,CampaignTileIndex=tile,Biome=Biome.Temperate,ElevationLevel=elevation};

    [Test] public void SameSeedProducesSamePlacement(){var a=BattleEnvironmentLayout.Generate(Cell(),profile,42,1.35f);var b=BattleEnvironmentLayout.Generate(Cell(),profile,42,1.35f);Assert.AreEqual(a.Count,b.Count);for(int i=0;i<a.Count;i++){Assert.AreEqual(a[i].LocalPosition,b[i].LocalPosition);Assert.AreEqual(a[i].Yaw,b[i].Yaw);Assert.AreEqual(a[i].Scale,b[i].Scale);}}
    [Test] public void DifferentSeedVariesPlacement(){var a=BattleEnvironmentLayout.Generate(Cell(),profile,42,1.35f);var b=BattleEnvironmentLayout.Generate(Cell(),profile,43,1.35f);Assert.That(a[0].LocalPosition,Is.Not.EqualTo(b[0].LocalPosition));}
    [Test] public void LargePropsStayInsideHexAndOutsideUnitAnchor(){foreach(var p in BattleEnvironmentLayout.Generate(Cell(),profile,9,1.35f))if(p.Kind!=BattleDecorationKind.Grass){float distance=new Vector2(p.LocalPosition.x,p.LocalPosition.z).magnitude;Assert.GreaterOrEqual(distance,profile.unitClearRadius-.001f);Assert.LessOrEqual(distance,1.35f*(1-profile.edgePadding)+.001f);}}
    [Test] public void WaterNeverReceivesLandDecoration(){var cell=Cell();cell.IsWater=true;Assert.IsEmpty(BattleEnvironmentLayout.Generate(cell,profile,1,1.35f));}
    [Test] public void ForestIncreasesTrees(){var open=Cell();var forest=Cell();forest.IsForest=true;int a=BattleEnvironmentLayout.Generate(open,profile,1,1.35f).FindAll(x=>x.Kind==BattleDecorationKind.Tree).Count;int b=BattleEnvironmentLayout.Generate(forest,profile,1,1.35f).FindAll(x=>x.Kind==BattleDecorationKind.Tree).Count;Assert.Greater(b,a);}
    [Test] public void ElevationReducesVegetationAndIncreasesRocks(){var low=BattleEnvironmentLayout.Generate(Cell(elevation:0),profile,1,1.35f);var high=BattleEnvironmentLayout.Generate(Cell(elevation:3),profile,1,1.35f);Assert.Less(high.FindAll(x=>x.Kind==BattleDecorationKind.Grass).Count,low.FindAll(x=>x.Kind==BattleDecorationKind.Grass).Count);Assert.Greater(high.FindAll(x=>x.Kind==BattleDecorationKind.Rock).Count,low.FindAll(x=>x.Kind==BattleDecorationKind.Rock).Count);}
    [Test] public void RiverExclusionFollowsProvidedOrientation(){var cell=Cell();cell.HasRiver=true;profile.treeDensity=1f;profile.maximumTrees=20;var placements=BattleEnvironmentLayout.Generate(cell,profile,5,1.35f,Vector2.right);foreach(var item in placements)if(item.Kind==BattleDecorationKind.Tree)Assert.GreaterOrEqual(Mathf.Abs(item.LocalPosition.z),profile.riverClearHalfWidth-.001f);}
    [Test] public void CoastExclusionUsesWaterFacingNormal(){var cell=Cell();cell.HasBeach=true;profile.treeDensity=1f;profile.maximumTrees=20;var placements=BattleEnvironmentLayout.Generate(cell,profile,7,1.35f,default,Vector2.right);foreach(var item in placements)if(item.Kind==BattleDecorationKind.Tree)Assert.LessOrEqual(item.LocalPosition.x,.001f);}
    [Test] public void NullPrefabArraysAreSafe(){profile.treePrefabs=null;profile.grassPrefabs=null;profile.rockPrefabs=null;Assert.DoesNotThrow(()=>BattleEnvironmentLayout.Generate(Cell(),profile,1,1.35f));}
    [Test] public void MissingArtUsesProceduralPlacementWhenEnabled(){profile.treePrefabs=null;profile.grassPrefabs=null;profile.rockPrefabs=null;profile.allowProceduralFallback=true;Assert.IsNotEmpty(BattleEnvironmentLayout.Generate(Cell(),profile,1,1.35f));}
    [Test] public void MissingArtCanDisableProceduralPlacement(){profile.treePrefabs=null;profile.grassPrefabs=null;profile.rockPrefabs=null;profile.allowProceduralFallback=false;Assert.IsEmpty(BattleEnvironmentLayout.Generate(Cell(),profile,1,1.35f));}

    [Test] public void ProjectileVisualResolverUsesWeaponProjectileAndIndirectArc()
    {
        var projectile=ScriptableObject.CreateInstance<ProjectileData>();var projectilePrefab=new GameObject("Projectile");projectile.projectilePrefab=projectilePrefab;projectile.launchSpeed=23f;
        var equipment=ScriptableObject.CreateInstance<EquipmentData>();equipment.projectileData=projectile;
        var weapon=new TacticalWeaponProfile{equipment=equipment,usesRangedAttack=true,usesIndirectFire=true,tacticalProjectileScale=new Vector3(2f,1f,1f)};
        var visual=BattleProjectileVisualResolver.ResolveForWeapon(weapon,false);
        Assert.AreSame(projectilePrefab,visual.Prefab);Assert.AreEqual(BattleProjectileTravelType.BallisticArc,visual.TravelType);Assert.AreEqual(23f,visual.Speed);Assert.AreEqual(new Vector3(2f,1f,1f),visual.Scale);
        Object.DestroyImmediate(equipment);Object.DestroyImmediate(projectile);Object.DestroyImmediate(projectilePrefab);
    }

    [Test] public void SpecialAttackProjectileOverridesOrdinaryWeaponVisual()
    {
        var ordinary=new GameObject("Ordinary");var specialPrefab=new GameObject("Special");var impact=new GameObject("Impact");
        var weapon=new TacticalWeaponProfile{tacticalProjectilePrefab=ordinary};var profile=ScriptableObject.CreateInstance<BattleAttackProfile>();profile.projectilePrefab=specialPrefab;profile.impactVfxPrefab=impact;profile.projectileTravelType=BattleProjectileTravelType.Beam;profile.projectileSpeed=31f;profile.projectileScale=new Vector3(2f,2f,2f);
        var visual=BattleProjectileVisualResolver.ResolveForWeapon(weapon,true,null,profile);
        Assert.AreSame(specialPrefab,visual.Prefab);Assert.AreSame(impact,visual.ImpactPrefab);Assert.AreEqual(BattleProjectileTravelType.Beam,visual.TravelType);Assert.AreEqual(31f,visual.Speed);Assert.AreEqual(new Vector3(3.2f,3.2f,3.2f),visual.Scale);
        Object.DestroyImmediate(profile);Object.DestroyImmediate(ordinary);Object.DestroyImmediate(specialPrefab);Object.DestroyImmediate(impact);
    }

    [Test] public void GroundResolvesBiomeForcedAndMountainVariants()
    {
        var db=ScriptableObject.CreateInstance<BiomeVisualDatabase>();var visual=ScriptableObject.CreateInstance<BiomeVisualData>();var family=ScriptableObject.CreateInstance<SurfaceFamilyData>();
        family.albedoArray=new Texture2DArray(1,1,3,TextureFormat.RGBA32,false);family.mountainAlbedoArray=new Texture2DArray(1,1,2,TextureFormat.RGBA32,false);visual.biome=Biome.Temperate;visual.surfaceFamily=family;visual.forcedVariant=1;db.biomes.Add(visual);
        var ground=BattleGroundSurfaceResolver.Resolve(Cell(elevation:3),7,db);Assert.AreSame(visual,ground.Visual);Assert.IsTrue(ground.Mountain);Assert.AreEqual(1,ground.Variant);Assert.AreSame(family.mountainAlbedoArray,ground.Albedo);
        Object.DestroyImmediate(family.albedoArray);Object.DestroyImmediate(family.mountainAlbedoArray);Object.DestroyImmediate(family);Object.DestroyImmediate(visual);Object.DestroyImmediate(db);
    }
    [Test] public void EnvironmentClearsAndSecondBuildDoesNotDuplicate()
    {
        var map=new BattleMap();var cell=Cell();cell.HasRiver=true;cell.NeighborIndices=System.Array.Empty<int>();map.AddCell(cell);
        var session=new BattleSession(5,BattleTheater.PlanetaryJoint,-999,-1,cell.CampaignTileIndex,10,12,map,new List<BattleUnitState>(),default,new List<BattleReinforcementGroup>());
        var root=new GameObject("Environment test");var renderer=root.AddComponent<BattleEnvironmentRenderer>();var layout=BattleBoardLayout.Build(session);
        renderer.Build(session,layout,null);int first=renderer.SpawnedObjectCount;Assert.Greater(first,0);renderer.Build(session,layout,null);Assert.AreEqual(first,renderer.SpawnedObjectCount);renderer.Clear();Assert.AreEqual(0,renderer.SpawnedObjectCount);Assert.AreEqual(0,renderer.GrassInstanceCount);Object.DestroyImmediate(root);
    }
}
#endif
