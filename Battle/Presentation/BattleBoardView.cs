using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BattleBoardView : MonoBehaviour
{
    private readonly List<BattleCellView> cells = new();
    private Material groundMaterial, overlayMaterial;
    public BattleBoardLayout Layout { get; private set; }
    public event Action<int> CellClicked;
    public void Build(BattleSession session)
    {
        Clear(); Layout = BattleBoardLayout.Build(session);
        groundMaterial = CreateMaterial(false); overlayMaterial = CreateMaterial(true);
        for(int i=0;i<session.Map.CellCount;i++) { var go=new GameObject($"Battle Cell {i}"); go.transform.SetParent(transform,false); var view=go.AddComponent<BattleCellView>(); view.Initialize(session.Map.Cells[i],Layout.GetCellCenter(i),groundMaterial,overlayMaterial); view.Clicked+=OnClicked; cells.Add(view); }
    }
    public void SetOverlay(int index, BattlePresenter.CellOverlay state, bool selected) { if(index>=0&&index<cells.Count)cells[index].SetOverlay(state,selected); }
    private void OnClicked(int index)=>CellClicked?.Invoke(index);
    public void Clear(){ for(int i=transform.childCount-1;i>=0;i--)Destroy(transform.GetChild(i).gameObject);cells.Clear();if(groundMaterial!=null)Destroy(groundMaterial);if(overlayMaterial!=null)Destroy(overlayMaterial); }
    private static Material CreateMaterial(bool transparent){ var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("HDRP/Lit")??Shader.Find("Standard");var m=new Material(shader);if(transparent){m.SetFloat("_Surface",1);m.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);m.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);m.renderQueue=3000;}return m; }
    private void OnDestroy()=>Clear();
}
