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
    private BattleDomain? visibleDomain;
    public event Action<int> CellClicked;
    public string VisibleLayerName=>visibleDomain?.ToString()??"All";
    public BattleBoardLayout Layout=>board!=null?board.Layout:null;

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
    private void PresentAction(BattlePresentationEvent action){if(action==null||Layout==null)return;if(action.Type==BattlePresentationEventType.Move||action.Type==BattlePresentationEventType.Retreat){if(unitViews.TryGetValue(action.UnitId,out var moving)){var points=new List<Vector3>();var state=manager.GetBattleUnit(action.UnitId);foreach(int cell in action.Path)points.Add(GetBattleCellWorldPosition(cell,state?.Domain??BattleDomain.Land,state?.DepthBand??BattleDepthBand.Surface));StartCoroutine(moving.Traverse(points,6f));}}else if(action.Type==BattlePresentationEventType.Attack)StartCoroutine(PlayAttack(action));else if(action.Type==BattlePresentationEventType.Defend&&unitViews.TryGetValue(action.UnitId,out var defending))defending.SetFortified(true);}
    private System.Collections.IEnumerator PlayAttack(BattlePresentationEvent action){if(!unitViews.TryGetValue(action.UnitId,out var attacker))yield break;unitViews.TryGetValue(action.TargetUnitId,out var defender);Vector3 target=Layout.GetCellCenter(action.TargetCell);attacker.Face(target);attacker.PlayAttack();yield return new WaitForSecondsRealtime(.28f);if(action.IsRanged)yield return StartCoroutine(PlayTracer(attacker.transform.position+Vector3.up*.35f,target+Vector3.up*.3f));if(defender!=null){if(action.Died)defender.PlayDeath();else defender.PlayHit();}yield return new WaitForSecondsRealtime(action.Died ? .8f : .3f);}
    private System.Collections.IEnumerator PlayTracer(Vector3 from,Vector3 to){var tracer=GameObject.CreatePrimitive(PrimitiveType.Sphere);tracer.name="Presentation-only projectile";tracer.transform.localScale=Vector3.one*.12f;var c=tracer.GetComponent<Collider>();if(c!=null)Destroy(c);float t=0f;while(t<1f){t+=Time.unscaledDeltaTime*5f;Vector3 p=Vector3.Lerp(from,to,t);p.y+=Mathf.Sin(Mathf.Clamp01(t)*Mathf.PI)*.8f;tracer.transform.position=p;yield return null;}Destroy(tracer);}
    private void Build(){if(root!=null)return;root=new GameObject("Tactical Battlefield");root.transform.SetParent(transform,false);board=new GameObject("Hex Board").AddComponent<BattleBoardView>();board.transform.SetParent(root.transform,false);board.CellClicked+=i=>CellClicked?.Invoke(i);unitsRoot=new GameObject("Tactical Unit Visuals").transform;unitsRoot.SetParent(root.transform,false);root.SetActive(false);}
    private void ClearUnits(){foreach(var p in unitViews)if(p.Value!=null)Destroy(p.Value.gameObject);unitViews.Clear();}
    private void Hide(){ClearUnits();if(board!=null)board.Clear();battleId=-1;if(root!=null)root.SetActive(false);}
}
