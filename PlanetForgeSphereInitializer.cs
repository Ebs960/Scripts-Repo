using System.Collections.Generic;
using UnityEngine;
using SpaceGraphicsToolkit.Landscape;
using SpaceGraphicsToolkit.Sky;
using SpaceGraphicsToolkit.Cloud;
using SpaceGraphicsToolkit.Ocean;

/// <summary>
/// Helper component that sets up a Planet Forge style sphere planet using
/// the <see cref="SgtSphereLandscape"/> system. This allows runtime creation
/// of a fully featured planet with optional atmosphere and ocean.
/// Texture lists are copied into a <see cref="SgtLandscapeBundle"/> so that
/// biomes and detail layers can access them.
/// </summary>
[ExecuteAlways]
public class PlanetForgeSphereInitializer : MonoBehaviour
{
    [Header("Landscape")]
    public float radius = 500f;
    public Material surfaceMaterial;
    public SgtSphereLandscape landscape;
    public SgtLandscapeBundle bundle;

    [Header("Biome References")]
    [Tooltip("Assign the BiomeTextureManager that will provide the biome index texture.")]
    public BiomeTextureManager biomeTextureManager;
    [Tooltip("Assign the IcoSphereGrid instance used by your planet generator.")]
    public IcoSphereGrid grid;

    [Header("Bundle Textures")]
    public List<Texture2D> heightTextures = new();
    public List<Texture2D> gradientTextures = new();
    public List<Texture2D> maskTextures = new();

    [Header("Optional Effects")]
    public bool addAtmosphere = true;
    public bool addOcean = true;

    void Reset()
    {
        Setup();
    }

    void Awake()
    {
        Setup();
    }

    // Ensures all required components exist and are configured.
    public void Setup()
    {
        if (landscape == null)
            landscape = GetComponent<SgtSphereLandscape>() ?? gameObject.AddComponent<SgtSphereLandscape>();
        if (bundle == null)
            bundle = GetComponent<SgtLandscapeBundle>() ?? gameObject.AddComponent<SgtLandscapeBundle>();

        landscape.Radius = radius;
        landscape.Material = surfaceMaterial;
        landscape.Bundle = bundle;

        // Clear and reassign textures
        bundle.HeightTextures.Clear();
        bundle.HeightTextures.AddRange(heightTextures);
        bundle.GradientTextures.Clear();
        bundle.GradientTextures.AddRange(gradientTextures);
        bundle.MaskTextures.Clear();
        bundle.MaskTextures.AddRange(maskTextures);

        // Force bundle to rebuild its texture atlases
        bundle.MarkAsDirty();
        bundle.Regenerate();

// 🔧 NEW: Automatically assign the BiomeTextureManager singleton
    if (biomeTextureManager == null)
    {
        biomeTextureManager = BiomeTextureManager.Instance;
        if (biomeTextureManager == null)
        {
            Debug.LogError("[PlanetForgeSphereInitializer] No BiomeTextureManager found in the scene!");
        }
    }


        if (surfaceMaterial != null)
        {
            // Set the biome atlas textures in the material
            surfaceMaterial.SetTexture("_HeightTopologyAtlas", bundle.HeightTopologyAtlas);
            surfaceMaterial.SetTexture("_GradientAtlas", bundle.GradientAtlas);
            surfaceMaterial.SetTexture("_MaskTopologyAtlas", bundle.MaskTopologyAtlas);

            // Apply biome index texture from the assigned BiomeTextureManager

            if (biomeTextureManager != null && grid != null)
            {
                biomeTextureManager.RegisterTarget(grid, surfaceMaterial);
            }

        }

        if (addAtmosphere && GetComponentInChildren<SgtSky>() == null)
        {
            var sky = SgtSky.Create(gameObject.layer, transform);
            sky.InnerMeshRadius = radius;
            sky.Height = radius / 10f;
        }

        if (addOcean && GetComponentInChildren<SgtOcean>() == null)
        {
            var ocean = SgtOcean.Create(gameObject.layer, transform);
            ocean.Radius = radius;
        }
    }

    /// <summary>
    /// Utility method to quickly add a biome child object with enhanced settings.
    /// </summary>
    public SgtLandscapeBiome AddBiome(string name, int gradientIndex, int heightIndex, int maskIndex)
    {
        if (landscape == null)
            return null;

        var go = new GameObject(name);
        go.transform.SetParent(landscape.transform, false);
        var biome = go.AddComponent<SgtLandscapeBiome>();

        // Enhanced biome setup
        biome.GradientIndex = gradientIndex;
        biome.Color = true; // Enable color blending
        biome.Space = SgtLandscapeBiome.SpaceType.Global;

        // Configure mask
        biome.Mask = true;
        biome.MaskIndex = maskIndex;
        biome.MaskSharpness = 2f; // Adjust for clearer biome boundaries

        // Add height layer
        var layer = new SgtLandscapeBiome.SgtLandscapeBiomeLayer
        {
            HeightIndex = heightIndex,
            HeightRange = 10f,
            HeightMidpoint = 0.5f,
            GlobalSize = 100f
        };
        biome.Layers.Add(layer);

        return biome;
    }
}
