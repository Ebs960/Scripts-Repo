using TMPro;
using UnityEngine;

/// <summary>
/// World-space marker used by the true 3D space map. It is visual-only and keeps
/// planet selection data separate from the real generated planet GameObjects.
/// </summary>
public class SpaceMapPlanetMarker : MonoBehaviour
{
    [SerializeField] private MeshRenderer planetRenderer;
    [SerializeField] private Collider selectionCollider;
    [SerializeField] private TextMeshPro label;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Color selectedTint = Color.yellow;
    [SerializeField] private Color currentPlanetTint = Color.cyan;

    private static MinimapUI cachedMinimapUI;

    private Material materialInstance;
    private Color baseColor = Color.white;

    public GameManager.PlanetData PlanetData { get; private set; }
    public int PlanetIndex => PlanetData != null ? PlanetData.planetIndex : -1;
    public int AnchorSpaceTileIndex { get; set; } = -1;

    public void Initialize(GameManager.PlanetData data, SpaceMapWorldController controller, float radius, Material template)
    {
        PlanetData = data;
        gameObject.name = data != null ? $"SpaceMapPlanet_{data.planetIndex}_{data.planetName}" : "SpaceMapPlanet";

        EnsureVisuals(template);
        transform.localScale = Vector3.one * Mathf.Max(0.05f, radius);

        Texture minimapTexture = TryGetMinimapTexture(data);
        ApplyTexture(minimapTexture);
        // White lets the wrapped minimap texture show through untinted; the flat type color is only a fallback.
        baseColor = minimapTexture != null ? Color.white : GetPlanetColor(data);
        ApplyTint(baseColor);

        if (label != null)
        {
            label.text = data != null ? data.planetName : "Unknown";
            label.transform.localPosition = Vector3.up * 1.45f;
        }
    }

    public void SetSelectionState(bool selected, bool currentPlanet)
    {
        Color tint = baseColor;
        if (currentPlanet) tint = Color.Lerp(tint, currentPlanetTint, 0.45f);
        if (selected) tint = Color.Lerp(tint, selectedTint, 0.55f);
        ApplyTint(tint);
    }

    private void EnsureVisuals(Material template)
    {
        if (visualRoot == null) visualRoot = transform;

        if (planetRenderer == null)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "PlanetVisual";
            sphere.transform.SetParent(visualRoot, false);
            planetRenderer = sphere.GetComponent<MeshRenderer>();
            selectionCollider = sphere.GetComponent<Collider>();
        }

        if (selectionCollider == null)
            selectionCollider = GetComponentInChildren<Collider>();

        if (template != null)
            materialInstance = new Material(template);
        else if (planetRenderer.sharedMaterial != null)
            materialInstance = new Material(planetRenderer.sharedMaterial);
        else
            materialInstance = new Material(Shader.Find("Standard"));

        planetRenderer.material = materialInstance;

        if (label == null)
        {
            GameObject labelGO = new GameObject("PlanetLabel");
            labelGO.transform.SetParent(transform, false);
            label = labelGO.AddComponent<TextMeshPro>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 2.5f;
            label.color = Color.white;
        }
    }

    private static Texture TryGetMinimapTexture(GameManager.PlanetData data)
    {
        if (data == null) return null;
        if (cachedMinimapUI == null) cachedMinimapUI = FindAnyObjectByType<MinimapUI>(FindObjectsInactive.Include);
        return cachedMinimapUI != null ? cachedMinimapUI.GetPlanetMinimapTexture(data.planetIndex) : null;
    }

    private void ApplyTexture(Texture texture)
    {
        if (materialInstance == null || texture == null) return;
        if (materialInstance.HasProperty("_BaseMap")) materialInstance.SetTexture("_BaseMap", texture);
        if (materialInstance.HasProperty("_MainTex")) materialInstance.SetTexture("_MainTex", texture);
    }

    private void ApplyTint(Color color)
    {
        if (materialInstance == null) return;
        if (materialInstance.HasProperty("_BaseColor")) materialInstance.SetColor("_BaseColor", color);
        if (materialInstance.HasProperty("_Color")) materialInstance.SetColor("_Color", color);
    }

    private static Color GetPlanetColor(GameManager.PlanetData data)
    {
        if (data == null) return Color.white;
        return data.planetType switch
        {
            GameManager.PlanetType.Gas_Giant => new Color(0.95f, 0.72f, 0.42f),
            GameManager.PlanetType.Volcanic => new Color(0.85f, 0.18f, 0.08f),
            GameManager.PlanetType.Barren => new Color(0.55f, 0.48f, 0.40f),
            GameManager.PlanetType.Ice => new Color(0.65f, 0.88f, 1f),
            GameManager.PlanetType.Desert => new Color(0.88f, 0.68f, 0.35f),
            GameManager.PlanetType.Ocean => new Color(0.10f, 0.35f, 0.95f),
            GameManager.PlanetType.Terran => new Color(0.18f, 0.62f, 0.28f),
            _ => new Color(0.45f, 0.65f, 1f)
        };
    }
}
