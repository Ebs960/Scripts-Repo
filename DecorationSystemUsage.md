# Decoration System Usage Guide

## Overview
The decoration system automatically spawns trees, bushes, rocks, and other environmental objects on planet and moon tiles based on their biome type. Each tile will get 1-3 decorations randomly placed around the tile's surface.

## Setup Instructions

### 1. Configure Biome Settings
In the Inspector for your `PlanetGenerator` or `MoonGenerator`, expand the **Biome Settings** list:

1. Set the size to include all biomes you want to decorate
2. For each biome entry:
   - Set the **Biome** dropdown to the biome type (Forest, Grassland, Desert, etc.)
   - Assign **Albedo Texture** (the color/diffuse map)
   - Optionally assign **Normal Texture** (for surface detail)
   - **Most importantly**: Drag decoration prefabs into the **Decorations** array

### 2. Create Decoration Prefabs
Your decoration prefabs should:
- Have their "up" direction aligned with the Y-axis (standard Unity convention)
- Be appropriately scaled (the system will apply additional scaling)
- Include any necessary colliders, renderers, or scripts

Examples:
- **Forest biome**: Tree prefabs, bush prefabs, flower patches
- **Desert biome**: Cactus prefabs, rock formations, sand dunes
- **Grassland biome**: Small trees, grass clusters, wildflowers
- **Moon Dunes**: Crystal formations, strange rocks, alien plants
- **Moon Caves**: Glowing crystals, stalactites, cave formations

### 3. Configure Decoration Settings
In the Inspector, under **Decoration Settings**:

- **Min/Max Decorations Per Tile**: Controls how many objects spawn per tile (1-3 recommended)
- **Decoration Min/Max Distance From Center**: Controls where decorations spawn relative to tile center
- **Decoration Scale**: Overall size multiplier for all decorations
- **Enable Decorations**: Toggle to turn the system on/off

### 4. Automatic Features
The system automatically:
- ✅ Randomly selects decorations from the biome's decoration array
- ✅ Positions them at a safe distance from the tile center
- ✅ Orients them to face "up" away from the planet/moon surface
- ✅ Adds random rotation around the up-axis for natural variety
- ✅ Applies consistent scaling
- ✅ Organizes decorations under parent objects for clean hierarchy
- ✅ Skips water biomes (Ocean, Coast, Seas) automatically

## Example Biome Configuration

### Forest Biome
```
Biome: Forest
Albedo Texture: ForestGrassTexture
Normal Texture: ForestNormalMap
Decorations:
  [0] PineTree_Prefab
  [1] OakTree_Prefab
  [2] BushCluster_Prefab
  [3] Fern_Prefab
  [4] Mushroom_Prefab
```

### Desert Biome
```
Biome: Desert
Albedo Texture: SandTexture
Decorations:
  [0] Cactus_Small
  [1] Cactus_Large
  [2] DesertRock_Prefab
  [3] Tumbleweed_Prefab
```

### Moon Dunes Biome
```
Biome: MoonDunes
Albedo Texture: MoonSandTexture
Decorations:
  [0] AlienCrystal_Prefab
  [1] MoonRock_Prefab
  [2] StrangePlant_Prefab
```

## Tips for Best Results

1. **Decoration Variety**: Include 3-5 different decoration prefabs per biome for variety
2. **Appropriate Scale**: Make sure your prefabs are reasonably sized (not too big for tiles)
3. **Performance**: Consider using LOD (Level of Detail) on decoration prefabs for better performance
4. **Water Biomes**: The system automatically skips Ocean, Coast, and Seas biomes
5. **Hierarchical Organization**: Decorations are automatically organized under "Decorations_Tile_X" parent objects

## Troubleshooting

- **No decorations appearing**: Check that biome settings have decorations assigned and "Enable Decorations" is checked
- **Decorations floating/underground**: Ensure your prefabs have Y-axis as "up" direction
- **Too many/few decorations**: Adjust min/max decorations per tile settings
- **Decorations too close to center**: Increase "Decoration Min Distance From Center"
