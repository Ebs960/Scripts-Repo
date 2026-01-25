using UnityEngine;

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class GasGiantRenderer : MonoBehaviour
{
    public GasGiantVisualData visualData;
    public Material gasGiantMaterial;
    public float rotationSpeed = 1f;

    private MeshRenderer mr;
    private bool warnedVisualDataMissing = false;

    private void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        if (gasGiantMaterial != null)
        {
            mr.sharedMaterial = gasGiantMaterial;
        }
        else
        {
            Debug.LogWarning("[GasGiantRenderer] No gasGiantMaterial assigned; renderer will use shared material if present.");
        }
        Debug.Log($"[GasGiantRenderer] Awake on '{gameObject.name}' materialAssigned={(gasGiantMaterial!=null)}");
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

        // Update material parameters from visualData
        if (mr.sharedMaterial != null)
        {
            if (visualData.baseGradient != null) mr.sharedMaterial.SetTexture("_BaseGradient", visualData.baseGradient);
            mr.sharedMaterial.SetColor("_Tint", visualData.tint);
            mr.sharedMaterial.SetFloat("_BandSharpness", visualData.bandSharpness);
            mr.sharedMaterial.SetFloat("_StormStrength", visualData.stormStrength);
            Debug.Log($"[GasGiantRenderer] Applied visualData to material on '{gameObject.name}' tint={visualData.tint} bandSharpness={visualData.bandSharpness} storm={visualData.stormStrength}");
        }
    }

    // Called by PlanetGenerator (data-driven) to enable/disable visuals
    public void SetEnabledForPlanet(bool enabled)
    {
        if (mr != null) mr.enabled = enabled;
        this.enabled = enabled;
        Debug.Log($"[GasGiantRenderer] SetEnabledForPlanet('{gameObject.name}') = {enabled}");
    }
}
