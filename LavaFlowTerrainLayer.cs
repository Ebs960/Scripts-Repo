using UnityEngine;

/// <summary>
/// Terrain layer that creates lava flows for volcanic biomes
/// Similar to rivers but for lava (higher temperature biomes)
/// </summary>
public class LavaFlowTerrainLayer : ITerrainLayer
{
    public bool IsEnabled { get; private set; }
    public int Priority => 20; // Applied after water layer
    
    private bool hasLavaFlow;
    private Vector2 lavaStart;
    private Vector2 lavaEnd;
    private float lavaWidth;
    private float lavaDepth;
    
    public LavaFlowTerrainLayer(bool hasLavaFlow, float mapSize, float temperature, Biome biome)
    {
        // Lava flows in high-temperature biomes (volcanic, desert with volcanic activity)
        this.hasLavaFlow = hasLavaFlow && temperature > 0.7f && 
                          (biome == Biome.Volcanic || biome == Biome.Desert);
        this.IsEnabled = this.hasLavaFlow;
        
        if (this.hasLavaFlow)
        {
            // Lava flows from high point to low point
            float edgeOffset = mapSize * 0.1f;
            
            // Lava typically flows from center (volcano) to edge
            int startEdge = Random.Range(0, 4);
            lavaStart = GetCenterPoint(mapSize);
            lavaEnd = GetEdgePoint(startEdge, mapSize, edgeOffset);
            
            // Lava dimensions
            lavaWidth = mapSize * 0.06f; // Slightly narrower than rivers
            lavaDepth = 0.08f; // Shallower than rivers (lava is more viscous)
        }
    }
    
    private Vector2 GetCenterPoint(float mapSize)
    {
        // Random point near center (volcano location)
        float centerRadius = mapSize * 0.2f;
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Random.Range(0f, centerRadius);
        return new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
    }
    
    private Vector2 GetEdgePoint(int edge, float mapSize, float offset)
    {
        float halfSize = mapSize * 0.5f;
        float randomPos = Random.Range(-halfSize + offset, halfSize - offset);
        
        switch (edge)
        {
            case 0: return new Vector2(randomPos, halfSize - offset); // Top
            case 1: return new Vector2(halfSize - offset, randomPos); // Right
            case 2: return new Vector2(randomPos, -halfSize + offset); // Bottom
            case 3: return new Vector2(-halfSize + offset, randomPos); // Left
            default: return Vector2.zero;
        }
    }
    
    public void ApplyLayer(float[,] heights, int resolution, float mapSize, Vector3 terrainPosition)
    {
        if (!IsEnabled) return;
        
        float halfMapSize = mapSize * 0.5f;
        float lavaWidthSquared = lavaWidth * lavaWidth;
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize - halfMapSize;
                float worldZ = (y / (float)resolution) * mapSize - halfMapSize;
                Vector2 point = new Vector2(worldX, worldZ);
                
                // Calculate distance from lava path
                float distanceToLava = DistanceToLineSegment(point, lavaStart, lavaEnd);
                
                if (distanceToLava < lavaWidth)
                {
                    // Inside lava flow - lower terrain
                    float normalizedDistance = distanceToLava / lavaWidth;
                    float depthFactor = 1f - (normalizedDistance * normalizedDistance);
                    float targetHeight = 0.2f - (lavaDepth * depthFactor); // Lava slightly above water level
                    
                    heights[y, x] = Mathf.Min(heights[y, x], targetHeight);
                }
            }
        }
    }
    
    private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lineLength = line.magnitude;
        
        if (lineLength < 0.001f) return Vector2.Distance(point, lineStart);
        
        Vector2 lineDir = line / lineLength;
        Vector2 toPoint = point - lineStart;
        float projection = Vector2.Dot(toPoint, lineDir);
        
        projection = Mathf.Clamp(projection, 0f, lineLength);
        Vector2 closestPoint = lineStart + lineDir * projection;
        
        return Vector2.Distance(point, closestPoint);
    }
}

