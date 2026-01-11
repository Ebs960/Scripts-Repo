using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

/// <summary>
/// Diagnostic script to inspect MapMagic 2's API and answer our 4 key questions:
/// 1. Does MapMagic 2 expose a public API for node creation/modification, or do we need reflection?
/// 2. Can we access exposed variables programmatically? (likely graph.exposed or graph.exposedVariables)
/// 3. What's the exact type of the graph property? (GraphAsset, MapMagic.Graph, etc.)
/// 4. Can we modify node parameters after graph creation, or only during creation?
/// </summary>
public class MapMagic2APIDiagnostics : MonoBehaviour
{
    [Header("MapMagic 2 Object")]
    [Tooltip("Assign your MapMagic GameObject here, or leave null to auto-detect")]
    public GameObject mapMagicObject;
    
    [Header("Diagnostics")]
    [Tooltip("Run diagnostics on Start")]
    public bool runOnStart = true;
    
    private void Start()
    {
        if (runOnStart)
        {
            RunDiagnostics();
        }
    }
    
    [ContextMenu("Run Diagnostics")]
    public void RunDiagnostics()
    {
        // Find MapMagic component
        object mapMagicInstance = FindMapMagicComponent();
        if (mapMagicInstance == null)
        {
            Debug.LogError("MapMagic 2 component not found! Make sure MapMagic 2 is installed and a MapMagic object exists in the scene.");
            return;
        }
        
        var mapMagicType = mapMagicInstance.GetType();
        
        // QUESTION 1: What's the exact type of the graph property?
        InspectGraphProperty(mapMagicType, mapMagicInstance);
        
        // QUESTION 2: Can we access exposed variables programmatically?
        InspectExposedVariables(mapMagicType, mapMagicInstance);
        
        // QUESTION 3: Does MapMagic 2 expose a public API for node creation/modification?
        InspectNodeAPI(mapMagicType, mapMagicInstance);
        
        // QUESTION 4: Can we modify node parameters after graph creation?
        InspectRuntimeNodeModification(mapMagicType, mapMagicInstance);
    }
    
    private object FindMapMagicComponent()
    {
        GameObject targetObject = mapMagicObject;
        
        // If no object assigned, try to find one in scene
        if (targetObject == null)
        {
            // Try common MapMagic GameObject names
            var found = GameObject.Find("MapMagic");
            if (found == null) found = GameObject.Find("Map Magic");
            if (found == null) found = GameObject.Find("MapMagicObject");
            if (found != null) targetObject = found;
        }
        
        if (targetObject == null)
        {
            Debug.LogWarning("No MapMagic GameObject found. Trying to find by component type...");
        }
        else
        {
            // Get ALL components and check their types
            Component[] allComponents = targetObject.GetComponents<Component>();
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                var compType = comp.GetType();
                // Check if this looks like a MapMagic component
                if (compType.Name.Contains("MapMagic") || 
                    compType.FullName.Contains("MapMagic") ||
                    compType.Name.Contains("Map") && compType.Name.Contains("Magic"))
                {
                    return comp;
                }
            }
        }
        
        // Try to find by type name (multiple possible names)
        string[] possibleTypeNames = new string[]
        {
            "MapMagic.Core.MapMagicObject",
            "MapMagic.MapMagicObject",
            "MapMagic.MapMagic",
            "MapMagicObject",
            "MapMagic"
        };
        
        foreach (var typeName in possibleTypeNames)
        {
            var mapMagicType = System.Type.GetType($"{typeName}, Assembly-CSharp");
            if (mapMagicType == null)
            {
                mapMagicType = System.Type.GetType(typeName);
            }
            
            if (mapMagicType != null)
            {
                // Try on assigned object first
                if (targetObject != null)
                {
                    var comp = targetObject.GetComponent(mapMagicType);
                    if (comp != null)
                    {
                        return comp;
                    }
                }
                
                // Try to find in scene
                var found = FindFirstObjectByType(mapMagicType);
                if (found != null)
                {
                    return found;
                }
            }
        }
        
