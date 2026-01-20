using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility: create a Texture2DArray from all textures in a folder.
/// Select the source folder in Project view and run Window/Create Texture2DArray.
/// </summary>
public class CreateTexture2DArrayFromFolder : EditorWindow
{
    private string folderPath = "Assets/Textures";
    private string outputPath = "Assets/TextureArrays";
    private int maxTextureSize = 2048;
    private bool generateMips = true;
    private bool useHDR = false; // uses RGBAHalf when true

    [MenuItem("Window/Tools/Create Texture2DArray from Folder")]
    static void OpenWindow()
    {
        GetWindow<CreateTexture2DArrayFromFolder>("Create Texture2DArray");
    }

    private void OnGUI()
    {
        GUILayout.Label("Texture2DArray Builder", EditorStyles.boldLabel);
        folderPath = EditorGUILayout.TextField("Source Folder", folderPath);
        outputPath = EditorGUILayout.TextField("Output Folder", outputPath);
        maxTextureSize = EditorGUILayout.IntField("Max Size", maxTextureSize);
        generateMips = EditorGUILayout.Toggle("Generate Mips", generateMips);
        useHDR = EditorGUILayout.Toggle("Use HDR (RGBAHalf)", useHDR);

        if (GUILayout.Button("Create Texture2DArray from Folder"))
        {
            CreateArrayFromFolder(folderPath, outputPath, maxTextureSize, generateMips, useHDR);
        }
    }

    private void CreateArrayFromFolder(string srcFolder, string outFolder, int maxSize, bool mips, bool hdr)
    {
        if (!AssetDatabase.IsValidFolder(outFolder))
        {
            Directory.CreateDirectory(outFolder);
            AssetDatabase.Refresh();
        }

        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { srcFolder });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("No textures found in folder: " + srcFolder);
            return;
        }

        var textures = guids.Select(g => AssetDatabase.GUIDToAssetPath(g)).Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p)).Where(t => t != null).ToArray();
        if (textures.Length == 0)
        {
            Debug.LogWarning("No valid Texture2D assets found.");
            return;
        }

        int width = textures[0].width;
        int height = textures[0].height;
        foreach (var t in textures)
        {
            if (t.width != width || t.height != height)
            {
                Debug.LogWarning($"Texture size mismatch: {t.name} ({t.width}x{t.height}) - all textures must match. Aborting.");
                return;
            }
        }

        int depth = textures.Length;
        var format = hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;
        var texArray = new Texture2DArray(width, height, depth, format, mips, true);
        texArray.wrapMode = TextureWrapMode.Repeat;
        texArray.filterMode = FilterMode.Bilinear;

        for (int i = 0; i < textures.Length; i++)
        {
            var t = textures[i];
            try
            {
                Graphics.CopyTexture(t, 0, 0, texArray, i, 0);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to copy texture {t.name} into array slice {i}: {ex.Message}");
            }
        }

        string name = Path.GetFileName(srcFolder) + "_Array.asset";
        string outAsset = Path.Combine(outFolder, name).Replace("\\", "/");
        AssetDatabase.CreateAsset(texArray, outAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Created Texture2DArray at {outAsset} with {depth} slices.");
    }
}
