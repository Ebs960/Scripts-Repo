#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

public class FactionSingleDemandTests
{
    private GameObject civilizationObject;
    private Civilization civilization;
    private FactionBloc faction;

    [SetUp]
    public void SetUp()
    {
        civilizationObject = new GameObject("Faction Demand Test Civilization");
        civilization = civilizationObject.AddComponent<Civilization>();
        var founder = new Governor(1, "Founder", Governor.Specialization.Economic);
        faction = new FactionBloc("Mercantile League", FactionAlignment.Mercantile, founder);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(civilizationObject);
    }

    [Test]
    public void GenerateDemandDoesNotCreateSecondActiveDemand()
    {
        var first = faction.GenerateDemand(civilization, 10);
        var second = faction.GenerateDemand(civilization, 11);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Null);
        Assert.That(faction.ActiveDemands, Has.Count.EqualTo(1));
        Assert.That(faction.ActiveDemands[0], Is.SameAs(first));
    }

    [Test]
    public void SuccessfulResolutionRemovesActiveDemand()
    {
        var demand = faction.GenerateDemand(civilization, 10);

        faction.ResolveDemand(demand, true, civilization, 10);

        Assert.That(faction.ActiveDemands, Is.Empty);
    }
}
#endif