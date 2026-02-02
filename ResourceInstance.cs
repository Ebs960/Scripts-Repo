// Assets/Scripts/Managers/ResourceInstance.cs
using UnityEngine;

/// <summary>
/// Attached to each spawned resource node, to track its type, tile index, and planet.
/// </summary>
public class ResourceInstance : MonoBehaviour
{
    [HideInInspector] public ResourceData data;
    [HideInInspector] public int tileIndex;
    [HideInInspector] public int planetIndex;

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
        
        // Clear occupancy when destroyed
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            var td = TileSystem.GetForPlanet(planetIndex)?.GetTileData(tileIndex);
            var layer = (td != null && !td.isLand) ? TileLayer.Underwater : TileLayer.Surface;
            occ?.ClearOccupant(tileIndex, layer);
        }
        catch { }
    }
}