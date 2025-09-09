using UnityEngine;
using System;

/// <summary>
/// Unified tile click detection system for the planet.
/// Handles raycasting against the planet and fires events when tiles are clicked.
/// </summary>
public class TileClickDetector : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera; // Assign in inspector or auto-detect
    public PlanetGenerator planetGenerator; // Assign in inspector or auto-detect
    public MoonGenerator moonGenerator; // Optional - for moon tile clicks
    
    [Header("Settings")]
    [Tooltip("Maximum raycast distance")]
    public float maxRaycastDistance = 1000f;
    
    [Tooltip("Layer mask used for tile hover raycasts (exclude Atmosphere layer)")]
    public LayerMask tileRaycastMask = -1;
    
    [Header("Visual Feedback")]
    public GameObject tileHighlightPrefab;
    [Header("Event Hooks")]
    [Tooltip("Optional ScriptableObject event raised when a tile is clicked (no payload)")]
    public GameEvent onTileClickedEvent;
    [Tooltip("Optional ScriptableObject event raised when a tile is hovered (no payload)")]
    public GameEvent onTileHoveredEvent;
    
    // Events for tile interactions
    public static event Action<int, Vector3, bool> OnTileClicked; // (tileIndex, worldPosition, isMoonTile)
    public static event Action<int, Vector3, bool> OnTileHovered; // (tileIndex, worldPosition, isMoonTile)
    public static event Action OnTileExited; // When mouse leaves all tiles
    
    // Private references
    private SphericalHexGrid planetGrid;
    private SphericalHexGrid moonGrid;
    private int lastHoveredTileIndex = -1;
    private bool lastHoverWasMoon = false;
    private GameObject currentHighlight;
    
    void Start()
    {
        // Auto-detect camera if not assigned
        if (mainCamera == null)
            mainCamera = Camera.main;
        
        // Wait for game to be ready before accessing planet generators
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted += InitializeTileDetection;
        }
        else
        {
            Debug.LogWarning("[TileClickDetector] GameManager not found, will not initialize properly");
        }

        // Register service for other systems to reference instead of FindAnyObjectByType
        ServiceRegistry.Register<TileClickDetector>(this);
    }
    
    private void InitializeTileDetection()
    {
        // Now safely access planet generators after game is ready
        if (planetGenerator == null)
            planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
        
        if (moonGenerator == null)
            moonGenerator = GameManager.Instance?.GetCurrentMoonGenerator();
        
        // Get grid references
        if (planetGenerator != null)
            planetGrid = planetGenerator.Grid;
        
        if (moonGenerator != null)
            moonGrid = moonGenerator.Grid;
        
        if (planetGrid == null)
        {
            Debug.LogWarning("[TileClickDetector] PlanetGenerator or its Grid not found after game started!");
        }
        else
        {
            Debug.Log("[TileClickDetector] Successfully initialized tile detection after game started");
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted -= InitializeTileDetection;
        }
        
    // Clear static events to prevent memory leaks
    OnTileClicked = null;
    OnTileHovered = null;
    OnTileExited = null;

    // Unregister service
    ServiceRegistry.Unregister<TileClickDetector>();
    }
    
    void Update()
    {
        // Handle left click
        if (Input.GetMouseButtonDown(0))
        {
            HandleTileClick();
        }
        
        // Handle hover detection
        HandleTileHover();
    }
    
    /// <summary>
    /// Detect and handle tile clicks
    /// </summary>
    private void HandleTileClick()
    {
        var hitInfo = GetMouseHitInfo();
        if (hitInfo.hit)
        {
            int tileIndex = hitInfo.tileIndex;
            if (tileIndex >= 0)
            {
                Debug.Log($"[TileClickDetector] Clicked tile {tileIndex} at {hitInfo.tileTransform.position}");

                var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
                if (tileData != null)
                    Debug.Log($"[TileClickDetector] Tile Data - Biome: {tileData.biome}, Elevation: {tileData.elevation:F2}");

                OnTileClicked?.Invoke(tileIndex, hitInfo.tileTransform.position, hitInfo.isMoon);
                onTileClickedEvent?.Raise();

                if (tileHighlightPrefab != null)
                {
                    Vector3 worldPos = hitInfo.tileTransform.position;

                    if (currentHighlight == null)
                        currentHighlight = Instantiate(tileHighlightPrefab, worldPos, Quaternion.identity);
                    else
                        currentHighlight.transform.position = worldPos;

                    currentHighlight.transform.up = hitInfo.tileTransform.up;
                    currentHighlight.SetActive(true);
                }
            }
        }
    }
    
    /// <summary>
    /// Handle tile hover detection for UI feedback
    /// </summary>
    private void HandleTileHover()
    {
        var hitInfo = GetMouseHitInfo();

        if (hitInfo.hit)
        {
            int tileIndex = hitInfo.tileIndex;

            if (tileIndex >= 0 && (tileIndex != lastHoveredTileIndex || hitInfo.isMoon != lastHoverWasMoon))
            {
                lastHoveredTileIndex = tileIndex;
                lastHoverWasMoon = hitInfo.isMoon;

                OnTileHovered?.Invoke(tileIndex, hitInfo.tileTransform.position, hitInfo.isMoon);
                    onTileHoveredEvent?.Raise();

                if (tileHighlightPrefab != null)
                {
                    Vector3 worldPos = hitInfo.tileTransform.position;

                    if (currentHighlight == null)
                        currentHighlight = Instantiate(tileHighlightPrefab, worldPos, Quaternion.identity);
                    else
                        currentHighlight.transform.position = worldPos;

                    currentHighlight.transform.up = hitInfo.tileTransform.up;
                    currentHighlight.SetActive(true);
                }
            }
        }
        else
        {
            if (lastHoveredTileIndex >= 0)
            {
                lastHoveredTileIndex = -1;
                lastHoverWasMoon = false;
                OnTileExited?.Invoke();

                if (currentHighlight != null)
                    currentHighlight.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Public API for other systems to perform the canonical tile raycast and hit resolution.
    /// Returns the same tuple as internal GetMouseHitInfo but for an arbitrary screen point.
    /// </summary>
    public (bool hit, int tileIndex, Transform tileTransform, bool isMoon) PerformTileRaycast(Vector2 screenPoint)
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        Ray ray = mainCamera.ScreenPointToRay(screenPoint);
        if (Physics.Raycast(ray, out RaycastHit hitInfo, maxRaycastDistance, tileRaycastMask))
        {
            TileIndexHolder holder = hitInfo.collider.GetComponentInParent<TileIndexHolder>();
            if (holder != null)
            {
                bool isMoonTile = moonGenerator != null && IsPartOfObject(holder.transform, moonGenerator.transform);
                return (true, holder.tileIndex, holder.transform, isMoonTile);
            }
        }
        return (false, -1, null, false);
    }
    
    /// <summary>
    /// Perform raycast and get hit information
    /// </summary>
    private (bool hit, int tileIndex, Transform tileTransform, bool isMoon) GetMouseHitInfo()
    {
        if (mainCamera == null)
            return (false, -1, null, false);

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hitInfo, maxRaycastDistance, tileRaycastMask))
        {
            TileIndexHolder holder = hitInfo.collider.GetComponentInParent<TileIndexHolder>();
            if (holder != null)
            {
                bool isMoonTile = moonGenerator != null && IsPartOfObject(holder.transform, moonGenerator.transform);
                return (true, holder.tileIndex, holder.transform, isMoonTile);
            }
        }

        return (false, -1, null, false);
    }
    
    /// <summary>
    /// Check if a transform is part of a parent object (including the parent itself)
    /// </summary>
    private bool IsPartOfObject(Transform child, Transform parent)
    {
        if (child == null || parent == null)
            return false;
            
        Transform current = child;
        while (current != null)
        {
            if (current == parent)
                return true;
            current = current.parent;
        }
        
        return false;
    }
    
    
    /// <summary>
    /// Public method to manually trigger a tile click (for testing or external systems)
    /// </summary>
    public void TriggerTileClick(int tileIndex, bool isMoonTile = false)
    {
        Vector3 worldPosition = Vector3.zero;
        
        if (isMoonTile && moonGrid != null && moonGenerator != null)
        {
            if (tileIndex >= 0 && tileIndex < moonGrid.tileCenters.Length)
            {
                worldPosition = moonGenerator.transform.TransformPoint(moonGrid.tileCenters[tileIndex]);
            }
        }
        else if (!isMoonTile && planetGrid != null && planetGenerator != null)
        {
            if (tileIndex >= 0 && tileIndex < planetGrid.tileCenters.Length)
            {
                worldPosition = planetGenerator.transform.TransformPoint(planetGrid.tileCenters[tileIndex]);
            }
        }
        
        OnTileClicked?.Invoke(tileIndex, worldPosition, isMoonTile);
    }
    
    /// <summary>
    /// Get information about the currently hovered tile
    /// </summary>
    public (int tileIndex, bool isMoon) GetCurrentHoveredTile()
    {
        return (lastHoveredTileIndex, lastHoverWasMoon);
    }
} 