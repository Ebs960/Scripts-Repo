using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Scatters biome-specific prefabs (e.g. cactuses, trees) on the planet using biome mask textures and BiomeSettings.
/// </summary>
[ExecuteAlways]
public class BiomePrefabScatterer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign either a PlanetGenerator or MoonGenerator here.")]
    public MonoBehaviour generator; // Can be PlanetGenerator or MoonGenerator
    public SpaceGraphicsToolkit.Landscape.SgtSphereLandscape landscape;
    [Tooltip("Global density multiplier for all scattered prefabs. Higher = more prefabs.")]
    [Range(0.1f, 5.0f)]
    public float globalDensity = 1.0f;
    [Tooltip("Random seed for scatter placement (0 = random)")]
    public int scatterSeed = 0;
    [Tooltip("Parent container for scattered prefabs (auto-created if null)")]
    public Transform scatterContainer;

    private List<GameObject> spawnedPrefabs = new List<GameObject>();

    // Object pooling for biome prefabs
    private Dictionary<GameObject, Queue<GameObject>> prefabPools = new();
    private List<GameObject> activePrefabs = new();

    private GameObject GetPooledObject(GameObject prefab)
    {
        if (!prefabPools.TryGetValue(prefab, out var pool))
        {
            pool = new Queue<GameObject>();
            prefabPools[prefab] = pool;
        }
        if (pool.Count > 0)
        {
            var go = pool.Dequeue();
            go.SetActive(true);
            return go;
        }
        else
        {
            return Application.isPlaying ? Instantiate(prefab) : (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
        }
    }

    private void ReturnPooledObject(GameObject prefab, GameObject go)
    {
        go.SetActive(false);
        if (!prefabPools.TryGetValue(prefab, out var pool))
        {
            pool = new Queue<GameObject>();
            prefabPools[prefab] = pool;
        }
        pool.Enqueue(go);
    }

    /// <summary>
    /// Coroutine version: Scatters all prefabs and updates the loading panel if provided.
    /// </summary>
    public IEnumerator ScatterAllPrefabsCoroutine(LoadingPanelController loadingPanel = null)
    {
        ClearAllPrefabs();

        // Support both PlanetGenerator and MoonGenerator
        IcoSphereGrid grid = null;
        List<BiomeSettings> biomeSettingsList = null;
        System.Func<int, HexTileData> getHexTileData = null;
        string generatorType = "";

        if (generator is PlanetGenerator pg)
        {
            if (pg.Grid == null) { Debug.LogWarning("BiomePrefabScatterer: PlanetGenerator's Grid is null."); yield break; }
            grid = pg.Grid;
            biomeSettingsList = pg.biomeSettings;
            getHexTileData = pg.GetHexTileData;
            generatorType = "Planet";
        }
        else if (generator is MoonGenerator mg)
        {
            if (mg.Grid == null) { Debug.LogWarning("BiomePrefabScatterer: MoonGenerator's Grid is null."); yield break; }
            grid = mg.Grid;
            // Use reflection for private field if needed, but here we use the public API
            // Assume biomeSettings is private, so add a public getter if needed
            var biomeSettingsField = typeof(MoonGenerator).GetField("biomeSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            biomeSettingsList = (List<BiomeSettings>)biomeSettingsField?.GetValue(mg);
            getHexTileData = mg.GetHexTileData;
            generatorType = "Moon";
        }
        else
        {
            Debug.LogWarning("BiomePrefabScatterer: Generator must be a PlanetGenerator or MoonGenerator.");
            yield break;
        }

        if (grid == null || biomeSettingsList == null || landscape == null)
        {
            Debug.LogWarning($"BiomePrefabScatterer: Missing {generatorType}Generator, Grid, BiomeSettings, or Landscape references.");
            yield break;
        }

        if (scatterContainer == null)
        {
            var containerObj = new GameObject("BiomePrefabScatterContainer");
            containerObj.transform.SetParent(transform, false);
            scatterContainer = containerObj.transform;
        }

        System.Random rand = (scatterSeed == 0) ? new System.Random() : new System.Random(scatterSeed);

        float bodyRadius = landscape.Radius;
        int tileCount = grid.TileCount;

        // Create a fast lookup for biome settings
        var biomeSettingsLookup = new Dictionary<Biome, BiomeSettings>();
        foreach (var bs in biomeSettingsList)
        {
            if (bs != null && !biomeSettingsLookup.ContainsKey(bs.biome))
            {
                biomeSettingsLookup.Add(bs.biome, bs);
            }
        }

        // Iterate through every tile on the planet

        for (int i = 0; i < tileCount; i++)
        {
            var tileData = getHexTileData(i);
            if (tileData == null || !tileData.isLand) continue;

            // Find the settings for this tile's biome
            if (biomeSettingsLookup.TryGetValue(tileData.biome, out var bs))
            {
                // Check if this biome has features to scatter
                if (bs.featurePrefabs != null && bs.featurePrefabs.Length > 0)
                {
                    // Use featureDensity as a probability to place an object
                    if (rand.NextDouble() < bs.featureDensity * globalDensity)
                    {
                        var prefab = bs.featurePrefabs[rand.Next(bs.featurePrefabs.Length)];
                        if (prefab == null) continue;

                        var go = GetPooledObject(prefab);
                        
                        // Use the tile's actual world position
                        Vector3 worldPos = grid.tileCenters[i];
                        
                        go.transform.SetParent(scatterContainer, false);
                        go.transform.position = worldPos;
                        go.transform.up = (go.transform.position - transform.position).normalized;
                        go.transform.Rotate(Vector3.up, (float)(rand.NextDouble() * 360f), Space.Self);
                        
                        float scale = Mathf.Lerp(bs.featureScaleRange.x, bs.featureScaleRange.y, (float)rand.NextDouble());
                        go.transform.localScale = Vector3.one * scale;
                        
                        activePrefabs.Add(go);
                    }
                }
            }

            // Yield every so often to prevent freezing
            if (i > 0 && i % 200 == 0)
            {
                if (loadingPanel != null)
                {
                    float progress = (float)i / tileCount;
                    loadingPanel.SetProgress(progress);
                    loadingPanel.SetStatus($"Scattering Biome Features... ({progress * 100f:F0}%)");
                }
                yield return null;
            }
        }

        if (loadingPanel != null)
        {
            loadingPanel.SetProgress(1f);
            loadingPanel.SetStatus("Finishing up biome features...");
        }
        Debug.Log($"BiomePrefabScatterer: Scattered {activePrefabs.Count} prefabs.");
    }

    /// <summary>
    /// Synchronous version for quick/manual use (no progress bar).
    /// </summary>
    public void ScatterAllPrefabs()
    {
        StartCoroutine(ScatterAllPrefabsCoroutine(null));
    }

    public void ClearAllPrefabs()
    {
        foreach (var go in activePrefabs)
        {
            if (go != null)
            {
                var prefab = go.name.Contains("(Clone)") ? go.name.Replace("(Clone)", "").Trim() : go.name;
                ReturnPooledObject(go, go); // Pool by instance
            }
        }
        activePrefabs.Clear();
        if (scatterContainer != null && scatterContainer.childCount == 0)
        {
            if (Application.isPlaying) Destroy(scatterContainer.gameObject);
            else DestroyImmediate(scatterContainer.gameObject);
            scatterContainer = null;
        }
    }
} 