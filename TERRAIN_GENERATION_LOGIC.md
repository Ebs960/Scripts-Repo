# Terrain Generation System - Logic Explanation

## Overview
The terrain generation system uses a **Factory Pattern** with **Strategy Pattern** to create biome-specific terrain. Each biome has its own generator that produces terrain with unique characteristics.

## How It Works

### 1. **Entry Point: BattleMapGenerator.GenerateBattleMap()**
```
GenerateBattleMap() 
  → GenerateTerrainWithCustomSystem()
    → BiomeTerrainGeneratorFactory.GetGenerator(biome)
      → CreateGenerator(biome) [switch statement]
        → Returns appropriate IBiomeTerrainGenerator
    → generator.Generate(terrain, elevation, moisture, temperature, mapSize)
      → Generates heightmap using biome-specific algorithm
    → ApplyBiomeMaterialToTerrain(terrain)
      → Sets terrain.materialTemplate directly
```

### 2. **Factory Pattern (BiomeTerrainGeneratorFactory)**

The factory uses a **switch expression** to map each `Biome` enum value to a specific generator class:

```csharp
Biome.Plains → PlainsTerrainGenerator
Biome.Desert → DesertTerrainGenerator
Biome.Mountain → MountainTerrainGenerator
Biome.Forest → ForestTerrainGenerator
// ... etc
```

**Key Features:**
- **Caching**: Generators are cached after first creation (performance optimization)
- **Fallback**: Unknown biomes default to `PlainsTerrainGenerator`
- **Grouping**: Similar biomes share the same generator (e.g., all ice biomes use `IceTerrainGenerator`)

### 3. **Generator Classes (Strategy Pattern)**

Each generator implements `IBiomeTerrainGenerator` interface:

```csharp
public interface IBiomeTerrainGenerator
{
    void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize);
    BiomeNoiseProfile GetNoiseProfile();
}
```

**How generators work:**
1. **Get TerrainData**: Access the terrain's `TerrainData` component
2. **Create Heightmap Array**: `float[,] heights = new float[resolution, resolution]`
3. **Generate Heights**: Loop through each pixel, calculate height using:
   - Base elevation
   - Layered Perlin noise (multiple octaves)
   - Biome-specific modifiers (hilliness, roughness, etc.)
4. **Apply Erosion** (optional): Smooth/flatten certain areas
5. **Set Heights**: `terrainData.SetHeights(0, 0, heights)`

### 4. **Noise Profile System**

Each generator has a `BiomeNoiseProfile` that controls:
- **baseHeight**: Starting elevation (0-1)
- **noiseScale**: Size of terrain features (lower = larger features)
- **roughness**: How jagged the terrain is
- **hilliness**: How much height variation
- **mountainSharpness**: How sharp peaks are (0 = rounded, 1 = sharp)
- **octaves**: Number of noise layers (more = more detail)
- **lacunarity**: Frequency multiplier per octave
- **persistence**: Amplitude multiplier per octave
- **maxHeightVariation**: Maximum height in world units
- **useErosion**: Whether to smooth/flatten terrain
- **erosionStrength**: How much to smooth

### 5. **Layered Noise Algorithm**

All generators use a similar layered noise approach:

```csharp
float GenerateLayeredNoise(float x, float z, BiomeNoiseProfile profile)
{
    float value = 0f;
    float amplitude = 1f;
    float frequency = profile.noiseScale;
    
    // Add multiple octaves (layers) of noise
    for (int i = 0; i < profile.octaves; i++)
    {
        value += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
        frequency *= profile.lacunarity;  // Each layer has higher frequency
        amplitude *= profile.persistence; // Each layer has lower amplitude
    }
    
    // Normalize to -1 to 1 range
    return normalized_value;
}
```

**Why layered noise?**
- **Low frequency** (first octave) = Large hills/mountains
- **High frequency** (later octaves) = Small details/roughness
- **Combined** = Realistic terrain with both large and small features

### 6. **Biome Mapping Logic**

The factory maps biomes to generators based on **terrain characteristics**:

| Generator | Characteristics | Example Biomes |
|-----------|----------------|----------------|
| `PlainsTerrainGenerator` | Rolling hills, gentle slopes | Plains, Grassland, Savannah |
| `DesertTerrainGenerator` | Sand dunes, flat areas | Desert, Scorched, Ashlands |
| `MountainTerrainGenerator` | Sharp peaks, high variation | Mountain, Volcanic, Hellscape |
| `ForestTerrainGenerator` | Moderate hills, more variation than plains | Forest, Jungle, Rainforest |
| `SwampTerrainGenerator` | Very flat, occasional depressions | Swamp, Marsh, Floodlands |
| `IceTerrainGenerator` | Flat to gently rolling, ice formations | Snow, Glacier, Tundra, Arctic |
| `OceanTerrainGenerator` | Very flat, near sea level | Ocean, Coast, Seas |

**Grouping Strategy:**
- Similar terrain types share generators
- Unique biomes can have their own generators if needed
- Planet-specific biomes map to Earth-equivalent generators

### 7. **Material Application**

After terrain generation:
```csharp
ApplyBiomeMaterialToTerrain(terrain)
  → CreateBiomeTerrainMaterial()
    → Gets textures from BiomeSettings
    → Creates Material with shader
    → Applies textures/colors
  → terrain.materialTemplate = biomeMaterial
```

**Why this works:**
- Unity Terrain's `materialTemplate` property directly applies materials
- No reflection needed (unlike MapMagic)
- Guaranteed to work with Unity's Terrain system

## Adding New Biomes

### Option 1: Map to Existing Generator
If your new biome is similar to an existing one, just add it to the switch statement:
```csharp
Biome.NewBiome => new ExistingTerrainGenerator(),
```

### Option 2: Create New Generator
1. Create new class implementing `IBiomeTerrainGenerator`
2. Copy structure from similar generator
3. Adjust `BiomeNoiseProfile` values
4. Add to factory switch statement

Example:
```csharp
public class NewBiomeTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    
    public NewBiomeTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.4f,
            noiseScale = 0.1f,
            // ... customize values
        };
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        // Custom generation logic
    }
    
    public BiomeNoiseProfile GetNoiseProfile() => noiseProfile;
}
```

## Performance Considerations

- **Caching**: Generators are cached after first creation
- **Resolution**: `CalculateOptimalResolution()` scales resolution with map size
- **Erosion**: Can be disabled for performance-critical scenarios
- **Octaves**: More octaves = more detail but slower generation

## Debugging

Check logs for:
- `"[BattleMapGenerator] Generated terrain using {biome} generator"` - Confirms generator selection
- `"[BattleMapGenerator] Applied biome material to terrain"` - Confirms material application
- `"[BattleMapGenerator] No generator found for {biome}"` - Biome not mapped (uses fallback)

