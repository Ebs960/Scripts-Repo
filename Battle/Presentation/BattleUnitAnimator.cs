using System.Collections.Generic;
using UnityEngine;

/// <summary>Safe adapter for heterogeneous campaign-authored animator controllers.</summary>
public sealed class BattleUnitAnimator : MonoBehaviour
{
    private sealed class Entry { public Animator Animator; public readonly HashSet<int> Bools=new(); public readonly HashSet<int> Triggers=new(); }
    private readonly List<Entry> entries=new();
    private static readonly int Walking=Animator.StringToHash("IsWalking"), Attack=Animator.StringToHash("Attack"), Hit=Animator.StringToHash("Hit"), Death=Animator.StringToHash("Death"), Fortified=Animator.StringToHash("IsFortified");
    public void Rebuild(){entries.Clear();foreach(var animator in GetComponentsInChildren<Animator>(true)){if(animator.runtimeAnimatorController==null)continue;var e=new Entry{Animator=animator};foreach(var p in animator.parameters){if(p.type==AnimatorControllerParameterType.Bool)e.Bools.Add(p.nameHash);else if(p.type==AnimatorControllerParameterType.Trigger)e.Triggers.Add(p.nameHash);}entries.Add(e);}}
    public void SetWalking(bool value)=>SetBool(Walking,value);
    public void SetFortified(bool value)=>SetBool(Fortified,value);
    public void PlayAttack()=>Trigger(Attack);
    public void PlayHit()=>Trigger(Hit);
    public void PlayDeath()=>Trigger(Death);
    private void SetBool(int id,bool value){foreach(var e in entries)if(e.Animator!=null&&e.Bools.Contains(id))e.Animator.SetBool(id,value);}
    private void Trigger(int id){foreach(var e in entries)if(e.Animator!=null&&e.Triggers.Contains(id))e.Animator.SetTrigger(id);}
}
