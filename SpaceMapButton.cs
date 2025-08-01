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
    
    private SpaceMapUI spaceMapUI;
    private SolarSystemManager solarSystemManager;

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

        // Find or create SpaceMapUI
        spaceMapUI = FindFirstObjectByType<SpaceMapUI>();
        if (spaceMapUI == null)
        {
            if (spaceMapUIPrefab != null)
            {
                GameObject spaceMapGO = Instantiate(spaceMapUIPrefab);
                spaceMapUI = spaceMapGO.GetComponent<SpaceMapUI>();
            }
            else
            {
                GameObject spaceMapGO = new GameObject("SpaceMapUI");
                spaceMapUI = spaceMapGO.AddComponent<SpaceMapUI>();
            }
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
    }

    /// <summary>
    /// Open the space map UI
    /// </summary>
    public void OpenSpaceMap()
    {
        if (spaceMapUI != null)
        {
            spaceMapUI.Show();
        }
        else
        {
            Debug.LogWarning("[SpaceMapButton] SpaceMapUI not found!");
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
