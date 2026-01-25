using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

// Controller that maps a GasGiantVisualData to an HDRP Volumetric Clouds VolumeProfile.
// Uses reflection so the script compiles even if HDRP types vary across versions.
[RequireComponent(typeof(Volume))]
public class GasGiantVolumetricController : MonoBehaviour
{
    [Tooltip("Scriptable object with tint / band / storm values to apply")]
    public GasGiantVisualData visualData;

    [Tooltip("If set, this Volume's profile will be instantiated per-planet so it can be modified at runtime.")]
    public bool instantiateProfile = true;

    [Tooltip("Time to fade in/out volume weight when enabling/disabling")]
    public float fadeDuration = 1f;

    Volume volume;
    VolumeProfile profileInstance;
    VolumeComponent cloudsComponent; // obtained via reflection-friendly lookup

    Coroutine fadeCoroutine;

    void Awake()
    {
        volume = GetComponent<Volume>();
        if (volume == null) return;

        if (instantiateProfile && volume.profile != null)
        {
            profileInstance = Instantiate(volume.profile);
            volume.profile = profileInstance;
        }
        else
        {
            profileInstance = volume.profile;
        }

        // Try to find a Volumetric Clouds component in the profile using type name.
        // We keep everything via VolumeComponent (base type) and use reflection to set values.
        Type cloudsType = Type.GetType("UnityEngine.Rendering.HighDefinition.VolumetricClouds, Unity.RenderPipelines.HighDefinition.Runtime")
                        ?? Type.GetType("UnityEngine.Rendering.VolumeComponent, Unity.RenderPipelines.Core.Runtime");

        if (profileInstance != null && cloudsType != null)
        {
            // Use VolumeProfile.TryGet(Type, out VolumeComponent)
            MethodInfo tryGetMethod = typeof(VolumeProfile).GetMethod("TryGet", new Type[] { typeof(Type), typeof(VolumeComponent).MakeByRefType() });
            if (tryGetMethod != null)
            {
                object[] args = new object[] { cloudsType, null };
                try
                {
                    bool ok = (bool)tryGetMethod.Invoke(profileInstance, args);
                    if (ok) cloudsComponent = args[1] as VolumeComponent;
                }
                catch { /* fallback: leave cloudsComponent null */ }
            }
        }

        Debug.Log($"[GasGiantVolumetricController] Awake: instantiateProfile={instantiateProfile} profileInstance={(profileInstance!=null)} cloudsComponent={(cloudsComponent!=null)}");

        ApplyVisualData(visualData);
    }

    // Public API: apply fields from GasGiantVisualData into the Volumetric Clouds component where possible.
    public void ApplyVisualData(GasGiantVisualData data)
    {
        visualData = data;
        if (visualData == null)
        {
            Debug.LogWarning("[GasGiantVolumetricController] ApplyVisualData called with null visualData.");
            return;
        }

        if (cloudsComponent == null)
        {
            Debug.LogWarning("[GasGiantVolumetricController] No Volumetric Clouds component found on profile; skipping volumetric mapping.");
            return;
        }

        Debug.Log($"[GasGiantVolumetricController] Applying visual data: shape3D={(visualData.shapeNoise3D!=null)} detail3D={(visualData.detailNoise3D!=null)} flowMap={(visualData.flowMap!=null)}");

        // Safe reflection helper: tries to set a named field.value or property.value on the clouds component
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
                            Debug.Log($"[GasGiantVolumetricController] Set field '{fieldName}' via VolumeParameter.value");
                            return true;
                        }
                    }
                }

                // Try property path as fallback (some versions expose properties)
                PropertyInfo p = cloudsComponent.GetType().GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(cloudsComponent, value);
                    Debug.Log($"[GasGiantVolumetricController] Set property '{fieldName}' on cloudsComponent");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GasGiantVolumetricController] Failed to set '{fieldName}': {ex.Message}");
            }
            return false;
        }

        // Common fields that many HDRP versions expose on VolumetricClouds as VolumeParameters
        TrySet("albedo", visualData.tint);
        TrySet("density", visualData.bandSharpness); // best-effort: density / coverage
        TrySet("meanHeight", 0f); // keep meanHeight at 0 unless tweaked
        TrySet("baseHeight", 0f);
        TrySet("layerThickness", 0.5f);

        // Try to set texture fields where available.
        // Require Texture3D inputs for real volumetric sampling. If absent, we skip (the controller will do nothing).
        if (visualData.shapeNoise3D != null) TrySet("shapeNoise", visualData.shapeNoise3D);
        if (visualData.detailNoise3D != null) TrySet("detailNoise", visualData.detailNoise3D);

        // Optional: flow map for advection / band animation
        if (visualData.flowMap != null) TrySet("flowMap", visualData.flowMap);

        // Hook for storm strength: many HDRP clouds expose a "weather" or "erosion"-like parameter; try common names
        TrySet("erosion", visualData.stormStrength);
        TrySet("weather", visualData.stormStrength);

        // You can extend this mapping as you discover the exact parameter names for your HDRP version.
    }

    public void SetEnabledSmooth(bool enabled)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeVolumeWeight(enabled ? 1f : 0f, fadeDuration));
    }

    IEnumerator FadeVolumeWeight(float target, float duration)
    {
        if (volume == null) yield break;
        float start = volume.weight;
        float t = 0f;
        if (duration <= 0f)
        {
            volume.weight = target;
            yield break;
        }
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            volume.weight = Mathf.Lerp(start, target, t);
            yield return null;
        }
        volume.weight = target;
    }
}
