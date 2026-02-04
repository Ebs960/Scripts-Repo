using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight pooling for equipment visual prefabs.
/// Motivation: equipping/unequipping can cause lots of Instantiate/Destroy churn (GC + spikes) when many units update visuals.
/// </summary>
public static class EquipmentVisualPool
{
    private static readonly Dictionary<int, Stack<GameObject>> _poolByPrefabId = new Dictionary<int, Stack<GameObject>>();
    private static Transform _poolRoot;

    private static Transform PoolRoot
    {
        get
        {
            if (_poolRoot != null) return _poolRoot;
            var go = new GameObject("__EquipmentVisualPool");
            go.hideFlags = HideFlags.DontSave;
            Object.DontDestroyOnLoad(go);
            _poolRoot = go.transform;
            return _poolRoot;
        }
    }

    public static bool IsPooledInstance(GameObject instance)
    {
        if (instance == null) return false;
        return instance.GetComponent<PooledEquipmentVisual>() != null;
    }

    public static GameObject Acquire(GameObject prefab)
    {
        if (prefab == null) return null;
        int prefabId = prefab.GetInstanceID();

        if (_poolByPrefabId.TryGetValue(prefabId, out var stack))
        {
            while (stack.Count > 0)
            {
                var inst = stack.Pop();
                if (inst == null) continue;

                var marker = inst.GetComponent<PooledEquipmentVisual>();
                if (marker == null)
                {
                    // Not expected; treat as non-pooled.
                    Object.Destroy(inst);
                    continue;
                }

                inst.SetActive(true);
                marker.ResetToAuthoredLocal();
                return inst;
            }
        }

        // Create new instance and tag it for pooling.
        var created = Object.Instantiate(prefab);
        var m = created.GetComponent<PooledEquipmentVisual>();
        if (m == null) m = created.AddComponent<PooledEquipmentVisual>();
        m.prefabId = prefabId;
        m.CaptureAuthoredLocal();
        return created;
    }

    public static void Release(GameObject instance)
    {
        if (instance == null) return;

        var marker = instance.GetComponent<PooledEquipmentVisual>();
        if (marker == null)
        {
            // Not a pooled instance; fall back to destroy.
            Object.Destroy(instance);
            return;
        }

        if (!_poolByPrefabId.TryGetValue(marker.prefabId, out var stack))
        {
            stack = new Stack<GameObject>();
            _poolByPrefabId[marker.prefabId] = stack;
        }

        // Detach and hide.
        instance.transform.SetParent(PoolRoot, worldPositionStays: false);
        marker.ResetToAuthoredLocal();
        instance.SetActive(false);
        stack.Push(instance);
    }

    /// <summary>
    /// Optional manual cleanup (e.g., for memory stress testing).
    /// </summary>
    public static void ClearAll()
    {
        foreach (var kv in _poolByPrefabId)
        {
            var stack = kv.Value;
            if (stack == null) continue;
            while (stack.Count > 0)
            {
                var go = stack.Pop();
                if (go != null) Object.Destroy(go);
            }
        }
        _poolByPrefabId.Clear();

        if (_poolRoot != null)
        {
            Object.Destroy(_poolRoot.gameObject);
            _poolRoot = null;
        }
    }
}

/// <summary>
/// Marker component that stores the prefab-authored local transform so pooled equipment visuals can be reset cleanly.
/// </summary>
public sealed class PooledEquipmentVisual : MonoBehaviour
{
    [HideInInspector] public int prefabId;

    private Vector3 _authoredLocalPosition;
    private Quaternion _authoredLocalRotation;
    private Vector3 _authoredLocalScale;
    private bool _captured;

    public void CaptureAuthoredLocal()
    {
        _authoredLocalPosition = transform.localPosition;
        _authoredLocalRotation = transform.localRotation;
        _authoredLocalScale = transform.localScale;
        _captured = true;
    }

    public void ResetToAuthoredLocal()
    {
        if (!_captured) CaptureAuthoredLocal();
        transform.localPosition = _authoredLocalPosition;
        transform.localRotation = _authoredLocalRotation;
        transform.localScale = _authoredLocalScale;
    }
}

