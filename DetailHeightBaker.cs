using UnityEditor;
using Unity.EditorCoroutines.Editor;
using UnityEngine;

public class DetailHeightBaker : MonoBehaviour
{
    [MenuItem("Tools/Generate Detail Height Texture")]
    static void Generate()
    {
        EditorCoroutineUtility.StartCoroutineOwnerless(GenerateRoutine());
    }

    static System.Collections.IEnumerator GenerateRoutine()
    {
        const int width  = 4096;
        const int height = 2048;
        Texture2D tex = new Texture2D(width, height, TextureFormat.R16, false, true);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 p = new Vector3(x, 0, y) / 128f;
                float v = Mathf.PerlinNoise(p.x, p.z); // simple fallback noise
                tex.SetPixel(x, y, new Color(v, v, v, 1));
            }
            if (y % 4 == 0)
                yield return null;
        }
        tex.Apply();
        AssetDatabase.CreateAsset(tex, "Assets/Textures/detailHeight.exr");
        Debug.Log("Detail height texture generated at Assets/Textures/detailHeight.exr");
    }
}
