using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Terrain/Biome Visual Database")]
public class BiomeVisualDatabase : ScriptableObject
{
    public List<BiomeVisualData> biomes = new List<BiomeVisualData>();

    private Dictionary<Biome, BiomeVisualData> lookup;

    public BiomeVisualData Get(Biome biome)
    {
        if (lookup == null || lookup.Count != biomes.Count)
        {
            BuildLookup();
        }

        if (lookup != null && lookup.TryGetValue(biome, out var data))
        {
            return data;
        }

        return null;
    }

    private void BuildLookup()
    {
        lookup = new Dictionary<Biome, BiomeVisualData>();
        foreach (var entry in biomes)
        {
            if (entry == null) continue;
            lookup[entry.biome] = entry;
        }
    }
}
