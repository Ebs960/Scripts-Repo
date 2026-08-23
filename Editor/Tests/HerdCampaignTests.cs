using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class HerdCampaignTests
{
    [Test]
    public void Herd_IsCampaignEntity_NotCombatSnapshotSource()
    {
        Assert.That(typeof(BaseUnit).IsAssignableFrom(typeof(Herd)), Is.False);
        Assert.That(typeof(CombatUnit).IsAssignableFrom(typeof(Herd)), Is.False);
        Assert.That(typeof(Herd).GetProperty("CurrentHealth"), Is.Null);
        Assert.That(typeof(BattleUnitSnapshot).GetFields().Any(f => f.FieldType == typeof(Herd)), Is.False);
    }

    [Test]
    public void FoodShortageDeterministicallyReducesLivestock()
    {
        var go = new GameObject("test-herd");
        var herd = go.AddComponent<Herd>();
        herd.animals.Add(new Herd.HerdEntry { species = Herd.HerdSpecies.Cow, count = 100 });
        herd.foodReserve = 1;
        herd.ProcessLivestockUpkeep();
        Assert.That(herd.foodReserve, Is.Zero);
        Assert.That(herd.GetTotalAnimalCount(), Is.EqualTo(50));
        Assert.That(herd.lastStarvationLoss, Is.EqualTo(50));
        Object.DestroyImmediate(go);
    }

    [Test]
    public void LivestockRaidPreservesHerdAndUsesConfiguredLosses()
    {
        var go = new GameObject("test-herd");
        var herd = go.AddComponent<Herd>();
        herd.animals.Add(new Herd.HerdEntry { species = Herd.HerdSpecies.Sheep, count = 100 });
        herd.foodReserve = 40; herd.predatorLivestockLossPct = .2f; herd.predatorFoodLossPct = .25f;
        herd.ResolveLivestockRaid();
        Assert.That(herd.GetTotalAnimalCount(), Is.EqualTo(80));
        Assert.That(herd.foodReserve, Is.EqualTo(30));
        Assert.That(herd, Is.Not.Null);
        Object.DestroyImmediate(go);
    }

    [Test]
    public void PackSettlePreservesStructuresAndProductionProgress()
    {
        var go = new GameObject("test-herd"); var herd = go.AddComponent<Herd>();
        var building = ScriptableObject.CreateInstance<BuildingData>(); building.buildableByHerd = true;
        herd.BuildStructure(building); herd.productionQueue.Add(new Herd.ProdEntry(building, 20, 0, null, null));
        herd.Settle(); int progress = herd.productionQueue[0].remainingPts; herd.Pack(); herd.ProcessProduction();
        Assert.That(herd.builtStructures, Contains.Item(building));
        Assert.That(herd.productionQueue[0].remainingPts, Is.EqualTo(progress));
        Object.DestroyImmediate(building); Object.DestroyImmediate(go);
    }
}
