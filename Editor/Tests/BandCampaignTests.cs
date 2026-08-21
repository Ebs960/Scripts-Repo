using NUnit.Framework;
using UnityEngine;

public sealed class BandCampaignTests
{
    [Test]
    public void Band_IsIndependentNonCombatEntity()
    {
        Assert.That(typeof(BaseUnit).IsAssignableFrom(typeof(Band)), Is.False);
        Assert.That(typeof(WorkerUnit).IsAssignableFrom(typeof(Band)), Is.False);
        Assert.That(typeof(CombatUnit).IsAssignableFrom(typeof(Band)), Is.False);
        Assert.That(typeof(Band).GetProperty("CurrentAttack"), Is.Null);
        Assert.That(typeof(Band).GetProperty("CurrentDefense"), Is.Null);
        Assert.That(typeof(Band).GetProperty("CurrentHealth"), Is.Null);
    }

    [Test]
    public void Starvation_IsDeterministic_UsesGrace_AndResetsWhenFed()
    {
        var data = ScriptableObject.CreateInstance<BandData>();
        data.startingPopulation = 10;
        data.startingFoodReserve = 0;
        data.baseFoodConsumptionPerTurn = 1;
        data.populationPerFoodUnit = 10;
        data.starvationGraceTurns = 2;
        data.populationLossPctPerStarvingTurn = .1f;
        data.collapseAfterStarvationTurns = 8;
        var go = new GameObject("test-band");
        var band = go.AddComponent<Band>();
        band.Initialize(data, null, 0, -1);

        band.ProcessFoodUpkeep();
        band.ProcessFoodUpkeep();
        Assert.That(band.Population, Is.EqualTo(10));
        band.ProcessFoodUpkeep();
        Assert.That(band.Population, Is.EqualTo(9));
        Assert.That(band.ConsecutiveStarvationTurns, Is.EqualTo(3));

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void WorkerCannotAttackAnyUnit()
    {
        var workerObject = new GameObject("civilian");
        var worker = workerObject.AddComponent<WorkerUnit>();
        Assert.That(worker.CanAttack(null), Is.False);
        Assert.That(worker.CurrentAttack, Is.Zero);
        Assert.That(worker.CurrentDefense, Is.Zero);
        Object.DestroyImmediate(workerObject);
    }

    [Test]
    public void CivilizationUsesLatestConfiguredArmyPrefabAtOrBeforeCurrentAge()
    {
        var civData = ScriptableObject.CreateInstance<CivData>();
        var early = new GameObject("early-army");
        var future = new GameObject("future-army");
        civData.armyPrefabsByAge = new[]
        {
            new ArmyPrefabByAge { techAge = TechAge.PaleolithicAge, armyPrefab = early },
            new ArmyPrefabByAge { techAge = (TechAge)int.MaxValue, armyPrefab = future }
        };
        var civObject = new GameObject("civilization");
        var civ = civObject.AddComponent<Civilization>();
        typeof(Civilization).GetProperty("civData").SetValue(civ, civData);
        Assert.That(civ.GetCampaignArmyPrefab(), Is.SameAs(early));
        Object.DestroyImmediate(civObject);
        Object.DestroyImmediate(early);
        Object.DestroyImmediate(future);
        Object.DestroyImmediate(civData);
    }
}
