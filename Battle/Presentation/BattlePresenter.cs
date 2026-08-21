using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>World-space tactical presentation. It observes authority and never mutates it.</summary>
public sealed class BattlePresenter : MonoBehaviour
{
    [Flags] public enum CellOverlay { None=0, Move=1, Attack=2, Invalid=4, Objective=8, Reinforcement=16, RetreatExit=32, RetreatPath=64, Suspected=128, Detected=256, Identified=512 }
    private BattleManager manager;
    private GameObject root;
    private BattleBoardView board;
    private Transform unitsRoot;
    private readonly Dictionary<int,BattleUnitView> unitViews=new();
    private readonly HashSet<int> moves=new(), attacks=new();
    private readonly Dictionary<int,CellOverlay> overlays=new();
    private int selected=-1, battleId=-1;
    private readonly Queue<BattlePresentationEvent> presentationQueue=new();
    private readonly List<GameObject> temporaryEffects=new();
    private Coroutine queueRoutine;
    public bool IsPresenting=>queueRoutine!=null||presentationQueue.Count>0;
    public event Action PresentationQueueDrained;
    private BattleDomain? visibleDomain;
    public event Action<int> CellClicked;
    public event Action<int> CellPointerEntered;
    public event Action<int> CellPointerExited;
    public string VisibleLayerName=>visibleDomain?.ToString()??"All";
    public BattleBoardLayout Layout=>board!=null?board.Layout:null;
    public void ResetTransientPresentation()=>ClearUnits();

    public static BattlePresenter GetOrCreate(BattleManager manager){var p=manager.GetComponent<BattlePresenter>();return p!=null?p:manager.gameObject.AddComponent<BattlePresenter>();}
    public void Bind(BattleManager battleManager){if(manager==battleManager)return;manager=battleManager;manager.BattleStarted+=Present;manager.BattleStateChanged+=Present;manager.BattlePreviewClosed+=Hide;manager.BattleActionPresented+=PresentAction;Build();}
    private void OnDestroy(){ClearUnits();if(manager!=null){manager.BattleStarted-=Present;manager.BattleStateChanged-=Present;manager.BattlePreviewClosed-=Hide;manager.BattleActionPresented-=PresentAction;}}
    public void CycleLayer(){visibleDomain=visibleDomain switch{null=>BattleDomain.Land,BattleDomain.Land=>BattleDomain.NavalSurface,BattleDomain.NavalSurface=>BattleDomain.Underwater,BattleDomain.Underwater=>BattleDomain.Air,BattleDomain.Air=>BattleDomain.Orbit,BattleDomain.Orbit=>BattleDomain.Space,_=>null};Refresh();}
    public void AdjustZoom(float delta)=>manager?.AdjustTacticalCameraZoom(delta);
    public BattleUnitState GetDisplayedUnitAtCell(int cell){var u=manager?.GetUnitAtCell(cell);return u!=null&&(!visibleDomain.HasValue||u.Domain==visibleDomain)?u:null;}
    public Vector3 GetBattleCellWorldPosition(int cellIndex,BattleDomain domain,BattleDepthBand depth=BattleDepthBand.Surface){if(Layout==null)return Vector3.zero;var dummy=manager?.GetUnitAtCell(cellIndex);if(dummy!=null)return Layout.GetUnitPosition(manager.ActiveBattle,dummy);Vector3 p=Layout.GetCellCenter(cellIndex);p.y+=domain switch{BattleDomain.Underwater=>depth==BattleDepthBand.Deep?-.8f:-.35f,BattleDomain.Air=>2.2f,BattleDomain.Orbit=>4.2f,BattleDomain.Space=>.45f,_=>.12f};return p;}
    public void SetOverlays(int selectedCell,IEnumerable<int> moveCells,IEnumerable<int> attackCells){selected=selectedCell;moves.Clear();attacks.Clear();if(moveCells!=null)foreach(int i in moveCells)moves.Add(i);if(attackCells!=null)foreach(int i in attackCells)attacks.Add(i);RefreshOverlays();}
    public void SetRichOverlays(Dictionary<int,CellOverlay> states){overlays.Clear();if(states!=null)foreach(var p in states)overlays[p.Key]=p.Value;RefreshOverlays();}

