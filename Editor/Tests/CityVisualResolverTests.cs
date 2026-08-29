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

    [TestCase(TechAge.PaleolithicAge, CityVisualSize.Village)]
    [TestCase(TechAge.NeolithicAge, CityVisualSize.Town)]
    [TestCase(TechAge.CopperAge, CityVisualSize.Town)]
    [TestCase(TechAge.BronzeAge, CityVisualSize.City)]
    [TestCase(TechAge.IronAge, CityVisualSize.City)]
    [TestCase(TechAge.ClassicalAge, CityVisualSize.Metropolis)]
    [TestCase(TechAge.DarkAge, CityVisualSize.Metropolis)]
    [TestCase(TechAge.RenaissanceAge, CityVisualSize.Metropolis)]
    [TestCase(TechAge.SteamAge, CityVisualSize.Metropolis)]
    [TestCase(TechAge.ModernAge, CityVisualSize.Metropolis)]
    [TestCase(TechAge.InformationAge, CityVisualSize.Metropolis)]
    [TestCase(TechAge.GalacticAge, CityVisualSize.Metropolis)]
    public void TechAgeCapsSettlementSize(TechAge age, CityVisualSize expected)
    {
        Assert.AreEqual(expected, CityVisualResolver.GetMaxVisualSizeForAge(age));
    }

    [Test]
    public void UnknownTechAgeHasNoImplicitVisualSizeCap()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            CityVisualResolver.GetMaxVisualSizeForAge((TechAge)int.MaxValue));
    }

    [TestCase(TechAge.PaleolithicAge, 40, CityVisualSize.Village)]
    [TestCase(TechAge.NeolithicAge, 1, CityVisualSize.Village)]
    [TestCase(TechAge.NeolithicAge, 4, CityVisualSize.Village)]
    [TestCase(TechAge.NeolithicAge, 5, CityVisualSize.Town)]
    [TestCase(TechAge.NeolithicAge, 9, CityVisualSize.Town)]
    [TestCase(TechAge.NeolithicAge, 10, CityVisualSize.Town)]
    [TestCase(TechAge.NeolithicAge, 20, CityVisualSize.Town)]
    [TestCase(TechAge.NeolithicAge, 40, CityVisualSize.Town)]
    [TestCase(TechAge.CopperAge, 40, CityVisualSize.Town)]
    [TestCase(TechAge.BronzeAge, 1, CityVisualSize.Village)]
    [TestCase(TechAge.BronzeAge, 5, CityVisualSize.Town)]
    [TestCase(TechAge.BronzeAge, 10, CityVisualSize.City)]
    [TestCase(TechAge.BronzeAge, 20, CityVisualSize.City)]
    [TestCase(TechAge.BronzeAge, 40, CityVisualSize.City)]
    [TestCase(TechAge.IronAge, 40, CityVisualSize.City)]
    [TestCase(TechAge.ClassicalAge, 1, CityVisualSize.Village)]
    [TestCase(TechAge.ClassicalAge, 5, CityVisualSize.Town)]
    [TestCase(TechAge.ClassicalAge, 10, CityVisualSize.City)]
    [TestCase(TechAge.ClassicalAge, 20, CityVisualSize.Metropolis)]
    [TestCase(TechAge.ClassicalAge, 40, CityVisualSize.Metropolis)]
    public void LevelAndAgeMapToEffectiveSettlementSize(TechAge age, int level, CityVisualSize expected)
    {
        Assert.AreEqual(expected, CityVisualResolver.GetVisualSizeForAge(age, level));
    }

    [Test]
    public void ResolverUsesExactAgeLimitedPrefabSlot()
    {
        var data = ScriptableObject.CreateInstance<CivData>();
        var village = new GameObject("VillagePrefab");
        var town = new GameObject("TownPrefab");
        var city = new GameObject("CityPrefab");
        var metropolis = new GameObject("MetropolisPrefab");
        try
        {
            data.cityVisuals = CompleteSets(village, town, city, metropolis);

            Assert.AreSame(village, CityVisualResolver.ResolveCityVisual(data, TechAge.PaleolithicAge, 20));
            Assert.AreSame(town, CityVisualResolver.ResolveCityVisual(data, TechAge.NeolithicAge, 20));
            Assert.AreSame(city, CityVisualResolver.ResolveCityVisual(data, TechAge.BronzeAge, 20));
            Assert.AreSame(metropolis, CityVisualResolver.ResolveCityVisual(data, TechAge.ClassicalAge, 20));
            Assert.AreSame(village, CityVisualResolver.ResolveCityVisual(data, TechAge.NeolithicAge, 4));
            Assert.AreSame(town, CityVisualResolver.ResolveCityVisual(data, TechAge.BronzeAge, 5));
            Assert.AreSame(city, CityVisualResolver.ResolveCityVisual(data, TechAge.ClassicalAge, 10));
        }
        finally
        {
            Object.DestroyImmediate(village); Object.DestroyImmediate(town);
            Object.DestroyImmediate(city); Object.DestroyImmediate(metropolis);
            Object.DestroyImmediate(data);
        }
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
        var neolithic = ScriptableObject.CreateInstance<TechData>();
        try
        {
            var civ = civObject.AddComponent<Civilization>();
            var city = cityRoot.AddComponent<City>();
            neolithic.techAge = TechAge.NeolithicAge;
            civ.civData = data;
            civ.researchedTechs = new System.Collections.Generic.List<TechData> { neolithic };
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
            Object.DestroyImmediate(data); Object.DestroyImmediate(neolithic);
        }
    }

    [Test]
    public void RefreshUsesEffectiveSizeForCachingAndAgeAdvancement()
    {
        var civObject = new GameObject("Civ");
        var cityRoot = new GameObject("PermanentCityRoot");
        var prehistoricTown = new GameObject("PrehistoricTown");
        var ancientCity = new GameObject("AncientCity");
        var data = ScriptableObject.CreateInstance<CivData>();
        var neolithic = ScriptableObject.CreateInstance<TechData>();
        var bronze = ScriptableObject.CreateInstance<TechData>();
        try
        {
            var civ = civObject.AddComponent<Civilization>();
            var city = cityRoot.AddComponent<City>();
            neolithic.techAge = TechAge.NeolithicAge;
            bronze.techAge = TechAge.BronzeAge;
            civ.civData = data;
            civ.researchedTechs = new System.Collections.Generic.List<TechData> { neolithic };
            data.cityVisuals = new[]
            {
                new CityVisualSet { period = CityVisualPeriod.Prehistoric, townPrefab = prehistoricTown },
                new CityVisualSet { period = CityVisualPeriod.Ancient, cityPrefab = ancientCity }
            };
            city.owner = civ;
            city.level = 5;

            city.RefreshCityVisual();
            var townInstance = city.CurrentVisualInstance;
            city.level = 15;
            city.RefreshCityVisual();
            Assert.AreSame(townInstance, city.CurrentVisualInstance, "Levels clamped to Town must not respawn artwork.");

            civ.researchedTechs.Add(bronze);
            city.RefreshCityVisual();
            Assert.AreNotSame(townInstance, city.CurrentVisualInstance);
            Assert.AreEqual("AncientCity(Clone)", city.CurrentVisualInstance.name);
        }
        finally
        {
            Object.DestroyImmediate(cityRoot); Object.DestroyImmediate(civObject);
            Object.DestroyImmediate(prehistoricTown); Object.DestroyImmediate(ancientCity);
            Object.DestroyImmediate(data); Object.DestroyImmediate(neolithic); Object.DestroyImmediate(bronze);
        }
    }

    private static CityVisualSet[] Sets(CityVisualPeriod period, GameObject village, GameObject town)
    {
        return new[] { new CityVisualSet { period = period, villagePrefab = village, townPrefab = town } };
    }

    private static CityVisualSet[] CompleteSets(GameObject village, GameObject town, GameObject city, GameObject metropolis)
    {
        return new[]
        {
            new CityVisualSet { period = CityVisualPeriod.Prehistoric, villagePrefab = village, townPrefab = town, cityPrefab = city, metropolisPrefab = metropolis },
            new CityVisualSet { period = CityVisualPeriod.Ancient, villagePrefab = village, townPrefab = town, cityPrefab = city, metropolisPrefab = metropolis },
            new CityVisualSet { period = CityVisualPeriod.Classical, villagePrefab = village, townPrefab = town, cityPrefab = city, metropolisPrefab = metropolis }
        };
    }
}
#endif
