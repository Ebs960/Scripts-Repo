using System.Collections.Generic;

public interface IHexasphereGenerator
{
    /// <summary>Return the list that defines all available biomes.</summary>
    List<BiomeSettings> GetBiomeSettings();
    
    /// <summary>Get the elevation value for a specific tile index.</summary>
    float GetTileElevation(int tileIndex);
    
    /// <summary>Get the full HexTileData for a specific tile index.</summary>
    HexTileData GetHexTileData(int tileIndex);
}
