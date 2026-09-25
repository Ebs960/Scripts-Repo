#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using System.Linq;

public class CrisisExpansionTests
{
    private static MissionData Mission(string name)
    {
        var guid=AssetDatabase.FindAssets($"t:MissionData {name}",new[]{"Assets/Scripts Repo/Missions"})
            .First(g=>AssetDatabase.LoadAssetAtPath<MissionData>(AssetDatabase.GUIDToAssetPath(g))?.missionName==name);
        return AssetDatabase.LoadAssetAtPath<MissionData>(AssetDatabase.GUIDToAssetPath(guid));
    }

    [Test] public void CrisisMissionNarratives_ResolveNumericTokensWithoutLeakingBraces()
    {
        var tiger=Mission("Tiger Slayers");
        string rendered=MissionNarrativeFormatter.Resolve(tiger.objectives[0].description,tiger,null,null);
        StringAssert.DoesNotContain("{Objective",rendered);
        StringAssert.IsMatch(@"\d+",rendered);
    }

    [Test] public void NarrativeBatchTwo_HasAuthoredDescriptionsAndResolvableTokens()
    {
        string[] names={"Financial Genius","Trade Lifeline","Rainy Day Fund","Unstoppable Industry","Mass Mobilization",
            "Engineer Corps","Crush the Coup","Buy Their Loyalty","Counterplot","Rule of Law","Grand Coalition",
            "Emergency Mandate","Appeasement","Iron Fist","Reform the State"};
        var snapshot=new CrisisNarrativeSnapshot { factionName="Reform League",demand="Repeal the levy",
            factionDemandSummary="The Reform League demands repeal of the levy.",primaryGrievance="the emergency levy",distinctDemandCount=2 };
        foreach(string name in names)
        {
            var mission=Mission(name);
            Assert.IsFalse(mission.description.Contains("Crisis-specific response objective."),name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(mission.flavorText),name);
            var state=new CrisisManager.MissionState { mission=mission,
                resolvedTargets=mission.objectives.Select(o=>Mathf.Max(1,o.targetValue)).ToArray(),narrativeSnapshot=snapshot };
            string rendered=MissionNarrativeFormatter.Resolve(mission.description+mission.flavorText+mission.objectives[0].description,mission,null,null,state);
            StringAssert.DoesNotMatch(@"\{[A-Za-z]",rendered,name);
        }
    }

    [Test] public void CorrectedIndustryAndTrainingAssets_UseAuthoritativeEventTypes()
    {
        var industry=Mission("Unstoppable Industry").objectives[0];
        Assert.AreEqual(MissionData.ObjectiveType.BuildBuilding,industry.type);
        Assert.IsTrue(industry.useBuildingCategoryFilter);
        Assert.AreEqual(BuildingCategory.Production,industry.buildingCategory);
        Assert.AreEqual(MissionData.ObjectiveType.TrainUnits,Mission("Mass Mobilization").objectives[0].type);
    }

    [Test] public void HoldAndCrisisEndAssets_HaveExplicitTiming()
    {
        Assert.AreEqual(4,Mission("Multitude").objectives[0].requiredConsecutiveTurns);
        Assert.AreEqual(10,Mission("Quarantine").objectives[0].requiredConsecutiveTurns);
        Assert.AreEqual(MissionData.ObjectiveComparison.AtMost,Mission("Quarantine").objectives[0].comparison);
        Assert.AreEqual(MissionData.ObjectiveCompletionTiming.CrisisEnd,Mission("Endure the Plague").objectives[0].completionTiming);
        Assert.AreEqual(MissionData.ObjectiveCompletionTiming.CrisisEnd,Mission("Rainy Day Fund").objectives[0].completionTiming);
        Assert.AreEqual(MissionData.ObjectiveCompletionTiming.CrisisEnd,Mission("Prepare for Impact").objectives[0].completionTiming);
    }

    [Test] public void MissionSavePayload_PreservesTargetsAndConsecutiveProgress()
    {
        var original=new CrisisManager.MissionStateSaveData { resolvedTargets=new[]{7,3}, consecutiveTurnProgress=new[]{4,1} };
        var restored=JsonUtility.FromJson<CrisisManager.MissionStateSaveData>(JsonUtility.ToJson(original));
        CollectionAssert.AreEqual(original.resolvedTargets,restored.resolvedTargets);
        CollectionAssert.AreEqual(original.consecutiveTurnProgress,restored.consecutiveTurnProgress);
    }

    [Test] public void MissionSavePayload_PreservesSnapshottedNarrative()
    {
        var original=new CrisisManager.MissionStateSaveData { narrativeSnapshot=new CrisisNarrativeSnapshot {
            civilizationIndex=2,factionName="Civic Bloc",demand="Restore elections",
            factionDemandSummary="Civic Bloc demands restored elections.",primaryGrievance="suspended elections",distinctDemandCount=2 } };
        var restored=JsonUtility.FromJson<CrisisManager.MissionStateSaveData>(JsonUtility.ToJson(original));
        Assert.AreEqual(original.narrativeSnapshot.factionName,restored.narrativeSnapshot.factionName);
        Assert.AreEqual(original.narrativeSnapshot.demand,restored.narrativeSnapshot.demand);
        Assert.AreEqual(2,restored.narrativeSnapshot.distinctDemandCount);
    }
    [Test] public void TenPercentCombatBonus_IsOnePointOneTimes()
        => Assert.AreEqual(110f, CombatModifierUtility.ApplyFractionalModifier(100f,.10f),.001f);

