#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

public class AsteroidStrikeTests
{
    [Test] public void RuntimeState_RoundTripPreservesTargetImpactProjectAndPreparation()
    {
        var context=new CrisisRuntimeContext { targetContinentId=3, asteroidState=new AsteroidThreatState {
            planetIndex=1,targetContinentId=3,impactTileIndex=91,impactTurn=120,intercepted=true,
            interceptionTurn=118,impactResolved=false } };
        context.asteroidState.cityPreparations.Add(new AsteroidCityPreparation { cityId=44,sheltersCompleted=true,infrastructureHardeningCompleted=true });
        context.projectProgress.Add(new CrisisProjectProgress { projectName="Asteroid Interception",civilizationIndex=2,
            productionInvested=620,resolvedProductionCost=1450 });
        var restored=JsonUtility.FromJson<CrisisRuntimeContext>(JsonUtility.ToJson(context));
        Assert.AreEqual(3,restored.targetContinentId);
        Assert.AreEqual(91,restored.asteroidState.impactTileIndex);
        Assert.AreEqual(118,restored.asteroidState.interceptionTurn);
        Assert.AreEqual(1450,restored.projectProgress[0].resolvedProductionCost);
        Assert.IsTrue(restored.asteroidState.cityPreparations[0].sheltersCompleted);
        Assert.IsTrue(restored.asteroidState.cityPreparations[0].infrastructureHardeningCompleted);
    }

    [Test] public void DistanceBandsDecreaseAndStopOutsideDisruption()
    {
        Assert.Greater(CrisisManager.GetAsteroidSeverity(1),CrisisManager.GetAsteroidSeverity(2));
        Assert.Greater(CrisisManager.GetAsteroidSeverity(3),CrisisManager.GetAsteroidSeverity(4));
        Assert.AreEqual(0f,CrisisManager.GetAsteroidSeverity(7));
    }

    [Test] public void SheltersAndHardeningReduceButDoNotEliminateDamage()
    {
        Assert.AreEqual(.3f,CrisisManager.ApplyAsteroidMitigation(1f,true,.7f),.001f);
        Assert.AreEqual(.4f,CrisisManager.ApplyAsteroidMitigation(1f,true,.6f),.001f);
        Assert.AreEqual(1f,CrisisManager.ApplyAsteroidMitigation(1f,false,.7f),.001f);
    }

    [Test] public void InterceptionAssetUsesDynamicFiveTurnTopThreeScaling()
    {
        var project=AssetDatabase.LoadAssetAtPath<CrisisProjectData>("Assets/Scripts Repo/Missions/Asteroid Strike/Asteroid Interception Project.asset");
        Assert.IsNotNull(project);
        Assert.IsTrue(project.useDynamicProductionCost);
        Assert.AreEqual(3,project.equivalentTopCityCount);
        Assert.AreEqual(5,project.targetProductionTurns);
    }
}
#endif
