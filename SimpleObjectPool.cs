using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple object pool for frequently instantiated objects like decorations
/// Reduces memory allocation and improves performance
/// </summary>
public class SimpleObjectPool : MonoBehaviour
{
    public static SimpleObjectPool Instance { get; private set; }
    
    private Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
    private Dictionary<GameObject, GameObject> activeObjects = new Dictionary<GameObject, GameObject>(); // Track which prefab each active object came from
    
    [Header("Pool Settings")]
    [SerializeField] private int defaultPoolSize = 50;
    [SerializeField] private int maxPoolSize = 200;
    
    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }
    
    /// <summary>
    /// Get an object from the pool or create a new one
    /// </summary>
    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null) return null;
        
        // Initialize pool if it doesn't exist
        if (!pools.ContainsKey(prefab))
        {
            pools[prefab] = new Queue<GameObject>();
            PrewarmPool(prefab, defaultPoolSize);
        }
        
        GameObject obj;
        var pool = pools[prefab];
        
        if (pool.Count > 0)
        {
            // Reuse from pool
            obj = pool.Dequeue();
            obj.transform.position = position;
            obj.transform.rotation = rotation;
            obj.transform.SetParent(parent);
            obj.SetActive(true);
        }
        else
        {
            // Create new if pool is empty
            obj = Instantiate(prefab, position, rotation, parent);
        }
        
        // Track which prefab this came from
        activeObjects[obj] = prefab;
        
        return obj;
    }
    
    /// <summary>
    /// Return an object to the pool
    /// </summary>
    public void Return(GameObject obj)
    {
        if (obj == null) return;
        
        if (activeObjects.TryGetValue(obj, out GameObject prefab))
        {
            activeObjects.Remove(obj);
            
            var pool = pools[prefab];
            if (pool.Count < maxPoolSize)
            {
                obj.SetActive(false);
                obj.transform.SetParent(transform); // Parent to pool for organization
                pool.Enqueue(obj);
            }
            else
            {
                // Pool is full, destroy the object
                Destroy(obj);
            }
        }
        else
        {
            // Object not tracked, just destroy it
            Destroy(obj);
        }
    }
    
    /// <summary>
    /// Pre-create objects to avoid frame drops during gameplay
    /// </summary>
    private void PrewarmPool(GameObject prefab, int count)
    {
        var pool = pools[prefab];
        for (int i = 0; i < count; i++)
        {
            GameObject obj = Instantiate(prefab, transform);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }
    }
    
    /// <summary>
    /// Clear all pools (useful for scene transitions)
    /// </summary>
    public void ClearAllPools()
    {
        foreach (var pool in pools.Values)
        {
            while (pool.Count > 0)
            {
                var obj = pool.Dequeue();
                if (obj != null) Destroy(obj);
            }
        }
        pools.Clear();
        activeObjects.Clear();
    }
    
    /// <summary>
    /// Return all active objects of a specific prefab type to pool
    /// </summary>
    public void ReturnAllOfType(GameObject prefab)
    {
        var objectsToReturn = new List<GameObject>();
        
        foreach (var kvp in activeObjects)
        {
            if (kvp.Value == prefab)
            {
                objectsToReturn.Add(kvp.Key);
            }
        }
        
        foreach (var obj in objectsToReturn)
        {
            Return(obj);
        }
    }
    
    void OnDestroy()
    {
        ClearAllPools();
    }
}