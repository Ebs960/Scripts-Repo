using System.Collections;
using UnityEngine;

// Sandbox helper to assemble a full generator test in one GameObject.
// Place this component in a new scene. You can manually drop your existing
// `PlanetGenerator`, `HexGrid` (as a component or object reference),
// `HexMapChunkManager`, `MinimapUI`, and `MinimapColorProvider` into the inspector.
// Use the inspector buttons to Generate, Rebuild, or Release GPU caches.
public class PlanetSandbox : MonoBehaviour
{
    [Header("References (assign existing scene components)")]
    public PlanetGenerator planetGenerator;
    public HexGridComponent hexGrid; // assign a HexGridComponent in-scene
    public HexMapChunkManager hexMapChunkManager;
    public MinimapUI minimapUI;
    public MinimapColorProvider colorProvider;
    public ComputeShader textureBakerComputeShader;

    [Header("Map Settings")]
    public int tilesX = 128;
    public int tilesZ = 64;
    public float mapWidth = 1024f;
    public float mapHeight = 512f;
    public int bakeWidth = 2048;
    public int bakeHeight = 1024;
    public int seed = 0;

    [Header("Generation Options")]
    [Tooltip("When true, trigger minimap (LUT/atlas) generation via the assigned MinimapUI.")]
    public bool generateMinimap = true;
    [Tooltip("When true, build the full terrain chunks via the assigned HexMapChunkManager.")]
    public bool buildTerrainChunks = true;
    [Tooltip("When true, run the full PlanetGenerator surface generation (biomes, elevation, water) before baking/minimap/chunks.")]
    public bool runFullSurfaceGeneration = true;

    [Header("Behavior")]
    public bool autoGenerateOnStart = false;
    [Header("Sandbox Options")]
    [Tooltip("When true the sandbox will respect the PlanetGenerator's inspector values and won't overwrite them with GameSetupData defaults.")]
    public bool respectGeneratorInspector = true;

