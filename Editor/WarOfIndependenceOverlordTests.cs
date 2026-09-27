#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class WarOfIndependenceOverlordTests
{
    private static T Asset<T>(string filter) where T : Object
    {
        var path=AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets(filter).First());
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    [Test]
    public void CrisisOffersOnlyTheTwoOverlordMissionsAndRetainedLegacies()
    {
        var crisis=Asset<CrisisData>("War of Independence t:CrisisData");
        CollectionAssert.AreEquivalent(new[]{"Imperial Reform","Rebellion Crusher"},crisis.crisisMissions.Select(m=>m.missionName));
        Assert.That(crisis.crisisMissions.All(m=>m.participantRole==MissionData.ParticipantRole.Overlord),Is.True);
        Assert.That(crisis.crisisMissions.Single(m=>m.missionName=="Imperial Reform").rewardTiers.Single().rewardLegacy.legacyName,Is.EqualTo("Flexible Empire"));
        Assert.That(crisis.crisisMissions.Single(m=>m.missionName=="Rebellion Crusher").rewardTiers.Single().rewardLegacy.legacyName,Is.EqualTo("Imperial Authority"));
        Assert.That(AssetDatabase.FindAssets("Liberty or Death t:MissionData"),Is.Empty);
        Assert.That(AssetDatabase.FindAssets("Negotiated Sovereignty t:MissionData"),Is.Empty);
        Assert.That(AssetDatabase.FindAssets("Founding Myth t:LegacyData"),Is.Empty);
        Assert.That(AssetDatabase.FindAssets("Autonomous Tradition t:LegacyData"),Is.Empty);
    }

    [Test]
    public void PendingDemandIsUniqueAndPeacefulAcceptanceRemovesContract()
    {
        var managerObject=new GameObject("SubjectManager test");
        var overlordObject=new GameObject("Overlord");
        var subjectObject=new GameObject("Subject");
        var manager=managerObject.AddComponent<SubjectManager>();
        var overlord=overlordObject.AddComponent<Civilization>();
        var subject=subjectObject.AddComponent<Civilization>();
        try
        {
            var contract=manager.CreateContract(overlord,subject,currentTurn:4);
            var first=manager.IssueIndependenceDemand(contract,12,false);
            var duplicate=manager.IssueIndependenceDemand(contract,13,false);
            Assert.That(duplicate,Is.SameAs(first));
            Assert.That(manager.IndependenceDemands.Count(d=>!d.resolved),Is.EqualTo(1));
            Assert.That(manager.AcceptIndependenceDemand(first,12),Is.True);
            Assert.That(manager.GetContract(overlord,subject),Is.Null);
            Assert.That(overlord.relations[subject],Is.EqualTo(DiplomaticState.Peace));
            Assert.That(subject.relations[overlord],Is.EqualTo(DiplomaticState.Peace));
            Assert.That(manager.GetDiplomaticOpinionModifier(subject,overlord,12),Is.EqualTo(15f));
            Assert.That(manager.DiplomaticOpinionModifiers.Single().reason,Is.EqualTo("Peaceful Independence"));
            StringAssert.Contains("\"resolved\":true",manager.CaptureStateJson());
        }
        finally
        {
            Object.DestroyImmediate(subjectObject);
            Object.DestroyImmediate(overlordObject);
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void AiHeuristicAcceptsWhenOverextendedAndRejectsWhenDominant()
    {
        var managerObject=new GameObject("SubjectManager AI test");
        var overlordObject=new GameObject("Overlord");
        var subjectObject=new GameObject("Subject");
        var manager=managerObject.AddComponent<SubjectManager>();
        var contract=new VassalContract { overlord=overlordObject.AddComponent<Civilization>(),subject=subjectObject.AddComponent<Civilization>(),libertyDesire=100,militaryConfidence=100,subjectOpinion=-100 };
        try
        {
            Assert.That(manager.ScoreIndependenceDemandRejection(contract),Is.LessThan(0));
            contract.goldTributePct=.5f; contract.scienceTributePct=.5f; contract.foodTributePct=.5f;
            contract.libertyDesire=0; contract.militaryConfidence=0; contract.subjectOpinion=100;
            Assert.That(manager.ScoreIndependenceDemandRejection(contract),Is.GreaterThan(0));
        }
        finally { Object.DestroyImmediate(subjectObject); Object.DestroyImmediate(overlordObject); Object.DestroyImmediate(managerObject); }
    }
}
#endif