    public void Present(BattleSession session)
    {
        Build();if(session==null){Hide();return;}root.SetActive(true);
        if(battleId!=session.BattleId){ClearUnits();board.Build(session,manager?.ResolveCampaignBiomeVisuals(),manager?.TacticalBiomeVisuals);battleId=session.BattleId;manager?.FrameTacticalBattlefield(board.Layout.Bounds);}
        Refresh();
    }
    private void Refresh(){var session=manager?.ActiveBattle;if(session==null||Layout==null)return;RefreshOverlays();var live=new HashSet<int>();
        foreach(var unit in session.Units){if(unit==null)continue;live.Add(unit.UnitId);if(!unitViews.TryGetValue(unit.UnitId,out var view)||view==null){var go=new GameObject($"Tactical Unit {unit.UnitId}");go.transform.SetParent(unitsRoot,false);view=go.AddComponent<BattleUnitView>();view.Initialize(unit);unitViews[unit.UnitId]=view;}
            bool inCell=unit.CellIndex>=0&&unit.CellIndex<session.Map.CellCount;var detection=inCell?manager.GetDetectionLevel(session.ActiveSide,unit):BattleDetectionLevel.Undetected;bool visible=inCell&&unit.IsAliveAndActive&&(unit.Side==session.ActiveSide||detection>=BattleDetectionLevel.Detected)&&(!visibleDomain.HasValue||unit.Domain==visibleDomain);view.Sync(unit,inCell?Layout.GetUnitPosition(session,unit):Vector3.zero,visible,selected==unit.CellIndex);}
        var stale=new List<int>();foreach(var p in unitViews)if(!live.Contains(p.Key))stale.Add(p.Key);foreach(int id in stale){if(unitViews[id]!=null)Destroy(unitViews[id].gameObject);unitViews.Remove(id);}}
    private void RefreshOverlays(){var session=manager?.ActiveBattle;if(session==null||board==null)return;for(int i=0;i<session.Map.CellCount;i++){overlays.TryGetValue(i,out var state);if(moves.Contains(i))state|=CellOverlay.Move;if(attacks.Contains(i))state|=CellOverlay.Attack;board.SetOverlay(i,state,i==selected);}}
    private void PresentAction(BattlePresentationEvent action)
    {
        if(action==null||Layout==null)return;
        PreserveAffectedViews(action);
        presentationQueue.Enqueue(action);
        manager?.TacticalInput?.SetCommandInputLocked(true);
        if(queueRoutine==null)queueRoutine=StartCoroutine(DrainPresentationQueue());
    }
    private void PreserveAffectedViews(BattlePresentationEvent action)
    {
        if(unitViews.TryGetValue(action.UnitId,out var actor))actor.BeginPresentation(retreating:action.Type==BattlePresentationEventType.Retreat||action.Type==BattlePresentationEventType.Embark||action.Type==BattlePresentationEventType.Recover);
        if(unitViews.TryGetValue(action.TargetUnitId,out var target))target.BeginPresentation(action.Died);
        foreach(var damage in action.SplashDamage)if(unitViews.TryGetValue(damage.TargetUnitId,out var splash))splash.BeginPresentation(damage.Died);
    }
    private System.Collections.IEnumerator DrainPresentationQueue()
    {
        while(presentationQueue.Count>0)yield return StartCoroutine(PlaySequence(presentationQueue.Dequeue()));
        queueRoutine=null;manager?.TacticalInput?.SetCommandInputLocked(false);PresentationQueueDrained?.Invoke();
    }
    private System.Collections.IEnumerator PlaySequence(BattlePresentationEvent action)
    {
        switch(action.Type)
        {
            case BattlePresentationEventType.Move:case BattlePresentationEventType.Retreat:
                if(unitViews.TryGetValue(action.UnitId,out var moving)){var points=BuildPath(action,moving);yield return StartCoroutine(moving.Traverse(points,6f));yield return new WaitForSecondsRealtime(.12f);moving.FinishPresentation(action.Type==BattlePresentationEventType.Retreat&&manager?.GetBattleUnit(action.UnitId)?.HasRetreated==true);}break;
            case BattlePresentationEventType.Attack:yield return StartCoroutine(PlayAttack(action));break;
            case BattlePresentationEventType.Defend:if(unitViews.TryGetValue(action.UnitId,out var defending)){defending.SetFortified(true);defending.FinishPresentation();}yield return new WaitForSecondsRealtime(.15f);break;
            case BattlePresentationEventType.Embark:case BattlePresentationEventType.Recover:yield return StartCoroutine(PlayTransfer(action,true));break;
            case BattlePresentationEventType.Disembark:case BattlePresentationEventType.Launch:yield return StartCoroutine(PlayTransfer(action,false));break;
            default:if(unitViews.TryGetValue(action.UnitId,out var unit))unit.FinishPresentation();yield return new WaitForSecondsRealtime(.12f);break;
        }
    }
    private List<Vector3> BuildPath(BattlePresentationEvent action,BattleUnitView view){var points=new List<Vector3>();var state=manager.GetBattleUnit(action.UnitId);foreach(int cell in action.Path)points.Add(GetBattleCellWorldPosition(cell,state?.Domain??BattleDomain.Land,state?.DepthBand??BattleDepthBand.Surface));return points;}
    private System.Collections.IEnumerator PlayTransfer(BattlePresentationEvent action,bool hide)
    {if(!unitViews.TryGetValue(action.UnitId,out var unit))yield break;Vector3 destination=action.TargetCell>=0?Layout.GetCellCenter(action.TargetCell):unit.transform.position;if(action.TargetUnitId>=0&&unitViews.TryGetValue(action.TargetUnitId,out var host))destination=host.transform.position;unit.gameObject.SetActive(true);unit.Face(destination);yield return StartCoroutine(unit.Traverse(new[]{destination},5f));yield return new WaitForSecondsRealtime(.15f);unit.FinishPresentation(hide);}

