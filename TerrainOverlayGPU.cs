using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GPU-accelerated terrain overlays (fog of war plus one reusable thematic map-mode layer).
/// Phase 6: Moves visual-only tile state updates to GPU for dramatically faster rendering.
/// 
/// IMPORTANT:
/// - CPU remains authoritative for game state
/// - GPU is used only for visual overlays
/// - No gameplay logic changes
/// </summary>
public class TerrainOverlayGPU : MonoBehaviour
{
    public event System.Action OnMapModeOverlayChanged;
    [Header("References")]
    [Tooltip("TileSystem reference for fog and ownership data (auto-finds if null)")]
    [SerializeField] private TileSystem tileSystem;
    
    [Header("Update Settings")]
    [Tooltip("Update overlays every N frames (0 = every frame, higher = less frequent)")]
    [SerializeField] private int updateIntervalFrames = 0;
    
    [Header("Fog of War")]
    [Tooltip("Enable fog of war overlay")]
    [SerializeField] private bool enableFogOverlay = true;
    [Tooltip("Fog mask texture resolution (should match terrain texture resolution)")]
    [SerializeField] private int fogTextureWidth = 2048;
    [Tooltip("Fog mask texture resolution (should match terrain texture resolution)")]
    [SerializeField] private int fogTextureHeight = 2048;
    
    [Header("Thematic Map Mode Overlay")]
    [Tooltip("Enable the single active thematic overlay")]
    [SerializeField] private bool enableOwnershipOverlay = true;
    