    private void Start()
    {
        if (autoGenerateOnStart)
            Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        // -----------------------------------------------------------------------------------------
        // IMPORTANT: Make PlanetSandbox behave like the NORMAL game flow.
        //
        // Normal flow (GameManager):
        // 1) GameSetupData.InitializeDefaults() (Standard baseline) if needed
        // 2) PlanetGenerator.Grid.GenerateFlatGrid(tilesX, tilesZ, width, height) using size preset
        // 3) Apply GameSetupData → PlanetGenerator knobs (mapTypeName, terrain preset, land/water counts, biases)
        // 4) Run PlanetGenerator.GenerateSurface()
        // 5) HexMapChunkManager builds chunks from the generated PlanetGenerator
        //
        // This sandbox enforces Standard defaults every time you press Generate so results are predictable.
        // -----------------------------------------------------------------------------------------

        if (!respectGeneratorInspector)
        {
            GameSetupData.InitializeDefaults(); // Force Standard defaults (matches "Start Game" baseline)
        }

        if (planetGenerator == null)
        {
            Debug.LogWarning("PlanetSandbox: PlanetGenerator is not assigned. Place your PlanetGenerator in scene and assign it.");
            return;
        }

        // Local build sizing (used later by optional debug HexGridComponent)
        int buildTilesX = tilesX;
        int buildTilesZ = tilesZ;
        float buildWidth = mapWidth;
        float buildHeight = mapHeight;

        // --- Build flat grid. If respecting the PlanetGenerator inspector, prefer the generator's existing grid
        if (respectGeneratorInspector && planetGenerator.Grid != null && planetGenerator.Grid.IsBuilt)
        {
            Debug.Log($"PlanetSandbox: Using PlanetGenerator's existing grid. TileCount={planetGenerator.Grid.TileCount} Size={planetGenerator.Grid.Width}x{planetGenerator.Grid.Height} Map={planetGenerator.Grid.MapWidth}x{planetGenerator.Grid.MapHeight}");
            buildTilesX = planetGenerator.Grid.Width;
            buildTilesZ = planetGenerator.Grid.Height;
            buildWidth = planetGenerator.Grid.MapWidth;
            buildHeight = planetGenerator.Grid.MapHeight;
        }
        else if (respectGeneratorInspector)
        {
            // Respect inspector: use the sandbox's inspector fields (tilesX/mapWidth etc) to build a grid
            planetGenerator.Grid.GenerateFlatGrid(tilesX, tilesZ, mapWidth, mapHeight);
            Debug.Log($"PlanetSandbox: PlanetGenerator grid built from sandbox inspector values. IsBuilt={planetGenerator.Grid != null && planetGenerator.Grid.IsBuilt} TileCount={planetGenerator.Grid?.TileCount ?? 0} Size={planetGenerator.Grid?.Width}x{planetGenerator.Grid?.Height} Map={planetGenerator.Grid?.MapWidth}x{planetGenerator.Grid?.MapHeight}");
            buildTilesX = tilesX;
            buildTilesZ = tilesZ;
            buildWidth = mapWidth;
            buildHeight = mapHeight;
        }
        else
        {
            // Original behavior: enforce Standard GameSetupData defaults and derive grid from the size preset
            var size = GameSetupData.mapSize; // InitializeDefaults sets this to Standard
            GameManager.GetFlatMapSizeParams(size, out float stdWidth, out float stdHeight);
            GameManager.GetFlatTileResolution(size, out int stdTilesX, out int stdTilesZ);

            // Keep inspector fields in sync so you can see what Standard is using.
            tilesX = stdTilesX;
            tilesZ = stdTilesZ;
            mapWidth = stdWidth;
            mapHeight = stdHeight;

            // Stamp sizing/min distances derived from size preset (mirrors GameManager.ApplyStampSettingsForMapSize)
            ApplyStampSettingsForMapSize(size);

            planetGenerator.Grid.GenerateFlatGrid(stdTilesX, stdTilesZ, stdWidth, stdHeight);
            Debug.Log($"PlanetSandbox: PlanetGenerator grid built (Standard). IsBuilt={planetGenerator.Grid != null && planetGenerator.Grid.IsBuilt} TileCount={planetGenerator.Grid?.TileCount ?? 0} Size={planetGenerator.Grid?.Width}x{planetGenerator.Grid?.Height} Map={planetGenerator.Grid?.MapWidth}x{planetGenerator.Grid?.MapHeight}");
            buildTilesX = stdTilesX;
            buildTilesZ = stdTilesZ;
            buildWidth = stdWidth;
            buildHeight = stdHeight;
        }

        if (planetGenerator.Grid == null || !planetGenerator.Grid.IsBuilt || planetGenerator.Grid.TileCount <= 0)
        {
            Debug.LogError("PlanetSandbox: PlanetGenerator grid is not built. Aborting (this will produce flat/empty results).");
            return;
        }

        // Optional: also build the standalone HexGridComponent (if assigned) for debugging/visual tools,
        // but it is NOT the authoritative grid used by the generator pipeline.
        if (hexGrid != null)
        {
            hexGrid.Generate(buildTilesX, buildTilesZ, buildWidth, buildHeight);
            Debug.Log($"PlanetSandbox: HexGridComponent also generated (debug only). IsBuilt={hexGrid.grid != null && hexGrid.grid.IsBuilt} TileCount={hexGrid.grid?.TileCount ?? 0}");
        }

        // --- Optionally apply GameSetupData settings to the PlanetGenerator (mirrors GameManager) ---
        if (!respectGeneratorInspector)
        {
            planetGenerator.SetMapTypeName(GameSetupData.mapTypeName ?? "");
            planetGenerator.ApplyTerrainPreset(GameSetupData.selectedTerrainPreset);
            planetGenerator.moistureBias = GameSetupData.moistureBias;
            planetGenerator.temperatureBias = GameSetupData.temperatureBias;
            planetGenerator.numberOfContinents = GameSetupData.numberOfContinents;
            planetGenerator.numberOfIslands = GameSetupData.numberOfIslands;
            planetGenerator.generateIslands = GameSetupData.generateIslands;

            // In sandbox scenes there is typically no authoritative layer config; PlanetGenerator.HasLayer() falls back
            // to "supported" in that case. This matches the normal flow's intent: don't block water features in legacy scenes.
            planetGenerator.enableRivers = GameSetupData.enableRivers;
            planetGenerator.enableLakes = GameSetupData.enableLakes;
            planetGenerator.numberOfLakes = GameSetupData.enableLakes ? GameSetupData.numberOfLakes : 0;
            planetGenerator.lakeMinRadiusTiles = GameSetupData.lakeMinRadiusTiles;
            planetGenerator.lakeMaxRadiusTiles = GameSetupData.lakeMaxRadiusTiles;
            planetGenerator.lakeMinDistanceFromCoast = GameSetupData.lakeMinDistanceFromCoast;
        }

        // If requested, run full surface generation first (this populates tiles/biomes/elevation)
        if (runFullSurfaceGeneration)
        {
            StartCoroutine(GenerateSurfaceThenSteps());
            return; // coroutine will continue with minimap / chunks when complete
        }

        // Optionally trigger minimap generation (LUT + tile atlas)
        if (generateMinimap)
        {
            if (minimapUI == null)
            {
                Debug.LogWarning("PlanetSandbox: MinimapUI not assigned; skipping minimap generation.");
            }
            else if (planetGenerator == null)
            {
                Debug.LogWarning("PlanetSandbox: PlanetGenerator not assigned; cannot generate minimap without planet index.");
            }
            else
            {
                try
                {
                    int w, h;
                    minimapUI.GetPlanetLUT(planetGenerator.planetIndex, out w, out h);
                    minimapUI.GetTileAtlasColors(planetGenerator.planetIndex, false);
                    Debug.Log("PlanetSandbox: Requested minimap LUT and tile atlas generation.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("PlanetSandbox: Minimapping request failed: " + ex.Message);
                }
            }
        }

        // Optionally build terrain chunks
        if (buildTerrainChunks)
        {
            if (hexMapChunkManager == null)
            {
                Debug.LogWarning("PlanetSandbox: HexMapChunkManager is not assigned. Assign it and then call Rebuild().");
                return;
            }

            // Trigger rebuild on chunk manager using the assigned planetGenerator (may be null)
            try
            {
                hexMapChunkManager.Rebuild(planetGenerator);
                Debug.Log("PlanetSandbox: Rebuild triggered on HexMapChunkManager.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("PlanetSandbox: Failed to call Rebuild() on HexMapChunkManager: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Mirror of the Standard sizing logic in GameManager.ApplyStampSettingsForMapSize (flat-only).
    /// We keep this here so PlanetSandbox can reproduce the real pipeline without requiring a GameManager instance.
    /// </summary>
    private static void ApplyStampSettingsForMapSize(GameManager.MapSize size)
    {
        int continentMinW = GameSetupData.minContinentWidthTilesStandard;
        int continentMaxW = GameSetupData.maxContinentWidthTilesStandard;
        int continentMinH = GameSetupData.minContinentHeightTilesStandard;
        int continentMaxH = GameSetupData.maxContinentHeightTilesStandard;
        int islandMinW = GameSetupData.minIslandWidthTilesStandard;
        int islandMaxW = GameSetupData.maxIslandWidthTilesStandard;
        int islandMinH = GameSetupData.minIslandHeightTilesStandard;
        int islandMaxH = GameSetupData.maxIslandHeightTilesStandard;

        switch (size)
        {
            case GameManager.MapSize.Small:
                continentMinW = GameSetupData.minContinentWidthTilesSmall;
                continentMaxW = GameSetupData.maxContinentWidthTilesSmall;
                continentMinH = GameSetupData.minContinentHeightTilesSmall;
                continentMaxH = GameSetupData.maxContinentHeightTilesSmall;
                islandMinW = GameSetupData.minIslandWidthTilesSmall;
                islandMaxW = GameSetupData.maxIslandWidthTilesSmall;
                islandMinH = GameSetupData.minIslandHeightTilesSmall;
                islandMaxH = GameSetupData.maxIslandHeightTilesSmall;
                break;
            case GameManager.MapSize.Large:
                continentMinW = GameSetupData.minContinentWidthTilesLarge;
                continentMaxW = GameSetupData.maxContinentWidthTilesLarge;
                continentMinH = GameSetupData.minContinentHeightTilesLarge;
                continentMaxH = GameSetupData.maxContinentHeightTilesLarge;
                islandMinW = GameSetupData.minIslandWidthTilesLarge;
                islandMaxW = GameSetupData.maxIslandWidthTilesLarge;
                islandMinH = GameSetupData.minIslandHeightTilesLarge;
                islandMaxH = GameSetupData.maxIslandHeightTilesLarge;
                break;
        }

        float sizeMul = GameSetupData.continentSizeMultiplier;
        continentMinW = Mathf.RoundToInt(continentMinW * sizeMul);
        continentMaxW = Mathf.RoundToInt(continentMaxW * sizeMul);
        continentMinH = Mathf.RoundToInt(continentMinH * sizeMul);
        continentMaxH = Mathf.RoundToInt(continentMaxH * sizeMul);

        GameSetupData.continentMinWidthTiles = continentMinW;
        GameSetupData.continentMaxWidthTiles = continentMaxW;
        GameSetupData.continentMinHeightTiles = continentMinH;
        GameSetupData.continentMaxHeightTiles = continentMaxH;

        int autoMinDistance = Mathf.Max(2, Mathf.RoundToInt(Mathf.Min(continentMinW, continentMinH) * 0.35f));
        GameSetupData.continentMinDistanceTiles = autoMinDistance;

        int minIslandDim = Mathf.Min(islandMinW, islandMinH);
        int maxIslandDim = Mathf.Max(islandMaxW, islandMaxH);
        GameSetupData.islandMinRadiusTiles = Mathf.Max(1, minIslandDim / 2);
        GameSetupData.islandMaxRadiusTiles = Mathf.Max(GameSetupData.islandMinRadiusTiles, maxIslandDim / 2);
        GameSetupData.islandMinDistanceFromContinents = Mathf.Max(2, GameSetupData.islandMinRadiusTiles);

        if (GameSetupData.lakeMinRadiusTiles <= 0) GameSetupData.lakeMinRadiusTiles = 3;
        if (GameSetupData.lakeMaxRadiusTiles <= 0) GameSetupData.lakeMaxRadiusTiles = Mathf.Max(3, 12);
    }

    private IEnumerator GenerateSurfaceThenSteps()
    {
        Debug.Log("PlanetSandbox: Starting full surface generation (GenerateSurface coroutine)...");
        IEnumerator gen = null;
        try
        {
            gen = planetGenerator.GenerateSurface();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("PlanetSandbox: Exception when calling GenerateSurface(): " + ex.Message);
            yield break;
        }

        if (gen != null)
        {
            yield return StartCoroutine(gen);
            Debug.Log("PlanetSandbox: GenerateSurface coroutine finished.");
        }

        // Proceed with minimap and chunk steps after generation
        if (generateMinimap)
        {
            try
            {
                int w, h;
                minimapUI.GetPlanetLUT(planetGenerator.planetIndex, out w, out h);
                minimapUI.GetTileAtlasColors(planetGenerator.planetIndex, false);
                Debug.Log("PlanetSandbox: Requested minimap LUT and tile atlas generation (post-generation).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("PlanetSandbox: Minimapping request failed: " + ex.Message);
            }
        }

        if (buildTerrainChunks)
        {
            try
            {
                hexMapChunkManager.Rebuild(planetGenerator);
                Debug.Log("PlanetSandbox: Rebuild triggered on HexMapChunkManager (post-generation).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("PlanetSandbox: Failed to call Rebuild() on HexMapChunkManager: " + ex.Message);
            }
        }
    }

    [ContextMenu("Rebuild Chunks")]
    public void RebuildChunks()
    {
        if (hexMapChunkManager == null)
        {
            Debug.LogWarning("PlanetSandbox: HexMapChunkManager not assigned.");
            return;
        }

        try
        {
            hexMapChunkManager.Rebuild(planetGenerator);
            Debug.Log("PlanetSandbox: RebuildChunks invoked.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("PlanetSandbox: RebuildChunks failed: " + ex.Message);
        }
    }

    [ContextMenu("Release GPU Caches")]
    public void ReleaseGpuCaches()
    {
        try
        {
            PlanetTextureBaker.ClearAllCaches();
            Debug.Log("PlanetSandbox: PlanetTextureBaker.ClearAllCaches() called.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("PlanetSandbox: Failed to clear PlanetTextureBaker caches: " + ex.Message);
        }

        if (hexMapChunkManager != null)
        {
            try
            {
                // call ReleaseGpuResources if present
                var method = typeof(HexMapChunkManager).GetMethod("ReleaseGpuResources");
                if (method != null)
                {
                    method.Invoke(hexMapChunkManager, null);
                    Debug.Log("PlanetSandbox: Called HexMapChunkManager.ReleaseGpuResources().");
                }
            }
            catch { }
        }
    }
}
