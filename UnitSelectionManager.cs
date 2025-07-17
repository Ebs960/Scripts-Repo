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
    
    // References
    private SphericalHexGrid planetGrid;
    private SphericalHexGrid moonGrid;
    private Camera mainCamera;
    
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
            
        // Find grids
        var planetGen = FindAnyObjectByType<PlanetGenerator>();
        if (planetGen != null)
            planetGrid = planetGen.Grid;
            
        var moonGen = FindAnyObjectByType<MoonGenerator>();
        if (moonGen != null)
            moonGrid = moonGen.Grid;
    }
    
    void Update()
    {
        HandleInput();
    }
    
    /// <summary>
    /// Handle mouse input for unit selection and movement
    /// </summary>
    private void HandleInput()
    {
        // Ignore input if over UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;
        
        // Left click: Select unit
        if (Input.GetMouseButtonDown(0))
        {
            HandleLeftClick();
        }
        
        // Right click: Move selected unit
        if (Input.GetMouseButtonDown(1))
        {
            HandleRightClick();
        }
    }
    
    /// <summary>
    /// Handle left-click for unit selection
    /// </summary>
    private void HandleLeftClick()
    {
        var hitInfo = GetMouseHitInfo();
        if (hitInfo.hit)
        {
            // Check if we clicked on a unit
            var clickedUnit = GetUnitAtPosition(hitInfo.worldPosition);
            if (clickedUnit != null)
            {
                SelectUnit(clickedUnit);
            }
            else
            {
                // Clicked on empty tile, deselect
                DeselectUnit();
            }
        }
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
        
        var hitInfo = GetMouseHitInfo();
        if (hitInfo.hit)
        {
            // Get the tile index at the clicked position
            int targetTileIndex = GetTileIndexAtPosition(hitInfo.worldPosition, hitInfo.grid);
            
            if (targetTileIndex >= 0)
            {
                // Issue move command to selected unit
                MoveSelectedUnitToTile(targetTileIndex);
            }
        }
    }
    
    /// <summary>
    /// Get mouse raycast hit information
    /// </summary>
    private (bool hit, Vector3 worldPosition, SphericalHexGrid grid) GetMouseHitInfo()
    {
        if (mainCamera == null)
            return (false, Vector3.zero, null);
        
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out RaycastHit hitInfo))
        {
            var planetGenerator = GameManager.Instance.planetGenerator;
            var moonGenerator = GameManager.Instance.moonGenerator;

            if (planetGenerator != null && hitInfo.transform == planetGenerator.transform)
            {
                return (true, hitInfo.point, planetGrid);
            }
            if (moonGenerator != null && hitInfo.transform == moonGenerator.transform)
            {
                 return (true, hitInfo.point, moonGrid);
            }
        }
        
        return (false, Vector3.zero, null);
    }
    
    /// <summary>
    /// Get the tile index at a world position
    /// </summary>
    private int GetTileIndexAtPosition(Vector3 worldPosition, SphericalHexGrid grid)
    {
        if (grid == null)
            return -1;

        Vector3 planetCenter = Vector3.zero;
        if (grid == planetGrid && GameManager.Instance.planetGenerator != null)
            planetCenter = GameManager.Instance.planetGenerator.transform.position;
        else if (grid == moonGrid && GameManager.Instance.moonGenerator != null)
            planetCenter = GameManager.Instance.moonGenerator.transform.position;

        Vector3 localDirection = (worldPosition - planetCenter).normalized;
        return grid.GetTileAtPosition(localDirection);
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
            // Fallback: create a simple colored sphere
            selectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            selectionIndicator.name = "SelectionIndicator";
            selectionIndicator.transform.SetParent(selectedUnit.transform);
            selectionIndicator.transform.localPosition = Vector3.up * 0.5f;
            selectionIndicator.transform.localScale = Vector3.one * 0.3f;
            
            // Make it transparent and colored
            var renderer = selectionIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = selectedUnitHighlightColor;
                material.SetFloat("_Mode", 3); // Transparent mode
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
                renderer.material = material;
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
} 