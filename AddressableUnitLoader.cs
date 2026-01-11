using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Generic;

/// <summary>
/// Loads unit prefabs using Unity Addressables (replaces Resources.Load)
/// This dramatically reduces memory usage by loading units only when needed
/// </summary>
public class AddressableUnitLoader : MonoBehaviour
{
    private static AddressableUnitLoader _instance;
    public static AddressableUnitLoader Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("AddressableUnitLoader");
                _instance = go.AddComponent<AddressableUnitLoader>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    // Cache of loaded prefabs (key = unit name/address, value = prefab)
    private Dictionary<string, GameObject> loadedPrefabs = new Dictionary<string, GameObject>();

    // Track loading operations to prevent duplicate loads
    private Dictionary<string, AsyncOperationHandle<GameObject>> loadingOperations = new Dictionary<string, AsyncOperationHandle<GameObject>>();

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
}
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// Initialize Addressables system early (call this from your game initialization)
    /// </summary>
    public static void InitializeAddressables()
    {
        // Addressables auto-initializes on first use, but we can force early initialization
        // This is optional but recommended for better error handling
        var handle = Addressables.InitializeAsync();
        handle.Completed += (op) =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded)
            {
}
            else
            {
                Debug.LogError($"[AddressableUnitLoader] Failed to initialize Addressables: {op.OperationException?.Message ?? "Unknown error"}");
            }
        };
    }

    /// <summary>
    /// Load unit prefab by name/address (async - recommended)
    /// </summary>
    public void LoadUnitPrefab(string unitName, System.Action<GameObject> onComplete)
    {
        if (string.IsNullOrEmpty(unitName))
        {
            Debug.LogWarning("[AddressableUnitLoader] Unit name is null or empty");
            onComplete?.Invoke(null);
            return;
        }

        // Check cache first
        if (loadedPrefabs.TryGetValue(unitName, out GameObject cachedPrefab))
        {
            onComplete?.Invoke(cachedPrefab);
            return;
        }

        // Check if already loading
        if (loadingOperations.TryGetValue(unitName, out AsyncOperationHandle<GameObject> existingHandle))
        {
            // Wait for existing load to complete
            existingHandle.Completed += (operation) =>
            {
                if (operation.Status == AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(operation.Result);
                }
                else
                {
                    onComplete?.Invoke(null);
                }
            };
            return;
        }

        // Start new load operation
        AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(unitName);
        loadingOperations[unitName] = handle;

        handle.Completed += (operation) =>
        {
            loadingOperations.Remove(unitName);

            if (operation.Status == AsyncOperationStatus.Succeeded)
            {
                GameObject prefab = operation.Result;
                loadedPrefabs[unitName] = prefab;
                onComplete?.Invoke(prefab);
            }
            else
            {
                Debug.LogError($"[AddressableUnitLoader] Failed to load unit '{unitName}': {operation.OperationException?.Message ?? "Unknown error"}");
                onComplete?.Invoke(null);
            }
        };
    }

    /// <summary>
    /// Synchronous version (for compatibility with existing code)
    /// WARNING: This blocks the main thread - use async version when possible
    /// </summary>
    public GameObject LoadUnitPrefabSync(string unitName)
    {
        if (string.IsNullOrEmpty(unitName))
            return null;

        // Check cache
        if (loadedPrefabs.TryGetValue(unitName, out GameObject cachedPrefab))
        {
            return cachedPrefab;
        }

        // Load synchronously (blocks until loaded)
        try
        {
            AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(unitName);
            handle.WaitForCompletion();

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                GameObject prefab = handle.Result;
                loadedPrefabs[unitName] = prefab;
                return prefab;
            }
            else
            {
                Debug.LogError($"[AddressableUnitLoader] Failed to load unit '{unitName}': {handle.OperationException?.Message ?? "Unknown error"}");
                Addressables.Release(handle);
                return null;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AddressableUnitLoader] Exception loading unit '{unitName}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if a unit is already loaded (without loading it)
    /// </summary>
    public bool IsUnitLoaded(string unitName)
    {
        return loadedPrefabs.ContainsKey(unitName);
    }

    /// <summary>
    /// Release a specific unit (unload from memory)
    /// </summary>
    public void ReleaseUnit(string unitName)
    {
        if (loadedPrefabs.TryGetValue(unitName, out GameObject prefab))
        {
            loadedPrefabs.Remove(unitName);
            Addressables.Release(prefab);
}
    }

    /// <summary>
    /// Release all loaded units (call when returning to main menu or ending battle)
    /// </summary>
    public void ReleaseAllUnits()
    {
        int count = loadedPrefabs.Count;
        
        foreach (var prefab in loadedPrefabs.Values)
        {
            Addressables.Release(prefab);
        }
        
        loadedPrefabs.Clear();
        loadingOperations.Clear();

        // Force cleanup
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
}

    /// <summary>
    /// Get count of currently loaded units (for debugging)
    /// </summary>
    public int GetLoadedUnitCount()
    {
        return loadedPrefabs.Count;
    }

    private void OnDestroy()
    {
        // Cleanup on destroy
        ReleaseAllUnits();
    }
}

