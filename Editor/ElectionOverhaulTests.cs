#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ElectionOverhaulTests
{
    private GameObject gameObject;
    private Civilization civilization;
    private GovernmentData government;

    [SetUp]
    public void SetUp()
    {
        gameObject = new GameObject("Election Test Civilization");
        civilization = gameObject.AddComponent<Civilization>();
        civilization.electionState = new ElectionState();
        government = ScriptableObject.CreateInstance<GovernmentData>();
        government.governmentName = "Test Republic";
        government.leaderTitleSuffix = "President";
        government.electionRules = new ElectionRules { enabled = true };
        civilization.currentGovernment = government;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(government);
        Object.DestroyImmediate(gameObject);
    }

    [TestCase(49.99f, false)]
    [TestCase(50f, true)]
    [TestCase(75f, true)]
    public void PlayerElectionUsesInclusiveFiftyPercentThreshold(float opinion, bool victory)
    {
        civilization.isPlayerControlled = true;
        civilization.electionState.publicApproval = opinion;
        var election = ElectionManager.CreateElection(civilization, government.electionRules, 25);

        ElectionManager.Resolve(civilization, government.electionRules, election, 25);

        Assert.That(election.playerVictory, Is.EqualTo(victory));
        Assert.That(civilization.electionState.pendingPlayerDefeat, Is.EqualTo(!victory));
    }

    [Test]
    public void PlayerElectionGeneratesNoCandidates()
    {
        civilization.isPlayerControlled = true;
        var election = ElectionManager.CreateElection(civilization, government.electionRules, 25);
        Assert.That(election.candidates, Is.Empty);
    }

    [Test]
    public void PlayerElectionsRepeatForThreeTwentyFiveTurnCycles()
    {
        civilization.isPlayerControlled = true;
        civilization.electionState.publicApproval = 75f;
        foreach (int turn in new[] { 25, 50, 75 })
        {
            var election = ElectionManager.CreateElection(civilization, government.electionRules, turn);
            ElectionManager.Resolve(civilization, government.electionRules, election, turn);
            Assert.That(civilization.electionState.nextElectionTurn, Is.EqualTo(turn + 25));
            Assert.That(civilization.electionState.activeElection, Is.Null);
        }
        Assert.That(civilization.electionState.electionsHeld, Is.EqualTo(3));
        Assert.That(civilization.electionState.history, Has.Count.EqualTo(3));
    }

    [Test]
    public void AiElectionChoosesGovernorWithoutReplacingFoundingLeader()
    {
        var foundingLeader = ScriptableObject.CreateInstance<LeaderData>();
        foundingLeader.leaderName = "Founder";
        typeof(Civilization).GetField("<leader>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(civilization, foundingLeader);
        var governor = new Governor(7, "Demetrios", Governor.Specialization.Economic);
        governor.AssignPortrait("portrait-7");
        civilization.governors.Add(governor);
        var election = ElectionManager.CreateElection(civilization, government.electionRules, 25);

        ElectionManager.Resolve(civilization, government.electionRules, election, 25);

        Assert.That(civilization.electionState.currentOffice.governorId, Is.EqualTo(7));
        Assert.That(civilization.electionState.currentOffice.officeholderName, Is.EqualTo("Demetrios"));
        Assert.That(civilization.electionState.currentOffice.portraitId, Is.EqualTo("portrait-7"));
        Assert.That(civilization.leader, Is.SameAs(foundingLeader));
        Object.DestroyImmediate(foundingLeader);
    }

    [Test]
    public void ElectedRulerIdentitySurvivesSerialization()
    {
        civilization.electionState.currentOffice = new ElectedOfficeRecord
        {
            governorId = 9,
            officeholderName = "Saved Ruler",
            portraitId = "saved-portrait",
        };
        string json = JsonUtility.ToJson(civilization.electionState);
        var restored = JsonUtility.FromJson<ElectionState>(json);
        Assert.That(restored.currentOffice.governorId, Is.EqualTo(9));
        Assert.That(restored.currentOffice.portraitId, Is.EqualTo("saved-portrait"));
    }

    [Test]
    public void MissingElectedGovernorBecomesCaretakerWithoutChangingLeader()
    {
        var foundingLeader = ScriptableObject.CreateInstance<LeaderData>();
        foundingLeader.leaderName = "Founder";
        typeof(Civilization).GetField("<leader>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(civilization, foundingLeader);
        civilization.electionState.currentOffice = new ElectedOfficeRecord
        {
            governorId = 999,
            officeholderName = "Missing Governor",
            portraitId = "missing",
        };

        Assert.DoesNotThrow(() => ElectionManager.ProcessTurn(civilization, 10));
        Assert.That(civilization.electionState.currentOffice.governorId, Is.EqualTo(-1));
        Assert.That(civilization.leader, Is.SameAs(foundingLeader));
        Object.DestroyImmediate(foundingLeader);
    }

    [Test]
    public void ConcedeClearsPendingDefeat()
    {
        civilization.electionState.pendingPlayerDefeat = true;
        ElectionManager.ConcedePlayerDefeat(civilization);
        Assert.That(civilization.electionState.pendingPlayerDefeat, Is.False);
    }

    [Test]
    public void SeizePowerForcesConfiguredGovernmentAndCannotRepeat()
    {
        var policyManagerObject = new GameObject("Policy Manager");
        var policyManager = policyManagerObject.AddComponent<PolicyManager>();
        var authoritarian = ScriptableObject.CreateInstance<GovernmentData>();
        authoritarian.governmentName = "Emergency Directorate";
        authoritarian.archetypes = GovernmentArchetypeFlags.CentralizedExecutive;
        authoritarian.emergencyAuthoritarianGovernment = true;
        authoritarian.electionRules = new ElectionRules { enabled = false };
        policyManager.allGovernments = new System.Collections.Generic.List<GovernmentData> { authoritarian };
        civilization.electionState.pendingPlayerDefeat = true;

        Assert.That(ElectionManager.SeizePower(civilization), Is.True);
        Assert.That(civilization.currentGovernment, Is.SameAs(authoritarian));
        Assert.That(civilization.electionState.takeoverRebellionTriggered, Is.True);
        Assert.That(ElectionManager.SeizePower(civilization), Is.False);

        Object.DestroyImmediate(authoritarian);
        Object.DestroyImmediate(policyManagerObject);
    }

    [Test]
    public void NonElectoralGovernmentDoesNotCreateElection()
    {
        government.electionRules.enabled = false;
        ElectionManager.ProcessTurn(civilization, 100);
        Assert.That(civilization.electionState.activeElection, Is.Null);
    }

    [Test]
    public void SwitchingGovernmentsPreservesElectionClock()
    {
        var authoritarian = ScriptableObject.CreateInstance<GovernmentData>();
        authoritarian.electionRules = new ElectionRules { enabled = false };
        civilization.electionState.nextElectionTurn = 25;

        ElectionManager.OnGovernmentChanged(civilization, government, authoritarian, 24);
        ElectionManager.OnGovernmentChanged(civilization, authoritarian, government, 24);

        Assert.That(civilization.electionState.nextElectionTurn, Is.EqualTo(25));
        Object.DestroyImmediate(authoritarian);
    }
}
#endif