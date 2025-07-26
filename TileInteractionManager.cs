using UnityEngine;

/// <summary>
/// Unified tile interaction system that handles all raycast-to-tile operations.
/// Replaces multiple disconnected raycast systems with a single, efficient solution.
/// </summary>
public static class TileInteractionManager
{
    /// <summary>
    /// Result of a tile raycast operation
    /// </summary>
    public struct TileHitInfo
    {
        public bool hit;                    // Whether we hit a tile
        public int tileIndex;               // Index of the hit tile
        public Vector3 worldPosition;       // World position of the hit
        public GameObject tileObject;       // The tile GameObject that was hit
        public TileIndexHolder tileHolder;  // Direct reference to the tile holder
        public HexTileData tileData;        // The tile's data
        public bool isMoonTile;             // Whether this is a moon tile
        public Collider hitCollider;       // The collider that was hit
        
        public static TileHitInfo Miss => new TileHitInfo { hit = false, tileIndex = -1 };
    }
    
    /// <summary>
    /// Get tile information from mouse position using the main camera
    /// </summary>
    public static TileHitInfo GetTileAtMousePosition(Camera camera = null, LayerMask layerMask = -1)
    {
        if (camera == null)
            camera = Camera.main;
            
        if (camera == null)
            return TileHitInfo.Miss;
            
        Ray ray = camera.ScreenPointToRay(Input.mousePosition);
        return GetTileAtRay(ray, layerMask);
    }
    
    /// <summary>
    /// Get tile information from a world position
    /// </summary>
    public static TileHitInfo GetTileAtWorldPosition(Vector3 worldPosition, Camera camera = null, LayerMask layerMask = -1)
    {
        if (camera == null)
            camera = Camera.main;
            
        if (camera == null)
            return TileHitInfo.Miss;
            
        Vector3 screenPoint = camera.WorldToScreenPoint(worldPosition);
        Ray ray = camera.ScreenPointToRay(screenPoint);
        return GetTileAtRay(ray, layerMask);
    }
    
    /// <summary>
    /// Get tile information from a ray (most flexible method)
    /// </summary>
    public static TileHitInfo GetTileAtRay(Ray ray, LayerMask layerMask = -1, float maxDistance = 1000f)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, layerMask))
        {
            return ProcessRaycastHit(hit);
        }
        
        return TileHitInfo.Miss;
    }
    
    /// <summary>
    /// Get multiple tiles along a ray (for line-of-sight, area effects, etc.)
    /// </summary>
    public static TileHitInfo[] GetTilesAlongRay(Ray ray, LayerMask layerMask = -1, float maxDistance = 1000f)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, layerMask);
        TileHitInfo[] tileHits = new TileHitInfo[hits.Length];
        
        for (int i = 0; i < hits.Length; i++)
        {
            tileHits[i] = ProcessRaycastHit(hits[i]);
        }
        
        return tileHits;
    }
    
    /// <summary>
    /// Process a RaycastHit and extract tile information
    /// </summary>
    private static TileHitInfo ProcessRaycastHit(RaycastHit hit)
    {
        TileHitInfo tileHit = new TileHitInfo
        {
            hit = true,
            worldPosition = hit.point,
            hitCollider = hit.collider,
            tileObject = hit.collider.gameObject
        };
        
        // Try to get TileIndexHolder from the hit object or its parents
        TileIndexHolder tileHolder = hit.collider.GetComponent<TileIndexHolder>();
        if (tileHolder == null)
        {
            tileHolder = hit.collider.GetComponentInParent<TileIndexHolder>();
        }
        
        if (tileHolder != null)
        {
            tileHit.tileHolder = tileHolder;
            tileHit.tileIndex = tileHolder.tileIndex;
            tileHit.isMoonTile = tileHolder.isMoonTile;
            tileHit.tileData = tileHolder.GetTileData();
        }
        else
        {
            // Fallback: try to determine tile index using legacy methods
            tileHit.tileIndex = GetTileIndexFromPosition(hit.point);
            tileHit.isMoonTile = IsPositionOnMoon(hit.point);
            
            // Get tile data using generators
            if (tileHit.tileIndex >= 0)
            {
                if (tileHit.isMoonTile)
                {
                    var moonGen = Object.FindFirstObjectByType<MoonGenerator>();
                    if (moonGen != null)
                        tileHit.tileData = moonGen.GetHexTileData(tileHit.tileIndex);
                }
                else
                {
                    var planetGen = Object.FindFirstObjectByType<PlanetGenerator>();
                    if (planetGen != null)
                        tileHit.tileData = planetGen.GetHexTileData(tileHit.tileIndex);
                }
            }
        }
        
        return tileHit;
    }
    
    /// <summary>
    /// Legacy fallback: determine tile index from world position
    /// </summary>
    private static int GetTileIndexFromPosition(Vector3 worldPosition)
    {
        // Try planet first
        var planetGen = Object.FindFirstObjectByType<PlanetGenerator>();
        if (planetGen != null && planetGen.Grid != null)
        {
            Vector3 localDir = (worldPosition - planetGen.transform.position).normalized;
            int tileIndex = planetGen.Grid.GetTileAtPosition(localDir);
            if (tileIndex >= 0)
                return tileIndex;
        }
        
        // Try moon if planet failed
        var moonGen = Object.FindFirstObjectByType<MoonGenerator>();
        if (moonGen != null && moonGen.Grid != null)
        {
            Vector3 localDir = (worldPosition - moonGen.transform.position).normalized;
            return moonGen.Grid.GetTileAtPosition(localDir);
        }
        
        return -1;
    }
    
    /// <summary>
    /// Legacy fallback: determine if position is on moon or planet
    /// </summary>
    private static bool IsPositionOnMoon(Vector3 worldPosition)
    {
        var moonGen = Object.FindFirstObjectByType<MoonGenerator>();
        var planetGen = Object.FindFirstObjectByType<PlanetGenerator>();
        
        if (moonGen == null)
            return false;
            
        if (planetGen == null)
            return true;
            
        // Check which is closer
        float moonDistance = Vector3.Distance(worldPosition, moonGen.transform.position);
        float planetDistance = Vector3.Distance(worldPosition, planetGen.transform.position);
        
        return moonDistance < planetDistance;
    }
    
    /// <summary>
    /// Check if a tile is valid for a specific operation (walkable, buildable, etc.)
    /// </summary>
    public static bool IsTileValidFor(TileHitInfo tileHit, TileValidationType validationType)
    {
        if (!tileHit.hit || tileHit.tileIndex < 0)
            return false;
            
        switch (validationType)
        {
            case TileValidationType.Movement:
                return tileHit.tileData.isPassable && !tileHit.tileData.biome.Equals(Biome.Mountain);
                
            case TileValidationType.Building:
                return tileHit.tileData.isLand && tileHit.tileData.isPassable;
                
            case TileValidationType.Naval:
                return !tileHit.tileData.isLand;
                
            case TileValidationType.Any:
            default:
                return true;
        }
    }
}

/// <summary>
/// Types of tile validation for different operations
/// </summary>
public enum TileValidationType
{
    Any,        // Any tile is valid
    Movement,   // Valid for unit movement
    Building,   // Valid for construction
    Naval       // Valid for naval units
}
