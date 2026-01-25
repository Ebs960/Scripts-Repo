using System.Collections.Generic;
using UnityEngine;

// Simple spawner + pool that enables a Volume prefab for planets within range.
public class GasGiantVolumeSpawner : MonoBehaviour
{
    [Tooltip("Prefab containing a Volume component and GasGiantVolumetricController")]
    public GameObject volumePrefab;

    [Tooltip("Maximum camera distance at which the volumetric layer will be created/enabled")]
    public float spawnDistance = 20000f;

    [Tooltip("Number of pooled instances to keep ready to avoid GC spikes")]
    public int poolSize = 2;

    Camera cam;
    List<GameObject> pool = new List<GameObject>();
    GameObject activeInstance;

    void Start()
    {
        cam = Camera.main;
        // Pre-warm pool
        for (int i = 0; i < poolSize; i++)
        {
            var go = Instantiate(volumePrefab);
            go.SetActive(false);
            pool.Add(go);
        }
        Debug.Log($"[GasGiantVolumeSpawner] Pool initialized size={pool.Count} spawnDistance={spawnDistance}");
    }

    void Update()
    {
        if (cam == null || volumePrefab == null) return;
        float dist = Vector3.Distance(cam.transform.position, transform.position);
        if (dist <= spawnDistance)
        {
            EnsureInstanceActive();
        }
        else
        {
            EnsureInstanceInactive();
        }
    }

    void EnsureInstanceActive()
    {
        if (activeInstance != null && activeInstance.activeSelf) return;
        GameObject inst = null;
        if (pool.Count > 0)
        {
            inst = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            Debug.Log("[GasGiantVolumeSpawner] Reusing pooled instance.");
        }
        else
        {
            inst = Instantiate(volumePrefab);
            Debug.Log("[GasGiantVolumeSpawner] Instantiated new volume instance (pool empty).");
        }

        activeInstance = inst;
        activeInstance.transform.SetParent(transform, false);
        activeInstance.transform.localPosition = Vector3.zero;
        activeInstance.transform.localRotation = Quaternion.identity;
        activeInstance.transform.localScale = Vector3.one;
        activeInstance.SetActive(true);

        var controller = activeInstance.GetComponent<GasGiantVolumetricController>();
        if (controller != null)
        {
            // Apply visual data from planet's GasGiantRenderer if available
            var renderer = GetComponentInChildren<GasGiantRenderer>();
            if (renderer != null && renderer.visualData != null)
            {
                controller.ApplyVisualData(renderer.visualData);
                controller.SetEnabledSmooth(true);
                Debug.Log("[GasGiantVolumeSpawner] Applied visualData to volumetric controller and enabled.");
            }
            else
            {
                Debug.LogWarning("[GasGiantVolumeSpawner] No GasGiantRenderer or visualData found to apply to controller.");
            }
        }
    }

    void EnsureInstanceInactive()
    {
        if (activeInstance == null) return;
        var controller = activeInstance.GetComponent<GasGiantVolumetricController>();
        if (controller != null)
        {
            controller.SetEnabledSmooth(false);
        }
        // return to pool after fadeDuration
        float d = controller != null ? controller.fadeDuration : 0f;
        StartCoroutine(ReturnToPoolAfter(activeInstance, d));
        Debug.Log($"[GasGiantVolumeSpawner] Deactivating active instance, will return to pool after {d}s");
        activeInstance = null;
    }

    System.Collections.IEnumerator ReturnToPoolAfter(GameObject go, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay + 0.1f);
        go.SetActive(false);
        pool.Add(go);
        Debug.Log($"[GasGiantVolumeSpawner] Returned instance to pool. Pool size={pool.Count}");
    }
}
