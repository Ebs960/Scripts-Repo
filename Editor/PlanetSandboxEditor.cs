using UnityEditor;
using UnityEngine;

// Simple editor utility to add a PlanetSandbox GameObject to the scene
public class PlanetSandboxEditor : EditorWindow
{
    [MenuItem("Tools/Planet Sandbox/Create Sandbox")]
    public static void CreateSandbox()
    {
        var go = new GameObject("PlanetSandbox");
        var sb = go.AddComponent<PlanetSandbox>();
        Selection.activeGameObject = go;
    }
}
