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
    
    [Tooltip("Layer mask for planet collision")]
    public LayerMask planetLayerMask = -1;
    
    [Header("Visual Feedback")]
    public GameObject tileHighlightPrefab;
    
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
        // Auto-detect references if not assigned
        if (mainCamera == null)
            mainCamera = Camera.main;
        
        if (planetGenerator == null)
            planetGenerator = FindAnyObjectByType<PlanetGenerator>();
        
        if (moonGenerator == null)
            moonGenerator = FindAnyObjectByType<MoonGenerator>();
        
        // Get grid references
        if (planetGenerator != null)
            planetGrid = planetGenerator.Grid;
        
        if (moonGenerator != null)
            moonGrid = moonGenerator.Grid;
        
        if (planetGrid == null)
        {
            Debug.LogWarning("[TileClickDetector] PlanetGenerator or its Grid not found!");
        }
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
            int tileIndex = GetTileIndexAtPosition(hitInfo.worldPosition, hitInfo.grid, hitInfo.transform);
            
            if (tileIndex >= 0)
            {
                Debug.Log($"[TileClickDetector] Clicked tile {tileIndex} at {hitInfo.worldPosition}");
                
                // Get tile data for debug info
                var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(tileIndex);
                if (tileData != null)
                {
                    Debug.Log($"[TileClickDetector] Tile Data - Biome: {tileData.biome}, Elevation: {tileData.elevation:F2}");
                }
                
                // Fire the click event
                OnTileClicked?.Invoke(tileIndex, hitInfo.worldPosition, hitInfo.isMoon);
                
                // Update highlight
                if (tileHighlightPrefab != null)
                {
                    Vector3 worldPos = hitInfo.transform.TransformPoint(hitInfo.grid.tileCenters[tileIndex]);

                    if (currentHighlight == null)
                    {
                        currentHighlight = Instantiate(tileHighlightPrefab, worldPos, Quaternion.identity);
                    }
                    else
                    {
                        currentHighlight.transform.position = worldPos;
                    }

                    currentHighlight.transform.up = (currentHighlight.transform.position - hitInfo.transform.position).normalized;
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
            int tileIndex = GetTileIndexAtPosition(hitInfo.worldPosition, hitInfo.grid, hitInfo.transform);
            
            // Check if we're hovering over a different tile
            if (tileIndex >= 0 && (tileIndex != lastHoveredTileIndex || hitInfo.isMoon != lastHoverWasMoon))
            {
                lastHoveredTileIndex = tileIndex;
                lastHoverWasMoon = hitInfo.isMoon;
                
                // Fire hover event
                OnTileHovered?.Invoke(tileIndex, hitInfo.worldPosition, hitInfo.isMoon);
            }
        }
        else
        {
            // Not hovering over any tile
            if (lastHoveredTileIndex >= 0)
            {
                lastHoveredTileIndex = -1;
                lastHoverWasMoon = false;
                OnTileExited?.Invoke();
            }
        }
    }
    
    /// <summary>
    /// Perform raycast and get hit information
    /// </summary>
    private (bool hit, Vector3 worldPosition, SphericalHexGrid grid, Transform transform, bool isMoon) GetMouseHitInfo()
    {
        if (mainCamera == null)
            return (false, Vector3.zero, null, null, false);
        
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out RaycastHit hitInfo, maxRaycastDistance, planetLayerMask))
        {
            // Check if we hit the planet
            if (planetGenerator != null && IsPartOfObject(hitInfo.transform, planetGenerator.transform))
            {
                return (true, hitInfo.point, planetGrid, planetGenerator.transform, false);
            }
            
            // Check if we hit the moon
            if (moonGenerator != null && IsPartOfObject(hitInfo.transform, moonGenerator.transform))
            {
                return (true, hitInfo.point, moonGrid, moonGenerator.transform, true);
            }
        }
        
        return (false, Vector3.zero, null, null, false);
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
    /// Get the tile index at a world position
    /// </summary>
    private int GetTileIndexAtPosition(Vector3 worldPosition, SphericalHexGrid grid, Transform planetTransform)
    {
        if (grid == null || planetTransform == null)
            return -1;
        
        // Convert world position to local direction from planet center
        Vector3 planetCenter = planetTransform.position;
        Vector3 localDirection = (worldPosition - planetCenter).normalized;
        
        return grid.GetTileAtPosition(localDirection);
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
    
    void OnDestroy()
    {
        // Clear static events to prevent memory leaks
        OnTileClicked = null;
        OnTileHovered = null;
        OnTileExited = null;
    }
} 