using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple object pool for frequently instantiated objects like projectiles
/// </summary>
public class SimpleObjectPool : MonoBehaviour
{
    public static SimpleObjectPool Instance { get; private set; }

    [Header("Pool Settings")]
    [SerializeField] private int maxPoolSize = 200;

    private Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
    private Dictionary<GameObject, GameObject> activeObjects = new Dictionary<GameObject, GameObject>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Get an object from the pool or create a new one
    /// </summary>
    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        GameObject obj = null;

        // Check if we have a pool for this prefab
        if (pools.ContainsKey(prefab) && pools[prefab].Count > 0)
        {
            obj = pools[prefab].Dequeue();
        }
        else
        {
            // Create new object
            obj = Instantiate(prefab);
        }

        if (obj != null)
        {
            obj.transform.position = position;
            obj.transform.rotation = rotation;
            obj.SetActive(true);
            activeObjects[obj] = prefab;
        }

        return obj;
    }

    /// <summary>
    /// Return an object to the pool
    /// </summary>
    public void Return(GameObject obj)
    {
        if (obj == null) return;

        if (activeObjects.ContainsKey(obj))
        {
            GameObject prefab = activeObjects[obj];
            activeObjects.Remove(obj);

            // Deactivate the object
            obj.SetActive(false);

            // Add to pool if we have space
            if (!pools.ContainsKey(prefab))
            {
                pools[prefab] = new Queue<GameObject>();
            }

            if (pools[prefab].Count < maxPoolSize)
            {
                pools[prefab].Enqueue(obj);
            }
            else
            {
                // Pool is full, destroy the object
                Destroy(obj);
            }
        }
    }

    /// <summary>
    /// Pre-populate the pool with objects
    /// </summary>
    public void PrePopulate(GameObject prefab, int count)
    {
        if (prefab == null) return;

        if (!pools.ContainsKey(prefab))
        {
            pools[prefab] = new Queue<GameObject>();
        }

        for (int i = 0; i < count; i++)
        {
            GameObject obj = Instantiate(prefab);
            obj.SetActive(false);
            pools[prefab].Enqueue(obj);
        }
    }

    /// <summary>
    /// Clear all pools
    /// </summary>
    public void ClearAllPools()
    {
        foreach (var pool in pools.Values)
        {
            while (pool.Count > 0)
            {
                GameObject obj = pool.Dequeue();
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
        }
        pools.Clear();
        activeObjects.Clear();
    }

    /// <summary>
    /// Get pool statistics for debugging
    /// </summary>
    public Dictionary<string, int> GetPoolStats()
    {
        var stats = new Dictionary<string, int>();
        foreach (var kvp in pools)
        {
            stats[kvp.Key.name] = kvp.Value.Count;
        }
        return stats;
    }
}