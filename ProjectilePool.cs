using System.Collections.Generic;
using UnityEngine;

public class ProjectilePool : MonoBehaviour
{
    public static ProjectilePool Instance { get; private set; }

    [Tooltip("Prefab used as a default projectile when none specified. Optional.")]
    public GameObject defaultProjectilePrefab;

    private Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this.gameObject);
        Instance = this;
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) prefab = defaultProjectilePrefab;
        if (prefab == null) return null;

        if (!pools.TryGetValue(prefab, out var q) || q.Count == 0)
        {
            var go = Instantiate(prefab, position, rotation);
            // Attach marker so we can return this instance to the correct pool
            var marker = go.GetComponent<PooledPrefabMarker>();
            if (marker == null) marker = go.AddComponent<PooledPrefabMarker>();
            marker.originalPrefab = prefab;
            return go;
        }

        var obj = q.Dequeue();
        obj.transform.position = position;
        obj.transform.rotation = rotation;
        // Reset common components
    var rb = obj.GetComponent<Rigidbody>(); if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        obj.SetActive(true);
        return obj;
    }

    public void Despawn(GameObject obj)
    {
        if (obj == null) return;
        var prefabComponent = obj.GetComponent<PooledPrefabMarker>();
        GameObject prefab = prefabComponent != null ? prefabComponent.originalPrefab : null;
        if (prefab == null)
        {
            Destroy(obj);
            return;
        }

        if (!pools.TryGetValue(prefab, out var q))
        {
            q = new Queue<GameObject>();
            pools[prefab] = q;
        }

        // Reset common components and deactivate
    var rb = obj.GetComponent<Rigidbody>(); if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        obj.SetActive(false);
        q.Enqueue(obj);
    }
}

// Small marker to store original prefab reference on pooled instances
public class PooledPrefabMarker : MonoBehaviour
{
    public GameObject originalPrefab;
}
