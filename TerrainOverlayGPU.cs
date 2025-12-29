using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GPU-accelerated terrain overlays (fog of war and ownership).
/// Phase 6: Moves visual-only tile state updates to GPU for dramatically faster rendering.
/// 
/// IMPORTANT:
/// - CPU remains authoritative for game state
/// - GPU is used only for visual overlays
/// - No gameplay logic changes
/// </summary>
public class TerrainOverlayGPU : MonoBehaviour
{
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
    [SerializeField] private int fogTextureHeight = 1024;
    
    [Header("Ownership Overlay")]
    [Tooltip("Enable ownership overlay")]
    [SerializeField] private bool enableOwnershipOverlay = true;
    
    // Public properties for external access
    public bool EnableFogOverlay => enableFogOverlay;
    public bool EnableOwnershipOverlay => enableOwnershipOverlay;
    [Tooltip("Ownership blend strength (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float ownershipBlend = 0.3f;
    [Tooltip("Ownership mode: 0 = blend with biome color, 1 = replace biome color")]
    [SerializeField] private int ownershipMode = 0;
    
    [Header("Compute Shader")]
    [Tooltip("Compute shader for updating overlay textures (TerrainOverlayUpdate.compute)")]
    [SerializeField] private ComputeShader overlayComputeShader;
    
    // Cached resources
    private RenderTexture _fogMaskTexture;
    private RenderTexture _ownershipTexture;
    private ComputeBuffer _fogBuffer;
    private ComputeBuffer _ownerBuffer;
    private ComputeBuffer _ownerColorBuffer;
    private ComputeBuffer _lutBuffer;
    private int[] _cachedLUT;
    private int _cachedLUTWidth;
    private int _cachedLUTHeight;
    
    // Dirty tracking
    private HashSet<int> _dirtyTiles = new HashSet<int>();
    
    private void Awake()
    {
        if (tileSystem == null)
            tileSystem = FindAnyObjectByType<TileSystem>();
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
        fogTextureWidth = textureWidth;
        fogTextureHeight = textureHeight;
        
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
        _ownerBuffer.SetData(ownerArray);
        _fogBuffer.SetData(fogArray);
        _ownerColorBuffer.SetData(ownerColors);
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
        if (enableOwnershipOverlay && _ownershipTexture != null)
        {
            UpdateOwnershipOverlay(dirtyArray, ownerColors.Length);
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
        return _ownershipTexture;
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
        if (_ownershipTexture != null)
        {
            _ownershipTexture.Release();
        }
        _ownershipTexture = new RenderTexture(fogTextureWidth, fogTextureHeight, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            name = "OwnershipTexture"
        };
        _ownershipTexture.Create();
        
        // Initialize to transparent (no ownership overlay)
        RenderTexture.active = _ownershipTexture;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = null;
    }
    
    private void EnsureBuffers(int tileCount, int ownerColorCount)
    {
        // Fog buffer (byte per tile: 0=hidden, 1=explored, 2=visible)
        if (_fogBuffer == null || _fogBuffer.count != tileCount)
        {
            _fogBuffer?.Release();
            _fogBuffer = new ComputeBuffer(tileCount, sizeof(byte));
        }
        
        // Owner buffer (int per tile: -1=neutral, >=0=civId)
        if (_ownerBuffer == null || _ownerBuffer.count != tileCount)
        {
            _ownerBuffer?.Release();
            _ownerBuffer = new ComputeBuffer(tileCount, sizeof(int));
        }
        
        // Owner color palette (Color per civ)
        if (_ownerColorBuffer == null || _ownerColorBuffer.count != ownerColorCount)
        {
            _ownerColorBuffer?.Release();
            _ownerColorBuffer = new ComputeBuffer(ownerColorCount, sizeof(float) * 4);
        }
        
        // LUT buffer (int per pixel: pixel -> tile index)
        if (_lutBuffer == null || _lutBuffer.count != _cachedLUT.Length)
        {
            _lutBuffer?.Release();
            _lutBuffer = new ComputeBuffer(_cachedLUT.Length, sizeof(int));
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
    
    private void UpdateOwnershipOverlay(int[] dirtyTiles, int ownerColorCount)
    {
        if (overlayComputeShader == null) return;
        
        int kernel = overlayComputeShader.FindKernel("UpdateOwnership");
        if (kernel < 0)
        {
            Debug.LogWarning("[TerrainOverlayGPU] UpdateOwnership kernel not found!");
            return;
        }
        
        // Set buffers
        overlayComputeShader.SetBuffer(kernel, "_PixelToTileLUT", _lutBuffer);
        overlayComputeShader.SetBuffer(kernel, "_OwnerByTile", _ownerBuffer);
        overlayComputeShader.SetBuffer(kernel, "_OwnerColors", _ownerColorBuffer);
        
        // Set output texture
        overlayComputeShader.SetTexture(kernel, "_OwnershipOverlay", _ownershipTexture);
        
        // Set parameters
        overlayComputeShader.SetInt("_Width", fogTextureWidth);
        overlayComputeShader.SetInt("_Height", fogTextureHeight);
        overlayComputeShader.SetInt("_TileCount", _ownerBuffer.count);
        overlayComputeShader.SetInt("_OwnerColorCount", ownerColorCount);
        overlayComputeShader.SetFloat("_OwnershipBlend", ownershipBlend);
        overlayComputeShader.SetInt("_OwnershipMode", ownershipMode);
        
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
        
        if (_ownershipTexture != null)
        {
            _ownershipTexture.Release();
            _ownershipTexture = null;
        }
        
        _fogBuffer?.Release();
        _fogBuffer = null;
        
        _ownerBuffer?.Release();
        _ownerBuffer = null;
        
        _ownerColorBuffer?.Release();
        _ownerColorBuffer = null;
        
        _lutBuffer?.Release();
        _lutBuffer = null;
    }
}

