using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public enum PoliticalEventType
{
    FactionDemand,
    GovernorPetition,
    CouncilElection,
    VassalPetition
}

[Serializable]
public enum PoliticalEventStatus
{
    Pending,
    Resolved,
    Expired
}

[Serializable]
public enum PoliticalActorType
{
    Governor,
    Faction,
    Vassal,
    Council
}

[Serializable]
public class PoliticalActorRef
{
    public PoliticalActorType actorType;
    public string civName;
    public int governorId = -1;
    public string factionName;
    public string subjectCivName;
    public string displayName;
}

[Serializable]
public class PoliticalEventOption
{
    public string optionId;
    public string label;
    public string summary;
    public int targetGovernorId = -1;
}

[Serializable]
public class PoliticalEventRecord
{
    public int id;
    public PoliticalEventType eventType;
    public PoliticalEventStatus status = PoliticalEventStatus.Pending;
    public string sourceKey;
    public string targetCivName;
    public string title;
    [TextArea] public string body;
    public int createdTurn;
    public int expiryTurn;
    public bool presentedToPlayer;

    public PoliticalActorRef primaryActor = new PoliticalActorRef();
    public string factionName;
    public string subjectCivName;
    public string factionDemandType;
    public int issuedTurn;
    public int targetGovernorId = -1;

    public List<PoliticalEventOption> options = new List<PoliticalEventOption>();
}