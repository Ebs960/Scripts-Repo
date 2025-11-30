using UnityEngine;

/// <summary>
/// Interface for biome-specific terrain generators
/// Each biome can have its own specialized terrain generation logic
/// </summary>
public interface IBiomeTerrainGenerator
{
    /// <summary>
    /// Generate terrain for a specific biome
    /// </summary>
    /// <param name="terrain">The Terrain component to modify</param>
    /// <param name="elevation">Base elevation (0-1)</param>
    /// <param name="moisture">Moisture level (0-1)</param>
    /// <param name="temperature">Temperature level (0-1)</param>
    /// <param name="mapSize">Size of the map in world units</param>
    void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize);
    
    /// <summary>
    /// Get the noise profile for this biome
    /// </summary>
    BiomeNoiseProfile GetNoiseProfile();
}

