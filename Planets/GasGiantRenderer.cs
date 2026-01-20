using UnityEngine;

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class GasGiantRenderer : MonoBehaviour
{
    public GasGiantVisualData visualData;
    public Material gasGiantMaterial;
    public float rotationSpeed = 1f;

    private MeshRenderer mr;

    private void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        if (gasGiantMaterial != null)
        {
            mr.sharedMaterial = gasGiantMaterial;
        }
    }

    private void Update()
    {
        if (visualData == null || mr == null) return;
        // Simple rotation animation
        transform.Rotate(Vector3.up, visualData.rotationSpeed * Time.deltaTime, Space.Self);

        // Update material parameters from visualData
        if (mr.sharedMaterial != null)
        {
            if (visualData.baseGradient != null) mr.sharedMaterial.SetTexture("_BaseGradient", visualData.baseGradient);
            if (visualData.noiseTexture != null) mr.sharedMaterial.SetTexture("_NoiseTex", visualData.noiseTexture);
            mr.sharedMaterial.SetColor("_Tint", visualData.tint);
            mr.sharedMaterial.SetFloat("_BandSharpness", visualData.bandSharpness);
            mr.sharedMaterial.SetFloat("_StormStrength", visualData.stormStrength);
        }
    }

    // Called by PlanetGenerator (data-driven) to enable/disable visuals
    public void SetEnabledForPlanet(bool enabled)
    {
        if (mr != null) mr.enabled = enabled;
        this.enabled = enabled;
    }
}
