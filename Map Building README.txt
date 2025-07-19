Core Systems
SphericalHexGrid.cs – Generates a true geodesic hexasphere from a subdivided icosahedron. The method GenerateFromSubdivision builds tile centers, neighbors and pentagon indices for the spherical grid

PlanetGenerator.cs – Main planet generation script. It exposes numerous parameters (continent counts, noise settings, island rules, river generation, etc.) and handles creation of the tile data. The surface generation coroutine initializes thresholds, produces deterministic continent seeds and applies noise to assign biomes and elevation

MoonGenerator.cs – Simplified terrain generator used when a moon is present. It generates dunes, caves and final visuals in a coroutine starting at GenerateSurface

NoiseSampler.cs – Wraps several FastNoiseLite instances to produce continent shapes, elevation variation, moisture, coastlines and temperature. Methods like GetContinent, GetElevation, and GetMoisture supply consistent noise values given a seed

BiomeHelper.cs – Holds the Biome enum, yield values, and the GetBiome logic that assigns a biome based on land/water, temperature and moisture. It also accounts for special map types such as demonic or scorched worlds

HexTileMeshBuilder.cs – Converts the hex grid into meshes. It provides both shared‑vertex and per‑tile builds so shaders can sample biome data efficiently

HexasphereRenderer.cs – Renders the hex sphere using a mesh built from the generator’s data. Optional atmosphere meshes and per-tile biome lookup are supported

GameManager.cs – Orchestrates game start-up. It instantiates generator prefabs and calls GenerateMap, which runs PlanetGenerator.GenerateSurface() and (optionally) MoonGenerator.GenerateSurface() before spawning civilizations and initializing UI

Map Type Helpers
MapTypeNameGenerator.cs – Combines climate, moisture, land type and elevation into a readable map name such as “Savanna Plateau” or “Arctic Basin”

MapTypeDescriptionGenerator.cs – Creates longer flavor text describing the map by mixing climate, moisture and geopolitical factors. GetDescription has overloads to supply civilization counts and animal prevalence

Visual Map Construction

PlanetGenerator includes a coroutine BuildPlanetVisualMapsBatched that converts tile data into textures (heightmap, biome index map, and color maps) using a pixel‑to‑tile lookup. The textures are later applied to the renderer

Map Generation Flow
GameManager determines map size and instantiates PlanetGenerator (and optionally MoonGenerator).

SphericalHexGrid creates a subdivided icosahedron according to the chosen subdivision level.

PlanetGenerator.GenerateSurface():

Validates thresholds.

Produces deterministic continent seeds with GenerateDeterministicSeeds.

Samples noise with NoiseSampler to decide where land exists, assign elevation and moisture, and select biomes via BiomeHelper.

Post‑processes coastlines, seas and rivers.

The generator builds visual textures using BuildPlanetVisualMapsBatched and hands them to HexasphereRenderer to display the world.

GameManager.GenerateMap() waits for planet/moon generation to finish before spawning gameplay systems.

This modular design allows different map sizes, climates and terrain themes to be combined into many unique map types. The helpers that generate map names and descriptions provide flavor text for each combination.