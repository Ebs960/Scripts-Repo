#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>Edit-mode regression coverage for the serialized Long Cold contract.</summary>
public class IceAgeCrisisTests
{
    private static T FindAsset<T>(string name) where T : UnityEngine.Object
    {
        string guid = AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}")
            .FirstOrDefault(candidate => AssetDatabase.GUIDToAssetPath(candidate).EndsWith($"/{name}.asset"));
        Assert.That(guid, Is.Not.Null, $"Missing {typeof(T).Name} asset '{name}'");
        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
    }

    [Test]
    public void HearthRequiresFullCrisisAndCompletedObjectiveForReward()
    {
        var mission = FindAsset<MissionData>("Oath of the Hearth");
        Assert.That(mission.objectives.Single().type, Is.EqualTo(MissionData.ObjectiveType.SurviveTurns));
        Assert.That(mission.objectives.Single().targetValue, Is.EqualTo(13));
        Assert.That(mission.rewardTiers.Single().requiredObjectivesCompleted, Is.EqualTo(1));
    }

    [Test]
    public void HearthFailsOnAnyPlayerCombatOrWorkerLoss()
    {
        var constraint = FindAsset<MissionData>("Oath of the Hearth").constraints.Single();
        Assert.That(constraint.type, Is.EqualTo(MissionData.ConstraintType.NoUnitLosses));
        Assert.That(constraint.activatesAfterObjectiveIndex, Is.EqualTo(-1));
        Assert.That(constraint.specificUnit, Is.Null);
        Assert.That(constraint.specificUnits, Is.Empty);
        Assert.That(constraint.specificWorkerUnits, Is.Empty);
        Assert.That(constraint.specificCategories, Is.Empty);
    }

    [Test]
    public void SteadfastnessAcceptsOneOrMoreShelters()
    {
        var constraint = FindAsset<MissionData>("Oath of Steadfastness").constraints.Single();
        Assert.That(constraint.type, Is.EqualTo(MissionData.ConstraintType.MaintainImprovementCount));
        Assert.That(constraint.comparison, Is.EqualTo(MissionData.CountComparison.AtLeast));
        Assert.That(constraint.targetValue, Is.EqualTo(1));
        Assert.That(constraint.specificImprovements.Length, Is.EqualTo(2));
    }

    [Test]
    public void SurvivalOathsMatchCrisisDuration()
    {
        var crisis = FindAsset<CrisisData>("The Long Cold");
        Assert.That(FindAsset<MissionData>("Oath of the Hearth").objectives.Single().targetValue, Is.EqualTo(crisis.durationTurns));
        Assert.That(FindAsset<MissionData>("Oath of Steadfastness").objectives.Single().targetValue, Is.EqualTo(crisis.durationTurns));
    }

    [Test]
    public void EveryOathAwardsItsIntendedLegacyAfterItsObjective()
    {
        AssertReward("Oath of the Hearth", "No Man Left Behind");
        AssertReward("Oath of Steadfastness", "Hold the Fort");
        AssertReward("Oath of the Spear", "Big Game Hunters");
        AssertReward("Oath of Blood", "Blood Lust");
    }

    [Test]
    public void LongColdContainsAllFourUniqueMissions()
    {
        var missions = FindAsset<CrisisData>("The Long Cold").crisisMissions;
        Assert.That(missions.Count, Is.EqualTo(4));
        Assert.That(missions.All(mission => mission != null), Is.True);
        Assert.That(missions.Select(mission => mission.name).Distinct().Count(), Is.EqualTo(4));
    }

    [Test]
    public void LongColdPhaseTimelineIsThirteenTurns()
    {
        var crisis = FindAsset<CrisisData>("The Long Cold");
        Assert.That(crisis.durationTurns, Is.EqualTo(13));
        Assert.That(crisis.escalationAtTurn, Is.EqualTo(4), "Elapsed turn 4 is player-facing turn 5");
        Assert.That(crisis.climaxAtTurn, Is.EqualTo(9), "Elapsed turn 9 is player-facing turn 10");
        Assert.That(crisis.escalationText, Is.Not.Empty);
        Assert.That(crisis.climaxText, Is.Not.Empty);
    }

    [Test]
    public void EnvironmentalPhasesReplaceEachEffectOnce()
    {
        var crisis = FindAsset<CrisisData>("The Long Cold");
        AssertUniqueOverrideTypes(crisis.worldOverrides);
        AssertUniqueOverrideTypes(crisis.escalationWorldOverrides);
        AssertUniqueOverrideTypes(crisis.climaxWorldOverrides);
        Assert.That(crisis.worldOverrides.Any(item => item.type == CrisisData.WorldOverrideType.FoodYieldMultiplier), Is.False);
    }

    [Test]
    public void EnvironmentalDangerIncreasesByPhase()
    {
        var crisis = FindAsset<CrisisData>("The Long Cold");
        Assert.That(Value(crisis.worldOverrides, CrisisData.WorldOverrideType.WinterAttritionDamage), Is.EqualTo(4));
        Assert.That(Value(crisis.escalationWorldOverrides, CrisisData.WorldOverrideType.WinterAttritionDamage), Is.EqualTo(6));
        Assert.That(Value(crisis.climaxWorldOverrides, CrisisData.WorldOverrideType.WinterAttritionDamage), Is.EqualTo(8));
        Assert.That(Value(crisis.worldOverrides, CrisisData.WorldOverrideType.PreySpawnMultiplier), Is.GreaterThan(Value(crisis.climaxWorldOverrides, CrisisData.WorldOverrideType.PreySpawnMultiplier)));
        Assert.That(Value(crisis.worldOverrides, CrisisData.WorldOverrideType.PredatorSpawnMultiplier), Is.LessThan(Value(crisis.climaxWorldOverrides, CrisisData.WorldOverrideType.PredatorSpawnMultiplier)));
    }

    private static void AssertReward(string missionName, string legacyName)
    {
        var mission = FindAsset<MissionData>(missionName);
        var tier = mission.rewardTiers.Single();
        Assert.That(tier.requiredObjectivesCompleted, Is.EqualTo(mission.objectives.Count));
        Assert.That(tier.rewardLegacy, Is.SameAs(FindAsset<LegacyData>(legacyName)));
    }

    private static void AssertUniqueOverrideTypes(CrisisData.WorldOverride[] overrides)
    {
        Assert.That(overrides, Is.Not.Null.And.Not.Empty);
        Assert.That(overrides.Select(item => item.type).Distinct().Count(), Is.EqualTo(overrides.Length));
    }

    private static float Value(CrisisData.WorldOverride[] overrides, CrisisData.WorldOverrideType type)
    {
        return overrides.Single(item => item.type == type).value;
    }
}
#endif
