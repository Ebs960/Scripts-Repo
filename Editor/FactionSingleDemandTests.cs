#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class FactionSingleDemandTests
{
    private GameObject civilizationObject;
    private Civilization civilization;
    private FactionBloc faction;
    private readonly List<Object> createdObjects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        civilizationObject = new GameObject("Faction Demand Test Civilization");
        createdObjects.Add(civilizationObject);
        civilization = civilizationObject.AddComponent<Civilization>();
        var founder = new Governor(1, "Founder", Governor.Specialization.Economic);
        faction = new FactionBloc("Mercantile League", FactionAlignment.Mercantile, founder);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
            if (createdObjects[i] != null) Object.DestroyImmediate(createdObjects[i]);
        createdObjects.Clear();
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

    [Test]
    public void SatisfiedCompatibleGovernorCanJoinFaction()
    {
        var governor = CreateGovernorWithCity(2, "Merchant", Governor.Specialization.Economic);
        governor.Opinion = 80f;

        Assert.That(faction.CanJoin(governor), Is.True);
    }

    [Test]
    public void SatisfiedCompatibleGovernorsCanFormAndKeepFaction()
    {
        civilization.nobleFactions.Clear();
        var first = CreateGovernorWithCity(20, "First Scholar", Governor.Specialization.Scientific);
        var second = CreateGovernorWithCity(21, "Second Scholar", Governor.Specialization.Scientific);
        first.Opinion = 80f;
        second.Opinion = 75f;
        civilization.governors.Add(first);
        civilization.governors.Add(second);

        civilization.ProcessFactionTick(3);
        civilization.ProcessFactionTick(4);

        Assert.That(civilization.nobleFactions, Has.Count.EqualTo(1));
        Assert.That(civilization.nobleFactions[0].Alignment, Is.EqualTo(FactionAlignment.Reformist));
        Assert.That(civilization.nobleFactions[0].Members, Has.Count.EqualTo(2));
    }

    [Test]
    public void CouncilSeatDoesNotRemoveFactionMembership()
    {
        var government = ScriptableObject.CreateInstance<GovernmentData>();
        createdObjects.Add(government);
        government.usesRoyalCouncil = true;
        government.councilSeatCount = 2;
        civilization.currentGovernment = government;
        var founder = faction.Leader;
        civilization.governors.Add(founder);

        Assert.That(civilization.AddToCouncil(founder), Is.True);
        Assert.That(founder.Faction, Is.SameAs(faction));
        Assert.That(faction.Members, Does.Contain(founder));
    }

    [Test]
    public void ScientificGovernorNaturallyFormsReformistAlignment()
    {
        var governor = new Governor(3, "Scholar", Governor.Specialization.Scientific);
        Assert.That(Civilization.DetermineAlignment(governor), Is.EqualTo(FactionAlignment.Reformist));
    }

    [Test]
    public void ReligiousFactionRejectsDifferentReligion()
    {
        var firstReligion = ScriptableObject.CreateInstance<ReligionData>();
        var secondReligion = ScriptableObject.CreateInstance<ReligionData>();
        createdObjects.Add(firstReligion);
        createdObjects.Add(secondReligion);
        var founder = new Governor(4, "Priest", Governor.Specialization.Religious) { PersonalReligion = firstReligion };
        var religiousFaction = new FactionBloc("Faithful", FactionAlignment.Religious, founder, firstReligion);
        var candidate = CreateGovernorWithCity(5, "Outsider", Governor.Specialization.Religious);
        candidate.PersonalReligion = secondReligion;

        Assert.That(religiousFaction.CanJoin(candidate), Is.False);
    }

    [Test]
    public void PowerDoesNotChangeWithSatisfaction()
    {
        CreateGovernorWithCity(6, "Second Merchant", Governor.Specialization.Economic, faction);
        float originalPower = faction.ComputePower();
        faction.RestorePoliticalState(5f, 0);
        float hostilePower = faction.ComputePower();
        faction.RestorePoliticalState(95f, 0);

        Assert.That(hostilePower, Is.EqualTo(originalPower));
        Assert.That(faction.ComputePower(), Is.EqualTo(originalPower));
    }

    [Test]
    public void SupportiveFactionProvidesCappedLoyaltyBenefit()
    {
        CreateGovernorWithCity(7, "Second Merchant", Governor.Specialization.Economic, faction);
        faction.RestorePoliticalState(90f, 0);
        Assert.That(faction.GetSupportLoyaltyBonus(), Is.GreaterThan(0f).And.LessThanOrEqualTo(5f));
    }

    [Test]
    public void CooperativeBuildingRequestCompletesOnlyOnceAfterAcceptance()
    {
        var city = CreateCityFor(faction.Leader);
        var building = ScriptableObject.CreateInstance<BuildingData>();
        createdObjects.Add(building);
        building.isFaithBuilding = true;
        var request = CooperativeRequest(building, 10);
        faction.ActiveDemands.Add(request);
        float before = faction.Satisfaction;

        faction.ResolveDemand(request, true, civilization, 10);
        Assert.That(faction.TryCompleteBuildingRequest(civilization, city, building, 11), Is.True);
        Assert.That(faction.TryCompleteBuildingRequest(civilization, city, building, 11), Is.False);
        Assert.That(faction.Satisfaction, Is.EqualTo(before + request.satisfactionReward).Within(0.01f));
        Assert.That(faction.ActiveDemands, Is.Empty);
    }

    [Test]
    public void CooperativeBuildingRequestRejectsNonQualifyingBuilding()
    {
        var city = CreateCityFor(faction.Leader);
        var requested = ScriptableObject.CreateInstance<BuildingData>();
        var wrong = ScriptableObject.CreateInstance<BuildingData>();
        createdObjects.Add(requested);
        createdObjects.Add(wrong);
        requested.isFaithBuilding = true;
        wrong.isFaithBuilding = false;
        var request = CooperativeRequest(requested, 10);
        faction.ActiveDemands.Add(request);
        faction.ResolveDemand(request, true, civilization, 10);

        Assert.That(faction.TryCompleteBuildingRequest(civilization, city, wrong, 11), Is.False);
        Assert.That(faction.ActiveDemands, Does.Contain(request));
    }

    [Test]
    public void DecliningCooperativeRequestDoesNotTriggerRebellion()
    {
        var request = CooperativeRequest(null, 10);
        faction.ActiveDemands.Add(request);
        bool rebelled = faction.ResolveDemand(request, false, civilization, 10);
        Assert.That(rebelled, Is.False);
        Assert.That(faction.IsInRebellion, Is.False);
    }

    [Test]
    public void MissedAcceptedCommitmentReducesSatisfaction()
    {
        var request = CooperativeRequest(null, 10);
        faction.ActiveDemands.Add(request);
        faction.ResolveDemand(request, true, civilization, 10);
        float acceptedSatisfaction = faction.Satisfaction;

        Assert.That(faction.ProcessProposalDeadline(21), Is.True);
        Assert.That(faction.Satisfaction, Is.LessThan(acceptedSatisfaction));
        Assert.That(faction.ActiveDemands, Is.Empty);
    }

    [Test]
    public void AcceptedProposalProgressSurvivesJsonRoundTrip()
    {
        var request = CooperativeRequest(null, 10);
        request.state = FactionProposalState.Accepted;
        request.acceptedTurn = 11;
        request.progress = 1;
        request.requiredProgress = 2;

        var restored = JsonUtility.FromJson<FactionDemand>(JsonUtility.ToJson(request));

        Assert.That(restored.proposalId, Is.EqualTo(request.proposalId));
        Assert.That(restored.category, Is.EqualTo(FactionProposalCategory.CooperativeRequest));
        Assert.That(restored.state, Is.EqualTo(FactionProposalState.Accepted));
        Assert.That(restored.deadlineTurn, Is.EqualTo(20));
        Assert.That(restored.progress, Is.EqualTo(1));
        Assert.That(restored.requiredProgress, Is.EqualTo(2));
    }

    [Test]
    public void OnlyActiveLegacyAffinityProvidesOngoingFactionSupport()
    {
        var legacy = ScriptableObject.CreateInstance<LegacyData>();
        createdObjects.Add(legacy);
        legacy.factionAffinities = new[]
        {
            new LegacyData.FactionAffinity { alignment = FactionAlignment.Mercantile, satisfactionModifier = 20f },
        };
        faction.RestorePoliticalState(50f, 0);
        civilization.activeLegacies.Add(legacy);
        for (int i = 0; i < 10; i++) faction.UpdateSatisfaction(civilization);
        float promotedSatisfaction = faction.Satisfaction;

        civilization.activeLegacies.Remove(legacy);
        for (int i = 0; i < 10; i++) faction.UpdateSatisfaction(civilization);

        Assert.That(promotedSatisfaction, Is.GreaterThan(55f));
        Assert.That(faction.Satisfaction, Is.LessThan(promotedSatisfaction));
    }

    private Governor CreateGovernorWithCity(int id, string name, Governor.Specialization specialization, FactionBloc targetFaction = null)
    {
        var governor = new Governor(id, name, specialization);
        CreateCityFor(governor);
        targetFaction?.AddMember(governor);
        return governor;
    }

    private City CreateCityFor(Governor governor)
    {
        var cityObject = new GameObject("Faction Test City");
        createdObjects.Add(cityObject);
        var city = cityObject.AddComponent<City>();
        city.owner = civilization;
        city.governor = governor;
        city.level = 1;
        governor.Cities.Add(city);
        civilization.cities.Add(city);
        return city;
    }

    private static FactionDemand CooperativeRequest(BuildingData building, int turn)
        => new FactionDemand
        {
            proposalId = "test-request",
            category = FactionProposalCategory.CooperativeRequest,
            state = FactionProposalState.Offered,
            objective = FactionProposalObjective.ConstructBuilding,
            type = FactionDemandType.ConstructFaithBuilding,
            issuedTurn = turn,
            expiryTurns = 10,
            deadlineTurn = turn + 10,
            targetBuilding = building,
            satisfactionReward = 15f,
            satisfactionFailurePenalty = 10f,
        };
}
#endif