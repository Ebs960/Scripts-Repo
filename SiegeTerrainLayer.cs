using UnityEngine;

/// <summary>
/// Terrain layer that creates elevated positions and fortifications for Siege battles
/// Adds strategic high ground and defensive positions
/// </summary>
public class SiegeTerrainLayer : ITerrainLayer
{
    public bool IsEnabled { get; private set; }
    public int Priority => 5; // Applied early, before water layers
    
    private bool hasSiegeFeatures;
    private Vector2 defenderPosition; // Fortified position
    private float fortificationRadius;
    private float fortificationHeight;
    
    public SiegeTerrainLayer(bool hasSiegeFeatures, float mapSize)
    {
        this.hasSiegeFeatures = hasSiegeFeatures;
        this.IsEnabled = this.hasSiegeFeatures;
        
        if (this.hasSiegeFeatures)
        {
            // Defender position is typically on one side (e.g., left side)
            defenderPosition = new Vector2(-mapSize * 0.3f, 0f);
            fortificationRadius = mapSize * 0.15f; // Fortified area
            fortificationHeight = 0.3f; // 30% height increase for elevated position
        }
    }
    
    public void ApplyLayer(float[,] heights, int resolution, float mapSize, Vector3 terrainPosition)
    {
        if (!IsEnabled) return;
        
        float halfMapSize = mapSize * 0.5f;
        float fortificationRadiusSquared = fortificationRadius * fortificationRadius;
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize - halfMapSize;
                float worldZ = (y / (float)resolution) * mapSize - halfMapSize;
                Vector2 point = new Vector2(worldX, worldZ);
                
                float distanceSquared = (point - defenderPosition).sqrMagnitude;
                
                if (distanceSquared < fortificationRadiusSquared)
                {
                    // Inside fortification area - raise terrain
                    float distance = Mathf.Sqrt(distanceSquared);
                    float normalizedDistance = distance / fortificationRadius; // 0 at center, 1 at edge
                    float heightFactor = 1f - normalizedDistance; // Higher at center
                    heightFactor = Mathf.SmoothStep(0f, 1f, heightFactor); // Smooth transition
                    
                    // Raise terrain for elevated defensive position
                    float heightIncrease = fortificationHeight * heightFactor;
                    heights[y, x] = Mathf.Min(1f, heights[y, x] + heightIncrease);
                }
            }
        }
    }
}

