using System.Collections.Generic;

public interface IHexasphereGenerator
{
    /// <summary>Return the list that defines all available biomes.</summary>
    List<BiomeSettings> GetBiomeSettings();
}
