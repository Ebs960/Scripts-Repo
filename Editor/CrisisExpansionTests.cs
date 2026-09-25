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

    private static LegacyData Legacy(string path)
        => AssetDatabase.LoadAssetAtPath<LegacyData>($"Assets/Scripts Repo/Missions/{path}");

    [Test]
    public void FiscalDiscipline_PromotionAndRemoval_AdjustsCorruptionWithoutChangingUnrest()
    {
        var legacy = Legacy("Financial Crisis/Legacies/Financial Genius Legacy.asset");
        Assert.IsNotNull(legacy);
        Assert.AreEqual(0.05f, legacy.goldModifier, 0.0001f);
        Assert.AreEqual(-0.03f, legacy.institutions.corruptionModifier, 0.0001f);
        Assert.AreEqual(0f, legacy.institutions.unrestModifier, 0.0001f);

        var managerObject = new GameObject("Fiscal Discipline LegacyManager Test");
        var civilizationObject = new GameObject("Fiscal Discipline Civilization Test");
        try
        {
            var manager = managerObject.AddComponent<LegacyManager>();
            var civ = civilizationObject.AddComponent<Civilization>();
            civ.corruptionModifier = 0.12f;
            civ.unrestModifier = 0.07f;
            civ.gold = legacy.goldCost;
            civ.policyPoints = legacy.policyPointCost;
            civ.earnedLegacies.Add(legacy);

            Assert.IsTrue(manager.PromoteLegacy(civ, legacy));
            Assert.AreEqual(0.09f, civ.corruptionModifier, 0.0001f);
            Assert.AreEqual(0.07f, civ.unrestModifier, 0.0001f);

            Assert.IsTrue(manager.DemoteLegacy(civ, legacy));
            Assert.AreEqual(0.12f, civ.corruptionModifier, 0.0001f);
            Assert.AreEqual(0.07f, civ.unrestModifier, 0.0001f);
        }
        finally
        {
            Object.DestroyImmediate(civilizationObject);
            Object.DestroyImmediate(managerObject);
        }
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

    [Test] public void FinalCrisisNarrativeBatch_UsesDedicatedFilteredObjectives()
    {
        var acceptance=Mission("Acceptance").objectives[0];
        Assert.AreEqual(MissionData.ObjectiveType.IntegrateCrisisUnits,acceptance.type);
        Assert.IsTrue(acceptance.useCrisisActorTagFilter);
        Assert.AreEqual(CrisisActorTag.Mutant,acceptance.targetCrisisActorTags);
        var exterminators=Mission("Mutant Exterminators").objectives[0];
        Assert.AreEqual(CrisisActorTag.Mutant,exterminators.targetCrisisActorTags);
        var defenders=Mission("Defenders of Earth").objectives[0];
        Assert.AreEqual(CrisisActorTag.Alien,defenders.targetCrisisActorTags);
        Assert.AreEqual(MissionData.ObjectiveType.NegotiateCrisisSettlement,Mission("Xenodiplomats").objectives[0].type);
    }

    [TestCase(7,.5f,1,8,4)]
    [TestCase(7,.6f,2,12,5)]
    [TestCase(9,.5f,3,15,5)]
    public void CrisisActorTargets_AreCeiledAndClamped(int count,float multiplier,int min,int max,int expected)
        => Assert.AreEqual(expected,CrisisManager.ResolveCrisisActorTarget(count,multiplier,min,max));

    [Test] public void CrisisActorRuntimeState_RoundTripsThroughJson()
    {
        var context=new CrisisRuntimeContext();
        context.actorCountsAtActivation.Add(new CrisisActorCountSnapshot { tag=CrisisActorTag.Alien,count=9 });
        context.integratedActorIds.Add(42); context.alienSettlementCivilizationIndices.Add(3);
        var restored=JsonUtility.FromJson<CrisisRuntimeContext>(JsonUtility.ToJson(context));
        Assert.AreEqual(9,restored.actorCountsAtActivation[0].count);
        CollectionAssert.AreEqual(new[]{42},restored.integratedActorIds);
        CollectionAssert.AreEqual(new[]{3},restored.alienSettlementCivilizationIndices);
    }

    [Test] public void FinalSixMissions_HaveAuthoredNarrativeAndResolvableTokens()
    {
        foreach(var name in new[]{"Acceptance","Mutant Exterminators","Cure the Genome","Xenodiplomats","Defenders of Earth","Reverse Engineers"})
        {
            var mission=Mission(name);
            StringAssert.DoesNotContain("Crisis-specific response objective.",mission.description,name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(mission.flavorText),name);
            var state=new CrisisManager.MissionState { mission=mission,resolvedTargets=mission.objectives.Select(o=>Mathf.Max(1,o.minimumTarget>0?o.minimumTarget:o.targetValue)).ToArray() };
            StringAssert.DoesNotMatch(@"\{Objective",MissionNarrativeFormatter.Resolve(mission.description+mission.objectives[0].description,mission,null,null,state),name);
        }
    }

    [Test] public void FinalSixLegacies_HaveAuthoredNarrativeAndIntendedMechanics()
    {
        var adaptive = Legacy("Genetic Disaster/Legacies/Acceptance Legacy.asset");
        Assert.AreEqual(.05f, adaptive.scienceModifier, .0001f);
        Assert.IsFalse(string.IsNullOrWhiteSpace(adaptive.flavorText));
        Assert.AreEqual(1, adaptive.unitBonuses.Length);
        Assert.IsTrue(adaptive.unitBonuses[0].useTargetUnitCategoryFilter);
        Assert.AreEqual(CombatCategory.Mutant, adaptive.unitBonuses[0].targetUnitCategory);
        Assert.AreEqual(.05f, adaptive.unitBonuses[0].defensePct, .0001f);
        Assert.AreEqual(.05f, adaptive.unitBonuses[0].healthPct, .0001f);
        Assert.IsFalse(adaptive.unitBonuses[0].useCrisisActorTagFilter);

        var hunters = Legacy("Genetic Disaster/Legacies/Mutant Exterminators Legacy.asset");
        Assert.IsTrue(hunters.unitBonuses[0].useTargetUnitCategoryFilter);
        Assert.AreEqual(CombatCategory.Mutant, hunters.unitBonuses[0].targetUnitCategory);
        Assert.AreEqual(.05f, hunters.unitBonuses[0].attackPct, .0001f);
        Assert.IsFalse(hunters.unitBonuses[0].useCrisisActorTagFilter);

        var medicine = Legacy("Genetic Disaster/Legacies/Cure the Genome Legacy.asset");
        Assert.IsTrue(medicine.diseaseBonuses[0].affectsAllDiseases);
        Assert.AreEqual(-.10f, medicine.diseaseBonuses[0].cityPopulationLossPct, .0001f);
        Assert.AreEqual(0f, medicine.institutions.populationGrowthModifier, .0001f);

        var xenodiplomacy = Legacy("Alien Invasion/Legacies/Xenodiplomats Legacy.asset");
        Assert.AreEqual(5f, xenodiplomacy.institutions.diplomaticOpinionModifier, .0001f);
        Assert.AreEqual(.05f, xenodiplomacy.institutions.foreignTradeModifier, .0001f);

        var warfare = Legacy("Alien Invasion/Legacies/Defenders of Earth Legacy.asset");
        Assert.IsTrue(warfare.unitBonuses[0].useCrisisActorTagFilter);
        Assert.AreEqual(CrisisActorTag.Alien, warfare.unitBonuses[0].targetCrisisActorTags);
        Assert.AreEqual(.05f, warfare.unitBonuses[0].attackPct, .0001f);

        var engineering = Legacy("Alien Invasion/Legacies/Reverse Engineers Legacy.asset");
        Assert.AreEqual(.08f, engineering.scienceModifier, .0001f);

        foreach (var legacy in new[] { adaptive, hunters, medicine, xenodiplomacy, warfare, engineering })
        {
            StringAssert.DoesNotContain("Earned permanently", legacy.description, legacy.name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(legacy.flavorText), legacy.name);
        }
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
