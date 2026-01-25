using System;
using UnityEditor;
using UnityEngine;

public class GenerateNoise3D : EditorWindow
{
    int size = 64;
    int octaves = 3;
    float frequency = 2.0f;
    float lacunarity = 2.0f;
    float persistence = 0.5f;
    int seed = 0;
    bool generateCurl = false;
    string outputFolder = "Assets/Generated";

    [MenuItem("Tools/Generate/Noise3D Generator")]
    static void ShowWindow()
    {
        var w = GetWindow<GenerateNoise3D>("Noise3D Generator");
        w.minSize = new Vector2(360, 200);
    }

    void OnGUI()
    {
        GUILayout.Label("Cheap Texture3D Generator", EditorStyles.boldLabel);
        size = EditorGUILayout.IntPopup("Size (cube)", size, new[] { "32", "64", "128", "256" }, new[] { 32, 64, 128, 256 });
        octaves = EditorGUILayout.IntSlider("Octaves", octaves, 1, 6);
        frequency = EditorGUILayout.Slider("Frequency", frequency, 0.25f, 8f);
        lacunarity = EditorGUILayout.Slider("Lacunarity", lacunarity, 1f, 4f);
        persistence = EditorGUILayout.Slider("Persistence", persistence, 0.1f, 1f);
        seed = EditorGUILayout.IntField("Seed", seed);
        generateCurl = EditorGUILayout.Toggle("Generate Curl Vector Field", generateCurl);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        GUILayout.Space(6);
        EditorGUILayout.HelpBox("Generates a Texture3D asset. Scalar noise will be stored in RGB channels equally. Curl mode packs normalized curl vector into RGB.", MessageType.Info);

        if (GUILayout.Button("Generate Texture3D"))
        {
            GenerateAndSave();
        }
    }

    void GenerateAndSave()
    {
        if (!System.IO.Directory.Exists(outputFolder))
        {
            System.IO.Directory.CreateDirectory(outputFolder);
            AssetDatabase.Refresh();
        }

        string name = generateCurl ? $"Noise3D_Curl_{size}_{seed}.asset" : $"Noise3D_Scalar_{size}_{seed}.asset";
        string path = System.IO.Path.Combine(outputFolder, name);

        try
        {
            var tex = new Texture3D(size, size, size, TextureFormat.RGBA32, true);
            Color[] cols = new Color[size * size * size];
            System.Random rng = new System.Random(seed);

            // Precompute scalar fields if curl requested
            float[,,] n1 = null, n2 = null, n3 = null;
            if (generateCurl)
            {
                n1 = new float[size, size, size];
                n2 = new float[size, size, size];
                n3 = new float[size, size, size];
            }

            float invSize = 1.0f / size;
            for (int z = 0; z < size; z++)
            {
                EditorUtility.DisplayProgressBar("Generating Noise3D", $"Slice {z+1}/{size}", (float)z / size);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) * invSize;
                        float v = (y + 0.5f) * invSize;
                        float w = (z + 0.5f) * invSize;

                        if (generateCurl)
                        {
                            // create three related scalar fields using offsets to form a vector field
                            float s1 = FractalNoise3D(u + 0.13f, v + 0.17f, w + 0.19f);
                            float s2 = FractalNoise3D(u + 0.23f, v + 0.29f, w + 0.31f);
                            float s3 = FractalNoise3D(u + 0.37f, v + 0.41f, w + 0.43f);
                            n1[x, y, z] = s1;
                            n2[x, y, z] = s2;
                            n3[x, y, z] = s3;
                        }
                        else
                        {
                            float s = FractalNoise3D(u, v, w);
                            cols[x + y * size + z * size * size] = new Color(s, s, s, 1f);
                        }
                    }
                }
            }

            if (generateCurl)
            {
                // compute curl using central differences on the precomputed scalar fields
                for (int z = 0; z < size; z++)
                {
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            // neighbors with clamped indexing
                            int xm = Mathf.Max(x - 1, 0);
                            int xp = Mathf.Min(x + 1, size - 1);
                            int ym = Mathf.Max(y - 1, 0);
                            int yp = Mathf.Min(y + 1, size - 1);
                            int zm = Mathf.Max(z - 1, 0);
                            int zp = Mathf.Min(z + 1, size - 1);

                            float dN3_dy = (n3[x, yp, z] - n3[x, ym, z]) * 0.5f * size;
                            float dN2_dz = (n2[x, y, zp] - n2[x, y, zm]) * 0.5f * size;
                            float cx = dN3_dy - dN2_dz;

                            float dN1_dz = (n1[x, y, zp] - n1[x, y, zm]) * 0.5f * size;
                            float dN3_dx = (n3[xp, y, z] - n3[xm, y, z]) * 0.5f * size;
                            float cy = dN1_dz - dN3_dx;

                            float dN2_dx = (n2[xp, y, z] - n2[xm, y, z]) * 0.5f * size;
                            float dN1_dy = (n1[x, yp, z] - n1[x, ym, z]) * 0.5f * size;
                            float cz = dN2_dx - dN1_dy;

                            Vector3 curl = new Vector3(cx, cy, cz);
                            // normalize and map -1..1 -> 0..1
                            curl = Vector3.ClampMagnitude(curl, 1f);
                            Color enc = new Color(curl.x * 0.5f + 0.5f, curl.y * 0.5f + 0.5f, curl.z * 0.5f + 0.5f, 1f);
                            cols[x + y * size + z * size * size] = enc;
                        }
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, false);

            // Save asset
            AssetDatabase.CreateAsset(tex, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Noise3D", $"Created Texture3D at {path}", "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError("Noise3D generation failed: " + ex.Message);
        }
    }

    float FractalNoise3D(float x, float y, float z)
    {
        float amplitude = 1f;
        float freq = frequency;
        float sum = 0f;
        for (int i = 0; i < octaves; i++)
        {
            // Simple fallback: combine Perlin calls as a cheap 3D noise approximation
            float v = (Mathf.PerlinNoise(x * freq + z * 0.1f, y * freq + z * 0.2f) - 0.5f) * 2f;
            sum += (v * 0.5f + 0.5f) * amplitude;
            amplitude *= persistence;
            freq *= lacunarity;
        }
        return Mathf.Clamp01(sum);
    }
}

// NOTE: This simple generator is intended for quick prototyping. For better results replace
// FractalNoise3D with a proper 3D Simplex/Perlin implementation or use a compute shader.
