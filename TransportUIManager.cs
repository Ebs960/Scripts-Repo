using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TransportUIManager : MonoBehaviour
{
    public static TransportUIManager Instance { get; private set; }

    [Header("UI Elements")]
    [SerializeField] private GameObject transportPanel;
    [SerializeField] private Transform transportedUnitsContainer;
    [SerializeField] private Button loadUnitButton;
    [SerializeField] private GameObject unitButtonPrefab;
    [SerializeField] private TextMeshProUGUI capacityText;

    [Header("Deploy Mode Settings")]
    [SerializeField] private Color validDeployTileColor = new Color(0, 1, 0, 0.3f);
    [SerializeField] private Color invalidDeployTileColor = new Color(1, 0, 0, 0.3f);

    // Internal state
    private CombatUnit selectedTransport;
    private CombatUnit selectedUnitToLoad;
    private CombatUnit selectedUnitToUnload;
    private bool isInLoadMode = false;
    private bool isInDeployMode = false;
    private SphericalHexGrid grid;
    private PlanetGenerator planet;
    private Dictionary<int, GameObject> tileHighlights = new Dictionary<int, GameObject>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
        
        // Use GameManager API for multi-planet support
        planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        grid = planet != null ? planet.Grid : null;
    }

    void Start()
    {
        // Hide panel by default
        if (transportPanel != null)
            transportPanel.SetActive(false);
        
        // Set up load button
        if (loadUnitButton != null)
            loadUnitButton.onClick.AddListener(EnterLoadUnitMode);
    }

    /// <summary>
    /// Shows the transport UI when a transport unit is selected.
    /// </summary>
    public void ShowTransportUI(CombatUnit transport)
    {
        if (transport == null || !transport.data.isTransport)
            return;
            
        // Store reference to selected transport
        selectedTransport = transport;
        
        // Reset UI state
        isInLoadMode = false;
        isInDeployMode = false;
        ClearAllHighlights();
        
        // Update UI
        if (transportPanel != null)
            transportPanel.SetActive(true);
            
        // Update capacity text
        UpdateCapacityText();
        
        // Clear and rebuild transported units list
        ClearTransportedUnitsList();
        PopulateTransportedUnitsList();
        
        // Subscribe to events
        selectedTransport.OnUnitLoaded.AddListener(OnUnitLoaded);
        selectedTransport.OnUnitUnloaded.AddListener(OnUnitUnloaded);
    }

    /// <summary>
    /// Hides the transport UI.
    /// </summary>
    public void HideTransportUI()
    {
        if (selectedTransport != null)
        {
            // Unsubscribe from events
            selectedTransport.OnUnitLoaded.RemoveListener(OnUnitLoaded);
            selectedTransport.OnUnitUnloaded.RemoveListener(OnUnitUnloaded);
            selectedTransport = null;
        }
        
        // Reset UI state
        isInLoadMode = false;
        isInDeployMode = false;
        ClearAllHighlights();
        
        // Hide panel
        if (transportPanel != null)
            transportPanel.SetActive(false);
    }

    /// <summary>
    /// Enters the mode for loading units into the transport.
    /// </summary>
    public void EnterLoadUnitMode()
    {
        if (selectedTransport == null)
            return;
            
        // Toggle load mode
        isInLoadMode = !isInLoadMode;
        isInDeployMode = false;
        
        if (isInLoadMode)
        {
            // Highlight adjacent tiles with valid units to load
            HighlightAdjacentTilesWithUnits();
        }
        else
        {
            // Clear highlights
            ClearAllHighlights();
        }
    }

    /// <summary>
    /// Enters the mode for deploying/unloading units from the transport.
    /// </summary>
    public void EnterDeployUnitMode(CombatUnit unitToUnload)
    {
        if (selectedTransport == null || unitToUnload == null)
            return;
            
        // Set up deploy mode
        isInDeployMode = true;
        isInLoadMode = false;
        selectedUnitToUnload = unitToUnload;
        
        // Highlight valid tiles for deployment
        HighlightValidDeploymentTiles(unitToUnload);
    }

    /// <summary>
    /// Attempts to load a unit when a tile is clicked in load mode.
    /// </summary>
    public void HandleTileClickInLoadMode(int tileIndex)
    {
        if (!isInLoadMode || selectedTransport == null)
            return;
            
        // Find unit on this tile
        GameObject unitObj = null;
        // Use GameManager API for multi-planet support
        MoonGenerator moon = GameManager.Instance?.GetCurrentMoonGenerator();
        PlanetGenerator planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        
        var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(tileIndex) : null;
        if (tileData != null && tileData.occupantId != 0)
        {
            unitObj = UnitRegistry.GetObject(tileData.occupantId);
        }
        
        if (unitObj != null)
        {
            CombatUnit unit = unitObj.GetComponent<CombatUnit>();
            if (unit != null && unit != selectedTransport)
            {
                // Attempt to load the unit
                selectedTransport.LoadUnit(unit);
            }
        }
        
        // Exit load mode
        isInLoadMode = false;
        ClearAllHighlights();
    }

    /// <summary>
    /// Attempts to deploy a unit when a tile is clicked in deploy mode.
    /// </summary>
    public void HandleTileClickInDeployMode(int tileIndex)
    {
        if (!isInDeployMode || selectedTransport == null || selectedUnitToUnload == null)
            return;
            
        // Check if this is a valid deployment tile
        bool canDeploy = false;
        
        // Check if tile is adjacent or same as transport
        if (tileIndex == selectedTransport.currentTileIndex)
        {
            canDeploy = true;
        }
        else
        {
            int[] neighbors = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(selectedTransport.currentTileIndex) : System.Array.Empty<int>();
            foreach (int neighbor in neighbors)
            {
                if (neighbor == tileIndex)
                {
                    canDeploy = true;
                    break;
                }
            }
        }
        
        // Check if the unit can move to this tile
        if (canDeploy && selectedUnitToUnload.CanMoveTo(tileIndex))
        {
            // Attempt to deploy
            selectedTransport.UnloadUnit(selectedUnitToUnload, tileIndex);
        }
        
        // Exit deploy mode
        isInDeployMode = false;
        selectedUnitToUnload = null;
        ClearAllHighlights();
    }

    // Helper methods for UI management

    private void UpdateCapacityText()
    {
        if (capacityText != null && selectedTransport != null)
        {
            int current = selectedTransport.GetTransportedUnits().Count;
            int max = selectedTransport.data.transportCapacity;
            capacityText.text = $"Capacity: {current}/{max}";
        }
    }

    private void ClearTransportedUnitsList()
    {
        if (transportedUnitsContainer == null)
            return;
            
        foreach (Transform child in transportedUnitsContainer)
        {
            Destroy(child.gameObject);
        }
    }

    private void PopulateTransportedUnitsList()
    {
        if (transportedUnitsContainer == null || unitButtonPrefab == null || selectedTransport == null)
            return;
            
        List<CombatUnit> units = selectedTransport.GetTransportedUnits();
        foreach (var unit in units)
        {
            GameObject buttonObj = Instantiate(unitButtonPrefab, transportedUnitsContainer);
            Button button = buttonObj.GetComponent<Button>();
            
            // Set button text to unit name
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = unit.data.unitName;
            }
            
            // Set icon if available
            Image buttonImage = buttonObj.GetComponentInChildren<Image>();
            if (buttonImage != null && unit.data.icon != null)
            {
                buttonImage.sprite = unit.data.icon;
            }
            
            // Set up button click handler
            if (button != null)
            {
                CombatUnit capturedUnit = unit; // Capture for lambda
                button.onClick.AddListener(() => EnterDeployUnitMode(capturedUnit));
            }
        }
    }

    private void HighlightAdjacentTilesWithUnits()
    {
        ClearAllHighlights();
        
        if (selectedTransport == null || grid == null)
            return;
            
        // Get current and adjacent tiles
        List<int> tilesToCheck = new List<int>();
        tilesToCheck.Add(selectedTransport.currentTileIndex);
        
        int[] neighbors = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(selectedTransport.currentTileIndex) : System.Array.Empty<int>();
        if (neighbors != null)
        {
            tilesToCheck.AddRange(neighbors);
        }
        
        // Highlight tiles with units
        foreach (int tileIndex in tilesToCheck)
        {
                    // Use GameManager API for multi-planet support
        PlanetGenerator planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        MoonGenerator moon = GameManager.Instance?.GetCurrentMoonGenerator();
            
            GameObject unitObj = null;
            var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(tileIndex) : null;
            if (tileData != null && tileData.occupantId != 0)
            {
                unitObj = UnitRegistry.GetObject(tileData.occupantId);
            }
            
            if (unitObj != null)
            {
                CombatUnit unit = unitObj.GetComponent<CombatUnit>();
                if (unit != null && unit != selectedTransport && unit.owner == selectedTransport.owner)
                {
                    // Highlight this tile as valid for loading
                    HighlightTile(tileIndex, validDeployTileColor);
                }
            }
        }
    }

    private void HighlightValidDeploymentTiles(CombatUnit unitToUnload)
    {
        ClearAllHighlights();
        
        if (selectedTransport == null || grid == null || unitToUnload == null)
            return;
            
        // Get current and adjacent tiles
        List<int> tilesToCheck = new List<int>();
        tilesToCheck.Add(selectedTransport.currentTileIndex);
        
    int[] neighbors = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(selectedTransport.currentTileIndex) : System.Array.Empty<int>();
        if (neighbors != null)
        {
            tilesToCheck.AddRange(neighbors);
        }
        
        // Highlight valid deployment tiles
        foreach (int tileIndex in tilesToCheck)
        {
            bool canDeploy = unitToUnload.CanMoveTo(tileIndex);
            Color highlightColor = canDeploy ? validDeployTileColor : invalidDeployTileColor;
            HighlightTile(tileIndex, highlightColor);
        }
    }

    private void HighlightTile(int tileIndex, Color color)
    {
        if (grid == null || planet == null)
            return;

        // Create a highlight object if it doesn't exist
        if (!tileHighlights.ContainsKey(tileIndex))
        {
            GameObject highlightObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
            highlightObj.name = $"TileHighlight_{tileIndex}";

            // Position at tile center, slightly above
            Vector3 tileCenter = planet.transform.TransformPoint(grid.tileCenters[tileIndex]);
            highlightObj.transform.position = tileCenter + Vector3.up * 0.05f;
            
            // Scale the highlight to match tile size
            float tileSize = 0.2f; // Adjust as needed
            highlightObj.transform.localScale = new Vector3(tileSize, 1, tileSize);
            
            // Create a material for the highlight
            Material highlightMat = new Material(Shader.Find("Standard"));
            highlightMat.color = color;
            highlightMat.SetFloat("_Mode", 3); // Transparent mode
            highlightMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            highlightMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            highlightMat.SetInt("_ZWrite", 0);
            highlightMat.DisableKeyword("_ALPHATEST_ON");
            highlightMat.EnableKeyword("_ALPHABLEND_ON");
            highlightMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            highlightMat.renderQueue = 3000;
            
            // Apply material
            MeshRenderer renderer = highlightObj.GetComponent<MeshRenderer>();
            renderer.material = highlightMat;
            
            // Store in dictionary
            tileHighlights[tileIndex] = highlightObj;
        }
        else
        {
            // Update existing highlight color
            GameObject highlightObj = tileHighlights[tileIndex];
            MeshRenderer renderer = highlightObj.GetComponent<MeshRenderer>();
            renderer.material.color = color;
        }
    }

    private void ClearAllHighlights()
    {
        foreach (var highlight in tileHighlights.Values)
        {
            Destroy(highlight);
        }
        tileHighlights.Clear();
    }

    private void OnUnitLoaded(CombatUnit unit)
    {
        // Update UI
        UpdateCapacityText();
        PopulateTransportedUnitsList();
    }

    private void OnUnitUnloaded(CombatUnit unit)
    {
        // Update UI
        UpdateCapacityText();
        ClearTransportedUnitsList();
        PopulateTransportedUnitsList();
    }

} 