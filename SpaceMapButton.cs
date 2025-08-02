using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Component to add space map functionality to existing game UI
/// Add this to your main game UI or create a dedicated button
/// </summary>
public class SpaceMapButton : MonoBehaviour
{
    [Header("UI References")]
    public Button spaceMapButton;
    public GameObject spaceMapUIPrefab; // Optional prefab for SpaceMapUI
    
    [Header("Button Settings")]
    public string buttonText = "Space Map";
    public KeyCode hotkey = KeyCode.M; // Press M to open space map

    [Header("Feature Buttons")]
    public Button tradeButton;
    public Button ruinsButton;

    private SpaceMapUI spaceMapUI;
    private SolarSystemManager solarSystemManager;
    private AncientRuinsManager ancientRuinsManager;

    void Awake()
    {
        SetupButton();
    }

    void Start()
    {
        // Find or create SolarSystemManager
        solarSystemManager = SolarSystemManager.Instance;
        if (solarSystemManager == null)
        {
            GameObject managerGO = new GameObject("SolarSystemManager");
            solarSystemManager = managerGO.AddComponent<SolarSystemManager>();
        }

        // Find managers
        
        ancientRuinsManager = FindFirstObjectByType<AncientRuinsManager>(FindObjectsInactive.Include);

        // Find or create SpaceMapUI (including inactive objects)
        spaceMapUI = FindSpaceMapUIInScene();
        if (spaceMapUI == null)
        {
            Debug.LogWarning("[SpaceMapButton] No SpaceMapUI found in scene, creating new one");
            if (spaceMapUIPrefab != null)
            {
                GameObject spaceMapGO = Instantiate(spaceMapUIPrefab);
                spaceMapUI = spaceMapGO.GetComponent<SpaceMapUI>();
                Debug.Log("[SpaceMapButton] Created SpaceMapUI from prefab");
            }
            else
            {
                GameObject spaceMapGO = new GameObject("SpaceMapUI");
                spaceMapUI = spaceMapGO.AddComponent<SpaceMapUI>();
                Debug.Log("[SpaceMapButton] Created SpaceMapUI from scratch");
            }
        }
        else
        {
            Debug.Log($"[SpaceMapButton] Found existing SpaceMapUI: {spaceMapUI.name} (Active: {spaceMapUI.gameObject.activeInHierarchy})");
        }

        // Initialize the space map UI
        if (spaceMapUI != null && solarSystemManager != null)
        {
            spaceMapUI.Initialize(solarSystemManager);
        }
    }

    void Update()
    {
        // Handle hotkey
        if (Input.GetKeyDown(hotkey))
        {
            OpenSpaceMap();
        }
    }

    /// <summary>
    /// Setup the button if not assigned
    /// </summary>
    private void SetupButton()
    {
        if (spaceMapButton == null)
        {
            spaceMapButton = GetComponent<Button>();
        }

        if (spaceMapButton != null)
        {
            spaceMapButton.onClick.AddListener(OpenSpaceMap);
            
            // Update button text if it has a Text or TextMeshPro component
            var buttonText = spaceMapButton.GetComponentInChildren<UnityEngine.UI.Text>();
            if (buttonText != null)
            {
                buttonText.text = this.buttonText;
            }
            
            var buttonTMP = spaceMapButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (buttonTMP != null)
            {
                buttonTMP.text = this.buttonText;
            }
        }

        if (tradeButton != null)
        {
            // This assumes you have a panel to show. A more robust solution would be needed.
            tradeButton.onClick.AddListener(() => Debug.Log("Trade button clicked."));
        }

        if (ruinsButton != null)
        {
            // We might not have a specific panel for ruins, but we can log something.
            ruinsButton.onClick.AddListener(() => Debug.Log("Ancient Ruins button clicked."));
        }
    }

    /// <summary>
    /// Open the space map UI
    /// </summary>
    public void OpenSpaceMap()
    {
        // Re-find SpaceMapUI if it's null (in case it was destroyed/recreated)
        if (spaceMapUI == null)
        {
            Debug.LogWarning("[SpaceMapButton] spaceMapUI is null, attempting to re-find...");
            spaceMapUI = FindSpaceMapUIInScene();
        }

        if (spaceMapUI != null)
        {
            Debug.Log($"[SpaceMapButton] Opening SpaceMapUI: {spaceMapUI.name}");
            spaceMapUI.Show();
        }
        else
        {
            Debug.LogWarning("[SpaceMapButton] SpaceMapUI not found!");
            
            // Additional debugging info
            SpaceMapUI[] allUIs = Resources.FindObjectsOfTypeAll<SpaceMapUI>();
            Debug.Log($"[SpaceMapButton] Found {allUIs.Length} SpaceMapUI components total (including prefabs)");
            
            foreach (SpaceMapUI ui in allUIs)
            {
                Debug.Log($"[SpaceMapButton] - {ui.name} (Scene: {ui.gameObject.scene.name}, Active: {ui.gameObject.activeInHierarchy})");
            }
        }
    }

    /// <summary>
    /// Enable or disable the space map button
    /// </summary>
    public void SetButtonEnabled(bool enabled)
    {
        if (spaceMapButton != null)
        {
            spaceMapButton.interactable = enabled;
        }
    }

    /// <summary>
    /// Find SpaceMapUI in scene, including inactive GameObjects
    /// </summary>
    private SpaceMapUI FindSpaceMapUIInScene()
    {
        // First try the normal active search
        SpaceMapUI activeUI = FindFirstObjectByType<SpaceMapUI>();
        if (activeUI != null)
            return activeUI;

        // If not found, search through all GameObjects including inactive ones
        SpaceMapUI[] allUIs = Resources.FindObjectsOfTypeAll<SpaceMapUI>();
        foreach (SpaceMapUI ui in allUIs)
        {
            // Make sure it's a scene object, not a prefab
            if (ui.gameObject.scene.name != null)
            {
                return ui;
            }
        }

        return null;
    }
}

/// <summary>
/// Simple script to add space map functionality to any GameObject
/// Just attach this script and it will create a space map button
/// </summary>
[System.Obsolete("Use SpaceMapButton component instead")]
public class QuickSpaceMapSetup : MonoBehaviour
{
    void Start()
    {
        // This is a helper script for quick setup
        // It will be marked obsolete in favor of the proper SpaceMapButton component
        
        GameObject buttonGO = new GameObject("SpaceMapButton");
        buttonGO.transform.SetParent(transform, false);
        
        SpaceMapButton spaceMapButton = buttonGO.AddComponent<SpaceMapButton>();
        
        Debug.Log("[QuickSpaceMapSetup] Space map button created. Consider using SpaceMapButton component directly.");
    }
}
