using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Collections;

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
        TryInitOrSubscribe();
    }

    private void TryInitOrSubscribe()
    {
        solarSystemManager = SolarSystemManager.Instance;
        if (solarSystemManager == null)
        {
            // Try again next frame until SolarSystemManager exists
            StartCoroutine(WaitForSolarSystemManagerThenSubscribe());
            return;
        }
        // If already initialized, initialize immediately
        if (solarSystemManager.GetAllPlanets() != null && solarSystemManager.GetAllPlanets().Count > 0)
        {
            OnSolarSystemReady();
        }
        else
        {
            // Subscribe to event
            solarSystemManager.OnSolarSystemInitialized += OnSolarSystemReady;
        }
    }

    private IEnumerator WaitForSolarSystemManagerThenSubscribe()
    {
        while (SolarSystemManager.Instance == null)
            yield return null;
        solarSystemManager = SolarSystemManager.Instance;
        TryInitOrSubscribe();
    }

    private void OnSolarSystemReady()
    {
        if (solarSystemManager != null)
            solarSystemManager.OnSolarSystemInitialized -= OnSolarSystemReady;

        // Find managers
        ancientRuinsManager = FindFirstObjectByType<AncientRuinsManager>(FindObjectsInactive.Include);

        // Find existing SpaceMapUI in scene (DON'T CREATE NEW ONES!)
        spaceMapUI = FindSpaceMapUIInScene();
        if (spaceMapUI == null)
        {
            Debug.LogError("[SpaceMapButton] No SpaceMapUI found in scene! Make sure you have a SpaceMapUI component in your scene.");
            Debug.LogError("[SpaceMapButton] Check that your SpaceMapUI GameObject is active and the component is attached properly.");
            // List all objects with SpaceMapUI components for debugging
            SpaceMapUI[] allSpaceMapUIs = FindObjectsByType<SpaceMapUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[SpaceMapButton] Found {allSpaceMapUIs.Length} SpaceMapUI components in scene:");
            foreach (var ui in allSpaceMapUIs)
            {
                Debug.Log($"[SpaceMapButton] - {ui.name} on GameObject: {ui.gameObject.name} (Active: {ui.gameObject.activeInHierarchy}, Scene: {ui.gameObject.scene.name})");
            }
        }
        else
        {
            Debug.Log($"[SpaceMapButton] Found existing SpaceMapUI: {spaceMapUI.name} (Active: {spaceMapUI.gameObject.activeInHierarchy})");
            // Initialize the space map UI with the existing one
            if (solarSystemManager != null)
            {
                spaceMapUI.Initialize(solarSystemManager);
                Debug.Log("[SpaceMapButton] Initialized existing SpaceMapUI with SolarSystemManager");
            }
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
        Debug.Log("[SpaceMapButton] OpenSpaceMap called");

        // Always re-find SpaceMapUI and SolarSystemManager in case of scene reloads
        solarSystemManager = SolarSystemManager.Instance;
        spaceMapUI = FindSpaceMapUIInScene();

        if (spaceMapUI != null)
        {
            Debug.Log($"[SpaceMapButton] Found SpaceMapUI: {spaceMapUI.name} on GameObject: {spaceMapUI.gameObject.name}");

            // FORCE the GameObject to be active first
            if (!spaceMapUI.gameObject.activeInHierarchy)
            {
                spaceMapUI.gameObject.SetActive(true);
                Debug.Log($"[SpaceMapButton] Activated GameObject: {spaceMapUI.gameObject.name}");
            }

            // Make sure the Canvas is also active if it's a parent
            Transform parent = spaceMapUI.transform.parent;
            while (parent != null)
            {
                if (parent.GetComponent<Canvas>() != null && !parent.gameObject.activeInHierarchy)
                {
                    parent.gameObject.SetActive(true);
                    Debug.Log($"[SpaceMapButton] Activated Canvas: {parent.name}");
                }
                parent = parent.parent;
            }

            // Always re-initialize with latest planet data
            if (solarSystemManager != null)
            {
                spaceMapUI.Initialize(solarSystemManager);
            }

            // Now show the UI
            spaceMapUI.Show();
            Debug.Log("[SpaceMapButton] SpaceMapUI.Show() called successfully");
        }
        else
        {
            Debug.LogError("[SpaceMapButton] Could not find SpaceMapUI in scene!");
            Debug.LogError("[SpaceMapButton] Make sure you have a SpaceMapUI component properly set up in your scene hierarchy.");

            // Debug: List all GameObjects with "Space" in the name
            GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            Debug.Log($"[SpaceMapButton] Searching through {allObjects.Length} GameObjects...");

            foreach (GameObject obj in allObjects)
            {
                if (obj.name.ToLower().Contains("space"))
                {
                    Debug.Log($"[SpaceMapButton] Found GameObject with 'space': {obj.name} (Active: {obj.activeInHierarchy}) - Components: {string.Join(", ", obj.GetComponents<Component>().Select(c => c.GetType().Name))}");
                }
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
        Debug.Log("[SpaceMapButton] FindSpaceMapUIInScene called");
        
        // Method 1: Try normal active search first
        SpaceMapUI activeUI = FindFirstObjectByType<SpaceMapUI>();
        if (activeUI != null)
        {
            Debug.Log($"[SpaceMapButton] Found active SpaceMapUI: {activeUI.name}");
            return activeUI;
        }

        // Method 2: Search including inactive objects using the new API
        SpaceMapUI[] allUIs = FindObjectsByType<SpaceMapUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (SpaceMapUI ui in allUIs)
        {
            // Make sure it's a scene object, not a prefab
            if (ui.gameObject.scene.name != null && ui.gameObject.scene.name != "")
            {
                Debug.Log($"[SpaceMapButton] Found inactive SpaceMapUI: {ui.name} in scene: {ui.gameObject.scene.name}");
                return ui;
            }
        }

        // Method 3: Search by GameObject name if component search fails
        GameObject[] allGameObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (GameObject go in allGameObjects)
        {
            if (go.name.ToLower().Contains("spacemap") || go.name.ToLower().Contains("space map"))
            {
                SpaceMapUI uiComponent = go.GetComponent<SpaceMapUI>();
                if (uiComponent != null && go.scene.name != null && go.scene.name != "")
                {
                    Debug.Log($"[SpaceMapButton] Found SpaceMapUI by name search: {go.name}");
                    return uiComponent;
                }
                
                // Also check children
                uiComponent = go.GetComponentInChildren<SpaceMapUI>();
                if (uiComponent != null && go.scene.name != null && go.scene.name != "")
                {
                    Debug.Log($"[SpaceMapButton] Found SpaceMapUI in children of: {go.name}");
                    return uiComponent;
                }
            }
        }

        Debug.LogWarning("[SpaceMapButton] Could not find SpaceMapUI anywhere in scene");
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
