using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Manages unit selection and movement commands.
/// Handles right-click movement orders and prevents conflicts with other input.
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }
    
    [Header("Selection Settings")]
    [SerializeField] private Color selectedUnitHighlightColor = Color.yellow;
    [SerializeField] private GameObject selectionIndicatorPrefab; // Optional visual indicator
    
    // Currently selected unit - now uses BaseUnit as common type
    private BaseUnit selectedUnit; // Can be CombatUnit or WorkerUnit (both inherit from BaseUnit)
    private GameObject selectionIndicator;
    // Cached highlight/selection materials to avoid allocations
    private static Material s_selectionIndicatorMaterial;
    private static UnityEngine.MaterialPropertyBlock s_selectionMPB;
    
    // References
    private Camera mainCamera;
    private Camera cachedMainCamera; // Cached reference to avoid repeated FindAnyObjectByType calls
    // Cached hover info provided by TileSystem events
    private int cachedHoveredTileIndex = -1;
    private Vector3 cachedHoveredWorldPos = Vector3.zero;
    private bool isHoveringTile = false;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        // Find references
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            // Use cached reference to avoid expensive FindAnyObjectByType call
            if (cachedMainCamera == null)
                cachedMainCamera = FindAnyObjectByType<Camera>();
            mainCamera = cachedMainCamera;
        }
        
        // Cache selection material once
        if (s_selectionIndicatorMaterial == null)
        {
            var shader = Shader.Find("Standard");
            if (shader != null)
            {
                s_selectionIndicatorMaterial = new Material(shader);
                s_selectionIndicatorMaterial.SetFloat("_Mode", 3);
                s_selectionIndicatorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                s_selectionIndicatorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                s_selectionIndicatorMaterial.SetInt("_ZWrite", 0);
                s_selectionIndicatorMaterial.DisableKeyword("_ALPHATEST_ON");
                s_selectionIndicatorMaterial.EnableKeyword("_ALPHABLEND_ON");
                s_selectionIndicatorMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                s_selectionIndicatorMaterial.renderQueue = 3000;
            }
            s_selectionMPB = new UnityEngine.MaterialPropertyBlock();
        }
            
    }
    
    void Update()
    {
        HandleInput();
    }

    private void OnEnable()
    {
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileHovered += OnTileHoveredTileSystem;
            TileSystem.Instance.OnTileHoverExited += OnTileExitedTileSystem;
            TileSystem.Instance.OnTileClicked += OnTileClickedTileSystem;
        }
    }

    private void OnDisable()
    {
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileHovered -= OnTileHoveredTileSystem;
            TileSystem.Instance.OnTileHoverExited -= OnTileExitedTileSystem;
            TileSystem.Instance.OnTileClicked -= OnTileClickedTileSystem;
        }
    }

    private void OnTileHoveredTileSystem(int tileIndex, Vector3 worldPos)
    {
        cachedHoveredTileIndex = tileIndex;
        cachedHoveredWorldPos = worldPos;
        isHoveringTile = true;
    }

    private void OnTileExitedTileSystem()
    {
        cachedHoveredTileIndex = -1;
        cachedHoveredWorldPos = Vector3.zero;
        isHoveringTile = false;
    }

    private void OnTileClickedTileSystem(int tileIndex, Vector3 worldPos)
    {
        // Left-click selection is routed via TileSystem; select/deselect units here.
        //
        // IMPORTANT (Flat map compatibility):
        // In flat equirectangular view, worldPos is on the flat plane, not near the unit's 3D position.
        // The authoritative identity is tileIndex. Use occupantId -> UnitRegistry first, then fall back
        // to a worldPos proximity lookup for legacy meshes.
        var clickedUnit = GetUnitOnTile(tileIndex);
        if (clickedUnit == null)
            clickedUnit = GetUnitAtPosition(worldPos);
        if (clickedUnit != null) SelectUnit(clickedUnit); else DeselectUnit();
        // Note: Right-click movement remains handled in Update() to detect mouse button 1
    }

    /// <summary>
    /// Get a unit occupying the given tile index.
    /// Returns BaseUnit since both CombatUnit and WorkerUnit inherit from it.
    /// </summary>
    private BaseUnit GetUnitOnTile(int tileIndex)
    {
        if (TileSystem.Instance == null) return null;
        var td = TileSystem.Instance.GetTileData(tileIndex);
        if (td == null || td.occupantId == 0) return null;

        var obj = UnitRegistry.GetObject(td.occupantId);
        if (obj == null) return null;

        // Try to get as BaseUnit (covers both CombatUnit and WorkerUnit)
        var baseUnit = obj.GetComponent<BaseUnit>();
        return baseUnit;
    }
    
    /// <summary>
    /// Handle mouse input for unit selection and movement
    /// </summary>
    private void HandleInput()
    {
        // MIGRATED: Use InputManager for UI blocking check
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
            return;
        
        // MIGRATED: Check if we can process gameplay input
        if (InputManager.Instance != null && !InputManager.Instance.CanProcessInput(InputManager.InputPriority.Gameplay))
            return;
        
        // Right click: Move selected unit
        if (Input.GetMouseButtonDown(1))
        {
            HandleRightClick();
        }
        
        // R key: Show space travel UI for selected unit (changed from Space to avoid conflicts)
        if (Input.GetKeyDown(KeyCode.R) && HasSelectedUnit())
        {
            HandleSpaceTravelKey();
        }
        
        // M key: Open space map for solar system overview
        if (Input.GetKeyDown(KeyCode.M))
        {
            HandleSpaceMapKey();
        }
    }
    
    /// <summary>
    /// Handle left-click for unit selection
    /// </summary>
    private void HandleLeftClick()
    {
        // Deprecated: Selection via TileSystem.OnTileClicked
    }
    
    /// <summary>
    /// Handle right-click for unit movement
    /// </summary>
    private void HandleRightClick()
    {
        if (selectedUnit == null)
        {
return;
        }
        // Prefer authoritative hovered tile info
        if (isHoveringTile && cachedHoveredTileIndex >= 0)
        {
            MoveSelectedUnitToTile(cachedHoveredTileIndex);
            return;
        }

    // Fallback: perform a raycast using TileSystem's mask if available
        var hitInfo = GetMouseHitInfo();
        if (hitInfo.hit && hitInfo.tileIndex >= 0)
        {
            MoveSelectedUnitToTile(hitInfo.tileIndex);
        }
    }
    
    /// <summary>
    /// Get mouse raycast hit information using the new texture-based picking system
    /// </summary>
    private (bool hit, Vector3 worldPosition, int tileIndex) GetMouseHitInfo()
    {
        // Use TileSystem's new texture-based picking system (replaces old TileIndexHolder approach)
        if (TileSystem.Instance != null)
        {
            var result = TileSystem.Instance.GetMouseHitInfo();
            return (result.hit, result.worldPosition, result.tileIndex);
        }

        return (false, Vector3.zero, -1);
    }
    
    
    /// <summary>
    /// Find a unit at the given world position
    /// </summary>
    private BaseUnit GetUnitAtPosition(Vector3 worldPosition)
    {
        // Use a small sphere to detect units near the click position
        Collider[] colliders = Physics.OverlapSphere(worldPosition, 0.5f);
        
        foreach (var collider in colliders)
        {
            // Try to get BaseUnit (covers both CombatUnit and WorkerUnit)
            var baseUnit = collider.GetComponentInParent<BaseUnit>();
            if (baseUnit != null)
                return baseUnit;
        }
        
        return null;
    }
    
    /// <summary>
    /// Select a unit
    /// </summary>
    public void SelectUnit(BaseUnit unit)
    {
        if (unit == null)
            return;
        
        // Deselect previous unit
        DeselectUnit();
        
        // Select new unit
        selectedUnit = unit;
        
        // Create visual indicator
        CreateSelectionIndicator();
        
        // Show unit info panel
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowUnitInfoPanelForUnit(unit);
        }
}
    
    /// <summary>
    /// Deselect the current unit
    /// </summary>
    public void DeselectUnit()
    {
        if (selectedUnit == null)
            return;
selectedUnit = null;

        // Remove visual indicator
        if (selectionIndicator != null)
        {
            Destroy(selectionIndicator);
            selectionIndicator = null;
        }

        // Hide the unit info panel when nothing is selected
        if (UIManager.Instance != null)
            UIManager.Instance.HideUnitInfoPanel();
    }
    
    /// <summary>
    /// Move the selected unit to the target tile
    /// </summary>
    private void MoveSelectedUnitToTile(int targetTileIndex)
    {
        if (selectedUnit == null)
            return;
        
        // Check if unit can move to target tile
        bool canMove = false;
        string unitName = "";
        
        if (selectedUnit is CombatUnit combatUnit)
        {
            canMove = combatUnit.CanMoveTo(targetTileIndex);
            unitName = combatUnit.data.unitName;
            
            if (canMove)
            {
                combatUnit.MoveTo(targetTileIndex);
}
        }
        else if (selectedUnit is WorkerUnit workerUnit)
        {
            canMove = workerUnit.CanMoveTo(targetTileIndex);
            unitName = workerUnit.data.unitName;
            
            if (canMove)
            {
                workerUnit.MoveTo(targetTileIndex);
}
        }
        
        if (!canMove)
        {
if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"{unitName} cannot move there!");
            }
        }
    }
    
    /// <summary>
    /// Create a visual selection indicator for the selected unit
    /// </summary>
    private void CreateSelectionIndicator()
    {
        if (selectedUnit == null)
            return;
        
        // Simple approach: create a colored sphere as selection indicator
        if (selectionIndicatorPrefab != null)
        {
            selectionIndicator = Instantiate(selectionIndicatorPrefab, selectedUnit.transform);
        }
        else
        {
            // Fallback: create a simple colored sphere and reuse a shared material to avoid allocations
            selectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            selectionIndicator.name = "SelectionIndicator";
            selectionIndicator.transform.SetParent(selectedUnit.transform);
            selectionIndicator.transform.localPosition = Vector3.up * 0.5f;
            selectionIndicator.transform.localScale = Vector3.one * 0.3f;

            var renderer = selectionIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (s_selectionIndicatorMaterial != null)
                    renderer.sharedMaterial = s_selectionIndicatorMaterial;

                // Set color using MaterialPropertyBlock to avoid creating instance materials
                s_selectionMPB.SetColor("_Color", selectedUnitHighlightColor);
                renderer.SetPropertyBlock(s_selectionMPB);
            }

            // Remove collider so it doesn't interfere with clicking
            var collider = selectionIndicator.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
        }
    }
    
    /// <summary>
    /// Get the name of a unit (works for both CombatUnit and WorkerUnit via BaseUnit)
    /// </summary>
    private string GetUnitName(BaseUnit unit)
    {
        if (unit != null)
            return unit.UnitName;
        return "Unknown";
    }
    
    /// <summary>
    /// Get the currently selected unit
    /// </summary>
    public BaseUnit GetSelectedUnit()
    {
        return selectedUnit;
    }
    
    /// <summary>
    /// Check if a unit is currently selected
    /// </summary>
    public bool HasSelectedUnit()
    {
        return selectedUnit != null;
    }

    /// <summary>
    /// Handle space key press to show space travel UI
    /// </summary>
    private void HandleSpaceTravelKey()
    {
        if (selectedUnit == null)
            return;

        // Get current planet index
        int currentPlanetIndex = GameManager.Instance?.currentPlanetIndex ?? 0;

        // Show embark UI
        SpaceEmbarkUI.ShowEmbarkUIForUnit(selectedUnit.gameObject, currentPlanetIndex);
}

    /// <summary>
    /// Handle M key press to show space map
    /// </summary>
    private void HandleSpaceMapKey()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowSpaceMap();
}
        else
        {
            Debug.LogWarning("[UnitSelectionManager] UIManager.Instance is null - cannot open space map");
        }
    }
}
