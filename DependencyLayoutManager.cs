using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages automatic layout of tech trees based on dependencies
/// </summary>
public class DependencyLayoutManager : MonoBehaviour
{
    [Header("Layout Settings")]
    public float layerSpacing = 250f;
    public float nodeSpacing = 120f;
    public float branchSpacing = 80f;
    public Vector2 startPosition = new Vector2(100f, 0f);
    
    [Header("Manual Positioning")]
    [Tooltip("Enable manual hints for tech positioning within layers")]
    public bool useManualHints = true;
    
    public static LayoutResult CalculateDependencyLayout(List<TechData> allTechs, bool useHints = true)
    {
        var result = new LayoutResult();
        var layers = CreateDependencyLayers(allTechs);
        var positions = new Dictionary<TechData, Vector2>();
        
        // Calculate positions for each layer
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            var layerPositions = CalculateLayerPositions(layer, layerIndex, layers, useHints);
            
            foreach (var kvp in layerPositions)
            {
                positions[kvp.Key] = kvp.Value;
            }
        }
        
        result.nodePositions = positions;
        result.layers = layers;
        result.bounds = CalculateBounds(positions);
        
        return result;
    }
    
    private static List<List<TechData>> CreateDependencyLayers(List<TechData> allTechs)
    {
        var layers = new List<List<TechData>>();
        var processed = new HashSet<TechData>();
        var techToLayer = new Dictionary<TechData, int>();
        
        // Continue until all techs are processed
        while (processed.Count < allTechs.Count)
        {
            var currentLayer = new List<TechData>();
            
            foreach (var tech in allTechs)
            {
                if (processed.Contains(tech)) continue;
                
                // Check if all dependencies are satisfied
                bool canPlace = true;
                int maxDependencyLayer = -1;
                
                if (tech.requiredTechnologies != null)
                {
                    foreach (var dependency in tech.requiredTechnologies)
                    {
                        if (dependency == null) continue;
                        
                        if (!processed.Contains(dependency))
                        {
                            canPlace = false;
                            break;
                        }
                        
                        maxDependencyLayer = Mathf.Max(maxDependencyLayer, techToLayer[dependency]);
                    }
                }
                
                // Place in the layer after its latest dependency
                if (canPlace)
                {
                    int targetLayer = maxDependencyLayer + 1;
                    
                    // Ensure we have enough layers
                    while (layers.Count <= targetLayer)
                    {
                        layers.Add(new List<TechData>());
                    }
                    
                    // Add to appropriate layer
                    if (targetLayer == layers.Count - 1) // Current layer being built
                    {
                        currentLayer.Add(tech);
                        techToLayer[tech] = targetLayer;
                        processed.Add(tech);
                    }
                }
            }
            
            if (currentLayer.Count > 0)
            {
                if (layers.Count == 0 || layers[layers.Count - 1].Count > 0)
                {
                    layers.Add(currentLayer);
                }
                else
                {
                    layers[layers.Count - 1] = currentLayer;
                }
            }
            else
            {
                break; // Prevent infinite loop
            }
        }
        
        return layers;
    }
    
    private static Dictionary<TechData, Vector2> CalculateLayerPositions(
        List<TechData> layer, 
        int layerIndex, 
        List<List<TechData>> allLayers,
        bool useHints)
    {
        var positions = new Dictionary<TechData, Vector2>();
        
        if (useHints)
        {
            // Use manual hints if available
            positions = CalculateHintBasedPositions(layer, layerIndex);
        }
        else
        {
            // Use automatic positioning
            positions = CalculateAutoPositions(layer, layerIndex);
        }
        
        // Apply dependency-based adjustments
        OptimizePositionsForDependencies(positions, layer, layerIndex, allLayers);
        
        return positions;
    }
    
    private static Dictionary<TechData, Vector2> CalculateHintBasedPositions(List<TechData> layer, int layerIndex)
    {
        var positions = new Dictionary<TechData, Vector2>();
        
        // Group by manual hints (using category and positionHint)
        var groupedTechs = layer.GroupBy(t => GetPositionHint(t)).OrderBy(g => g.Key);
        
        float layerX = 100f + layerIndex * 250f;
        int groupIndex = 0;
        
        foreach (var group in groupedTechs)
        {
            var techsInGroup = group.OrderBy(t => t.positionHint).ThenBy(t => t.name).ToList();
            float groupCenterY = groupIndex * 200f;
            
            for (int i = 0; i < techsInGroup.Count; i++)
            {
                float techY = groupCenterY + (i - techsInGroup.Count / 2f) * 120f;
                positions[techsInGroup[i]] = new Vector2(layerX, techY);
            }
            
            groupIndex++;
        }
        
        return positions;
    }
    
    private static Dictionary<TechData, Vector2> CalculateAutoPositions(List<TechData> layer, int layerIndex)
    {
        var positions = new Dictionary<TechData, Vector2>();
        
        float layerX = 100f + layerIndex * 250f;
        float startY = -(layer.Count - 1) * 60f; // Center the layer vertically
        
        for (int i = 0; i < layer.Count; i++)
        {
            float techY = startY + i * 120f;
            positions[layer[i]] = new Vector2(layerX, techY);
        }
        
        return positions;
    }
    
    private static void OptimizePositionsForDependencies(
        Dictionary<TechData, Vector2> positions,
        List<TechData> layer,
        int layerIndex,
        List<List<TechData>> allLayers)
    {
        // This is where you can implement algorithms to reduce line crossings
        // For now, we'll use a simple approach
        
        if (layerIndex == 0) return; // No dependencies for first layer
        
        // Sort techs by the average Y position of their dependencies
        var techDependencyY = new Dictionary<TechData, float>();
        
        foreach (var tech in layer)
        {
            float avgY = 0f;
            int depCount = 0;
            
            if (tech.requiredTechnologies != null)
            {
                foreach (var dep in tech.requiredTechnologies)
                {
                    if (dep != null)
                    {
                        // Find dependency position in previous layers
                        foreach (var prevLayerTechs in allLayers.Take(layerIndex))
                        {
                            if (prevLayerTechs.Contains(dep))
                            {
                                avgY += positions[dep].y;
                                depCount++;
                                break;
                            }
                        }
                    }
                }
            }
            
            techDependencyY[tech] = depCount > 0 ? avgY / depCount : positions[tech].y;
        }
        
        // Re-sort and reposition based on dependency Y positions
        var sortedTechs = layer.OrderBy(t => techDependencyY[t]).ToList();
        float layerX = positions[layer[0]].x;
        
        for (int i = 0; i < sortedTechs.Count; i++)
        {
            float newY = -(sortedTechs.Count - 1) * 60f + i * 120f;
            positions[sortedTechs[i]] = new Vector2(layerX, newY);
        }
    }
    
    private static int GetPositionHint(TechData tech)
    {
        // Use the category combined with position hint for grouping
        return (int)tech.category * 100 + tech.positionHint;
    }
    
    private static Rect CalculateBounds(Dictionary<TechData, Vector2> positions)
    {
        if (positions.Count == 0) return new Rect(0, 0, 100, 100);
        
        float minX = positions.Values.Min(p => p.x) - 60f;
        float maxX = positions.Values.Max(p => p.x) + 60f;
        float minY = positions.Values.Min(p => p.y) - 60f;
        float maxY = positions.Values.Max(p => p.y) + 60f;
        
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
    
    [System.Serializable]
    public class LayoutResult
    {
        public Dictionary<TechData, Vector2> nodePositions;
        public List<List<TechData>> layers;
        public Rect bounds;
    }
}