        Debug.LogError("Could not find MapMagic component. Tried:");
        Debug.LogError("  - Checking all components on assigned GameObject");
        Debug.LogError("  - Searching for types: " + string.Join(", ", possibleTypeNames));
        
        return null;
    }
    
    private new Component FindFirstObjectByType(System.Type type)
    {
        var findMethod = typeof(Object).GetMethod("FindFirstObjectByType", BindingFlags.Public | BindingFlags.Static);
        if (findMethod != null)
        {
            var genericMethod = findMethod.MakeGenericMethod(type);
            return genericMethod.Invoke(null, null) as Component;
        }
        return null;
    }
    
    private void InspectGraphProperty(System.Type mapMagicType, object mapMagicInstance)
    {
        // Check for graph property
        var graphProperty = mapMagicType.GetProperty("graph", BindingFlags.Public | BindingFlags.Instance);
        if (graphProperty != null)
        {
            var graphValue = graphProperty.GetValue(mapMagicInstance);
            if (graphValue != null)
            {
                var graphType = graphValue.GetType();
                // Check if it's writable - this is diagnostic info only
                if (!graphProperty.CanWrite)
                {
                    Debug.LogWarning("  - Graph property is read-only");
                }
            }
            else
            {
                Debug.LogWarning("  - Graph property exists but value is NULL (no graph assigned)");
            }
        }
        else
        {
            // Try field instead
            var graphField = mapMagicType.GetField("graph", BindingFlags.Public | BindingFlags.Instance);
            if (graphField != null)
            {
                var graphValue = graphField.GetValue(mapMagicInstance);
                if (graphValue == null)
                {
                    Debug.LogWarning("  - Graph field exists but value is NULL");
                }
            }
            else
            {
                Debug.LogError("  - ✗ Graph property/field NOT FOUND!");
            }
        }
    }
    
    private void InspectExposedVariables(System.Type mapMagicType, object mapMagicInstance)
    {
        // Get graph first
        var graphProperty = mapMagicType.GetProperty("graph", BindingFlags.Public | BindingFlags.Instance);
        if (graphProperty == null)
        {
            Debug.LogError("  - Cannot inspect exposed variables: graph property not found");
            return;
        }
        
        var graph = graphProperty.GetValue(mapMagicInstance);
        if (graph == null)
        {
            Debug.LogWarning("  - Cannot inspect exposed variables: graph is null");
            return;
        }
        
        var graphType = graph.GetType();
        
        // Look for exposed variables property
        var exposedProperty = graphType.GetProperty("exposed", BindingFlags.Public | BindingFlags.Instance);
        if (exposedProperty == null)
        {
            exposedProperty = graphType.GetProperty("exposedVariables", BindingFlags.Public | BindingFlags.Instance);
        }
        if (exposedProperty == null)
        {
            exposedProperty = graphType.GetProperty("Exposed", BindingFlags.Public | BindingFlags.Instance);
        }
        if (exposedProperty == null)
        {
            exposedProperty = graphType.GetProperty("ExposedVariables", BindingFlags.Public | BindingFlags.Instance);
        }
        
        if (exposedProperty != null)
        {
            var exposedValue = exposedProperty.GetValue(graph);
            if (exposedValue != null)
            {
                var exposedType = exposedValue.GetType();
                
                // Try to find SetValue/GetValue methods
                var setValueMethod = exposedType.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Instance);
                if (setValueMethod == null)
                {
                    setValueMethod = exposedType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance); // Indexer
                }
                
                if (setValueMethod == null)
                {
                    Debug.LogWarning($"  - ✗ SetValue method NOT FOUND");
                }
            }
        }
        else
        {
            Debug.LogWarning("  - ✗ Exposed variables property NOT FOUND!");
            
            // List all public properties
            var allProperties = graphType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in allProperties)
            {
                if (prop.Name.ToLower().Contains("exposed") || prop.Name.ToLower().Contains("variable"))
                {
                    Debug.LogWarning($"  - Found related property: {prop.Name}");
                }
            }
        }
    }
    
    private void InspectNodeAPI(System.Type mapMagicType, object mapMagicInstance)
    {
        // Get graph
        var graphProperty = mapMagicType.GetProperty("graph", BindingFlags.Public | BindingFlags.Instance);
        if (graphProperty == null)
        {
            Debug.LogError("  - Cannot inspect node API: graph property not found");
            return;
        }
        
        var graph = graphProperty.GetValue(mapMagicInstance);
        if (graph == null)
        {
            Debug.LogWarning("  - Cannot inspect node API: graph is null");
            return;
        }
        
        var graphType = graph.GetType();
        
        // Look for node/generator collection
        var generatorsProperty = graphType.GetProperty("generators", BindingFlags.Public | BindingFlags.Instance);
        if (generatorsProperty == null)
        {
            generatorsProperty = graphType.GetProperty("nodes", BindingFlags.Public | BindingFlags.Instance);
        }
        if (generatorsProperty == null)
        {
            generatorsProperty = graphType.GetProperty("Generators", BindingFlags.Public | BindingFlags.Instance);
        }
        if (generatorsProperty == null)
        {
            generatorsProperty = graphType.GetProperty("Nodes", BindingFlags.Public | BindingFlags.Instance);
        }
        
        if (generatorsProperty == null)
        {
            Debug.LogWarning("  - ✗ Node/Generator collection property NOT FOUND!");
        }
        
        // Look for CreateNode/AddNode methods
        var createNodeMethod = graphType.GetMethod("CreateNode", BindingFlags.Public | BindingFlags.Instance);
        if (createNodeMethod == null)
        {
            createNodeMethod = graphType.GetMethod("AddNode", BindingFlags.Public | BindingFlags.Instance);
        }
        if (createNodeMethod == null)
        {
            createNodeMethod = graphType.GetMethod("CreateGenerator", BindingFlags.Public | BindingFlags.Instance);
        }
        
        if (createNodeMethod == null)
        {
            Debug.LogWarning("  - ✗ Node creation method NOT FOUND (may need to use reflection)");
        }
    }
    
    private void InspectRuntimeNodeModification(System.Type mapMagicType, object mapMagicInstance)
    {
        // Get graph
        var graphProperty = mapMagicType.GetProperty("graph", BindingFlags.Public | BindingFlags.Instance);
        if (graphProperty == null)
        {
            Debug.LogError("  - Cannot inspect runtime modification: graph property not found");
            return;
        }
        
        var graph = graphProperty.GetValue(mapMagicInstance);
        if (graph == null)
        {
            Debug.LogWarning("  - Cannot inspect runtime modification: graph is null");
            return;
        }
        
        var graphType = graph.GetType();
        
        // Try to get a node and check if we can modify its properties
        var generatorsProperty = graphType.GetProperty("generators", BindingFlags.Public | BindingFlags.Instance);
        if (generatorsProperty == null)
        {
            generatorsProperty = graphType.GetProperty("nodes", BindingFlags.Public | BindingFlags.Instance);
        }
        
        if (generatorsProperty != null)
        {
            var generators = generatorsProperty.GetValue(graph);
            if (generators != null && generators is System.Collections.IEnumerable enumerable)
            {
                int nodeCount = 0;
                int modifiableCount = 0;
                
                foreach (var node in enumerable)
                {
                    if (node == null) continue;
                    nodeCount++;
                    
                    var nodeType = node.GetType();
                    var properties = nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    
                    // Check if node has modifiable properties (like intensity, frequency, etc.)
                    bool hasModifiableProps = false;
                    foreach (var prop in properties)
                    {
                        if (prop.CanWrite && 
                            (prop.Name.ToLower().Contains("intensity") ||
                             prop.Name.ToLower().Contains("frequency") ||
                             prop.Name.ToLower().Contains("scale") ||
                             prop.Name.ToLower().Contains("value")))
                        {
                            hasModifiableProps = true;
                            break;
                        }
                    }
                    
                    if (hasModifiableProps)
                    {
                        modifiableCount++;
                    }
                }
                
                if (modifiableCount == 0)
                {
                    Debug.LogWarning($"  - ✗ No modifiable properties found on nodes");
                }
            }
        }
        else
        {
            Debug.LogWarning("  - Cannot check node modification: node collection not found");
        }
    }
}

