// Assets/Scripts/Managers/ResourceInstance.cs
using UnityEngine;

/// <summary>
/// Attached to each spawned resource node, to track its type, tile index, and planet.
/// </summary>
public class ResourceInstance : MonoBehaviour
{
    [Tooltip("Optional authoring anchor representing the point that touches terrain. This avoids relying on prefab root offsets or helper bounds.")]
    [SerializeField] private Transform groundAnchor;
    [HideInInspector] public ResourceData data;
    [HideInInspector] public int tileIndex;
    [HideInInspector] public int planetIndex;

    public float GroundToSurface(float surfaceY, float visualOffset = 0f)
    {
        float groundY;
        if (groundAnchor != null)
        {
            groundY = groundAnchor.position.y;
        }
        else
        {
            groundY = float.PositiveInfinity;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(false))
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    groundY = Mathf.Min(groundY, renderer.bounds.min.y);
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    groundY = Mathf.Min(groundY, renderer.bounds.min.y);

            // Legacy prefabs without an enabled visible mesh retain root-based placement.
            if (float.IsInfinity(groundY)) groundY = transform.position.y;
        }

        float correction = surfaceY + visualOffset - groundY;
        transform.position += Vector3.up * correction;
        return correction;
    }

    private void Awake()
    {
        // Register with UnitRegistry so TileOccupancyManager can resolve this object by instance ID.
        // Without this, GetOccupantObject() returns null even when the occupancy manager has the ID.
        UnitRegistry.Register(gameObject);
    }

    private void OnDestroy()
    {
        // Unregister to avoid stale references and clean up occupancy
        UnitRegistry.Unregister(gameObject);
        // Resources do not participate in TileOccupancy. No need to clear occupancy here.
    }
}
