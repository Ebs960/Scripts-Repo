using UnityEngine;

/// <summary>
/// Terrain layer that creates lakes (circular water bodies)
/// Used for moist biomes or specific battle scenarios
/// </summary>
public class LakeTerrainLayer : ITerrainLayer
{
    public bool IsEnabled { get; private set; }
    public int Priority => 15; // Applied after water layer, before rivers
    
    private bool hasLake;
    private Vector2 lakeCenter;
    private float lakeRadius;
    private float lakeDepth;
    
    public LakeTerrainLayer(bool hasLake, float mapSize, float moisture)
    {
        // Lakes in moist biomes
        this.hasLake = hasLake && moisture > 0.4f;
        this.IsEnabled = this.hasLake;
        
        if (this.hasLake)
        {
            // Random lake position (avoid edges)
            float centerArea = mapSize * 0.6f;
            lakeCenter = new Vector2(
                Random.Range(-centerArea * 0.5f, centerArea * 0.5f),
                Random.Range(-centerArea * 0.5f, centerArea * 0.5f)
            );
            
            // Lake size based on map size
            lakeRadius = mapSize * Random.Range(0.08f, 0.15f);
            lakeDepth = 0.12f; // Deeper than rivers
        }
    }
    
    public void ApplyLayer(float[,] heights, int resolution, float mapSize, Vector3 terrainPosition)
    {
        if (!IsEnabled) return;
        
        float halfMapSize = mapSize * 0.5f;
        float lakeRadiusSquared = lakeRadius * lakeRadius;
        const float WATER_LEVEL = 0.15f;
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize - halfMapSize;
                float worldZ = (y / (float)resolution) * mapSize - halfMapSize;
                Vector2 point = new Vector2(worldX, worldZ);
                
                float distanceSquared = (point - lakeCenter).sqrMagnitude;
                
                if (distanceSquared < lakeRadiusSquared)
                {
                    // Inside lake - lower terrain with smooth falloff
                    float distance = Mathf.Sqrt(distanceSquared);
                    float normalizedDistance = distance / lakeRadius; // 0 at center, 1 at edge
                    float depthFactor = 1f - normalizedDistance; // Smooth circular falloff
                    depthFactor = Mathf.SmoothStep(0f, 1f, depthFactor); // Smooth transition
                    
                    float targetHeight = WATER_LEVEL - (lakeDepth * depthFactor);
                    heights[y, x] = Mathf.Min(heights[y, x], targetHeight);
                }
            }
        }
    }
}

