# Custom Terrain Generation System - Setup Guide

## Overview
The new custom terrain generation system uses Unity's Terrain API with biome-specific generators. It provides full control over terrain generation and guaranteed material application.

## Scene Setup Steps

### Step 1: Create BattleMapGenerator GameObject

1. **Create Empty GameObject**
   - Right-click in Hierarchy → Create Empty
   - Name it: `BattleMapGenerator`

2. **Add BattleMapGenerator Component**
   - Select the GameObject
   - In Inspector, click "Add Component"
   - Search for "BattleMapGenerator" and add it

3. **Configure BattleMapGenerator Settings**

   **Map Generation Section:**
   - `Terrain Resolution`: 1.0 (default - fine for most cases)
   - `Height Variation`: 3.0 (default - controls terrain height)
   - `Noise Scale`: 0.1 (default - controls feature size)
   - `Elevation Noise Scale`: 0.05 (default - controls detail)

   **Biome Settings Section:**
   - `Primary Battle Biome`: Select your desired biome (e.g., Plains, Desert, Mountain)
   - `Battle Tile Elevation`: 0.5 (0-1, controls base height)
   - `Battle Tile Moisture`: 0.5 (0-1, affects vegetation)
   - `Battle Tile Temperature`: 0.5 (0-1, affects biome selection)

   **Battle Type:**
   - `Battle Type`: Land (or Naval/Coastal/Siege)

   **Terrain Generation Method:**
   - **IMPORTANT**: Set `Terrain Generation Method` to **Custom**
   - This uses the new biome-specific generator system

   **Note:** The terrain system now uses only the custom Unity Terrain API. MapMagic 2 integration has been removed.
   - `Map Magic 2 Object`: Leave empty (not needed for custom generation)

   **Biome Settings Array:**
   - Expand `Biome Settings` array
   - Add entries for each biome you want to use
   - For each entry:
     - `Biome`: Select the biome type
     - `Albedo Texture`: Assign terrain texture (optional but recommended)
     - `Normal Texture`: Assign normal map (optional)
     - `Decorations`: Assign decoration prefabs (trees, rocks, etc.)
     - `Spawn Chance`: 0.15 (default - chance to spawn decorations)

   **Obstacles & Decorations:**
   - `Obstacle Density`: 0.15 (0-1, how many decorations)
   - `Max Decorations`: 50 (limit to prevent memory issues)
   - `Cover Density`: 0.1 (0-1, density of cover objects)

   **Spawn Points:**
   - `Spawn Distance`: 30.0 (distance between attacker/defender spawns)

   **Grounding & Decoration Placement:**
   - `Battlefield Layers`: Select "Battlefield" layer (or leave as "Everything" if layer doesn't exist)

### Step 2: Create BattleTestSimple GameObject

1. **Create Empty GameObject**
   - Right-click in Hierarchy → Create Empty
   - Name it: `BattleTestSimple`

2. **Add BattleTestSimple Component**
   - Select the GameObject
   - In Inspector, click "Add Component"
   - Search for "BattleTestSimple" and add it

3. **Link BattleMapGenerator**
   - In BattleTestSimple Inspector, find `[Header("Battle Map")]`
   - Drag the `BattleMapGenerator` GameObject into the `Map Generator` field

4. **Configure Battle Map Settings (Editor Testing)**
   - Find `[Header("Battle Map Settings (Editor Testing)")]`
   - `Test Biome`: Select biome for testing (e.g., Plains, Desert, Mountain)
   - `Test Battle Type`: Select battle type (Land, Naval, Coastal, Siege)
   - `Use Custom Hilliness`: Check if you want to override biome default hilliness
   - `Custom Hilliness`: 0.5 (0-1, only used if Use Custom Hilliness is checked)
     - 0.0 = Flat terrain
     - 0.5 = Moderate hills
     - 1.0 = Very hilly/mountainous

5. **Configure Other BattleTestSimple Settings**
   - `Battle Map Size`: 100.0 (size of battle map)
   - `Generate New Map`: **Checked** (generates new map each battle)
   - `Formations Per Side`: 3
   - `Soldiers Per Formation`: 9 (default, but actual comes from CombatUnitData)

### Step 3: Set Up Battlefield Layer (Optional but Recommended)

1. **Create Battlefield Layer**
   - Edit → Project Settings → Tags and Layers
   - Under "Layers", find an empty slot (e.g., Layer 9)
   - Name it: `Battlefield`

2. **Assign Layer to Terrain**
   - The terrain will automatically use this layer when generated
   - Make sure `Battlefield Layers` in BattleMapGenerator includes this layer

### Step 4: Configure Biome Settings

1. **In BattleMapGenerator Inspector**
   - Expand `Biome Settings` array
   - For each biome you'll use, add an entry:
     ```
     Biome Settings[0]:
       - Biome: Plains
       - Albedo Texture: [Assign your plains texture]
       - Normal Texture: [Optional - assign normal map]
       - Decorations: [Assign tree/rock prefabs]
       - Spawn Chance: 0.15
     ```

2. **Repeat for other biomes** (Desert, Forest, Mountain, etc.)

### Step 5: Test the System

1. **In BattleTestSimple Inspector**
   - Set `Test Biome` to desired biome (e.g., Plains)
   - Set `Test Battle Type` to Land
   - Optionally check `Use Custom Hilliness` and set value

2. **Click "Start Test" button** (or call `StartTest()` method)

3. **What Happens:**
   - System generates terrain using biome-specific generator
   - Applies biome material/texture to terrain
   - Places decorations based on biome
   - Creates spawn points
   - Bakes NavMesh for AI navigation
   - Spawns formations

## How It Works

### Terrain Generation Flow:

1. **BattleTestSimple.StartTest()** is called
2. **BattleTestSimple.CreateSimpleTest()** runs
3. **BattleMapGenerator.GenerateBattleMap()** is called with:
   - Map size
   - Biome from `testBiome` (editor mode) or defender tile (campaign mode)
   - Battle type from `testBattleType`
4. **BattleMapGenerator.GenerateTerrainWithCustomSystem()** runs:
   - Creates Unity Terrain GameObject
   - Gets biome-specific generator from `BiomeTerrainGeneratorFactory`
   - Generator creates heightmap using layered Perlin noise
   - Applies biome material directly to terrain
5. **Decorations are placed** using raycasting on terrain surface
6. **NavMesh is baked** for AI navigation
7. **Formations spawn** at calculated spawn points

### Biome Selection:

- **Editor Test Mode**: Uses `BattleTestSimple.testBiome`
- **Campaign Mode**: Uses defender's tile biome (from `storedDefenderTile`)

### Hilliness Control:

- **Default**: Each biome generator has its own default hilliness
- **Custom Override**: If `Use Custom Hilliness` is checked, it overrides the biome default
- **How it works**: Modifies the generator's `BiomeNoiseProfile.hilliness` and `maxHeightVariation` parameters

## Troubleshooting

### Terrain Not Generating:
- Check that `Terrain Generation Method` is set to **Custom**
- Check console for errors
- Verify `BattleMapGenerator` is assigned in `BattleTestSimple`

### Materials Not Appearing:
- Check that `Biome Settings` array has an entry for your selected biome
- Verify `Albedo Texture` is assigned in `BiomeSettings`
- Check console for: `"[BattleMapGenerator] Applied biome material to terrain"`

### Wrong Terrain Shape:
- Check `Test Biome` in `BattleTestSimple` matches what you want
- Try different biomes to see different terrain shapes:
  - Plains: Rolling hills
  - Desert: Sand dunes
  - Mountain: Sharp peaks
  - Forest: Moderate hills
  - Swamp: Very flat

### Decorations Not Spawning:
- Check `Obstacle Density` in `BattleMapGenerator` (increase if needed)
- Verify `Decorations` array in `BiomeSettings` has prefabs assigned
- Check `Max Decorations` limit (increase if needed)

### NavMesh Not Working:
- Check that terrain has colliders (should be automatic with Unity Terrain)
- Verify `Battlefield Layers` includes the terrain layer
- Check console for NavMesh baking messages

## Example Configuration

### For a Plains Battle:
```
BattleMapGenerator:
  - Primary Battle Biome: Plains
  - Terrain Generation Method: Custom
  - Biome Settings[0]:
      Biome: Plains
      Albedo Texture: [Grass texture]
      Decorations: [Tree prefabs, Rock prefabs]

BattleTestSimple:
  - Map Generator: [Drag BattleMapGenerator GameObject]
  - Test Biome: Plains
  - Test Battle Type: Land
  - Use Custom Hilliness: Unchecked (use biome default)
```

### For a Mountain Battle:
```
BattleMapGenerator:
  - Primary Battle Biome: Mountain
  - Terrain Generation Method: Custom
  - Biome Settings[0]:
      Biome: Mountain
      Albedo Texture: [Rock texture]
      Decorations: [Boulder prefabs]

BattleTestSimple:
  - Map Generator: [Drag BattleMapGenerator GameObject]
  - Test Biome: Mountain
  - Test Battle Type: Land
  - Use Custom Hilliness: Checked
  - Custom Hilliness: 0.9 (very hilly)
```

## Key Points

1. **Always set `Terrain Generation Method = Custom`** to use the new system
2. **Biome Settings array** must have entries for biomes you want to use
3. **Test Biome** in BattleTestSimple overrides BattleMapGenerator's biome in editor mode
4. **Custom Hilliness** only works if `Use Custom Hilliness` is checked
5. **Materials are applied automatically** - no manual setup needed
6. **All biomes are supported** - factory maps all 70+ biomes to appropriate generators

