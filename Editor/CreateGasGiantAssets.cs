using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CreateGasGiantAssets
{
    [MenuItem("Tools/Create Sample Gas Giant Assets")]
    public static void CreateAssets()
    {
        // Create folder
        string folder = "Assets/Planets/Samples";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Planets", "Samples");

        // Create a banded gradient texture
        Texture2D grad = CreateBandedGradientTexture(1024, 32, new Color[] {
            new Color(0.95f,0.85f,0.7f),
            new Color(0.9f,0.8f,0.6f),
            new Color(0.8f,0.6f,0.4f),
            new Color(0.95f,0.9f,0.8f)
        });
        string texPath = Path.Combine(folder, "GasGiant_Gradient.png");
        File.WriteAllBytes(texPath, grad.EncodeToPNG());
        AssetDatabase.ImportAsset(texPath);
        TextureImporter ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
        Texture2D importedGrad = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        // Create a simple noise texture
        Texture2D noise = CreatePerlinNoiseTexture(512,512);
        string noisePath = Path.Combine(folder, "GasGiant_Noise.png");
        File.WriteAllBytes(noisePath, noise.EncodeToPNG());
        AssetDatabase.ImportAsset(noisePath);
        TextureImporter nti = AssetImporter.GetAtPath(noisePath) as TextureImporter;
        if (nti != null)
        {
            nti.textureType = TextureImporterType.Default;
            nti.wrapMode = TextureWrapMode.Repeat;
            nti.mipmapEnabled = true;
            nti.SaveAndReimport();
        }
        Texture2D importedNoise = AssetDatabase.LoadAssetAtPath<Texture2D>(noisePath);

        // Create material
        Shader shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
        Material mat = new Material(shader);
        mat.name = "Mat_GasGiant_Sample";
        // Try common property names for base texture
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", importedGrad);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", importedGrad);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
        // Assign noise to a property if present
        if (mat.HasProperty("_NoiseTex")) mat.SetTexture("_NoiseTex", importedNoise);

        string matPath = Path.Combine(folder, mat.name + ".mat");
        AssetDatabase.CreateAsset(mat, matPath);

        // Create GasGiantVisualData asset if type exists
        var ggType = typeof(UnityEngine.Object).Assembly.GetType("GasGiantVisualData") ?? null;
        if (ggType == null)
        {
            // Try project's assembly
            ggType = System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "GasGiantVisualData");
        }

        if (ggType != null)
        {
            ScriptableObject gg = ScriptableObject.CreateInstance(ggType);
            var soPath = Path.Combine(folder, "GasGiantVisualData_Sample.asset");
            // try to set fields via reflection
            var baseGradField = ggType.GetField("baseGradient");
            var noiseField = ggType.GetField("noiseTexture");
            var tintField = ggType.GetField("tint");
            var sharpField = ggType.GetField("bandSharpness");
            var stormField = ggType.GetField("stormStrength");
            var rotField = ggType.GetField("rotationSpeed");
            if (baseGradField != null) baseGradField.SetValue(gg, importedGrad);
            if (noiseField != null) noiseField.SetValue(gg, importedNoise);
            if (tintField != null) tintField.SetValue(gg, new Color(1f,0.9f,0.8f));
            if (sharpField != null) sharpField.SetValue(gg, 2f);
            if (stormField != null) stormField.SetValue(gg, 0.6f);
            if (rotField != null) rotField.SetValue(gg, 1f);

            AssetDatabase.CreateAsset(gg, soPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Gas Giant Assets", $"Created sample material and textures in {folder}", "OK");
    }

    private static Texture2D CreateBandedGradientTexture(int width, int height, Color[] bandColors)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, true);
        int bands = bandColors.Length;
        for (int y = 0; y < height; y++)
        {
            float v = (float)y / (height - 1);
            // make bands across width using a sin+step pattern for natural look
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float bandPos = u * bands * 1.5f + Mathf.Sin(u * 10f) * 0.02f;
                int idx = Mathf.Abs(Mathf.FloorToInt(bandPos)) % bands;
                Color c = bandColors[idx];
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    private static Texture2D CreatePerlinNoiseTexture(int w, int h)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = (float)x / w * 4f;
                float ny = (float)y / h * 4f;
                float v = Mathf.PerlinNoise(nx, ny);
                Color c = new Color(v, v, v, 1f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }
}
