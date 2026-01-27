using UnityEngine;

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class GasGiantRenderer : MonoBehaviour
{
    public GasGiantVisualData visualData;
    public Material gasGiantMaterial;
    public float rotationSpeed = 1f;

    private MeshRenderer mr;
    private bool warnedVisualDataMissing = false;
    private MaterialPropertyBlock propertyBlock;
    // Last-applied values to avoid redundant per-frame material updates and logs
    private Color lastTint = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
    private float lastBandSharpness = float.NaN;
    private float lastStormStrength = float.NaN;
    private Texture lastBaseGradient = null;

    private void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();
        if (gasGiantMaterial != null)
        {
            mr.sharedMaterial = gasGiantMaterial;
        }
        else
        {
            Debug.LogWarning("[GasGiantRenderer] No gasGiantMaterial assigned; renderer will use shared material if present.");
        }
        Debug.Log($"[GasGiantRenderer] Awake on '{gameObject.name}' materialAssigned={(gasGiantMaterial!=null)}");

        // Apply visual data once at startup if available
        ApplyVisualDataToMaterial(force: true);
    }

    private void Update()
    {
        if (visualData == null || mr == null)
        {
            if (!warnedVisualDataMissing && mr != null)
            {
                Debug.LogWarning("[GasGiantRenderer] visualData is null; gas giant will not update material parameters.");
                warnedVisualDataMissing = true;
            }
            return;
        }
        // Simple rotation animation
        transform.Rotate(Vector3.up, visualData.rotationSpeed * Time.deltaTime, Space.Self);

        // Apply material parameters only when they change to avoid per-frame updates/logs
        ApplyVisualDataToMaterial(force: false);
    }

    private void ApplyVisualDataToMaterial(bool force = false)
    {
        if (visualData == null || mr == null || mr.sharedMaterial == null) return;

        bool changed = force;

        // Detect changes
        if (lastBaseGradient != visualData.baseGradient) changed = true;
        if (!ColorEquals(lastTint, visualData.tint)) changed = true;
        if (!Mathf.Approximately(lastBandSharpness, visualData.bandSharpness)) changed = true;
        if (!Mathf.Approximately(lastStormStrength, visualData.stormStrength)) changed = true;

        if (!changed) return;

        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        mr.GetPropertyBlock(propertyBlock);
        if (visualData.baseGradient != null) propertyBlock.SetTexture("_BaseGradient", visualData.baseGradient);
        propertyBlock.SetColor("_Tint", visualData.tint);
        propertyBlock.SetFloat("_BandSharpness", visualData.bandSharpness);
        propertyBlock.SetFloat("_StormStrength", visualData.stormStrength);
        mr.SetPropertyBlock(propertyBlock);

        // Update last-applied values
        lastBaseGradient = visualData.baseGradient;
        lastTint = visualData.tint;
        lastBandSharpness = visualData.bandSharpness;
        lastStormStrength = visualData.stormStrength;

        Debug.Log($"[GasGiantRenderer] Applied visualData to material on '{gameObject.name}' tint={visualData.tint} bandSharpness={visualData.bandSharpness} storm={visualData.stormStrength}");
    }

    private static bool ColorEquals(Color a, Color b)
    {
        const float eps = 0.0001f;
        return Mathf.Abs(a.r - b.r) <= eps && Mathf.Abs(a.g - b.g) <= eps && Mathf.Abs(a.b - b.b) <= eps && Mathf.Abs(a.a - b.a) <= eps;
    }

    // Called by PlanetGenerator (data-driven) to enable/disable visuals
    public void SetEnabledForPlanet(bool enabled)
    {
        if (mr != null) mr.enabled = enabled;
        this.enabled = enabled;
        Debug.Log($"[GasGiantRenderer] SetEnabledForPlanet('{gameObject.name}') = {enabled}");
    }
}
