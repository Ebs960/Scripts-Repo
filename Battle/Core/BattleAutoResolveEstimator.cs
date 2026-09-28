using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BattleAutoResolveForecast
{
    public float AttackerWinProbability;
    public float DefenderWinProbability;
    public int AttackerSimulatedWins;
    public int DefenderSimulatedWins;
    public int SimulationCount;
    public float ExpectedAttackerCasualtyFraction;
    public float ExpectedDefenderCasualtyFraction;
    public int EstimatedAttackerSoldierLosses;
    public int EstimatedDefenderSoldierLosses;
    public string AdvantageLabel;
    public readonly List<string> AttackerFactors = new();
    public readonly List<string> DefenderFactors = new();
}

/// <summary>Non-destructive Monte Carlo forecast over the production tactical simulation.</summary>
public sealed class BattleAutoResolveEstimator
{
    private readonly Dictionary<int, BattleAutoResolveForecast> cache = new();

    public IEnumerator Estimate(BattleManager manager, EngagementPreview preview, Action<BattleAutoResolveForecast> completed)
    {
        int signature = BuildSignature(preview);
        if (cache.TryGetValue(signature, out var cached)) { completed?.Invoke(cached); yield break; }
        int requested = manager != null ? manager.ForecastSampleCount : 50;
        int attackerWins=0, defenderWins=0, samples=0;
        long attackerLosses=0, defenderLosses=0;
        var snapshots = BuildSnapshotIndex(preview);
        for (int i=0;i<requested;i++)
        {
            int seed = ForecastSeed(preview.RandomSeed, i);
            if (manager.TrySimulateForecastSample(preview, seed, out var result))
            {
                samples++; if(result.WinningSide==BattleSide.Attacker)attackerWins++;else defenderWins++;
                AccumulateLosses(result, snapshots, ref attackerLosses, ref defenderLosses);
            }
            // Unity simulation stays on its owning thread while work is spread across frames.
            yield return null;
        }
        if(samples==0)yield break;
        var forecast=new BattleAutoResolveForecast {
            AttackerSimulatedWins=attackerWins, DefenderSimulatedWins=defenderWins, SimulationCount=samples,
            AttackerWinProbability=attackerWins/(float)samples, DefenderWinProbability=defenderWins/(float)samples,
            EstimatedAttackerSoldierLosses=Mathf.RoundToInt(attackerLosses/(float)samples),
            EstimatedDefenderSoldierLosses=Mathf.RoundToInt(defenderLosses/(float)samples)
        };
        int attackerTotal=TotalSoldiers(preview.AttackerUnits, preview.Reinforcements, BattleSide.Attacker);
        int defenderTotal=TotalSoldiers(preview.DefenderUnits, preview.Reinforcements, BattleSide.Defender);
        forecast.ExpectedAttackerCasualtyFraction=attackerTotal>0?forecast.EstimatedAttackerSoldierLosses/(float)attackerTotal:0;
        forecast.ExpectedDefenderCasualtyFraction=defenderTotal>0?forecast.EstimatedDefenderSoldierLosses/(float)defenderTotal:0;
        forecast.AdvantageLabel=AdvantageLabel(forecast.AttackerWinProbability);
        AddFactors(preview, forecast, attackerTotal, defenderTotal);
        cache[signature]=forecast; completed?.Invoke(forecast);
    }

    public static int BuildSignature(EngagementPreview preview)
    {
        unchecked { int h=17; h=h*31+(preview?.RandomSeed??0); if(preview==null)return h;
            HashUnits(preview.AttackerUnits,ref h); HashUnits(preview.DefenderUnits,ref h);
            foreach(var g in preview.Reinforcements){h=h*31+g.ReinforcementGroupId;h=h*31+g.AvailableFromRound;h=h*31+(g.IsEligible?1:0);HashUnits(g.Units,ref h);}
            foreach(var f in preview.Fortifications){h=h*31+f.StructureId;h=h*31+f.CurrentHitPoints;h=h*31+(f.IsBreached?1:0);}
            var service=MilitaryCommanderAssignmentService.Instance; if(service!=null)foreach(var a in service.Assignments)
                if(a.IsActive){h=h*31+StableStringHash(a.FormationId);h=h*31+(int)a.Role;h=h*31+a.CharacterId;h=h*31+(int)a.Status;}
            return h; }
    }

    private static void HashUnits(IReadOnlyList<BattleUnitSnapshot> units,ref int h){for(int i=0;i<units.Count;i++){var u=units[i];h=h*31+u.CampaignRuntimeId;h=h*31+u.StartingHealth;h=h*31+u.SoldierCount;h=h*31+u.MeleeAttack;h=h*31+u.RangedAttack;h=h*31+u.Defense;}}
    private static int StableStringHash(string value){unchecked{int h=23;if(value!=null)for(int i=0;i<value.Length;i++)h=h*31+value[i];return h;}}
    private static int ForecastSeed(int seed,int sample){unchecked{uint x=(uint)seed+0x9E3779B9u*(uint)(sample+1);x^=x>>16;x*=0x7FEB352Du;x^=x>>15;return(int)x;}}
    private static Dictionary<int,BattleUnitSnapshot> BuildSnapshotIndex(EngagementPreview p){var d=new Dictionary<int,BattleUnitSnapshot>();Add(p.AttackerUnits,d);Add(p.DefenderUnits,d);foreach(var g in p.Reinforcements)Add(g.Units,d);return d;}
    private static void Add(IReadOnlyList<BattleUnitSnapshot> units,Dictionary<int,BattleUnitSnapshot>d){foreach(var u in units)d[u.CampaignRuntimeId]=u;}
    private static void AccumulateLosses(BattleResult result,Dictionary<int,BattleUnitSnapshot> units,ref long a,ref long d){foreach(var o in result.UnitOutcomes)if(units.TryGetValue(o.CampaignRuntimeId,out var u)){float lost=1f-Mathf.Clamp01(o.FinalHealth/(float)Mathf.Max(1,u.MaximumHealth));int soldiers=Mathf.RoundToInt(u.SoldierCount*lost);if(o.Side==BattleSide.Attacker)a+=soldiers;else d+=soldiers;}}
    private static int TotalSoldiers(IReadOnlyList<BattleUnitSnapshot> immediate,IReadOnlyList<BattleReinforcementGroup> groups,BattleSide side){int n=0;foreach(var u in immediate)n+=u.SoldierCount;foreach(var g in groups)if(g.Side==side)foreach(var u in g.Units)n+=u.SoldierCount;return n;}
    public static string AdvantageLabel(float p)=>p>=.9f?"Overwhelming Advantage":p>=.75f?"Strong Advantage":p>=.6f?"Advantage":p>=.45f?"Even Battle":p>=.3f?"Disadvantage":p>=.15f?"Strong Disadvantage":"Desperate Battle";
    private static void AddFactors(EngagementPreview p,BattleAutoResolveForecast f,int a,int d){if(a>d)f.AttackerFactors.Add("Superior numbers");if(d>a)f.DefenderFactors.Add("Superior numbers");if(p.Fortifications.Count>0)f.DefenderFactors.Add("Fortified position");if(HasReinforcements(p,BattleSide.Attacker))f.AttackerFactors.Add("Reinforcements approaching");if(HasReinforcements(p,BattleSide.Defender))f.DefenderFactors.Add("Reinforcements approaching");}
    private static bool HasReinforcements(EngagementPreview p,BattleSide side){foreach(var g in p.Reinforcements)if(g.Side==side&&g.IsEligible&&g.Units.Count>0)return true;return false;}
}
