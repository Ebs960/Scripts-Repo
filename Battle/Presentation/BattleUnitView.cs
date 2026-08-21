using System.Collections.Generic;
using UnityEngine;

/// <summary>Visual-only tactical representation. It never owns or mutates campaign state.</summary>
public sealed class BattleUnitView : MonoBehaviour
{
    public enum VisualLifecycle { Normal, PresentingAction, Dying, Retreating, Hidden }
    public int BattleUnitId { get; private set; }
    public BattleUnitSnapshot Snapshot { get; private set; }

    private readonly List<GameObject> figures = new();
    private Transform figureRoot;
    private GameObject selectionRing;
    private Transform healthBar;
    private Transform healthFill;
    private Camera tacticalCamera;
    private Vector3 targetPosition;
    private bool hasPosition;
    private bool presenting;
    private BattleUnitAnimator animationAdapter;
    private int deferredFigureCount = -1;
    public VisualLifecycle Lifecycle { get; private set; }

    public void Initialize(BattleUnitState state,Camera camera=null)
    {
        tacticalCamera=camera;
        BattleUnitId = state != null ? state.UnitId : -1;
        Snapshot = state?.Snapshot;
        figureRoot = new GameObject("Figures").transform;
        figureRoot.SetParent(transform, false);
        figureRoot.localRotation = Quaternion.Euler(0f,
            state != null && state.Side == BattleSide.Defender ? 180f : 0f, 0f);
        BattleUnitVisualFactory.Populate(this, state, figureRoot, figures);
        animationAdapter = gameObject.AddComponent<BattleUnitAnimator>();
        animationAdapter.Rebuild();
        selectionRing = BattleUnitVisualFactory.CreateSelectionRing(transform, state?.Side ?? BattleSide.Attacker);
        selectionRing.SetActive(true);
        CreateHealthBar();
    }

    public void Face(Vector3 target){Vector3 direction=target-transform.position;direction.y=0f;if(direction.sqrMagnitude>.001f)figureRoot.rotation=Quaternion.LookRotation(direction,Vector3.up);}
    public void PlayAttack()=>animationAdapter?.PlayAttack();
    public void PlayHit()=>animationAdapter?.PlayHit();
    public void PlayDeath()=>animationAdapter?.PlayDeath();
    public void BeginPresentation(bool dying=false,bool retreating=false)
    { Lifecycle=dying?VisualLifecycle.Dying:retreating?VisualLifecycle.Retreating:VisualLifecycle.PresentingAction; gameObject.SetActive(true); }
    public void FinishPresentation(bool hide=false)
    {
        Lifecycle=hide?VisualLifecycle.Hidden:VisualLifecycle.Normal;
        if(deferredFigureCount>=0){SetFigureCount(deferredFigureCount);deferredFigureCount=-1;}
        if(hide)gameObject.SetActive(false);
    }
    public System.Collections.IEnumerator Lunge(Vector3 target,float distance=.28f)
    {
        if(figureRoot==null)yield break;Vector3 start=figureRoot.localPosition;Vector3 direction=transform.InverseTransformDirection(target-transform.position);direction.y=0f;direction=direction.sqrMagnitude>.001f?direction.normalized:Vector3.forward;
        float t=0f;while(t<1f){t+=Time.unscaledDeltaTime*7f;figureRoot.localPosition=Vector3.Lerp(start,start+direction*distance,Mathf.Clamp01(t));yield return null;}
        PlayAttack();yield return new WaitForSecondsRealtime(.16f);t=0f;while(t<1f){t+=Time.unscaledDeltaTime*8f;figureRoot.localPosition=Vector3.Lerp(start+direction*distance,start,Mathf.Clamp01(t));yield return null;}figureRoot.localPosition=start;
    }
    public void SetFortified(bool value)=>animationAdapter?.SetFortified(value);
    public System.Collections.IEnumerator Traverse(IReadOnlyList<Vector3> path,float speed)
    {
        presenting=true;
        animationAdapter?.SetWalking(true);
        for(int i=0;i<path.Count;i++){targetPosition=path[i];while((transform.position-targetPosition).sqrMagnitude>.0025f)yield return null;transform.position=targetPosition;}
        animationAdapter?.SetWalking(false);
        presenting=false;
    }

