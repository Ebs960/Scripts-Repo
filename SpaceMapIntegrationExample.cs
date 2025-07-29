using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Example setup script to demonstrate how to integrate the space map system
/// This script shows how to add space map functionality to your existing game
/// </summary>
public class SpaceMapIntegrationExample : MonoBehaviour
{
    [Header("Integration Options")]
    [Tooltip("Add space map button to existing UI")]
    public bool addSpaceMapButton = true;
    
    [Tooltip("Create standalone space map system")]
    public bool createStandaloneSystem = true;
    
    [Tooltip("Parent for the space map button (if null, will use this transform)")]
    public Transform buttonParent;

    void Start()
    {
        if (createStandaloneSystem)
        {
            SetupSolarSystemManager();
        }
        
        if (addSpaceMapButton)
        {
            CreateSpaceMapButton();
        }
    }

    /// <summary>
    /// Setup the solar system manager if it doesn't exist
    /// </summary>
    private void SetupSolarSystemManager()
    {
        // Check if SolarSystemManager already exists
        if (SolarSystemManager.Instance == null)
        {
            GameObject managerGO = new GameObject("SolarSystemManager");
            SolarSystemManager manager = managerGO.AddComponent<SolarSystemManager>();
            
            Debug.Log("[SpaceMapIntegration] Created SolarSystemManager");
        }
        
        // Setup planet transition loader
        if (PlanetTransitionLoader.Instance == null)
        {
            GameObject loaderGO = new GameObject("PlanetTransitionLoader");
            PlanetTransitionLoader loader = loaderGO.AddComponent<PlanetTransitionLoader>();
            
            Debug.Log("[SpaceMapIntegration] Created PlanetTransitionLoader");
        }
    }

    /// <summary>
    /// Create a space map button and add it to the UI
    /// </summary>
    private void CreateSpaceMapButton()
    {
        Transform parent = buttonParent != null ? buttonParent : transform;
        
        // Create button GameObject
        GameObject buttonGO = new GameObject("SpaceMapButton");
        buttonGO.transform.SetParent(parent, false);
        
        // Add UI components
        Image buttonImage = buttonGO.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.4f, 0.8f, 1f); // Blue button
        
        Button button = buttonGO.AddComponent<Button>();
        
        // Add button text
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        
        var text = textGO.AddComponent<TMPro.TextMeshProUGUI>();
        text.text = "Space Map";
        text.fontSize = 16;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = Color.white;
        
        // Position text
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        // Add SpaceMapButton component
        SpaceMapButton spaceMapButton = buttonGO.AddComponent<SpaceMapButton>();
        spaceMapButton.spaceMapButton = button;
        
        // Size the button appropriately
        RectTransform buttonRect = buttonGO.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(120, 40);
        
        Debug.Log("[SpaceMapIntegration] Created space map button");
    }
}

/// <summary>
/// Development helper script to test the space map system
/// Remove this component in production builds
/// </summary>
[System.Obsolete("Development use only - remove in production")]
public class SpaceMapTester : MonoBehaviour
{
    [Header("Test Controls")]
    public KeyCode testGeneratePlanet = KeyCode.F1;
    public KeyCode testSwitchPlanet = KeyCode.F2;
    public KeyCode testShowSpaceMap = KeyCode.F3;
    
    [Header("Test Settings")]
    public int testPlanetIndex = 1;

    void Update()
    {
        if (SolarSystemManager.Instance == null) return;

        if (Input.GetKeyDown(testGeneratePlanet))
        {
            TestGeneratePlanet();
        }
        
        if (Input.GetKeyDown(testSwitchPlanet))
        {
            TestSwitchPlanet();
        }
        
        if (Input.GetKeyDown(testShowSpaceMap))
        {
            TestShowSpaceMap();
        }
    }

    private void TestGeneratePlanet()
    {
        Debug.Log($"[Test] Generating planet {testPlanetIndex}");
        SolarSystemManager.Instance.SwitchToPlanet(testPlanetIndex);
    }

    private void TestSwitchPlanet()
    {
        int nextPlanet = (SolarSystemManager.Instance.currentPlanetIndex + 1) % 8;
        Debug.Log($"[Test] Switching from planet {SolarSystemManager.Instance.currentPlanetIndex} to {nextPlanet}");
        SolarSystemManager.Instance.SwitchToPlanet(nextPlanet);
    }

    private void TestShowSpaceMap()
    {
        Debug.Log("[Test] Opening space map");
        SpaceMapUI spaceMapUI = FindObjectOfType<SpaceMapUI>();
        if (spaceMapUI != null)
        {
            spaceMapUI.Show();
        }
        else
        {
            SolarSystemManager.Instance.OpenSpaceMap();
        }
    }

    void OnGUI()
    {
        if (SolarSystemManager.Instance == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("Space Map Test Controls:");
        GUILayout.Label($"F1 - Generate Planet {testPlanetIndex}");
        GUILayout.Label($"F2 - Switch to Next Planet");
        GUILayout.Label($"F3 - Show Space Map");
        GUILayout.Label($"Current Planet: {SolarSystemManager.Instance.currentPlanetIndex}");
        
        var currentPlanet = SolarSystemManager.Instance.GetCurrentPlanet();
        if (currentPlanet != null)
        {
            GUILayout.Label($"Planet Name: {currentPlanet.planetName}");
            GUILayout.Label($"Generated: {currentPlanet.isGenerated}");
            GUILayout.Label($"Civilizations: {currentPlanet.civilizations?.Count ?? 0}");
        }
        
        GUILayout.EndArea();
    }
}
