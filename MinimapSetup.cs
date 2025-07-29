using UnityEngine;

/// <summary>
/// Helper component to easily set up minimap system in the scene
/// This can be attached to a GameObject to provide easy configuration
/// </summary>
public class MinimapSetup : MonoBehaviour
{
    [Header("Minimap Components")]
    [Tooltip("The minimap controller that manages switching between planet and moon")]
    public MinimapController minimapController;
    
    [Header("Minimap Generators")]
    [Tooltip("Generator for planet minimap")]
    public MinimapGenerator planetMinimapGenerator;
    [Tooltip("Generator for moon minimap")]
    public MinimapGenerator moonMinimapGenerator;
    
    [Header("Color Providers")]
    [Tooltip("Color provider for planet (can be shared or separate)")]
    public MinimapColorProvider planetColorProvider;
    [Tooltip("Color provider for moon (can be shared or separate)")]
    public MinimapColorProvider moonColorProvider;
    
    void Start()
    {
        // Auto-assign components if not already set
        if (minimapController == null)
            minimapController = FindAnyObjectByType<MinimapController>();
            
        if (planetMinimapGenerator == null)
            planetMinimapGenerator = GameObject.Find("PlanetMinimapGenerator")?.GetComponent<MinimapGenerator>();
            
        if (moonMinimapGenerator == null)
            moonMinimapGenerator = GameObject.Find("MoonMinimapGenerator")?.GetComponent<MinimapGenerator>();
        
        // Assign generators to controller
        if (minimapController != null)
        {
            if (planetMinimapGenerator != null)
                minimapController.planetGenerator = planetMinimapGenerator;
                
            if (moonMinimapGenerator != null)
                minimapController.moonGenerator = moonMinimapGenerator;
        }
        
        // Assign color providers
        if (planetMinimapGenerator != null && planetColorProvider != null)
            planetMinimapGenerator.colorProvider = planetColorProvider;
            
        if (moonMinimapGenerator != null && moonColorProvider != null)
            moonMinimapGenerator.colorProvider = moonColorProvider;
        else if (moonMinimapGenerator != null && planetColorProvider != null)
            moonMinimapGenerator.colorProvider = planetColorProvider; // Fallback to shared
    }
    
    [ContextMenu("Auto-Find Components")]
    public void AutoFindComponents()
    {
        minimapController = FindAnyObjectByType<MinimapController>();
        
        var generators = FindObjectsByType<MinimapGenerator>(FindObjectsSortMode.None);
        foreach (var gen in generators)
        {
            if (gen.name.ToLower().Contains("planet"))
                planetMinimapGenerator = gen;
            else if (gen.name.ToLower().Contains("moon"))
                moonMinimapGenerator = gen;
        }
        
        var providers = FindObjectsByType<MinimapColorProvider>(FindObjectsSortMode.None);
        if (providers.Length > 0)
        {
            planetColorProvider = providers[0];
            if (providers.Length > 1)
                moonColorProvider = providers[1];
        }
        
        Debug.Log($"Auto-found: Controller={minimapController != null}, Planet Gen={planetMinimapGenerator != null}, Moon Gen={moonMinimapGenerator != null}");
    }
}
