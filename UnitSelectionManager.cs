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
    
    // Currently selected unit
    private MonoBehaviour selectedUnit; // Can be CombatUnit or WorkerUnit
    private GameObject selectionIndicator;
    // Cached highlight/selection materials to avoid allocations
    private static Material s_selectionIndicatorMaterial;
    private static UnityEngine.MaterialPropertyBlock s_selectionMPB;
    
    // References
    private Camera mainCamera;
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
            mainCamera = FindAnyObjectByType<Camera>();
        
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
        // Left-click selection is routed via TileSystem; select/deselect units here
        var clickedUnit = GetUnitAtPosition(worldPos);
        if (clickedUnit != null) SelectUnit(clickedUnit); else DeselectUnit();
        // Note: Right-click movement remains handled in Update() to detect mouse button 1
    }
    
    /// <summary>
    /// Handle mouse input for unit selection and movement
    /// </summary>
    private void HandleInput()
    {
        // Ignore input if over UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;
        
        // Right click: Move selected unit
        if (Input.GetMouseButtonDown(1))
        {
            HandleRightClick();
        }
        
        // Space key: Show space travel UI for selected unit
        if (Input.GetKeyDown(KeyCode.Space) && HasSelectedUnit())
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
            Debug.Log("[UnitSelectionManager] No unit selected for movement command");
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
    /// Get mouse raycast hit information
    /// </summary>
    private (bool hit, Vector3 worldPosition, int tileIndex) GetMouseHitInfo()
    {
        if (mainCamera == null)
            return (false, Vector3.zero, -1);

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        // Use TileSystem raycast mask if available; otherwise default
        int layerMask = Physics.DefaultRaycastLayers;
        if (TileSystem.Instance != null)
            layerMask = TileSystem.Instance.tileRaycastMask.value;

        if (Physics.Raycast(ray, out RaycastHit hitInfo, Mathf.Infinity, layerMask))
        {
            var holder = hitInfo.collider.GetComponentInParent<TileIndexHolder>();
            if (holder != null)
                return (true, hitInfo.point, holder.tileIndex);
        }

        return (false, Vector3.zero, -1);
    }
    
    
    /// <summary>
    /// Find a unit at the given world position
    /// </summary>
    private MonoBehaviour GetUnitAtPosition(Vector3 worldPosition)
    {
        // Use a small sphere to detect units near the click position
        Collider[] colliders = Physics.OverlapSphere(worldPosition, 0.5f);
        
        foreach (var collider in colliders)
        {
            // Check for CombatUnit
            var combatUnit = collider.GetComponentInParent<CombatUnit>();
            if (combatUnit != null)
                return combatUnit;
            
            // Check for WorkerUnit
            var workerUnit = collider.GetComponentInParent<WorkerUnit>();
            if (workerUnit != null)
                return workerUnit;
        }
        
        return null;
    }
    
    /// <summary>
    /// Select a unit
    /// </summary>
    public void SelectUnit(MonoBehaviour unit)
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
        
        Debug.Log($"[UnitSelectionManager] Selected unit: {GetUnitName(unit)}");
    }
    
    /// <summary>
    /// Deselect the current unit
    /// </summary>
    public void DeselectUnit()
    {
        if (selectedUnit == null)
            return;
        
        Debug.Log($"[UnitSelectionManager] Deselected unit: {GetUnitName(selectedUnit)}");

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
                Debug.Log($"[UnitSelectionManager] Ordered {unitName} to move to tile {targetTileIndex}");
            }
        }
        else if (selectedUnit is WorkerUnit workerUnit)
        {
            canMove = workerUnit.CanMoveTo(targetTileIndex);
            unitName = workerUnit.data.unitName;
            
            if (canMove)
            {
                workerUnit.MoveTo(targetTileIndex);
                Debug.Log($"[UnitSelectionManager] Ordered {unitName} to move to tile {targetTileIndex}");
            }
        }
        
        if (!canMove)
        {
            Debug.Log($"[UnitSelectionManager] {unitName} cannot move to tile {targetTileIndex}");
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
    /// Get the name of a unit (works for both CombatUnit and WorkerUnit)
    /// </summary>
    private string GetUnitName(MonoBehaviour unit)
    {
        if (unit is CombatUnit combatUnit)
            return combatUnit.data.unitName;
        else if (unit is WorkerUnit workerUnit)
            return workerUnit.data.unitName;
        else
            return unit.name;
    }
    
    /// <summary>
    /// Get the currently selected unit
    /// </summary>
    public MonoBehaviour GetSelectedUnit()
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
        
        Debug.Log($"[UnitSelectionManager] Showing space travel UI for {GetUnitName(selectedUnit)} on Planet {currentPlanetIndex}");
    }

    /// <summary>
    /// Handle M key press to show space map
    /// </summary>
    private void HandleSpaceMapKey()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowSpaceMap();
            Debug.Log("[UnitSelectionManager] Opening Space Map (M key pressed)");
        }
        else
        {
            Debug.LogWarning("[UnitSelectionManager] UIManager.Instance is null - cannot open space map");
        }
    }
}