using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Inspectable, deterministic risk composition. Diversity is deliberately absent:
/// only concrete instability and grievance signals contribute to political violence.</summary>
public static class CrisisRiskEvaluator
{
    [Serializable] public class Inputs
    {
        [Range(0,100)] public float averageOrder=50, averageHappiness=50, loyalty=50;
        public float unrest, warWeariness;
        [Range(0,100)] public float publicApproval=50, legitimacy=50, governorOpinion=50;
        public float grievanceSeverity, factionPower, activeDemandSeverity;
        public float libertyDesire, tributeExhaustion;
        [Range(-100,100)] public float subjectOpinion;
        public int robotUnits;
        public float cyberDefense, tradeDependence;
        public int treasury, foodReserve;
    }
    public readonly struct Result
    {
        public readonly float score; public readonly string explanation;
        public Result(float score, string explanation) { this.score=score; this.explanation=explanation; }
    }
    public static Result EvaluatePoliticalInstability(Inputs i)
    {
        if (i == null) return new Result(0,"No inputs");
        var reasons=new List<string>(); float score=0;
        void Add(string label,float value) { if (value<=0) return; score+=value; reasons.Add(label+" +"+value.ToString("0.##")); }
        Add("low order",Mathf.Max(0,50-i.averageOrder)*.5f);
        Add("low happiness",Mathf.Max(0,50-i.averageHappiness)*.35f);
        Add("low loyalty",Mathf.Max(0,50-i.loyalty)*.35f);
        Add("unrest",Mathf.Max(0,i.unrest)*20f); Add("war weariness",Mathf.Max(0,i.warWeariness)*10f);
        Add("low legitimacy",Mathf.Max(0,50-i.legitimacy)*.3f);
        Add("governor grievances",Mathf.Max(0,i.grievanceSeverity));
        Add("active faction demands",Mathf.Max(0,i.activeDemandSeverity*i.factionPower));
        return new Result(Mathf.Max(0,score), reasons.Count==0?"stable; no concrete grievance":string.Join(", ",reasons));
    }
    public static Result EvaluateTerrorism(Inputs i)
    {
        var political=EvaluatePoliticalInstability(i);
        // A grievance is a gate: cultural/religious difference without gameplay consequences is zero risk.
        if (i == null || i.grievanceSeverity<=0) return new Result(0,"no severe gameplay grievance");
        return new Result(political.score+i.grievanceSeverity*2f, political.explanation+", severe grievance");
    }
    public static Result EvaluateRobotUprising(Inputs i)
    {
        if (i == null || i.robotUnits < 2) return new Result(0,"insufficient robot/cyborg presence");
        float score=Mathf.Max(0,i.robotUnits*10f-i.cyberDefense*25f);
        return new Result(score,$"{i.robotUnits} robot/cyborg units, cyber defense {i.cyberDefense:P0}");
    }
}
