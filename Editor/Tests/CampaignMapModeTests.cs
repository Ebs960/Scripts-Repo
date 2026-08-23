using NUnit.Framework;

public class CampaignMapModeTests
{
    [Test]
    public void UnifiedEnumContainsAllSevenModes()
    {
        Assert.AreEqual(7, System.Enum.GetValues(typeof(CampaignMapMode)).Length);
    }

    [TestCase(DiplomaticState.War, CampaignDiplomacyCategory.Hostile)]
    [TestCase(DiplomaticState.Alliance, CampaignDiplomacyCategory.Friendly)]
    [TestCase(DiplomaticState.Vassal, CampaignDiplomacyCategory.Friendly)]
    [TestCase(DiplomaticState.Peace, CampaignDiplomacyCategory.Neutral)]
    [TestCase(DiplomaticState.Trade, CampaignDiplomacyCategory.Neutral)]
    [TestCase(DiplomaticState.Protected, CampaignDiplomacyCategory.Neutral)]
    public void DiplomacyUsesCanonicalStateMapping(DiplomaticState state, CampaignDiplomacyCategory expected)
    {
        Assert.AreEqual(expected, CampaignMapModeDataService.ClassifyDiplomaticState(false, state));
    }

    [Test]
    public void DiplomacySelfOverridesRelationship()
    {
        Assert.AreEqual(CampaignDiplomacyCategory.Self,
            CampaignMapModeDataService.ClassifyDiplomaticState(true, DiplomaticState.War));
    }

    [Test]
    public void ReligionDominanceUsesDominantOverTotal()
    {
        Assert.AreEqual(.7f, CampaignMapModeDataService.CalculateDominance(70f, 100f), .0001f);
        Assert.AreEqual(0f, CampaignMapModeDataService.CalculateDominance(0f, 0f));
    }

    [Test]
    public void SharedBorderEdgeIsGeneratedOnlyOnce()
    {
        Assert.IsTrue(CampaignMapBorderRenderer.ShouldCreateThematicEdge(3, 4, 1, 2));
        Assert.IsFalse(CampaignMapBorderRenderer.ShouldCreateThematicEdge(4, 3, 2, 1));
        Assert.IsFalse(CampaignMapBorderRenderer.ShouldCreateThematicEdge(3, 4, 1, 1));
    }
}
