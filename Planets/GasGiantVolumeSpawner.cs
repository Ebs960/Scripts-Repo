using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages volumetric cloud effects for gas giant planets.
/// Activation is controlled via SetVolumetricsEnabled() from PlayerUI, NOT by camera distance.
/// Only activates on planets that have an Atmosphere layer and no Surface layer.
/// </summary>
public class GasGiantVolumeSpawner : MonoBehaviour
{
    [Tooltip("Prefab containing a Volume component")]
    public GameObject volumePrefab;

    [Tooltip("Number of pooled instances to keep ready to avoid GC spikes")]
    public int poolSize = 2;

    [Tooltip("Enable verbose logging for debugging gas giant volumetrics")]
    public bool verboseLogs = false;

    [Tooltip("If set, the spawned Volume.profile will be instantiated per-instance for runtime modification")]
    public bool instantiateProfile = true;

    [Tooltip("Fade duration when enabling/disabling volumetrics")]
    public float fadeDuration = 1f;

    List<GameObject> pool = new List<GameObject>();
    GameObject activeInstance;
    
    /// <summary>
    /// Whether volumetrics are currently enabled (set via UI toggle)
    /// </summary>
    public bool IsVolumetricsEnabled => activeInstance != null && activeInstance.activeSelf;

    void Start()
    {
        // Pre-warm pool
        if (volumePrefab != null)
        {
            for (int i = 0; i < poolSize; i++)
            {
                var go = Instantiate(volumePrefab, transform);
                go.SetActive(false);
                pool.Add(go);
            }
            if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.Log($"[GasGiantVolumeSpawner] Pool initialized size={pool.Count}");
        }
        else
        {
            if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.LogWarning("[GasGiantVolumeSpawner] volumePrefab is not assigned. Volumetric clouds will not work.");
        }
    }

    // NOTE: Update() removed - volumetrics are now controlled via SetVolumetricsEnabled() from PlayerUI

    /// <summary>
    /// Enable or disable volumetric clouds. Called from PlayerUI when toggling atmosphere layer.
    /// </summary>
    /// <param name="enabled">True to show volumetric clouds, false to hide them</param>
    public void SetVolumetricsEnabled(bool enabled)
    {
        if (enabled)
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
            if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.Log("[GasGiantVolumeSpawner] Reusing pooled instance.");
        }
        else
        {
            inst = Instantiate(volumePrefab, transform);
            if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.Log("[GasGiantVolumeSpawner] Instantiated new volume instance (pool empty).");
        }

        activeInstance = inst;
        activeInstance.transform.SetParent(transform, false);
        activeInstance.transform.localPosition = Vector3.zero;
        activeInstance.transform.localRotation = Quaternion.identity;
        activeInstance.transform.localScale = Vector3.one;
        activeInstance.SetActive(true);

