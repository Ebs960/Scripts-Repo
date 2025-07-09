using System.Collections.Generic;
using UnityEngine;

public class PolicyManager : MonoBehaviour
{
    public static PolicyManager Instance { get; private set; }

    [Tooltip("All policies in the game")]
    public List<PolicyData> allPolicies = new List<PolicyData>();
    [Tooltip("All governments in the game")]
    public List<GovernmentData> allGovernments = new List<GovernmentData>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// Which policies can the civ adopt right now?
    /// </summary>
    public List<PolicyData> GetAvailablePolicies(Civilization civ)
    {
        var avail = new List<PolicyData>();
        foreach (var p in allPolicies)
        {
            if (civ.activePolicies.Contains(p) || civ.unlockedPolicies.Contains(p)) 
                continue;
            if (civ.policyPoints < p.policyPointCost) 
                continue;
            bool ok = true;
            foreach (var req in p.requiredTechs)
                if (!civ.researchedTechs.Contains(req)) { ok = false; break; }
            foreach (var req in p.requiredCultures)
                if (!civ.researchedCultures.Contains(req)) { ok = false; break; }
            foreach (var req in p.requiredGovernments)
                if (civ.currentGovernment != req) { ok = false; break; }
            if (civ.cities.Count < p.requiredCityCount) ok = false;
            if (ok) avail.Add(p);
        }
        return avail;
    }

    /// <summary>
    /// Pay policy points and adopt a policy.
    /// </summary>
    public bool AdoptPolicy(Civilization civ, PolicyData p)
    {
        if (!GetAvailablePolicies(civ).Contains(p)) return false;
        civ.policyPoints -= p.policyPointCost;
        civ.AdoptPolicy(p);
        return true;
    }

    /// <summary>
    /// Which governments can the civ switch to right now?
    /// </summary>
    public List<GovernmentData> GetAvailableGovernments(Civilization civ)
    {
        var avail = new List<GovernmentData>();
        foreach (var g in allGovernments)
        {
            if (civ.unlockedGovernments.Contains(g) || civ.currentGovernment == g) 
                continue;
            if (civ.policyPoints < g.policyPointCost) 
                continue;
            bool ok = true;
            foreach (var req in g.requiredTechs)
                if (!civ.researchedTechs.Contains(req)) { ok = false; break; }
            foreach (var req in g.requiredCultures)
                if (!civ.researchedCultures.Contains(req)) { ok = false; break; }
            if (civ.cities.Count < g.requiredCityCount) ok = false;
            if (ok) avail.Add(g);
        }
        return avail;
    }

    /// <summary>
    /// Switch government, unlocking new policies.
    /// </summary>
    public bool ChangeGovernment(Civilization civ, GovernmentData g)
    {
        if (!GetAvailableGovernments(civ).Contains(g)) return false;
        civ.policyPoints -= g.policyPointCost;
        civ.ChangeGovernment(g);
        return true;
    }
} 