    // Public properties for external access
    public bool EnableFogOverlay => enableFogOverlay;
    public bool EnableOwnershipOverlay => enableOwnershipOverlay;
    #pragma warning disable CS0414 // Retained so existing serialized overlay settings remain compatible.
    [Tooltip("Ownership blend strength (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float ownershipBlend = 0.3f;
    [Tooltip("Ownership mode: 0 = blend with biome color, 1 = replace biome color")]
    [SerializeField] private int ownershipMode = 0;
    #pragma warning restore CS0414
    
    [Header("Compute Shader")]
    [Tooltip("Compute shader for updating overlay textures (TerrainOverlayUpdate.compute)")]
    [SerializeField] private ComputeShader overlayComputeShader;
    
    // Cached resources
    private RenderTexture _fogMaskTexture;
    private RenderTexture _mapModeOverlayTexture;
    private ComputeBuffer _fogBuffer;
    private ComputeBuffer _mapColorBuffer;
    private ComputeBuffer _lutBuffer;
    // Unity ComputeBuffer stride must be a multiple of 4, so fog values (byte[]) are expanded to int[] for GPU upload.
    private int[] _fogIntCache;
    private int[] _cachedLUT;
    private int _cachedLUTWidth;
    private int _cachedLUTHeight;
    private Color[] _mapColorByTile;
    private bool _mapModeActive;
    
    // Dirty tracking
    private HashSet<int> _dirtyTiles = new HashSet<int>();
    
    private void Awake()
    {
        if (tileSystem == null)
        {
            int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
            tileSystem = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        }
    }
    
    private void Update()
    {
        // Update overlays periodically if there are dirty tiles
        if (_dirtyTiles.Count > 0)
        {
            if (updateIntervalFrames <= 0 || Time.frameCount % (updateIntervalFrames + 1) == 0)
            {
                UpdateOverlays();
            }
        }
    }
    
    private void OnDestroy()
    {
        ReleaseResources();
    }
    
    /// <summary>
    /// Initialize overlay system with LUT and texture resolution.
    /// Call this when planet is generated or texture resolution changes.
    /// </summary>
    public void Initialize(int[] lut, int lutWidth, int lutHeight, int textureWidth, int textureHeight)
    {
        _cachedLUT = lut;
        _cachedLUTWidth = lutWidth;
        _cachedLUTHeight = lutHeight;
        
        // IMPORTANT:
        // The overlay compute shaders index into the LUT using (_Width,_Height) and pixelIndex=y*_Width+x.
        // Therefore the overlay textures MUST match the LUT dimensions exactly, or mapping becomes incorrect
        // (and can go out-of-bounds if overlay is larger than LUT).
        //
        // To keep things robust, we treat LUT resolution as the single source of truth and ignore the
        // passed-in textureWidth/textureHeight except for a diagnostic warning.
        if (textureWidth != lutWidth || textureHeight != lutHeight)
        {
            Debug.LogWarning($"[TerrainOverlayGPU] Initialize resolution mismatch: lut={lutWidth}x{lutHeight} vs texture={textureWidth}x{textureHeight}. " +
                             $"Overlays will use LUT resolution ({lutWidth}x{lutHeight}) to stay correct.");
        }
        fogTextureWidth = lutWidth;
        fogTextureHeight = lutHeight;
        
        ReleaseResources();
        CreateOverlayTextures();
        
        // Mark all tiles dirty and update overlays on initialization
        MarkAllTilesDirty();
        UpdateOverlays();
    }
    
    /// <summary>
    /// Mark tiles as dirty (need overlay update).
    /// </summary>
    public void MarkTilesDirty(IEnumerable<int> tiles)
    {
        foreach (var tile in tiles)
        {
            if (tile >= 0)
                _dirtyTiles.Add(tile);
        }
    }
    
    /// <summary>
    /// Mark all tiles as dirty (full overlay update).
    /// </summary>
    public void MarkAllTilesDirty()
    {
        if (tileSystem != null && tileSystem.IsReady())
        {
            int tileCount = tileSystem.GetOwnerArray()?.Length ?? 0;
            for (int i = 0; i < tileCount; i++)
            {
                _dirtyTiles.Add(i);
            }
        }
    }
    
    /// <summary>
    /// Update overlay textures for dirty tiles.
    /// Call this when fog or ownership changes.
    /// </summary>
    public void UpdateOverlays()
    {
        // Ensure we are bound to the current planet's TileSystem (multi-planet gameplay)
        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        if (tileSystem == null || tileSystem.planetIndex != pIndex)
            tileSystem = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;

        if (tileSystem == null || !tileSystem.IsReady())
            return;
        
        if (_cachedLUT == null || _cachedLUT.Length == 0)
            return;
        
        if (overlayComputeShader == null)
        {
            Debug.LogWarning("[TerrainOverlayGPU] Compute shader not assigned!");
            return;
        }
        
        if (_dirtyTiles.Count == 0)
            return;
        
        // Get data from TileSystem
        var ownerArray = tileSystem.GetOwnerArray();
        var fogArray = tileSystem.GetMergedFogArray();
        var ownerColors = tileSystem.GetOwnerColors();
        
        if (ownerArray == null || fogArray == null || ownerColors == null)
            return;
        
        // Ensure buffers exist
        EnsureBuffers(ownerArray.Length, ownerColors.Length);
        
        // Update buffers with latest data
        // Expand fog bytes (0..2) into ints for stride-4 ComputeBuffer upload.
        EnsureFogIntCache(ownerArray.Length);
        for (int i = 0; i < fogArray.Length && i < _fogIntCache.Length; i++)
        {
            _fogIntCache[i] = fogArray[i];
        }
        _fogBuffer.SetData(_fogIntCache);
        if (_mapColorByTile != null && _mapColorByTile.Length == ownerArray.Length) _mapColorBuffer.SetData(_mapColorByTile);
        _lutBuffer.SetData(_cachedLUT);
        
        // Convert dirty tiles to array for compute shader
        int[] dirtyArray = new int[_dirtyTiles.Count];
        int idx = 0;
        foreach (var tile in _dirtyTiles)
        {
            dirtyArray[idx++] = tile;
        }
        
        // Update fog mask if enabled
        if (enableFogOverlay && _fogMaskTexture != null)
        {
            UpdateFogMask(dirtyArray);
        }
        
        // Update ownership overlay if enabled
        if (enableOwnershipOverlay && _mapModeOverlayTexture != null)
        {
            UpdateMapModeOverlay();
        }
        
        // Clear dirty set
        _dirtyTiles.Clear();
    }
    
    /// <summary>
    /// Get fog mask texture (for shader blending).
    /// </summary>
    public RenderTexture GetFogMaskTexture()
    {
        return _fogMaskTexture;
    }
    
    /// <summary>
    /// Get ownership overlay texture (for shader blending).
    /// </summary>
    public RenderTexture GetOwnershipTexture()
    {
        return _mapModeOverlayTexture;
    }

    public RenderTexture GetMapModeOverlayTexture() => _mapModeOverlayTexture;
    public bool IsMapModeOverlayActive => enableOwnershipOverlay && _mapModeActive;

    /// <summary>Bind the controller's reused per-tile color array. The array remains CPU presentation state.</summary>
    public void SetMapModeData(TileSystem source, Color[] colors, bool active, IEnumerable<int> dirtyTiles = null)
    {
        if (source != null) tileSystem = source;
        _mapColorByTile = colors; _mapModeActive = active;
        if (dirtyTiles != null) MarkTilesDirty(dirtyTiles); else MarkAllTilesDirty();
        UpdateOverlays();
        OnMapModeOverlayChanged?.Invoke();
    }
    
    private void CreateOverlayTextures()
    {
        // Create fog mask texture (R8 - single channel for visibility)
        if (_fogMaskTexture != null)
        {
            _fogMaskTexture.Release();
        }
        _fogMaskTexture = new RenderTexture(fogTextureWidth, fogTextureHeight, 0, RenderTextureFormat.R8)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            name = "FogMaskTexture"
        };
        _fogMaskTexture.Create();
        
        // Initialize to fully visible (white = visible)
        RenderTexture.active = _fogMaskTexture;
        GL.Clear(true, true, Color.white);
        RenderTexture.active = null;
        
        // Create ownership overlay texture (RGBA32 - full color overlay)
        if (_mapModeOverlayTexture != null)
        {
            _mapModeOverlayTexture.Release();
        }
        _mapModeOverlayTexture = new RenderTexture(fogTextureWidth, fogTextureHeight, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            name = "MapModeOverlayTexture"
        };
        _mapModeOverlayTexture.Create();
        
        // Initialize to transparent (no ownership overlay)
        RenderTexture.active = _mapModeOverlayTexture;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = null;
    }
    
    private void EnsureBuffers(int tileCount, int ownerColorCount)
    {
        // Fog buffer (uint per tile: 0=hidden, 1=explored, 2=visible).
        // IMPORTANT: Unity does not support ComputeBuffer stride=1, so we cannot upload bytes directly.
        if (_fogBuffer == null || _fogBuffer.count != tileCount)
        {
            _fogBuffer?.Release();
            _fogBuffer = new ComputeBuffer(tileCount, sizeof(int));
        }
        
        // Owner buffer (int per tile: -1=neutral, >=0=civId)
        if (_mapColorBuffer == null || _mapColorBuffer.count != tileCount)
        {
            _mapColorBuffer?.Release();
            _mapColorBuffer = new ComputeBuffer(tileCount, sizeof(float) * 4);
        }
        
        // LUT buffer (int per pixel: pixel -> tile index)
        if (_lutBuffer == null || _lutBuffer.count != _cachedLUT.Length)
        {
            _lutBuffer?.Release();
            _lutBuffer = new ComputeBuffer(_cachedLUT.Length, sizeof(int));
        }
    }

    private void EnsureFogIntCache(int tileCount)
    {
        if (_fogIntCache == null || _fogIntCache.Length != tileCount)
        {
            _fogIntCache = new int[tileCount];
        }
    }
    
    private void UpdateFogMask(int[] dirtyTiles)
    {
        if (overlayComputeShader == null) return;
        
        int kernel = overlayComputeShader.FindKernel("UpdateFogMask");
        if (kernel < 0)
        {
            Debug.LogWarning("[TerrainOverlayGPU] UpdateFogMask kernel not found!");
            return;
        }
        
        // Set buffers
        overlayComputeShader.SetBuffer(kernel, "_PixelToTileLUT", _lutBuffer);
        overlayComputeShader.SetBuffer(kernel, "_FogByTile", _fogBuffer);
        
        // Set output texture
        overlayComputeShader.SetTexture(kernel, "_FogMask", _fogMaskTexture);
        
        // Set parameters
        overlayComputeShader.SetInt("_Width", fogTextureWidth);
        overlayComputeShader.SetInt("_Height", fogTextureHeight);
        overlayComputeShader.SetInt("_TileCount", _fogBuffer.count);
        
        // Dispatch (update ALL pixels - compute shader processes entire texture)
        // Note: We update all pixels because fog/ownership affects the entire visual map
        int threadGroupsX = Mathf.CeilToInt(fogTextureWidth / 8f);
        int threadGroupsY = Mathf.CeilToInt(fogTextureHeight / 8f);
        overlayComputeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
    }
    
    private void UpdateMapModeOverlay()
    {
        if (overlayComputeShader == null) return;
        
        int kernel = overlayComputeShader.FindKernel("UpdateMapModeOverlay");
        if (kernel < 0)
        {
            Debug.LogWarning("[TerrainOverlayGPU] UpdateMapModeOverlay kernel not found!");
            return;
        }
        
        // Set buffers
        overlayComputeShader.SetBuffer(kernel, "_PixelToTileLUT", _lutBuffer);
        overlayComputeShader.SetBuffer(kernel, "_MapColorByTile", _mapColorBuffer);
        
        // Set output texture
        overlayComputeShader.SetTexture(kernel, "_MapModeOverlay", _mapModeOverlayTexture);
        
        // Set parameters
        overlayComputeShader.SetInt("_Width", fogTextureWidth);
        overlayComputeShader.SetInt("_Height", fogTextureHeight);
        overlayComputeShader.SetInt("_TileCount", _mapColorBuffer.count);
        overlayComputeShader.SetInt("_MapModeActive", _mapModeActive ? 1 : 0);
        
        // Dispatch (update ALL pixels - compute shader processes entire texture)
        // Note: We update all pixels because fog/ownership affects the entire visual map
        int threadGroupsX = Mathf.CeilToInt(fogTextureWidth / 8f);
        int threadGroupsY = Mathf.CeilToInt(fogTextureHeight / 8f);
        overlayComputeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
    }
    
    private void ReleaseResources()
    {
        if (_fogMaskTexture != null)
        {
            _fogMaskTexture.Release();
            _fogMaskTexture = null;
        }
        
        if (_mapModeOverlayTexture != null)
        {
            _mapModeOverlayTexture.Release();
            _mapModeOverlayTexture = null;
        }
        
        _fogBuffer?.Release();
        _fogBuffer = null;
        
        _mapColorBuffer?.Release();
        _mapColorBuffer = null;
        
        _lutBuffer?.Release();
        _lutBuffer = null;
    }
}
