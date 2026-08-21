using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BattleBoardView : MonoBehaviour
{
    private readonly List<BattleCellView> cells = new();
    private Material groundMaterial, overlayMaterial;
    private BattleEnvironmentRenderer environment;
    public BattleBoardLayout Layout { get; private set; }
    public event Action<int> CellClicked;
    public event Action<int> CellPointerEntered;
    public event Action<int> CellPointerExited;
    public void Build(BattleSession session,BiomeVisualDatabase campaignVisuals=null,BattleBiomeVisualDatabase battleVisuals=null)
    {
        Clear(); Layout = BattleBoardLayout.Build(session);
        groundMaterial = CreateGroundMaterial(); overlayMaterial = CreateMaterial(true);
        for(int i=0;i<session.Map.CellCount;i++) { var cell=session.Map.Cells[i];var go=new GameObject($"Battle Cell {i}"); go.transform.SetParent(transform,false); var view=go.AddComponent<BattleCellView>();var profile=battleVisuals?.Get(cell.Biome);var surface=BattleGroundSurfaceResolver.Resolve(cell,session.RandomSeed,campaignVisuals,profile);view.Initialize(cell,Layout.GetCellCenter(i),groundMaterial,overlayMaterial,surface); view.Clicked+=OnClicked;view.PointerEntered+=OnPointerEntered;view.PointerExited+=OnPointerExited; cells.Add(view); }
        environment=new GameObject("Battle Environment").AddComponent<BattleEnvironmentRenderer>();environment.transform.SetParent(transform,false);environment.Build(session,Layout,battleVisuals);
    }
    public void SetOverlay(int index, BattlePresenter.CellOverlay state, bool selected) { if(index>=0&&index<cells.Count)cells[index].SetOverlay(state,selected); }
    private void OnClicked(int index)=>CellClicked?.Invoke(index);
    private void OnPointerEntered(int index)=>CellPointerEntered?.Invoke(index);
    private void OnPointerExited(int index)=>CellPointerExited?.Invoke(index);
    public void Clear(){if(environment!=null)environment.Clear();environment=null;for(int i=transform.childCount-1;i>=0;i--)Destroy(transform.GetChild(i).gameObject);cells.Clear();if(groundMaterial!=null)Destroy(groundMaterial);if(overlayMaterial!=null)Destroy(overlayMaterial); }
    private static Material CreateGroundMaterial(){var shader=Shader.Find("Battle/Tactical Terrain Array")??Shader.Find("HDRP/Lit")??Shader.Find("Standard");return new Material(shader){name="Shared Tactical Biome Ground"};}
    private static Material CreateMaterial(bool transparent){ var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("HDRP/Lit")??Shader.Find("Standard");var m=new Material(shader);if(transparent){m.SetFloat("_Surface",1);m.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);m.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);m.renderQueue=3000;}return m; }
    private void OnDestroy()=>Clear();
}
