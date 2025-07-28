using System.Collections.Generic;

public interface IHexasphereGenerator
{
    /// <summary>Get the elevation value for a specific tile index.</summary>
    float GetTileElevation(int tileIndex);
    
    /// <summary>Get the full HexTileData for a specific tile index.</summary>
    HexTileData GetHexTileData(int tileIndex);
}
