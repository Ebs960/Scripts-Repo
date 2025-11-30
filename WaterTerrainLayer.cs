using UnityEngine;

/// <summary>
/// Terrain layer that creates water bodies (oceans, lakes, rivers)
/// Used for Naval and Coastal battle types
/// </summary>
public class WaterTerrainLayer : ITerrainLayer
{
    public bool IsEnabled { get; private set; }
    public int Priority => 10; // Applied after base terrain
    
    private BattleType battleType;
    private float waterLevel;
    private float waterAreaSize;
    private Vector2 waterCenter;
    
    public WaterTerrainLayer(BattleType battleType, float mapSize)
    {
        this.battleType = battleType;
        this.IsEnabled = battleType == BattleType.Naval || battleType == BattleType.Coastal;
        
        // Water level is slightly below terrain base (creates visible water)
        waterLevel = 0.15f; // 15% of heightVariation (relative to terrain heightVariation)
        
        // Calculate water area based on battle type
        if (battleType == BattleType.Naval)
        {
            // Naval: Most of map is water, small islands
            waterAreaSize = mapSize * 0.9f; // 90% of map
            waterCenter = Vector2.zero; // Center of map
        }
        else if (battleType == BattleType.Coastal)
        {
            // Coastal: Half water, half land (gradient)
            waterAreaSize = mapSize * 0.5f;
            waterCenter = new Vector2(-mapSize * 0.25f, 0f); // Left side is water
        }
    }
    
    public void ApplyLayer(float[,] heights, int resolution, float mapSize, Vector3 terrainPosition)
    {
        if (!IsEnabled) return;
        
        float halfMapSize = mapSize * 0.5f;
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize - halfMapSize;
                float worldZ = (y / (float)resolution) * mapSize - halfMapSize;
                
                float distanceFromCenter = Vector2.Distance(new Vector2(worldX, worldZ), waterCenter);
                
                if (battleType == BattleType.Naval)
                {
                    // Naval: Most of map is water, only edges have islands
                    float islandRadius = mapSize * 0.1f; // Small islands at edges
                    if (distanceFromCenter > islandRadius)
                    {
                        // Water area - lower terrain
                        heights[y, x] = Mathf.Min(heights[y, x], waterLevel);
                    }
                }
                else if (battleType == BattleType.Coastal)
                {
                    // Coastal: Gradient from water (left) to land (right)
                    float normalizedX = (worldX + halfMapSize) / mapSize; // 0 to 1
                    float waterBlend = 1f - normalizedX; // 1.0 at left (water), 0.0 at right (land)
                    
                    // Create smooth transition
                    float targetHeight = Mathf.Lerp(heights[y, x], waterLevel, waterBlend * 0.8f);
                    heights[y, x] = Mathf.Min(heights[y, x], targetHeight);
                }
            }
        }
    }
}

