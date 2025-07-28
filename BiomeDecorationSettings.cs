using UnityEngine;
using System;

/// <summary>
/// Modern decoration system for biomes - separate from legacy BiomeSettings
/// This handles decoration prefabs for each biome type independently
/// </summary>
[System.Serializable]
public struct BiomeDecorationEntry
{
    [Header("Biome Configuration")]
    public Biome biome;
    
    [Header("Decoration Prefabs")]
    [Tooltip("Decoration prefabs for this biome (trees, bushes, rocks, etc.)")]
    public GameObject[] decorationPrefabs;
    
    [Header("Spawn Settings")]
    [Range(0f, 1f)]
    [Tooltip("Chance this biome will get decorations (0 = never, 1 = always)")]
    public float spawnChance;
    
    [Range(1, 8)]
    [Tooltip("Minimum decorations to spawn on tiles of this biome")]
    public int minDecorations;
    
    [Range(1, 12)]
    [Tooltip("Maximum decorations to spawn on tiles of this biome")]
    public int maxDecorations;
    
    [Header("Positioning")]
    [Range(0.1f, 0.9f)]
    [Tooltip("Minimum distance from tile center (as fraction of tile radius)")]
    public float minDistanceFromCenter;
    
    [Range(0.1f, 0.95f)]
    [Tooltip("Maximum distance from tile center (as fraction of tile radius)")]
    public float maxDistanceFromCenter;
    
    [Header("Scale and Rotation")]
    [Range(0.1f, 5.0f)]
    [Tooltip("Scale multiplier for decorations in this biome")]
    public float scaleMultiplier;
    
    [Range(0f, 1f)]
    [Tooltip("Random scale variation (0 = no variation, 1 = ±100% variation)")]
    public float scaleVariation;
    
    [Tooltip("Should decorations randomly rotate around their up axis?")]
    public bool randomRotation;
    
    /// <summary>
    /// Get default settings for a biome
    /// </summary>
    public static BiomeDecorationEntry GetDefault(Biome biome)
    {
        return new BiomeDecorationEntry
        {
            biome = biome,
            decorationPrefabs = new GameObject[0],
            spawnChance = GetDefaultSpawnChance(biome),
            minDecorations = GetDefaultMinDecorations(biome),
            maxDecorations = GetDefaultMaxDecorations(biome),
            minDistanceFromCenter = 0.4f,
            maxDistanceFromCenter = 0.85f,
            scaleMultiplier = 1.0f,
            scaleVariation = 0.2f,
            randomRotation = true
        };
    }
    
    private static float GetDefaultSpawnChance(Biome biome)
    {
        return biome switch
        {
            // Water biomes - no decorations
            Biome.Ocean or Biome.Coast or Biome.Seas => 0f,
            
            // Lush biomes - high decoration chance
            Biome.Forest or Biome.Jungle or Biome.Rainforest => 0.9f,
            Biome.Grassland or Biome.Plains or Biome.Savannah => 0.8f,
            
            // Moderate decoration biomes
            Biome.Taiga or Biome.PineForest => 0.7f,
            Biome.Marsh or Biome.Swamp => 0.6f,
            
            // Sparse decoration biomes
            Biome.Desert or Biome.Tundra => 0.4f,
            Biome.Mountain or Biome.Snow => 0.3f,
            
            // Hostile biomes - minimal decorations
            Biome.Volcanic or Biome.Steam => 0.2f,
            Biome.Hellscape or Biome.Brimstone => 0.1f,
            
            // Moon biomes
            Biome.MoonDunes => 0.5f,
            Biome.MoonCaves => 0.3f,
            
            // Default
            _ => 0.5f
        };
    }
    
    private static int GetDefaultMinDecorations(Biome biome)
    {
        return biome switch
        {
            // Lush biomes
            Biome.Forest or Biome.Jungle or Biome.Rainforest => 2,
            Biome.Grassland or Biome.Plains => 1,
            
            // Sparse biomes
            Biome.Desert or Biome.Tundra or Biome.Mountain => 1,
            
            // Very sparse biomes
            Biome.Volcanic or Biome.Hellscape => 1,
            
            // Default
            _ => 1
        };
    }
    
    private static int GetDefaultMaxDecorations(Biome biome)
    {
        return biome switch
        {
            // Lush biomes - lots of decorations
            Biome.Forest or Biome.Jungle or Biome.Rainforest => 5,
            Biome.Grassland or Biome.Plains or Biome.Savannah => 4,
            
            // Moderate biomes
            Biome.Taiga or Biome.PineForest => 3,
            Biome.Marsh or Biome.Swamp => 3,
            
            // Sparse biomes
            Biome.Desert or Biome.Tundra or Biome.Mountain => 2,
            
            // Very sparse biomes
            Biome.Volcanic or Biome.Hellscape => 1,
            
            // Default
            _ => 3
        };
    }
}

/// <summary>
/// Component that manages decoration spawning for both planet and moon generators
/// This replaces the decoration functionality that was embedded in BiomeSettings
/// </summary>
[System.Serializable]
public class BiomeDecorationManager
{
    [Header("Biome Decoration Configuration")]
    [Tooltip("Decoration settings for each biome type")]
    public BiomeDecorationEntry[] biomeDecorations = new BiomeDecorationEntry[0];
    
    [Header("Global Decoration Settings")]
    [Tooltip("Enable decoration spawning")]
    public bool enableDecorations = true;
    
    [Range(0.5f, 3.0f)]
    [Tooltip("Global scale multiplier applied to all decorations")]
    public float globalScaleMultiplier = 1.0f;
    
    private System.Collections.Generic.Dictionary<Biome, BiomeDecorationEntry> decorationLookup;
    
    /// <summary>
    /// Initialize the decoration lookup dictionary
    /// </summary>
    public void Initialize()
    {
        decorationLookup = new System.Collections.Generic.Dictionary<Biome, BiomeDecorationEntry>();
        
        foreach (var entry in biomeDecorations)
        {
            decorationLookup[entry.biome] = entry;
        }
    }
    
    /// <summary>
    /// Get decoration settings for a specific biome
    /// </summary>
    public BiomeDecorationEntry GetDecorationSettings(Biome biome)
    {
        if (decorationLookup == null)
            Initialize();
            
        if (decorationLookup.TryGetValue(biome, out var settings))
            return settings;
            
        // Return default settings if not found
        return BiomeDecorationEntry.GetDefault(biome);
    }
    
    /// <summary>
    /// Check if a biome should have decorations spawned
    /// </summary>
    public bool ShouldSpawnDecorations(Biome biome)
    {
        if (!enableDecorations)
            return false;
            
        var settings = GetDecorationSettings(biome);
        return settings.decorationPrefabs.Length > 0 && UnityEngine.Random.value < settings.spawnChance;
    }
    
    /// <summary>
    /// Get a random decoration prefab for a biome
    /// </summary>
    public GameObject GetRandomDecorationPrefab(Biome biome)
    {
        var settings = GetDecorationSettings(biome);
        if (settings.decorationPrefabs.Length == 0)
            return null;
            
        return settings.decorationPrefabs[UnityEngine.Random.Range(0, settings.decorationPrefabs.Length)];
    }
    
    /// <summary>
    /// Get the number of decorations to spawn for a tile
    /// </summary>
    public int GetDecorationCount(Biome biome)
    {
        var settings = GetDecorationSettings(biome);
        return UnityEngine.Random.Range(settings.minDecorations, settings.maxDecorations + 1);
    }
}
