using System;
using System.Collections.Generic;

public enum ElectorateModel { Elite, Broad, Regional, Parliamentary, Commercial }
public enum ElectionIssueType { Economy, FoodSecurity, PublicOrder, War, Peace, Science, Religion, Trade, Autonomy, Vassals }

[Serializable]
public class ElectionRules
{
    public bool enabled;
    public int termLengthTurns = 20;
    public ElectorateModel electorateModel = ElectorateModel.Broad;
    public bool incumbentEligible = true;
    public int candidateCount = 3;
    public float volatility = 0.08f;
    public string executiveTitle = "President";
    public bool councilEndorsementsMatter = true;
    [Range01] public float publicOpinionWeight = 0.5f;
    [Range01] public float governorEliteWeight = 0.5f;
    public int campaignLeadTurns = 2;
}

// Marker attribute used only to document normalized serialized values without depending on Unity's RangeAttribute.
[AttributeUsage(AttributeTargets.Field)] public sealed class Range01Attribute : Attribute { }

[Serializable] public class ElectionIssueRecord { public ElectionIssueType issue; public float salience; public string summary; }

[Serializable]
public class ElectionCandidateRecord
{
    public string candidateId;
    public string displayName;
    public int governorId = -1;
    public string factionName;
    public bool incumbent;
    public float competence;
    public float eliteAppeal;
    public float publicAppeal;
    public List<ElectionIssueType> priorities = new List<ElectionIssueType>();
    public float finalSupport;
}

[Serializable]
public class ElectedOfficeRecord
{
    public string candidateId;
    public string officeholderName;
    public string factionName;
    public string title;
    public int electionWonTurn;
    public int termEndTurn;
    public List<ElectionIssueType> mandate = new List<ElectionIssueType>();
}

[Serializable]
public class ElectionRecord
{
    public int electionId;
    public int openedTurn;
    public int resolutionTurn;
    public int deterministicSeed;
    public bool resolved;
    public string endorsedCandidateId;
    public int campaignGoldSpent;
    public List<ElectionIssueRecord> issues = new List<ElectionIssueRecord>();
    public List<ElectionCandidateRecord> candidates = new List<ElectionCandidateRecord>();
    public string winnerCandidateId;
    public float winningMargin;
}

[Serializable]
public class ElectionState
{
    public float publicApproval = 50f;
    public float governmentLegitimacy = 50f;
    public int nextElectionTurn = -1;
    public int electionsHeld;
    public ElectedOfficeRecord currentOffice;
    public ElectionRecord activeElection;
}
