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

public enum PolicyListState { Active, Available, Replace, Locked }

/// <summary>Structured result of PolicyManager.EvaluatePolicy; mirrors the rules AdoptPolicy enforces.</summary>
public class PolicyAdoptionEvaluation
{
    public PolicyData policy;
    public PolicyArea policyArea;

    /// <summary>Policies currently active in this area (excluding the candidate).</summary>
    public List<PolicyData> activeAreaPolicies = new List<PolicyData>();
    /// <summary>Free slots in the area after supersessions, before any selected replacement.</summary>
    public int remainingCapacity;
    /// <summary>Active policies the candidate supersedes; removed automatically on adoption.</summary>
    public List<PolicyData> supersededActivePolicies = new List<PolicyData>();
    /// <summary>The explicitly chosen policy to replace, when one was supplied and is valid.</summary>
    public PolicyData selectedReplacement;
    /// <summary>True when the area is full and no replacement was supplied.</summary>
    public bool requiresReplacementSelection;
    /// <summary>Active area policies that can legally be replaced by this candidate.</summary>
    public List<PolicyData> replacementChoices = new List<PolicyData>();

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

    /// <summary>Adoptable once the player picks one of replacementChoices.</summary>
    public bool CanAdoptWithReplacement => requiresReplacementSelection && replacementChoices.Count > 0;

    public PolicyListState State => alreadyActive ? PolicyListState.Active
        : canAdopt ? PolicyListState.Available
        : CanAdoptWithReplacement ? PolicyListState.Replace : PolicyListState.Locked;
}
