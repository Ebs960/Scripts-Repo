using UnityEngine;

/// <summary>
/// Helper script to easily set up biome settings for BattleMapGenerator
/// This can be used to copy settings from your main planet generator
/// </summary>
public class BattleMapBiomeSetup : MonoBehaviour
{
    [Header("Source Settings")]
    [Tooltip("Reference to your main PlanetGenerator to copy biome settings from")]
    public PlanetGenerator sourcePlanetGenerator;
    
    [Header("Target Settings")]
    [Tooltip("Reference to the BattleMapGenerator to set up")]
    public BattleMapGenerator targetBattleMapGenerator;
    
    [Header("Biome Selection")]
    [Tooltip("Which biomes to include in the battle map")]
    public Biome[] biomesToInclude = { 
        Biome.Plains, 
        Biome.Forest, 
        Biome.Mountain, 
        Biome.Desert, 
        Biome.Swamp,
        Biome.Snow,
        Biome.Grassland,
        Biome.Jungle,
        Biome.Taiga
    };
    
    [ContextMenu("Copy Biome Settings from Planet Generator")]
    public void CopyBiomeSettingsFromPlanetGenerator()
    {
        if (sourcePlanetGenerator == null)
        {
            Debug.LogError("[BattleMapBiomeSetup] No source PlanetGenerator assigned!");
            return;
        }
        
        if (targetBattleMapGenerator == null)
        {
            Debug.LogError("[BattleMapBiomeSetup] No target BattleMapGenerator assigned!");
            return;
        }
        
        // Get biome decoration settings from the planet generator
        var planetDecorationManager = sourcePlanetGenerator.decorationManager;
        if (planetDecorationManager == null || planetDecorationManager.biomeDecorations == null || planetDecorationManager.biomeDecorations.Length == 0)
        {
            Debug.LogWarning("[BattleMapBiomeSetup] No biome decoration settings found in PlanetGenerator!");
            return;
        }
        
        var planetBiomeSettings = planetDecorationManager.biomeDecorations;
        
        // Filter and copy relevant biome settings
        var battleBiomeSettings = new System.Collections.Generic.List<BiomeSettings>();
        
        foreach (var biome in biomesToInclude)
        {
            foreach (var decorationEntry in planetBiomeSettings)
            {
                if (decorationEntry.biome == biome)
                {
                    // Convert BiomeDecorationEntry to BiomeSettings
                    var biomeSetting = new BiomeSettings
                    {
                        biome = decorationEntry.biome,
                        albedoTexture = null, // No texture data in decoration entry
                        normalTexture = null,
                        decorations = decorationEntry.decorationPrefabs,
                        spawnChance = decorationEntry.spawnChance
                    };
                    battleBiomeSettings.Add(biomeSetting);
                    break;
                }
            }
        }
        
        // Apply to battle map generator
        targetBattleMapGenerator.biomeSettings = battleBiomeSettings.ToArray();
// Also sync textures from planet generator
        SyncBattleBiomeTextures();
    }
    
    /// <summary>
    /// Sync battle biome textures from planet generator's decoration manager
    /// This ensures battle maps use the same textures as the campaign map
    /// </summary>
    public void SyncBattleBiomeTextures()
    {
        if (sourcePlanetGenerator == null || targetBattleMapGenerator == null)
        {
            Debug.LogWarning("[BattleMapBiomeSetup] Cannot sync textures - missing source or target references");
            return;
        }
        
        var planetDecorationManager = sourcePlanetGenerator.decorationManager;
        if (planetDecorationManager == null || planetDecorationManager.biomeDecorations == null)
        {
            Debug.LogWarning("[BattleMapBiomeSetup] No planet decoration manager found for texture sync");
            return;
        }
        
        // Update each biome setting in the battle map generator with textures from planet generator
        for (int i = 0; i < targetBattleMapGenerator.biomeSettings.Length; i++)
        {
            var battleSetting = targetBattleMapGenerator.biomeSettings[i];
            if (battleSetting == null) continue;
            
            // Find matching biome in planet generator
            // BiomeDecorationEntry is a struct, so we check for default (biome == 0) instead of null
            var planetMatch = System.Linq.Enumerable.FirstOrDefault(
                planetDecorationManager.biomeDecorations,
                x => x.biome == battleSetting.biome && x.biome != Biome.Any
            );
            
            // Check if match was found (struct default would have biome == 0/Any)
            if (planetMatch.biome != Biome.Any && planetMatch.decorationPrefabs != null)
            {
                // Copy decoration prefabs from planet to battle map
                battleSetting.decorations = planetMatch.decorationPrefabs;
}
        }
}
    
    [ContextMenu("Set Default Biome Settings")]
    public void SetDefaultBiomeSettings()
    {
        if (targetBattleMapGenerator == null)
        {
            Debug.LogError("[BattleMapBiomeSetup] No target BattleMapGenerator assigned!");
            return;
        }
        
        // Create default biome settings with basic colors
        var defaultSettings = new BiomeSettings[biomesToInclude.Length];
        
        for (int i = 0; i < biomesToInclude.Length; i++)
        {
            defaultSettings[i] = new BiomeSettings
            {
                biome = biomesToInclude[i],
                albedoTexture = null, // Will use fallback colors
                normalTexture = null,
                decorations = new GameObject[0],
                spawnChance = 0.15f
            };
        }
        
        targetBattleMapGenerator.biomeSettings = defaultSettings;
}
    
    [ContextMenu("Test Battle Map Generation")]
    public void TestBattleMapGeneration()
    {
        if (targetBattleMapGenerator == null)
        {
            Debug.LogError("[BattleMapBiomeSetup] No target BattleMapGenerator assigned!");
            return;
        }
targetBattleMapGenerator.GenerateBattleMap(50f, 9, 9);
    }
}
