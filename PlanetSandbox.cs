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
    public bool runFullSurfaceGeneration = false;

    [Header("Behavior")]
    public bool autoGenerateOnStart = false;

    private void Start()
    {
        if (autoGenerateOnStart)
            Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (hexGrid == null)
        {
            Debug.LogWarning("PlanetSandbox: HexGridComponent is not assigned. Cannot generate grid.");
            return;
        }

        hexGrid.Generate(tilesX, tilesZ, mapWidth, mapHeight);

        if (planetGenerator == null)
        {
            Debug.LogWarning("PlanetSandbox: PlanetGenerator is not assigned. Place your PlanetGenerator in scene and assign it.");
        }
        else
        {
            // Inject the generated HexGrid into the PlanetGenerator so downstream builders
            // (HexMapChunkManager, bakers) use the scene-prepared grid.
            try
            {
                planetGenerator.SetGrid(hexGrid.grid);
                Debug.Log("PlanetSandbox: Injected HexGrid into PlanetGenerator.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("PlanetSandbox: Failed to inject grid into PlanetGenerator: " + ex.Message);
            }
        }

        // If requested, run full surface generation first (this populates tiles/biomes/elevation)
        if (runFullSurfaceGeneration)
        {
            if (planetGenerator == null)
            {
                Debug.LogWarning("PlanetSandbox: PlanetGenerator not assigned; cannot run full surface generation.");
            }
            else
            {
                StartCoroutine(GenerateSurfaceThenSteps());
                return; // coroutine will continue with minimap / chunks when complete
            }
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
