#if UNITY_EDITOR
using UnityEngine;
using System.Text;

/// <summary>
/// Runtime helper: dumps a transform tree and key components (MeshRenderer, SkinnedMeshRenderer, Animator, scripts)
/// Use in Play mode: set `inspectRoot` to the unit (or leave blank), optionally set `nameContains` to match the equipment name,
/// then press F12 (or call the context menu) to print a comprehensive hierarchy and component dump to the Console.
/// This will reveal skinned mesh root bones, bone world positions, child offsets, and any scripts/animators that may move the visual.
/// </summary>
public class EquipDiagnostics : MonoBehaviour
{
    [Tooltip("Root to search under. If empty, this GameObject will be used.")]
    public Transform inspectRoot;
    [Tooltip("If non-empty, only Transforms whose name contains this string (case-insensitive) will be dumped.")]
    public string nameContains = "";
    [Tooltip("Key to press to trigger the dump in Play mode.")]
    public KeyCode dumpKey = KeyCode.F12;

    void Update()
    {
        if (Input.GetKeyDown(dumpKey)) Dump();
    }

    [ContextMenu("Dump Equip Hierarchy")]
    public void Dump()
    {
        Transform root = inspectRoot != null ? inspectRoot : transform;
        if (root == null)
        {
            Debug.LogWarning("[EquipDiagnostics] inspectRoot and this.transform are both null.");
            return;
        }

        if (string.IsNullOrEmpty(nameContains))
        {
            Debug.Log($"[EquipDiagnostics] Dumping hierarchy under: {GetPath(root)}", root);
            DumpTransform(root, 0);
            return;
        }

        var found = false;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = true;
                Debug.Log($"[EquipDiagnostics] Found match: {GetPath(t)}", t);
                DumpTransform(t, 0);
            }
        }
        if (!found) Debug.Log($"[EquipDiagnostics] No child containing '{nameContains}' under {GetPath(root)}.");
    }

    static void DumpTransform(Transform t, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        var line = new StringBuilder();
        line.Append(indentStr);
        line.AppendFormat("T: {0} | path: {1} | localPos:{2} localRot:{3} localScale:{4} | worldPos:{5} worldRot:{6}",
            t.name, GetPath(t), t.localPosition, t.localRotation.eulerAngles, t.localScale, t.position, t.rotation.eulerAngles);
        Debug.Log(line.ToString(), t);

        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null)
            Debug.Log($"  MeshRenderer: bounds center={mr.bounds.center} extents={mr.bounds.extents}", t);

        var smr = t.GetComponent<SkinnedMeshRenderer>();
        if (smr != null)
        {
            Debug.Log($"  SkinnedMeshRenderer: rootBone={(smr.rootBone!=null?smr.rootBone.name:"null")} bones={smr.bones?.Length ?? 0}", t);
            if (smr.rootBone != null)
                Debug.Log($"    rootBone worldPos={smr.rootBone.position}", smr.rootBone);
            if (smr.bones != null)
            {
                foreach (var b in smr.bones)
                    if (b != null) Debug.Log($"    bone: {b.name} worldPos={b.position}", b);
            }
        }

        var anim = t.GetComponent<Animator>();
        if (anim != null)
            Debug.Log($"  Animator: controller={(anim.runtimeAnimatorController!=null?anim.runtimeAnimatorController.name:"null")} updateMode={anim.updateMode} applyRootMotion={anim.applyRootMotion}", t);

        var scripts = t.GetComponents<MonoBehaviour>();
        foreach (var s in scripts)
            if (s != null)
                Debug.Log($"  Script: {s.GetType().FullName}", t);

        foreach (Transform child in t)
            DumpTransform(child, indent + 1);
    }

    static string GetPath(Transform t)
    {
        if (t == null) return "<null>";
        var parts = new System.Collections.Generic.List<string>();
        var cur = t;
        while (cur != null)
        {
            parts.Insert(0, cur.name);
            cur = cur.parent;
        }
        return string.Join("/", parts.ToArray());
    }
}
#endif
