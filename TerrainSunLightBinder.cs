using UnityEngine;

/// <summary>
/// Mirrors the active scene directional light onto the shared biome terrain material
/// so the shader fallback lighting path responds to the same sun as the scene.
/// </summary>
[ExecuteAlways]
public class TerrainSunLightBinder : MonoBehaviour
{
    [SerializeField] private HexMapChunkManager chunkManager;
    [SerializeField] private Light directionalLight;
    [SerializeField] private float fallbackAmbient = 0.35f;

    private void OnEnable()
    {
        BindSunLight();
    }

    private void LateUpdate()
    {
        BindSunLight();
    }

    private void OnValidate()
    {
        BindSunLight();
    }

    private void BindSunLight()
    {
        if (chunkManager == null)
        {
            chunkManager = FindAnyObjectByType<HexMapChunkManager>();
        }

        if (directionalLight == null || directionalLight.type != LightType.Directional)
        {
            directionalLight = FindDirectionalLight();
        }

        Material sharedMaterial = chunkManager != null ? chunkManager.SharedMaterial : null;
        if (sharedMaterial == null || directionalLight == null)
        {
            return;
        }

        Vector3 sunDirectionWS = -directionalLight.transform.forward;
        sharedMaterial.SetVector("_FallbackSunDirectionWS", new Vector4(sunDirectionWS.x, sunDirectionWS.y, sunDirectionWS.z, 0f));
        sharedMaterial.SetColor("_FallbackSunColor", directionalLight.color);
        sharedMaterial.SetFloat("_FallbackSunIntensity", directionalLight.intensity);
        sharedMaterial.SetFloat("_FallbackAmbient", fallbackAmbient);
    }

    private static Light FindDirectionalLight()
    {
        Light[] lights = FindObjectsByType<Light>();
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light != null && light.type == LightType.Directional && light.enabled && light.gameObject.activeInHierarchy)
            {
                return light;
            }
        }

        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light != null && light.type == LightType.Directional)
            {
                return light;
            }
        }

        return null;
    }
}
