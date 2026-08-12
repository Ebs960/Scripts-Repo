#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

public class PolicyPrerequisiteTests
{
    private GameObject root;
    private PolicyManager manager;
    private Civilization civ;

    [SetUp] public void SetUp()
    {
        root = new GameObject("Policy prerequisite tests");
        manager = root.AddComponent<PolicyManager>();
        civ = root.AddComponent<Civilization>();
    }
    [TearDown] public void TearDown() => Object.DestroyImmediate(root);

    private static T Data<T>() where T : ScriptableObject => ScriptableObject.CreateInstance<T>();
    private PolicyData Policy(params GovernmentData[] governments) => new PolicyDataFixture { Data = Data<PolicyData>() }.WithGovernments(governments);
    private bool Structural(PolicyData p) => manager.SatisfiesPolicyStructuralRequirements(civ, p);

    [Test] public void MultipleGovernmentsAreOrAlternatives()
    { var republic=Data<GovernmentData>(); var democracy=Data<GovernmentData>(); var p=Policy(republic,democracy); civ.currentGovernment=republic; Assert.True(Structural(p)); civ.currentGovernment=democracy; Assert.True(Structural(p)); }
    [Test] public void UnrelatedGovernmentFails()
    { var p=Policy(Data<GovernmentData>(),Data<GovernmentData>()); civ.currentGovernment=Data<GovernmentData>(); Assert.False(Structural(p)); }
    [Test] public void OneGovernmentRetainsExistingBehavior()
    { var required=Data<GovernmentData>(); var p=Policy(required); civ.currentGovernment=required; Assert.True(Structural(p)); civ.currentGovernment=Data<GovernmentData>(); Assert.False(Structural(p)); }
    [Test] public void EmptyGovernmentArrayIsUnrestricted()
    { Assert.True(Structural(Policy())); }
    [Test] public void TechRequirementsRemainAllOf()
    { var a=Data<TechData>(); var b=Data<TechData>(); var p=Policy(); p.requiredTechs=new[]{a,b}; civ.researchedTechs.Add(a); Assert.False(Structural(p)); civ.researchedTechs.Add(b); Assert.True(Structural(p)); }
    [Test] public void CultureRequirementsRemainAllOf()
    { var a=Data<CultureData>(); var b=Data<CultureData>(); var p=Policy(); p.requiredCultures=new[]{a,b}; civ.researchedCultures.Add(a); Assert.False(Structural(p)); civ.researchedCultures.Add(b); Assert.True(Structural(p)); }

    [Test] public void SpiritMatchesExactSpiritAndUpgradedGodButNotUnrelatedGod()
    { var spirit=Data<PantheonData>(); var god=Data<PantheonData>(); god.tier=PantheonTier.God; spirit.upgradedPantheon=god; var other=Data<PantheonData>(); other.tier=PantheonTier.God; var p=Religious(Pantheons(spirit)); civ.foundedPantheons.Add(spirit); Assert.True(Structural(p)); civ.foundedPantheons[0]=god; Assert.True(Structural(p)); civ.foundedPantheons[0]=other; Assert.False(Structural(p)); }
    [Test] public void GodRequirementDoesNotMatchLowerSpirit()
    { var god=Data<PantheonData>(); god.tier=PantheonTier.God; civ.foundedPantheons.Add(Data<PantheonData>()); Assert.False(Structural(Religious(Pantheons(god)))); }
    [Test] public void MinimumGodTierWorks()
    { var g=new PolicyReligiousRequirementGroup{useMinimumPantheonTier=true,minimumPantheonTier=PantheonTier.God}; var p=Religious(g); civ.foundedPantheons.Add(Data<PantheonData>()); Assert.False(Structural(p)); civ.foundedPantheons[0]=Data<PantheonData>(); civ.foundedPantheons[0].tier=PantheonTier.God; Assert.True(Structural(p)); }
    [Test] public void StateReligionRequirementWorks()
    { var p=Religious(new PolicyReligiousRequirementGroup{requiresStateReligion=true}); Assert.False(Structural(p)); ReligionPoliticsService.TrySetStateReligion(civ,Data<ReligionData>(),StateReligionChangeReason.Event,false,out _); Assert.True(Structural(p)); }
    [Test] public void SpecificReligionRequirementWorks()
    { var required=Data<ReligionData>(); var p=Religious(new PolicyReligiousRequirementGroup{anyStateReligions=new[]{required}}); Assert.False(Structural(p)); ReligionPoliticsService.TrySetStateReligion(civ,required,StateReligionChangeReason.Event,false,out _); Assert.True(Structural(p)); }
    [Test] public void SpecificBeliefAndCategoryRequirementsUsePossessionNotSeason()
    { var b=Data<BeliefData>(); b.category=BeliefCategory.Ritual; b.useSeasonFilter=true; var specific=Religious(new PolicyReligiousRequirementGroup{anyBeliefs=new[]{b}}); var category=Religious(new PolicyReligiousRequirementGroup{anyBeliefCategories=new[]{BeliefCategory.Ritual}}); civ.customAssignedBeliefs.Add(b); Assert.True(Structural(specific)); Assert.True(Structural(category)); }
    [Test] public void ClausesWithinGroupAreAnd()
    { var b=Data<BeliefData>(); var p=Religious(new PolicyReligiousRequirementGroup{requiresStateReligion=true,anyBeliefs=new[]{b}}); civ.customAssignedBeliefs.Add(b); Assert.False(Structural(p)); ReligionPoliticsService.TrySetStateReligion(civ,Data<ReligionData>(),StateReligionChangeReason.Event,false,out _); Assert.True(Structural(p)); }
    [Test] public void ReligiousGroupsAreOr()
    { var spirit=Data<PantheonData>(); var belief=Data<BeliefData>(); var p=Religious(Pantheons(spirit),new PolicyReligiousRequirementGroup{anyBeliefs=new[]{belief}}); civ.customAssignedBeliefs.Add(belief); Assert.True(Structural(p)); }
    [Test] public void NoReligiousGroupsPreservesOldBehavior() => Assert.True(Structural(Policy()));

    [Test] public void GovernmentLossAutoRevokesWithoutRefundOrCouncilPreservation()
    { var required=Data<GovernmentData>(); var p=Policy(required); civ.currentGovernment=Data<GovernmentData>(); civ.activePolicies.Add(p); civ.policyPoints=17; manager.RevalidateActivePolicies(civ); Assert.False(civ.activePolicies.Contains(p)); Assert.AreEqual(17,civ.policyPoints); }
    [Test] public void ReligionLossAutoRevokesWithoutRefund()
    { var religion=Data<ReligionData>(); ReligionPoliticsService.TrySetStateReligion(civ,religion,StateReligionChangeReason.Event,false,out _); var p=Religious(new PolicyReligiousRequirementGroup{requiresStateReligion=true}); civ.activePolicies.Add(p); civ.policyPoints=23; ReligionPoliticsService.TrySetStateReligion(civ,null,StateReligionChangeReason.Event,false,out _); Assert.False(civ.activePolicies.Contains(p)); Assert.AreEqual(23,civ.policyPoints); }

    private PolicyData Religious(params PolicyReligiousRequirementGroup[] groups) { var p=Policy(); p.religiousRequirementGroups=groups; return p; }
    private static PolicyReligiousRequirementGroup Pantheons(params PantheonData[] values) => new PolicyReligiousRequirementGroup{anyPantheons=values,allowPantheonUpgradeDescendants=true};
    private sealed class PolicyDataFixture { public PolicyData Data; public PolicyData WithGovernments(GovernmentData[] values) { Data.requiredGovernments=values; return Data; } }
}
#endif
