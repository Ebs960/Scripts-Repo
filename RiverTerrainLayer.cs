using UnityEngine;

/// <summary>
/// Terrain layer that carves rivers into the terrain
/// Creates a riverbed by lowering terrain along a path
/// </summary>
public class RiverTerrainLayer : ITerrainLayer
{
    public bool IsEnabled { get; private set; }
    public int Priority => 20; // Applied after water layer
    
    private bool hasRiver;
    private Vector2 riverStart;
    private Vector2 riverEnd;
    private float riverWidth;
    private float riverDepth;
    
    public RiverTerrainLayer(bool hasRiver, float mapSize, float moisture)
    {
        this.hasRiver = hasRiver && moisture > 0.3f; // Only rivers in moist biomes
        this.IsEnabled = this.hasRiver;
        
        if (this.hasRiver)
        {
            // River flows from one edge to another
            // Random start/end points
            float edgeOffset = mapSize * 0.1f;
            
            // Choose random edges (0=top, 1=right, 2=bottom, 3=left)
            int startEdge = Random.Range(0, 4);
            int endEdge = (startEdge + 2) % 4; // Opposite edge
            
            riverStart = GetEdgePoint(startEdge, mapSize, edgeOffset);
            riverEnd = GetEdgePoint(endEdge, mapSize, edgeOffset);
            
            // River dimensions based on map size
            riverWidth = mapSize * 0.08f; // 8% of map width
            riverDepth = 0.1f; // 10% of heightVariation
        }
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
        float riverWidthSquared = riverWidth * riverWidth;
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize - halfMapSize;
                float worldZ = (y / (float)resolution) * mapSize - halfMapSize;
                Vector2 point = new Vector2(worldX, worldZ);
                
                // Calculate distance from river path (line segment)
                float distanceToRiver = DistanceToLineSegment(point, riverStart, riverEnd);
                
                if (distanceToRiver < riverWidth)
                {
                    // Inside river - lower terrain
                    float normalizedDistance = distanceToRiver / riverWidth; // 0 at center, 1 at edge
                    float depthFactor = 1f - (normalizedDistance * normalizedDistance); // Smooth falloff
                    float targetHeight = WATER_LEVEL - (riverDepth * depthFactor);
                    
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
        
        // Clamp to line segment
        projection = Mathf.Clamp(projection, 0f, lineLength);
        Vector2 closestPoint = lineStart + lineDir * projection;
        
        return Vector2.Distance(point, closestPoint);
    }
    
    private const float WATER_LEVEL = 0.15f; // Same as WaterTerrainLayer (15% of heightVariation)
}

