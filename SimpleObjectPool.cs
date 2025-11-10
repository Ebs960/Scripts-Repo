using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unified object pool for frequently instantiated objects like projectiles, decorations, etc.
/// Consolidates SimpleObjectPool and ProjectilePool functionality.
/// </summary>
public class SimpleObjectPool : MonoBehaviour
{
    public static SimpleObjectPool Instance { get; private set; }

    [Header("Pool Settings")]
    [SerializeField] private int maxPoolSize = 200;
    
    [Header("Default Prefab (Optional)")]
    [Tooltip("Default prefab to use when none is specified (for backward compatibility with ProjectilePool)")]
    public GameObject defaultPrefab;

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
        // Support default prefab (for backward compatibility with ProjectilePool)
        if (prefab == null) prefab = defaultPrefab;
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
            obj = Instantiate(prefab, position, rotation);
            // Attach marker for tracking (for backward compatibility with ProjectilePool)
            var marker = obj.GetComponent<PooledPrefabMarker>();
            if (marker == null) marker = obj.AddComponent<PooledPrefabMarker>();
            marker.originalPrefab = prefab;
        }

        if (obj != null)
        {
            obj.transform.position = position;
            obj.transform.rotation = rotation;
            
            // Reset Rigidbody if present (for projectiles)
            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            
            obj.SetActive(true);
            activeObjects[obj] = prefab;
        }

        return obj;
    }
    
    /// <summary>
    /// Spawn an object (alias for Get, for backward compatibility with ProjectilePool)
    /// </summary>
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        return Get(prefab, position, rotation);
    }

    /// <summary>
    /// Return an object to the pool
    /// </summary>
    public void Return(GameObject obj)
    {
        if (obj == null) return;

        GameObject prefab = null;
        
        // Try to get prefab from activeObjects first (preferred method)
        if (activeObjects.ContainsKey(obj))
        {
            prefab = activeObjects[obj];
            activeObjects.Remove(obj);
        }
        else
        {
            // Fallback: try to get prefab from PooledPrefabMarker (for backward compatibility)
            var marker = obj.GetComponent<PooledPrefabMarker>();
            if (marker != null && marker.originalPrefab != null)
            {
                prefab = marker.originalPrefab;
            }
        }
        
        if (prefab == null)
        {
            // No prefab found, destroy the object
            Destroy(obj);
            return;
        }

        // Reset Rigidbody if present (for projectiles)
        var rb = obj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
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
    
    /// <summary>
    /// Despawn an object (alias for Return, for backward compatibility with ProjectilePool)
    /// </summary>
    public void Despawn(GameObject obj)
    {
        Return(obj);
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

/// <summary>
/// Small marker to store original prefab reference on pooled instances
/// (for backward compatibility with ProjectilePool)
/// </summary>
public class PooledPrefabMarker : MonoBehaviour
{
    public GameObject originalPrefab;
}