using UnityEngine;

/// <summary>
/// Interface for terrain modification layers
/// Layers are applied in sequence to build up the final terrain
/// </summary>
public interface ITerrainLayer
{
    /// <summary>
    /// Apply this layer to the heightmap
    /// </summary>
    /// <param name="heights">Heightmap array (resolution x resolution, values 0-1)</param>
    /// <param name="resolution">Heightmap resolution</param>
    /// <param name="mapSize">Map size in world units</param>
    /// <param name="terrainPosition">Terrain position in world space</param>
    void ApplyLayer(float[,] heights, int resolution, float mapSize, Vector3 terrainPosition);
    
    /// <summary>
    /// Whether this layer is enabled
    /// </summary>
    bool IsEnabled { get; }
    
    /// <summary>
    /// Layer priority (lower = applied first)
    /// </summary>
    int Priority { get; }
}

