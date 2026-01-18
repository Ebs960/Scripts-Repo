using UnityEngine;
public class WaterMeshGenerator : MonoBehaviour
{
    public Material waterMaterial;
    [Tooltip("Height of the water surface above the flat map plane.")]
    public float waterSurfaceElevation = 0.12f;
    [Tooltip("Inset per tile to avoid overlap artifacts between water and land.")]
    public float tileInset = 0.02f;

    [SerializeField] private WaterSurfaceGenerator waterSurfaceGenerator;

    GameObject waterObject;

    public void Generate(PlanetGenerator planetGen)
    {
        if (waterSurfaceGenerator == null)
        {
            waterSurfaceGenerator = GetComponentInChildren<WaterSurfaceGenerator>();
        }

        if (waterSurfaceGenerator != null)
        {
            waterSurfaceGenerator.Generate(planetGen);
            return;
        }

        Debug.LogWarning("[WaterMeshGenerator] WaterSurfaceGenerator not found; HDRP water surfaces will not be generated.");
    }

}