        // Apply visual data from planet's GasGiantRenderer directly onto the Volume (embedded mapping)
        var renderer = GetComponentInChildren<GasGiantRenderer>();
        if (renderer != null && renderer.visualData != null)
        {
            var vol = activeInstance.GetComponent<Volume>();
            if (vol != null)
            {
                if (instantiateProfile && vol.profile != null)
                {
                    vol.profile = Instantiate(vol.profile);
                }
                ApplyVisualDataToVolume(vol, renderer.visualData);
                StartFadeVolume(vol, 1f, fadeDuration);
                if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.Log($"[GasGiantVolumeSpawner] Applied visualData to Volume and enabled.");
            }
            else
            {
                if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.LogWarning("[GasGiantVolumeSpawner] No Volume component found on volumePrefab instance.");
            }
        }
        else
        {
            if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.LogWarning("[GasGiantVolumeSpawner] No GasGiantRenderer or visualData found to apply to volumetrics.");
        }
    }

    void EnsureInstanceInactive()
    {
        if (activeInstance == null) return;
        var vol = activeInstance.GetComponent<Volume>();
        if (vol != null)
        {
            StartFadeVolume(vol, 0f, fadeDuration);
        }
        // return to pool after fadeDuration
        StartCoroutine(ReturnToPoolAfter(activeInstance, fadeDuration));
        if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.Log($"[GasGiantVolumeSpawner] Deactivating active instance, will return to pool after {fadeDuration}s");
        activeInstance = null;
    }

    System.Collections.IEnumerator ReturnToPoolAfter(GameObject go, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay + 0.1f);
        go.SetActive(false);
        pool.Add(go);
        if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.Log($"[GasGiantVolumeSpawner] Returned instance to pool. Pool size={pool.Count}");
    }

    // Fade helper: smoothly adjust Volume.weight
    void StartFadeVolume(Volume vol, float target, float duration)
    {
        if (vol == null) return;
        StopCoroutine(FadeVolumeWeightCoroutine(vol, target, duration));
        StartCoroutine(FadeVolumeWeightCoroutine(vol, target, duration));
    }

    System.Collections.IEnumerator FadeVolumeWeightCoroutine(Volume vol, float target, float duration)
    {
        if (vol == null) yield break;
        float start = vol.weight;
        float t = 0f;
        if (duration <= 0f)
        {
            vol.weight = target;
            yield break;
        }
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            vol.weight = Mathf.Lerp(start, target, t);
            yield return null;
        }
        vol.weight = target;
    }

    // Map GasGiantVisualData onto a Volume's VolumeProfile/VolumeComponent using reflection (best-effort)
    void ApplyVisualDataToVolume(Volume vol, GasGiantVisualData data)
    {
        if (vol == null || data == null) return;
        var profile = vol.profile;
        if (profile == null) return;

        // Try to find a Volumetric Clouds VolumeComponent in the profile
        Type cloudsType = Type.GetType("UnityEngine.Rendering.HighDefinition.VolumetricClouds, Unity.RenderPipelines.HighDefinition.Runtime")
                        ?? Type.GetType("UnityEngine.Rendering.VolumeComponent, Unity.RenderPipelines.Core.Runtime");
        if (cloudsType == null) return;

        // Use TryGet(Type, out VolumeComponent)
        MethodInfo tryGetMethod = typeof(VolumeProfile).GetMethod("TryGet", new Type[] { typeof(Type), typeof(VolumeComponent).MakeByRefType() });
        if (tryGetMethod == null) return;

        object[] args = new object[] { cloudsType, null };
        try
        {
            bool ok = (bool)tryGetMethod.Invoke(profile, args);
            if (!ok) return;
        }
        catch { return; }

        var cloudsComponent = args[1] as VolumeComponent;
        if (cloudsComponent == null) return;

        bool TrySet(string fieldName, object value)
        {
            try
            {
                FieldInfo f = cloudsComponent.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    object param = f.GetValue(cloudsComponent);
                    if (param != null)
                    {
                        PropertyInfo pv = param.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pv != null && pv.CanWrite)
                        {
                            pv.SetValue(param, value);
                            if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.Log($"[GasGiantVolumeSpawner] Set field '{fieldName}' via VolumeParameter.value");
                            return true;
                        }
                    }
                }

                PropertyInfo p = cloudsComponent.GetType().GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(cloudsComponent, value);
                    if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.Log($"[GasGiantVolumeSpawner] Set property '{fieldName}' on cloudsComponent");
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (verboseLogs || (GasGiantDebug.Instance != null && GasGiantDebug.Instance.verboseLogs)) Debug.LogWarning($"[GasGiantVolumeSpawner] Failed to set '{fieldName}': {ex.Message}");
            }
            return false;
        }

        // Apply common fields
        TrySet("albedo", data.tint);
        TrySet("density", data.bandSharpness);
        TrySet("meanHeight", 0f);
        TrySet("baseHeight", 0f);
        TrySet("layerThickness", 0.5f);
        if (data.shapeNoise3D != null) TrySet("shapeNoise", data.shapeNoise3D);
        if (data.detailNoise3D != null) TrySet("detailNoise", data.detailNoise3D);
        if (data.flowMap != null) TrySet("flowMap", data.flowMap);
        TrySet("erosion", data.stormStrength);
        TrySet("weather", data.stormStrength);
    }
}
