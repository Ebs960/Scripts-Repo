using UnityEngine;
using System.Collections.Generic;

public class BiomeTextureManager : MonoBehaviour
{
    public static BiomeTextureManager Instance { get; private set; }

    // Holds per-target data
    private class BiomeTarget
    {
        public IcoSphereGrid grid;
        public Material material;
        public Texture2D biomeIndexTexture;
        public Texture2D albedoTexture;
    }

    // Registered targets: key is grid instance
    private readonly Dictionary<IcoSphereGrid, BiomeTarget> targets = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Register a (grid, material) pair as a biome target. Call this from planet/moon generators.
    /// </summary>
    public void RegisterTarget(IcoSphereGrid grid, Material material)
    {
        if (grid == null || material == null) return;
        if (!targets.ContainsKey(grid))
        {
            targets[grid] = new BiomeTarget { grid = grid, material = material };
        }
        else
        {
            targets[grid].material = material;
        }

        // Immediately generate textures upon registration
        GenerateBiomeIndexTexture(grid);
        GenerateBiomeAlbedoTexture(grid);
    }

    /// <summary>
    /// Generate and assign a biome index texture for the given grid/material pair.
    /// </summary>
    public void GenerateBiomeIndexTexture(IcoSphereGrid grid)
    {
        if (grid == null || !targets.ContainsKey(grid))
        {
            Debug.LogWarning("[BiomeTextureManager] Tried to generate biome index for unregistered grid.");
            return;
        }
        var target = targets[grid];
        int tileCount = grid.TileCount;
        int width = Mathf.CeilToInt(Mathf.Sqrt(tileCount));
        int height = Mathf.CeilToInt(tileCount / (float)width);

        // Clean up old texture if present
        if (target.biomeIndexTexture != null)
        {
            Destroy(target.biomeIndexTexture);
        }

        target.biomeIndexTexture = new Texture2D(width, height, TextureFormat.R8, false);
        target.biomeIndexTexture.wrapMode = TextureWrapMode.Clamp;
        target.biomeIndexTexture.filterMode = FilterMode.Point;

        Color32[] pixels = new Color32[width * height];
        for (int i = 0; i < tileCount; i++)
        {
            HexTileData tile = TileDataHelper.Instance.GetTileData(i).tileData;
            byte biomeID = (byte)tile.biome;
            pixels[i] = new Color32(biomeID, 0, 0, 255); // biome ID in Red channel
        }

        target.biomeIndexTexture.SetPixels32(pixels);
        target.biomeIndexTexture.Apply();

        UpdateMaterialTextures(grid);
    }

    /// <summary>
    /// Generate and assign the albedo texture by sampling biome settings.
    /// </summary>
    public void GenerateBiomeAlbedoTexture(IcoSphereGrid grid)
    {
        if (grid == null || !targets.ContainsKey(grid)) return;

        var target = targets[grid];
        int tileCount = grid.TileCount;
        int width = Mathf.CeilToInt(Mathf.Sqrt(tileCount));
        int height = Mathf.CeilToInt(tileCount / (float)width);

        if (target.albedoTexture != null)
        {
            Destroy(target.albedoTexture);
        }

        target.albedoTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        target.albedoTexture.wrapMode = TextureWrapMode.Clamp;
        target.albedoTexture.filterMode = FilterMode.Bilinear;

        Color[] albedoPixels = new Color[width * height];

        var biomeList = FindObjectOfType<PlanetGenerator>()?.biomeSettings;

        for (int i = 0; i < tileCount; i++)
        {
            HexTileData tile = TileDataHelper.Instance.GetTileData(i).tileData;
            int biomeIndex = (int)tile.biome;

            // Default fallback color in case something's null
            Color sampledColor = Color.magenta;

            if (biomeList != null && biomeIndex >= 0 && biomeIndex < biomeList.Count)
            {
                var biome = biomeList[biomeIndex];
                if (biome != null && biome.albedoTexture != null)
                {
                    sampledColor = biome.albedoTexture.GetPixelBilinear(0.5f, 0.5f);
                }
            }

            albedoPixels[i] = sampledColor;
        }

        target.albedoTexture.SetPixels(albedoPixels);
        target.albedoTexture.Apply();

        UpdateMaterialTextures(grid);
    }

    /// <summary>
    /// Assign the biome index and albedo textures to the material.
    /// </summary>
    private void UpdateMaterialTextures(IcoSphereGrid grid)
    {
        if (!targets.ContainsKey(grid)) return;
        var target = targets[grid];
        if (target.material != null)
        {
            if (target.biomeIndexTexture != null)
            {
                target.material.SetTexture("_BiomeIndexTex", target.biomeIndexTexture);
                target.material.SetFloat("_BiomeTexWidth", target.biomeIndexTexture.width);
                target.material.SetFloat("_BiomeTexHeight", target.biomeIndexTexture.height);
                target.material.EnableKeyword("_USE_BIOME_TEX");
            }
            if (target.albedoTexture != null)
            {
                target.material.SetTexture("_AlbedoTex", target.albedoTexture);
                target.material.EnableKeyword("_USE_ALBEDO_TEX");
            }
        }
    }

    /// <summary>
    /// Get the biome index texture for a given grid (if needed elsewhere).
    /// </summary>
    public Texture2D GetBiomeIndexTexture(IcoSphereGrid grid)
    {
        if (targets.TryGetValue(grid, out var target))
            return target.biomeIndexTexture;
        return null;
    }

    private void OnDestroy()
    {
        foreach (var target in targets.Values)
        {
            if (target.biomeIndexTexture != null)
            {
                Destroy(target.biomeIndexTexture);
            }
            if (target.albedoTexture != null)
            {
                Destroy(target.albedoTexture);
            }
        }
        targets.Clear();
    }
}
