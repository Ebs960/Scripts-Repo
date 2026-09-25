#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Edit-mode regression coverage for the Batch 3 legacy mechanics.</summary>
public class LegacyBatch3Tests
{
    private static T FindAsset<T>(string name) where T : Object
    {
        string guid = AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}")
            .FirstOrDefault(candidate => AssetDatabase.GUIDToAssetPath(candidate).EndsWith($"/{name}.asset"));
        Assert.That(guid, Is.Not.Null, $"Missing {typeof(T).Name} asset '{name}'");
        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
    }

    [Test]
    public void AgriculturalExpansionOnlyAcceleratesAgriculturalImprovementWork()
    {
        var legacy = FindAsset<LegacyData>("New Pastures Legacy");
        Assert.That(legacy.improvementBonuses, Is.Empty);
        Assert.That(legacy.improvementWorkBonuses.Single().agriculturalOnly, Is.True);
        Assert.That(legacy.improvementWorkBonuses.Single().workPct, Is.EqualTo(0.10f));

        var civObject = new GameObject("Legacy work test civilization");
        var civ = civObject.AddComponent<Civilization>();
        var farm = ScriptableObject.CreateInstance<ImprovementData>();
        var mine = ScriptableObject.CreateInstance<ImprovementData>();
        farm.name = "Test Farm";
        mine.name = "Test Mine";
        civ.activeLegacies.Add(legacy);
        try
        {
            // Player commands and automatic/AI assignment both call AddWork with this same calculation.
            Assert.That(ImprovementManager.GetImprovementWorkPoints(civ, farm, 10), Is.EqualTo(11));
            Assert.That(ImprovementManager.GetImprovementWorkPoints(civ, mine, 10), Is.EqualTo(10));
            civ.activeLegacies.Remove(legacy);
            Assert.That(ImprovementManager.GetImprovementWorkPoints(civ, farm, 10), Is.EqualTo(10));
        }
        finally
        {
            Object.DestroyImmediate(farm);
            Object.DestroyImmediate(mine);
            Object.DestroyImmediate(civObject);
        }
    }

    [Test]
    public void FlexibleEmpireChangesEffectiveButNotBaseSubjectOpinion()
    {
        var legacy = FindAsset<LegacyData>("Imperial Reform Legacy");
        var managerObject = new GameObject("Subject manager test");
        var overlordObject = new GameObject("Overlord");
        var manager = managerObject.AddComponent<SubjectManager>();
        var overlord = overlordObject.AddComponent<Civilization>();
        var contract = new VassalContract { overlord = overlord, subjectOpinion = 12f };
        try
        {
            Assert.That(manager.GetEffectiveSubjectOpinion(contract), Is.EqualTo(12f));
            overlord.activeLegacies.Add(legacy);
            Assert.That(manager.GetEffectiveSubjectOpinion(contract), Is.EqualTo(17f));
            Assert.That(contract.subjectOpinion, Is.EqualTo(12f));
            overlord.activeLegacies.Remove(legacy);
            Assert.That(manager.GetEffectiveSubjectOpinion(contract), Is.EqualTo(12f));
        }
        finally
        {
            Object.DestroyImmediate(overlordObject);
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void CorrectedTargetedLegaciesHaveRequestedFilters()
    {
        var foundingMyth = FindAsset<LegacyData>("Liberty or Death Legacy");
        Assert.That(foundingMyth.defenseModifier, Is.Zero);
        Assert.That(foundingMyth.unitBonuses.Single().territoryRequirement, Is.EqualTo(UnitTerritoryRequirement.Owned));
        Assert.That(foundingMyth.unitBonuses.Single().defensePct, Is.EqualTo(0.05f));

        var cohesion = FindAsset<LegacyData>("Hearts and Minds Legacy");
        Assert.That(cohesion.nonStateReligionUnhappinessModifiers.Single().unhappinessPct, Is.EqualTo(-0.10f));
        Assert.That(cohesion.nonStateReligionUnhappinessModifiers.Single().unhappinessPerFollowerAdd, Is.Zero);

        var command = FindAsset<LegacyData>("Human Supremacy Legacy");
        CollectionAssert.AreEquivalent(new[] { CombatCategory.Robot, CombatCategory.Cyborg },
            command.unitBonuses.Select(bonus => bonus.targetUnitCategory));
        Assert.That(command.unitBonuses.All(bonus => bonus.attackPct == 0.05f && bonus.defensePct == 0.05f), Is.True);
    }
}
#endif
