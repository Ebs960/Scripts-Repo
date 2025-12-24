# Flat + Globe Map System Refactor

This document describes the refactored map system that separates data from rendering, using a texture-based approach for both flat and globe views.

## Overview

The refactor implements:
- **Gameplay**: Flat equirectangular map (texture-based quad)
- **Visual**: Globe view (single sphere mesh with texture shader)
- **Zoom-based switching**: Zoom IN = flat map, Zoom OUT = globe
- **Data preservation**: All tile data generation and biome logic remains unchanged

## Key Components

### 1. FlatMapTextureRenderer.cs
Renders the flat equirectangular map using a texture instead of individual tile meshes.

**Features:**
- Creates a texture from tile data using `PlanetTextureBaker`
- Renders as a quad with horizontal wrapping enabled
- Provides methods for UV-to-world position conversion
- Includes tile index lookup via LUT

**Usage:**
```csharp
FlatMapTextureRenderer flatMap = GetComponent<FlatMapTextureRenderer>();
flatMap.Rebuild(planetGenerator);
flatMap.SetVisible(true); // Show/hide the flat map
```

### 2. GlobeRenderer.cs
Renders the planet as a globe using a single sphere mesh with a shader that samples the flat map texture.

**Features:**
- Single sphere mesh (no individual tile meshes)
- Uses the same texture as the flat map
- Custom shader support (falls back to Standard if not provided)
- Visual-only rendering

**Usage:**
```csharp
GlobeRenderer globe = GetComponent<GlobeRenderer>();
globe.Rebuild(planetGenerator, flatMapRenderer);
globe.SetVisible(true); // Show/hide the globe
```

### 3. CameraZoomController.cs
Automatically switches between flat map and globe views based on camera zoom level.

**Features:**
- Configurable zoom threshold
- Automatic view switching
- Smooth transitions (optional)
- Debug visualization

**Usage:**
```csharp
CameraZoomController zoomController = GetComponent<CameraZoomController>();
zoomController.SetZoomThreshold(15f); // Below 15 = flat, above = globe
```

### 4. GlobeMapShader.shader
Custom shader for the globe that samples the flat map texture with proper lighting.

**Features:**
- Converts sphere UV to lat/lon for equirectangular sampling
- Applies lighting for realistic globe appearance
- Configurable metallic and smoothness

## Setup Instructions

### Step 1: Disable Tile Mesh Instantiation
In `PlanetGenerator`, set `SpawnTilePrefabs = false` to prevent individual tile meshes from being created. The tile data will still be generated normally.

### Step 2: Create Flat Map Renderer
1. Create an empty GameObject in your scene
2. Add `FlatMapTextureRenderer` component
3. Optionally assign a `MinimapColorProvider` for custom coloring
4. The renderer will automatically build when the planet is ready (if `preBuildOnPlanetReady` is true)

### Step 3: Create Globe Renderer
1. Create an empty GameObject in your scene
2. Add `GlobeRenderer` component
3. Assign the `FlatMapTextureRenderer` reference (or it will auto-find)
4. Optionally assign a custom shader (uses Standard shader by default)

### Step 4: Setup Camera Zoom Controller
1. Add `CameraZoomController` component to your camera or a manager object
2. Assign references to `FlatMapTextureRenderer` and `GlobeRenderer`
3. Assign `PlanetaryCameraManager` reference
4. Set the zoom threshold (default: 15)

### Step 5: Minimap Integration (Optional)
The minimap can reuse the flat map texture:
```csharp
FlatMapTextureRenderer flatMap = FindObjectOfType<FlatMapTextureRenderer>();
Texture2D minimapTexture = flatMap.GetDownscaledTexture(512, 256);
// Use this texture in your minimap UI
```

## Important Notes

### Biome Logic Unchanged
- All biome generation logic remains in `BiomeHelper.GetBiome()`
- Climate and biome calculations are unchanged
- Only visualization has changed, not gameplay rules

### Tile Data Storage
- Tile data is stored as lat/lon indexed entries in `HexTileData`
- Access via `PlanetGenerator.GetHexTileData(tileIndex)`
- All tile properties (biome, elevation, resources, etc.) are preserved

### Performance Benefits
- No individual tile meshes = reduced draw calls
- Single texture = efficient GPU memory usage
- Texture can be shared between flat map, globe, and minimap

## Migration from Old System

If you were using `FlatMapRenderer` (the old system that cloned tile meshes):
1. Replace `FlatMapRenderer` with `FlatMapTextureRenderer`
2. Remove the old `FlatMapRenderer` component
3. The new system uses textures instead of cloned meshes

## Troubleshooting

### Flat map not showing
- Check that `FlatMapTextureRenderer.IsBuilt` is true
- Verify the planet generator has completed generation
- Ensure `SetVisible(true)` is called

### Globe not showing
- Check that `GlobeRenderer.IsBuilt` is true
- Verify the flat map texture is available
- Ensure `SetVisible(true)` is called

### Zoom switching not working
- Verify `CameraZoomController` has all references assigned
- Check that `PlanetaryCameraManager.orbitRadius` is being updated
- Adjust the zoom threshold if needed

### Texture quality issues
- Increase `textureWidth` and `textureHeight` in `FlatMapTextureRenderer`
- Default is 2048x1024, can go higher for better quality

## Future Enhancements

Potential improvements:
- Dynamic texture updates when tiles change
- LOD system for different zoom levels
- Animated transitions between flat/globe views
- Custom shader effects for globe rendering

