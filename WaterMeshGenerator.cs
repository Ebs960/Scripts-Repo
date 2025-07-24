using UnityEngine;

public class WaterMeshGenerator : MonoBehaviour
{
    public Material waterMaterial;
    [Tooltip("Height of the water surface above the planet radius.")]
    public float waterSurfaceElevation = 0.12f;

    GameObject waterObject;

    public void Generate(float planetRadius)
    {
        if (waterObject != null)
        {
            Destroy(waterObject);
        }

        waterObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        waterObject.name = "WaterMesh";
        waterObject.transform.SetParent(transform, false);

        float radius = planetRadius + waterSurfaceElevation;
        waterObject.transform.localScale = Vector3.one * radius * 2f;

        var renderer = waterObject.GetComponent<MeshRenderer>();
        if (waterMaterial != null)
            renderer.material = waterMaterial;

        var collider = waterObject.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
    }
}
