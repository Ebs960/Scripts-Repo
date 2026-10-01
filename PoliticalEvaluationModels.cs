using System.Collections.Generic;

/// <summary>Structured result of PolicyManager.EvaluateGovernment; mirrors the rules ChangeGovernment enforces.</summary>
public class GovernmentAdoptionEvaluation
{
    public GovernmentData government;
    public bool unlocked;
    public bool isCurrentGovernment;
    public bool affordable;
    public bool meetsTechRequirements;
    public bool meetsCultureRequirements;
    public bool meetsCityRequirement;
    public bool meetsReligionRequirement;
    public bool meetsVassalRequirement;
    public bool canAdopt;

    public int policyPointCost;
    public int currentPolicyPoints;

    public List<string> failureReasons = new List<string>();

    /// <summary>Every rule except policy-point affordability.</summary>
    public bool MeetsStructuralPrerequisites => unlocked && !isCurrentGovernment && meetsTechRequirements
        && meetsCultureRequirements && meetsCityRequirement && meetsReligionRequirement && meetsVassalRequirement;
}

public enum PolicyListState { Active, Available, Locked }

/// <summary>Structured result of PolicyManager.EvaluatePolicy; mirrors the rules AdoptPolicy enforces.</summary>
public class PolicyAdoptionEvaluation
{
    public PolicyData policy;

    public bool alreadyActive;
    public bool affordable;
    public bool meetsTechRequirements;
    public bool meetsCultureRequirements;
    public bool meetsGovernmentRequirement;
    public bool meetsCityRequirement;
    public bool meetsReligionRequirement;
    public bool meetsRequiredPolicies;
    public bool hasConflict;
    public bool canAdopt;

    public int policyPointCost;
    public int currentPolicyPoints;

    public List<PolicyData> conflictingActivePolicies = new List<PolicyData>();
    public List<string> failureReasons = new List<string>();

    public bool MeetsStructuralRequirements => meetsTechRequirements && meetsCultureRequirements
        && meetsGovernmentRequirement && meetsCityRequirement && meetsReligionRequirement && meetsRequiredPolicies;

    public PolicyListState State => alreadyActive ? PolicyListState.Active
        : canAdopt ? PolicyListState.Available : PolicyListState.Locked;
}
