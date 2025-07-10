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
    public PlanetGenerator planetGenerator;
    public SpaceGraphicsToolkit.Landscape.SgtSphereLandscape landscape;
    [Tooltip("Number of scatter attempts per biome (higher = denser, but slower)")]
    public int scatterAttemptsPerBiome = 2000;
    [Tooltip("Random seed for scatter placement (0 = random)")]
    public int scatterSeed = 0;
    [Tooltip("Parent container for scattered prefabs (auto-created if null)")]
    public Transform scatterContainer;

    private List<GameObject> spawnedPrefabs = new List<GameObject>();

    /// <summary>
    /// Coroutine version: Scatters all prefabs and updates the loading panel if provided.
    /// </summary>
    public IEnumerator ScatterAllPrefabsCoroutine(LoadingPanelController loadingPanel = null)
    {
        ClearAllPrefabs();
        if (planetGenerator == null || landscape == null)
        {
            Debug.LogWarning("BiomePrefabScatterer: Missing references.");
            yield break;
        }
        var biomeSettings = planetGenerator.biomeSettings;
        var biomeMaskTextures = GenerateBiomeMaskTexturesFromPlanet();
        int biomeCount = Mathf.Min(biomeSettings.Count, biomeMaskTextures.Count);
        if (scatterContainer == null)
        {
            var containerObj = new GameObject("BiomePrefabScatterContainer");
            containerObj.transform.SetParent(transform, false);
            scatterContainer = containerObj.transform;
        }
        System.Random rand = scatterSeed == 0 ? new System.Random() : new System.Random(scatterSeed);
        float planetRadius = landscape.Radius;
        int totalToScatter = 0;
        for (int b = 0; b < biomeCount; b++)
        {
            var bs = biomeSettings[b];
            if (bs.featurePrefabs == null || bs.featurePrefabs.Length == 0) continue;
            totalToScatter += Mathf.RoundToInt(bs.featureDensity * scatterAttemptsPerBiome);
        }
        int scattered = 0;
        for (int b = 0; b < biomeCount; b++)
        {
            var bs = biomeSettings[b];
            var maskTex = biomeMaskTextures[b];
            if (bs.featurePrefabs == null || bs.featurePrefabs.Length == 0 || maskTex == null) continue;
            int numToScatter = Mathf.RoundToInt(bs.featureDensity * scatterAttemptsPerBiome);
            for (int i = 0; i < numToScatter; i++)
            {
                float theta = (float)(rand.NextDouble() * Mathf.PI * 2f);
                float phi = (float)(Mathf.Acos(2f * (float)rand.NextDouble() - 1f));
                Vector3 dir = new Vector3(
                    Mathf.Sin(phi) * Mathf.Cos(theta),
                    Mathf.Cos(phi),
                    Mathf.Sin(phi) * Mathf.Sin(theta)
                );
                Vector3 worldPos = dir * planetRadius;
                float u = 0.5f + Mathf.Atan2(dir.x, dir.z) / (2f * Mathf.PI);
                float v = 0.5f - Mathf.Asin(dir.y) / Mathf.PI;
                Color maskCol = maskTex.GetPixelBilinear(u, v);
                if (maskCol.r < 0.5f) continue;
                var prefab = bs.featurePrefabs[rand.Next(bs.featurePrefabs.Length)];
                if (prefab == null) continue;
                var go = Application.isPlaying ? Instantiate(prefab) : (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetParent(scatterContainer, true);
                go.transform.position = transform.TransformPoint(worldPos);
                go.transform.up = (go.transform.position - transform.position).normalized;
                go.transform.Rotate(Vector3.up, (float)(rand.NextDouble() * 360f), Space.Self);
                float scale = Mathf.Lerp(bs.featureScaleRange.x, bs.featureScaleRange.y, (float)rand.NextDouble());
                go.transform.localScale = Vector3.one * scale;
                spawnedPrefabs.Add(go);
                scattered++;
                if (loadingPanel != null && scattered % 50 == 0)
                {
                    float progress = (float)scattered / Mathf.Max(1, totalToScatter);
                    loadingPanel.SetProgress(progress);
                    loadingPanel.SetStatus($"Scattering Biome Features... ({progress * 100f:F0}%)");
                    yield return null;
                }
            }
        }
        if (loadingPanel != null)
        {
            loadingPanel.SetProgress(1f);
            loadingPanel.SetStatus("Finishing up biome features...");
        }
        Debug.Log($"BiomePrefabScatterer: Scattered {spawnedPrefabs.Count} prefabs.");
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
        foreach (var go in spawnedPrefabs)
        {
            if (go != null)
            {
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }
        spawnedPrefabs.Clear();
        if (scatterContainer != null && scatterContainer.childCount == 0)
        {
            if (Application.isPlaying) Destroy(scatterContainer.gameObject);
            else DestroyImmediate(scatterContainer.gameObject);
            scatterContainer = null;
        }
    }

    // Helper to get the mask textures from the planet (assumes they are generated and available)
    private List<Texture2D> GenerateBiomeMaskTexturesFromPlanet()
    {
        var result = new List<Texture2D>();
        var mat = landscape.GetComponent<Renderer>()?.sharedMaterial;
        if (mat == null) return result;
        for (int i = 0; i < planetGenerator.biomeSettings.Count; i++)
        {
            var mask = mat.GetTexture($"_BiomeMask{i}") as Texture2D;
            result.Add(mask);
        }
        return result;
    }
} 