    private System.Collections.IEnumerator PlayAttack(BattlePresentationEvent action)
    {if(!unitViews.TryGetValue(action.UnitId,out var attacker))yield break;unitViews.TryGetValue(action.TargetUnitId,out var defender);Vector3 target=defender!=null?defender.transform.position:Layout.GetCellCenter(action.TargetCell);attacker.Face(target);if(action.IsRanged){attacker.PlayAttack();yield return new WaitForSecondsRealtime(.2f);yield return StartCoroutine(PlayProjectile(attacker.Snapshot,action.WeaponIndex,action.IsSpecial,attacker.transform.position+Vector3.up*.35f,target+Vector3.up*.3f));}else yield return StartCoroutine(attacker.Lunge(target));
        if(defender!=null){if(action.Died)defender.PlayDeath();else defender.PlayHit();}foreach(var damage in action.SplashDamage)if(unitViews.TryGetValue(damage.TargetUnitId,out var splash)){if(damage.Died)splash.PlayDeath();else splash.PlayHit();}yield return new WaitForSecondsRealtime(action.Died ? .8f : .3f);if(defender!=null)defender.FinishPresentation(action.Died);
        foreach(var damage in action.SplashDamage)if(unitViews.TryGetValue(damage.TargetUnitId,out var splash))splash.FinishPresentation(damage.Died);
        if(action.CounterAttack!=null){var counter=action.CounterAttack;var counterEvent=new BattlePresentationEvent{Type=BattlePresentationEventType.CounterAttack,UnitId=counter.AttackerUnitId,TargetUnitId=counter.TargetUnitId,WeaponIndex=counter.WeaponIndex,IsRanged=counter.IsRanged,Died=counter.Damage?.Died??false};if(unitViews.TryGetValue(counter.AttackerUnitId,out var counterView)&&unitViews.TryGetValue(counter.TargetUnitId,out var counterTarget)){Vector3 counterPoint=counterTarget.transform.position;counterView.Face(counterPoint);if(counter.IsRanged){counterView.PlayAttack();yield return new WaitForSecondsRealtime(.2f);yield return StartCoroutine(PlayProjectile(counterView.Snapshot,counter.WeaponIndex,false,counterView.transform.position+Vector3.up*.35f,counterPoint+Vector3.up*.3f));}else yield return StartCoroutine(counterView.Lunge(counterPoint));if(counterEvent.Died)counterTarget.PlayDeath();else counterTarget.PlayHit();yield return new WaitForSecondsRealtime(counterEvent.Died ? .8f : .3f);counterTarget.FinishPresentation(counterEvent.Died);counterView.FinishPresentation();}}
        attacker.FinishPresentation(action.CounterAttack?.Damage?.Died??false);}