    [Test] public void DiversityWithoutGrievance_CannotCreateTerrorismRisk()
    {
        var input=new CrisisRiskEvaluator.Inputs { averageOrder=100, averageHappiness=100, loyalty=100, grievanceSeverity=0 };
        Assert.AreEqual(0,CrisisRiskEvaluator.EvaluateTerrorism(input).score);
    }

    [Test] public void SevereGrievanceAndInstability_IncreasesTerrorismRisk()
    {
        var input=new CrisisRiskEvaluator.Inputs { averageOrder=10, averageHappiness=10, loyalty=10, grievanceSeverity=20 };
        Assert.Greater(CrisisRiskEvaluator.EvaluateTerrorism(input).score,20);
    }

    [Test] public void GovernmentFlags_RoutePoliticalCrisesStructurally()
    {
        var representative=ScriptableObject.CreateInstance<GovernmentData>(); representative.archetypes=GovernmentArchetypeFlags.Representative|GovernmentArchetypeFlags.Legislature;
        var centralized=ScriptableObject.CreateInstance<GovernmentData>(); centralized.archetypes=GovernmentArchetypeFlags.CentralizedExecutive;
        var hive=ScriptableObject.CreateInstance<GovernmentData>(); hive.archetypes=GovernmentArchetypeFlags.CollectiveMind|GovernmentArchetypeFlags.MachineRule;
        Assert.IsTrue(CrisisEligibility.IsConstitutional(representative)); Assert.IsFalse(CrisisEligibility.IsConventionalCoup(representative));
        Assert.IsTrue(CrisisEligibility.IsConventionalCoup(centralized)); Assert.IsFalse(CrisisEligibility.IsConventionalCoup(hive));
        Object.DestroyImmediate(representative); Object.DestroyImmediate(centralized); Object.DestroyImmediate(hive);
    }

    [Test] public void CrisisActorTags_RoundTripThroughUnitSavePayload()
    {
        var save=new PauseMenuManager.CombatUnitSaveData { crisisActorTags=(int)(CrisisActorTag.Alien|CrisisActorTag.Rebel), crisisOriginalOwnerCivIndex=3 };
        var restored=JsonUtility.FromJson<PauseMenuManager.CombatUnitSaveData>(JsonUtility.ToJson(save));
        Assert.AreEqual(save.crisisActorTags,restored.crisisActorTags);
        Assert.AreEqual(3,restored.crisisOriginalOwnerCivIndex);
    }

    [Test] public void AllRegisteredCrisisAssets_HaveMissionsAndValidWindows()
    {
        var guids=AssetDatabase.FindAssets("t:CrisisData",new[]{"Assets/Scripts Repo/Missions"}).Where(g=>AssetDatabase.LoadAssetAtPath<CrisisData>(AssetDatabase.GUIDToAssetPath(g)).crisisName!="The Long Cold").ToArray();
        Assert.AreEqual(17,guids.Length);
        foreach(var guid in guids)
        {
            var crisis=AssetDatabase.LoadAssetAtPath<CrisisData>(AssetDatabase.GUIDToAssetPath(guid));
            Assert.IsNotNull(crisis); Assert.IsNotEmpty(crisis.crisisName);
            Assert.LessOrEqual(crisis.minimumAge,crisis.maximumAge,crisis.crisisName);
            Assert.IsTrue(crisis.crisisMissions != null && crisis.crisisMissions.Count >= 2,crisis.crisisName);
            Assert.IsTrue(crisis.crisisMissions.All(m=>m!=null && m.objectives.Count>0 && m.rewardTiers.Any(t=>t.rewardLegacy!=null)),crisis.crisisName);
        }
    }

    [Test] public void LocustAsset_UsesSeasonFilterAndDelayedStateModel()
    {
        var crisis=AssetDatabase.LoadAssetAtPath<CrisisData>("Assets/Scripts Repo/Missions/Locust Infestation/Locust Infestation.asset");
        Assert.IsTrue(crisis.useSeasonFilter);
        CollectionAssert.AreEquivalent(new[]{Season.Spring,Season.Summer},crisis.allowedSeasons);
        Assert.AreEqual(CrisisData.CrisisMechanic.LocustInfestation,crisis.mechanic);
        Assert.AreEqual(ImprovementManager.CrisisImprovementState.Infested,
            System.Enum.Parse(typeof(ImprovementManager.CrisisImprovementState),"Infested"));
    }

    [Test] public void OccurrenceRules_EnforceOneTimeCooldownAndRepeatability()
    {
        var crisis=ScriptableObject.CreateInstance<CrisisData>();
        var history=new CrisisOccurrenceRecord { occurrenceCount=1,lastResolutionTurn=10 };
        crisis.repeatMode=CrisisData.CrisisRepeatMode.OneTime;
        Assert.IsFalse(CrisisOccurrenceRules.CanTrigger(crisis,history,100));
        crisis.repeatMode=CrisisData.CrisisRepeatMode.Repeatable; crisis.cooldownTurns=20;
        Assert.IsFalse(CrisisOccurrenceRules.CanTrigger(crisis,history,29));
        Assert.IsTrue(CrisisOccurrenceRules.CanTrigger(crisis,history,30));
        Object.DestroyImmediate(crisis);
    }
}
#endif
