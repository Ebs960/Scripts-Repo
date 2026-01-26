using UnityEngine;
using System;

/// <summary>
/// Hover event adapter.
/// IMPORTANT: This system does NOT perform tile picking/raycasting.
/// Tile picking is centralized in TileSystem; this script translates TileSystem hover events
/// into richer Enter/Exit/Stay signals (with optional delay) for legacy consumers.
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
        tileSystem = TileSystem.Instance;
    }
    
    private void Update()
    {
        if (!enableHover) return;

        if (tileSystem == null)
        {
            tileSystem = TileSystem.Instance;
        }

        // Drive "stay" events without re-picking (TileSystem is authoritative for picking).
        if (tileSystem != null && currentHoveredTile >= 0)
        {
            var tileData = tileSystem.GetTileData(currentHoveredTile);
            OnTileHoverStay?.Invoke(currentHoveredTile, tileData, lastHitPoint);
        }

        // If a tile is pending (due to hoverDelay), count up and promote it when ready.
        if (pendingTile >= 0)
        {
            hoverTimer += Time.deltaTime;
            if (hoverTimer >= hoverDelay)
            {
                SetHoveredTile(pendingTile, lastHitPoint);
            }
        }
    }
    
    private void OnEnable()
    {
        tileSystem = TileSystem.Instance;
        if (tileSystem != null)
        {
            tileSystem.OnTileHovered += HandleTileHovered;
            tileSystem.OnTileHoverExited += HandleTileHoverExited;
        }
    }

    private void OnDisable()
    {
        if (tileSystem != null)
        {
            tileSystem.OnTileHovered -= HandleTileHovered;
            tileSystem.OnTileHoverExited -= HandleTileHoverExited;
        }
        ClearHover();
    }

    private void HandleTileHovered(int tileIndex, Vector3 worldPos)
    {
        if (!enableHover) return;

        lastHitPoint = worldPos;

        // Same tile: no enter/exit, stay is handled in Update.
        if (tileIndex == currentHoveredTile) return;

        // New tile - use delay to avoid flicker.
        if (tileIndex != pendingTile)
        {
            pendingTile = tileIndex;
            hoverTimer = 0f;
        }
    }

    private void HandleTileHoverExited()
    {
        if (!enableHover) return;
        ClearHover();
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
    
    // OnDisable handled above (unsubscribe + ClearHover)
}
