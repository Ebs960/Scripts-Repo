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

    public void Initialize(BattleCell cell, Vector3 center, Material groundMaterial, Material overlayMaterial,BattleGroundSurface surface)
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
        properties = new MaterialPropertyBlock(); SetGroundSurface(cell,surface); SetOverlay(BattlePresenter.CellOverlay.None, false);
        AddTerrainCues(cell);
    }

    private void OnMouseDown() => Clicked?.Invoke(CellIndex);
    public void SetOverlay(BattlePresenter.CellOverlay state, bool selected)
    {
        Color color = selected ? new Color(1f,.75f,.12f,.75f) : OverlayColor(state);
        properties.Clear(); properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color); overlay.SetPropertyBlock(properties);
        overlay.enabled = selected || state != BattlePresenter.CellOverlay.None;
    }
    private void SetGroundSurface(BattleCell cell,BattleGroundSurface surface)
    {
        properties.Clear();Color tint=surface.Visual!=null?surface.Visual.tint:FallbackColor(cell);if(surface.Family!=null)tint*=surface.Family.defaultTint;
        if(surface.Visual!=null&&ClimateManager.Instance!=null){BiomeSeasonVisualResponse season=ClimateManager.Instance.currentSeason switch{Season.Spring=>surface.Visual.springResponse,Season.Summer=>surface.Visual.summerResponse,Season.Autumn=>surface.Visual.autumnResponse,Season.Winter=>surface.Visual.winterResponse,_=>default};if(season.tint.a>0f)tint*=season.tint;tint=Color.Lerp(tint,new Color(.9f,.93f,.96f,tint.a),season.snow*.7f);}
        properties.SetColor("_BaseColor",tint);properties.SetColor("_Color",tint);properties.SetFloat("_Slice",surface.Variant);
        properties.SetFloat("_Tiling",Mathf.Max(.01f,(surface.Visual?.tiling??1f)*(surface.Family?.defaultTiling??1f)));
        properties.SetFloat("_NormalStrength",surface.Family?.normalStrength??1f);properties.SetFloat("_HasSurface",surface.Albedo!=null?1f:0f);
        if(surface.Albedo!=null)properties.SetTexture("_AlbedoArray",surface.Albedo);if(surface.Normal!=null)properties.SetTexture("_NormalArray",surface.Normal);
        if(surface.Mask!=null)properties.SetTexture("_MaskArray",surface.Mask);if(surface.Height!=null)properties.SetTexture("_HeightArray",surface.Height);if(surface.Emissive!=null)properties.SetTexture("_EmissiveArray",surface.Emissive);
        properties.SetFloat("_HasNormal",surface.Normal!=null?1f:0f);properties.SetFloat("_HasMask",surface.Mask!=null?1f:0f);properties.SetFloat("_HasHeight",surface.Height!=null?1f:0f);properties.SetFloat("_HasEmissive",surface.Emissive!=null?1f:0f);ground.SetPropertyBlock(properties);
    }

    private static Color FallbackColor(BattleCell c)
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
        if (!c.IsObjective) return;
        var cue = GameObject.CreatePrimitive(PrimitiveType.Cylinder);cue.name="Objective";
        cue.transform.SetParent(transform, false); cue.transform.localPosition = new Vector3(.45f,.18f,-.25f); cue.transform.localScale = new Vector3(.12f,.7f,.12f);
        var collider = cue.GetComponent<Collider>(); if (collider != null) Destroy(collider);
    }
    private static Mesh GetHexMesh()
    {
        if (sharedHex != null) return sharedHex;
        var vertices = new Vector3[14];var uv=new Vector2[14];var triangles = new int[72];
        for (int i=0;i<6;i++){float a=Mathf.Deg2Rad*(60*i+30);vertices[i]=new Vector3(Mathf.Cos(a)*BattleBoardLayout.HexRadius,.5f,Mathf.Sin(a)*BattleBoardLayout.HexRadius);vertices[i+7]=new Vector3(vertices[i].x,-.5f,vertices[i].z);uv[i]=uv[i+7]=new Vector2(vertices[i].x/BattleBoardLayout.HexRadius*.5f+.5f,vertices[i].z/BattleBoardLayout.HexRadius*.5f+.5f);}vertices[6]=new Vector3(0,.5f,0);vertices[13]=new Vector3(0,-.5f,0);uv[6]=uv[13]=new Vector2(.5f,.5f);
        int t=0;for(int i=0;i<6;i++){int next=(i+1)%6;triangles[t++]=6;triangles[t++]=i;triangles[t++]=next;triangles[t++]=13;triangles[t++]=next+7;triangles[t++]=i+7;triangles[t++]=i;triangles[t++]=i+7;triangles[t++]=next+7;triangles[t++]=i;triangles[t++]=next+7;triangles[t++]=next;}
        sharedHex = new Mesh { name="Tactical Hex" }; sharedHex.vertices=vertices;sharedHex.uv=uv;sharedHex.triangles=triangles; sharedHex.RecalculateNormals(); sharedHex.RecalculateBounds(); return sharedHex;
    }
}
