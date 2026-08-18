using System;
using UnityEngine;

/// <summary>Raycastable world-space rendering of one authoritative BattleCell.</summary>
public sealed class BattleCellView : MonoBehaviour
{
    private static Mesh sharedHex;
    private Renderer ground;
    private Renderer overlay;
    private MaterialPropertyBlock properties;
    public int CellIndex { get; private set; }
    public event Action<int> Clicked;

    public void Initialize(BattleCell cell, Vector3 center, Material groundMaterial, Material overlayMaterial)
    {
        CellIndex = cell.BattleIndex; transform.localPosition = center;
        float height = Mathf.Max(.12f, center.y + .12f);
        var groundObject = new GameObject("Terrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        groundObject.transform.SetParent(transform, false); groundObject.transform.localPosition = new Vector3(0f, -center.y * .5f, 0f);
        groundObject.transform.localScale = new Vector3(1f, height, 1f);
        groundObject.GetComponent<MeshFilter>().sharedMesh = GetHexMesh(); groundObject.GetComponent<MeshCollider>().sharedMesh = GetHexMesh();
        ground = groundObject.GetComponent<MeshRenderer>(); ground.sharedMaterial = groundMaterial;
        var overlayObject = new GameObject("Overlay", typeof(MeshFilter), typeof(MeshRenderer)); overlayObject.transform.SetParent(transform, false);
        overlayObject.transform.localPosition = new Vector3(0f, .025f, 0f); overlayObject.transform.localScale = new Vector3(.94f,.02f,.94f);
        overlayObject.GetComponent<MeshFilter>().sharedMesh = GetHexMesh(); overlay = overlayObject.GetComponent<MeshRenderer>(); overlay.sharedMaterial = overlayMaterial;
        properties = new MaterialPropertyBlock(); SetGroundColor(TerrainColor(cell)); SetOverlay(BattlePresenter.CellOverlay.None, false);
        AddTerrainCues(cell);
    }

    private void OnMouseDown() => Clicked?.Invoke(CellIndex);
    public void SetOverlay(BattlePresenter.CellOverlay state, bool selected)
    {
        Color color = selected ? new Color(1f,.75f,.12f,.75f) : OverlayColor(state);
        properties.Clear(); properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color); overlay.SetPropertyBlock(properties);
        overlay.enabled = selected || state != BattlePresenter.CellOverlay.None;
    }
    private void SetGroundColor(Color color) { properties.Clear(); properties.SetColor("_BaseColor",color); properties.SetColor("_Color",color); ground.SetPropertyBlock(properties); }

    private static Color TerrainColor(BattleCell c)
    {
        if (c.IsWater) return c.WaterDepthLevel > 1 ? new Color(.035f,.15f,.29f) : new Color(.08f,.36f,.5f);
        if (c.HasBeach) return new Color(.65f,.57f,.35f);
        if (c.IsForest) return new Color(.10f,.29f,.12f);
        return c.Biome switch { Biome.Desert => new Color(.62f,.48f,.25f), Biome.Tundra => new Color(.48f,.55f,.54f), _ => new Color(.25f,.39f,.20f) };
    }
    private static Color OverlayColor(BattlePresenter.CellOverlay s)
    {
        if ((s & BattlePresenter.CellOverlay.Attack) != 0) return new Color(.85f,.12f,.08f,.62f);
        if ((s & BattlePresenter.CellOverlay.Move) != 0) return new Color(.08f,.65f,.75f,.54f);
        if ((s & BattlePresenter.CellOverlay.RetreatPath) != 0) return new Color(.1f,.8f,.45f,.58f);
        if ((s & BattlePresenter.CellOverlay.Objective) != 0) return new Color(.92f,.66f,.08f,.55f);
        if ((s & BattlePresenter.CellOverlay.Reinforcement) != 0) return new Color(.48f,.2f,.7f,.5f);
        if ((s & BattlePresenter.CellOverlay.Invalid) != 0) return new Color(.45f,.05f,.05f,.25f);
        return new Color(.2f,.6f,.3f,.42f);
    }
    private void AddTerrainCues(BattleCell c)
    {
        if (!(c.IsForest || c.HasHardCover || c.HasSoftCover || c.HasPort || c.HasRiver || c.IsObjective)) return;
        var cue = GameObject.CreatePrimitive(c.HasHardCover || c.HasPort ? PrimitiveType.Cube : PrimitiveType.Cylinder);
        cue.name = c.IsObjective ? "Objective" : c.HasPort ? "Port" : c.HasHardCover ? "Hard Cover" : c.IsForest ? "Forest" : "Terrain Feature";
        cue.transform.SetParent(transform, false); cue.transform.localPosition = new Vector3(.45f,.18f,-.25f); cue.transform.localScale = c.IsObjective ? new Vector3(.12f,.7f,.12f) : new Vector3(.22f,.35f,.22f);
        var collider = cue.GetComponent<Collider>(); if (collider != null) Destroy(collider);
    }
    private static Mesh GetHexMesh()
    {
        if (sharedHex != null) return sharedHex;
        var vertices = new Vector3[14]; var triangles = new int[72];
        for (int i=0;i<7;i++) { float a=Mathf.Deg2Rad*(60*i+30); vertices[i]=new Vector3(Mathf.Cos(a)*BattleBoardLayout.HexRadius,.5f,Mathf.Sin(a)*BattleBoardLayout.HexRadius); vertices[i+7]=new Vector3(vertices[i].x,-.5f,vertices[i].z); }
        int t=0; for(int i=0;i<6;i++){ triangles[t++]=6;triangles[t++]=i;triangles[t++]=i+1; triangles[t++]=13;triangles[t++]=i+8;triangles[t++]=i+7; triangles[t++]=i;triangles[t++]=i+7;triangles[t++]=i+8;triangles[t++]=i;triangles[t++]=i+8;triangles[t++]=i+1; }
        sharedHex = new Mesh { name="Tactical Hex" }; sharedHex.vertices=vertices; sharedHex.triangles=triangles; sharedHex.RecalculateNormals(); sharedHex.RecalculateBounds(); return sharedHex;
    }
}
