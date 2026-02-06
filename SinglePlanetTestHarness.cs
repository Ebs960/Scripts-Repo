using UnityEngine;
using UnityEngine.UI;

// Lightweight test harness to create a single planet scene for memory/gpu tests.
// - Creates a sphere mesh as planet
// - Adds a directional light
// - Adds a minimap camera rendering to a small RenderTexture and displays it on a UI RawImage
// - Adds a simple decal quad above the planet
// Use this by placing the component on an empty GameObject in a new scene and pressing Play.
public class SinglePlanetTestHarness : MonoBehaviour
{
    [Header("Planet")]
    public int planetResolution = 32;
    public float planetRadius = 50f;
    public Material planetMaterial;

    [Header("Minimap")]
    public int minimapSize = 512;
    public Material minimapMaterial;

    [Header("UI")]
    public Canvas uiCanvas;

    private GameObject planetGO;
    private Camera minimapCam;
    private RenderTexture minimapRT;

    void Start()
    {
        // Create simple planet sphere
        planetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planetGO.name = "TestPlanet";
        planetGO.transform.position = Vector3.zero;
        planetGO.transform.localScale = Vector3.one * planetRadius * 2f;
        if (planetMaterial != null)
            planetGO.GetComponent<Renderer>().sharedMaterial = planetMaterial;

        // Simple directional light
        var lightGO = new GameObject("TestDirectionalLight");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Minimap camera
        var camGO = new GameObject("MinimapCam");
        minimapCam = camGO.AddComponent<Camera>();
        minimapCam.transform.position = new Vector3(0, planetRadius * 2f, 0);
        minimapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        minimapCam.orthographic = true;
        minimapCam.orthographicSize = planetRadius;
        minimapCam.clearFlags = CameraClearFlags.SolidColor;
        minimapCam.backgroundColor = Color.black;
        minimapCam.cullingMask = ~0; // everything

        minimapRT = new RenderTexture(minimapSize, minimapSize, 16, RenderTextureFormat.ARGB32);
        minimapRT.name = "MinimapRT";
        minimapRT.Create();
        minimapCam.targetTexture = minimapRT;

        // UI: create Canvas if not assigned
        if (uiCanvas == null)
        {
            var canvasGO = new GameObject("MinimapCanvas");
            uiCanvas = canvasGO.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        // RawImage to display minimap
        var rawGO = new GameObject("MinimapRawImage");
        rawGO.transform.SetParent(uiCanvas.transform, false);
        var raw = rawGO.AddComponent<RawImage>();
        raw.texture = minimapRT;
        raw.rectTransform.anchorMin = new Vector2(0.75f, 0.75f);
        raw.rectTransform.anchorMax = new Vector2(0.98f, 0.98f);
        raw.rectTransform.offsetMin = Vector2.zero;
        raw.rectTransform.offsetMax = Vector2.zero;

        // Simple decal: quad above the planet with a semi-transparent material if provided
        var decalGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        decalGO.name = "TestDecal";
        decalGO.transform.SetParent(planetGO.transform, false);
        decalGO.transform.localPosition = new Vector3(0, planetRadius * 0.5f, 0);
        decalGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        decalGO.transform.localScale = Vector3.one * planetRadius * 0.5f;
        if (minimapMaterial != null)
            decalGO.GetComponent<Renderer>().sharedMaterial = minimapMaterial;

        Debug.Log("SinglePlanetTestHarness: scene constructed. Play to run a single-planet test.");
    }

    void OnDestroy()
    {
        if (minimapRT != null)
        {
            try { if (minimapRT.IsCreated()) minimapRT.Release(); } catch { }
            Object.DestroyImmediate(minimapRT);
            minimapRT = null;
        }

        if (planetGO != null) Destroy(planetGO);
    }
}
