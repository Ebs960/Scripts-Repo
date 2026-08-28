#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;

public class CityVisualResolverTests
{
    [TestCase(TechAge.PaleolithicAge, CityVisualPeriod.Prehistoric)]
    [TestCase(TechAge.NeolithicAge, CityVisualPeriod.Prehistoric)]
    [TestCase(TechAge.CopperAge, CityVisualPeriod.Prehistoric)]
    [TestCase(TechAge.BronzeAge, CityVisualPeriod.Ancient)]
    [TestCase(TechAge.IronAge, CityVisualPeriod.Ancient)]
    [TestCase(TechAge.ClassicalAge, CityVisualPeriod.Classical)]
    [TestCase(TechAge.DarkAge, CityVisualPeriod.Medieval)]
    [TestCase(TechAge.FeudalAge, CityVisualPeriod.Medieval)]
    [TestCase(TechAge.CastleAge, CityVisualPeriod.Medieval)]
    [TestCase(TechAge.RenaissanceAge, CityVisualPeriod.Medieval)]
    [TestCase(TechAge.ColonialAge, CityVisualPeriod.Enlightenment)]
    [TestCase(TechAge.EnlightenmentAge, CityVisualPeriod.Enlightenment)]
    [TestCase(TechAge.SteamAge, CityVisualPeriod.Enlightenment)]
    [TestCase(TechAge.ImperialAge, CityVisualPeriod.Modern)]
    [TestCase(TechAge.ModernAge, CityVisualPeriod.Modern)]
    [TestCase(TechAge.InformationAge, CityVisualPeriod.Information)]
    [TestCase(TechAge.NanoAge, CityVisualPeriod.Information)]
    [TestCase(TechAge.SolarAge, CityVisualPeriod.Futuristic)]
    [TestCase(TechAge.InterstellarAge, CityVisualPeriod.Futuristic)]
    [TestCase(TechAge.GalacticAge, CityVisualPeriod.Futuristic)]
    public void TechAgeMapsToBroadVisualPeriod(TechAge age, CityVisualPeriod expected)
    {
        Assert.AreEqual(expected, CityVisualResolver.GetVisualPeriod(age));
    }

    [TestCase(1, CityVisualSize.Village)]
    [TestCase(4, CityVisualSize.Village)]
    [TestCase(5, CityVisualSize.Town)]
    [TestCase(9, CityVisualSize.Town)]
    [TestCase(10, CityVisualSize.City)]
    [TestCase(19, CityVisualSize.City)]
    [TestCase(20, CityVisualSize.Metropolis)]
    [TestCase(40, CityVisualSize.Metropolis)]
    [TestCase(100, CityVisualSize.Metropolis)]
    public void LevelMapsToSettlementSize(int level, CityVisualSize expected)
    {
        Assert.AreEqual(expected, CityVisualResolver.GetVisualSize(level));
    }

    [Test]
    public void CivDataIsAuthoritativeEvenForMatchingCultureGroups()
    {
        var first = ScriptableObject.CreateInstance<CivData>();
        var second = ScriptableObject.CreateInstance<CivData>();
        var firstPrefab = new GameObject("FirstVisual");
        var secondPrefab = new GameObject("SecondVisual");
        try
        {
            first.cultureGroup = second.cultureGroup = CultureGroup.Western;
            first.cityVisuals = Sets(CityVisualPeriod.Ancient, firstPrefab, firstPrefab);
            second.cityVisuals = Sets(CityVisualPeriod.Ancient, secondPrefab, secondPrefab);
            Assert.AreSame(firstPrefab, CityVisualResolver.ResolveCityVisual(first, TechAge.BronzeAge, 1));
            Assert.AreSame(secondPrefab, CityVisualResolver.ResolveCityVisual(second, TechAge.BronzeAge, 1));
        }
        finally
        {
            Object.DestroyImmediate(firstPrefab); Object.DestroyImmediate(secondPrefab);
            Object.DestroyImmediate(first); Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void RefreshReplacesOnlyArtworkAndAvoidsEquivalentRespawns()
    {
        var civObject = new GameObject("Civ");
        var cityRoot = new GameObject("PermanentCityRoot");
        var village = new GameObject("VillageVisual");
        var town = new GameObject("TownVisual");
        var data = ScriptableObject.CreateInstance<CivData>();
        var paleolithic = ScriptableObject.CreateInstance<TechData>();
        try
        {
            var civ = civObject.AddComponent<Civilization>();
            var city = cityRoot.AddComponent<City>();
            paleolithic.techAge = TechAge.PaleolithicAge;
            civ.civData = data;
            civ.researchedTechs = new System.Collections.Generic.List<TechData> { paleolithic };
            data.cityVisuals = Sets(CityVisualPeriod.Prehistoric, village, town);
            city.owner = civ;
            city.cityName = "Stateful";
            city.centerTileIndex = 12;
            city.planetIndex = 3;
            city.productionQueue.Add(new City.ProdEntry(null, 7, 0, null, null, false, false, City.ProdEntry.Type.Building));

            city.level = 4;
            city.RefreshCityVisual();
            var firstInstance = city.CurrentVisualInstance;
            city.level = 2;
            city.RefreshCityVisual();
            Assert.AreSame(firstInstance, city.CurrentVisualInstance, "Same bucket must not respawn artwork.");

            city.level = 5;
            city.RefreshCityVisual();
            Assert.AreSame(cityRoot, city.gameObject);
            Assert.AreNotSame(firstInstance, city.CurrentVisualInstance);
            Assert.AreSame(civ, city.owner);
            Assert.AreEqual("Stateful", city.cityName);
            Assert.AreEqual(5, city.level);
            Assert.AreEqual(12, city.centerTileIndex);
            Assert.AreEqual(3, city.planetIndex);
            Assert.AreEqual(1, city.productionQueue.Count);

            var valid = city.CurrentVisualInstance;
            data.cityVisuals[0].townPrefab = null;
            LogAssert.Expect(LogType.Warning, "[City] Missing city visual for civilization '', period Prehistoric, size Town. Keeping the current visual.");
            city.RefreshCityVisual(force: true);
            Assert.AreSame(valid, city.CurrentVisualInstance, "Missing artwork must preserve the valid visual.");
        }
        finally
        {
            Object.DestroyImmediate(cityRoot); Object.DestroyImmediate(civObject);
            Object.DestroyImmediate(village); Object.DestroyImmediate(town);
            Object.DestroyImmediate(data); Object.DestroyImmediate(paleolithic);
        }
    }

    private static CityVisualSet[] Sets(CityVisualPeriod period, GameObject village, GameObject town)
    {
        return new[] { new CityVisualSet { period = period, villagePrefab = village, townPrefab = town } };
    }
}
#endif
