#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

public class GovernmentElectionTests
{
    private GameObject root;
    private Civilization civ;
    private GovernmentData government;

    [SetUp] public void SetUp()
    {
        root = new GameObject("Election tests");
        civ = root.AddComponent<Civilization>();
        government = ScriptableObject.CreateInstance<GovernmentData>();
        government.governmentName = "Test elected government";
        government.electionRules.enabled = true;
        government.electionRules.termLengthTurns = 12;
        government.electionRules.candidateCount = 3;
        civ.currentGovernment = government;
        civ.electionState.nextElectionTurn = 5;
    }
    [TearDown] public void TearDown() { Object.DestroyImmediate(government); Object.DestroyImmediate(root); }

    [Test] public void ElectionTriggersAndRollsTermAtConfiguredTurn()
    {
        ElectionManager.ProcessTurn(civ, 3);
        Assert.NotNull(civ.electionState.activeElection);
        Assert.False(civ.electionState.activeElection.resolved);
        ElectionManager.ProcessTurn(civ, 5);
        Assert.True(civ.electionState.activeElection.resolved);
        Assert.AreEqual(17, civ.electionState.nextElectionTurn);
        Assert.NotNull(civ.electionState.currentOffice);
    }

    [Test] public void ElectorateWeightsProduceDifferentSupport()
    {
        var election = ElectionManager.CreateElection(civ, government.electionRules, 3);
        government.electionRules.publicOpinionWeight = .8f; government.electionRules.governorEliteWeight = .1f;
        ElectionManager.Resolve(civ, government.electionRules, election, 5);
        float broad = election.candidates[0].finalSupport;
        var eliteElection = ElectionManager.CreateElection(civ, government.electionRules, 3);
        government.electionRules.publicOpinionWeight = .1f; government.electionRules.governorEliteWeight = .8f;
        ElectionManager.Resolve(civ, government.electionRules, eliteElection, 5);
        Assert.AreNotEqual(broad, eliteElection.candidates[0].finalSupport);
    }

    [Test] public void OutcomeIsDeterministicAcrossSerializedState()
    {
        var original = ElectionManager.CreateElection(civ, government.electionRules, 3);
        var loaded = JsonUtility.FromJson<ElectionRecord>(JsonUtility.ToJson(original));
        ElectionManager.Resolve(civ, government.electionRules, original, 5);
        civ.electionState.currentOffice = null;
        ElectionManager.Resolve(civ, government.electionRules, loaded, 5);
        Assert.AreEqual(original.winnerCandidateId, loaded.winnerCandidateId);
        Assert.AreEqual(original.winningMargin, loaded.winningMargin, .0001f);
    }

    [Test] public void EndorsementInfluencesButDoesNotDirectlyResolve()
    {
        civ.gold = 100;
        civ.electionState.activeElection = ElectionManager.CreateElection(civ, government.electionRules, 3);
        string id = civ.electionState.activeElection.candidates[0].candidateId;
        Assert.True(ElectionManager.EndorseCandidate(civ, id, 50));
        Assert.False(civ.electionState.activeElection.resolved);
        Assert.AreEqual(50, civ.gold);
    }

    [Test] public void GovernmentSwitchCancelsElectionAndBoundsPoliticalMeters()
    {
        civ.electionState.activeElection = ElectionManager.CreateElection(civ, government.electionRules, 3);
        civ.electionState.publicApproval = 150f; civ.electionState.governmentLegitimacy = -5f;
        ElectionManager.ProcessTurn(civ, 3);
        Assert.That(civ.electionState.publicApproval, Is.InRange(0f, 100f));
        Assert.That(civ.electionState.governmentLegitimacy, Is.InRange(0f, 100f));
        var unelected = ScriptableObject.CreateInstance<GovernmentData>();
        ElectionManager.OnGovernmentChanged(civ, government, unelected, 4);
        Assert.IsNull(civ.electionState.activeElection);
        Assert.AreEqual(-1, civ.electionState.nextElectionTurn);
        Object.DestroyImmediate(unelected);
    }

    [Test] public void ValidatorDetectsMalformedCouncilAndElection()
    {
        government.usesRoyalCouncil = false; government.councilSeatCount = 2;
        Assert.True(GovernmentDataValidator.HasInvalidCouncilConfiguration(government));
        government.electionRules.termLengthTurns = 0;
        Assert.True(GovernmentDataValidator.HasMalformedElectionRules(government));
    }
}
#endif
