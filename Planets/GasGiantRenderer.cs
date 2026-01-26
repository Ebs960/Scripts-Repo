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
        // IMPORTANT: Do not mutate shared material assets per-frame. Use a per-renderer MaterialPropertyBlock
        // so multiple gas giants sharing a material don't stomp each other's parameters.
        if (mr.sharedMaterial != null)
        {
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            mr.GetPropertyBlock(propertyBlock);
            if (visualData.baseGradient != null) propertyBlock.SetTexture("_BaseGradient", visualData.baseGradient);
            propertyBlock.SetColor("_Tint", visualData.tint);
            propertyBlock.SetFloat("_BandSharpness", visualData.bandSharpness);
            propertyBlock.SetFloat("_StormStrength", visualData.stormStrength);
            mr.SetPropertyBlock(propertyBlock);
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
