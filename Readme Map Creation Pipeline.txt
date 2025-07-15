Pipeline Overview
The new map-building process consists of two main stages:
1. Data generation (terrain, biomes, elevation, etc.)

2. Visual map creation and mesh setup

Briefly: the code first creates a hexagonal spherical grid and assigns each tile (“hex”) a land/water status, elevation, biome, and yields. After all game data (height, biome, resource yields) is computed, the system then generates visual textures (heightmaps, biome masks) and applies them via the HexasphereRenderer. The mesh itself is built once from the raw grid (a unit sphere) and then remains static; per-tile elevation is conveyed via textures/shaders.
________________


Initialization
Grid Setup: On startup, an icospherical grid of hex tiles is generated (PlanetGenerator.Awake) based on the chosen subdivision level. This creates all tile centers and adjacency for the planet. The HexasphereRenderer is pointed at this generator and immediately builds a unit-sphere mesh from it via HexTileMeshBuilder.Build(grid). This yields a spherical mesh (radius = 1) with hex tiles, but no elevation applied yet.
Initial Data Structures: The code clears any old map data and initializes noise samplers (for elevation, moisture, etc.) with the map’s seed. It also precomputes latitude/longitude for each tile center for later use.
________________


Land/Water (Continent) Generation
Deterministic Seeds: The system begins by placing continent seed points on the sphere in a pseudo-random but repeatable pattern (GenerateDeterministicSeeds). Each seed defines a geographic mask (latitude/longitude range) for a continent.
Noise Peak Selection: For each continent seed, the code scans all tiles within that mask and finds the tile with maximum continent-noise (Perlin/Simplex noise). That “peak” tile is guaranteed land.
Land Flood Fill: Next, every tile within each seed’s mask is tested: if its continent-noise value exceeds a threshold (landThreshold), it is marked as land. This ensures continents have a coherent shape around their peaks. Land tiles are tallied for statistics.
Islands & Poles: Optionally, additional islands are generated in random locations if enabled. Polar landmasses (if any) are also added to ensure high-latitude areas have some land. At this point, every tile is labeled land or water (with polar adjustments).
________________


Elevation & Biome Assignment
Raw Elevation: For each tile, a base noise elevation in [0–1] is sampled (using a second noise function). This “rawNoise” is scaled into a final elevation:
   * Land: baseLandElevation + rawNoise * (maxTotalElevation - baseLandElevation)

   * Water: 0 (except glaciers, see below)
All elevations are capped by maxTotalElevation.

Climate (Moisture/Temperature):
      * Moisture: sampled via noise + bias, clamped 0–1

      * Temperature: blends latitude and noise

         * Above polar threshold: purely cold (latitude-based)

         * Else: mix of latitude + noise + bias

Biome Determination:
            * Uses GetBiomeForTile() based on land/ocean and climate values

            * Polar latitudes may override with snow/glacier

            * Hills and mountains:

               * If elevation > mountainThreshold: biome = Mountain

               * Else if > hillThreshold: isHill = true

                  * Ocean default: Ocean or Glacier (polar)

                  * Glaciers treated as land (non-zero elevation)

Data Structuring:
                     * Each tile gets a HexTileData object

                     * Stores: biome, yields, occupant, land/hill flags, elevation, temp, moisture

                     * Saved in data[tileIndex]

                     * Original “base” copy saved for resets

________________


Post-Processing (Coasts, Seas, Rivers)
                        * Coastlines: Any land tile adjacent to water becomes a Coast biome (except snow/glacier land), with fixed low elevation (coastElevation)

                        * Shallow Seas: Ocean tiles adjacent to coasts become Seas biome

                        * Rivers (Optional): High-elevation land tiles spawn rivers flowing downhill toward coasts. Rivers must meet a minimum length.

                        * Final Flags: At this point, all final biomes, elevations, and terrain types are committed.

________________


Visual Texture Generation
Heightmap:
                           * After terrain generation, builds a 2D equirectangular texture

                           * Each pixel maps to a tile’s elevation

                           * Pixel R-channel = normalized elevation

                           * Result is heightTex

Biome Masks & Albedo:
                              * Biome lookup textures built next

                              * Several modes supported:

                                 * One 8-bit mask per biome (R=255 where that biome applies)

                                 * RGBA packed masks

                                 * Biome index texture (biomeIndexTex)

                                    * Albedo texture array (biomeAlbedoArray) built from biome colors

________________


Applying Textures
Once heightTex, biomeIndexTex, and biomeAlbedoArray are built, they are sent to:

csharp
CopyEdit
hexasphereRenderer.ApplyHeightDisplacement(1f);
hexasphereRenderer.PushBiomeLookups(biomeIndexTex, biomeAlbedoArray);
                                       *                                        * The HexasphereRenderer binds these to the planet material for rendering

________________


Mesh & Rendering Integration
                                          * The planet mesh is built once using HexasphereRenderer.BuildMesh(grid)

                                          * The mesh is a unit sphere subdivided into hexes

                                          * No vertices are moved after mesh creation

                                          * All visual variation (height, biome) is done via textures + shader

________________


Moon Generation
                                             * A simpler version runs via MoonGenerator.GenerateSurface()

                                             * Uses similar logic for elevation and biome

                                             * Final rendering uses same shader + mesh system

________________


Summary of Steps
                                                1. Grid + Mesh: Build icosphere grid → Build mesh

                                                2. Terrain Data: Mark land/water using noise

                                                3. Elevation & Biome: Sample noise → assign elevation → determine biome

                                                4. Post-Processing: Add coast, sea, rivers

                                                5. Texture Generation: Build heightmap, biome index, biome albedo array

                                                6. Rendering: Send all textures to shader via HexasphereRenderer

________________


All of this confirms: data → textures → visuals
 The visual layer is driven entirely by the underlying simulation data — not the other way around. This ensures clean separation of logic and rendering and supports long-term extensibility (underground layers, weather effects, unit visibility, etc.).