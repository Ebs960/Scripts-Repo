#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class BaseGameContentDatabaseTests
{
    [SetUp]
    public void SetUp() => ResourceCache.Clear();

    [TearDown]
    public void TearDown() => ResourceCache.Clear();

    [Test]
    public void RuntimeManifest_ContainsRepresentativeBaseContent()
    {
        Assert.That(Resources.Load<BaseGameContentDatabase>("BaseGameContentDatabase"), Is.Not.Null);
        Assert.That(ResourceCache.GetAllCombatUnits(), Is.Not.Empty);
        Assert.That(ResourceCache.GetAllBuildings(), Is.Not.Empty);
        Assert.That(ResourceCache.GetAllTechData(), Is.Not.Empty);
        Assert.That(ResourceCache.GetAllCivDatas(), Is.Not.Empty);
        Assert.That(ResourceCache.GetAllImprovements(), Is.Not.Empty);
        Assert.That(ResourceCache.GetAllPolicyData(), Is.Not.Empty);
        Assert.That(ResourceCache.GetAllBuildings().Any(value => value != null && value.name == "Palisade"), Is.True);
        Assert.That(ResourceCache.GetCombatUnitByName("Slinger"), Is.Not.Null);
        Assert.That(ResourceCache.GetAllTechData().Any(value => value != null && value.name == "Agriculture"), Is.True);
        Assert.That(ResourceCache.GetAllCivDatas().Any(value => value != null && value.name == "Iroquois"), Is.True);
    }

    [Test]
    public void Clear_AllowsCatalogToLoadAgainWithoutDuplicates()
    {
        var first = ResourceCache.GetAllCombatUnits();
        int count = first.Length;
        Assert.That(count, Is.GreaterThan(0));

        ResourceCache.Clear();

        var second = ResourceCache.GetAllCombatUnits();
        Assert.That(second, Has.Length.EqualTo(count));
        Assert.That(second.Where(value => value != null).Distinct().Count(),
            Is.EqualTo(second.Count(value => value != null)));
    }
}
#endif
