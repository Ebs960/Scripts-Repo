using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Build-once, presentation-only biome props and instanced grass.</summary>
public sealed class BattleEnvironmentRenderer : MonoBehaviour
{
    private readonly List<GameObject> spawned=new();
    private readonly Dictionary<InstanceKey,List<Matrix4x4>> grassInstances=new();
    private readonly List<GrassBatch> grassBatches=new();
    private readonly Dictionary<Material,Material> instancedMaterials=new();
    private Material featureMaterial;
    private readonly struct InstanceKey
    {
        public readonly Mesh Mesh;public readonly Material Material;public readonly int Submesh;
        public InstanceKey(Mesh mesh,Material material,int submesh){Mesh=mesh;Material=material;Submesh=submesh;}
        public override int GetHashCode()=>((Mesh!=null?Mesh.GetEntityId().GetHashCode():0)*397)^(Material!=null?Material.GetEntityId().GetHashCode():0)^Submesh;
        public override bool Equals(object obj)=>obj is InstanceKey other&&other.Mesh==Mesh&&other.Material==Material&&other.Submesh==Submesh;
    }
    private sealed class GrassBatch{public InstanceKey Key;public Matrix4x4[] Matrices;}

    public int SpawnedObjectCount=>spawned.Count;
    public int GrassInstanceCount{get{int count=0;foreach(var batch in grassInstances)count+=batch.Value.Count;return count;}}
    public void Build(BattleSession session,BattleBoardLayout layout,BattleBiomeVisualDatabase database)
    {
        Clear();featureMaterial=CreateFeatureMaterial();
        if(database==null||database.profiles==null||database.profiles.Count==0)Debug.LogWarning("Battle biome environment profiles are not configured. Ground surfaces will render, but optional vegetation/props may be absent.",this);
        for(int i=0;i<session.Map.CellCount;i++)
        {
            BattleCell cell=session.Map.Cells[i];BattleBiomeVisualProfile profile=database?.Get(cell.Biome);
            if(profile!=null&&!cell.IsWater)
                foreach(var placement in BattleEnvironmentLayout.Generate(cell,profile,session.RandomSeed,BattleBoardLayout.HexRadius,RiverDirection(session.Map,cell,layout),FeatureDirection(session.Map,cell,layout,c=>c.IsWater)))Spawn(profile,placement,layout.GetCellCenter(i));
            CreateFeatures(session.Map,cell,layout.GetCellCenter(i),layout,profile);
        }
        CreateFortifications(session,layout);
        FinalizeGrassBatches();
    }
    private void CreateFortifications(BattleSession session,BattleBoardLayout layout)
    {
        var profile=session.FortificationProfile; if(profile==null)return;
        foreach(var structure in session.Fortifications)
        {
            var visual=profile.visualProfile; GameObject prefab=structure.Kind switch
            {
                BattleFortificationKind.Gate=>structure.IsBreached?visual?.breachedGatePrefab:visual?.gatePrefab,
                BattleFortificationKind.Wall=>structure.IsBreached?visual?.breachedWallPrefab:visual?.wallPrefab,
                _=>visual?.strongpointPrefab
            };
            Vector3 center=layout.GetCellCenter(structure.CellIndex);
            if(prefab!=null){var instance=Instantiate(prefab,transform);instance.name=$"{structure.Kind} {structure.StructureId}";instance.transform.position=transform.TransformPoint(center);foreach(var c in instance.GetComponentsInChildren<Collider>(true))c.enabled=false;spawned.Add(instance);continue;}
            Color color=visual!=null?visual.fallbackColor:profile.material switch
            { BattleFortificationMaterial.Wood=>new Color(.38f,.2f,.08f),BattleFortificationMaterial.Stone=>new Color(.42f,.43f,.46f),BattleFortificationMaterial.Earthwork=>new Color(.3f,.24f,.13f),_=>Color.gray };
            Vector3 scale=profile.material==BattleFortificationMaterial.Wood?new Vector3(1.15f,.65f,.16f):new Vector3(1.2f,.45f,.3f);
            if(structure.IsBreached)scale=new Vector3(scale.x*.55f,scale.y*.35f,scale.z*1.4f);
            CreateStrip(structure.IsBreached?$"Breached {structure.Kind}":structure.Kind.ToString(),center+Vector3.up*scale.y*.5f,scale,0f,color);
        }
    }
    private void Spawn(BattleBiomeVisualProfile profile,BattleDecorationPlacement placement,Vector3 center)
    {
        GameObject prefab=GetPrefab(profile,placement);
        if(prefab==null){if(profile.allowProceduralFallback)SpawnFallback(profile,placement,center);return;}
        Matrix4x4 matrix=transform.localToWorldMatrix*Matrix4x4.TRS(center+placement.LocalPosition,Quaternion.Euler(0f,placement.Yaw,0f),Vector3.one*placement.Scale);
        if(placement.Kind==BattleDecorationKind.Grass&&TryAddInstanced(prefab,matrix))return;
        var instance=Instantiate(prefab,transform);instance.name=$"Battle {placement.Kind} ({prefab.name})";instance.transform.SetPositionAndRotation(matrix.GetColumn(3),matrix.rotation);instance.transform.localScale=Vector3.one*placement.Scale;
        foreach(var collider in instance.GetComponentsInChildren<Collider>(true))collider.enabled=false;spawned.Add(instance);
    }
    private void SpawnFallback(BattleBiomeVisualProfile profile,BattleDecorationPlacement placement,Vector3 center)
    {
        // Deliberately simple silhouettes keep the fallback readable and cheap; authored prefabs always win.
        PrimitiveType primitive=placement.Kind==BattleDecorationKind.Rock||placement.Kind==BattleDecorationKind.HardCover?PrimitiveType.Sphere:PrimitiveType.Cube;
        Vector3 scale;Color color;
        switch(placement.Kind)
        {
            case BattleDecorationKind.Tree: scale=new Vector3(.13f,.72f,.13f);color=profile.foliageColor;break;
            case BattleDecorationKind.Grass: scale=new Vector3(.05f,.12f,.05f);color=profile.grassColor;break;
            case BattleDecorationKind.Bush: case BattleDecorationKind.SoftCover: scale=new Vector3(.32f,.2f,.3f);color=profile.foliageColor;break;
            case BattleDecorationKind.Rock: case BattleDecorationKind.HardCover: scale=new Vector3(.28f,.2f,.24f);color=profile.rockColor;break;
            default: scale=new Vector3(.2f,.24f,.18f);color=profile.clutterColor;break;
        }
        scale*=placement.Scale;var go=GameObject.CreatePrimitive(primitive);go.name=$"Procedural {placement.Kind}";go.transform.SetParent(transform,false);
        go.transform.localPosition=center+placement.LocalPosition+Vector3.up*scale.y*.5f;go.transform.localRotation=Quaternion.Euler(0f,placement.Yaw,0f);go.transform.localScale=scale;
        var collider=go.GetComponent<Collider>();if(collider!=null)Destroy(collider);var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=featureMaterial;
        var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);renderer.SetPropertyBlock(block);spawned.Add(go);
    }
    private bool TryAddInstanced(GameObject prefab,Matrix4x4 matrix)
    {
        var filter=prefab.GetComponentInChildren<MeshFilter>();var renderer=prefab.GetComponentInChildren<MeshRenderer>();if(filter?.sharedMesh==null||renderer?.sharedMaterials==null)return false;
        Matrix4x4 childLocal=prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;var materials=renderer.sharedMaterials;for(int sub=0;sub<Mathf.Min(filter.sharedMesh.subMeshCount,materials.Length);sub++){if(materials[sub]==null)continue;Material material=GetInstancedMaterial(materials[sub]);var key=new InstanceKey(filter.sharedMesh,material,sub);if(!grassInstances.TryGetValue(key,out var matrices)){matrices=new List<Matrix4x4>();grassInstances.Add(key,matrices);}matrices.Add(matrix*childLocal);}return true;
    }
    private Material GetInstancedMaterial(Material source){if(instancedMaterials.TryGetValue(source,out var material))return material;material=new Material(source){name=$"{source.name} (Tactical Instanced)",enableInstancing=true};instancedMaterials.Add(source,material);return material;}
    private void LateUpdate()
    {
        foreach(var batch in grassBatches)Graphics.DrawMeshInstanced(batch.Key.Mesh,batch.Key.Submesh,batch.Key.Material,batch.Matrices,batch.Matrices.Length,null,ShadowCastingMode.On,true,gameObject.layer);
    }
    private void FinalizeGrassBatches(){grassBatches.Clear();foreach(var source in grassInstances)for(int offset=0;offset<source.Value.Count;offset+=1023){int count=Mathf.Min(1023,source.Value.Count-offset);var matrices=new Matrix4x4[count];source.Value.CopyTo(offset,matrices,0,count);grassBatches.Add(new GrassBatch{Key=source.Key,Matrices=matrices});}grassInstances.Clear();}
    private void CreateFeatures(BattleMap map,BattleCell cell,Vector3 center,BattleBoardLayout layout,BattleBiomeVisualProfile profile)
    {
        if(cell.HasRiver)CreateConnectedFeature("River",map,cell,center,layout,c=>c.HasRiver,.24f,new Color(.05f,.35f,.55f),true);
        if(cell.HasBeach)CreateConnectedFeature("Shoreline",map,cell,center,layout,c=>c.IsWater,.3f,new Color(.72f,.62f,.38f),false);
        if(cell.IsWater){var water=CreateStrip(cell.WaterDepthLevel>1?"Deep Water":"Shallow Water",center+Vector3.up*.045f,new Vector3(BattleBoardLayout.HexRadius*1.5f,.025f,BattleBoardLayout.HexRadius*1.5f),0f,cell.WaterDepthLevel>1?new Color(.02f,.12f,.28f,.82f):new Color(.05f,.38f,.52f,.75f));water.transform.localRotation=Quaternion.identity;}
        if(cell.HasPort&&!HasPrefab(profile?.portPrefabs))CreateStrip("Port Pier",center+new Vector3(.65f,.12f,0f),new Vector3(.9f,.12f,.22f),90f,new Color(.3f,.2f,.12f));
        if(cell.HasHardCover&&!HasPrefab(profile?.hardCoverPrefabs))CreateStrip("Hard Cover",center+new Vector3(-.75f,.16f,.2f),new Vector3(.5f,.3f,.16f),20f,new Color(.28f,.28f,.25f));
        else if(cell.HasSoftCover&&!HasPrefab(profile?.softCoverPrefabs))CreateStrip("Soft Cover",center+new Vector3(-.75f,.1f,.2f),new Vector3(.55f,.18f,.18f),20f,new Color(.18f,.32f,.12f));
    }
    private GameObject CreateStrip(string name,Vector3 position,Vector3 scale,float yaw,Color color){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(transform,false);go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(0f,yaw,0f);go.transform.localScale=scale;var c=go.GetComponent<Collider>();if(c!=null)Destroy(c);var r=go.GetComponent<Renderer>();r.sharedMaterial=featureMaterial;var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);r.SetPropertyBlock(block);spawned.Add(go);return go;}
    private void CreateConnectedFeature(string name,BattleMap map,BattleCell cell,Vector3 center,BattleBoardLayout layout,System.Func<BattleCell,bool> predicate,float width,Color color,bool throughCenter)
    {int made=0;foreach(int n in cell.NeighborIndices??System.Array.Empty<int>()){var other=map.GetCell(n);if(other==null||!predicate(other))continue;Vector3 edge=(layout.GetCellCenter(n)-center);edge.y=0f;edge=edge.normalized*BattleBoardLayout.HexRadius*.9f;Vector3 start=throughCenter?Vector3.zero:edge*.72f;Vector3 delta=edge-start;Vector3 midpoint=center+start+delta*.5f;CreateStrip(name,midpoint,new Vector3(width,.025f,Mathf.Max(.08f,delta.magnitude)),Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,color);made++;}if(made==0)CreateStrip(name,center,new Vector3(width,.025f,BattleBoardLayout.HexRadius*.85f),ConnectionYaw(map,cell,layout,predicate),color);}
    private static float ConnectionYaw(BattleMap map,BattleCell cell,BattleBoardLayout layout,System.Func<BattleCell,bool> predicate){foreach(int n in cell.NeighborIndices??System.Array.Empty<int>()){var other=map.GetCell(n);if(other!=null&&predicate(other)){Vector3 d=layout.GetCellCenter(n)-layout.GetCellCenter(cell.BattleIndex);return Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg;}}return 0f;}
    public static Vector2 FeatureDirection(BattleMap map,BattleCell cell,BattleBoardLayout layout,System.Func<BattleCell,bool> predicate){Vector2 sum=Vector2.zero;int count=0;foreach(int n in cell.NeighborIndices??System.Array.Empty<int>()){var other=map.GetCell(n);if(other==null||!predicate(other))continue;Vector3 d=layout.GetCellCenter(n)-layout.GetCellCenter(cell.BattleIndex);sum+=new Vector2(d.x,d.z).normalized;count++;}return count>0?sum.normalized:Vector2.zero;}
    public static Vector2 RiverDirection(BattleMap map,BattleCell cell,BattleBoardLayout layout){var connected=new List<Vector2>();foreach(int n in cell.NeighborIndices??System.Array.Empty<int>()){var other=map.GetCell(n);if(other==null||!other.HasRiver)continue;Vector3 d=layout.GetCellCenter(n)-layout.GetCellCenter(cell.BattleIndex);connected.Add(new Vector2(d.x,d.z));}if(connected.Count>=2)return(connected[1]-connected[0]).normalized;if(connected.Count==1)return connected[0].normalized;return Vector2.zero;}
    private static GameObject GetPrefab(BattleBiomeVisualProfile p,BattleDecorationPlacement x){GameObject[] a=x.Kind switch{BattleDecorationKind.Tree=>p.treePrefabs,BattleDecorationKind.Grass=>p.grassPrefabs,BattleDecorationKind.Bush=>p.bushPrefabs,BattleDecorationKind.Rock=>p.rockPrefabs,BattleDecorationKind.Prop=>p.environmentalPropPrefabs,BattleDecorationKind.SoftCover=>p.softCoverPrefabs,BattleDecorationKind.HardCover=>p.hardCoverPrefabs,BattleDecorationKind.Port=>p.portPrefabs,_=>null};return a!=null&&x.PrefabIndex>=0&&x.PrefabIndex<a.Length?a[x.PrefabIndex]:null;}
    private static bool HasPrefab(GameObject[] prefabs){if(prefabs!=null)foreach(var prefab in prefabs)if(prefab!=null)return true;return false;}
    private static Material CreateFeatureMaterial(){var shader=Shader.Find("HDRP/Unlit")??Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Color")??Shader.Find("Standard");return new Material(shader){name="Shared Tactical Feature Material"};}
    public void Clear(){foreach(var go in spawned)DestroyTrackedObject(go);spawned.Clear();grassInstances.Clear();grassBatches.Clear();foreach(var material in instancedMaterials.Values)DestroyTrackedObject(material);instancedMaterials.Clear();DestroyTrackedObject(featureMaterial);featureMaterial=null;}
    private static void DestroyTrackedObject(Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    private void OnDestroy()=>Clear();
}
