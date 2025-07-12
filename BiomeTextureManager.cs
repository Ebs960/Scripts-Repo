using UnityEngine;

public class BiomeTextureManager : MonoBehaviour
{
    public static BiomeTextureManager Instance { get; private set; }

    [Header("Biome Texture Settings")]
    public int textureResolution = 512;
    public Texture2D biomeIndexTexture; // Each pixel = one tile's biome
    public Material targetMaterial;     // Material that will use the shader

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
    /// Call this when the grid is ready and all tile biome info is known.
    /// </summary>
    public void GenerateBiomeIndexTexture(IcoSphereGrid grid)
    {
        int tileCount = grid.TileCount;
        int width = Mathf.CeilToInt(Mathf.Sqrt(tileCount));
        int height = Mathf.CeilToInt(tileCount / (float)width);

        if (biomeIndexTexture != null)
        {
            Destroy(biomeIndexTexture);
        }

        biomeIndexTexture = new Texture2D(width, height, TextureFormat.R8, false);
        biomeIndexTexture.wrapMode = TextureWrapMode.Clamp;
        biomeIndexTexture.filterMode = FilterMode.Point;

        Color32[] pixels = new Color32[width * height];

        for (int i = 0; i < tileCount; i++)
        {
            HexTileData tile = TileDataHelper.Instance.GetTileData(i).tileData;
            byte biomeID = (byte)tile.biome;
            pixels[i] = new Color32(biomeID, 0, 0, 255); // biome ID in Red channel
        }

        biomeIndexTexture.SetPixels32(pixels);
        biomeIndexTexture.Apply();

        UpdateMaterialTextures();
    }

    private void UpdateMaterialTextures()
    {
        if (targetMaterial != null && biomeIndexTexture != null)
        {
            targetMaterial.SetTexture("_BiomeIndexTex", biomeIndexTexture);
            targetMaterial.SetFloat("_BiomeTexWidth", biomeIndexTexture.width);
            targetMaterial.SetFloat("_BiomeTexHeight", biomeIndexTexture.height);
            
            // Make sure the material knows we're using biome textures
            targetMaterial.EnableKeyword("_USE_BIOME_TEX");
            
            // Update the planet's SgtLandscapeBundle if it exists
            var planetInitializer = FindObjectOfType<PlanetForgeSphereInitializer>();
            if (planetInitializer != null && planetInitializer.bundle != null)
            {
                planetInitializer.Setup(); // Refresh the bundle setup
            }
        }
    }

    private void OnDestroy()
    {
        if (biomeIndexTexture != null)
        {
            Destroy(biomeIndexTexture);
        }
    }
}
