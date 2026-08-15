using System;
using NUnit.Framework;
using System.Collections.Generic;

public class GovernorPortraitTests
{
    [TestCase(TechAge.PaleolithicAge, GovernorPortraitEra.Prehistoric)]
    [TestCase(TechAge.NeolithicAge, GovernorPortraitEra.Prehistoric)]
    [TestCase(TechAge.CopperAge, GovernorPortraitEra.Prehistoric)]
    [TestCase(TechAge.BronzeAge, GovernorPortraitEra.Ancient)]
    [TestCase(TechAge.IronAge, GovernorPortraitEra.Ancient)]
    [TestCase(TechAge.ClassicalAge, GovernorPortraitEra.Ancient)]
    [TestCase(TechAge.DarkAge, GovernorPortraitEra.Ancient)]
    public void MapsEarlyAges(TechAge age, GovernorPortraitEra expected) => Assert.AreEqual(expected, GovernorPortraitEraUtility.GetPortraitEra(age));

    [TestCase(TechAge.FeudalAge, GovernorPortraitEra.Medieval)]
    [TestCase(TechAge.CastleAge, GovernorPortraitEra.Medieval)]
    [TestCase(TechAge.RenaissanceAge, GovernorPortraitEra.Medieval)]
    [TestCase(TechAge.ColonialAge, GovernorPortraitEra.Enlightenment)]
    [TestCase(TechAge.EnlightenmentAge, GovernorPortraitEra.Enlightenment)]
    [TestCase(TechAge.SteamAge, GovernorPortraitEra.Industrial)]
    [TestCase(TechAge.ImperialAge, GovernorPortraitEra.Industrial)]
    [TestCase(TechAge.ModernAge, GovernorPortraitEra.Modern)]
    [TestCase(TechAge.InformationAge, GovernorPortraitEra.Modern)]
    [TestCase(TechAge.NanoAge, GovernorPortraitEra.Modern)]
    [TestCase(TechAge.SolarAge, GovernorPortraitEra.NearFuture)]
    [TestCase(TechAge.InterstellarAge, GovernorPortraitEra.FarFuture)]
    [TestCase(TechAge.GalacticAge, GovernorPortraitEra.FarFuture)]
    public void MapsLaterAges(TechAge age, GovernorPortraitEra expected) => Assert.AreEqual(expected, GovernorPortraitEraUtility.GetPortraitEra(age));

    [Test]
    public void CultureGroupPreservesSerializedValues()
    {
        Assert.AreEqual(8, Enum.GetValues(typeof(CultureGroup)).Length);
        Assert.AreEqual(0, (int)CultureGroup.Western);
        Assert.AreEqual(2, (int)CultureGroup.EastAsian);
        Assert.AreEqual(8, (int)CultureGroup.NativeAmerican);
        Assert.AreEqual(10, (int)CultureGroup.NativeNorthAmerican);
        Assert.AreEqual(13, (int)CultureGroup.SouthAsian);
    }

    [Test]
    public void SelectionAvoidsUsedIdsAndReusesAfterExhaustion()
    {
        var pool = new GovernorPortraitPool();
        for (int i = 1; i <= 10; i++) pool.portraits.Add(new GovernorPortraitEntry { portraitId = $"western_ancient_{i:00}" });
        var nineUsed = new List<string>();
        for (int i = 1; i <= 9; i++) nineUsed.Add($"western_ancient_{i:00}");
        Assert.AreEqual("western_ancient_10", GovernorPortraitService.ChoosePortraitId(pool, nineUsed));
        Assert.Contains(GovernorPortraitService.ChoosePortraitId(pool, pool.portraits.ConvertAll(p => p.portraitId)), pool.portraits.ConvertAll(p => p.portraitId));
    }

    [Test]
    public void SelectionHandlesMissingOrInvalidPool()
    {
        Assert.IsNull(GovernorPortraitService.ChoosePortraitId(null, null));
        Assert.IsNull(GovernorPortraitService.ChoosePortraitId(new GovernorPortraitPool { portraits = null }, null));
        Assert.IsNull(GovernorPortraitService.ChoosePortraitId(new GovernorPortraitPool { portraits = new List<GovernorPortraitEntry> { null, new GovernorPortraitEntry() } }, null));
    }
}
