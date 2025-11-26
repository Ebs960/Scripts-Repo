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
        Debug.Log("=== MapMagic 2 API Diagnostics ===");
        
        // Find MapMagic component
        object mapMagicInstance = FindMapMagicComponent();
        if (mapMagicInstance == null)
        {
            Debug.LogError("MapMagic 2 component not found! Make sure MapMagic 2 is installed and a MapMagic object exists in the scene.");
            return;
        }
        
        var mapMagicType = mapMagicInstance.GetType();
        Debug.Log($"MapMagic Component Type: {mapMagicType.FullName}");
        Debug.Log($"MapMagic Component Assembly: {mapMagicType.Assembly.FullName}");
        
        // QUESTION 1: What's the exact type of the graph property?
        Debug.Log("\n--- QUESTION 1: Graph Property Type ---");
        InspectGraphProperty(mapMagicType, mapMagicInstance);
        
        // QUESTION 2: Can we access exposed variables programmatically?
        Debug.Log("\n--- QUESTION 2: Exposed Variables Access ---");
        InspectExposedVariables(mapMagicType, mapMagicInstance);
        
        // QUESTION 3: Does MapMagic 2 expose a public API for node creation/modification?
        Debug.Log("\n--- QUESTION 3: Node Creation/Modification API ---");
        InspectNodeAPI(mapMagicType, mapMagicInstance);
        
        // QUESTION 4: Can we modify node parameters after graph creation?
        Debug.Log("\n--- QUESTION 4: Runtime Node Parameter Modification ---");
        InspectRuntimeNodeModification(mapMagicType, mapMagicInstance);
        
        Debug.Log("\n=== Diagnostics Complete ===");
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
            Debug.Log($"Checking GameObject: {targetObject.name}");
            
            // Get ALL components and check their types
            Component[] allComponents = targetObject.GetComponents<Component>();
            Debug.Log($"Found {allComponents.Length} components on GameObject:");
            
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                var compType = comp.GetType();
                Debug.Log($"  - {compType.Name} ({compType.FullName})");
                
                // Check if this looks like a MapMagic component
                if (compType.Name.Contains("MapMagic") || 
                    compType.FullName.Contains("MapMagic") ||
                    compType.Name.Contains("Map") && compType.Name.Contains("Magic"))
                {
                    Debug.Log($"  ✓ Found MapMagic component: {compType.FullName}");
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
                Debug.Log($"Trying to find component of type: {mapMagicType.FullName}");
                
                // Try on assigned object first
                if (targetObject != null)
                {
                    var comp = targetObject.GetComponent(mapMagicType);
                    if (comp != null)
                    {
                        Debug.Log($"  ✓ Found component on assigned GameObject!");
                        return comp;
                    }
                }
                
                // Try to find in scene
                var found = FindFirstObjectByType(mapMagicType);
                if (found != null)
                {
                    Debug.Log($"  ✓ Found component in scene!");
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
                Debug.Log($"✓ Graph Property Found!");
                Debug.Log($"  - Property Type: {graphProperty.PropertyType.FullName}");
                Debug.Log($"  - Actual Value Type: {graphType.FullName}");
                Debug.Log($"  - Is ScriptableObject: {typeof(ScriptableObject).IsAssignableFrom(graphType)}");
                Debug.Log($"  - Is Null: {graphValue == null}");
                
                // Check if it's writable
                if (graphProperty.CanWrite)
                {
                    Debug.Log($"  - ✓ Graph property is WRITABLE (can be changed at runtime)");
                }
                else
                {
                    Debug.Log($"  - ✗ Graph property is READ-ONLY (cannot be changed at runtime)");
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
                if (graphValue != null)
                {
                    var graphType = graphValue.GetType();
                    Debug.Log($"✓ Graph Field Found!");
                    Debug.Log($"  - Field Type: {graphField.FieldType.FullName}");
                    Debug.Log($"  - Actual Value Type: {graphType.FullName}");
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
        Debug.Log($"Graph Type: {graphType.FullName}");
        
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
            Debug.Log($"✓ Exposed Variables Property Found!");
            Debug.Log($"  - Property Name: {exposedProperty.Name}");
            Debug.Log($"  - Property Type: {exposedProperty.PropertyType.FullName}");
            
            if (exposedValue != null)
            {
                var exposedType = exposedValue.GetType();
                Debug.Log($"  - Actual Value Type: {exposedType.FullName}");
                
                // Try to find SetValue/GetValue methods
                var setValueMethod = exposedType.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Instance);
                if (setValueMethod == null)
                {
                    setValueMethod = exposedType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance); // Indexer
                }
                
                if (setValueMethod != null)
                {
                    Debug.Log($"  - ✓ SetValue method found: {setValueMethod.Name}");
                    Debug.Log($"    Parameters: {string.Join(", ", System.Array.ConvertAll(setValueMethod.GetParameters(), p => $"{p.ParameterType.Name} {p.Name}"))}");
                }
                else
                {
                    Debug.LogWarning($"  - ✗ SetValue method NOT FOUND");
                }
                
                // Check if it's a dictionary or collection
                if (exposedValue is System.Collections.IDictionary dict)
                {
                    Debug.Log($"  - Is Dictionary/Collection: YES ({dict.Count} items)");
                }
            }
        }
        else
        {
            Debug.LogWarning("  - ✗ Exposed variables property NOT FOUND!");
            Debug.Log("  - Checking for alternative names...");
            
            // List all public properties
            var allProperties = graphType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Debug.Log($"  - All public properties on graph ({allProperties.Length}):");
            foreach (var prop in allProperties)
            {
                if (prop.Name.ToLower().Contains("exposed") || prop.Name.ToLower().Contains("variable"))
                {
                    Debug.Log($"    * {prop.Name} ({prop.PropertyType.Name})");
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
        
        if (generatorsProperty != null)
        {
            Debug.Log($"✓ Node/Generator Collection Found!");
            Debug.Log($"  - Property Name: {generatorsProperty.Name}");
            Debug.Log($"  - Property Type: {generatorsProperty.PropertyType.FullName}");
            
            var generators = generatorsProperty.GetValue(graph);
            if (generators != null)
            {
                if (generators is System.Collections.ICollection collection)
                {
                    Debug.Log($"  - Collection Count: {collection.Count}");
                }
            }
        }
        else
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
        
        if (createNodeMethod != null)
        {
            Debug.Log($"✓ Node Creation Method Found!");
            Debug.Log($"  - Method Name: {createNodeMethod.Name}");
            Debug.Log($"  - Parameters: {string.Join(", ", System.Array.ConvertAll(createNodeMethod.GetParameters(), p => $"{p.ParameterType.Name} {p.Name}"))}");
        }
        else
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
                
                Debug.Log($"✓ Found {nodeCount} nodes in graph");
                Debug.Log($"  - {modifiableCount} nodes have modifiable properties (intensity/frequency/scale/value)");
                
                if (modifiableCount > 0)
                {
                    Debug.Log($"  - ✓ Nodes CAN be modified at runtime!");
                }
                else
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

