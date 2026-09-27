using System;

public enum SubjectDemandType
{
    FullIndependence,
}

/// <summary>A save-safe ultimatum issued while the vassal contract is still in force.</summary>
[Serializable]
public class IndependenceDemand
{
    [NonSerialized] public Civilization overlord;
    [NonSerialized] public Civilization subject;
    public string overlordCivName;
    public string subjectCivName;
    public int turnIssued;
    public SubjectDemandType demandType = SubjectDemandType.FullIndependence;
    public bool resolved;
}
