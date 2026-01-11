using UnityEngine;
using System;

/// <summary>
/// Detects when the mouse hovers over tiles using WorldPicker.
/// Fires events for other systems (highlighter, info panel) to respond to.
/// </summary>
public class TileHoverSystem : MonoBehaviour
{
    public static TileHoverSystem Instance { get; private set; }
    
    [Header("Settings")]
    [SerializeField] private bool enableHover = true;
    [SerializeField] private float hoverDelay = 0.05f; // Small delay to avoid flickering
    
    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    
    // Events
    public event Action<int, HexTileData, Vector3> OnTileHoverEnter;
    public event Action<int> OnTileHoverExit;
    public event Action<int, HexTileData, Vector3> OnTileHoverStay;
    
    // State
    private int currentHoveredTile = -1;
    private int lastHoveredTile = -1;
    private float hoverTimer = 0f;
    private int pendingTile = -1;
    private Vector3 lastHitPoint;
    
    // Cached references
    private WorldPicker worldPicker;
    private TileSystem tileSystem;
    
    // Public accessors
    public int CurrentHoveredTile => currentHoveredTile;
    public bool IsHovering => currentHoveredTile >= 0;
    public bool EnableHover
    {
        get => enableHover;
        set
        {
            enableHover = value;
            if (!value) ClearHover();
        }
    }
    
    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        worldPicker = FindAnyObjectByType<WorldPicker>();
        tileSystem = TileSystem.Instance;
    }
    
    private void Update()
    {
        if (!enableHover) return;
        if (worldPicker == null) 
        {
            worldPicker = FindAnyObjectByType<WorldPicker>();
            if (worldPicker == null) return;
        }
        if (tileSystem == null)
        {
            tileSystem = TileSystem.Instance;
            if (tileSystem == null) return;
        }
        
        // Check if mouse is over UI
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            ClearHover();
            return;
        }
        
        // Try to pick tile under mouse
        if (worldPicker.TryPickTileIndex(Input.mousePosition, out int tileIndex, out Vector3 hitPoint))
        {
            lastHitPoint = hitPoint;
            
            // Same tile as before
            if (tileIndex == currentHoveredTile)
            {
                // Fire stay event
                var tileData = tileSystem.GetTileData(tileIndex);
                OnTileHoverStay?.Invoke(tileIndex, tileData, hitPoint);
                return;
            }
            
            // New tile - use delay to avoid flicker
            if (tileIndex != pendingTile)
            {
                pendingTile = tileIndex;
                hoverTimer = 0f;
            }
            else
            {
                hoverTimer += Time.deltaTime;
                if (hoverTimer >= hoverDelay)
                {
                    SetHoveredTile(tileIndex, hitPoint);
                }
            }
        }
        else
        {
            // No tile under mouse
            ClearHover();
        }
    }
    
    private void SetHoveredTile(int tileIndex, Vector3 hitPoint)
    {
        // Exit previous tile
        if (currentHoveredTile >= 0 && currentHoveredTile != tileIndex)
        {
            if (debugLog) Debug.Log($"[TileHoverSystem] Exit tile {currentHoveredTile}");
            OnTileHoverExit?.Invoke(currentHoveredTile);
        }
        
        lastHoveredTile = currentHoveredTile;
        currentHoveredTile = tileIndex;
        pendingTile = -1;
        
        // Get tile data and fire enter event
        var tileData = tileSystem?.GetTileData(tileIndex);
        if (debugLog) Debug.Log($"[TileHoverSystem] Enter tile {tileIndex}, Biome: {tileData?.biome}");
        OnTileHoverEnter?.Invoke(tileIndex, tileData, hitPoint);
    }
    
    private void ClearHover()
    {
        if (currentHoveredTile >= 0)
        {
            if (debugLog) Debug.Log($"[TileHoverSystem] Exit tile {currentHoveredTile}");
            OnTileHoverExit?.Invoke(currentHoveredTile);
        }
        
        lastHoveredTile = currentHoveredTile;
        currentHoveredTile = -1;
        pendingTile = -1;
        hoverTimer = 0f;
    }
    
    /// <summary>
    /// Get the world position of the tile center for the currently hovered tile.
    /// </summary>
    public Vector3 GetHoveredTileCenter()
    {
        if (currentHoveredTile < 0) return Vector3.zero;
        
        var gen = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (gen?.Grid?.tileCenters != null && currentHoveredTile < gen.Grid.tileCenters.Length)
        {
            return gen.Grid.tileCenters[currentHoveredTile];
        }
        
        return lastHitPoint;
    }
    
    private void OnDisable()
    {
        ClearHover();
    }
}