    private System.Collections.IEnumerator PlayProjectile(BattleUnitSnapshot snapshot,int weaponIndex,bool special,Vector3 from,Vector3 to)
    {
        BattleProjectileVisual visual=BattleProjectileVisualResolver.Resolve(snapshot,weaponIndex,special);
        if(visual.TravelType==BattleProjectileTravelType.Beam){var beam=GameObject.CreatePrimitive(PrimitiveType.Cube);Track(beam,"Presentation-only beam");Vector3 delta=to-from;beam.transform.SetPositionAndRotation((from+to)*.5f,Quaternion.LookRotation(delta));beam.transform.localScale=new Vector3(special?.08f:.035f,special?.08f:.035f,delta.magnitude);yield return new WaitForSecondsRealtime(special?.18f:.1f);DestroyEffect(beam);}
        else {GameObject projectile=visual.Prefab!=null?Instantiate(visual.Prefab):GameObject.CreatePrimitive(PrimitiveType.Sphere);Track(projectile,visual.Prefab!=null?$"Tactical projectile ({visual.Prefab.name})":"Presentation-only projectile fallback");projectile.transform.position=from;projectile.transform.localScale=Vector3.Scale(projectile.transform.localScale,visual.Prefab!=null?visual.Scale:visual.Scale*.12f);float distance=Vector3.Distance(from,to),duration=Mathf.Clamp(distance/visual.Speed,.08f,2f),elapsed=0f;Vector3 previous=from;while(elapsed<duration){elapsed+=Time.unscaledDeltaTime;float t=Mathf.Clamp01(elapsed/duration);Vector3 p=Vector3.Lerp(from,to,t);if(visual.TravelType==BattleProjectileTravelType.BallisticArc||visual.TravelType==BattleProjectileTravelType.Tracer)p.y+=Mathf.Sin(t*Mathf.PI)*visual.ArcHeight;Vector3 velocity=p-previous;if(velocity.sqrMagnitude>.00001f)projectile.transform.rotation=Quaternion.LookRotation(velocity);projectile.transform.position=p;previous=p;yield return null;}DestroyEffect(projectile);}
        if(visual.ImpactPrefab!=null){var impact=Instantiate(visual.ImpactPrefab,to,Quaternion.identity);Track(impact,$"Tactical impact ({visual.ImpactPrefab.name})");impact.transform.localScale*=special?1.5f:1f;yield return new WaitForSecondsRealtime(special?.35f:.2f);DestroyEffect(impact);}
    }
    private void Track(GameObject effect,string effectName){effect.name=effectName;temporaryEffects.Add(effect);foreach(var collider in effect.GetComponentsInChildren<Collider>(true))collider.enabled=false;}
    private void DestroyEffect(GameObject effect){temporaryEffects.Remove(effect);if(effect!=null)Destroy(effect);}
    private void Build(){if(root!=null)return;root=new GameObject("Tactical Battlefield");root.transform.SetParent(transform,false);board=new GameObject("Hex Board").AddComponent<BattleBoardView>();board.transform.SetParent(root.transform,false);board.CellClicked+=i=>CellClicked?.Invoke(i);board.CellPointerEntered+=i=>CellPointerEntered?.Invoke(i);board.CellPointerExited+=i=>CellPointerExited?.Invoke(i);unitsRoot=new GameObject("Tactical Unit Visuals").transform;unitsRoot.SetParent(root.transform,false);root.SetActive(false);}
    private void ClearUnits(){StopAllCoroutines();queueRoutine=null;presentationQueue.Clear();manager?.TacticalInput?.SetCommandInputLocked(false);foreach(var effect in temporaryEffects)if(effect!=null)Destroy(effect);temporaryEffects.Clear();foreach(var p in unitViews)if(p.Value!=null)Destroy(p.Value.gameObject);unitViews.Clear();}
    private void Hide(){ClearUnits();if(board!=null)board.Clear();battleId=-1;if(root!=null)root.SetActive(false);}
}