    public void Sync(BattleUnitState state, Vector3 worldPosition, bool visible, bool selected)
    {
        if (state == null)
        {
            gameObject.SetActive(false);
            return;
        }

        bool preserve = Lifecycle != VisualLifecycle.Normal && Lifecycle != VisualLifecycle.Hidden;
        gameObject.SetActive(visible || preserve);
        if (!visible && !preserve)
            return;

        if(!presenting) targetPosition = worldPosition;
        if (!hasPosition)
        {
            transform.position = targetPosition;
            hasPosition = true;
        }

        int desiredFigures = state.CurrentHealth > 0 && state.Snapshot != null
            ? Mathf.Max(1, Mathf.CeilToInt(
                Mathf.Clamp01(state.CurrentHealth / (float)Mathf.Max(1, state.Snapshot.MaximumHealth))
                * Mathf.Max(1, state.Snapshot.TacticalFigureCount)))
            : 0;
        if(preserve)deferredFigureCount=desiredFigures;else SetFigureCount(desiredFigures);

        if (selectionRing != null)
            selectionRing.transform.localScale = selected
                ? new Vector3(0.62f, 0.014f, 0.62f)
                : new Vector3(0.48f, 0.01f, 0.48f);
        if(healthBar!=null){float ratio=Mathf.Clamp01(state.CurrentHealth/(float)Mathf.Max(1,state.Snapshot?.MaximumHealth??1));healthFill.localScale=new Vector3(ratio,1f,1f);healthFill.localPosition=new Vector3((ratio-1f)*.3f,0f,-.006f);healthBar.gameObject.SetActive(visible);}
    }

    private void CreateHealthBar()
    {
        healthBar=new GameObject("Compact HP Bar").transform;healthBar.SetParent(transform,false);healthBar.localPosition=new Vector3(0f,.9f,0f);
        var background=GameObject.CreatePrimitive(PrimitiveType.Cube);background.name="Background";background.transform.SetParent(healthBar,false);background.transform.localScale=new Vector3(.64f,.075f,.025f);Destroy(background.GetComponent<Collider>());SetColor(background,new Color(.04f,.04f,.04f,.9f));
        var fill=GameObject.CreatePrimitive(PrimitiveType.Cube);fill.name="Health";fill.transform.SetParent(healthBar,false);fill.transform.localScale=new Vector3(.6f,.045f,.018f);fill.transform.localPosition=new Vector3(0f,0f,-.006f);Destroy(fill.GetComponent<Collider>());SetColor(fill,new Color(.18f,.8f,.25f));healthFill=fill.transform;
    }
    private static void SetColor(GameObject go,Color color){var renderer=go.GetComponent<Renderer>();var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);renderer.SetPropertyBlock(block);}

    private void SetFigureCount(int count){for(int i=0;i<figures.Count;i++)if(figures[i]!=null)figures[i].SetActive(i<count);}

    private void Update()
    {
        if (!hasPosition || !gameObject.activeInHierarchy)
            return;

        Vector3 previous = transform.position;
        transform.position = Vector3.Lerp(previous, targetPosition, 1f - Mathf.Exp(-(presenting?6f:12f) * Time.unscaledDeltaTime));
        Vector3 movement = targetPosition - previous;
        movement.y = 0f;
        if (movement.sqrMagnitude > 0.0001f && figureRoot != null)
            figureRoot.rotation = Quaternion.Slerp(figureRoot.rotation,
                Quaternion.LookRotation(movement.normalized, Vector3.up),
                1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
    }
    private void LateUpdate(){if(healthBar!=null&&tacticalCamera!=null)healthBar.rotation=tacticalCamera.transform.rotation;}
}
